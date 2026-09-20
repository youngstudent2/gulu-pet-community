using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuluPet.Animation;
using GuluPet.Behavior;
using GuluPet.Dialogue;
using GuluPet.Domain;
using GuluPet.Persistence;
using GuluPet.Platform;
using GuluPet.Presentation;
using GuluPet.Runtime;
using GuluPet.Sensing;

namespace GuluPet.Tests;

internal static class PetControllerTests
{
    public static void RunAll()
    {
        Run(
            nameof(UnknownTestClipDoesNotDisturbActiveBehavior),
            UnknownTestClipDoesNotDisturbActiveBehavior);
        Run(
            nameof(TopLevelPresentationBlocksTestClip),
            TopLevelPresentationBlocksTestClip);
        Run(
            nameof(ContextMonitorLifecycleAndFullscreenMerge),
            ContextMonitorLifecycleAndFullscreenMerge);
        Run(
            nameof(ActiveInteractionSessionsAreCountedOnce),
            ActiveInteractionSessionsAreCountedOnce);
        Run(
            nameof(ClickClassificationUsesAdvanceTimerPathWithoutDoubleCounting),
            ClickClassificationUsesAdvanceTimerPathWithoutDoubleCounting);
        Run(
            nameof(CareAndInteractionBarApisUseAcceptedCommitPipeline),
            CareAndInteractionBarApisUseAcceptedCommitPipeline);
        Run(
            nameof(SideDockPausesAndBlocksBehaviorUntilReleased),
            SideDockPausesAndBlocksBehaviorUntilReleased);
        Run(
            nameof(SideDockTransfersPauseToTopLevelWithoutPlaybackGap),
            SideDockTransfersPauseToTopLevelWithoutPlaybackGap);
        Run(
            nameof(DisposeReleasesSideDockPresentation),
            DisposeReleasesSideDockPresentation);
        Run(
            nameof(RelationshipPersistsAcrossControllerRestart),
            RelationshipPersistsAcrossControllerRestart);
    }

    internal static void RunRuntimeContextOnly() =>
        Run(
            nameof(ContextMonitorLifecycleAndFullscreenMerge),
            ContextMonitorLifecycleAndFullscreenMerge);

    internal static void RunClickClassificationOnly() =>
        Run(
            nameof(ClickClassificationUsesAdvanceTimerPathWithoutDoubleCounting),
            ClickClassificationUsesAdvanceTimerPathWithoutDoubleCounting);

    internal static void RunRelationshipOnly() =>
        Run(
            nameof(RelationshipPersistsAcrossControllerRestart),
            RelationshipPersistsAcrossControllerRestart);

    internal static void RunSideDockOnly()
    {
        Run(
            nameof(SideDockPausesAndBlocksBehaviorUntilReleased),
            SideDockPausesAndBlocksBehaviorUntilReleased);
        Run(
            nameof(SideDockTransfersPauseToTopLevelWithoutPlaybackGap),
            SideDockTransfersPauseToTopLevelWithoutPlaybackGap);
        Run(
            nameof(DisposeReleasesSideDockPresentation),
            DisposeReleasesSideDockPresentation);
    }

