using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GuluPet.Platform;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace GuluPet.Presentation;

public partial class MemoryPlaybackWindow : Window, IMemoryPlaybackSession
{
    internal const double PreferredWidth = 528;
    internal const double PreferredHeight = 320;
    private static readonly TimeSpan PlayerWarmupTimeout =
        TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PlayerWarmupPosition =
        TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan PlayerWarmupObservationLimit =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FollowingSegmentWarmupDelay =
        TimeSpan.FromMilliseconds(250);

    private readonly Window _owner;
    private readonly IReadOnlyList<Uri> _videoSources;
    private readonly TaskCompletionSource<MemoryPlaybackResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration _cancellationRegistration;
    private readonly CancellationTokenSource _preparationLifetime = new();
    private readonly HashSet<MediaElement> _preparingPlayers = [];
    private IReadOnlyList<MemoryVideoDimensions>? _videoDimensions;
    private MediaElement _activeVideo;
    private MediaElement _standbyVideo;
    private Task<MemoryPlaybackPreparationResult>? _preparationTask;
    private Task<MemoryPlaybackPreparationResult>? _standbyPreparationTask;
    private MemoryPlaybackPhase _phase = MemoryPlaybackPhase.Loading;
    private int _videoIndex;
    private int _activePreparedIndex = -1;
    private int _standbyPreparedIndex = -1;
    private int _standbyPreparingIndex = -1;
    private double _loadingFraction;
    private bool _naturalCompletionRaised;
    private bool _playbackFailed;
    private bool _retryRequestRaised;
    private bool _playbackTransitionInProgress;
    private bool _repositionQueued;
    private Exception? _playbackError;
    private MemoryWindowSide? _placementSide;
    private double _desiredWidth = PreferredWidth;
    private double _desiredHeight = PreferredHeight;
    private int _mediaOpenCount;
    private int _mediaCloseCount;
    private int _mediaSwapCount;
    private long _endedEntranceGeneration;
    private bool? _endedEntranceAnimationsEnabledForTest;

    internal MemoryPlaybackWindow(
        Window owner,
        IReadOnlyList<Uri> videoSources,
        string title,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(videoSources);
        if (videoSources.Count == 0
            || videoSources.Any(static source => source is null))
        {
            throw new ArgumentException(
                "A memory presentation requires at least one video source.",
                nameof(videoSources));
        }

        InitializeComponent();
        _activeVideo = MemoryVideo;
        _standbyVideo = StandbyVideo;
        _owner = owner;
        _videoSources = videoSources.ToArray();
        Owner = owner;
        Topmost = owner.Topmost;
        if (!string.IsNullOrWhiteSpace(title))
        {
            System.Windows.Automation.AutomationProperties.SetHelpText(
                this,
                title.Trim());
        }

        owner.LocationChanged += OnOwnerGeometryChanged;
        owner.SizeChanged += OnOwnerSizeChanged;
        owner.StateChanged += OnOwnerStateChanged;
        owner.IsVisibleChanged += OnOwnerVisibilityChanged;
        owner.Closed += OnOwnerClosed;
        SizeChanged += OnPlaybackWindowSizeChanged;
        Closing += OnPlaybackWindowClosing;

        if (cancellationToken.CanBeCanceled)
        {
            _cancellationRegistration = cancellationToken.Register(
                () => Dispatcher.BeginInvoke(
                    DispatcherPriority.Send,
                    Close));
        }
    }

    public event EventHandler? PhaseChanged;

    public event EventHandler? NaturalPlaybackCompleted;

    public event EventHandler? RetryRequested;

    public MemoryPlaybackPhase Phase => _phase;

    public Task<MemoryPlaybackResult> Completion => _completion.Task;

    internal int CurrentVideoIndexForTest => _videoIndex;

    internal bool NaturalCompletionRaisedForTest =>
        _naturalCompletionRaised;

    internal double DesiredWidthForTest => _desiredWidth;

    internal double DesiredHeightForTest => _desiredHeight;

    internal bool IsInitialSegmentPreparedForTest =>
        _activePreparedIndex == 0
        && _activeVideo.Source == _videoSources[0];

    internal Uri? ActiveVideoSourceForTest => _activeVideo.Source;

    internal Uri? StandbyVideoSourceForTest => _standbyVideo.Source;

    internal int ActivePreparedIndexForTest => _activePreparedIndex;

    internal int StandbyPreparedIndexForTest => _standbyPreparedIndex;

    internal int MediaOpenCountForTest => _mediaOpenCount;

    internal int MediaCloseCountForTest => _mediaCloseCount;

    internal int MediaSwapCountForTest => _mediaSwapCount;

    internal long EndedEntranceGenerationForTest =>
        _endedEntranceGeneration;

