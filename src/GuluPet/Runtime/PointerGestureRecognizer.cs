namespace GuluPet.Runtime;

public enum PetPointerGesture
{
    Approach,
    Hover,
    Petting,
    PettingEnded,
    RapidPointer,
    CirclePointer
}

public readonly record struct PetPointerSample(
    TimeSpan At,
    double X,
    double Y,
    double SurfaceWidth,
    double SurfaceHeight);

/// <summary>
/// Classifies a short, in-memory pointer trajectory while the cursor focuses
/// the pet. Raw coordinates are bounded and discarded when focus ends.
/// </summary>
public sealed class PointerGestureRecognizer
{
    private static readonly TimeSpan TrajectoryWindow = TimeSpan.FromSeconds(4);
    private const int MaximumSamples = 180;
    private readonly Queue<PetPointerSample> _samples = [];
    private readonly HashSet<PetPointerGesture> _emitted = [];
    private PetPointerSample? _first;
    private PetPointerSample? _previous;
    private (double X, double Y)? _lastSignificantDirection;
    private double? _previousAngle;
    private double _signedAngle;
    private double _absoluteAngle;
    private double _pathLength;
    private int _reversals;
    private bool _focused;

    public int RecentSampleCount => _samples.Count;

    public bool IsPetting => _emitted.Contains(PetPointerGesture.Petting);

    public void Begin(PetPointerSample first)
    {
        Validate(first);
        Reset();
        _focused = true;
        _samples.Enqueue(first);
        RebuildStatistics();
    }

    /// <summary>
    /// Continues the current focus session after a short pointer excursion.
    /// The trajectory is re-anchored so movement outside the pet is not counted,
    /// while emitted gestures remain latched for the lifetime of the session.
    /// </summary>
    public void Resume(PetPointerSample sample)
    {
        Validate(sample);
        if (!_focused)
        {
            Begin(sample);
            return;
        }

        _samples.Clear();
        _samples.Enqueue(sample);
        RebuildStatistics();
    }

    public IReadOnlyList<PetPointerGesture> Observe(PetPointerSample sample)
    {
        Validate(sample);
        if (!_focused)
        {
            Begin(sample);
            return [];
        }

        if (_previous is not { } previous)
        {
            throw new InvalidOperationException(
                "Pointer trajectory state is incomplete.");
        }

        if (sample.At < previous.At)
        {
            throw new InvalidOperationException(
                "Pointer samples cannot move backwards in time.");
        }

        var recognized = new List<PetPointerGesture>();
        double dx = sample.X - previous.X;
        double dy = sample.Y - previous.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        TimeSpan elapsed = sample.At - previous.At;

        _samples.Enqueue(sample);
        Trim(sample.At);
        RebuildStatistics();

        if (_first is not { } first)
        {
            throw new InvalidOperationException(
                "Pointer trajectory state is incomplete.");
        }

        if (!_emitted.Contains(PetPointerGesture.RapidPointer) &&
            elapsed >= TimeSpan.FromMilliseconds(8) &&
            distance / elapsed.TotalSeconds >= 1_250)
        {
            Emit(PetPointerGesture.RapidPointer, recognized);
        }

        double firstRadius = RadiusFromCenter(first);
        double currentRadius = RadiusFromCenter(sample);
        if (!_emitted.Contains(PetPointerGesture.Approach) &&
            sample.At - first.At <= TimeSpan.FromSeconds(1.5) &&
            firstRadius >= 55 &&
            firstRadius - currentRadius >= 24)
        {
            Emit(PetPointerGesture.Approach, recognized);
        }

        if (!_emitted.Contains(PetPointerGesture.Hover) &&
            sample.At - first.At >= TimeSpan.FromMilliseconds(850) &&
            _pathLength <= 45)
        {
            Emit(PetPointerGesture.Hover, recognized);
        }

        if (!_emitted.Contains(PetPointerGesture.Petting) &&
            _reversals >= 3 &&
            _pathLength >= 95)
        {
            Emit(PetPointerGesture.Petting, recognized);
        }

        if (!_emitted.Contains(PetPointerGesture.CirclePointer) &&
            Math.Abs(_signedAngle) >= Math.PI * 1.55 &&
            _absoluteAngle <= Math.Abs(_signedAngle) * 1.55)
        {
            Emit(PetPointerGesture.CirclePointer, recognized);
        }

        return recognized;
    }

