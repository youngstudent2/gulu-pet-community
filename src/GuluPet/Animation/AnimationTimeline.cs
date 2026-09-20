namespace GuluPet.Animation;

/// <summary>
/// Converts monotonic wall-clock time into animation frame advances.
/// </summary>
internal sealed class AnimationTimeline
{
    // Fractional frame boundaries rarely land on an exact TimeSpan tick.
    // A tiny tolerance keeps common timestamps on the intended boundary
    // without making a perceptible difference to playback.
    private const double BoundaryToleranceSeconds = 0.00005;

    private double _framesPerSecond = 1;
    private long _consumedSteps;

    public void Reset(double framesPerSecond)
    {
        _framesPerSecond = Math.Clamp(framesPerSecond, 1, 60);
        _consumedSteps = 0;
    }

    public long ConsumeDueSteps(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return 0;
        }

        var scaledSteps =
            (elapsed.TotalSeconds + BoundaryToleranceSeconds) * _framesPerSecond;
        var totalDueSteps =
            scaledSteps >= long.MaxValue
                ? long.MaxValue
                : (long)Math.Floor(scaledSteps);

        if (totalDueSteps <= _consumedSteps)
        {
            return 0;
        }

        var dueSteps = totalDueSteps - _consumedSteps;
        _consumedSteps = totalDueSteps;
        return dueSteps;
    }
}
