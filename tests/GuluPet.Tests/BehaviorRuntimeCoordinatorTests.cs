using GuluPet.Behavior;
using GuluPet.Domain;

namespace GuluPet.Tests;

internal static class BehaviorRuntimeCoordinatorTests
{
    public static void RunAll()
    {
        Run(
            nameof(StartTickCompletionReturnsToFallback),
            StartTickCompletionReturnsToFallback);
        Run(
            nameof(AutomaticSleepQueuesAtFallbackBoundaryAndWakesViaExit),
            AutomaticSleepQueuesAtFallbackBoundaryAndWakesViaExit);
        Run(
            nameof(ActiveTickCanQueueEligibleExternalStateLifecycle),
            ActiveTickCanQueueEligibleExternalStateLifecycle);
        Run(
            nameof(AutomaticWakeTriggersPlaySleepExitBeforeSoftContinuation),
            AutomaticWakeTriggersPlaySleepExitBeforeSoftContinuation);
        Run(
            nameof(AutomaticWakeDuringSleepEnterStillUsesExit),
            AutomaticWakeDuringSleepEnterStillUsesExit);
        Run(
            nameof(DailyOpportunityCountsOnlyAfterFirstFrame),
            DailyOpportunityCountsOnlyAfterFirstFrame);
        Run(
            nameof(DailyWeatherTagsShareOnePendingOpportunity),
            DailyWeatherTagsShareOnePendingOpportunity);
        Run(
            nameof(DailyAutomaticWakePreservesMetadataAndExpiration),
            DailyAutomaticWakePreservesMetadataAndExpiration);
        Run(
            nameof(AutomaticWakeDiscardsPendingSleepBeforeItStarts),
            AutomaticWakeDiscardsPendingSleepBeforeItStarts);
        Run(
            nameof(SingleClickStartsAfterClassificationWindow),
            SingleClickStartsAfterClassificationWindow);
        Run(
            nameof(ThreeClicksWithinWindowStartRepeatImmediately),
            ThreeClicksWithinWindowStartRepeatImmediately);
        Run(
            nameof(ClickArrivingWhileBusyIsDroppedWithoutDeferredPlayback),
            ClickArrivingWhileBusyIsDroppedWithoutDeferredPlayback);
        Run(
            nameof(DragCancelsPendingClickBurst),
            DragCancelsPendingClickBurst);
        Run(
            nameof(RepeatedPettingIsDroppedUntilBehaviorCompletes),
            RepeatedPettingIsDroppedUntilBehaviorCompletes);
        Run(
            nameof(ToolbarActionSupersedesPointerInteraction),
            ToolbarActionSupersedesPointerInteraction);
        Run(
            nameof(ToolbarActionDoesNotSupersedeActiveCareInteraction),
            ToolbarActionDoesNotSupersedeActiveCareInteraction);
        Run(
            nameof(ToolbarActionDoesNotSupersedeAnotherToolbarAction),
            ToolbarActionDoesNotSupersedeAnotherToolbarAction);
        Run(
            nameof(SessionEventsExposeCommittedAndTerminatedRecords),
            SessionEventsExposeCommittedAndTerminatedRecords);
        Run(
            nameof(InteractionTerminationExposesLeaseBeforeFirstFrame),
            InteractionTerminationExposesLeaseBeforeFirstFrame);
        Run(
            nameof(PettingEndIsDroppedWhilePettingBehaviorRuns),
            PettingEndIsDroppedWhilePettingBehaviorRuns);
        Run(
            nameof(DifferentInteractionIsDroppedWhileBehaviorRuns),
            DifferentInteractionIsDroppedWhileBehaviorRuns);
        Run(
            nameof(ImmediateStartIsDroppedWhileBehaviorRuns),
            ImmediateStartIsDroppedWhileBehaviorRuns);
        Run(
            nameof(RejectedInteractionStartReleasesLease),
            RejectedInteractionStartReleasesLease);
        Run(
            nameof(SynchronousInteractionDuringPlaybackStartIsDropped),
            SynchronousInteractionDuringPlaybackStartIsDropped);
        Run(
            nameof(PublicImmediateStartRejectsInteractionSourceWithoutLeaseMetadata),
            PublicImmediateStartRejectsInteractionSourceWithoutLeaseMetadata);
        Run(
            nameof(SynchronousCompletionDoesNotLeaveInteractionLease),
            SynchronousCompletionDoesNotLeaveInteractionLease);
        Run(
            nameof(PausingInteractionReleasesLease),
            PausingInteractionReleasesLease);
        Run(
            nameof(ExternalPauseTransfersToTopLevelWithoutPlaybackGap),
            ExternalPauseTransfersToTopLevelWithoutPlaybackGap);
        Run(
            nameof(StartSuspendedWaitsForTopLevelPresentation),
            StartSuspendedWaitsForTopLevelPresentation);
        Run(
            nameof(TopLevelPresentationWaitsForInteractionCompletion),
            TopLevelPresentationWaitsForInteractionCompletion);
        Run(
            nameof(TopLevelPresentationPendingRejectsNewDrag),
            TopLevelPresentationPendingRejectsNewDrag);
        Run(
            nameof(TopLevelPresentationPendingRejectsDirectMutations),
            TopLevelPresentationPendingRejectsDirectMutations);
        Run(
            nameof(TopLevelPresentationWaitsForSleepExitAndFeedback),
            TopLevelPresentationWaitsForSleepExitAndFeedback);
        Run(
            nameof(SleepingClickPlaysExitBeforeFeedback),
            SleepingClickPlaysExitBeforeFeedback);
        Run(
            nameof(SleepingLongPressPlaysExitBeforeTaggedFeedback),
            SleepingLongPressPlaysExitBeforeTaggedFeedback);
        Run(
            nameof(SleepingCareBindsLeaseToTheActualCareSession),
            SleepingCareBindsLeaseToTheActualCareSession);
        Run(
            nameof(LongPressUsesTaggedBehavior),
            LongPressUsesTaggedBehavior);
        Run(
            nameof(DragReleaseUsesTaggedBehavior),
            DragReleaseUsesTaggedBehavior);
        Run(
            nameof(PausedDragReleaseClosesGateWithoutStartingBehavior),
            PausedDragReleaseClosesGateWithoutStartingBehavior);
        Run(
            nameof(DraggingIsAStableStateAndDefersQueuedWork),
            DraggingIsAStableStateAndDefersQueuedWork);
        Run(
            nameof(DragInterruptsCurrentBehaviorAndReleasePrecedesQueue),
            DragInterruptsCurrentBehaviorAndReleasePrecedesQueue);
        Run(
            nameof(FullscreenKeepsInteractionsAndFiltersAutomaticBehavior),
            FullscreenKeepsInteractionsAndFiltersAutomaticBehavior);
        Run(
            nameof(StaleDragReleaseRecoversQueueAndState),
            StaleDragReleaseRecoversQueueAndState);
        Run(
            nameof(PausedSleepingStateResumesDirectlyAtLoop),
            PausedSleepingStateResumesDirectlyAtLoop);
        Run(
            nameof(SeededTickIsRepeatableWithoutChangingRuntimeRandomStream),
            SeededTickIsRepeatableWithoutChangingRuntimeRandomStream);
        Run(
            nameof(ActiveTickPublishesMatchingTrace),
            ActiveTickPublishesMatchingTrace);
        Run(
            nameof(SynchronousFirstFrameKeepsJournalCausalOrder),
            SynchronousFirstFrameKeepsJournalCausalOrder);
        Run(
            nameof(FallbackJournalStartsWithRequestReceived),
            FallbackJournalStartsWithRequestReceived);
        Run(
            nameof(ManualPreviewInterruptsAndBypassesNormalConditions),
            ManualPreviewInterruptsAndBypassesNormalConditions);
        Run(
            nameof(ManualPreviewSynthesizesSleepingForWakeBehavior),
            ManualPreviewSynthesizesSleepingForWakeBehavior);
        Run(
            nameof(ManualPreviewDoesNotInterruptDragging),
            ManualPreviewDoesNotInterruptDragging);
        Run(
            nameof(ShortTermEmotionsUseMonotonicRegressionAndIgnoreRollback),
            ShortTermEmotionsUseMonotonicRegressionAndIgnoreRollback);
        Run(
            nameof(AffectionEffectsPublishStableRelationshipStage),
            AffectionEffectsPublishStableRelationshipStage);
    }

    internal static void RunRelationshipOnly()
    {
        Run(
            nameof(ShortTermEmotionsUseMonotonicRegressionAndIgnoreRollback),
            ShortTermEmotionsUseMonotonicRegressionAndIgnoreRollback);
        Run(
            nameof(AffectionEffectsPublishStableRelationshipStage),
            AffectionEffectsPublishStableRelationshipStage);
    }

