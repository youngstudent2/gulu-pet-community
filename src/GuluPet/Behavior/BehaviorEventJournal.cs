namespace GuluPet.Behavior;

public enum BehaviorEventKind
{
    RequestReceived,
    RequestRejected,
    RequestEnqueued,
    RequestRefreshed,
    GroupEvicted,
    SessionStarted,
    FirstFramePresented,
    PhaseChanged,
    BubbleShown,
    BubbleSuppressed,
    SessionTerminated,
    TickEvaluated
}

public sealed record BehaviorRuntimeEvent
{
    public required long Sequence { get; init; }

    public required TimeSpan OccurredAt { get; init; }

    public required BehaviorEventKind Kind { get; init; }

    public BehaviorId? BehaviorId { get; init; }

    public long? RequestId { get; init; }

    public long? GroupId { get; init; }

    public BehaviorSessionToken? SessionToken { get; init; }

    public BehaviorPhase? Phase { get; init; }

    public BehaviorTerminalStatus? TerminalStatus { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyDictionary<string, string> Details { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record BehaviorEventPage
{
    public required long RequestedAfterSequence { get; init; }

    public required long? TruncatedBeforeSequence { get; init; }

    public required IReadOnlyList<BehaviorRuntimeEvent> Events { get; init; }
}

public sealed class BehaviorEventJournal
{
    private readonly IMonotonicClock _clock;
    private readonly int _capacity;
    private readonly Queue<BehaviorRuntimeEvent> _events = [];
    private long _nextSequence = 1;
    private long? _truncatedBeforeSequence;

    public BehaviorEventJournal(
        IMonotonicClock? clock = null,
        int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _clock = clock ?? SystemMonotonicClock.Instance;
        _capacity = capacity;
    }

    public BehaviorRuntimeEvent Append(
        BehaviorEventKind kind,
        BehaviorId? behaviorId = null,
        long? requestId = null,
        long? groupId = null,
        BehaviorSessionToken? sessionToken = null,
        BehaviorPhase? phase = null,
        BehaviorTerminalStatus? terminalStatus = null,
        string? reason = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        var entry = new BehaviorRuntimeEvent
        {
            Sequence = _nextSequence++,
            OccurredAt = _clock.Elapsed,
            Kind = kind,
            BehaviorId = behaviorId,
            RequestId = requestId,
            GroupId = groupId,
            SessionToken = sessionToken,
            Phase = phase,
            TerminalStatus = terminalStatus,
            Reason = reason,
            Details = details ??
                      new Dictionary<string, string>(StringComparer.Ordinal),
        };

        _events.Enqueue(entry);
        while (_events.Count > _capacity)
        {
            var removed = _events.Dequeue();
            _truncatedBeforeSequence = removed.Sequence;
        }

        return entry;
    }

    public BehaviorEventPage ReadAfter(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        return new BehaviorEventPage
        {
            RequestedAfterSequence = sequence,
            TruncatedBeforeSequence =
                _truncatedBeforeSequence is { } truncated && sequence < truncated
                    ? truncated
                    : null,
            Events = _events
                .Where(entry => entry.Sequence > sequence)
                .ToArray(),
        };
    }
}

