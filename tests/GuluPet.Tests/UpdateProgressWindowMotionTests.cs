using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class UpdateProgressWindowMotionTests
{
    public static void RunAll()
    {
        Run(
            nameof(CompletionAnimationIsFastAndLinear),
            CompletionAnimationIsFastAndLinear);
        Run(
            nameof(CompletionSettlesAtHundredAndPreservesTextBinding),
            CompletionSettlesAtHundredAndPreservesTextBinding);
        Run(
            nameof(CompletionWaitsThroughASettledRenderBeforeReturning),
            CompletionWaitsThroughASettledRenderBeforeReturning);
        Run(
            nameof(DisabledMotionCompletesWithoutAClock),
            DisabledMotionCompletesWithoutAClock);
        Run(
            nameof(CloseDuringRenderYieldCancelsCompletion),
            CloseDuringRenderYieldCancelsCompletion);
        Run(
            nameof(CancellationDuringRenderYieldCancelsCompletion),
            CancellationDuringRenderYieldCancelsCompletion);
        Run(
            nameof(RetriggerDuringRenderYieldRejectsTheOldCaller),
            RetriggerDuringRenderYieldRejectsTheOldCaller);
        Run(
            nameof(CloseMidMotionCancelsAndClearsTheClock),
            CloseMidMotionCancelsAndClearsTheClock);
        Run(
            nameof(ApplicationCancellationStopsCompletionMotion),
            ApplicationCancellationStopsCompletionMotion);
        Run(
            nameof(RetriggerStartsFromTheCurrentValueAndRejectsStaleCompletion),
            RetriggerStartsFromTheCurrentValueAndRejectsStaleCompletion);
        Run(
            nameof(UpToDateBranchCompletesProgressBeforeClosingAndShowingDialog),
            UpToDateBranchCompletesProgressBeforeClosingAndShowingDialog);
    }

    private static void CompletionAnimationIsFastAndLinear()
    {
        BehaviorTestCheck.Equal(
            TimeSpan.FromMilliseconds(260),
            UpdateProgressWindow.LatestVersionCompletionDuration);
        BehaviorTestCheck.Equal(
            HandoffBehavior.SnapshotAndReplace,
            UpdateProgressWindow.LatestVersionCompletionHandoff);
        BehaviorTestCheck.True(
            UpdateProgressWindow.ShouldAnimateLatestVersionCompletion(
                clientAreaAnimationEnabled: true,
                highContrastEnabled: false));
        BehaviorTestCheck.False(
            UpdateProgressWindow.ShouldAnimateLatestVersionCompletion(
                clientAreaAnimationEnabled: false,
                highContrastEnabled: false));
        BehaviorTestCheck.False(
            UpdateProgressWindow.ShouldAnimateLatestVersionCompletion(
                clientAreaAnimationEnabled: true,
                highContrastEnabled: true));

        DoubleAnimation animation =
            UpdateProgressWindow.CreateLatestVersionCompletionAnimation(37);

        BehaviorTestCheck.Close(37, animation.From ?? double.NaN);
        BehaviorTestCheck.Close(100, animation.To ?? double.NaN);
        BehaviorTestCheck.Null(animation.EasingFunction);
        BehaviorTestCheck.Close(0, animation.AccelerationRatio);
        BehaviorTestCheck.Close(0, animation.DecelerationRatio);
        BehaviorTestCheck.Equal(
            UpdateProgressWindow.LatestVersionCompletionDuration,
            animation.Duration.TimeSpan);
        BehaviorTestCheck.Equal(FillBehavior.HoldEnd, animation.FillBehavior);
    }

    private static void CompletionSettlesAtHundredAndPreservesTextBinding()
    {
        RunSta(
            () =>
            {
                using var scope = new ProgressWindowScope(
                    startPercent: 12,
                    animationsEnabled: true);
                UpdateProgressWindow window = scope.Window;
                BindingExpression originalBinding = BehaviorTestCheck.NotNull(
                    BindingOperations.GetBindingExpression(
                        window.ProgressPercentText,
                        TextBlock.TextProperty));

                Task completion = window.CompleteLatestVersionProgressAsync();

                BehaviorTestCheck.False(completion.IsCompleted);
                PumpDispatcherUntil(
                    () => window.DownloadProgress.Value > 13
                        && window.DownloadProgress.Value < 99);
                BehaviorTestCheck.Equal(
                    $"{window.DownloadProgress.Value:0}%",
                    window.ProgressPercentText.Text);
                PumpDispatcherUntil(() => completion.IsCompleted);
                completion.GetAwaiter().GetResult();

                BehaviorTestCheck.Close(100, window.DownloadProgress.Value);
                BehaviorTestCheck.Close(
                    100,
                    (double)window.DownloadProgress.GetAnimationBaseValue(
                        RangeBase.ValueProperty));
                BehaviorTestCheck.Equal("100%", window.ProgressPercentText.Text);
                BehaviorTestCheck.False(
                    window.LatestVersionProgressHasAnimatedPropertiesForTest);
                BehaviorTestCheck.True(
                    ReferenceEquals(
                        originalBinding,
                        BindingOperations.GetBindingExpression(
                            window.ProgressPercentText,
                            TextBlock.TextProperty)));
            });
    }

    private static void DisabledMotionCompletesWithoutAClock()
    {
        RunSta(
            () =>
            {
                using var scope = new ProgressWindowScope(
                    startPercent: 0,
                    animationsEnabled: false);
                UpdateProgressWindow window = scope.Window;

                Task completion = window.CompleteLatestVersionProgressAsync();
                PumpDispatcherUntil(() => completion.IsCompleted);
                completion.GetAwaiter().GetResult();

                BehaviorTestCheck.Close(100, window.DownloadProgress.Value);
                BehaviorTestCheck.Close(
                    100,
                    (double)window.DownloadProgress.GetAnimationBaseValue(
                        RangeBase.ValueProperty));
                BehaviorTestCheck.Equal("100%", window.ProgressPercentText.Text);
                BehaviorTestCheck.False(
                    window.LatestVersionProgressHasAnimatedPropertiesForTest);
            });
    }

    private static void CompletionWaitsThroughASettledRenderBeforeReturning()
    {
        RunSta(
            () =>
            {
                using var scope = new ProgressWindowScope(
                    startPercent: 12,
                    animationsEnabled: true);
                UpdateProgressWindow window = scope.Window;
                Task? completion = null;
                bool settledRenderObserved = false;
                bool completionWasPendingDuringSettledRender = false;
                EventHandler rendering = (_, _) =>
                {
                    if (completion is null
                        || window.DownloadProgress.Value < 100
                        || (double)window.DownloadProgress
                            .GetAnimationBaseValue(RangeBase.ValueProperty) < 100
                        || window
                            .LatestVersionProgressHasAnimatedPropertiesForTest)
                    {
                        return;
                    }

                    settledRenderObserved = true;
                    completionWasPendingDuringSettledRender |=
                        !completion.IsCompleted;
                };

                CompositionTarget.Rendering += rendering;
                try
                {
                    completion =
                        window.CompleteLatestVersionProgressAsync();
                    PumpDispatcherUntil(() => completion.IsCompleted);
                    completion.GetAwaiter().GetResult();
                }
                finally
                {
                    CompositionTarget.Rendering -= rendering;
                }

                BehaviorTestCheck.True(settledRenderObserved);
                BehaviorTestCheck.True(
                    completionWasPendingDuringSettledRender,
                    "Completion must stay pending until a render callback " +
                    "observes the settled 100% base value without a clock.");
            });
    }

    private static void CloseDuringRenderYieldCancelsCompletion()
    {
        RunSta(
            () =>
            {
                using var scope = new ProgressWindowScope(
                    startPercent: 0,
                    animationsEnabled: false);
                UpdateProgressWindow window = scope.Window;

                Task completion =
                    window.CompleteLatestVersionProgressAsync();

                BehaviorTestCheck.False(completion.IsCompleted);
                BehaviorTestCheck.Close(100, window.DownloadProgress.Value);
                window.CloseForCompletion();
                PumpDispatcherUntil(() => completion.IsCompleted);

                BehaviorTestCheck.True(completion.IsCanceled);
                BehaviorTestCheck.False(
                    window.LatestVersionProgressHasAnimatedPropertiesForTest);
            });
    }

    private static void RetriggerDuringRenderYieldRejectsTheOldCaller()
    {
        RunSta(
            () =>
            {
                using var scope = new ProgressWindowScope(
                    startPercent: 0,
                    animationsEnabled: false);
                UpdateProgressWindow window = scope.Window;
                Task firstCompletion =
                    window.CompleteLatestVersionProgressAsync();

                BehaviorTestCheck.False(firstCompletion.IsCompleted);
                Task secondCompletion =
                    window.CompleteLatestVersionProgressAsync();
                BehaviorTestCheck.False(secondCompletion.IsCompleted);
                PumpDispatcherUntil(
                    () => firstCompletion.IsCompleted
                        && secondCompletion.IsCompleted);

                BehaviorTestCheck.True(firstCompletion.IsCanceled);
                secondCompletion.GetAwaiter().GetResult();
                BehaviorTestCheck.Close(100, window.DownloadProgress.Value);
                BehaviorTestCheck.Close(
                    100,
                    (double)window.DownloadProgress.GetAnimationBaseValue(
                        RangeBase.ValueProperty));
                BehaviorTestCheck.False(
                    window.LatestVersionProgressHasAnimatedPropertiesForTest);
            });
    }

    private static void CancellationDuringRenderYieldCancelsCompletion()
    {
        RunSta(
            () =>
            {
                using var scope = new ProgressWindowScope(
                    startPercent: 0,
                    animationsEnabled: false);
                using var cancellation = new CancellationTokenSource();
                UpdateProgressWindow window = scope.Window;
                Task completion =
                    window.CompleteLatestVersionProgressAsync(
                        cancellation.Token);

                BehaviorTestCheck.False(completion.IsCompleted);
                cancellation.Cancel();
                PumpDispatcherUntil(() => completion.IsCompleted);

                BehaviorTestCheck.True(completion.IsCanceled);
                BehaviorTestCheck.Close(
                    window.DownloadProgress.Value,
                    (double)window.DownloadProgress.GetAnimationBaseValue(
                        RangeBase.ValueProperty));
                BehaviorTestCheck.False(
                    window.LatestVersionProgressHasAnimatedPropertiesForTest);
            });
    }

    private static void CloseMidMotionCancelsAndClearsTheClock()
    {
        RunSta(
            () =>
            {
                using var scope = new ProgressWindowScope(
                    startPercent: 0,
                    animationsEnabled: true);
                UpdateProgressWindow window = scope.Window;
                Task completion = window.CompleteLatestVersionProgressAsync();
                PumpDispatcherUntil(
                    () => window.LatestVersionProgressHasAnimatedPropertiesForTest
                        && window.DownloadProgress.Value > 0);
                long generation =
                    window.LatestVersionCompletionGenerationForTest;

                window.CloseForCompletion();
                PumpDispatcherUntil(() => completion.IsCompleted);

                BehaviorTestCheck.True(completion.IsCanceled);
                BehaviorTestCheck.True(
                    window.LatestVersionCompletionGenerationForTest
                        > generation);
                BehaviorTestCheck.False(
                    window.LatestVersionProgressHasAnimatedPropertiesForTest);
            });
    }

    private static void RetriggerStartsFromTheCurrentValueAndRejectsStaleCompletion()
    {
        RunSta(
            () =>
            {
                using var scope = new ProgressWindowScope(
                    startPercent: 8,
                    animationsEnabled: true);
                UpdateProgressWindow window = scope.Window;
                Task firstCompletion =
                    window.CompleteLatestVersionProgressAsync();
                PumpDispatcherUntil(
                    () => window.DownloadProgress.Value > 9
                        && window.DownloadProgress.Value < 99);
                double valueBeforeRetrigger = window.DownloadProgress.Value;
                long firstGeneration =
                    window.LatestVersionCompletionGenerationForTest;

                Task secondCompletion =
                    window.CompleteLatestVersionProgressAsync();

                BehaviorTestCheck.True(
                    window.LatestVersionCompletionGenerationForTest
                        > firstGeneration);
                BehaviorTestCheck.True(
                    window.DownloadProgress.Value
                        >= valueBeforeRetrigger - 0.5,
                    $"Retriggering completion must not reset visible progress " +
                    $"(before={valueBeforeRetrigger:0.###}, " +
                    $"after={window.DownloadProgress.Value:0.###}).");
                PumpDispatcherUntil(() => firstCompletion.IsCompleted);
                BehaviorTestCheck.True(firstCompletion.IsCanceled);
                PumpDispatcherUntil(() => secondCompletion.IsCompleted);
                secondCompletion.GetAwaiter().GetResult();
                PumpDispatcherFor(
                    UpdateProgressWindow.LatestVersionCompletionDuration);

                BehaviorTestCheck.Close(100, window.DownloadProgress.Value);
                BehaviorTestCheck.Close(
                    100,
                    (double)window.DownloadProgress.GetAnimationBaseValue(
                        RangeBase.ValueProperty));
                BehaviorTestCheck.False(
                    window.LatestVersionProgressHasAnimatedPropertiesForTest);
            });
    }

    private static void ApplicationCancellationStopsCompletionMotion()
    {
        RunSta(
            () =>
            {
                using var scope = new ProgressWindowScope(
                    startPercent: 0,
                    animationsEnabled: true);
                using var cancellation = new CancellationTokenSource();
                UpdateProgressWindow window = scope.Window;
                Task completion = window.CompleteLatestVersionProgressAsync(
                    cancellation.Token);
                PumpDispatcherUntil(
                    () => window.LatestVersionProgressHasAnimatedPropertiesForTest
                        && window.DownloadProgress.Value > 0);

                cancellation.Cancel();
                PumpDispatcherUntil(() => completion.IsCompleted);

                BehaviorTestCheck.True(completion.IsCanceled);
                BehaviorTestCheck.False(
                    window.LatestVersionProgressHasAnimatedPropertiesForTest);
            });
    }

    private static void UpToDateBranchCompletesProgressBeforeClosingAndShowingDialog()
    {
        // This source contract only locks the App call order. The real WPF
        // progress, render, cancellation, and clock behavior is covered above.
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "GuluPet",
            "App.xaml.cs"));
        BehaviorTestCheck.Equal(
            1,
            source.Split(
                "CompleteLatestVersionProgressAsync(",
                StringSplitOptions.None).Length - 1);
        int branchStart = source.IndexOf(
            "case UpdateCheckStatus.UpToDate:",
            StringComparison.Ordinal);
        BehaviorTestCheck.True(branchStart >= 0);
        int branchEnd = source.IndexOf(
            "case UpdateCheckStatus.UpdateAvailable:",
            branchStart,
            StringComparison.Ordinal);
        BehaviorTestCheck.True(branchEnd > branchStart);
        string branch = source[branchStart..branchEnd];

        int cancellationBefore = branch.IndexOf(
            "cancellation.Token.ThrowIfCancellationRequested();",
            StringComparison.Ordinal);
        int completionCopy = branch.IndexOf(
            "\"检查完成\"",
            StringComparison.Ordinal);
        int awaitedProgress = branch.IndexOf(
            "await progressWindow.CompleteLatestVersionProgressAsync(",
            StringComparison.Ordinal);
        int cancellationAfter = branch.IndexOf(
            "cancellation.Token.ThrowIfCancellationRequested();",
            cancellationBefore + 1,
            StringComparison.Ordinal);
        int close = branch.IndexOf(
            "progressWindow.CloseForCompletion();",
            StringComparison.Ordinal);
        int dialog = branch.IndexOf(
            "MessageBox.Show(",
            StringComparison.Ordinal);

        BehaviorTestCheck.True(cancellationBefore >= 0);
        BehaviorTestCheck.True(completionCopy > cancellationBefore);
        BehaviorTestCheck.True(awaitedProgress > completionCopy);
        BehaviorTestCheck.True(cancellationAfter > awaitedProgress);
        BehaviorTestCheck.True(close > cancellationAfter);
        BehaviorTestCheck.True(dialog > close);
    }

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                SynchronizationContext? previousContext =
                    SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(
                        Dispatcher.CurrentDispatcher));
                try
                {
                    test();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(
                        previousContext);
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void PumpDispatcherUntil(
        Func<bool> condition,
        int timeoutMilliseconds = 2_000)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException(
                    "Update progress motion did not settle in time.");
            }

            PumpDispatcherOnce();
        }
    }

    private static void PumpDispatcherFor(TimeSpan duration)
    {
        long deadline = Environment.TickCount64
            + Math.Max(1, (long)Math.Ceiling(duration.TotalMilliseconds));
        while (Environment.TickCount64 < deadline)
        {
            PumpDispatcherOnce();
        }
    }

    private static void PumpDispatcherOnce()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(5),
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "GuluPetCommunity.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Unable to find the GuluPet repository root.");
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(UpdateProgressWindowMotionTests)}.{name}");
    }

    private sealed class ProgressWindowScope : IDisposable
    {
        internal ProgressWindowScope(
            double startPercent,
            bool animationsEnabled)
        {
            Window = new UpdateProgressWindow
            {
                Left = -20_000,
                Top = -20_000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            Window.SetLatestVersionCompletionAnimationsEnabledForTest(
                animationsEnabled);
            Window.SetStage(
                "正在检查新版本…",
                "正在连接更新服务。",
                percent: startPercent);
            Window.Show();
            Window.UpdateLayout();
        }

        internal UpdateProgressWindow Window { get; }

        public void Dispose()
        {
            Window.SetLatestVersionCompletionAnimationsEnabledForTest(null);
            Window.CloseForCompletion();
        }
    }
}
