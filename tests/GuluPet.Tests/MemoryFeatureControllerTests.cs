using System.Runtime.ExceptionServices;
using GuluPet.Memories;
using GuluPet.Platform;
using GuluPet.Presentation;
using GuluPet.Runtime;
using GuluPet.Sensing;
using System.Windows.Threading;

namespace GuluPet.Tests;

internal static class MemoryFeatureControllerTests
{
    public static void RunAll()
    {
        Run(
            nameof(TestInteractionBatchIsRejectedAtomically),
            TestInteractionBatchIsRejectedAtomically);
        Run(
            nameof(SynchronousPresenterFailureClearsActiveOperation),
            SynchronousPresenterFailureClearsActiveOperation);
        Run(
            nameof(MenuStateListsCompletedMemoriesAndDisablesBusyReplay),
            MenuStateListsCompletedMemoriesAndDisablesBusyReplay);
        Run(
            nameof(PetMenuRaisesGalleryRequest),
            PetMenuRaisesGalleryRequest);
        Run(
            nameof(CompletedReplayDoesNotMutateProgress),
            CompletedReplayDoesNotMutateProgress);
        Run(
            nameof(LaterKeepsPendingMemoryReachableFromMenu),
            LaterKeepsPendingMemoryReachableFromMenu);
        Run(
            nameof(CloseOfferDismissesTheActivePrompt),
            CloseOfferDismissesTheActivePrompt);
        Run(
            nameof(VisibilityRefreshDoesNotReenterOffer),
            VisibilityRefreshDoesNotReenterOffer);
        Run(
            nameof(PreflightFailureKeepsPendingUntilWindowCloses),
            PreflightFailureKeepsPendingUntilWindowCloses);
        Run(
            nameof(FailedPlaybackRetryRecreatesPendingSessionAndCompletesOnce),
            FailedPlaybackRetryRecreatesPendingSessionAndCompletesOnce);
        Run(
            nameof(CompletedReplayRetryPreservesProgress),
            CompletedReplayRetryPreservesProgress);
        Run(
            nameof(CloseDuringLoadingCancelsPreflight),
            CloseDuringLoadingCancelsPreflight);
        Run(
            nameof(ClosePlaybackCancelsAndPresenterRemainsReusable),
            ClosePlaybackCancelsAndPresenterRemainsReusable);
        Run(
            nameof(WpfPresenterCloseCurrentCanOpenAgain),
            WpfPresenterCloseCurrentCanOpenAgain);
        Run(
            nameof(MemoryOfferWindowEntranceKeepsImmediateInput),
            MemoryOfferWindowEntranceKeepsImmediateInput);
        Run(
            nameof(MemoryOfferWindowEntranceCommitsNaturalCompletion),
            MemoryOfferWindowEntranceCommitsNaturalCompletion);
        Run(
            nameof(MemoryOfferActiveEntranceDismissesImmediately),
            MemoryOfferActiveEntranceDismissesImmediately);
        Run(
            nameof(MemoryOfferWindowReducedMotionSettlesImmediately),
            MemoryOfferWindowReducedMotionSettlesImmediately);
        Run(
            nameof(WpfMemoryOfferPolicyCloseCancelsEntrance),
            WpfMemoryOfferPolicyCloseCancelsEntrance);
        Run(
            nameof(PreparationFailureKeepsPendingUntilWindowCloses),
            PreparationFailureKeepsPendingUntilWindowCloses);
        Run(
            nameof(CloseDuringPreparationCancelsPreparation),
            CloseDuringPreparationCancelsPreparation);
        Run(
            nameof(PreflightDimensionsReachReadyPlaybackSession),
            PreflightDimensionsReachReadyPlaybackSession);
        Run(
            nameof(PlaybackWindowReportsFailureWithoutException),
            PlaybackWindowReportsFailureWithoutException);
        Run(
            nameof(OpenedMediaPrerollDoesNotRequirePositionAdvance),
            OpenedMediaPrerollDoesNotRequirePositionAdvance);
        Run(
            nameof(PlaybackWindowWaitsForClickAndKeepsEndedReplayable),
            PlaybackWindowWaitsForClickAndKeepsEndedReplayable);
        Run(
            nameof(PlaybackWindowSingleSegmentReplayKeepsPlayerWarm),
            PlaybackWindowSingleSegmentReplayKeepsPlayerWarm);
        Run(
            nameof(PlaybackWindowPreparationCancellationDoesNotReachReady),
            PlaybackWindowPreparationCancellationDoesNotReachReady);
        Run(
            nameof(PlaybackWindowTracksMemory08MixedOrientationSequence),
            PlaybackWindowTracksMemory08MixedOrientationSequence);
        Run(
            nameof(NaturalEndCompletesPendingOnceWhileWindowStaysOpen),
            NaturalEndCompletesPendingOnceWhileWindowStaysOpen);
        Run(
            nameof(CloseBeforeNaturalEndKeepsPending),
            CloseBeforeNaturalEndKeepsPending);
        Run(
            nameof(CommunityPackageOmitsOptionalMemoryMedia),
            CommunityPackageOmitsOptionalMemoryMedia);
    }

