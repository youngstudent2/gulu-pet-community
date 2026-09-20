namespace GuluPet.Behavior;

public enum SoftQueueEnqueueDisposition
{
    Enqueued,
    Refreshed,
    ReplacedActiveTick,
    SuppressedActiveTick,
    RejectedExpired,
    RejectedCapacity
}

public enum SoftQueueRemovalReason
{
    Expired,
    CapacityEviction,
    SupersededByAutomaticWake
}

public sealed record SoftQueueEnqueueResult
{
    public required SoftQueueEnqueueDisposition Disposition { get; init; }

    public required long RequestId { get; init; }

    public long? GroupId { get; init; }

    public bool RandomConsumed { get; init; }

    public string? Diagnostic { get; init; }
}

public sealed record SoftQueueGroupSnapshot
{
    public required long GroupId { get; init; }

    public required int AcceptedCount { get; init; }

    public required bool HasStarted { get; init; }

    public required bool Closed { get; init; }

    public required bool IsActive { get; init; }

    public required IReadOnlyList<BehaviorRequest> PendingItems { get; init; }
}

public sealed record SoftQueueRemovalRecord
{
    public required TimeSpan OccurredAt { get; init; }

    public required SoftQueueRemovalReason Reason { get; init; }

    public required long GroupId { get; init; }

    public required IReadOnlyList<long> RequestIds { get; init; }

    public required IReadOnlyList<BehaviorId> BehaviorIds { get; init; }
}

public sealed record SoftQueueEvictionRecord
{
    public required TimeSpan OccurredAt { get; init; }

    public required long GroupId { get; init; }

    public required IReadOnlyList<long> RequestIds { get; init; }

    public required IReadOnlyList<BehaviorId> BehaviorIds { get; init; }
}

public sealed record SoftQueueDiagnosticRecord
{
    public required TimeSpan OccurredAt { get; init; }

    public required string Operation { get; init; }

    public required string Outcome { get; init; }

    public long? RequestId { get; init; }

    public long? GroupId { get; init; }

    public string? Detail { get; init; }
}

public sealed record SoftBehaviorQueueSnapshot
{
    public required TimeSpan CapturedAt { get; init; }

    public required int PendingCount { get; init; }

    public required BehaviorRequest? ActiveRequest { get; init; }

    public required long? ActiveGroupId { get; init; }

    public required TimeSpan? ResumeAfter { get; init; }

    public required IReadOnlyList<SoftQueueGroupSnapshot> Groups { get; init; }

    public required IReadOnlyList<SoftQueueEvictionRecord> Evictions { get; init; }

    public required IReadOnlyList<SoftQueueRemovalRecord> RecentRemovals { get; init; }

    public required IReadOnlyList<SoftQueueDiagnosticRecord> RecentDiagnostics { get; init; }
}

/// <summary>
/// Owns pending soft behavior requests. The queue is safe to call from multiple
/// producers, although runtime execution should still use a single dispatcher
/// as the owner of behavior and animation state.
/// </summary>
public sealed class SoftBehaviorQueue
{
    public const int MaximumPendingItems = 10;
    public const int MaximumAcceptedPerGroup = 3;

    public static readonly TimeSpan InterGroupDelay = TimeSpan.FromSeconds(5);

    // Prevent indefinitely refreshed proactive/context requests from becoming
    // stale. Scheduled and weather requests keep their explicit ExpiresAt.
    public static readonly TimeSpan MaximumResidence = TimeSpan.FromSeconds(120);

    private const int MaximumDiagnosticHistory = 64;
    private const int MaximumRemovalHistory = 64;

    private readonly object _gate = new();
    private readonly IMonotonicClock _clock;
    private readonly IRandomSource _evictionRandom;
    private readonly List<GroupState> _groups = [];
    private readonly List<SoftQueueEvictionRecord> _evictions = [];
    private readonly List<SoftQueueRemovalRecord> _removals = [];
    private readonly List<SoftQueueDiagnosticRecord> _diagnostics = [];

    private long _nextGroupId = 1;
    private int _pendingCount;
    private long? _activeGroupId;
    private BehaviorRequest? _activeRequest;
    private TimeSpan? _resumeAfter;

