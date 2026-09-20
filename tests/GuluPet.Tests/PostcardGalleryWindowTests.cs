using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuluPet.Postcards;
using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class PostcardGalleryWindowTests
{
    private const int OriginalWidth = 2001;
    private const int OriginalHeight = 2;

    public static void RunAll()
    {
        Run(
            nameof(DefaultViewerStartInfoUsesShellExecute),
            DefaultViewerStartInfoUsesShellExecute);
        Run(
            nameof(OpenCommandUsesOriginalImagePath),
            OpenCommandUsesOriginalImagePath);
        Run(
            nameof(LauncherFailureShowsTruthfulNonModalFeedback),
            LauncherFailureShowsTruthfulNonModalFeedback);
        Run(
            nameof(RapidFailureRetargetsAnActiveExit),
            RapidFailureRetargetsAnActiveExit);
        Run(
            nameof(SuccessAndWindowCloseClearFeedbackImmediately),
            SuccessAndWindowCloseClearFeedbackImmediately);
        Run(
            nameof(ReducedMotionShowsAndExpiresImmediately),
            ReducedMotionShowsAndExpiresImmediately);
        Run(
            nameof(MissingImageDoesNotInvokeLauncher),
            MissingImageDoesNotInvokeLauncher);
        Run(
            nameof(SeventeenCardChapterShowsEightyFivePercent),
            SeventeenCardChapterShowsEightyFivePercent);
        Run(
            nameof(ThreeHundredCardGalleryLoadsOnlyTheVisibleChapter),
            ThreeHundredCardGalleryLoadsOnlyTheVisibleChapter);
    }

    private static void DefaultViewerStartInfoUsesShellExecute()
    {
        string imagePath = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "viewer-contract.jpg"));
        ProcessStartInfo startInfo =
            PostcardGalleryWindow.CreateImageOpenStartInfo(imagePath);

        BehaviorTestCheck.Equal(imagePath, startInfo.FileName);
        BehaviorTestCheck.True(startInfo.UseShellExecute);
        BehaviorTestCheck.Equal(string.Empty, startInfo.Arguments);
    }

    private static void OpenCommandUsesOriginalImagePath()
    {
        WithPostcardImage(
            imagePath =>
            {
                string? openedPath = null;
                var window = new PostcardGalleryWindow(
                    path => openedPath = path);
                try
                {
                    SetAndOpenPostcard(window, imagePath);
                    BehaviorTestCheck.Equal(
                        Path.GetFullPath(imagePath),
                        BehaviorTestCheck.NotNull(openedPath));
                    BehaviorTestCheck.Equal(
                        Visibility.Collapsed,
                        window.ImageOpenFeedbackBorder.Visibility);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void LauncherFailureShowsTruthfulNonModalFeedback()
    {
        WithPostcardImage(
            imagePath =>
            {
                var window = new PostcardGalleryWindow(
                    _ => throw new Win32Exception("No image association."));
                try
                {
                    window.SetImageOpenFeedbackAnimationsEnabledForTest(true);
                    window.Show();
                    SetAndOpenPostcard(window, imagePath);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.ImageOpenFeedbackBorder.Visibility);
                    BehaviorTestCheck.Equal(
                        "图片暂时打不开",
                        window.ImageOpenFeedbackText.Text);
                    BehaviorTestCheck.False(
                        window.ImageOpenFeedbackBorder.IsHitTestVisible);
                    BehaviorTestCheck.Equal(
                        "图片暂时打不开",
                        AutomationProperties.GetName(
                            window.ImageOpenFeedbackText));
                    BehaviorTestCheck.Equal(
                        AutomationLiveSetting.Polite,
                        AutomationProperties.GetLiveSetting(
                            window.ImageOpenFeedbackText));
                    BehaviorTestCheck.Equal(
                        TimeSpan.FromSeconds(2.4),
                        window.ImageOpenFeedbackDwellForTest);
                    BehaviorTestCheck.True(
                        window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        0,
                        (double)window.ImageOpenFeedbackBorder
                            .GetAnimationBaseValue(
                                UIElement.OpacityProperty));
                    BehaviorTestCheck.Close(
                        8,
                        (double)window.ImageOpenFeedbackTranslate
                            .GetAnimationBaseValue(
                                TranslateTransform.YProperty));

                    PumpDispatcherUntil(
                        () => !window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        1,
                        window.ImageOpenFeedbackBorder.Opacity);
                    BehaviorTestCheck.Close(
                        0,
                        window.ImageOpenFeedbackTranslate.Y);

                    window.ExpireImageOpenFeedbackForTest();
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.ImageOpenFeedbackBorder.Visibility);
                    BehaviorTestCheck.True(
                        window.ImageOpenFeedbackExitInProgressForTest);
                    BehaviorTestCheck.True(
                        window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);
                    PumpDispatcherUntil(
                        () => window.ImageOpenFeedbackBorder.Visibility
                            == Visibility.Collapsed);
                    BehaviorTestCheck.False(
                        window.ImageOpenFeedbackExitInProgressForTest);
                    BehaviorTestCheck.False(
                        window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        0,
                        window.ImageOpenFeedbackBorder.Opacity);
                    BehaviorTestCheck.Close(
                        8,
                        window.ImageOpenFeedbackTranslate.Y);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void RapidFailureRetargetsAnActiveExit()
    {
        WithPostcardImage(
            imagePath =>
            {
                var openAttempt = 0;
                var retryPresentationOpacity = double.NaN;
                var retryPresentationY = double.NaN;
                PostcardGalleryWindow window = null!;
                window = new PostcardGalleryWindow(
                    _ =>
                    {
                        openAttempt++;
                        if (openAttempt == 2)
                        {
                            BehaviorTestCheck.Equal(
                                Visibility.Visible,
                                window.ImageOpenFeedbackBorder.Visibility);
                            BehaviorTestCheck.True(
                                window.ImageOpenFeedbackExitInProgressForTest);
                            BehaviorTestCheck.True(
                                window
                                    .ImageOpenFeedbackHasAnimatedPropertiesForTest);
                            retryPresentationOpacity =
                                window.ImageOpenFeedbackBorder.Opacity;
                            retryPresentationY =
                                window.ImageOpenFeedbackTranslate.Y;
                        }

                        throw new Win32Exception("No image association.");
                    });
                try
                {
                    window.SetImageOpenFeedbackAnimationsEnabledForTest(true);
                    window.Show();
                    SetAndOpenPostcard(window, imagePath);
                    PumpDispatcherUntil(
                        () => !window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);

                    window.ExpireImageOpenFeedbackForTest();
                    long exitVersion =
                        window.ImageOpenFeedbackVersionForTest;
                    window.OpenPostcard("copy-preview-test");
                    BehaviorTestCheck.True(
                        window.ImageOpenFeedbackVersionForTest > exitVersion);
                    BehaviorTestCheck.False(
                        window.ImageOpenFeedbackExitInProgressForTest);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.ImageOpenFeedbackBorder.Visibility);
                    BehaviorTestCheck.Equal(
                        "图片暂时打不开",
                        window.ImageOpenFeedbackText.Text);
                    BehaviorTestCheck.True(
                        window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);
                    double retargetOpacity =
                        (double)window.ImageOpenFeedbackBorder
                            .GetAnimationBaseValue(UIElement.OpacityProperty);
                    double retargetY =
                        (double)window.ImageOpenFeedbackTranslate
                            .GetAnimationBaseValue(
                                TranslateTransform.YProperty);
                    BehaviorTestCheck.Close(
                        retryPresentationOpacity,
                        retargetOpacity,
                        tolerance: 0.02);
                    BehaviorTestCheck.Close(
                        retryPresentationY,
                        retargetY,
                        tolerance: 0.16);

                    PumpDispatcherFor(TimeSpan.FromMilliseconds(130));
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.ImageOpenFeedbackBorder.Visibility);
                    BehaviorTestCheck.False(
                        window.ImageOpenFeedbackExitInProgressForTest);
                    PumpDispatcherUntil(
                        () => !window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.ImageOpenFeedbackBorder.Visibility);
                    BehaviorTestCheck.Close(
                        1,
                        window.ImageOpenFeedbackBorder.Opacity);
                    BehaviorTestCheck.Close(
                        0,
                        window.ImageOpenFeedbackTranslate.Y);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void SuccessAndWindowCloseClearFeedbackImmediately()
    {
        WithPostcardImage(
            imagePath =>
            {
                bool shouldFail = true;
                var window = new PostcardGalleryWindow(
                    _ =>
                    {
                        if (shouldFail)
                        {
                            throw new Win32Exception(
                                "No image association.");
                        }
                    });
                try
                {
                    window.SetImageOpenFeedbackAnimationsEnabledForTest(true);
                    window.Show();
                    SetAndOpenPostcard(window, imagePath);
                    BehaviorTestCheck.True(
                        window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);

                    shouldFail = false;
                    SetAndOpenPostcard(window, imagePath);
                    BehaviorTestCheck.Equal(
                        Visibility.Collapsed,
                        window.ImageOpenFeedbackBorder.Visibility);
                    BehaviorTestCheck.False(
                        window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        0,
                        window.ImageOpenFeedbackBorder.Opacity);
                    BehaviorTestCheck.Close(
                        8,
                        window.ImageOpenFeedbackTranslate.Y);

                    shouldFail = true;
                    SetAndOpenPostcard(window, imagePath);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.ImageOpenFeedbackBorder.Visibility);
                    window.Close();
                    BehaviorTestCheck.Equal(
                        Visibility.Collapsed,
                        window.ImageOpenFeedbackBorder.Visibility);
                    BehaviorTestCheck.False(
                        window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        0,
                        window.ImageOpenFeedbackBorder.Opacity);
                    BehaviorTestCheck.Close(
                        8,
                        window.ImageOpenFeedbackTranslate.Y);
                }
                finally
                {
                    if (window.IsVisible)
                    {
                        window.Close();
                    }
                }
            });
    }

    private static void ReducedMotionShowsAndExpiresImmediately()
    {
        WithPostcardImage(
            imagePath =>
            {
                var window = new PostcardGalleryWindow(
                    _ => throw new Win32Exception("No image association."));
                try
                {
                    window.SetImageOpenFeedbackAnimationsEnabledForTest(false);
                    window.Show();
                    SetAndOpenPostcard(window, imagePath);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.ImageOpenFeedbackBorder.Visibility);
                    BehaviorTestCheck.False(
                        window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        1,
                        window.ImageOpenFeedbackBorder.Opacity);
                    BehaviorTestCheck.Close(
                        0,
                        window.ImageOpenFeedbackTranslate.Y);

                    window.ExpireImageOpenFeedbackForTest();
                    BehaviorTestCheck.Equal(
                        Visibility.Collapsed,
                        window.ImageOpenFeedbackBorder.Visibility);
                    BehaviorTestCheck.False(
                        window
                            .ImageOpenFeedbackHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.Close(
                        0,
                        window.ImageOpenFeedbackBorder.Opacity);
                    BehaviorTestCheck.Close(
                        8,
                        window.ImageOpenFeedbackTranslate.Y);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void MissingImageDoesNotInvokeLauncher()
    {
        RunSta(
            () =>
            {
                var openCount = 0;
                var window = new PostcardGalleryWindow(
                    _ => openCount++);
                try
                {
                    SetAndOpenPostcard(
                        window,
                        Path.Combine(
                            Path.GetTempPath(),
                            $"missing-postcard-{Guid.NewGuid():N}.png"));
                    BehaviorTestCheck.Equal(0, openCount);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.ImageOpenFeedbackBorder.Visibility);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void SeventeenCardChapterShowsEightyFivePercent()
    {
        RunSta(
            () =>
            {
                PostcardDefinition[] catalog = CreateChapterCatalog(300);
                var waiting = new PostcardOutingStatus(
                    PostcardOutingPhase.WaitingAtDoor,
                    StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-30),
                    ReadyAtUtc: DateTimeOffset.UtcNow,
                    ArrivedAtUtc: DateTimeOffset.UtcNow,
                    Remaining: TimeSpan.Zero,
                    NextPostcardId: catalog[17].Id);
                var window = new PostcardGalleryWindow(_ => { });
                try
                {
                    window.SetPostcards(catalog.Take(17), catalog, waiting);

                    BehaviorTestCheck.Equal(20d, window.NextPostcardProgress.Maximum);
                    BehaviorTestCheck.Equal(17d, window.NextPostcardProgress.Value);
                    BehaviorTestCheck.Equal("17/20", window.NextProgressText.Text);
                    BehaviorTestCheck.Equal(
                        "第 1 章 · 章节 1",
                        window.ChapterPageText.Text);
                    BehaviorTestCheck.Equal(
                        "明信片待收",
                        window.NextPostcardLabelText.Text);
                    BehaviorTestCheck.Equal(
                        "进入第2章还差3次出游",
                        window.FooterHintText.Text);
                    BehaviorTestCheck.Equal(
                        12d,
                        window.FooterHintText.Margin.Top);
                    BehaviorTestCheck.Equal(
                        "已收 17 张",
                        window.UnlockedCountText.Text);

                    var style = BehaviorTestCheck.NotNull(
                        window.Resources["ThumbnailButtonStyle"] as Style);
                    var heightSetter = BehaviorTestCheck.NotNull(
                        style.Setters
                            .OfType<Setter>()
                            .Single(setter =>
                                setter.Property == FrameworkElement.HeightProperty));
                    BehaviorTestCheck.True(
                        Convert.ToDouble(heightSetter.Value) >= 208d);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void ThreeHundredCardGalleryLoadsOnlyTheVisibleChapter()
    {
        RunSta(
            () =>
            {
                PostcardDefinition[] catalog = CreateChapterCatalog(300);
                var complete = new PostcardOutingStatus(
                    PostcardOutingPhase.CollectionComplete,
                    StartedAtUtc: null,
                    ReadyAtUtc: null,
                    ArrivedAtUtc: null,
                    Remaining: TimeSpan.Zero,
                    NextPostcardId: null);
                var window = new PostcardGalleryWindow(_ => { });
                try
                {
                    window.SetPostcards(catalog, catalog, complete);

                    BehaviorTestCheck.Equal(300, window.UnlockedCount);
                    BehaviorTestCheck.Equal(20, window.VisiblePostcardCount);
                    BehaviorTestCheck.Equal(15, window.AvailableChapterCount);
                    BehaviorTestCheck.Equal(1, window.CurrentChapterNumber);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.ChapterNavigation.Visibility);

                    window.NextChapterButton.RaiseEvent(
                        new RoutedEventArgs(
                            Button.ClickEvent,
                            window.NextChapterButton));
                    BehaviorTestCheck.Equal(2, window.CurrentChapterNumber);
                    BehaviorTestCheck.Equal(20, window.VisiblePostcardCount);

                    window.SetPostcards(
                        catalog.Take(205),
                        catalog,
                        complete with
                        {
                            Phase = PostcardOutingPhase.AtHome,
                        });
                    BehaviorTestCheck.Equal(205, window.UnlockedCount);
                    BehaviorTestCheck.Equal(2, window.CurrentChapterNumber);
                    BehaviorTestCheck.Equal(20, window.VisiblePostcardCount);
                    for (var page = 2; page < 11; page++)
                    {
                        window.NextChapterButton.RaiseEvent(
                            new RoutedEventArgs(
                                Button.ClickEvent,
                                window.NextChapterButton));
                    }

                    BehaviorTestCheck.Equal(11, window.CurrentChapterNumber);
                    BehaviorTestCheck.Equal(5, window.VisiblePostcardCount);
                    BehaviorTestCheck.Equal(5d, window.NextPostcardProgress.Value);
                    BehaviorTestCheck.False(window.NextChapterButton.IsEnabled);

                    window.SetPostcards(
                        [catalog[0], catalog[40]],
                        catalog,
                        complete with
                        {
                            Phase = PostcardOutingPhase.AtHome,
                        });
                    BehaviorTestCheck.Equal(2, window.UnlockedCount);
                    BehaviorTestCheck.Equal(1, window.VisiblePostcardCount);
                    BehaviorTestCheck.Equal(3, window.CurrentChapterNumber);
                    BehaviorTestCheck.True(
                        window.PreviousChapterButton.IsEnabled);
                    BehaviorTestCheck.False(window.NextChapterButton.IsEnabled);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void SetAndOpenPostcard(
        PostcardGalleryWindow window,
        string imagePath)
    {
        var postcard = new PostcardDefinition(
            "copy-preview-test",
            "测试明信片",
            "测试地点",
            imagePath,
            1,
            "测试画面",
            "第一次远行");
        window.SetPostcards([postcard], availableCount: 1);
        window.OpenPostcard(postcard.Id);
    }

    private static PostcardDefinition[] CreateChapterCatalog(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new PostcardDefinition(
                $"card-{index}",
                $"明信片 {index}",
                $"地点 {index}",
                Path.Combine(
                    Path.GetTempPath(),
                    $"missing-gallery-card-{index}.jpg"),
                index,
                $"咕噜的第 {index} 张明信片",
                $"章节 {(index - 1) / 20 + 1}"))
            .ToArray();

    private static void WithPostcardImage(Action<string> test)
    {
        RunSta(
            () =>
            {
                string directory = Path.Combine(
                    Path.GetTempPath(),
                    $"GuluPet.PostcardCopy.{Guid.NewGuid():N}");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "postcard.png");
                try
                {
                    WritePostcardPng(path);
                    test(path);
                }
                finally
                {
                    Directory.Delete(directory, recursive: true);
                }
            });
    }

    private static void WritePostcardPng(string path)
    {
        int stride = OriginalWidth * 4;
        var pixels = new byte[stride * OriginalHeight];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 0x99;
            pixels[offset + 1] = 0xCC;
            pixels[offset + 2] = 0xEE;
            pixels[offset + 3] = 0xFF;
        }

        BitmapSource bitmap = BitmapSource.Create(
            OriginalWidth,
            OriginalHeight,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read);
        encoder.Save(stream);
    }

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    test();
                }
                catch (Exception exception)
                {
                    failure = exception;
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
        int timeoutMilliseconds = 5_000)
    {
        ArgumentNullException.ThrowIfNull(condition);
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException(
                    "The postcard feedback did not settle in time.");
            }

            PumpDispatcherOnce();
        }
    }

    private static void PumpDispatcherFor(TimeSpan duration)
    {
        long deadline = Environment.TickCount64
            + Math.Max(0, (long)duration.TotalMilliseconds);
        do
        {
            PumpDispatcherOnce();
        }
        while (Environment.TickCount64 < deadline);
    }

    private static void PumpDispatcherOnce()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(PostcardGalleryWindowTests)}.{name}");
    }
}