    private static void SideDockPausesAndBlocksBehaviorUntilReleased()
    {
        RunSta(
            () =>
            {
                using ControllerContentFixture content =
                    ControllerContentFixture.Create();
                var petWindow = new PetWindow();
                try
                {
                    _ = new WindowInteropHelper(petWindow).EnsureHandle();
                    SideDockCandidate candidate =
                        CreateSideDockCandidate(petWindow);
                    using var controller = new PetController(
                        petWindow,
                        content.Animations,
                        content.Behaviors,
                        content.Dialogues);
                    int acceptedInteractions = 0;
                    controller.AcceptedActiveInteraction +=
                        (_, _) => acceptedInteractions++;
                    controller.Start();
                    BehaviorTestCheck.Equal(
                        new BehaviorId("idle_fallback"),
                        BehaviorTestCheck.NotNull(
                            controller.GetBehaviorTestSnapshot().Session)
                            .BehaviorId);

                    controller.EnterSideDockForTest(candidate);

                    BehaviorTestCheck.True(controller.IsSideDockedForTest);
                    BehaviorTestCheck.True(petWindow.IsSideDocked);
                    BehaviorCoordinatorSnapshot docked =
                        controller.GetBehaviorTestSnapshot();
                    BehaviorSessionSnapshot interrupted =
                        AssertPausedWithoutActiveBehavior(docked);
                    BehaviorTestCheck.Equal(
                        StablePetState.Normal,
                        docked.State.StableState);
                    BehaviorTestCheck.Equal(
                        TestClipPlaybackResult.SideDocked,
                        controller.PlayTestClipForTest("click_action"));
                    AssertRejectedWhileDocked(
                        controller.TriggerClickForTest(4_001));
                    AssertRejectedWhileDocked(
                        controller.TriggerCareInteraction("feed"));
                    AssertRejectedWhileDocked(
                        controller.TriggerPettingFromInteractionBar());
                    AssertRejectedWhileDocked(
                        controller.EnqueueBehaviorForTest("scheduled_noon"));
                    AssertRejectedWhileDocked(
                        controller.StartBehaviorIfIdleForTest(
                            "scheduled_noon"));
                    AssertRejectedWhileDocked(
                        controller.StartBehaviorManualPreview(
                            new BehaviorId("scheduled_noon")));

                    BehaviorTickResult blockedTick =
                        controller.ForceBehaviorTickForTest(seed: 42);
                    BehaviorTestCheck.Null(blockedTick.SelectedBehaviorId);
                    BehaviorTestCheck.Equal(0, blockedTick.AttemptOrder.Count);
                    BehaviorTestCheck.Null(blockedTick.QueueResult);
                    controller.AdvanceBehaviorRuntimeForTest();
                    BehaviorCoordinatorSnapshot afterBlockedTriggers =
                        controller.GetBehaviorTestSnapshot();
                    BehaviorSessionSnapshot stillInterrupted =
                        AssertPausedWithoutActiveBehavior(
                            afterBlockedTriggers);
                    BehaviorTestCheck.Equal(
                        interrupted.SessionToken,
                        stillInterrupted.SessionToken);
                    BehaviorTestCheck.Equal(
                        0,
                        afterBlockedTriggers.Queue.PendingCount);
                    BehaviorTestCheck.Equal(0, acceptedInteractions);

                    petWindow.SetSideDockHoverForTest(hovered: true);
                    petWindow.AdvanceSideDockBlinkForTest();
                    BehaviorTestCheck.True(petWindow.IsSideDockHovered);
                    BehaviorTestCheck.True(
                        petWindow.IsSideDockBlinkClosed);
                    BehaviorTestCheck.Equal(
                        "left_hover_closed.png",
                        petWindow.CurrentSideDockAssetFileName);
                    BehaviorSessionSnapshot afterHover =
                        AssertPausedWithoutActiveBehavior(
                            controller.GetBehaviorTestSnapshot());
                    BehaviorTestCheck.Equal(
                        interrupted.SessionToken,
                        afterHover.SessionToken);

                    petWindow.ExitSideDock();

                    BehaviorTestCheck.False(controller.IsSideDockedForTest);
                    BehaviorTestCheck.False(petWindow.IsSideDocked);
                    BehaviorCoordinatorSnapshot resumed =
                        controller.GetBehaviorTestSnapshot();
                    BehaviorTestCheck.False(resumed.Paused);
                    BehaviorSessionSnapshot resumedSession =
                        BehaviorTestCheck.NotNull(resumed.Session);
                    BehaviorTestCheck.Equal(
                        new BehaviorId("idle_fallback"),
                        resumedSession.BehaviorId);
                    controller.ExitSideDockForTest();
                    BehaviorCoordinatorSnapshot afterSecondExit =
                        controller.GetBehaviorTestSnapshot();
                    BehaviorTestCheck.False(afterSecondExit.Paused);
                    BehaviorTestCheck.Equal(
                        resumedSession.SessionToken,
                        BehaviorTestCheck.NotNull(afterSecondExit.Session)
                            .SessionToken);
                    BehaviorMutationResult feed =
                        controller.TriggerCareInteraction("feed");
                    BehaviorTestCheck.True(feed.Accepted, feed.RejectionReason);
                    BehaviorTestCheck.Equal(
                        new BehaviorId("care_feed_cat_treat"),
                        feed.BehaviorId!.Value);
                    BehaviorTestCheck.Equal(1, acceptedInteractions);
                }
                finally
                {
                    petWindow.Close();
                }
            });
    }

    private static void AssertRejectedWhileDocked(
        BehaviorMutationResult result)
    {
        BehaviorTestCheck.False(result.Accepted);
        BehaviorTestCheck.Equal("side-docked", result.RejectionReason);
    }

