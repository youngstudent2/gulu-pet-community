using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuluPet.Platform;
using GuluPet.Postcards;
using Forms = System.Windows.Forms;

namespace GuluPet.Presentation;

public sealed class PostcardDismissedEventArgs : EventArgs
{
    internal PostcardDismissedEventArgs(
        PostcardDefinition postcard,
        bool automatic,
        bool isTravelEcho,
        bool isCatalogSummary = false)
    {
        Postcard = postcard;
        Automatic = automatic;
        IsTravelEcho = isTravelEcho;
        IsCatalogSummary = isCatalogSummary;
    }

    public PostcardDefinition Postcard { get; }

    public bool Automatic { get; }

    public bool IsTravelEcho { get; }

    public bool IsCatalogSummary { get; }
}

public partial class PostcardUnlockWindow : Window
{
    private const double DefaultWindowWidth = 420;
    private const double DefaultWindowHeight = 390;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _autoCloseTimer;
    private readonly FrameworkElement[] _sparkles;
    private PostcardDefinition? _currentPostcard;
    private bool _dismissInProgress;
    private long _presentationVersion;

    public PostcardUnlockWindow()
    {
        InitializeComponent();
        _sparkles =
        [
            Sparkle1,
            Sparkle2,
            Sparkle3,
            Sparkle4,
            Sparkle5,
            Sparkle6,
            Sparkle7,
            Sparkle8,
        ];
        _autoCloseTimer = new DispatcherTimer(
            VisibleDuration,
            DispatcherPriority.Normal,
            OnAutoCloseTimerTick,
            Dispatcher);
        LoadGuluStamp();
    }

    public event EventHandler<PostcardDismissedEventArgs>? Dismissed;

    public PostcardDefinition? CurrentPostcard => _currentPostcard;

    public bool IsPresenting =>
        IsVisible && _currentPostcard is not null && !_dismissInProgress;

    public void ShowPostcard(
        PostcardDefinition postcard,
        Window? anchor = null)
    {
        ShowPostcard(postcard, availableCount: 0, anchor: anchor);
    }

    public void ShowPostcard(
        PostcardDefinition postcard,
        int availableCount,
        Window? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(postcard);
        ArgumentOutOfRangeException.ThrowIfNegative(availableCount);
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(
                () => ShowPostcard(postcard, availableCount, anchor));
            return;
        }

