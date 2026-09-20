using System.Net;
using GuluPet.Diary;

namespace GuluPet.Tests;

internal static class DiaryGenerationCoordinatorTests
{
    private static readonly TimeZoneInfo ChinaTimeZone =
        TimeZoneInfo.CreateCustomTimeZone(
            "GuluPet-Diary-Coordinator-China-Test",
            TimeSpan.FromHours(8),
            "GuluPet Diary Coordinator China Test",
            "GuluPet Diary Coordinator China Test");

    public static void RunAll()
    {
        Run(nameof(NoDataDoesNotGenerate), NoDataDoesNotGenerate);
        Run(
            nameof(ExactlyThirtyMinutesWithoutInteractionDoesNotGenerate),
            ExactlyThirtyMinutesWithoutInteractionDoesNotGenerate);
        Run(
            nameof(OutsideGenerationWindowDoesNotGenerate),
            OutsideGenerationWindowDoesNotGenerate);
        Run(
            nameof(SuccessIsPersistedAndIdempotent),
            SuccessIsPersistedAndIdempotent);
        Run(
            nameof(ServerFailureRetriesAfterThirtyMinutes),
            ServerFailureRetriesAfterThirtyMinutes);
        Run(
            nameof(NetworkFailureRetriesAfterThirtyMinutes),
            NetworkFailureRetriesAfterThirtyMinutes);
        Run(
            nameof(ScheduledSlotJitterDoesNotSkipRetry),
            ScheduledSlotJitterDoesNotSkipRetry);
        Run(
            nameof(HistoricalPristineDayIsNotBackfilled),
            HistoricalPristineDayIsNotBackfilled);
        Run(
            nameof(HistoricalRetryableFailureRemainsEligible),
            HistoricalRetryableFailureRemainsEligible);
        Run(
            nameof(PaymentRequiredDefersAndCanRecover),
            PaymentRequiredDefersAndCanRecover);
        Run(
            nameof(OtherClientFailureIsTerminalAndDoesNotRetry),
            OtherClientFailureIsTerminalAndDoesNotRetry);
        Run(
            nameof(RetryAfterDefersWithoutMakingDateTerminal),
            RetryAfterDefersWithoutMakingDateTerminal);
        Run(
            nameof(FreshCurrentDayPrecedesHistoricalRetryBacklog),
            FreshCurrentDayPrecedesHistoricalRetryBacklog);
        Run(
            nameof(RateLimitedBacklogRotatesAtNextSlot),
            RateLimitedBacklogRotatesAtNextSlot);
        Run(
            nameof(AcknowledgementRunsAfterDurableLedgerCommit),
            AcknowledgementRunsAfterDurableLedgerCommit);
        Run(
            nameof(BacklogIsBoundedToNinetyDaysAndEightSuccessesPerPass),
            BacklogIsBoundedToNinetyDaysAndEightSuccessesPerPass);
    }

    private static void NoDataDoesNotGenerate()
    {
        DateTimeOffset nowUtc = Utc(2026, 8, 3, 18, 30, 0);
        var ledger = new DiaryLedger();
        var generator = new ScriptedDiaryGenerator();
        using var coordinator = CreateCoordinator(ledger, generator);

        int generated = Check(coordinator, nowUtc);

        BehaviorTestCheck.Equal(0, generated);
        BehaviorTestCheck.Equal(0, generator.Calls);
        BehaviorTestCheck.Equal(0, ledger.Current.Days.Count);
    }

    private static void ExactlyThirtyMinutesWithoutInteractionDoesNotGenerate()
    {
        var date = new DateOnly(2026, 8, 3);
        DateTimeOffset nowUtc = Utc(2026, 8, 3, 18, 30, 0);
        var ledger = new DiaryLedger();
        ledger.AddRuntime(
            date,
            (long)TimeSpan.FromMinutes(30).TotalSeconds,
            Utc(2026, 8, 3, 18, 29, 0));
        var generator = new ScriptedDiaryGenerator();
        using var coordinator = CreateCoordinator(ledger, generator);

        int generated = Check(coordinator, nowUtc);

        BehaviorTestCheck.Equal(0, generated);
        BehaviorTestCheck.Equal(0, generator.Calls);
        BehaviorTestCheck.False(GetDay(ledger, date).HasMeaningfulRecord);
    }

