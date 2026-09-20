using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuluPet.Persistence;

namespace GuluPet.Diary;

public enum DiaryGenerationOutcome
{
    Success,
    RetryableFailure,
    TerminalFailure,
    InsufficientBalance,
}

public sealed record DiaryGenerationResult(
    DiaryGenerationOutcome Outcome,
    GeneratedDiaryEntry? Entry,
    string Code,
    HttpStatusCode? StatusCode = null,
    TimeSpan? RetryAfter = null,
    Guid? RequestId = null)
{
    public static DiaryGenerationResult Success(GeneratedDiaryEntry entry) =>
        new(DiaryGenerationOutcome.Success, entry, "success", HttpStatusCode.OK);
}

public sealed record HttpDiaryOptions
{
    internal static readonly Uri DefaultEndpoint =
        new(
            "https://localhost.invalid/v1/diary-generations",
            UriKind.Absolute);
    internal static readonly Uri DefaultRegistrationEndpoint =
        new("https://localhost.invalid/v1/installations", UriKind.Absolute);

    // Network generation is an extension point and is disabled in the stock
    // community build. Integrators must supply their own HTTPS endpoint and
    // explicitly opt in.
    public bool Enabled { get; init; }

    internal Uri Endpoint { get; init; } = DefaultEndpoint;

    internal Uri RegistrationEndpoint { get; init; } =
        DefaultRegistrationEndpoint;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(11);

    public int MaximumResponseBytes { get; init; } = 128 * 1024;

    internal TimeSpan MaximumRetryAfter { get; init; } = TimeSpan.FromDays(1);

    internal TimeSpan ImmediateRetryLimit { get; init; } =
        TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        if (!IsSafeEndpoint(Endpoint, "/v1/diary-generations")
            || !IsSafeEndpoint(RegistrationEndpoint, "/v1/installations")
            || !string.Equals(
                Endpoint.GetLeftPart(UriPartial.Authority),
                RegistrationEndpoint.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase)
            || RequestTimeout <= TimeSpan.Zero
            || RequestTimeout > TimeSpan.FromMinutes(15)
            || MaximumResponseBytes is < 1024 or > 1024 * 1024
            || MaximumRetryAfter < TimeSpan.FromMinutes(1)
            || MaximumRetryAfter > TimeSpan.FromDays(1)
            || ImmediateRetryLimit < TimeSpan.Zero
            || ImmediateRetryLimit > TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException(
                "HTTP diary options are invalid.");
        }
    }

    private static bool IsSafeEndpoint(Uri endpoint, string expectedPath) =>
        endpoint.IsAbsoluteUri
        && string.Equals(
            endpoint.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase)
        && endpoint.IsDefaultPort
        && string.Equals(
            endpoint.AbsolutePath,
            expectedPath,
            StringComparison.Ordinal)
        && string.IsNullOrEmpty(endpoint.Query)
        && string.IsNullOrEmpty(endpoint.Fragment)
        && string.IsNullOrWhiteSpace(endpoint.UserInfo);
}

public interface IDiaryGenerator
{
    Task<DiaryGenerationResult> GenerateAsync(
        DiaryGenerationInput input,
        CancellationToken cancellationToken);

    void Acknowledge(
        DiaryGenerationInput input,
        DiaryGenerationResult result)
    {
    }

    void Reconcile(
        DiaryState state,
        DateOnly latestRetainedDate)
    {
    }
}

