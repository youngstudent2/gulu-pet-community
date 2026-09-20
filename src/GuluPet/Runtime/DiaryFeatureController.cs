using System.IO;
using System.Security.Cryptography;
using System.Windows.Threading;
using GuluPet.Behavior;
using GuluPet.Diagnostics;
using GuluPet.Diary;
using GuluPet.Persistence;
using GuluPet.Presentation;
using Microsoft.Win32;

namespace GuluPet.Runtime;

/// <summary>
/// Connects durable diary facts to the pet runtime. The community build uses
/// a deterministic local generator; applications may inject another
/// <see cref="IDiaryGenerator"/> explicitly.
/// </summary>
internal sealed class DiaryFeatureController : IDisposable
{
    private static readonly TimeSpan RuntimeObservationInterval =
        TimeSpan.FromSeconds(30);
    private readonly PetController _petController;
    private readonly PostcardFeatureController _postcardFeature;
    private readonly MemoryFeatureController _memoryFeature;
    private readonly LocalErrorLog _errorLog;
    private readonly DiaryDatePolicy _datePolicy;
    private readonly DiaryLedger _ledger;
    private readonly DiaryRuntimeAccumulator _runtimeAccumulator;
    private readonly Func<TimeSpan> _readElapsed;
    private readonly TimeProvider _timeProvider;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _runtimeTimer;
    private readonly DispatcherTimer _generationTimer;
    private readonly DiaryGenerationCoordinator? _generationCoordinator;
    private readonly TimeSpan _generationJitter;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly bool _observesPowerEvents;
    private DateTimeOffset? _scheduledGenerationSlotUtc;
    private bool _disposed;

    internal DiaryFeatureController(
        PetWindow petWindow,
        PetController petController,
        PostcardFeatureController postcardFeature,
        MemoryFeatureController memoryFeature,
        LocalErrorLog errorLog,
        bool isolatedTestInstance)
        : this(
            petWindow,
            petController,
            postcardFeature,
            memoryFeature,
            errorLog,
            isolatedTestInstance ? null : new DiaryStateStore(),
            static () => SystemMonotonicClock.Instance.Elapsed,
            TimeProvider.System,
            observePowerEvents: !isolatedTestInstance,
            enableGeneration: !isolatedTestInstance)
    {
    }

    internal DiaryFeatureController(
        PetWindow petWindow,
        PetController petController,
        PostcardFeatureController postcardFeature,
        MemoryFeatureController memoryFeature,
        LocalErrorLog errorLog,
        DiaryStateStore? stateStore,
        Func<TimeSpan> readElapsed,
        TimeProvider timeProvider,
        bool observePowerEvents,
        bool enableGeneration,
        IDiaryGenerator? generator = null)
    {
        ArgumentNullException.ThrowIfNull(petWindow);
        _petController = petController
            ?? throw new ArgumentNullException(nameof(petController));
        _postcardFeature = postcardFeature
            ?? throw new ArgumentNullException(nameof(postcardFeature));
        _memoryFeature = memoryFeature
            ?? throw new ArgumentNullException(nameof(memoryFeature));
        _errorLog = errorLog
            ?? throw new ArgumentNullException(nameof(errorLog));
        _readElapsed = readElapsed
            ?? throw new ArgumentNullException(nameof(readElapsed));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _dispatcher = petWindow.Dispatcher;
        _dispatcher.VerifyAccess();
        _observesPowerEvents = observePowerEvents;

        _datePolicy = new DiaryDatePolicy();
        try
        {
            _ledger = new DiaryLedger(stateStore);
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException
                  or InvalidOperationException)
        {
            _errorLog.Write(
                "diary",
                "load-state",
                "diary-state-load-failed",
                exception);
            _ledger = new DiaryLedger();
        }

        TimeSpan initialElapsed = ReadElapsed();
        _runtimeAccumulator = new DiaryRuntimeAccumulator(
            _datePolicy,
            initialElapsed);
        _runtimeTimer = new DispatcherTimer(
            RuntimeObservationInterval,
            DispatcherPriority.Background,
            OnRuntimeTimerTick,
            _dispatcher);
        _generationTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            _dispatcher);
        _generationTimer.Tick += OnGenerationTimerTick;
        _generationJitter = TimeSpan.Zero;

        if (enableGeneration)
        {
            IDiaryGenerator usableGenerator =
                generator ?? new LocalDiaryGenerator(_timeProvider);
            _generationCoordinator = new DiaryGenerationCoordinator(
                _ledger,
                _datePolicy,
                usableGenerator);
            _generationCoordinator.FailureObserved +=
                OnGenerationFailureObserved;
        }