    private static void OutsideGenerationWindowDoesNotGenerate()
    {
        var date = new DateOnly(2026, 8, 3);
        var ledger = LedgerWithInteraction(date);
        var generator = new ScriptedDiaryGenerator();
        using var coordinator = CreateCoordinator(ledger, generator);

        int generated = Check(
            coordinator,
            Utc(2026, 8, 4, 8, 30, 1));

        BehaviorTestCheck.Equal(0, generated);
        BehaviorTestCheck.Equal(0, generator.Calls);
        DiaryDayState day = GetDay(ledger, date);
        BehaviorTestCheck.Null(day.Entry);
        BehaviorTestCheck.Null(day.GenerationFailure);
    }

    private static void SuccessIsPersistedAndIdempotent()
    {
        var date = new DateOnly(2026, 8, 3);
        DateTimeOffset nowUtc = Utc(2026, 8, 3, 18, 30, 0);
        var ledger = LedgerWithInteraction(date);
        GeneratedDiaryEntry entry = CreateEntry(nowUtc);
        var generator = new ScriptedDiaryGenerator(
            DiaryGenerationResult.Success(entry));
        using var coordinator = CreateCoordinator(ledger, generator);

        int firstGenerated = Check(coordinator, nowUtc);
        int secondGenerated = Check(coordinator, nowUtc.AddMinutes(30));

        BehaviorTestCheck.Equal(1, firstGenerated);
        BehaviorTestCheck.Equal(0, secondGenerated);
        BehaviorTestCheck.Equal(1, generator.Calls);
        BehaviorTestCheck.Equal(date, generator.Inputs.Single().Date);
        DiaryDayState day = GetDay(ledger, date);
        BehaviorTestCheck.Equal(entry, day.Entry!);
        BehaviorTestCheck.Null(day.GenerationFailure);
    }

    private static void ServerFailureRetriesAfterThirtyMinutes()
    {
        var date = new DateOnly(2026, 8, 3);
        DateTimeOffset firstAttemptUtc = Utc(2026, 8, 3, 18, 30, 0);
        var ledger = LedgerWithInteraction(date);
        var generator = new ScriptedDiaryGenerator(
            new DiaryGenerationResult(
                DiaryGenerationOutcome.RetryableFailure,
                null,
                "http-503",
                HttpStatusCode.ServiceUnavailable),
            DiaryGenerationResult.Success(
                CreateEntry(firstAttemptUtc.AddMinutes(30))));
        using var coordinator = CreateCoordinator(ledger, generator);

        BehaviorTestCheck.Equal(0, Check(coordinator, firstAttemptUtc));
        DiaryGenerationFailure firstFailure = BehaviorTestCheck.NotNull(
            GetDay(ledger, date).GenerationFailure);
        BehaviorTestCheck.Equal(
            DiaryGenerationFailureDisposition.Retryable,
            firstFailure.Disposition);
        BehaviorTestCheck.Equal(1, firstFailure.AttemptCount);
        BehaviorTestCheck.Equal(firstAttemptUtc, firstFailure.LastAttemptAtUtc);
        BehaviorTestCheck.Equal(1, generator.Calls);

        BehaviorTestCheck.Equal(
            0,
            Check(coordinator, firstAttemptUtc.AddMinutes(30).AddTicks(-1)));
        BehaviorTestCheck.Equal(1, generator.Calls);

        BehaviorTestCheck.Equal(
            1,
            Check(coordinator, firstAttemptUtc.AddMinutes(30)));
        BehaviorTestCheck.Equal(2, generator.Calls);
        DiaryDayState generatedDay = GetDay(ledger, date);
        BehaviorTestCheck.NotNull(generatedDay.Entry);
        BehaviorTestCheck.Null(generatedDay.GenerationFailure);
    }

