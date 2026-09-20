using GuluPet.Diary;

namespace GuluPet.Tests;

internal static class DiaryDatePolicyTests
{
    private static readonly TimeZoneInfo ChinaTimeZone =
        TimeZoneInfo.CreateCustomTimeZone(
            "GuluPet-China-Test",
            TimeSpan.FromHours(8),
            "GuluPet China Test",
            "GuluPet China Test");

    public static void RunAll()
    {
        Run(nameof(ActivityDateChangesAt1830), ActivityDateChangesAt1830);
        Run(
            nameof(GenerationWindowUsesPreviousDayAfterMidnight),
            GenerationWindowUsesPreviousDayAfterMidnight);
        Run(
            nameof(GenerationSlotsAbsorbDispatcherJitter),
            GenerationSlotsAbsorbDispatcherJitter);
        Run(
            nameof(InstallationJitterDelaysFireButPreservesIntendedSlot),
            InstallationJitterDelaysFireButPreservesIntendedSlot);
        Run(nameof(RuntimeSplitsAtDiaryBoundary), RuntimeSplitsAtDiaryBoundary);
        Run(nameof(SuspendedTimeIsNotAccumulated), SuspendedTimeIsNotAccumulated);
    }

    private static void ActivityDateChangesAt1830()
    {
        var policy = new DiaryDatePolicy(ChinaTimeZone);
        BehaviorTestCheck.Equal(
            new DateOnly(2026, 8, 3),
            policy.GetActivityDate(Utc(2026, 8, 3, 18, 29, 59)));
        BehaviorTestCheck.Equal(
            new DateOnly(2026, 8, 4),
            policy.GetActivityDate(Utc(2026, 8, 3, 18, 30, 0)));

        DiaryDayPeriod period = policy.GetPeriod(new DateOnly(2026, 8, 3));
        BehaviorTestCheck.Equal(
            Utc(2026, 8, 2, 18, 30, 0),
            period.StartsAtUtc);
        BehaviorTestCheck.Equal(
            Utc(2026, 8, 3, 18, 30, 0),
            period.EndsAtUtc);
    }

    private static void GenerationWindowUsesPreviousDayAfterMidnight()
    {
        var policy = new DiaryDatePolicy(ChinaTimeZone);
        BehaviorTestCheck.True(policy.TryGetGenerationTarget(
            Utc(2026, 8, 4, 0, 0, 0),
            out DateOnly midnightTarget));
        BehaviorTestCheck.Equal(new DateOnly(2026, 8, 3), midnightTarget);

        BehaviorTestCheck.True(policy.TryGetGenerationTarget(
            Utc(2026, 8, 4, 8, 30, 0),
            out DateOnly morningTarget));
        BehaviorTestCheck.Equal(new DateOnly(2026, 8, 3), morningTarget);

        BehaviorTestCheck.False(policy.TryGetGenerationTarget(
            Utc(2026, 8, 4, 8, 30, 1),
            out _));
        BehaviorTestCheck.False(policy.TryGetGenerationTarget(
            Utc(2026, 8, 4, 18, 29, 59),
            out _));

        BehaviorTestCheck.True(policy.TryGetGenerationTarget(
            Utc(2026, 8, 4, 18, 30, 0),
            out DateOnly eveningTarget));
        BehaviorTestCheck.Equal(new DateOnly(2026, 8, 4), eveningTarget);
    }

    private static void RuntimeSplitsAtDiaryBoundary()
    {
        var policy = new DiaryDatePolicy(ChinaTimeZone);
        var accumulator = new DiaryRuntimeAccumulator(policy);
        DiaryRuntimeObservation observation = accumulator.Observe(
            Utc(2026, 8, 3, 18, 31, 0),
            TimeSpan.FromMinutes(2));

        BehaviorTestCheck.Equal(2, observation.Segments.Count);
        BehaviorTestCheck.Equal(
            new DateOnly(2026, 8, 3),
            observation.Segments[0].Date);
        BehaviorTestCheck.Equal(60L, observation.Segments[0].ElapsedSeconds);
        BehaviorTestCheck.Equal(
            new DateOnly(2026, 8, 4),
            observation.Segments[1].Date);
        BehaviorTestCheck.Equal(60L, observation.Segments[1].ElapsedSeconds);

        accumulator.Accept(observation);
        BehaviorTestCheck.Equal(
            0,
            accumulator.Observe(
                Utc(2026, 8, 3, 18, 31, 0),
                TimeSpan.FromMinutes(2)).Segments.Count);
    }

