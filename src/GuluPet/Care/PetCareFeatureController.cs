using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using GuluPet.Behavior;
using GuluPet.Presentation;
using GuluPet.Runtime;
using Microsoft.Win32;

namespace GuluPet.Care;

/// <summary>
/// Application-level care orchestration: online-time depletion, durable state,
/// interaction-bar routing and verified behavior lifecycle presentation.
/// </summary>
internal sealed class PetCareFeatureController : IDisposable
{
    private static readonly TimeSpan OnlineTickInterval =
        TimeSpan.FromSeconds(1);

    private static readonly TimeSpan SaveInterval =
        TimeSpan.FromSeconds(10);

    private readonly PetWindow _petWindow;
    private readonly PetController _petController;
    private readonly Dispatcher _dispatcher;
    private readonly CareStateTracker _care;
    private readonly CareRuntimeState _runtimeState;
    private readonly CareOnlineSessionTracker _onlineSession;
    private readonly CareStateStore? _store;
    private readonly Func<TimeSpan> _readElapsed;
    private readonly DispatcherTimer _onlineTimer;
    private readonly bool _observesPowerEvents;
    private TimeSpan _nextSaveAttemptAt;
    private bool _stateDirty;
    private bool _disposed;

    internal PetCareFeatureController(
        PetWindow petWindow,
        PetController petController,
        bool isolatedTestInstance)
        : this(
            petWindow,
            petController,
            isolatedTestInstance ? null : new CareStateStore(),
            static () => SystemMonotonicClock.Instance.Elapsed,
            observePowerEvents: !isolatedTestInstance)
    {
    }

    internal PetCareFeatureController(
        PetWindow petWindow,
        PetController petController,
        CareStateStore? stateStore,
        Func<TimeSpan> readElapsed,
        bool observePowerEvents)
    {
        _petWindow = petWindow
            ?? throw new ArgumentNullException(nameof(petWindow));
        _petController = petController
            ?? throw new ArgumentNullException(nameof(petController));
        _dispatcher = petWindow.Dispatcher;
        _dispatcher.VerifyAccess();
        _readElapsed = readElapsed
            ?? throw new ArgumentNullException(nameof(readElapsed));
        _observesPowerEvents = observePowerEvents;

        CareState initialState = CareState.CreateDefault();
        CareStateStore? usableStore = stateStore;
        if (usableStore is not null)
        {
            try
            {
                initialState = usableStore.Load();
            }
            catch (Exception exception)
                when (exception is InvalidDataException
                      or IOException
                      or UnauthorizedAccessException
                      or InvalidOperationException
                      or NotSupportedException)
            {
                // If the store could not preserve an unreadable document,
                // leave it untouched and disable writes for this session.
                Debug.WriteLine(
                    "Unable to load GuluPet care state; persistence is " +
                    $"disabled for this session: {exception}");
                usableStore = null;
            }
        }

        _store = usableStore;
        _care = new CareStateTracker(initialState);
        _runtimeState = new CareRuntimeState(_care);
        _onlineSession = new CareOnlineSessionTracker(
            _care,
            _readElapsed);
        _nextSaveAttemptAt = AddSaturated(
            ReadElapsed(),
            SaveInterval);

        _petWindow.FeedRequested += OnFeedRequested;
        _petWindow.WaterRequested += OnWaterRequested;
        _petWindow.PettingRequested += OnPettingRequested;
        _petController.CareInteractionCommitted +=
            OnCareInteractionCommitted;
        _petController.CareInteractionTerminated +=
            OnCareInteractionTerminated;

        if (_observesPowerEvents)
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        _onlineTimer = new DispatcherTimer(
            OnlineTickInterval,
            DispatcherPriority.Background,
            OnOnlineTimerTick,
            _dispatcher);
        UpdateCareUi();
        _onlineTimer.Start();
    }

    internal CareState Current => _care.Current;