    internal bool EndedEntranceHasAnimatedPropertiesForTest =>
        EndedDimmingLayer.HasAnimatedProperties
        || PrimaryButton.HasAnimatedProperties
        || PrimaryButtonScale.HasAnimatedProperties;

    internal void SetEndedEntranceAnimationsEnabledForTest(bool? enabled) =>
        _endedEntranceAnimationsEnabledForTest = enabled;

    internal static bool IsPlayerPrerollSatisfiedForTest(
        TimeSpan position,
        TimeSpan elapsed) =>
        IsPlayerPrerollSatisfied(position, elapsed);

    void IMemoryPlaybackSession.ConfigureVideoDimensions(
        IReadOnlyList<MemoryVideoDimensions> dimensions) =>
        ConfigureVideoDimensions(dimensions);

    Task<MemoryPlaybackPreparationResult>
        IMemoryPlaybackSession.PrepareAsync(
            IProgress<MemoryLoadingProgress>? progress,
            CancellationToken cancellationToken) =>
        PrepareAsync(progress, cancellationToken);

    internal void ConfigureVideoDimensions(
        IReadOnlyList<MemoryVideoDimensions> dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ConfigureVideoDimensions(dimensions));
            return;
        }

        if (dimensions.Count != _videoSources.Count)
        {
            throw new ArgumentException(
                "Video dimensions must match the playback source count.",
                nameof(dimensions));
        }

        _videoDimensions = dimensions.ToArray();
        ApplyConfiguredVideoDimensions(_videoIndex);
    }

    internal Task<MemoryPlaybackPreparationResult> PrepareAsync(
        IProgress<MemoryLoadingProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.InvokeAsync(
                    () => PrepareAsync(progress, cancellationToken),
                    DispatcherPriority.Send)
                .Task
                .Unwrap();
        }

        if (_preparationTask is not null)
        {
            return _preparationTask;
        }

        if (_phase != MemoryPlaybackPhase.Loading)
        {
            return Task.FromResult(
                new MemoryPlaybackPreparationResult(
                    false,
                    "播放器已经结束准备，无法继续加载。",
                    new InvalidOperationException(
                        "Memory playback can only be prepared while loading.")));
        }

        _preparationTask = PrepareInitialPlaybackAsync(
            progress,
            cancellationToken);
        return _preparationTask;
    }

    private async Task<MemoryPlaybackPreparationResult>
        PrepareInitialPlaybackAsync(
            IProgress<MemoryLoadingProgress>? progress,
            CancellationToken cancellationToken)
    {
        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _preparationLifetime.Token);
        CancellationToken linkedToken = linkedCancellation.Token;
        try
        {
            ReportPreparationProgress(
                progress,
                0d,
                "正在准备播放器");
            MemoryVideoDimensions firstDimensions =
                await PreparePlayerAsync(
                    _activeVideo,
                    _videoSources[0],
                    linkedToken);
            linkedToken.ThrowIfCancellationRequested();
            _activePreparedIndex = 0;
            ApplyVideoDimensions(firstDimensions);
            ReportPreparationProgress(
                progress,
                _videoSources.Count > 1 ? 0.72d : 1d,
                "第一段视频准备完成");

            if (_videoSources.Count > 1)
            {
                MemoryPlaybackPreparationResult standby =
                    await EnsureStandbyPreparedAsync(1, linkedToken);
                if (!standby.Succeeded)
                {
                    ClosePreparedPlayers();
                    return standby;
                }

                ReportPreparationProgress(
                    progress,
                    1d,
                    "所有视频准备完成");
            }

            return MemoryPlaybackPreparationResult.Success;
        }
        catch (OperationCanceledException)
            when (linkedToken.IsCancellationRequested)
        {
            ClosePreparedPlayers();
            throw;
        }
        catch (Exception error)
        {
            ClosePreparedPlayers();
            return new MemoryPlaybackPreparationResult(
                false,
                error is TimeoutException
                    ? "播放器准备超时，请再试一次。"
                    : "播放器没能准备好这段视频，请再试一次。",
                error);
        }
    }

    private async Task<MemoryVideoDimensions> PreparePlayerAsync(
        MediaElement player,
        Uri source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (player.Source == source)
        {
            player.Pause();
            player.Position = TimeSpan.Zero;
            await Dispatcher.Yield(DispatcherPriority.Render);
            return GetNaturalDimensions(player);
        }

        var opened = new TaskCompletionSource<MemoryVideoDimensions>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? openedHandler = null;
        EventHandler<ExceptionRoutedEventArgs>? failedHandler = null;
        openedHandler = (_, _) =>
        {
            if (player.NaturalVideoWidth > 0
                && player.NaturalVideoHeight > 0)
            {
                opened.TrySetResult(GetNaturalDimensions(player));
            }
        };
        failedHandler = (_, e) => failed.TrySetResult(
            e.ErrorException
            ?? new InvalidOperationException(
                "The WPF media pipeline failed without an exception."));

        player.MediaOpened += openedHandler;
        player.MediaFailed += failedHandler;
        _preparingPlayers.Add(player);
        using var warmupCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        warmupCancellation.CancelAfter(PlayerWarmupTimeout);
        try
        {
            ClosePlayer(player);
            player.Source = source;
            _mediaOpenCount++;
            player.Position = TimeSpan.Zero;
            player.Play();

            Task openedOrFailed = await Task.WhenAny(
                    opened.Task,
                    failed.Task)
                .WaitAsync(warmupCancellation.Token);
            if (ReferenceEquals(openedOrFailed, failed.Task))
            {
                throw await failed.Task;
            }

            MemoryVideoDimensions dimensions = await opened.Task;
            Stopwatch warmupClock = Stopwatch.StartNew();
            while (!IsPlayerPrerollSatisfied(
                       player.Position,
                       warmupClock.Elapsed))
            {
                Task delay = Task.Delay(
                    TimeSpan.FromMilliseconds(16),
                    warmupCancellation.Token);
                Task tickOrFailure = await Task.WhenAny(
                    delay,
                    failed.Task);
                if (ReferenceEquals(tickOrFailure, failed.Task))
                {
                    throw await failed.Task;
                }

                await delay;
            }

            player.Pause();
            player.Position = TimeSpan.Zero;
            var renderedFrames = 0;
            while (true)
            {
                await Dispatcher.Yield(DispatcherPriority.Render);
                warmupCancellation.Token.ThrowIfCancellationRequested();
                if (failed.Task.IsCompleted)
                {
                    throw await failed.Task;
                }

                renderedFrames++;
                if (renderedFrames >= 2
                    && player.Position <= TimeSpan.FromMilliseconds(40))
                {
                    break;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(16),
                    warmupCancellation.Token);
            }

            return dimensions;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested
                  && warmupCancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The WPF media pipeline did not finish preroll within " +
                $"{PlayerWarmupTimeout.TotalSeconds:0} seconds.");
        }
        finally
        {
            _preparingPlayers.Remove(player);
            player.MediaOpened -= openedHandler;
            player.MediaFailed -= failedHandler;
        }
    }

    private static MemoryVideoDimensions GetNaturalDimensions(
        MediaElement player)
    {
        if (player.NaturalVideoWidth < 1
            || player.NaturalVideoHeight < 1)
        {
            throw new InvalidOperationException(
                "The WPF media pipeline opened a video without dimensions.");
        }

        return new MemoryVideoDimensions(
            player.NaturalVideoWidth,
            player.NaturalVideoHeight);
    }

    private static bool IsPlayerPrerollSatisfied(
        TimeSpan position,
        TimeSpan elapsed) =>
        position >= PlayerWarmupPosition
        || elapsed >= PlayerWarmupObservationLimit;

    private void ReportPreparationProgress(
        IProgress<MemoryLoadingProgress>? progress,
        double fraction,
        string statusText)
    {
        var update = new MemoryLoadingProgress(fraction, statusText);
        if (progress is null)
        {
            ReportLoadingProgress(update);
        }
        else
        {
            progress.Report(update);
        }
    }

    private Task<MemoryPlaybackPreparationResult>
        EnsureStandbyPreparedAsync(
            int videoIndex,
            CancellationToken cancellationToken)
    {
        if (videoIndex < 0 || videoIndex >= _videoSources.Count)
        {
            return Task.FromResult(
                new MemoryPlaybackPreparationResult(
                    false,
                    "找不到下一段视频，请重新打开这段回忆。",
                    new ArgumentOutOfRangeException(nameof(videoIndex))));
        }

        if (_standbyPreparedIndex == videoIndex
            && _standbyVideo.Source == _videoSources[videoIndex])
        {
            return Task.FromResult(
                MemoryPlaybackPreparationResult.Success);
        }

        if (_standbyPreparationTask is not null
            && !_standbyPreparationTask.IsCompleted
            && _standbyPreparingIndex == videoIndex)
        {
            return _standbyPreparationTask;
        }

        if (_standbyPreparationTask is not null
            && !_standbyPreparationTask.IsCompleted)
        {
            return AwaitPreviousStandbyThenPrepareAsync(
                videoIndex,
                cancellationToken);
        }

        _standbyPreparingIndex = videoIndex;
        MediaElement player = _standbyVideo;
        _standbyPreparationTask = PrepareStandbyAsync(
            player,
            videoIndex,
            cancellationToken);
        return _standbyPreparationTask;
    }

    private async Task<MemoryPlaybackPreparationResult>
        AwaitPreviousStandbyThenPrepareAsync(
            int videoIndex,
            CancellationToken cancellationToken)
    {
        Task<MemoryPlaybackPreparationResult>? previous =
            _standbyPreparationTask;
        if (previous is not null)
        {
            _ = await previous;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await EnsureStandbyPreparedAsync(
            videoIndex,
            cancellationToken);
    }

    private async Task<MemoryPlaybackPreparationResult> PrepareStandbyAsync(
        MediaElement player,
        int videoIndex,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await PreparePlayerAsync(
                player,
                _videoSources[videoIndex],
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(player, _standbyVideo))
            {
                return new MemoryPlaybackPreparationResult(
                    false,
                    "播放器没能切换到下一段视频，请再试一次。",
                    new InvalidOperationException(
                        "A prepared player is no longer the standby slot."));
            }

            _standbyPreparedIndex = videoIndex;
            return MemoryPlaybackPreparationResult.Success;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            ClosePlayer(player);
            if (ReferenceEquals(player, _standbyVideo))
            {
                _standbyPreparedIndex = -1;
            }

            return new MemoryPlaybackPreparationResult(
                false,
                error is TimeoutException
                    ? "下一段视频准备超时，请再试一次。"
                    : "下一段视频没能准备好，请再试一次。",
                error);
        }
    }

    private void BeginPreparingFollowingSegment(int activeIndex)
    {
        if (_videoSources.Count < 2)
        {
            return;
        }

        int followingIndex = activeIndex + 1 < _videoSources.Count
            ? activeIndex + 1
            : 0;
        _ = ObserveStandbyPreparationAsync(
            activeIndex,
            followingIndex);
    }

    private async Task ObserveStandbyPreparationAsync(
        int activeIndex,
        int videoIndex)
    {
        try
        {
            await Task.Delay(
                FollowingSegmentWarmupDelay,
                _preparationLifetime.Token);
            if (_phase != MemoryPlaybackPhase.Playing
                || _videoIndex != activeIndex)
            {
                return;
            }

            _ = await EnsureStandbyPreparedAsync(
                videoIndex,
                _preparationLifetime.Token);
        }
        catch (OperationCanceledException)
            when (_preparationLifetime.IsCancellationRequested)
        {
        }
    }

    private void SwapPreparedStandbyToActive(int videoIndex)
    {
        if (_standbyPreparedIndex != videoIndex
            || _standbyVideo.Source != _videoSources[videoIndex])
        {
            throw new InvalidOperationException(
                "The requested standby memory segment is not prepared.");
        }

        _activeVideo.Pause();
        MediaElement previousActive = _activeVideo;
        _activeVideo = _standbyVideo;
        _standbyVideo = previousActive;
        _activePreparedIndex = videoIndex;
        _standbyPreparedIndex = -1;
        _standbyPreparingIndex = -1;
        _standbyPreparationTask = null;
        System.Windows.Controls.Panel.SetZIndex(_activeVideo, 1);
        _activeVideo.Opacity = 1d;
        System.Windows.Controls.Panel.SetZIndex(_standbyVideo, 0);
        _standbyVideo.Opacity = 0d;
        _mediaSwapCount++;
    }

    private void ClosePreparedPlayers()
    {
        ClosePlayer(MemoryVideo);
        ClosePlayer(StandbyVideo);
        _activePreparedIndex = -1;
        _standbyPreparedIndex = -1;
        _standbyPreparingIndex = -1;
    }

    private void ClosePlayer(MediaElement player)
    {
        if (player.Source is null)
        {
            return;
        }

        try
        {
            player.Stop();
            player.Close();
        }
        finally
        {
            player.Source = null;
            _mediaCloseCount++;
        }
    }

    public void ReportLoadingProgress(MemoryLoadingProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                () => ReportLoadingProgress(progress));
            return;
        }

        if (_phase != MemoryPlaybackPhase.Loading)
        {
            return;
        }

        double fraction = Math.Clamp(progress.Fraction, 0d, 1d);
        if (fraction + 0.0001d < _loadingFraction)
        {
            return;
        }

        _loadingFraction = fraction;
        double percentage = Math.Round(_loadingFraction * 100d);
        LoadingProgressBar.Value = percentage;
        LoadingPercentText.Text = $"{percentage:0}%";
        LoadingStatusText.Text = string.IsNullOrWhiteSpace(progress.StatusText)
            ? "检查视频"
            : progress.StatusText.Trim();
    }

    public void MarkReady()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(MarkReady);
            return;
        }

        if (_phase != MemoryPlaybackPhase.Loading)
        {
            return;
        }

        if (_activePreparedIndex != 0
            || _activeVideo.Source != _videoSources[0])
        {
            throw new InvalidOperationException(
                "Memory playback cannot become ready before its formal " +
                "player has completed preroll.");
        }

        ReportLoadingProgress(
            new MemoryLoadingProgress(1d, "可以播放"));
        SetPhase(MemoryPlaybackPhase.Ready);
    }

    public void MarkLoadingFailed(
        string userMessage,
        Exception? error = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                () => MarkLoadingFailed(userMessage, error));
            return;
        }

        if (_phase == MemoryPlaybackPhase.Closed)
        {
            return;
        }

        _playbackError = error;
        _playbackFailed = true;
        _preparationLifetime.Cancel();
        ClosePreparedPlayers();
        FailureText.Text = string.IsNullOrWhiteSpace(userMessage)
            ? "播放器没能打开这段视频，请再试一次。"
            : userMessage.Trim();
        SetPhase(MemoryPlaybackPhase.Failed);
    }

    public void Dispose()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Dispose);
            return;
        }

        if (IsVisible)
        {
            Close();
        }
    }

    internal void QueueReposition()
    {
        if (_repositionQueued || !IsVisible)
        {
            return;
        }

        _repositionQueued = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            () =>
            {
                _repositionQueued = false;
                RepositionNow();
            });
    }

    private void RepositionNow()
    {
        if (!IsVisible)
        {
            return;
        }

        Topmost = _owner.Topmost;
        UpdateLayout();
        if (WindowPlacementService.PositionMemoryWindowNear(
            this,
            _owner,
            _desiredWidth,
            _desiredHeight,
            _placementSide,
            out MemoryWindowSide placedSide))
        {
            _placementSide = placedSide;
        }
    }

    internal Task CompleteCurrentSegmentForTestAsync() =>
        CompleteActiveSegmentAsync();

    internal void CompleteCurrentSegmentForTest()
    {
        Task completion = CompleteCurrentSegmentForTestAsync();
        if (completion.IsCompleted)
        {
            completion.GetAwaiter().GetResult();
        }
    }

    private async void OnPrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (_playbackTransitionInProgress)
        {
            return;
        }

        _playbackTransitionInProgress = true;
        PrimaryButton.IsEnabled = false;
        try
        {
            if (_phase is MemoryPlaybackPhase.Ready
                or MemoryPlaybackPhase.Ended)
            {
                await StartSequenceFromBeginningAsync();
            }
            else if (_phase == MemoryPlaybackPhase.Failed)
            {
                RequestRetry();
            }
        }
        catch (OperationCanceledException)
            when (_preparationLifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            MarkLoadingFailed(
                "播放器没能开始播放，请再试一次。",
                error);
        }
        finally
        {
            _playbackTransitionInProgress = false;
            if (_phase is MemoryPlaybackPhase.Ready
                or MemoryPlaybackPhase.Ended)
            {
                PrimaryButton.IsEnabled = true;
            }
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void RequestRetry()
    {
        if (_retryRequestRaised || _phase != MemoryPlaybackPhase.Failed)
        {
            return;
        }

        _retryRequestRaised = true;
        PrimaryButton.IsEnabled = false;
        try
        {
            RetryRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Close();
        }
    }

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter
                 && PrimaryButton.IsEnabled
                 && PrimaryButton.Visibility == Visibility.Visible
                 && (Keyboard.FocusedElement
                         is not System.Windows.Controls.Button focusedButton
                     || ReferenceEquals(focusedButton, PrimaryButton)))
        {
            e.Handled = true;
            OnPrimaryClicked(PrimaryButton, e);
        }
    }

    private async Task StartSequenceFromBeginningAsync()
    {
        if (_videoSources.Count == 1 && _activePreparedIndex == 0)
        {
            PlayActiveSegment(0);
            return;
        }

        if (_activePreparedIndex == 0)
        {
            PlayActiveSegment(0);
            return;
        }

        MemoryPlaybackPreparationResult prepared =
            await EnsureStandbyPreparedAsync(
                0,
                _preparationLifetime.Token);
        if (!prepared.Succeeded)
        {
            MarkLoadingFailed(
                prepared.UserMessage
                ?? "第一段视频还没有准备好，请再试一次。",
                prepared.Error);
            return;
        }

        SwapPreparedStandbyToActive(0);
        PlayActiveSegment(0);
    }

    private void PlayActiveSegment(int videoIndex)
    {
        if (videoIndex < 0 || videoIndex >= _videoSources.Count
            || _activePreparedIndex != videoIndex
            || _activeVideo.Source != _videoSources[videoIndex])
        {
            MarkLoadingFailed(
                "这段回忆的播放画面还没有准备好。",
                new InvalidOperationException(
                    "Playback attempted to use an unprepared media slot."));
            return;
        }

        try
        {
            _videoIndex = videoIndex;
            SetPhase(MemoryPlaybackPhase.Playing);
            _activeVideo.Position = TimeSpan.Zero;
            _activeVideo.Play();
            ApplyConfiguredVideoDimensions(_videoIndex);
            BeginPreparingFollowingSegment(videoIndex);
        }
        catch (Exception error)
        {
            MarkLoadingFailed(
                "播放器没能播放这段视频，请再试一次。",
                error);
        }
    }

    private async void OnMemoryVideoEnded(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement player
            || !ReferenceEquals(player, _activeVideo))
        {
            return;
        }

        try
        {
            await CompleteActiveSegmentAsync();
        }
        catch (OperationCanceledException)
            when (_preparationLifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            MarkLoadingFailed(
                "播放器没能继续播放下一段，请再试一次。",
                error);
        }
    }

    private async Task CompleteActiveSegmentAsync()
    {
        if (_phase != MemoryPlaybackPhase.Playing)
        {
            return;
        }

        if (_videoIndex + 1 < _videoSources.Count)
        {
            int nextIndex = _videoIndex + 1;
            MemoryPlaybackPreparationResult prepared =
                await EnsureStandbyPreparedAsync(
                    nextIndex,
                    _preparationLifetime.Token);
            if (!prepared.Succeeded)
            {
                MarkLoadingFailed(
                    prepared.UserMessage
                    ?? "下一段视频还没有准备好，请再试一次。",
                    prepared.Error);
                return;
            }

            if (_phase != MemoryPlaybackPhase.Playing)
            {
                return;
            }

            SwapPreparedStandbyToActive(nextIndex);
            PlayActiveSegment(nextIndex);
            return;
        }

        _activeVideo.Pause();
        SetPhase(MemoryPlaybackPhase.Ended);
        if (_videoSources.Count > 1)
        {
            _ = ObserveReplayPreparationAsync();
        }

        if (!_naturalCompletionRaised)
        {
            _naturalCompletionRaised = true;
            NaturalPlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ObserveReplayPreparationAsync()
    {
        try
        {
            _ = await EnsureStandbyPreparedAsync(
                0,
                _preparationLifetime.Token);
        }
        catch (OperationCanceledException)
            when (_preparationLifetime.IsCancellationRequested)
        {
        }
    }

    private void OnMemoryVideoOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement player
            || !ReferenceEquals(player, _activeVideo)
            || player.NaturalVideoWidth < 1
            || player.NaturalVideoHeight < 1)
        {
            return;
        }

        ApplyVideoDimensions(
            new MemoryVideoDimensions(
                player.NaturalVideoWidth,
                player.NaturalVideoHeight));
    }

    private void OnMemoryVideoFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        if (sender is MediaElement player
            && _preparingPlayers.Contains(player))
        {
            return;
        }

        if (sender is not MediaElement failedPlayer
            || !ReferenceEquals(failedPlayer, _activeVideo))
        {
            return;
        }

        MarkLoadingFailed(
            "播放器没能播放这段视频，请再试一次。",
            e.ErrorException);
    }

    private void SetPhase(MemoryPlaybackPhase phase)
    {
        if (_phase == phase || _phase == MemoryPlaybackPhase.Closed)
        {
            return;
        }

        _phase = phase;
        if (phase != MemoryPlaybackPhase.Ended)
        {
            ResetEndedEntrance();
        }

        switch (phase)
        {
            case MemoryPlaybackPhase.Loading:
                break;
            case MemoryPlaybackPhase.Ready:
                LoadingArtwork.Visibility = Visibility.Visible;
                VideoLayer.Visibility = Visibility.Visible;
                FailureOverlay.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Collapsed;
                PlaybackActionOverlay.Visibility = Visibility.Visible;
                EndedDimmingLayer.Visibility = Visibility.Collapsed;
                ConfigurePlaybackButton(
                    "\uE768",
                    "播放",
                    "播放回忆");
                break;
            case MemoryPlaybackPhase.Playing:
                LoadingArtwork.Visibility = Visibility.Collapsed;
                VideoLayer.Visibility = Visibility.Visible;
                FailureOverlay.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Collapsed;
                PlaybackActionOverlay.Visibility = Visibility.Collapsed;
                EndedDimmingLayer.Visibility = Visibility.Collapsed;
                PrimaryButton.Visibility = Visibility.Collapsed;
                break;
            case MemoryPlaybackPhase.Ended:
                LoadingArtwork.Visibility = Visibility.Collapsed;
                VideoLayer.Visibility = Visibility.Visible;
                FailureOverlay.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Collapsed;
                PlaybackActionOverlay.Visibility = Visibility.Visible;
                EndedDimmingLayer.Visibility = Visibility.Visible;
                ConfigurePlaybackButton(
                    "\uE72C",
                    "重播",
                    "重播回忆");
                BeginEndedEntrance();
                break;
            case MemoryPlaybackPhase.Failed:
                VideoLayer.Visibility = Visibility.Collapsed;
                LoadingArtwork.Visibility = Visibility.Visible;
                FailureOverlay.Visibility = Visibility.Visible;
                ProgressPanel.Visibility = Visibility.Collapsed;
                PlaybackActionOverlay.Visibility = Visibility.Visible;
                EndedDimmingLayer.Visibility = Visibility.Collapsed;
                ConfigureRetryButton();
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    () =>
                    {
                        if (_phase == MemoryPlaybackPhase.Failed
                            && PrimaryButton.IsVisible
                            && PrimaryButton.IsEnabled)
                        {
                            _ = PrimaryButton.Focus();
                            _ = Keyboard.Focus(PrimaryButton);
                        }
                    });
                break;
            case MemoryPlaybackPhase.Closed:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(phase));
        }

        PhaseChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BeginEndedEntrance()
    {
        long generation = ++_endedEntranceGeneration;
        RemoveEndedEntranceMotionClocks();
        if (!ShouldAnimateEndedEntrance())
        {
            CommitEndedEntrance(generation);
            return;
        }

        EndedDimmingLayer.Opacity = 0;
        PrimaryButton.Opacity = 0;
        PrimaryButtonScale.ScaleX = 0.96;
        PrimaryButtonScale.ScaleY = 0.96;

        var dimmingAnimation = GuluMotion.SplineTo(
            1,
            GuluMotion.FadeEntrance);
        dimmingAnimation.Completed += (_, _) =>
            CommitEndedDimming(generation);

        var buttonOpacityAnimation = GuluMotion.SplineTo(
            1,
            GuluMotion.MoveEntrance);
        var buttonScaleXAnimation = GuluMotion.SplineTo(
            1,
            GuluMotion.MoveEntrance);
        var buttonScaleYAnimation = GuluMotion.SplineTo(
            1,
            GuluMotion.MoveEntrance);
        buttonScaleYAnimation.Completed += (_, _) =>
            CommitEndedButton(generation);

        EndedDimmingLayer.BeginAnimation(
            OpacityProperty,
            dimmingAnimation,
            HandoffBehavior.SnapshotAndReplace);
        PrimaryButton.BeginAnimation(
            OpacityProperty,
            buttonOpacityAnimation,
            HandoffBehavior.SnapshotAndReplace);
        PrimaryButtonScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            buttonScaleXAnimation,
            HandoffBehavior.SnapshotAndReplace);
        PrimaryButtonScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            buttonScaleYAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private bool ShouldAnimateEndedEntrance() =>
        _endedEntranceAnimationsEnabledForTest
        ?? GuluMotion.ShouldAnimate(
            SystemParameters.ClientAreaAnimation,
            SystemParameters.HighContrast);

    private void CommitEndedDimming(long generation)
    {
        if (!IsCurrentEndedEntrance(generation))
        {
            return;
        }

        EndedDimmingLayer.BeginAnimation(OpacityProperty, null);
        EndedDimmingLayer.Opacity = 1;
    }

    private void CommitEndedButton(long generation)
    {
        if (!IsCurrentEndedEntrance(generation))
        {
            return;
        }

        PrimaryButton.BeginAnimation(OpacityProperty, null);
        PrimaryButtonScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PrimaryButtonScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PrimaryButton.Opacity = 1;
        PrimaryButtonScale.ScaleX = 1;
        PrimaryButtonScale.ScaleY = 1;
    }

    private void CommitEndedEntrance(long generation)
    {
        if (!IsCurrentEndedEntrance(generation))
        {
            return;
        }

        RemoveEndedEntranceMotionClocks();
        EndedDimmingLayer.Opacity = 1;
        PrimaryButton.Opacity = 1;
        PrimaryButtonScale.ScaleX = 1;
        PrimaryButtonScale.ScaleY = 1;
    }

    private bool IsCurrentEndedEntrance(long generation) =>
        generation == _endedEntranceGeneration
        && _phase == MemoryPlaybackPhase.Ended;

    private void ResetEndedEntrance()
    {
        _endedEntranceGeneration++;
        RemoveEndedEntranceMotionClocks();
        EndedDimmingLayer.Opacity = 1;
        PrimaryButton.Opacity = 1;
        PrimaryButtonScale.ScaleX = 1;
        PrimaryButtonScale.ScaleY = 1;
    }

    private void RemoveEndedEntranceMotionClocks()
    {
        EndedDimmingLayer.BeginAnimation(OpacityProperty, null);
        PrimaryButton.BeginAnimation(OpacityProperty, null);
        PrimaryButtonScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PrimaryButtonScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    private void ConfigurePlaybackButton(
        string content,
        string toolTip,
        string automationName)
    {
        PrimaryButton.Width = 56;
        PrimaryButton.Height = 56;
        PrimaryButton.Margin = new Thickness(0);
        PrimaryButton.Padding = new Thickness(0);
        PrimaryButton.VerticalAlignment = VerticalAlignment.Center;
        PrimaryButton.Style = (Style)FindResource(
            "MemoryPlaybackActionButtonStyle");
        PrimaryButton.FontFamily = new System.Windows.Media.FontFamily(
            "Segoe MDL2 Assets");
        PrimaryButton.FontSize = 24;
        PrimaryButton.FontWeight = FontWeights.Normal;
        PrimaryButton.IsDefault = false;
        PrimaryButton.Visibility = Visibility.Visible;
        PrimaryButton.IsEnabled = true;
        PrimaryButton.Content = content;
        PrimaryButton.ToolTip = toolTip;
        System.Windows.Automation.AutomationProperties.SetName(
            PrimaryButton,
            automationName);
    }

    private void ConfigureRetryButton()
    {
        PrimaryButton.Width = 112;
        PrimaryButton.Height = 38;
        PrimaryButton.Margin = new Thickness(0, 96, 0, 0);
        PrimaryButton.Padding = new Thickness(16, 0, 16, 0);
        PrimaryButton.VerticalAlignment = VerticalAlignment.Center;
        PrimaryButton.Style = (Style)FindResource("MemoryRetryButtonStyle");
        PrimaryButton.FontFamily = new System.Windows.Media.FontFamily(
            "Microsoft YaHei UI");
        PrimaryButton.FontSize = 13;
        PrimaryButton.FontWeight = FontWeights.SemiBold;
        PrimaryButton.IsDefault = true;
        PrimaryButton.Visibility = Visibility.Visible;
        PrimaryButton.IsEnabled = true;
        PrimaryButton.Content = "再试一次";
        PrimaryButton.ToolTip = "重新加载并播放这段回忆";
        System.Windows.Automation.AutomationProperties.SetName(
            PrimaryButton,
            "再试一次");
    }

    private void ApplyConfiguredVideoDimensions(int videoIndex)
    {
        if (_videoDimensions is null
            || videoIndex < 0
            || videoIndex >= _videoDimensions.Count)
        {
            return;
        }

        ApplyVideoDimensions(_videoDimensions[videoIndex]);
    }

    private void ApplyVideoDimensions(MemoryVideoDimensions dimensions)
    {
        MemoryPlaybackWindowSize desired =
            MemoryPlaybackWindowSizing.Calculate(
                dimensions.Width,
                dimensions.Height);
        if (Math.Abs(_desiredWidth - desired.WidthDips) < 0.01
            && Math.Abs(_desiredHeight - desired.HeightDips) < 0.01)
        {
            return;
        }

        _desiredWidth = desired.WidthDips;
        _desiredHeight = desired.HeightDips;
        RepositionNow();
    }

    private void OnPlaybackWindowClosing(
        object? sender,
        CancelEventArgs e)
    {
        if (_phase == MemoryPlaybackPhase.Closed)
        {
            return;
        }

        MemoryPlaybackOutcome outcome = _naturalCompletionRaised
            ? MemoryPlaybackOutcome.Ended
            : _playbackFailed
                ? MemoryPlaybackOutcome.Failed
                : MemoryPlaybackOutcome.Cancelled;
        ResetEndedEntrance();
        _phase = MemoryPlaybackPhase.Closed;
        _preparationLifetime.Cancel();
        ClosePreparedPlayers();
        DetachOwner();
        _cancellationRegistration.Dispose();
        _preparationLifetime.Dispose();
        _completion.TrySetResult(
            new MemoryPlaybackResult(outcome, _playbackError));
        PhaseChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnOwnerGeometryChanged(object? sender, EventArgs e) =>
        QueueReposition();

    private void OnOwnerSizeChanged(object sender, SizeChangedEventArgs e) =>
        QueueReposition();

    private void OnOwnerStateChanged(object? sender, EventArgs e) =>
        QueueReposition();

    private void OnPlaybackWindowSizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        QueueReposition();

    private void OnOwnerVisibilityChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            QueueReposition();
        }
    }

    private void OnOwnerClosed(object? sender, EventArgs e) => Close();

    private void DetachOwner()
    {
        _owner.LocationChanged -= OnOwnerGeometryChanged;
        _owner.SizeChanged -= OnOwnerSizeChanged;
        _owner.StateChanged -= OnOwnerStateChanged;
        _owner.IsVisibleChanged -= OnOwnerVisibilityChanged;
        _owner.Closed -= OnOwnerClosed;
        SizeChanged -= OnPlaybackWindowSizeChanged;
        Closing -= OnPlaybackWindowClosing;
    }
}