    private static void NetworkFailureRetriesAfterThirtyMinutes()
    {
        var date = new DateOnly(2026, 8, 3);
        DateTimeOffset firstAttemptUtc = Utc(2026, 8, 3, 19, 0, 0);
        var ledger = LedgerWithInteraction(date);
        var generator = new ScriptedDiaryGenerator(
            new DiaryGenerationResult(
                DiaryGenerationOutcome.RetryableFailure,
                null,
                "network-error"),
            DiaryGenerationResult.Success(
                CreateEntry(firstAttemptUtc.AddMinutes(30))));
        using var coordinator = CreateCoordinator(ledger, generator);

        BehaviorTestCheck.Equal(0, Check(coordinator, firstAttemptUtc));
        DiaryGenerationFailure failure = BehaviorTestCheck.NotNull(
            GetDay(ledger, date).GenerationFailure);
        BehaviorTestCheck.Equal(
            DiaryGenerationFailureDisposition.Retryable,
            failure.Disposition);
        BehaviorTestCheck.Equal("network-error", failure.Code);
        BehaviorTestCheck.Equal(1, generator.Calls);

        BehaviorTestCheck.Equal(
            0,
            Check(coordinator, firstAttemptUtc.AddMinutes(29)));
        BehaviorTestCheck.Equal(1, generator.Calls);

        BehaviorTestCheck.Equal(
            1,
            Check(coordinator, firstAttemptUtc.AddMinutes(30)));
        BehaviorTestCheck.Equal(2, generator.Calls);
        BehaviorTestCheck.NotNull(GetDay(ledger, date).Entry);
    }

    private static void HistoricalPristineDayIsNotBackfilled()
    {
        var oldDate = new DateOnly(2026, 8, 3);
        var ledger = LedgerWithInteraction(oldDate);
        var generator = new ScriptedDiaryGenerator();
        using var coordinator = CreateCoordinator(ledger, generator);

        int generated = Check(
            coordinator,
            Utc(2026, 8, 4, 18, 30, 0));

        BehaviorTestCheck.Equal(0, generated);
        BehaviorTestCheck.Equal(0, generator.Calls);
        BehaviorTestCheck.Null(GetDay(ledger, oldDate).Entry);
    }

    private static void ScheduledSlotJitterDoesNotSkipRetry()
    {
        var date = new DateOnly(2026, 8, 3);
        DateTimeOffset delayedOpeningTick =
            Utc(2026, 8, 3, 18, 30, 0).AddMilliseconds(750);
        DateTimeOffset firstSlot =
            DiaryGenerationSchedule.NormalizeImmediateCheckUtc(
                delayedOpeningTick,
                ChinaTimeZone);
        DateTimeOffset secondSlot =
            DiaryGenerationSchedule.GetNextSlotUtc(
                delayedOpeningTick,
                ChinaTimeZone);
        var ledger = LedgerWithInteraction(date);
        var generator = new ScriptedDiaryGenerator(
            new DiaryGenerationResult(
                DiaryGenerationOutcome.RetryableFailure,
                null,
                "http-503",
                HttpStatusCode.ServiceUnavailable),
            DiaryGenerationResult.Success(CreateEntry(secondSlot)));
        using var coordinator = CreateCoordinator(ledger, generator);

        BehaviorTestCheck.Equal(0, Check(coordinator, firstSlot));
        BehaviorTestCheck.Equal(1, Check(coordinator, secondSlot));
        BehaviorTestCheck.Equal(2, generator.Calls);
    }

    private static void HistoricalRetryableFailureRemainsEligible()
    {
        var oldDate = new DateOnly(2026, 8, 3);
        DateTimeOffset firstAttemptUtc = Utc(2026, 8, 3, 18, 30, 0);
        var ledger = LedgerWithInteraction(oldDate);
        var generator = new ScriptedDiaryGenerator(
            new DiaryGenerationResult(
                DiaryGenerationOutcome.RetryableFailure,
                null,
                "http-503",
                HttpStatusCode.ServiceUnavailable),
            DiaryGenerationResult.Success(
                CreateEntry(Utc(2026, 8, 4, 18, 30, 0))));
        using var coordinator = CreateCoordinator(ledger, generator);

        BehaviorTestCheck.Equal(0, Check(coordinator, firstAttemptUtc));
        BehaviorTestCheck.Equal(
            1,
            Check(coordinator, Utc(2026, 8, 4, 18, 30, 0)));
        BehaviorTestCheck.Equal(2, generator.Calls);
        BehaviorTestCheck.NotNull(GetDay(ledger, oldDate).Entry);
    }

