using System.IO;

namespace GuluPet.Memories;

public sealed record MemoryProgressUpdate(
    MemoryProgressState State,
    long AddedAcceptedInteractionCount,
    MemoryDefinition? NewlyUnlockedPending);

/// <summary>
/// Converts accepted, deliberate pet interactions into a single durable
/// pending memory. It does not queue a second memory while playback is pending.
/// </summary>
public sealed class MemoryProgressTracker
{
    private readonly object _gate = new();
    private readonly MemoryCatalog _catalog;
    private MemoryProgressState _state;

    public MemoryProgressTracker(
        MemoryCatalog? catalog = null,
        MemoryProgressState? initialState = null)
    {
        _catalog = catalog ?? MemoryCatalog.Default;
        _state = MemoryProgressState.ValidateAndSnapshot(
            initialState ?? MemoryProgressState.CreateDefault());
        ValidateAgainstCatalog(_state);
    }

    public MemoryProgressState Current
    {
        get
        {
            lock (_gate)
            {
                return Snapshot(_state);
            }
        }
    }

    public MemoryDefinition? PendingDefinition
    {
        get
        {
            lock (_gate)
            {
                if (_state.Pending is not { } pending)
                {
                    return null;
                }

                return _catalog.TryGet(
                    pending.MemoryId,
                    out MemoryDefinition? definition)
                    ? definition
                    : throw new InvalidDataException(
                        $"Pending memory '{pending.MemoryId}' is not present " +
                        "in the current catalog.");
            }
        }
    }

    public long AcceptedInteractionsUntilNextUnlock
    {
        get
        {
            lock (_gate)
            {
                if (_state.Pending is not null)
                {
                    return 0;
                }

                MemoryDefinition? next = GetNextDefinition(_state);
                if (next is null)
                {
                    return 0;
                }

                return Math.Max(
                    0,
                    next.UnlockAtAcceptedInteractionCount
                    - _state.AcceptedInteractionCount);
            }
        }
    }

