using System.IO;

namespace GuluPet.Postcards;

public sealed record PostcardProgressUpdate(
    PostcardCollectionState State,
    long AddedInputCount,
    IReadOnlyList<PostcardDefinition> NewlyUnlocked,
    long NewlyEarnedTravelStampCount = 0);

public sealed record PostcardOutingStatus(
    PostcardOutingPhase Phase,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? ArrivedAtUtc,
    TimeSpan Remaining,
    string? NextPostcardId);

public sealed record PostcardOutingUpdate(
    PostcardCollectionState State,
    PostcardOutingStatus Status,
    bool Changed);

public sealed record PostcardClaimResult(
    PostcardCollectionState State,
    PostcardDefinition Postcard);

/// <summary>
/// An immutable outing-start candidate. Persist <see cref="CandidateState"/>
/// before committing it so a failed write cannot leave a volatile outing in
/// progress.
/// </summary>
public sealed class PostcardOutingStartPlan
{
    private readonly PostcardCollectionState _candidateState;

    internal PostcardOutingStartPlan(
        PostcardProgressTracker owner,
        PostcardCollectionState expectedState,
        PostcardCollectionState candidateState,
        DateTimeOffset startedAtUtc)
    {
        Owner = owner;
        ExpectedState = expectedState;
        _candidateState = candidateState;
        StartedAtUtc = startedAtUtc;
    }

    internal PostcardProgressTracker Owner { get; }

    internal PostcardCollectionState ExpectedState { get; }

    internal PostcardCollectionState CandidateStateForCommit =>
        _candidateState;

    public PostcardCollectionState CandidateState =>
        PostcardCollectionState.ValidateAndSnapshot(_candidateState);

    public DateTimeOffset StartedAtUtc { get; }
}

/// <summary>
/// An immutable claim candidate. Callers may durably save
/// <see cref="CandidateState"/> before publishing it through
/// <see cref="PostcardProgressTracker.CommitClaimArrival"/>.
/// </summary>
public sealed class PostcardClaimPlan
{
    private readonly PostcardCollectionState _candidateState;

    internal PostcardClaimPlan(
        PostcardProgressTracker owner,
        PostcardCollectionState expectedState,
        PostcardCollectionState candidateState,
        PostcardDefinition postcard)
    {
        Owner = owner;
        ExpectedState = expectedState;
        _candidateState = candidateState;
        Postcard = postcard;
    }

    internal PostcardProgressTracker Owner { get; }

    internal PostcardCollectionState ExpectedState { get; }

    internal PostcardCollectionState CandidateStateForCommit =>
        _candidateState;

    public PostcardCollectionState CandidateState =>
        PostcardCollectionState.ValidateAndSnapshot(_candidateState);

    public PostcardDefinition Postcard { get; }
}

/// <summary>
/// Owns deterministic postcard-outing transitions. The historical input API
/// remains as a no-op compatibility surface until all runtime callers migrate;
/// input counters can no longer earn postcards.
/// </summary>
public sealed class PostcardProgressTracker
{
    public const long InputsPerUnlock = 1_000;

    private readonly object _gate = new();
    private readonly PostcardCatalog _catalog;
    private PostcardCollectionState _state;

    public PostcardProgressTracker(
        PostcardCatalog catalog,
        PostcardCollectionState? initialState = null)
    {
        _catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        _state = PostcardCollectionState.ValidateAndSnapshot(
            initialState ?? PostcardCollectionState.CreateDefault());
        _ = _catalog.GetUnlockedDefinitions(_state);
    }

    public PostcardCollectionState Current
    {
        get
        {
            lock (_gate)
            {
                return Snapshot(_state);
            }
        }
    }

    public long InputsUntilNextUnlock
    {
        get => 0;
    }

    public IReadOnlyList<PostcardDefinition> UnlockedDefinitions
    {
        get
        {
            lock (_gate)
            {
                return _catalog.GetUnlockedDefinitions(_state);
            }
        }
    }

    /// <summary>
    /// The stamp total is derived from durable input; only pending echo
    /// presentation metadata is stored.
    /// </summary>
    public long TravelStampCount
    {
        get => 0;
    }