        _petController.UserInteractionAccepted += OnUserInteractionAccepted;
        _petController.RuntimeContextObserved += OnRuntimeContextObserved;
        _postcardFeature.UserInteractionAccepted += OnUserInteractionAccepted;
        _postcardFeature.StateChanged += OnUnlockStateChanged;
        _memoryFeature.UserInteractionAccepted += OnUserInteractionAccepted;
        _memoryFeature.StateChanged += OnUnlockStateChanged;
        if (_observesPowerEvents)
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        SyncUnlocks();
        RecordWeather(_petController.CurrentRuntimeContext);
        _runtimeTimer.Start();
        if (_generationCoordinator is not null)
        {
            ScheduleNextGenerationCheck();
            RequestGenerationCheck();
        }
    }

    internal event EventHandler? StateChanged;

    internal DiaryState Current => _ledger.Current;

    internal void RequestGenerationCheck()
    {
        if (_disposed || _generationCoordinator is null)
        {
            return;
        }

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        DateTimeOffset intendedCheckUtc =
            DiaryGenerationSchedule.NormalizeImmediateCheckUtc(
                nowUtc,
                _datePolicy.TimeZone);
        if (_generationJitter <= TimeSpan.Zero)
        {
            _ = CheckGenerationAsync(intendedCheckUtc);
            return;
        }

        ScheduleGenerationTimer(
            intendedCheckUtc,
            DiaryGenerationSchedule.ApplyInstallationJitter(
                nowUtc,
                _generationJitter));
    }

    internal void SeedPreviewForTest()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dispatcher.VerifyAccess();
        if (_generationCoordinator is not null || _observesPowerEvents)
        {
            throw new InvalidOperationException(
                "Diary preview data is restricted to isolated test instances.");
        }

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        DateOnly date = _datePolicy.GetCalendarDate(nowUtc);
        if (_ledger.Current.Days.Any(day => day.Date == date))
        {
            RaiseStateChanged();
            return;
        }

        _ledger.AddRuntime(
            date,
            (long)TimeSpan.FromHours(4.7).TotalSeconds,
            nowUtc);
        for (var index = 0; index < 3; index++)
        {
            _ledger.RecordInteraction(
                date,
                DiaryInteractionKinds.Click,
                nowUtc.AddMinutes(-90 + index));
        }

        for (var index = 0; index < 5; index++)
        {
            _ledger.RecordInteraction(
                date,
                DiaryInteractionKinds.Petting,
                nowUtc.AddMinutes(-60 + index));
        }

        _ledger.RecordInteraction(
            date,
            DiaryInteractionKinds.Feed,
            nowUtc.AddMinutes(-45));
        _ledger.RecordInteraction(
            date,
            DiaryInteractionKinds.OutingStart,
            nowUtc.AddMinutes(-30));
        _ledger.RecordWeather(
            date,
            new DiaryWeatherSnapshot
            {
                Kind = "clear",
                TemperatureCelsius = 28.6,
                ObservedAtUtc = nowUtc.AddHours(-1),
            },
            nowUtc);
        _ledger.RecordPostcard(
            date,
            new DiaryUnlockRecord
            {
                Id = "preview-postcard",
                Title = "晚风里的木棉",
                Detail = "广州",
                UnlockedAtUtc = nowUtc.AddMinutes(-25),
            },
            nowUtc);
        _ledger.RecordMemory(
            date,
            new DiaryUnlockRecord
            {
                Id = "preview-memory",
                Title = "第一次靠近妈咪的手心",
                Description = "咕噜走近伸来的手，先低头嗅闻指尖，再用脸颊轻轻贴过掌心。",
                UnlockedAtUtc = nowUtc.AddMinutes(-20),
            },
            nowUtc);
        _ledger.RecordGenerationSuccess(
            date,
            new GeneratedDiaryEntry
            {
                Title = "只是刚好待在这里",
                Mood = "若无其事",
                Body = "今天妈咪在桌前忙了很久。我原本没打算理她，" +
                    "只是在旁边找了个不碍事的位置趴着。她经过时点了点我，" +
                    "又认真摸了好几次。算了，看在手心还算暖的份上，我没有躲开。" +
                    "后来我吃到一小份猫条，又背着轻轻的风出了趟门，带回一张写着木棉晚风的明信片。" +
                    "天气很晴，窗边的光落在毛尖上。妈咪没有一直看我，这样正好。" +
                    "四个多小时里，她忙她的，我守我的。至于是不是特意陪她？" +
                    "才不是，只是这里待惯了而已。",
                Model = "local-template-v1",
                GeneratedAtUtc = nowUtc,
            },
            nowUtc);

        for (var dayOffset = 1; dayOffset <= 10; dayOffset++)
        {
            DateOnly historicalDate = date.AddDays(-dayOffset);
            DateTimeOffset historicalTimestamp = nowUtc.AddDays(-dayOffset);
            _ledger.AddRuntime(
                historicalDate,
                (long)TimeSpan.FromMinutes(35 + (dayOffset * 17)).TotalSeconds,
                historicalTimestamp);
            _ledger.RecordInteraction(
                historicalDate,
                dayOffset % 2 == 0
                    ? DiaryInteractionKinds.Click
                    : DiaryInteractionKinds.Petting,
                historicalTimestamp.AddMinutes(-20));

            if (dayOffset % 3 == 1)
            {
                continue;
            }

            _ledger.RecordGenerationSuccess(
                historicalDate,
                new GeneratedDiaryEntry
                {
                    Title = dayOffset % 2 == 0
                        ? "窗边的位置还算不错"
                        : "安静陪着也不算麻烦",
                    Mood = dayOffset % 2 == 0 ? "悠闲" : "平静",
                    Body = "妈咪照常忙她的事情，我也照常选了一个看得见她的位置。" +
                        "偶尔得到一点关注，偶尔什么都不做，这样的一天也很好。",
                    Model = "local-template-v1",
                    GeneratedAtUtc = historicalTimestamp,
                },
                historicalTimestamp);
        }

        RaiseStateChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.VerifyAccess();
        _disposed = true;
        _lifetime.Cancel();
        _runtimeTimer.Stop();
        _runtimeTimer.Tick -= OnRuntimeTimerTick;
        _generationTimer.Stop();
        _generationTimer.Tick -= OnGenerationTimerTick;
        if (_observesPowerEvents)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }

        _petController.UserInteractionAccepted -= OnUserInteractionAccepted;
        _petController.RuntimeContextObserved -= OnRuntimeContextObserved;
        _postcardFeature.UserInteractionAccepted -= OnUserInteractionAccepted;
        _postcardFeature.StateChanged -= OnUnlockStateChanged;
        _memoryFeature.UserInteractionAccepted -= OnUserInteractionAccepted;
        _memoryFeature.StateChanged -= OnUnlockStateChanged;
        if (_generationCoordinator is not null)
        {
            _generationCoordinator.FailureObserved -=
                OnGenerationFailureObserved;
            _generationCoordinator.Dispose();
        }

        FlushRuntime();
        _lifetime.Dispose();
    }

    private void OnRuntimeTimerTick(object? sender, EventArgs e) =>
        FlushRuntime();

    private void FlushRuntime()
    {
        if (_runtimeAccumulator.IsSuspended)
        {
            return;
        }

        try
        {
            DiaryRuntimeObservation observation = _runtimeAccumulator.Observe(
                _timeProvider.GetUtcNow(),
                ReadElapsed());
            if (observation.Segments.Count > 0)
            {
                _ledger.AddRuntimeSegments(
                    observation.Segments,
                    observation.ObservedAtUtc);
            }

            _runtimeAccumulator.Accept(observation);
            if (observation.Segments.Count > 0)
            {
                RaiseStateChanged();
            }
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException
                  or InvalidOperationException
                  or OverflowException)
        {
            _errorLog.Write(
                "diary",
                "record-runtime",
                "diary-runtime-write-failed",
                exception);
        }
    }

    private void OnUserInteractionAccepted(
        object? sender,
        UserInteractionAcceptedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _ledger.RecordInteraction(
                _datePolicy.GetActivityDate(e.OccurredAtUtc),
                MapInteraction(e.Kind),
                e.OccurredAtUtc);
            SyncUnlocks();
            RaiseStateChanged();
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException
                  or InvalidOperationException
                  or ArgumentException
                  or OverflowException)
        {
            _errorLog.Write(
                "diary",
                "record-interaction",
                "diary-interaction-write-failed",
                exception);
        }
    }

    private void OnUnlockStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        SyncUnlocks();
    }

    private void SyncUnlocks()
    {
        try
        {
            bool changed = false;
            foreach (PostcardDiaryUnlockSnapshot postcard in
                     _postcardFeature.GetDiaryUnlocks())
            {
                changed |= _ledger.RecordPostcard(
                    _datePolicy.GetActivityDate(postcard.UnlockedAtUtc),
                    new DiaryUnlockRecord
                    {
                        Id = postcard.Id,
                        Title = postcard.Title,
                        Detail = postcard.Location,
                        UnlockedAtUtc = postcard.UnlockedAtUtc,
                    },
                    _timeProvider.GetUtcNow());
            }

            foreach (MemoryDiaryUnlockSnapshot memory in
                     _memoryFeature.GetDiaryUnlocks())
            {
                changed |= _ledger.RecordMemory(
                    _datePolicy.GetActivityDate(memory.UnlockedAtUtc),
                    new DiaryUnlockRecord
                    {
                        Id = memory.Id,
                        Title = memory.Title,
                        Description = memory.Description,
                        UnlockedAtUtc = memory.UnlockedAtUtc,
                    },
                    _timeProvider.GetUtcNow());
            }

            if (changed)
            {
                RaiseStateChanged();
            }
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException
                  or InvalidOperationException)
        {
            _errorLog.Write(
                "diary",
                "sync-unlocks",
                "diary-unlock-sync-failed",
                exception);
        }
    }

    private void OnRuntimeContextObserved(
        object? sender,
        RuntimeContextObservedEventArgs e) =>
        RecordWeather(e.Snapshot);

    private void RecordWeather(BehaviorContextSnapshot context)
    {
        if (_disposed
            || !context.WeatherAvailable
            || string.IsNullOrWhiteSpace(context.WeatherKind)
            || !double.IsFinite(context.TemperatureCelsius))
        {
            return;
        }

        DateTimeOffset observedAtUtc = _timeProvider.GetUtcNow();
        try
        {
            DateOnly activityDate = _datePolicy.GetActivityDate(observedAtUtc);
            DiaryWeatherSnapshot? previous = _ledger.Current.Days
                .FirstOrDefault(day => day.Date == activityDate)?
                .Weather;
            if (previous is not null
                && observedAtUtc - previous.ObservedAtUtc
                    < TimeSpan.FromMinutes(30)
                && string.Equals(
                    previous.Kind,
                    context.WeatherKind,
                    StringComparison.Ordinal)
                && Math.Abs(
                    previous.TemperatureCelsius
                    - context.TemperatureCelsius) < 0.5)
            {
                return;
            }

            _ledger.RecordWeather(
                activityDate,
                new DiaryWeatherSnapshot
                {
                    Kind = context.WeatherKind,
                    TemperatureCelsius = context.TemperatureCelsius,
                    ObservedAtUtc = observedAtUtc,
                },
                observedAtUtc);
            RaiseStateChanged();
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or IOException
                  or UnauthorizedAccessException
                  or InvalidOperationException)
        {
            _errorLog.Write(
                "diary",
                "record-weather",
                "diary-weather-write-failed",
                exception);
        }
    }

    private async void OnGenerationTimerTick(object? sender, EventArgs e)
    {
        _generationTimer.Stop();
        DateTimeOffset checkSlotUtc = _scheduledGenerationSlotUtc
            ?? DiaryGenerationSchedule.NormalizeImmediateCheckUtc(
                _timeProvider.GetUtcNow(),
                _datePolicy.TimeZone);
        _scheduledGenerationSlotUtc = null;
        try
        {
            await CheckGenerationAsync(checkSlotUtc);
        }
        finally
        {
            if (!_disposed)
            {
                ScheduleNextGenerationCheck();
            }
        }
    }

    private async Task CheckGenerationAsync(DateTimeOffset checkAtUtc)
    {
        if (_disposed || _generationCoordinator is null)
        {
            return;
        }

        FlushRuntime();
        SyncUnlocks();
        try
        {
            int generated = await _generationCoordinator.CheckAsync(
                checkAtUtc,
                _lifetime.Token);
            if (generated > 0)
            {
                RaiseStateChanged();
            }
        }
        catch (OperationCanceledException)
            when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _errorLog.Write(
                "diary",
                "generate",
                "diary-generation-unhandled",
                exception);
        }
    }

    private void OnGenerationFailureObserved(
        object? sender,
        DiaryGenerationFailureObservedEventArgs e)
    {
        if (e.Result.Outcome == DiaryGenerationOutcome.InsufficientBalance
            || string.Equals(
                e.Result.Code,
                "upstream_balance_unavailable",
                StringComparison.Ordinal)
            || string.Equals(
                e.Result.Code,
                "upstream-balance-unavailable",
                StringComparison.Ordinal))
        {
            // Keep this retryable failure quiet in each client. The proxy
            // aggregates one operator alert and supplies the retry window.
            return;
        }

        var context = new Dictionary<string, string>
        {
            ["failureCode"] = e.Result.Code,
            ["diaryDate"] = e.Date.ToString("yyyy-MM-dd"),
            ["service"] = "configured-diary-provider",
        };
        if (e.Result.StatusCode is { } statusCode)
        {
            context["statusCode"] = ((int)statusCode).ToString();
        }

        _errorLog.Write(
            "diary",
            "generate",
            e.Result.Code,
            context: context);
    }

    private void ScheduleNextGenerationCheck()
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        DateTimeOffset targetUtc = DiaryGenerationSchedule.GetNextSlotUtc(
            nowUtc,
            _datePolicy.TimeZone);
        ScheduleGenerationTimer(
            targetUtc,
            DiaryGenerationSchedule.ApplyInstallationJitter(
                targetUtc,
                _generationJitter));
    }

    private void ScheduleGenerationTimer(
        DateTimeOffset intendedCheckUtc,
        DateTimeOffset fireAtUtc)
    {
        _generationTimer.Stop();
        _scheduledGenerationSlotUtc = intendedCheckUtc;
        TimeSpan delay = fireAtUtc - _timeProvider.GetUtcNow();
        _generationTimer.Interval = delay > TimeSpan.FromSeconds(1)
            ? delay
            : TimeSpan.FromSeconds(1);
        _generationTimer.Start();
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

        void ApplyBoundary()
        {
            if (_disposed)
            {
                return;
            }

            TimeSpan elapsed = ReadElapsed();
            if (e.Mode == PowerModes.Suspend)
            {
                FlushRuntime();
                _runtimeAccumulator.Suspend(elapsed);
                _runtimeTimer.Stop();
                _generationTimer.Stop();
            }
            else
            {
                _runtimeAccumulator.Resume(elapsed);
                _runtimeTimer.Start();
                ScheduleNextGenerationCheck();
                RequestGenerationCheck();
            }
        }

        try
        {
            if (_dispatcher.CheckAccess())
            {
                ApplyBoundary();
            }
            else
            {
                _dispatcher.Invoke(
                    DispatcherPriority.Send,
                    (Action)ApplyBoundary);
            }
        }
        catch (InvalidOperationException exception)
        {
            _errorLog.Write(
                "diary",
                "power-boundary",
                "diary-power-boundary-failed",
                exception);
        }
    }

    private TimeSpan ReadElapsed()
    {
        TimeSpan elapsed = _readElapsed();
        if (elapsed < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The diary monotonic clock returned a negative value.");
        }

        return elapsed;
    }

    private void RaiseStateChanged()
    {
        if (_dispatcher.CheckAccess())
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (!_dispatcher.HasShutdownStarted
                 && !_dispatcher.HasShutdownFinished)
        {
            _ = _dispatcher.BeginInvoke(
                () => StateChanged?.Invoke(this, EventArgs.Empty),
                DispatcherPriority.Background);
        }
    }

    private static string MapInteraction(UserInteractionKind kind) =>
        kind switch
        {
            UserInteractionKind.Click => DiaryInteractionKinds.Click,
            UserInteractionKind.LongPress => DiaryInteractionKinds.LongPress,
            UserInteractionKind.Drag => DiaryInteractionKinds.Drag,
            UserInteractionKind.DockLeft => DiaryInteractionKinds.DockLeft,
            UserInteractionKind.DockRight => DiaryInteractionKinds.DockRight,
            UserInteractionKind.Approach => DiaryInteractionKinds.Approach,
            UserInteractionKind.Hover => DiaryInteractionKinds.Hover,
            UserInteractionKind.Petting => DiaryInteractionKinds.Petting,
            UserInteractionKind.PettingEnded =>
                DiaryInteractionKinds.PettingEnded,
            UserInteractionKind.ToolbarPetting =>
                DiaryInteractionKinds.ToolbarPetting,
            UserInteractionKind.RapidPointer =>
                DiaryInteractionKinds.RapidPointer,
            UserInteractionKind.CirclePointer =>
                DiaryInteractionKinds.CirclePointer,
            UserInteractionKind.Feed => DiaryInteractionKinds.Feed,
            UserInteractionKind.Water => DiaryInteractionKinds.Water,
            UserInteractionKind.OutingStart =>
                DiaryInteractionKinds.OutingStart,
            UserInteractionKind.PostcardClaim =>
                DiaryInteractionKinds.PostcardClaim,
            UserInteractionKind.MemoryReplay =>
                DiaryInteractionKinds.MemoryReplay,
            UserInteractionKind.ManualBehavior =>
                DiaryInteractionKinds.ManualBehavior,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}
