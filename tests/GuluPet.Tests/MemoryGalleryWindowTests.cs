using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using GuluPet.Memories;
using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class MemoryGalleryWindowTests
{
    public static void RunAll()
    {
        Run(
            nameof(ProjectsOnlyCompletedAndPendingMemories),
            ProjectsOnlyCompletedAndPendingMemories);
        Run(
            nameof(ShowsBusyAndFailureFeedback),
            ShowsBusyAndFailureFeedback);
        Run(
            nameof(RejectsProgressThatReferencesUnknownMemories),
            RejectsProgressThatReferencesUnknownMemories);
        Run(
            nameof(RefreshesOneWindowAndPreventsDuplicateReplayRequests),
            RefreshesOneWindowAndPreventsDuplicateReplayRequests);
        Run(
            nameof(ShowsFriendlyEmptyStateWithoutLockedCards),
            ShowsFriendlyEmptyStateWithoutLockedCards);
    }

    private static void ProjectsOnlyCompletedAndPendingMemories()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MemoryGalleryState state = MemoryGalleryState.Create(
            MemoryCatalog.Default,
            new MemoryProgressState
            {
                AcceptedInteractionCount = 30,
                Completed =
                [
                    Completed("memory-01", now.AddMinutes(-4)),
                    Completed("memory-02", now.AddMinutes(-3)),
                ],
                Pending = new PendingMemoryPlayback
                {
                    MemoryId = "memory-03",
                    UnlockedAtUtc = now.AddMinutes(-1),
                },
                UpdatedAtUtc = now,
            },
            Path.GetTempPath(),
            playbackBusy: false);

        BehaviorTestCheck.Equal(3, state.Entries.Count);
        BehaviorTestCheck.SequenceEqual(
            new[] { "memory-01", "memory-02", "memory-03" },
            state.Entries.Select(static entry => entry.MemoryId));
        BehaviorTestCheck.SequenceEqual(
            new[] { "已收好", "已收好", "新回忆" },
            state.Entries.Select(static entry => entry.StatusLabel));
        BehaviorTestCheck.SequenceEqual(
            new[] { "重温", "重温", "看看" },
            state.Entries.Select(static entry => entry.ActionLabel));
        BehaviorTestCheck.False(
            state.Entries.Any(static entry => entry.MemoryId == "memory-04"));
        BehaviorTestCheck.Equal(
            "已收好 2 段 · 新回忆 1 段",
            state.SummaryText);
    }

    private static void ShowsBusyAndFailureFeedback()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MemoryProgressState progress = CompletedProgress("memory-01", now);
        MemoryGalleryState busy = MemoryGalleryState.Create(
            MemoryCatalog.Default,
            progress,
            Path.GetTempPath(),
            playbackBusy: true,
            playingMemoryId: "memory-01");

        BehaviorTestCheck.True(busy.IsBusy);
        BehaviorTestCheck.False(busy.Entries[0].IsEnabled);
        BehaviorTestCheck.Equal("播放中", busy.Entries[0].ActionLabel);
        BehaviorTestCheck.Equal(
            "正在播放「第一次追着你」",
            busy.StatusText);

        MemoryGalleryState failed = MemoryGalleryState.Create(
            MemoryCatalog.Default,
            progress,
            Path.GetTempPath(),
            playbackBusy: false,
            failedMemoryId: "memory-01",
            failureMessage: "视频今天有点害羞，晚点再试吧");
        BehaviorTestCheck.Equal("再试一次", failed.Entries[0].ActionLabel);
        BehaviorTestCheck.Equal(
            "视频今天有点害羞，晚点再试吧",
            failed.StatusText);
    }

    private static void RejectsProgressThatReferencesUnknownMemories()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => MemoryGalleryState.Create(
                MemoryCatalog.Default,
                CompletedProgress("memory-99", now),
                Path.GetTempPath(),
                playbackBusy: false));

        BehaviorTestCheck.Throws<InvalidDataException>(
            () => MemoryGalleryState.Create(
                MemoryCatalog.Default,
                CompletedProgress("memory-01", now),
                Path.GetTempPath(),
                playbackBusy: false,
                failedMemoryId: "memory-02"));
    }

    private static void RefreshesOneWindowAndPreventsDuplicateReplayRequests()
    {
        RunSta(
            () =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                MemoryGalleryState oneEntry = MemoryGalleryState.Create(
                    MemoryCatalog.Default,
                    CompletedProgress("memory-01", now),
                    Path.GetTempPath(),
                    playbackBusy: false);
                var window = new MemoryGalleryWindow(oneEntry);
                try
                {
                    var requestCount = 0;
                    string? requestedMemoryId = null;
                    window.MemoryReplayRequested += (_, e) =>
                    {
                        requestCount++;
                        requestedMemoryId = e.MemoryId;
                    };

                    BehaviorTestCheck.True(
                        window.TryRequestReplayForTest("memory-01"));
                    BehaviorTestCheck.False(
                        window.TryRequestReplayForTest("memory-01"));
                    BehaviorTestCheck.Equal(1, requestCount);
                    BehaviorTestCheck.Equal("memory-01", requestedMemoryId);
                    BehaviorTestCheck.True(
                        window.ReplayRequestInFlightForTest);

                    MemoryGalleryState twoEntries =
                        MemoryGalleryState.Create(
                            MemoryCatalog.Default,
                            new MemoryProgressState
                            {
                                AcceptedInteractionCount = 20,
                                Completed =
                                [
                                    Completed(
                                        "memory-01",
                                        now.AddMinutes(-2)),
                                    Completed(
                                        "memory-02",
                                        now.AddMinutes(-1)),
                                ],
                                UpdatedAtUtc = now,
                            },
                            Path.GetTempPath(),
                            playbackBusy: false);
                    window.Refresh(twoEntries);

                    BehaviorTestCheck.Equal(2, window.VisibleMemoryCount);
                    BehaviorTestCheck.SequenceEqual(
                        new[] { "memory-01", "memory-02" },
                        window.VisibleMemoryIdsForTest);
                    BehaviorTestCheck.False(
                        window.ReplayRequestInFlightForTest);
                    BehaviorTestCheck.True(
                        window.TryRequestReplayForTest("memory-02"));
                    BehaviorTestCheck.Equal(2, requestCount);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void ShowsFriendlyEmptyStateWithoutLockedCards()
    {
        RunSta(
            () =>
            {
                var window = new MemoryGalleryWindow();
                try
                {
                    BehaviorTestCheck.Equal(0, window.VisibleMemoryCount);
                    BehaviorTestCheck.Equal(
                        "还没有回忆",
                        window.SummaryTextForTest);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.EmptyState.Visibility);
                    BehaviorTestCheck.Equal(
                        Visibility.Collapsed,
                        window.GalleryScrollViewer.Visibility);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static MemoryProgressState CompletedProgress(
        string memoryId,
        DateTimeOffset now) =>
        new()
        {
            AcceptedInteractionCount = 10,
            Completed = [Completed(memoryId, now.AddMinutes(-1))],
            UpdatedAtUtc = now,
        };

    private static CompletedMemoryPlayback Completed(
        string memoryId,
        DateTimeOffset unlockedAtUtc) =>
        new()
        {
            MemoryId = memoryId,
            UnlockedAtUtc = unlockedAtUtc,
            CompletedAtUtc = unlockedAtUtc.AddSeconds(10),
        };

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    test();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(MemoryGalleryWindowTests)}.{name}");
    }
}