    public PostcardProgressUpdate ObserveSessionCounts(
        long keyboardCount,
        long mouseClickCount,
        DateTimeOffset observedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keyboardCount);
        ArgumentOutOfRangeException.ThrowIfNegative(mouseClickCount);
        if (observedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedAtUtc),
                "An observation time is required.");
        }

        lock (_gate)
        {
            return new PostcardProgressUpdate(
                Snapshot(_state),
                AddedInputCount: 0,
                NewlyUnlocked: [],
                NewlyEarnedTravelStampCount: 0);
        }
    }

    public PostcardOutingStatus GetOutingStatus(DateTimeOffset observedAtUtc)
    {
        ValidateTimestamp(observedAtUtc, nameof(observedAtUtc));
        lock (_gate)
        {
            return CreateOutingStatus(_state, observedAtUtc);
        }
    }

    public PostcardOutingStartPlan PreviewStartOuting(
        DateTimeOffset startedAtUtc)
    {
        ValidateTimestamp(startedAtUtc, nameof(startedAtUtc));
        lock (_gate)
        {
            PostcardOutingStatus current = CreateOutingStatus(
                _state,
                startedAtUtc);
            if (current.Phase == PostcardOutingPhase.CollectionComplete)
            {
                throw new InvalidOperationException(
                    "The postcard collection is already complete.");
            }

            if (current.Phase != PostcardOutingPhase.AtHome)
            {
                throw new InvalidOperationException(
                    "A postcard outing is already active.");
            }

            DateTimeOffset readyAtUtc;
            try
            {
                readyAtUtc = startedAtUtc.Add(PostcardOutingRecord.Duration);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startedAtUtc),
                    startedAtUtc,
                    "The outing ready time is outside the supported range.");
            }

            PostcardCollectionState candidate =
                PostcardCollectionState.ValidateAndSnapshot(
                _state with
                {
                    CurrentOuting = new PostcardOutingRecord
                    {
                        StartedAtUtc = startedAtUtc,
                        ReadyAtUtc = readyAtUtc,
                    },
                    UpdatedAtUtc = Max(_state.UpdatedAtUtc, startedAtUtc),
                });
            return new PostcardOutingStartPlan(
                this,
                _state,
                candidate,
                startedAtUtc);
        }
    }

    public PostcardOutingUpdate CommitStartOuting(
        PostcardOutingStartPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (_gate)
        {
            if (!ReferenceEquals(plan.Owner, this) ||
                !ReferenceEquals(plan.ExpectedState, _state))
            {
                throw new InvalidOperationException(
                    "The postcard outing start plan is stale or belongs to another tracker.");
            }

            _state = plan.CandidateStateForCommit;
            return new PostcardOutingUpdate(
                Snapshot(_state),
                CreateOutingStatus(_state, plan.StartedAtUtc),
                Changed: true);
        }
    }

    public PostcardOutingUpdate StartOuting(DateTimeOffset startedAtUtc) =>
        CommitStartOuting(PreviewStartOuting(startedAtUtc));

    public PostcardOutingUpdate MarkArrived(DateTimeOffset observedAtUtc)
    {
        ValidateTimestamp(observedAtUtc, nameof(observedAtUtc));
        lock (_gate)
        {
            PostcardOutingRecord outing = _state.CurrentOuting
                ?? throw new InvalidOperationException(
                    "There is no active postcard outing.");
            if (outing.ArrivedAtUtc is not null)
            {
                return new PostcardOutingUpdate(
                    Snapshot(_state),
                    CreateOutingStatus(_state, observedAtUtc),
                    Changed: false);
            }

            if (observedAtUtc < outing.ReadyAtUtc)
            {
                return new PostcardOutingUpdate(
                    Snapshot(_state),
                    CreateOutingStatus(_state, observedAtUtc),
                    Changed: false);
            }

            DateTimeOffset arrivedAtUtc = Max(
                Max(observedAtUtc, outing.ReadyAtUtc),
                _state.UpdatedAtUtc);
            _state = PostcardCollectionState.ValidateAndSnapshot(
                _state with
                {
                    CurrentOuting = outing with
                    {
                        ArrivedAtUtc = arrivedAtUtc,
                    },
                    UpdatedAtUtc = arrivedAtUtc,
                });
            return new PostcardOutingUpdate(
                Snapshot(_state),
                CreateOutingStatus(_state, observedAtUtc),
                Changed: true);
        }
    }

    public PostcardClaimPlan PreviewClaimArrival(
        DateTimeOffset claimedAtUtc)
    {
        ValidateTimestamp(claimedAtUtc, nameof(claimedAtUtc));
        lock (_gate)
        {
            HashSet<string> unlockedIds = _state.Unlocked
                .Select(static record => record.PostcardId)
                .ToHashSet(StringComparer.Ordinal);
            PostcardDefinition? postcard = _catalog.Definitions
                .FirstOrDefault(definition => !unlockedIds.Contains(
                    definition.Id));
            if (postcard is null)
            {
                throw new InvalidOperationException(
                    "The postcard collection is already complete.");
            }

            PostcardOutingRecord outing = _state.CurrentOuting
                ?? throw new InvalidOperationException(
                    "There is no postcard waiting at the door.");
            if (outing.ArrivedAtUtc is not { } arrivedAtUtc)
            {
                throw new InvalidOperationException(
                    "The postcard outing has not arrived yet.");
            }

            DateTimeOffset effectiveClaimedAtUtc = Max(
                Max(claimedAtUtc, arrivedAtUtc),
                _state.UpdatedAtUtc);
            PostcardCollectionState candidate =
                PostcardCollectionState.ValidateAndSnapshot(
                    _state with
                    {
                        Unlocked =
                        [
                            .. _state.Unlocked.Select(
                                static record => record with { }),
                            new PostcardUnlockRecord
                            {
                                PostcardId = postcard.Id,
                                UnlockedAtUtc = effectiveClaimedAtUtc,
                            },
                        ],
                        CurrentOuting = null,
                        UpdatedAtUtc = effectiveClaimedAtUtc,
                    });
            return new PostcardClaimPlan(
                this,
                _state,
                candidate,
                postcard);
        }
    }

    public PostcardClaimResult CommitClaimArrival(PostcardClaimPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (_gate)
        {
            if (!ReferenceEquals(plan.Owner, this)
                || !ReferenceEquals(plan.ExpectedState, _state))
            {
                throw new InvalidOperationException(
                    "The postcard claim plan is stale or belongs to another tracker.");
            }

            _state = plan.CandidateStateForCommit;
            return new PostcardClaimResult(
                Snapshot(_state),
                plan.Postcard);
        }
    }

    public PostcardClaimResult ClaimArrival(DateTimeOffset claimedAtUtc) =>
        CommitClaimArrival(PreviewClaimArrival(claimedAtUtc));

    public PostcardCollectionState AcknowledgeNotification(
        string postcardId,
        DateTimeOffset acknowledgedAtUtc)
    {
        if (!PostcardCatalog.IsValidId(postcardId))
        {
            throw new ArgumentException(
                "A valid postcard id is required.",
                nameof(postcardId));
        }

        if (acknowledgedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acknowledgedAtUtc),
                "An acknowledgement time is required.");
        }

        lock (_gate)
        {
            PostcardUnlockRecord? existing = _state.Unlocked.FirstOrDefault(
                record => string.Equals(
                    record.PostcardId,
                    postcardId,
                    StringComparison.Ordinal));
            if (existing is null)
            {
                throw new InvalidOperationException(
                    $"Postcard '{postcardId}' is not unlocked.");
            }

            if (existing.NotificationAcknowledgedAtUtc is not null)
            {
                return Snapshot(_state);
            }

            if (acknowledgedAtUtc < existing.UnlockedAtUtc)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(acknowledgedAtUtc),
                    "A postcard cannot be acknowledged before it is unlocked.");
            }

            PostcardUnlockRecord[] unlocked = _state.Unlocked
                .Select(record =>
                    string.Equals(
                        record.PostcardId,
                        postcardId,
                        StringComparison.Ordinal)
                        ? record with
                        {
                            NotificationAcknowledgedAtUtc =
                                acknowledgedAtUtc,
                        }
                        : record with { })
                .ToArray();
            _state = _state with
            {
                Unlocked = unlocked,
                UpdatedAtUtc = acknowledgedAtUtc,
            };
            return Snapshot(_state);
        }
    }

    /// <summary>
    /// Marks catalog-growth unlocks as one durable notification batch. The
    /// individual cards remain unacknowledged until that summary is dismissed.
    /// </summary>
    public PostcardCollectionState RegisterCatalogSummary(
        IEnumerable<string> postcardIds,
        int totalUnlockedCount,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(postcardIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalUnlockedCount);
        if (createdAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(createdAtUtc),
                "A catalog summary creation time is required.");
        }

        lock (_gate)
        {
            var ids = new List<string>();
            PostcardCatalogSummaryRecord? existingSummary =
                _state.PendingCatalogSummary;
            if (existingSummary is not null)
            {
                ids.AddRange(existingSummary.PostcardIds);
            }

            DateTimeOffset effectiveCreatedAtUtc =
                existingSummary is not null
                && existingSummary.CreatedAtUtc > createdAtUtc
                    ? existingSummary.CreatedAtUtc
                    : createdAtUtc;

            ids.AddRange(postcardIds);
            string[] distinctIds = ids
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (distinctIds.Length == 0)
            {
                throw new ArgumentException(
                    "At least one reconciled postcard id is required.",
                    nameof(postcardIds));
            }

            IReadOnlyDictionary<string, PostcardUnlockRecord> unlockedById =
                _state.Unlocked.ToDictionary(
                    static record => record.PostcardId,
                    StringComparer.Ordinal);
            foreach (string postcardId in distinctIds)
            {
                if (!unlockedById.TryGetValue(
                        postcardId,
                        out PostcardUnlockRecord? record))
                {
                    throw new InvalidOperationException(
                        $"Postcard '{postcardId}' is not unlocked.");
                }

                if (record.NotificationAcknowledgedAtUtc is not null)
                {
                    throw new InvalidOperationException(
                        $"Postcard '{postcardId}' is already acknowledged.");
                }

                if (effectiveCreatedAtUtc < record.UnlockedAtUtc)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(createdAtUtc),
                        "A catalog summary cannot predate its postcards.");
                }
            }

            _state = _state with
            {
                PendingCatalogSummary = new PostcardCatalogSummaryRecord
                {
                    PostcardIds = distinctIds,
                    TotalUnlockedCount = Math.Max(
                        totalUnlockedCount,
                        distinctIds.Length),
                    CreatedAtUtc = effectiveCreatedAtUtc,
                },
                UpdatedAtUtc = effectiveCreatedAtUtc > _state.UpdatedAtUtc
                    ? effectiveCreatedAtUtc
                    : _state.UpdatedAtUtc,
            };
            return Snapshot(_state);
        }
    }

    /// <summary>
    /// Acknowledges only the ids captured by the pending catalog summary and
    /// clears that marker as one in-memory state transition.
    /// </summary>
    public PostcardCollectionState AcknowledgeCatalogSummary(
        DateTimeOffset acknowledgedAtUtc)
    {
        if (acknowledgedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acknowledgedAtUtc),
                "An acknowledgement time is required.");
        }

        lock (_gate)
        {
            PostcardCatalogSummaryRecord pending =
                _state.PendingCatalogSummary
                ?? throw new InvalidOperationException(
                    "There is no pending postcard catalog summary.");
            var pendingIds = pending.PostcardIds.ToHashSet(
                StringComparer.Ordinal);
            PostcardUnlockRecord[] unlocked = _state.Unlocked
                .Select(record =>
                {
                    if (!pendingIds.Contains(record.PostcardId))
                    {
                        return record with { };
                    }

                    if (acknowledgedAtUtc < record.UnlockedAtUtc)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(acknowledgedAtUtc),
                            "A postcard cannot be acknowledged before it is " +
                            "unlocked.");
                    }

                    return record with
                    {
                        NotificationAcknowledgedAtUtc = acknowledgedAtUtc,
                    };
                })
                .ToArray();
            _state = _state with
            {
                Unlocked = unlocked,
                PendingCatalogSummary = null,
                UpdatedAtUtc = acknowledgedAtUtc,
            };
            return Snapshot(_state);
        }
    }

    /// <summary>
    /// Adds post-collection stamp feedback to one durable pending echo. New
    /// observations merge into the pending counts until that echo is shown.
    /// </summary>
    public PostcardCollectionState RegisterTravelEcho(
        long newlyEarnedStampCount,
        long totalStampCount,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            newlyEarnedStampCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalStampCount);
        if (newlyEarnedStampCount > totalStampCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newlyEarnedStampCount),
                "New travel stamps cannot exceed the total stamp count.");
        }

        if (createdAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(createdAtUtc),
                "A travel echo creation time is required.");
        }

        lock (_gate)
        {
            PostcardTravelEchoRecord? pending = _state.PendingTravelEcho;
            if (pending is not null && totalStampCount < pending.TotalStampCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalStampCount),
                    "Travel stamp totals cannot move backwards.");
            }

            long mergedNewCount = SaturatingAdd(
                pending?.NewlyEarnedStampCount ?? 0,
                newlyEarnedStampCount);
            if (mergedNewCount > totalStampCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newlyEarnedStampCount),
                    "Merged new travel stamps cannot exceed the total.");
            }

            _state = _state with
            {
                PendingTravelEcho = new PostcardTravelEchoRecord
                {
                    NewlyEarnedStampCount = mergedNewCount,
                    TotalStampCount = totalStampCount,
                    CreatedAtUtc = pending?.CreatedAtUtc ?? createdAtUtc,
                },
                UpdatedAtUtc = createdAtUtc > _state.UpdatedAtUtc
                    ? createdAtUtc
                    : _state.UpdatedAtUtc,
            };
            return Snapshot(_state);
        }
    }

    public PostcardCollectionState AcknowledgeTravelEcho(
        long totalStampCount,
        DateTimeOffset acknowledgedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalStampCount);
        if (acknowledgedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acknowledgedAtUtc),
                "An acknowledgement time is required.");
        }

        lock (_gate)
        {
            PostcardTravelEchoRecord pending = _state.PendingTravelEcho
                ?? throw new InvalidOperationException(
                    "There is no pending travel echo.");
            if (pending.TotalStampCount != totalStampCount)
            {
                throw new InvalidOperationException(
                    "The dismissed travel echo is no longer current.");
            }

            if (acknowledgedAtUtc < pending.CreatedAtUtc)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(acknowledgedAtUtc),
                    "A travel echo cannot be acknowledged before creation.");
            }

            _state = _state with
            {
                PendingTravelEcho = null,
                UpdatedAtUtc = acknowledgedAtUtc,
            };
            return Snapshot(_state);
        }
    }

    private static long SaturatingAdd(long first, long second) =>
        first > long.MaxValue - second
            ? long.MaxValue
            : first + second;

    private PostcardOutingStatus CreateOutingStatus(
        PostcardCollectionState state,
        DateTimeOffset observedAtUtc)
    {
        HashSet<string> unlockedIds = state.Unlocked
            .Select(static record => record.PostcardId)
            .ToHashSet(StringComparer.Ordinal);
        PostcardDefinition? nextPostcard = _catalog.Definitions
            .FirstOrDefault(definition => !unlockedIds.Contains(definition.Id));
        if (nextPostcard is null)
        {
            return new PostcardOutingStatus(
                PostcardOutingPhase.CollectionComplete,
                state.CurrentOuting?.StartedAtUtc,
                state.CurrentOuting?.ReadyAtUtc,
                state.CurrentOuting?.ArrivedAtUtc,
                TimeSpan.Zero,
                NextPostcardId: null);
        }

        string nextPostcardId = nextPostcard.Id;
        if (state.CurrentOuting is not { } outing)
        {
            return new PostcardOutingStatus(
                PostcardOutingPhase.AtHome,
                StartedAtUtc: null,
                ReadyAtUtc: null,
                ArrivedAtUtc: null,
                TimeSpan.Zero,
                nextPostcardId);
        }

        if (outing.ArrivedAtUtc is not null)
        {
            return new PostcardOutingStatus(
                PostcardOutingPhase.WaitingAtDoor,
                outing.StartedAtUtc,
                outing.ReadyAtUtc,
                outing.ArrivedAtUtc,
                TimeSpan.Zero,
                nextPostcardId);
        }

        TimeSpan remaining = outing.ReadyAtUtc - observedAtUtc;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }
        else if (remaining > PostcardOutingRecord.Duration)
        {
            remaining = PostcardOutingRecord.Duration;
        }

        return new PostcardOutingStatus(
            PostcardOutingPhase.Traveling,
            outing.StartedAtUtc,
            outing.ReadyAtUtc,
            ArrivedAtUtc: null,
            remaining,
            nextPostcardId);
    }

    private static void ValidateTimestamp(
        DateTimeOffset timestamp,
        string parameterName)
    {
        if (timestamp == default)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "A postcard outing timestamp is required.");
        }
    }

    private static DateTimeOffset Max(
        DateTimeOffset first,
        DateTimeOffset second) =>
        first >= second ? first : second;

    private static PostcardCollectionState Snapshot(
        PostcardCollectionState state) =>
        state with
        {
            Unlocked = state.Unlocked
                .Select(static record => record with { })
                .ToArray(),
            PendingCatalogSummary = state.PendingCatalogSummary is null
                ? null
                : state.PendingCatalogSummary with
                {
                    PostcardIds = state.PendingCatalogSummary.PostcardIds
                        .ToArray(),
                },
            PendingTravelEcho = state.PendingTravelEcho is null
                ? null
                : state.PendingTravelEcho with { },
            CurrentOuting = state.CurrentOuting is null
                ? null
                : state.CurrentOuting with { },
        };
}