    private static void StartTickCompletionReturnsToFallback()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorSessionSnapshot fallback =
            BehaviorTestCheck.NotNull(fixture.Coordinator.Sessions.Snapshot);
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fallback.BehaviorId);
        fixture.Coordinator.NotifyFirstFrame(
            BehaviorTestCheck.NotNull(
                fixture.Coordinator.Sessions.CurrentPlaybackToken));

        BehaviorTickResult tick =
            fixture.Coordinator.ForceActiveTick(deterministicSeed: 7);
        BehaviorTestCheck.True(tick.SelectedBehaviorId is not null);
        BehaviorSessionSnapshot beforeBoundary =
            BehaviorTestCheck.NotNull(fixture.Coordinator.Sessions.Snapshot);
        BehaviorTestCheck.Equal(fallback.BehaviorId, beforeBoundary.BehaviorId);
        BehaviorTestCheck.Equal(
            1,
            fixture.Coordinator.GetSnapshot().Queue.PendingCount);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyLoopBoundary(
                BehaviorTestCheck.NotNull(
                    fixture.Coordinator.Sessions.CurrentPlaybackToken)));

        BehaviorPlaybackToken playback =
            BehaviorTestCheck.NotNull(
                fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(playback));
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyClipCompleted(playback));

        BehaviorSessionSnapshot restored =
            BehaviorTestCheck.NotNull(fixture.Coordinator.Sessions.Snapshot);
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            restored.BehaviorId);
        BehaviorTestCheck.Equal(
            1,
            fixture.Coordinator.GetSnapshot().UtilityHistory.CommitCount);
    }

    private static void AutomaticSleepQueuesAtFallbackBoundaryAndWakesViaExit()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.UpdateContext(
            new BehaviorContextSnapshot
            {
                Revision = 1,
                LocalNow = new DateTimeOffset(
                    2026,
                    7,
                    28,
                    23,
                    30,
                    0,
                    TimeSpan.FromHours(8)),
            });
        fixture.Coordinator.Start();
        BehaviorPlaybackToken fallback = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(fallback));

        BehaviorMutationResult ordinary =
            fixture.Coordinator.EnqueueSoft(
                new BehaviorId("sleep_state"),
                BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.False(ordinary.Accepted);
        BehaviorTestCheck.Equal(
            "behavior-not-queueable",
            ordinary.RejectionReason);

        BehaviorMutationResult automatic =
            fixture.Coordinator.TriggerSoftTag(
                "trigger:time:late",
                BehaviorRequestSource.Scheduled,
                TimeSpan.FromHours(6));
        BehaviorTestCheck.True(automatic.Accepted, automatic.RejectionReason);
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.Equal(
            1,
            fixture.Coordinator.GetSnapshot().Queue.PendingCount);
        BehaviorTestCheck.Equal(
            fixture.Clock.Elapsed + TimeSpan.FromHours(6),
            fixture.Coordinator.GetSnapshot()
                .Queue.Groups
                .SelectMany(static group => group.PendingItems)
                .Single()
                .ExpiresAt);

        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyLoopBoundary(fallback));
        BehaviorSessionSnapshot entering =
            BehaviorTestCheck.NotNull(fixture.Coordinator.Sessions.Snapshot);
        BehaviorTestCheck.Equal(
            new BehaviorId("sleep_state"),
            entering.BehaviorId);
        BehaviorTestCheck.Equal("sleep_enter", entering.ClipId);

        BehaviorPlaybackToken enter = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(enter));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(enter));
        BehaviorPlaybackToken loop = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(loop));
        BehaviorTestCheck.Equal(
            StablePetState.Sleeping,
            fixture.Coordinator.StateMachine.StableState);

        BehaviorMutationResult click = fixture.Coordinator.TriggerClick();
        BehaviorTestCheck.True(click.Accepted, click.RejectionReason);
        ExpirePendingClick(fixture);
        BehaviorTestCheck.Equal(
            "sleep_exit",
            fixture.Coordinator.Sessions.Snapshot!.ClipId);
        BehaviorPlaybackToken exit = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(exit));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(exit));
        BehaviorTestCheck.Equal(
            new BehaviorId("click_blink"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
    }

    private static void AutomaticWakeTriggersPlaySleepExitBeforeSoftContinuation()
    {
        AssertAutomaticWake(
            "trigger:time:morning",
            BehaviorRequestSource.Scheduled,
            "morning_blink");
        AssertAutomaticWake(
            "trigger:user:return",
            BehaviorRequestSource.UserContext,
            "return_blink");
        AssertAutomaticWake(
            "trigger:user:resume",
            BehaviorRequestSource.UserContext,
            "resume_blink");
    }

    private static void ActiveTickCanQueueEligibleExternalStateLifecycle()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.UpdateContext(
            new BehaviorContextSnapshot
            {
                Revision = 2,
                LocalNow = new DateTimeOffset(
                    2026,
                    7,
                    28,
                    23,
                    30,
                    0,
                    TimeSpan.FromHours(8)),
            });
        fixture.Coordinator.Start();
        BehaviorPlaybackToken fallback = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        fixture.Coordinator.NotifyFirstFrame(fallback);

        BehaviorTickResult tick = fixture.Coordinator.ForceActiveTick();
        BehaviorTestCheck.Equal(
            new BehaviorId("sleep_state"),
            tick.SelectedBehaviorId!.Value);
        BehaviorTestCheck.True(
            tick.QueueResult?.Disposition is
                SoftQueueEnqueueDisposition.Enqueued or
                SoftQueueEnqueueDisposition.ReplacedActiveTick);
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);

        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyLoopBoundary(fallback));
        BehaviorTestCheck.Equal(
            new BehaviorId("sleep_state"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.Equal(
            "sleep_enter",
            fixture.Coordinator.Sessions.Snapshot!.ClipId);
    }

    private static void AutomaticWakeDuringSleepEnterStillUsesExit()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult sleep = fixture.Coordinator.TryStartImmediate(
            "sleep_state",
            BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(sleep.Accepted, sleep.RejectionReason);
        BehaviorPlaybackToken enter = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);

        BehaviorMutationResult wake = fixture.Coordinator.TriggerSoftTag(
            "trigger:time:morning",
            BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(wake.Accepted, wake.RejectionReason);
        BehaviorMutationResult duplicateWake =
            fixture.Coordinator.TriggerSoftTag(
                "trigger:time:morning",
                BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.False(duplicateWake.Accepted);
        BehaviorTestCheck.Equal(
            "automatic-wake-already-pending",
            duplicateWake.RejectionReason!);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(enter));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(enter));
        BehaviorTestCheck.Equal(
            "sleep_exit",
            fixture.Coordinator.Sessions.Snapshot!.ClipId);
        BehaviorTestCheck.False(
            fixture.Animation.Requests.Any(static request =>
                request.Role == BehaviorClipRole.Loop &&
                request.ClipId == "sleep_loop"));
    }

    private static void DailyOpportunityCountsOnlyAfterFirstFrame()
    {
        var fixture = CreateFixture();
        var opportunity = new BehaviorDailyOpportunity(
            "time:morning",
            new DateOnly(2026, 7, 28),
            new DateTimeOffset(
                2026,
                7,
                28,
                8,
                0,
                0,
                TimeSpan.FromHours(8)),
            AcceptedCount: 0,
            LastAcceptedAt: null);
        var presented = new List<BehaviorDailyOpportunity>();
        fixture.Coordinator.DailyOpportunityPresented += presented.Add;
        fixture.Coordinator.Start();
        BehaviorPlaybackToken fallback = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(fallback));

        BehaviorMutationResult queued =
            fixture.Coordinator.TriggerSoftTag(
                "trigger:time:morning",
                BehaviorRequestSource.Scheduled,
                TimeSpan.FromMinutes(30),
                opportunity);
        BehaviorTestCheck.True(queued.Accepted, queued.RejectionReason);
        BehaviorTestCheck.Equal(0, presented.Count);

        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyLoopBoundary(fallback));
        BehaviorPlaybackToken morning = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.Equal(0, presented.Count);

        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(morning));
        BehaviorTestCheck.Equal(1, presented.Count);
        BehaviorTestCheck.Equal(opportunity, presented.Single());
        BehaviorTestCheck.False(
            fixture.Coordinator.NotifyFirstFrame(morning));
        BehaviorTestCheck.Equal(1, presented.Count);
    }

    private static void DailyWeatherTagsShareOnePendingOpportunity()
    {
        IReadOnlyList<BehaviorDefinition> definitions =
        [
            .. Definitions(),
            Definition(
                "weather_clear",
                "blink",
                tags: ["trigger:weather:clear"],
                baseScore: 30),
            Definition(
                "weather_hot",
                "stretch",
                tags: ["trigger:weather:hot"],
                baseScore: 30),
        ];
        var fixture = CreateFixture(definitions);
        var opportunity = new BehaviorDailyOpportunity(
            "weather:daily",
            new DateOnly(2026, 7, 28),
            new DateTimeOffset(
                2026,
                7,
                28,
                12,
                0,
                0,
                TimeSpan.FromHours(8)),
            AcceptedCount: 0,
            LastAcceptedAt: null);
        var presented = new List<BehaviorDailyOpportunity>();
        fixture.Coordinator.DailyOpportunityPresented += presented.Add;
        fixture.Coordinator.Start();
        BehaviorPlaybackToken fallback = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(fallback));

        BehaviorMutationResult clear =
            fixture.Coordinator.TriggerSoftTag(
                "trigger:weather:clear",
                BehaviorRequestSource.Weather,
                TimeSpan.FromHours(6),
                opportunity);
        BehaviorMutationResult hot =
            fixture.Coordinator.TriggerSoftTag(
                "trigger:weather:hot",
                BehaviorRequestSource.Weather,
                TimeSpan.FromHours(6),
                opportunity with
                {
                    ObservedAt = opportunity.ObservedAt.AddMinutes(1),
                });
        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.Enqueued,
            clear.QueueResult!.Disposition);
        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.Refreshed,
            hot.QueueResult!.Disposition);
        BehaviorTestCheck.Equal(
            1,
            fixture.Coordinator.GetSnapshot().Queue.PendingCount);

        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyLoopBoundary(fallback));
        BehaviorPlaybackToken weather = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(weather));
        BehaviorTestCheck.Equal(1, presented.Count);
        BehaviorTestCheck.Equal(opportunity, presented.Single());
    }

    private static void DailyAutomaticWakePreservesMetadataAndExpiration()
    {
        var opportunity = new BehaviorDailyOpportunity(
            "time:morning",
            new DateOnly(2026, 7, 28),
            new DateTimeOffset(
                2026,
                7,
                28,
                8,
                0,
                0,
                TimeSpan.FromHours(8)),
            AcceptedCount: 0,
            LastAcceptedAt: null);

        var fixture = CreateFixture();
        var presented = new List<BehaviorDailyOpportunity>();
        fixture.Coordinator.DailyOpportunityPresented += presented.Add;
        fixture.Coordinator.Start();
        BehaviorMutationResult sleep =
            fixture.Coordinator.TryStartImmediate(
                "sleep_state",
                BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(sleep.Accepted, sleep.RejectionReason);
        BehaviorPlaybackToken enter = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorMutationResult wake =
            fixture.Coordinator.TriggerSoftTag(
                "trigger:time:morning",
                BehaviorRequestSource.Scheduled,
                TimeSpan.FromMinutes(1),
                opportunity);
        BehaviorTestCheck.True(wake.Accepted, wake.RejectionReason);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(enter));
        BehaviorTestCheck.Equal(0, presented.Count);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyClipCompleted(enter));
        BehaviorPlaybackToken exit = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(exit));
        BehaviorTestCheck.Equal(0, presented.Count);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyClipCompleted(exit));
        BehaviorPlaybackToken morning = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(morning));
        BehaviorTestCheck.Equal(1, presented.Count);
        BehaviorTestCheck.Equal(opportunity, presented.Single());

        var expiredFixture = CreateFixture();
        var expiredPresented = new List<BehaviorDailyOpportunity>();
        expiredFixture.Coordinator.DailyOpportunityPresented +=
            expiredPresented.Add;
        expiredFixture.Coordinator.Start();
        BehaviorMutationResult expiredSleep =
            expiredFixture.Coordinator.TryStartImmediate(
                "sleep_state",
                BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(
            expiredSleep.Accepted,
            expiredSleep.RejectionReason);
        BehaviorPlaybackToken expiredEnter = BehaviorTestCheck.NotNull(
            expiredFixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(
            expiredFixture.Coordinator.NotifyFirstFrame(expiredEnter));
        BehaviorMutationResult expiringWake =
            expiredFixture.Coordinator.TriggerSoftTag(
                "trigger:time:morning",
                BehaviorRequestSource.Scheduled,
                TimeSpan.FromSeconds(1),
                opportunity);
        BehaviorTestCheck.True(
            expiringWake.Accepted,
            expiringWake.RejectionReason);
        expiredFixture.Clock.Advance(TimeSpan.FromSeconds(2));
        BehaviorTestCheck.True(
            expiredFixture.Coordinator.NotifyClipCompleted(expiredEnter));
        BehaviorPlaybackToken expiredExit = BehaviorTestCheck.NotNull(
            expiredFixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(
            expiredFixture.Coordinator.NotifyFirstFrame(expiredExit));
        BehaviorTestCheck.True(
            expiredFixture.Coordinator.NotifyClipCompleted(expiredExit));
        BehaviorTestCheck.Equal(0, expiredPresented.Count);
        BehaviorTestCheck.False(
            expiredFixture.Coordinator.GetSnapshot()
                .Queue.Groups
                .SelectMany(static group => group.PendingItems)
                .Any(static request =>
                    request.DailyOpportunity is not null));
    }

    private static void AutomaticWakeDiscardsPendingSleepBeforeItStarts()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.UpdateContext(
            new BehaviorContextSnapshot
            {
                Revision = 1,
                LocalNow = new DateTimeOffset(
                    2026,
                    7,
                    28,
                    23,
                    30,
                    0,
                    TimeSpan.FromHours(8)),
            });
        fixture.Coordinator.Start();
        BehaviorPlaybackToken fallback = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(fallback));

        BehaviorMutationResult sleep = fixture.Coordinator.TriggerSoftTag(
            "trigger:time:late",
            BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(sleep.Accepted, sleep.RejectionReason);
        BehaviorMutationResult wake = fixture.Coordinator.TriggerSoftTag(
            "trigger:time:morning",
            BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(wake.Accepted, wake.RejectionReason);

        SoftBehaviorQueueSnapshot queued =
            fixture.Coordinator.GetSnapshot().Queue;
        BehaviorTestCheck.False(
            queued.Groups
                .SelectMany(static group => group.PendingItems)
                .Any(static request =>
                    request.BehaviorId == new BehaviorId("sleep_state")));
        BehaviorTestCheck.True(
            queued.RecentRemovals.Any(removal =>
                removal.Reason ==
                    SoftQueueRemovalReason.SupersededByAutomaticWake &&
                removal.BehaviorIds.Contains(
                    new BehaviorId("sleep_state"))));

        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyLoopBoundary(fallback));
        BehaviorTestCheck.Equal(
            new BehaviorId("morning_blink"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.Equal(
            StablePetState.Normal,
            fixture.Coordinator.StateMachine.StableState);
    }

    private static void AssertAutomaticWake(
        string tag,
        BehaviorRequestSource source,
        BehaviorId expectedBehaviorId)
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult sleep = fixture.Coordinator.TryStartImmediate(
            "sleep_state",
            BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(sleep.Accepted, sleep.RejectionReason);
        BehaviorPlaybackToken enter = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(enter));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(enter));
        BehaviorPlaybackToken loop = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(loop));

        BehaviorMutationResult wake =
            fixture.Coordinator.TriggerSoftTag(tag, source);
        BehaviorTestCheck.True(wake.Accepted, wake.RejectionReason);
        BehaviorTestCheck.Equal(
            "sleep_exit",
            fixture.Coordinator.Sessions.Snapshot!.ClipId);
        BehaviorTestCheck.False(
            fixture.Coordinator.ReadEventsAfter(0).Events.Any(entry =>
                entry.Kind == BehaviorEventKind.SessionStarted &&
                entry.BehaviorId == expectedBehaviorId));

        BehaviorPlaybackToken exit = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(exit));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(exit));
        BehaviorTestCheck.Equal(
            StablePetState.Normal,
            fixture.Coordinator.StateMachine.StableState);

        if (fixture.Coordinator.Sessions.Snapshot?.BehaviorId ==
            new BehaviorId("idle_fallback"))
        {
            fixture.Clock.Advance(SoftBehaviorQueue.InterGroupDelay);
            fixture.Coordinator.Advance();
            fixture.Coordinator.NotifyLoopBoundary(
                BehaviorTestCheck.NotNull(
                    fixture.Coordinator.Sessions.CurrentPlaybackToken));
        }

        BehaviorTestCheck.Equal(
            expectedBehaviorId,
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorRuntimeEvent[] events =
            [.. fixture.Coordinator.ReadEventsAfter(0).Events];
        BehaviorRuntimeEvent sleepTerminal = BehaviorTestCheck.NotNull(
            events.LastOrDefault(static entry =>
                entry.Kind == BehaviorEventKind.SessionTerminated &&
                entry.BehaviorId == new BehaviorId("sleep_state")));
        BehaviorRuntimeEvent wakeStarted = BehaviorTestCheck.NotNull(
            events.LastOrDefault(entry =>
                entry.Kind == BehaviorEventKind.SessionStarted &&
                entry.BehaviorId == expectedBehaviorId));
        BehaviorTestCheck.True(sleepTerminal.Sequence < wakeStarted.Sequence);
    }

    private static void SingleClickStartsAfterClassificationWindow()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorSessionToken fallback =
            fixture.Coordinator.Sessions.Snapshot!.SessionToken;

        BehaviorMutationResult pending = fixture.Coordinator.TriggerClick();

        BehaviorTestCheck.True(pending.Accepted, pending.RejectionReason);
        BehaviorTestCheck.Null(pending.BehaviorId);
        BehaviorTestCheck.Null(pending.RequestId);
        BehaviorTestCheck.Equal(
            fallback,
            fixture.Coordinator.Sessions.Snapshot!.SessionToken);

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(700));
        fixture.Coordinator.Advance();
        BehaviorTestCheck.Equal(
            fallback,
            fixture.Coordinator.Sessions.Snapshot!.SessionToken);

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
        fixture.Coordinator.Advance();
        BehaviorTestCheck.Equal(
            new BehaviorId("click_blink"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
    }

    private static void ThreeClicksWithinWindowStartRepeatImmediately()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();

        BehaviorMutationResult first = fixture.Coordinator.TriggerClick();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(200));
        BehaviorMutationResult second = fixture.Coordinator.TriggerClick();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(500));
        BehaviorMutationResult third = fixture.Coordinator.TriggerClick();

        BehaviorTestCheck.True(first.Accepted, first.RejectionReason);
        BehaviorTestCheck.Null(first.BehaviorId);
        BehaviorTestCheck.True(second.Accepted, second.RejectionReason);
        BehaviorTestCheck.Null(second.BehaviorId);
        BehaviorTestCheck.True(third.Accepted, third.RejectionReason);
        BehaviorTestCheck.Equal(
            new BehaviorId("repeat_feedback"),
            third.BehaviorId!.Value);
        BehaviorTestCheck.Equal(
            new BehaviorId("repeat_feedback"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
    }

    private static void ClickArrivingWhileBusyIsDroppedWithoutDeferredPlayback()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult longPress =
            fixture.Coordinator.TriggerLongPress();
        BehaviorTestCheck.True(longPress.Accepted, longPress.RejectionReason);

        BehaviorMutationResult dropped = fixture.Coordinator.TriggerClick();

        BehaviorTestCheck.False(dropped.Accepted);
        BehaviorTestCheck.Equal("behavior-busy", dropped.RejectionReason);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(701));
        fixture.Coordinator.Advance();
        BehaviorTestCheck.Equal(
            new BehaviorId("long_press_groom"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);

        CompleteCurrentPerform(fixture);
        fixture.Coordinator.Advance();
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.False(
            fixture.Coordinator.ReadEventsAfter(0).Events.Any(
                static entry =>
                    entry.Kind == BehaviorEventKind.RequestReceived &&
                    (entry.BehaviorId == new BehaviorId("click_blink") ||
                     entry.BehaviorId == new BehaviorId("repeat_feedback"))));
    }

    private static void DragCancelsPendingClickBurst()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult pending = fixture.Coordinator.TriggerClick();
        BehaviorTestCheck.True(pending.Accepted, pending.RejectionReason);

        BehaviorTestCheck.True(
            fixture.Coordinator.BeginDragging(out string? beginError),
            beginError);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(701));
        fixture.Coordinator.Advance();
        BehaviorTestCheck.True(
            fixture.Coordinator.CompleteDragging(out string? completeError),
            completeError);

        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.False(
            fixture.Coordinator.ReadEventsAfter(0).Events.Any(
                static entry =>
                    entry.Kind == BehaviorEventKind.RequestReceived &&
                    (entry.BehaviorId == new BehaviorId("click_blink") ||
                     entry.BehaviorId == new BehaviorId("repeat_feedback"))));
    }

    private static void RepeatedPettingIsDroppedUntilBehaviorCompletes()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult first =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorTestCheck.True(first.Accepted, first.RejectionReason);
        BehaviorPlaybackToken playback = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        int randomCalls = fixture.UtilityRandom.Calls;

        BehaviorMutationResult repeated =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorTestCheck.False(repeated.Accepted);
        BehaviorTestCheck.Equal("behavior-busy", repeated.RejectionReason);
        BehaviorTestCheck.Equal(randomCalls, fixture.UtilityRandom.Calls);

        fixture.Coordinator.NotifyFirstFrame(playback);
        fixture.Coordinator.NotifyClipCompleted(playback);
        BehaviorMutationResult next =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorTestCheck.True(next.Accepted, next.RejectionReason);
        BehaviorTestCheck.True(next.RequestId != first.RequestId);
    }

    private static void ToolbarActionSupersedesPointerInteraction()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult pointer =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorTestCheck.True(pointer.Accepted, pointer.RejectionReason);
        BehaviorSessionToken pointerSession = fixture.Coordinator
            .Sessions
            .Snapshot!
            .SessionToken;

        BehaviorMutationResult toolbar =
            fixture.Coordinator.TriggerToolbarInteractionTag("trigger:feed");

        BehaviorTestCheck.True(toolbar.Accepted, toolbar.RejectionReason);
        BehaviorTestCheck.Equal(
            new BehaviorId("care_feed_cat_treat"),
            toolbar.BehaviorId!.Value);
        BehaviorTestCheck.True(
            fixture.Coordinator.Sessions.Snapshot!.SessionToken !=
            pointerSession);
        BehaviorTestCheck.Equal(
            "trigger:feed",
            fixture.Coordinator.GetSnapshot().InteractionLease!.TriggerTag);
    }

    private static void ToolbarActionDoesNotSupersedeActiveCareInteraction()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult feed =
            fixture.Coordinator.TriggerToolbarInteractionTag("trigger:feed");
        BehaviorTestCheck.True(feed.Accepted, feed.RejectionReason);
        BehaviorSessionToken feedSession = fixture.Coordinator
            .Sessions
            .Snapshot!
            .SessionToken;

        BehaviorMutationResult petting =
            fixture.Coordinator.TriggerToolbarInteractionTag("trigger:petting");

        BehaviorTestCheck.False(petting.Accepted);
        BehaviorTestCheck.Equal("behavior-busy", petting.RejectionReason);
        BehaviorTestCheck.Equal(
            feedSession,
            fixture.Coordinator.Sessions.Snapshot!.SessionToken);
        BehaviorTestCheck.Equal(
            "trigger:feed",
            fixture.Coordinator.GetSnapshot().InteractionLease!.TriggerTag);
    }

    private static void ToolbarActionDoesNotSupersedeAnotherToolbarAction()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult first =
            fixture.Coordinator.TriggerToolbarInteractionTag("trigger:petting");
        BehaviorTestCheck.True(first.Accepted, first.RejectionReason);
        BehaviorSessionToken firstSession = fixture.Coordinator
            .Sessions
            .Snapshot!
            .SessionToken;

        BehaviorMutationResult feed =
            fixture.Coordinator.TriggerToolbarInteractionTag("trigger:feed");
        BehaviorMutationResult repeatedPetting =
            fixture.Coordinator.TriggerToolbarInteractionTag("trigger:petting");

        BehaviorTestCheck.False(feed.Accepted);
        BehaviorTestCheck.Equal("behavior-busy", feed.RejectionReason);
        BehaviorTestCheck.False(repeatedPetting.Accepted);
        BehaviorTestCheck.Equal(
            "behavior-busy",
            repeatedPetting.RejectionReason);
        BehaviorTestCheck.Equal(
            firstSession,
            fixture.Coordinator.Sessions.Snapshot!.SessionToken);
        BehaviorTestCheck.Equal(
            "trigger:petting",
            fixture.Coordinator.GetSnapshot().InteractionLease!.TriggerTag);
    }

    private static void SessionEventsExposeCommittedAndTerminatedRecords()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        var commits = new List<BehaviorSessionCommitRecord>();
        var terminals = new List<BehaviorTerminalRecord>();
        fixture.Coordinator.SessionCommitted += commits.Add;
        fixture.Coordinator.SessionTerminated += terminals.Add;

        BehaviorMutationResult result =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorTestCheck.True(result.Accepted, result.RejectionReason);
        BehaviorSessionSnapshot session = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.Snapshot);
        BehaviorPlaybackToken playback = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.Equal(0, commits.Count);

        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(playback));
        BehaviorSessionCommitRecord committed = commits.Single(record =>
            record.BehaviorId == new BehaviorId("petting_nuzzle"));
        BehaviorTestCheck.Equal(session.SessionToken, committed.SessionToken);
        BehaviorTestCheck.Equal(result.RequestId!.Value, committed.RequestId);

        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyClipCompleted(playback));
        BehaviorTerminalRecord terminal = terminals.Single(record =>
            record.BehaviorId == new BehaviorId("petting_nuzzle"));
        BehaviorTestCheck.Equal(committed.SessionToken, terminal.SessionToken);
        BehaviorTestCheck.Equal(committed.RequestId, terminal.RequestId);
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Completed,
            terminal.Status);
    }

    private static void InteractionTerminationExposesLeaseBeforeFirstFrame()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        fixture.Animation.RejectedClips.Add("groom");
        var terminations = new List<(
            BehaviorTerminalRecord Terminal,
            BehaviorInteractionLeaseSnapshot Lease)>();
        fixture.Coordinator.InteractionSessionTerminated +=
            (terminal, lease) => terminations.Add((terminal, lease));

        BehaviorMutationResult fallback =
            fixture.Coordinator.TriggerToolbarInteractionTag("trigger:feed");

        BehaviorTestCheck.True(fallback.Accepted, fallback.RejectionReason);
        BehaviorTestCheck.Equal(
            new BehaviorId("safe_micro_feedback"),
            fallback.BehaviorId!.Value);
        (BehaviorTerminalRecord terminal,
         BehaviorInteractionLeaseSnapshot lease) = terminations.Single();
        BehaviorTestCheck.Equal(
            new BehaviorId("care_feed_cat_treat"),
            terminal.BehaviorId);
        BehaviorTestCheck.Equal(terminal.RequestId, lease.RequestId);
        BehaviorTestCheck.Equal(terminal.BehaviorId, lease.BehaviorId);
        BehaviorTestCheck.Equal("trigger:feed", lease.TriggerTag);
        BehaviorTestCheck.Null(lease.SessionToken);
    }

    private static void PettingEndIsDroppedWhilePettingBehaviorRuns()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult petting =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorTestCheck.True(petting.Accepted, petting.RejectionReason);
        BehaviorPlaybackToken playback = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);

        BehaviorMutationResult during =
            fixture.Coordinator.TriggerInteractionTag("trigger:pet_end");
        BehaviorTestCheck.False(during.Accepted);
        BehaviorTestCheck.Equal("behavior-busy", during.RejectionReason);

        fixture.Coordinator.NotifyFirstFrame(playback);
        fixture.Coordinator.NotifyClipCompleted(playback);
        BehaviorMutationResult after =
            fixture.Coordinator.TriggerInteractionTag("trigger:pet_end");
        BehaviorTestCheck.True(after.Accepted, after.RejectionReason);
        BehaviorTestCheck.Equal(
            new BehaviorId("pet_end_feedback"),
            after.BehaviorId!.Value);
    }

    private static void DifferentInteractionIsDroppedWhileBehaviorRuns()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult petting =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorTestCheck.True(petting.Accepted, petting.RejectionReason);
        BehaviorSessionToken session =
            fixture.Coordinator.Sessions.Snapshot!.SessionToken;

        BehaviorMutationResult longPress =
            fixture.Coordinator.TriggerLongPress();
        BehaviorTestCheck.False(longPress.Accepted);
        BehaviorTestCheck.Equal("behavior-busy", longPress.RejectionReason);
        BehaviorTestCheck.Equal(
            session,
            fixture.Coordinator.Sessions.Snapshot!.SessionToken);
    }

    private static void ImmediateStartIsDroppedWhileBehaviorRuns()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult longPress =
            fixture.Coordinator.TriggerLongPress();
        BehaviorTestCheck.True(longPress.Accepted, longPress.RejectionReason);
        BehaviorSessionToken session =
            fixture.Coordinator.Sessions.Snapshot!.SessionToken;

        BehaviorMutationResult dropped =
            fixture.Coordinator.TryStartImmediate(
                new BehaviorId("active_stretch"),
                BehaviorRequestSource.TestControl);

        BehaviorTestCheck.False(dropped.Accepted);
        BehaviorTestCheck.Equal("behavior-busy", dropped.RejectionReason);
        BehaviorTestCheck.Equal(
            session,
            fixture.Coordinator.Sessions.Snapshot!.SessionToken);
        BehaviorTestCheck.Equal(
            0,
            fixture.Coordinator.GetSnapshot().Queue.PendingCount);
    }

    private static void RejectedInteractionStartReleasesLease()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        fixture.Animation.RejectedClips.Add("groom");
        fixture.Animation.RejectedClips.Add("blink");

        BehaviorMutationResult rejected =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorTestCheck.False(rejected.Accepted);
        BehaviorCoordinatorSnapshot failed = fixture.Coordinator.GetSnapshot();
        BehaviorTestCheck.Null(failed.InteractionLease);

        fixture.Animation.RejectedClips.Clear();
        BehaviorMutationResult retry =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorTestCheck.True(retry.Accepted, retry.RejectionReason);
        BehaviorTestCheck.Equal(
            "petting",
            fixture.Coordinator.GetSnapshot().InteractionLease!.Key);
    }

    private static void SynchronousInteractionDuringPlaybackStartIsDropped()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult? nested = null;
        fixture.Animation.PlaybackStarted = request =>
        {
            if (request.ClipId == "groom" && nested is null)
            {
                nested =
                    fixture.Coordinator.TriggerInteractionTag(
                        "trigger:petting");
            }
        };
        int playbackCount = fixture.Animation.Requests.Count;

        BehaviorMutationResult outer =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");

        BehaviorTestCheck.True(outer.Accepted, outer.RejectionReason);
        BehaviorMutationResult nestedResult =
            BehaviorTestCheck.NotNull(nested);
        BehaviorTestCheck.False(nestedResult.Accepted);
        BehaviorTestCheck.Equal(
            "behavior-busy",
            nestedResult.RejectionReason);
        BehaviorTestCheck.Null(nestedResult.RequestId);
        BehaviorTestCheck.Equal(
            playbackCount + 1,
            fixture.Animation.Requests.Count);
        BehaviorTestCheck.Equal(
            outer.RequestId!.Value,
            fixture.Coordinator.GetSnapshot()
                .InteractionLease!
                .RequestId);
    }

    private static void PausingInteractionReleasesLease()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult petting =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorTestCheck.True(petting.Accepted, petting.RejectionReason);
        BehaviorMutationResult dropped =
            fixture.Coordinator.TriggerInteractionTag("trigger:pet_end");
        BehaviorTestCheck.False(dropped.Accepted);
        BehaviorTestCheck.Equal("behavior-busy", dropped.RejectionReason);

        fixture.Coordinator.PauseForExternalControl();

        BehaviorTestCheck.Null(
            fixture.Coordinator.GetSnapshot().InteractionLease);
        BehaviorTestCheck.False(fixture.Coordinator.Sessions.HasActiveSession);
        fixture.Coordinator.ResumeAfterExternalControl();
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.False(
            fixture.Coordinator.ReadEventsAfter(0).Events.Any(
                static entry =>
                    entry.Kind == BehaviorEventKind.SessionStarted &&
                    entry.BehaviorId == new BehaviorId("pet_end_feedback")));
    }

    private static void ExternalPauseTransfersToTopLevelWithoutPlaybackGap()
    {
        var fixture = CreateFixture();
        BehaviorTestCheck.False(
            fixture.Coordinator
                .TryTransferExternalControlPauseToTopLevelPresentation());
        fixture.Coordinator.Start();
        BehaviorTestCheck.False(
            fixture.Coordinator
                .TryTransferExternalControlPauseToTopLevelPresentation());
        BehaviorPlaybackToken interruptedPlayback = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);

        fixture.Coordinator.PauseForExternalControl();
        int playbackCountAtPause = fixture.Animation.Requests.Count;
        int sessionStartsAtPause = fixture.Coordinator.ReadEventsAfter(0)
            .Events
            .Count(static entry =>
                entry.Kind == BehaviorEventKind.SessionStarted);

        BehaviorTestCheck.True(
            fixture.Coordinator
                .TryTransferExternalControlPauseToTopLevelPresentation());
        BehaviorTestCheck.False(
            fixture.Coordinator
                .TryTransferExternalControlPauseToTopLevelPresentation());
        BehaviorTestCheck.True(fixture.Coordinator.GetSnapshot().Paused);
        BehaviorTestCheck.False(fixture.Coordinator.Sessions.HasActiveSession);
        BehaviorTestCheck.False(
            fixture.Coordinator.NotifyFirstFrame(interruptedPlayback));
        BehaviorTestCheck.Equal(
            playbackCountAtPause,
            fixture.Animation.Requests.Count);
        BehaviorTestCheck.Equal(
            sessionStartsAtPause,
            fixture.Coordinator.ReadEventsAfter(0)
                .Events
                .Count(static entry =>
                    entry.Kind == BehaviorEventKind.SessionStarted));

        fixture.Coordinator.ResumeAfterExternalControl();

        BehaviorTestCheck.True(fixture.Coordinator.GetSnapshot().Paused);
        BehaviorTestCheck.False(fixture.Coordinator.Sessions.HasActiveSession);
        BehaviorTestCheck.Equal(
            playbackCountAtPause,
            fixture.Animation.Requests.Count);

        fixture.Coordinator.ResumeAfterTopLevelPresentation();

        BehaviorTestCheck.False(fixture.Coordinator.GetSnapshot().Paused);
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.Equal(
            playbackCountAtPause + 1,
            fixture.Animation.Requests.Count);
    }

    private static void SynchronousCompletionDoesNotLeaveInteractionLease()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        fixture.Animation.PlaybackStarted = request =>
        {
            if (request.ClipId != "groom")
            {
                return;
            }

            BehaviorTestCheck.True(
                fixture.Coordinator.NotifyFirstFrame(request.PlaybackToken));
            BehaviorTestCheck.True(
                fixture.Coordinator.NotifyClipCompleted(request.PlaybackToken));
        };

        BehaviorMutationResult completedSynchronously =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");

        BehaviorTestCheck.True(
            completedSynchronously.Accepted,
            completedSynchronously.RejectionReason);
        BehaviorTestCheck.Null(
            fixture.Coordinator.GetSnapshot().InteractionLease);
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorRuntimeEvent terminal = BehaviorTestCheck.NotNull(
            fixture.Coordinator.ReadEventsAfter(0).Events.LastOrDefault(
                entry =>
                    entry.Kind == BehaviorEventKind.SessionTerminated &&
                    entry.RequestId == completedSynchronously.RequestId));
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Completed,
            terminal.TerminalStatus!.Value);
    }

    private static void StartSuspendedWaitsForTopLevelPresentation()
    {
        var fixture = CreateFixture();

        fixture.Coordinator.StartSuspendedForTopLevelPresentation();

        BehaviorTestCheck.True(
            fixture.Coordinator.GetSnapshot().Paused);
        BehaviorTestCheck.False(
            fixture.Coordinator.Sessions.HasActiveSession);
        BehaviorTestCheck.Equal(0, fixture.Animation.Requests.Count);

        fixture.Coordinator.ResumeAfterTopLevelPresentation();

        BehaviorTestCheck.False(
            fixture.Coordinator.GetSnapshot().Paused);
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
    }

    private static void TopLevelPresentationWaitsForInteractionCompletion()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(
                BehaviorTestCheck.NotNull(
                    fixture.Coordinator.Sessions.CurrentPlaybackToken)));

        BehaviorMutationResult click = fixture.Coordinator.TriggerClick();
        BehaviorTestCheck.True(click.Accepted, click.RejectionReason);
        Task suspension =
            fixture.Coordinator.SuspendAfterCurrentInteractionAsync();
        BehaviorTestCheck.False(suspension.IsCompleted);
        ExpirePendingClick(fixture);
        int requestsBeforeCompletion = fixture.Animation.Requests.Count;

        CompleteCurrentPerform(fixture);

        BehaviorTestCheck.True(suspension.IsCompletedSuccessfully);
        BehaviorTestCheck.True(
            fixture.Coordinator.GetSnapshot().Paused);
        BehaviorTestCheck.False(
            fixture.Coordinator.Sessions.HasActiveSession);
        BehaviorTestCheck.Equal(
            requestsBeforeCompletion,
            fixture.Animation.Requests.Count);

        fixture.Coordinator.ResumeAfterTopLevelPresentation();
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
    }

    private static void TopLevelPresentationPendingRejectsNewDrag()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(
                BehaviorTestCheck.NotNull(
                    fixture.Coordinator.Sessions.CurrentPlaybackToken)));

        BehaviorMutationResult click = fixture.Coordinator.TriggerClick();
        BehaviorTestCheck.True(click.Accepted, click.RejectionReason);
        BehaviorSessionToken fallbackSession =
            fixture.Coordinator.Sessions.Snapshot!.SessionToken;
        Task suspension =
            fixture.Coordinator.SuspendAfterCurrentInteractionAsync();
        BehaviorTestCheck.False(suspension.IsCompleted);

        BehaviorTestCheck.False(
            fixture.Coordinator.BeginDragging(out string? rejectionReason));
        BehaviorTestCheck.Equal(
            "top-level-presentation-pending",
            rejectionReason!);
        BehaviorTestCheck.Equal(
            StablePetState.Normal,
            fixture.Coordinator.StateMachine.StableState);
        BehaviorTestCheck.Equal(
            fallbackSession,
            fixture.Coordinator.Sessions.Snapshot!.SessionToken);
        BehaviorTestCheck.False(suspension.IsCompleted);

        ExpirePendingClick(fixture);
        CompleteCurrentPerform(fixture);

        BehaviorTestCheck.True(suspension.IsCompletedSuccessfully);
        BehaviorTestCheck.True(
            fixture.Coordinator.GetSnapshot().Paused);
        BehaviorTestCheck.False(
            fixture.Coordinator.Sessions.HasActiveSession);
    }

    private static void TopLevelPresentationPendingRejectsDirectMutations()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(
                BehaviorTestCheck.NotNull(
                    fixture.Coordinator.Sessions.CurrentPlaybackToken)));

        BehaviorMutationResult click = fixture.Coordinator.TriggerClick();
        BehaviorTestCheck.True(click.Accepted, click.RejectionReason);
        BehaviorSessionToken fallbackSession =
            fixture.Coordinator.Sessions.Snapshot!.SessionToken;
        Task suspension =
            fixture.Coordinator.SuspendAfterCurrentInteractionAsync();
        BehaviorTestCheck.False(suspension.IsCompleted);

        BehaviorMutationResult preview =
            fixture.Coordinator.StartManualPreview(
                new BehaviorId("active_blink"));
        BehaviorTestCheck.False(preview.Accepted);
        BehaviorTestCheck.Equal(
            "top-level-presentation-pending",
            preview.RejectionReason!);

        BehaviorMutationResult immediate =
            fixture.Coordinator.TryStartImmediate(
                new BehaviorId("active_stretch"),
                BehaviorRequestSource.TestControl);
        BehaviorTestCheck.False(immediate.Accepted);
        BehaviorTestCheck.Equal(
            "top-level-presentation-pending",
            immediate.RejectionReason!);

        BehaviorMutationResult repeatedClick =
            fixture.Coordinator.TriggerClick();
        BehaviorTestCheck.False(repeatedClick.Accepted);
        BehaviorTestCheck.Equal(
            "top-level-presentation-pending",
            repeatedClick.RejectionReason!);

        BehaviorMutationResult longPress =
            fixture.Coordinator.TriggerLongPress();
        BehaviorTestCheck.False(longPress.Accepted);
        BehaviorTestCheck.Equal(
            "top-level-presentation-pending",
            longPress.RejectionReason!);

        BehaviorMutationResult pointerTag =
            fixture.Coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorTestCheck.False(pointerTag.Accepted);
        BehaviorTestCheck.Equal(
            "top-level-presentation-pending",
            pointerTag.RejectionReason!);
        BehaviorTestCheck.Equal(
            fallbackSession,
            fixture.Coordinator.Sessions.Snapshot!.SessionToken);
        BehaviorTestCheck.False(suspension.IsCompleted);

        ExpirePendingClick(fixture);
        CompleteCurrentPerform(fixture);

        BehaviorTestCheck.True(suspension.IsCompletedSuccessfully);
        BehaviorTestCheck.True(
            fixture.Coordinator.GetSnapshot().Paused);
        BehaviorTestCheck.False(
            fixture.Coordinator.Sessions.HasActiveSession);
    }

    private static void TopLevelPresentationWaitsForSleepExitAndFeedback()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult sleep = fixture.Coordinator.TryStartImmediate(
            new BehaviorId("sleep_state"),
            BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(sleep.Accepted, sleep.RejectionReason);

        BehaviorPlaybackToken enter = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(enter));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(enter));
        BehaviorPlaybackToken loop = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(loop));

        BehaviorMutationResult click = fixture.Coordinator.TriggerClick();
        BehaviorTestCheck.True(click.Accepted, click.RejectionReason);
        Task suspension =
            fixture.Coordinator.SuspendAfterCurrentInteractionAsync();
        BehaviorTestCheck.False(suspension.IsCompleted);

        ExpirePendingClick(fixture);
        BehaviorPlaybackToken exit = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(exit));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(exit));
        BehaviorTestCheck.False(suspension.IsCompleted);
        BehaviorTestCheck.Equal(
            new BehaviorId("click_blink"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);

        CompleteCurrentPerform(fixture);

        BehaviorTestCheck.True(suspension.IsCompletedSuccessfully);
        BehaviorTestCheck.True(
            fixture.Coordinator.GetSnapshot().Paused);
        BehaviorTestCheck.False(
            fixture.Coordinator.Sessions.HasActiveSession);
    }

    private static void PublicImmediateStartRejectsInteractionSourceWithoutLeaseMetadata()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorSessionToken fallbackSession =
            fixture.Coordinator.Sessions.Snapshot!.SessionToken;

        BehaviorMutationResult rejected = fixture.Coordinator.TryStartImmediate(
            new BehaviorId("click_blink"),
            BehaviorRequestSource.Interaction);

        BehaviorTestCheck.False(rejected.Accepted);
        BehaviorTestCheck.Equal(
            "interaction-source-requires-trigger",
            rejected.RejectionReason!);
        BehaviorTestCheck.Equal(
            fallbackSession,
            fixture.Coordinator.Sessions.Snapshot!.SessionToken);
        BehaviorTestCheck.Null(
            fixture.Coordinator.GetSnapshot().InteractionLease);
    }

    private static void SleepingClickPlaysExitBeforeFeedback()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult sleep = fixture.Coordinator.TryStartImmediate(
            new BehaviorId("sleep_state"),
            BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(sleep.Accepted, sleep.RejectionReason);

        BehaviorPlaybackToken enter = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(enter));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(enter));
        BehaviorPlaybackToken loop = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(loop));
        BehaviorTestCheck.Equal(
            StablePetState.Sleeping,
            fixture.Coordinator.StateMachine.StableState);

        BehaviorMutationResult click = fixture.Coordinator.TriggerClick();
        BehaviorTestCheck.True(click.Accepted, click.RejectionReason);
        ExpirePendingClick(fixture);
        BehaviorSessionSnapshot exiting = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.Snapshot);
        BehaviorTestCheck.Equal(new BehaviorId("sleep_state"), exiting.BehaviorId);
        BehaviorTestCheck.Equal(BehaviorPhase.Exiting, exiting.Phase);
        BehaviorTestCheck.Equal("sleep_exit", exiting.ClipId!);
        BehaviorTestCheck.Equal(
            BehaviorClipRole.Exit,
            fixture.Animation.Requests[^1].Role);

        BehaviorPlaybackToken exit = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(exit));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(exit));
        BehaviorTestCheck.Equal(
            StablePetState.Normal,
            fixture.Coordinator.StateMachine.StableState);
        BehaviorTestCheck.Equal(
            new BehaviorId("click_blink"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.Equal(
            BehaviorClipRole.Perform,
            fixture.Animation.Requests[^1].Role);
    }

    private static void SleepingCareBindsLeaseToTheActualCareSession()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult sleep = fixture.Coordinator.TryStartImmediate(
            new BehaviorId("sleep_state"),
            BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(sleep.Accepted, sleep.RejectionReason);

        BehaviorPlaybackToken enter = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(enter));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(enter));
        BehaviorPlaybackToken loop = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(loop));

        BehaviorMutationResult wake =
            fixture.Coordinator.TriggerInteractionTag("trigger:feed");
        BehaviorTestCheck.True(wake.Accepted, wake.RejectionReason);
        BehaviorPlaybackToken exit = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(exit));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(exit));

        BehaviorSessionSnapshot careSession = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.Snapshot);
        BehaviorInteractionLeaseSnapshot lease = BehaviorTestCheck.NotNull(
            fixture.Coordinator.GetSnapshot().InteractionLease);
        BehaviorTestCheck.Equal(
            new BehaviorId("care_feed_cat_treat"),
            careSession.BehaviorId);
        BehaviorTestCheck.Equal("trigger:feed", lease.TriggerTag);
        BehaviorTestCheck.Equal(careSession.BehaviorId, lease.BehaviorId);
        BehaviorTestCheck.Equal(careSession.SessionToken, lease.SessionToken!);
        BehaviorTestCheck.True(lease.RequestId > 0);

        var commits = new List<BehaviorSessionCommitRecord>();
        fixture.Coordinator.SessionCommitted += commits.Add;
        BehaviorPlaybackToken carePlayback = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(carePlayback));
        BehaviorSessionCommitRecord committed = commits.Single(record =>
            record.BehaviorId == careSession.BehaviorId);
        BehaviorTestCheck.Equal(lease.RequestId, committed.RequestId);
        BehaviorTestCheck.Equal(
            lease.SessionToken!,
            committed.SessionToken);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyClipCompleted(carePlayback));
    }

    private static void LongPressUsesTaggedBehavior()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();

        BehaviorMutationResult result = fixture.Coordinator.TriggerLongPress();

        BehaviorTestCheck.True(result.Accepted, result.RejectionReason);
        BehaviorTestCheck.Equal(
            new BehaviorId("long_press_groom"),
            result.BehaviorId!.Value);
        BehaviorTestCheck.Equal(
            new BehaviorId("long_press_groom"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
    }

    private static void SleepingLongPressPlaysExitBeforeTaggedFeedback()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult sleep = fixture.Coordinator.TryStartImmediate(
            new BehaviorId("sleep_state"),
            BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(sleep.Accepted, sleep.RejectionReason);

        BehaviorPlaybackToken enter = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(enter));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(enter));
        BehaviorPlaybackToken loop = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(loop));

        BehaviorMutationResult longPress =
            fixture.Coordinator.TriggerLongPress();
        BehaviorTestCheck.True(longPress.Accepted, longPress.RejectionReason);
        BehaviorSessionSnapshot exiting = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.Snapshot);
        BehaviorTestCheck.Equal(new BehaviorId("sleep_state"), exiting.BehaviorId);
        BehaviorTestCheck.Equal("sleep_exit", exiting.ClipId!);
        BehaviorTestCheck.False(
            fixture.Coordinator.ReadEventsAfter(0).Events.Any(
                static entry =>
                    entry.Kind == BehaviorEventKind.SessionStarted &&
                    entry.BehaviorId == new BehaviorId("long_press_groom")));

        BehaviorPlaybackToken exit = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(fixture.Coordinator.NotifyFirstFrame(exit));
        BehaviorTestCheck.True(fixture.Coordinator.NotifyClipCompleted(exit));

        BehaviorTestCheck.Equal(
            StablePetState.Normal,
            fixture.Coordinator.StateMachine.StableState);
        BehaviorTestCheck.Equal(
            new BehaviorId("long_press_groom"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);

        BehaviorRuntimeEvent[] events =
            [.. fixture.Coordinator.ReadEventsAfter(0).Events];
        BehaviorRuntimeEvent sleepTerminal = BehaviorTestCheck.NotNull(
            events.LastOrDefault(static entry =>
                entry.Kind == BehaviorEventKind.SessionTerminated &&
                entry.BehaviorId == new BehaviorId("sleep_state")));
        BehaviorRuntimeEvent longPressStarted = BehaviorTestCheck.NotNull(
            events.LastOrDefault(static entry =>
                entry.Kind == BehaviorEventKind.SessionStarted &&
                entry.BehaviorId == new BehaviorId("long_press_groom")));
        BehaviorTestCheck.True(
            sleepTerminal.Sequence < longPressStarted.Sequence);
    }

    private static void DragReleaseUsesTaggedBehavior()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorTestCheck.True(
            fixture.Coordinator.BeginDragging(out string? beginError),
            beginError);

        BehaviorMutationResult result = fixture.Coordinator.TriggerDragRelease();

        BehaviorTestCheck.True(result.Accepted, result.RejectionReason);
        BehaviorTestCheck.Equal(
            new BehaviorId("drag_release_stretch"),
            result.BehaviorId!.Value);
        BehaviorTestCheck.Equal(
            StablePetState.Normal,
            fixture.Coordinator.StateMachine.StableState);
        BehaviorTestCheck.Equal(
            new BehaviorId("drag_release_stretch"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
    }

    private static void PausedDragReleaseClosesGateWithoutStartingBehavior()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorTestCheck.True(
            fixture.Coordinator.BeginDragging(out string? beginError),
            beginError);
        int playbackCountBeforePause = fixture.Animation.Requests.Count;

        fixture.Coordinator.PauseForExternalControl();
        BehaviorMutationResult release =
            fixture.Coordinator.TriggerDragRelease();

        BehaviorTestCheck.False(release.Accepted);
        BehaviorTestCheck.Equal("runtime-paused", release.RejectionReason);
        BehaviorTestCheck.Equal(
            playbackCountBeforePause,
            fixture.Animation.Requests.Count);
        BehaviorCoordinatorSnapshot paused =
            fixture.Coordinator.GetSnapshot();
        BehaviorTestCheck.Equal(
            StablePetState.Normal,
            paused.State.StableState);
        BehaviorTestCheck.False(fixture.Coordinator.Sessions.HasActiveSession);

        fixture.Coordinator.ResumeAfterExternalControl();
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
    }

    private static void DraggingIsAStableStateAndDefersQueuedWork()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorTestCheck.True(
            fixture.Coordinator.BeginDragging(out var beginError),
            beginError);
        BehaviorTestCheck.Equal(
            StablePetState.Dragging,
            fixture.Coordinator.StateMachine.StableState);
        BehaviorTestCheck.False(fixture.Coordinator.Sessions.HasActiveSession);

        BehaviorMutationResult queued = fixture.Coordinator.EnqueueSoft(
            new BehaviorId("active_blink"),
            BehaviorRequestSource.UserContext);
        BehaviorTestCheck.True(queued.Accepted);
        fixture.Coordinator.Advance();
        BehaviorTestCheck.False(fixture.Coordinator.Sessions.HasActiveSession);

        BehaviorTestCheck.True(
            fixture.Coordinator.CompleteDragging(out var completeError),
            completeError);
        BehaviorTestCheck.Equal(
            StablePetState.Normal,
            fixture.Coordinator.StateMachine.StableState);
        BehaviorTestCheck.Equal(
            new BehaviorId("active_blink"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
    }

    private static void DragInterruptsCurrentBehaviorAndReleasePrecedesQueue()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult longPress =
            fixture.Coordinator.TriggerLongPress();
        BehaviorTestCheck.True(longPress.Accepted);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(
                BehaviorTestCheck.NotNull(
                    fixture.Coordinator.Sessions.CurrentPlaybackToken)));
        BehaviorTestCheck.Equal(
            1,
            fixture.Coordinator.GetSnapshot().UtilityHistory.CommitCount);

        BehaviorTestCheck.True(
            fixture.Coordinator.BeginDragging(out var beginError),
            beginError);
        BehaviorTestCheck.False(fixture.Coordinator.Sessions.HasActiveSession);
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Interrupted,
            fixture.Coordinator.GetSnapshot().LastTerminal!.Status);

        BehaviorMutationResult queued = fixture.Coordinator.EnqueueSoft(
            new BehaviorId("active_blink"),
            BehaviorRequestSource.UserContext);
        BehaviorTestCheck.True(queued.Accepted);

        BehaviorMutationResult release =
            fixture.Coordinator.CompleteDraggingAndStart(
                new BehaviorId("active_stretch"),
                BehaviorRequestSource.Interaction);
        BehaviorTestCheck.True(release.Accepted, release.RejectionReason);
        BehaviorTestCheck.Equal(
            new BehaviorId("active_stretch"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.Equal(
            1,
            fixture.Coordinator.GetSnapshot().Queue.PendingCount);
        BehaviorTestCheck.Equal(
            1,
            fixture.Coordinator.GetSnapshot().UtilityHistory.CommitCount);

        BehaviorPlaybackToken stretch =
            BehaviorTestCheck.NotNull(
                fixture.Coordinator.Sessions.CurrentPlaybackToken);
        fixture.Coordinator.NotifyFirstFrame(stretch);
        BehaviorTestCheck.Equal(
            2,
            fixture.Coordinator.GetSnapshot().UtilityHistory.CommitCount);
        fixture.Coordinator.NotifyClipCompleted(stretch);

        BehaviorTestCheck.Equal(
            new BehaviorId("active_blink"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
    }

    private static void FullscreenKeepsInteractionsAndFiltersAutomaticBehavior()
    {
        var definitions = new List<BehaviorDefinition>
        {
            Definition(
                "idle_fallback",
                "idle",
                tags: ["state:fallback"],
                queueable: false,
                fallback: true,
                completion: new BehaviorCompletionPolicy
                {
                    Kind = BehaviorCompletionKind.External,
                },
                maximumDuration: TimeSpan.FromDays(1)),
            Definition(
                "fullscreen_automatic",
                "stretch",
                tags: ["trigger:active_tick"],
                baseScore: 100,
                disturbance: BehaviorDisturbanceLevel.Strong),
            Definition(
                "safe_micro_feedback",
                "blink",
                tags: ["fallback:interaction"],
                baseScore: 1),
        };
        definitions.AddRange(
            Enumerable.Range(1, 5)
                .Select(
                    index => Definition(
                        $"fullscreen_click_{index}",
                        "groom",
                        tags: ["trigger:click"],
                        baseScore: 30,
                        disturbance: BehaviorDisturbanceLevel.Strong)));

        var fixture = CreateFixture(definitions);
        fixture.Coordinator.UpdateContext(
            new BehaviorContextSnapshot
            {
                Revision = 2,
                LocalNow = new DateTimeOffset(
                    2026,
                    7,
                    28,
                    12,
                    0,
                    0,
                    TimeSpan.FromHours(8)),
                IsFullscreen = true,
            });
        fixture.Coordinator.Start();

        BehaviorTickResult automatic =
            fixture.Coordinator.ForceActiveTick(deterministicSeed: 4);
        BehaviorTestCheck.Null(automatic.SelectedBehaviorId);
        BehaviorEvaluation automaticEvaluation =
            automatic.Evaluation.AllEvaluations.Single();
        BehaviorTestCheck.False(automaticEvaluation.IsEligible);
        BehaviorTestCheck.Equal(
            "fullscreen-disturbance",
            automaticEvaluation.RejectionReason!);

        BehaviorMutationResult click = fixture.Coordinator.TriggerClick();
        BehaviorTestCheck.True(click.Accepted, click.RejectionReason);
        ExpirePendingClick(fixture);

        BehaviorId selected =
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId;
        BehaviorTestCheck.True(
            Enumerable.Range(1, 5)
                .Select(index => new BehaviorId($"fullscreen_click_{index}"))
                .Contains(selected),
            $"Expected one of five fullscreen click behaviors, got '{selected}'.");
    }

    private static void PausedSleepingStateResumesDirectlyAtLoop()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorMutationResult sleep = fixture.Coordinator.TryStartImmediate(
            new BehaviorId("sleep_state"),
            BehaviorRequestSource.Scheduled);
        BehaviorTestCheck.True(sleep.Accepted, sleep.RejectionReason);

        BehaviorPlaybackToken enter =
            BehaviorTestCheck.NotNull(
                fixture.Coordinator.Sessions.CurrentPlaybackToken);
        fixture.Coordinator.NotifyFirstFrame(enter);
        fixture.Coordinator.NotifyClipCompleted(enter);
        BehaviorPlaybackToken loop =
            BehaviorTestCheck.NotNull(
                fixture.Coordinator.Sessions.CurrentPlaybackToken);
        fixture.Coordinator.NotifyFirstFrame(loop);
        BehaviorTestCheck.Equal(
            StablePetState.Sleeping,
            fixture.Coordinator.StateMachine.StableState);
        BehaviorTestCheck.Equal(1, fixture.AnimationVariantRandom.Calls);

        fixture.Coordinator.PauseForExternalControl();
        BehaviorTestCheck.False(fixture.Coordinator.Sessions.HasActiveSession);
        BehaviorTestCheck.Equal(
            StablePetState.Sleeping,
            fixture.Coordinator.StateMachine.StableState);

        fixture.Coordinator.ResumeAfterExternalControl();
        BehaviorSessionSnapshot resumed =
            BehaviorTestCheck.NotNull(
                fixture.Coordinator.Sessions.Snapshot);
        BehaviorTestCheck.Equal(
            new BehaviorId("sleep_state"),
            resumed.BehaviorId);
        BehaviorTestCheck.Equal("sleep_loop", resumed.ClipId);
        BehaviorTestCheck.Equal(
            BehaviorClipRole.Loop,
            fixture.Animation.Requests[^1].Role);
        BehaviorTestCheck.Equal(
            1,
            fixture.Animation.Requests.Count(static request =>
                request.Role == BehaviorClipRole.Enter));
        BehaviorTestCheck.Equal(1, fixture.AnimationVariantRandom.Calls);
    }

    private static void StaleDragReleaseRecoversQueueAndState()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorTestCheck.True(
            fixture.Coordinator.BeginDragging(out var beginError),
            beginError);
        BehaviorMutationResult queued = fixture.Coordinator.EnqueueSoft(
            new BehaviorId("active_blink"),
            BehaviorRequestSource.UserContext);
        BehaviorTestCheck.True(queued.Accepted);

        fixture.Coordinator.StateMachine.ForceNormal();
        BehaviorMutationResult release =
            fixture.Coordinator.CompleteDraggingAndStart(
                new BehaviorId("active_stretch"),
                BehaviorRequestSource.Interaction);

        BehaviorTestCheck.False(release.Accepted);
        BehaviorTestCheck.Equal(
            "stale-drag-transition",
            release.RejectionReason);
        BehaviorCoordinatorSnapshot recovered =
            fixture.Coordinator.GetSnapshot();
        BehaviorTestCheck.Equal(
            StablePetState.Normal,
            recovered.State.StableState);
        BehaviorTestCheck.Equal(
            new BehaviorId("active_blink"),
            recovered.Session!.BehaviorId);
    }

    private static void SeededTickIsRepeatableWithoutChangingRuntimeRandomStream()
    {
        var first = CreateFixture();
        first.Coordinator.Start();
        BehaviorTickResult firstTick =
            first.Coordinator.ForceActiveTick(deterministicSeed: 123);

        var second = CreateFixture();
        second.Coordinator.Start();
        BehaviorTickResult secondTick =
            second.Coordinator.ForceActiveTick(deterministicSeed: 123);

        BehaviorTestCheck.Equal(
            firstTick.SelectedBehaviorId,
            secondTick.SelectedBehaviorId);
        BehaviorTestCheck.SequenceEqual(
            firstTick.AttemptOrder,
            secondTick.AttemptOrder);
        BehaviorTestCheck.Equal(0, first.UtilityRandom.Calls);
        BehaviorTestCheck.Equal(0, second.UtilityRandom.Calls);
    }

    private static void ActiveTickPublishesMatchingTrace()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorTickTrace? published = null;
        fixture.Coordinator.ActiveTickEvaluated += trace => published = trace;

        BehaviorTickResult result =
            fixture.Coordinator.ForceActiveTick(deterministicSeed: 42);

        BehaviorTickTrace trace = BehaviorTestCheck.NotNull(published);
        BehaviorTestCheck.True(ReferenceEquals(result, trace.Result));
        BehaviorTestCheck.Equal(1L, trace.Context.Revision);
        BehaviorTestCheck.Equal(
            fixture.Coordinator.GetSnapshot().State.StableState,
            trace.State.StableState);
        BehaviorTestCheck.Equal(
            result.SelectedBehaviorId,
            trace.Result.SelectedBehaviorId);
        BehaviorTestCheck.SequenceEqual(
            result.AttemptOrder,
            trace.Result.AttemptOrder);
    }

    private static void SynchronousFirstFrameKeepsJournalCausalOrder()
    {
        var fixture = CreateFixture();
        fixture.Animation.PlaybackStarted = request =>
            fixture.Coordinator.NotifyFirstFrame(request.PlaybackToken);
        fixture.Coordinator.Start();

        BehaviorRuntimeEvent[] events =
            [.. fixture.Coordinator.ReadEventsAfter(0).Events];
        BehaviorRuntimeEvent started = BehaviorTestCheck.NotNull(
            events.FirstOrDefault(static entry =>
                entry.Kind == BehaviorEventKind.SessionStarted));
        BehaviorRuntimeEvent firstFrame = BehaviorTestCheck.NotNull(
            events.FirstOrDefault(static entry =>
                entry.Kind == BehaviorEventKind.FirstFramePresented));
        BehaviorTestCheck.Equal(started.SessionToken, firstFrame.SessionToken);
        BehaviorTestCheck.True(started.Sequence < firstFrame.Sequence);
    }

    private static void FallbackJournalStartsWithRequestReceived()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();

        BehaviorRuntimeEvent[] events =
            [.. fixture.Coordinator.ReadEventsAfter(0).Events];
        BehaviorRuntimeEvent received = BehaviorTestCheck.NotNull(
            events.FirstOrDefault(static entry =>
                entry.Kind == BehaviorEventKind.RequestReceived &&
                entry.BehaviorId == new BehaviorId("idle_fallback")));
        BehaviorRuntimeEvent started = BehaviorTestCheck.NotNull(
            events.FirstOrDefault(static entry =>
                entry.Kind == BehaviorEventKind.SessionStarted &&
                entry.BehaviorId == new BehaviorId("idle_fallback")));

        BehaviorTestCheck.Equal(received.RequestId, started.RequestId);
        BehaviorTestCheck.True(received.Sequence < started.Sequence);
    }

    private static void ManualPreviewInterruptsAndBypassesNormalConditions()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();

        BehaviorMutationResult first = fixture.Coordinator.StartManualPreview(
            new BehaviorId("active_blink"));
        BehaviorTestCheck.True(first.Accepted, first.RejectionReason);
        BehaviorTestCheck.Equal(
            new BehaviorId("active_blink"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.Equal(
            BubbleRequestKind.ManualPreview,
            fixture.Coordinator.GetBubbleKind(
                fixture.Coordinator.Sessions.Snapshot.SessionToken));
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(
                BehaviorTestCheck.NotNull(
                    fixture.Coordinator.Sessions.CurrentPlaybackToken)));
        BehaviorTestCheck.Equal(
            0,
            fixture.Coordinator.GetSnapshot().UtilityHistory.CommitCount);

        BehaviorMutationResult second = fixture.Coordinator.StartManualPreview(
            new BehaviorId("active_stretch"));
        BehaviorTestCheck.True(second.Accepted, second.RejectionReason);
        BehaviorTestCheck.Equal(
            new BehaviorId("active_stretch"),
            fixture.Coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Superseded,
            fixture.Coordinator.GetSnapshot().LastTerminal!.Status);
    }

    private static void ManualPreviewSynthesizesSleepingForWakeBehavior()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();

        BehaviorMutationResult wake = fixture.Coordinator.StartManualPreview(
            new BehaviorId("wake_preview"));
        BehaviorTestCheck.True(wake.Accepted, wake.RejectionReason);
        BehaviorTestCheck.Equal(
            StablePetState.Sleeping,
            fixture.Coordinator.StateMachine.StableState);
        BehaviorTestCheck.Equal(
            "sleep_exit",
            fixture.Coordinator.Sessions.Snapshot!.ClipId);
    }

    private static void ManualPreviewDoesNotInterruptDragging()
    {
        var fixture = CreateFixture();
        fixture.Coordinator.Start();
        BehaviorTestCheck.True(
            fixture.Coordinator.BeginDragging(out string? reason),
            reason);

        BehaviorMutationResult preview =
            fixture.Coordinator.StartManualPreview(
                new BehaviorId("active_blink"));
        BehaviorTestCheck.False(preview.Accepted);
        BehaviorTestCheck.Equal("dragging", preview.RejectionReason!);
        BehaviorTestCheck.Equal(
            StablePetState.Dragging,
            fixture.Coordinator.StateMachine.StableState);
    }

    private static void ShortTermEmotionsUseMonotonicRegressionAndIgnoreRollback()
    {
        var initial = new EmotionState
        {
            Energy = 100,
            Sleepiness = 0,
            Boredom = 100,
            Curiosity = 0,
            Happiness = 100,
            Stress = 100,
            Affection = 82,
        };
        var fixture = CreateFixture(initialEmotions: initial);
        TimeSpan halfLife = EmotionState.ShortTermRegressionHalfLife;

        fixture.Clock.Advance(halfLife);
        EmotionState halfway = fixture.Coordinator.GetSnapshot().Emotions;
        BehaviorTestCheck.Close(82.5, halfway.Energy);
        BehaviorTestCheck.Close(10, halfway.Sleepiness);
        BehaviorTestCheck.Close(57.5, halfway.Boredom);
        BehaviorTestCheck.Close(25, halfway.Curiosity);
        BehaviorTestCheck.Close(80, halfway.Happiness);
        BehaviorTestCheck.Close(55, halfway.Stress);
        BehaviorTestCheck.Close(82, halfway.Affection);
        BehaviorTestCheck.Equal(
            RelationshipStages.Family,
            fixture.Coordinator.GetSnapshot().RelationshipStage);

        fixture.Clock.SetMonotonicForTest(TimeSpan.FromMinutes(5));
        EmotionState afterRollback =
            fixture.Coordinator.GetSnapshot().Emotions;
        AssertEmotionsEqual(halfway, afterRollback);

        fixture.Clock.SetMonotonicForTest(halfLife + halfLife);
        EmotionState resumed = fixture.Coordinator.GetSnapshot().Emotions;
        BehaviorTestCheck.Close(73.75, resumed.Energy);
        BehaviorTestCheck.Close(15, resumed.Sleepiness);
        BehaviorTestCheck.Close(36.25, resumed.Boredom);
        BehaviorTestCheck.Close(37.5, resumed.Curiosity);
        BehaviorTestCheck.Close(70, resumed.Happiness);
        BehaviorTestCheck.Close(32.5, resumed.Stress);
        BehaviorTestCheck.Close(82, resumed.Affection);
    }

    private static void AffectionEffectsPublishStableRelationshipStage()
    {
        BehaviorDefinition bonding = Definition(
            "bonding",
            "groom",
            tags: ["trigger:bonding"],
            startedEmotionEffect: new EmotionDelta(Affection: 35));
        var fixture = CreateFixture(
            [.. Definitions(), bonding],
            new EmotionState
            {
                Affection = EmotionState.DefaultAffection,
            });
        double? observedAffection = null;
        fixture.Coordinator.AffectionChanged +=
            affection => observedAffection = affection;
        fixture.Coordinator.Start();

        BehaviorMutationResult result =
            fixture.Coordinator.TryStartImmediate(
                bonding.Id,
                BehaviorRequestSource.TestControl);

        BehaviorTestCheck.True(result.Accepted, result.RejectionReason);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(
                BehaviorTestCheck.NotNull(
                    fixture.Coordinator.Sessions.CurrentPlaybackToken)));
        BehaviorTestCheck.Close(
            75,
            observedAffection
            ?? throw new InvalidOperationException(
                "Expected an affection change notification."));
        BehaviorTestCheck.Close(
            75,
            fixture.Coordinator.GetSnapshot().Emotions.Affection);
        BehaviorTestCheck.Equal(
            RelationshipStages.Family,
            fixture.Coordinator.RelationshipStage);
        BehaviorTestCheck.Equal(
            RelationshipStages.Family,
            fixture.Coordinator.GetSnapshot().RelationshipStage);
    }

    private static void AssertEmotionsEqual(
        EmotionState expected,
        EmotionState actual)
    {
        BehaviorTestCheck.Close(expected.Energy, actual.Energy);
        BehaviorTestCheck.Close(expected.Sleepiness, actual.Sleepiness);
        BehaviorTestCheck.Close(expected.Boredom, actual.Boredom);
        BehaviorTestCheck.Close(expected.Curiosity, actual.Curiosity);
        BehaviorTestCheck.Close(expected.Happiness, actual.Happiness);
        BehaviorTestCheck.Close(expected.Stress, actual.Stress);
        BehaviorTestCheck.Close(expected.Affection, actual.Affection);
    }

    private static Fixture CreateFixture(
        IReadOnlyList<BehaviorDefinition>? definitions = null,
        EmotionState? initialEmotions = null)
    {
        var clock = new ManualDualClock();
        string[] clips =
        [
            "idle",
            "blink",
            "stretch",
            "groom",
            "sleep_enter",
            "sleep_loop",
            "sleep_exit",
            "sleep_enter_b",
            "sleep_loop_b",
            "sleep_exit_b",
            "sleep_enter_c",
            "sleep_loop_c",
            "sleep_exit_c",
        ];
        var animation = new FakeAnimationPort(clips);
        var utilityRandom = new CountingConstantRandomSource(0.25);
        var animationVariantRandom = new CountingConstantRandomSource(0);
        var streams = new BehaviorRandomStreams(
            new CountingConstantRandomSource(0.5),
            utilityRandom,
            new CountingConstantRandomSource(0.5),
            animationVariantRandom);
        var coordinator = new BehaviorRuntimeCoordinator(
            new BehaviorCatalog(definitions ?? Definitions()),
            animation,
            new FakeBubblePort(),
            initialEmotions: initialEmotions ?? new EmotionState(),
            clock: clock,
            randomStreams: streams);
        coordinator.UpdateContext(
            new BehaviorContextSnapshot
            {
                Revision = 1,
                LocalNow = new DateTimeOffset(
                    2026,
                    7,
                    28,
                    12,
                    0,
                    0,
                    TimeSpan.FromHours(8)),
            });
        return new Fixture(
            coordinator,
            animation,
            utilityRandom,
            animationVariantRandom,
            clock);
    }

    private static IReadOnlyList<BehaviorDefinition> Definitions() =>
    [
        Definition(
            "idle_fallback",
            "idle",
            tags: ["state:fallback"],
            queueable: false,
            fallback: true,
            completion: new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.External,
            },
            maximumDuration: TimeSpan.FromDays(1)),
        Definition(
            "active_blink",
            "blink",
            tags: ["trigger:active_tick"],
            baseScore: 34),
        Definition(
            "active_stretch",
            "stretch",
            tags: ["trigger:active_tick"],
            baseScore: 30),
        Definition(
            "click_blink",
            "blink",
            tags: ["trigger:click"],
            baseScore: 30),
        Definition(
            "repeat_feedback",
            "blink",
            tags: ["trigger:repeat_click"],
            baseScore: 30),
        Definition(
            "petting_nuzzle",
            "groom",
            tags: ["trigger:petting"],
            baseScore: 30),
        Definition(
            "care_feed_cat_treat",
            "groom",
            tags: ["trigger:feed"],
            baseScore: 100),
        Definition(
            "pet_end_feedback",
            "stretch",
            tags: ["trigger:pet_end"],
            baseScore: 30),
        Definition(
            "long_press_groom",
            "groom",
            tags: ["trigger:long_press"],
            baseScore: 30),
        Definition(
            "drag_release_stretch",
            "stretch",
            tags: ["trigger:drag_release"],
            baseScore: 30),
        Definition(
            "morning_blink",
            "blink",
            tags:
            [
                "trigger:time:morning",
                "transition:wake-from-sleep",
            ],
            baseScore: 30),
        Definition(
            "return_blink",
            "blink",
            tags:
            [
                "trigger:user:return",
                "transition:wake-from-sleep",
            ],
            baseScore: 30),
        Definition(
            "resume_blink",
            "blink",
            tags:
            [
                "trigger:user:resume",
                "transition:wake-from-sleep",
            ],
            baseScore: 30),
        Definition(
            "safe_micro_feedback",
            "blink",
            tags: ["fallback:interaction"],
            baseScore: 1,
            completion: new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.Duration,
                Duration = TimeSpan.FromMilliseconds(300),
            },
            maximumDuration: TimeSpan.FromMilliseconds(300)),
        new BehaviorDefinition
        {
            Id = new BehaviorId("sleep_state"),
            DisplayName = "sleep",
            Family = "sleep",
            ClipFamily = "sleep",
            Tags =
            [
                "state:sleep",
                "trigger:time:late",
                "trigger:user:away",
                "trigger:active_tick",
            ],
            AllowedStates = [StablePetState.Normal, StablePetState.Sleeping],
            TargetState = StablePetState.Sleeping,
            Animation = new BehaviorAnimationPlan
            {
                VariantId = "a",
                EnterClipId = "sleep_enter",
                LoopClipId = "sleep_loop",
                ExitClipId = "sleep_exit",
                Variants =
                [
                    new BehaviorAnimationVariant
                    {
                        Id = "b",
                        EnterClipId = "sleep_enter_b",
                        LoopClipId = "sleep_loop_b",
                        ExitClipId = "sleep_exit_b",
                    },
                    new BehaviorAnimationVariant
                    {
                        Id = "c",
                        EnterClipId = "sleep_enter_c",
                        LoopClipId = "sleep_loop_c",
                        ExitClipId = "sleep_exit_c",
                    },
                ],
            },
            Completion = new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.External,
            },
            MaximumDuration = TimeSpan.FromDays(1),
            Queueable = false,
            Utility = new BehaviorUtilityRule
            {
                BaseScore = 100,
                MaxConsecutiveRuns = 1,
            },
            AutomaticEligibility = new BehaviorAutomaticEligibilityRule
            {
                TimeSlots = ["late"],
                MinimumIdleFor = TimeSpan.FromMinutes(5),
                MatchAny = true,
            },
        },
        new BehaviorDefinition
        {
            Id = new BehaviorId("wake_preview"),
            DisplayName = "wake preview",
            Family = "sleep",
            ClipFamily = "sleep",
            Tags = ["trigger:wake"],
            AllowedStates = [StablePetState.Sleeping],
            TargetState = StablePetState.Normal,
            Animation = new BehaviorAnimationPlan
            {
                ExitClipId = "sleep_exit",
            },
            Completion = new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.ClipEnd,
            },
            MaximumDuration = TimeSpan.FromSeconds(4),
        },
    ];

    private static BehaviorDefinition Definition(
        string id,
        string clip,
        IReadOnlyList<string> tags,
        double baseScore = 0,
        BehaviorDisturbanceLevel disturbance =
            BehaviorDisturbanceLevel.SilentMicro,
        bool queueable = true,
        bool fallback = false,
        BehaviorCompletionPolicy? completion = null,
        TimeSpan? maximumDuration = null,
        EmotionDelta? startedEmotionEffect = null) =>
        new()
        {
            Id = new BehaviorId(id),
            DisplayName = id,
            Family = id,
            ClipFamily = clip,
            Tags = tags,
            AllowedStates = [StablePetState.Normal],
            Animation = fallback
                ? new BehaviorAnimationPlan { LoopClipId = clip }
                : new BehaviorAnimationPlan { PerformClipId = clip },
            Completion = completion ??
                         new BehaviorCompletionPolicy
                         {
                             Kind = BehaviorCompletionKind.ClipEnd,
                         },
            MinimumCommitTime = TimeSpan.Zero,
            MaximumDuration = maximumDuration ?? TimeSpan.FromSeconds(4),
            Cooldown = TimeSpan.Zero,
            StartedEmotionEffect =
                startedEmotionEffect ?? new EmotionDelta(),
            Utility = new BehaviorUtilityRule
            {
                BaseScore = baseScore,
                MaxConsecutiveRuns = 3,
            },
            DisturbanceLevel = disturbance,
            Queueable = queueable,
            IsStateFallback = fallback,
        };

    private static void CompleteCurrentPerform(Fixture fixture)
    {
        BehaviorPlaybackToken playback = BehaviorTestCheck.NotNull(
            fixture.Coordinator.Sessions.CurrentPlaybackToken);
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyFirstFrame(playback));
        BehaviorTestCheck.True(
            fixture.Coordinator.NotifyClipCompleted(playback));
    }

    private static void ExpirePendingClick(Fixture fixture)
    {
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(701));
        fixture.Coordinator.Advance();
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(BehaviorRuntimeCoordinatorTests)}.{name}");
    }

    private sealed record Fixture(
        BehaviorRuntimeCoordinator Coordinator,
        FakeAnimationPort Animation,
        CountingConstantRandomSource UtilityRandom,
        CountingConstantRandomSource AnimationVariantRandom,
        ManualDualClock Clock);

    private sealed class FakeAnimationPort : IBehaviorAnimationSessionPort
    {
        private readonly HashSet<string> _clips;

        public FakeAnimationPort(params string[] clips)
        {
            _clips = clips.ToHashSet(StringComparer.Ordinal);
        }

        public List<BehaviorPlaybackRequest> Requests { get; } = [];

        public HashSet<string> RejectedClips { get; } =
            new(StringComparer.Ordinal);

        public Action<BehaviorPlaybackRequest>? PlaybackStarted { get; set; }

        public bool IsClipAvailable(string clipId) => _clips.Contains(clipId);

        public bool TryStartPlayback(BehaviorPlaybackRequest request)
        {
            if (!IsClipAvailable(request.ClipId))
            {
                return false;
            }

            Requests.Add(request);
            if (RejectedClips.Contains(request.ClipId))
            {
                return false;
            }

            PlaybackStarted?.Invoke(request);
            return true;
        }

        public void StopPlayback(BehaviorPlaybackToken playbackToken)
        {
        }
    }

    private sealed class FakeBubblePort : IBehaviorBubbleSessionPort
    {
        public void Show(BehaviorBubblePresentation presentation)
        {
        }

        public void Hide(BehaviorSessionToken ownerSessionToken)
        {
        }
    }

    private sealed class CountingConstantRandomSource(double value) : IRandomSource
    {
        public int Calls { get; private set; }

        public double NextUnit()
        {
            Calls++;
            return value;
        }
    }
}
