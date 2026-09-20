using GuluPet.Behavior;

namespace GuluPet.Tests;

internal static class BehaviorEventJournalTests
{
    public static void RunAll()
    {
        Run(nameof(EventsUseMonotonicSequenceAndTime), EventsUseMonotonicSequenceAndTime);
        Run(nameof(BoundedJournalReportsTruncation), BoundedJournalReportsTruncation);
        Run(nameof(ReadAfterReturnsStableOrderedPage), ReadAfterReturnsStableOrderedPage);
    }

    private static void EventsUseMonotonicSequenceAndTime()
    {
        var clock = new ManualDualClock();
        var journal = new BehaviorEventJournal(clock, capacity: 3);
        var first = journal.Append(
            BehaviorEventKind.RequestReceived,
            behaviorId: "first",
            requestId: 1);
        clock.AdjustWallClock(TimeSpan.FromDays(-2));
        clock.AdvanceMonotonicOnly(TimeSpan.FromSeconds(2));
        var second = journal.Append(
            BehaviorEventKind.RequestEnqueued,
            behaviorId: "second",
            requestId: 2);

        BehaviorTestCheck.Equal(1L, first.Sequence);
        BehaviorTestCheck.Equal(2L, second.Sequence);
        BehaviorTestCheck.Equal(TimeSpan.Zero, first.OccurredAt);
        BehaviorTestCheck.Equal(TimeSpan.FromSeconds(2), second.OccurredAt);
    }

    private static void BoundedJournalReportsTruncation()
    {
        var journal = new BehaviorEventJournal(new ManualDualClock(), capacity: 2);
        journal.Append(BehaviorEventKind.RequestReceived, behaviorId: "a");
        journal.Append(BehaviorEventKind.RequestReceived, behaviorId: "b");
        journal.Append(BehaviorEventKind.RequestReceived, behaviorId: "c");

        var page = journal.ReadAfter(0);
        BehaviorTestCheck.Equal(1L, page.TruncatedBeforeSequence!.Value);
        BehaviorTestCheck.SequenceEqual(
            new long[] { 2, 3 },
            page.Events.Select(static entry => entry.Sequence));
    }

    private static void ReadAfterReturnsStableOrderedPage()
    {
        var journal = new BehaviorEventJournal(new ManualDualClock());
        journal.Append(BehaviorEventKind.RequestReceived, behaviorId: "a");
        journal.Append(BehaviorEventKind.RequestReceived, behaviorId: "b");
        journal.Append(BehaviorEventKind.RequestReceived, behaviorId: "c");

        var page = journal.ReadAfter(1);
        BehaviorTestCheck.SequenceEqual(
            new[] { "b", "c" },
            page.Events.Select(static entry => entry.BehaviorId!.Value.Value));
        BehaviorTestCheck.Null(page.TruncatedBeforeSequence);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
}
