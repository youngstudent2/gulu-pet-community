using System.Diagnostics;

namespace GuluPet.Behavior;

public interface IMonotonicClock
{
    TimeSpan Elapsed { get; }
}

public sealed class SystemMonotonicClock : IMonotonicClock
{
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    public static SystemMonotonicClock Instance { get; } = new();

    private SystemMonotonicClock()
    {
    }

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_startedAt);
}

public sealed class BehaviorRandomStreams
{
    public BehaviorRandomStreams(
        IRandomSource tickInterval,
        IRandomSource utilitySelection,
        IRandomSource queueEviction,
        IRandomSource? animationVariant = null)
    {
        TickInterval = tickInterval
            ?? throw new ArgumentNullException(nameof(tickInterval));
        UtilitySelection = utilitySelection
            ?? throw new ArgumentNullException(nameof(utilitySelection));
        QueueEviction = queueEviction
            ?? throw new ArgumentNullException(nameof(queueEviction));
        AnimationVariant = animationVariant ??
            new SystemRandomSource(new Random(Random.Shared.Next()));
    }

    public IRandomSource TickInterval { get; }

    public IRandomSource UtilitySelection { get; }

    public IRandomSource QueueEviction { get; }

    public IRandomSource AnimationVariant { get; }

    public static BehaviorRandomStreams CreateSystem() =>
        new(
            new SystemRandomSource(new Random(Random.Shared.Next())),
            new SystemRandomSource(new Random(Random.Shared.Next())),
            new SystemRandomSource(new Random(Random.Shared.Next())),
            new SystemRandomSource(new Random(Random.Shared.Next())));
}
