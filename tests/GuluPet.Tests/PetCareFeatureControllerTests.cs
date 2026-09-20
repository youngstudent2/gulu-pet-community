using GuluPet.Care;

namespace GuluPet.Tests;

internal static class PetCareFeatureControllerTests
{
    public static void RunAll()
    {
        Run(
            nameof(OnlineClockChargesOnlyObservedApplicationTime),
            OnlineClockChargesOnlyObservedApplicationTime);
        Run(
            nameof(MonotonicRollbackAndEmptyStateFailClosed),
            MonotonicRollbackAndEmptyStateFailClosed);
        Run(
            nameof(RejectedOrUnidentifiedTriggerNeverRefillsOrBecomesBusy),
            RejectedOrUnidentifiedTriggerNeverRefillsOrBecomesBusy);
        Run(
            nameof(VerifiedCommitRefillsAndOnlyItsSessionCanTerminate),
            VerifiedCommitRefillsAndOnlyItsSessionCanTerminate);
        Run(
            nameof(SynchronousCommitBeforeTriggerReturnIsRetained),
            SynchronousCommitBeforeTriggerReturnIsRetained);
        Run(
            nameof(RejectedRetryRestoresAnEarlierPendingWakeRequest),
            RejectedRetryRestoresAnEarlierPendingWakeRequest);
        Run(
            nameof(PreCommitTerminationCancelsWithoutRefilling),
            PreCommitTerminationCancelsWithoutRefilling);
        Run(
            nameof(SynchronousPreCommitTerminationStaysCancelled),
            SynchronousPreCommitTerminationStaysCancelled);
    }

    private static void OnlineClockChargesOnlyObservedApplicationTime()
    {
        TimeSpan elapsed = TimeSpan.Zero;
        var care = new CareStateTracker();
        var online = new CareOnlineSessionTracker(care, () => elapsed);

        elapsed += TimeSpan.FromHours(1);
        CareOnlineObservation first = online.Observe();
        BehaviorTestCheck.Equal(TimeSpan.FromHours(1), first.AppliedElapsed);
        BehaviorTestCheck.True(first.StateChanged);
        BehaviorTestCheck.Close(87.5, first.State.FoodLevel);
        BehaviorTestCheck.Close(100d - (100d / 6d), first.State.WaterLevel);

        elapsed += TimeSpan.FromMinutes(30);
        CareOnlineObservation beforeSuspend = online.Suspend();
        BehaviorTestCheck.Close(81.25, beforeSuspend.State.FoodLevel);
        BehaviorTestCheck.Close(75, beforeSuspend.State.WaterLevel);
        BehaviorTestCheck.True(online.IsSuspended);

        elapsed += TimeSpan.FromHours(4);
        CareOnlineObservation offline = online.Observe();
        BehaviorTestCheck.Equal(TimeSpan.Zero, offline.AppliedElapsed);
        BehaviorTestCheck.False(offline.StateChanged);
        BehaviorTestCheck.Close(81.25, offline.State.FoodLevel);
        BehaviorTestCheck.Close(75, offline.State.WaterLevel);

        online.Resume();
        elapsed += TimeSpan.FromMinutes(30);
        CareOnlineObservation afterResume = online.Observe();
        BehaviorTestCheck.Close(75, afterResume.State.FoodLevel);
        BehaviorTestCheck.Close(
            100d - (2d * 100d / 6d),
            afterResume.State.WaterLevel);
    }

    private static void MonotonicRollbackAndEmptyStateFailClosed()
    {
        TimeSpan elapsed = TimeSpan.Zero;
        var care = new CareStateTracker();
        var online = new CareOnlineSessionTracker(care, () => elapsed);

        elapsed = TimeSpan.FromMinutes(10);
        CareState afterTen = online.Observe().State;
        elapsed = TimeSpan.FromMinutes(5);
        CareOnlineObservation rollback = online.Observe();
        BehaviorTestCheck.Equal(TimeSpan.Zero, rollback.AppliedElapsed);
        BehaviorTestCheck.Close(afterTen.FoodLevel, rollback.State.FoodLevel);
        BehaviorTestCheck.Close(afterTen.WaterLevel, rollback.State.WaterLevel);

        elapsed = TimeSpan.FromMinutes(15);
        CareOnlineObservation recovered = online.Observe();
        BehaviorTestCheck.Equal(
            TimeSpan.FromMinutes(10),
            recovered.AppliedElapsed);

        var emptyCare = new CareStateTracker(CareState.Create(0, 0));
        elapsed = TimeSpan.Zero;
        var emptyOnline = new CareOnlineSessionTracker(
            emptyCare,
            () => elapsed);
        elapsed = TimeSpan.FromHours(1);
        CareOnlineObservation empty = emptyOnline.Observe();
        BehaviorTestCheck.False(empty.StateChanged);
        BehaviorTestCheck.Close(0, empty.State.FoodLevel);
        BehaviorTestCheck.Close(0, empty.State.WaterLevel);
    }

