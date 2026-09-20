namespace GuluPet.Care;

/// <summary>
/// Applies explicitly observed online time to durable care levels. Merely
/// constructing or reading the tracker never applies offline decay.
/// </summary>
public sealed class CareStateTracker
{
    public static TimeSpan FoodDepletionDuration { get; } =
        TimeSpan.FromHours(8);

    public static TimeSpan WaterDepletionDuration { get; } =
        TimeSpan.FromHours(6);

    private readonly object _gate = new();
    private CareState _state;

    public CareStateTracker(CareState? initialState = null)
    {
        _state = CareState.ValidateAndSnapshot(
            initialState ?? CareState.CreateDefault());
    }

    public CareState Current
    {
        get
        {
            lock (_gate)
            {
                return Snapshot(_state);
            }
        }
    }

    public CareState AdvanceOnline(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsed),
                elapsed,
                "Online care time cannot be negative.");
        }

        lock (_gate)
        {
            if (elapsed == TimeSpan.Zero)
            {
                return Snapshot(_state);
            }

            _state = CareState.Create(
                Deplete(
                    _state.FoodLevel,
                    elapsed,
                    FoodDepletionDuration),
                Deplete(
                    _state.WaterLevel,
                    elapsed,
                    WaterDepletionDuration));
            return Snapshot(_state);
        }
    }

    public CareState RefillFood()
    {
        lock (_gate)
        {
            _state = CareState.Create(
                CareState.FullLevel,
                _state.WaterLevel);
            return Snapshot(_state);
        }
    }

    public CareState RefillWater()
    {
        lock (_gate)
        {
            _state = CareState.Create(
                _state.FoodLevel,
                CareState.FullLevel);
            return Snapshot(_state);
        }
    }

    private static double Deplete(
        double current,
        TimeSpan elapsed,
        TimeSpan fullDuration)
    {
        double depletion = CareState.FullLevel *
            (elapsed.TotalSeconds / fullDuration.TotalSeconds);
        return Math.Max(0, current - depletion);
    }

    private static CareState Snapshot(CareState state) => state with { };
}