    private static BehaviorSessionSnapshot AssertPausedWithoutActiveBehavior(
        BehaviorCoordinatorSnapshot snapshot)
    {
        BehaviorTestCheck.True(snapshot.Paused);
        BehaviorSessionSnapshot terminal =
            BehaviorTestCheck.NotNull(snapshot.Session);
        BehaviorTestCheck.Equal(BehaviorPhase.Terminal, terminal.Phase);
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Interrupted,
            BehaviorTestCheck.NotNull(terminal.Terminal).Status);
        return terminal;
    }

    private static void SideDockTransfersPauseToTopLevelWithoutPlaybackGap()
    {
        RunSta(
            () =>
            {
                using ControllerContentFixture content =
                    ControllerContentFixture.Create();
                var petWindow = new PetWindow();
                try
                {
                    _ = new WindowInteropHelper(petWindow).EnsureHandle();
                    using var controller = new PetController(
                        petWindow,
                        content.Animations,
                        content.Behaviors,
                        content.Dialogues);
                    controller.Start();
                    controller.EnterSideDockForTest(
                        CreateSideDockCandidate(petWindow));
                    BehaviorSessionSnapshot interrupted =
                        AssertPausedWithoutActiveBehavior(
                            controller.GetBehaviorTestSnapshot());

                    Task suspension =
                        controller.SuspendAfterCurrentInteractionAsync();

                    BehaviorTestCheck.True(suspension.IsCompletedSuccessfully);
                    suspension.GetAwaiter().GetResult();
                    BehaviorTestCheck.False(petWindow.IsSideDocked);
                    BehaviorTestCheck.False(controller.IsSideDockedForTest);
                    BehaviorTestCheck.True(
                        controller.IsTopLevelPresentationRequestedOrActive);
                    BehaviorCoordinatorSnapshot transferred =
                        controller.GetBehaviorTestSnapshot();
                    BehaviorTestCheck.True(transferred.Paused);
                    BehaviorSessionSnapshot sameTerminal =
                        BehaviorTestCheck.NotNull(transferred.Session);
                    BehaviorTestCheck.Equal(
                        interrupted.SessionToken,
                        sameTerminal.SessionToken);
                    BehaviorTestCheck.Equal(
                        BehaviorPhase.Terminal,
                        sameTerminal.Phase);
                }
                finally
                {
                    petWindow.Close();
                }
            });
    }

    private static void DisposeReleasesSideDockPresentation()
    {
        RunSta(
            () =>
            {
                using ControllerContentFixture content =
                    ControllerContentFixture.Create();
                var petWindow = new PetWindow();
                PetController? controller = null;
                try
                {
                    _ = new WindowInteropHelper(petWindow).EnsureHandle();
                    controller = new PetController(
                        petWindow,
                        content.Animations,
                        content.Behaviors,
                        content.Dialogues);
                    controller.Start();
                    controller.EnterSideDockForTest(
                        CreateSideDockCandidate(petWindow));
                    BehaviorTestCheck.True(petWindow.IsSideDocked);

                    controller.Dispose();

                    BehaviorTestCheck.False(petWindow.IsSideDocked);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        petWindow.PetImage.Visibility);
                    BehaviorTestCheck.Equal(
                        Visibility.Collapsed,
                        petWindow.SideDockImage.Visibility);
                }
                finally
                {
                    controller?.Dispose();
                    petWindow.Close();
                }
            });
    }

    private static SideDockCandidate CreateSideDockCandidate(
        PetWindow petWindow,
        SideDockEdge edge = SideDockEdge.Left)
    {
        WindowRectangle original =
            WindowPlacementService.GetRectangle(petWindow);
        return new SideDockCandidate
        {
            Edge = edge,
            DockBounds = new WindowRectangle(
                checked(original.Left + 24),
                original.Top,
                original.Width,
                original.Height),
            RestoreBounds = original,
            MonitorBounds = new WindowRectangle(
                original.Left - 1_000,
                original.Top - 1_000,
                3_000,
                3_000),
            WorkArea = new WindowRectangle(
                original.Left - 1_000,
                original.Top - 1_000,
                3_000,
                3_000),
        };
    }

    private static void RelationshipPersistsAcrossControllerRestart()
    {
        RunSta(
            () =>
            {
                using ControllerContentFixture content =
                    ControllerContentFixture.Create();
                string statePath = Path.Combine(
                    content.Root,
                    "relationship.json");
                var firstWindow = new PetWindow();
                try
                {
                    var firstClock = new ManualDualClock();
                    using var first = new PetController(
                        firstWindow,
                        content.Animations,
                        content.Behaviors,
                        content.Dialogues,
                        behaviorClock: firstClock,
                        relationshipStateStore:
                            new RelationshipStateStore(statePath));
                    BehaviorTestCheck.Close(
                        EmotionState.DefaultAffection,
                        first.GetBehaviorTestSnapshot().Emotions.Affection);
                    BehaviorTestCheck.Equal(
                        RelationshipStages.Familiar,
                        first.RelationshipStage);
                    first.Start();

                    BehaviorMutationResult click =
                        first.TriggerClickForTest(501);
                    BehaviorTestCheck.True(
                        click.Accepted,
                        click.RejectionReason);
                    firstClock.Advance(TimeSpan.FromMilliseconds(701));
                    first.AdvanceBehaviorRuntimeForTest();

                    BehaviorCoordinatorSnapshot changed =
                        first.GetBehaviorTestSnapshot();
                    BehaviorTestCheck.Close(55, changed.Emotions.Affection);
                    BehaviorTestCheck.Close(90, changed.Emotions.Happiness);
                    BehaviorTestCheck.Equal(
                        RelationshipStages.Close,
                        changed.RelationshipStage);
                }
                finally
                {
                    firstWindow.Close();
                }

                RelationshipState durable =
                    new RelationshipStateStore(statePath).Load();
                BehaviorTestCheck.Close(55, durable.Affection);
                BehaviorTestCheck.Equal(
                    RelationshipStages.Close,
                    durable.Stage);

                var secondWindow = new PetWindow();
                try
                {
                    using var restarted = new PetController(
                        secondWindow,
                        content.Animations,
                        content.Behaviors,
                        content.Dialogues,
                        relationshipStateStore:
                            new RelationshipStateStore(statePath));
                    BehaviorCoordinatorSnapshot restored =
                        restarted.GetBehaviorTestSnapshot();
                    BehaviorTestCheck.Close(55, restored.Emotions.Affection);
                    BehaviorTestCheck.Close(
                        EmotionState.DefaultHappiness,
                        restored.Emotions.Happiness);
                    BehaviorTestCheck.Equal(
                        RelationshipStages.Close,
                        restarted.RelationshipStage);
                }
                finally
                {
                    secondWindow.Close();
                }
            });
    }

    private static void ClickClassificationUsesAdvanceTimerPathWithoutDoubleCounting()
    {
        RunSta(
            () =>
            {
                using ControllerContentFixture content =
                    ControllerContentFixture.Create();
                var petWindow = new PetWindow();
                var clock = new ManualDualClock();
                try
                {
                    using var controller = new PetController(
                        petWindow,
                        content.Animations,
                        content.Behaviors,
                        content.Dialogues,
                        behaviorClock: clock,
                        behaviorInteractionOptions:
                            new BehaviorInteractionOptions
                            {
                                RepeatedClickThreshold = 3,
                                RepeatedClickWindow =
                                    TimeSpan.FromMilliseconds(700),
                            });
                    int countedInteractions = 0;
                    controller.AcceptedActiveInteraction +=
                        (_, _) => countedInteractions++;
                    controller.Start();

                    BehaviorMutationResult pending =
                        controller.TriggerClickForTest(101);
                    BehaviorTestCheck.True(
                        pending.Accepted,
                        pending.RejectionReason);
                    BehaviorTestCheck.Null(pending.BehaviorId);
                    BehaviorTestCheck.Equal(1, countedInteractions);
                    BehaviorTestCheck.Equal(
                        new BehaviorId("idle_fallback"),
                        controller.GetBehaviorTestSnapshot().Session!.BehaviorId);

                    clock.Advance(TimeSpan.FromMilliseconds(700));
                    controller.AdvanceBehaviorRuntimeForTest();
                    BehaviorTestCheck.Equal(
                        new BehaviorId("idle_fallback"),
                        controller.GetBehaviorTestSnapshot().Session!.BehaviorId);

                    clock.Advance(TimeSpan.FromMilliseconds(1));
                    controller.AdvanceBehaviorRuntimeForTest();
                    BehaviorTestCheck.Equal(
                        new BehaviorId("click_feedback"),
                        controller.GetBehaviorTestSnapshot().Session!.BehaviorId);
                    BehaviorTestCheck.True(
                        countedInteractions == 1,
                        "The timer-side classified dispatch must not recount " +
                        "the physical click.");
                }
                finally
                {
                    petWindow.Close();
                }
            });
    }

    private static void CareAndInteractionBarApisUseAcceptedCommitPipeline()
    {
        RunSta(
            () =>
            {
                using ControllerContentFixture content =
                    ControllerContentFixture.Create();

                var previewWindow = new PetWindow();
                try
                {
                    using var previewController = new PetController(
                        previewWindow,
                        content.Animations,
                        content.Behaviors,
                        content.Dialogues);
                    int careCommits = 0;
                    previewController.CareInteractionCommitted +=
                        (_, _) => careCommits++;
                    previewController.Start();

                    BehaviorMutationResult preview =
                        previewController.StartBehaviorManualPreview(
                            new BehaviorId("care_feed_cat_treat"));
                    BehaviorTestCheck.True(
                        preview.Accepted,
                        preview.RejectionReason);
                    BehaviorTestCheck.Equal(
                        new BehaviorId("care_feed_cat_treat"),
                        preview.BehaviorId!.Value);
                    BehaviorTestCheck.True(
                        careCommits == 0,
                        "A behavior-browser preview must not refill care.");
                }
                finally
                {
                    previewWindow.Close();
                }

                foreach ((string action, BehaviorId behaviorId) in new[]
                         {
                             ("feed", new BehaviorId("care_feed_cat_treat")),
                             ("water", new BehaviorId("care_drink_water")),
                         })
                {
                    var petWindow = new PetWindow();
                    try
                    {
                        using var controller = new PetController(
                            petWindow,
                            content.Animations,
                            content.Behaviors,
                            content.Dialogues);
                        int acceptedInteractions = 0;
                        var commits =
                            new List<CareInteractionCommittedEventArgs>();
                        controller.AcceptedActiveInteraction +=
                            (_, _) => acceptedInteractions++;
                        controller.CareInteractionCommitted +=
                            (_, e) => commits.Add(e);
                        controller.Start();

                        _ = BehaviorTestCheck.Throws<ArgumentException>(
                            () => controller.TriggerCareInteraction(
                                action.ToUpperInvariant()));
                        BehaviorTestCheck.Equal(0, acceptedInteractions);
                        BehaviorTestCheck.Equal(0, commits.Count);

                        BehaviorMutationResult result =
                            controller.TriggerCareInteraction(action);
                        BehaviorTestCheck.True(
                            result.Accepted,
                            result.RejectionReason);
                        BehaviorTestCheck.Equal(behaviorId, result.BehaviorId!.Value);
                        BehaviorTestCheck.Equal(1, acceptedInteractions);
                        CareInteractionCommittedEventArgs committed =
                            commits.Single();
                        BehaviorTestCheck.Equal(action, committed.CareAction);
                        BehaviorTestCheck.Equal(behaviorId, committed.BehaviorId);
                        BehaviorTestCheck.Equal(
                            result.RequestId!.Value,
                            committed.RequestId);
                        BehaviorTestCheck.True(
                            committed.SessionToken.Value > 0);
                    }
                    finally
                    {
                        petWindow.Close();
                    }
                }

                var pettingWindow = new PetWindow();
                try
                {
                    using var controller = new PetController(
                        pettingWindow,
                        content.Animations,
                        content.Behaviors,
                        content.Dialogues);
                    int acceptedInteractions = 0;
                    int careCommits = 0;
                    controller.AcceptedActiveInteraction +=
                        (_, _) => acceptedInteractions++;
                    controller.CareInteractionCommitted +=
                        (_, _) => careCommits++;
                    controller.Start();

                    BehaviorMutationResult result =
                        controller.TriggerPettingFromInteractionBar();
                    BehaviorTestCheck.True(
                        result.Accepted,
                        result.RejectionReason);
                    BehaviorTestCheck.Equal(
                        new BehaviorId("petting_from_bar"),
                        result.BehaviorId!.Value);
                    BehaviorTestCheck.Equal(1, acceptedInteractions);
                    BehaviorTestCheck.Equal(0, careCommits);
                }
                finally
                {
                    pettingWindow.Close();
                }
            });
    }

    private static void ActiveInteractionSessionsAreCountedOnce()
    {
        var deduplicator = new ActiveInteractionDeduplicator();

        BehaviorTestCheck.True(
            deduplicator.TryAcceptPressSession(1));
        BehaviorTestCheck.False(
            deduplicator.TryAcceptPressSession(1));
        BehaviorTestCheck.True(
            deduplicator.TryAcceptPressSession(2));

        BehaviorTestCheck.True(
            deduplicator.TryAcceptPointerFocusSession(1));
        BehaviorTestCheck.False(
            deduplicator.TryAcceptPointerFocusSession(1));
        BehaviorTestCheck.True(
            deduplicator.TryAcceptPointerFocusSession(2));

        // Press and pointer-focus streams are independent even when their
        // monotonically increasing numeric ids happen to match.
        BehaviorTestCheck.True(
            deduplicator.TryAcceptPressSession(3));
        BehaviorTestCheck.True(
            deduplicator.TryAcceptPointerFocusSession(3));

        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => deduplicator.TryAcceptPressSession(0));
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => deduplicator.TryAcceptPointerFocusSession(-1));
    }

    private static void ContextMonitorLifecycleAndFullscreenMerge()
    {
        RunSta(
            () =>
            {
                using ControllerContentFixture content =
                    ControllerContentFixture.Create();
                var monitor = new FakeRuntimeContextMonitor();
                var petWindow = new PetWindow();
                var controller = new PetController(
                    petWindow,
                    content.Animations,
                    content.Behaviors,
                    content.Dialogues,
                    monitor,
                    new InMemoryBehaviorDailyOpportunityLedger());
                try
                {
                    monitor.Publish(
                        new BehaviorContextSnapshot
                        {
                            Revision = 800,
                            LocalNow = new DateTimeOffset(
                                2026,
                                7,
                                28,
                                12,
                                0,
                                0,
                                TimeSpan.FromHours(8)),
                            ApplicationCategory = "office",
                            ApplicationCategoryAvailable = true,
                            ApplicationStableFor = TimeSpan.FromMinutes(1),
                        });
                    controller.Start();
                    BehaviorTestCheck.True(monitor.Started);
                    BehaviorTestCheck.True(
                        controller
                            .ReadBehaviorEventsForTest(0)
                            .Events
                            .Any(
                                static entry =>
                                    entry.Kind
                                        == BehaviorEventKind.RequestReceived
                                    && entry.BehaviorId
                                        == new BehaviorId(
                                            "scheduled_noon")),
                        "The startup context trigger was consumed before " +
                        "the behavior runtime started.");

                    monitor.Publish(
                        new BehaviorContextSnapshot
                        {
                            Revision = 900,
                            LocalNow = new DateTimeOffset(
                                2026,
                                7,
                                28,
                                14,
                                0,
                                0,
                                TimeSpan.FromHours(8)),
                            ApplicationCategory = "ide",
                            ApplicationCategoryAvailable = true,
                            ApplicationStableFor = TimeSpan.FromMinutes(3),
                            KeyboardPerMinute = 120,
                            MouseClicksPerMinute = 20,
                            KeyboardCountSinceStart = 12_345,
                            MouseClickCountSinceStart = 2_345,
                            IdleFor = TimeSpan.FromSeconds(2),
                            IsLocked = true,
                            WeatherKind = "rain",
                            WeatherAvailable = true,
                            TemperatureCelsius = 31,
                        });

                    BehaviorContextSnapshot sensed = controller
                        .GetBehaviorTestSnapshot()
                        .Context;
                    BehaviorTestCheck.Equal("ide", sensed.ApplicationCategory);
                    BehaviorTestCheck.Close(
                        120,
                        sensed.KeyboardPerMinute);
                    BehaviorTestCheck.Equal(
                        12_345L,
                        sensed.KeyboardCountSinceStart);
                    BehaviorTestCheck.Equal(
                        2_345L,
                        sensed.MouseClickCountSinceStart);
                    BehaviorTestCheck.True(sensed.IsLocked);
                    BehaviorTestCheck.Equal("rain", sensed.WeatherKind);
                    BehaviorTestCheck.False(sensed.IsFullscreen);
                    long sensedRevision = sensed.Revision;

                    controller.SetFullscreen(true);
                    BehaviorContextSnapshot fullscreen = controller
                        .GetBehaviorTestSnapshot()
                        .Context;
                    BehaviorTestCheck.True(fullscreen.IsFullscreen);
                    BehaviorTestCheck.Equal(
                        "ide",
                        fullscreen.ApplicationCategory);
                    BehaviorTestCheck.Equal(
                        "rain",
                        fullscreen.WeatherKind);
                    BehaviorTestCheck.Equal(
                        12_345L,
                        fullscreen.KeyboardCountSinceStart);
                    BehaviorTestCheck.Equal(
                        2_345L,
                        fullscreen.MouseClickCountSinceStart);
                    BehaviorTestCheck.True(fullscreen.IsLocked);
                    BehaviorTestCheck.True(
                        fullscreen.Revision > sensedRevision);
                }
                finally
                {
                    controller.Dispose();
                    petWindow.Close();
                }

                BehaviorTestCheck.True(monitor.Stopped);
                BehaviorTestCheck.True(monitor.Disposed);
                BehaviorTestCheck.Equal(0, monitor.SubscriberCount);
            });
    }

    private static void UnknownTestClipDoesNotDisturbActiveBehavior()
    {
        RunSta(
            () =>
            {
                using ControllerContentFixture content =
                    ControllerContentFixture.Create();
                var petWindow = new PetWindow();
                try
                {
                    using var controller = new PetController(
                        petWindow,
                        content.Animations,
                        content.Behaviors,
                        content.Dialogues);
                    controller.Start();

                    BehaviorCoordinatorSnapshot before =
                        controller.GetBehaviorTestSnapshot();
                    BehaviorSessionSnapshot beforeSession =
                        BehaviorTestCheck.NotNull(before.Session);
                    string? beforeAction =
                        controller.GetTestDiagnostics().CurrentAction;
                    BehaviorEventPage beforeEvents =
                        controller.ReadBehaviorEventsForTest(0);
                    long lastSequence =
                        beforeEvents.Events.LastOrDefault()?.Sequence ?? 0;

                    BehaviorTestCheck.Equal(
                        TestClipPlaybackResult.UnknownClip,
                        controller.PlayTestClipForTest(
                            "definitely_missing_test_clip"));

                    BehaviorCoordinatorSnapshot after =
                        controller.GetBehaviorTestSnapshot();
                    BehaviorSessionSnapshot afterSession =
                        BehaviorTestCheck.NotNull(after.Session);
                    BehaviorTestCheck.False(after.Paused);
                    BehaviorTestCheck.Equal(
                        beforeSession.SessionToken,
                        afterSession.SessionToken);
                    BehaviorTestCheck.Equal(
                        beforeSession.BehaviorId,
                        afterSession.BehaviorId);
                    BehaviorTestCheck.Equal(
                        beforeAction,
                        controller.GetTestDiagnostics().CurrentAction);
                    BehaviorTestCheck.Equal(
                        0,
                        controller
                            .ReadBehaviorEventsForTest(lastSequence)
                            .Events
                            .Count);
                }
                finally
                {
                    petWindow.Close();
                }
            });
    }

    private static void TopLevelPresentationBlocksTestClip()
    {
        RunSta(
            () =>
            {
                using ControllerContentFixture content =
                    ControllerContentFixture.Create();
                var petWindow = new PetWindow();
                try
                {
                    using var controller = new PetController(
                        petWindow,
                        content.Animations,
                        content.Behaviors,
                        content.Dialogues);
                    controller.Start(
                        suspendedForTopLevelPresentation: true);

                    BehaviorTestCheck.True(
                        controller
                            .IsTopLevelPresentationRequestedOrActive);
                    BehaviorTestCheck.Equal(
                        TestClipPlaybackResult
                            .TopLevelPresentationActive,
                        controller.PlayTestClipForTest(
                            "idle_breathe"));
                }
                finally
                {
                    petWindow.Close();
                }
            });
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
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("The STA controller test timed out.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(PetControllerTests)}.{name}");
    }

    private sealed class FakeRuntimeContextMonitor : IRuntimeContextMonitor
    {
        private EventHandler<RuntimeContextSnapshotChangedEventArgs>?
            _snapshotChanged;

        public event EventHandler<RuntimeContextSnapshotChangedEventArgs>?
            SnapshotChanged
        {
            add => _snapshotChanged += value;
            remove => _snapshotChanged -= value;
        }

        public BehaviorContextSnapshot Current { get; private set; } =
            BehaviorContextSnapshot.Empty with
            {
                LocalNow = DateTimeOffset.Now,
            };

        public bool Started { get; private set; }

        public bool Stopped { get; private set; }

        public bool Disposed { get; private set; }

        public int SubscriberCount =>
            _snapshotChanged?.GetInvocationList().Length ?? 0;

        public InputActivityReading ReadCurrentInputActivity() =>
            new(
                Current.KeyboardCountSinceStart,
                Current.MouseClickCountSinceStart,
                true);

        public void Start()
        {
            Started = true;
        }

        public void Stop()
        {
            Stopped = true;
        }

        public void Publish(BehaviorContextSnapshot snapshot)
        {
            Current = snapshot;
            _snapshotChanged?.Invoke(
                this,
                new RuntimeContextSnapshotChangedEventArgs(snapshot));
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class ControllerContentFixture : IDisposable
    {
        private ControllerContentFixture(
            string root,
            AnimationCatalog animations,
            BehaviorCatalog behaviors,
            DialogueCatalog dialogues)
        {
            Root = root;
            Animations = animations;
            Behaviors = behaviors;
            Dialogues = dialogues;
        }

        public string Root { get; }

        public AnimationCatalog Animations { get; }

        public BehaviorCatalog Behaviors { get; }

        public DialogueCatalog Dialogues { get; }

        public static ControllerContentFixture Create()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                $"GuluPet.PetController.{Guid.NewGuid():N}");
            string animationRoot = Path.Combine(root, "Animations");
            string dataDirectory = Path.Combine(root, "Data");
            Directory.CreateDirectory(animationRoot);
            Directory.CreateDirectory(dataDirectory);
            try
            {
                WriteClip(animationRoot, "idle_breathe", loop: true);
                WriteClip(animationRoot, "click_action", loop: false);
                WriteClip(animationRoot, "repeat_action", loop: false);
                WriteClip(animationRoot, "eat_cat_treat", loop: false);
                WriteClip(animationRoot, "drink_water", loop: false);
                WriteClip(animationRoot, "petting_action", loop: false);
                string dialoguePath = Path.Combine(
                    dataDirectory,
                    "dialogues.json");
                File.WriteAllText(
                    dialoguePath,
                    """
                    [
                      {
                        "id": "test-neutral",
                        "text": "喵~",
                        "meaning": "我在这里陪你。",
                        "scene": "companionship",
                        "action": "settle",
                        "mood": "calm",
                        "relationshipStages": ["familiar"],
                        "type": "dialogue",
                        "weight": 1
                      }
                    ]
                    """);

                AnimationCatalog animations = AnimationCatalog.LoadAsync(
                        animationRoot,
                        decodePixelWidth: 0)
                    .GetAwaiter()
                    .GetResult();
                var behaviors = new BehaviorCatalog(
                [
                    new BehaviorDefinition
                    {
                        Id = new BehaviorId("idle_fallback"),
                        DisplayName = "idle fallback",
                        Family = "quiet",
                        ClipFamily = "idle",
                        Tags = ["state:fallback"],
                        AllowedStates = [StablePetState.Normal],
                        Animation = new BehaviorAnimationPlan
                        {
                            LoopClipId = "idle_breathe",
                        },
                        Completion = new BehaviorCompletionPolicy
                        {
                            Kind = BehaviorCompletionKind.External,
                        },
                        MaximumDuration = TimeSpan.FromDays(1),
                        Queueable = false,
                        IsStateFallback = true,
                        Utility = new BehaviorUtilityRule
                        {
                            BaseScore = 0,
                            MaxConsecutiveRuns = 0,
                        },
                    },
                    new BehaviorDefinition
                    {
                        Id = new BehaviorId("scheduled_noon"),
                        DisplayName = "scheduled noon",
                        Family = "scheduled",
                        ClipFamily = "idle",
                        Tags = ["trigger:time:noon"],
                        AllowedStates = [StablePetState.Normal],
                        Animation = new BehaviorAnimationPlan
                        {
                            PerformClipId = "idle_breathe",
                        },
                        Completion = new BehaviorCompletionPolicy
                        {
                            Kind = BehaviorCompletionKind.Duration,
                            Duration = TimeSpan.FromMilliseconds(500),
                        },
                        MaximumDuration = TimeSpan.FromSeconds(1),
                        Utility = new BehaviorUtilityRule
                        {
                            BaseScore = 100,
                            MaxConsecutiveRuns = 1,
                        },
                    },
                    InteractionBehavior(
                        "click_feedback",
                        "click_action",
                        "trigger:click",
                        new EmotionDelta(
                            Happiness: 30,
                            Affection: 15)),
                    InteractionBehavior(
                        "repeat_feedback",
                        "repeat_action",
                        "trigger:repeat_click"),
                    InteractionBehavior(
                        "care_feed_cat_treat",
                        "eat_cat_treat",
                        "trigger:feed"),
                    InteractionBehavior(
                        "care_drink_water",
                        "drink_water",
                        "trigger:water"),
                    InteractionBehavior(
                        "petting_from_bar",
                        "petting_action",
                        "trigger:petting"),
                    InteractionBehavior(
                        "safe_micro_feedback",
                        "click_action",
                        "fallback:interaction"),
                ]);
                DialogueCatalog dialogues = DialogueCatalog.LoadAsync(
                        dialoguePath)
                    .GetAwaiter()
                    .GetResult();
                return new ControllerContentFixture(
                    root,
                    animations,
                    behaviors,
                    dialogues);
            }
            catch
            {
                Directory.Delete(root, recursive: true);
                throw;
            }
        }

        public void Dispose()
        {
            Animations.Dispose();
            Directory.Delete(Root, recursive: true);
        }

        private static void WriteClip(
            string animationRoot,
            string clipId,
            bool loop)
        {
            string clipDirectory = Path.Combine(animationRoot, clipId);
            Directory.CreateDirectory(clipDirectory);
            TestAnimatedWebp.Write(
                Path.Combine(clipDirectory, "animation.webp"));
            File.WriteAllText(
                Path.Combine(clipDirectory, "clip.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        name = clipId,
                        frameCount = TestAnimatedWebp.FrameCount,
                        fps = RuntimeContentContract.DefaultFramesPerSecond,
                        loop,
                        events = Array.Empty<object>(),
                        placeholder = false,
                        assetStatus = "production",
                        knownIssues = Array.Empty<string>(),
                    }));
        }

        private static BehaviorDefinition InteractionBehavior(
            string behaviorId,
            string clipId,
            string triggerTag,
            EmotionDelta? startedEmotionEffect = null) =>
            new()
            {
                Id = new BehaviorId(behaviorId),
                DisplayName = behaviorId,
                Family = behaviorId,
                ClipFamily = clipId,
                Tags = [triggerTag],
                AllowedStates = [StablePetState.Normal],
                Animation = new BehaviorAnimationPlan
                {
                    PerformClipId = clipId,
                },
                Completion = new BehaviorCompletionPolicy
                {
                    Kind = BehaviorCompletionKind.ClipEnd,
                },
                MaximumDuration = TimeSpan.FromSeconds(1),
                StartedEmotionEffect =
                    startedEmotionEffect ?? new EmotionDelta(),
                Utility = new BehaviorUtilityRule
                {
                    BaseScore = 30,
                    MaxConsecutiveRuns = 3,
                },
            };
    }
}
