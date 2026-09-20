using GuluPet.Behavior;

namespace GuluPet.Sensing;

internal readonly record struct InputActivityReading(
    long KeyboardCount,
    long MouseClickCount,
    bool IsAvailable);

internal readonly record struct InputActivityRates(
    double KeyboardPerMinute,
    double MouseClicksPerMinute);

internal readonly record struct ForegroundApplicationReading(
    string Category,
    bool IsAvailable,
    bool IsGuluPet);

internal readonly record struct ApplicationContextReading(
    string Category,
    bool IsAvailable,
    TimeSpan StableFor);

internal readonly record struct SessionLockReading(
    bool IsLocked,
    bool IsAvailable);

internal enum WeatherDataQuality
{
    Fresh,
    Stale,
    Unavailable,
}

internal readonly record struct WeatherReading(
    WeatherDataQuality Quality,
    string Kind,
    double TemperatureCelsius,
    int? WeatherCode,
    DateTimeOffset? ObservedAt)
{
    internal static WeatherReading Unavailable { get; } = new(
        WeatherDataQuality.Unavailable,
        "unavailable",
        0,
        null,
        null);
}

internal interface IInputActivitySource : IDisposable
{
    void Start();

    void Stop();

    InputActivityReading Read();
}

internal interface IForegroundApplicationSource
{
    ForegroundApplicationReading Read();
}

internal interface IIdleTimeSource
{
    bool TryGetIdleTime(out TimeSpan idleFor);
}

internal interface ISessionLockSource
{
    SessionLockReading Read();
}

internal interface IWeatherProvider : IDisposable
{
    WeatherReading GetCurrent(DateTimeOffset now);

    Task RefreshIfDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

internal interface IRuntimeContextMonitor : IDisposable
{
    event EventHandler<RuntimeContextSnapshotChangedEventArgs>? SnapshotChanged;

    BehaviorContextSnapshot Current { get; }

    /// <summary>
    /// Reads the cumulative input counters directly from the underlying
    /// source. This closes the gap between the last periodic snapshot and
    /// shutdown.
    /// </summary>
    InputActivityReading ReadCurrentInputActivity();

    void Start();

    /// <summary>
    /// Synchronously stops the active collection run. The same monitor may
    /// be started again after this method returns.
    /// </summary>
    void Stop();
}

internal sealed class UnavailableSessionLockSource : ISessionLockSource
{
    public SessionLockReading Read() => new(false, false);
}

internal sealed class RuntimeContextSnapshotChangedEventArgs(
    BehaviorContextSnapshot snapshot) : EventArgs
{
    internal BehaviorContextSnapshot Snapshot { get; } = snapshot;
}

internal sealed class RollingInputRateWindow(TimeSpan window)
{
    private readonly TimeSpan _window = window > TimeSpan.Zero
        ? window
        : throw new ArgumentOutOfRangeException(nameof(window));
    private readonly Queue<ActivityDelta> _deltas = [];
    private long _lastKeyboardCount;
    private long _lastMouseClickCount;
    private DateTimeOffset _lastObservedAt;
    private bool _hasBaseline;

    internal InputActivityRates Observe(
        DateTimeOffset now,
        InputActivityReading reading)
    {
        if (!reading.IsAvailable)
        {
            Reset();
            return default;
        }

        if (!_hasBaseline || now < _lastObservedAt)
        {
            SetBaseline(now, reading);
            return default;
        }

        long keyboardDelta = reading.KeyboardCount - _lastKeyboardCount;
        long mouseDelta = reading.MouseClickCount - _lastMouseClickCount;
        if (keyboardDelta < 0 || mouseDelta < 0)
        {
            SetBaseline(now, reading);
            _deltas.Clear();
            return default;
        }

        _lastKeyboardCount = reading.KeyboardCount;
        _lastMouseClickCount = reading.MouseClickCount;
        _lastObservedAt = now;
        if (keyboardDelta != 0 || mouseDelta != 0)
        {
            _deltas.Enqueue(
                new ActivityDelta(now, keyboardDelta, mouseDelta));
        }

        DateTimeOffset cutoff = now - _window;
        while (_deltas.TryPeek(out ActivityDelta oldest)
               && oldest.ObservedAt <= cutoff)
        {
            _deltas.Dequeue();
        }

        long keyboard = 0;
        long mouse = 0;
        foreach (ActivityDelta delta in _deltas)
        {
            keyboard = checked(keyboard + delta.KeyboardCount);
            mouse = checked(mouse + delta.MouseClickCount);
        }

        double minuteScale = TimeSpan.FromMinutes(1).TotalSeconds
            / _window.TotalSeconds;
        return new InputActivityRates(
            keyboard * minuteScale,
            mouse * minuteScale);
    }