    private static void GenerationSlotsAbsorbDispatcherJitter()
    {
        var policy = new DiaryDatePolicy(ChinaTimeZone);
        DateTimeOffset exactClosingSlot = Utc(2026, 8, 4, 8, 30, 0);
        DateTimeOffset delayedTick = exactClosingSlot.AddMilliseconds(750);
        DateTimeOffset normalized =
            DiaryGenerationSchedule.NormalizeImmediateCheckUtc(
                delayedTick,
                ChinaTimeZone);

        BehaviorTestCheck.Equal(exactClosingSlot, normalized);
        BehaviorTestCheck.True(policy.TryGetGenerationTarget(
            normalized,
            out DateOnly target));
        BehaviorTestCheck.Equal(new DateOnly(2026, 8, 3), target);
        BehaviorTestCheck.Equal(
            Utc(2026, 8, 4, 9, 0, 0),
            DiaryGenerationSchedule.GetNextSlotUtc(
                delayedTick,
                ChinaTimeZone));

        DateTimeOffset outsideGrace = exactClosingSlot.AddSeconds(6);
        BehaviorTestCheck.Equal(
            outsideGrace,
            DiaryGenerationSchedule.NormalizeImmediateCheckUtc(
                outsideGrace,
                ChinaTimeZone));
        BehaviorTestCheck.False(policy.TryGetGenerationTarget(
            outsideGrace,
            out _));
    }

    private static void InstallationJitterDelaysFireButPreservesIntendedSlot()
    {
        var installationId = Guid.Parse(
            "62d21e8d-9846-467b-b993-d642092d8a09");
        TimeSpan first = DiaryGenerationSchedule.GetStableInstallationJitter(
            installationId);
        TimeSpan second = DiaryGenerationSchedule.GetStableInstallationJitter(
            installationId);
        BehaviorTestCheck.Equal(first, second);
        BehaviorTestCheck.True(first >= TimeSpan.Zero);
        BehaviorTestCheck.True(
            first <= DiaryGenerationSchedule.MaximumInstallationJitter);

        var policy = new DiaryDatePolicy(ChinaTimeZone);
        DateTimeOffset closingSlot = Utc(2026, 8, 4, 8, 30, 0);
        DateTimeOffset delayedFire =
            DiaryGenerationSchedule.ApplyInstallationJitter(
                closingSlot,
                TimeSpan.FromMinutes(12));
        BehaviorTestCheck.Equal(
            closingSlot.AddMinutes(12),
            delayedFire);
        BehaviorTestCheck.True(policy.TryGetGenerationTarget(
            closingSlot,
            out DateOnly intendedTarget));
        BehaviorTestCheck.Equal(
            new DateOnly(2026, 8, 3),
            intendedTarget);
        BehaviorTestCheck.False(policy.TryGetGenerationTarget(
            delayedFire,
            out _));
        BehaviorTestCheck.Equal(
            new DateOnly(2026, 8, 3),
            policy.GetLatestClosedDate(delayedFire));
    }

    private static void SuspendedTimeIsNotAccumulated()
    {
        var accumulator = new DiaryRuntimeAccumulator(
            new DiaryDatePolicy(ChinaTimeZone));
        DiaryRuntimeObservation beforeSuspend = accumulator.Observe(
            Utc(2026, 8, 3, 17, 0, 0),
            TimeSpan.FromMinutes(5));
        accumulator.Accept(beforeSuspend);
        accumulator.Suspend(TimeSpan.FromMinutes(5));

        BehaviorTestCheck.Equal(
            0,
            accumulator.Observe(
                Utc(2026, 8, 3, 18, 0, 0),
                TimeSpan.FromHours(1)).Segments.Count);
        accumulator.Resume(TimeSpan.FromHours(1));
        DiaryRuntimeObservation afterResume = accumulator.Observe(
            Utc(2026, 8, 3, 18, 1, 0),
            TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
        BehaviorTestCheck.Equal(1, afterResume.Segments.Count);
        BehaviorTestCheck.Equal(60L, afterResume.Segments[0].ElapsedSeconds);
    }

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
        Console.WriteLine($"PASS {nameof(DiaryDatePolicyTests)}.{name}");
    }
}
