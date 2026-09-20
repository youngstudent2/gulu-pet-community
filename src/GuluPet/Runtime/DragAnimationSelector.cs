namespace GuluPet.Runtime;

public enum DragHoldPose
{
    Relaxed,
    Tense,
    Stretched
}

/// <summary>
/// Selects one of the three authored drag loops with hysteresis and a short
/// stability dwell so noisy velocity samples cannot make the cat twitch.
/// </summary>
public sealed class DragAnimationSelector
{
    private static readonly TimeSpan RequiredDwell =
        TimeSpan.FromMilliseconds(120);
    private DragHoldPose? _candidate;
    private TimeSpan _candidateFor;

    public DragHoldPose Current { get; private set; } = DragHoldPose.Relaxed;

    public string CurrentClipId => ClipId(Current);

    public void Begin()
    {
        Current = DragHoldPose.Relaxed;
        _candidate = null;
        _candidateFor = TimeSpan.Zero;
    }

    public bool Observe(double speedPixelsPerSecond, TimeSpan elapsed)
    {
        if (!double.IsFinite(speedPixelsPerSecond))
        {
            speedPixelsPerSecond = 0;
        }

        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        DragHoldPose desired = ResolveDesired(
            Math.Abs(speedPixelsPerSecond));
        if (desired == Current)
        {
            _candidate = null;
            _candidateFor = TimeSpan.Zero;
            return false;
        }

        if (_candidate != desired)
        {
            _candidate = desired;
            _candidateFor = elapsed;
        }
        else
        {
            _candidateFor += elapsed;
        }

        if (_candidateFor < RequiredDwell)
        {
            return false;
        }

        Current = desired;
        _candidate = null;
        _candidateFor = TimeSpan.Zero;
        return true;
    }

    public static string ClipId(DragHoldPose pose) =>
        pose switch
        {
            DragHoldPose.Relaxed => "drag_hold_relaxed",
            DragHoldPose.Tense => "drag_hold_tense",
            DragHoldPose.Stretched => "drag_hold_stretched",
            _ => throw new ArgumentOutOfRangeException(nameof(pose)),
        };

    private DragHoldPose ResolveDesired(double speed) =>
        Current switch
        {
            DragHoldPose.Relaxed =>
                speed >= 1_100
                    ? DragHoldPose.Stretched
                    : speed >= 500
                        ? DragHoldPose.Tense
                        : DragHoldPose.Relaxed,
            DragHoldPose.Tense =>
                speed >= 1_100
                    ? DragHoldPose.Stretched
                    : speed <= 300
                        ? DragHoldPose.Relaxed
                        : DragHoldPose.Tense,
            DragHoldPose.Stretched =>
                speed <= 300
                    ? DragHoldPose.Relaxed
                    : speed <= 800
                        ? DragHoldPose.Tense
                        : DragHoldPose.Stretched,
            _ => DragHoldPose.Relaxed,
        };
}
