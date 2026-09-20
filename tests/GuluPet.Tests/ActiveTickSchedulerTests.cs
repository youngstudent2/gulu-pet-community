using GuluPet.Behavior;

namespace GuluPet.Tests;

internal static class ActiveTickSchedulerTests
{
    public static void RunAll()
    {
        Run(
            nameof(IntervalsIncludeBothIntegerEndpoints),
            IntervalsIncludeBothIntegerEndpoints);
        Run(
            nameof(AdjacentBucketsHaveDeterministicBoundaries),
            AdjacentBucketsHaveDeterministicBoundaries);
        Run(
            nameof(OverdueTickFiresExactlyOnce),
            OverdueTickFiresExactlyOnce);
        Run(
            nameof(CompletionAndInteractionRescheduleFromNow),
            CompletionAndInteractionRescheduleFromNow);
        Run(
            nameof(StartIsIdempotentAndCancelAllowsRestart),
            StartIsIdempotentAndCancelAllowsRestart);
        Run(
            nameof(InvalidRandomDoesNotPartiallySchedule),
            InvalidRandomDoesNotPartiallySchedule);
        Run(
            nameof(TickUsesOnlyItsNamedRandomStream),
            TickUsesOnlyItsNamedRandomStream);
        Run(
            nameof(ClockRollbackDoesNotConsumeRandomOrAlterSchedule),
            ClockRollbackDoesNotConsumeRandomOrAlterSchedule);
    }