    public MemoryProgressUpdate RecordAcceptedInteraction(
        DateTimeOffset acceptedAtUtc)
    {
        if (acceptedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acceptedAtUtc),
                "An accepted interaction time is required.");
        }

        lock (_gate)
        {
            if (_state.Pending is not null)
            {
                throw new InvalidOperationException(
                    "Cannot record an accepted interaction while a memory " +
                    "playback is pending.");
            }

            long total = _state.AcceptedInteractionCount == long.MaxValue
                ? long.MaxValue
                : _state.AcceptedInteractionCount + 1;
            long added = total - _state.AcceptedInteractionCount;
            MemoryDefinition? next = GetNextDefinition(_state);
            MemoryDefinition? newlyUnlocked = null;
            PendingMemoryPlayback? pending = null;
            if (next is not null
                && total >= next.UnlockAtAcceptedInteractionCount)
            {
                pending = new PendingMemoryPlayback
                {
                    MemoryId = next.Id,
                    UnlockedAtUtc = acceptedAtUtc,
                };
                newlyUnlocked = next;
            }

            _state = new MemoryProgressState
            {
                AcceptedInteractionCount = total,
                Pending = pending,
                Completed = _state.Completed
                    .Select(static record => record with { })
                    .ToArray(),
                UpdatedAtUtc = LaterOf(
                    _state.UpdatedAtUtc,
                    acceptedAtUtc),
            };
            return new MemoryProgressUpdate(
                Snapshot(_state),
                added,
                newlyUnlocked);
        }
    }

    /// <summary>
    /// Clears the singular pending item only after the presentation layer
    /// observes natural media completion. Closing, cancellation, failure, or
    /// process exit must not call this method.
    /// </summary>
    public MemoryProgressState RecordNaturalPlaybackCompleted(
        string memoryId,
        DateTimeOffset completedAtUtc)
    {
        if (!MemoryCatalog.IsValidId(memoryId))
        {
            throw new ArgumentException(
                "A valid memory id is required.",
                nameof(memoryId));
        }

        if (completedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAtUtc),
                "A natural playback completion time is required.");
        }

        lock (_gate)
        {
            PendingMemoryPlayback pending = _state.Pending
                ?? throw new InvalidOperationException(
                    "No memory playback is pending.");
            if (!string.Equals(
                    pending.MemoryId,
                    memoryId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Memory '{memoryId}' cannot complete while " +
                    $"'{pending.MemoryId}' is pending.");
            }

            if (completedAtUtc < pending.UnlockedAtUtc)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(completedAtUtc),
                    "A memory cannot complete before it is unlocked.");
            }

            var completed = _state.Completed
                .Select(static record => record with { })
                .ToList();
            completed.Add(
                new CompletedMemoryPlayback
                {
                    MemoryId = pending.MemoryId,
                    UnlockedAtUtc = pending.UnlockedAtUtc,
                    CompletedAtUtc = completedAtUtc,
                });
            _state = _state with
            {
                Pending = null,
                Completed = completed.ToArray(),
                UpdatedAtUtc = LaterOf(
                    _state.UpdatedAtUtc,
                    completedAtUtc),
            };
            return Snapshot(_state);
        }
    }

    private MemoryDefinition? GetNextDefinition(
        MemoryProgressState state)
    {
        HashSet<string> completedIds = state.Completed
            .Select(static record => record.MemoryId)
            .ToHashSet(StringComparer.Ordinal);
        return _catalog.Definitions.FirstOrDefault(
            definition => !completedIds.Contains(definition.Id));
    }

    private void ValidateAgainstCatalog(MemoryProgressState state)
    {
        HashSet<string> completedIds = state.Completed
            .Select(static record => record.MemoryId)
            .ToHashSet(StringComparer.Ordinal);
        bool prefixEnded = false;
        foreach (MemoryDefinition definition in _catalog.Definitions)
        {
            if (!completedIds.Contains(definition.Id))
            {
                prefixEnded = true;
                continue;
            }

            if (prefixEnded)
            {
                throw new InvalidDataException(
                    $"Completed memory '{definition.Id}' does not form a " +
                    "prefix of the current catalog.");
            }

            if (state.AcceptedInteractionCount
                < definition.UnlockAtAcceptedInteractionCount)
            {
                throw new InvalidDataException(
                    $"Memory '{definition.Id}' is completed but only " +
                    $"'{state.AcceptedInteractionCount}' accepted " +
                    "interactions were recorded.");
            }
        }

        if (state.Pending is not { } pending)
        {
            return;
        }

        if (!_catalog.TryGet(
                pending.MemoryId,
                out MemoryDefinition? pendingDefinition)
            || pendingDefinition is null)
        {
            throw new InvalidDataException(
                $"Pending memory '{pending.MemoryId}' is not present in the " +
                "current catalog.");
        }

        MemoryDefinition? next = GetNextDefinition(state);
        if (next is null
            || !string.Equals(
                next.Id,
                pendingDefinition.Id,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Pending memory '{pending.MemoryId}' is not the next memory " +
                "in the current catalog.");
        }

        if (state.AcceptedInteractionCount
            < pendingDefinition.UnlockAtAcceptedInteractionCount)
        {
            throw new InvalidDataException(
                $"Pending memory '{pending.MemoryId}' requires " +
                $"'{pendingDefinition.UnlockAtAcceptedInteractionCount}' " +
                $"accepted interactions, but only " +
                $"'{state.AcceptedInteractionCount}' were recorded.");
        }
    }

    private static DateTimeOffset LaterOf(
        DateTimeOffset first,
        DateTimeOffset second) =>
        first >= second ? first : second;

    private static MemoryProgressState Snapshot(
        MemoryProgressState state) =>
        state with
        {
            Pending = state.Pending is null
                ? null
                : state.Pending with { },
            Completed = state.Completed
                .Select(static record => record with { })
                .ToArray(),
        };
}
