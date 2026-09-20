using GuluPet.Behavior;

namespace GuluPet.Tests;

internal static class SoftBehaviorQueueTests
{
    public static void RunAll()
    {
        Run(
            nameof(PendingBoundariesCreateGroupsOfAtMostThree),
            PendingBoundariesCreateGroupsOfAtMostThree);
        Run(
            nameof(EleventhRequestEvictsAWholeGroupAndKeepsTheNewRequest),
            EleventhRequestEvictsAWholeGroupAndKeepsTheNewRequest);
        Run(
            nameof(FifoGroupsWaitExactlyFiveSeconds),
            FifoGroupsWaitExactlyFiveSeconds);
        Run(
            nameof(DedupeRefreshKeepsPositionAndDoesNotConsumeRandom),
            DedupeRefreshKeepsPositionAndDoesNotConsumeRandom);
        Run(
            nameof(DedupeRefreshPreservesDailyOpportunityIdentity),
            DedupeRefreshPreservesDailyOpportunityIdentity);
        Run(
            nameof(DedupeRefreshCannotExtendPastMaximumResidence),
            DedupeRefreshCannotExtendPastMaximumResidence);
        Run(
            nameof(ScheduledAndWeatherRequestsHonorExplicitExpiration),
            ScheduledAndWeatherRequestsHonorExplicitExpiration);
        Run(
            nameof(ScheduledRefreshIsNotCappedAtTwoMinutes),
            ScheduledRefreshIsNotCappedAtTwoMinutes);
        Run(
            nameof(ExpirationUsesAnInclusiveBoundary),
            ExpirationUsesAnInclusiveBoundary);
        Run(
            nameof(ExpiredCleanupPrecedesCapacityEviction),
            ExpiredCleanupPrecedesCapacityEviction);
        Run(
            nameof(ActiveTickIsUniqueAndSuppressedAtThreePendingItems),
            ActiveTickIsUniqueAndSuppressedAtThreePendingItems);
        Run(
            nameof(StartedActiveGroupCannotBeEvicted),
            StartedActiveGroupCannotBeEvicted);
        Run(
            nameof(QueueRejectsImmediateRequests),
            QueueRejectsImmediateRequests);
        Run(
            nameof(DiscardPendingRemovesOnlyUnstartedMatches),
            DiscardPendingRemovesOnlyUnstartedMatches);
    }

    private static void DiscardPendingRemovesOnlyUnstartedMatches()
    {
        var clock = new ManualDualClock();
        var queue =
            new SoftBehaviorQueue(clock, new SequenceRandomSource());
        EnqueueRange(queue, clock, 1, 4);

        BehaviorRequest active =
            BehaviorTestCheck.NotNull(queue.TryStartNext());
        IReadOnlyList<BehaviorRequest> discarded = queue.DiscardPending(
            request => request.RequestId is 1 or 2 or 4,
            SoftQueueRemovalReason.SupersededByAutomaticWake);
        SoftBehaviorQueueSnapshot snapshot = queue.GetSnapshot();

        BehaviorTestCheck.Equal(1L, active.RequestId);
        BehaviorTestCheck.SequenceEqual(
            new long[] { 2, 4 },
            discarded.Select(static request => request.RequestId));
        BehaviorTestCheck.Equal(1, snapshot.PendingCount);
        BehaviorTestCheck.Equal(1L, snapshot.ActiveRequest!.RequestId);
        BehaviorTestCheck.SequenceEqual(
            new long[] { 3 },
            snapshot.Groups
                .SelectMany(static group => group.PendingItems)
                .Select(static request => request.RequestId));
        BehaviorTestCheck.True(
            snapshot.RecentRemovals.Any(removal =>
                removal.Reason ==
                    SoftQueueRemovalReason.SupersededByAutomaticWake &&
                removal.RequestIds.Contains(2)));
        BehaviorTestCheck.True(
            snapshot.RecentRemovals.Any(removal =>
                removal.Reason ==
                    SoftQueueRemovalReason.SupersededByAutomaticWake &&
                removal.RequestIds.Contains(4)));
    }