    private static void RejectedOrUnidentifiedTriggerNeverRefillsOrBecomesBusy()
    {
        var care = new CareStateTracker(CareState.Create(25, 35));
        var runtime = new CareRuntimeState(care);

        CareRequestAttempt rejected = runtime.BeginRequest(
            CareRuntimeState.FeedAction);
        BehaviorTestCheck.False(runtime.InteractionBusy);
        BehaviorTestCheck.False(runtime.CompleteRequest(
            rejected,
            accepted: false,
            triggerRequestId: 1,
            triggerBehaviorId: "care_feed"));
        BehaviorTestCheck.Null(runtime.Pending);

        CareRequestAttempt unidentified = runtime.BeginRequest(
            CareRuntimeState.WaterAction);
        BehaviorTestCheck.False(runtime.CompleteRequest(
            unidentified,
            accepted: true,
            triggerRequestId: null,
            triggerBehaviorId: "care_drink"));
        BehaviorTestCheck.Null(runtime.Pending);
        BehaviorTestCheck.False(runtime.InteractionBusy);
        BehaviorTestCheck.Close(25, care.Current.FoodLevel);
        BehaviorTestCheck.Close(35, care.Current.WaterLevel);

        BehaviorTestCheck.False(runtime.TryCommitVerified(
            CareRuntimeState.FeedAction,
            careRequestId: 2,
            careBehaviorId: "care_feed",
            sessionToken: 20,
            out _));
        BehaviorTestCheck.Close(25, care.Current.FoodLevel);
    }

    private static void VerifiedCommitRefillsAndOnlyItsSessionCanTerminate()
    {
        var care = new CareStateTracker(CareState.Create(20, 30));
        var runtime = new CareRuntimeState(care);
        CareRequestAttempt request = runtime.BeginRequest(
            CareRuntimeState.FeedAction);

        BehaviorTestCheck.True(runtime.CompleteRequest(
            request,
            accepted: true,
            triggerRequestId: 12,
            triggerBehaviorId: "wake_feedback"));
        BehaviorTestCheck.False(runtime.InteractionBusy);
        BehaviorTestCheck.Equal(
            12L,
            BehaviorTestCheck.NotNull(runtime.Pending).TriggerRequestId!.Value);

        BehaviorTestCheck.False(runtime.TryCommitVerified(
            CareRuntimeState.WaterAction,
            careRequestId: 13,
            careBehaviorId: "care_drink",
            sessionToken: 77,
            out _));
        BehaviorTestCheck.True(runtime.TryCommitVerified(
            CareRuntimeState.FeedAction,
            careRequestId: 13,
            careBehaviorId: "care_feed",
            sessionToken: 77,
            out CareState committed));
        BehaviorTestCheck.Close(100, committed.FoodLevel);
        BehaviorTestCheck.Close(30, committed.WaterLevel);
        BehaviorTestCheck.True(runtime.InteractionBusy);
        ActiveCareSession active = BehaviorTestCheck.NotNull(runtime.Active);
        BehaviorTestCheck.Equal(12L, active.TriggerRequestId!.Value);
        BehaviorTestCheck.Equal("wake_feedback", active.TriggerBehaviorId);

        BehaviorTestCheck.False(runtime.TryCommitVerified(
            CareRuntimeState.FeedAction,
            careRequestId: 13,
            careBehaviorId: "care_feed",
            sessionToken: 77,
            out _));
        BehaviorTestCheck.False(runtime.TryTerminateVerified(
            CareRuntimeState.FeedAction,
            sessionToken: 78));
        BehaviorTestCheck.True(runtime.InteractionBusy);
        BehaviorTestCheck.True(runtime.TryTerminateVerified(
            CareRuntimeState.FeedAction,
            sessionToken: 77));
        BehaviorTestCheck.False(runtime.InteractionBusy);
    }