    public IReadOnlyList<PetPointerGesture> End()
    {
        if (!_focused)
        {
            return [];
        }

        bool wasPetting = IsPetting;
        Reset();
        return wasPetting
            ? [PetPointerGesture.PettingEnded]
            : [];
    }

    /// <summary>
    /// Discards the current focus session without converting any previously
    /// recognized gesture into an end event. Pointer press and drag boundaries
    /// use this so a pre-press trajectory cannot affect the release response.
    /// </summary>
    public void Cancel()
    {
        Reset();
    }

    private void ObserveDirection(
        double dx,
        double dy,
        double distance)
    {
        if (distance < 7)
        {
            return;
        }

        var direction = (X: dx / distance, Y: dy / distance);
        if (_lastSignificantDirection is { } previousDirection)
        {
            double dot =
                direction.X * previousDirection.X +
                direction.Y * previousDirection.Y;
            if (dot <= -0.55)
            {
                _reversals++;
            }
        }

        _lastSignificantDirection = direction;
    }

    private void ObserveCircle(PetPointerSample sample)
    {
        if (RadiusFromCenter(sample) < 22)
        {
            _previousAngle = null;
            return;
        }

        double angle = AngleFromCenter(sample);
        if (_previousAngle is { } previousAngle)
        {
            double delta = angle - previousAngle;
            while (delta > Math.PI)
            {
                delta -= Math.PI * 2;
            }

            while (delta < -Math.PI)
            {
                delta += Math.PI * 2;
            }

            if (Math.Abs(delta) <= 1.1)
            {
                _signedAngle += delta;
                _absoluteAngle += Math.Abs(delta);
            }
        }

        _previousAngle = angle;
    }

    private void Trim(TimeSpan now)
    {
        while (_samples.Count > MaximumSamples ||
               _samples.Count > 1 &&
               now - _samples.Peek().At > TrajectoryWindow)
        {
            _samples.Dequeue();
        }
    }

    private void RebuildStatistics()
    {
        _first = null;
        _previous = null;
        _lastSignificantDirection = null;
        _previousAngle = null;
        _signedAngle = 0;
        _absoluteAngle = 0;
        _pathLength = 0;
        _reversals = 0;

        foreach (PetPointerSample current in _samples)
        {
            if (_first is null)
            {
                _first = current;
                _previous = current;
                _previousAngle = RadiusFromCenter(current) >= 22
                    ? AngleFromCenter(current)
                    : null;
                continue;
            }

            PetPointerSample previous = _previous!.Value;
            double dx = current.X - previous.X;
            double dy = current.Y - previous.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            _pathLength += distance;
            ObserveDirection(dx, dy, distance);
            ObserveCircle(current);
            _previous = current;
        }
    }

    private void Emit(
        PetPointerGesture gesture,
        ICollection<PetPointerGesture> output)
    {
        _emitted.Add(gesture);
        output.Add(gesture);
    }

    private static double RadiusFromCenter(PetPointerSample sample)
    {
        double dx = sample.X - sample.SurfaceWidth / 2;
        double dy = sample.Y - sample.SurfaceHeight / 2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double AngleFromCenter(PetPointerSample sample) =>
        Math.Atan2(
            sample.Y - sample.SurfaceHeight / 2,
            sample.X - sample.SurfaceWidth / 2);

    private static void Validate(PetPointerSample sample)
    {
        if (sample.At < TimeSpan.Zero ||
            !double.IsFinite(sample.X) ||
            !double.IsFinite(sample.Y) ||
            !double.IsFinite(sample.SurfaceWidth) ||
            !double.IsFinite(sample.SurfaceHeight) ||
            sample.SurfaceWidth <= 0 ||
            sample.SurfaceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sample),
                "Pointer samples must contain finite coordinates, time, and size.");
        }
    }

    private void Reset()
    {
        _samples.Clear();
        _emitted.Clear();
        _first = null;
        _previous = null;
        _lastSignificantDirection = null;
        _previousAngle = null;
        _signedAngle = 0;
        _absoluteAngle = 0;
        _pathLength = 0;
        _reversals = 0;
        _focused = false;
    }
}
