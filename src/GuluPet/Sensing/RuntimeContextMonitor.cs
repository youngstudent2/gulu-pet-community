using GuluPet.Behavior;
using GuluPet.Diagnostics;
using GuluPet.Platform;

namespace GuluPet.Sensing;

internal sealed record RuntimeContextMonitorOptions
{
    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    internal TimeSpan InputRateWindow { get; init; } = TimeSpan.FromMinutes(1);

    internal TimeSpan GuluPetForegroundHold { get; init; } =
        TimeSpan.FromSeconds(5);

    internal TimeSpan BusyEnterAfter { get; init; } = TimeSpan.FromMinutes(2);

    internal TimeSpan BusyExitAfter { get; init; } = TimeSpan.FromSeconds(8);

    internal TimeSpan RecoveryInitialDelay { get; init; } =
        TimeSpan.FromSeconds(2);

    internal TimeSpan RecoveryMaximumDelay { get; init; } =
        TimeSpan.FromMinutes(2);

    internal void Validate()
    {
        if (PollInterval <= TimeSpan.Zero
            || InputRateWindow <= TimeSpan.Zero
            || GuluPetForegroundHold < TimeSpan.Zero
            || BusyEnterAfter < TimeSpan.Zero
            || BusyExitAfter < TimeSpan.Zero
            || RecoveryInitialDelay <= TimeSpan.Zero
            || RecoveryMaximumDelay < RecoveryInitialDelay)
        {
            throw new InvalidOperationException(
                "Runtime context monitor intervals are invalid.");
        }
    }
}

internal sealed class RuntimeContextMonitor : IRuntimeContextMonitor
{
    private readonly object _lifecycleGate = new();
    private readonly SemaphoreSlim _lifecycleSerial = new(1, 1);
    private readonly IInputActivitySource _input;
    private readonly IForegroundApplicationSource _foreground;
    private readonly IIdleTimeSource _idle;
    private readonly ISessionLockSource _sessionLock;
    private readonly IWeatherProvider _weather;
    private readonly TimeProvider _timeProvider;
    private readonly RuntimeContextMonitorOptions _options;
    private readonly IContextHealthSink _healthSink;
    private readonly bool _ownsHealthSink;
    private readonly SourceRecoveryState _inputRecovery;
    private readonly SourceRecoveryState _foregroundRecovery;
    private readonly SourceRecoveryState _idleRecovery;
    private readonly SourceRecoveryState _sessionLockRecovery;
    private readonly SourceRecoveryState _weatherRecovery;
    private readonly RollingInputRateWindow _inputRates;
    private readonly ApplicationStabilityTracker _applicationStability;
    private readonly BusyStateTracker _busyState;
    private BehaviorContextSnapshot _current;
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private Task? _weatherRefreshTask;
    private long _revision;
    private int _publishingThreadId;
    private bool _inputStarted;
    private bool _disposed;

