using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuluPet.Persistence;

namespace GuluPet.Diagnostics;

public enum ErrorLogReportOutcome
{
    Uploaded,
    NoErrors,
    Failed,
}

public sealed record ErrorLogReportResult(
    ErrorLogReportOutcome Outcome,
    Guid? ReportId = null,
    HttpStatusCode? StatusCode = null,
    string? FailureCode = null);

public sealed record ErrorLogReportOptions
{
    public Uri ServiceBaseUri { get; init; } =
        new("https://localhost.invalid/", UriKind.Absolute);

    public bool Enabled { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int MaximumRequestBytes { get; init; } = 1024 * 1024;

    internal void Validate()
    {
        if (!ServiceBaseUri.IsAbsoluteUri
            || !string.Equals(
                ServiceBaseUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || RequestTimeout <= TimeSpan.Zero
            || MaximumRequestBytes is < 64 * 1024 or > 1024 * 1024)
        {
            throw new InvalidOperationException(
                "The error-log report options are invalid.");
        }
    }
}

/// <summary>
/// Optional HTTPS sink for allow-listed local error records. Reporting is
/// disabled by default and requires an extension to provide an endpoint and
/// set <see cref="ErrorLogReportOptions.Enabled"/>.
/// </summary>
public sealed class ErrorLogReportUploader : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly TimeSpan ReportAge = TimeSpan.FromDays(3);
    private const int MaximumEvents = 500;
    private const int MaximumFeedbackCharacters = 2000;

    private readonly LocalErrorLog _errorLog;
    private readonly LogReportCredentialStore _credentialStore;
    private readonly PendingErrorReportStore _pendingStore;
    private readonly ReportedErrorEventStore _reportedEventStore;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ErrorLogReportOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public ErrorLogReportUploader(
        LocalErrorLog errorLog,
        LogReportCredentialStore credentialStore,
        PendingErrorReportStore pendingStore,
        ReportedErrorEventStore reportedEventStore,
        HttpClient httpClient,
        ErrorLogReportOptions? options = null,
        TimeProvider? timeProvider = null,
        bool ownsHttpClient = false)
    {
        _errorLog = errorLog ?? throw new ArgumentNullException(nameof(errorLog));
        _credentialStore = credentialStore
            ?? throw new ArgumentNullException(nameof(credentialStore));
        _pendingStore = pendingStore
            ?? throw new ArgumentNullException(nameof(pendingStore));
        _reportedEventStore = reportedEventStore
            ?? throw new ArgumentNullException(nameof(reportedEventStore));
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new ErrorLogReportOptions();
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ownsHttpClient = ownsHttpClient;
    }

    public static ErrorLogReportUploader CreateDefault(LocalErrorLog errorLog)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false,
        };
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GuluPet/0.1");
        return new ErrorLogReportUploader(
            errorLog,
            new LogReportCredentialStore(),
            new PendingErrorReportStore(),
            new ReportedErrorEventStore(),
            client,
            ownsHttpClient: true);
    }

    public Task<ErrorLogReportResult> UploadAsync(
        CancellationToken cancellationToken = default) =>
        UploadAsync(userFeedback: null, cancellationToken);

    public async Task<ErrorLogReportResult> UploadAsync(
        string? userFeedback,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_options.Enabled)
        {
            return new ErrorLogReportResult(
                ErrorLogReportOutcome.Failed,
                FailureCode: "not-configured");
        }

        string? feedback = NormalizeFeedback(userFeedback);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryLoadPendingReport(out PendingErrorReport? pending))
            {
                return Failed("pending-report-load-failed");
            }

            if (pending is not null)
            {
                if (!TryLoadCredential(
                        out LogReportCredential? pendingCredential))
                {
                    return Failed(
                        "credential-load-failed",
                        reportId: pending.ReportId);
                }

                if (pendingCredential is null)
                {
                    _errorLog.Write(
                        "diagnostics",
                        "upload-errors",
                        "pending-report-credential-missing",
                        context: ReportContext(pending.ReportId));
                    return Failed(
                        "pending-report-credential-missing",
                        reportId: pending.ReportId);
                }

                if (pendingCredential.InstallationId
                    != pending.InstallationId)
                {
                    _errorLog.Write(
                        "diagnostics",
                        "upload-errors",
                        "pending-report-credential-mismatch",
                        context: ReportContext(pending.ReportId));
                    return Failed(
                        "pending-report-credential-mismatch",
                        reportId: pending.ReportId);
                }

                RegistrationResult pendingRegistration =
                    await EnsureRegisteredAsync(
                            pendingCredential,
                            force: !pendingCredential.RegistrationConfirmed,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (pendingRegistration.Credential is null)
                {
                    return Failed(
                        pendingRegistration.Code,
                        pendingRegistration.StatusCode,
                        pending.ReportId);
                }

                pendingCredential = pendingRegistration.Credential;
                ErrorLogReportResult retry = await SendPendingAsync(
                        pending,
                        pendingCredential,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (retry.StatusCode == HttpStatusCode.Unauthorized)
                {
                    RegistrationResult recovery =
                        await EnsureRegisteredAsync(
                                pendingCredential,
                                force: true,
                                cancellationToken)
                            .ConfigureAwait(false);
                    if (recovery.Credential is null)
                    {
                        return Failed(
                            recovery.Code,
                            recovery.StatusCode,
                            pending.ReportId);
                    }

                    retry = await SendPendingAsync(
                            pending,
                            recovery.Credential,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                ErrorLogReportResult completedPending = CompletePendingAttempt(
                    pending,
                    retry,
                    keepUnauthorized: true);
                if (completedPending.Outcome != ErrorLogReportOutcome.Uploaded
                    || feedback is null
                    || PendingContainsFeedback(pending, feedback))
                {
                    return completedPending;
                }
            }

            if (!TryLoadReportedEventIds(
                    out IReadOnlySet<Guid>? reportedEventIds))
            {
                return Failed("reported-events-load-failed");
            }

            IReadOnlyList<LocalErrorRecord> records =
                _errorLog.ReadRecentRecords(
                    ReportAge,
                    MaximumEvents,
                    reportedEventIds);
            if (records.Count == 0 && feedback is null)
            {
                return new ErrorLogReportResult(
                    ErrorLogReportOutcome.NoErrors);
            }

            if (!TryLoadCredential(out LogReportCredential? credential))
            {
                return Failed("credential-load-failed");
            }

            RegistrationResult registration = await EnsureRegisteredAsync(
                    credential,
                    force: credential is { RegistrationConfirmed: false },
                    cancellationToken)
                .ConfigureAwait(false);
            if (registration.Credential is null)
            {
                return Failed(
                    registration.Code,
                    registration.StatusCode);
            }

            credential = registration.Credential;
            PendingErrorReport? created = CreateAndSavePending(
                records,
                credential,
                feedback);
            if (created is null)
            {
                return Failed("pending-report-save-failed");
            }

            ErrorLogReportResult upload = await SendPendingAsync(
                    created,
                    credential,
                    cancellationToken)
                .ConfigureAwait(false);
            if (upload.StatusCode != HttpStatusCode.Unauthorized)
            {
                return CompletePendingAttempt(
                    created,
                    upload,
                    keepUnauthorized: false);
            }

            // Re-register the exact same locally generated installation
            // credential, then replay the exact same report envelope. The
            // server treats both operations idempotently.
            RegistrationResult recovered = await EnsureRegisteredAsync(
                    credential,
                    force: true,
                    cancellationToken)
                .ConfigureAwait(false);
            if (recovered.Credential is null)
            {
                return Failed(
                    recovered.Code,
                    recovered.StatusCode,
                    created.ReportId);
            }

            ErrorLogReportResult refreshedUpload = await SendPendingAsync(
                    created,
                    recovered.Credential,
                    cancellationToken)
                .ConfigureAwait(false);
            return CompletePendingAttempt(
                created,
                refreshedUpload,
                keepUnauthorized: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<RegistrationResult> EnsureRegisteredAsync(
        LogReportCredential? credential,
        bool force,
        CancellationToken cancellationToken)
    {
        if (credential is { RegistrationConfirmed: true } && !force)
        {
            return new RegistrationResult(credential, "registered-local", null);
        }

        LogReportCredential proposed;
        if (credential is null)
        {
            try
            {
                // Persist the exact intent before the first network byte. If
                // the 201 response is lost, the next click can safely repeat
                // this same installation registration. Diary generation uses
                // the same atomic installation identity creation path.
                proposed = _credentialStore.GetOrCreateRegistrationIntent();
            }
            catch (Exception exception)
                when (exception is InvalidDataException
                      or IOException
                      or UnauthorizedAccessException
                      or CryptographicException)
            {
                _errorLog.Write(
                    "diagnostics",
                    "prepare-installation-registration",
                    "register-intent-save-failed",
                    exception);
                return new RegistrationResult(
                    null,
                    "register-intent-save-failed",
                    null);
            }
        }
        else
        {
            proposed = credential;
        }

        using var timeout = CreateTimeout(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_options.ServiceBaseUri, "v1/installations"))
        {
            Content = JsonContent.Create(new
            {
                installationId = proposed.InstallationId,
                token = proposed.Token,
            }),
        };
        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Created)
            {
                string code = $"register-http-{(int)response.StatusCode}";
                LogHttpFailure("register-installation", code, response.StatusCode);
                return new RegistrationResult(null, code, response.StatusCode);
            }

            LogReportCredential confirmed = proposed with
            {
                RegistrationConfirmed = true,
            };
            _credentialStore.Save(confirmed);
            return new RegistrationResult(
                confirmed,
                "registered",
                response.StatusCode);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _errorLog.Write(
                "diagnostics",
                "register-installation",
                "register-timeout");
            return new RegistrationResult(null, "register-timeout", null);
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                  or InvalidDataException
                  or IOException
                  or UnauthorizedAccessException
                  or CryptographicException)
        {
            _errorLog.Write(
                "diagnostics",
                "register-installation",
                "register-failed",
                exception);
            return new RegistrationResult(null, "register-failed", null);
        }
    }

    private async Task<ErrorLogReportResult> SendPendingAsync(
        PendingErrorReport pending,
        LogReportCredential credential,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_options.ServiceBaseUri, "v1/log-reports"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            credential.Token);
        request.Headers.Add(
            "X-GuluPet-Installation-Id",
            credential.InstallationId.ToString("D"));
        request.Headers.Add(
            "Idempotency-Key",
            pending.ReportId.ToString("D"));
        request.Headers.Add(
            "X-GuluPet-Timestamp",
            pending.CreatedAtUtc.ToString("O"));
        request.Headers.Add("X-Content-SHA256", pending.ContentSha256);
        request.Content = new ByteArrayContent(pending.Body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/json");

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                return new ErrorLogReportResult(
                    ErrorLogReportOutcome.Uploaded,
                    pending.ReportId,
                    response.StatusCode);
            }

            string code = $"upload-http-{(int)response.StatusCode}";
            LogHttpFailure(
                "upload-errors",
                code,
                response.StatusCode,
                pending.ReportId);
            return Failed(code, response.StatusCode, pending.ReportId);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _errorLog.Write(
                "diagnostics",
                "upload-errors",
                "upload-timeout",
                context: ReportContext(pending.ReportId));
            return Failed(
                "upload-timeout",
                reportId: pending.ReportId);
        }
        catch (HttpRequestException exception)
        {
            _errorLog.Write(
                "diagnostics",
                "upload-errors",
                "upload-network-failed",
                exception,
                ReportContext(pending.ReportId));
            return Failed(
                "upload-network-failed",
                reportId: pending.ReportId);
        }
    }

    private PendingErrorReport? CreateAndSavePending(
        IReadOnlyList<LocalErrorRecord> records,
        LogReportCredential credential,
        string? feedback)
    {
        Guid reportId = Guid.NewGuid();
        DateTimeOffset createdAtUtc = _timeProvider.GetUtcNow();
        try
        {
            BoundedPayload bounded = BuildBoundedPayload(
                records,
                feedback,
                credential.InstallationId,
                reportId,
                createdAtUtc);
            var pending = new PendingErrorReport(
                credential.InstallationId,
                reportId,
                createdAtUtc,
                Convert.ToHexString(SHA256.HashData(bounded.Body))
                    .ToLowerInvariant(),
                bounded.Body,
                bounded.Receipts);
            _pendingStore.Save(pending);
            return pending;
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException
                  or CryptographicException)
        {
            _errorLog.Write(
                "diagnostics",
                "save-pending-report",
                "pending-report-save-failed",
                exception,
                ReportContext(reportId));
            return null;
        }
    }

    private ErrorLogReportResult CompletePendingAttempt(
        PendingErrorReport pending,
        ErrorLogReportResult result,
        bool keepUnauthorized)
    {
        bool accepted = result.Outcome == ErrorLogReportOutcome.Uploaded;
        int? status = result.StatusCode is { } statusCode
            ? (int)statusCode
            : null;
        bool definitivelyRejected = status is >= 300 and < 500
            && !(keepUnauthorized
                 && result.StatusCode == HttpStatusCode.Unauthorized);
        if (!accepted && !definitivelyRejected)
        {
            return result;
        }

        if (accepted && !TryRecordReportedEvents(pending))
        {
            return Failed(
                "reported-events-save-failed",
                result.StatusCode,
                pending.ReportId);
        }

        if (TryClearPending(pending.ReportId))
        {
            return result;
        }

        return Failed(
            "pending-report-clear-failed",
            result.StatusCode,
            pending.ReportId);
    }

    private bool TryLoadPendingReport(out PendingErrorReport? pending)
    {
        try
        {
            pending = _pendingStore.Load();
            return true;
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException)
        {
            _errorLog.Write(
                "diagnostics",
                "load-pending-report",
                "pending-report-load-failed",
                exception);
            pending = null;
            return false;
        }
    }

    private bool TryLoadCredential(out LogReportCredential? credential)
    {
        try
        {
            credential = _credentialStore.Load();
            return true;
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException)
        {
            _errorLog.Write(
                "diagnostics",
                "load-report-credential",
                "credential-load-failed",
                exception);
            credential = null;
            return false;
        }
    }

    private bool TryLoadReportedEventIds(
        out IReadOnlySet<Guid>? eventIds)
    {
        try
        {
            eventIds = _reportedEventStore.LoadEventIds();
            return true;
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException)
        {
            _errorLog.Write(
                "diagnostics",
                "load-reported-events",
                "reported-events-load-failed",
                exception);
            eventIds = null;
            return false;
        }
    }

    private bool TryRecordReportedEvents(PendingErrorReport pending)
    {
        if (pending.Receipts.Count == 0)
        {
            return true;
        }

        try
        {
            _reportedEventStore.RecordReported(pending.Receipts);
            return true;
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException
                  or CryptographicException)
        {
            _errorLog.Write(
                "diagnostics",
                "save-reported-events",
                "reported-events-save-failed",
                exception,
                ReportContext(pending.ReportId));
            return false;
        }
    }

    private bool TryClearPending(Guid reportId)
    {
        try
        {
            _pendingStore.Clear();
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            _errorLog.Write(
                "diagnostics",
                "clear-pending-report",
                "pending-report-clear-failed",
                exception,
                ReportContext(reportId));
            return false;
        }
    }

    private static Dictionary<string, string> ReportContext(Guid reportId) =>
        new(StringComparer.Ordinal)
        {
            ["reportId"] = reportId.ToString("D"),
        };

    private BoundedPayload BuildBoundedPayload(
        IReadOnlyList<LocalErrorRecord> records,
        string? feedback,
        Guid installationId,
        Guid reportId,
        DateTimeOffset createdAtUtc)
    {
        int errorEventLimit = feedback is null
            ? MaximumEvents
            : MaximumEvents - 1;
        var events = records
            .TakeLast(errorEventLimit)
            .Select(static record => new ReportEvent(
                new LogEventReport
                {
                    TimestampUtc = record.OccurredAtUtc,
                    Level = "error",
                    Component = record.Component,
                    Operation = record.Operation,
                    ErrorType = record.ExceptionType,
                    ErrorCode = record.Code,
                    Message = record.Message,
                    StackTrace = record.StackTrace,
                    Context = record.Context,
                },
                new ReportedErrorEventReceipt(
                    record.EventId,
                    record.OccurredAtUtc)))
            .ToList();
        if (feedback is not null)
        {
            events.Add(new ReportEvent(
                new LogEventReport
                {
                    TimestampUtc = createdAtUtc,
                    Level = "feedback",
                    Component = "feedback",
                    Operation = "submit-feedback",
                    ErrorCode = "user-feedback",
                    Message = feedback,
                    Context = new Dictionary<string, string>(
                        StringComparer.Ordinal),
                },
                Receipt: null));
        }

        while (events.Count > 0)
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                new LogReportPayload
                {
                    SchemaVersion = 1,
                    InstallationId = installationId,
                    ReportId = reportId,
                    CreatedAtUtc = createdAtUtc,
                    AppVersion = Assembly.GetEntryAssembly()?
                        .GetName()
                        .Version?
                        .ToString() ?? "unknown",
                    OsVersion = Environment.OSVersion.VersionString,
                    Events = events
                        .Select(static item => item.Event)
                        .ToArray(),
                },
                JsonOptions);
            if (payload.Length <= _options.MaximumRequestBytes)
            {
                return new BoundedPayload(
                    payload,
                    events
                        .Where(static item => item.Receipt is not null)
                        .Select(static item => item.Receipt!)
                        .ToArray());
            }

            events.RemoveAt(0);
        }

        throw new InvalidDataException(
            "No local error event fits within the upload size limit.");
    }

    private void LogHttpFailure(
        string operation,
        string code,
        HttpStatusCode statusCode,
        Guid? reportId = null)
    {
        var context = new Dictionary<string, string>
        {
            ["statusCode"] = ((int)statusCode).ToString(),
        };
        if (reportId is { } id)
        {
            context["reportId"] = id.ToString("D");
        }

        _errorLog.Write(
            "diagnostics",
            operation,
            code,
            context: context);
    }

    private CancellationTokenSource CreateTimeout(
        CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        return timeout;
    }

    private static ErrorLogReportResult Failed(
        string code,
        HttpStatusCode? statusCode = null,
        Guid? reportId = null) =>
        new(
            ErrorLogReportOutcome.Failed,
            reportId,
            statusCode,
            code);

    private static string? NormalizeFeedback(string? feedback)
    {
        string? normalized = feedback?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= MaximumFeedbackCharacters
            ? normalized
            : normalized[..MaximumFeedbackCharacters];
    }

    private static bool PendingContainsFeedback(
        PendingErrorReport pending,
        string feedback)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(pending.Body);
            return document.RootElement
                .GetProperty("events")
                .EnumerateArray()
                .Any(item =>
                    item.TryGetProperty("level", out JsonElement level)
                    && string.Equals(
                        level.GetString(),
                        "feedback",
                        StringComparison.Ordinal)
                    && item.TryGetProperty("message", out JsonElement message)
                    && string.Equals(
                        message.GetString(),
                        feedback,
                        StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record RegistrationResult(
        LogReportCredential? Credential,
        string Code,
        HttpStatusCode? StatusCode);

    private sealed record LogReportPayload
    {
        public int SchemaVersion { get; init; }

        public Guid InstallationId { get; init; }

        public Guid ReportId { get; init; }

        public DateTimeOffset CreatedAtUtc { get; init; }

        public required string AppVersion { get; init; }

        public required string OsVersion { get; init; }

        public required IReadOnlyList<LogEventReport> Events { get; init; }
    }

    private sealed record ReportEvent(
        LogEventReport Event,
        ReportedErrorEventReceipt? Receipt);

    private sealed record BoundedPayload(
        byte[] Body,
        IReadOnlyList<ReportedErrorEventReceipt> Receipts);

    private sealed record LogEventReport
    {
        public DateTimeOffset TimestampUtc { get; init; }

        public required string Level { get; init; }

        public required string Component { get; init; }

        public required string Operation { get; init; }

        public string? ErrorType { get; init; }

        public required string ErrorCode { get; init; }

        public string? Message { get; init; }

        public string? StackTrace { get; init; }

        public required IReadOnlyDictionary<string, string> Context { get; init; }
    }
}
