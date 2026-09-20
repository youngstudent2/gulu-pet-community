using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GuluPet.Behavior;
using GuluPet.Diagnostics;
using GuluPet.Platform;
using GuluPet.Sensing;

namespace GuluPet.Tests;

internal static class RuntimeContextMonitorTests
{
    private static readonly DateTimeOffset Origin =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.FromHours(8));

    public static void RunAll()
    {
        Run(
            nameof(ApplicationClassifierReturnsOnlyBroadCategories),
            ApplicationClassifierReturnsOnlyBroadCategories);
        Run(
            nameof(RollingInputWindowKeepsOnlyAggregateLastMinute),
            RollingInputWindowKeepsOnlyAggregateLastMinute);
        Run(
            nameof(ApplicationStabilityKeepsExternalCategoryDuringPetFocus),
            ApplicationStabilityKeepsExternalCategoryDuringPetFocus);
        Run(
            nameof(BusyStateUsesEnterAndExitHysteresis),
            BusyStateUsesEnterAndExitHysteresis);
        Run(
            nameof(MonitorComposesContextFromDerivedSources),
            MonitorComposesContextFromDerivedSources);
        Run(
            nameof(MonitorStartDoesNotWaitForWeatherNetwork),
            MonitorStartDoesNotWaitForWeatherNetwork);
        Run(
            nameof(MonitorIsolatesFailedSourceAndRecoversAfterBackoff),
            MonitorIsolatesFailedSourceAndRecoversAfterBackoff);
        Run(
            nameof(MonitorRestartsUnavailableInputAfterBackoff),
            MonitorRestartsUnavailableInputAfterBackoff);
        Run(
            nameof(MonitorStopRestartAndDisposeAreSerialized),
            MonitorStopRestartAndDisposeAreSerialized);
        Run(
            nameof(SessionLockFlowsIntoContextAndUnlockTriggersResume),
            SessionLockFlowsIntoContextAndUnlockTriggersResume);
        Run(
            nameof(GlobalInputCounterStartsAndStopsWithoutUiThreadWork),
            GlobalInputCounterStartsAndStopsWithoutUiThreadWork);
        Run(
            nameof(GlobalInputPartialHookFailureCleansUpAndCanRestart),
            GlobalInputPartialHookFailureCleansUpAndCanRestart);
        Run(
            nameof(WindowsSourcesExposeOnlyDerivedContext),
            WindowsSourcesExposeOnlyDerivedContext);
        Run(
            nameof(OpenMeteoMapsCurrentWeatherAndRetainsBoundedCache),
            OpenMeteoMapsCurrentWeatherAndRetainsBoundedCache);
        Run(
            nameof(OpenMeteoTimeoutBecomesUnavailable),
            OpenMeteoTimeoutBecomesUnavailable);
        Run(
            nameof(OpenMeteoClassifiesAnonymousFailureCategories),
            OpenMeteoClassifiesAnonymousFailureCategories);
        Run(
            nameof(OpenMeteoBacksOffAndRecovers),
            OpenMeteoBacksOffAndRecovers);
        Run(
            nameof(HealthLogUsesWhitelistAndBoundedRotation),
            HealthLogUsesWhitelistAndBoundedRotation);
        Run(
            nameof(HealthLogPrunesLegacyMixedRecordsByTimestamp),
            HealthLogPrunesLegacyMixedRecordsByTimestamp);
        Run(
            nameof(HealthLogUtcDayAndSizeRotationRemainAgeBounded),
            HealthLogUtcDayAndSizeRotationRemainAgeBounded);
        Run(
            nameof(HealthLogHourlyMaintenanceExpiresBeforeSeventyThreeHours),
            HealthLogHourlyMaintenanceExpiresBeforeSeventyThreeHours);
        Run(
            nameof(HealthLogClockRollbackPrunesFutureRecords),
            HealthLogClockRollbackPrunesFutureRecords);
        Run(
            nameof(HealthLogRepairsMissingNewlineAndDropsFutureEvents),
            HealthLogRepairsMissingNewlineAndDropsFutureEvents);
        Run(
            nameof(HealthLogDeletesOnlyExclusiveExactOrphanTemps),
            HealthLogDeletesOnlyExclusiveExactOrphanTemps);
        Run(
            nameof(HealthLogRefusesReparseDirectory),
            HealthLogRefusesReparseDirectory);
    }

    private static void ApplicationClassifierReturnsOnlyBroadCategories()
    {
        var examples = new Dictionary<string, string>
        {
            ["chrome.exe"] = "browser",
            ["PotPlayerMini64"] = "media_player",
            ["WINWORD.EXE"] = "office",
            ["Code"] = "ide",
            ["cs2.exe"] = "game",
            ["Battle.net"] = "game",
            ["WeChat.exe"] = "communication",
            ["explorer.exe"] = "file_manager",
            ["private-customer-document.exe"] = "unknown",
        };
        string[] allowed =
        [
            "browser",
            "media_player",
            "office",
            "ide",
            "game",
            "communication",
            "file_manager",
            "unknown",
        ];
        foreach ((string processName, string expected) in examples)
        {
            string actual = ApplicationCategoryClassifier.Classify(
                processName);
            BehaviorTestCheck.Equal(expected, actual);
            BehaviorTestCheck.True(allowed.Contains(actual));
        }
    }

    private static void RollingInputWindowKeepsOnlyAggregateLastMinute()
    {
        var window = new RollingInputRateWindow(TimeSpan.FromMinutes(1));
        BehaviorTestCheck.Equal(
            default,
            window.Observe(
                Origin,
                new InputActivityReading(100, 20, true)));

        InputActivityRates active = window.Observe(
            Origin + TimeSpan.FromSeconds(10),
            new InputActivityReading(112, 23, true));
        BehaviorTestCheck.Close(12, active.KeyboardPerMinute);
        BehaviorTestCheck.Close(3, active.MouseClicksPerMinute);

        InputActivityRates expired = window.Observe(
            Origin + TimeSpan.FromSeconds(71),
            new InputActivityReading(112, 23, true));
        BehaviorTestCheck.Close(0, expired.KeyboardPerMinute);
        BehaviorTestCheck.Close(0, expired.MouseClicksPerMinute);

        InputActivityRates unavailable = window.Observe(
            Origin + TimeSpan.FromSeconds(72),
            new InputActivityReading(999, 999, false));
        BehaviorTestCheck.Equal(default, unavailable);

        InputActivityRates newBaseline = window.Observe(
            Origin + TimeSpan.FromSeconds(73),
            new InputActivityReading(5, 1, true));
        BehaviorTestCheck.Equal(default, newBaseline);
    }

    private static void ApplicationStabilityKeepsExternalCategoryDuringPetFocus()
    {
        var tracker = new ApplicationStabilityTracker(
            TimeSpan.FromSeconds(5));
        ApplicationContextReading browser = tracker.Observe(
            Origin,
            new ForegroundApplicationReading("browser", true, false));
        BehaviorTestCheck.Equal("browser", browser.Category);
        BehaviorTestCheck.True(browser.IsAvailable);

        ApplicationContextReading pet = tracker.Observe(
            Origin + TimeSpan.FromSeconds(3),
            new ForegroundApplicationReading("unknown", false, true));
        BehaviorTestCheck.Equal("browser", pet.Category);
        BehaviorTestCheck.Equal(TimeSpan.FromSeconds(3), pet.StableFor);

        ApplicationContextReading expired = tracker.Observe(
            Origin + TimeSpan.FromSeconds(6),
            new ForegroundApplicationReading("unknown", false, true));
        BehaviorTestCheck.Equal("unknown", expired.Category);
        BehaviorTestCheck.False(expired.IsAvailable);
        BehaviorTestCheck.Equal(TimeSpan.Zero, expired.StableFor);
    }

    private static void BusyStateUsesEnterAndExitHysteresis()
    {
        var tracker = new BusyStateTracker(
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(8));
        var high = new InputActivityRates(180, 5);
        BehaviorTestCheck.False(
            tracker.Observe(
                Origin,
                high,
                TimeSpan.FromSeconds(2),
                dataAvailable: true));
        BehaviorTestCheck.False(
            tracker.Observe(
                Origin + TimeSpan.FromSeconds(119),
                high,
                TimeSpan.FromSeconds(2),
                dataAvailable: true));
        BehaviorTestCheck.True(
            tracker.Observe(
                Origin + TimeSpan.FromMinutes(2),
                high,
                TimeSpan.FromSeconds(2),
                dataAvailable: true));

        BehaviorTestCheck.True(
            tracker.Observe(
                Origin + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(7),
                default,
                TimeSpan.FromSeconds(10),
                dataAvailable: true));
        BehaviorTestCheck.False(
            tracker.Observe(
                Origin + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(15),
                default,
                TimeSpan.FromSeconds(10),
                dataAvailable: true));
    }

    private static void MonitorComposesContextFromDerivedSources()
    {
        var input = new FakeInputSource(
            new InputActivityReading(10, 2, true));
        var foreground = new FakeForegroundSource(
            new ForegroundApplicationReading("browser", true, false));
        var idle = new FakeIdleSource(TimeSpan.FromSeconds(4));
        var weather = new FakeWeatherProvider(
            new WeatherReading(
                WeatherDataQuality.Fresh,
                "rain",
                31.5,
                63,
                Origin));
        using var monitor = new RuntimeContextMonitor(
            input,
            foreground,
            idle,
            weather,
            options: new RuntimeContextMonitorOptions
            {
                BusyEnterAfter = TimeSpan.Zero,
            });

        BehaviorContextSnapshot baseline = monitor.CaptureOnce(Origin);
        input.Reading = new InputActivityReading(190, 47, true);
        BehaviorContextSnapshot snapshot = monitor.CaptureOnce(
            Origin + TimeSpan.FromSeconds(30));

        BehaviorTestCheck.True(snapshot.Revision > baseline.Revision);
        BehaviorTestCheck.Equal(
            Origin + TimeSpan.FromSeconds(30),
            snapshot.LocalNow);
        BehaviorTestCheck.Equal("browser", snapshot.ApplicationCategory);
        BehaviorTestCheck.True(snapshot.ApplicationCategoryAvailable);
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(30),
            snapshot.ApplicationStableFor);
        BehaviorTestCheck.Close(180, snapshot.KeyboardPerMinute);
        BehaviorTestCheck.Close(45, snapshot.MouseClicksPerMinute);
        BehaviorTestCheck.Equal(190L, snapshot.KeyboardCountSinceStart);
        BehaviorTestCheck.Equal(47L, snapshot.MouseClickCountSinceStart);
        BehaviorTestCheck.Equal(
            new InputActivityReading(190, 47, true),
            monitor.ReadCurrentInputActivity());
        BehaviorTestCheck.Equal(TimeSpan.FromSeconds(4), snapshot.IdleFor);
        BehaviorTestCheck.True(snapshot.IsBusy);
        BehaviorTestCheck.True(snapshot.WeatherAvailable);
        BehaviorTestCheck.Equal("rain", snapshot.WeatherKind);
        BehaviorTestCheck.Close(31.5, snapshot.TemperatureCelsius);
    }

    private static void MonitorStartDoesNotWaitForWeatherNetwork()
    {
        var weather = new BlockingWeatherProvider();
        using var monitor = new RuntimeContextMonitor(
            new FakeInputSource(
                new InputActivityReading(0, 0, true)),
            new FakeForegroundSource(
                new ForegroundApplicationReading(
                    "unknown",
                    true,
                    false)),
            new FakeIdleSource(TimeSpan.Zero),
            weather,
            options: new RuntimeContextMonitorOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(20),
            });
        var stopwatch = Stopwatch.StartNew();
        monitor.Start();
        stopwatch.Stop();

        BehaviorTestCheck.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(250),
            $"Start blocked for {stopwatch.Elapsed}.");
        BehaviorTestCheck.True(
            SpinWait.SpinUntil(
                () => weather.RefreshStarted,
                TimeSpan.FromSeconds(2)),
            "The background weather refresh did not start.");
    }

    private static void MonitorIsolatesFailedSourceAndRecoversAfterBackoff()
    {
        var health = new RecordingHealthSink();
        var foreground = new RecoveringForegroundSource();
        using var monitor = new RuntimeContextMonitor(
            new FakeInputSource(
                new InputActivityReading(7, 2, true)),
            foreground,
            new FakeIdleSource(TimeSpan.FromSeconds(3)),
            new FakeWeatherProvider(
                new WeatherReading(
                    WeatherDataQuality.Fresh,
                    "clear",
                    26,
                    0,
                    Origin)),
            options: new RuntimeContextMonitorOptions
            {
                RecoveryInitialDelay = TimeSpan.FromSeconds(2),
                RecoveryMaximumDelay = TimeSpan.FromSeconds(4),
            },
            healthSink: health);

        BehaviorContextSnapshot failed = monitor.CaptureOnce(Origin);
        BehaviorTestCheck.False(
            failed.ApplicationCategoryAvailable);
        BehaviorTestCheck.True(failed.WeatherAvailable);
        BehaviorTestCheck.Equal(7L, failed.KeyboardCountSinceStart);
        BehaviorTestCheck.Equal(1, foreground.ReadCalls);

        BehaviorContextSnapshot backingOff = monitor.CaptureOnce(
            Origin + TimeSpan.FromSeconds(1));
        BehaviorTestCheck.False(
            backingOff.ApplicationCategoryAvailable);
        BehaviorTestCheck.Equal(1, foreground.ReadCalls);

        BehaviorContextSnapshot recovered = monitor.CaptureOnce(
            Origin + TimeSpan.FromSeconds(2));
        BehaviorTestCheck.True(
            recovered.ApplicationCategoryAvailable);
        BehaviorTestCheck.Equal("browser", recovered.ApplicationCategory);
        BehaviorTestCheck.Equal(2, foreground.ReadCalls);
        BehaviorTestCheck.True(
            health.Events.Any(
                static item =>
                    item.Source
                        == ContextHealthSource.ForegroundApplication
                    && item.Outcome
                        == ContextHealthOutcome.Failure
                    && item.ErrorCategory
                        == ContextHealthErrorCategory.Unexpected
                    && item.ConsecutiveFailures == 1));
        BehaviorTestCheck.True(
            health.Events.Any(
                static item =>
                    item.Source
                        == ContextHealthSource.ForegroundApplication
                    && item.Outcome
                        == ContextHealthOutcome.Recovered
                    && item.ErrorCategory
                        == ContextHealthErrorCategory.None
                    && item.ConsecutiveFailures == 0));
    }

    private static void MonitorRestartsUnavailableInputAfterBackoff()
    {
        var health = new RecordingHealthSink();
        var input = new FakeInputSource(
            new InputActivityReading(0, 0, false));
        using var monitor = new RuntimeContextMonitor(
            input,
            new FakeForegroundSource(
                new ForegroundApplicationReading(
                    "browser",
                    true,
                    false)),
            new FakeIdleSource(TimeSpan.Zero),
            new FakeWeatherProvider(WeatherReading.Unavailable),
            options: new RuntimeContextMonitorOptions
            {
                RecoveryInitialDelay = TimeSpan.FromSeconds(2),
                RecoveryMaximumDelay = TimeSpan.FromSeconds(4),
            },
            healthSink: health);

        BehaviorContextSnapshot unavailable = monitor.CaptureOnce(Origin);
        BehaviorTestCheck.Equal(0L, unavailable.KeyboardCountSinceStart);
        BehaviorTestCheck.Equal(1, input.StartCalls);
        BehaviorTestCheck.Equal(1, input.StopCalls);

        input.Reading = new InputActivityReading(12, 3, true);
        BehaviorContextSnapshot backingOff = monitor.CaptureOnce(
            Origin + TimeSpan.FromSeconds(1));
        BehaviorTestCheck.Equal(0L, backingOff.KeyboardCountSinceStart);
        BehaviorTestCheck.Equal(1, input.StartCalls);

        BehaviorContextSnapshot recovered = monitor.CaptureOnce(
            Origin + TimeSpan.FromSeconds(2));
        BehaviorTestCheck.Equal(12L, recovered.KeyboardCountSinceStart);
        BehaviorTestCheck.Equal(2, input.StartCalls);
        BehaviorTestCheck.True(
            health.Events.Any(
                static item =>
                    item.Source == ContextHealthSource.Input
                    && item.Outcome == ContextHealthOutcome.Recovered));
    }

    private static void MonitorStopRestartAndDisposeAreSerialized()
    {
        var input = new FakeInputSource(
            new InputActivityReading(0, 0, true));
        var monitor = new RuntimeContextMonitor(
            input,
            new FakeForegroundSource(
                new ForegroundApplicationReading(
                    "unknown",
                    true,
                    false)),
            new FakeIdleSource(TimeSpan.Zero),
            new FakeWeatherProvider(WeatherReading.Unavailable),
            options: new RuntimeContextMonitorOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(10),
            });
        var callbackCount = 0;
        monitor.SnapshotChanged += (_, _) =>
            Interlocked.Increment(ref callbackCount);
        monitor.Start();
        BehaviorTestCheck.True(
            SpinWait.SpinUntil(
                () => Volatile.Read(ref callbackCount) > 0,
                TimeSpan.FromSeconds(2)));

        monitor.Stop();
        int stoppedCount = Volatile.Read(ref callbackCount);
        Thread.Sleep(50);
        BehaviorTestCheck.Equal(
            stoppedCount,
            Volatile.Read(ref callbackCount));

        monitor.Start();
        BehaviorTestCheck.True(
            SpinWait.SpinUntil(
                () => Volatile.Read(ref callbackCount) > stoppedCount,
                TimeSpan.FromSeconds(2)));
        monitor.Stop();
        BehaviorTestCheck.Equal(2, input.StartCalls);
        BehaviorTestCheck.Equal(2, input.StopCalls);

        monitor.Dispose();
        BehaviorTestCheck.True(input.Disposed);
        BehaviorTestCheck.Throws<ObjectDisposedException>(monitor.Start);
    }

    private static void SessionLockFlowsIntoContextAndUnlockTriggersResume()
    {
        var session = new FakeSessionLockSource(
            new SessionLockReading(true, true));
        using var monitor = new RuntimeContextMonitor(
            new FakeInputSource(
                new InputActivityReading(0, 0, true)),
            new FakeForegroundSource(
                new ForegroundApplicationReading(
                    "unknown",
                    false,
                    false)),
            new FakeIdleSource(TimeSpan.Zero),
            new FakeWeatherProvider(WeatherReading.Unavailable),
            session);
        var router = new BehaviorContextTriggerRouter();

        BehaviorContextSnapshot locked = monitor.CaptureOnce(Origin);
        BehaviorTestCheck.True(locked.IsLocked);
        _ = router.Observe(locked);

        session.Reading = new SessionLockReading(false, true);
        BehaviorContextSnapshot unlocked = monitor.CaptureOnce(
            Origin + TimeSpan.FromSeconds(2));
        BehaviorTestCheck.False(unlocked.IsLocked);
        BehaviorTestCheck.True(
            router.Observe(unlocked).Any(
                static signal =>
                    signal.Tag == "trigger:user:resume"));

        var windowsNative = new FakeWindowsSessionLockNativeApi(
            available: true,
            isLocked: true);
        var windowsSource = new WindowsSessionLockSource(windowsNative);
        BehaviorTestCheck.Equal(
            new SessionLockReading(true, true),
            windowsSource.Read());
        windowsNative.Available = false;
        BehaviorTestCheck.Equal(
            new SessionLockReading(false, false),
            windowsSource.Read());
    }

    private static void GlobalInputCounterStartsAndStopsWithoutUiThreadWork()
    {
        using var source = new GlobalInputActivitySource();
        var stopwatch = Stopwatch.StartNew();
        source.Start();
        Thread.Sleep(25);
        InputActivityReading reading = source.Read();
        source.Stop();
        BehaviorTestCheck.False(source.Read().IsAvailable);
        source.Start();
        Thread.Sleep(25);
        source.Stop();
        stopwatch.Stop();

        BehaviorTestCheck.True(reading.KeyboardCount >= 0);
        BehaviorTestCheck.True(reading.MouseClickCount >= 0);
        BehaviorTestCheck.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"Global input hook lifecycle took {stopwatch.Elapsed}.");
    }

    private static void GlobalInputPartialHookFailureCleansUpAndCanRestart()
    {
        var native = new FakeGlobalInputHookNativeApi
        {
            FailMouseInstall = true,
        };
        using var source = new GlobalInputActivitySource(native);
        source.Start();
        BehaviorTestCheck.False(source.Read().IsAvailable);
        BehaviorTestCheck.True(
            SpinWait.SpinUntil(
                () => native.UnhookCalls == 1,
                TimeSpan.FromSeconds(2)),
            "The successfully installed keyboard hook was not cleaned up.");
        source.Stop();

        native.FailMouseInstall = false;
        source.Start();
        BehaviorTestCheck.True(source.Read().IsAvailable);
        source.Stop();
        BehaviorTestCheck.False(source.Read().IsAvailable);
        BehaviorTestCheck.Equal(3, native.UnhookCalls);
        BehaviorTestCheck.True(native.PostQuitCalls >= 1);
    }

    private static void WindowsSourcesExposeOnlyDerivedContext()
    {
        var foreground = new WindowsForegroundApplicationSource(
            new ApplicationCategoryProcessClassifier());
        ForegroundApplicationReading application = foreground.Read();
        string[] allowed =
        [
            "browser",
            "media_player",
            "office",
            "ide",
            "game",
            "communication",
            "file_manager",
            "unknown",
        ];
        BehaviorTestCheck.True(allowed.Contains(application.Category));

        var idle = new WindowsIdleTimeSource();
        if (idle.TryGetIdleTime(out TimeSpan idleFor))
        {
            BehaviorTestCheck.True(idleFor >= TimeSpan.Zero);
        }

        SessionLockReading session = new WindowsSessionLockSource().Read();
        if (session.IsAvailable)
        {
            _ = session.IsLocked;
        }
    }

    private static void OpenMeteoMapsCurrentWeatherAndRetainsBoundedCache()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "current": {
                "time": "2026-07-28T12:00",
                "temperature_2m": 31.25,
                "weather_code": 0
              }
            }
            """);
        handler.EnqueueResponse(HttpStatusCode.BadGateway);
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var provider = CreateWeatherProvider(client);

        provider.RefreshIfDueAsync(Origin, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        WeatherReading fresh = provider.GetCurrent(Origin);
        BehaviorTestCheck.Equal(WeatherDataQuality.Fresh, fresh.Quality);
        BehaviorTestCheck.Equal("clear", fresh.Kind);
        BehaviorTestCheck.Close(31.25, fresh.TemperatureCelsius);
        BehaviorTestCheck.Equal(0, fresh.WeatherCode);
        BehaviorTestCheck.Equal(1, handler.RequestCount);
        Uri defaultUri =
            OpenMeteoWeatherProvider.DefaultGuangzhouRequestUri;
        BehaviorTestCheck.True(
            defaultUri.Query.Contains(
                "latitude=23.1291",
                StringComparison.Ordinal));
        BehaviorTestCheck.True(
            defaultUri.Query.Contains(
                "longitude=113.2644",
                StringComparison.Ordinal));
        BehaviorTestCheck.True(
            defaultUri.Query.Contains(
                "current=temperature_2m%2Cweather_code",
                StringComparison.OrdinalIgnoreCase));

        provider.RefreshIfDueAsync(
                Origin + TimeSpan.FromMinutes(10),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        BehaviorTestCheck.Equal(1, handler.RequestCount);

        provider.RefreshIfDueAsync(
                Origin + TimeSpan.FromMinutes(46),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        BehaviorTestCheck.Equal(2, handler.RequestCount);
        WeatherReading stale = provider.GetCurrent(
            Origin + TimeSpan.FromHours(2));
        BehaviorTestCheck.Equal(WeatherDataQuality.Stale, stale.Quality);
        BehaviorTestCheck.Equal("clear", stale.Kind);

        using var staleMonitor = new RuntimeContextMonitor(
            new FakeInputSource(
                new InputActivityReading(0, 0, true)),
            new FakeForegroundSource(
                new ForegroundApplicationReading(
                    "unknown",
                    false,
                    false)),
            new FakeIdleSource(TimeSpan.Zero),
            provider);
        BehaviorContextSnapshot staleContext = staleMonitor.CaptureOnce(
            Origin + TimeSpan.FromHours(2));
        BehaviorTestCheck.False(staleContext.WeatherAvailable);
        BehaviorTestCheck.Equal("unavailable", staleContext.WeatherKind);
        BehaviorTestCheck.Close(0, staleContext.TemperatureCelsius);

        WeatherReading expired = provider.GetCurrent(
            Origin + TimeSpan.FromHours(13));
        BehaviorTestCheck.Equal(
            WeatherDataQuality.Unavailable,
            expired.Quality);
        BehaviorTestCheck.Equal("unavailable", expired.Kind);

        BehaviorTestCheck.Equal(
            "rain",
            OpenMeteoWeatherProvider.MapWeatherCode(63));
        BehaviorTestCheck.Equal(
            "storm",
            OpenMeteoWeatherProvider.MapWeatherCode(95));
        BehaviorTestCheck.Equal(
            "fog",
            OpenMeteoWeatherProvider.MapWeatherCode(48));
    }

    private static void OpenMeteoTimeoutBecomesUnavailable()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(
            async (_, cancellationToken) =>
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            });
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var health = new RecordingHealthSink();
        using var provider = CreateWeatherProvider(
            client,
            requestTimeout: TimeSpan.FromMilliseconds(50),
            healthSink: health);
        var stopwatch = Stopwatch.StartNew();
        provider.RefreshIfDueAsync(Origin, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        stopwatch.Stop();

        BehaviorTestCheck.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Weather timeout took {stopwatch.Elapsed}.");
        BehaviorTestCheck.Equal(
            WeatherDataQuality.Unavailable,
            provider.GetCurrent(Origin).Quality);
        BehaviorTestCheck.Equal(
            ContextHealthErrorCategory.Timeout,
            health.Events.Single().ErrorCategory);
    }

    private static void OpenMeteoClassifiesAnonymousFailureCategories()
    {
        VerifyWeatherFailureCategory(
            handler => handler.EnqueueResponse(HttpStatusCode.BadGateway),
            ContextHealthErrorCategory.HttpStatus);
        VerifyWeatherFailureCategory(
            handler => handler.EnqueueJson("{"),
            ContextHealthErrorCategory.InvalidJson);
        VerifyWeatherFailureCategory(
            handler => handler.EnqueueJson("""{"current":{}}"""),
            ContextHealthErrorCategory.InvalidData);
        VerifyWeatherFailureCategory(
            handler => handler.Enqueue(
                (_, _) => throw new HttpRequestException("offline")),
            ContextHealthErrorCategory.Unexpected);

        var cancelledHandler = new QueueHttpMessageHandler();
        cancelledHandler.Enqueue(
            (_, cancellationToken) =>
                Task.FromCanceled<HttpResponseMessage>(
                    cancellationToken));
        using var cancelledClient = new HttpClient(cancelledHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var cancelledHealth = new RecordingHealthSink();
        using var cancelledProvider = CreateWeatherProvider(
            cancelledClient,
            healthSink: cancelledHealth);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        cancelledProvider.RefreshIfDueAsync(
                Origin,
                cancellation.Token)
            .GetAwaiter()
            .GetResult();
        BehaviorTestCheck.Equal(
            ContextHealthErrorCategory.Cancel,
            cancelledHealth.Events.Single().ErrorCategory);
        BehaviorTestCheck.Equal(
            0,
            cancelledHealth.Events.Single().ConsecutiveFailures);
    }

    private static void HealthLogUsesWhitelistAndBoundedRotation()
    {
        using (var production = ContextHealthLogWriter.CreateDefault())
        {
            // The production log directory is derived from AppIdentity so
            // forks relocate it by changing one constant.
            string expectedDirectory = Path.Combine(
                global::GuluPet.AppIdentity.LocalDataDirectory,
                "Logs");
            BehaviorTestCheck.Equal(
                Path.GetFullPath(expectedDirectory),
                Path.GetDirectoryName(production.ActivePath)!);
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            "gulu-context-health-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new MutableTimeProvider(Origin);
            var options = new ContextHealthLogOptions
            {
                DirectoryPath = directory,
                MaximumFileBytes = 320,
                RetainedFileCount = 2,
                TimeProvider = clock,
            };
            using (var writer = new ContextHealthLogWriter(options))
            {
                for (int index = 0; index < 12; index++)
                {
                    clock.UtcNow = Origin + TimeSpan.FromSeconds(index);
                    writer.Write(
                        new ContextHealthEvent(
                            clock.UtcNow,
                            ContextHealthSource.Weather,
                            ContextHealthOutcome.Failure,
                            ContextHealthErrorCategory.HttpStatus,
                            index + 1,
                            index));
                }

                BehaviorTestCheck.True(
                    writer.ActivePath.StartsWith(
                        Path.GetFullPath(directory),
                        StringComparison.OrdinalIgnoreCase));
            }

            string[] files = Directory.GetFiles(
                directory,
                "context-health.jsonl*");
            BehaviorTestCheck.Equal(2, files.Length);
            string[] allowed =
            [
                "timestamp",
                "source",
                "outcome",
                "errorCategory",
                "consecutiveFailures",
                "lastSuccessAgeSeconds",
            ];
            foreach (string line in files.SelectMany(File.ReadAllLines))
            {
                using JsonDocument document = JsonDocument.Parse(line);
                string[] names = document.RootElement
                    .EnumerateObject()
                    .Select(static property => property.Name)
                    .ToArray();
                BehaviorTestCheck.Equal(
                    allowed.Length,
                    names.Length);
                BehaviorTestCheck.True(
                    names.Order()
                        .SequenceEqual(allowed.Order()));
                BehaviorTestCheck.False(
                    line.Contains("process", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("window", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("url", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("payload", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("message", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void HealthLogPrunesLegacyMixedRecordsByTimestamp()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "gulu-context-health-retention-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var now = new DateTimeOffset(
                2026,
                8,
                16,
                12,
                0,
                0,
                TimeSpan.Zero);
            DateTimeOffset cutoff = now - TimeSpan.FromHours(72);
            string active = Path.Combine(directory, "context-health.jsonl");
            string archive = active + ".1";
            string staleArchive = active + ".2";
            string unrelated = Path.Combine(
                directory,
                "context-health.user.jsonl");
            File.WriteAllLines(
                active,
                [
                    ContextRetentionEntry(cutoff - TimeSpan.FromTicks(1)),
                    ContextRetentionEntry(cutoff),
                    "{malformed-json",
                    ContextRetentionEntry(now + TimeSpan.FromTicks(1)),
                    ContextRetentionEntry(now + TimeSpan.FromDays(1)),
                ]);
            File.WriteAllLines(
                archive,
                [
                    ContextRetentionEntry(cutoff - TimeSpan.FromDays(2)),
                    ContextRetentionEntry(now - TimeSpan.FromHours(1)),
                ]);
            File.WriteAllText(
                staleArchive,
                ContextRetentionEntry(cutoff - TimeSpan.FromDays(5)) + "\n");
            File.WriteAllText(unrelated, "user-owned-sentinel");
            DateTime restoredMtime = now.UtcDateTime.AddDays(-20);
            File.SetLastWriteTimeUtc(active, restoredMtime);
            var clock = new MutableTimeProvider(now);
            using var writer = new ContextHealthLogWriter(
                new ContextHealthLogOptions
                {
                    DirectoryPath = directory,
                    TimeProvider = clock,
                });

            writer.Write(CreateHealthEvent(now));
            writer.Write(CreateHealthEvent(cutoff - TimeSpan.FromSeconds(1)));

            BehaviorTestCheck.SequenceEqual(
                [cutoff, now],
                File.ReadLines(active).Select(ReadContextTimestampUtc));
            BehaviorTestCheck.SequenceEqual(
                [now - TimeSpan.FromHours(1)],
                File.ReadLines(archive).Select(ReadContextTimestampUtc));
            BehaviorTestCheck.False(File.Exists(staleArchive));
            BehaviorTestCheck.Equal(
                "user-owned-sentinel",
                File.ReadAllText(unrelated));
            BehaviorTestCheck.True(
                File.GetLastWriteTimeUtc(active) > restoredMtime,
                "The retained active file should receive the current event.");
            BehaviorTestCheck.Equal(
                0,
                Directory.GetFiles(directory, "*.retention-*.tmp").Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void HealthLogUtcDayAndSizeRotationRemainAgeBounded()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "gulu-context-health-day-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new MutableTimeProvider(
                new DateTimeOffset(
                    2026,
                    8,
                    16,
                    23,
                    59,
                    0,
                    TimeSpan.Zero));
            using (var writer = new ContextHealthLogWriter(
                       new ContextHealthLogOptions
                       {
                           DirectoryPath = directory,
                           MaximumFileBytes = 256,
                           RetainedFileCount = 3,
                           TimeProvider = clock,
                       }))
            {
                writer.Write(CreateHealthEvent(clock.UtcNow));
                clock.UtcNow = clock.UtcNow.AddMinutes(2);
                writer.Write(CreateHealthEvent(clock.UtcNow));
                clock.UtcNow = clock.UtcNow.AddSeconds(1);
                writer.Write(CreateHealthEvent(clock.UtcNow));

                string active = Path.Combine(
                    directory,
                    "context-health.jsonl");
                BehaviorTestCheck.True(File.Exists(active));
                BehaviorTestCheck.True(File.Exists(active + ".1"));
                BehaviorTestCheck.True(File.Exists(active + ".2"));

                clock.UtcNow = clock.UtcNow.AddDays(4);
                writer.Write(CreateHealthEvent(clock.UtcNow));
            }

            DateTimeOffset cutoff = clock.UtcNow - TimeSpan.FromHours(72);
            string[] controlledFiles =
            [
                Path.Combine(directory, "context-health.jsonl"),
                Path.Combine(directory, "context-health.jsonl.1"),
                Path.Combine(directory, "context-health.jsonl.2"),
            ];
            BehaviorTestCheck.True(
                controlledFiles
                    .Where(File.Exists)
                    .SelectMany(File.ReadLines)
                    .Select(ReadContextTimestampUtc)
                    .All(timestamp => timestamp >= cutoff));
            BehaviorTestCheck.True(
                controlledFiles.Count(File.Exists) <= 3);

            using var restarted = new ContextHealthLogWriter(
                new ContextHealthLogOptions
                {
                    DirectoryPath = directory,
                    MaximumFileBytes = 256,
                    RetainedFileCount = 3,
                    TimeProvider = clock,
                });
            clock.UtcNow = clock.UtcNow.AddSeconds(1);
            restarted.Write(CreateHealthEvent(clock.UtcNow));
            BehaviorTestCheck.True(
                controlledFiles
                    .Where(File.Exists)
                    .SelectMany(File.ReadLines)
                    .Select(ReadContextTimestampUtc)
                    .All(timestamp => timestamp >= cutoff));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void HealthLogRefusesReparseDirectory()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "gulu-context-health-reparse-tests",
            Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "target");
        string link = Path.Combine(root, "logs-link");
        Directory.CreateDirectory(target);
        string sentinel = Path.Combine(target, "sentinel.txt");
        File.WriteAllText(sentinel, "do-not-touch");
        CreateDirectoryReparsePoint(link, target);
        try
        {
            var now = new DateTimeOffset(
                2026,
                8,
                16,
                12,
                0,
                0,
                TimeSpan.Zero);
            using var writer = new ContextHealthLogWriter(
                new ContextHealthLogOptions
                {
                    DirectoryPath = link,
                    TimeProvider = new MutableTimeProvider(now),
                });
            _ = BehaviorTestCheck.Throws<IOException>(
                () => writer.Write(CreateHealthEvent(now)));
            BehaviorTestCheck.Equal("do-not-touch", File.ReadAllText(sentinel));
            BehaviorTestCheck.False(File.Exists(Path.Combine(
                target,
                "context-health.jsonl")));
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void HealthLogHourlyMaintenanceExpiresBeforeSeventyThreeHours()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "gulu-context-health-hourly-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new MutableTimeProvider(
                new DateTimeOffset(
                    2026,
                    8,
                    13,
                    0,
                    1,
                    0,
                    TimeSpan.Zero));
            DateTimeOffset original = clock.UtcNow;
            using var writer = new ContextHealthLogWriter(
                new ContextHealthLogOptions
                {
                    DirectoryPath = directory,
                    MaximumFileBytes = 64 * 1024,
                    RetainedFileCount = 5,
                    TimeProvider = clock,
                });
            writer.Write(CreateHealthEvent(clock.UtcNow));
            foreach (DateTimeOffset midnight in new[]
            {
                new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
            })
            {
                clock.UtcNow = midnight;
                writer.Write(CreateHealthEvent(clock.UtcNow));
            }

            clock.UtcNow = new DateTimeOffset(
                2026,
                8,
                16,
                0,
                30,
                0,
                TimeSpan.Zero);
            writer.Write(CreateHealthEvent(clock.UtcNow));
            BehaviorTestCheck.True(
                ReadAllContextTimestamps(directory, 5).Contains(original));

            clock.UtcNow = new DateTimeOffset(
                2026,
                8,
                16,
                1,
                0,
                0,
                TimeSpan.Zero);
            writer.Write(CreateHealthEvent(clock.UtcNow));
            DateTimeOffset[] retained = ReadAllContextTimestamps(directory, 5);
            BehaviorTestCheck.False(retained.Contains(original));
            BehaviorTestCheck.True(retained.All(timestamp =>
                timestamp >= clock.UtcNow - TimeSpan.FromHours(72)));
            BehaviorTestCheck.True(
                clock.UtcNow - original < TimeSpan.FromHours(73));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void HealthLogRepairsMissingNewlineAndDropsFutureEvents()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "gulu-context-health-newline-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var now = new DateTimeOffset(
                2026,
                8,
                16,
                12,
                0,
                0,
                TimeSpan.Zero);
            string active = Path.Combine(directory, "context-health.jsonl");
            File.WriteAllText(active, ContextRetentionEntry(now));
            using var writer = new ContextHealthLogWriter(
                new ContextHealthLogOptions
                {
                    DirectoryPath = directory,
                    TimeProvider = new MutableTimeProvider(now),
                });

            writer.Write(CreateHealthEvent(now + TimeSpan.FromTicks(1)));
            writer.Write(CreateHealthEvent(now + TimeSpan.FromDays(1)));
            writer.Write(CreateHealthEvent(now));

            string[] lines = File.ReadAllLines(active);
            BehaviorTestCheck.Equal(2, lines.Length);
            BehaviorTestCheck.True(lines
                .Select(ReadContextTimestampUtc)
                .All(timestamp => timestamp == now));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void HealthLogClockRollbackPrunesFutureRecords()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "gulu-context-health-rollback-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new MutableTimeProvider(
                new DateTimeOffset(
                    2026,
                    8,
                    16,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));
            DateTimeOffset beforeRollback = clock.UtcNow;
            using var writer = new ContextHealthLogWriter(
                new ContextHealthLogOptions
                {
                    DirectoryPath = directory,
                    TimeProvider = clock,
                });
            writer.Write(CreateHealthEvent(clock.UtcNow));

            clock.UtcNow = beforeRollback - TimeSpan.FromHours(1);
            writer.Write(CreateHealthEvent(clock.UtcNow));

            BehaviorTestCheck.SequenceEqual(
                [clock.UtcNow],
                ReadAllContextTimestamps(directory, 3));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void HealthLogDeletesOnlyExclusiveExactOrphanTemps()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "gulu-context-health-orphan-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string reparseTemp = Path.Combine(
            directory,
            $".context-health.jsonl.retention-{Guid.NewGuid():N}.tmp");
        try
        {
            string guid = Guid.NewGuid().ToString("N");
            string exact = Path.Combine(
                directory,
                $".context-health.jsonl.retention-{guid}.tmp");
            string similar = Path.Combine(
                directory,
                $".context-health.jsonl.retention-not-{guid}.tmp");
            string otherLog = Path.Combine(
                directory,
                $".{BehaviorTickLogWriter.LogFileName}.retention-{guid}.tmp");
            string child = Path.Combine(directory, "child");
            Directory.CreateDirectory(child);
            string nested = Path.Combine(
                child,
                $".context-health.jsonl.retention-{guid}.tmp");
            File.WriteAllText(exact, "active-sentinel");
            File.WriteAllText(similar, "similar-sentinel");
            File.WriteAllText(otherLog, "other-log-sentinel");
            File.WriteAllText(nested, "nested-sentinel");
            string reparseTarget = Path.Combine(directory, "reparse-target");
            Directory.CreateDirectory(reparseTarget);
            string reparseSentinel = Path.Combine(
                reparseTarget,
                "reparse-sentinel.txt");
            File.WriteAllText(reparseSentinel, "reparse-sentinel");
            CreateDirectoryReparsePoint(reparseTemp, reparseTarget);
            var now = new DateTimeOffset(
                2026,
                8,
                16,
                12,
                0,
                0,
                TimeSpan.Zero);
            var options = new ContextHealthLogOptions
            {
                DirectoryPath = directory,
                TimeProvider = new MutableTimeProvider(now),
            };

            using (var active = new FileStream(
                       exact,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None))
            using (var writer = new ContextHealthLogWriter(options))
            {
                writer.Write(CreateHealthEvent(now));
                BehaviorTestCheck.True(File.Exists(exact));
            }

            using (var writer = new ContextHealthLogWriter(options))
            {
                writer.Write(CreateHealthEvent(now));
            }

            BehaviorTestCheck.False(File.Exists(exact));
            BehaviorTestCheck.Equal(
                "similar-sentinel",
                File.ReadAllText(similar));
            BehaviorTestCheck.Equal(
                "other-log-sentinel",
                File.ReadAllText(otherLog));
            BehaviorTestCheck.Equal(
                "nested-sentinel",
                File.ReadAllText(nested));
            BehaviorTestCheck.True(Directory.Exists(reparseTemp));
            BehaviorTestCheck.Equal(
                "reparse-sentinel",
                File.ReadAllText(reparseSentinel));
        }
        finally
        {
            if (Directory.Exists(reparseTemp))
            {
                Directory.Delete(reparseTemp);
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    private static ContextHealthEvent CreateHealthEvent(
        DateTimeOffset timestamp) =>
        new(
            timestamp,
            ContextHealthSource.Weather,
            ContextHealthOutcome.Failure,
            ContextHealthErrorCategory.HttpStatus,
            1,
            5);

    private static string ContextRetentionEntry(DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new
        {
            timestamp,
            source = "weather",
            outcome = "failure",
            errorCategory = "http-status",
            consecutiveFailures = 1,
            lastSuccessAgeSeconds = 5,
        });

    private static DateTimeOffset ReadContextTimestampUtc(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement
            .GetProperty("timestamp")
            .GetDateTimeOffset()
            .ToUniversalTime();
    }

    private static DateTimeOffset[] ReadAllContextTimestamps(
        string directory,
        int retainedFileCount)
    {
        string active = Path.Combine(directory, "context-health.jsonl");
        string[] paths =
        [
            active,
            .. Enumerable.Range(1, retainedFileCount - 1)
                .Select(index => active + "." + index),
        ];
        return paths
            .Where(File.Exists)
            .SelectMany(File.ReadLines)
            .Select(ReadContextTimestampUtc)
            .ToArray();
    }

    private static void CreateDirectoryReparsePoint(
        string linkPath,
        string targetPath)
    {
        try
        {
            _ = Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException
                  or IOException
                  or PlatformNotSupportedException)
        {
            string commandInterpreter = Environment.GetEnvironmentVariable(
                "ComSpec")
                ?? Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.System),
                    "cmd.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = commandInterpreter,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in new[]
            {
                "/d",
                "/c",
                "mklink",
                "/J",
                linkPath,
                targetPath,
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Could not start the junction test helper.");
            process.WaitForExit();
            if (process.ExitCode != 0 || !Directory.Exists(linkPath))
            {
                throw new InvalidOperationException(
                    "Could not create a directory reparse point for the " +
                    "context-health retention test.");
            }
        }
    }

    private static void OpenMeteoBacksOffAndRecovers()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.ServiceUnavailable);
        handler.EnqueueResponse(HttpStatusCode.ServiceUnavailable);
        handler.EnqueueJson(
            """
            {
              "current": {
                "temperature_2m": 27.5,
                "weather_code": 1
              }
            }
            """);
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var health = new RecordingHealthSink();
        using var provider = new OpenMeteoWeatherProvider(
            client,
            new OpenMeteoWeatherOptions
            {
                RequestUri =
                    OpenMeteoWeatherProvider.DefaultGuangzhouRequestUri,
                FailureRetryInterval = TimeSpan.FromSeconds(1),
                FailureRetryMaximumInterval = TimeSpan.FromSeconds(4),
                RequestTimeout = TimeSpan.FromSeconds(1),
            },
            healthSink: health);

        Refresh(provider, Origin);
        Refresh(provider, Origin + TimeSpan.FromMilliseconds(900));
        BehaviorTestCheck.Equal(1, handler.RequestCount);

        Refresh(provider, Origin + TimeSpan.FromSeconds(1));
        Refresh(provider, Origin + TimeSpan.FromSeconds(2));
        BehaviorTestCheck.Equal(2, handler.RequestCount);

        Refresh(provider, Origin + TimeSpan.FromSeconds(3));
        BehaviorTestCheck.Equal(3, handler.RequestCount);
        WeatherReading recovered = provider.GetCurrent(
            Origin + TimeSpan.FromSeconds(3));
        BehaviorTestCheck.Equal(WeatherDataQuality.Fresh, recovered.Quality);
        BehaviorTestCheck.Equal("cloudy", recovered.Kind);
        BehaviorTestCheck.Equal(
            ContextHealthOutcome.Recovered,
            health.Events.Last().Outcome);
        BehaviorTestCheck.Equal(0, health.Events.Last().ConsecutiveFailures);
    }

    private static void Refresh(
        OpenMeteoWeatherProvider provider,
        DateTimeOffset now) =>
        provider.RefreshIfDueAsync(now, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static void VerifyWeatherFailureCategory(
        Action<QueueHttpMessageHandler> arrange,
        ContextHealthErrorCategory expected)
    {
        var handler = new QueueHttpMessageHandler();
        arrange(handler);
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var health = new RecordingHealthSink();
        using var provider = CreateWeatherProvider(
            client,
            healthSink: health);
        provider.RefreshIfDueAsync(Origin, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ContextHealthEvent healthEvent = health.Events.Single();
        BehaviorTestCheck.Equal(expected, healthEvent.ErrorCategory);
        BehaviorTestCheck.Equal(
            ContextHealthOutcome.Failure,
            healthEvent.Outcome);
        BehaviorTestCheck.Equal(1, healthEvent.ConsecutiveFailures);
    }

    private static OpenMeteoWeatherProvider CreateWeatherProvider(
        HttpClient client,
        TimeSpan? requestTimeout = null,
        IContextHealthSink? healthSink = null) =>
        new(
            client,
            new OpenMeteoWeatherOptions
            {
                RequestUri = new Uri(
                    "https://api.open-meteo.com/v1/forecast" +
                    "?latitude=23.1291&longitude=113.2644" +
                    "&current=temperature_2m%2Cweather_code" +
                    "&timezone=Asia%2FShanghai&forecast_days=1"),
                RequestTimeout =
                    requestTimeout ?? TimeSpan.FromSeconds(1),
            },
            healthSink: healthSink);

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(RuntimeContextMonitorTests)}.{name}");
    }

    private sealed class FakeInputSource(InputActivityReading reading)
        : IInputActivitySource
    {
        public InputActivityReading Reading { get; set; } = reading;

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public bool Disposed { get; private set; }

        public void Start()
        {
            StartCalls++;
        }

        public void Stop()
        {
            StopCalls++;
        }

        public InputActivityReading Read() => Reading;

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class FakeSessionLockSource(SessionLockReading reading)
        : ISessionLockSource
    {
        public SessionLockReading Reading { get; set; } = reading;

        public SessionLockReading Read() => Reading;
    }

    private sealed class FakeWindowsSessionLockNativeApi(
        bool available,
        bool isLocked) : IWindowsSessionLockNativeApi
    {
        public bool Available { get; set; } = available;

        public bool IsLocked { get; set; } = isLocked;

        public bool TryRead(out bool isLocked)
        {
            isLocked = IsLocked;
            return Available;
        }
    }

    private sealed class FakeGlobalInputHookNativeApi
        : IGlobalInputHookNativeApi
    {
        private readonly ManualResetEventSlim _quit = new(false);
        private int _nextHandle;

        public bool FailMouseInstall { get; set; }

        public int UnhookCalls { get; private set; }

        public int PostQuitCalls { get; private set; }

        public void EnsureMessageQueue()
        {
            _quit.Reset();
        }

        public int GetCurrentThreadId() => 17;

        public IntPtr InstallKeyboardHook(
            NativeMethods.HookProcedure procedure) =>
            new(Interlocked.Increment(ref _nextHandle));

        public IntPtr InstallMouseHook(
            NativeMethods.HookProcedure procedure) =>
            FailMouseInstall
                ? IntPtr.Zero
                : new(Interlocked.Increment(ref _nextHandle));

        public bool Unhook(IntPtr hookHandle)
        {
            UnhookCalls++;
            return true;
        }

        public bool PostQuit(int threadId)
        {
            PostQuitCalls++;
            _quit.Set();
            return true;
        }

        public int GetMessage()
        {
            _quit.Wait();
            return 0;
        }

        public IntPtr CallNext(
            int code,
            IntPtr message,
            IntPtr eventData) => IntPtr.Zero;
    }

    private sealed class FakeForegroundSource(
        ForegroundApplicationReading reading)
        : IForegroundApplicationSource
    {
        public ForegroundApplicationReading Read() => reading;
    }

    private sealed class RecoveringForegroundSource
        : IForegroundApplicationSource
    {
        public int ReadCalls { get; private set; }

        public ForegroundApplicationReading Read()
        {
            ReadCalls++;
            if (ReadCalls == 1)
            {
                throw new InvalidOperationException(
                    "Synthetic collector failure.");
            }

            return new ForegroundApplicationReading(
                "browser",
                true,
                false);
        }
    }

    private sealed class FakeIdleSource(TimeSpan idleFor) : IIdleTimeSource
    {
        public bool TryGetIdleTime(out TimeSpan value)
        {
            value = idleFor;
            return true;
        }
    }

    private sealed class FakeWeatherProvider(WeatherReading reading)
        : IWeatherProvider
    {
        public WeatherReading GetCurrent(DateTimeOffset now) => reading;

        public Task RefreshIfDueAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class BlockingWeatherProvider : IWeatherProvider
    {
        private readonly TaskCompletionSource _never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _refreshStarted;

        public bool RefreshStarted =>
            Volatile.Read(ref _refreshStarted) != 0;

        public WeatherReading GetCurrent(DateTimeOffset now) =>
            WeatherReading.Unavailable;

        public async Task RefreshIfDueAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Volatile.Write(ref _refreshStarted, 1);
            await _never.Task.WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
            _never.TrySetResult();
        }
    }

    private sealed class RecordingHealthSink : IContextHealthSink
    {
        public List<ContextHealthEvent> Events { get; } = [];

        public void Write(ContextHealthEvent healthEvent) =>
            Events.Add(healthEvent);

        public void Dispose()
        {
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<
            Func<
                HttpRequestMessage,
                CancellationToken,
                Task<HttpResponseMessage>>> _responses = [];

        public int RequestCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public void EnqueueJson(string json) =>
            Enqueue(
                (_, _) =>
                    Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                json,
                                Encoding.UTF8,
                                "application/json"),
                        }));

        public void EnqueueResponse(HttpStatusCode statusCode) =>
            Enqueue(
                (_, _) =>
                    Task.FromResult(
                        new HttpResponseMessage(statusCode)));

        public void Enqueue(
            Func<
                HttpRequestMessage,
                CancellationToken,
                Task<HttpResponseMessage>> response) =>
            _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            if (!_responses.TryDequeue(out var response))
            {
                throw new InvalidOperationException(
                    "No queued HTTP response.");
            }

            return response(request, cancellationToken);
        }
    }
}
