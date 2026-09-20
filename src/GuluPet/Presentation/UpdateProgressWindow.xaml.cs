using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GuluPet.Presentation;

public partial class UpdateProgressWindow : Window
{
    internal static readonly TimeSpan LatestVersionCompletionDuration =
        TimeSpan.FromMilliseconds(260);

    internal static readonly HandoffBehavior LatestVersionCompletionHandoff =
        HandoffBehavior.SnapshotAndReplace;

    private bool _allowClose;
    private bool _hasClosed;
    private bool _cancelRequested;
    private bool _canCancel = true;
    private long _latestVersionCompletionGeneration;
    private TaskCompletionSource<bool>? _latestVersionCompletion;
    private bool? _latestVersionCompletionAnimationsEnabledForTest;

    public UpdateProgressWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? CancelRequested;

    internal long LatestVersionCompletionGenerationForTest =>
        _latestVersionCompletionGeneration;

    internal bool LatestVersionProgressHasAnimatedPropertiesForTest =>
        DependencyPropertyHelper.GetValueSource(
            DownloadProgress,
            RangeBase.ValueProperty).IsAnimated;

    internal void SetLatestVersionCompletionAnimationsEnabledForTest(
        bool? enabled) =>
        _latestVersionCompletionAnimationsEnabledForTest = enabled;

    public void SetStage(
        string status,
        string? detail = null,
        double? percent = null,
        bool canCancel = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        double? clampedPercent = percent is null
            ? null
            : Math.Clamp(percent.Value, 0, 100);
        if (!ShouldApplyStage(
                _cancelRequested,
                canCancel,
                DownloadProgress.Value,
                clampedPercent))
        {
            return;
        }

        StatusText.Text = status;
        DetailText.Text = detail ?? string.Empty;
        if (clampedPercent is not null)
        {
            DownloadProgress.Value = clampedPercent.Value;
        }

        _canCancel = canCancel;
        CancelButton.IsEnabled = canCancel && !_cancelRequested;
        CancelButton.Visibility = canCancel
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    internal async Task CompleteLatestVersionProgressAsync(
        CancellationToken cancellationToken = default)
    {
        Dispatcher.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        double startValue = Math.Clamp(
            DownloadProgress.Value,
            DownloadProgress.Minimum,
            DownloadProgress.Maximum);
        StopLatestVersionCompletionMotion();
        long generation = ++_latestVersionCompletionGeneration;

        try
        {
            if (!ShouldAnimateLatestVersionCompletion()
                || DownloadProgress.Value >= DownloadProgress.Maximum)
            {
                CommitLatestVersionProgress();
            }
            else
            {
                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _latestVersionCompletion = completion;
                DoubleAnimation animation =
                    CreateLatestVersionCompletionAnimation(startValue);
                animation.Completed += (_, _) =>
                    CompleteLatestVersionCompletionMotion(
                        generation,
                        completion);
                DownloadProgress.BeginAnimation(
                    RangeBase.ValueProperty,
                    animation,
                    LatestVersionCompletionHandoff);
                await completion.Task.WaitAsync(cancellationToken);
            }

            await Dispatcher.Yield(DispatcherPriority.Render);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _latestVersionCompletionGeneration
                || _hasClosed)
            {
                throw new OperationCanceledException(
                    "Latest-version progress completion was superseded.",
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            if (generation == _latestVersionCompletionGeneration)
            {
                StopLatestVersionCompletionMotion();
            }

            throw;
        }
    }

    internal static DoubleAnimation CreateLatestVersionCompletionAnimation(
        double fromValue) =>
        new()
        {
            From = fromValue,
            To = 100,
            Duration = new Duration(LatestVersionCompletionDuration),
            FillBehavior = FillBehavior.HoldEnd,
        };

    internal static bool ShouldApplyStage(
        bool cancelRequested,
        bool canCancel,
        double currentPercent,
        double? nextPercent) =>
        !(cancelRequested && canCancel)
        && (nextPercent is null || nextPercent >= currentPercent);

    public void CloseForCompletion()
    {
        StopLatestVersionCompletionMotion();
        if (_hasClosed)
        {
            return;
        }

        _allowClose = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopLatestVersionCompletionMotion();
        _hasClosed = true;
        base.OnClosed(e);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) =>
        RequestCancellation();

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        RequestCancellation();
    }

    private void RequestCancellation()
    {
        if (!_canCancel || _cancelRequested)
        {
            return;
        }

        _cancelRequested = true;
        CancelButton.IsEnabled = false;
        StatusText.Text = "正在停下来…";
        DetailText.Text = "已经完成的临时文件会被安全清理。";
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool ShouldAnimateLatestVersionCompletion() =>
        _latestVersionCompletionAnimationsEnabledForTest
        ?? ShouldAnimateLatestVersionCompletion(
            SystemParameters.ClientAreaAnimation,
            SystemParameters.HighContrast);

    internal static bool ShouldAnimateLatestVersionCompletion(
        bool clientAreaAnimationEnabled,
        bool highContrastEnabled) =>
        GuluMotion.ShouldAnimate(
            clientAreaAnimationEnabled,
            highContrastEnabled);

    private void CompleteLatestVersionCompletionMotion(
        long generation,
        TaskCompletionSource<bool> completion)
    {
        if (generation != _latestVersionCompletionGeneration
            || !ReferenceEquals(_latestVersionCompletion, completion))
        {
            return;
        }

        CommitLatestVersionProgress();
        _latestVersionCompletion = null;
        completion.TrySetResult(true);
    }

    private void CommitLatestVersionProgress()
    {
        DownloadProgress.BeginAnimation(RangeBase.ValueProperty, null);
        DownloadProgress.SetValue(
            RangeBase.ValueProperty,
            DownloadProgress.Maximum);
    }

    private void StopLatestVersionCompletionMotion()
    {
        double currentValue = DownloadProgress.Value;
        ++_latestVersionCompletionGeneration;
        TaskCompletionSource<bool>? completion = _latestVersionCompletion;
        _latestVersionCompletion = null;

        DownloadProgress.BeginAnimation(RangeBase.ValueProperty, null);
        DownloadProgress.SetValue(
            RangeBase.ValueProperty,
            Math.Clamp(
                currentValue,
                DownloadProgress.Minimum,
                DownloadProgress.Maximum));
        completion?.TrySetCanceled();
    }
}
