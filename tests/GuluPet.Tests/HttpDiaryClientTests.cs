using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuluPet.Diary;
using GuluPet.Persistence;

namespace GuluPet.Tests;

internal static class HttpDiaryClientTests
{
    private static readonly Uri Endpoint =
        new("https://diary-proxy.test/v1/diary-generations");
    private static readonly Uri RegistrationEndpoint =
        new("https://diary-proxy.test/v1/installations");

    public static void RunAll()
    {
        RunAsync(
            nameof(RegistersAndSendsOnlyTypedFacts),
            RegistersAndSendsOnlyTypedFacts);
        RunAsync(
            nameof(UnauthorizedReregistersAndReplaysExactRequest),
            UnauthorizedReregistersAndReplaysExactRequest);
        RunAsync(
            nameof(NetworkFailureReplaysAcrossClientRestart),
            NetworkFailureReplaysAcrossClientRestart);
        RunAsync(
            nameof(TransientStatusesAndRetryAfterRemainRetryable),
            TransientStatusesAndRetryAfterRemainRetryable);
        RunAsync(
            nameof(ChunkedSuccessErrorEnvelopeIsClassified),
            ChunkedSuccessErrorEnvelopeIsClassified);
        RunAsync(
            nameof(BalanceFailuresRetryWithoutDiscardingPendingRequest),
            BalanceFailuresRetryWithoutDiscardingPendingRequest);
        RunAsync(
            nameof(ReconcilePrunesOnlyValidatedNinetyFirstDayPendingRequest),
            ReconcilePrunesOnlyValidatedNinetyFirstDayPendingRequest);
        RunAsync(
            nameof(ScheduleJitterCredentialFailureFallsBackWithoutThrowing),
            ScheduleJitterCredentialFailureFallsBackWithoutThrowing);
    }