    private static void IntervalsIncludeBothIntegerEndpoints()
    {
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource(0, 1);
        var scheduler = new ActiveTickScheduler(clock, random);

        BehaviorTestCheck.False(scheduler.IsScheduled);
        BehaviorTestCheck.Null(scheduler.NextDueAt);
        BehaviorTestCheck.Null(scheduler.LastInterval);
        BehaviorTestCheck.Equal(0, scheduler.ScheduleCount);
        BehaviorTestCheck.Equal(0, random.Calls);

        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(25),
            scheduler.Start());
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(25),
            scheduler.NextDueAt!.Value);

        clock.Advance(TimeSpan.FromSeconds(5));
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(60),
            scheduler.NotifyInteractionHandled());
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(65),
            scheduler.NextDueAt!.Value);
        BehaviorTestCheck.Equal(2, random.Calls);
        BehaviorTestCheck.Equal(2, scheduler.ScheduleCount);
    }

    private static void AdjacentBucketsHaveDeterministicBoundaries()
    {
        const double boundary = 1d / 36d;
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource(
            boundary - 0.000_000_001,
            boundary + 0.000_000_001);
        var scheduler = new ActiveTickScheduler(clock, random);

        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(25),
            scheduler.Start());
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(26),
            scheduler.NotifyTickCompleted());
    }

    private static void OverdueTickFiresExactlyOnce()
    {
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource(0, 0.5);
        var scheduler = new ActiveTickScheduler(clock, random);

        scheduler.Start();
        clock.Advance(TimeSpan.FromSeconds(24));
        BehaviorTestCheck.False(scheduler.TryConsumeDueTick());
        clock.Advance(TimeSpan.FromSeconds(1));
        BehaviorTestCheck.True(scheduler.TryConsumeDueTick());
        BehaviorTestCheck.False(scheduler.TryConsumeDueTick());
        BehaviorTestCheck.False(scheduler.IsScheduled);

        clock.Advance(TimeSpan.FromHours(1));
        BehaviorTestCheck.False(scheduler.TryConsumeDueTick());
        var rescheduledAt = clock.Elapsed;
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(43),
            scheduler.NotifyTickCompleted());
        BehaviorTestCheck.Equal(
            rescheduledAt + TimeSpan.FromSeconds(43),
            scheduler.NextDueAt!.Value);
        BehaviorTestCheck.Equal(2, random.Calls);
    }

    private static void CompletionAndInteractionRescheduleFromNow()
    {
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource(0.25, 0.75, 1);
        var scheduler = new ActiveTickScheduler(clock, random);

        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(34),
            scheduler.Start());
        clock.Advance(TimeSpan.FromSeconds(10));
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(52),
            scheduler.NotifyInteractionHandled());
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(62),
            scheduler.NextDueAt!.Value);

        clock.Advance(TimeSpan.FromSeconds(10));
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(60),
            scheduler.NotifyTickCompleted());
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(80),
            scheduler.NextDueAt!.Value);
        BehaviorTestCheck.Equal(3, random.Calls);
    }

    private static void StartIsIdempotentAndCancelAllowsRestart()
    {
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource(0, 0.5);
        var scheduler = new ActiveTickScheduler(clock, random);

        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(25),
            scheduler.Start());
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(25),
            scheduler.Start());
        BehaviorTestCheck.Equal(1, random.Calls);
        BehaviorTestCheck.Equal(1, scheduler.ScheduleCount);

        scheduler.Cancel();
        BehaviorTestCheck.False(scheduler.IsScheduled);
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(43),
            scheduler.Start());
        BehaviorTestCheck.Equal(2, random.Calls);
        BehaviorTestCheck.Equal(2, scheduler.ScheduleCount);
    }

    private static void InvalidRandomDoesNotPartiallySchedule()
    {
        var random = new SequenceRandomSource(double.NaN);
        var scheduler = new ActiveTickScheduler(
            new ManualDualClock(),
            random);

        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => scheduler.Start());
        BehaviorTestCheck.Equal(1, random.Calls);
        BehaviorTestCheck.False(scheduler.IsScheduled);
        BehaviorTestCheck.Null(scheduler.NextDueAt);
        BehaviorTestCheck.Null(scheduler.LastInterval);
        BehaviorTestCheck.Equal(0, scheduler.ScheduleCount);
    }

    private static void TickUsesOnlyItsNamedRandomStream()
    {
        var tickRandom = new SequenceRandomSource(0);
        var utilityRandom = new SequenceRandomSource();
        var evictionRandom = new SequenceRandomSource();
        var streams = new BehaviorRandomStreams(
            tickRandom,
            utilityRandom,
            evictionRandom);
        var scheduler = new ActiveTickScheduler(
            streams,
            new ManualDualClock());

        scheduler.Start();

        BehaviorTestCheck.Equal(1, tickRandom.Calls);
        BehaviorTestCheck.Equal(0, utilityRandom.Calls);
        BehaviorTestCheck.Equal(0, evictionRandom.Calls);
    }

    private static void ClockRollbackDoesNotConsumeRandomOrAlterSchedule()
    {
        var clock = new RewindableMonotonicClock();
        var random = new SequenceRandomSource(0, 1);
        var scheduler = new ActiveTickScheduler(clock, random);

        scheduler.Start();
        var originalDueAt = scheduler.NextDueAt;
        var originalInterval = scheduler.LastInterval;
        clock.Set(TimeSpan.FromSeconds(10));
        BehaviorTestCheck.False(scheduler.TryConsumeDueTick());

        clock.Set(TimeSpan.FromSeconds(9));
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => scheduler.NotifyInteractionHandled());
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => scheduler.TryConsumeDueTick());
        BehaviorTestCheck.Equal(1, random.Calls);
        BehaviorTestCheck.Equal(1, scheduler.ScheduleCount);
        BehaviorTestCheck.Equal(originalDueAt, scheduler.NextDueAt);
        BehaviorTestCheck.Equal(originalInterval, scheduler.LastInterval);

        clock.Set(TimeSpan.FromSeconds(10));
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(60),
            scheduler.NotifyInteractionHandled());
        BehaviorTestCheck.Equal(2, random.Calls);
        BehaviorTestCheck.Equal(2, scheduler.ScheduleCount);
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(70),
            scheduler.NextDueAt!.Value);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
}