/// <summary>
/// Optional adapter that sends bounded diary facts to an integrator-provided
/// HTTPS endpoint. It is disabled by default and never accepts an upstream
/// model credential from the desktop application.
/// </summary>
public sealed class HttpDiaryClient : IDiaryGenerator, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly HttpDiaryOptions _options;
    private readonly LogReportCredentialStore _credentialStore;
    private readonly PendingDiaryGenerationStore _pendingStore;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public HttpDiaryClient(
        HttpClient httpClient,
        HttpDiaryOptions? options = null,
        LogReportCredentialStore? credentialStore = null,
        PendingDiaryGenerationStore? pendingStore = null,
        TimeProvider? timeProvider = null,
        bool ownsHttpClient = false)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new HttpDiaryOptions();
        _options.Validate();
        _credentialStore = credentialStore ?? new LogReportCredentialStore();
        _pendingStore = pendingStore ?? new PendingDiaryGenerationStore();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ownsHttpClient = ownsHttpClient;
    }

    public static HttpDiaryClient CreateDefault(
        HttpDiaryOptions? options = null)
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GuluPet/0.2");
        return new HttpDiaryClient(
            client,
            options,
            ownsHttpClient: true);
    }

    public async Task<DiaryGenerationResult> GenerateAsync(
        DiaryGenerationInput input,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(input);
        if (!_options.Enabled)
        {
            return new DiaryGenerationResult(
                DiaryGenerationOutcome.TerminalFailure,
                Entry: null,
                Code: "not-configured");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CredentialResolution registration =
                await EnsureRegisteredAsync(force: false, cancellationToken)
                    .ConfigureAwait(false);
            if (registration.Credential is null)
            {
                return Retryable(registration.Code, registration.StatusCode);
            }

            PendingDiaryGeneration pending;
            try
            {
                PendingDiaryGeneration? existing = _pendingStore.Load(input.Date);
                if (existing is not null
                    && existing.InstallationId
                        != registration.Credential.InstallationId)
                {
                    // The old bearer identity is irrecoverable after local
                    // credential loss. Clear only the exact dated operation;
                    // the next scheduled check will create one new request.
                    _pendingStore.Clear(input.Date, existing.RequestId);
                    return Retryable("installation-identity-changed");
                }

                pending = existing ?? CreateAndSavePending(
                    input,
                    registration.Credential.InstallationId);
            }
            catch (Exception exception)
                when (exception is InvalidDataException
                      or IOException
                      or UnauthorizedAccessException
                      or CryptographicException)
            {
                return Retryable(
                    exception is InvalidDataException
                        ? "pending-request-invalid"
                        : "pending-request-save-failed");
            }

            LogReportCredential credential = registration.Credential;
            bool refreshedRegistration = false;
            bool repeatedShortLimit = false;
            while (true)
            {
                ProxyAttempt attempt = await SendAsync(
                        pending,
                        credential,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (attempt.RequiresRegistration && !refreshedRegistration)
                {
                    CredentialResolution refreshed =
                        await EnsureRegisteredAsync(
                                force: true,
                                cancellationToken)
                            .ConfigureAwait(false);
                    if (refreshed.Credential is null)
                    {
                        return Retryable(
                            refreshed.Code,
                            refreshed.StatusCode);
                    }

                    credential = refreshed.Credential;
                    refreshedRegistration = true;
                    continue;
                }

                if (attempt.Result.StatusCode == HttpStatusCode.TooManyRequests
                    && !repeatedShortLimit
                    && attempt.Result.RetryAfter is { } shortDelay
                    && shortDelay > TimeSpan.Zero
                    && shortDelay <= _options.ImmediateRetryLimit)
                {
                    repeatedShortLimit = true;
                    await Task.Delay(shortDelay, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                return attempt.Result with { RequestId = pending.RequestId };
            }
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

    public void Acknowledge(
        DiaryGenerationInput input,
        DiaryGenerationResult result)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome is DiaryGenerationOutcome.RetryableFailure
            || result.RequestId is not { } requestId)
        {
            return;
        }

        _pendingStore.Clear(input.Date, requestId);
    }

    public void Reconcile(
        DiaryState state,
        DateOnly latestRetainedDate)
    {
        ArgumentNullException.ThrowIfNull(state);
        DateOnly oldestRetainedDate = DiaryLedger.GetOldestEligibleDate(
            latestRetainedDate);
        _pendingStore.PruneBefore(oldestRetainedDate);
        foreach (DiaryDayState day in state.Days.Where(static day =>
                     day.Entry is not null || day.IsGenerationTerminal))
        {
            PendingDiaryGeneration? pending = _pendingStore.Load(day.Date);
            if (pending is not null)
            {
                _pendingStore.Clear(day.Date, pending.RequestId);
            }
        }
    }

    internal TimeSpan GetStableScheduleJitter()
    {
        return ResolveStableScheduleJitter(
            _credentialStore.GetOrCreateRegistrationIntent);
    }

    internal static TimeSpan ResolveStableScheduleJitter(
        Func<LogReportCredential> resolveCredential)
    {
        ArgumentNullException.ThrowIfNull(resolveCredential);
        try
        {
            LogReportCredential credential = resolveCredential();
            return DiaryGenerationSchedule.GetStableInstallationJitter(
                credential.InstallationId);
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException
                  or CryptographicException)
        {
            // Schedule spreading is an availability optimization. Local
            // credential/DPAPI failure must not prevent the pet from starting;
            // the generation path will retry credential preparation later.
            return TimeSpan.Zero;
        }
    }

    private async Task<CredentialResolution> EnsureRegisteredAsync(
        bool force,
        CancellationToken cancellationToken)
    {
        LogReportCredential proposed;
        try
        {
            proposed = _credentialStore.GetOrCreateRegistrationIntent();
            if (proposed.RegistrationConfirmed && !force)
            {
                return new CredentialResolution(
                    proposed,
                    "registered-local",
                    null);
            }

            if (proposed.RegistrationConfirmed)
            {
                proposed = proposed with { RegistrationConfirmed = false };
                _credentialStore.Save(proposed);
            }
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException
                  or CryptographicException)
        {
            return new CredentialResolution(
                null,
                "installation-credential-unavailable",
                null);
        }

        using var timeout = CreateTimeout(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            _options.RegistrationEndpoint)
        {
            Content = JsonContent.Create(
                new InstallationRegistration(
                    proposed.InstallationId,
                    proposed.Token),
                options: JsonOptions),
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
                return new CredentialResolution(
                    null,
                    $"registration-http-{(int)response.StatusCode}",
                    response.StatusCode);
            }

            LogReportCredential confirmed = proposed with
            {
                RegistrationConfirmed = true,
            };
            _credentialStore.Save(confirmed);
            return new CredentialResolution(
                confirmed,
                "registered",
                response.StatusCode);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new CredentialResolution(
                null,
                "registration-timeout",
                null);
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                  or InvalidDataException
                  or IOException
                  or UnauthorizedAccessException
                  or CryptographicException)
        {
            return new CredentialResolution(
                null,
                "registration-failed",
                null);
        }
    }

    private PendingDiaryGeneration CreateAndSavePending(
        DiaryGenerationInput input,
        Guid installationId)
    {
        Guid requestId = Guid.NewGuid();
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(
            CreateRequest(input, requestId),
            JsonOptions);
        var pending = new PendingDiaryGeneration
        {
            InstallationId = installationId,
            RequestId = requestId,
            DiaryDate = input.Date,
            CreatedAtUtc = _timeProvider.GetUtcNow(),
            ContentSha256 = Convert.ToHexString(SHA256.HashData(body))
                .ToLowerInvariant(),
            Body = body,
        };
        _pendingStore.SaveNew(pending);
        return pending;
    }

    private async Task<ProxyAttempt> SendAsync(
        PendingDiaryGeneration pending,
        LogReportCredential credential,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            _options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            credential.Token);
        request.Headers.Add(
            "X-GuluPet-Installation-Id",
            credential.InstallationId.ToString("D"));
        request.Headers.Add(
            "Idempotency-Key",
            pending.RequestId.ToString("D"));
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
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return new ProxyAttempt(
                    await ParseSuccessAsync(
                            response,
                            pending.RequestId,
                            timeout.Token)
                        .ConfigureAwait(false));
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ProxyAttempt(
                    Retryable("proxy-authentication-required", response.StatusCode),
                    RequiresRegistration: true);
            }

            if (response.StatusCode == HttpStatusCode.PaymentRequired)
            {
                return new ProxyAttempt(Retryable(
                    "upstream-balance-unavailable",
                    response.StatusCode,
                    ParseRetryAfter(response) ?? TimeSpan.FromHours(6)));
            }

            int numericStatus = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                || numericStatus == 425
                || numericStatus is >= 500 and <= 599)
            {
                return new ProxyAttempt(Retryable(
                    $"proxy-http-{numericStatus}",
                    response.StatusCode,
                    ParseRetryAfter(response)));
            }

            if (numericStatus is 400 or 409 or 413 or 415 or 422)
            {
                return new ProxyAttempt(new DiaryGenerationResult(
                    DiaryGenerationOutcome.TerminalFailure,
                    null,
                    $"proxy-http-{numericStatus}",
                    response.StatusCode));
            }


            if (numericStatus is >= 400 and <= 499)
            {
                return new ProxyAttempt(Retryable(
                    $"proxy-http-{numericStatus}",
                    response.StatusCode,
                    ParseRetryAfter(response)));
            }

            return new ProxyAttempt(Retryable(
                $"proxy-http-{numericStatus}",
                response.StatusCode,
                ParseRetryAfter(response)));
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new ProxyAttempt(Retryable("network-timeout"));
        }
        catch (HttpRequestException)
        {
            return new ProxyAttempt(Retryable("network-error"));
        }
        catch (IOException)
        {
            // A chunked 200 response may fail only after headers have arrived.
            // Keep the exact pending request so the next slot safely replays.
            return new ProxyAttempt(Retryable("network-body-error"));
        }
    }

    private async Task<DiaryGenerationResult> ParseSuccessAsync(
        HttpResponseMessage response,
        Guid expectedRequestId,
        CancellationToken cancellationToken)
    {
        byte[] payload;
        try
        {
            payload = await ReadLimitedAsync(
                    response.Content,
                    _options.MaximumResponseBytes,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return Retryable(
                "response-too-large",
                response.StatusCode);
        }

        try
        {
            DiaryProxyResponse? proxy = JsonSerializer.Deserialize<
                DiaryProxyResponse>(payload, JsonOptions);
            if (proxy is null
                || proxy.RequestId != expectedRequestId)
            {
                return Retryable("invalid-proxy-response", response.StatusCode);
            }

            if (!string.IsNullOrWhiteSpace(proxy.Error))
            {
                string error = proxy.Error.Trim();
                if (error.Length > 80
                    || error.Any(character =>
                        !char.IsAsciiLetterOrDigit(character)
                        && character != '_'))
                {
                    return Retryable(
                        "invalid-proxy-response",
                        response.StatusCode);
                }

                if (string.Equals(
                        error,
                        "upstream_balance_unavailable",
                        StringComparison.Ordinal))
                {
                    TimeSpan balanceRetryAfter = ParseEnvelopeRetryAfter(
                            proxy.RetryAfterSeconds)
                        ?? TimeSpan.FromHours(6);
                    return Retryable(
                        error,
                        response.StatusCode,
                        balanceRetryAfter);
                }

                TimeSpan? retryAfter = ParseEnvelopeRetryAfter(
                    proxy.RetryAfterSeconds);
                return proxy.Retryable == true
                    ? Retryable(error, response.StatusCode, retryAfter)
                    : new DiaryGenerationResult(
                        DiaryGenerationOutcome.TerminalFailure,
                        null,
                        error,
                        response.StatusCode);
            }

            if (proxy.Entry is null || proxy.Duplicate is null)
            {
                return Retryable("invalid-proxy-response", response.StatusCode);
            }

            var entry = new GeneratedDiaryEntry
            {
                Title = proxy.Entry.Title.Trim(),
                Mood = proxy.Entry.Mood.Trim(),
                Body = proxy.Entry.Body.Trim(),
                Model = proxy.Entry.Model.Trim(),
                GeneratedAtUtc = proxy.Entry.GeneratedAtUtc,
            };
            _ = DiaryState.ValidateAndSnapshot(new DiaryState
            {
                UpdatedAtUtc = entry.GeneratedAtUtc,
                Days =
                [
                    DiaryDayState.Create(
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        entry.GeneratedAtUtc) with
                    {
                        Entry = entry,
                    },
                ],
            });
            return DiaryGenerationResult.Success(entry);
        }
        catch (Exception exception)
            when (exception is JsonException
                  or InvalidDataException
                  or NullReferenceException)
        {
            return Retryable("invalid-proxy-response", response.StatusCode);
        }
    }

    private DiaryProxyRequest CreateRequest(
        DiaryGenerationInput input,
        Guid requestId) =>
        new()
        {
            SchemaVersion = 1,
            RequestId = requestId,
            DiaryDate = input.Date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture),
            Period = new DiaryProxyPeriod(
                input.Period.StartsAtUtc,
                input.Period.EndsAtUtc),
            RuntimeSeconds = input.RuntimeSeconds,
            Interactions = input.Interactions
                .OrderBy(static item => item.Kind, StringComparer.Ordinal)
                .Select(static item => new DiaryProxyInteraction(
                    item.Kind,
                    item.Count))
                .ToArray(),
            Postcards = input.Postcards
                .OrderBy(static item => item.Id, StringComparer.Ordinal)
                .Select(CreateUnlock)
                .ToArray(),
            Memories = input.Memories
                .OrderBy(static item => item.Id, StringComparer.Ordinal)
                .Select(CreateUnlock)
                .ToArray(),
            Weather = input.Weather is null
                ? null
                : new DiaryProxyWeather(
                    input.Weather.Kind,
                    input.Weather.TemperatureCelsius),
        };

    private static DiaryProxyUnlock CreateUnlock(DiaryUnlockRecord item) =>
        new(item.Id);

    private TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null
            && response.Headers.RetryAfter?.Date is { } date)
        {
            retryAfter = date - _timeProvider.GetUtcNow();
        }

        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter <= TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(1);
        }

        return retryAfter > _options.MaximumRetryAfter
            ? _options.MaximumRetryAfter
            : retryAfter;
    }

    private TimeSpan? ParseEnvelopeRetryAfter(double? seconds)
    {
        if (seconds is not { } value
            || !double.IsFinite(value)
            || value <= 0)
        {
            return null;
        }

        return TimeSpan.FromSeconds(Math.Min(
            value,
            _options.MaximumRetryAfter.TotalSeconds));
    }

    private CancellationTokenSource CreateTimeout(
        CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        return timeout;
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException(
                "HTTP diary response exceeded the configured limit.");
        }

        await using Stream source = await content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            int read = await source.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    "HTTP diary response exceeded the configured limit.");
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static DiaryGenerationResult Retryable(
        string code,
        HttpStatusCode? statusCode = null,
        TimeSpan? retryAfter = null) =>
        new(
            DiaryGenerationOutcome.RetryableFailure,
            null,
            code,
            statusCode,
            retryAfter);

    private sealed record CredentialResolution(
        LogReportCredential? Credential,
        string Code,
        HttpStatusCode? StatusCode);

    private sealed record ProxyAttempt(
        DiaryGenerationResult Result,
        bool RequiresRegistration = false);

    private sealed record InstallationRegistration(
        [property: JsonPropertyName("installationId")] Guid InstallationId,
        [property: JsonPropertyName("token")] string Token);

    private sealed record DiaryProxyRequest
    {
        public int SchemaVersion { get; init; }

        public Guid RequestId { get; init; }

        public required string DiaryDate { get; init; }

        public required DiaryProxyPeriod Period { get; init; }

        public long RuntimeSeconds { get; init; }

        public required IReadOnlyList<DiaryProxyInteraction> Interactions
        {
            get;
            init;
        }

        public required IReadOnlyList<DiaryProxyUnlock> Postcards { get; init; }

        public required IReadOnlyList<DiaryProxyUnlock> Memories { get; init; }

        public DiaryProxyWeather? Weather { get; init; }
    }

    private sealed record DiaryProxyPeriod(
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc);

    private sealed record DiaryProxyInteraction(string Kind, int Count);

    private sealed record DiaryProxyUnlock(string Id);

    private sealed record DiaryProxyWeather(
        string Kind,
        double TemperatureCelsius);

    private sealed record DiaryProxyResponse
    {
        [JsonRequired]
        public Guid RequestId { get; init; }

        public bool? Duplicate { get; init; }

        public DiaryProxyEntry? Entry { get; init; }

        public string? Error { get; init; }

        public bool? Retryable { get; init; }

        public double? RetryAfterSeconds { get; init; }
    }

    private sealed record DiaryProxyEntry
    {
        [JsonRequired]
        public required string Title { get; init; }

        [JsonRequired]
        public required string Mood { get; init; }

        [JsonRequired]
        public required string Body { get; init; }

        [JsonRequired]
        public required string Model { get; init; }

        [JsonRequired]
        public DateTimeOffset GeneratedAtUtc { get; init; }
    }
}