    private static void PaymentRequiredDefersAndCanRecover()
    {
        var date = new DateOnly(2026, 8, 3);
        DateTimeOffset firstAttemptUtc = Utc(2026, 8, 3, 18, 30, 0);
        var ledger = LedgerWithInteraction(date);
        var generator = new ScriptedDiaryGenerator(
            new DiaryGenerationResult(
                DiaryGenerationOutcome.InsufficientBalance,
                null,
                "http-402",
                HttpStatusCode.PaymentRequired),
            DiaryGenerationResult.Success(
                CreateEntry(firstAttemptUtc.AddHours(6))));
        using var coordinator = CreateCoordinator(ledger, generator);

        BehaviorTestCheck.Equal(0, Check(coordinator, firstAttemptUtc));
        DiaryGenerationFailure failure = BehaviorTestCheck.NotNull(
            GetDay(ledger, date).GenerationFailure);
        BehaviorTestCheck.Equal(
            DiaryGenerationFailureDisposition.Retryable,
            failure.Disposition);
        BehaviorTestCheck.Equal(1, failure.AttemptCount);
        BehaviorTestCheck.Equal(1, generator.Calls);

        BehaviorTestCheck.Equal(
            0,
            Check(coordinator, firstAttemptUtc.AddHours(6).AddTicks(-1)));
        BehaviorTestCheck.Equal(1, generator.Calls);
        BehaviorTestCheck.Equal(
            1,
            Check(coordinator, firstAttemptUtc.AddHours(6)));
        BehaviorTestCheck.Equal(2, generator.Calls);
        BehaviorTestCheck.NotNull(GetDay(ledger, date).Entry);
    }

    private static void OtherClientFailureIsTerminalAndDoesNotRetry()
    {
        var date = new DateOnly(2026, 8, 3);
        DateTimeOffset firstAttemptUtc = Utc(2026, 8, 3, 18, 30, 0);
        var ledger = LedgerWithInteraction(date);
        var generator = new ScriptedDiaryGenerator(
            new DiaryGenerationResult(
                DiaryGenerationOutcome.TerminalFailure,
                null,
                "http-401",
                HttpStatusCode.Unauthorized));
        using var coordinator = CreateCoordinator(ledger, generator);

        BehaviorTestCheck.Equal(0, Check(coordinator, firstAttemptUtc));
        DiaryGenerationFailure failure = BehaviorTestCheck.NotNull(
            GetDay(ledger, date).GenerationFailure);
        BehaviorTestCheck.Equal(
            DiaryGenerationFailureDisposition.Terminal,
            failure.Disposition);
        BehaviorTestCheck.Equal("http-401", failure.Code);
        BehaviorTestCheck.Equal(1, failure.AttemptCount);
        BehaviorTestCheck.Equal(1, generator.Calls);

        BehaviorTestCheck.Equal(
            0,
            Check(coordinator, firstAttemptUtc.AddHours(1)));
        BehaviorTestCheck.Equal(1, generator.Calls);
        BehaviorTestCheck.Null(GetDay(ledger, date).Entry);
    }

    private static void RetryAfterDefersWithoutMakingDateTerminal()
    {
        var date = new DateOnly(2026, 8, 3);
        DateTimeOffset firstAttemptUtc = Utc(2026, 8, 3, 18, 30, 0);
        var ledger = LedgerWithInteraction(date);
        var generator = new ScriptedDiaryGenerator(
            new DiaryGenerationResult(
                DiaryGenerationOutcome.RetryableFailure,
                null,
                "proxy-http-429",
                HttpStatusCode.TooManyRequests,
                TimeSpan.FromHours(2)),
            DiaryGenerationResult.Success(
                CreateEntry(firstAttemptUtc.AddHours(2))));
        using var coordinator = CreateCoordinator(ledger, generator);

        BehaviorTestCheck.Equal(0, Check(coordinator, firstAttemptUtc));
        DiaryGenerationFailure failure = BehaviorTestCheck.NotNull(
            GetDay(ledger, date).GenerationFailure);
        BehaviorTestCheck.Equal(
            firstAttemptUtc.AddHours(2),
            failure.RetryNotBeforeUtc);
        BehaviorTestCheck.Equal(
            DiaryGenerationFailureDisposition.Retryable,
            failure.Disposition);
        BehaviorTestCheck.Equal(
            0,
            Check(coordinator, firstAttemptUtc.AddMinutes(30)));
        BehaviorTestCheck.Equal(1, generator.Calls);
        BehaviorTestCheck.Equal(
            1,
            Check(coordinator, firstAttemptUtc.AddHours(2)));
        BehaviorTestCheck.Equal(2, generator.Calls);
    }

