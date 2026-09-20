using GuluPet.Behavior;

namespace GuluPet.Tests;

internal sealed class ManualDualClock : IMonotonicClock
{
    public DateTimeOffset UtcNow { get; private set; } =
        new(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);

    public TimeSpan Elapsed { get; private set; }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        UtcNow += elapsed;
        Elapsed += elapsed;
    }

    public void AdjustWallClock(TimeSpan delta) => UtcNow += delta;

    public void AdvanceMonotonicOnly(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        Elapsed += elapsed;
    }

    public void SetMonotonicForTest(TimeSpan elapsed) => Elapsed = elapsed;
}

internal sealed class SequenceRandomSource : IRandomSource
{
    private readonly Queue<double> _values;

    public SequenceRandomSource(params double[] values)
    {
        _values = new Queue<double>(values);
    }

    public int Calls { get; private set; }

    public int Remaining => _values.Count;

    public double NextUnit()
    {
        Calls++;
        if (_values.Count == 0)
        {
            throw new InvalidOperationException(
                "The deterministic random sequence was exhausted.");
        }

        return _values.Dequeue();
    }
}

internal static class BehaviorTestCheck
{
    public static void True(bool value, string? message = null)
    {
        if (!value)
        {
            throw new InvalidOperationException(message ?? "Expected true.");
        }
    }

    public static void False(bool value, string? message = null) =>
        True(!value, message ?? "Expected false.");

    public static void Null(object? value)
    {
        if (value is not null)
        {
            throw new InvalidOperationException($"Expected null, got '{value}'.");
        }
    }

    public static T NotNull<T>(T? value)
        where T : class =>
        value ?? throw new InvalidOperationException("Expected a non-null value.");

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected '{expected}', got '{actual}'.");
        }
    }

    public static void Close(
        double expected,
        double actual,
        double tolerance = 0.000_001)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"Expected '{expected}', got '{actual}'.");
        }
    }

    public static void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string? message = null)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                message ?? "Sequences were not equal.");
        }
    }

    public static T Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new InvalidOperationException(
            $"Expected exception '{typeof(T).Name}'.");
    }
}
