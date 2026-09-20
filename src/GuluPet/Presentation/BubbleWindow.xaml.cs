using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuluPet.Platform;

namespace GuluPet.Presentation;

public partial class BubbleWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const double MessageFontSize = 15;
    internal const double MinimumMessageTextWidth = 38;
    internal const double MaximumMessageTextWidth = 360;
    private static readonly Typeface MessageTypeface = new(
        new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
        FontStyles.Normal,
        FontWeights.Medium,
        FontStretches.Normal);

    private readonly DispatcherTimer _hideTimer;
    private Window? _anchor;
    private bool _repositionQueued;

    public BubbleWindow()
    {
        InitializeComponent();
        LoadGuluHeadSkin();
        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4),
        };
        _hideTimer.Tick += OnHideTimerTick;
        SizeChanged += OnBubbleSizeChanged;
    }

    public void ShowMessage(
        string text,
        Window anchor,
        TimeSpan? duration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(anchor);

        _hideTimer.Stop();
        MessageText.Text = text;
        UpdateMessageLayout(text);
        Topmost = anchor.Topmost;
        AttachAnchor(anchor);

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        if (!PositionNear(anchor))
        {
            HideImmediately();
            return;
        }

        QueueReposition();

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(Opacity, 1, TimeSpan.FromMilliseconds(150))
            {
                FillBehavior = FillBehavior.HoldEnd,
            });

        _hideTimer.Interval = duration ?? TimeSpan.FromSeconds(4);
        _hideTimer.Start();
    }

    public void HideImmediately()
    {
        _hideTimer.Stop();
        DetachAnchor();
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        extendedStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(extendedStyle));
    }

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer.Stop();
        _hideTimer.Tick -= OnHideTimerTick;
        SizeChanged -= OnBubbleSizeChanged;
        DetachAnchor();
        base.OnClosed(e);
    }

    private bool PositionNear(Window anchor)
    {
        return WindowPlacementService.PositionBubbleNear(this, anchor);
    }

    internal static double ClampMessageTextWidth(double naturalWidth)
    {
        if (!double.IsFinite(naturalWidth) || naturalWidth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(naturalWidth),
                "Measured message width must be finite and non-negative.");
        }

        return Math.Clamp(
            Math.Ceiling(naturalWidth),
            MinimumMessageTextWidth,
            MaximumMessageTextWidth);
    }

    internal static double MeasureMessageTextWidth(
        string text,
        double pixelsPerDip = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!double.IsFinite(pixelsPerDip) || pixelsPerDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerDip));
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            MessageTypeface,
            MessageFontSize,
            System.Windows.Media.Brushes.Black,
            pixelsPerDip);
        return ClampMessageTextWidth(
            formatted.WidthIncludingTrailingWhitespace);
    }

    private void UpdateMessageLayout(string text)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        MessageText.Width = MeasureMessageTextWidth(text, pixelsPerDip);
    }

    private void LoadGuluHeadSkin()
    {
        string iconPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "AppIcon.png");
        if (!File.Exists(iconPath))
        {
            return;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.UniformToFill,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect(0.08, 0.01, 0.84, 0.76),
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Top,
        };
        brush.Freeze();
        GuluHeadImage.Background = brush;
    }

    private void AttachAnchor(Window anchor)
    {
        if (ReferenceEquals(_anchor, anchor))
        {
            return;
        }

        DetachAnchor();
        _anchor = anchor;
        _anchor.LocationChanged += OnAnchorGeometryChanged;
        _anchor.SizeChanged += OnAnchorSizeChanged;
    }

    private void DetachAnchor()
    {
        if (_anchor is null)
        {
            return;
        }

        _anchor.LocationChanged -= OnAnchorGeometryChanged;
        _anchor.SizeChanged -= OnAnchorSizeChanged;
        _anchor = null;
    }

    private void OnAnchorGeometryChanged(object? sender, EventArgs e) =>
        QueueReposition();

    private void OnAnchorSizeChanged(object sender, SizeChangedEventArgs e) =>
        QueueReposition();

    private void OnBubbleSizeChanged(object sender, SizeChangedEventArgs e) =>
        QueueReposition();

    private void QueueReposition()
    {
        if (_repositionQueued || !IsVisible || _anchor is null)
        {
            return;
        }

        _repositionQueued = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            () =>
            {
                _repositionQueued = false;
                if (!IsVisible || _anchor is null)
                {
                    return;
                }

                UpdateLayout();
                if (!PositionNear(_anchor))
                {
                    HideImmediately();
                }
            });
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        var fadeOut = new DoubleAnimation(
            Opacity,
            0,
            TimeSpan.FromMilliseconds(220))
        {
            FillBehavior = FillBehavior.Stop,
        };
        fadeOut.Completed += (_, _) => HideImmediately();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private static IntPtr GetWindowLongPtr(IntPtr window, int index) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(window, index)
            : new IntPtr(GetWindowLong32(window, index));

    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(window, index, value)
            : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
}
