using System.IO;
using System.Runtime.ExceptionServices;
using GuluPet.Postcards;
using GuluPet.Presentation;
using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class PostcardProgressTrackerTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 8, 1, 4, 0, 0, TimeSpan.Zero);

    public static void RunAll()
    {
        Run(nameof(DefaultStateIsAtHome), DefaultStateIsAtHome);
        Run(
            nameof(OutingLastsExactlyThirtyMinutes),
            OutingLastsExactlyThirtyMinutes);
        Run(
            nameof(ArrivalRequiresTheDeadlineAndLatches),
            ArrivalRequiresTheDeadlineAndLatches);
        Run(
            nameof(ClaimPreviewDoesNotPublishBeforeCommit),
            ClaimPreviewDoesNotPublishBeforeCommit);
        Run(
            nameof(ClaimCommitRejectsStaleAndForeignPlans),
            ClaimCommitRejectsStaleAndForeignPlans);
        Run(
            nameof(ConvenienceClaimUnlocksTheNextPrefixCard),
            ConvenienceClaimUnlocksTheNextPrefixCard);
        Run(
            nameof(CollectionCompleteBlocksAnotherOuting),
            CollectionCompleteBlocksAnotherOuting);
        Run(
            nameof(LegacyInputNeverUnlocksOrAdvancesState),
            LegacyInputNeverUnlocksOrAdvancesState);
        Run(
            nameof(MigrationPrefixWinsOverHugeLegacyInput),
            MigrationPrefixWinsOverHugeLegacyInput);
        Run(
            nameof(UnknownRecordsSurviveAClaimWithoutChangingThePrefix),
            UnknownRecordsSurviveAClaimWithoutChangingThePrefix);
        Run(
            nameof(CatalogHoleClaimsTheFirstMissingCard),
            CatalogHoleClaimsTheFirstMissingCard);
        Run(
            nameof(ClaimTimesNeverMoveDurableStateBackwards),
            ClaimTimesNeverMoveDurableStateBackwards);
        Run(
            nameof(PersistFailureDoesNotStartOuting),
            PersistFailureDoesNotStartOuting);
        Run(
            nameof(PersistSuccessStartsOutingAfterTheWrite),
            PersistSuccessStartsOutingAfterTheWrite);
        Run(
            nameof(PersistFailureDoesNotCommitOrPublishClaim),
            PersistFailureDoesNotCommitOrPublishClaim);
        Run(
            nameof(PersistSuccessCommitsAndPublishesExactlyOnce),
            PersistSuccessCommitsAndPublishesExactlyOnce);
        Run(
            nameof(GalleryProgressUsesOutingStates),
            GalleryProgressUsesOutingStates);
        Run(
            nameof(ControllerTestHooksDriveOneOuting),
            ControllerTestHooksDriveOneOuting);
        Run(
            nameof(ControllerCloseGalleryIsIdempotentAndKeepsUnlockPopup),
            ControllerCloseGalleryIsIdempotentAndKeepsUnlockPopup);
        Run(
            nameof(RejectsInvalidOutingDocuments),
            RejectsInvalidOutingDocuments);
    }

    private static void DefaultStateIsAtHome()
    {
        var tracker = CreateTracker(CreateCatalog(3));

        PostcardOutingStatus status = tracker.GetOutingStatus(BaseTime);

        BehaviorTestCheck.Equal(PostcardOutingPhase.AtHome, status.Phase);
        BehaviorTestCheck.Equal("card-1", status.NextPostcardId!);
        BehaviorTestCheck.Equal(TimeSpan.Zero, status.Remaining);
        BehaviorTestCheck.True(status.StartedAtUtc is null);
        BehaviorTestCheck.Equal(2, tracker.Current.SchemaVersion);
    }

    private static void OutingLastsExactlyThirtyMinutes()
    {
        var tracker = CreateTracker(CreateCatalog(2));

        PostcardOutingUpdate started = tracker.StartOuting(BaseTime);

        BehaviorTestCheck.True(started.Changed);
        BehaviorTestCheck.Equal(
            PostcardOutingPhase.Traveling,
            started.Status.Phase);
        BehaviorTestCheck.Equal(BaseTime, started.Status.StartedAtUtc!.Value);
        BehaviorTestCheck.Equal(
            BaseTime.AddMinutes(30),
            started.Status.ReadyAtUtc!.Value);
        BehaviorTestCheck.Equal(
            PostcardOutingRecord.Duration,
            started.Status.Remaining);
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => tracker.StartOuting(BaseTime.AddMinutes(1)));

        PostcardOutingStatus clockRollback = tracker.GetOutingStatus(
            BaseTime.AddDays(-1));
        BehaviorTestCheck.Equal(
            PostcardOutingRecord.Duration,
            clockRollback.Remaining);

        PostcardOutingStatus overdue = tracker.GetOutingStatus(
            BaseTime.AddHours(1));
        BehaviorTestCheck.Equal(
            PostcardOutingPhase.Traveling,
            overdue.Phase);
        BehaviorTestCheck.Equal(TimeSpan.Zero, overdue.Remaining);
        BehaviorTestCheck.Equal(0, tracker.Current.Unlocked.Count);
    }

    private static void ArrivalRequiresTheDeadlineAndLatches()
    {
        var tracker = CreateTracker(CreateCatalog(2));
        tracker.StartOuting(BaseTime);

        PostcardOutingUpdate early = tracker.MarkArrived(
            BaseTime.AddMinutes(30).AddTicks(-1));
        BehaviorTestCheck.False(early.Changed);
        BehaviorTestCheck.Equal(
            PostcardOutingPhase.Traveling,
            early.Status.Phase);

        PostcardOutingUpdate arrived = tracker.MarkArrived(
            BaseTime.AddMinutes(30));
        BehaviorTestCheck.True(arrived.Changed);
        BehaviorTestCheck.Equal(
            PostcardOutingPhase.WaitingAtDoor,
            arrived.Status.Phase);
        BehaviorTestCheck.Equal(
            BaseTime.AddMinutes(30),
            arrived.Status.ArrivedAtUtc!.Value);

        PostcardOutingUpdate repeated = tracker.MarkArrived(
            BaseTime.AddHours(2));
        BehaviorTestCheck.False(repeated.Changed);
        BehaviorTestCheck.Equal(
            BaseTime.AddMinutes(30),
            repeated.Status.ArrivedAtUtc!.Value);
        BehaviorTestCheck.Equal(
            PostcardOutingPhase.WaitingAtDoor,
            tracker.GetOutingStatus(BaseTime).Phase);
    }

    private static void ClaimPreviewDoesNotPublishBeforeCommit()
    {
        var tracker = CreateArrivedTracker(CreateCatalog(3));

        PostcardClaimPlan plan = tracker.PreviewClaimArrival(
            BaseTime.AddMinutes(30));

        BehaviorTestCheck.Equal("card-1", plan.Postcard.Id);
        BehaviorTestCheck.Equal(0, tracker.Current.Unlocked.Count);
        BehaviorTestCheck.NotNull(tracker.Current.CurrentOuting);
        BehaviorTestCheck.Equal(1, plan.CandidateState.Unlocked.Count);
        BehaviorTestCheck.True(plan.CandidateState.CurrentOuting is null);

        PostcardClaimResult committed = tracker.CommitClaimArrival(plan);
        BehaviorTestCheck.Equal("card-1", committed.Postcard.Id);
        BehaviorTestCheck.SequenceEqual(
            ["card-1"],
            committed.State.Unlocked.Select(static record => record.PostcardId));
        BehaviorTestCheck.True(committed.State.CurrentOuting is null);
        BehaviorTestCheck.Equal(
            PostcardOutingPhase.AtHome,
            tracker.GetOutingStatus(BaseTime.AddMinutes(30)).Phase);
        BehaviorTestCheck.Equal(
            "card-2",
            tracker.GetOutingStatus(BaseTime.AddMinutes(30)).NextPostcardId!);
    }

    private static void ClaimCommitRejectsStaleAndForeignPlans()
    {
        var first = CreateArrivedTracker(CreateCatalog(2));
        var second = CreateArrivedTracker(CreateCatalog(2));
        PostcardClaimPlan stale = first.PreviewClaimArrival(
            BaseTime.AddMinutes(30));
        PostcardClaimPlan winner = first.PreviewClaimArrival(
            BaseTime.AddMinutes(30));

        _ = first.CommitClaimArrival(winner);

        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => first.CommitClaimArrival(stale));
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => second.CommitClaimArrival(stale));
        BehaviorTestCheck.Equal(1, first.Current.Unlocked.Count);
        BehaviorTestCheck.Equal(0, second.Current.Unlocked.Count);
    }

    private static void ConvenienceClaimUnlocksTheNextPrefixCard()
    {
        PostcardCatalog catalog = CreateCatalog(3);
        PostcardCollectionState initial = StateWithUnlocked(catalog, 1);
        var tracker = new PostcardProgressTracker(catalog, initial);
        tracker.StartOuting(BaseTime.AddHours(1));
        tracker.MarkArrived(BaseTime.AddHours(1).AddMinutes(30));

        PostcardClaimResult result = tracker.ClaimArrival(
            BaseTime.AddHours(1).AddMinutes(31));

        BehaviorTestCheck.Equal("card-2", result.Postcard.Id);
        BehaviorTestCheck.SequenceEqual(
            ["card-1", "card-2"],
            result.State.Unlocked.Select(static record => record.PostcardId));
        BehaviorTestCheck.True(
            result.State.Unlocked[1].NotificationAcknowledgedAtUtc is null);
    }

    private static void CollectionCompleteBlocksAnotherOuting()
    {
        PostcardCatalog catalog = CreateCatalog(1);
        var tracker = new PostcardProgressTracker(
            catalog,
            StateWithUnlocked(catalog, 1));

        BehaviorTestCheck.Equal(
            PostcardOutingPhase.CollectionComplete,
            tracker.GetOutingStatus(BaseTime).Phase);
        BehaviorTestCheck.True(
            tracker.GetOutingStatus(BaseTime).NextPostcardId is null);
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => tracker.StartOuting(BaseTime));
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => tracker.PreviewClaimArrival(BaseTime));
    }

    private static void LegacyInputNeverUnlocksOrAdvancesState()
    {
        var state = PostcardCollectionState.CreateDefault(BaseTime) with
        {
            TotalInputCount = 50_000,
        };
        var tracker = new PostcardProgressTracker(CreateCatalog(3), state);

        PostcardProgressUpdate update = tracker.ObserveSessionCounts(
            long.MaxValue,
            long.MaxValue,
            BaseTime.AddMinutes(1));

        BehaviorTestCheck.Equal(0L, update.AddedInputCount);
        BehaviorTestCheck.Equal(0, update.NewlyUnlocked.Count);
        BehaviorTestCheck.Equal(0L, update.NewlyEarnedTravelStampCount);
        BehaviorTestCheck.Equal(50_000L, tracker.Current.TotalInputCount);
        BehaviorTestCheck.Equal(0, tracker.Current.Unlocked.Count);
        BehaviorTestCheck.Equal(0L, tracker.InputsUntilNextUnlock);
        BehaviorTestCheck.Equal(0L, tracker.TravelStampCount);
    }

    private static void MigrationPrefixWinsOverHugeLegacyInput()
    {
        PostcardCatalog catalog = CreateCatalog(3);
        PostcardCollectionState state = StateWithUnlocked(catalog, 1) with
        {
            TotalInputCount = long.MaxValue,
        };
        var tracker = new PostcardProgressTracker(catalog, state);

        BehaviorTestCheck.SequenceEqual(
            ["card-1"],
            tracker.UnlockedDefinitions.Select(static postcard => postcard.Id));
        BehaviorTestCheck.Equal(
            "card-2",
            tracker.GetOutingStatus(BaseTime).NextPostcardId!);
    }

    private static void UnknownRecordsSurviveAClaimWithoutChangingThePrefix()
    {
        PostcardCatalog catalog = CreateCatalog(3);
        var state = new PostcardCollectionState
        {
            TotalInputCount = 99_000,
            Unlocked =
            [
                new PostcardUnlockRecord
                {
                    PostcardId = "future-card",
                    UnlockedAtUtc = BaseTime.AddMinutes(-2),
                },
                new PostcardUnlockRecord
                {
                    PostcardId = "card-1",
                    UnlockedAtUtc = BaseTime.AddMinutes(-1),
                },
            ],
            UpdatedAtUtc = BaseTime,
        };
        var tracker = new PostcardProgressTracker(catalog, state);
        tracker.StartOuting(BaseTime.AddMinutes(1));
        tracker.MarkArrived(BaseTime.AddMinutes(31));

        PostcardClaimResult result = tracker.ClaimArrival(
            BaseTime.AddMinutes(31));

        BehaviorTestCheck.SequenceEqual(
            ["future-card", "card-1", "card-2"],
            result.State.Unlocked.Select(static record => record.PostcardId));
        BehaviorTestCheck.SequenceEqual(
            ["card-1", "card-2"],
            tracker.UnlockedDefinitions.Select(static postcard => postcard.Id));
    }

    private static void CatalogHoleClaimsTheFirstMissingCard()
    {
        PostcardCatalog catalog = CreateCatalog(3);
        var state = new PostcardCollectionState
        {
            Unlocked =
            [
                new PostcardUnlockRecord
                {
                    PostcardId = "card-2",
                    UnlockedAtUtc = BaseTime,
                },
            ],
            UpdatedAtUtc = BaseTime,
        };
        var tracker = new PostcardProgressTracker(catalog, state);
        BehaviorTestCheck.Equal(
            "card-1",
            tracker.GetOutingStatus(BaseTime).NextPostcardId!);
        tracker.StartOuting(BaseTime);
        tracker.MarkArrived(BaseTime.AddMinutes(30));

        PostcardClaimResult result = tracker.ClaimArrival(
            BaseTime.AddMinutes(30));

        BehaviorTestCheck.Equal("card-1", result.Postcard.Id);
        BehaviorTestCheck.SequenceEqual(
            ["card-2", "card-1"],
            result.State.Unlocked.Select(static record => record.PostcardId));
        BehaviorTestCheck.SequenceEqual(
            ["card-1", "card-2"],
            tracker.UnlockedDefinitions.Select(static postcard => postcard.Id));
        BehaviorTestCheck.Equal(
            "card-3",
            tracker.GetOutingStatus(BaseTime).NextPostcardId!);
    }

    private static void ClaimTimesNeverMoveDurableStateBackwards()
    {
        DateTimeOffset futureUpdate = BaseTime.AddHours(2);
        var tracker = new PostcardProgressTracker(
            CreateCatalog(2),
            PostcardCollectionState.CreateDefault(futureUpdate));
        tracker.StartOuting(BaseTime);
        tracker.MarkArrived(BaseTime.AddMinutes(30));

        PostcardClaimResult result = tracker.ClaimArrival(
            BaseTime.AddMinutes(31));

        BehaviorTestCheck.Equal(futureUpdate, result.State.UpdatedAtUtc);
        BehaviorTestCheck.Equal(
            futureUpdate,
            result.State.Unlocked.Single().UnlockedAtUtc);
    }

    private static void PersistFailureDoesNotCommitOrPublishClaim()
    {
        Exception[] failures =
        [
            new IOException("simulated persistence failure"),
            new NotSupportedException("simulated replace failure"),
        ];
        foreach (Exception failure in failures)
        {
            var tracker = CreateArrivedTracker(CreateCatalog(2));
            PostcardClaimPlan plan = tracker.PreviewClaimArrival(
                BaseTime.AddMinutes(30));
            var published = new List<string>();

            bool committed =
                PostcardFeatureController.TryPersistAndCommitClaim(
                    tracker,
                    plan,
                    _ => throw failure,
                    postcard => published.Add(postcard.Id));

            BehaviorTestCheck.False(committed);
            BehaviorTestCheck.Equal(0, tracker.Current.Unlocked.Count);
            BehaviorTestCheck.NotNull(tracker.Current.CurrentOuting);
            BehaviorTestCheck.Equal(0, published.Count);
            BehaviorTestCheck.Equal(
                PostcardOutingPhase.WaitingAtDoor,
                tracker.GetOutingStatus(BaseTime).Phase);
        }
    }

    private static void PersistFailureDoesNotStartOuting()
    {
        Exception[] failures =
        [
            new IOException("simulated persistence failure"),
            new NotSupportedException("simulated replace failure"),
        ];
        foreach (Exception failure in failures)
        {
            var tracker = CreateTracker(CreateCatalog(2));

            bool committed =
                PostcardFeatureController.TryPersistAndCommitOutingStart(
                    tracker,
                    BaseTime,
                    _ => throw failure,
                    out PostcardOutingUpdate? update);

            BehaviorTestCheck.False(committed);
            BehaviorTestCheck.Null(update);
            BehaviorTestCheck.Null(tracker.Current.CurrentOuting);
            BehaviorTestCheck.Equal(
                PostcardOutingPhase.AtHome,
                tracker.GetOutingStatus(BaseTime).Phase);
        }
    }

    private static void PersistSuccessStartsOutingAfterTheWrite()
    {
        var tracker = CreateTracker(CreateCatalog(2));
        PostcardCollectionState? persisted = null;
        bool trackerWasAtHomeDuringWrite = false;

        bool committed =
            PostcardFeatureController.TryPersistAndCommitOutingStart(
                tracker,
                BaseTime,
                state =>
                {
                    trackerWasAtHomeDuringWrite =
                        tracker.Current.CurrentOuting is null;
                    persisted = state;
                },
                out PostcardOutingUpdate? update);

        BehaviorTestCheck.True(committed);
        BehaviorTestCheck.True(trackerWasAtHomeDuringWrite);
        BehaviorTestCheck.NotNull(persisted);
        BehaviorTestCheck.NotNull(persisted!.CurrentOuting);
        BehaviorTestCheck.NotNull(update);
        BehaviorTestCheck.Equal(
            PostcardOutingPhase.Traveling,
            update!.Status.Phase);
        BehaviorTestCheck.Equal(
            PostcardOutingPhase.Traveling,
            tracker.GetOutingStatus(BaseTime).Phase);
    }

    private static void PersistSuccessCommitsAndPublishesExactlyOnce()
    {
        var tracker = CreateArrivedTracker(CreateCatalog(2));
        PostcardClaimPlan plan = tracker.PreviewClaimArrival(
            BaseTime.AddMinutes(30));
        PostcardCollectionState? persisted = null;
        var published = new List<string>();

        bool committed = PostcardFeatureController.TryPersistAndCommitClaim(
            tracker,
            plan,
            state => persisted = state,
            postcard => published.Add(postcard.Id));

        BehaviorTestCheck.True(committed);
        BehaviorTestCheck.NotNull(persisted);
        BehaviorTestCheck.SequenceEqual(
            ["card-1"],
            persisted!.Unlocked.Select(static record => record.PostcardId));
        BehaviorTestCheck.SequenceEqual(["card-1"], published);
        BehaviorTestCheck.Equal(1, tracker.Current.Unlocked.Count);
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => tracker.CommitClaimArrival(plan));
        BehaviorTestCheck.SequenceEqual(["card-1"], published);
    }

    private static void RejectsInvalidOutingDocuments()
    {
        PostcardCatalog catalog = CreateCatalog(2);
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new PostcardProgressTracker(
                catalog,
                StateWithOuting(
                    BaseTime,
                    BaseTime.AddMinutes(29),
                    arrivedAtUtc: null,
                    updatedAtUtc: BaseTime)));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new PostcardProgressTracker(
                catalog,
                StateWithOuting(
                    BaseTime,
                    BaseTime.AddMinutes(30),
                    BaseTime.AddMinutes(29),
                    BaseTime.AddMinutes(30))));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new PostcardProgressTracker(
                catalog,
                StateWithOuting(
                    BaseTime,
                    BaseTime.AddMinutes(30),
                    arrivedAtUtc: null,
                    updatedAtUtc: BaseTime.AddMinutes(-1))));
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => new PostcardProgressTracker(catalog).MarkArrived(BaseTime));
    }

    private static void GalleryProgressUsesOutingStates()
    {
        var traveling = new PostcardOutingStatus(
            PostcardOutingPhase.Traveling,
            BaseTime,
            BaseTime.AddMinutes(30),
            ArrivedAtUtc: null,
            Remaining: TimeSpan.FromMinutes(12),
            NextPostcardId: "card-2");
        PostcardGalleryProgress progress =
            PostcardGalleryProgressFormatter.Create(
                traveling,
                unlockedCount: 1,
                availableCount: 3);

        BehaviorTestCheck.Equal("咕噜在外面逛逛", progress.Label);
        BehaviorTestCheck.Equal(
            "咕噜回家的脚步还有 12:00",
            progress.Detail);
        BehaviorTestCheck.Equal(
            PostcardOutingRecord.Duration.TotalSeconds,
            progress.Maximum);
        BehaviorTestCheck.Equal(
            TimeSpan.FromMinutes(18).TotalSeconds,
            progress.Progress);
        BehaviorTestCheck.False(progress.Footer.Contains(
            "输入",
            StringComparison.Ordinal));

        PostcardGalleryProgress waiting =
            PostcardGalleryProgressFormatter.Create(
                traveling with
                {
                    Phase = PostcardOutingPhase.WaitingAtDoor,
                    ArrivedAtUtc = BaseTime.AddMinutes(30),
                    Remaining = TimeSpan.Zero,
                },
                unlockedCount: 1,
                availableCount: 3);
        BehaviorTestCheck.Equal("咕噜回到家啦", waiting.Label);
        BehaviorTestCheck.True(waiting.Footer.Contains(
            "「在家门口」",
            StringComparison.Ordinal));
    }

    private static void ControllerTestHooksDriveOneOuting()
    {
        RunSta(
            () =>
            {
                var petWindow = new PetWindow();
                using var controller = new PostcardFeatureController(
                    CreateCatalog(2),
                    petWindow,
                    isolatedTestInstance: true,
                    new FixedTimeProvider(BaseTime));
                try
                {
                    BehaviorTestCheck.True(
                        controller.CanControlOutingForTest);
                    BehaviorTestCheck.False(controller.CanInjectTestInput);
                    BehaviorTestCheck.Throws<InvalidOperationException>(
                        () => controller.AddInputForTest(1));

                    controller.StartOutingForTest();
                    PostcardFeatureSnapshot traveling =
                        controller.GetSnapshot();
                    BehaviorTestCheck.Equal(
                        PostcardOutingPhase.Traveling,
                        traveling.OutingPhase);
                    BehaviorTestCheck.Equal(
                        1_800L,
                        traveling.OutingRemainingSeconds);
                    BehaviorTestCheck.Equal(
                        "card-1",
                        traveling.NextPostcardId!);

                    controller.CompleteOutingForTest();
                    BehaviorTestCheck.Equal(
                        PostcardOutingPhase.WaitingAtDoor,
                        controller.GetSnapshot().OutingPhase);
                    BehaviorTestCheck.True(
                        controller.ClaimArrivalForTest());

                    PostcardFeatureSnapshot claimed =
                        controller.GetSnapshot();
                    BehaviorTestCheck.Equal(
                        PostcardOutingPhase.AtHome,
                        claimed.OutingPhase);
                    BehaviorTestCheck.SequenceEqual(
                        ["card-1"],
                        claimed.UnlockedPostcardIds);
                    BehaviorTestCheck.SequenceEqual(
                        ["card-1"],
                        claimed.UnacknowledgedPostcardIds);
                    BehaviorTestCheck.Equal(
                        "card-2",
                        claimed.NextPostcardId!);
                }
                finally
                {
                    petWindow.Close();
                }
            });
    }

    private static void ControllerCloseGalleryIsIdempotentAndKeepsUnlockPopup()
    {
        RunSta(
            () =>
            {
                var petWindow = new PetWindow();
                using var controller = new PostcardFeatureController(
                    CreateCatalog(2),
                    petWindow,
                    isolatedTestInstance: true,
                    new FixedTimeProvider(BaseTime));
                try
                {
                    controller.SetPresentationVisible(true);
                    BehaviorTestCheck.True(
                        controller.ShowPostcardForTest("card-1"));
                    controller.ShowGallery();
                    PostcardFeatureSnapshot opened = controller.GetSnapshot();
                    BehaviorTestCheck.True(opened.PopupVisible);
                    BehaviorTestCheck.True(opened.GalleryVisible);

                    controller.CloseGallery();
                    PostcardFeatureSnapshot closed = controller.GetSnapshot();
                    BehaviorTestCheck.True(closed.PopupVisible);
                    BehaviorTestCheck.False(closed.GalleryVisible);

                    controller.CloseGallery();
                    BehaviorTestCheck.True(
                        controller.GetSnapshot().PopupVisible);
                }
                finally
                {
                    petWindow.Close();
                }
            });
    }

    private static PostcardProgressTracker CreateArrivedTracker(
        PostcardCatalog catalog)
    {
        var tracker = CreateTracker(catalog);
        tracker.StartOuting(BaseTime);
        tracker.MarkArrived(BaseTime.AddMinutes(30));
        return tracker;
    }

    private static PostcardCollectionState StateWithUnlocked(
        PostcardCatalog catalog,
        int count)
    {
        PostcardUnlockRecord[] records = catalog.Definitions
            .Take(count)
            .Select((postcard, index) => new PostcardUnlockRecord
            {
                PostcardId = postcard.Id,
                UnlockedAtUtc = BaseTime.AddMinutes(index),
                NotificationAcknowledgedAtUtc = BaseTime.AddMinutes(index + 1),
            })
            .ToArray();
        return new PostcardCollectionState
        {
            Unlocked = records,
            UpdatedAtUtc = count == 0
                ? BaseTime
                : BaseTime.AddMinutes(count),
        };
    }

    private static PostcardProgressTracker CreateTracker(
        PostcardCatalog catalog) =>
        new(catalog, PostcardCollectionState.CreateDefault(BaseTime));

    private static PostcardCollectionState StateWithOuting(
        DateTimeOffset startedAtUtc,
        DateTimeOffset readyAtUtc,
        DateTimeOffset? arrivedAtUtc,
        DateTimeOffset updatedAtUtc) =>
        new()
        {
            CurrentOuting = new PostcardOutingRecord
            {
                StartedAtUtc = startedAtUtc,
                ReadyAtUtc = readyAtUtc,
                ArrivedAtUtc = arrivedAtUtc,
            },
            UpdatedAtUtc = updatedAtUtc,
        };

    private static PostcardCatalog CreateCatalog(int count) =>
        new(
            Enumerable.Range(1, count)
                .Select(index => new PostcardDefinition(
                    $"card-{index}",
                    $"明信片 {index}",
                    "中国",
                    Path.GetFullPath($"card-{index}.jpg"),
                    index,
                    $"咕噜的明信片 {index}")));

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(PostcardProgressTrackerTests)}.{name}");
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
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
