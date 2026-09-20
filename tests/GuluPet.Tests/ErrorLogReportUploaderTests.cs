using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuluPet.Diagnostics;
using GuluPet.Persistence;

namespace GuluPet.Tests;

internal static class ErrorLogReportUploaderTests
{
    private static readonly Uri ServiceBaseUri =
        new("https://logs.gulupet.test/", UriKind.Absolute);

    public static void RunAll()
    {
        RunAsync(
            nameof(NoErrorsReturnsWithoutNetworkAccess),
            NoErrorsReturnsWithoutNetworkAccess);
        RunAsync(
            nameof(UserFeedbackUploadsWithoutLocalErrors),
            UserFeedbackUploadsWithoutLocalErrors);
        RunAsync(
            nameof(RegistrationThenUploadSendsAuthenticatedBoundedPayload),
            RegistrationThenUploadSendsAuthenticatedBoundedPayload);
        RunAsync(
            nameof(ExistingCredentialSkipsRegistration),
            ExistingCredentialSkipsRegistration);
        RunAsync(
            nameof(EventsOlderThanThreeDaysAreNotUploaded),
            EventsOlderThanThreeDaysAreNotUploaded);
        RunAsync(
            nameof(UploadHttpFailureReturnsFailedResult),
            UploadHttpFailureReturnsFailedResult);
        RunAsync(
            nameof(AmbiguousFailureRetriesExactPersistedEnvelope),
            AmbiguousFailureRetriesExactPersistedEnvelope);
        RunAsync(
            nameof(LostRegistrationResponseRetriesSameIntent),
            LostRegistrationResponseRetriesSameIntent);
        RunAsync(
            nameof(PendingUnauthorizedReRegistersSameCredentialAndEnvelope),
            PendingUnauthorizedReRegistersSameCredentialAndEnvelope);
        RunAsync(
            nameof(BoundedPayloadReceiptsOnlyAcknowledgeIncludedEvents),
            BoundedPayloadReceiptsOnlyAcknowledgeIncludedEvents);
    }

    private static Task NoErrorsReturnsWithoutNetworkAccess() =>
        WithTemporaryEnvironment(
            async (errorLog, credentialStore, pendingStore, reportedStore, timeProvider) =>
            {
                var handler = new FakeHttpMessageHandler();
                using var httpClient = new HttpClient(handler);
                using var uploader = CreateUploader(
                    errorLog,
                    credentialStore,
                    pendingStore,
                    reportedStore,
                    httpClient,
                    timeProvider);

                ErrorLogReportResult result = await uploader.UploadAsync();

                BehaviorTestCheck.Equal(
                    ErrorLogReportOutcome.NoErrors,
                    result.Outcome);
                BehaviorTestCheck.Equal(0, handler.Requests.Count);
                BehaviorTestCheck.Null(result.ReportId);
                BehaviorTestCheck.Null(result.StatusCode);
                BehaviorTestCheck.Null(result.FailureCode);
            });