    private static async Task RegistersAndSendsOnlyTypedFacts()
    {
        await WithFixtureAsync(
            async fixture =>
            {
                var handler = new RecordingHandler((request, _) =>
                {
                    if (request.Uri == RegistrationEndpoint)
                    {
                        return new HttpResponseMessage(HttpStatusCode.Created);
                    }

                    BehaviorTestCheck.Equal(Endpoint, request.Uri);
                    return SuccessFor(request.Body);
                });
                using var httpClient = new HttpClient(handler);
                using var client = fixture.CreateClient(httpClient);

                DiaryGenerationResult result = await client.GenerateAsync(
                    CreateInput(),
                    CancellationToken.None);

                BehaviorTestCheck.Equal(
                    DiaryGenerationOutcome.Success,
                    result.Outcome);
                BehaviorTestCheck.Equal(2, handler.Requests.Count);
                CapturedRequest registration = handler.Requests[0];
                CapturedRequest generation = handler.Requests[1];
                BehaviorTestCheck.Equal(
                    RegistrationEndpoint,
                    registration.Uri);
                BehaviorTestCheck.Equal("Bearer", generation.Authorization?.Scheme);
                BehaviorTestCheck.Equal(
                    fixture.CredentialStore.Load()!.Token,
                    generation.Authorization?.Parameter);
                BehaviorTestCheck.Equal(
                    fixture.CredentialStore.Load()!.InstallationId.ToString("D"),
                    generation.Header("X-GuluPet-Installation-Id"));
                BehaviorTestCheck.Equal(
                    Convert.ToHexString(SHA256.HashData(generation.Body))
                        .ToLowerInvariant(),
                    generation.Header("X-Content-SHA256"));

                using JsonDocument document = JsonDocument.Parse(generation.Body);
                JsonElement root = document.RootElement;
                BehaviorTestCheck.Equal(1, root.GetProperty("schemaVersion").GetInt32());
                BehaviorTestCheck.Equal(
                    generation.Header("Idempotency-Key"),
                    root.GetProperty("requestId").GetGuid().ToString("D"));
                BehaviorTestCheck.Equal(
                    "2026-08-03",
                    root.GetProperty("diaryDate").GetString());
                BehaviorTestCheck.True(root.TryGetProperty("period", out _));
                BehaviorTestCheck.True(root.TryGetProperty("runtimeSeconds", out _));
                BehaviorTestCheck.True(root.TryGetProperty("interactions", out _));
                BehaviorTestCheck.True(root.TryGetProperty("postcards", out _));
                BehaviorTestCheck.True(root.TryGetProperty("memories", out _));
                BehaviorTestCheck.True(root.TryGetProperty("weather", out _));
                string text = Encoding.UTF8.GetString(generation.Body);
                foreach (string forbidden in new[]
                         {
                             "apiKey",
                             "model",
                             "messages",
                             "prompt",
                             "system",
                             "title",
                             "detail",
                             "description",
                         })
                {
                    BehaviorTestCheck.False(
                        text.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
                }
            });
    }

    private static async Task UnauthorizedReregistersAndReplaysExactRequest()
    {
        await WithFixtureAsync(
            async fixture =>
            {
                fixture.SaveConfirmedCredential();
                var generationCalls = 0;
                var handler = new RecordingHandler((request, _) =>
                {
                    if (request.Uri == RegistrationEndpoint)
                    {
                        return new HttpResponseMessage(HttpStatusCode.Created);
                    }

                    generationCalls++;
                    return generationCalls == 1
                        ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                        : SuccessFor(request.Body);
                });
                using var httpClient = new HttpClient(handler);
                using var client = fixture.CreateClient(httpClient);

                DiaryGenerationResult result = await client.GenerateAsync(
                    CreateInput(),
                    CancellationToken.None);

                BehaviorTestCheck.Equal(
                    DiaryGenerationOutcome.Success,
                    result.Outcome);
                BehaviorTestCheck.Equal(3, handler.Requests.Count);
                CapturedRequest first = handler.Requests[0];
                CapturedRequest replay = handler.Requests[2];
                BehaviorTestCheck.SequenceEqual(first.Body, replay.Body);
                BehaviorTestCheck.Equal(
                    first.Header("Idempotency-Key"),
                    replay.Header("Idempotency-Key"));
                BehaviorTestCheck.Equal(
                    first.Header("X-Content-SHA256"),
                    replay.Header("X-Content-SHA256"));
                BehaviorTestCheck.True(
                    fixture.CredentialStore.Load()!.RegistrationConfirmed);
            });
    }

    private static async Task NetworkFailureReplaysAcrossClientRestart()
    {
        await WithFixtureAsync(
            async fixture =>
            {
                fixture.SaveConfirmedCredential();
                var failedHandler = new RecordingHandler((request, _) =>
                    throw new HttpRequestException("fixture network failure"));
                byte[] firstBody;
                string firstKey;
                using (var httpClient = new HttpClient(failedHandler))
                using (var client = fixture.CreateClient(httpClient))
                {
                    DiaryGenerationResult failed = await client.GenerateAsync(
                        CreateInput(),
                        CancellationToken.None);
                    BehaviorTestCheck.Equal(
                        DiaryGenerationOutcome.RetryableFailure,
                        failed.Outcome);
                    firstBody = failedHandler.Requests.Single().Body;
                    firstKey = failedHandler.Requests.Single()
                        .Header("Idempotency-Key");
                }

                var replayHandler = new RecordingHandler((request, _) =>
                    SuccessFor(request.Body, duplicate: true));
                using var replayHttpClient = new HttpClient(replayHandler);
                using var replayClient = fixture.CreateClient(replayHttpClient);
                DiaryGenerationResult replayed = await replayClient.GenerateAsync(
                    CreateInput(),
                    CancellationToken.None);

                BehaviorTestCheck.Equal(
                    DiaryGenerationOutcome.Success,
                    replayed.Outcome);
                BehaviorTestCheck.SequenceEqual(
                    firstBody,
                    replayHandler.Requests.Single().Body);
                BehaviorTestCheck.Equal(
                    firstKey,
                    replayHandler.Requests.Single().Header("Idempotency-Key"));
                BehaviorTestCheck.NotNull(
                    fixture.PendingStore.Load(CreateInput().Date));
                replayClient.Acknowledge(CreateInput(), replayed);
                BehaviorTestCheck.Null(
                    fixture.PendingStore.Load(CreateInput().Date));
            });
    }

    private static async Task TransientStatusesAndRetryAfterRemainRetryable()
    {
        foreach (int status in new[] { 403, 404, 405, 408, 425, 429, 503 })
        {
            await WithFixtureAsync(
                async fixture =>
                {
                    fixture.SaveConfirmedCredential();
                    var handler = new RecordingHandler((_, _) =>
                    {
                        var response = new HttpResponseMessage(
                            (HttpStatusCode)status);
                        if (status == 429)
                        {
                            response.Headers.RetryAfter =
                                new RetryConditionHeaderValue(
                                    TimeSpan.FromHours(1));
                        }

                        return response;
                    });
                    using var httpClient = new HttpClient(handler);
                    using var client = fixture.CreateClient(
                        httpClient,
                        immediateRetryLimit: TimeSpan.Zero);

                    DiaryGenerationResult result = await client.GenerateAsync(
                        CreateInput(),
                        CancellationToken.None);

                    BehaviorTestCheck.Equal(
                        DiaryGenerationOutcome.RetryableFailure,
                        result.Outcome);
                    if (status == 429)
                    {
                        BehaviorTestCheck.Equal(
                            TimeSpan.FromHours(1),
                            result.RetryAfter);
                    }

                    BehaviorTestCheck.NotNull(
                        fixture.PendingStore.Load(CreateInput().Date));
                });
        }
    }

    private static async Task ChunkedSuccessErrorEnvelopeIsClassified()
    {
        await WithFixtureAsync(
            async fixture =>
            {
                fixture.SaveConfirmedCredential();
                var handler = new RecordingHandler((request, _) =>
                {
                    Guid requestId = RequestId(request.Body);
                    return JsonResponse(
                        " \r\n\t" + JsonSerializer.Serialize(new
                        {
                            requestId,
                            error = "upstream_unavailable",
                            retryable = true,
                            retryAfterSeconds = 60,
                        }));
                });
                using var httpClient = new HttpClient(handler);
                using var client = fixture.CreateClient(
                    httpClient,
                    immediateRetryLimit: TimeSpan.Zero);

                DiaryGenerationResult result = await client.GenerateAsync(
                    CreateInput(),
                    CancellationToken.None);

                BehaviorTestCheck.Equal(
                    DiaryGenerationOutcome.RetryableFailure,
                    result.Outcome);
                BehaviorTestCheck.Equal("upstream_unavailable", result.Code);
                BehaviorTestCheck.Equal(TimeSpan.FromSeconds(60), result.RetryAfter);
            });
    }

    private static async Task BalanceFailuresRetryWithoutDiscardingPendingRequest()
    {
        await WithFixtureAsync(
            async fixture =>
            {
                fixture.SaveConfirmedCredential();
                var handler = new RecordingHandler((request, _) =>
                {
                    Guid requestId = RequestId(request.Body);
                    return JsonResponse(JsonSerializer.Serialize(new
                    {
                        requestId,
                        error = "upstream_balance_unavailable",
                        retryable = true,
                        retryAfterSeconds = 21600,
                    }));
                });
                using var httpClient = new HttpClient(handler);
                using var client = fixture.CreateClient(
                    httpClient,
                    immediateRetryLimit: TimeSpan.Zero);

                DiaryGenerationResult result = await client.GenerateAsync(
                    CreateInput(),
                    CancellationToken.None);

                BehaviorTestCheck.Equal(
                    DiaryGenerationOutcome.RetryableFailure,
                    result.Outcome);
                BehaviorTestCheck.Equal(
                    TimeSpan.FromHours(6),
                    result.RetryAfter);
                BehaviorTestCheck.NotNull(
                    fixture.PendingStore.Load(CreateInput().Date));
            });

        await WithFixtureAsync(
            async fixture =>
            {
                fixture.SaveConfirmedCredential();
                var handler = new RecordingHandler((_, _) =>
                    new HttpResponseMessage(HttpStatusCode.PaymentRequired));
                using var httpClient = new HttpClient(handler);
                using var client = fixture.CreateClient(httpClient);

                DiaryGenerationResult result = await client.GenerateAsync(
                    CreateInput(),
                    CancellationToken.None);

                BehaviorTestCheck.Equal(
                    DiaryGenerationOutcome.RetryableFailure,
                    result.Outcome);
                BehaviorTestCheck.Equal(
                    TimeSpan.FromHours(6),
                    result.RetryAfter);
                BehaviorTestCheck.NotNull(
                    fixture.PendingStore.Load(CreateInput().Date));
            });
    }

    private static async Task ReconcilePrunesOnlyValidatedNinetyFirstDayPendingRequest()
    {
        await WithFixtureAsync(
            async fixture =>
            {
                fixture.SaveConfirmedCredential();
                var handler = new RecordingHandler((_, _) =>
                    throw new HttpRequestException("fixture network failure"));
                using var httpClient = new HttpClient(handler);
                using var client = fixture.CreateClient(httpClient);
                DateOnly latestRetainedDate = new(2026, 8, 3);
                DateOnly ninetyFirstDate = latestRetainedDate.AddDays(-90);
                DateOnly boundaryDate = latestRetainedDate.AddDays(-89);

                _ = await client.GenerateAsync(
                    CreateInput(ninetyFirstDate),
                    CancellationToken.None);
                _ = await client.GenerateAsync(
                    CreateInput(boundaryDate),
                    CancellationToken.None);
                BehaviorTestCheck.NotNull(
                    fixture.PendingStore.Load(ninetyFirstDate));
                BehaviorTestCheck.NotNull(
                    fixture.PendingStore.Load(boundaryDate));

                DateOnly malformedDate = latestRetainedDate.AddDays(-91);
                string malformedPath = Path.Combine(
                    fixture.PendingStore.DirectoryPath,
                    $"diary-{malformedDate:yyyy-MM-dd}.pending.json");
                File.WriteAllText(malformedPath, "user-owned-invalid-sentinel");
                string unrelatedPath = Path.Combine(
                    fixture.PendingStore.DirectoryPath,
                    "keep-user-file.txt");
                File.WriteAllText(unrelatedPath, "keep");

                DateTimeOffset attemptedAtUtc = DateTimeOffset.Parse(
                    "2026-08-03T10:00:00Z");
                DiaryDayState FailedDay(DateOnly date) =>
                    DiaryDayState.Create(date, attemptedAtUtc) with
                    {
                        RuntimeSeconds = 1900,
                        GenerationFailure = new DiaryGenerationFailure
                        {
                            Disposition =
                                DiaryGenerationFailureDisposition.Retryable,
                            Code = "network-error",
                            AttemptCount = 1,
                            LastAttemptAtUtc = attemptedAtUtc,
                        },
                    };
                client.Reconcile(
                    new DiaryState
                    {
                        UpdatedAtUtc = attemptedAtUtc,
                        Days =
                        [
                            FailedDay(ninetyFirstDate),
                            FailedDay(boundaryDate),
                        ],
                    },
                    latestRetainedDate);

                BehaviorTestCheck.Null(
                    fixture.PendingStore.Load(ninetyFirstDate));
                BehaviorTestCheck.NotNull(
                    fixture.PendingStore.Load(boundaryDate));
                BehaviorTestCheck.True(File.Exists(malformedPath));
                BehaviorTestCheck.True(File.Exists(unrelatedPath));
            });
    }

    private static Task ScheduleJitterCredentialFailureFallsBackWithoutThrowing()
    {
        Exception[] localFailures =
        [
            new CryptographicException("fixture DPAPI failure"),
            new IOException("fixture persistence failure"),
            new UnauthorizedAccessException("fixture access failure"),
            new InvalidDataException("fixture credential failure"),
        ];
        foreach (Exception failure in localFailures)
        {
            TimeSpan jitter = HttpDiaryClient.ResolveStableScheduleJitter(
                () => throw failure);
            BehaviorTestCheck.Equal(TimeSpan.Zero, jitter);
        }

        return Task.CompletedTask;
    }

    private static DiaryGenerationInput CreateInput()
        => CreateInput(new DateOnly(2026, 8, 3));

    private static DiaryGenerationInput CreateInput(DateOnly date)
    {
        var policy = new DiaryDatePolicy(TimeZoneInfo.Utc);
        return new DiaryGenerationInput(
            date,
            policy.GetPeriod(date),
            1900,
            [new DiaryInteractionCount { Kind = DiaryInteractionKinds.Click, Count = 2 }],
            [
                new DiaryUnlockRecord
                {
                    Id = "postcard-1",
                    Title = "晚风",
                    Detail = "广州",
                    UnlockedAtUtc = DateTimeOffset.Parse("2026-08-03T10:00:00Z"),
                },
            ],
            [
                new DiaryUnlockRecord
                {
                    Id = "memory-1",
                    Title = "手心",
                    Description = "咕噜嗅闻指尖。",
                    UnlockedAtUtc = DateTimeOffset.Parse("2026-08-03T11:00:00Z"),
                },
            ],
            new DiaryWeatherSnapshot
            {
                Kind = "clear",
                TemperatureCelsius = 28.5,
                ObservedAtUtc = DateTimeOffset.Parse("2026-08-03T09:00:00Z"),
            });
    }

    private static HttpResponseMessage SuccessFor(
        byte[] requestBody,
        bool duplicate = false)
    {
        Guid requestId = RequestId(requestBody);
        return JsonResponse(JsonSerializer.Serialize(new
        {
            requestId,
            duplicate,
            entry = new
            {
                title = "安静陪伴",
                mood = "安心",
                body = "今天我在窗边安静地陪了妈咪一会儿。",
                model = "local-template-v1",
                generatedAtUtc = DateTimeOffset.Parse(
                    "2026-08-03T18:31:00Z"),
            },
        }));
    }

    private static Guid RequestId(byte[] requestBody)
    {
        using JsonDocument request = JsonDocument.Parse(requestBody);
        return request.RootElement.GetProperty("requestId").GetGuid();
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static async Task WithFixtureAsync(Func<Fixture, Task> test)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.HttpDiary.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await test(new Fixture(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RunAsync(string name, Func<Task> test)
    {
        test().GetAwaiter().GetResult();
        Console.WriteLine($"PASS {nameof(HttpDiaryClientTests)}.{name}");
    }

    private sealed class Fixture
    {
        internal Fixture(string root)
        {
            CredentialStore = new LogReportCredentialStore(
                Path.Combine(root, "credential.bin"));
            PendingStore = new PendingDiaryGenerationStore(
                Path.Combine(root, "pending"));
        }

        internal LogReportCredentialStore CredentialStore { get; }

        internal PendingDiaryGenerationStore PendingStore { get; }

        internal void SaveConfirmedCredential() => CredentialStore.Save(
            new LogReportCredential(
                Guid.Parse("62d21e8d-9846-467b-b993-d642092d8a09"),
                "diary_proxy_test_token_0123456789_ABCDEFGHIJKLMN",
                RegistrationConfirmed: true));

        internal HttpDiaryClient CreateClient(
            HttpClient httpClient,
            TimeSpan? immediateRetryLimit = null) =>
            new(
                httpClient,
                new HttpDiaryOptions
                {
                    Enabled = true,
                    Endpoint = Endpoint,
                    RegistrationEndpoint = RegistrationEndpoint,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                    ImmediateRetryLimit =
                        immediateRetryLimit ?? TimeSpan.FromSeconds(10),
                },
                CredentialStore,
                PendingStore);
    }

    private sealed record CapturedRequest(
        Uri Uri,
        AuthenticationHeaderValue? Authorization,
        IReadOnlyDictionary<string, string> Headers,
        byte[] Body)
    {
        internal string Header(string name) => Headers[name];
    }

    private sealed class RecordingHandler(
        Func<CapturedRequest, int, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        internal List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[] body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var headers = request.Headers
                .ToDictionary(
                    static item => item.Key,
                    static item => string.Join(",", item.Value),
                    StringComparer.OrdinalIgnoreCase);
            var captured = new CapturedRequest(
                BehaviorTestCheck.NotNull(request.RequestUri),
                request.Headers.Authorization,
                headers,
                body);
            Requests.Add(captured);
            return responseFactory(captured, Requests.Count);
        }
    }
}