    private void SetBaseline(
        DateTimeOffset now,
        InputActivityReading reading)
    {
        _hasBaseline = true;
        _lastObservedAt = now;
        _lastKeyboardCount = reading.KeyboardCount;
        _lastMouseClickCount = reading.MouseClickCount;
    }

    private void Reset()
    {
        _hasBaseline = false;
        _lastKeyboardCount = 0;
        _lastMouseClickCount = 0;
        _lastObservedAt = default;
        _deltas.Clear();
    }

    private readonly record struct ActivityDelta(
        DateTimeOffset ObservedAt,
        long KeyboardCount,
        long MouseClickCount);
}

internal sealed class ApplicationStabilityTracker(
    TimeSpan guluPetForegroundHold)
{
    private readonly TimeSpan _guluPetForegroundHold =
        guluPetForegroundHold >= TimeSpan.Zero
            ? guluPetForegroundHold
            : throw new ArgumentOutOfRangeException(
                nameof(guluPetForegroundHold));
    private string _category = "unknown";
    private bool _isAvailable;
    private DateTimeOffset _stableSince;
    private DateTimeOffset _lastExternalObservation;
    private bool _hasObservation;

    internal ApplicationContextReading Observe(
        DateTimeOffset now,
        ForegroundApplicationReading reading)
    {
        if (reading.IsGuluPet
            && _isAvailable
            && now >= _lastExternalObservation
            && now - _lastExternalObservation <= _guluPetForegroundHold)
        {
            return new ApplicationContextReading(
                _category,
                true,
                NonNegative(now - _stableSince));
        }

        string category = reading.IsAvailable
            ? ApplicationCategoryClassifier.NormalizeCategory(reading.Category)
            : "unknown";
        bool available = reading.IsAvailable;
        if (!_hasObservation
            || now < _stableSince
            || available != _isAvailable
            || !string.Equals(
                category,
                _category,
                StringComparison.OrdinalIgnoreCase))
        {
            _category = category;
            _isAvailable = available;
            _stableSince = now;
            _hasObservation = true;
        }

        if (available && !reading.IsGuluPet)
        {
            _lastExternalObservation = now;
        }

        return new ApplicationContextReading(
            _category,
            _isAvailable,
            NonNegative(now - _stableSince));
    }

    private static TimeSpan NonNegative(TimeSpan value) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value;
}

internal sealed class BusyStateTracker(
    TimeSpan enterAfter,
    TimeSpan exitAfter,
    double keyboardThreshold = 180,
    double mouseClickThreshold = 45)
{
    private readonly TimeSpan _enterAfter = enterAfter >= TimeSpan.Zero
        ? enterAfter
        : throw new ArgumentOutOfRangeException(nameof(enterAfter));
    private readonly TimeSpan _exitAfter = exitAfter >= TimeSpan.Zero
        ? exitAfter
        : throw new ArgumentOutOfRangeException(nameof(exitAfter));
    private readonly double _keyboardThreshold = keyboardThreshold;
    private readonly double _mouseClickThreshold = mouseClickThreshold;
    private DateTimeOffset? _enterCandidateSince;
    private DateTimeOffset? _exitCandidateSince;
    private DateTimeOffset _lastObservedAt;
    private bool _isBusy;

    internal bool Observe(
        DateTimeOffset now,
        InputActivityRates rates,
        TimeSpan idleFor,
        bool dataAvailable)
    {
        if (_lastObservedAt != default && now < _lastObservedAt)
        {
            _enterCandidateSince = null;
            _exitCandidateSince = null;
            _isBusy = false;
        }

        _lastObservedAt = now;
        bool aboveThreshold = dataAvailable
            && idleFor < TimeSpan.FromSeconds(90)
            && (rates.KeyboardPerMinute >= _keyboardThreshold
                || rates.MouseClicksPerMinute >= _mouseClickThreshold);
        if (!_isBusy)
        {
            _exitCandidateSince = null;
            if (!aboveThreshold)
            {
                _enterCandidateSince = null;
                return false;
            }

            _enterCandidateSince ??= now;
            if (now - _enterCandidateSince.Value >= _enterAfter)
            {
                _isBusy = true;
                _enterCandidateSince = null;
            }

            return _isBusy;
        }

        _enterCandidateSince = null;
        if (aboveThreshold)
        {
            _exitCandidateSince = null;
            return true;
        }

        _exitCandidateSince ??= now;
        if (now - _exitCandidateSince.Value >= _exitAfter)
        {
            _isBusy = false;
            _exitCandidateSince = null;
        }

        return _isBusy;
    }
}