    private static void SynchronousCommitBeforeTriggerReturnIsRetained()
    {
        var care = new CareStateTracker(CareState.Create(45, 10));
        var runtime = new CareRuntimeState(care);
        CareRequestAttempt request = runtime.BeginRequest(
            CareRuntimeState.WaterAction);

        BehaviorTestCheck.True(runtime.TryCommitVerified(
            CareRuntimeState.WaterAction,
            careRequestId: 31,
            careBehaviorId: "care_drink",
            sessionToken: 88,
            out CareState synchronousCommit));
        BehaviorTestCheck.Close(45, synchronousCommit.FoodLevel);
        BehaviorTestCheck.Close(100, synchronousCommit.WaterLevel);
        BehaviorTestCheck.True(runtime.InteractionBusy);

        BehaviorTestCheck.True(runtime.CompleteRequest(
            request,
            accepted: true,
            triggerRequestId: 30,
            triggerBehaviorId: "care_drink"));
        ActiveCareSession active = BehaviorTestCheck.NotNull(runtime.Active);
        BehaviorTestCheck.Equal(30L, active.TriggerRequestId!.Value);
        BehaviorTestCheck.Equal(88L, active.SessionToken);
    }

    private static void RejectedRetryRestoresAnEarlierPendingWakeRequest()
    {
        var runtime = new CareRuntimeState(new CareStateTracker());
        CareRequestAttempt feed = runtime.BeginRequest(
            CareRuntimeState.FeedAction);
        BehaviorTestCheck.True(runtime.CompleteRequest(
            feed,
            accepted: true,
            triggerRequestId: 4,
            triggerBehaviorId: "wake_feedback"));

        CareRequestAttempt waterRetry = runtime.BeginRequest(
            CareRuntimeState.WaterAction);
        BehaviorTestCheck.False(runtime.CompleteRequest(
            waterRetry,
            accepted: false,
            triggerRequestId: 5,
            triggerBehaviorId: "care_drink"));

        PendingCareRequest restored =
            BehaviorTestCheck.NotNull(runtime.Pending);
        BehaviorTestCheck.Equal(CareRuntimeState.FeedAction, restored.CareAction);
        BehaviorTestCheck.Equal(4L, restored.TriggerRequestId!.Value);
    }

    private static void PreCommitTerminationCancelsWithoutRefilling()
    {
        var care = new CareStateTracker(CareState.Create(22, 33));
        var runtime = new CareRuntimeState(care);
        CareRequestAttempt request = runtime.BeginRequest(
            CareRuntimeState.WaterAction);
        BehaviorTestCheck.True(runtime.CompleteRequest(
            request,
            accepted: true,
            triggerRequestId: 90,
            triggerBehaviorId: "care_drink"));

        BehaviorTestCheck.True(
            runtime.TryCancelPendingVerifiedTermination(
                CareRuntimeState.WaterAction));
        BehaviorTestCheck.Null(runtime.Pending);
        BehaviorTestCheck.False(runtime.InteractionBusy);
        BehaviorTestCheck.Close(22, care.Current.FoodLevel);
        BehaviorTestCheck.Close(33, care.Current.WaterLevel);
    }

    private static void SynchronousPreCommitTerminationStaysCancelled()
    {
        var care = new CareStateTracker(CareState.Create(18, 27));
        var runtime = new CareRuntimeState(care);
        CareRequestAttempt request = runtime.BeginRequest(
            CareRuntimeState.FeedAction);

        BehaviorTestCheck.True(
            runtime.TryCancelPendingVerifiedTermination(
                CareRuntimeState.FeedAction));
        BehaviorTestCheck.False(runtime.CompleteRequest(
            request,
            accepted: true,
            triggerRequestId: 101,
            triggerBehaviorId: "care_feed_cat_treat"));

        BehaviorTestCheck.Null(runtime.Pending);
        BehaviorTestCheck.False(runtime.InteractionBusy);
        BehaviorTestCheck.Close(18, care.Current.FoodLevel);
        BehaviorTestCheck.Close(27, care.Current.WaterLevel);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(PetCareFeatureControllerTests)}.{name}");
    }
}