    private static void FreshCurrentDayPrecedesHistoricalRetryBacklog()
    {
        var oldDate = new DateOnly(2026, 8, 2);
        var currentDate = new DateOnly(2026, 8, 3);
        DateTimeOffset nowUtc = Utc(2026, 8, 3, 18, 30, 0);
        var ledger = LedgerWithInteraction(oldDate);
        ledger.RecordGenerationFailure(
            oldDate,
            DiaryGenerationFailureDisposition.Retryable,
            "network-error",
            nowUtc.AddHours(-1));
        ledger.RecordInteraction(
            currentDate,
            DiaryInteractionKinds.Click,
            nowUtc.AddMinutes(-5));
        var generator = new ScriptedDiaryGenerator(
            DiaryGenerationResult.Success(CreateEntry(nowUtc)),
            DiaryGenerationResult.Success(CreateEntry(nowUtc)));
        using var coordinator = CreateCoordinator(ledger, generator);

        BehaviorTestCheck.Equal(2, Check(coordinator, nowUtc));
        BehaviorTestCheck.Equal(
            currentDate,
            generator.Inputs[0].Date);
        BehaviorTestCheck.Equal(oldDate, generator.Inputs[1].Date);
    }

    private static void RateLimitedBacklogRotatesAtNextSlot()
    {
        var oldestDate = new DateOnly(2026, 8, 1);
        var nextDate = new DateOnly(2026, 8, 2);
        var currentDate = new DateOnly(2026, 8, 3);
        DateTimeOffset firstSlot = Utc(2026, 8, 3, 18, 30, 0);
        var ledger = LedgerWithInteraction(oldestDate);
        ledger.RecordGenerationFailure(
            oldestDate,
            DiaryGenerationFailureDisposition.Retryable,
            "network-error",
            firstSlot.AddHours(-2));
        ledger.RecordInteraction(
            nextDate,
            DiaryInteractionKinds.Click,
            firstSlot.AddHours(-1));
        ledger.RecordGenerationFailure(
            nextDate,
            DiaryGenerationFailureDisposition.Retryable,
            "network-error",
            firstSlot.AddHours(-1));
        ledger.RecordInteraction(
            currentDate,
            DiaryInteractionKinds.Click,
            firstSlot.AddMinutes(-5));
        var generator = new ScriptedDiaryGenerator(
            DiaryGenerationResult.Success(CreateEntry(firstSlot)),
            new DiaryGenerationResult(
                DiaryGenerationOutcome.RetryableFailure,
                null,
                "proxy-http-429",
                HttpStatusCode.TooManyRequests,
                TimeSpan.FromMinutes(30)),
            DiaryGenerationResult.Success(
                CreateEntry(firstSlot.AddMinutes(30))),
            DiaryGenerationResult.Success(
                CreateEntry(firstSlot.AddMinutes(30))));
        using var coordinator = CreateCoordinator(ledger, generator);

        BehaviorTestCheck.Equal(1, Check(coordinator, firstSlot));
        BehaviorTestCheck.Equal(currentDate, generator.Inputs[0].Date);
        BehaviorTestCheck.Equal(oldestDate, generator.Inputs[1].Date);
        BehaviorTestCheck.Equal(
            2,
            Check(coordinator, firstSlot.AddMinutes(30)));
        BehaviorTestCheck.Equal(nextDate, generator.Inputs[2].Date);
        BehaviorTestCheck.Equal(oldestDate, generator.Inputs[3].Date);
    }

    private static void AcknowledgementRunsAfterDurableLedgerCommit()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.DiaryAck.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var date = new DateOnly(2026, 8, 3);
            DateTimeOffset nowUtc = Utc(2026, 8, 3, 18, 30, 0);
            var store = new DiaryStateStore(Path.Combine(root, "diary.json"));
            var ledger = new DiaryLedger(store);
            ledger.RecordInteraction(
                date,
                DiaryInteractionKinds.Click,
                nowUtc.AddMinutes(-1));
            var generator = new ScriptedDiaryGenerator(
                DiaryGenerationResult.Success(CreateEntry(nowUtc)))
            {
                AcknowledgementObserver = (_, _) =>
                {
                    DiaryState persisted = store.Load();
                    BehaviorTestCheck.NotNull(
                        persisted.Days.Single(day => day.Date == date).Entry);
                },
            };
            using var coordinator = CreateCoordinator(ledger, generator);

