namespace GuluPet.Behavior;

public sealed class ActiveTickScheduler
{
    public const int MinimumIntervalSeconds = 25;
    public const int MaximumIntervalSeconds = 60;

    private const int IntervalBucketCount =
        MaximumIntervalSeconds - MinimumIntervalSeconds + 1;

    private readonly IMonotonicClock _clock;
    private readonly IRandomSource _intervalRandom;
    private TimeSpan _lastObserved;

    public ActiveTickScheduler(
        IMonotonicClock? clock = null,
        IRandomSource? intervalRandom = null)
    {
        _clock = clock ?? SystemMonotonicClock.Instance;
        _intervalRandom = intervalRandom ?? new SystemRandomSource();
        _lastObserved = ObserveClock();
    }

    public ActiveTickScheduler(
        BehaviorRandomStreams randomStreams,
        IMonotonicClock? clock = null)
        : this(
            clock,
            (randomStreams
                ?? throw new ArgumentNullException(nameof(randomStreams)))
                .TickInterval)
    {
    }

    public bool IsScheduled => NextDueAt is not null;

    public TimeSpan? NextDueAt { get; private set; }

    public TimeSpan? LastInterval { get; private set; }

    public int ScheduleCount { get; private set; }

    public TimeSpan Start()
    {
        ObserveClock();
        return IsScheduled
            ? LastInterval!.Value
            : ScheduleNext();
    }

    public bool TryConsumeDueTick()
    {
        var now = ObserveClock();
        if (NextDueAt is null || now < NextDueAt)
        {
            return false;
        }

        NextDueAt = null;
        return true;
    }

    public TimeSpan NotifyTickCompleted() => ScheduleNext();

    public TimeSpan NotifyInteractionHandled() => ScheduleNext();

    public void Cancel()
    {
        ObserveClock();
        NextDueAt = null;
    }

    private TimeSpan ScheduleNext()
    {
        var now = ObserveClock();
        var randomValue = _intervalRandom.NextUnit();
        if (randomValue is < 0 or > 1 || double.IsNaN(randomValue))
        {
            throw new InvalidOperationException(
                "The active-tick random source must return a value in the inclusive range 0 to 1.");
        }

        var bounded = Math.Min(randomValue, Math.BitDecrement(1d));
        var bucket = (int)Math.Floor(bounded * IntervalBucketCount);
        var interval = TimeSpan.FromSeconds(
            MinimumIntervalSeconds + bucket);
        LastInterval = interval;
        NextDueAt = now + interval;
        ScheduleCount++;
        return interval;
    }

    private TimeSpan ObserveClock()
    {
        var now = _clock.Elapsed;
        if (now < TimeSpan.Zero || now < _lastObserved)
        {
            throw new InvalidOperationException(
                "The monotonic active-tick clock cannot move backwards.");
        }

        _lastObserved = now;
        return now;
    }
}
