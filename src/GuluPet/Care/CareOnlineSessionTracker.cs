namespace GuluPet.Care;

internal readonly record struct CareOnlineObservation(
    CareState State,
    TimeSpan AppliedElapsed,
    bool StateChanged);

/// <summary>
/// Converts a monotonic application-session clock into explicit online-time
/// observations. Suspend and resume form a hard boundary, so elapsed time
/// while Windows is asleep is never passed to <see cref="CareStateTracker"/>.
/// </summary>
internal sealed class CareOnlineSessionTracker
{
    private readonly CareStateTracker _care;
    private readonly Func<TimeSpan> _readElapsed;
    private TimeSpan _lastObservedAt;

    internal CareOnlineSessionTracker(
        CareStateTracker care,
        Func<TimeSpan> readElapsed)
    {
        _care = care ?? throw new ArgumentNullException(nameof(care));
        _readElapsed = readElapsed
            ?? throw new ArgumentNullException(nameof(readElapsed));
        _lastObservedAt = ReadElapsed();
    }

    internal bool IsSuspended { get; private set; }

    internal CareOnlineObservation Observe()
    {
        if (IsSuspended)
        {
            return Unchanged();
        }

        TimeSpan observedAt = ReadElapsed();
        if (observedAt <= _lastObservedAt)
        {
            // A monotonic source should not move backwards. If a platform or
            // test source does, fail closed by charging no care time and
            // establish a fresh baseline.
            _lastObservedAt = observedAt;
            return Unchanged();
        }

        TimeSpan elapsed = observedAt - _lastObservedAt;
        _lastObservedAt = observedAt;
        CareState before = _care.Current;
        CareState after = _care.AdvanceOnline(elapsed);
        return new CareOnlineObservation(
            after,
            elapsed,
            before.FoodLevel != after.FoodLevel
            || before.WaterLevel != after.WaterLevel);
    }

    internal CareOnlineObservation Suspend()
    {
        if (IsSuspended)
        {
            return Unchanged();
        }

        CareOnlineObservation finalOnlineObservation = Observe();
        IsSuspended = true;
        return finalOnlineObservation;
    }

    internal void Resume()
    {
        if (!IsSuspended)
        {
            return;
        }

        _lastObservedAt = ReadElapsed();
        IsSuspended = false;
    }

    private CareOnlineObservation Unchanged() =>
        new(_care.Current, TimeSpan.Zero, StateChanged: false);

    private TimeSpan ReadElapsed()
    {
        TimeSpan elapsed = _readElapsed();
        if (elapsed < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The care monotonic clock returned a negative value.");
        }

        return elapsed;
    }
}