    public SoftBehaviorQueue(
        IMonotonicClock clock,
        IRandomSource evictionRandom)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _evictionRandom =
            evictionRandom ?? throw new ArgumentNullException(nameof(evictionRandom));
    }

    public SoftQueueEnqueueResult Enqueue(BehaviorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (request.SwitchMode != BehaviorSwitchMode.Queued)
        {
            throw new ArgumentException(
                "SoftBehaviorQueue only accepts queued behavior requests.",
                nameof(request));
        }

        lock (_gate)
        {
            var now = _clock.Elapsed;
            CleanupExpiredCore(now);

            var normalized = NormalizeInitialRequest(request, now);
            if (normalized is null)
            {
                return RecordEnqueueResult(
                    now,
                    request,
                    SoftQueueEnqueueDisposition.RejectedExpired,
                    diagnostic: "request-expired");
            }

            var duplicate = FindByDedupeKey(normalized.DedupeKey);
            if (duplicate is not null)
            {
                var refreshed = RefreshRequest(duplicate.Request, normalized, now);
                if (refreshed is null)
                {
                    return RecordEnqueueResult(
                        now,
                        request,
                        SoftQueueEnqueueDisposition.RejectedExpired,
                        duplicate.GroupId,
                        diagnostic: "refresh-expired");
                }

                duplicate.Replace(refreshed);
                return RecordEnqueueResult(
                    now,
                    refreshed,
                    SoftQueueEnqueueDisposition.Refreshed,
                    duplicate.GroupId,
                    diagnostic: "dedupe-refreshed");
            }

            if (normalized.Source == BehaviorRequestSource.ActiveTick)
            {
                if (_pendingCount >= MaximumAcceptedPerGroup)
                {
                    return RecordEnqueueResult(
                        now,
                        normalized,
                        SoftQueueEnqueueDisposition.SuppressedActiveTick,
                        diagnostic: "pending-at-least-three");
                }

                var oldActiveTick = FindPendingActiveTick();
                if (oldActiveTick is not null)
                {
                    var replacement = ReplaceActiveTick(
                        oldActiveTick.Request,
                        normalized,
                        now);
                    if (replacement is null)
                    {
                        return RecordEnqueueResult(
                            now,
                            normalized,
                            SoftQueueEnqueueDisposition.RejectedExpired,
                            oldActiveTick.GroupId,
                            diagnostic: "active-tick-replacement-expired");
                    }

                    oldActiveTick.Replace(replacement);
                    return RecordEnqueueResult(
                        now,
                        replacement,
                        SoftQueueEnqueueDisposition.ReplacedActiveTick,
                        oldActiveTick.GroupId,
                        diagnostic: "active-tick-replaced");
                }
            }

            bool randomConsumed = false;
            if (_pendingCount + 1 > MaximumPendingItems)
            {
                var evictable = _groups
                    .Where(static group =>
                        !group.HasStarted && group.PendingItems.Count > 0)
                    .ToArray();
                if (evictable.Length == 0)
                {
                    return RecordEnqueueResult(
                        now,
                        normalized,
                        SoftQueueEnqueueDisposition.RejectedCapacity,
                        randomConsumed: false,
                        diagnostic: "no-evictable-group");
                }

                int index = SelectEvictionIndex(evictable.Length);
                randomConsumed = true;
                EvictGroup(evictable[index], now);
            }

            var group = GetOrCreateAcceptingGroup();
            group.PendingItems.Add(normalized);
            group.AcceptedCount++;
            _pendingCount++;
            if (group.AcceptedCount >= MaximumAcceptedPerGroup)
            {
                group.Closed = true;
            }

            return RecordEnqueueResult(
                now,
                normalized,
                SoftQueueEnqueueDisposition.Enqueued,
                group.GroupId,
                randomConsumed,
                "queued");
        }
    }

    public BehaviorRequest? TryStartNext()
    {
        lock (_gate)
        {
            var now = _clock.Elapsed;
            CleanupExpiredCore(now);

            if (_activeRequest is not null)
            {
                return null;
            }

            if (_resumeAfter is { } resumeAfter)
            {
                if (now < resumeAfter)
                {
                    return null;
                }

                _resumeAfter = null;
            }

            RemoveEmptyUnstartedGroups();
            if (_groups.Count == 0)
            {
                return null;
            }

            var group = _groups[0];
            group.HasStarted = true;
            _activeGroupId = group.GroupId;

            var request = group.PendingItems[0];
            group.PendingItems.RemoveAt(0);
            _pendingCount--;
            _activeRequest = request;

            RecordDiagnostic(
                now,
                "start",
                "started",
                request.RequestId,
                group.GroupId);
            return request;
        }
    }

    public bool CompleteActiveBehavior()
    {
        lock (_gate)
        {
            if (_activeRequest is null || _activeGroupId is null)
            {
                return false;
            }

            var now = _clock.Elapsed;
            var completed = _activeRequest;
            _activeRequest = null;
            CleanupExpiredCore(now);

            var group = FindGroup(_activeGroupId.Value);
            if (group is not null && group.PendingItems.Count > 0)
            {
                RecordDiagnostic(
                    now,
                    "complete",
                    "same-group-ready",
                    completed.RequestId,
                    group.GroupId);
                return true;
            }

            if (group is not null)
            {
                group.Closed = true;
                _groups.Remove(group);
            }

            _activeGroupId = null;
            RemoveEmptyUnstartedGroups();
            _resumeAfter =
                _groups.Count > 0 ? AddSaturated(now, InterGroupDelay) : null;

            RecordDiagnostic(
                now,
                "complete",
                _resumeAfter is null ? "queue-empty" : "inter-group-wait",
                completed.RequestId,
                group?.GroupId);
            return true;
        }
    }

    /// <summary>
    /// Removes matching requests that have not started. The active request and
    /// its execution gate are intentionally left untouched.
    /// </summary>
    public IReadOnlyList<BehaviorRequest> DiscardPending(
        Func<BehaviorRequest, bool> predicate,
        SoftQueueRemovalReason reason)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        lock (_gate)
        {
            var now = _clock.Elapsed;
            CleanupExpiredCore(now);
            var discarded = new List<BehaviorRequest>();

            foreach (GroupState group in _groups.ToArray())
            {
                var removedFromGroup = new List<BehaviorRequest>();
                var retained = new List<BehaviorRequest>(
                    group.PendingItems.Count);
                foreach (BehaviorRequest request in group.PendingItems)
                {
                    if (predicate(request))
                    {
                        removedFromGroup.Add(request);
                    }
                    else
                    {
                        retained.Add(request);
                    }
                }

                if (removedFromGroup.Count == 0)
                {
                    continue;
                }

                group.PendingItems.Clear();
                group.PendingItems.AddRange(retained);
                _pendingCount -= removedFromGroup.Count;
                discarded.AddRange(removedFromGroup);
                RecordRemoval(
                    now,
                    reason,
                    group.GroupId,
                    removedFromGroup);
            }

            RemoveEmptyUnstartedGroups();
            if (_groups.Count == 0 && _activeRequest is null)
            {
                _resumeAfter = null;
            }

            RecordDiagnostic(
                now,
                "discard-pending",
                discarded.Count == 0 ? "no-match" : "discarded",
                detail: $"reason:{reason};removed:{discarded.Count}");
            return discarded.ToArray();
        }
    }

    public int CleanupExpired()
    {
        lock (_gate)
        {
            return CleanupExpiredCore(_clock.Elapsed);
        }
    }

    public SoftBehaviorQueueSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var now = _clock.Elapsed;
            CleanupExpiredCore(now);
            return new SoftBehaviorQueueSnapshot
            {
                CapturedAt = now,
                PendingCount = _pendingCount,
                ActiveRequest = _activeRequest,
                ActiveGroupId = _activeGroupId,
                ResumeAfter = _resumeAfter,
                Groups = _groups
                    .Select(group => new SoftQueueGroupSnapshot
                    {
                        GroupId = group.GroupId,
                        AcceptedCount = group.AcceptedCount,
                        HasStarted = group.HasStarted,
                        Closed = group.Closed,
                        IsActive = group.GroupId == _activeGroupId,
                        PendingItems = group.PendingItems.ToArray(),
                    })
                    .ToArray(),
                Evictions = _evictions.ToArray(),
                RecentRemovals = _removals.ToArray(),
                RecentDiagnostics = _diagnostics.ToArray(),
            };
        }
    }

    private BehaviorRequest? NormalizeInitialRequest(
        BehaviorRequest request,
        TimeSpan now)
    {
        var expiresAt = ApplyRefreshStalenessLimit(
            request.Source,
            request.FirstCreatedAt,
            request.ExpiresAt);
        if (expiresAt <= now || expiresAt <= request.CreatedAt)
        {
            return null;
        }

        return request with { ExpiresAt = expiresAt };
    }

    private static BehaviorRequest? RefreshRequest(
        BehaviorRequest existing,
        BehaviorRequest incoming,
        TimeSpan now)
    {
        var expiresAt = ApplyRefreshStalenessLimit(
            existing.Source,
            existing.FirstCreatedAt,
            incoming.ExpiresAt);
        if (expiresAt <= now || expiresAt <= incoming.CreatedAt)
        {
            return null;
        }

        return existing with
        {
            Priority = incoming.Priority,
            CreatedAt = incoming.CreatedAt,
            ExpiresAt = expiresAt,
            ContextRevision = incoming.ContextRevision,
            Parameters = incoming.Parameters,
        };
    }

    private static BehaviorRequest? ReplaceActiveTick(
        BehaviorRequest existing,
        BehaviorRequest incoming,
        TimeSpan now)
    {
        var expiresAt = ApplyRefreshStalenessLimit(
            BehaviorRequestSource.ActiveTick,
            existing.FirstCreatedAt,
            incoming.ExpiresAt);
        if (expiresAt <= now || expiresAt <= incoming.CreatedAt)
        {
            return null;
        }

        return incoming with
        {
            FirstCreatedAt = existing.FirstCreatedAt,
            ExpiresAt = expiresAt,
        };
    }

    private RequestLocation? FindByDedupeKey(string dedupeKey)
    {
        if (_activeRequest is not null &&
            string.Equals(
                _activeRequest.DedupeKey,
                dedupeKey,
                StringComparison.Ordinal))
        {
            return new RequestLocation(
                _activeRequest,
                _activeGroupId,
                replacement => _activeRequest = replacement);
        }

        foreach (var group in _groups)
        {
            for (int index = 0; index < group.PendingItems.Count; index++)
            {
                var request = group.PendingItems[index];
                if (!string.Equals(
                        request.DedupeKey,
                        dedupeKey,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                int capturedIndex = index;
                return new RequestLocation(
                    request,
                    group.GroupId,
                    replacement => group.PendingItems[capturedIndex] = replacement);
            }
        }

        return null;
    }

    private RequestLocation? FindPendingActiveTick()
    {
        foreach (var group in _groups)
        {
            for (int index = 0; index < group.PendingItems.Count; index++)
            {
                var request = group.PendingItems[index];
                if (request.Source != BehaviorRequestSource.ActiveTick)
                {
                    continue;
                }

                int capturedIndex = index;
                return new RequestLocation(
                    request,
                    group.GroupId,
                    replacement => group.PendingItems[capturedIndex] = replacement);
            }
        }

        return null;
    }

    private GroupState GetOrCreateAcceptingGroup()
    {
        if (_activeGroupId is { } activeGroupId)
        {
            var activeGroup = FindGroup(activeGroupId);
            if (activeGroup is not null &&
                !activeGroup.Closed &&
                activeGroup.AcceptedCount < MaximumAcceptedPerGroup)
            {
                return activeGroup;
            }
        }

        if (_groups.Count > 0)
        {
            var tail = _groups[^1];
            if (!tail.HasStarted &&
                !tail.Closed &&
                tail.AcceptedCount < MaximumAcceptedPerGroup)
            {
                return tail;
            }
        }

        var created = new GroupState(_nextGroupId++);
        _groups.Add(created);
        return created;
    }

    private int SelectEvictionIndex(int count)
    {
        var value = _evictionRandom.NextUnit();
        if (value is < 0 or > 1 || double.IsNaN(value))
        {
            throw new InvalidOperationException(
                "The queue eviction random source must return a value in the inclusive range 0 to 1.");
        }

        var normalized = Math.Min(value, Math.BitDecrement(1d));
        return Math.Min((int)(normalized * count), count - 1);
    }

    private void EvictGroup(GroupState group, TimeSpan now)
    {
        var removed = group.PendingItems.ToArray();
        _pendingCount -= removed.Length;
        _groups.Remove(group);

        var eviction = new SoftQueueEvictionRecord
        {
            OccurredAt = now,
            GroupId = group.GroupId,
            RequestIds = removed.Select(static request => request.RequestId).ToArray(),
            BehaviorIds = removed.Select(static request => request.BehaviorId).ToArray(),
        };
        _evictions.Add(eviction);
        TrimHistory(_evictions, MaximumRemovalHistory);
        RecordRemoval(
            now,
            SoftQueueRemovalReason.CapacityEviction,
            group.GroupId,
            removed);
        RecordDiagnostic(
            now,
            "evict",
            "group-evicted",
            groupId: group.GroupId,
            detail: $"removed:{removed.Length}");
    }

    private int CleanupExpiredCore(TimeSpan now)
    {
        int removedCount = 0;
        foreach (var group in _groups.ToArray())
        {
            var expired = group.PendingItems
                .Where(request => request.IsExpired(now))
                .ToArray();
            if (expired.Length == 0)
            {
                continue;
            }

            group.PendingItems.RemoveAll(request => request.IsExpired(now));
            _pendingCount -= expired.Length;
            removedCount += expired.Length;
            RecordRemoval(
                now,
                SoftQueueRemovalReason.Expired,
                group.GroupId,
                expired);
        }

        RemoveEmptyUnstartedGroups();
        if (_groups.Count == 0 && _activeRequest is null)
        {
            _resumeAfter = null;
        }

        if (removedCount > 0)
        {
            RecordDiagnostic(
                now,
                "cleanup",
                "expired-removed",
                detail: $"removed:{removedCount}");
        }

        return removedCount;
    }

    private void RemoveEmptyUnstartedGroups()
    {
        _groups.RemoveAll(group =>
            !group.HasStarted && group.PendingItems.Count == 0);
    }

    private GroupState? FindGroup(long groupId) =>
        _groups.FirstOrDefault(group => group.GroupId == groupId);

    private void RecordRemoval(
        TimeSpan now,
        SoftQueueRemovalReason reason,
        long groupId,
        IReadOnlyList<BehaviorRequest> requests)
    {
        _removals.Add(
            new SoftQueueRemovalRecord
            {
                OccurredAt = now,
                Reason = reason,
                GroupId = groupId,
                RequestIds =
                    requests.Select(static request => request.RequestId).ToArray(),
                BehaviorIds =
                    requests.Select(static request => request.BehaviorId).ToArray(),
            });
        TrimHistory(_removals, MaximumRemovalHistory);
    }

    private SoftQueueEnqueueResult RecordEnqueueResult(
        TimeSpan now,
        BehaviorRequest request,
        SoftQueueEnqueueDisposition disposition,
        long? groupId = null,
        bool randomConsumed = false,
        string? diagnostic = null)
    {
        RecordDiagnostic(
            now,
            "enqueue",
            disposition.ToString(),
            request.RequestId,
            groupId,
            diagnostic);
        return new SoftQueueEnqueueResult
        {
            Disposition = disposition,
            RequestId = request.RequestId,
            GroupId = groupId,
            RandomConsumed = randomConsumed,
            Diagnostic = diagnostic,
        };
    }

    private void RecordDiagnostic(
        TimeSpan now,
        string operation,
        string outcome,
        long? requestId = null,
        long? groupId = null,
        string? detail = null)
    {
        _diagnostics.Add(
            new SoftQueueDiagnosticRecord
            {
                OccurredAt = now,
                Operation = operation,
                Outcome = outcome,
                RequestId = requestId,
                GroupId = groupId,
                Detail = detail,
            });
        TrimHistory(_diagnostics, MaximumDiagnosticHistory);
    }

    private static void TrimHistory<T>(List<T> history, int maximum)
    {
        if (history.Count > maximum)
        {
            history.RemoveRange(0, history.Count - maximum);
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private static TimeSpan ApplyRefreshStalenessLimit(
        BehaviorRequestSource source,
        TimeSpan firstCreatedAt,
        TimeSpan requestedExpiry)
    {
        if (source is not (
                BehaviorRequestSource.ActiveTick or
                BehaviorRequestSource.UserContext))
        {
            return requestedExpiry;
        }

        return Min(
            requestedExpiry,
            AddSaturated(firstCreatedAt, MaximumResidence));
    }

    private static TimeSpan AddSaturated(TimeSpan value, TimeSpan delta) =>
        value > TimeSpan.MaxValue - delta
            ? TimeSpan.MaxValue
            : value + delta;

    private sealed class GroupState(long groupId)
    {
        public long GroupId { get; } = groupId;

        public int AcceptedCount { get; set; }

        public List<BehaviorRequest> PendingItems { get; } = [];

        public bool HasStarted { get; set; }

        public bool Closed { get; set; }
    }

    private sealed class RequestLocation(
        BehaviorRequest request,
        long? groupId,
        Action<BehaviorRequest> replace)
    {
        public BehaviorRequest Request { get; } = request;

        public long? GroupId { get; } = groupId;

        public void Replace(BehaviorRequest replacement) => replace(replacement);
    }
}