    private static void PendingBoundariesCreateGroupsOfAtMostThree()
    {
        foreach (int count in new[] { 0, 1, 2, 3, 4, 9, 10 })
        {
            var clock = new ManualDualClock();
            var random = new SequenceRandomSource();
            var queue = new SoftBehaviorQueue(clock, random);

            for (int index = 1; index <= count; index++)
            {
                SoftQueueEnqueueResult result =
                    queue.Enqueue(Request(index, clock.Elapsed));
                BehaviorTestCheck.Equal(
                    SoftQueueEnqueueDisposition.Enqueued,
                    result.Disposition);
            }

            SoftBehaviorQueueSnapshot snapshot = queue.GetSnapshot();
            BehaviorTestCheck.Equal(count, snapshot.PendingCount);
            BehaviorTestCheck.Equal((count + 2) / 3, snapshot.Groups.Count);
            BehaviorTestCheck.True(
                snapshot.Groups.All(group =>
                    group.AcceptedCount is >= 1 and <= 3));
            BehaviorTestCheck.True(
                snapshot.Groups.All(group =>
                    group.PendingItems.Count == group.AcceptedCount));
            BehaviorTestCheck.Equal(0, random.Calls);
        }
    }

    private static void EleventhRequestEvictsAWholeGroupAndKeepsTheNewRequest()
    {
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource(0.999_999);
        var queue = new SoftBehaviorQueue(clock, random);
        EnqueueRange(queue, clock, 1, 10);

        SoftQueueEnqueueResult result =
            queue.Enqueue(Request(11, clock.Elapsed));
        SoftBehaviorQueueSnapshot snapshot = queue.GetSnapshot();

        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.Enqueued,
            result.Disposition);
        BehaviorTestCheck.True(result.RandomConsumed);
        BehaviorTestCheck.Equal(1, random.Calls);
        BehaviorTestCheck.Equal(10, snapshot.PendingCount);
        BehaviorTestCheck.Equal(1, snapshot.Evictions.Count);
        BehaviorTestCheck.Equal(4L, snapshot.Evictions[0].GroupId);
        BehaviorTestCheck.SequenceEqual(
            new long[] { 10 },
            snapshot.Evictions[0].RequestIds);
        BehaviorTestCheck.True(
            snapshot.Groups
                .SelectMany(static group => group.PendingItems)
                .Any(static request => request.RequestId == 11),
            "The incoming request must survive overflow eviction.");
        BehaviorTestCheck.True(
            snapshot.RecentRemovals.Any(removal =>
                removal.Reason == SoftQueueRemovalReason.CapacityEviction &&
                removal.GroupId == 4));
    }

    private static void FifoGroupsWaitExactlyFiveSeconds()
    {
        var clock = new ManualDualClock();
        var queue =
            new SoftBehaviorQueue(clock, new SequenceRandomSource());
        EnqueueRange(queue, clock, 1, 4);

        for (long expected = 1; expected <= 3; expected++)
        {
            BehaviorRequest started =
                BehaviorTestCheck.NotNull(queue.TryStartNext());
            BehaviorTestCheck.Equal(expected, started.RequestId);
            BehaviorTestCheck.True(queue.CompleteActiveBehavior());
        }

        SoftBehaviorQueueSnapshot waiting = queue.GetSnapshot();
        BehaviorTestCheck.True(waiting.ResumeAfter.HasValue);
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(5),
            waiting.ResumeAfter!.Value);
        BehaviorTestCheck.Null(queue.TryStartNext());

        clock.Advance(TimeSpan.FromMilliseconds(4_999));
        BehaviorTestCheck.Null(queue.TryStartNext());

        clock.Advance(TimeSpan.FromMilliseconds(1));
        BehaviorRequest fourth =
            BehaviorTestCheck.NotNull(queue.TryStartNext());
        BehaviorTestCheck.Equal(4L, fourth.RequestId);
    }

    private static void DedupeRefreshKeepsPositionAndDoesNotConsumeRandom()
    {
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource();
        var queue = new SoftBehaviorQueue(clock, random);
        EnqueueRange(queue, clock, 1, 10);

        clock.Advance(TimeSpan.FromSeconds(1));
        var refresh = Request(
            101,
            clock.Elapsed,
            dedupeKey: "request-1",
            expiresAfter: TimeSpan.FromSeconds(90),
            contextRevision: 42);
        SoftQueueEnqueueResult result = queue.Enqueue(refresh);
        SoftBehaviorQueueSnapshot snapshot = queue.GetSnapshot();
        BehaviorRequest first = snapshot.Groups[0].PendingItems[0];

        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.Refreshed,
            result.Disposition);
        BehaviorTestCheck.False(result.RandomConsumed);
        BehaviorTestCheck.Equal(0, random.Calls);
        BehaviorTestCheck.Equal(10, snapshot.PendingCount);
        BehaviorTestCheck.Equal(1L, first.RequestId);
        BehaviorTestCheck.Equal(42L, first.ContextRevision);
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(91),
            first.ExpiresAt);
        BehaviorTestCheck.Equal(TimeSpan.Zero, first.FirstCreatedAt);
        BehaviorTestCheck.Equal(1L, snapshot.Groups[0].GroupId);
    }

    private static void DedupeRefreshPreservesDailyOpportunityIdentity()
    {
        var clock = new ManualDualClock();
        var queue =
            new SoftBehaviorQueue(clock, new SequenceRandomSource());
        var date = new DateOnly(2026, 7, 31);
        var original = new BehaviorDailyOpportunity(
            "weather:daily",
            date,
            new DateTimeOffset(
                2026,
                7,
                31,
                9,
                0,
                0,
                TimeSpan.FromHours(8)),
            AcceptedCount: 0,
            LastAcceptedAt: null);
        var newer = original with
        {
            ObservedAt = original.ObservedAt.AddMinutes(1),
            AcceptedCount = 1,
        };
        BehaviorRequest first = Request(
            1,
            clock.Elapsed,
            BehaviorRequestSource.Weather,
            dedupeKey: $"daily:weather:daily:{date.DayNumber}") with
        {
            DailyOpportunity = original,
        };
        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.Enqueued,
            queue.Enqueue(first).Disposition);

        clock.Advance(TimeSpan.FromSeconds(1));
        BehaviorRequest refresh = Request(
            2,
            clock.Elapsed,
            BehaviorRequestSource.Weather,
            dedupeKey: first.DedupeKey) with
        {
            DailyOpportunity = newer,
        };
        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.Refreshed,
            queue.Enqueue(refresh).Disposition);

        BehaviorRequest pending = queue.GetSnapshot()
            .Groups
            .SelectMany(static group => group.PendingItems)
            .Single();
        BehaviorTestCheck.Equal(1L, pending.RequestId);
        BehaviorTestCheck.Equal(original, pending.DailyOpportunity);
    }

    private static void ExpirationUsesAnInclusiveBoundary()
    {
        var clock = new ManualDualClock();
        var queue =
            new SoftBehaviorQueue(clock, new SequenceRandomSource());
        queue.Enqueue(
            Request(
                1,
                clock.Elapsed,
                expiresAfter: TimeSpan.FromSeconds(5)));

        clock.Advance(TimeSpan.FromMilliseconds(4_999));
        BehaviorTestCheck.Equal(0, queue.CleanupExpired());
        BehaviorTestCheck.Equal(1, queue.GetSnapshot().PendingCount);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        BehaviorTestCheck.Equal(1, queue.CleanupExpired());
        SoftBehaviorQueueSnapshot snapshot = queue.GetSnapshot();
        BehaviorTestCheck.Equal(0, snapshot.PendingCount);
        BehaviorTestCheck.Equal(0, snapshot.Groups.Count);
        BehaviorTestCheck.True(
            snapshot.RecentRemovals.Any(removal =>
                removal.Reason == SoftQueueRemovalReason.Expired &&
                removal.RequestIds.SequenceEqual(new long[] { 1 })));
    }

    private static void DedupeRefreshCannotExtendPastMaximumResidence()
    {
        var clock = new ManualDualClock();
        var queue =
            new SoftBehaviorQueue(clock, new SequenceRandomSource());
        queue.Enqueue(
            Request(
                1,
                clock.Elapsed,
                expiresAfter: TimeSpan.FromSeconds(119)));

        clock.Advance(TimeSpan.FromSeconds(60));
        SoftQueueEnqueueResult refreshed = queue.Enqueue(
            Request(
                2,
                clock.Elapsed,
                dedupeKey: "request-1",
                expiresAfter: TimeSpan.FromSeconds(120)));
        BehaviorRequest pending =
            queue.GetSnapshot().Groups.Single().PendingItems.Single();

        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.Refreshed,
            refreshed.Disposition);
        BehaviorTestCheck.Equal(
            SoftBehaviorQueue.MaximumResidence,
            pending.ExpiresAt);

        clock.Advance(TimeSpan.FromMilliseconds(59_999));
        BehaviorTestCheck.Equal(1, queue.GetSnapshot().PendingCount);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        BehaviorTestCheck.Equal(0, queue.GetSnapshot().PendingCount);
    }

    private static void ScheduledAndWeatherRequestsHonorExplicitExpiration()
    {
        var clock = new ManualDualClock();
        var queue =
            new SoftBehaviorQueue(clock, new SequenceRandomSource());
        queue.Enqueue(
            Request(
                1,
                clock.Elapsed,
                source: BehaviorRequestSource.Scheduled,
                expiresAfter: TimeSpan.FromMinutes(10)));
        queue.Enqueue(
            Request(
                2,
                clock.Elapsed,
                source: BehaviorRequestSource.Weather,
                expiresAfter: TimeSpan.FromMinutes(30)));

        clock.Advance(SoftBehaviorQueue.MaximumResidence);
        SoftBehaviorQueueSnapshot afterTwoMinutes = queue.GetSnapshot();
        BehaviorTestCheck.Equal(2, afterTwoMinutes.PendingCount);

        clock.Advance(TimeSpan.FromMinutes(8));
        SoftBehaviorQueueSnapshot atScheduledExpiry = queue.GetSnapshot();
        BehaviorTestCheck.Equal(1, atScheduledExpiry.PendingCount);
        BehaviorTestCheck.Equal(
            BehaviorRequestSource.Weather,
            atScheduledExpiry.Groups
                .SelectMany(static group => group.PendingItems)
                .Single()
                .Source);

        clock.Advance(TimeSpan.FromMinutes(20));
        BehaviorTestCheck.Equal(0, queue.GetSnapshot().PendingCount);
    }

    private static void ScheduledRefreshIsNotCappedAtTwoMinutes()
    {
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource();
        var queue = new SoftBehaviorQueue(clock, random);
        queue.Enqueue(
            Request(
                1,
                clock.Elapsed,
                source: BehaviorRequestSource.Scheduled,
                expiresAfter: TimeSpan.FromMinutes(5)));

        clock.Advance(TimeSpan.FromMinutes(1));
        SoftQueueEnqueueResult refresh = queue.Enqueue(
            Request(
                2,
                clock.Elapsed,
                source: BehaviorRequestSource.Scheduled,
                dedupeKey: "request-1",
                expiresAfter: TimeSpan.FromMinutes(9)));
        BehaviorRequest pending =
            queue.GetSnapshot().Groups.Single().PendingItems.Single();

        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.Refreshed,
            refresh.Disposition);
        BehaviorTestCheck.Equal(TimeSpan.FromMinutes(10), pending.ExpiresAt);
        BehaviorTestCheck.Equal(0, random.Calls);

        clock.Advance(TimeSpan.FromMinutes(8));
        BehaviorTestCheck.Equal(1, queue.GetSnapshot().PendingCount);
        clock.Advance(TimeSpan.FromMinutes(1));
        BehaviorTestCheck.Equal(0, queue.GetSnapshot().PendingCount);
    }

    private static void ExpiredCleanupPrecedesCapacityEviction()
    {
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource();
        var queue = new SoftBehaviorQueue(clock, random);

        queue.Enqueue(
            Request(
                1,
                clock.Elapsed,
                expiresAfter: TimeSpan.FromSeconds(1)));
        EnqueueRange(queue, clock, 2, 10);

        clock.Advance(TimeSpan.FromSeconds(1));
        SoftQueueEnqueueResult result =
            queue.Enqueue(Request(11, clock.Elapsed));
        SoftBehaviorQueueSnapshot snapshot = queue.GetSnapshot();

        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.Enqueued,
            result.Disposition);
        BehaviorTestCheck.False(result.RandomConsumed);
        BehaviorTestCheck.Equal(0, random.Calls);
        BehaviorTestCheck.Equal(10, snapshot.PendingCount);
        BehaviorTestCheck.Equal(0, snapshot.Evictions.Count);
    }

    private static void ActiveTickIsUniqueAndSuppressedAtThreePendingItems()
    {
        var replacementClock = new ManualDualClock();
        var replacementQueue = new SoftBehaviorQueue(
            replacementClock,
            new SequenceRandomSource());
        replacementQueue.Enqueue(
            Request(
                1,
                replacementClock.Elapsed,
                source: BehaviorRequestSource.ActiveTick));
        replacementClock.Advance(TimeSpan.FromSeconds(1));

        SoftQueueEnqueueResult replacement = replacementQueue.Enqueue(
            Request(
                2,
                replacementClock.Elapsed,
                source: BehaviorRequestSource.ActiveTick));
        SoftBehaviorQueueSnapshot replaced = replacementQueue.GetSnapshot();

        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.ReplacedActiveTick,
            replacement.Disposition);
        BehaviorTestCheck.Equal(1, replaced.PendingCount);
        BehaviorTestCheck.Equal(1, replaced.Groups[0].AcceptedCount);
        BehaviorTestCheck.Equal(
            2L,
            replaced.Groups[0].PendingItems.Single().RequestId);
        BehaviorTestCheck.Equal(
            TimeSpan.Zero,
            replaced.Groups[0].PendingItems.Single().FirstCreatedAt);

        var suppressionClock = new ManualDualClock();
        var suppressionRandom = new SequenceRandomSource();
        var suppressionQueue = new SoftBehaviorQueue(
            suppressionClock,
            suppressionRandom);
        EnqueueRange(suppressionQueue, suppressionClock, 1, 3);

        SoftQueueEnqueueResult suppressed = suppressionQueue.Enqueue(
            Request(
                4,
                suppressionClock.Elapsed,
                source: BehaviorRequestSource.ActiveTick));
        SoftBehaviorQueueSnapshot suppressionSnapshot =
            suppressionQueue.GetSnapshot();

        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.SuppressedActiveTick,
            suppressed.Disposition);
        BehaviorTestCheck.Equal(3, suppressionSnapshot.PendingCount);
        BehaviorTestCheck.Equal(0, suppressionRandom.Calls);
        BehaviorTestCheck.True(
            suppressionSnapshot.RecentDiagnostics.Any(diagnostic =>
                diagnostic.RequestId == 4 &&
                diagnostic.Detail == "pending-at-least-three"));
    }

    private static void StartedActiveGroupCannotBeEvicted()
    {
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource(0);
        var queue = new SoftBehaviorQueue(clock, random);
        EnqueueRange(queue, clock, 1, 3);

        BehaviorTestCheck.Equal(
            1L,
            BehaviorTestCheck.NotNull(queue.TryStartNext()).RequestId);
        EnqueueRange(queue, clock, 4, 11);
        BehaviorTestCheck.Equal(10, queue.GetSnapshot().PendingCount);

        SoftQueueEnqueueResult overflow =
            queue.Enqueue(Request(12, clock.Elapsed));
        SoftBehaviorQueueSnapshot snapshot = queue.GetSnapshot();

        BehaviorTestCheck.Equal(
            SoftQueueEnqueueDisposition.Enqueued,
            overflow.Disposition);
        BehaviorTestCheck.Equal(1, random.Calls);
        BehaviorTestCheck.Equal(2L, snapshot.Evictions.Single().GroupId);
        SoftQueueGroupSnapshot active =
            snapshot.Groups.Single(group => group.GroupId == 1);
        BehaviorTestCheck.True(active.HasStarted);
        BehaviorTestCheck.True(active.IsActive);
        BehaviorTestCheck.SequenceEqual(
            new long[] { 2, 3 },
            active.PendingItems.Select(static request => request.RequestId));
        BehaviorTestCheck.True(
            snapshot.Groups
                .SelectMany(static group => group.PendingItems)
                .Any(static request => request.RequestId == 12));
    }

    private static void QueueRejectsImmediateRequests()
    {
        var clock = new ManualDualClock();
        var queue =
            new SoftBehaviorQueue(clock, new SequenceRandomSource());
        BehaviorRequest immediate = Request(1, clock.Elapsed) with
        {
            SwitchMode = BehaviorSwitchMode.ImmediateIfIdle,
        };

        BehaviorTestCheck.Throws<ArgumentException>(
            () => queue.Enqueue(immediate));
        BehaviorTestCheck.Equal(0, queue.GetSnapshot().PendingCount);
    }

    private static BehaviorRequest Request(
        long requestId,
        TimeSpan now,
        BehaviorRequestSource source = BehaviorRequestSource.UserContext,
        string? dedupeKey = null,
        TimeSpan? expiresAfter = null,
        long contextRevision = 0) =>
        new()
        {
            RequestId = requestId,
            BehaviorId = new BehaviorId($"behavior-{requestId}"),
            Source = source,
            SwitchMode = BehaviorSwitchMode.Queued,
            Priority = 50,
            CreatedAt = now,
            FirstCreatedAt = now,
            ExpiresAt = now + (expiresAfter ?? TimeSpan.FromSeconds(60)),
            DedupeKey = dedupeKey ?? $"request-{requestId}",
            ContextRevision = contextRevision,
        };

    private static void EnqueueRange(
        SoftBehaviorQueue queue,
        ManualDualClock clock,
        int first,
        int last)
    {
        for (int requestId = first; requestId <= last; requestId++)
        {
            SoftQueueEnqueueResult result =
                queue.Enqueue(Request(requestId, clock.Elapsed));
            BehaviorTestCheck.Equal(
                SoftQueueEnqueueDisposition.Enqueued,
                result.Disposition);
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
}