            BehaviorTestCheck.Equal(1, Check(coordinator, nowUtc));
            BehaviorTestCheck.Equal(1, generator.Acknowledgements);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void BacklogIsBoundedToNinetyDaysAndEightSuccessesPerPass()
    {
        var targetDate = new DateOnly(2026, 8, 3);
        DateTimeOffset nowUtc = Utc(2026, 8, 3, 18, 30, 0);
        var ledger = new DiaryLedger();
        for (var offset = 0; offset <= 90; offset++)
        {
            DateOnly date = targetDate.AddDays(-offset);
            ledger.RecordInteraction(
                date,
                DiaryInteractionKinds.Click,
                nowUtc.AddDays(-offset));
            if (offset > 0)
            {
                ledger.RecordGenerationFailure(
                    date,
                    DiaryGenerationFailureDisposition.Retryable,
                    "network-error",
                    nowUtc.AddHours(-2).AddMinutes(-offset));
            }
        }

        var generator = new ScriptedDiaryGenerator(
            Enumerable.Range(0, DiaryGenerationCoordinator.MaximumGenerationsPerPass)
                .Select(_ => DiaryGenerationResult.Success(CreateEntry(nowUtc)))
                .ToArray());
        using var coordinator = CreateCoordinator(ledger, generator);

        BehaviorTestCheck.Equal(
            DiaryGenerationCoordinator.MaximumGenerationsPerPass,
            Check(coordinator, nowUtc));
        BehaviorTestCheck.Equal(
            DiaryGenerationCoordinator.MaximumGenerationsPerPass,
            generator.Calls);
        BehaviorTestCheck.NotNull(GetDay(ledger, targetDate).Entry);
        BehaviorTestCheck.Null(
            GetDay(ledger, targetDate.AddDays(-90)).Entry);
    }

    private static DiaryGenerationCoordinator CreateCoordinator(
        DiaryLedger ledger,
        IDiaryGenerator generator) =>
        new(ledger, new DiaryDatePolicy(ChinaTimeZone), generator);

    private static DiaryLedger LedgerWithInteraction(DateOnly date)
    {
        var ledger = new DiaryLedger();
        ledger.RecordInteraction(
            date,
            DiaryInteractionKinds.Click,
            Utc(date.Year, date.Month, date.Day, 17, 0, 0));
        return ledger;
    }

    private static DiaryDayState GetDay(DiaryLedger ledger, DateOnly date) =>
        ledger.Current.Days.Single(day => day.Date == date);

    private static GeneratedDiaryEntry CreateEntry(DateTimeOffset generatedAtUtc) =>
        new()
        {
            Title = "窗边的一小段陪伴",
            Mood = "安心",
            Body = "今天我安静地陪着主人，也收到了主人的一次摸摸。",
            Model = "local-template-v1",
            GeneratedAtUtc = generatedAtUtc,
        };

    private static int Check(
        DiaryGenerationCoordinator coordinator,
        DateTimeOffset nowUtc) =>
        coordinator.CheckAsync(nowUtc, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static DateTimeOffset Utc(
        int year,
        int month,
        int day,
        int localHour,
        int minute,
        int second) =>
        new DateTimeOffset(
            year,
            month,
            day,
            localHour,
            minute,
            second,
            TimeSpan.FromHours(8)).ToUniversalTime();

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(DiaryGenerationCoordinatorTests)}.{name}");
    }

    private sealed class ScriptedDiaryGenerator : IDiaryGenerator
    {
        private readonly Queue<DiaryGenerationResult> _results;
        private readonly List<DiaryGenerationInput> _inputs = [];

        public ScriptedDiaryGenerator(params DiaryGenerationResult[] results)
        {
            _results = new Queue<DiaryGenerationResult>(results);
        }

        public int Calls { get; private set; }

        public int Acknowledgements { get; private set; }

        public Action<DiaryGenerationInput, DiaryGenerationResult>?
            AcknowledgementObserver { get; init; }

        public IReadOnlyList<DiaryGenerationInput> Inputs => _inputs;

        public Task<DiaryGenerationResult> GenerateAsync(
            DiaryGenerationInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            _inputs.Add(input);
            if (_results.Count == 0)
            {
                throw new InvalidOperationException(
                    "The diary generator was called unexpectedly.");
            }

            return Task.FromResult(_results.Dequeue());
        }

        public void Acknowledge(
            DiaryGenerationInput input,
            DiaryGenerationResult result)
        {
            AcknowledgementObserver?.Invoke(input, result);
            Acknowledgements++;
        }
    }
}