    private static Task UserFeedbackUploadsWithoutLocalErrors() =>
        WithTemporaryEnvironment(
            async (errorLog, credentialStore, pendingStore, reportedStore, timeProvider) =>
            {
                credentialStore.Save(new LogReportCredential(
                    Guid.Parse("f9bf36e1-f1e5-44c1-a57c-d3a4c81e05d6"),
                    "feedback_token_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
                var handler = new FakeHttpMessageHandler();
                handler.EnqueueStatus(HttpStatusCode.Accepted);
                using var httpClient = new HttpClient(handler);
                using var uploader = CreateUploader(
                    errorLog,
                    credentialStore,
                    pendingStore,
                    reportedStore,
                    httpClient,
                    timeProvider);

                ErrorLogReportResult result = await uploader.UploadAsync(
                    "  打开回忆后一直无法播放。  ");

                BehaviorTestCheck.Equal(
                    ErrorLogReportOutcome.Uploaded,
                    result.Outcome);
                BehaviorTestCheck.Equal(1, handler.Requests.Count);
                using JsonDocument payload =
                    JsonDocument.Parse(handler.Requests.Single().Body);
                JsonElement uploadedEvent = payload.RootElement
                    .GetProperty("events")
                    .EnumerateArray()
                    .Single();
                BehaviorTestCheck.Equal(
                    "feedback",
                    uploadedEvent.GetProperty("level").GetString()!);
                BehaviorTestCheck.Equal(
                    "submit-feedback",
                    uploadedEvent.GetProperty("operation").GetString()!);
                BehaviorTestCheck.Equal(
                    "打开回忆后一直无法播放。",
                    uploadedEvent.GetProperty("message").GetString()!);
                BehaviorTestCheck.Equal(0, reportedStore.Load().Count);
                BehaviorTestCheck.Null(pendingStore.Load());
            });

    private static Task RegistrationThenUploadSendsAuthenticatedBoundedPayload() =>
        WithTemporaryEnvironment(
            async (errorLog, credentialStore, pendingStore, reportedStore, timeProvider) =>
            {
                const string promptSentinel =
                    "PROMPT-MUST-NOT-BE-UPLOADED";
                const string apiKeySentinel =
                    "sk-api-key-must-not-be-uploaded-123456";
                WriteError(
                    errorLog,
                    promptSentinel,
                    apiKeySentinel);
                BehaviorTestCheck.Equal(
                    1,
                    errorLog.ReadRecentRecords(TimeSpan.FromDays(7)).Count);

                var handler = new FakeHttpMessageHandler();
                handler.EnqueueStatus(HttpStatusCode.Created);
                handler.EnqueueStatus(HttpStatusCode.Accepted);
                using var httpClient = new HttpClient(handler);
                using var uploader = CreateUploader(
                    errorLog,
                    credentialStore,
                    pendingStore,
                    reportedStore,
                    httpClient,
                    timeProvider);

                ErrorLogReportResult result = await uploader.UploadAsync();

                BehaviorTestCheck.Equal(
                    ErrorLogReportOutcome.Uploaded,
                    result.Outcome);
                BehaviorTestCheck.Equal(HttpStatusCode.Accepted, result.StatusCode);
                BehaviorTestCheck.True(result.ReportId.HasValue);
                BehaviorTestCheck.Null(result.FailureCode);
                BehaviorTestCheck.Equal(2, handler.Requests.Count);

                CapturedRequest registration = handler.Requests[0];
                BehaviorTestCheck.Equal(HttpMethod.Post, registration.Method);
                BehaviorTestCheck.Equal(
                    new Uri(ServiceBaseUri, "v1/installations"),
                    registration.RequestUri);
                using JsonDocument registrationPayload =
                    JsonDocument.Parse(registration.Body);
                Guid installationId = registrationPayload.RootElement
                    .GetProperty("installationId")
                    .GetGuid();
                string reportToken = registrationPayload.RootElement
                    .GetProperty("token")
                    .GetString()!;
                BehaviorTestCheck.True(installationId != Guid.Empty);
                BehaviorTestCheck.True(reportToken.Length >= 32);

                CapturedRequest upload = handler.Requests[1];
                BehaviorTestCheck.Equal(HttpMethod.Post, upload.Method);
                BehaviorTestCheck.Equal(
                    new Uri(ServiceBaseUri, "v1/log-reports"),
                    upload.RequestUri);
                BehaviorTestCheck.Equal(
                    $"Bearer {reportToken}",
                    upload.Headers["Authorization"]);
                BehaviorTestCheck.Equal(
                    installationId.ToString("D"),
                    upload.Headers["X-GuluPet-Installation-Id"]);
                BehaviorTestCheck.Equal(
                    result.ReportId!.Value.ToString("D"),
                    upload.Headers["Idempotency-Key"]);
                BehaviorTestCheck.Equal(
                    timeProvider.GetUtcNow().ToString("O"),
                    upload.Headers["X-GuluPet-Timestamp"]);
                BehaviorTestCheck.True(
                    upload.Headers["Content-Type"].StartsWith(
                        "application/json",
                        StringComparison.OrdinalIgnoreCase));

                string expectedHash = Convert.ToHexString(
                        SHA256.HashData(upload.Body))
                    .ToLowerInvariant();
                BehaviorTestCheck.Equal(
                    expectedHash,
                    upload.Headers["X-Content-SHA256"]);

                string bodyText = Encoding.UTF8.GetString(upload.Body);
                BehaviorTestCheck.False(
                    bodyText.Contains(promptSentinel, StringComparison.Ordinal));
                BehaviorTestCheck.False(
                    bodyText.Contains(apiKeySentinel, StringComparison.Ordinal));
                BehaviorTestCheck.False(
                    bodyText.Contains("\"prompt\"", StringComparison.OrdinalIgnoreCase));
                BehaviorTestCheck.False(
                    bodyText.Contains("apiKey", StringComparison.OrdinalIgnoreCase));
                BehaviorTestCheck.False(
                    bodyText.Contains(reportToken, StringComparison.Ordinal));

                using JsonDocument payload = JsonDocument.Parse(upload.Body);
                JsonElement root = payload.RootElement;
                BehaviorTestCheck.Equal(
                    installationId,
                    root.GetProperty("installationId").GetGuid());
                BehaviorTestCheck.Equal(
                    result.ReportId.Value,
                    root.GetProperty("reportId").GetGuid());
                JsonElement uploadedEvent = root
                    .GetProperty("events")
                    .EnumerateArray()
                    .Single();
                BehaviorTestCheck.Equal(
                    "diary",
                    uploadedEvent.GetProperty("component").GetString()!);
                BehaviorTestCheck.Equal(
                    "503",
                    uploadedEvent
                        .GetProperty("context")
                        .GetProperty("statusCode")
                        .GetString()!);
                BehaviorTestCheck.False(
                    uploadedEvent.GetProperty("context").TryGetProperty(
                        "prompt",
                        out _));

                BehaviorTestCheck.Equal(
                    new LogReportCredential(
                        installationId,
                        reportToken,
                        RegistrationConfirmed: true),
                    credentialStore.Load()!);
                BehaviorTestCheck.Null(pendingStore.Load());
                BehaviorTestCheck.Equal(1, reportedStore.Load().Count);

                ErrorLogReportResult duplicateClick =
                    await uploader.UploadAsync();
                BehaviorTestCheck.Equal(
                    ErrorLogReportOutcome.NoErrors,
                    duplicateClick.Outcome);
                BehaviorTestCheck.Equal(2, handler.Requests.Count);
            });

    private static Task ExistingCredentialSkipsRegistration() =>
        WithTemporaryEnvironment(
            async (errorLog, credentialStore, pendingStore, reportedStore, timeProvider) =>
            {
                WriteError(errorLog);
                var credential = new LogReportCredential(
                    Guid.Parse("b8099081-14d4-42bd-b32d-7a5834cd9c1d"),
                    "existing_token_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ");
                credentialStore.Save(credential);
                var handler = new FakeHttpMessageHandler();
                handler.EnqueueStatus(HttpStatusCode.Accepted);
                using var httpClient = new HttpClient(handler);
                using var uploader = CreateUploader(
                    errorLog,
                    credentialStore,
                    pendingStore,
                    reportedStore,
                    httpClient,
                    timeProvider);

                ErrorLogReportResult result = await uploader.UploadAsync();

                BehaviorTestCheck.Equal(
                    ErrorLogReportOutcome.Uploaded,
                    result.Outcome);
                BehaviorTestCheck.Equal(1, handler.Requests.Count);
                CapturedRequest request = handler.Requests.Single();
                BehaviorTestCheck.Equal(
                    new Uri(ServiceBaseUri, "v1/log-reports"),
                    request.RequestUri);
                BehaviorTestCheck.Equal(
                    $"Bearer {credential.Token}",
                    request.Headers["Authorization"]);
                BehaviorTestCheck.Equal(
                    credential.InstallationId.ToString("D"),
                    request.Headers["X-GuluPet-Installation-Id"]);
            });

    private static Task EventsOlderThanThreeDaysAreNotUploaded() =>
        WithTemporaryEnvironment(
            async (errorLog, credentialStore, pendingStore, reportedStore, timeProvider) =>
            {
                DateTimeOffset nowUtc = timeProvider.GetUtcNow();
                Guid oldEventId = Guid.Parse(
                    "5890b404-c2cc-49a5-bcec-ec9f27ac6cf4");
                var oldRecord = new LocalErrorRecord
                {
                    EventId = oldEventId,
                    OccurredAtUtc = nowUtc.AddDays(-3).AddSeconds(-1),
                    Component = "diagnostics",
                    Operation = "retention-contract",
                    Code = "older-than-three-days",
                    AppVersion = "test",
                };
                Directory.CreateDirectory(errorLog.DirectoryPath);
                string oldLogPath = Path.Combine(
                    errorLog.DirectoryPath,
                    "errors-old-upload-contract.jsonl");
                File.WriteAllText(
                    oldLogPath,
                    JsonSerializer.Serialize(
                        oldRecord,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    + Environment.NewLine);
                File.SetLastWriteTimeUtc(oldLogPath, nowUtc.UtcDateTime);
                errorLog.Write(
                    "diagnostics",
                    "retention-contract",
                    "within-three-days");

                credentialStore.Save(new LogReportCredential(
                    Guid.Parse("ea43d66c-ced1-48c5-8f30-1273c10b7084"),
                    "retention_token_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
                var handler = new FakeHttpMessageHandler();
                handler.EnqueueStatus(HttpStatusCode.Accepted);
                using var httpClient = new HttpClient(handler);
                using var uploader = CreateUploader(
                    errorLog,
                    credentialStore,
                    pendingStore,
                    reportedStore,
                    httpClient,
                    timeProvider);

                ErrorLogReportResult result = await uploader.UploadAsync();

                BehaviorTestCheck.Equal(
                    ErrorLogReportOutcome.Uploaded,
                    result.Outcome);
                using JsonDocument payload =
                    JsonDocument.Parse(handler.Requests.Single().Body);
                JsonElement uploadedEvent = payload.RootElement
                    .GetProperty("events")
                    .EnumerateArray()
                    .Single();
                BehaviorTestCheck.Equal(
                    "within-three-days",
                    uploadedEvent.GetProperty("errorCode").GetString()!);
                BehaviorTestCheck.False(
                    reportedStore.LoadEventIds().Contains(oldEventId));
                BehaviorTestCheck.Null(pendingStore.Load());
            });

    private static Task UploadHttpFailureReturnsFailedResult() =>
        WithTemporaryEnvironment(
            async (errorLog, credentialStore, pendingStore, reportedStore, timeProvider) =>
            {
                WriteError(errorLog);
                credentialStore.Save(new LogReportCredential(
                    Guid.Parse("72af4440-b783-43fa-8dca-72d026bf0ee8"),
                    "failure_token_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
                var handler = new FakeHttpMessageHandler();
                handler.EnqueueStatus(HttpStatusCode.InternalServerError);
                using var httpClient = new HttpClient(handler);
                using var uploader = CreateUploader(
                    errorLog,
                    credentialStore,
                    pendingStore,
                    reportedStore,
                    httpClient,
                    timeProvider);

                ErrorLogReportResult result = await uploader.UploadAsync();

                BehaviorTestCheck.Equal(
                    ErrorLogReportOutcome.Failed,
                    result.Outcome);
                BehaviorTestCheck.Equal(
                    HttpStatusCode.InternalServerError,
                    result.StatusCode);
                BehaviorTestCheck.Equal("upload-http-500", result.FailureCode!);
                BehaviorTestCheck.True(result.ReportId.HasValue);
                BehaviorTestCheck.Equal(1, handler.Requests.Count);
                BehaviorTestCheck.Equal(
                    result.ReportId!.Value,
                    pendingStore.Load()!.ReportId);
            });

    private static Task AmbiguousFailureRetriesExactPersistedEnvelope() =>
        WithTemporaryEnvironment(
            async (errorLog, credentialStore, pendingStore, reportedStore, timeProvider) =>
            {
                WriteError(errorLog);
                var credential = new LogReportCredential(
                    Guid.Parse("88906b0c-8b07-4830-8d4a-50e2a3f4994a"),
                    "retry_token_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ");
                credentialStore.Save(credential);

                var firstHandler = new FakeHttpMessageHandler();
                firstHandler.EnqueueException(
                    new HttpRequestException("simulated lost response"));
                using (var firstClient = new HttpClient(firstHandler))
                using (var firstUploader = CreateUploader(
                           errorLog,
                           credentialStore,
                           pendingStore,
                           reportedStore,
                           firstClient,
                           timeProvider))
                {
                    ErrorLogReportResult first =
                        await firstUploader.UploadAsync();
                    BehaviorTestCheck.Equal(
                        ErrorLogReportOutcome.Failed,
                        first.Outcome);
                    BehaviorTestCheck.Equal(
                        "upload-network-failed",
                        first.FailureCode!);
                }

                PendingErrorReport persisted = pendingStore.Load()
                    ?? throw new InvalidOperationException(
                        "The ambiguous report was not persisted.");
                CapturedRequest original = firstHandler.Requests.Single();
                File.SetLastWriteTimeUtc(
                    pendingStore.PendingPath,
                    timeProvider.GetUtcNow().AddDays(-4).UtcDateTime);
                _ = new LocalErrorLog(errorLog.DirectoryPath, timeProvider);
                BehaviorTestCheck.True(File.Exists(pendingStore.PendingPath));

                var secondHandler = new FakeHttpMessageHandler();
                secondHandler.EnqueueStatus(HttpStatusCode.Accepted);
                using (var secondClient = new HttpClient(secondHandler))
                using (var secondUploader = CreateUploader(
                           errorLog,
                           credentialStore,
                           pendingStore,
                           reportedStore,
                           secondClient,
                           timeProvider))
                {
                    ErrorLogReportResult second =
                        await secondUploader.UploadAsync();
                    BehaviorTestCheck.Equal(
                        ErrorLogReportOutcome.Uploaded,
                        second.Outcome);
                    BehaviorTestCheck.Equal(
                        persisted.ReportId,
                        second.ReportId!.Value);
                }

                CapturedRequest replay = secondHandler.Requests.Single();
                BehaviorTestCheck.Equal(
                    original.Headers["Idempotency-Key"],
                    replay.Headers["Idempotency-Key"]);
                BehaviorTestCheck.Equal(
                    original.Headers["X-GuluPet-Timestamp"],
                    replay.Headers["X-GuluPet-Timestamp"]);
                BehaviorTestCheck.Equal(
                    original.Headers["X-Content-SHA256"],
                    replay.Headers["X-Content-SHA256"]);
                BehaviorTestCheck.True(original.Body.SequenceEqual(replay.Body));
                BehaviorTestCheck.Null(pendingStore.Load());
            });

    private static Task LostRegistrationResponseRetriesSameIntent() =>
        WithTemporaryEnvironment(
            async (errorLog, credentialStore, pendingStore, reportedStore, timeProvider) =>
            {
                WriteError(errorLog);
                var firstHandler = new FakeHttpMessageHandler();
                firstHandler.EnqueueException(
                    new HttpRequestException("simulated lost 201"));
                using (var firstClient = new HttpClient(firstHandler))
                using (var firstUploader = CreateUploader(
                           errorLog,
                           credentialStore,
                           pendingStore,
                           reportedStore,
                           firstClient,
                           timeProvider))
                {
                    ErrorLogReportResult first =
                        await firstUploader.UploadAsync();
                    BehaviorTestCheck.Equal(
                        ErrorLogReportOutcome.Failed,
                        first.Outcome);
                    BehaviorTestCheck.Equal(
                        "register-failed",
                        first.FailureCode!);
                }

                LogReportCredential intent = credentialStore.Load()
                    ?? throw new InvalidOperationException(
                        "The registration intent was not persisted.");
                BehaviorTestCheck.False(intent.RegistrationConfirmed);
                CapturedRequest firstRegistration =
                    firstHandler.Requests.Single();

                var secondHandler = new FakeHttpMessageHandler();
                secondHandler.EnqueueStatus(HttpStatusCode.Created);
                secondHandler.EnqueueStatus(HttpStatusCode.Accepted);
                using (var secondClient = new HttpClient(secondHandler))
                using (var secondUploader = CreateUploader(
                           errorLog,
                           credentialStore,
                           pendingStore,
                           reportedStore,
                           secondClient,
                           timeProvider))
                {
                    ErrorLogReportResult second =
                        await secondUploader.UploadAsync();
                    BehaviorTestCheck.Equal(
                        ErrorLogReportOutcome.Uploaded,
                        second.Outcome);
                }

                CapturedRequest repeatedRegistration =
                    secondHandler.Requests[0];
                BehaviorTestCheck.True(
                    firstRegistration.Body.SequenceEqual(
                        repeatedRegistration.Body));
                BehaviorTestCheck.Equal(
                    $"Bearer {intent.Token}",
                    secondHandler.Requests[1].Headers["Authorization"]);
                BehaviorTestCheck.True(
                    credentialStore.Load()!.RegistrationConfirmed);
            });

    private static Task PendingUnauthorizedReRegistersSameCredentialAndEnvelope() =>
        WithTemporaryEnvironment(
            async (errorLog, credentialStore, pendingStore, reportedStore, timeProvider) =>
            {
                WriteError(errorLog);
                var credential = new LogReportCredential(
                    Guid.Parse("96858037-e79b-47a7-9527-169344a4bca9"),
                    "recovery_token_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ");
                credentialStore.Save(credential);

                var initialHandler = new FakeHttpMessageHandler();
                initialHandler.EnqueueStatus(HttpStatusCode.InternalServerError);
                using (var initialClient = new HttpClient(initialHandler))
                using (var initialUploader = CreateUploader(
                           errorLog,
                           credentialStore,
                           pendingStore,
                           reportedStore,
                           initialClient,
                           timeProvider))
                {
                    _ = await initialUploader.UploadAsync();
                }

                var recoveryHandler = new FakeHttpMessageHandler();
                recoveryHandler.EnqueueStatus(HttpStatusCode.Unauthorized);
                recoveryHandler.EnqueueStatus(HttpStatusCode.Created);
                recoveryHandler.EnqueueStatus(HttpStatusCode.Accepted);
                using (var recoveryClient = new HttpClient(recoveryHandler))
                using (var recoveryUploader = CreateUploader(
                           errorLog,
                           credentialStore,
                           pendingStore,
                           reportedStore,
                           recoveryClient,
                           timeProvider))
                {
                    ErrorLogReportResult recovered =
                        await recoveryUploader.UploadAsync();
                    BehaviorTestCheck.Equal(
                        ErrorLogReportOutcome.Uploaded,
                        recovered.Outcome);
                }

                BehaviorTestCheck.Equal(3, recoveryHandler.Requests.Count);
                CapturedRequest firstUpload = recoveryHandler.Requests[0];
                CapturedRequest registration = recoveryHandler.Requests[1];
                CapturedRequest secondUpload = recoveryHandler.Requests[2];
                BehaviorTestCheck.Equal(
                    new Uri(ServiceBaseUri, "v1/installations"),
                    registration.RequestUri);
                BehaviorTestCheck.True(
                    firstUpload.Body.SequenceEqual(secondUpload.Body));
                BehaviorTestCheck.Equal(
                    firstUpload.Headers["Idempotency-Key"],
                    secondUpload.Headers["Idempotency-Key"]);
                using JsonDocument registrationBody =
                    JsonDocument.Parse(registration.Body);
                BehaviorTestCheck.Equal(
                    credential.InstallationId,
                    registrationBody.RootElement
                        .GetProperty("installationId")
                        .GetGuid());
                BehaviorTestCheck.Equal(
                    credential.Token,
                    registrationBody.RootElement
                        .GetProperty("token")
                        .GetString()!);
                BehaviorTestCheck.Null(pendingStore.Load());
            });

    private static Task BoundedPayloadReceiptsOnlyAcknowledgeIncludedEvents() =>
        WithTemporaryEnvironment(
            async (errorLog, credentialStore, pendingStore, reportedStore, timeProvider) =>
            {
                const int totalEvents = 100;
                for (var index = 0; index < totalEvents; index++)
                {
                    errorLog.Write(
                        "diagnostics",
                        "large-report-test",
                        "large-test-error",
                        new InvalidOperationException(
                            $"{index:D3}:" + new string('x', 1000)));
                }

                credentialStore.Save(new LogReportCredential(
                    Guid.Parse("1ea37cb1-8a5b-4dff-af82-0e965b9d4435"),
                    "bounded_token_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
                var handler = new FakeHttpMessageHandler();
                handler.EnqueueStatus(HttpStatusCode.Accepted);
                using var httpClient = new HttpClient(handler);
                using var uploader = CreateUploader(
                    errorLog,
                    credentialStore,
                    pendingStore,
                    reportedStore,
                    httpClient,
                    timeProvider,
                    maximumRequestBytes: 64 * 1024);

                ErrorLogReportResult result = await uploader.UploadAsync();

                BehaviorTestCheck.Equal(
                    ErrorLogReportOutcome.Uploaded,
                    result.Outcome);
                using JsonDocument payload =
                    JsonDocument.Parse(handler.Requests.Single().Body);
                int uploadedEvents = payload.RootElement
                    .GetProperty("events")
                    .GetArrayLength();
                BehaviorTestCheck.True(uploadedEvents > 0);
                BehaviorTestCheck.True(uploadedEvents < totalEvents);
                IReadOnlySet<Guid> reportedIds = reportedStore.LoadEventIds();
                BehaviorTestCheck.Equal(uploadedEvents, reportedIds.Count);
                BehaviorTestCheck.Equal(
                    totalEvents - uploadedEvents,
                    errorLog.ReadRecentRecords(
                        TimeSpan.FromDays(7),
                        500,
                        reportedIds).Count);
            });

    private static ErrorLogReportUploader CreateUploader(
        LocalErrorLog errorLog,
        LogReportCredentialStore credentialStore,
        PendingErrorReportStore pendingStore,
        ReportedErrorEventStore reportedStore,
        HttpClient httpClient,
        TimeProvider timeProvider,
        int maximumRequestBytes = 1024 * 1024) =>
        new(
            errorLog,
            credentialStore,
            pendingStore,
            reportedStore,
            httpClient,
            new ErrorLogReportOptions
            {
                ServiceBaseUri = ServiceBaseUri,
                RequestTimeout = TimeSpan.FromSeconds(5),
                MaximumRequestBytes = maximumRequestBytes,
                Enabled = true,
            },
            timeProvider);

    private static void WriteError(
        LocalErrorLog errorLog,
        string prompt = "unused-prompt",
        string apiKey = "sk-unused-api-key")
    {
        errorLog.Write(
            "diary",
            "generate",
            "http-503",
            new InvalidOperationException(
                $"Authorization: Bearer {apiKey}"),
            new Dictionary<string, string>
            {
                ["statusCode"] = "503",
                ["prompt"] = prompt,
                ["apiKey"] = apiKey,
            });
    }

    private static async Task WithTemporaryEnvironment(
        Func<
            LocalErrorLog,
            LogReportCredentialStore,
            PendingErrorReportStore,
            ReportedErrorEventStore,
            TimeProvider,
            Task> test)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.ErrorLogReport.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);
        try
        {
            await test(
                new LocalErrorLog(
                    Path.Combine(root, "logs"),
                    timeProvider),
                new LogReportCredentialStore(
                    Path.Combine(root, "credential.bin")),
                new PendingErrorReportStore(
                    Path.Combine(root, "pending-report.bin")),
                new ReportedErrorEventStore(
                    Path.Combine(root, "reported-events.bin"),
                    timeProvider),
                timeProvider);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RunAsync(string name, Func<Task> test)
    {
        test().GetAwaiter().GetResult();
        Console.WriteLine(
            $"PASS {nameof(ErrorLogReportUploaderTests)}.{name}");
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri RequestUri,
        IReadOnlyDictionary<string, string> Headers,
        byte[] Body);

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = [];
        private readonly List<CapturedRequest> _requests = [];

        public IReadOnlyList<CapturedRequest> Requests => _requests;

        public void EnqueueStatus(HttpStatusCode statusCode) =>
            _responses.Enqueue(() => new HttpResponseMessage(statusCode));

        public void EnqueueJson(HttpStatusCode statusCode, string json) =>
            _responses.Enqueue(
                () => new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"),
                });

        public void EnqueueException(Exception exception) =>
            _responses.Enqueue(() => throw exception);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var headers = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IEnumerable<string>> header
                     in request.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            if (request.Content is not null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header
                         in request.Content.Headers)
                {
                    headers[header.Key] = string.Join(", ", header.Value);
                }
            }

            _requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri
                    ?? throw new InvalidOperationException(
                        "The request URI is missing."),
                headers,
                body));
            if (!_responses.TryDequeue(out Func<HttpResponseMessage>? response))
            {
                throw new InvalidOperationException(
                    "No fake HTTP response was queued.");
            }

            return response();
        }
    }
}