    internal RuntimeContextMonitor(
        IInputActivitySource input,
        IForegroundApplicationSource foreground,
        IIdleTimeSource idle,
        IWeatherProvider weather,
        ISessionLockSource? sessionLock = null,
        TimeProvider? timeProvider = null,
        RuntimeContextMonitorOptions? options = null,
        IContextHealthSink? healthSink = null,
        bool ownsHealthSink = false)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _foreground = foreground
            ?? throw new ArgumentNullException(nameof(foreground));
        _idle = idle ?? throw new ArgumentNullException(nameof(idle));
        _weather = weather ?? throw new ArgumentNullException(nameof(weather));
        _sessionLock = sessionLock ?? new UnavailableSessionLockSource();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new RuntimeContextMonitorOptions();
        _options.Validate();
        _healthSink = healthSink ?? NullContextHealthSink.Instance;
        _ownsHealthSink = ownsHealthSink;
        _inputRecovery = CreateRecovery(ContextHealthSource.Input);
        _foregroundRecovery = CreateRecovery(
            ContextHealthSource.ForegroundApplication);
        _idleRecovery = CreateRecovery(ContextHealthSource.IdleTime);
        _sessionLockRecovery = CreateRecovery(
            ContextHealthSource.SessionLock);
        _weatherRecovery = CreateRecovery(
            ContextHealthSource.Weather,
            reportInitialSuccess: false);
        _inputRates = new RollingInputRateWindow(_options.InputRateWindow);
        _applicationStability = new ApplicationStabilityTracker(
            _options.GuluPetForegroundHold);
        _busyState = new BusyStateTracker(
            _options.BusyEnterAfter,
            _options.BusyExitAfter);
        _current = BehaviorContextSnapshot.Empty with
        {
            LocalNow = _timeProvider.GetLocalNow(),
        };
    }

    public event EventHandler<RuntimeContextSnapshotChangedEventArgs>?
        SnapshotChanged;

    public BehaviorContextSnapshot Current => Volatile.Read(ref _current);

    public InputActivityReading ReadCurrentInputActivity()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return CaptureInput(_timeProvider.GetLocalNow());
        }
    }

    internal static IRuntimeContextMonitor CreateDefault()
    {
        var healthLog = ContextHealthLogWriter.CreateDefault();
        try
        {
            return new RuntimeContextMonitor(
                new GlobalInputActivitySource(),
                new WindowsForegroundApplicationSource(
                    new ApplicationCategoryProcessClassifier()),
                new WindowsIdleTimeSource(),
                OpenMeteoWeatherProvider.CreateGuangzhouDefault(healthLog),
                new WindowsSessionLockSource(),
                healthSink: healthLog,
                ownsHealthSink: true);
        }
        catch
        {
            healthLog.Dispose();
            throw;
        }
    }

    internal static IRuntimeContextMonitor CreateDisabled() =>
        new DisabledRuntimeContextMonitor();

    public void Start()
    {
        _lifecycleSerial.Wait();
        try
        {
            lock (_lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_cancellation is not null)
                {
                    return;
                }

                TryStartInput(_timeProvider.GetLocalNow());

                _cancellation = new CancellationTokenSource();
                CancellationTokenSource run = _cancellation;
                _weatherRefreshTask = null;
                _loopTask = Task.Run(
                    () => RunAsync(run),
                    CancellationToken.None);
            }
        }
        finally
        {
            _lifecycleSerial.Release();
        }
    }

    public void Stop()
    {
        _lifecycleSerial.Wait();
        try
        {
            StopActiveRun();
        }
        finally
        {
            _lifecycleSerial.Release();
        }
    }

    public void Dispose()
    {
        _lifecycleSerial.Wait();
        try
        {
            lock (_lifecycleGate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            StopActiveRun();
            if (_inputStarted)
            {
                try
                {
                    _input.Stop();
                }
                catch
                {
                    // Dispose still owns the remaining resources.
                }
                finally
                {
                    _inputStarted = false;
                }
            }

            _input.Dispose();
            _weather.Dispose();
            if (_ownsHealthSink)
            {
                _healthSink.Dispose();
            }
        }
        finally
        {
            _lifecycleSerial.Release();
        }
    }

    internal BehaviorContextSnapshot CaptureOnce(DateTimeOffset now)
    {
        InputActivityReading input = CaptureInput(now);
        InputActivityRates rates = _inputRates.Observe(now, input);
        ApplicationContextReading application = _applicationStability.Observe(
            now,
            _foregroundRecovery.TryRead(
                now,
                _foreground.Read,
                new ForegroundApplicationReading(
                    "unknown",
                    false,
                    false),
                static reading =>
                    reading.IsAvailable || reading.IsGuluPet));
        (bool idleAvailable, TimeSpan idleFor) = _idleRecovery.TryRead(
            now,
            ReadIdle,
            (false, TimeSpan.Zero),
            static reading => reading.Item1);
        if (!idleAvailable || idleFor < TimeSpan.Zero)
        {
            idleFor = TimeSpan.Zero;
        }

        SessionLockReading sessionLock = _sessionLockRecovery.TryRead(
            now,
            _sessionLock.Read,
            new SessionLockReading(false, false),
            static reading => reading.IsAvailable);
        WeatherReading weather = _weatherRecovery.TryRead(
            now,
            () => _weather.GetCurrent(now),
            WeatherReading.Unavailable,
            static _ => true);
        bool weatherAvailable = weather.Quality == WeatherDataQuality.Fresh;
        bool isBusy = _busyState.Observe(
            now,
            rates,
            idleFor,
            input.IsAvailable && idleAvailable);
        return new BehaviorContextSnapshot
        {
            Revision = checked(Interlocked.Increment(ref _revision)),
            LocalNow = now,
            ApplicationCategory = application.Category,
            ApplicationCategoryAvailable = application.IsAvailable,
            ApplicationStableFor = application.StableFor,
            KeyboardPerMinute = rates.KeyboardPerMinute,
            MouseClicksPerMinute = rates.MouseClicksPerMinute,
            KeyboardCountSinceStart = Math.Max(0, input.KeyboardCount),
            MouseClickCountSinceStart = Math.Max(
                0,
                input.MouseClickCount),
            IdleFor = idleFor,
            IsBusy = isBusy,
            IsLocked = sessionLock.IsAvailable && sessionLock.IsLocked,
            WeatherKind = weatherAvailable
                ? weather.Kind
                : "unavailable",
            WeatherAvailable = weatherAvailable,
            TemperatureCelsius = weatherAvailable
                ? weather.TemperatureCelsius
                : 0,
        };
    }

    private async Task RunAsync(CancellationTokenSource run)
    {
        CancellationToken cancellationToken = run.Token;
        try
        {
            using var timer = new PeriodicTimer(
                _options.PollInterval,
                _timeProvider);
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset now = _timeProvider.GetLocalNow();
                try
                {
                    BehaviorContextSnapshot snapshot = CaptureOnce(now);
                    lock (_lifecycleGate)
                    {
                        if (!ReferenceEquals(_cancellation, run)
                            || cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        Volatile.Write(ref _current, snapshot);
                    }

                    Publish(snapshot, run);
                    StartWeatherRefresh(now, run);
                }
                catch
                {
                    // An unexpected derivation failure is isolated to this
                    // cycle. The next timer tick probes every source again.
                }

                if (!await timer.WaitForNextTickAsync(
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void StartWeatherRefresh(
        DateTimeOffset now,
        CancellationTokenSource run)
    {
        lock (_lifecycleGate)
        {
            if (!ReferenceEquals(_cancellation, run)
                || run.IsCancellationRequested
                || _weatherRefreshTask is { IsCompleted: false })
            {
                return;
            }

            _weatherRefreshTask = RefreshWeatherSafelyAsync(
                now,
                run.Token);
        }
    }

    private async Task RefreshWeatherSafelyAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            await _weather.RefreshIfDueAsync(now, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // The provider exposes Unavailable or its last valid cache.
        }
    }

    private void Publish(
        BehaviorContextSnapshot snapshot,
        CancellationTokenSource run)
    {
        lock (_lifecycleGate)
        {
            if (_disposed
                || !ReferenceEquals(_cancellation, run)
                || run.IsCancellationRequested)
            {
                return;
            }
        }

        EventHandler<RuntimeContextSnapshotChangedEventArgs>? handlers =
            SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        var args = new RuntimeContextSnapshotChangedEventArgs(snapshot);
        Volatile.Write(
            ref _publishingThreadId,
            Environment.CurrentManagedThreadId);
        try
        {
            foreach (EventHandler<RuntimeContextSnapshotChangedEventArgs> handler
                     in handlers.GetInvocationList())
            {
                lock (_lifecycleGate)
                {
                    if (_disposed
                        || !ReferenceEquals(_cancellation, run)
                        || run.IsCancellationRequested)
                    {
                        break;
                    }
                }

                try
                {
                    handler(this, args);
                }
                catch
                {
                    // A presentation subscriber cannot stop background
                    // sensing.
                }
            }
        }
        finally
        {
            Volatile.Write(ref _publishingThreadId, 0);
        }
    }

    private void StopActiveRun()
    {
        CancellationTokenSource? cancellation;
        Task? loopTask;
        Task? weatherRefreshTask;
        lock (_lifecycleGate)
        {
            cancellation = _cancellation;
            if (cancellation is null)
            {
                return;
            }

            _cancellation = null;
            loopTask = _loopTask;
            _loopTask = null;
            weatherRefreshTask = _weatherRefreshTask;
            _weatherRefreshTask = null;
        }

        cancellation.Cancel();
        try
        {
            try
            {
                _input.Stop();
            }
            catch
            {
                // Shutdown must still join the monitor tasks.
            }
            finally
            {
                _inputStarted = false;
            }
        }
        finally
        {
            bool stoppingFromPublisher =
                Volatile.Read(ref _publishingThreadId)
                == Environment.CurrentManagedThreadId;
            if (!stoppingFromPublisher)
            {
                WaitForCompletion(loopTask);
            }

            WaitForCompletion(weatherRefreshTask);
            cancellation.Dispose();
        }
    }

    private static void WaitForCompletion(Task? task)
    {
        if (task is null || task.IsCompleted)
        {
            return;
        }

        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private SourceRecoveryState CreateRecovery(
        ContextHealthSource source,
        bool reportInitialSuccess = true) =>
        new(
            source,
            _healthSink,
            _options.RecoveryInitialDelay,
            _options.RecoveryMaximumDelay,
            reportInitialSuccess);

    private InputActivityReading CaptureInput(DateTimeOffset now)
    {
        if (!_inputStarted && !TryStartInput(now))
        {
            return new InputActivityReading(0, 0, false);
        }

        try
        {
            InputActivityReading reading = _input.Read();
            if (!reading.IsAvailable)
            {
                MarkInputUnavailable(now);
                return new InputActivityReading(0, 0, false);
            }

            _inputRecovery.MarkSuccess(now);
            return reading;
        }
        catch
        {
            MarkInputFailure(
                now,
                ContextHealthErrorCategory.Unexpected);
            return new InputActivityReading(0, 0, false);
        }
    }

    private bool TryStartInput(DateTimeOffset now)
    {
        if (_inputStarted)
        {
            return true;
        }

        if (!_inputRecovery.CanAttempt(now))
        {
            return false;
        }

        try
        {
            _input.Start();
            _inputStarted = true;
            return true;
        }
        catch
        {
            _inputRecovery.MarkFailure(
                now,
                ContextHealthErrorCategory.Unexpected);
            return false;
        }
    }

    private void MarkInputUnavailable(DateTimeOffset now) =>
        MarkInputFailure(now, ContextHealthErrorCategory.Unavailable);

    private void MarkInputFailure(
        DateTimeOffset now,
        ContextHealthErrorCategory category)
    {
        try
        {
            _input.Stop();
        }
        catch
        {
            // The recovery schedule remains authoritative.
        }

        _inputStarted = false;
        _inputRecovery.MarkFailure(now, category);
    }

    private (bool, TimeSpan) ReadIdle()
    {
        bool available = _idle.TryGetIdleTime(out TimeSpan idleFor);
        return (available, idleFor);
    }

    private sealed class SourceRecoveryState(
        ContextHealthSource source,
        IContextHealthSink healthSink,
        TimeSpan initialDelay,
        TimeSpan maximumDelay,
        bool reportInitialSuccess)
    {
        private DateTimeOffset _nextAttemptAt = DateTimeOffset.MinValue;
        private DateTimeOffset _lastClockObservation;
        private DateTimeOffset? _lastSuccessAt;
        private int _consecutiveFailures;
        private bool _reportedInitialSuccess;

        internal bool CanAttempt(DateTimeOffset now)
        {
            if (_lastClockObservation != default
                && now < _lastClockObservation)
            {
                _nextAttemptAt = DateTimeOffset.MinValue;
            }

            _lastClockObservation = now;
            return now >= _nextAttemptAt;
        }

        internal T TryRead<T>(
            DateTimeOffset now,
            Func<T> read,
            T fallback,
            Func<T, bool> isAvailable)
        {
            if (!CanAttempt(now))
            {
                return fallback;
            }

            try
            {
                T result = read();
                if (!isAvailable(result))
                {
                    MarkFailure(
                        now,
                        ContextHealthErrorCategory.Unavailable);
                    return fallback;
                }

                MarkSuccess(now);
                return result;
            }
            catch
            {
                MarkFailure(
                    now,
                    ContextHealthErrorCategory.Unexpected);
                return fallback;
            }
        }

        internal void MarkSuccess(DateTimeOffset now)
        {
            ContextHealthOutcome outcome = _consecutiveFailures > 0
                ? ContextHealthOutcome.Recovered
                : ContextHealthOutcome.Success;
            bool shouldReport = outcome == ContextHealthOutcome.Recovered
                || (reportInitialSuccess && !_reportedInitialSuccess);
            _consecutiveFailures = 0;
            _nextAttemptAt = DateTimeOffset.MinValue;
            _lastSuccessAt = now;
            _reportedInitialSuccess = true;
            if (shouldReport)
            {
                TryWrite(
                    new ContextHealthEvent(
                        now,
                        source,
                        outcome,
                        ContextHealthErrorCategory.None,
                        0,
                        0));
            }
        }

        internal void MarkFailure(
            DateTimeOffset now,
            ContextHealthErrorCategory category)
        {
            _consecutiveFailures = checked(_consecutiveFailures + 1);
            double exponent = Math.Min(
                _consecutiveFailures - 1,
                20);
            double delayMilliseconds = Math.Min(
                maximumDelay.TotalMilliseconds,
                initialDelay.TotalMilliseconds * Math.Pow(2, exponent));
            _nextAttemptAt = now
                + TimeSpan.FromMilliseconds(delayMilliseconds);
            TryWrite(
                new ContextHealthEvent(
                    now,
                    source,
                    ContextHealthOutcome.Failure,
                    category,
                    _consecutiveFailures,
                    LastSuccessAgeSeconds(now)));
        }

        private double? LastSuccessAgeSeconds(DateTimeOffset now) =>
            _lastSuccessAt is DateTimeOffset success
                ? Math.Max(0, (now - success).TotalSeconds)
                : null;

        private void TryWrite(ContextHealthEvent healthEvent)
        {
            try
            {
                healthSink.Write(healthEvent);
            }
            catch
            {
                // Diagnostics are fail-open for product behavior.
            }
        }
    }
}

internal sealed class DisabledRuntimeContextMonitor : IRuntimeContextMonitor
{
    private bool _disposed;

    public event EventHandler<RuntimeContextSnapshotChangedEventArgs>?
        SnapshotChanged
    {
        add { }
        remove { }
    }

    public BehaviorContextSnapshot Current =>
        BehaviorContextSnapshot.Empty with
        {
            LocalNow = DateTimeOffset.Now,
        };

    public InputActivityReading ReadCurrentInputActivity() =>
        new(0, 0, false);

    public void Start() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Stop()
    {
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