        long presentationVersion = ++_presentationVersion;
        _dismissInProgress = false;
        _currentPostcard = postcard;
        Populate(postcard, availableCount);
        Topmost = anchor?.Topmost ?? false;

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        PositionForPresentation(anchor);
        BeginRevealAnimation();
        _autoCloseTimer.Stop();
        _autoCloseTimer.Start();

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            () =>
            {
                if (presentationVersion == _presentationVersion)
                {
                    RaiseUnlockAnnouncement();
                }
            });
    }

    public void Dismiss() => BeginDismiss(automatic: false);

    /// <summary>
    /// Hides the current reward without acknowledging it. The caller owns the
    /// returned postcard and may present it again when the pet becomes visible.
    /// </summary>
    public PostcardDefinition? Defer()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(Defer);
        }

        PostcardDefinition? postcard = _currentPostcard;
        ++_presentationVersion;
        _autoCloseTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (IsVisible)
        {
            Hide();
        }

        _dismissInProgress = false;
        _currentPostcard = null;
        return postcard;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        long extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        extendedStyle |= WsExToolWindow | WsExNoActivate;
        _ = SetWindowLongPtr(
            handle,
            GwlExStyle,
            new IntPtr(extendedStyle));

        _ = DwmWindowChrome.TryApplyRoundedCorners(handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        _autoCloseTimer.Tick -= OnAutoCloseTimerTick;
        base.OnClosed(e);
    }

    private void Populate(
        PostcardDefinition postcard,
        int availableCount)
    {
        bool isTravelEcho = IsTravelEcho(postcard);
        bool isCatalogSummary = IsCatalogSummary(postcard);
        bool isSyntheticNotice = isTravelEcho || isCatalogSummary;
        bool hasChapter = postcard.UnlockOrder > 0
            && !string.IsNullOrWhiteSpace(postcard.Chapter);
        int chapterPosition = hasChapter
            ? GetChapterPosition(postcard)
            : 0;
        PostcardTitleText.Text = postcard.Title;
        PostcardLocationText.Text = postcard.Location;
        JourneyOrderText.Text = isTravelEcho
            ? $"一路已经收好 {GetTravelEchoNumber(postcard):N0} 枚小印章"
            : isCatalogSummary
                ? "明信片册添了新页"
                : hasChapter
                    ? $"「{postcard.Chapter}」的第 {chapterPosition} 张"
                    : $"第 {postcard.UnlockOrder:00} 次远行";
        RevealAnnouncementText.Text = isTravelEcho
            ? "咕噜盖下新的旅行印章"
            : isCatalogSummary
                ? "咕噜整理好了一叠旧明信片"
                : hasChapter
                    && chapterPosition
                        == PostcardChapterProgressFormatter.CardsPerChapter
                    ? $"「{postcard.Chapter}」这一程收好啦"
                    : hasChapter
                        ? $"咕噜寄来「{postcard.Chapter}」的新明信片"
                        : "咕噜寄来了一张明信片";
        RevealSubtitleText.Text = isTravelEcho
            ? "一路带回的小印章，也替妈咪好好收着"
            : isCatalogSummary
                ? "咕噜把一路上的收获一起带回来了"
                : hasChapter
                    && availableCount > 0
                    && postcard.UnlockOrder == availableCount
                ? $"这一程的风景都收好了，" +
                  $"{GetChapterCount(availableCount):N0} 段远方也都到家啦"
                : hasChapter
                    && chapterPosition
                        == PostcardChapterProgressFormatter.CardsPerChapter
                    ? "这一程的风景都收好了，下一段远方在等咕噜"
                    : hasChapter
                        ? $"这一程已经有 {chapterPosition} 张风景了"
                        : "这段小旅行，咕噜替妈咪收好了";
        AutomationProperties.SetName(
            PostcardButton,
            isTravelEcho
                ? $"收好咕噜带回的小印章：{postcard.Title}，{postcard.Location}"
                : isCatalogSummary
                    ? $"收下咕噜整理好的明信片：{postcard.Title}，{postcard.Location}"
                    : hasChapter
                        ? $"收下明信片：{postcard.Title}，{postcard.Chapter}，" +
                          $"这一程里的第 {chapterPosition} 张"
                        : $"收下明信片：{postcard.Title}");
        AutomationProperties.SetHelpText(
            PostcardButton,
            isTravelEcho
                ? "点击收好这次带回的旅行印章"
                : isCatalogSummary
                    ? "点击把这一叠明信片一起收好"
                    : hasChapter
                        ? $"把「{postcard.Chapter}」里的第 {chapterPosition} 张" +
                          "放进明信片册"
                        : "把这张风景放进明信片册");
        AutomationProperties.SetName(
            JourneyOrderText,
            JourneyOrderText.Text);
        AutomationProperties.SetName(
            PostcardImage,
            string.IsNullOrWhiteSpace(postcard.AltText)
                ? $"{postcard.Location}，{postcard.Title}"
                : postcard.AltText);

        TravelEchoStamp.Visibility = isSyntheticNotice
            ? Visibility.Visible
            : Visibility.Collapsed;
        PostcardImage.Visibility = isSyntheticNotice
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (isSyntheticNotice)
        {
            SyntheticStampLabelText.Text = isTravelEcho
                ? "咕噜的小印章"
                : "手账新页";
            AutomationProperties.SetName(
                TravelEchoGuluImage,
                postcard.AltText);
            PostcardImage.Source = null;
            ImageFailureText.Visibility = Visibility.Collapsed;
            return;
        }

        BitmapSource? image = TryLoadBitmap(postcard.ImagePath, decodePixelWidth: 1200);
        PostcardImage.Source = image;
        ImageFailureText.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadGuluStamp()
    {
        string[] candidates =
        [
            Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "AppIcon.png"),
            Path.Combine(AppContext.BaseDirectory, "AppIcon.png"),
        ];
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is not null)
        {
            GuluStampImage.Source = TryLoadBitmap(path, decodePixelWidth: 96);
            TravelEchoGuluImage.Source = TryLoadBitmap(path, decodePixelWidth: 160);
        }
    }

    internal static bool IsTravelEcho(PostcardDefinition postcard) =>
        postcard is not null
        && postcard.Id.StartsWith("travel-echo-", StringComparison.Ordinal)
        && string.IsNullOrWhiteSpace(postcard.ImagePath);

    internal static bool IsCatalogSummary(PostcardDefinition postcard) =>
        postcard is not null
        && string.Equals(
            postcard.Id,
            "catalog-batch-summary",
            StringComparison.Ordinal)
        && string.IsNullOrWhiteSpace(postcard.ImagePath);

    private static int GetChapterPosition(PostcardDefinition postcard) =>
        postcard.UnlockOrder > 0
            ? PostcardChapterProgressFormatter.GetPositionInChapter(postcard)
            : 0;

    private static int GetChapterCount(int availableCount) =>
        (availableCount
            + PostcardChapterProgressFormatter.CardsPerChapter
            - 1)
        / PostcardChapterProgressFormatter.CardsPerChapter;

    private static long GetTravelEchoNumber(PostcardDefinition postcard) =>
        long.TryParse(
            postcard.Id["travel-echo-".Length..],
            out long number)
            ? number
            : 0;

    private void BeginRevealAnimation()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CardRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        SweepTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SweepBand.BeginAnimation(OpacityProperty, null);

        bool useFullMotion =
            SystemParameters.ClientAreaAnimation &&
            !SystemParameters.HighContrast;
        var fade = new DoubleAnimation(
            0,
            1,
            TimeSpan.FromMilliseconds(useFullMotion ? 220 : 150))
        {
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut,
            },
        };
        BeginAnimation(OpacityProperty, fade);

        if (!useFullMotion)
        {
            CardScale.ScaleX = 1;
            CardScale.ScaleY = 1;
            CardRotation.Angle = 0;
            SweepBand.Opacity = 0;
            foreach (FrameworkElement sparkle in _sparkles)
            {
                sparkle.Opacity = 0;
            }

            return;
        }

        CardScale.ScaleX = 0.92;
        CardScale.ScaleY = 0.92;
        CardRotation.Angle = -1.5;

        DoubleAnimationUsingKeyFrames scale = CreateScaleAnimation();
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        CardScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            scale.Clone());
        CardRotation.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation(
                -1.5,
                0,
                TimeSpan.FromMilliseconds(430))
            {
                FillBehavior = FillBehavior.HoldEnd,
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut,
                },
            });

        SweepBand.Opacity = 0;
        SweepTransform.X = -130;
        var sweepOpacity = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(180),
            FillBehavior = FillBehavior.Stop,
        };
        sweepOpacity.KeyFrames.Add(
            new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sweepOpacity.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                0.78,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        sweepOpacity.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
        SweepBand.BeginAnimation(OpacityProperty, sweepOpacity);
        SweepTransform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(
                -130,
                420,
                TimeSpan.FromMilliseconds(900))
            {
                BeginTime = TimeSpan.FromMilliseconds(180),
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseInOut,
                },
            });

        for (var index = 0; index < _sparkles.Length; index++)
        {
            AnimateSparkle(
                _sparkles[index],
                TimeSpan.FromMilliseconds(85 * index));
        }
    }

    private static DoubleAnimationUsingKeyFrames CreateScaleAnimation()
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            FillBehavior = FillBehavior.HoldEnd,
        };
        animation.KeyFrames.Add(
            new DiscreteDoubleKeyFrame(
                0.92,
                KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1.025,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230)),
                new CubicEase
                {
                    EasingMode = EasingMode.EaseOut,
                }));
        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500)),
                new CubicEase
                {
                    EasingMode = EasingMode.EaseInOut,
                }));
        return animation;
    }

    private static void AnimateSparkle(
        FrameworkElement sparkle,
        TimeSpan delay)
    {
        sparkle.BeginAnimation(OpacityProperty, null);
        sparkle.Opacity = 0;
        if (sparkle.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = 0.35;
        scale.ScaleY = 0.35;

        var opacity = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = delay,
            FillBehavior = FillBehavior.Stop,
        };
        opacity.KeyFrames.Add(
            new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130))));
        opacity.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                0.72,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360))));
        opacity.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(720))));
        sparkle.BeginAnimation(OpacityProperty, opacity);

        var sparkleScale = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = delay,
            FillBehavior = FillBehavior.Stop,
        };
        sparkleScale.KeyFrames.Add(
            new DiscreteDoubleKeyFrame(
                0.35,
                KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sparkleScale.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1.18,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
                new CubicEase
                {
                    EasingMode = EasingMode.EaseOut,
                }));
        sparkleScale.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0.86,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(720)),
                new CubicEase
                {
                    EasingMode = EasingMode.EaseIn,
                }));
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            sparkleScale);
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            sparkleScale.Clone());
    }

    private void BeginDismiss(bool automatic)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => BeginDismiss(automatic));
            return;
        }

        if (_dismissInProgress || !IsVisible || _currentPostcard is null)
        {
            return;
        }

        _dismissInProgress = true;
        _autoCloseTimer.Stop();
        long presentationVersion = _presentationVersion;
        PostcardDefinition postcard = _currentPostcard;
        bool isTravelEcho = IsTravelEcho(postcard);
        bool isCatalogSummary = IsCatalogSummary(postcard);
        var fade = new DoubleAnimation(
            Opacity,
            0,
            TimeSpan.FromMilliseconds(automatic ? 220 : 110))
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseIn,
            },
        };
        fade.Completed += (_, _) =>
        {
            if (presentationVersion != _presentationVersion)
            {
                return;
            }

            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Hide();
            _dismissInProgress = false;
            _currentPostcard = null;
            Dismissed?.Invoke(
                this,
                new PostcardDismissedEventArgs(
                    postcard,
                    automatic,
                    isTravelEcho,
                    isCatalogSummary));
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void PositionForPresentation(Window? anchor)
    {
        IntPtr windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (anchor is not null)
        {
            IntPtr anchorHandle = new WindowInteropHelper(anchor).Handle;
            if (anchorHandle != IntPtr.Zero
                && NativeMethods.TryGetExtendedWindowRect(
                    anchorHandle,
                    out NativeMethods.NativeRect anchorRect))
            {
                PositionNearAnchor(
                    windowHandle,
                    anchorHandle,
                    anchorRect);
                return;
            }
        }

        PositionOnPointerMonitor(windowHandle);
    }

    private void PositionNearAnchor(
        IntPtr windowHandle,
        IntPtr anchorHandle,
        NativeMethods.NativeRect anchorRect)
    {
        Rectangle anchorBounds = new(
            anchorRect.Left,
            anchorRect.Top,
            Math.Max(1, anchorRect.Width),
            Math.Max(1, anchorRect.Height));
        Rectangle workArea = Forms.Screen
            .FromRectangle(anchorBounds)
            .WorkingArea;
        NativeMethods.MonitorDpi dpi =
            NativeMethods.GetWindowDpiOrDefault(anchorHandle);
        int marginX = DipToPixels(10, dpi.X);
        int marginY = DipToPixels(10, dpi.Y);
        FitWindowToWorkArea(workArea, dpi, marginX, marginY);
        int width = DipToPixels(
            ActualWidth > 0 ? ActualWidth : Width,
            dpi.X);
        int height = DipToPixels(
            ActualHeight > 0 ? ActualHeight : Height,
            dpi.Y);
        int gapX = DipToPixels(12, dpi.X);
        int gapY = DipToPixels(12, dpi.Y);

        (int X, int Y)[] candidates =
        [
            (
                anchorBounds.Left + (anchorBounds.Width - width) / 2,
                anchorBounds.Top - height - gapY),
            (
                anchorBounds.Right + gapX,
                anchorBounds.Top + (anchorBounds.Height - height) / 2),
            (
                anchorBounds.Left - width - gapX,
                anchorBounds.Top + (anchorBounds.Height - height) / 2),
            (
                anchorBounds.Left + (anchorBounds.Width - width) / 2,
                anchorBounds.Bottom + gapY),
        ];
        (int X, int Y) target = candidates.FirstOrDefault(candidate =>
            Fits(
                candidate.X,
                candidate.Y,
                width,
                height,
                workArea,
                marginX,
                marginY));
        if (target == default
            && !Fits(
                target.X,
                target.Y,
                width,
                height,
                workArea,
                marginX,
                marginY))
        {
            target = Clamp(
                candidates[0].X,
                candidates[0].Y,
                width,
                height,
                workArea,
                marginX,
                marginY);
        }

        MoveAndSize(
            windowHandle,
            target.X,
            target.Y,
            width,
            height);
        ClampNativeWindow(windowHandle, workArea, marginX, marginY);
    }

    private void PositionOnPointerMonitor(IntPtr windowHandle)
    {
        NativeMethods.GetPhysicalCursorPos(out NativeMethods.NativePoint point);
        IntPtr monitor = NativeMethods.MonitorFromPoint(
            point,
            NativeMethods.MonitorDefaultToNearest);
        Rectangle workArea = Forms.Screen.FromPoint(
            new System.Drawing.Point(point.X, point.Y)).WorkingArea;
        NativeMethods.MonitorDpi dpi =
            NativeMethods.GetMonitorDpiOrDefault(monitor, windowHandle);
        int marginX = DipToPixels(10, dpi.X);
        int marginY = DipToPixels(10, dpi.Y);
        FitWindowToWorkArea(workArea, dpi, marginX, marginY);
        int width = DipToPixels(
            ActualWidth > 0 ? ActualWidth : Width,
            dpi.X);
        int height = DipToPixels(
            ActualHeight > 0 ? ActualHeight : Height,
            dpi.Y);
        (int X, int Y) target = Clamp(
            workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2,
            width,
            height,
            workArea,
            marginX,
            marginY);
        MoveAndSize(
            windowHandle,
            target.X,
            target.Y,
            width,
            height);
        ClampNativeWindow(windowHandle, workArea, marginX, marginY);
    }

    private void FitWindowToWorkArea(
        Rectangle workArea,
        NativeMethods.MonitorDpi dpi,
        int marginX,
        int marginY)
    {
        int maximumWidth = Math.Max(1, workArea.Width - 2 * marginX);
        int maximumHeight = Math.Max(1, workArea.Height - 2 * marginY);
        double maximumDipWidth = PixelsToDip(maximumWidth, dpi.X);
        double maximumDipHeight = PixelsToDip(maximumHeight, dpi.Y);
        double scale = Math.Min(
            1,
            Math.Min(
                maximumDipWidth / DefaultWindowWidth,
                maximumDipHeight / DefaultWindowHeight));
        Width = Math.Max(1, DefaultWindowWidth * scale);
        Height = Math.Max(1, DefaultWindowHeight * scale);
        UpdateLayout();
    }

    private static bool Fits(
        int x,
        int y,
        int width,
        int height,
        Rectangle workArea,
        int marginX,
        int marginY)
    {
        return x >= workArea.Left + marginX
            && y >= workArea.Top + marginY
            && (long)x + width <= (long)workArea.Right - marginX
            && (long)y + height <= (long)workArea.Bottom - marginY;
    }

    private static (int X, int Y) Clamp(
        int x,
        int y,
        int width,
        int height,
        Rectangle workArea,
        int marginX,
        int marginY)
    {
        int minimumX = workArea.Left + marginX;
        int minimumY = workArea.Top + marginY;
        int maximumX = Math.Max(
            minimumX,
            workArea.Right - marginX - width);
        int maximumY = Math.Max(
            minimumY,
            workArea.Bottom - marginY - height);
        return (
            Math.Clamp(x, minimumX, maximumX),
            Math.Clamp(y, minimumY, maximumY));
    }

    private static void ClampNativeWindow(
        IntPtr windowHandle,
        Rectangle workArea,
        int marginX,
        int marginY)
    {
        if (!NativeMethods.TryGetExtendedWindowRect(
                windowHandle,
                out NativeMethods.NativeRect actual))
        {
            return;
        }

        (int X, int Y) corrected = Clamp(
            actual.Left,
            actual.Top,
            Math.Max(1, actual.Width),
            Math.Max(1, actual.Height),
            workArea,
            marginX,
            marginY);
        if (corrected.X != actual.Left || corrected.Y != actual.Top)
        {
            Move(windowHandle, corrected.X, corrected.Y);
        }
    }

    private static void Move(IntPtr windowHandle, int x, int y)
    {
        _ = NativeMethods.SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            NativeMethods.SwpNoSize
                | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate);
    }

    private static void MoveAndSize(
        IntPtr windowHandle,
        int x,
        int y,
        int width,
        int height)
    {
        _ = NativeMethods.SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            x,
            y,
            Math.Max(1, width),
            Math.Max(1, height),
            NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate);
    }

    private static int DipToPixels(double dip, uint dpi)
    {
        return Math.Max(
            1,
            (int)Math.Round(
                dip * Math.Max(1, dpi) / 96d,
                MidpointRounding.AwayFromZero));
    }

    private static double PixelsToDip(int pixels, uint dpi) =>
        Math.Max(1, pixels) * 96d / Math.Max(1, dpi);

    private static BitmapSource? TryLoadBitmap(
        string path,
        int decodePixelWidth)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = decodePixelWidth;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void RaiseUnlockAnnouncement()
    {
        try
        {
            var peer = new TextBlockAutomationPeer(
                RevealAnnouncementText);
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (InvalidOperationException)
        {
            // The reward is already persisted. Accessibility notification
            // failure must not keep the transient presentation alive.
        }
    }

    private void OnPostcardClicked(
        object sender,
        RoutedEventArgs e) =>
        BeginDismiss(automatic: false);

    private void OnAutoCloseTimerTick(
        object? sender,
        EventArgs e)
    {
        _autoCloseTimer.Stop();
        BeginDismiss(automatic: true);
    }

    private static IntPtr GetWindowLongPtr(IntPtr window, int index) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(window, index)
            : new IntPtr(GetWindowLong32(window, index));

    private static IntPtr SetWindowLongPtr(
        IntPtr window,
        int index,
        IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(window, index, value)
            : new IntPtr(
                SetWindowLong32(window, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(
        IntPtr window,
        int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(
        IntPtr window,
        int index,
        int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr window,
        int index,
        IntPtr value);

}
