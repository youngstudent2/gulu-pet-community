using System.IO;
using GuluPet.Memories;

namespace GuluPet.Tests;

internal static class MemoryProgressTrackerTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    public static void RunAll()
    {
        Run(
            nameof(StartsAtZeroAndUnlocksOnlyOnTheTenthInteraction),
            StartsAtZeroAndUnlocksOnlyOnTheTenthInteraction);
        Run(
            nameof(PendingMemoryBlocksASecondAcceptedInteraction),
            PendingMemoryBlocksASecondAcceptedInteraction);
        Run(
            nameof(NaturalCompletionAdvancesToTheNextMilestone),
            NaturalCompletionAdvancesToTheNextMilestone);
        Run(
            nameof(PendingSurvivesRestartAndCompletedDoesNotReplay),
            PendingSurvivesRestartAndCompletedDoesNotReplay);
        Run(
            nameof(CompletesAllSixteenMemoriesInCatalogOrder),
            CompletesAllSixteenMemoriesInCatalogOrder);
        Run(
            nameof(RejectsInvalidOrOutOfOrderPersistedProgress),
            RejectsInvalidOrOutOfOrderPersistedProgress);
    }

    private static void StartsAtZeroAndUnlocksOnlyOnTheTenthInteraction()
    {
        var tracker = new MemoryProgressTracker();

        BehaviorTestCheck.Equal(
            0L,
            tracker.Current.AcceptedInteractionCount);
        BehaviorTestCheck.Null(tracker.PendingDefinition);
        BehaviorTestCheck.Equal(
            10L,
            tracker.AcceptedInteractionsUntilNextUnlock);

        for (var index = 1; index <= 9; index++)
        {
            MemoryProgressUpdate update =
                tracker.RecordAcceptedInteraction(
                    BaseTime.AddSeconds(index));
            BehaviorTestCheck.Null(update.NewlyUnlockedPending);
            BehaviorTestCheck.Equal(
                index,
                checked((int)update.State.AcceptedInteractionCount));
        }

        MemoryProgressUpdate threshold =
            tracker.RecordAcceptedInteraction(BaseTime.AddSeconds(10));
        BehaviorTestCheck.Equal(
            "memory-01",
            BehaviorTestCheck.NotNull(
                threshold.NewlyUnlockedPending).Id);
        BehaviorTestCheck.Equal(
            "memory-01",
            BehaviorTestCheck.NotNull(
                threshold.State.Pending).MemoryId);
        BehaviorTestCheck.Equal(0L, tracker.AcceptedInteractionsUntilNextUnlock);
    }

    private static void PendingMemoryBlocksASecondAcceptedInteraction()
    {
        var tracker = UnlockFirstMemory();

        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => tracker.RecordAcceptedInteraction(
                BaseTime.AddSeconds(11)));
        BehaviorTestCheck.Equal(
            10L,
            tracker.Current.AcceptedInteractionCount);
        BehaviorTestCheck.Equal(
            "memory-01",
            BehaviorTestCheck.NotNull(tracker.PendingDefinition).Id);
    }

    private static void NaturalCompletionAdvancesToTheNextMilestone()
    {
        var tracker = UnlockFirstMemory();
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => tracker.RecordNaturalPlaybackCompleted(
                "memory-02",
                BaseTime.AddSeconds(11)));

        MemoryProgressState completed =
            tracker.RecordNaturalPlaybackCompleted(
                "memory-01",
                BaseTime.AddSeconds(12));
        BehaviorTestCheck.Null(completed.Pending);
        BehaviorTestCheck.Equal(1, completed.Completed.Count);
        BehaviorTestCheck.Equal(
            "memory-01",
            completed.Completed[0].MemoryId);
        BehaviorTestCheck.Equal(
            10L,
            tracker.AcceptedInteractionsUntilNextUnlock);

        MemoryDefinition? newlyUnlocked = null;
        for (var index = 11; index <= 20; index++)
        {
            newlyUnlocked = tracker.RecordAcceptedInteraction(
                    BaseTime.AddSeconds(index + 2))
                .NewlyUnlockedPending;
        }

        BehaviorTestCheck.Equal(
            "memory-02",
            BehaviorTestCheck.NotNull(newlyUnlocked).Id);
    }

    private static void PendingSurvivesRestartAndCompletedDoesNotReplay()
    {
        MemoryProgressState pendingState = UnlockFirstMemory().Current;
        var restartedWhilePending = new MemoryProgressTracker(
            initialState: pendingState);

        BehaviorTestCheck.Equal(
            "memory-01",
            BehaviorTestCheck.NotNull(
                restartedWhilePending.PendingDefinition).Id);
        BehaviorTestCheck.Equal(
            "memory-01",
            BehaviorTestCheck.NotNull(
                restartedWhilePending.Current.Pending).MemoryId);

        MemoryProgressState completed =
            restartedWhilePending.RecordNaturalPlaybackCompleted(
                "memory-01",
                BaseTime.AddMinutes(1));
        var restartedAfterCompletion = new MemoryProgressTracker(
            initialState: completed);

        BehaviorTestCheck.Null(restartedAfterCompletion.PendingDefinition);
        BehaviorTestCheck.Equal(
            10L,
            restartedAfterCompletion.AcceptedInteractionsUntilNextUnlock);
        MemoryProgressUpdate eleventh =
            restartedAfterCompletion.RecordAcceptedInteraction(
                BaseTime.AddMinutes(1).AddSeconds(1));
        BehaviorTestCheck.Null(eleventh.NewlyUnlockedPending);
        BehaviorTestCheck.Null(eleventh.State.Pending);
        BehaviorTestCheck.Equal(1, eleventh.State.Completed.Count);
    }

    private static void CompletesAllSixteenMemoriesInCatalogOrder()
    {
        var tracker = new MemoryProgressTracker();
        DateTimeOffset clock = BaseTime;
        for (var sequence = 1; sequence <= 16; sequence++)
        {
            MemoryDefinition? unlocked = null;
            for (var interaction = 0; interaction < 10; interaction++)
            {
                clock = clock.AddSeconds(1);
                unlocked = tracker.RecordAcceptedInteraction(clock)
                    .NewlyUnlockedPending;
                if (interaction < 9)
                {
                    BehaviorTestCheck.Null(unlocked);
                }
            }

            string expectedId = $"memory-{sequence:00}";
            BehaviorTestCheck.Equal(
                expectedId,
                BehaviorTestCheck.NotNull(unlocked).Id);
            clock = clock.AddSeconds(1);
            tracker.RecordNaturalPlaybackCompleted(expectedId, clock);
        }

        BehaviorTestCheck.Equal(
            160L,
            tracker.Current.AcceptedInteractionCount);
        BehaviorTestCheck.Equal(16, tracker.Current.Completed.Count);
        BehaviorTestCheck.Null(tracker.PendingDefinition);
        BehaviorTestCheck.Equal(
            0L,
            tracker.AcceptedInteractionsUntilNextUnlock);

        MemoryProgressUpdate afterCatalog =
            tracker.RecordAcceptedInteraction(clock.AddSeconds(1));
        BehaviorTestCheck.Null(afterCatalog.NewlyUnlockedPending);
        BehaviorTestCheck.Equal(
            161L,
            afterCatalog.State.AcceptedInteractionCount);
    }

    private static void RejectsInvalidOrOutOfOrderPersistedProgress()
    {
        var insufficientCount = new MemoryProgressState
        {
            AcceptedInteractionCount = 9,
            Pending = new PendingMemoryPlayback
            {
                MemoryId = "memory-01",
                UnlockedAtUtc = BaseTime,
            },
            UpdatedAtUtc = BaseTime,
        };
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new MemoryProgressTracker(
                initialState: insufficientCount));

        var skippedFirst = new MemoryProgressState
        {
            AcceptedInteractionCount = 20,
            Completed =
            [
                new CompletedMemoryPlayback
                {
                    MemoryId = "memory-02",
                    UnlockedAtUtc = BaseTime,
                    CompletedAtUtc = BaseTime.AddSeconds(1),
                },
            ],
            UpdatedAtUtc = BaseTime.AddSeconds(1),
        };
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new MemoryProgressTracker(initialState: skippedFirst));
    }

    private static MemoryProgressTracker UnlockFirstMemory()
    {
        var tracker = new MemoryProgressTracker();
        for (var index = 1; index <= 10; index++)
        {
            tracker.RecordAcceptedInteraction(BaseTime.AddSeconds(index));
        }

        return tracker;
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(MemoryProgressTrackerTests)}.{name}");
    }
}
