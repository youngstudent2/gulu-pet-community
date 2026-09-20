using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GuluPet.Accessories;
using GuluPet.Behavior;
using GuluPet.Care;
using GuluPet.Diagnostics;
using GuluPet.Diary;
using GuluPet.Memories;
using GuluPet.Persistence;
using GuluPet.Platform;
using GuluPet.Postcards;
using GuluPet.Presentation;
using GuluPet.Runtime;
using GuluPet.Sensing;
using GuluPet.Testing;
using GuluPet.Update;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using WindowState = System.Windows.WindowState;

namespace GuluPet;

public partial class App : Application
{
    static App()
    {
        // WPF owns the generated entry point, so configure the mixed
        // WPF/WinForms process before the first Application instance exists.
        if (WindowsCompatibility.IsCurrentOperatingSystemSupported)
        {
            _ = System.Windows.Forms.Application.SetHighDpiMode(
                System.Windows.Forms.HighDpiMode.PerMonitorV2);
        }
    }

    private SingleInstanceService? _singleInstance;
    private UserSettingsStore? _settingsStore;
    private UserSettings? _settings;
    private StartupRegistrationService? _startupRegistration;
    private FullscreenMonitor? _fullscreenMonitor;
    private TaskbarIconService? _taskbarIcon;
    private ManualUpdateService? _updateService;
    private CancellationTokenSource? _updateOperationCancellation;
    private TaskCompletionSource<bool>? _updateOperationCompleted;
    private UpdateProgressWindow? _updateProgressWindow;
    private PetWindow? _petWindow;
    private PetController? _petController;
    private BehaviorTickLogWriter? _tickScoreLogWriter;
    private PetCareFeatureController? _careFeature;
    private EyeAccessoryFeatureController? _eyeAccessoryFeature;
    private PostcardFeatureController? _postcardFeature;
    private MemoryFeatureController? _memoryFeature;
    private MemoryGalleryWindow? _memoryGalleryWindow;
    private DiaryFeatureController? _diaryFeature;
    private DiaryWindow? _diaryWindow;
    private DiaryReadTrackingState _diaryReadTracking =
        new(false, []);
    private SettingsWindow? _settingsWindow;
    private LocalErrorLog? _errorLog;
    private ErrorLogReportUploader? _errorLogUploader;
    private BehaviorBrowserWindow? _behaviorBrowserWindow;
    private IReadOnlyList<BehaviorDefinition> _behaviorDefinitions = [];
    private TestControlServer? _testControlServer;
    private bool _userWantsPetVisible = true;
    private bool _foregroundIsFullscreen;
    private bool _isExiting;
    private bool _updateCheckInProgress;
    private bool _exitAfterUpdateCancellation;
    private bool _isolatedTestInstance;
    private readonly StartupPresentationGate _startupPresentation = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!WindowsCompatibility.IsCurrentOperatingSystemSupported)
        {
            MessageBox.Show(
                "咕噜需要 Windows 10 1809（内部版本 17763）或更新的 x64 系统。" +
                "请先更新 Windows 后再运行。",
                "无法启动咕噜",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(3);
            return;
        }

        bool isolatedLogScope = HasCommandLineSwitch(
            e.Args,
            "--isolated-test-instance");
        _errorLog = isolatedLogScope
            ? new LocalErrorLog(Path.Combine(
                Path.GetTempPath(),
                $"GuluPet.Isolated.{Environment.ProcessId}",
                "Logs"))
            : new LocalErrorLog();
        _errorLogUploader = ErrorLogReportUploader.CreateDefault(_errorLog);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException +=
            OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException +=
            OnUnobservedTaskException;

        PostUpdateReadySignal? postUpdateReadySignal;
        bool showUpdateRollbackNotice = HasCommandLineSwitch(
            e.Args,
            "--post-update-rollback-notice");
        try
        {
            postUpdateReadySignal = PostUpdateReadySignal.Parse(e.Args);
        }
        catch (FormatException exception)
        {
            _errorLog?.Write(
                "update",
                "parse-ready-signal",
                "post-update-ready-argument-invalid",
                exception);
            Shutdown(2);
            return;
        }

        if (HasCommandLineSwitch(e.Args, "--validate-content"))
        {
            try
            {
                await RuntimeContentContract.LoadAndValidateAsync(
                    Path.Combine(AppContext.BaseDirectory, "Assets"),
                    validateEveryFrame: true);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"GuluPet content-only validation failed: {exception}");
                Shutdown(1);
            }

            return;
        }

        bool commandLineTestControl = HasCommandLineSwitch(e.Args, "--test-control");
        bool testControlEnabled = commandLineTestControl
            || string.Equals(
                Environment.GetEnvironmentVariable("GULUPET_TEST_CONTROL"),
                "1",
                StringComparison.Ordinal);
        string testControlPipeName;
        try
        {
            testControlPipeName = TestControlStartupOptions.ResolvePipeName(
                e.Args,
                commandLineTestControl);
        }
        catch (ArgumentException exception)
        {
            Debug.WriteLine(
                $"GuluPet test-control startup arguments are invalid: {exception}");
            Shutdown(2);
            return;
        }

        bool startHidden = commandLineTestControl
            && HasCommandLineSwitch(e.Args, "--start-hidden");
        _isolatedTestInstance = commandLineTestControl
            && HasCommandLineSwitch(e.Args, "--isolated-test-instance");
        _userWantsPetVisible = !startHidden;

        string? isolatedInstanceKey =
            _isolatedTestInstance &&
            !string.Equals(
                testControlPipeName,
                TestControlProtocol.PipeName,
                StringComparison.Ordinal)
                ? testControlPipeName
                : null;
        _singleInstance = new SingleInstanceService(
            _isolatedTestInstance,
            isolatedInstanceKey);
        if (!_singleInstance.TryAcquire(
                ActivateRunningInstance,
                notifyExistingInstance: !testControlEnabled))
        {
            Shutdown(0);
            return;
        }

        if (!_isolatedTestInstance)
        {
            try
            {
                _ = await Task.Run(() =>
                {
                    string updatesRoot = Path.Combine(
                        AppIdentity.LocalDataDirectory,
                        "Updates");
                    _ = ManualUpdateService.CleanupExpiredUpdateInstallLogs(
                        updatesRoot);
                    return ManualUpdateService.CleanupStaleOperationWorkspaces(
                        AppContext.BaseDirectory);
                });
            }
            catch (Exception exception)
                when (exception is IOException
                      or UnauthorizedAccessException
                      or InvalidDataException
                      or InvalidOperationException)
            {
                _errorLog?.Write(
                    "update",
                    "cleanup-stale-operation-workspace",
                    "stale-operation-workspace-cleanup-failed",
                    exception);
            }
        }

        ValidatedRuntimeContent runtimeContent;
        try
        {
            runtimeContent = await RuntimeContentContract.LoadAndValidateAsync(
                Path.Combine(AppContext.BaseDirectory, "Assets"));
        }
        catch (Exception exception)
        {
            HandleRuntimeInitializationFailure(exception);
            return;
        }

        _behaviorDefinitions = runtimeContent.Behaviors.Definitions
            .OrderBy(static definition => definition.Id.Value, StringComparer.Ordinal)
            .ToArray();

        _startupRegistration = new StartupRegistrationService();
        if (_isolatedTestInstance)
        {
            // An automation-only instance must not race with or mutate the
            // user's live settings while the normal desktop pet is running.
            _settings = UserSettings.CreateDefault();
        }
        else
        {
            _settingsStore = new UserSettingsStore();
            _settings = _settingsStore.Load();

            // Reflect the registry's real state, including out-of-app changes.
            // First run remains disabled because no Run value exists.
            _settings.StartWithWindows = _startupRegistration.IsEnabled;
        }

        _tickScoreLogWriter = new BehaviorTickLogWriter();
        // SetEnabled is memory-only, so the main window does not wait for the
        // potentially large legacy scan. Writes are dropped while preparation
        // is in progress.
        _tickScoreLogWriter.SetEnabled(
            _settings.TickScoreLoggingEnabled);
        if (!_isolatedTestInstance)
        {
            // Fire-and-observe: this worker runs even when logging is disabled,
            // but never delays initial pet presentation.
            _ = PrepareTickScoreRetentionAsync(_tickScoreLogWriter);
        }

        _petWindow = new PetWindow();
        _petWindow.SetInitialPosition(_settings.WindowLeft, _settings.WindowTop);
        _petWindow.SetControlState(
            _settings.AlwaysOnTop,
            _settings.StartWithWindows,
            _userWantsPetVisible,
            _settings.TickScoreLoggingEnabled);
        _petWindow.Topmost = _settings.AlwaysOnTop;
        // Create the native window without showing it, then fail closed before
        // runtime initialization if Windows did not honor PerMonitorV2.
        IntPtr petWindowHandle =
            new WindowInteropHelper(_petWindow).EnsureHandle();
        if (!string.Equals(
                NativeMethods.GetWindowDpiAwarenessName(petWindowHandle),
                "perMonitorV2",
                StringComparison.Ordinal))
        {
            HandleRuntimeInitializationFailure(
                new InvalidOperationException(
                    "咕噜需要 PerMonitorV2 DPI 感知；Windows 当前返回了" +
                    "不兼容的虚拟化桌面坐标系。"));
            return;
        }

        if (runtimeContent.Postcards is not null)
        {
            try
            {
                _postcardFeature = new PostcardFeatureController(
                    runtimeContent.Postcards,
                    _petWindow,
                    _isolatedTestInstance);
                _postcardFeature.StateChanged += OnPostcardStateChanged;
                UpdatePostcardUi();
            }
            catch (Exception exception)
            {
                HandleRuntimeInitializationFailure(exception);
                return;
            }
        }

        _petWindow.AlwaysOnTopToggleRequested += OnAlwaysOnTopToggleRequested;
        _petWindow.StartWithWindowsToggleRequested += OnStartWithWindowsToggleRequested;
        _petWindow.BehaviorsRequested += OnBehaviorsRequested;
        _petWindow.SettingsRequested += OnSettingsRequested;
        _petWindow.VisibilityToggleRequested += OnVisibilityToggleRequested;
        _petWindow.ResetPositionRequested += OnResetPositionRequested;
        _petWindow.ExitRequested += OnExitRequested;
        _petWindow.DragCompleted += OnPetDragCompleted;
        _petWindow.Closing += OnPetWindowClosing;

        MainWindow = _petWindow;
        if (!InitializePetController(runtimeContent))
        {
            return;
        }
        UpdateMemoryUi();

        _taskbarIcon = new TaskbarIconService();
        _taskbarIcon.AlwaysOnTopToggleRequested += OnAlwaysOnTopToggleRequested;
        _taskbarIcon.StartWithWindowsToggleRequested += OnStartWithWindowsToggleRequested;
        _taskbarIcon.BehaviorsRequested += OnBehaviorsRequested;
        _taskbarIcon.SettingsRequested += OnSettingsRequested;
        _taskbarIcon.ExitRequested += OnExitRequested;
        _taskbarIcon.ActivateRequested += OnActivateRequested;
        _taskbarIcon.SetControlState(
            _settings.AlwaysOnTop,
            _settings.StartWithWindows,
            _userWantsPetVisible,
            _settings.TickScoreLoggingEnabled);
        UpdatePostcardUi();
        UpdateMemoryUi();
        UpdateDiaryUi();

        _updateService = new ManualUpdateService(manifestUri: null);

        bool activateAfterInitialization =
            _startupPresentation.MarkRuntimeReady();
        ApplyPresentationVisibility(activateAfterInitialization);

        if (!_isolatedTestInstance)
        {
            _taskbarIcon.Show();
        }

        if (testControlEnabled)
        {
            _testControlServer = new TestControlServer(
                testControlPipeName,
                HandleTestControlRequestAsync);
            _testControlServer.Start();
        }

        if (!_isolatedTestInstance)
        {
            SaveSettings();
        }

        if (postUpdateReadySignal is not null)
        {
            try
            {
                postUpdateReadySignal.SignalExisting();
            }
            catch (Exception exception)
                when (exception is WaitHandleCannotBeOpenedException
                      or UnauthorizedAccessException
                      or InvalidOperationException)
            {
                _errorLog?.Write(
                    "update",
                    "signal-ready",
                    "post-update-ready-signal-failed",
                    exception);
                Shutdown(3);
                return;
            }
        }

        if (showUpdateRollbackNotice)
        {
            MessageBox.Show(
                "这次更新没能正常启动，已经安全恢复到原来的版本。" +
                "咕噜和本地记录都没有丢失。",
                "咕噜已恢复原版本",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        if (_isolatedTestInstance
            && HasCommandLineSwitch(e.Args, "--diary-preview"))
        {
            _diaryFeature?.SeedPreviewForTest();
            OnDiaryRequested(this, EventArgs.Empty);
            string? previewPath = Environment.GetEnvironmentVariable(
                "GULUPET_DIARY_PREVIEW_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(previewPath)
                && _diaryWindow is not null)
            {
                _ = Dispatcher.BeginInvoke(
                    () =>
                    {
                        try
                        {
                            _diaryWindow?.SaveRenderedPreview(previewPath);
                        }
                        catch (Exception exception)
                        {
                            _errorLog?.Write(
                                "diary",
                                "render-preview",
                                "diary-preview-render-failed",
                                exception);
                        }
                    },
                    DispatcherPriority.ApplicationIdle);
            }
        }
    }

    private async Task PrepareTickScoreRetentionAsync(
        BehaviorTickLogWriter writer)
    {
        try
        {
            await writer.PrepareRetentionAsync();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            _errorLog?.Write(
                "diagnostics",
                "prepare-tick-score-retention",
                "tick-score-retention-maintenance-failed",
                exception);
            writer.SetEnabled(false);
            if (_settings is not null)
            {
                _settings.TickScoreLoggingEnabled = false;
                UpdateControlStates();
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateOperationCancellation?.Cancel();
        _updateProgressWindow?.CloseForCompletion();
        _updateProgressWindow = null;
        _testControlServer?.Dispose();
        if (_diaryWindow is not null)
        {
            _diaryWindow.DiaryDateDisplayed -= OnDiaryDateDisplayed;
            _diaryWindow.Closed -= OnDiaryWindowClosed;
            _diaryWindow.Close();
            _diaryWindow = null;
        }

        if (_memoryGalleryWindow is not null)
        {
            _memoryGalleryWindow.MemoryReplayRequested -=
                OnMemoryReplayRequested;
            _memoryGalleryWindow.Closed -= OnMemoryGalleryClosed;
            _memoryGalleryWindow.Close();
            _memoryGalleryWindow = null;
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            _settingsWindow.Close();
            _settingsWindow = null;
        }

        if (_behaviorBrowserWindow is not null)
        {
            _behaviorBrowserWindow.PreviewRequested -=
                OnBehaviorPreviewRequested;
            _behaviorBrowserWindow.Closed -= OnBehaviorBrowserClosed;
            _behaviorBrowserWindow.Close();
            _behaviorBrowserWindow = null;
        }

        if (_diaryFeature is not null)
        {
            _diaryFeature.StateChanged -= OnDiaryStateChanged;
            _diaryFeature.Dispose();
            _diaryFeature = null;
        }

        _careFeature?.Dispose();
        _careFeature = null;
        _eyeAccessoryFeature?.Dispose();
        _eyeAccessoryFeature = null;

        if (_postcardFeature is not null)
        {
            _postcardFeature.StateChanged -= OnPostcardStateChanged;
            _postcardFeature.Dispose();
            _postcardFeature = null;
        }

        if (_memoryFeature is not null)
        {
            _memoryFeature.StateChanged -= OnMemoryStateChanged;
            _memoryFeature.Dispose();
            _memoryFeature = null;
        }

        _petController?.Dispose();
        _fullscreenMonitor?.Dispose();
        _taskbarIcon?.Dispose();
        _updateService?.Dispose();
        _errorLogUploader?.Dispose();
        _errorLogUploader = null;
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -=
            OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -=
            OnUnobservedTaskException;
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnPostcardsRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => OnPostcardsRequested(sender, e));
            return;
        }

        if (_postcardFeature is null
            || !CanOpenAuxiliarySurface())
        {
            return;
        }

        CloseAuxiliarySurfacesExcept(AuxiliarySurface.Postcards);
        _postcardFeature.ShowGallery();
    }

    private void OnDiaryRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => OnDiaryRequested(sender, e));
            return;
        }

        if (_diaryFeature is null)
        {
            return;
        }

        if (!CanOpenAuxiliarySurface())
        {
            return;
        }

        CloseAuxiliarySurfacesExcept(AuxiliarySurface.Diary);

        ReconcileDiaryReadTracking();

        if (_diaryWindow is null)
        {
            _diaryWindow = new DiaryWindow();
            _diaryWindow.DiaryDateDisplayed += OnDiaryDateDisplayed;
            _diaryWindow.Closed += OnDiaryWindowClosed;
        }

        _diaryWindow.RefreshReadDiaryDates(
            _diaryReadTracking.ReadDates);
        _diaryWindow.Refresh(_diaryFeature.Current);
        ShowAndActivate(_diaryWindow);
    }

    private void OnDiaryStateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => OnDiaryStateChanged(sender, e),
                DispatcherPriority.Background);
            return;
        }

        if (_diaryFeature is null)
        {
            return;
        }

        ReconcileDiaryReadTracking();
        if (_diaryWindow is null)
        {
            return;
        }

        _diaryWindow.RefreshReadDiaryDates(
            _diaryReadTracking.ReadDates);
        _diaryWindow.Refresh(_diaryFeature.Current);
    }

    private void OnDiaryDateDisplayed(
        object? sender,
        DiaryDateDisplayedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        DiaryReadTrackingState next = DiaryReadTracker.MarkRead(
            _diaryReadTracking,
            e.Date);
        if (_diaryReadTracking.IsInitialized == next.IsInitialized
            && _diaryReadTracking.ReadDates.SequenceEqual(next.ReadDates))
        {
            return;
        }

        StoreDiaryReadTracking(next);
        _diaryWindow?.RefreshReadDiaryDates(next.ReadDates);
        UpdateDiaryUi();
    }

    private void OnDiaryWindowClosed(object? sender, EventArgs e)
    {
        if (_diaryWindow is null)
        {
            return;
        }

        _diaryWindow.DiaryDateDisplayed -= OnDiaryDateDisplayed;
        _diaryWindow.Closed -= OnDiaryWindowClosed;
        _diaryWindow = null;
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => OnSettingsRequested(sender, e));
            return;
        }

        if (_errorLogUploader is null
            || _errorLog is null)
        {
            return;
        }

        if (!CanOpenAuxiliarySurface())
        {
            return;
        }

        CloseAuxiliarySurfacesExcept(AuxiliarySurface.Feedback);

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(
                _errorLogUploader,
                _errorLog);
            _settingsWindow.Closed += OnSettingsWindowClosed;
        }

        ShowAndActivate(_settingsWindow);
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        if (_settingsWindow is null)
        {
            return;
        }

        _settingsWindow.Closed -= OnSettingsWindowClosed;
        _settingsWindow = null;
    }

    private static void ShowAndActivate(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private bool CanOpenAuxiliarySurface()
    {
        if (!_updateCheckInProgress)
        {
            return true;
        }

        if (_updateProgressWindow is not null)
        {
            ShowAndActivate(_updateProgressWindow);
        }

        return false;
    }

    private void CloseAuxiliarySurfacesExcept(AuxiliarySurface opening)
    {
        foreach (AuxiliarySurface surface in
                 AuxiliarySurfacePolicy.GetSurfacesToClose(opening))
        {
            CloseAuxiliarySurface(surface);
        }
    }

    private void CloseAllAuxiliarySurfaces()
    {
        foreach (AuxiliarySurface surface in
                 AuxiliarySurfacePolicy.GetAllSurfaces())
        {
            CloseAuxiliarySurface(surface);
        }
    }

    private void CloseAuxiliarySurface(AuxiliarySurface surface)
    {
        switch (surface)
        {
            case AuxiliarySurface.Postcards:
                _postcardFeature?.CloseGallery();
                break;
            case AuxiliarySurface.Diary:
                _diaryWindow?.Close();
                break;
            case AuxiliarySurface.Feedback:
                _settingsWindow?.Close();
                break;
            case AuxiliarySurface.Behaviors:
                _behaviorBrowserWindow?.Close();
                break;
            case AuxiliarySurface.Memories:
                _memoryGalleryWindow?.Close();
                break;
            case AuxiliarySurface.MemoryOffer:
                _memoryFeature?.CloseOffer();
                break;
            case AuxiliarySurface.MemoryPlayback:
                _memoryFeature?.ClosePlayback();
                break;
            default:
                throw new InvalidEnumArgumentException(
                    nameof(surface),
                    (int)surface,
                    typeof(AuxiliarySurface));
        }
    }

    private void OnBehaviorsRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => OnBehaviorsRequested(sender, e));
            return;
        }

        if (!CanOpenAuxiliarySurface())
        {
            return;
        }

        CloseAuxiliarySurfacesExcept(AuxiliarySurface.Behaviors);

        if (_behaviorBrowserWindow is null)
        {
            _behaviorBrowserWindow =
                new BehaviorBrowserWindow(_behaviorDefinitions);
            _behaviorBrowserWindow.PreviewRequested +=
                OnBehaviorPreviewRequested;
            _behaviorBrowserWindow.Closed += OnBehaviorBrowserClosed;
        }

        if (!_behaviorBrowserWindow.IsVisible)
        {
            _behaviorBrowserWindow.Show();
        }

        if (_behaviorBrowserWindow.WindowState == WindowState.Minimized)
        {
            _behaviorBrowserWindow.WindowState = WindowState.Normal;
        }

        _behaviorBrowserWindow.Activate();
    }

    private void OnBehaviorPreviewRequested(
        object? sender,
        BehaviorPreviewRequestedEventArgs e)
    {
        e.Result = _petController is null
            ? new BehaviorMutationResult
            {
                Accepted = false,
                BehaviorId = e.BehaviorId,
                RequestId = null,
                RejectionReason = "runtime-not-started",
            }
            : _petController.StartBehaviorManualPreview(e.BehaviorId);
    }

    private void OnBehaviorBrowserClosed(object? sender, EventArgs e)
    {
        if (_behaviorBrowserWindow is null)
        {
            return;
        }

        _behaviorBrowserWindow.PreviewRequested -=
            OnBehaviorPreviewRequested;
        _behaviorBrowserWindow.Closed -= OnBehaviorBrowserClosed;
        _behaviorBrowserWindow = null;
    }

    private void OnPostcardStateChanged(object? sender, EventArgs e) =>
        UpdatePostcardUi();

    private void OnMemoryStateChanged(object? sender, EventArgs e)
    {
        if (_memoryFeature?.IsOfferVisible == true)
        {
            CloseAuxiliarySurfacesExcept(AuxiliarySurface.MemoryOffer);
        }

        ApplyPresentationVisibility();
        _ = Dispatcher.BeginInvoke(
            UpdateMemoryUi,
            DispatcherPriority.Background);
    }

    private void UpdatePostcardUi()
    {
        if (_postcardFeature is null)
        {
            return;
        }

        PostcardFeatureSnapshot snapshot = _postcardFeature.GetSnapshot();
        _petWindow?.SetPostcardSummary(
            snapshot.UnlockedPostcardIds.Count,
            snapshot.UnacknowledgedPostcardIds.Count);
        _taskbarIcon?.SetPostcardSummary(
            snapshot.UnlockedPostcardIds.Count,
            snapshot.UnacknowledgedPostcardIds.Count);
    }

    private void OnMemoryReplayRequested(
        object? sender,
        MemoryReplayRequestedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => OnMemoryReplayRequested(sender, e));
            return;
        }

        if (_memoryFeature is null || !CanOpenAuxiliarySurface())
        {
            UpdateMemoryUi();
            return;
        }

        bool accepted = _memoryFeature.TryReplayCompletedMemory(e.MemoryId);
        if (accepted)
        {
            CloseAuxiliarySurfacesExcept(AuxiliarySurface.MemoryPlayback);
        }

        // Refresh synchronously as well as through StateChanged. If the
        // controller rejects a stale click, the gallery must release its
        // optimistic in-flight lock immediately.
        UpdateMemoryUi();
    }

    private void OnMemoriesRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => OnMemoriesRequested(sender, e));
            return;
        }

        if (_memoryFeature is null)
        {
            return;
        }

        if (!CanOpenAuxiliarySurface())
        {
            return;
        }

        CloseAuxiliarySurfacesExcept(AuxiliarySurface.Memories);

        if (_memoryGalleryWindow is null)
        {
            _memoryGalleryWindow = new MemoryGalleryWindow();
            _memoryGalleryWindow.MemoryReplayRequested +=
                OnMemoryReplayRequested;
            _memoryGalleryWindow.Closed += OnMemoryGalleryClosed;
        }

        _memoryGalleryWindow.Refresh(_memoryFeature.GetGalleryState());
        _memoryGalleryWindow.ShowGallery();
    }

    private void OnMemoryGalleryClosed(object? sender, EventArgs e)
    {
        if (_memoryGalleryWindow is null)
        {
            return;
        }

        _memoryGalleryWindow.MemoryReplayRequested -=
            OnMemoryReplayRequested;
        _memoryGalleryWindow.Closed -= OnMemoryGalleryClosed;
        _memoryGalleryWindow = null;
    }

    private void UpdateMemoryUi()
    {
        if (_memoryFeature is null)
        {
            return;
        }

        _memoryGalleryWindow?.Refresh(
            _memoryFeature.GetGalleryState());
    }

    private void ReconcileDiaryReadTracking()
    {
        if (_diaryFeature is null || _settings is null)
        {
            return;
        }

        DateOnly[] diaryDates = GetGeneratedDiaryDates();
        DiaryReadTrackingState next = DiaryReadTracker.Initialize(
            _settings.DiaryReadTrackingInitialized,
            _settings.ReadDiaryDates,
            diaryDates);
        StoreDiaryReadTracking(next);
        UpdateDiaryUi(diaryDates);
    }

    private void StoreDiaryReadTracking(DiaryReadTrackingState state)
    {
        if (_settings is null)
        {
            return;
        }

        bool changed =
            _settings.DiaryReadTrackingInitialized != state.IsInitialized
            || !_settings.ReadDiaryDates.SequenceEqual(state.ReadDates);
        _diaryReadTracking = state;
        _settings.DiaryReadTrackingInitialized = state.IsInitialized;
        _settings.ReadDiaryDates = state.ReadDates.ToList();
        if (changed)
        {
            SaveSettings();
        }
    }

    private void UpdateDiaryUi(IReadOnlyList<DateOnly>? diaryDates = null)
    {
        if (_diaryFeature is null)
        {
            return;
        }

        diaryDates ??= GetGeneratedDiaryDates();
        int unreadCount = DiaryReadTracker.GetUnreadCount(
            _diaryReadTracking,
            diaryDates);
        _petWindow?.SetDiaryUnreadCount(unreadCount);
        _taskbarIcon?.SetDiaryUnreadCount(unreadCount);
    }

    private DateOnly[] GetGeneratedDiaryDates() =>
        _diaryFeature?.Current.Days
            .Where(static day => day.Entry is not null)
            .Select(static day => day.Date)
            .Distinct()
            .Order()
            .ToArray()
        ?? [];

    private void OnAlwaysOnTopToggleRequested(object? sender, EventArgs e)
    {
        if (_settings is null || _petWindow is null)
        {
            return;
        }

        _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
        _petWindow.Topmost = _settings.AlwaysOnTop;
        _petController?.SetTopmost(_settings.AlwaysOnTop);
        UpdateControlStates();
        ApplyPresentationVisibility();
        SaveSettings();
    }

    private void OnStartWithWindowsToggleRequested(object? sender, EventArgs e)
    {
        if (_settings is null || _startupRegistration is null)
        {
            return;
        }

        try
        {
            _startupRegistration.SetEnabled(!_startupRegistration.IsEnabled);
            _settings.StartWithWindows = _startupRegistration.IsEnabled;
            SaveSettings();
        }
        catch (Exception exception)
        {
            _errorLog?.Write(
                "application",
                "toggle-start-with-windows",
                "start-with-windows-toggle-failed",
                exception);
            MessageBox.Show(
                "咕噜没能跟着电脑一起醒来，稍后再试吧。",
                "咕噜",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _settings.StartWithWindows = _startupRegistration.IsEnabled;
            UpdateControlStates();
        }
    }

    private async void OnManualUpdateRequested(object? sender, EventArgs e)
    {
        if (_updateCheckInProgress || _updateService is null)
        {
            return;
        }

        CloseAllAuxiliarySurfaces();

        _updateCheckInProgress = true;
        _taskbarIcon?.SetUpdateCheckInProgress(true);
        _petWindow?.SetUpdateCheckInProgress(true);

        var cancellation = new CancellationTokenSource();
        var operationCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var progressWindow = new UpdateProgressWindow();
        if (_petWindow?.IsVisible == true)
        {
            progressWindow.Owner = _petWindow;
        }

        progressWindow.CancelRequested += (_, _) => cancellation.Cancel();
        _updateOperationCancellation = cancellation;
        _updateOperationCompleted = operationCompleted;
        _updateProgressWindow = progressWindow;
        progressWindow.SetStage(
            "正在检查新版本…",
            "正在连接更新服务。",
            percent: 0);
        progressWindow.Show();

        string? stagingDirectory = null;
        string? stateDirectory = null;
        bool installerStarted = false;

        try
        {
            UpdateCheckResult result = await _updateService.CheckAsync(
                cancellation.Token);
            switch (result.Status)
            {
                case UpdateCheckStatus.NotConfigured:
                    progressWindow.CloseForCompletion();
                    MessageBox.Show(
                        "暂时无法检查新版本，请稍后再试。",
                        "检查新版本",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    break;

                case UpdateCheckStatus.UpToDate:
                    cancellation.Token.ThrowIfCancellationRequested();
                    progressWindow.SetStage(
                        "检查完成",
                        "当前版本已是最新版本。",
                        percent: null,
                        canCancel: false);
                    await progressWindow.CompleteLatestVersionProgressAsync(
                        cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    progressWindow.CloseForCompletion();
                    MessageBox.Show(
                        $"当前已是最新版本（{result.CurrentVersion}）。",
                        "检查新版本",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    break;

                case UpdateCheckStatus.UpdateAvailable:
                    UpdatePackageDescriptor package = result.Package
                        ?? throw new InvalidDataException(
                            "Validated update result has no package descriptor.");
                    progressWindow.SetStage(
                        $"发现新版本 {result.AvailableVersion}",
                        "确认后会下载并验证文件，再开始安装。",
                        percent: 8);
                    MessageBoxResult answer = MessageBox.Show(
                        progressWindow,
                        BuildUpdateConfirmationMessage(result, package),
                        "检查新版本",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                    if (answer != MessageBoxResult.Yes)
                    {
                        break;
                    }

                    Guid operationId = Guid.NewGuid();
                    string installationDirectory = Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(AppContext.BaseDirectory));
                    string installationParent = Path.GetDirectoryName(
                        installationDirectory)
                        ?? throw new InvalidOperationException(
                            "The application install directory has no parent.");
                    stagingDirectory = Path.Combine(
                        installationParent,
                        $"gulu-update-staging-{operationId:N}");
                    string backupDirectory = Path.Combine(
                        installationParent,
                        $"gulu-update-backup-{operationId:N}");

                    progressWindow.SetStage(
                        $"正在下载 {result.AvailableVersion}…",
                        BuildDownloadProgressText(0, package.PackageSize),
                        percent: 8);
                    var progress = new Progress<UpdatePreparationProgress>(value =>
                    {
                        string status = value.Phase switch
                        {
                            UpdatePreparationPhase.Downloading =>
                                $"正在下载 {result.AvailableVersion}…",
                            UpdatePreparationPhase.Verifying =>
                                "正在校验更新文件…",
                            UpdatePreparationPhase.Extracting =>
                                "正在准备更新文件…",
                            UpdatePreparationPhase.Finalizing =>
                                "正在完成最后准备…",
                            UpdatePreparationPhase.Ready =>
                                "更新文件已准备好",
                            _ => throw new InvalidEnumArgumentException(
                                nameof(value.Phase),
                                (int)value.Phase,
                                typeof(UpdatePreparationPhase)),
                        };
                        progressWindow.SetStage(
                            status,
                            BuildUpdatePreparationProgressText(value),
                            8 + value.Percentage * 0.92);
                    });

                    _ = await _updateService.StageUpdateAsync(
                        result,
                        installationDirectory,
                        stagingDirectory,
                        progress,
                        cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();

                    progressWindow.SetStage(
                        "更新文件已准备好，即将开始安装…",
                        "应用会暂时关闭；若新版本启动失败，将自动恢复当前版本。",
                        percent: 100,
                        canCancel: false);
                    stateDirectory = StartUpdateInstaller(
                        operationId,
                        installationDirectory,
                        stagingDirectory,
                        backupDirectory);
                    installerStarted = true;
                    progressWindow.CloseForCompletion();
                    ExitApplication(waitForUpdate: false);
                    break;

                case UpdateCheckStatus.Failed:
                    _errorLog?.Write(
                        "update",
                        "check-manually",
                        "manual-update-check-failed",
                        new InvalidOperationException(
                            result.ErrorMessage ?? "Update check failed."));
                    progressWindow.CloseForCompletion();
                    MessageBox.Show(
                        "未能检查新版本。当前版本仍可正常使用，请稍后再试。",
                        "检查新版本",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    break;
            }
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            progressWindow.CloseForCompletion();
        }
        catch (Exception exception)
        {
            _errorLog?.Write(
                "update",
                "check-manually",
                "manual-update-operation-failed",
                exception);
            progressWindow.CloseForCompletion();
            MessageBox.Show(
                "未能检查新版本。当前版本仍可正常使用，请稍后再试。",
                "检查新版本",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            if (!installerStarted)
            {
                TryDeleteUpdateWorkingDirectory(
                    stagingDirectory,
                    "gulu-update-staging-");
                TryDeleteUpdateStateDirectory(stateDirectory);
            }

            if (ReferenceEquals(_updateProgressWindow, progressWindow))
            {
                _updateProgressWindow = null;
            }

            if (ReferenceEquals(_updateOperationCancellation, cancellation))
            {
                _updateOperationCancellation = null;
            }

            if (ReferenceEquals(_updateOperationCompleted, operationCompleted))
            {
                _updateOperationCompleted = null;
            }

            if (progressWindow.IsLoaded)
            {
                progressWindow.CloseForCompletion();
            }

            cancellation.Dispose();
            _updateCheckInProgress = false;
            _taskbarIcon?.SetUpdateCheckInProgress(false);
            _petWindow?.SetUpdateCheckInProgress(false);
            operationCompleted.TrySetResult(true);
        }
    }

    private void OnVisibilityToggleRequested(object? sender, EventArgs e)
    {
        _userWantsPetVisible = !_userWantsPetVisible;
        ApplyPresentationVisibility(activate: _userWantsPetVisible);
        UpdateControlStates();
    }

    private void OnResetPositionRequested(object? sender, EventArgs e)
    {
        if (_petWindow is null || _settings is null)
        {
            return;
        }

        _petWindow.ResetToDefaultPosition();
        SaveCurrentWindowPosition();
        _userWantsPetVisible = true;
        if (!_startupPresentation.RequestActivation())
        {
            return;
        }

        ApplyPresentationVisibility(activate: true);
        UpdateControlStates();
    }

    private void OnExitRequested(object? sender, EventArgs e) => ExitApplication();

    private void OnActivateRequested(object? sender, EventArgs e)
    {
        _userWantsPetVisible = true;
        ApplyPresentationVisibility(activate: true);
        UpdateControlStates();
    }

    private void OnPetDragCompleted(object? sender, PetDragMotionEventArgs e)
    {
        WindowPosition? restorePosition = e.SideDockCandidate is { } candidate
            ? new WindowPosition(
                candidate.RestoreBounds.Left,
                candidate.RestoreBounds.Top)
            : null;
        SaveCurrentWindowPosition(restorePosition);
    }

    private void OnPetWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        ExitApplication();
    }

    private void OnFullscreenStateChanged(object? sender, FullscreenStateChangedEventArgs e)
    {
        _foregroundIsFullscreen = e.IsFullscreen;
        ApplyPresentationVisibility();
    }

    private void ActivateRunningInstance()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ActivateRunningInstance);
            return;
        }

        _userWantsPetVisible = true;
        if (!_startupPresentation.RequestActivation())
        {
            return;
        }

        ApplyPresentationVisibility(activate: true);
        UpdateControlStates();
    }

    private void ApplyPresentationVisibility(bool activate = false)
    {
        if (!_startupPresentation.IsRuntimeReady ||
            _petWindow is null ||
            _petController is null ||
            _settings is null)
        {
            return;
        }

        bool shouldShow = FullscreenPresentationPolicy.ShouldShowPet(
            _userWantsPetVisible,
            _settings.AlwaysOnTop,
            _foregroundIsFullscreen);
        shouldShow = shouldShow || _memoryFeature?.IsPlaying == true;

        if (shouldShow)
        {
            if (!_petWindow.IsVisible)
            {
                bool previousShowActivated = _petWindow.ShowActivated;
                _petWindow.ShowActivated = activate;
                try
                {
                    _petWindow.Show();
                }
                finally
                {
                    _petWindow.ShowActivated = previousShowActivated;
                }
            }

            if (_petWindow.WindowState == WindowState.Minimized)
            {
                _petWindow.WindowState = WindowState.Normal;
            }

            if (activate)
            {
                _petWindow.Activate();
            }
        }
        else if (_petWindow.IsVisible)
        {
            _petWindow.Hide();
        }

        _petController.SetPresentationVisible(shouldShow);
        _petController.SetFullscreen(_foregroundIsFullscreen);
        _postcardFeature?.SetPresentationVisible(
            shouldShow && _memoryFeature?.IsPlaying != true);
        _memoryFeature?.SetPresentationVisible(shouldShow);
    }

    private void UpdateControlStates()
    {
        if (_settings is null)
        {
            return;
        }

        _petWindow?.SetControlState(
            _settings.AlwaysOnTop,
            _settings.StartWithWindows,
            _userWantsPetVisible,
            _settings.TickScoreLoggingEnabled);
        _taskbarIcon?.SetControlState(
            _settings.AlwaysOnTop,
            _settings.StartWithWindows,
            _userWantsPetVisible,
            _settings.TickScoreLoggingEnabled);
    }

    private void SaveCurrentWindowPosition(
        WindowPosition? preferredPosition = null)
    {
        if (_petWindow is null || _settings is null)
        {
            return;
        }

        WindowPosition position = preferredPosition ??
            _petWindow.GetWindowPosition();
        _settings.WindowLeft = position.Left;
        _settings.WindowTop = position.Top;
        _settings.WindowCoordinateSpaceVersion =
            UserSettings.CurrentWindowCoordinateSpaceVersion;
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (_settingsStore is null || _settings is null)
        {
            return;
        }

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to save GuluPet settings: {exception}");
            _errorLog?.Write(
                "settings",
                "save",
                "settings-save-failed",
                exception);
        }
    }

    private void OnEyeAccessorySelectionChanged(string? selectedId)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.SelectedEyeAccessoryId = selectedId;
        SaveSettings();
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        _errorLog?.Write(
            "application",
            "dispatcher-unhandled",
            "unhandled-exception",
            e.Exception);
        // Do not mark the exception handled: diagnostics must not keep a
        // corrupted UI process alive.
    }

    private void OnAppDomainUnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        _errorLog?.Write(
            "application",
            "appdomain-unhandled",
            "unhandled-exception",
            e.ExceptionObject as Exception
                ?? new InvalidOperationException(
                    "A non-Exception object reached the unhandled hook."));
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        _errorLog?.Write(
            "application",
            "task-unobserved",
            "unobserved-task-exception",
            e.Exception);
    }

    private void ExitApplication(bool waitForUpdate = true)
    {
        if (_isExiting)
        {
            return;
        }

        if (waitForUpdate
            && _updateCheckInProgress
            && _updateOperationCompleted is not null)
        {
            if (_exitAfterUpdateCancellation)
            {
                return;
            }

            _exitAfterUpdateCancellation = true;
            _updateProgressWindow?.SetStage(
                "正在安全停下更新…",
                "临时下载和暂存文件清理完成后，咕噜就会退出。",
                canCancel: false);
            _updateOperationCancellation?.Cancel();
            _ = ExitWhenUpdateStopsAsync(_updateOperationCompleted.Task);
            return;
        }

        _isExiting = true;
        SaveCurrentWindowPosition();
        _fullscreenMonitor?.Stop();
        _taskbarIcon?.Hide();
        Shutdown(0);
    }

    private async Task ExitWhenUpdateStopsAsync(Task updateOperation)
    {
        try
        {
            await updateOperation;
        }
        finally
        {
            _exitAfterUpdateCancellation = false;
            ExitApplication(waitForUpdate: false);
        }
    }

    private static string BuildUpdateConfirmationMessage(
        UpdateCheckResult result,
        UpdatePackageDescriptor package)
    {
        double packageMebibytes = package.PackageSize / 1024d / 1024d;
        return $"发现新版本 {result.AvailableVersion}。\n\n" +
            $"需要下载约 {packageMebibytes:0.0} MiB。下载完成后会验证签名，" +
            "然后自动安装并重新启动；如果新版本没能正常启动，会恢复现在的版本。" +
            "\n\n现在更新吗？";
    }

    private static string BuildDownloadProgressText(
        long bytesReceived,
        long totalBytes)
    {
        double receivedMebibytes = bytesReceived / 1024d / 1024d;
        double totalMebibytes = totalBytes / 1024d / 1024d;
        return $"{receivedMebibytes:0.0} / {totalMebibytes:0.0} MiB";
    }

    private static string BuildUpdatePreparationProgressText(
        UpdatePreparationProgress progress) =>
        progress.Phase switch
        {
            UpdatePreparationPhase.Downloading => BuildDownloadProgressText(
                progress.BytesProcessed,
                progress.TotalBytes),
            UpdatePreparationPhase.Verifying =>
                "下载完成，正在核对文件完整性。",
            UpdatePreparationPhase.Extracting =>
                "正在展开并检查更新内容。",
            UpdatePreparationPhase.Finalizing =>
                "正在生成可安装的新版本。",
            UpdatePreparationPhase.Ready =>
                "文件校验、解压和暂存均已完成。",
            _ => throw new InvalidEnumArgumentException(
                nameof(progress.Phase),
                (int)progress.Phase,
                typeof(UpdatePreparationPhase)),
        };

    private static string StartUpdateInstaller(
        Guid operationId,
        string installationDirectory,
        string stagingDirectory,
        string backupDirectory)
    {
        string stateDirectory = Path.Combine(
            AppIdentity.LocalDataDirectory,
            "Updates",
            operationId.ToString("N"));
        if (Directory.Exists(stateDirectory) || File.Exists(stateDirectory))
        {
            throw new IOException(
                "The create-only update state directory already exists.");
        }

        Directory.CreateDirectory(stateDirectory);
        try
        {
            string sourceScript = Path.Combine(
                AppContext.BaseDirectory,
                "Update",
                "Install-GuluPetUpdate.ps1");
            if (!File.Exists(sourceScript))
            {
                throw new FileNotFoundException(
                    "The trusted update installer is missing.",
                    sourceScript);
            }

            string installerScript = Path.Combine(
                stateDirectory,
                "Install-GuluPetUpdate.ps1");
            File.Copy(sourceScript, installerScript, overwrite: false);

            string windowsPowerShell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            if (!File.Exists(windowsPowerShell))
            {
                throw new FileNotFoundException(
                    "Windows PowerShell 5.1 is unavailable.",
                    windowsPowerShell);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = windowsPowerShell,
                WorkingDirectory = stateDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-WindowStyle",
                "Hidden",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                installerScript,
                "-InstallDirectory",
                installationDirectory,
                "-StagingDirectory",
                stagingDirectory,
                "-BackupDirectory",
                backupDirectory,
                "-StateDirectory",
                stateDirectory,
                "-OperationId",
                operationId.ToString("N"),
                "-CurrentProcessId",
                Environment.ProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process installer = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Windows did not start the update installer.");
            return stateDirectory;
        }
        catch
        {
            TryDeleteUpdateStateDirectory(stateDirectory);
            throw;
        }
    }

    private static void TryDeleteUpdateWorkingDirectory(
        string? directory,
        string requiredPrefix)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            string fullPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(directory));
            string leaf = Path.GetFileName(fullPath);
            if (!leaf.StartsWith(requiredPrefix, StringComparison.Ordinal)
                || !Guid.TryParseExact(
                    leaf[requiredPrefix.Length..],
                    "N",
                    out _)
                || !Directory.Exists(fullPath))
            {
                return;
            }

            var directoryInfo = new DirectoryInfo(fullPath);
            if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            Directory.Delete(fullPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteUpdateStateDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string expectedParent = Path.Combine(
            AppIdentity.LocalDataDirectory,
            "Updates");
        try
        {
            string fullPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(directory));
            if (!string.Equals(
                    Path.GetDirectoryName(fullPath),
                    Path.GetFullPath(expectedParent),
                    StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(
                    Path.GetFileName(fullPath),
                    "N",
                    out _)
                || !Directory.Exists(fullPath))
            {
                return;
            }

            var directoryInfo = new DirectoryInfo(fullPath);
            if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            Directory.Delete(fullPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool HasCommandLineSwitch(
        IEnumerable<string> arguments,
        string expected) =>
        arguments.Any(argument =>
            string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase));

    private Task<TestControlResponse> HandleTestControlRequestAsync(
        TestControlRequest request,
        CancellationToken cancellationToken)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return Task.FromResult(TestControlFailure(
                TestControlErrorCodes.InternalError,
                "The application is shutting down."));
        }

        if (Dispatcher.CheckAccess())
        {
            return Task.FromResult(HandleTestControlRequest(request));
        }

        return Dispatcher.InvokeAsync(
            () => HandleTestControlRequest(request),
            System.Windows.Threading.DispatcherPriority.Send,
            cancellationToken).Task;
    }

    private TestControlResponse HandleTestControlRequest(
        TestControlRequest request)
    {
        if (!TestControlProtocol.ValidateRequest(
                request,
                out string? errorCode,
                out string? error))
        {
            return TestControlFailure(
                errorCode ?? TestControlErrorCodes.InvalidRequest,
                error ?? "The request is invalid.");
        }

        try
        {
            string command = request.Command.Trim();
            if (command.Equals(
                    TestControlCommands.Ping,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new TestControlResponse
                {
                    Ok = true,
                    Message = "pong",
                };
            }

            if (command.Equals(
                    TestControlCommands.Status,
                    StringComparison.OrdinalIgnoreCase))
            {
                return TestControlSuccess(snapshot: CreateTestRuntimeSnapshot());
            }

            if (command.Equals(
                    TestControlCommands.Clips,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_petController is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The pet runtime is unavailable.");
                }

                IReadOnlyList<TestClipInfo> clips = _petController
                    .GetTestClipDiagnostics()
                    .Select(clip => new TestClipInfo
                    {
                        Name = clip.Name,
                        FrameCount = clip.FrameCount,
                        Fps = clip.Fps,
                        Loop = clip.Loop,
                        AssetStatus = clip.AssetStatus,
                        Placeholder = clip.Placeholder,
                        SourceBatch = clip.SourceBatch,
                        SourceStage = clip.SourceStage,
                        SourceFrameIndices = clip.SourceFrameIndices,
                        KnownIssues = clip.KnownIssues,
                    })
                    .ToArray();
                return TestControlSuccess(clips: clips);
            }

            if (command.Equals(
                    TestControlCommands.BehaviorStatus,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_petController is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The pet runtime is unavailable.");
                }

                return TestControlSuccess(
                    behaviorStatus: CreateTestBehaviorStatus(
                        _petController.GetBehaviorTestSnapshot(),
                        _petController.GetBehaviorTestPlaybackToken()));
            }

            if (command.Equals(
                    TestControlCommands.BehaviorOpenBrowser,
                    StringComparison.OrdinalIgnoreCase))
            {
                OnBehaviorsRequested(this, EventArgs.Empty);
                return TestControlSuccess(
                    message: "Behavior browser opened.");
            }

            if (command.Equals(
                    TestControlCommands.BehaviorEvaluate,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_petController is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The pet runtime is unavailable.");
                }

                BehaviorCoordinatorSnapshot snapshot =
                    _petController.GetBehaviorTestSnapshot();
                BehaviorSelectionPlan evaluation =
                    _petController.EvaluateBehaviorForTest();
                return TestControlSuccess(
                    behaviorEvaluation: CreateTestBehaviorEvaluation(
                        evaluation,
                        snapshot.Context.Revision,
                        selectedBehaviorId: null,
                        attemptOrder: []));
            }

            if (command.Equals(
                    TestControlCommands.BehaviorTick,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_petController is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The pet runtime is unavailable.");
                }

                if (_petController.IsSideDockedForTest)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.OperationBlocked,
                        "Side-dock mode blocks behavior ticks.");
                }

                BehaviorTickResult result =
                    _petController.ForceBehaviorTickForTest(request.Seed);
                BehaviorCoordinatorSnapshot snapshot =
                    _petController.GetBehaviorTestSnapshot();
                return TestControlSuccess(
                    behaviorEvaluation: CreateTestBehaviorEvaluation(
                        result.Evaluation,
                        snapshot.Context.Revision,
                        result.SelectedBehaviorId,
                        result.AttemptOrder),
                    behaviorTick: new TestBehaviorTickResult
                    {
                        EvaluatedAtUtc = DateTimeOffset.UtcNow,
                        Seed = request.Seed,
                        Evaluated = true,
                        SelectedBehaviorId =
                            result.SelectedBehaviorId?.Value,
                        Enqueued = result.QueueResult?.Disposition is
                            SoftQueueEnqueueDisposition.Enqueued or
                            SoftQueueEnqueueDisposition.Refreshed or
                            SoftQueueEnqueueDisposition.ReplacedActiveTick,
                        QueueDisposition =
                            result.QueueResult?.Disposition.ToString(),
                        SuppressionReason =
                            result.QueueResult?.Disposition is
                                SoftQueueEnqueueDisposition.Enqueued or
                                SoftQueueEnqueueDisposition.Refreshed or
                                SoftQueueEnqueueDisposition.ReplacedActiveTick
                                ? null
                                : result.QueueResult?.Diagnostic,
                        NextIntervalSeconds = snapshot.LastTickInterval is { } interval
                            ? checked((int)Math.Round(interval.TotalSeconds))
                            : null,
                    });
            }

            if (command.Equals(
                    TestControlCommands.Enqueue,
                    StringComparison.OrdinalIgnoreCase) ||
                command.Equals(
                    TestControlCommands.StartIfIdle,
                    StringComparison.OrdinalIgnoreCase) ||
                command.Equals(
                    TestControlCommands.BehaviorPreview,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_petController is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The pet runtime is unavailable.");
                }

                string behaviorId = request.BehaviorId!.Trim();
                BehaviorMutationResult mutation = command.Equals(
                    TestControlCommands.Enqueue,
                    StringComparison.OrdinalIgnoreCase)
                    ? _petController.EnqueueBehaviorForTest(behaviorId)
                    : command.Equals(
                        TestControlCommands.StartIfIdle,
                        StringComparison.OrdinalIgnoreCase)
                        ? _petController.StartBehaviorIfIdleForTest(behaviorId)
                        : _petController.StartBehaviorManualPreview(
                            new BehaviorId(behaviorId));
                TestBehaviorMutationResult wireMutation =
                    CreateTestBehaviorMutation(command, behaviorId, mutation);
                return mutation.Accepted
                    ? TestControlSuccess(behaviorMutation: wireMutation)
                    : new TestControlResponse
                    {
                        Ok = false,
                        ErrorCode = string.Equals(
                            mutation.RejectionReason,
                            "side-docked",
                            StringComparison.Ordinal)
                                ? TestControlErrorCodes.OperationBlocked
                                : "behavior_rejected",
                        Error = mutation.RejectionReason ??
                                "The behavior request was rejected.",
                        BehaviorMutation = wireMutation,
                    };
            }

            if (command.Equals(
                    TestControlCommands.Queue,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_petController is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The pet runtime is unavailable.");
                }

                return TestControlSuccess(
                    behaviorQueue: CreateTestBehaviorQueue(
                        _petController.GetBehaviorTestSnapshot().Queue));
            }

            if (command.Equals(
                    TestControlCommands.BehaviorEvents,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_petController is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The pet runtime is unavailable.");
                }

                return TestControlSuccess(
                    behaviorEvents: CreateTestBehaviorEvents(
                        _petController.ReadBehaviorEventsForTest(
                            request.After ?? 0)));
            }

            if (command.Equals(
                    TestControlCommands.MemoryStatus,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_memoryFeature is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The memory runtime is unavailable.");
                }

                return TestControlSuccess(
                    "Memory status captured.",
                    memoryStatus: CreateTestMemoryStatus(
                        _memoryFeature.GetSnapshot()));
            }

            if (command.Equals(
                    TestControlCommands.MemoryAddInteraction,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_memoryFeature is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The memory runtime is unavailable.");
                }

                if (!_memoryFeature.CanInjectTestInteraction)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.RequiresIsolatedInstance,
                        "Memory interaction injection requires an isolated " +
                        "test instance.");
                }

                MemoryTestInteractionBatchResult addResult =
                    _memoryFeature.AddAcceptedInteractionsForTest(
                        request.Count!.Value,
                        out MemoryFeatureSnapshot snapshot);
                if (addResult ==
                    MemoryTestInteractionBatchResult.PresentationBusy)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.OperationBlocked,
                        "A memory is already pending or playing.");
                }

                if (addResult ==
                    MemoryTestInteractionBatchResult.CrossesNextUnlock)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InvalidCount,
                        "The requested batch would cross the next memory " +
                        "unlock. Add no more than " +
                        $"{snapshot.AcceptedInteractionsUntilNextUnlock}.");
                }

                return TestControlSuccess(
                    "Memory interactions were added.",
                    memoryStatus: CreateTestMemoryStatus(snapshot));
            }

            if (command.Equals(
                    TestControlCommands.PostcardStatus,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_postcardFeature is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The postcard runtime is unavailable.");
                }

                return TestControlSuccess(
                    "Postcard status captured.",
                    postcardStatus: CreateTestPostcardStatus(
                        _postcardFeature.GetSnapshot()));
            }

            if (command.Equals(
                    TestControlCommands.PostcardStartOuting,
                    StringComparison.OrdinalIgnoreCase) ||
                command.Equals(
                    TestControlCommands.PostcardCompleteOuting,
                    StringComparison.OrdinalIgnoreCase) ||
                command.Equals(
                    TestControlCommands.PostcardClaimArrival,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_postcardFeature is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The postcard runtime is unavailable.");
                }

                if (!_postcardFeature.CanControlOutingForTest)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.RequiresIsolatedInstance,
                        "Postcard outing control requires an isolated test instance.");
                }

                try
                {
                    string message;
                    if (command.Equals(
                            TestControlCommands.PostcardStartOuting,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = _postcardFeature.StartOutingForTest();
                        message = "Postcard outing started.";
                    }
                    else if (command.Equals(
                                 TestControlCommands.PostcardCompleteOuting,
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        _ = _postcardFeature.CompleteOutingForTest();
                        message = "Postcard outing completed.";
                    }
                    else
                    {
                        if (!_postcardFeature.ClaimArrivalForTest())
                        {
                            return TestControlFailure(
                                TestControlErrorCodes.OperationBlocked,
                                "The waiting postcard could not be durably claimed.");
                        }

                        message = "Waiting postcard claimed.";
                    }

                    return TestControlSuccess(
                        message,
                        postcardStatus: CreateTestPostcardStatus(
                            _postcardFeature.GetSnapshot()));
                }
                catch (InvalidOperationException exception)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.OperationBlocked,
                        exception.Message);
                }
            }

            if (command.Equals(
                    TestControlCommands.PostcardOpenGallery,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_postcardFeature is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The postcard runtime is unavailable.");
                }

                _postcardFeature.ShowGallery();
                return TestControlSuccess(
                    "The postcard gallery was opened.",
                    postcardStatus: CreateTestPostcardStatus(
                        _postcardFeature.GetSnapshot()));
            }

            if (command.Equals(
                    TestControlCommands.PostcardShow,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_postcardFeature is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The postcard runtime is unavailable.");
                }

                string postcardId = request.PostcardId!.Trim();
                if (!_postcardFeature.ShowPostcardForTest(postcardId))
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InvalidPostcardId,
                        $"Postcard '{postcardId}' is unknown or cannot be shown.");
                }

                return TestControlSuccess(
                    $"Showing postcard '{postcardId}'.",
                    postcardStatus: CreateTestPostcardStatus(
                        _postcardFeature.GetSnapshot()));
            }

            if (command.Equals(
                    TestControlCommands.PostcardDismissPopup,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_postcardFeature is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The postcard runtime is unavailable.");
                }

                bool dismissed = _postcardFeature.DismissPopupForTest();
                return TestControlSuccess(
                    dismissed
                        ? "The postcard popup is being dismissed."
                        : "No postcard popup was visible.",
                    postcardStatus: CreateTestPostcardStatus(
                        _postcardFeature.GetSnapshot()));
            }

            if (command.Equals(
                    TestControlCommands.BeginProbe,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_petController is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The pet runtime is unavailable.");
                }

                _petController.SetTestProbeMode(enabled: true);
                return TestControlSuccess(
                    "Random behavior and dialogue were paused for the probe.",
                    CreateTestRuntimeSnapshot());
            }

            if (command.Equals(
                    TestControlCommands.EndProbe,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_petController is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The pet runtime is unavailable.");
                }

                _petController.SetTestProbeMode(enabled: false);
                return TestControlSuccess(
                    _petController.IsSideDockedForTest
                        ? "The probe ended; side-dock mode keeps behavior " +
                          "and dialogue paused."
                        : "Random behavior and dialogue resumed after the probe.",
                    CreateTestRuntimeSnapshot());
            }

            if (command.Equals(
                    TestControlCommands.Play,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_petController is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The pet runtime is unavailable.");
                }

                string action = request.Action!.Trim();
                TestClipPlaybackResult playResult =
                    _petController.PlayTestClipForTest(action);
                if (playResult ==
                    TestClipPlaybackResult.TopLevelPresentationActive)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.OperationBlocked,
                        "Memory presentation currently owns the pet window.");
                }

                if (playResult == TestClipPlaybackResult.SideDocked)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.OperationBlocked,
                        "Side-dock mode currently owns the pet window.");
                }

                if (playResult == TestClipPlaybackResult.UnknownClip)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.UnknownAction,
                        $"Unknown animation action '{action}'.");
                }

                if (playResult == TestClipPlaybackResult.PlaybackFailed)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        $"Animation action '{action}' could not be started.");
                }

                return TestControlSuccess(
                    $"Playing '{action}'.",
                    CreateTestRuntimeSnapshot());
            }

            if (command.Equals(
                    TestControlCommands.Show,
                    StringComparison.OrdinalIgnoreCase))
            {
                _userWantsPetVisible = true;
                ApplyPresentationVisibility(activate: false);
                UpdateControlStates();
                return TestControlSuccess(
                    "The pet was requested visible.",
                    CreateTestRuntimeSnapshot());
            }

            if (command.Equals(
                    TestControlCommands.Hide,
                    StringComparison.OrdinalIgnoreCase))
            {
                _userWantsPetVisible = false;
                ApplyPresentationVisibility(activate: false);
                UpdateControlStates();
                return TestControlSuccess(
                    "The pet was requested hidden.",
                    CreateTestRuntimeSnapshot());
            }

            if (command.Equals(
                    TestControlCommands.ResetPosition,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_petWindow is null)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.InternalError,
                        "The pet window is unavailable.");
                }

                if (_petController?
                        .IsTopLevelPresentationRequestedOrActive == true)
                {
                    return TestControlFailure(
                        TestControlErrorCodes.OperationBlocked,
                        "A top-level presentation currently owns the pet window.");
                }

                _petWindow.ResetToDefaultPosition();
                SaveCurrentWindowPosition();
                return TestControlSuccess(
                    "The pet window position was reset.",
                    CreateTestRuntimeSnapshot());
            }

            return TestControlFailure(
                TestControlErrorCodes.InvalidCommand,
                $"Unknown command '{request.Command}'.");
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"GuluPet test-control command failed: {exception}");
            return TestControlFailure(
                TestControlErrorCodes.InternalError,
                "The test-control command failed.");
        }
    }

    private static TestBehaviorStatus CreateTestBehaviorStatus(
        BehaviorCoordinatorSnapshot snapshot,
        BehaviorPlaybackToken? playbackToken)
    {
        BehaviorSessionSnapshot? session = snapshot.Session;
        return new TestBehaviorStatus
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            RuntimePaused = snapshot.Paused,
            ContextRevision = snapshot.Context.Revision,
            LocalNow = snapshot.Context.LocalNow,
            ApplicationCategory = snapshot.Context.ApplicationCategory,
            ApplicationCategoryAvailable =
                snapshot.Context.ApplicationCategoryAvailable,
            ApplicationStableMilliseconds =
                snapshot.Context.ApplicationStableFor.TotalMilliseconds,
            KeyboardPerMinute = snapshot.Context.KeyboardPerMinute,
            MouseClicksPerMinute = snapshot.Context.MouseClicksPerMinute,
            KeyboardCountSinceStart =
                snapshot.Context.KeyboardCountSinceStart,
            MouseClickCountSinceStart =
                snapshot.Context.MouseClickCountSinceStart,
            IdleMilliseconds = snapshot.Context.IdleFor.TotalMilliseconds,
            IsBusy = snapshot.Context.IsBusy,
            IsFullscreen = snapshot.Context.IsFullscreen,
            IsLocked = snapshot.Context.IsLocked,
            WeatherKind = snapshot.Context.WeatherKind,
            WeatherAvailable = snapshot.Context.WeatherAvailable,
            TemperatureCelsius = snapshot.Context.TemperatureCelsius,
            StableState = ToCamelCase(snapshot.State.StableState),
            LifecyclePhase = ToCamelCase(snapshot.State.Phase),
            CurrentBehaviorId = session?.BehaviorId.Value,
            SessionToken = session?.SessionToken.Value,
            PlaybackToken = playbackToken?.Value,
            BehaviorPhase = session is null
                ? null
                : ToCamelCase(session.Phase),
            ClipId = session?.ClipId,
            LogicalTimeMilliseconds =
                session?.LogicalTime.TotalMilliseconds ?? 0,
            Committed = session?.Committed ?? false,
            NextCueIndex = session?.NextCueIndex ?? 0,
            TerminalStatus = session?.Terminal is null
                ? null
                : ToCamelCase(session.Terminal.Status),
            TerminalReason = session?.Terminal?.Reason,
            ActiveInteractionKey = snapshot.InteractionLease?.Key,
            ActiveInteractionTriggerTag =
                snapshot.InteractionLease?.TriggerTag,
            InteractionRequestId = snapshot.InteractionLease?.RequestId,
            InteractionSessionToken =
                snapshot.InteractionLease?.SessionToken?.Value,
        };
    }

    internal static TestPostcardStatus CreateTestPostcardStatus(
        PostcardFeatureSnapshot snapshot) =>
        new()
        {
            CapturedAtUtc = snapshot.CapturedAtUtc,
            OutingPhase = ToCamelCase(snapshot.OutingPhase),
            OutingStartedAtUtc = snapshot.OutingStartedAtUtc,
            OutingReadyAtUtc = snapshot.OutingReadyAtUtc,
            OutingArrivedAtUtc = snapshot.OutingArrivedAtUtc,
            OutingRemainingSeconds = snapshot.OutingRemainingSeconds,
            NextPostcardId = snapshot.NextPostcardId,
            AvailablePostcardCount = snapshot.AvailablePostcardCount,
            UnlockedPostcardIds = snapshot.UnlockedPostcardIds,
            UnacknowledgedPostcardIds =
                snapshot.UnacknowledgedPostcardIds,
            PopupVisible = snapshot.PopupVisible,
            PopupPostcardId = snapshot.PopupPostcardId,
            GalleryVisible = snapshot.GalleryVisible,
            GalleryPostcardId = snapshot.GalleryPostcardId,
        };

    internal static TestMemoryStatus CreateTestMemoryStatus(
        MemoryFeatureSnapshot snapshot) =>
        new()
        {
            CapturedAtUtc = snapshot.CapturedAtUtc,
            AcceptedInteractionCount =
                snapshot.AcceptedInteractionCount,
            AcceptedInteractionsUntilNextUnlock =
                snapshot.AcceptedInteractionsUntilNextUnlock,
            PendingMemoryId = snapshot.PendingMemoryId,
            CompletedMemoryIds = snapshot.CompletedMemoryIds,
            PlaybackTaskActive = snapshot.IsPlaying,
            PlayingMemoryId = snapshot.PlayingMemoryId,
            PlaybackPhase = snapshot.PlaybackPhase,
            PresentationActive = snapshot.PresentationActive,
            LastPlaybackOutcome = snapshot.LastPlaybackOutcome,
            LastPlaybackError = snapshot.LastPlaybackError,
        };

    private static TestBehaviorEvaluationReport CreateTestBehaviorEvaluation(
        BehaviorSelectionPlan plan,
        long contextRevision,
        BehaviorId? selectedBehaviorId,
        IReadOnlyList<BehaviorId> attemptOrder) =>
        new()
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ContextRevision = contextRevision,
            Source = "activeTick",
            SelectedBehaviorId = selectedBehaviorId?.Value,
            AttemptOrder = attemptOrder
                .Select(static id => id.Value)
                .ToArray(),
            TopBehaviorIds = plan.TopCandidates
                .Select(static item => item.BehaviorId.Value)
                .ToArray(),
            RandomConsumed = plan.RandomConsumed,
            Evaluations = plan.AllEvaluations
                .Select(
                    static item => new TestBehaviorEvaluationItem
                    {
                        BehaviorId = item.BehaviorId.Value,
                        Eligible = item.IsEligible,
                        RejectionReason = item.RejectionReason,
                        Score = item.Breakdown.Total,
                        Rank = item.Rank,
                        Probability = item.Probability,
                        Components = new Dictionary<string, double>(
                            StringComparer.Ordinal)
                        {
                            ["base"] = item.Breakdown.Base,
                            ["emotion"] = item.Breakdown.Emotion,
                            ["time"] = item.Breakdown.Time,
                            ["activity"] = item.Breakdown.Activity,
                            ["application"] = item.Breakdown.Application,
                            ["weather"] = item.Breakdown.Weather,
                            ["triggerBoost"] = item.Breakdown.TriggerBoost,
                            ["timeSinceLastRun"] =
                                item.Breakdown.TimeSinceLastRun,
                            ["relationship"] = item.Breakdown.Relationship,
                            ["sameBehaviorPenalty"] =
                                item.Breakdown.SameBehaviorPenalty,
                            ["sameFamilyPenalty"] =
                                item.Breakdown.SameFamilyPenalty,
                            ["sameClipPenalty"] =
                                item.Breakdown.SameClipPenalty,
                            ["disturbUserPenalty"] =
                                item.Breakdown.DisturbUserPenalty,
                        },
                    })
                .ToArray(),
        };

    private TestBehaviorMutationResult CreateTestBehaviorMutation(
        string command,
        string requestedBehaviorId,
        BehaviorMutationResult mutation)
    {
        BehaviorCoordinatorSnapshot snapshot =
            _petController!.GetBehaviorTestSnapshot();
        BehaviorSessionSnapshot? session = snapshot.Session;
        return new TestBehaviorMutationResult
        {
            Command = command,
            BehaviorId = mutation.BehaviorId?.Value ?? requestedBehaviorId,
            Accepted = mutation.Accepted,
            Disposition = mutation.QueueResult?.Disposition.ToString()
                          ?? (mutation.Accepted
                              ? "started"
                              : "rejected"),
            RequestId = mutation.RequestId,
            GroupId = mutation.QueueResult?.GroupId,
            SessionToken = session is not null &&
                           session.BehaviorId == mutation.BehaviorId
                ? session.SessionToken.Value
                : null,
            Diagnostic = mutation.RejectionReason
                          ?? mutation.QueueResult?.Diagnostic,
            InteractionKey = mutation.InteractionKey,
        };
    }

    private static TestBehaviorQueueSnapshot CreateTestBehaviorQueue(
        SoftBehaviorQueueSnapshot snapshot) =>
        new()
        {
            CapturedAtMilliseconds = snapshot.CapturedAt.TotalMilliseconds,
            PendingCount = snapshot.PendingCount,
            ActiveRequest = snapshot.ActiveRequest is null
                ? null
                : CreateTestBehaviorRequest(snapshot.ActiveRequest),
            ActiveGroupId = snapshot.ActiveGroupId,
            ResumeAfterMilliseconds =
                snapshot.ResumeAfter?.TotalMilliseconds,
            Groups = snapshot.Groups
                .Select(
                    static group => new TestBehaviorQueueGroup
                    {
                        GroupId = group.GroupId,
                        AcceptedCount = group.AcceptedCount,
                        HasStarted = group.HasStarted,
                        Closed = group.Closed,
                        Active = group.IsActive,
                        PendingItems = group.PendingItems
                            .Select(CreateTestBehaviorRequest)
                            .ToArray(),
                    })
                .ToArray(),
            Evictions = snapshot.Evictions
                .Select(
                    static eviction => new TestBehaviorQueueEviction
                    {
                        OccurredAtMilliseconds =
                            eviction.OccurredAt.TotalMilliseconds,
                        GroupId = eviction.GroupId,
                        RequestIds = eviction.RequestIds,
                        BehaviorIds = eviction.BehaviorIds
                            .Select(static id => id.Value)
                            .ToArray(),
                    })
                .ToArray(),
            RecentDiagnostics = snapshot.RecentDiagnostics
                .Select(
                    static diagnostic => new TestBehaviorQueueDiagnostic
                    {
                        OccurredAtMilliseconds =
                            diagnostic.OccurredAt.TotalMilliseconds,
                        Operation = diagnostic.Operation,
                        Outcome = diagnostic.Outcome,
                        RequestId = diagnostic.RequestId,
                        GroupId = diagnostic.GroupId,
                        Detail = diagnostic.Detail,
                    })
                .ToArray(),
        };

    private static TestBehaviorRequestInfo CreateTestBehaviorRequest(
        BehaviorRequest request) =>
        new()
        {
            RequestId = request.RequestId,
            BehaviorId = request.BehaviorId.Value,
            Source = ToCamelCase(request.Source),
            SwitchMode = ToCamelCase(request.SwitchMode),
            Priority = request.Priority,
            CreatedAtMilliseconds = request.CreatedAt.TotalMilliseconds,
            ExpiresAtMilliseconds = request.ExpiresAt.TotalMilliseconds,
            DedupeKey = request.DedupeKey,
            ContextRevision = request.ContextRevision,
        };

    private static TestBehaviorEventPage CreateTestBehaviorEvents(
        BehaviorEventPage page) =>
        new()
        {
            RequestedAfterSequence = page.RequestedAfterSequence,
            TruncatedBeforeSequence = page.TruncatedBeforeSequence,
            Events = page.Events
                .Select(
                    static behaviorEvent => new TestBehaviorEvent
                    {
                        Sequence = behaviorEvent.Sequence,
                        OccurredAtMilliseconds =
                            behaviorEvent.OccurredAt.TotalMilliseconds,
                        Kind = ToCamelCase(behaviorEvent.Kind),
                        BehaviorId = behaviorEvent.BehaviorId?.Value,
                        RequestId = behaviorEvent.RequestId,
                        GroupId = behaviorEvent.GroupId,
                        SessionToken =
                            behaviorEvent.SessionToken?.Value,
                        Phase = behaviorEvent.Phase is null
                            ? null
                            : ToCamelCase(behaviorEvent.Phase.Value),
                        TerminalStatus =
                            behaviorEvent.TerminalStatus is null
                                ? null
                                : ToCamelCase(
                                    behaviorEvent.TerminalStatus.Value),
                        Reason = behaviorEvent.Reason,
                        Details = behaviorEvent.Details,
                    })
                .ToArray(),
        };

    private static string ToCamelCase<T>(T value)
        where T : struct, Enum
    {
        string text = value.ToString();
        return text.Length == 0
            ? text
            : char.ToLowerInvariant(text[0]) + text[1..];
    }

    private TestRuntimeSnapshot CreateTestRuntimeSnapshot()
    {
        PetRuntimeDiagnostics? runtime = _petController?.GetTestDiagnostics();
        TestWindowRect? window = null;
        if (_petWindow is not null)
        {
            WindowDisplayDiagnostics diagnostics =
                WindowPlacementService.GetDiagnostics(_petWindow);
            window = new TestWindowRect
            {
                Left = diagnostics.Window.Left,
                Top = diagnostics.Window.Top,
                Width = diagnostics.Window.Width,
                Height = diagnostics.Window.Height,
                CoordinateSpace = diagnostics.CoordinateSpace,
                DpiAwareness = diagnostics.DpiAwareness,
                Dpi = diagnostics.Dpi,
                MonitorBounds = ToTestPhysicalRect(
                    diagnostics.MonitorBounds),
                WorkArea = ToTestPhysicalRect(diagnostics.WorkArea),
            };
        }

        bool visible = _petWindow?.IsVisible == true;
        return new TestRuntimeSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Version = typeof(App).Assembly.GetName().Version?.ToString(),
            CurrentAction = runtime?.CurrentAction,
            FrameIndex = runtime?.FrameIndex ?? -1,
            FrameCount = runtime?.FrameCount ?? 0,
            Fps = runtime?.Fps ?? 0,
            Loop = runtime?.Loop ?? false,
            AssetStatus = runtime?.AssetStatus,
            Visible = visible,
            RequestedVisible = _userWantsPetVisible,
            HiddenReason = GetTestHiddenReason(visible),
            Topmost = _petWindow?.Topmost == true,
            Window = window,
            BehaviorState = runtime?.BehaviorState ?? "unavailable",
            Emotions = runtime?.Emotions
                ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
            AppBasePath = AppContext.BaseDirectory,
        };
    }

    private static TestPhysicalRect? ToTestPhysicalRect(
        WindowRectangle? rectangle) =>
        rectangle is WindowRectangle value
            ? new TestPhysicalRect
            {
                Left = value.Left,
                Top = value.Top,
                Width = value.Width,
                Height = value.Height,
            }
            : null;

    private string? GetTestHiddenReason(bool visible)
    {
        if (visible)
        {
            return null;
        }

        if (!_userWantsPetVisible)
        {
            return "requested-hidden";
        }

        if (_foregroundIsFullscreen && _settings?.AlwaysOnTop == false)
        {
            return "fullscreen";
        }

        return "window-not-visible";
    }

    private static TestControlResponse TestControlSuccess(
        string? message = null,
        TestRuntimeSnapshot? snapshot = null,
        IReadOnlyList<TestClipInfo>? clips = null,
        TestBehaviorStatus? behaviorStatus = null,
        TestBehaviorEvaluationReport? behaviorEvaluation = null,
        TestBehaviorTickResult? behaviorTick = null,
        TestBehaviorMutationResult? behaviorMutation = null,
        TestBehaviorQueueSnapshot? behaviorQueue = null,
        TestBehaviorEventPage? behaviorEvents = null,
        TestMemoryStatus? memoryStatus = null,
        TestPostcardStatus? postcardStatus = null) =>
        new()
        {
            Ok = true,
            Message = message,
            Snapshot = snapshot,
            Clips = clips,
            BehaviorStatus = behaviorStatus,
            BehaviorEvaluation = behaviorEvaluation,
            BehaviorTick = behaviorTick,
            BehaviorMutation = behaviorMutation,
            BehaviorQueue = behaviorQueue,
            BehaviorEvents = behaviorEvents,
            MemoryStatus = memoryStatus,
            PostcardStatus = postcardStatus,
        };

    private static TestControlResponse TestControlFailure(
        string errorCode,
        string error) =>
        new()
        {
            Ok = false,
            ErrorCode = errorCode,
            Error = error,
        };

    private bool InitializePetController(ValidatedRuntimeContent content)
    {
        if (_petWindow is null)
        {
            return false;
        }

        IRuntimeContextMonitor? runtimeContextMonitor = null;
        try
        {
            // The community build is offline-first. Applications can replace
            // this disabled monitor through their own composition root.
            runtimeContextMonitor = RuntimeContextMonitor.CreateDisabled();
            RelationshipStateStore? relationshipStateStore =
                _isolatedTestInstance
                    ? null
                    : new RelationshipStateStore();
            _petController = new PetController(
                _petWindow,
                content,
                runtimeContextMonitor,
                _tickScoreLogWriter,
                relationshipStateStore);
            _careFeature = new PetCareFeatureController(
                _petWindow,
                _petController,
                _isolatedTestInstance);
            // PetController now owns the monitor lifecycle.
            runtimeContextMonitor = null;
            _fullscreenMonitor = new FullscreenMonitor();
            _fullscreenMonitor.FullscreenStateChanged +=
                OnFullscreenStateChanged;
            _fullscreenMonitor.Start();

            bool initiallyVisible =
                FullscreenPresentationPolicy.ShouldShowPet(
                    _userWantsPetVisible,
                    _settings!.AlwaysOnTop,
                    _foregroundIsFullscreen);
            _petController.SetPresentationVisible(initiallyVisible);
            _petController.Start();
            _petController.SetFullscreen(_foregroundIsFullscreen);
            return true;
        }
        catch (Exception exception)
        {
            HandleRuntimeInitializationFailure(exception);
            runtimeContextMonitor?.Dispose();
            return false;
        }
    }

    private void HandleRuntimeInitializationFailure(Exception exception)
    {
        _startupPresentation.MarkInitializationFailed();
        _fullscreenMonitor?.Dispose();
        _fullscreenMonitor = null;
        _careFeature?.Dispose();
        _careFeature = null;
        _eyeAccessoryFeature?.Dispose();
        _eyeAccessoryFeature = null;
        if (_diaryFeature is not null)
        {
            _diaryFeature.StateChanged -= OnDiaryStateChanged;
            _diaryFeature.Dispose();
            _diaryFeature = null;
        }
        if (_memoryFeature is not null)
        {
            _memoryFeature.StateChanged -= OnMemoryStateChanged;
            _memoryFeature.Dispose();
            _memoryFeature = null;
        }

        if (_postcardFeature is not null)
        {
            _postcardFeature.StateChanged -= OnPostcardStateChanged;
            _postcardFeature.Dispose();
            _postcardFeature = null;
        }

        _petController?.Dispose();
        _petController = null;
        Debug.WriteLine($"Unable to initialize GuluPet runtime: {exception}");
        _errorLog?.Write(
            "application",
            "initialize-runtime",
            "runtime-initialization-failed",
            exception);
        if (!_isolatedTestInstance)
        {
            MessageBox.Show(
                "咕噜今天有点不舒服，暂时没能醒来。" +
                "请重新打开试试吧。",
                "咕噜还没醒来",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        _isExiting = true;
        Shutdown(1);
    }
}

internal sealed class StartupPresentationGate
{
    private bool _activationPending;

    public bool IsRuntimeReady { get; private set; }

    public bool RequestActivation()
    {
        if (IsRuntimeReady)
        {
            return true;
        }

        _activationPending = true;
        return false;
    }

    public bool MarkRuntimeReady()
    {
        IsRuntimeReady = true;
        bool activate = _activationPending;
        _activationPending = false;
        return activate;
    }

    public void MarkInitializationFailed()
    {
        IsRuntimeReady = false;
        _activationPending = false;
    }
}