    internal bool InteractionBusy => _runtimeState.InteractionBusy;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.VerifyAccess();
        _onlineTimer.Stop();
        _onlineTimer.Tick -= OnOnlineTimerTick;
        if (_observesPowerEvents)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }

        _petWindow.FeedRequested -= OnFeedRequested;
        _petWindow.WaterRequested -= OnWaterRequested;
        _petWindow.PettingRequested -= OnPettingRequested;
        _petController.CareInteractionCommitted -=
            OnCareInteractionCommitted;
        _petController.CareInteractionTerminated -=
            OnCareInteractionTerminated;

        if (!_onlineSession.IsSuspended)
        {
            ApplyOnlineObservation(_onlineSession.Suspend());
        }

        if (!TryPersist(force: true))
        {
            Debug.WriteLine(
                "GuluPet care state could not be flushed during exit.");
        }

        _runtimeState.Clear();
        UpdateCareUi();
        _disposed = true;
    }

    internal void SuspendOnlineTracking()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dispatcher.VerifyAccess();
        if (_onlineSession.IsSuspended)
        {
            return;
        }

        _onlineTimer.Stop();
        ApplyOnlineObservation(_onlineSession.Suspend());
        if (!TryPersist(force: true))
        {
            Debug.WriteLine(
                "GuluPet care state could not be flushed before suspend.");
        }
    }

    internal void ResumeOnlineTracking()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dispatcher.VerifyAccess();
        if (!_onlineSession.IsSuspended)
        {
            return;
        }

        _onlineSession.Resume();
        _nextSaveAttemptAt = AddSaturated(
            ReadElapsed(),
            SaveInterval);
        _onlineTimer.Start();
    }

    private void OnFeedRequested(object? sender, EventArgs e) =>
        RequestCare(CareRuntimeState.FeedAction);

    private void OnWaterRequested(object? sender, EventArgs e) =>
        RequestCare(CareRuntimeState.WaterAction);

    private void OnPettingRequested(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _ = _petController.TriggerPettingFromInteractionBar();
        }
        catch (InvalidOperationException exception)
        {
            Debug.WriteLine(
                $"Unable to start GuluPet petting interaction: {exception}");
        }
    }

    private void RequestCare(string careAction)
    {
        if (_disposed || _runtimeState.InteractionBusy)
        {
            return;
        }

        // Arm before triggering. AnimationPlayer can synchronously present a
        // first frame and raise CareInteractionCommitted before the trigger
        // method returns.
        CareRequestAttempt attempt =
            _runtimeState.BeginRequest(careAction);
        BehaviorMutationResult result;
        try
        {
            result = _petController.TriggerCareInteraction(careAction);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                  or ArgumentException)
        {
            _ = _runtimeState.CompleteRequest(
                attempt,
                accepted: false,
                triggerRequestId: null,
                triggerBehaviorId: null);
            Debug.WriteLine(
                $"Unable to start GuluPet {careAction} interaction: " +
                exception);
            return;
        }

        _ = _runtimeState.CompleteRequest(
            attempt,
            result.Accepted,
            result.RequestId,
            result.BehaviorId?.Value);
    }

    private void OnCareInteractionCommitted(
        object? sender,
        CareInteractionCommittedEventArgs e)
    {
        if (_disposed
            || !_runtimeState.TryCommitVerified(
                e.CareAction,
                e.RequestId,
                e.BehaviorId.Value,
                e.SessionToken.Value,
                out CareState state))
        {
            return;
        }

        _stateDirty = true;
        _ = TryPersist(force: true);
        _petWindow.SetCareState(
            state.FoodLevel,
            state.WaterLevel,
            interactionBusy: true);
    }

    private void OnCareInteractionTerminated(
        object? sender,
        CareInteractionTerminatedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_runtimeState.TryTerminateVerified(
                e.CareAction,
                e.SessionToken.Value))
        {
            UpdateCareUi();
            return;
        }

        // A verified care session can terminate before presenting a first
        // frame. It did not refill and never entered busy, but its pending
        // request must not remain armed.
        _ = _runtimeState.TryCancelPendingVerifiedTermination(e.CareAction);
    }

    private void OnOnlineTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || _onlineSession.IsSuspended)
        {
            return;
        }

        ApplyOnlineObservation(_onlineSession.Observe());
        _ = TryPersist(force: false);
    }

    private void ApplyOnlineObservation(CareOnlineObservation observation)
    {
        if (observation.AppliedElapsed <= TimeSpan.Zero)
        {
            return;
        }

        CareState displayed = observation.State;
        CareState current = _care.Current;
        if (displayed.FoodLevel != current.FoodLevel
            || displayed.WaterLevel != current.WaterLevel)
        {
            throw new InvalidOperationException(
                "The care online observation is not the current state.");
        }

        if (observation.StateChanged)
        {
            _stateDirty = true;
            UpdateCareUi();
        }
    }

    private bool TryPersist(bool force)
    {
        TimeSpan now = ReadElapsed();
        if (!force && now < _nextSaveAttemptAt)
        {
            return true;
        }

        _nextSaveAttemptAt = AddSaturated(now, SaveInterval);
        if (!_stateDirty)
        {
            return true;
        }

        if (_store is null)
        {
            _stateDirty = false;
            return true;
        }

        try
        {
            _store.Save(_care.Current);
            _stateDirty = false;
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or InvalidDataException
                  or InvalidOperationException
                  or NotSupportedException)
        {
            Debug.WriteLine(
                $"Unable to save GuluPet care state: {exception}");
            return false;
        }
    }

    private void UpdateCareUi()
    {
        CareState state = _care.Current;
        _petWindow.SetCareState(
            state.FoodLevel,
            state.WaterLevel,
            _runtimeState.InteractionBusy);
    }

    private void OnPowerModeChanged(
        object sender,
        PowerModeChangedEventArgs e)
    {
        if (_disposed
            || e.Mode is not (PowerModes.Suspend or PowerModes.Resume)
            || _dispatcher.HasShutdownStarted
            || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        void ApplyPowerBoundary()
        {
            if (_disposed)
            {
                return;
            }

            if (e.Mode == PowerModes.Suspend)
            {
                SuspendOnlineTracking();
            }
            else
            {
                ResumeOnlineTracking();
            }
        }

        try
        {
            if (_dispatcher.CheckAccess())
            {
                ApplyPowerBoundary();
            }
            else
            {
                _dispatcher.Invoke(
                    DispatcherPriority.Send,
                    (Action)ApplyPowerBoundary);
            }
        }
        catch (InvalidOperationException exception)
        {
            Debug.WriteLine(
                $"Unable to apply GuluPet care power boundary: {exception}");
        }
    }

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

    private static TimeSpan AddSaturated(
        TimeSpan value,
        TimeSpan increment) =>
        value > TimeSpan.MaxValue - increment
            ? TimeSpan.MaxValue
            : value + increment;
}
