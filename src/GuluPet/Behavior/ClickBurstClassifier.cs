namespace GuluPet.Behavior;

internal enum ClickBurstObservation
{
    Pending,
    Repeated
}

/// <summary>
/// Classifies clicks against one fixed window that starts at the first click.
/// The caller owns dispatch: a pending burst becomes a single click only when
/// <see cref="TryExpireSingle"/> is observed, while reaching the configured
/// threshold consumes the burst immediately as a repeated click.
/// </summary>
internal sealed class ClickBurstClassifier
{
    private readonly IMonotonicClock _clock;
    private readonly int _repeatedClickThreshold;
    private readonly TimeSpan _classificationWindow;
    private TimeSpan? _deadline;
    private int _clickCount;

    public ClickBurstClassifier(
        IMonotonicClock clock,
        int repeatedClickThreshold,
        TimeSpan classificationWindow)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            repeatedClickThreshold,
            2);
        if (classificationWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(classificationWindow),
                "The click classification window must be positive.");
        }

        _repeatedClickThreshold = repeatedClickThreshold;
        _classificationWindow = classificationWindow;
    }

    public bool HasPending => _deadline is not null;

    public ClickBurstObservation ObserveClick()
    {
        TimeSpan now = _clock.Elapsed;
        if (_deadline is { } deadline && now > deadline)
        {
            throw new InvalidOperationException(
                "An expired click burst must be consumed before observing " +
                "another click.");
        }

        if (_deadline is null)
        {
            _deadline = AddSaturated(now, _classificationWindow);
            _clickCount = 0;
        }

        _clickCount++;
        if (_clickCount < _repeatedClickThreshold)
        {
            return ClickBurstObservation.Pending;
        }

        Reset();
        return ClickBurstObservation.Repeated;
    }

    public bool TryExpireSingle()
    {
        if (_deadline is not { } deadline || _clock.Elapsed <= deadline)
        {
            return false;
        }

        Reset();
        return true;
    }

    public bool Cancel()
    {
        bool hadPending = HasPending;
        Reset();
        return hadPending;
    }

    private void Reset()
    {
        _deadline = null;
        _clickCount = 0;
    }

    private static TimeSpan AddSaturated(TimeSpan left, TimeSpan right) =>
        left > TimeSpan.MaxValue - right
            ? TimeSpan.MaxValue
            : left + right;
}