    private static void TestInteractionBatchIsRejectedAtomically()
    {
        RunWithController(
            (memory, _) =>
            {
                MemoryTestInteractionBatchResult first =
                    memory.AddAcceptedInteractionsForTest(
                        9,
                        out MemoryFeatureSnapshot afterNine);
                BehaviorTestCheck.Equal(
                    MemoryTestInteractionBatchResult.Added,
                    first);
                BehaviorTestCheck.Equal(
                    9L,
                    afterNine.AcceptedInteractionCount);
                BehaviorTestCheck.Equal(
                    1L,
                    afterNine.AcceptedInteractionsUntilNextUnlock);

                MemoryTestInteractionBatchResult crossing =
                    memory.AddAcceptedInteractionsForTest(
                        2,
                        out MemoryFeatureSnapshot afterRejectedBatch);
                BehaviorTestCheck.Equal(
                    MemoryTestInteractionBatchResult.CrossesNextUnlock,
                    crossing);
                BehaviorTestCheck.Equal(
                    9L,
                    afterRejectedBatch.AcceptedInteractionCount);
                BehaviorTestCheck.Equal(
                    1L,
                    afterRejectedBatch.AcceptedInteractionsUntilNextUnlock);
                BehaviorTestCheck.True(
                    afterRejectedBatch.PendingMemoryId is null);

                MemoryTestInteractionBatchResult threshold =
                    memory.AddAcceptedInteractionsForTest(
                        1,
                        out MemoryFeatureSnapshot pending);
                BehaviorTestCheck.Equal(
                    MemoryTestInteractionBatchResult.Added,
                    threshold);
                BehaviorTestCheck.Equal(
                    10L,
                    pending.AcceptedInteractionCount);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    pending.PendingMemoryId);

                MemoryTestInteractionBatchResult whilePending =
                    memory.AddAcceptedInteractionsForTest(
                        1,
                        out MemoryFeatureSnapshot unchanged);
                BehaviorTestCheck.Equal(
                    MemoryTestInteractionBatchResult.PresentationBusy,
                    whilePending);
                BehaviorTestCheck.Equal(
                    10L,
                    unchanged.AcceptedInteractionCount);
            });
    }

    private static void SynchronousPresenterFailureClearsActiveOperation()
    {
        RunWithController(
            (memory, _) =>
            {
                memory.SetPresentationVisible(visible: true);

                MemoryTestInteractionBatchResult result =
                    memory.AddAcceptedInteractionsForTest(
                        10,
                        out MemoryFeatureSnapshot failed);

                BehaviorTestCheck.Equal(
                    MemoryTestInteractionBatchResult.Added,
                    result);
                BehaviorTestCheck.False(failed.IsPlaying);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    failed.PendingMemoryId);
                BehaviorTestCheck.Equal(
                    "failed",
                    failed.LastPlaybackOutcome);
                BehaviorTestCheck.True(
                    !string.IsNullOrWhiteSpace(
                        failed.LastPlaybackError));
            },
            playbackPresenter: new ThrowingMemoryPlaybackPresenter());
    }

    private static void MenuStateListsCompletedMemoriesAndDisablesBusyReplay()
    {
        MemoryMenuState ready = MemoryMenuState.Create(
            MemoryCatalog.Default,
            ["memory-01", "memory-02"],
            playbackBusy: false);
        BehaviorTestCheck.Equal("咕噜的回忆 · 已收好 2 段", ready.Header);
        BehaviorTestCheck.Equal(2, ready.Entries.Count);
        BehaviorTestCheck.Equal("memory-01", ready.Entries[0].MemoryId);
        BehaviorTestCheck.Equal("第一次追着你", ready.Entries[0].Header);
        BehaviorTestCheck.True(ready.Entries.All(
            static entry => entry.IsEnabled));
        BehaviorTestCheck.True(ready.Entries.All(
            static entry => !entry.IsPending));

        MemoryMenuState busy = MemoryMenuState.Create(
            MemoryCatalog.Default,
            ["memory-01", "memory-02"],
            playbackBusy: true);
        BehaviorTestCheck.True(busy.Entries.All(
            static entry => !entry.IsEnabled));

        MemoryMenuState empty = MemoryMenuState.Create(
            MemoryCatalog.Default,
            [],
            playbackBusy: false);
        BehaviorTestCheck.Equal("咕噜的回忆 · 已收好 0 段", empty.Header);
        BehaviorTestCheck.Equal(0, empty.Entries.Count);

        MemoryMenuState complete = MemoryMenuState.Create(
            MemoryCatalog.Default,
            MemoryCatalog.Default.Definitions
                .Select(static definition => definition.Id)
                .ToArray(),
            playbackBusy: false);
        BehaviorTestCheck.Equal("咕噜的回忆 · 已收好 16 段", complete.Header);
        BehaviorTestCheck.Equal(16, complete.Entries.Count);

        MemoryMenuState pending = MemoryMenuState.Create(
            MemoryCatalog.Default,
            ["memory-01"],
            pendingMemoryId: "memory-02",
            playbackBusy: false,
            failedMemoryId: null);
        BehaviorTestCheck.Equal(
            "咕噜的回忆 · 已收好 1 段，还有 1 段等妈咪",
            pending.Header);
        BehaviorTestCheck.Equal(2, pending.Entries.Count);
        BehaviorTestCheck.Equal(
            "等妈咪来看 · 怀里的午后",
            pending.Entries[1].Header);
        BehaviorTestCheck.True(pending.Entries[1].IsPending);
        BehaviorTestCheck.True(pending.Entries.All(
            static entry => entry.IsEnabled));
    }

    private static void PetMenuRaisesGalleryRequest()
    {
        RunSta(
            () =>
            {
                var petWindow = new PetWindow();
                try
                {
                    BehaviorTestCheck.Equal(
                        "回忆",
                        petWindow.MemoriesMenuItem.Header);

                    var requestCount = 0;
                    petWindow.MemoriesRequested += (_, _) => requestCount++;
                    petWindow.MemoriesMenuItem.RaiseEvent(
                        new System.Windows.RoutedEventArgs(
                            System.Windows.Controls.MenuItem.ClickEvent));

                    BehaviorTestCheck.Equal(1, requestCount);
                }
                finally
                {
                    petWindow.Close();
                }
            });
    }

    private static void CompletedReplayDoesNotMutateProgress()
    {
        var presenter = new TestMemoryPlaybackPresenter();
        RunWithCompletedMemoryController(
            (memory, _) =>
            {
                BehaviorTestCheck.False(
                    memory.TryReplayCompletedMemory("memory-02"));

                BehaviorTestCheck.True(
                    memory.TryReplayCompletedMemory("memory-01"));
                MemoryFeatureSnapshot active = memory.GetSnapshot();
                BehaviorTestCheck.Equal(
                    10L,
                    active.AcceptedInteractionCount);
                BehaviorTestCheck.Null(active.PendingMemoryId);
                BehaviorTestCheck.Equal(
                    1,
                    active.CompletedMemoryIds.Count);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    active.CompletedMemoryIds[0]);
                BehaviorTestCheck.True(active.IsPlaying);
                BehaviorTestCheck.Equal("ready", active.PlaybackPhase);

                TestMemoryPlaybackSession session =
                    BehaviorTestCheck.NotNull(presenter.LastSession);
                session.RaiseNaturalPlaybackCompleted();
                session.Close(MemoryPlaybackOutcome.Ended);
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);

                MemoryFeatureSnapshot completed = memory.GetSnapshot();
                BehaviorTestCheck.Equal(
                    10L,
                    completed.AcceptedInteractionCount);
                BehaviorTestCheck.Null(completed.PendingMemoryId);
                BehaviorTestCheck.Equal(
                    1,
                    completed.CompletedMemoryIds.Count);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    completed.CompletedMemoryIds[0]);
                BehaviorTestCheck.Equal(
                    "ended",
                    completed.LastPlaybackOutcome);

                MemoryMenuState menu = memory.GetMenuState();
                BehaviorTestCheck.Equal(1, menu.Entries.Count);
                BehaviorTestCheck.True(menu.Entries[0].IsEnabled);
            },
            playbackPresenter: presenter);
    }

    private static void LaterKeepsPendingMemoryReachableFromMenu()
    {
        using var prompt = new TestMemoryOfferPrompt(
            MemoryOfferDecision.Later);
        RunWithController(
            (memory, _) =>
            {
                memory.SetPresentationVisible(visible: true);
                MemoryTestInteractionBatchResult result =
                    memory.AddAcceptedInteractionsForTest(
                        10,
                        out MemoryFeatureSnapshot snapshot);
                BehaviorTestCheck.Equal(
                    MemoryTestInteractionBatchResult.Added,
                    result);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    snapshot.PendingMemoryId);
                BehaviorTestCheck.False(snapshot.IsPlaying);
                BehaviorTestCheck.Equal(1, prompt.ShowCount);

                MemoryMenuState menu = memory.GetMenuState();
                BehaviorTestCheck.Equal(1, menu.Entries.Count);
                BehaviorTestCheck.True(menu.Entries[0].IsPending);
                BehaviorTestCheck.True(menu.Entries[0].IsEnabled);
                BehaviorTestCheck.Equal(
                    "等妈咪来看 · 第一次追着你",
                    menu.Entries[0].Header);
            },
            offerPrompt: prompt);
    }

    private static void CloseOfferDismissesTheActivePrompt()
    {
        using var prompt = new CloseAwareMemoryOfferPrompt();
        RunWithController(
            (memory, _) =>
            {
                BehaviorTestCheck.False(memory.IsOfferVisible);
                BehaviorTestCheck.False(memory.CloseOffer());
                prompt.OnShow = () =>
                {
                    BehaviorTestCheck.True(memory.IsOfferVisible);
                    BehaviorTestCheck.True(memory.CloseOffer());
                };

                memory.SetPresentationVisible(visible: true);
                MemoryTestInteractionBatchResult result =
                    memory.AddAcceptedInteractionsForTest(
                        10,
                        out MemoryFeatureSnapshot snapshot);

                BehaviorTestCheck.Equal(
                    MemoryTestInteractionBatchResult.Added,
                    result);
                BehaviorTestCheck.Equal(1, prompt.ShowCount);
                BehaviorTestCheck.Equal(1, prompt.CloseCount);
                BehaviorTestCheck.False(memory.IsOfferVisible);
                BehaviorTestCheck.Equal("memory-01", snapshot.PendingMemoryId);
                BehaviorTestCheck.False(snapshot.IsPlaying);
            },
            offerPrompt: prompt);
    }

    private static void VisibilityRefreshDoesNotReenterOffer()
    {
        using var prompt = new FailOnRepeatedMemoryOfferPrompt();
        RunWithController(
            (memory, _) =>
            {
                memory.StateChanged += (_, _) =>
                    memory.SetPresentationVisible(visible: true);
                memory.SetPresentationVisible(visible: true);

                MemoryTestInteractionBatchResult result =
                    memory.AddAcceptedInteractionsForTest(
                        10,
                        out MemoryFeatureSnapshot snapshot);

                BehaviorTestCheck.Equal(
                    MemoryTestInteractionBatchResult.Added,
                    result);
                BehaviorTestCheck.Equal(1, prompt.ShowCount);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    snapshot.PendingMemoryId);
                BehaviorTestCheck.False(snapshot.IsPlaying);
                BehaviorTestCheck.Equal(
                    "failed",
                    snapshot.LastPlaybackOutcome);
            },
            offerPrompt: prompt,
            playbackPresenter: new ThrowingMemoryPlaybackPresenter());
    }

    private static void PreflightFailureKeepsPendingUntilWindowCloses()
    {
        var presenter = new TestMemoryPlaybackPresenter();
        RunWithController(
            (memory, petController) =>
            {
                memory.SetPresentationVisible(visible: true);
                _ = memory.AddAcceptedInteractionsForTest(
                    10,
                    out MemoryFeatureSnapshot snapshot);
                BehaviorTestCheck.True(snapshot.IsPlaying);
                BehaviorTestCheck.Equal(
                    "preflight-failed",
                    snapshot.LastPlaybackOutcome);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    snapshot.PendingMemoryId);

                TestMemoryPlaybackSession session =
                    BehaviorTestCheck.NotNull(presenter.LastSession);
                BehaviorTestCheck.Equal(
                    MemoryPlaybackPhase.Failed,
                    session.Phase);
                session.Close(MemoryPlaybackOutcome.Failed);
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);
                MemoryFeatureSnapshot closed = memory.GetSnapshot();
                BehaviorTestCheck.Equal(
                    "memory-01",
                    closed.PendingMemoryId);
                BehaviorTestCheck.Equal(
                    "preflight-failed",
                    closed.LastPlaybackOutcome);
            },
            mediaPreflight: new FailingMemoryMediaPreflight(),
            playbackPresenter: presenter);
    }

    private static void FailedPlaybackRetryRecreatesPendingSessionAndCompletesOnce()
    {
        var firstSession = new TestMemoryPlaybackSession(
            new MemoryPlaybackPreparationResult(
                false,
                "播放器没能准备好这段视频，请再试一次。",
                new InvalidOperationException("First preparation failed.")));
        var secondSession = new TestMemoryPlaybackSession();
        var presenter = new TestMemoryPlaybackPresenter(
            firstSession,
            secondSession);
        var preflight = new CountingMemoryMediaPreflight();
        RunWithController(
            (memory, _) =>
            {
                memory.SetPresentationVisible(visible: true);
                memory.AddAcceptedInteractionsForTest(
                    10,
                    out MemoryFeatureSnapshot failed);

                BehaviorTestCheck.True(failed.IsPlaying);
                BehaviorTestCheck.Equal("memory-01", failed.PendingMemoryId);
                BehaviorTestCheck.Equal(0, failed.CompletedMemoryIds.Count);
                BehaviorTestCheck.Equal("preparation-failed", failed.LastPlaybackOutcome);
                BehaviorTestCheck.Equal(1, presenter.OpenCount);
                BehaviorTestCheck.Equal(1, preflight.CallCount);
                BehaviorTestCheck.Equal(
                    MemoryPlaybackPhase.Failed,
                    firstSession.Phase);

                firstSession.RequestRetry();
                PumpDispatcherUntil(
                    () => presenter.OpenCount == 2
                        && memory.GetSnapshot().PlaybackPhase == "ready");

                MemoryFeatureSnapshot retried = memory.GetSnapshot();
                BehaviorTestCheck.True(retried.IsPlaying);
                BehaviorTestCheck.Equal("memory-01", retried.PendingMemoryId);
                BehaviorTestCheck.Equal(0, retried.CompletedMemoryIds.Count);
                BehaviorTestCheck.Equal(10L, retried.AcceptedInteractionCount);
                BehaviorTestCheck.Equal(2, preflight.CallCount);
                BehaviorTestCheck.Equal(
                    secondSession,
                    presenter.LastSession);
                BehaviorTestCheck.Equal(1, secondSession.PrepareCallCount);

                secondSession.RaiseNaturalPlaybackCompleted();
                secondSession.Close(MemoryPlaybackOutcome.Ended);
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);

                MemoryFeatureSnapshot completed = memory.GetSnapshot();
                BehaviorTestCheck.Equal(10L, completed.AcceptedInteractionCount);
                BehaviorTestCheck.Null(completed.PendingMemoryId);
                BehaviorTestCheck.Equal(1, completed.CompletedMemoryIds.Count);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    completed.CompletedMemoryIds[0]);
                BehaviorTestCheck.Equal("ended", completed.LastPlaybackOutcome);
            },
            mediaPreflight: preflight,
            playbackPresenter: presenter);
    }

    private static void CompletedReplayRetryPreservesProgress()
    {
        var firstSession = new TestMemoryPlaybackSession();
        var secondSession = new TestMemoryPlaybackSession();
        var presenter = new TestMemoryPlaybackPresenter(
            firstSession,
            secondSession);
        var preflight = new CountingMemoryMediaPreflight();
        RunWithCompletedMemoryController(
            (memory, _) =>
            {
                BehaviorTestCheck.True(
                    memory.TryReplayCompletedMemory("memory-01"));
                BehaviorTestCheck.Equal(1, presenter.OpenCount);
                BehaviorTestCheck.Equal(1, preflight.CallCount);

                firstSession.MarkLoadingFailed(
                    "播放器没能播放这段视频，请再试一次。");
                firstSession.RequestRetry();
                PumpDispatcherUntil(
                    () => presenter.OpenCount == 2
                        && memory.GetSnapshot().PlaybackPhase == "ready");

                MemoryFeatureSnapshot retried = memory.GetSnapshot();
                BehaviorTestCheck.Equal(10L, retried.AcceptedInteractionCount);
                BehaviorTestCheck.Null(retried.PendingMemoryId);
                BehaviorTestCheck.Equal(1, retried.CompletedMemoryIds.Count);
                BehaviorTestCheck.Equal(2, preflight.CallCount);

                secondSession.RaiseNaturalPlaybackCompleted();
                secondSession.Close(MemoryPlaybackOutcome.Ended);
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);

                MemoryFeatureSnapshot completed = memory.GetSnapshot();
                BehaviorTestCheck.Equal(10L, completed.AcceptedInteractionCount);
                BehaviorTestCheck.Null(completed.PendingMemoryId);
                BehaviorTestCheck.Equal(1, completed.CompletedMemoryIds.Count);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    completed.CompletedMemoryIds[0]);
            },
            playbackPresenter: presenter,
            mediaPreflight: preflight);
    }

    private static void PlaybackWindowWaitsForClickAndKeepsEndedReplayable()
    {
        RunSta(
            () =>
            {
                EnsureDispatcherSynchronizationContext();
                Uri source = GetTestMemoryVideoUri("memory-01.mp4");
                Uri landscapeSource = GetTestMemoryVideoUri("memory-02.mp4");
                var owner = new PetWindow();
                MemoryPlaybackWindow? playbackWindow = null;
                try
                {
                    owner.Show();
                    playbackWindow = new MemoryPlaybackWindow(
                        owner,
                        [source, landscapeSource],
                        "第一次追着你",
                        CancellationToken.None);
                    playbackWindow.SetEndedEntranceAnimationsEnabledForTest(
                        true);
                    BehaviorTestCheck.Equal(
                        "第一次追着你",
                        System.Windows.Automation.AutomationProperties
                            .GetHelpText(playbackWindow));
                    int naturalEndCount = 0;
                    playbackWindow.NaturalPlaybackCompleted += (_, _) =>
                        naturalEndCount++;
                    playbackWindow.Show();

                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Loading,
                        playbackWindow.Phase);
                    BehaviorTestCheck.False(
                        playbackWindow.PrimaryButton.IsEnabled);
                    BehaviorTestCheck.Null(playbackWindow.MemoryVideo.Source);

                    playbackWindow.ConfigureVideoDimensions(
                    [
                        new MemoryVideoDimensions(720, 1_280),
                        new MemoryVideoDimensions(1_280, 720),
                    ]);
                    BehaviorTestCheck.Close(
                        328,
                        playbackWindow.DesiredWidthForTest);
                    BehaviorTestCheck.Close(
                        540,
                        playbackWindow.DesiredHeightForTest);

                    MemoryPlaybackPreparationResult preparation =
                        AwaitWithDispatcher(
                            playbackWindow.PrepareAsync(
                                progress: null,
                                CancellationToken.None),
                            timeoutMilliseconds: 20_000);
                    BehaviorTestCheck.True(preparation.Succeeded);
                    BehaviorTestCheck.True(
                        playbackWindow.IsInitialSegmentPreparedForTest);
                    BehaviorTestCheck.Equal(
                        source,
                        playbackWindow.ActiveVideoSourceForTest);
                    BehaviorTestCheck.Equal(
                        landscapeSource,
                        playbackWindow.StandbyVideoSourceForTest);
                    BehaviorTestCheck.Equal(
                        0,
                        playbackWindow.ActivePreparedIndexForTest);
                    BehaviorTestCheck.Equal(
                        1,
                        playbackWindow.StandbyPreparedIndexForTest);
                    int opensAfterPreparation =
                        playbackWindow.MediaOpenCountForTest;
                    int closesAfterPreparation =
                        playbackWindow.MediaCloseCountForTest;

                    playbackWindow.MarkReady();
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Ready,
                        playbackWindow.Phase);
                    BehaviorTestCheck.True(
                        playbackWindow.Width
                            < MemoryPlaybackWindow.PreferredWidth,
                        "Ready must expose the already-shaped portrait window.");
                    BehaviorTestCheck.True(
                        playbackWindow.PrimaryButton.IsEnabled);
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Visible,
                        playbackWindow.PrimaryButton.Visibility);
                    BehaviorTestCheck.True(
                        playbackWindow.PrimaryButton.IsHitTestVisible);
                    BehaviorTestCheck.Equal(
                        "\uE768",
                        playbackWindow.PrimaryButton.Content);
                    BehaviorTestCheck.Equal(
                        "播放回忆",
                        System.Windows.Automation.AutomationProperties
                            .GetName(playbackWindow.PrimaryButton));
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Visible,
                        playbackWindow.PlaybackActionOverlay.Visibility);
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Collapsed,
                        playbackWindow.ProgressPanel.Visibility);
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Visible,
                        playbackWindow.VideoLayer.Visibility);
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Visible,
                        playbackWindow.LoadingArtwork.Visibility);
                    BehaviorTestCheck.Equal(
                        System.Windows.Media.Stretch.UniformToFill,
                        playbackWindow.LoadingArtworkBackdrop.Stretch);
                    BehaviorTestCheck.Equal(
                        System.Windows.Media.Stretch.Uniform,
                        playbackWindow.LoadingArtworkForeground.Stretch);
                    BehaviorTestCheck.True(
                        playbackWindow.LoadingArtworkBackdrop.Opacity
                            < playbackWindow.LoadingArtworkForeground.Opacity);
                    BehaviorTestCheck.Equal(
                        source,
                        playbackWindow.ActiveVideoSourceForTest);

                    playbackWindow.PrimaryButton.RaiseEvent(
                        new System.Windows.RoutedEventArgs(
                            System.Windows.Controls.Primitives
                                .ButtonBase.ClickEvent));
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Playing,
                        playbackWindow.Phase);
                    BehaviorTestCheck.Equal(
                        source,
                        playbackWindow.ActiveVideoSourceForTest);
                    BehaviorTestCheck.Equal(
                        opensAfterPreparation,
                        playbackWindow.MediaOpenCountForTest);
                    BehaviorTestCheck.Equal(
                        closesAfterPreparation,
                        playbackWindow.MediaCloseCountForTest);

                    AwaitWithDispatcher(
                        playbackWindow.CompleteCurrentSegmentForTestAsync(),
                        timeoutMilliseconds: 20_000);
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Playing,
                        playbackWindow.Phase);
                    BehaviorTestCheck.Equal(
                        1,
                        playbackWindow.CurrentVideoIndexForTest);
                    BehaviorTestCheck.Equal(
                        landscapeSource,
                        playbackWindow.ActiveVideoSourceForTest);
                    BehaviorTestCheck.Equal(
                        1,
                        playbackWindow.MediaSwapCountForTest);
                    BehaviorTestCheck.Close(
                        528,
                        playbackWindow.DesiredWidthForTest);
                    BehaviorTestCheck.Close(
                        319.5,
                        playbackWindow.DesiredHeightForTest);
                    BehaviorTestCheck.Equal(
                        "回忆",
                        playbackWindow.Title);
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Collapsed,
                        playbackWindow.TitleText.Visibility);
                    PumpDispatcherUntil(
                        () => playbackWindow.StandbyPreparedIndexForTest == 0,
                        timeoutMilliseconds: 20_000);
                    int opensBeforeReplay =
                        playbackWindow.MediaOpenCountForTest;
                    int closesBeforeReplay =
                        playbackWindow.MediaCloseCountForTest;
                    AwaitWithDispatcher(
                        playbackWindow.CompleteCurrentSegmentForTestAsync(),
                        timeoutMilliseconds: 20_000);
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Ended,
                        playbackWindow.Phase);
                    BehaviorTestCheck.True(playbackWindow.IsVisible);
                    BehaviorTestCheck.Equal(1, naturalEndCount);
                    BehaviorTestCheck.Equal(
                        "\uE72C",
                        playbackWindow.PrimaryButton.Content);
                    BehaviorTestCheck.True(
                        playbackWindow.PrimaryButton.IsEnabled);
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Visible,
                        playbackWindow.PrimaryButton.Visibility);
                    BehaviorTestCheck.True(
                        playbackWindow.PrimaryButton.IsHitTestVisible);
                    BehaviorTestCheck.Equal(
                        "重播回忆",
                        System.Windows.Automation.AutomationProperties
                            .GetName(playbackWindow.PrimaryButton));
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Visible,
                        playbackWindow.PlaybackActionOverlay.Visibility);
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Visible,
                        playbackWindow.EndedDimmingLayer.Visibility);
                    BehaviorTestCheck.True(
                        playbackWindow
                            .EndedEntranceHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.True(
                        playbackWindow.EndedDimmingLayer.Opacity < 1);
                    BehaviorTestCheck.True(
                        playbackWindow.PrimaryButton.Opacity < 1);
                    BehaviorTestCheck.True(
                        playbackWindow.PrimaryButtonScale.ScaleX < 1);
                    BehaviorTestCheck.True(
                        playbackWindow.PrimaryButtonScale.ScaleY < 1);
                    BehaviorTestCheck.Close(
                        0,
                        (double)playbackWindow.EndedDimmingLayer
                            .GetAnimationBaseValue(
                                System.Windows.UIElement.OpacityProperty));
                    BehaviorTestCheck.Close(
                        0.96,
                        (double)playbackWindow.PrimaryButtonScale
                            .GetAnimationBaseValue(
                                System.Windows.Media.ScaleTransform
                                    .ScaleXProperty));
                    long endedEntranceGeneration =
                        playbackWindow.EndedEntranceGenerationForTest;
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Collapsed,
                        playbackWindow.ProgressPanel.Visibility);

                    playbackWindow.PrimaryButton.RaiseEvent(
                        new System.Windows.RoutedEventArgs(
                            System.Windows.Controls.Primitives
                                .ButtonBase.ClickEvent));
                    PumpDispatcherUntil(
                        () => playbackWindow.Phase
                                == MemoryPlaybackPhase.Playing
                            && playbackWindow.CurrentVideoIndexForTest == 0,
                        timeoutMilliseconds: 20_000);
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Playing,
                        playbackWindow.Phase);
                    BehaviorTestCheck.True(
                        playbackWindow.EndedEntranceGenerationForTest
                            > endedEntranceGeneration);
                    BehaviorTestCheck.False(
                        playbackWindow
                            .EndedEntranceHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        1,
                        playbackWindow.EndedDimmingLayer.Opacity);
                    BehaviorTestCheck.Close(
                        1,
                        playbackWindow.PrimaryButton.Opacity);
                    BehaviorTestCheck.Close(
                        1,
                        playbackWindow.PrimaryButtonScale.ScaleX);
                    BehaviorTestCheck.Close(
                        1,
                        playbackWindow.PrimaryButtonScale.ScaleY);
                    PumpDispatcherFor(TimeSpan.FromMilliseconds(240));
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Playing,
                        playbackWindow.Phase);
                    BehaviorTestCheck.False(
                        playbackWindow
                            .EndedEntranceHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        1,
                        playbackWindow.EndedDimmingLayer.Opacity);
                    BehaviorTestCheck.Close(
                        1,
                        playbackWindow.PrimaryButton.Opacity);
                    BehaviorTestCheck.Equal(
                        0,
                        playbackWindow.CurrentVideoIndexForTest);
                    BehaviorTestCheck.Close(
                        328,
                        playbackWindow.DesiredWidthForTest);
                    BehaviorTestCheck.Close(
                        540,
                        playbackWindow.DesiredHeightForTest);
                    BehaviorTestCheck.Equal(
                        opensBeforeReplay,
                        playbackWindow.MediaOpenCountForTest);
                    BehaviorTestCheck.Equal(
                        closesBeforeReplay,
                        playbackWindow.MediaCloseCountForTest);
                    BehaviorTestCheck.Equal(
                        2,
                        playbackWindow.MediaSwapCountForTest);
                    AwaitWithDispatcher(
                        playbackWindow.CompleteCurrentSegmentForTestAsync(),
                        timeoutMilliseconds: 20_000);
                    AwaitWithDispatcher(
                        playbackWindow.CompleteCurrentSegmentForTestAsync(),
                        timeoutMilliseconds: 20_000);
                    BehaviorTestCheck.Equal(1, naturalEndCount);
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Ended,
                        playbackWindow.Phase);
                    long naturalCompletionGeneration =
                        playbackWindow.EndedEntranceGenerationForTest;
                    PumpDispatcherUntil(
                        () => !playbackWindow
                            .EndedEntranceHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Equal(
                        naturalCompletionGeneration,
                        playbackWindow.EndedEntranceGenerationForTest);
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Ended,
                        playbackWindow.Phase);
                    BehaviorTestCheck.True(
                        playbackWindow.PrimaryButton.IsEnabled);
                    BehaviorTestCheck.True(
                        playbackWindow.PrimaryButton.IsHitTestVisible);
                    AssertPlaybackEndedMotionSettled(playbackWindow);

                    playbackWindow.PrimaryButton.RaiseEvent(
                        new System.Windows.RoutedEventArgs(
                            System.Windows.Controls.Primitives
                                .ButtonBase.ClickEvent));
                    PumpDispatcherUntil(
                        () => playbackWindow.Phase
                                == MemoryPlaybackPhase.Playing
                            && playbackWindow.CurrentVideoIndexForTest == 0,
                        timeoutMilliseconds: 20_000);
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Playing,
                        playbackWindow.Phase);

                    Task<MemoryPlaybackResult> completion =
                        playbackWindow.Completion;
                    playbackWindow.Close();
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackOutcome.Ended,
                        completion.GetAwaiter().GetResult().Outcome);
                }
                finally
                {
                    if (playbackWindow?.IsVisible == true)
                    {
                        playbackWindow.Close();
                    }

                    owner.Close();
                }
            });
    }

    private static void PlaybackWindowSingleSegmentReplayKeepsPlayerWarm()
    {
        RunSta(
            () =>
            {
                EnsureDispatcherSynchronizationContext();
                Uri source = GetTestMemoryVideoUri("memory-01.mp4");
                var owner = new PetWindow();
                MemoryPlaybackWindow? playbackWindow = null;
                try
                {
                    owner.Show();
                    playbackWindow = new MemoryPlaybackWindow(
                        owner,
                        [source],
                        "单段回忆",
                        CancellationToken.None);
                    playbackWindow.Show();
                    MemoryPlaybackPreparationResult preparation =
                        AwaitWithDispatcher(
                            playbackWindow.PrepareAsync(
                                progress: null,
                                CancellationToken.None),
                            timeoutMilliseconds: 20_000);
                    BehaviorTestCheck.True(preparation.Succeeded);
                    playbackWindow.MarkReady();

                    int preparedOpenCount =
                        playbackWindow.MediaOpenCountForTest;
                    int preparedCloseCount =
                        playbackWindow.MediaCloseCountForTest;
                    playbackWindow.PrimaryButton.RaiseEvent(
                        new System.Windows.RoutedEventArgs(
                            System.Windows.Controls.Primitives
                                .ButtonBase.ClickEvent));
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Playing,
                        playbackWindow.Phase);
                    BehaviorTestCheck.Equal(
                        preparedOpenCount,
                        playbackWindow.MediaOpenCountForTest);
                    BehaviorTestCheck.Equal(
                        preparedCloseCount,
                        playbackWindow.MediaCloseCountForTest);

                    playbackWindow.SetEndedEntranceAnimationsEnabledForTest(
                        false);
                    AwaitWithDispatcher(
                        playbackWindow.CompleteCurrentSegmentForTestAsync());
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Ended,
                        playbackWindow.Phase);
                    BehaviorTestCheck.True(
                        playbackWindow.PrimaryButton.IsEnabled);
                    BehaviorTestCheck.True(
                        playbackWindow.PrimaryButton.IsHitTestVisible);
                    BehaviorTestCheck.False(
                        playbackWindow
                            .EndedEntranceHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        1,
                        playbackWindow.EndedDimmingLayer.Opacity);
                    BehaviorTestCheck.Close(
                        1,
                        playbackWindow.PrimaryButton.Opacity);
                    BehaviorTestCheck.Close(
                        1,
                        playbackWindow.PrimaryButtonScale.ScaleX);
                    BehaviorTestCheck.Close(
                        1,
                        playbackWindow.PrimaryButtonScale.ScaleY);
                    playbackWindow.PrimaryButton.RaiseEvent(
                        new System.Windows.RoutedEventArgs(
                            System.Windows.Controls.Primitives
                                .ButtonBase.ClickEvent));
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Playing,
                        playbackWindow.Phase);
                    BehaviorTestCheck.Equal(
                        source,
                        playbackWindow.ActiveVideoSourceForTest);
                    BehaviorTestCheck.Equal(
                        preparedOpenCount,
                        playbackWindow.MediaOpenCountForTest);
                    BehaviorTestCheck.Equal(
                        preparedCloseCount,
                        playbackWindow.MediaCloseCountForTest);
                    BehaviorTestCheck.Equal(
                        0,
                        playbackWindow.MediaSwapCountForTest);
                }
                finally
                {
                    if (playbackWindow?.IsVisible == true)
                    {
                        playbackWindow.Close();
                    }

                    owner.Close();
                }
            });
    }

    private static void PlaybackWindowPreparationCancellationDoesNotReachReady()
    {
        RunSta(
            () =>
            {
                EnsureDispatcherSynchronizationContext();
                Uri source = GetTestMemoryVideoUri("memory-01.mp4");
                var owner = new PetWindow();
                MemoryPlaybackWindow? playbackWindow = null;
                try
                {
                    owner.Show();
                    playbackWindow = new MemoryPlaybackWindow(
                        owner,
                        [source],
                        "取消预热",
                        CancellationToken.None);
                    playbackWindow.Show();
                    using var cancellation = new CancellationTokenSource();
                    Task<MemoryPlaybackPreparationResult> preparation =
                        playbackWindow.PrepareAsync(
                            progress: null,
                            cancellation.Token);
                    BehaviorTestCheck.Equal(
                        source,
                        playbackWindow.ActiveVideoSourceForTest);
                    cancellation.Cancel();
                    PumpDispatcherUntil(
                        () => preparation.IsCompleted,
                        timeoutMilliseconds: 5_000);
                    BehaviorTestCheck.Throws<OperationCanceledException>(
                        () => preparation.GetAwaiter().GetResult());
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Loading,
                        playbackWindow.Phase);
                    BehaviorTestCheck.False(
                        playbackWindow.IsInitialSegmentPreparedForTest);
                    BehaviorTestCheck.Null(
                        playbackWindow.ActiveVideoSourceForTest);
                    BehaviorTestCheck.Null(
                        playbackWindow.StandbyVideoSourceForTest);
                    BehaviorTestCheck.True(
                        playbackWindow.MediaCloseCountForTest >= 1);
                    BehaviorTestCheck.False(
                        playbackWindow.PrimaryButton.IsEnabled);
                }
                finally
                {
                    if (playbackWindow?.IsVisible == true)
                    {
                        playbackWindow.Close();
                    }

                    owner.Close();
                }
            });
    }

    private static void PlaybackWindowTracksMemory08MixedOrientationSequence()
    {
        RunSta(
            () =>
            {
                EnsureDispatcherSynchronizationContext();
                string videoDirectory = GetTestMediaDirectory();
                Uri[] sources = Enumerable.Range(1, 9)
                    .Select(index => new Uri(
                        Path.Combine(
                            videoDirectory,
                            $"memory-08-{index:00}.mp4"),
                        UriKind.Absolute))
                    .ToArray();
                MemoryVideoDimensions portrait = new(1_080, 1_440);
                MemoryVideoDimensions landscape = new(1_440, 1_080);
                MemoryVideoDimensions[] dimensions =
                [
                    portrait,
                    portrait,
                    portrait,
                    portrait,
                    landscape,
                    portrait,
                    portrait,
                    landscape,
                    portrait,
                ];
                MemoryPlaybackWindowSize[] expectedSizes = dimensions
                    .Select(static dimension =>
                        MemoryPlaybackWindowSizing.Calculate(
                            dimension.Width,
                            dimension.Height))
                    .ToArray();
                var owner = new PetWindow();
                MemoryPlaybackWindow? playbackWindow = null;
                try
                {
                    owner.Show();
                    playbackWindow = new MemoryPlaybackWindow(
                        owner,
                        sources,
                        "第八段回忆",
                        CancellationToken.None);
                    playbackWindow.Show();
                    playbackWindow.ConfigureVideoDimensions(dimensions);
                    MemoryPlaybackPreparationResult preparation =
                        AwaitWithDispatcher(
                            playbackWindow.PrepareAsync(
                                progress: null,
                                CancellationToken.None),
                            timeoutMilliseconds: 20_000);
                    BehaviorTestCheck.True(preparation.Succeeded);
                    playbackWindow.MarkReady();
                    playbackWindow.PrimaryButton.RaiseEvent(
                        new System.Windows.RoutedEventArgs(
                            System.Windows.Controls.Primitives
                                .ButtonBase.ClickEvent));

                    for (var index = 0; index < sources.Length; index++)
                    {
                        BehaviorTestCheck.Equal(
                            index,
                            playbackWindow.CurrentVideoIndexForTest);
                        BehaviorTestCheck.Equal(
                            index,
                            playbackWindow.ActivePreparedIndexForTest);
                        BehaviorTestCheck.Equal(
                            sources[index],
                            playbackWindow.ActiveVideoSourceForTest);
                        BehaviorTestCheck.Close(
                            expectedSizes[index].WidthDips,
                            playbackWindow.DesiredWidthForTest);
                        BehaviorTestCheck.Close(
                            expectedSizes[index].HeightDips,
                            playbackWindow.DesiredHeightForTest);
                        AwaitWithDispatcher(
                            playbackWindow
                                .CompleteCurrentSegmentForTestAsync(),
                            timeoutMilliseconds: 20_000);
                    }

                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Ended,
                        playbackWindow.Phase);
                    PumpDispatcherUntil(
                        () => playbackWindow.StandbyPreparedIndexForTest == 0,
                        timeoutMilliseconds: 20_000);
                    int opensBeforeReplay =
                        playbackWindow.MediaOpenCountForTest;
                    int closesBeforeReplay =
                        playbackWindow.MediaCloseCountForTest;
                    playbackWindow.PrimaryButton.RaiseEvent(
                        new System.Windows.RoutedEventArgs(
                            System.Windows.Controls.Primitives
                                .ButtonBase.ClickEvent));
                    PumpDispatcherUntil(
                        () => playbackWindow.Phase
                                == MemoryPlaybackPhase.Playing
                            && playbackWindow.CurrentVideoIndexForTest == 0,
                        timeoutMilliseconds: 20_000);
                    BehaviorTestCheck.Equal(
                        0,
                        playbackWindow.CurrentVideoIndexForTest);
                    BehaviorTestCheck.Close(
                        expectedSizes[0].WidthDips,
                        playbackWindow.DesiredWidthForTest);
                    BehaviorTestCheck.Close(
                        expectedSizes[0].HeightDips,
                        playbackWindow.DesiredHeightForTest);
                    BehaviorTestCheck.Equal(
                        sources[0],
                        playbackWindow.ActiveVideoSourceForTest);
                    PumpDispatcherUntil(
                        () => playbackWindow.StandbyPreparedIndexForTest == 1,
                        timeoutMilliseconds: 20_000);
                    BehaviorTestCheck.Equal(
                        opensBeforeReplay + 1,
                        playbackWindow.MediaOpenCountForTest);
                    BehaviorTestCheck.Equal(
                        closesBeforeReplay + 1,
                        playbackWindow.MediaCloseCountForTest);
                    BehaviorTestCheck.Equal(
                        sources.Length,
                        playbackWindow.MediaSwapCountForTest);
                }
                finally
                {
                    if (playbackWindow?.IsVisible == true)
                    {
                        playbackWindow.Close();
                    }

                    owner.Close();
                }
            });
    }

    private static void CloseDuringLoadingCancelsPreflight()
    {
        var presenter = new TestMemoryPlaybackPresenter();
        var preflight = new BlockingMemoryMediaPreflight();
        RunWithController(
            (memory, _) =>
            {
                memory.SetPresentationVisible(visible: true);
                memory.AddAcceptedInteractionsForTest(
                    10,
                    out MemoryFeatureSnapshot loading);
                BehaviorTestCheck.True(loading.IsPlaying);
                BehaviorTestCheck.Equal("loading", loading.PlaybackPhase);
                BehaviorTestCheck.True(preflight.Started);

                TestMemoryPlaybackSession session =
                    BehaviorTestCheck.NotNull(presenter.LastSession);
                session.Close(MemoryPlaybackOutcome.Cancelled);
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);

                BehaviorTestCheck.True(preflight.CancellationObserved);
                MemoryFeatureSnapshot closed = memory.GetSnapshot();
                BehaviorTestCheck.Equal("memory-01", closed.PendingMemoryId);
                BehaviorTestCheck.Equal(
                    "cancelled",
                    closed.LastPlaybackOutcome);
            },
            mediaPreflight: preflight,
            playbackPresenter: presenter);
    }

    private static void ClosePlaybackCancelsAndPresenterRemainsReusable()
    {
        var firstSession = new TestMemoryPlaybackSession();
        var secondSession = new TestMemoryPlaybackSession();
        var presenter = new TestMemoryPlaybackPresenter(
            firstSession,
            secondSession);
        RunWithController(
            (memory, _) =>
            {
                memory.SetPresentationVisible(visible: true);
                memory.AddAcceptedInteractionsForTest(
                    10,
                    out MemoryFeatureSnapshot ready);
                BehaviorTestCheck.True(ready.IsPlaying);
                BehaviorTestCheck.Equal("ready", ready.PlaybackPhase);

                BehaviorTestCheck.True(memory.ClosePlayback());
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);
                BehaviorTestCheck.Equal(
                    MemoryPlaybackOutcome.Cancelled,
                    firstSession.Completion.GetAwaiter().GetResult().Outcome);
                BehaviorTestCheck.Equal(1, presenter.CloseCurrentCallCount);

                MemoryFeatureSnapshot closed = memory.GetSnapshot();
                BehaviorTestCheck.Equal(10L, closed.AcceptedInteractionCount);
                BehaviorTestCheck.Equal("memory-01", closed.PendingMemoryId);
                BehaviorTestCheck.Equal(0, closed.CompletedMemoryIds.Count);
                BehaviorTestCheck.Equal("cancelled", closed.LastPlaybackOutcome);
                BehaviorTestCheck.False(memory.ClosePlayback());

                BehaviorTestCheck.True(
                    memory.TryReplayCompletedMemory("memory-01"));
                BehaviorTestCheck.Equal(2, presenter.OpenCount);
                BehaviorTestCheck.Equal(secondSession, presenter.LastSession);
                BehaviorTestCheck.Equal(
                    MemoryPlaybackPhase.Ready,
                    secondSession.Phase);

                BehaviorTestCheck.True(memory.ClosePlayback());
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);
                MemoryFeatureSnapshot closedAgain = memory.GetSnapshot();
                BehaviorTestCheck.Equal(10L, closedAgain.AcceptedInteractionCount);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    closedAgain.PendingMemoryId);
                BehaviorTestCheck.Equal(0, closedAgain.CompletedMemoryIds.Count);
            },
            playbackPresenter: presenter);
    }

    private static void WpfPresenterCloseCurrentCanOpenAgain()
    {
        RunSta(
            () =>
            {
                string videoPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "Memories",
                    "Videos",
                    "memory-01.mp4");
                var owner = new PetWindow();
                var presenter = new WpfMemoryPlaybackPresenter(owner);
                try
                {
                    owner.Show();
                    IMemoryPlaybackSession first = presenter.Open(
                        [new Uri(videoPath, UriKind.Absolute)],
                        "第一次追着你",
                        CancellationToken.None);
                    BehaviorTestCheck.True(first.IsVisible);

                    presenter.CloseCurrent();
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackOutcome.Cancelled,
                        first.Completion.GetAwaiter().GetResult().Outcome);

                    IMemoryPlaybackSession second = presenter.Open(
                        [new Uri(videoPath, UriKind.Absolute)],
                        "第一次追着你",
                        CancellationToken.None);
                    BehaviorTestCheck.True(second.IsVisible);
                    BehaviorTestCheck.False(ReferenceEquals(first, second));

                    presenter.CloseCurrent();
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackOutcome.Cancelled,
                        second.Completion.GetAwaiter().GetResult().Outcome);
                }
                finally
                {
                    presenter.Dispose();
                    owner.Close();
                }
            });
    }

    private static void MemoryOfferWindowEntranceKeepsImmediateInput()
    {
        RunSta(
            () =>
            {
                EnsureDispatcherSynchronizationContext();
                var owner = new PetWindow();
                MemoryOfferWindow? offerWindow = null;
                Exception? callbackFailure = null;
                long entranceGeneration = 0;
                try
                {
                    owner.Show();
                    offerWindow = new MemoryOfferWindow(
                        new MemoryOfferRequest(
                            "第一次追着你",
                            GetPackagedMemoryThumbnailPath("memory-01.jpg"),
                            ErrorMessage: null))
                    {
                        Owner = owner,
                    };
                    offerWindow.SetOfferEntranceAnimationsEnabledForTest(true);
                    offerWindow.ContentRendered += (_, _) =>
                    {
                        _ = offerWindow.Dispatcher.BeginInvoke(
                            DispatcherPriority.Render,
                            () =>
                            {
                                try
                                {
                                    BehaviorTestCheck.True(
                                        offerWindow
                                            .OfferEntranceHasAnimatedPropertiesForTest);
                                    BehaviorTestCheck.True(
                                        offerWindow.OfferSurface.Opacity < 1);
                                    BehaviorTestCheck.True(
                                        offerWindow.OfferSurfaceScale.ScaleX < 1);
                                    BehaviorTestCheck.Close(
                                        0,
                                        (double)offerWindow.OfferSurface
                                            .GetAnimationBaseValue(
                                                System.Windows.UIElement
                                                    .OpacityProperty));
                                    BehaviorTestCheck.Close(
                                        0.97,
                                        (double)offerWindow.OfferSurfaceScale
                                            .GetAnimationBaseValue(
                                                System.Windows.Media.ScaleTransform
                                                    .ScaleXProperty));
                                    BehaviorTestCheck.True(
                                        offerWindow.PlayNowButton.IsEnabled);
                                    BehaviorTestCheck.True(
                                        offerWindow.PlayNowButton
                                            .IsHitTestVisible);
                                    BehaviorTestCheck.True(
                                        offerWindow.LaterButton.IsEnabled);
                                    entranceGeneration = offerWindow
                                        .OfferEntranceGenerationForTest;
                                    offerWindow.PlayNowButton.RaiseEvent(
                                        new System.Windows.RoutedEventArgs(
                                            System.Windows.Controls.Primitives
                                                .ButtonBase.ClickEvent));
                                }
                                catch (Exception error)
                                {
                                    callbackFailure = error;
                                    offerWindow.Close();
                                }
                            });
                    };

                    bool? dialogResult = offerWindow.ShowDialog();
                    if (callbackFailure is not null)
                    {
                        ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                    }

                    BehaviorTestCheck.True(dialogResult is true);
                    BehaviorTestCheck.Equal(
                        MemoryOfferDecision.PlayNow,
                        offerWindow.Decision);
                    BehaviorTestCheck.True(
                        offerWindow.OfferEntranceGenerationForTest
                            > entranceGeneration);
                    BehaviorTestCheck.False(
                        offerWindow
                            .OfferEntranceHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        1,
                        offerWindow.OfferSurface.Opacity);
                    BehaviorTestCheck.Close(
                        1,
                        offerWindow.OfferSurfaceScale.ScaleX);
                    BehaviorTestCheck.Close(
                        1,
                        offerWindow.OfferSurfaceScale.ScaleY);
                    PumpDispatcherFor(TimeSpan.FromMilliseconds(280));
                    BehaviorTestCheck.Close(
                        1,
                        offerWindow.OfferSurface.Opacity);
                    BehaviorTestCheck.Close(
                        1,
                        offerWindow.OfferSurfaceScale.ScaleX);
                }
                finally
                {
                    if (offerWindow?.IsVisible == true)
                    {
                        offerWindow.Close();
                    }

                    owner.Close();
                }
            });
    }

    private static void MemoryOfferWindowEntranceCommitsNaturalCompletion()
    {
        RunSta(
            () =>
            {
                EnsureDispatcherSynchronizationContext();
                var owner = new PetWindow();
                MemoryOfferWindow? offerWindow = null;
                Exception? callbackFailure = null;
                long completedGeneration = 0;
                try
                {
                    owner.Show();
                    offerWindow = new MemoryOfferWindow(
                        new MemoryOfferRequest(
                            "第一次追着你",
                            GetPackagedMemoryThumbnailPath("memory-01.jpg"),
                            ErrorMessage: null))
                    {
                        Owner = owner,
                    };
                    offerWindow.SetOfferEntranceAnimationsEnabledForTest(true);
                    offerWindow.ContentRendered += (_, _) =>
                    {
                        _ = offerWindow.Dispatcher.BeginInvoke(
                            DispatcherPriority.Render,
                            () =>
                            {
                                try
                                {
                                    BehaviorTestCheck.True(
                                        offerWindow
                                            .OfferEntranceHasAnimatedPropertiesForTest);
                                    completedGeneration = offerWindow
                                        .OfferEntranceGenerationForTest;
                                    PumpDispatcherUntil(
                                        () => !offerWindow
                                            .OfferEntranceHasAnimatedPropertiesForTest);
                                    BehaviorTestCheck.True(
                                        offerWindow.IsVisible);
                                    BehaviorTestCheck.Equal(
                                        completedGeneration,
                                        offerWindow
                                            .OfferEntranceGenerationForTest);
                                    BehaviorTestCheck.True(
                                        offerWindow.PlayNowButton.IsEnabled);
                                    BehaviorTestCheck.True(
                                        offerWindow.PlayNowButton
                                            .IsHitTestVisible);
                                    BehaviorTestCheck.True(
                                        offerWindow.LaterButton.IsEnabled);
                                    AssertOfferMotionSettled(offerWindow);
                                    offerWindow.Close();
                                }
                                catch (Exception error)
                                {
                                    callbackFailure = error;
                                    offerWindow.Close();
                                }
                            });
                    };

                    _ = offerWindow.ShowDialog();
                    if (callbackFailure is not null)
                    {
                        ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                    }

                    BehaviorTestCheck.True(completedGeneration > 0);
                    BehaviorTestCheck.True(
                        offerWindow.OfferEntranceGenerationForTest
                            > completedGeneration);
                    AssertOfferMotionSettled(offerWindow);
                }
                finally
                {
                    if (offerWindow?.IsVisible == true)
                    {
                        offerWindow.Close();
                    }

                    owner.Close();
                }
            });
    }

    private static void MemoryOfferActiveEntranceDismissesImmediately()
    {
        RunActiveOfferImmediateDismissal(useEscape: false);
        RunActiveOfferImmediateDismissal(useEscape: true);
    }

    private static void RunActiveOfferImmediateDismissal(bool useEscape)
    {
        RunSta(
            () =>
            {
                EnsureDispatcherSynchronizationContext();
                var owner = new PetWindow();
                MemoryOfferWindow? offerWindow = null;
                Exception? callbackFailure = null;
                long entranceGeneration = 0;
                try
                {
                    owner.Show();
                    offerWindow = new MemoryOfferWindow(
                        new MemoryOfferRequest(
                            "第一次追着你",
                            GetPackagedMemoryThumbnailPath("memory-01.jpg"),
                            ErrorMessage: null))
                    {
                        Owner = owner,
                    };
                    offerWindow.SetOfferEntranceAnimationsEnabledForTest(true);
                    offerWindow.ContentRendered += (_, _) =>
                    {
                        _ = offerWindow.Dispatcher.BeginInvoke(
                            DispatcherPriority.Render,
                            () =>
                            {
                                try
                                {
                                    BehaviorTestCheck.True(
                                        offerWindow
                                            .OfferEntranceHasAnimatedPropertiesForTest);
                                    entranceGeneration = offerWindow
                                        .OfferEntranceGenerationForTest;
                                    BehaviorTestCheck.True(
                                        offerWindow.LaterButton.IsEnabled);
                                    if (useEscape)
                                    {
                                        System.Windows.PresentationSource source =
                                            System.Windows.PresentationSource
                                                .FromVisual(offerWindow)
                                            ?? throw new InvalidOperationException(
                                                "The shown memory offer has no presentation source.");
                                        var args =
                                            new System.Windows.Input.KeyEventArgs(
                                                System.Windows.Input.Keyboard
                                                    .PrimaryDevice,
                                                source,
                                                Environment.TickCount,
                                                System.Windows.Input.Key.Escape)
                                            {
                                                RoutedEvent = System.Windows.Input
                                                    .Keyboard.PreviewKeyDownEvent,
                                            };
                                        offerWindow.RaiseEvent(args);
                                        BehaviorTestCheck.True(args.Handled);
                                    }
                                    else
                                    {
                                        offerWindow.LaterButton.RaiseEvent(
                                            new System.Windows.RoutedEventArgs(
                                                System.Windows.Controls.Primitives
                                                    .ButtonBase.ClickEvent));
                                    }

                                    BehaviorTestCheck.False(
                                        offerWindow.IsVisible);
                                    BehaviorTestCheck.True(
                                        offerWindow
                                            .OfferEntranceGenerationForTest
                                            > entranceGeneration);
                                    AssertOfferMotionSettled(offerWindow);
                                }
                                catch (Exception error)
                                {
                                    callbackFailure = error;
                                    offerWindow.Close();
                                }
                            });
                    };

                    bool? dialogResult = offerWindow.ShowDialog();
                    if (callbackFailure is not null)
                    {
                        ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                    }

                    BehaviorTestCheck.True(dialogResult is false);
                    BehaviorTestCheck.Equal(
                        MemoryOfferDecision.Later,
                        offerWindow.Decision);
                    BehaviorTestCheck.True(entranceGeneration > 0);
                }
                finally
                {
                    if (offerWindow?.IsVisible == true)
                    {
                        offerWindow.Close();
                    }

                    owner.Close();
                }
            });
    }

    private static void MemoryOfferWindowReducedMotionSettlesImmediately()
    {
        RunSta(
            () =>
            {
                EnsureDispatcherSynchronizationContext();
                var owner = new PetWindow();
                MemoryOfferWindow? offerWindow = null;
                Exception? callbackFailure = null;
                try
                {
                    owner.Show();
                    offerWindow = new MemoryOfferWindow(
                        new MemoryOfferRequest(
                            "第一次追着你",
                            GetPackagedMemoryThumbnailPath("memory-01.jpg"),
                            ErrorMessage: null))
                    {
                        Owner = owner,
                    };
                    offerWindow.SetOfferEntranceAnimationsEnabledForTest(false);
                    offerWindow.ContentRendered += (_, _) =>
                    {
                        _ = offerWindow.Dispatcher.BeginInvoke(
                            DispatcherPriority.Input,
                            () =>
                            {
                                try
                                {
                                    BehaviorTestCheck.False(
                                        offerWindow
                                            .OfferEntranceHasAnimatedPropertiesForTest);
                                    BehaviorTestCheck.Close(
                                        1,
                                        offerWindow.OfferSurface.Opacity);
                                    BehaviorTestCheck.Close(
                                        1,
                                        offerWindow.OfferSurfaceScale.ScaleX);
                                    BehaviorTestCheck.Close(
                                        1,
                                        offerWindow.OfferSurfaceScale.ScaleY);
                                    BehaviorTestCheck.True(
                                        offerWindow.LaterButton.IsEnabled);
                                    offerWindow.LaterButton.RaiseEvent(
                                        new System.Windows.RoutedEventArgs(
                                            System.Windows.Controls.Primitives
                                                .ButtonBase.ClickEvent));
                                }
                                catch (Exception error)
                                {
                                    callbackFailure = error;
                                    offerWindow.Close();
                                }
                            });
                    };

                    bool? dialogResult = offerWindow.ShowDialog();
                    if (callbackFailure is not null)
                    {
                        ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                    }

                    BehaviorTestCheck.True(dialogResult is false);
                    BehaviorTestCheck.Equal(
                        MemoryOfferDecision.Later,
                        offerWindow.Decision);
                }
                finally
                {
                    if (offerWindow?.IsVisible == true)
                    {
                        offerWindow.Close();
                    }

                    owner.Close();
                }
            });
    }

    private static void WpfMemoryOfferPolicyCloseCancelsEntrance()
    {
        RunSta(
            () =>
            {
                EnsureDispatcherSynchronizationContext();
                var owner = new PetWindow();
                var prompt = new WpfMemoryOfferPrompt(owner);
                MemoryOfferWindow? offeredWindow = null;
                Exception? callbackFailure = null;
                long entranceGeneration = 0;
                long deadline = Environment.TickCount64 + 5_000;
                try
                {
                    owner.Show();
                    Action? closeWhenLoaded = null;
                    closeWhenLoaded = () =>
                    {
                        MemoryOfferWindow? current = prompt.CurrentWindowForTest;
                        if (current is null || !current.IsLoaded)
                        {
                            if (Environment.TickCount64 >= deadline)
                            {
                                callbackFailure = new TimeoutException(
                                    "The memory offer did not load in time.");
                                prompt.CloseCurrent();
                                return;
                            }

                            _ = owner.Dispatcher.BeginInvoke(
                                DispatcherPriority.Render,
                                closeWhenLoaded);
                            return;
                        }

                        try
                        {
                            current.SetOfferEntranceAnimationsEnabledForTest(
                                true);
                            offeredWindow = current;
                            entranceGeneration = current
                                .OfferEntranceGenerationForTest;
                            BehaviorTestCheck.True(
                                current
                                    .OfferEntranceHasAnimatedPropertiesForTest);
                            prompt.CloseCurrent();
                            BehaviorTestCheck.False(current.IsVisible);
                            BehaviorTestCheck.False(
                                current
                                    .OfferEntranceHasAnimatedPropertiesForTest);
                        }
                        catch (Exception error)
                        {
                            callbackFailure = error;
                            prompt.CloseCurrent();
                        }
                    };
                    _ = owner.Dispatcher.BeginInvoke(
                        DispatcherPriority.Render,
                        closeWhenLoaded);

                    MemoryOfferDecision decision = prompt.Show(
                        new MemoryOfferRequest(
                            "第一次追着你",
                            GetPackagedMemoryThumbnailPath("memory-01.jpg"),
                            ErrorMessage: null));
                    if (callbackFailure is not null)
                    {
                        ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                    }

                    BehaviorTestCheck.Equal(
                        MemoryOfferDecision.Later,
                        decision);
                    BehaviorTestCheck.True(offeredWindow is not null);
                    BehaviorTestCheck.True(
                        offeredWindow!.OfferEntranceGenerationForTest
                            > entranceGeneration);
                    PumpDispatcherFor(TimeSpan.FromMilliseconds(280));
                    BehaviorTestCheck.False(
                        offeredWindow
                            .OfferEntranceHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        1,
                        offeredWindow.OfferSurface.Opacity);
                    BehaviorTestCheck.Close(
                        1,
                        offeredWindow.OfferSurfaceScale.ScaleX);
                }
                finally
                {
                    prompt.Dispose();
                    owner.Close();
                }
            });
    }

    private static void PreparationFailureKeepsPendingUntilWindowCloses()
    {
        var preparationError = new InvalidOperationException(
            "The formal player could not preroll.");
        var session = new TestMemoryPlaybackSession(
            new MemoryPlaybackPreparationResult(
                false,
                "播放器预热失败",
                preparationError));
        var presenter = new TestMemoryPlaybackPresenter(session);
        RunWithController(
            (memory, _) =>
            {
                memory.SetPresentationVisible(visible: true);
                memory.AddAcceptedInteractionsForTest(
                    10,
                    out MemoryFeatureSnapshot failed);

                BehaviorTestCheck.True(failed.IsPlaying);
                BehaviorTestCheck.Equal(
                    "preparation-failed",
                    failed.LastPlaybackOutcome);
                BehaviorTestCheck.Equal(
                    "播放器预热失败",
                    failed.LastPlaybackError);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    failed.PendingMemoryId);
                BehaviorTestCheck.Equal(1, session.PrepareCallCount);
                BehaviorTestCheck.Equal(0, session.MarkReadyCallCount);
                BehaviorTestCheck.Equal(
                    MemoryPlaybackPhase.Failed,
                    session.Phase);
                BehaviorTestCheck.Equal(
                    preparationError,
                    session.LastLoadingFailureError);
                BehaviorTestCheck.True(
                    BehaviorTestCheck.NotNull(session.LastProgress).Fraction
                        < 1d);

                session.Close(MemoryPlaybackOutcome.Failed);
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);
                BehaviorTestCheck.Equal(
                    "preparation-failed",
                    memory.GetSnapshot().LastPlaybackOutcome);
            },
            playbackPresenter: presenter);
    }

    private static void CloseDuringPreparationCancelsPreparation()
    {
        var session = new TestMemoryPlaybackSession(
            preparationResult: null,
            blockPreparation: true);
        var presenter = new TestMemoryPlaybackPresenter(session);
        RunWithController(
            (memory, _) =>
            {
                memory.SetPresentationVisible(visible: true);
                memory.AddAcceptedInteractionsForTest(
                    10,
                    out MemoryFeatureSnapshot loading);

                BehaviorTestCheck.True(loading.IsPlaying);
                BehaviorTestCheck.Equal("loading", loading.PlaybackPhase);
                BehaviorTestCheck.True(session.PrepareStarted);
                BehaviorTestCheck.Equal(0, session.MarkReadyCallCount);

                session.Close(MemoryPlaybackOutcome.Cancelled);
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);

                BehaviorTestCheck.True(
                    session.PreparationCancellationObserved);
                BehaviorTestCheck.Equal(0, session.MarkReadyCallCount);
                MemoryFeatureSnapshot closed = memory.GetSnapshot();
                BehaviorTestCheck.Equal(
                    "memory-01",
                    closed.PendingMemoryId);
                BehaviorTestCheck.Equal(
                    "cancelled",
                    closed.LastPlaybackOutcome);
            },
            playbackPresenter: presenter);
    }

    private static void PreflightDimensionsReachReadyPlaybackSession()
    {
        var presenter = new TestMemoryPlaybackPresenter();
        RunWithController(
            (memory, _) =>
            {
                memory.SetPresentationVisible(visible: true);
                memory.AddAcceptedInteractionsForTest(
                    10,
                    out MemoryFeatureSnapshot ready);
                BehaviorTestCheck.Equal("ready", ready.PlaybackPhase);

                TestMemoryPlaybackSession session =
                    BehaviorTestCheck.NotNull(presenter.LastSession);
                IReadOnlyList<MemoryVideoDimensions> dimensions =
                    BehaviorTestCheck.NotNull(session.VideoDimensions);
                BehaviorTestCheck.Equal(1, dimensions.Count);
                BehaviorTestCheck.Equal(
                    new MemoryVideoDimensions(720, 1_280),
                    dimensions[0]);
                BehaviorTestCheck.Equal(1, session.PrepareCallCount);
                BehaviorTestCheck.Equal(1, session.MarkReadyCallCount);
                BehaviorTestCheck.Equal(
                    0.92d,
                    session.ProgressFractionAtPrepareStart);
                BehaviorTestCheck.True(
                    session.LoadingProgressHistory.Any(
                        progress => Math.Abs(
                            progress.Fraction - 0.92d) < 0.0001d));
                BehaviorTestCheck.True(
                    session.LoadingProgressHistory.Any(
                        progress => Math.Abs(
                            progress.Fraction - 0.96d) < 0.0001d));
                BehaviorTestCheck.Equal(
                    1d,
                    BehaviorTestCheck.NotNull(session.LastProgress).Fraction);
                BehaviorTestCheck.Equal(
                    "可以播放",
                    BehaviorTestCheck.NotNull(session.LastProgress).StatusText);
                BehaviorTestCheck.True(
                    session.LoadingProgressHistory.Any(
                        static progress => progress.StatusText
                            == "检查视频 1/1"));
                BehaviorTestCheck.True(
                    session.LoadingProgressHistory.Any(
                        static progress => progress.StatusText
                            == "检查播放器兼容性 1/1"));
                BehaviorTestCheck.True(
                    session.LoadingProgressHistory.Any(
                        static progress => progress.StatusText
                            == "正在准备播放器"));
                BehaviorTestCheck.True(
                    session.LoadingProgressHistory
                        .Zip(
                            session.LoadingProgressHistory.Skip(1),
                            static (previous, current) =>
                                current.Fraction >= previous.Fraction)
                        .All(static monotonic => monotonic));

                session.Close(MemoryPlaybackOutcome.Cancelled);
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);
            },
            mediaPreflight: new DimensionMemoryMediaPreflight(),
            playbackPresenter: presenter);
    }

    private static void PlaybackWindowReportsFailureWithoutException()
    {
        RunSta(
            () =>
            {
                string videoPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "Memories",
                    "Videos",
                    "memory-01.mp4");
                var owner = new PetWindow();
                MemoryPlaybackWindow? playbackWindow = null;
                try
                {
                    owner.Show();
                    playbackWindow = new MemoryPlaybackWindow(
                        owner,
                        [new Uri(videoPath, UriKind.Absolute)],
                        "第一次追着你",
                        CancellationToken.None);
                    playbackWindow.Show();
                    playbackWindow.MarkLoadingFailed("安全错误");

                    BehaviorTestCheck.Equal(
                        MemoryPlaybackPhase.Failed,
                        playbackWindow.Phase);
                    BehaviorTestCheck.Equal(
                        "回忆暂时无法播放",
                        playbackWindow.FailureHeadingText.Text);
                    BehaviorTestCheck.Equal(
                        "安全错误",
                        playbackWindow.FailureText.Text);
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Visible,
                        playbackWindow.LoadingArtwork.Visibility);
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Collapsed,
                        playbackWindow.VideoLayer.Visibility);
                    BehaviorTestCheck.Equal(
                        System.Windows.Visibility.Visible,
                        playbackWindow.PrimaryButton.Visibility);
                    BehaviorTestCheck.True(playbackWindow.PrimaryButton.IsEnabled);
                    BehaviorTestCheck.True(playbackWindow.PrimaryButton.Focusable);
                    BehaviorTestCheck.True(playbackWindow.PrimaryButton.IsTabStop);
                    BehaviorTestCheck.True(playbackWindow.PrimaryButton.IsDefault);
                    BehaviorTestCheck.Equal(
                        "再试一次",
                        playbackWindow.PrimaryButton.Content);
                    BehaviorTestCheck.Equal(
                        "再试一次",
                        System.Windows.Automation.AutomationProperties.GetName(
                            playbackWindow.PrimaryButton));

                    var retryCount = 0;
                    playbackWindow.RetryRequested += (_, _) => retryCount++;
                    Task<MemoryPlaybackResult> completion =
                        playbackWindow.Completion;
                    playbackWindow.PrimaryButton.RaiseEvent(
                        new System.Windows.RoutedEventArgs(
                            System.Windows.Controls.Primitives
                                .ButtonBase.ClickEvent));
                    BehaviorTestCheck.Equal(1, retryCount);
                    BehaviorTestCheck.Equal(
                        MemoryPlaybackOutcome.Failed,
                        completion.GetAwaiter().GetResult().Outcome);
                }
                finally
                {
                    if (playbackWindow?.IsVisible == true)
                    {
                        playbackWindow.Close();
                    }

                    owner.Close();
                }
            });
    }

    private static void OpenedMediaPrerollDoesNotRequirePositionAdvance()
    {
        BehaviorTestCheck.False(
            MemoryPlaybackWindow.IsPlayerPrerollSatisfiedForTest(
                TimeSpan.Zero,
                TimeSpan.Zero));
        BehaviorTestCheck.True(
            MemoryPlaybackWindow.IsPlayerPrerollSatisfiedForTest(
                TimeSpan.FromMilliseconds(80),
                TimeSpan.Zero));
        BehaviorTestCheck.True(
            MemoryPlaybackWindow.IsPlayerPrerollSatisfiedForTest(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1)));
    }

    private static void NaturalEndCompletesPendingOnceWhileWindowStaysOpen()
    {
        var presenter = new TestMemoryPlaybackPresenter();
        RunWithController(
            (memory, petController) =>
            {
                memory.SetPresentationVisible(visible: true);
                memory.AddAcceptedInteractionsForTest(
                    10,
                    out MemoryFeatureSnapshot ready);
                BehaviorTestCheck.True(ready.IsPlaying);
                BehaviorTestCheck.Equal("ready", ready.PlaybackPhase);
                BehaviorTestCheck.Equal("memory-01", ready.PendingMemoryId);

                TestMemoryPlaybackSession session =
                    BehaviorTestCheck.NotNull(presenter.LastSession);
                session.RaiseNaturalPlaybackCompleted();
                MemoryFeatureSnapshot ended = memory.GetSnapshot();
                BehaviorTestCheck.True(ended.IsPlaying);
                BehaviorTestCheck.Equal("ended", ended.PlaybackPhase);
                BehaviorTestCheck.Null(ended.PendingMemoryId);
                BehaviorTestCheck.Equal(1, ended.CompletedMemoryIds.Count);
                BehaviorTestCheck.Equal("ended", ended.LastPlaybackOutcome);

                session.RaiseNaturalPlaybackCompleted();
                MemoryFeatureSnapshot repeated = memory.GetSnapshot();
                BehaviorTestCheck.Equal(1, repeated.CompletedMemoryIds.Count);
                BehaviorTestCheck.Null(repeated.PendingMemoryId);

                session.Close(MemoryPlaybackOutcome.Ended);
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);
            },
            playbackPresenter: presenter);
    }

    private static void CloseBeforeNaturalEndKeepsPending()
    {
        var presenter = new TestMemoryPlaybackPresenter();
        RunWithController(
            (memory, petController) =>
            {
                memory.SetPresentationVisible(visible: true);
                memory.AddAcceptedInteractionsForTest(
                    10,
                    out MemoryFeatureSnapshot ready);
                BehaviorTestCheck.Equal("memory-01", ready.PendingMemoryId);

                TestMemoryPlaybackSession session =
                    BehaviorTestCheck.NotNull(presenter.LastSession);
                session.Close(MemoryPlaybackOutcome.Cancelled);
                PumpDispatcherUntil(
                    () => !memory.GetSnapshot().IsPlaying);

                MemoryFeatureSnapshot closed = memory.GetSnapshot();
                BehaviorTestCheck.Equal("memory-01", closed.PendingMemoryId);
                BehaviorTestCheck.Equal(0, closed.CompletedMemoryIds.Count);
                BehaviorTestCheck.Equal(
                    "cancelled",
                    closed.LastPlaybackOutcome);
            },
            playbackPresenter: presenter);
    }

    private static void CommunityPackageOmitsOptionalMemoryMedia()
    {
        // The community package ships only the sample loading illustration
        // under Assets/Memories/UI; private memory videos and thumbnails are
        // optional user-supplied media and must not be packaged.
        string memoryDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Memories");
        BehaviorTestCheck.False(
            Directory.Exists(Path.Combine(memoryDirectory, "Videos")));
        BehaviorTestCheck.False(
            Directory.Exists(Path.Combine(memoryDirectory, "Thumbnails")));
    }

    private static string CreateMemoryAssetsFixture()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.MemoryAssets.{Guid.NewGuid():N}");
        PopulateMemoryAssetsFixture(root);
        return root;
    }

    private static void PopulateMemoryAssetsFixture(string root)
    {
        string videoDirectory = Path.Combine(root, "Memories", "Videos");
        string thumbnailDirectory = Path.Combine(
            root,
            "Memories",
            "Thumbnails");
        Directory.CreateDirectory(videoDirectory);
        Directory.CreateDirectory(thumbnailDirectory);
        string sampleThumbnail = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "AppIcon.png");

        foreach (MemoryDefinition memory in MemoryCatalog.Default.Definitions)
        {
            foreach (string fileName in memory.VideoFileNames)
            {
                File.WriteAllBytes(
                    Path.Combine(videoDirectory, fileName),
                    [0]);
            }

            File.Copy(
                sampleThumbnail,
                Path.Combine(thumbnailDirectory, memory.ThumbnailFileName));
        }
    }

    private static void RunWithController(
        Action<MemoryFeatureController, PetController> test,
        IMemoryOfferPrompt? offerPrompt = null,
        IMemoryMediaPreflight? mediaPreflight = null,
        IMemoryPlaybackPresenter? playbackPresenter = null)
    {
        RunSta(
            () =>
            {
                string runtimeAssetsRoot = Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets");
                string memoryAssetsRoot = CreateMemoryAssetsFixture();
                try
                {
                    ValidatedRuntimeContent content =
                        RuntimeContentContract.LoadAndValidateAsync(
                                runtimeAssetsRoot)
                            .GetAwaiter()
                            .GetResult();
                    var petWindow = new PetWindow();
                    var petController = new PetController(
                        petWindow,
                        content,
                        RuntimeContextMonitor.CreateDisabled());
                    var memory = new MemoryFeatureController(
                        petWindow,
                        petController,
                        memoryAssetsRoot,
                        isolatedTestInstance: true,
                        offerPrompt: offerPrompt,
                        mediaPreflight: mediaPreflight,
                        playbackPresenter: playbackPresenter);
                    try
                    {
                        test(memory, petController);
                    }
                    finally
                    {
                        memory.Dispose();
                        petController.Dispose();
                        petWindow.Close();
                    }
                }
                finally
                {
                    Directory.Delete(memoryAssetsRoot, recursive: true);
                }
            });
    }

    private static void RunWithCompletedMemoryController(
        Action<MemoryFeatureController, PetController> test,
        IMemoryPlaybackPresenter? playbackPresenter = null,
        IMemoryMediaPreflight? mediaPreflight = null)
    {
        RunSta(
            () =>
            {
                string directory = Path.Combine(
                    Path.GetTempPath(),
                    $"GuluPet.MemoryReplay.{Guid.NewGuid():N}");
                Directory.CreateDirectory(directory);
                try
                {
                    var store = new MemoryProgressStore(
                        Path.Combine(directory, "memories.json"));
                    DateTimeOffset unlockedAt =
                        DateTimeOffset.UtcNow.AddMinutes(-1);
                    DateTimeOffset completedAt =
                        DateTimeOffset.UtcNow;
                    store.Save(
                        new MemoryProgressState
                        {
                            AcceptedInteractionCount = 10,
                            Completed =
                            [
                                new CompletedMemoryPlayback
                                {
                                    MemoryId = "memory-01",
                                    UnlockedAtUtc = unlockedAt,
                                    CompletedAtUtc = completedAt,
                                },
                            ],
                            UpdatedAtUtc = completedAt,
                        });

                    string runtimeAssetsRoot = Path.Combine(
                        AppContext.BaseDirectory,
                        "Assets");
                    PopulateMemoryAssetsFixture(directory);
                    ValidatedRuntimeContent content =
                        RuntimeContentContract.LoadAndValidateAsync(
                                runtimeAssetsRoot)
                            .GetAwaiter()
                            .GetResult();
                    var petWindow = new PetWindow();
                    var petController = new PetController(
                        petWindow,
                        content,
                        RuntimeContextMonitor.CreateDisabled());
                    var memory = new MemoryFeatureController(
                        petWindow,
                        petController,
                        directory,
                        isolatedTestInstance: false,
                        store: store,
                        offerPrompt: new AutomaticMemoryOfferPrompt(),
                        mediaPreflight: mediaPreflight
                            ?? new PassThroughMemoryMediaPreflight(),
                        playbackPresenter: playbackPresenter);
                    try
                    {
                        test(memory, petController);
                    }
                    finally
                    {
                        memory.Dispose();
                        petController.Dispose();
                        petWindow.Close();
                    }
                }
                finally
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
            });
    }

    private static string GetTestMediaDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "TestMedia");

    private static Uri GetTestMemoryVideoUri(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return new Uri(
            Path.Combine(GetTestMediaDirectory(), fileName),
            UriKind.Absolute);
    }

    private static string GetPackagedMemoryThumbnailPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // The community package ships no private memory thumbnails, so the
        // offer-window tests use the packaged sample app icon as preview art.
        // WPF decodes by content, so the file name is informational only.
        _ = fileName;
        return Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "AppIcon.png");
    }

    private static void AssertPlaybackEndedMotionSettled(
        MemoryPlaybackWindow playbackWindow)
    {
        BehaviorTestCheck.False(
            playbackWindow.EndedEntranceHasAnimatedPropertiesForTest);
        BehaviorTestCheck.Close(
            1,
            playbackWindow.EndedDimmingLayer.Opacity);
        BehaviorTestCheck.Close(
            1,
            playbackWindow.PrimaryButton.Opacity);
        BehaviorTestCheck.Close(
            1,
            playbackWindow.PrimaryButtonScale.ScaleX);
        BehaviorTestCheck.Close(
            1,
            playbackWindow.PrimaryButtonScale.ScaleY);
        BehaviorTestCheck.Close(
            1,
            (double)playbackWindow.EndedDimmingLayer
                .GetAnimationBaseValue(
                    System.Windows.UIElement.OpacityProperty));
        BehaviorTestCheck.Close(
            1,
            (double)playbackWindow.PrimaryButton
                .GetAnimationBaseValue(
                    System.Windows.UIElement.OpacityProperty));
        BehaviorTestCheck.Close(
            1,
            (double)playbackWindow.PrimaryButtonScale
                .GetAnimationBaseValue(
                    System.Windows.Media.ScaleTransform.ScaleXProperty));
        BehaviorTestCheck.Close(
            1,
            (double)playbackWindow.PrimaryButtonScale
                .GetAnimationBaseValue(
                    System.Windows.Media.ScaleTransform.ScaleYProperty));
    }

    private static void AssertOfferMotionSettled(
        MemoryOfferWindow offerWindow)
    {
        BehaviorTestCheck.False(
            offerWindow.OfferEntranceHasAnimatedPropertiesForTest);
        BehaviorTestCheck.Close(1, offerWindow.OfferSurface.Opacity);
        BehaviorTestCheck.Close(1, offerWindow.OfferSurfaceScale.ScaleX);
        BehaviorTestCheck.Close(1, offerWindow.OfferSurfaceScale.ScaleY);
        BehaviorTestCheck.Close(
            1,
            (double)offerWindow.OfferSurface.GetAnimationBaseValue(
                System.Windows.UIElement.OpacityProperty));
        BehaviorTestCheck.Close(
            1,
            (double)offerWindow.OfferSurfaceScale.GetAnimationBaseValue(
                System.Windows.Media.ScaleTransform.ScaleXProperty));
        BehaviorTestCheck.Close(
            1,
            (double)offerWindow.OfferSurfaceScale.GetAnimationBaseValue(
                System.Windows.Media.ScaleTransform.ScaleYProperty));
    }

    private static void EnsureDispatcherSynchronizationContext()
    {
        if (SynchronizationContext.Current
            is DispatcherSynchronizationContext)
        {
            return;
        }

        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(
                Dispatcher.CurrentDispatcher));
    }

    private static T AwaitWithDispatcher<T>(
        Task<T> task,
        int timeoutMilliseconds = 5_000)
    {
        ArgumentNullException.ThrowIfNull(task);
        PumpDispatcherUntil(
            () => task.IsCompleted,
            timeoutMilliseconds);
        return task.GetAwaiter().GetResult();
    }

    private static void AwaitWithDispatcher(
        Task task,
        int timeoutMilliseconds = 5_000)
    {
        ArgumentNullException.ThrowIfNull(task);
        PumpDispatcherUntil(
            () => task.IsCompleted,
            timeoutMilliseconds);
        task.GetAwaiter().GetResult();
    }

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
        if (!thread.Join(TimeSpan.FromSeconds(60)))
        {
            throw new TimeoutException(
                "The STA memory feature test timed out.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void PumpDispatcherUntil(
        Func<bool> condition,
        int timeoutMilliseconds = 5_000)
    {
        ArgumentNullException.ThrowIfNull(condition);
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException(
                    "The memory controller did not settle in time.");
            }

            var frame = new DispatcherFrame();
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
        }
    }

    private static void PumpDispatcherFor(TimeSpan duration)
    {
        long deadline = Environment.TickCount64
            + Math.Max(0, (long)duration.TotalMilliseconds);
        do
        {
            var frame = new DispatcherFrame();
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
        }
        while (Environment.TickCount64 < deadline);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(MemoryFeatureControllerTests)}.{name}");
    }

    private sealed class TestMemoryOfferPrompt(
        MemoryOfferDecision decision) : IMemoryOfferPrompt
    {
        public int ShowCount { get; private set; }

        public MemoryOfferDecision Show(MemoryOfferRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ShowCount++;
            return decision;
        }

        public void CloseCurrent()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FailOnRepeatedMemoryOfferPrompt
        : IMemoryOfferPrompt
    {
        public int ShowCount { get; private set; }

        public MemoryOfferDecision Show(MemoryOfferRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ShowCount++;
            if (ShowCount > 1)
            {
                throw new InvalidOperationException(
                    "The same memory offer was re-entered.");
            }

            return MemoryOfferDecision.PlayNow;
        }

        public void CloseCurrent()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class CloseAwareMemoryOfferPrompt
        : IMemoryOfferPrompt
    {
        public Action? OnShow { get; set; }

        public int ShowCount { get; private set; }

        public int CloseCount { get; private set; }

        public MemoryOfferDecision Show(MemoryOfferRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ShowCount++;
            OnShow?.Invoke();
            return MemoryOfferDecision.Later;
        }

        public void CloseCurrent() => CloseCount++;

        public void Dispose()
        {
        }
    }

    private sealed class FailingMemoryMediaPreflight
        : IMemoryMediaPreflight
    {
        public Task<MemoryMediaPreflightResult> ValidateAsync(
            IReadOnlyList<Uri> sources,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new MemoryMediaPreflightResult(
                    false,
                    "decoder-failed",
                    "安全错误"));
    }

    private sealed class DimensionMemoryMediaPreflight
        : IMemoryMediaPreflight
    {
        public Task<MemoryMediaPreflightResult> ValidateAsync(
            IReadOnlyList<Uri> sources,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                MemoryMediaPreflightResult.SuccessWithDimensions(
                [
                    new MemoryVideoDimensions(720, 1_280),
                ]));
        }

        public Task<MemoryMediaPreflightResult> ValidateAsync(
            IReadOnlyList<Uri> sources,
            IProgress<MemoryMediaPreflightProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(
                new MemoryMediaPreflightProgress(
                    0.25d,
                    MemoryMediaPreflightStage.PackageIntegrity,
                    1,
                    1));
            progress?.Report(
                new MemoryMediaPreflightProgress(
                    0.75d,
                    MemoryMediaPreflightStage.Decoder,
                    1,
                    1));
            progress?.Report(
                new MemoryMediaPreflightProgress(
                    1d,
                    MemoryMediaPreflightStage.Completed,
                    1,
                    1));
            return ValidateAsync(sources, cancellationToken);
        }
    }

    private sealed class CountingMemoryMediaPreflight
        : IMemoryMediaPreflight
    {
        public int CallCount { get; private set; }

        public Task<MemoryMediaPreflightResult> ValidateAsync(
            IReadOnlyList<Uri> sources,
            CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(MemoryMediaPreflightResult.Success);
        }
    }

    private sealed class BlockingMemoryMediaPreflight
        : IMemoryMediaPreflight
    {
        public bool Started { get; private set; }

        public bool CancellationObserved { get; private set; }

        public Task<MemoryMediaPreflightResult> ValidateAsync(
            IReadOnlyList<Uri> sources,
            CancellationToken cancellationToken) =>
            ValidateAsync(sources, progress: null, cancellationToken);

        public Task<MemoryMediaPreflightResult> ValidateAsync(
            IReadOnlyList<Uri> sources,
            IProgress<MemoryMediaPreflightProgress>? progress,
            CancellationToken cancellationToken)
        {
            Started = true;
            var completion =
                new TaskCompletionSource<MemoryMediaPreflightResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            _ = cancellationToken.Register(
                () =>
                {
                    CancellationObserved = true;
                    completion.TrySetCanceled(cancellationToken);
                });
            return completion.Task;
        }
    }

    private sealed class ThrowingMemoryPlaybackPresenter
        : IMemoryPlaybackPresenter
    {
        public IMemoryPlaybackSession Open(
            IReadOnlyList<Uri> videoSources,
            string title,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Synchronous playback presenter failure.");

        public void CloseCurrent()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestMemoryPlaybackPresenter
        : IMemoryPlaybackPresenter
    {
        private readonly Queue<TestMemoryPlaybackSession> _scheduledSessions;
        private readonly List<TestMemoryPlaybackSession> _openedSessions = [];

        public TestMemoryPlaybackPresenter(
            params TestMemoryPlaybackSession[] sessions)
        {
            _scheduledSessions = new Queue<TestMemoryPlaybackSession>(
                sessions ?? []);
        }

        public TestMemoryPlaybackSession? LastSession { get; private set; }

        public int OpenCount => _openedSessions.Count;

        public int CloseCurrentCallCount { get; private set; }

        public IMemoryPlaybackSession Open(
            IReadOnlyList<Uri> videoSources,
            string title,
            CancellationToken cancellationToken)
        {
            LastSession = _scheduledSessions.Count > 0
                ? _scheduledSessions.Dequeue()
                : new TestMemoryPlaybackSession();
            _openedSessions.Add(LastSession);
            return LastSession;
        }

        public void CloseCurrent()
        {
            CloseCurrentCallCount++;
            LastSession?.Close(MemoryPlaybackOutcome.Cancelled);
        }

        public void Dispose()
        {
            foreach (TestMemoryPlaybackSession session in _openedSessions)
            {
                session.Dispose();
            }
        }
    }

    private sealed class TestMemoryPlaybackSession
        : IMemoryPlaybackSession
    {
        private readonly TaskCompletionSource<MemoryPlaybackResult>
            _completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MemoryPlaybackPreparationResult
            _preparationResult;
        private readonly bool _blockPreparation;
        private bool _naturalPlaybackCompleted;

        public TestMemoryPlaybackSession(
            MemoryPlaybackPreparationResult? preparationResult = null,
            bool blockPreparation = false)
        {
            _preparationResult = preparationResult
                ?? MemoryPlaybackPreparationResult.Success;
            _blockPreparation = blockPreparation;
        }

        public event EventHandler? PhaseChanged;

        public event EventHandler? NaturalPlaybackCompleted;

        public event EventHandler? RetryRequested;

        public MemoryPlaybackPhase Phase { get; private set; } =
            MemoryPlaybackPhase.Loading;

        public bool IsVisible { get; private set; } = true;

        public Task<MemoryPlaybackResult> Completion => _completion.Task;

        public MemoryLoadingProgress? LastProgress { get; private set; }

        public List<MemoryLoadingProgress> LoadingProgressHistory
        {
            get;
        } = [];

        public int PrepareCallCount { get; private set; }

        public bool PrepareStarted { get; private set; }

        public bool PreparationCancellationObserved { get; private set; }

        public double? ProgressFractionAtPrepareStart { get; private set; }

        public int MarkReadyCallCount { get; private set; }

        public Exception? LastLoadingFailureError { get; private set; }

        public IReadOnlyList<MemoryVideoDimensions>? VideoDimensions
        {
            get;
            private set;
        }

        public void ReportLoadingProgress(MemoryLoadingProgress progress)
        {
            LastProgress = progress;
            LoadingProgressHistory.Add(progress);
        }

        public void ConfigureVideoDimensions(
            IReadOnlyList<MemoryVideoDimensions> dimensions)
        {
            VideoDimensions = dimensions.ToArray();
        }

        public Task<MemoryPlaybackPreparationResult> PrepareAsync(
            IProgress<MemoryLoadingProgress>? progress,
            CancellationToken cancellationToken)
        {
            PrepareCallCount++;
            PrepareStarted = true;
            ProgressFractionAtPrepareStart = LastProgress?.Fraction;
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(
                new MemoryLoadingProgress(
                    0.5d,
                    "正在准备播放器"));
            if (!_blockPreparation)
            {
                return Task.FromResult(_preparationResult);
            }

            var completion =
                new TaskCompletionSource<MemoryPlaybackPreparationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            _ = cancellationToken.Register(
                () =>
                {
                    PreparationCancellationObserved = true;
                    completion.TrySetCanceled(cancellationToken);
                });
            return completion.Task;
        }

        public void MarkReady()
        {
            MarkReadyCallCount++;
            Phase = MemoryPlaybackPhase.Ready;
            PhaseChanged?.Invoke(this, EventArgs.Empty);
        }

        public void MarkLoadingFailed(
            string userMessage,
            Exception? error = null)
        {
            LastLoadingFailureError = error;
            Phase = MemoryPlaybackPhase.Failed;
            PhaseChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseNaturalPlaybackCompleted()
        {
            Phase = MemoryPlaybackPhase.Ended;
            PhaseChanged?.Invoke(this, EventArgs.Empty);
            if (_naturalPlaybackCompleted)
            {
                NaturalPlaybackCompleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            _naturalPlaybackCompleted = true;
            NaturalPlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }

        public void RequestRetry()
        {
            if (Phase != MemoryPlaybackPhase.Failed)
            {
                throw new InvalidOperationException(
                    "Retry can only be requested from a failed session.");
            }

            RetryRequested?.Invoke(this, EventArgs.Empty);
            Close(
                MemoryPlaybackOutcome.Failed,
                LastLoadingFailureError);
        }

        public void Close(
            MemoryPlaybackOutcome outcome,
            Exception? error = null)
        {
            if (!IsVisible)
            {
                return;
            }

            IsVisible = false;
            Phase = MemoryPlaybackPhase.Closed;
            PhaseChanged?.Invoke(this, EventArgs.Empty);
            _completion.TrySetResult(new MemoryPlaybackResult(outcome, error));
        }

        public void Dispose()
        {
            Close(
                _naturalPlaybackCompleted
                    ? MemoryPlaybackOutcome.Ended
                    : MemoryPlaybackOutcome.Cancelled);
        }
    }
}
