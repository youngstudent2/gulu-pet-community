using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuluPet.Accessories;
using GuluPet.Memories;
using GuluPet.Platform;
using GuluPet.Runtime;
using ContextMenuEventArgs = System.Windows.Controls.ContextMenuEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using WpfButton = System.Windows.Controls.Button;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace GuluPet.Presentation;

public sealed class PetDragMotionEventArgs : EventArgs
{
    internal PetDragMotionEventArgs(
        Point screenPosition,
        Point windowPosition,
        Vector delta,
        Vector totalDelta,
        Vector velocity,
        long pressInteractionSessionId,
        SideDockCandidate? sideDockCandidate = null)
    {
        ScreenPosition = screenPosition;
        WindowPosition = windowPosition;
        Delta = delta;
        TotalDelta = totalDelta;
        Velocity = velocity;
        PressInteractionSessionId = pressInteractionSessionId;
        SideDockCandidate = sideDockCandidate;
    }

    /// <summary>Cursor location in physical desktop pixels.</summary>
    public Point ScreenPosition { get; }

    /// <summary>Window location in physical desktop pixels.</summary>
    public Point WindowPosition { get; }

    /// <summary>Pointer movement since the previous event, in physical pixels.</summary>
    public Vector Delta { get; }

    /// <summary>Pointer movement since drag start, in physical pixels.</summary>
    public Vector TotalDelta { get; }

    /// <summary>Pointer velocity in physical pixels per second.</summary>
    public Vector Velocity { get; }

    /// <summary>
    /// Identifies the uninterrupted left-button press that produced this
    /// drag event.
    /// </summary>
    public long PressInteractionSessionId { get; }

    /// <summary>
    /// Proposed side-edge placement captured before the released window is
    /// brought fully back into its monitor work area. The runtime owner decides
    /// whether the drag may actually enter side-dock presentation.
    /// </summary>
    public SideDockCandidate? SideDockCandidate { get; }

    public double SpeedPixelsPerSecond => Velocity.Length;
}

public sealed class PetPressEventArgs : EventArgs
{
    internal PetPressEventArgs(
        TimeSpan holdDuration,
        long pressInteractionSessionId)
    {
        HoldDuration = holdDuration;
        PressInteractionSessionId = pressInteractionSessionId;
    }

    public TimeSpan HoldDuration { get; }

    /// <summary>
    /// Identifies the uninterrupted left-button press that produced this
    /// click or long press.
    /// </summary>
    public long PressInteractionSessionId { get; }
}

public sealed class PetPointerGestureEventArgs : EventArgs
{
    internal PetPointerGestureEventArgs(
        PetPointerGesture gesture,
        long pointerFocusSessionId)
    {
        Gesture = gesture;
        PointerFocusSessionId = pointerFocusSessionId;
    }

    public PetPointerGesture Gesture { get; }

    /// <summary>
    /// Identifies one continuous pointer-focus session. All gestures emitted
    /// before the pointer focus ends share this value.
    /// </summary>
    public long PointerFocusSessionId { get; }
}

public enum MemoryPlaybackOutcome
{
    Ended,
    Skipped,
    Failed,
    Cancelled,
}

public sealed record MemoryPlaybackResult(
    MemoryPlaybackOutcome Outcome,
    Exception? Error = null);

public sealed class MemoryPlaybackFailedEventArgs : EventArgs
{
    internal MemoryPlaybackFailedEventArgs(Uri source, Exception? error)
    {
        Source = source;
        Error = error;
    }

    public Uri Source { get; }

    public Exception? Error { get; }
}

public sealed class MemoryModeChangedEventArgs : EventArgs
{
    internal MemoryModeChangedEventArgs(bool isActive)
    {
        IsActive = isActive;
    }

    public bool IsActive { get; }
}

internal sealed class MemoryWindowRestoreLatch
{
    internal const int RequiredStablePasses = 2;

    private int _stablePassCount;

    public WindowRectangle? TargetBounds { get; private set; }

    public bool IsActive => TargetBounds is not null;

    public void Begin(WindowRectangle targetBounds)
    {
        TargetBounds = targetBounds;
        _stablePassCount = 0;
    }

    public void NoteDpiChange()
    {
        if (IsActive)
        {
            _stablePassCount = 0;
        }
    }

    public bool ObserveSettledBounds(
        WindowRectangle actualBounds,
        bool dpiCallbackPending)
    {
        if (TargetBounds is not WindowRectangle targetBounds
            || dpiCallbackPending
            || actualBounds != targetBounds)
        {
            _stablePassCount = 0;
            return false;
        }

        _stablePassCount++;
        return _stablePassCount >= RequiredStablePasses;
    }

    public void Complete()
    {
        TargetBounds = null;
        _stablePassCount = 0;
    }
}

internal static class MemoryVideoPlacement
{
    public static WindowRectangle FitVideoAroundAnchor(
        WindowRectangle anchorBounds,
        WindowRectangle workArea,
        int naturalWidth,
        int naturalHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(naturalWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(naturalHeight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workArea.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workArea.Height, 1);

        double scale = Math.Min(
            1d,
            Math.Min(
                workArea.Width / (double)naturalWidth,
                workArea.Height / (double)naturalHeight));
        int width = Math.Clamp(
            (int)Math.Round(naturalWidth * scale),
            1,
            workArea.Width);
        int height = Math.Clamp(
            (int)Math.Round(naturalHeight * scale),
            1,
            workArea.Height);
        return CenterAndClamp(
            anchorBounds,
            workArea,
            width,
            height);
    }

    public static WindowRectangle CenterPetOnVideo(
        WindowRectangle videoBounds,
        WindowRectangle workArea,
        int petWidth,
        int petHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(petWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(petHeight, 1);
        return CenterAndClamp(
            videoBounds,
            workArea,
            petWidth,
            petHeight);
    }

    public static WindowRectangle CenterPetOnVideoAtDpi(
        WindowRectangle videoBounds,
        WindowRectangle workArea,
        double petWidthDip,
        double petHeightDip,
        uint dpiX,
        uint dpiY)
    {
        if (!double.IsFinite(petWidthDip) || petWidthDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(petWidthDip));
        }

        if (!double.IsFinite(petHeightDip) || petHeightDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(petHeightDip));
        }

        return CenterPetOnVideo(
            videoBounds,
            workArea,
            Math.Max(
                1,
                PhysicalDragPlacement.DipsToPhysicalPixels(
                    petWidthDip,
                    dpiX)),
            Math.Max(
                1,
                PhysicalDragPlacement.DipsToPhysicalPixels(
                    petHeightDip,
                    dpiY)));
    }

    private static WindowRectangle CenterAndClamp(
        WindowRectangle anchorBounds,
        WindowRectangle workArea,
        int width,
        int height)
    {
        long desiredLeft =
            (long)anchorBounds.Left + (anchorBounds.Width - width) / 2L;
        long desiredTop =
            (long)anchorBounds.Top + (anchorBounds.Height - height) / 2L;
        long maximumLeft = Math.Max(
            (long)workArea.Left,
            (long)workArea.Left + workArea.Width - width);
        long maximumTop = Math.Max(
            (long)workArea.Top,
            (long)workArea.Top + workArea.Height - height);
        int left = (int)Math.Clamp(
            desiredLeft,
            workArea.Left,
            maximumLeft);
        int top = (int)Math.Clamp(
            desiredTop,
            workArea.Top,
            maximumTop);
        return new WindowRectangle(left, top, width, height);
    }
}

public partial class PetWindow : Window
{
    [Flags]
    private enum WardrobePopupDismissReason
    {
        None = 0,
        PointerLeave = 1,
        FocusLoss = 2,
    }

    private sealed record WardrobeItemPresentation(
        string? Id,
        string DisplayName,
        ImageSource? Thumbnail,
        bool IsSelected,
        bool IsNone,
        double ThumbnailSize,
        double NoneIconSize,
        double NameMaxWidth,
        double NameFontSize)
    {
        public string SelectionStatus =>
            IsSelected ? "已选中" : "未选中";
    }

    public const double NormalPetWidth = 280;
    public const double NormalPetHeight = 260;

    private const double DragThresholdPixels = 4;
    private const double WardrobePopupSurfaceDesiredWidth = 640;
    private const double WardrobePopupSurfaceDesiredHeight = 132;
    private const double WardrobePopupShadowPadding = 24;
    private const double InteractionPopupShadowPadding = 24;
    private const double InteractionPopupPetOverlap = 8;
    private const int MemoryRestoreMaxSettlePasses = 8;
    private static readonly TimeSpan LongPressThreshold = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan PointerExitGrace =
        TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan InteractionPopupHideDelay =
        TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan SideDockBlinkOpenDuration =
        TimeSpan.FromMilliseconds(3_200);
    private static readonly TimeSpan SideDockBlinkClosedDuration =
        TimeSpan.FromMilliseconds(150);
    private readonly PointerGestureRecognizer _pointerRecognizer = new();
    private readonly DispatcherTimer _pointerFocusTimer;
    private readonly DispatcherTimer _pointerExitGraceTimer;
    private readonly DispatcherTimer _longPressTimer;
    private readonly DispatcherTimer _interactionPopupHideTimer;
    private readonly DispatcherTimer _wardrobePopupHideTimer;
    private readonly DispatcherTimer _sideDockBlinkTimer;
    private readonly Dictionary<string, ImageSource?>
        _wardrobeThumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource?> _sideDockAssets =
        new(StringComparer.Ordinal);
    private readonly MemoryWindowRestoreLatch _memoryRestoreLatch = new();
    private bool _pointerDown;
    private bool _dragging;
    private bool _pressBeganSideDocked;
    private bool _longPressRaised;
    private bool _alwaysOnTop;
    private bool _startWithWindows;
    private bool _tickScoreLoggingEnabled;
    private bool _isPetVisible = true;
    private bool _careInteractionBusy;
    private bool _travelActionEnabled = true;
    private string? _selectedEyeAccessoryId;
    private WardrobeMenuState? _wardrobeMenuState;
    private WardrobePopupPlacementSide _wardrobePopupPlacementSide =
        WardrobePopupPlacementSide.BelowPet;
    private double? _initialLeft;
    private double? _initialTop;
    private NativeMethods.NativePoint _pressCursor;
    private NativeMethods.NativePoint _lastCursor;
    private Point _pressAnchorDip;
    private long _lastMotionTimestamp;
    private long _pressTimestamp;
    private long _pressInteractionSessionId;
    private long _focusTimestamp;
    private long _pointerFocusSessionId;
    private Vector _lastVelocity;
    private HwndSource? _windowSource;
    private bool _dpiReanchorPending;
    private bool _isMemoryMode;
    private bool _memoryExitInProgress;
    private bool _memoryPointerDown;
    private bool _memoryDragging;
    private NativeMethods.NativePoint _memoryPressCursor;
    private Point _memoryPressAnchorDip;
    private WindowRectangle? _memoryRestoreBounds;
    private TaskCompletionSource<MemoryPlaybackResult>?
        _memoryPlaybackCompletion;
    private CancellationTokenRegistration _memoryPlaybackCancellation;
    private IReadOnlyList<Uri>? _memoryVideoSources;
    private int _memoryVideoIndex;
    private int _memoryNaturalVideoWidth;
    private int _memoryNaturalVideoHeight;
    private SideDockCandidate? _sideDockCandidate;
    private bool _sideDockHovered;
    private bool _sideDockBlinkClosed;
    private bool _sideDockRefreshPending;
    private string? _currentSideDockAssetFileName;
    private long _interactionPopupMotionGeneration;
    private long _interactionPopupHideMotionGeneration;
    private bool _interactionPopupDismissInProgress;
    private double _interactionPopupApproachOffset = -6;
    private bool? _interactionPopupAnimationsEnabledForTest;
    private long _wardrobePopupMotionGeneration;
    private long _wardrobePopupHideMotionGeneration;
    private bool _wardrobePopupDismissInProgress;
    private bool? _wardrobePopupAnimationsEnabledForTest;
    private bool _wardrobePopupRevealSettled;
    private bool _wardrobePopupPointerInteractionArmed;
    private bool _wardrobePopupAutomaticFocusPending;
    private bool _wardrobePopupFocusBaselineEstablished;
    private long _wardrobePopupAutomaticFocusGeneration;
    private WardrobePopupDismissReason _wardrobePopupDismissReasons;
    private Point? _wardrobePopupOpenCursor;
    private Func<Point?>? _wardrobePopupCursorProviderForTest;

    public PetWindow()
    {
        InitializeComponent();
        InteractionPopup.CustomPopupPlacementCallback =
            PlaceInteractionPopup;
        _pointerFocusTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Input,
            OnPointerFocusTick,
            Dispatcher);
        _pointerExitGraceTimer = new DispatcherTimer(
            PointerExitGrace,
            DispatcherPriority.Input,
            OnPointerExitGraceElapsed,
            Dispatcher);
        _longPressTimer = new DispatcherTimer(
            LongPressThreshold,
            DispatcherPriority.Input,
            OnLongPressThresholdReached,
            Dispatcher);
        _interactionPopupHideTimer = new DispatcherTimer(
            InteractionPopupHideDelay,
            DispatcherPriority.Input,
            OnInteractionPopupHideElapsed,
            Dispatcher);
        _wardrobePopupHideTimer = new DispatcherTimer(
            InteractionPopupHideDelay,
            DispatcherPriority.Input,
            OnWardrobePopupHideElapsed,
            Dispatcher);
        _sideDockBlinkTimer = new DispatcherTimer(
            SideDockBlinkOpenDuration,
            DispatcherPriority.Background,
            OnSideDockBlinkElapsed,
            Dispatcher);
        IsVisibleChanged += OnWindowIsVisibleChanged;
        InteractionPopupSurface.IsKeyboardFocusWithinChanged +=
            OnInteractionPopupKeyboardFocusWithinChanged;
        WardrobePopupSurface.IsKeyboardFocusWithinChanged +=
            OnWardrobePopupKeyboardFocusWithinChanged;
        InitializeInteractionAssets();
        InitializeSideDockAssets();
        ResetInteractionPopupMotionVisuals();
    }

    public event EventHandler<PetPressEventArgs>? PetClicked;

    public event EventHandler<PetPressEventArgs>? PetLongPressed;

    public event EventHandler<PetDragMotionEventArgs>? DragStarted;

    public event EventHandler<PetDragMotionEventArgs>? DragMoved;

    public event EventHandler<PetDragMotionEventArgs>? DragCompleted;

    /// <summary>
    /// Raised synchronously when an active side-dock presentation is released.
    /// Drag release raises this before <see cref="DragStarted"/>, allowing the
    /// behavior owner to resume before it accepts the new drag transaction.
    /// </summary>
    public event EventHandler? SideDockReleased;

    public event EventHandler<PetPointerGestureEventArgs>?
        PointerGestureRecognized;

    public event EventHandler? AlwaysOnTopToggleRequested;

    public event EventHandler? StartWithWindowsToggleRequested;

    public event EventHandler? ManualUpdateRequested;

    public event EventHandler? BehaviorsRequested;

    public event EventHandler? PostcardsRequested;

    public event EventHandler? DiaryRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? FeedRequested;

    public event EventHandler? WaterRequested;

    public event EventHandler? TravelRequested;

    public event EventHandler? PettingRequested;

    public event EventHandler<EyeAccessorySelectionRequestedEventArgs>?
        EyeAccessorySelectionRequested;

    public event EventHandler? MemoriesRequested;

    public event EventHandler? VisibilityToggleRequested;

    public event EventHandler? ResetPositionRequested;

    public event EventHandler? ExitRequested;

    /// <summary>
    /// Raised after a memory reaches its natural media end. The window stays
    /// in memory mode until <see cref="ExitMemoryModeAsync"/> is called.
    /// </summary>
    public event EventHandler? MemoryPlaybackEnded;

    /// <summary>
    /// Raised when WPF cannot open or decode the selected memory video.
    /// A failed playback is not treated as naturally completed.
    /// </summary>
    public event EventHandler<MemoryPlaybackFailedEventArgs>?
        MemoryPlaybackFailed;

    /// <summary>
    /// Allows the owner to hide or suppress presentation that lives outside
    /// PetWindow, such as BubbleWindow, while a memory is on screen.
    /// </summary>
    public event EventHandler<MemoryModeChangedEventArgs>? MemoryModeChanged;

    /// <summary>
    /// Raised after the user moves the borderless memory player. The owner
    /// can persist the pet position returned by <see cref="GetWindowPosition"/>.
    /// </summary>
    public event EventHandler? MemoryPositionChanged;

    public bool IsMemoryMode => _isMemoryMode;

    internal bool IsSideDocked => _sideDockCandidate is not null;

    internal bool HasVisibleEyeAccessory =>
        !_isMemoryMode
        && !IsSideDocked
        && EyeAccessoryViewport.Visibility == Visibility.Visible
        && EyeAccessoryImage.Source is not null;

    internal SideDockEdge? CurrentSideDockEdge => _sideDockCandidate?.Edge;

    internal bool IsSideDockHovered => _sideDockHovered;

    internal bool IsSideDockBlinkClosed => _sideDockBlinkClosed;

    internal string? CurrentSideDockAssetFileName =>
        _currentSideDockAssetFileName;

    internal bool IsInteractionPopupOpenForTest => InteractionPopup.IsOpen;

    internal bool IsInteractionPopupDismissInProgressForTest =>
        _interactionPopupDismissInProgress;

    internal long InteractionPopupMotionGenerationForTest =>
        _interactionPopupMotionGeneration;

    internal double InteractionPopupOpacityForTest =>
        InteractionPopupSurface.Opacity;

    internal double InteractionPopupTranslateXForTest =>
        InteractionPopupTranslate.X;

    internal bool InteractionPopupHasAnimatedPropertiesForTest =>
        InteractionPopupSurface.HasAnimatedProperties
        || InteractionPopupTranslate.HasAnimatedProperties;

    internal void SetInteractionPopupAnimationsEnabledForTest(bool? enabled) =>
        _interactionPopupAnimationsEnabledForTest = enabled;

    internal void OpenInteractionPopupForTest(bool revealImmediately = false) =>
        OpenInteractionPopup(revealImmediately);

    internal void DismissInteractionPopupForTest() =>
        DismissInteractionPopup();

    internal void CloseInteractionPopupImmediatelyForTest() =>
        CloseInteractionPopupImmediately();

    internal bool IsWardrobePopupOpenForTest => WardrobePopup.IsOpen;

    internal bool IsWardrobePopupDismissInProgressForTest =>
        _wardrobePopupDismissInProgress;

    internal long WardrobePopupMotionGenerationForTest =>
        _wardrobePopupMotionGeneration;

    internal double WardrobePopupOpacityForTest =>
        WardrobePopupSurface.Opacity;

    internal double WardrobePopupTranslateYForTest =>
        WardrobePopupTranslate.Y;

    internal bool WardrobePopupIsHitTestVisibleForTest =>
        WardrobePopupHost.IsHitTestVisible;

    internal bool WardrobePopupHasAnimatedPropertiesForTest =>
        WardrobePopupSurface.HasAnimatedProperties
        || WardrobePopupTranslate.HasAnimatedProperties;

    internal bool WardrobePopupRevealSettledForTest =>
        _wardrobePopupRevealSettled;

    internal bool WardrobePopupPointerInteractionArmedForTest =>
        _wardrobePopupPointerInteractionArmed;

    internal bool WardrobePopupHideTimerEnabledForTest =>
        _wardrobePopupHideTimer.IsEnabled;

    internal static TimeSpan WardrobePopupHideDelayForTest =>
        InteractionPopupHideDelay;

    internal bool WardrobePopupHasKeyboardFocusForTest =>
        WardrobePopupSurface.IsKeyboardFocusWithin;

    internal void SetWardrobePopupAnimationsEnabledForTest(bool? enabled) =>
        _wardrobePopupAnimationsEnabledForTest = enabled;

    internal void SetWardrobePopupPlacementSideForTest(
        WardrobePopupPlacementSide side) =>
        _wardrobePopupPlacementSide = side;

    internal void OpenWardrobePopupForTest() => OpenWardrobePopup();

    internal void DismissWardrobePopupForTest(bool restoreFocus = false) =>
        DismissWardrobePopup(restoreFocus);

    internal void SetWardrobePopupCursorProviderForTest(
        Func<Point?>? provider) =>
        _wardrobePopupCursorProviderForTest = provider;

    internal IReadOnlyList<string?> WardrobeVisibleIdsForTest =>
        _wardrobeMenuState?.CurrentPageEntries
            .Select(static entry => entry.Id)
            .ToArray()
        ?? Array.Empty<string?>();

    internal int WardrobeTotalItemCountForTest =>
        _wardrobeMenuState?.Entries.Count ?? 0;

    internal int WardrobeCurrentPageForTest =>
        _wardrobeMenuState?.CurrentPageNumber ?? 0;

    internal bool SelectWardrobeItemForTest(string? id)
    {
        Dispatcher.VerifyAccess();
        return SelectWardrobeItem(id);
    }

    public void SetPetFrame(ImageSource? frame)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetPetFrame(frame));
            return;
        }

        PetImage.Source = frame;
    }

    public void ConfigureEyeAccessoryMenu(
        IReadOnlyList<EyeAccessoryDefinition> accessories,
        string? selectedId)
    {
        ArgumentNullException.ThrowIfNull(accessories);
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => ConfigureEyeAccessoryMenu(accessories, selectedId));
            return;
        }

        _wardrobeMenuState = new WardrobeMenuState(accessories, selectedId);
        _selectedEyeAccessoryId =
            _wardrobeMenuState.SelectedAccessoryId;
        if (WardrobePopup.IsOpen)
        {
            PositionWardrobePopup();
        }

        RefreshWardrobePopupItems();
        RefreshInteractionButtonState();

        string label = _selectedEyeAccessoryId is null
            ? "给咕噜换个造型 · 今天什么都不戴"
            : accessories.FirstOrDefault(
                    item => string.Equals(
                        item.Id,
                        _selectedEyeAccessoryId,
                        StringComparison.Ordinal))?.DisplayName is { } name
                ? $"给咕噜换个造型 · 今天戴{name}"
                : "给咕噜换个造型";
        WardrobeInteractionButton.ToolTip = label;
        AutomationProperties.SetName(WardrobeInteractionButton, label);
    }

    public void SetEyeAccessoryPresentation(
        ImageSource? image,
        Matrix? transform)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => SetEyeAccessoryPresentation(image, transform));
            return;
        }

        if (image is not BitmapSource bitmap ||
            bitmap.PixelWidth <= 0 ||
            bitmap.PixelHeight <= 0 ||
            transform is not Matrix matrix ||
            !IsFiniteMatrix(matrix) ||
            !matrix.HasInverse ||
            _isMemoryMode ||
            IsSideDocked)
        {
            HideEyeAccessory();
            return;
        }

        var matrixTransform = new MatrixTransform(matrix);
        if (matrixTransform.CanFreeze)
        {
            matrixTransform.Freeze();
        }

        // Matrix registration uses source pixels. Give the Image the same DIP
        // dimensions so non-512 validated assets cannot be silently stretched
        // away from their declared registration coordinates.
        EyeAccessoryImage.Width = bitmap.PixelWidth;
        EyeAccessoryImage.Height = bitmap.PixelHeight;
        EyeAccessoryImage.Source = bitmap;
        EyeAccessoryImage.RenderTransform = matrixTransform;
        EyeAccessoryViewport.Opacity = 1;
        EyeAccessoryViewport.Visibility = Visibility.Visible;
    }

    public void SetEyeAccessoryOpacity(double opacity)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => SetEyeAccessoryOpacity(opacity));
            return;
        }

        if (!double.IsFinite(opacity)
            || opacity <= 0
            || _isMemoryMode
            || IsSideDocked
            || EyeAccessoryViewport.Visibility != Visibility.Visible
            || EyeAccessoryImage.Source is null)
        {
            HideEyeAccessory();
            return;
        }

        EyeAccessoryViewport.Opacity = Math.Clamp(opacity, 0, 1);
    }

    public void HideEyeAccessory()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(HideEyeAccessory);
            return;
        }

        EyeAccessoryViewport.Visibility = Visibility.Collapsed;
        EyeAccessoryViewport.Opacity = 1;
        EyeAccessoryImage.Source = null;
        EyeAccessoryImage.RenderTransform = Transform.Identity;
    }

    private static ImageSource? LoadBitmapFile(
        string path,
        int decodePixelWidth)
    {
        try
        {
            if (!File.Exists(path))
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
        catch (Exception error) when (
            error is IOException or
            NotSupportedException or
            ArgumentException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            System.Runtime.InteropServices.COMException)
        {
            Trace.TraceWarning(
                "Eye accessory thumbnail could not be loaded ({0}): {1}",
                path,
                error.Message);
            return null;
        }
    }

    private static bool IsFiniteMatrix(Matrix matrix) =>
        double.IsFinite(matrix.M11) &&
        double.IsFinite(matrix.M12) &&
        double.IsFinite(matrix.M21) &&
        double.IsFinite(matrix.M22) &&
        double.IsFinite(matrix.OffsetX) &&
        double.IsFinite(matrix.OffsetY);

    internal void EnterSideDock(SideDockCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Dispatcher.VerifyAccess();
        if (_isMemoryMode)
        {
            throw new InvalidOperationException(
                "Side-dock presentation cannot replace an active memory.");
        }

        if (!HasCompleteSideDockAssetSet(candidate.Edge))
        {
            throw new InvalidDataException(
                $"Side-dock assets for '{candidate.Edge}' are incomplete.");
        }

        CloseInteractionPopupImmediately();
        PetContextMenu.IsOpen = false;
        HideEyeAccessory();
        CancelPointerFocus();
        _longPressTimer.Stop();

        _sideDockCandidate = candidate;
        _sideDockHovered = IsMouseOver;
        _sideDockBlinkClosed = false;
        try
        {
            if (!TryApplySideDockFrame())
            {
                throw new InvalidDataException(
                    "The initial side-dock frame is unavailable.");
            }
            PetImage.Visibility = Visibility.Collapsed;
            SideDockImage.Visibility = Visibility.Visible;
            if (!WindowPlacementService.TryMoveTo(this, candidate.DockBounds))
            {
                throw new Win32Exception(
                    "Could not apply the side-dock window position.");
            }

            _sideDockBlinkTimer.Interval = SideDockBlinkOpenDuration;
            _sideDockBlinkTimer.Start();
            RefreshInteractionButtonState();
        }
        catch
        {
            ResetSideDockPresentation();
            WindowPlacementService.EnsureVisible(this);
            throw;
        }
    }

    internal void ExitSideDock(bool restoreWindow = true)
    {
        Dispatcher.VerifyAccess();
        if (_sideDockCandidate is not { } candidate)
        {
            return;
        }

        ResetSideDockPresentation();
        if (restoreWindow)
        {
            _ = WindowPlacementService.TryMoveTo(
                this,
                candidate.RestoreBounds);
            WindowPlacementService.EnsureVisible(this);
        }

        RefreshInteractionButtonState();
        SideDockReleased?.Invoke(this, EventArgs.Empty);
        RestartPointerFocusIfHovered();
    }

    internal void SetSideDockHoverForTest(bool hovered)
    {
        Dispatcher.VerifyAccess();
        SetSideDockHover(hovered);
    }

    internal void AdvanceSideDockBlinkForTest()
    {
        Dispatcher.VerifyAccess();
        AdvanceSideDockBlink();
    }

    internal static string ResolveSideDockAssetFileName(
        SideDockEdge edge,
        bool hovered,
        bool blinkClosed)
    {
        string side = edge switch
        {
            SideDockEdge.Left => "left",
            SideDockEdge.Right => "right",
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };
        return $"{side}_{(hovered ? "hover" : "rest")}_" +
            $"{(blinkClosed ? "closed" : "open")}.png";
    }

    /// <summary>
    /// Switches this PetWindow into the borderless memory presentation,
    /// plays the supplied video segments in order, and completes when the
    /// sequence ends, fails, or is cancelled. The presentation can be moved
    /// without emitting pet interactions. Natural completion does not leave
    /// memory mode; the owner should first prepare the resumed pet frame and
    /// then call <see cref="ExitMemoryModeAsync"/>.
    /// </summary>
    public Task<MemoryPlaybackResult> EnterMemoryModeAsync(
        IReadOnlyList<Uri> videoSources,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(videoSources);
        if (videoSources.Count == 0
            || videoSources.Any(static source => source is null))
        {
            throw new ArgumentException(
                "A memory presentation requires at least one video source.",
                nameof(videoSources));
        }

        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.InvokeAsync(
                    () => EnterMemoryModeAsync(
                        videoSources,
                        title,
                        cancellationToken),
                    DispatcherPriority.Send)
                .Task
                .Unwrap();
        }

        if (_isMemoryMode)
        {
            throw new InvalidOperationException(
                "A memory presentation is already active.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(
                new MemoryPlaybackResult(
                    MemoryPlaybackOutcome.Cancelled));
        }

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "PetWindow must be shown before a memory can play.");
        }

        WindowRectangle restoreBounds =
            WindowPlacementService.GetRectangle(this);
        _memoryRestoreBounds = restoreBounds;
        _memoryVideoSources = videoSources.ToArray();
        _memoryVideoIndex = 0;
        _memoryNaturalVideoWidth = 0;
        _memoryNaturalVideoHeight = 0;
        _memoryPlaybackCompletion =
            new TaskCompletionSource<MemoryPlaybackResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        _isMemoryMode = true;
        HideEyeAccessory();

        CloseInteractionPopupImmediately();
        RefreshInteractionButtonState();
        CancelPetInputForMemory();
        PetContextMenu.IsOpen = false;

        PetSurface.Visibility = Visibility.Collapsed;
        MemoryLayer.Visibility = Visibility.Visible;
        MemoryTitleText.Text = string.IsNullOrWhiteSpace(title)
            ? "咕噜的回忆"
            : title.Trim();
        MemoryVideo.IsMuted = true;
        MemoryVideo.Volume = 0;

        try
        {
            MemoryModeChanged?.Invoke(
                this,
                new MemoryModeChangedEventArgs(isActive: true));

            if (cancellationToken.CanBeCanceled)
            {
                _memoryPlaybackCancellation =
                    cancellationToken.Register(
                        () => Dispatcher.BeginInvoke(
                            DispatcherPriority.Send,
                            CancelActiveMemoryPlayback));
            }

            StartCurrentMemoryVideo();
            _ = Activate();
            _ = Keyboard.Focus(MemoryLayer);
        }
        catch (Exception error)
        {
            CompleteMemoryPlayback(
                MemoryPlaybackOutcome.Failed,
                error);
        }

        return _memoryPlaybackCompletion.Task;
    }

    /// <summary>
    /// Leaves memory mode and restores the ordinary pet around the latest
    /// saved presentation center.
    /// </summary>
    public Task ExitMemoryModeAsync()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.InvokeAsync(
                    ExitMemoryModeAsync,
                    DispatcherPriority.Send)
                .Task
                .Unwrap();
        }

        return ExitMemoryModeCoreAsync();
    }

    private async Task ExitMemoryModeCoreAsync()
    {
        if (!_isMemoryMode || _memoryExitInProgress)
        {
            return;
        }

        _memoryExitInProgress = true;
        CompleteMemoryPlayback(MemoryPlaybackOutcome.Cancelled);
        WindowRectangle? restoreBounds = null;

        try
        {
            CancelMemoryWindowDrag(notifyPositionChanged: true);
            restoreBounds = _memoryRestoreBounds;
            if (restoreBounds is WindowRectangle targetBounds)
            {
                _memoryRestoreLatch.Begin(targetBounds);
            }

            MemoryVideo.Stop();
            MemoryVideo.Close();
            MemoryVideo.Source = null;
            MemoryTitleText.Text = string.Empty;

            MemoryLayer.Visibility = Visibility.Collapsed;
            PetSurface.Visibility = Visibility.Visible;

            if (restoreBounds is WindowRectangle initialRestoreBounds)
            {
                // Move back to the original monitor before changing WPF's
                // logical size. This makes the normal 280x260 DIPs resolve
                // against the pet's original DPI instead of the monitor
                // selected by the video player.
                SetExactNativeBounds(initialRestoreBounds);
                await Dispatcher.InvokeAsync(
                    static () => { },
                    DispatcherPriority.ContextIdle);
            }

            Width = NormalPetWidth;
            Height = NormalPetHeight;
            UpdateLayout();

            if (restoreBounds is WindowRectangle exactRestoreBounds)
            {
                await RestoreExactMemoryBoundsAsync(exactRestoreBounds);
            }
        }
        finally
        {
            _memoryPlaybackCancellation.Dispose();
            _memoryPlaybackCancellation = default;
            _memoryPlaybackCompletion = null;
            _memoryVideoSources = null;
            _memoryVideoIndex = 0;
            _memoryNaturalVideoWidth = 0;
            _memoryNaturalVideoHeight = 0;
            _memoryRestoreLatch.Complete();
            _memoryRestoreBounds = null;
            _memoryExitInProgress = false;
            _isMemoryMode = false;
            RefreshInteractionButtonState();
            MemoryModeChanged?.Invoke(
                this,
                new MemoryModeChangedEventArgs(isActive: false));
            RestartPointerFocusIfHovered();
        }
    }

    public void SetInitialPosition(double? left, double? top)
    {
        _initialLeft = left;
        _initialTop = top;

        if (!_isMemoryMode
            && !IsSideDocked
            && new WindowInteropHelper(this).Handle != IntPtr.Zero)
        {
            WindowPlacementService.Restore(this, _initialLeft, _initialTop);
        }
    }

    public void ResetToDefaultPosition()
    {
        if (_isMemoryMode)
        {
            return;
        }

        ExitSideDock(restoreWindow: false);
        WindowPlacementService.Reset(this);
    }

    public WindowPosition GetWindowPosition()
    {
        if (_isMemoryMode
            && _memoryRestoreBounds is WindowRectangle restoreBounds)
        {
            return new WindowPosition(
                restoreBounds.Left,
                restoreBounds.Top);
        }

        if (_sideDockCandidate is { } sideDock)
        {
            return new WindowPosition(
                sideDock.RestoreBounds.Left,
                sideDock.RestoreBounds.Top);
        }

        return WindowPlacementService.GetPosition(this);
    }

    public void SetControlState(
        bool alwaysOnTop,
        bool startWithWindows,
        bool isPetVisible,
        bool tickScoreLoggingEnabled)
    {
        _alwaysOnTop = alwaysOnTop;
        _startWithWindows = startWithWindows;
        _isPetVisible = isPetVisible;
        _tickScoreLoggingEnabled = tickScoreLoggingEnabled;
        if (!isPetVisible)
        {
            CloseInteractionPopupImmediately();
        }

        RefreshContextMenuState();
    }

    public void SetUpdateCheckInProgress(bool isInProgress)
    {
        UpdateMenuItem.IsEnabled = !isInProgress;
        UpdateMenuItem.Header = isInProgress
            ? "正在检查…"
            : "检查新版本";
    }

    public void SetPostcardSummary(
        int unlockedCount,
        int unseenCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unlockedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(unseenCount);
        if (unseenCount > unlockedCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unseenCount),
                "Unseen postcards cannot exceed unlocked postcards.");
        }
    }

    public void SetDiaryUnreadCount(int unreadCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unreadCount);
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => SetDiaryUnreadCount(unreadCount));
            return;
        }

        DiaryMenuItem.Header = unreadCount > 0
            ? $"日记({unreadCount})"
            : "日记";
    }

    public void SetCareState(
        double foodLevel,
        double waterLevel,
        bool interactionBusy)
    {
        ValidateCareLevel(foodLevel, nameof(foodLevel));
        ValidateCareLevel(waterLevel, nameof(waterLevel));
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => SetCareState(
                    foodLevel,
                    waterLevel,
                    interactionBusy));
            return;
        }

        int roundedFood = (int)Math.Round(foodLevel);
        int roundedWater = (int)Math.Round(waterLevel);
        FoodMeterFill.Width = 50d * foodLevel / 100d;
        WaterMeterFill.Width = 50d * waterLevel / 100d;
        FoodMeterText.Text = $"{roundedFood}%";
        WaterMeterText.Text = $"{roundedWater}%";
        FeedInteractionButton.ToolTip =
            $"喂猫条 · 咕噜的小肚子 {roundedFood}%";
        WaterInteractionButton.ToolTip =
            $"添点水 · 咕噜的小水碗 {roundedWater}%";
        AutomationProperties.SetName(
            FeedInteractionButton,
            $"给咕噜喂猫条，小肚子现在 {roundedFood}%");
        AutomationProperties.SetName(
            WaterInteractionButton,
            $"给咕噜添水，小水碗现在 {roundedWater}%");
        _careInteractionBusy = interactionBusy;
        RefreshInteractionButtonState();
    }

    public void SetTravelActionState(
        string actionLabel,
        string? countdown,
        bool isEnabled,
        string? tooltip = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionLabel);
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => SetTravelActionState(
                    actionLabel,
                    countdown,
                    isEnabled,
                    tooltip));
            return;
        }

        TravelInteractionText.Text = actionLabel.Trim();
        TravelCountdownText.Text = countdown?.Trim() ?? string.Empty;
        TravelCountdownText.Visibility =
            string.IsNullOrWhiteSpace(countdown)
                ? Visibility.Collapsed
                : Visibility.Visible;
        TravelInteractionButton.ToolTip = string.IsNullOrWhiteSpace(tooltip)
            ? actionLabel.Trim()
            : tooltip.Trim();
        AutomationProperties.SetName(
            TravelInteractionButton,
            string.IsNullOrWhiteSpace(countdown)
                ? actionLabel.Trim()
                : $"{actionLabel.Trim()}，{countdown.Trim()}");
        _travelActionEnabled = isEnabled;
        RefreshInteractionButtonState();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = HwndSource.FromHwnd(
            new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(OnWindowMessage);
        WindowPlacementService.Restore(this, _initialLeft, _initialTop);
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                if (!_dragging && !IsSideDocked)
                {
                    WindowPlacementService.EnsureVisible(this);
                }
            });
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (_isMemoryMode)
        {
            return;
        }

        if (IsSideDocked)
        {
            SetSideDockHover(hovered: true);
            return;
        }

        OpenInteractionPopup();
        if (!_pointerDown)
        {
            ResumePointerFocus(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_isMemoryMode)
        {
            return;
        }

        if (IsSideDocked)
        {
            SetSideDockHover(hovered: false);
            return;
        }

        ScheduleInteractionPopupClose();
        SuspendPointerFocus();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (WardrobePopup.IsOpen)
        {
            CloseWardrobePopupImmediately(restoreFocus: false);
        }
    }

    private void OnWindowIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
        {
            CloseInteractionPopupImmediately();
            SetSideDockHover(hovered: false);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_isMemoryMode)
        {
            CancelMemoryWindowDrag(notifyPositionChanged: false);
            CompleteMemoryPlayback(MemoryPlaybackOutcome.Cancelled);
            MemoryVideo.Stop();
            MemoryVideo.Close();
        }

        _memoryPlaybackCancellation.Dispose();
        _pointerFocusTimer.Stop();
        _pointerExitGraceTimer.Stop();
        _longPressTimer.Stop();
        _interactionPopupHideTimer.Stop();
        CancelWardrobePopupClose();
        _sideDockBlinkTimer.Stop();
        IsVisibleChanged -= OnWindowIsVisibleChanged;
        InteractionPopupSurface.IsKeyboardFocusWithinChanged -=
            OnInteractionPopupKeyboardFocusWithinChanged;
        WardrobePopupSurface.IsKeyboardFocusWithinChanged -=
            OnWardrobePopupKeyboardFocusWithinChanged;
        _ = BeginInteractionPopupMotionOperation();
        _interactionPopupDismissInProgress = false;
        RemoveInteractionPopupMotionClocks();
        _ = BeginWardrobePopupMotionOperation();
        _wardrobePopupDismissInProgress = false;
        RemoveWardrobePopupMotionClocks();
        ResetWardrobePopupPointerDismissSession();
        WardrobePopupHost.IsHitTestVisible = true;
        WardrobePopupSurface.Opacity = 1;
        WardrobePopupTranslate.Y = 0;
        WardrobePopup.IsOpen = false;
        InteractionPopup.IsOpen = false;
        _windowSource?.RemoveHook(OnWindowMessage);
        _windowSource = null;
        base.OnClosed(e);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (_isMemoryMode)
        {
            if (IsButtonInteraction(e.OriginalSource))
            {
                return;
            }

            BeginMemoryWindowDrag(e);
            e.Handled = true;
            return;
        }

        if (IsButtonInteraction(e.OriginalSource) ||
            IsInteractionPopupInput(e.OriginalSource))
        {
            return;
        }

        if (e.ButtonState != MouseButtonState.Pressed
            || !NativeMethods.GetPhysicalCursorPos(out _pressCursor))
        {
            return;
        }

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _pressAnchorDip = e.GetPosition(this);
        _pointerDown = Mouse.Capture(this, CaptureMode.SubTree);
        _dragging = false;
        _pressBeganSideDocked = IsSideDocked;
        _longPressRaised = false;
        _lastCursor = _pressCursor;
        _pressTimestamp = Stopwatch.GetTimestamp();
        _lastMotionTimestamp = Stopwatch.GetTimestamp();
        _lastVelocity = default;
        if (_pointerDown)
        {
            _pressInteractionSessionId =
                checked(_pressInteractionSessionId + 1);
            CancelPointerFocus();
            _longPressTimer.Stop();
            if (!_pressBeganSideDocked)
            {
                _longPressTimer.Start();
            }
        }

        e.Handled = _pointerDown;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_isMemoryMode)
        {
            if (IsButtonInteraction(e.OriginalSource))
            {
                return;
            }

            MoveMemoryWindow(e);
            e.Handled = true;
            return;
        }

        if (IsInteractionPopupInput(e.OriginalSource))
        {
            return;
        }

        if (!_pointerDown && !IsSideDocked)
        {
            ObserveFocusedPointer(e.GetPosition(this));
        }

        if (!_pointerDown
            || e.LeftButton != MouseButtonState.Pressed
            || !NativeMethods.GetPhysicalCursorPos(
                out NativeMethods.NativePoint cursor))
        {
            return;
        }

        Vector totalDelta = new(
            cursor.X - _pressCursor.X,
            cursor.Y - _pressCursor.Y);

        if (!_dragging && totalDelta.Length < DragThresholdPixels)
        {
            return;
        }

        bool releasedSideDock = IsSideDocked;
        if (releasedSideDock)
        {
            ExitSideDock(restoreWindow: false);
        }

        long now = Stopwatch.GetTimestamp();
        Vector delta = new(cursor.X - _lastCursor.X, cursor.Y - _lastCursor.Y);
        double elapsedSeconds = (now - _lastMotionTimestamp)
            / (double)Stopwatch.Frequency;
        Vector velocity = elapsedSeconds > 0
            ? delta / elapsedSeconds
            : default;

        if (!TryMoveWindowToDragAnchor(
                cursor,
                _pressAnchorDip,
                out PhysicalWindowOrigin windowOrigin))
        {
            if (releasedSideDock)
            {
                WindowPlacementService.EnsureVisible(this);
            }

            return;
        }

        if (!_dragging)
        {
            _dragging = true;
            CloseInteractionPopupImmediately();
            CancelPointerFocus();
            _longPressTimer.Stop();
            DragStarted?.Invoke(
                this,
                CreateMotionEventArgs(
                    cursor,
                    windowOrigin.Left,
                    windowOrigin.Top,
                    delta,
                    totalDelta,
                    velocity,
                    _pressInteractionSessionId));
        }

        _lastCursor = cursor;
        _lastMotionTimestamp = now;
        _lastVelocity = velocity;
        DragMoved?.Invoke(
            this,
            CreateMotionEventArgs(
                cursor,
                windowOrigin.Left,
                windowOrigin.Top,
                delta,
                totalDelta,
                velocity,
                _pressInteractionSessionId));
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_isMemoryMode)
        {
            if (IsButtonInteraction(e.OriginalSource))
            {
                return;
            }

            CancelMemoryWindowDrag(notifyPositionChanged: true);
            e.Handled = true;
            return;
        }

        if (IsInteractionPopupInput(e.OriginalSource))
        {
            return;
        }

        if (!_pointerDown)
        {
            return;
        }

        bool wasDragging = _dragging;
        bool pressBeganSideDocked = _pressBeganSideDocked;
        TimeSpan holdDuration = _pressTimestamp == 0
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(_pressTimestamp);
        CompletePointerGesture(allowSideDock: true);
        if (!wasDragging && !pressBeganSideDocked)
        {
            if (!_longPressRaised && holdDuration >= LongPressThreshold)
            {
                PetLongPressed?.Invoke(
                    this,
                    new PetPressEventArgs(
                        holdDuration,
                        _pressInteractionSessionId));
            }
            else if (!_longPressRaised)
            {
                PetClicked?.Invoke(
                    this,
                    new PetPressEventArgs(
                        holdDuration,
                        _pressInteractionSessionId));
            }
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_isMemoryMode)
        {
            CancelMemoryWindowDrag(notifyPositionChanged: true);
            return;
        }

        if (_pointerDown)
        {
            CompletePointerGesture(allowSideDock: false);
        }
    }

    protected override void OnPreviewMouseRightButtonDown(
        MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonDown(e);
        if (_isMemoryMode)
        {
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseRightButtonUp(
        MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);
        if (_isMemoryMode)
        {
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (_isMemoryMode)
        {
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_isMemoryMode)
        {
            if (e.Key == Key.Escape)
            {
                SkipActiveMemoryPlayback();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2 && !IsSideDocked)
        {
            OpenInteractionPopup(revealImmediately: true);
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                () =>
                {
                    if (InteractionPopup.IsOpen)
                    {
                        _ = Keyboard.Focus(FeedInteractionButton);
                    }
                });
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && WardrobePopup.IsOpen)
        {
            CloseWardrobeAndReturnToTrigger();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && InteractionPopup.IsOpen)
        {
            CloseInteractionPopupImmediately();
            e.Handled = true;
        }
    }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);
        if (_isMemoryMode)
        {
            e.Handled = true;
        }
        else
        {
            CloseInteractionPopupImmediately();
            if (IsSideDocked)
            {
                CancelPointerFocus();
                RefreshInteractionButtonState();
            }
        }
    }

    protected override void OnPreviewTouchDown(TouchEventArgs e)
    {
        base.OnPreviewTouchDown(e);
        if (_isMemoryMode)
        {
            e.Handled = true;
        }
    }

    protected override void OnPreviewTouchMove(TouchEventArgs e)
    {
        base.OnPreviewTouchMove(e);
        if (_isMemoryMode)
        {
            e.Handled = true;
        }
    }

    protected override void OnPreviewTouchUp(TouchEventArgs e)
    {
        base.OnPreviewTouchUp(e);
        if (_isMemoryMode)
        {
            e.Handled = true;
        }
    }

    private void OnMemoryVideoOpened(object sender, RoutedEventArgs e)
    {
        if (!IsMemoryPlaybackActive)
        {
            return;
        }

        if (MemoryVideo.NaturalVideoWidth <= 0
            || MemoryVideo.NaturalVideoHeight <= 0)
        {
            CompleteMemoryPlayback(
                MemoryPlaybackOutcome.Failed,
                new InvalidDataException(
                    "The memory video did not report a valid resolution."));
            return;
        }

        _memoryNaturalVideoWidth = MemoryVideo.NaturalVideoWidth;
        _memoryNaturalVideoHeight = MemoryVideo.NaturalVideoHeight;
        try
        {
            AnchorMemoryWindow();
        }
        catch (Exception error)
        {
            CompleteMemoryPlayback(
                MemoryPlaybackOutcome.Failed,
                error);
        }
    }

    private void OnMemoryVideoEnded(object sender, RoutedEventArgs e)
    {
        if (!IsMemoryPlaybackActive)
        {
            return;
        }

        if (_memoryVideoSources is { } sources
            && _memoryVideoIndex + 1 < sources.Count)
        {
            _memoryVideoIndex++;
            _memoryNaturalVideoWidth = 0;
            _memoryNaturalVideoHeight = 0;
            try
            {
                StartCurrentMemoryVideo();
            }
            catch (Exception error)
            {
                CompleteMemoryPlayback(
                    MemoryPlaybackOutcome.Failed,
                    error);
            }

            return;
        }

        CompleteMemoryPlayback(MemoryPlaybackOutcome.Ended);
    }

    private void OnMemoryVideoFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        if (!IsMemoryPlaybackActive)
        {
            return;
        }

        CompleteMemoryPlayback(
            MemoryPlaybackOutcome.Failed,
            e.ErrorException);
    }

    private void CancelActiveMemoryPlayback()
    {
        if (!_isMemoryMode
            || _memoryPlaybackCompletion?.Task.IsCompleted != false)
        {
            return;
        }

        MemoryVideo.Pause();
        CompleteMemoryPlayback(MemoryPlaybackOutcome.Cancelled);
    }

    private void SkipActiveMemoryPlayback()
    {
        if (!IsMemoryPlaybackActive)
        {
            return;
        }

        MemoryVideo.Pause();
        CompleteMemoryPlayback(MemoryPlaybackOutcome.Skipped);
    }

    private void CompleteMemoryPlayback(
        MemoryPlaybackOutcome outcome,
        Exception? error = null)
    {
        TaskCompletionSource<MemoryPlaybackResult>? completion =
            _memoryPlaybackCompletion;
        if (completion is null
            || !completion.TrySetResult(
                new MemoryPlaybackResult(outcome, error)))
        {
            return;
        }

        _memoryPlaybackCancellation.Dispose();
        _memoryPlaybackCancellation = default;

        if (outcome == MemoryPlaybackOutcome.Ended)
        {
            MemoryPlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
        else if (outcome == MemoryPlaybackOutcome.Failed
                 && CurrentMemoryVideoSource is Uri source)
        {
            MemoryPlaybackFailed?.Invoke(
                this,
                new MemoryPlaybackFailedEventArgs(source, error));
        }
    }

    private void CancelPetInputForMemory()
    {
        _longPressTimer.Stop();
        _pointerDown = false;
        _dragging = false;
        _pressBeganSideDocked = false;
        _longPressRaised = false;
        _pressTimestamp = 0;
        _lastMotionTimestamp = 0;
        _lastVelocity = default;
        CancelPointerFocus();
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private static bool IsButtonInteraction(object source)
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null)
        {
            if (current is WpfButtonBase)
            {
                return true;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool IsInteractionPopupInput(object source)
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, InteractionPopupSurface)
                || ReferenceEquals(current, WardrobePopupHost))
            {
                return true;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private Uri? CurrentMemoryVideoSource =>
        _memoryVideoSources is { } sources
        && _memoryVideoIndex >= 0
        && _memoryVideoIndex < sources.Count
            ? sources[_memoryVideoIndex]
            : null;

    private bool IsMemoryPlaybackActive =>
        _isMemoryMode
        && _memoryPlaybackCompletion?.Task.IsCompleted == false;

    private void StartCurrentMemoryVideo()
    {
        Uri source = CurrentMemoryVideoSource
            ?? throw new InvalidOperationException(
                "The memory video sequence has no current segment.");
        MemoryVideo.Stop();
        MemoryVideo.Source = source;
        MemoryVideo.Position = TimeSpan.Zero;
        MemoryVideo.Play();
    }

    private void BeginMemoryWindowDrag(MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed
            || !NativeMethods.GetPhysicalCursorPos(
                out _memoryPressCursor))
        {
            return;
        }

        _memoryPressAnchorDip = e.GetPosition(this);
        _memoryPointerDown =
            Mouse.Capture(this, CaptureMode.SubTree);
        _memoryDragging = false;
    }

    private void MoveMemoryWindow(MouseEventArgs e)
    {
        if (!_memoryPointerDown
            || e.LeftButton != MouseButtonState.Pressed
            || !NativeMethods.GetPhysicalCursorPos(
                out NativeMethods.NativePoint cursor))
        {
            return;
        }

        var totalDelta = new Vector(
            cursor.X - _memoryPressCursor.X,
            cursor.Y - _memoryPressCursor.Y);
        if (!_memoryDragging
            && totalDelta.Length < DragThresholdPixels)
        {
            return;
        }

        if (!TryMoveWindowToDragAnchor(
                cursor,
                _memoryPressAnchorDip,
                out _))
        {
            return;
        }

        _memoryDragging = true;
        UpdateMemoryRestoreBoundsFromCurrentWindow();
    }

    private void CancelMemoryWindowDrag(bool notifyPositionChanged)
    {
        bool moved = _memoryDragging;
        _memoryPointerDown = false;
        _memoryDragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (!moved)
        {
            return;
        }

        WindowPlacementService.EnsureVisible(this);
        UpdateMemoryRestoreBoundsFromCurrentWindow();
        if (!_memoryExitInProgress)
        {
            AnchorMemoryWindow();
            UpdateMemoryRestoreBoundsFromCurrentWindow();
        }

        if (notifyPositionChanged)
        {
            MemoryPositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AnchorMemoryWindow()
    {
        if (_memoryRestoreBounds is not WindowRectangle restoreBounds)
        {
            throw new InvalidOperationException(
                "The original pet window bounds were not captured.");
        }

        if (_memoryNaturalVideoWidth <= 0
            || _memoryNaturalVideoHeight <= 0)
        {
            return;
        }

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "PetWindow does not have a native handle.");
        }

        WindowRectangle workArea = GetMemoryWorkArea(restoreBounds);
        WindowRectangle memoryBounds =
            MemoryVideoPlacement.FitVideoAroundAnchor(
                restoreBounds,
                workArea,
                _memoryNaturalVideoWidth,
                _memoryNaturalVideoHeight);
        NativeMethods.MonitorDpi dpi =
            NativeMethods.GetWindowDpiOrDefault(handle);
        double deviceScaleX = dpi.X / 96d;
        double deviceScaleY = dpi.Y / 96d;

        MemoryLayer.LayoutTransform = Transform.Identity;
        Width = memoryBounds.Width / deviceScaleX;
        Height = memoryBounds.Height / deviceScaleY;
        UpdateLayout();
        SetExactNativeBounds(memoryBounds);
    }

    private WindowRectangle GetMemoryWorkArea(
        WindowRectangle fallbackBounds)
    {
        WindowDisplayDiagnostics diagnostics =
            WindowPlacementService.GetDiagnostics(this);
        return diagnostics.WorkArea
            ?? new WindowRectangle(
                fallbackBounds.Left,
                fallbackBounds.Top,
                Math.Max(fallbackBounds.Width, _memoryNaturalVideoWidth),
                Math.Max(fallbackBounds.Height, _memoryNaturalVideoHeight));
    }

    private void UpdateMemoryRestoreBoundsFromCurrentWindow()
    {
        if (_memoryRestoreBounds is not WindowRectangle restoreBounds)
        {
            return;
        }

        WindowRectangle videoBounds =
            WindowPlacementService.GetRectangle(this);
        WindowRectangle workArea =
            GetMemoryWorkArea(videoBounds);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        NativeMethods.MonitorDpi dpi =
            NativeMethods.GetWindowDpiOrDefault(handle);
        _memoryRestoreBounds =
            MemoryVideoPlacement.CenterPetOnVideoAtDpi(
                videoBounds,
                workArea,
                NormalPetWidth,
                NormalPetHeight,
                dpi.X,
                dpi.Y);
    }

    private void SetExactNativeBounds(WindowRectangle bounds)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero
            || !NativeMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeMethods.SwpNoZOrder
                    | NativeMethods.SwpNoActivate))
        {
            throw new Win32Exception(
                "Could not apply the memory presentation window bounds.");
        }
    }

    private async Task RestoreExactMemoryBoundsAsync(
        WindowRectangle restoreBounds)
    {
        WindowRectangle actualBounds =
            WindowPlacementService.GetRectangle(this);
        for (int pass = 0;
             pass < MemoryRestoreMaxSettlePasses;
             pass++)
        {
            SetExactNativeBounds(restoreBounds);

            // ContextIdle runs after Render, Loaded, and Input work already
            // queued by SetWindowPos/WM_DPICHANGED. Waiting here is essential:
            // a Render-only wait can finish before our Input-priority DPI
            // callback and let it run after the restore guard is cleared.
            await Dispatcher.InvokeAsync(
                static () => { },
                DispatcherPriority.ContextIdle);

            actualBounds = WindowPlacementService.GetRectangle(this);
            if (_memoryRestoreLatch.ObserveSettledBounds(
                    actualBounds,
                    _dpiReanchorPending))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "PetWindow did not settle at its pre-memory physical bounds. "
            + $"Expected {restoreBounds}; actual {actualBounds}.");
    }

    private void CompletePointerGesture(bool allowSideDock)
    {
        bool wasDragging = _dragging;
        _longPressTimer.Stop();
        _pointerDown = false;
        _dragging = false;
        _pressBeganSideDocked = false;
        _pressTimestamp = 0;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (wasDragging)
        {
            NativeMethods.NativePoint cursor =
                NativeMethods.GetPhysicalCursorPos(
                    out NativeMethods.NativePoint currentCursor)
                    ? currentCursor
                    : _lastCursor;
            Vector totalDelta = new(
                cursor.X - _pressCursor.X,
                cursor.Y - _pressCursor.Y);
            Vector delta = new(
                cursor.X - _lastCursor.X,
                cursor.Y - _lastCursor.Y);

            SideDockCandidate? sideDockCandidate = null;
            if (allowSideDock)
            {
                _ = WindowPlacementService.TryCreateSideDockCandidate(
                    this,
                    cursor,
                    out sideDockCandidate);
            }

            if (sideDockCandidate is not { } candidate ||
                !WindowPlacementService.TryMoveTo(
                    this,
                    candidate.RestoreBounds))
            {
                WindowPlacementService.EnsureVisible(this);
            }

            WindowPosition position = WindowPlacementService.GetPosition(this);
            DragCompleted?.Invoke(
                this,
                new PetDragMotionEventArgs(
                    new Point(cursor.X, cursor.Y),
                    new Point(position.Left, position.Top),
                    delta,
                    totalDelta,
                    _lastVelocity,
                    _pressInteractionSessionId,
                    sideDockCandidate));
        }

        RestartPointerFocusIfHovered();
    }

    private bool TryMoveWindowToDragAnchor(
        NativeMethods.NativePoint cursor,
        Point anchorDip,
        out PhysicalWindowOrigin origin)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            origin = default;
            return false;
        }

        NativeMethods.MonitorDpi dpi =
            NativeMethods.GetWindowDpiOrDefault(handle);
        origin = PhysicalDragPlacement.Calculate(
            cursor.X,
            cursor.Y,
            anchorDip.X,
            anchorDip.Y,
            dpi.X,
            dpi.Y);
        if (!MoveWindow(handle, origin))
        {
            return false;
        }

        NativeMethods.MonitorDpi updatedDpi =
            NativeMethods.GetWindowDpiOrDefault(handle);
        if (updatedDpi != dpi)
        {
            origin = PhysicalDragPlacement.Calculate(
                cursor.X,
                cursor.Y,
                anchorDip.X,
                anchorDip.Y,
                updatedDpi.X,
                updatedDpi.Y);
            return MoveWindow(handle, origin);
        }

        return true;
    }

    private static bool MoveWindow(
        IntPtr handle,
        PhysicalWindowOrigin origin) =>
        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            origin.Left,
            origin.Top,
            0,
            0,
            NativeMethods.SwpNoSize
                | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate);

    private IntPtr OnWindowMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message == NativeMethods.WmDpiChanged)
        {
            _memoryRestoreLatch.NoteDpiChange();
            if (_dpiReanchorPending)
            {
                return IntPtr.Zero;
            }

            _dpiReanchorPending = true;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                () =>
                {
                    try
                    {
                        if (_memoryRestoreLatch.TargetBounds
                            is WindowRectangle restoreBounds)
                        {
                            // The exit path owns placement until the original
                            // physical rectangle has survived two idle passes.
                            // In particular, never call EnsureVisible for a
                            // delayed DPI callback from the video window.
                            SetExactNativeBounds(restoreBounds);
                        }
                        else if (_isMemoryMode)
                        {
                            if (!_memoryExitInProgress)
                            {
                                try
                                {
                                    if (_memoryPointerDown
                                        && _memoryDragging
                                        && NativeMethods
                                            .GetPhysicalCursorPos(
                                                out NativeMethods.NativePoint
                                                    memoryCursor))
                                    {
                                        TryMoveWindowToDragAnchor(
                                            memoryCursor,
                                            _memoryPressAnchorDip,
                                            out _);
                                        UpdateMemoryRestoreBoundsFromCurrentWindow();
                                    }

                                    AnchorMemoryWindow();
                                }
                                catch (Exception error)
                                {
                                    CompleteMemoryPlayback(
                                        MemoryPlaybackOutcome.Failed,
                                        error);
                                }
                            }
                        }
                        else if (IsSideDocked)
                        {
                            RefreshSideDockPlacement();
                        }
                        else if (_dragging
                            && NativeMethods.GetPhysicalCursorPos(
                                out NativeMethods.NativePoint cursor))
                        {
                            TryMoveWindowToDragAnchor(
                                cursor,
                                _pressAnchorDip,
                                out _);
                        }
                        else
                        {
                            WindowPlacementService.EnsureVisible(this);
                        }

                        if (WardrobePopup.IsOpen)
                        {
                            PositionWardrobePopup();
                            RefreshWardrobePopupItems();
                        }
                    }
                    finally
                    {
                        _dpiReanchorPending = false;
                    }
                });
        }
        else if (message is NativeMethods.WmDisplayChange or
                 NativeMethods.WmSettingChange)
        {
            ScheduleSideDockRefresh();
            ScheduleWardrobePopupReposition();
        }

        return IntPtr.Zero;
    }

    private void OnLongPressThresholdReached(
        object? sender,
        EventArgs e)
    {
        _longPressTimer.Stop();
        if (_isMemoryMode
            || IsSideDocked
            || !_pointerDown
            || _dragging
            || _longPressRaised)
        {
            return;
        }

        _longPressRaised = true;
        TimeSpan holdDuration = _pressTimestamp == 0
            ? LongPressThreshold
            : Stopwatch.GetElapsedTime(_pressTimestamp);
        PetLongPressed?.Invoke(
            this,
            new PetPressEventArgs(
                holdDuration,
                _pressInteractionSessionId));
    }

    private void OnPointerFocusTick(object? sender, EventArgs e)
    {
        if (_isMemoryMode || IsSideDocked)
        {
            CancelPointerFocus();
            return;
        }

        if (!IsMouseOver)
        {
            SuspendPointerFocus();
            return;
        }

        if (!_pointerDown)
        {
            ObserveFocusedPointer(Mouse.GetPosition(this));
        }
    }

    private void OnPointerExitGraceElapsed(object? sender, EventArgs e)
    {
        _pointerExitGraceTimer.Stop();
        if (_isMemoryMode || IsSideDocked)
        {
            CancelPointerFocus();
            return;
        }

        if (IsMouseOver && !_pointerDown)
        {
            ResumePointerFocus(Mouse.GetPosition(this));
            return;
        }

        EndPointerFocus();
    }

    private void ObserveFocusedPointer(Point position)
    {
        if (_focusTimestamp == 0)
        {
            BeginPointerFocus(position);
            return;
        }

        foreach (PetPointerGesture gesture in
                 _pointerRecognizer.Observe(CreatePointerSample(position)))
        {
            PointerGestureRecognized?.Invoke(
                this,
                new PetPointerGestureEventArgs(
                    gesture,
                    _pointerFocusSessionId));
        }
    }

    private void EndPointerFocus()
    {
        _pointerFocusTimer.Stop();
        _pointerExitGraceTimer.Stop();
        _focusTimestamp = 0;
        foreach (PetPointerGesture gesture in _pointerRecognizer.End())
        {
            PointerGestureRecognized?.Invoke(
                this,
                new PetPointerGestureEventArgs(
                    gesture,
                    _pointerFocusSessionId));
        }
    }

    private void CancelPointerFocus()
    {
        _pointerFocusTimer.Stop();
        _pointerExitGraceTimer.Stop();
        _focusTimestamp = 0;
        _pointerRecognizer.Cancel();
    }

    private void BeginPointerFocus(Point position)
    {
        if (_isMemoryMode || IsSideDocked)
        {
            return;
        }

        CancelPointerFocus();
        _pointerFocusSessionId = checked(_pointerFocusSessionId + 1);
        _focusTimestamp = Stopwatch.GetTimestamp();
        _pointerRecognizer.Begin(CreatePointerSample(position));
        _pointerFocusTimer.Start();
    }

    private void SuspendPointerFocus()
    {
        _pointerFocusTimer.Stop();
        if (_focusTimestamp == 0 || _pointerDown)
        {
            return;
        }

        _pointerExitGraceTimer.Stop();
        _pointerExitGraceTimer.Start();
    }

    private void ResumePointerFocus(Point position)
    {
        if (_focusTimestamp == 0)
        {
            BeginPointerFocus(position);
            return;
        }

        _pointerExitGraceTimer.Stop();
        _pointerRecognizer.Resume(CreatePointerSample(position));
        _pointerFocusTimer.Start();
    }

    private void RestartPointerFocusIfHovered()
    {
        if (!_isMemoryMode && !IsSideDocked && IsMouseOver && !_pointerDown)
        {
            BeginPointerFocus(Mouse.GetPosition(this));
        }
    }

    private PetPointerSample CreatePointerSample(Point position) =>
        new(
            _focusTimestamp == 0
                ? TimeSpan.Zero
                : Stopwatch.GetElapsedTime(_focusTimestamp),
            position.X,
            position.Y,
            Math.Max(ActualWidth, 1),
            Math.Max(ActualHeight, 1));

    private void InitializeInteractionAssets()
    {
        ImageSource? feed = LoadInteractionAsset("feed.png");
        ImageSource? water = LoadInteractionAsset("water.png");
        FeedInteractionIcon.Source = feed;
        WaterInteractionIcon.Source = water;
        TravelInteractionIcon.Source = LoadInteractionAsset("travel.png");
        PetInteractionIcon.Source = LoadInteractionAsset("pet.png");
        WardrobeInteractionIcon.Source = LoadInteractionAsset("wardrobe.png");
    }

    private static ImageSource? LoadInteractionAsset(string fileName)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Interactions",
            fileName);
        if (!File.Exists(path))
        {
            Trace.TraceWarning(
                "Interaction asset is missing: {0}",
                path);
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error)
        {
            Trace.TraceWarning(
                "Interaction asset could not be loaded ({0}): {1}",
                path,
                error.Message);
            return null;
        }
    }

    private void InitializeSideDockAssets()
    {
        foreach (SideDockEdge edge in Enum.GetValues<SideDockEdge>())
        {
            foreach (bool hovered in new[] { false, true })
            {
                foreach (bool blinkClosed in new[] { false, true })
                {
                    string fileName = ResolveSideDockAssetFileName(
                        edge,
                        hovered,
                        blinkClosed);
                    _sideDockAssets[fileName] = LoadSideDockAsset(fileName);
                }
            }
        }
    }

    private static ImageSource? LoadSideDockAsset(string fileName)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Docking",
            fileName);
        if (!File.Exists(path))
        {
            Trace.TraceWarning("Side-dock asset is missing: {0}", path);
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error)
        {
            Trace.TraceWarning(
                "Side-dock asset could not be loaded ({0}): {1}",
                path,
                error.Message);
            return null;
        }
    }

    private void OnSideDockBlinkElapsed(object? sender, EventArgs e) =>
        AdvanceSideDockBlink();

    private void AdvanceSideDockBlink()
    {
        if (!IsSideDocked)
        {
            _sideDockBlinkTimer.Stop();
            return;
        }

        _sideDockBlinkClosed = !_sideDockBlinkClosed;
        if (!TryApplySideDockFrame())
        {
            ExitSideDock();
            return;
        }

        _sideDockBlinkTimer.Interval = _sideDockBlinkClosed
            ? SideDockBlinkClosedDuration
            : SideDockBlinkOpenDuration;
    }

    private void SetSideDockHover(bool hovered)
    {
        if (!IsSideDocked || _sideDockHovered == hovered)
        {
            return;
        }

        _sideDockHovered = hovered;
        if (!TryApplySideDockFrame())
        {
            ExitSideDock();
        }
    }

    private bool HasCompleteSideDockAssetSet(SideDockEdge edge) =>
        new[] { false, true }.All(
            hovered => new[] { false, true }.All(
                blinkClosed =>
                    _sideDockAssets.TryGetValue(
                        ResolveSideDockAssetFileName(
                            edge,
                            hovered,
                            blinkClosed),
                        out ImageSource? source) &&
                    source is not null));

    private bool TryApplySideDockFrame()
    {
        if (_sideDockCandidate is not { } candidate)
        {
            return false;
        }

        string fileName = ResolveSideDockAssetFileName(
            candidate.Edge,
            _sideDockHovered,
            _sideDockBlinkClosed);
        if (!_sideDockAssets.TryGetValue(fileName, out ImageSource? source) ||
            source is null)
        {
            Trace.TraceWarning(
                "Required side-dock asset is unavailable: {0}",
                fileName);
            return false;
        }

        SideDockImage.Source = source;
        _currentSideDockAssetFileName = fileName;
        return true;
    }

    private void ResetSideDockPresentation()
    {
        _sideDockBlinkTimer.Stop();
        _sideDockCandidate = null;
        _sideDockHovered = false;
        _sideDockBlinkClosed = false;
        _currentSideDockAssetFileName = null;
        SideDockImage.Source = null;
        SideDockImage.Visibility = Visibility.Collapsed;
        PetImage.Visibility = Visibility.Visible;
    }

    private void RefreshSideDockPlacement()
    {
        if (_sideDockCandidate is not { } current)
        {
            return;
        }

        if (!WindowPlacementService.TryRefreshSideDockCandidate(
                this,
                current,
                out SideDockCandidate? refreshed) ||
            refreshed is null)
        {
            ExitSideDock();
            return;
        }

        _sideDockCandidate = refreshed;
        if (!WindowPlacementService.TryMoveTo(this, refreshed.DockBounds))
        {
            ExitSideDock();
            return;
        }

        if (!TryApplySideDockFrame())
        {
            ExitSideDock();
        }
    }

    private void ScheduleSideDockRefresh()
    {
        if (!IsSideDocked || _sideDockRefreshPending)
        {
            return;
        }

        _sideDockRefreshPending = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            () =>
            {
                try
                {
                    if (IsSideDocked && !_isMemoryMode)
                    {
                        RefreshSideDockPlacement();
                    }
                }
                finally
                {
                    _sideDockRefreshPending = false;
                }
            });
    }

    private void OpenInteractionPopup(bool revealImmediately = false)
    {
        if (_isMemoryMode
            || IsSideDocked
            || !_isPetVisible
            || !IsVisible
            || _pointerDown)
        {
            return;
        }

        if (WardrobePopup.IsOpen)
        {
            CloseWardrobePopupImmediately(restoreFocus: false);
        }

        _interactionPopupHideTimer.Stop();
        if (InteractionPopup.IsOpen)
        {
            if (revealImmediately || !ShouldAnimateInteractionPopup())
            {
                RevealInteractionPopupImmediately();
            }
            else if (_interactionPopupDismissInProgress)
            {
                long generation = BeginInteractionPopupMotionOperation();
                _interactionPopupDismissInProgress = false;
                BeginInteractionPopupRevealAnimation(generation);
            }

            return;
        }

        long openGeneration = BeginInteractionPopupMotionOperation();
        _interactionPopupDismissInProgress = false;
        PrepareInteractionPopupForOpen();
        InteractionPopup.IsOpen = true;
        if (revealImmediately || !ShouldAnimateInteractionPopup())
        {
            CommitInteractionPopupReveal(openGeneration);
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            () => BeginInteractionPopupReveal(openGeneration));
    }

    internal static CustomPopupPlacement[] PlaceInteractionPopup(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        double top = offset.Y - InteractionPopupShadowPadding;
        double right = targetSize.Width
            - InteractionPopupPetOverlap
            - InteractionPopupShadowPadding
            + offset.X;
        double left = -popupSize.Width
            + InteractionPopupShadowPadding
            + InteractionPopupPetOverlap
            + offset.X;
        return
        [
            new CustomPopupPlacement(
                new Point(right, top),
                PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(
                new Point(left, top),
                PopupPrimaryAxis.Horizontal),
        ];
    }

    private void BeginInteractionPopupReveal(long generation)
    {
        if (!IsCurrentInteractionPopupMotion(generation)
            || !InteractionPopup.IsOpen
            || _interactionPopupDismissInProgress)
        {
            return;
        }

        _interactionPopupApproachOffset =
            ResolveInteractionPopupApproachOffset();
        RemoveInteractionPopupMotionClocks();
        InteractionPopupSurface.Opacity = 0;
        InteractionPopupTranslate.X = _interactionPopupApproachOffset;
        BeginInteractionPopupRevealAnimation(generation);
    }

    private void BeginInteractionPopupRevealAnimation(long generation)
    {
        var opacityAnimation = GuluMotion.SplineTo(
            1,
            GuluMotion.QuickEntrance);
        var translateAnimation = GuluMotion.SplineTo(
            0,
            GuluMotion.QuickEntrance);
        translateAnimation.Completed += (_, _) =>
        {
            CommitInteractionPopupReveal(generation);
        };
        InteractionPopupSurface.BeginAnimation(
            OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);
        InteractionPopupTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            translateAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void CommitInteractionPopupReveal(long generation)
    {
        if (!IsCurrentInteractionPopupMotion(generation)
            || !InteractionPopup.IsOpen
            || _interactionPopupDismissInProgress)
        {
            return;
        }

        RemoveInteractionPopupMotionClocks();
        InteractionPopupSurface.Opacity = 1;
        InteractionPopupTranslate.X = 0;
    }

    private void RevealInteractionPopupImmediately()
    {
        long generation = BeginInteractionPopupMotionOperation();
        _interactionPopupDismissInProgress = false;
        CommitInteractionPopupChildren();
        CommitInteractionPopupReveal(generation);
    }

    private void PrepareInteractionPopupForOpen()
    {
        RemoveInteractionPopupMotionClocks();
        CommitInteractionPopupChildren();
        InteractionPopupSurface.Opacity = 0;
        InteractionPopupTranslate.X = 0;
    }

    private void ResetInteractionPopupMotionVisuals()
    {
        _interactionPopupDismissInProgress = false;
        RemoveInteractionPopupMotionClocks();
        CommitInteractionPopupChildren();
        InteractionPopupSurface.Opacity = 0;
        InteractionPopupTranslate.X = 0;
    }

    private void CommitInteractionPopupChildren()
    {
        CareMeterPanel.BeginAnimation(OpacityProperty, null);
        CareMeterPanel.Opacity = 1;
        foreach (FrameworkElement button in new FrameworkElement[]
                 {
                     FeedInteractionButton,
                     WaterInteractionButton,
                     TravelInteractionButton,
                     PetInteractionButton,
                     WardrobeInteractionButton,
                 })
        {
            button.BeginAnimation(OpacityProperty, null);
            button.Opacity = 1;
            button.IsHitTestVisible = true;
        }
    }

    private void ScheduleInteractionPopupClose()
    {
        if (!InteractionPopup.IsOpen)
        {
            return;
        }

        _interactionPopupHideTimer.Stop();
        _interactionPopupHideMotionGeneration =
            _interactionPopupMotionGeneration;
        _interactionPopupHideTimer.Start();
    }

    private void DismissInteractionPopup()
    {
        _interactionPopupHideTimer.Stop();
        bool restoreKeyboardFocus =
            InteractionPopupSurface.IsKeyboardFocusWithin;
        if (!InteractionPopup.IsOpen)
        {
            return;
        }

        if (!ShouldAnimateInteractionPopup())
        {
            CloseInteractionPopupImmediately(
                restoreKeyboardFocus,
                closeWardrobe: false);
            return;
        }

        long generation = BeginInteractionPopupMotionOperation();
        _interactionPopupDismissInProgress = true;
        _interactionPopupApproachOffset =
            ResolveInteractionPopupApproachOffset();
        var opacityAnimation = GuluMotion.SplineTo(
            0,
            GuluMotion.QuickExit);
        var translateAnimation = GuluMotion.SplineTo(
            _interactionPopupApproachOffset,
            GuluMotion.QuickExit);
        translateAnimation.Completed += (_, _) =>
        {
            if (!IsCurrentInteractionPopupMotion(generation)
                || !InteractionPopup.IsOpen
                || !_interactionPopupDismissInProgress)
            {
                return;
            }

            RemoveInteractionPopupMotionClocks();
            InteractionPopupSurface.Opacity = 0;
            InteractionPopupTranslate.X =
                _interactionPopupApproachOffset;
            _interactionPopupDismissInProgress = false;
            InteractionPopup.IsOpen = false;
            RestoreInteractionPopupKeyboardFocus(restoreKeyboardFocus);
        };
        InteractionPopupSurface.BeginAnimation(
            OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);
        InteractionPopupTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            translateAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void CloseInteractionPopupImmediately(
        bool restoreKeyboardFocus = true,
        bool closeWardrobe = true)
    {
        _interactionPopupHideTimer.Stop();
        bool hadKeyboardFocus = restoreKeyboardFocus
            && (InteractionPopupSurface.IsKeyboardFocusWithin
                || WardrobePopupSurface.IsKeyboardFocusWithin);
        if (closeWardrobe)
        {
            CloseWardrobePopupImmediately(restoreFocus: false);
        }

        _interactionPopupHideTimer.Stop();
        _ = BeginInteractionPopupMotionOperation();
        _interactionPopupDismissInProgress = false;
        RemoveInteractionPopupMotionClocks();
        InteractionPopupSurface.Opacity = 0;
        InteractionPopupTranslate.X = 0;
        InteractionPopup.IsOpen = false;
        RestoreInteractionPopupKeyboardFocus(hadKeyboardFocus);
    }

    private void RestoreInteractionPopupKeyboardFocus(bool restoreKeyboardFocus)
    {
        if (restoreKeyboardFocus
            && IsVisible
            && _isPetVisible
            && !_isMemoryMode
            && !IsSideDocked)
        {
            _ = Keyboard.Focus(this);
        }
    }

    private long BeginInteractionPopupMotionOperation() =>
        ++_interactionPopupMotionGeneration;

    private bool IsCurrentInteractionPopupMotion(long generation) =>
        generation == _interactionPopupMotionGeneration;

    private bool ShouldAnimateInteractionPopup() =>
        _interactionPopupAnimationsEnabledForTest
        ?? GuluMotion.ShouldAnimate(
            SystemParameters.ClientAreaAnimation,
            SystemParameters.HighContrast);

    private double ResolveInteractionPopupApproachOffset()
    {
        try
        {
            Point petLeft = PetSurface.PointToScreen(new Point(0, 0));
            Point petRight = PetSurface.PointToScreen(
                new Point(PetSurface.ActualWidth, 0));
            Point popupLeft = InteractionPopupSurface.PointToScreen(
                new Point(0, 0));
            Point popupRight = InteractionPopupSurface.PointToScreen(
                new Point(InteractionPopupSurface.ActualWidth, 0));
            return GuluMotion.ResolveHorizontalApproachOffset(
                petLeft.X,
                petRight.X,
                popupLeft.X,
                popupRight.X);
        }
        catch (InvalidOperationException)
        {
            return _interactionPopupApproachOffset;
        }
    }

    private void RemoveInteractionPopupMotionClocks()
    {
        InteractionPopupSurface.BeginAnimation(OpacityProperty, null);
        InteractionPopupTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            null);
    }

    private void OnInteractionPopupHideElapsed(object? sender, EventArgs e)
    {
        _interactionPopupHideTimer.Stop();
        if (!IsCurrentInteractionPopupMotion(
                _interactionPopupHideMotionGeneration)
            || !InteractionPopup.IsOpen
            || IsMouseOver
            || InteractionPopupHost.IsMouseOver
            || InteractionPopupSurface.IsKeyboardFocusWithin)
        {
            return;
        }

        DismissInteractionPopup();
    }

    private void OnInteractionPopupMouseEnter(
        object sender,
        MouseEventArgs e)
    {
        _interactionPopupHideTimer.Stop();
        if (_interactionPopupDismissInProgress)
        {
            OpenInteractionPopup();
        }

        CancelPointerFocus();
    }

    private void OnInteractionPopupMouseLeave(
        object sender,
        MouseEventArgs e)
    {
        ScheduleInteractionPopupClose();
    }

    private void OnInteractionPopupKeyboardFocusWithinChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _interactionPopupHideTimer.Stop();
            if (_interactionPopupDismissInProgress)
            {
                OpenInteractionPopup();
            }

            return;
        }

        if (!IsMouseOver
            && !InteractionPopupHost.IsMouseOver
            && !WardrobePopupHost.IsMouseOver
            && !WardrobePopupSurface.IsKeyboardFocusWithin)
        {
            ScheduleInteractionPopupClose();
        }
    }

    private void OnInteractionPopupPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        CloseInteractionPopupImmediately();
        e.Handled = true;
    }

    private void RefreshInteractionButtonState()
    {
        bool careEnabled =
            !_careInteractionBusy && !_isMemoryMode && !IsSideDocked;
        FeedInteractionButton.IsEnabled = careEnabled;
        WaterInteractionButton.IsEnabled = careEnabled;
        PetInteractionButton.IsEnabled = careEnabled;
        TravelInteractionButton.IsEnabled =
            careEnabled && _travelActionEnabled;
        bool wardrobeEnabled = !_isMemoryMode && !IsSideDocked;
        WardrobeInteractionButton.IsEnabled = wardrobeEnabled;
    }

    private void OnFeedInteractionClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsSideDocked)
        {
            FeedRequested?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }

    private void OnWaterInteractionClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsSideDocked)
        {
            WaterRequested?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }

    private void OnTravelInteractionClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsSideDocked)
        {
            TravelRequested?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }

    private void OnPetInteractionClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsSideDocked)
        {
            PettingRequested?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }

    private void OnWardrobeInteractionClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_isMemoryMode || IsSideDocked)
        {
            e.Handled = true;
            return;
        }

        _interactionPopupHideTimer.Stop();
        CancelWardrobePopupClose();
        PetContextMenu.IsOpen = false;
        if (WardrobePopup.IsOpen)
        {
            if (_wardrobePopupDismissInProgress)
            {
                OpenWardrobePopup();
            }
            else
            {
                DismissWardrobePopup(restoreFocus: true);
            }
        }
        else
        {
            OpenWardrobePopup();
        }

        e.Handled = true;
    }

    private void OpenWardrobePopup()
    {
        if (_wardrobeMenuState is null
            || _isMemoryMode
            || IsSideDocked
            || !_isPetVisible
            || !IsVisible)
        {
            if (WardrobePopup.IsOpen)
            {
                CloseWardrobePopupImmediately(restoreFocus: false);
            }

            return;
        }

        _interactionPopupHideTimer.Stop();
        CancelWardrobePopupClose();
        PetContextMenu.IsOpen = false;
        CloseInteractionPopupImmediately(
            restoreKeyboardFocus: false,
            closeWardrobe: false);

        PositionWardrobePopup();
        RefreshWardrobePopupItems();
        if (WardrobePopup.IsOpen)
        {
            BeginWardrobePopupPointerDismissSession();
            long reopenGeneration = BeginWardrobePopupMotionOperation();
            _wardrobePopupDismissInProgress = false;
            WardrobePopupHost.IsHitTestVisible = true;
            BeginWardrobePopupRevealAnimation(reopenGeneration);
            QueueInitialWardrobePopupFocus(reopenGeneration);
            return;
        }

        _ = BeginWardrobePopupMotionOperation();
        _wardrobePopupDismissInProgress = false;
        BeginWardrobePopupPointerDismissSession();
        WardrobePopupHost.IsHitTestVisible = true;
        PrepareWardrobePopupReveal();
        WardrobePopup.IsOpen = true;
    }

    private void CloseWardrobeAndReturnToTrigger()
    {
        CloseWardrobePopupImmediately(restoreFocus: false);
        OpenInteractionPopup(revealImmediately: true);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (InteractionPopup.IsOpen
                    && WardrobeInteractionButton.IsEnabled)
                {
                    _ = Keyboard.Focus(WardrobeInteractionButton);
                }
            });
    }

    private void DismissWardrobePopup(bool restoreFocus)
    {
        CancelWardrobePopupClose();
        bool hadKeyboardFocus =
            WardrobePopupSurface.IsKeyboardFocusWithin;
        if (!WardrobePopup.IsOpen)
        {
            return;
        }

        if (_wardrobePopupDismissInProgress)
        {
            return;
        }

        if (!ShouldAnimateWardrobePopup())
        {
            CloseWardrobePopupImmediately(restoreFocus);
            return;
        }

        long generation = BeginWardrobePopupMotionOperation();
        _wardrobePopupDismissInProgress = true;
        WardrobePopupHost.IsHitTestVisible = false;
        double targetY = ResolveWardrobePopupDismissOffset(
            _wardrobePopupPlacementSide);
        var opacityAnimation = GuluMotion.SplineTo(
            0,
            GuluMotion.QuickExit);
        var translateAnimation = GuluMotion.SplineTo(
            targetY,
            GuluMotion.QuickExit);
        translateAnimation.Completed += (_, _) =>
        {
            CommitWardrobePopupDismiss(
                generation,
                targetY,
                restoreFocus && hadKeyboardFocus);
        };
        WardrobePopupSurface.BeginAnimation(
            OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);
        WardrobePopupTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            translateAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void CommitWardrobePopupDismiss(
        long generation,
        double targetY,
        bool restoreKeyboardFocus)
    {
        if (!IsCurrentWardrobePopupMotion(generation)
            || !WardrobePopup.IsOpen
            || !_wardrobePopupDismissInProgress)
        {
            return;
        }

        RemoveWardrobePopupMotionClocks();
        WardrobePopupSurface.Opacity = 0;
        WardrobePopupTranslate.Y = targetY;
        _wardrobePopupDismissInProgress = false;
        ResetWardrobePopupPointerDismissSession();
        WardrobePopupHost.IsHitTestVisible = true;
        WardrobePopup.IsOpen = false;
        WardrobePopupSurface.Opacity = 1;
        WardrobePopupTranslate.Y = 0;
        RestoreWardrobePopupKeyboardFocus(restoreKeyboardFocus);
    }

    private void CloseWardrobePopupImmediately(bool restoreFocus)
    {
        CancelWardrobePopupClose();
        bool hadKeyboardFocus = restoreFocus
            && WardrobePopupSurface.IsKeyboardFocusWithin;
        _ = BeginWardrobePopupMotionOperation();
        _wardrobePopupDismissInProgress = false;
        ResetWardrobePopupPointerDismissSession();
        RemoveWardrobePopupMotionClocks();
        WardrobePopupSurface.Opacity = 1;
        WardrobePopupTranslate.Y = 0;
        WardrobePopupHost.IsHitTestVisible = true;
        if (WardrobePopup.IsOpen)
        {
            WardrobePopup.IsOpen = false;
        }

        RestoreWardrobePopupKeyboardFocus(hadKeyboardFocus);
    }

    private void RestoreWardrobePopupKeyboardFocus(bool restoreKeyboardFocus)
    {
        if (restoreKeyboardFocus)
        {
            if (InteractionPopup.IsOpen
                && WardrobeInteractionButton.IsEnabled)
            {
                _ = Keyboard.Focus(WardrobeInteractionButton);
            }
            else if (IsVisible
                && _isPetVisible
                && !_isMemoryMode
                && !IsSideDocked)
            {
                _ = Keyboard.Focus(this);
            }
        }
    }

    private void RefreshWardrobePopupItems()
    {
        if (_wardrobeMenuState is null)
        {
            WardrobeItems.ItemsSource = null;
            WardrobeCurrentSelectionText.Text = string.Empty;
            WardrobePageText.Text = string.Empty;
            WardrobePreviousPageButton.IsEnabled = false;
            WardrobeNextPageButton.IsEnabled = false;
            return;
        }

        bool compact = WardrobePopupSurface.Width < 600;
        double thumbnailSize = compact ? 34 : 42;
        double noneIconSize = compact ? 21 : 24;
        double nameMaxWidth = compact ? 44 : 56;
        double nameFontSize = compact ? 9.5 : 10.5;
        WardrobeItemPresentation[] items = _wardrobeMenuState
            .CurrentPageEntries
            .Select(
                entry => new WardrobeItemPresentation(
                    entry.Id,
                    entry.DisplayName,
                    LoadWardrobeThumbnail(entry.ImagePath),
                    entry.IsSelected,
                    entry.IsNone,
                    thumbnailSize,
                    noneIconSize,
                    nameMaxWidth,
                    nameFontSize))
            .ToArray();
        WardrobeItems.ItemsSource = items;
        WardrobePageText.Text =
            $"{_wardrobeMenuState.CurrentPageNumber} / "
            + _wardrobeMenuState.PageCount;
        AutomationProperties.SetName(
            WardrobePageText,
            $"第 {_wardrobeMenuState.CurrentPageNumber} 页，"
            + $"共 {_wardrobeMenuState.PageCount} 页");
        WardrobePreviousPageButton.IsEnabled =
            _wardrobeMenuState.CanMovePreviousPage;
        WardrobeNextPageButton.IsEnabled =
            _wardrobeMenuState.CanMoveNextPage;

        WardrobeMenuEntry? selected = _wardrobeMenuState.Entries
            .FirstOrDefault(static entry => entry.IsSelected);
        WardrobeCurrentSelectionText.Text = selected is null
            ? string.Empty
            : $"当前：{selected.DisplayName}";
    }

    private ImageSource? LoadWardrobeThumbnail(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        if (_wardrobeThumbnailCache.TryGetValue(
                imagePath,
                out ImageSource? cached))
        {
            return cached;
        }

        ImageSource? loaded = LoadBitmapFile(
            imagePath,
            decodePixelWidth: 84);
        if (loaded is BitmapSource bitmap)
        {
            loaded = CropTransparentPadding(bitmap);
        }

        if (loaded is not null)
        {
            _wardrobeThumbnailCache[imagePath] = loaded;
        }

        return loaded;
    }

    private static BitmapSource CropTransparentPadding(BitmapSource source)
    {
        BitmapSource pixels = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(
                source,
                PixelFormats.Bgra32,
                destinationPalette: null,
                alphaThreshold: 0);
        if (pixels.CanFreeze && !pixels.IsFrozen)
        {
            pixels.Freeze();
        }

        int width = pixels.PixelWidth;
        int height = pixels.PixelHeight;
        int stride = checked(width * 4);
        byte[] buffer = new byte[checked(stride * height)];
        pixels.CopyPixels(buffer, stride, 0);

        int minimumX = width;
        int minimumY = height;
        int maximumX = -1;
        int maximumY = -1;
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                if (buffer[row + x * 4 + 3] <= 8)
                {
                    continue;
                }

                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
            }
        }

        if (maximumX < minimumX || maximumY < minimumY)
        {
            return source;
        }

        const int padding = 2;
        minimumX = Math.Max(0, minimumX - padding);
        minimumY = Math.Max(0, minimumY - padding);
        maximumX = Math.Min(width - 1, maximumX + padding);
        maximumY = Math.Min(height - 1, maximumY + padding);
        if (minimumX == 0
            && minimumY == 0
            && maximumX == width - 1
            && maximumY == height - 1)
        {
            return source;
        }

        var cropped = new CroppedBitmap(
            pixels,
            new Int32Rect(
                minimumX,
                minimumY,
                maximumX - minimumX + 1,
                maximumY - minimumY + 1));
        cropped.Freeze();
        return cropped;
    }

    private void PositionWardrobePopup()
    {
        WindowDisplayDiagnostics diagnostics =
            WindowPlacementService.GetDiagnostics(this);
        double scale = Math.Max(1, diagnostics.Dpi) / 96d;
        WindowRectangle window = diagnostics.Window;
        WardrobePopupDipRectangle petBounds = new(
            window.Left / scale,
            window.Top / scale,
            window.Width / scale,
            window.Height / scale);

        WardrobePopupDipRectangle workArea;
        if (diagnostics.WorkArea is WindowRectangle physicalWorkArea)
        {
            workArea = new WardrobePopupDipRectangle(
                physicalWorkArea.Left / scale,
                physicalWorkArea.Top / scale,
                physicalWorkArea.Width / scale,
                physicalWorkArea.Height / scale);
        }
        else
        {
            Rect fallback = SystemParameters.WorkArea;
            workArea = new WardrobePopupDipRectangle(
                fallback.Left,
                fallback.Top,
                fallback.Width,
                fallback.Height);
        }

        WardrobePopupHostPlacementResult placement =
            WardrobePopupPlacement.CalculateWithShadowHost(
                petBounds,
                workArea,
                WardrobePopupSurfaceDesiredWidth,
                WardrobePopupSurfaceDesiredHeight,
                WardrobePopupShadowPadding);
        _wardrobePopupPlacementSide = placement.Side;
        WardrobePopupHost.Width = placement.HostBoundsDips.Width;
        WardrobePopupHost.Height = placement.HostBoundsDips.Height;
        WardrobePopupSurface.Width = placement.SurfaceBoundsDips.Width;
        WardrobePopupSurface.Height = placement.SurfaceBoundsDips.Height;
        WardrobePopup.HorizontalOffset =
            placement.HostBoundsDips.Left - petBounds.Left;
        WardrobePopup.VerticalOffset =
            placement.HostBoundsDips.Top - petBounds.Top;
    }

    private void ScheduleWardrobePopupReposition()
    {
        if (!WardrobePopup.IsOpen)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (WardrobePopup.IsOpen)
                {
                    PositionWardrobePopup();
                    RefreshWardrobePopupItems();
                }
            });
    }

    private void PrepareWardrobePopupReveal()
    {
        _wardrobePopupRevealSettled = false;
        RemoveWardrobePopupMotionClocks();
        if (!ShouldAnimateWardrobePopup())
        {
            WardrobePopupSurface.Opacity = 1;
            WardrobePopupTranslate.Y = 0;
            _wardrobePopupRevealSettled = true;
            return;
        }

        WardrobePopupSurface.Opacity = 0;
        WardrobePopupTranslate.Y = ResolveWardrobePopupMotionOffset(
            _wardrobePopupPlacementSide);
    }

    private void BeginWardrobePopupRevealAnimation(long generation)
    {
        if (!IsCurrentWardrobePopupMotion(generation)
            || !WardrobePopup.IsOpen
            || _wardrobePopupDismissInProgress)
        {
            return;
        }

        if (!ShouldAnimateWardrobePopup())
        {
            CommitWardrobePopupReveal(generation);
            return;
        }

        var opacityAnimation = GuluMotion.SplineTo(
            1,
            GuluMotion.FadeEntrance);
        var translateAnimation = GuluMotion.SplineTo(
            0,
            GuluMotion.MoveEntrance);
        opacityAnimation.Completed += (_, _) =>
        {
            if (!IsCurrentWardrobePopupMotion(generation)
                || !WardrobePopup.IsOpen
                || _wardrobePopupDismissInProgress)
            {
                return;
            }

            WardrobePopupSurface.BeginAnimation(OpacityProperty, null);
            WardrobePopupSurface.Opacity = 1;
        };
        translateAnimation.Completed += (_, _) =>
        {
            CommitWardrobePopupReveal(generation);
        };
        WardrobePopupSurface.BeginAnimation(
            OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);
        WardrobePopupTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            translateAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void CommitWardrobePopupReveal(long generation)
    {
        if (!IsCurrentWardrobePopupMotion(generation)
            || !WardrobePopup.IsOpen
            || _wardrobePopupDismissInProgress)
        {
            return;
        }

        RemoveWardrobePopupMotionClocks();
        WardrobePopupSurface.Opacity = 1;
        WardrobePopupTranslate.Y = 0;
        _wardrobePopupRevealSettled = true;
        WardrobePopupHost.IsHitTestVisible = true;
    }

    private void OnWardrobePopupOpened(object sender, EventArgs e)
    {
        _interactionPopupHideTimer.Stop();
        PositionWardrobePopup();
        RefreshWardrobePopupItems();
        long generation = _wardrobePopupMotionGeneration;
        BeginWardrobePopupRevealAnimation(generation);
        QueueInitialWardrobePopupFocus(generation);
    }

    private void OnWardrobePopupClosed(object sender, EventArgs e)
    {
        _ = BeginWardrobePopupMotionOperation();
        _wardrobePopupDismissInProgress = false;
        CancelWardrobePopupClose();
        ResetWardrobePopupPointerDismissSession();
        RemoveWardrobePopupMotionClocks();
        WardrobePopupSurface.Opacity = 1;
        WardrobePopupTranslate.Y = 0;
        WardrobePopupHost.IsHitTestVisible = true;
        if (InteractionPopup.IsOpen
            && !IsMouseOver
            && !InteractionPopupHost.IsMouseOver
            && !InteractionPopupSurface.IsKeyboardFocusWithin)
        {
            ScheduleInteractionPopupClose();
        }
    }

    private long BeginWardrobePopupMotionOperation() =>
        ++_wardrobePopupMotionGeneration;

    private bool IsCurrentWardrobePopupMotion(long generation) =>
        generation == _wardrobePopupMotionGeneration;

    private bool ShouldAnimateWardrobePopup() =>
        _wardrobePopupAnimationsEnabledForTest
        ?? GuluMotion.ShouldAnimate(
            SystemParameters.ClientAreaAnimation,
            SystemParameters.HighContrast);

    private static double ResolveWardrobePopupMotionOffset(
        WardrobePopupPlacementSide side) =>
        side == WardrobePopupPlacementSide.BelowPet ? -8 : 8;

    internal static double ResolveWardrobePopupDismissOffset(
        WardrobePopupPlacementSide side) =>
        ResolveWardrobePopupMotionOffset(side);

    private void RemoveWardrobePopupMotionClocks()
    {
        WardrobePopupSurface.BeginAnimation(OpacityProperty, null);
        WardrobePopupTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            null);
    }

    private void FocusSelectedWardrobeItem()
    {
        if (!WardrobePopup.IsOpen)
        {
            return;
        }

        WardrobePopupSurface.UpdateLayout();
        WpfButton? first = null;
        WpfButton? selected = null;
        FindWardrobeButtons(
            WardrobeItems,
            ref first,
            ref selected);
        IInputElement focusTarget = selected is not null
            ? selected
            : first is not null
                ? first
                : WardrobePopupSurface;
        _ = Keyboard.Focus(focusTarget);
    }

    private void QueueInitialWardrobePopupFocus(long generation)
    {
        _wardrobePopupAutomaticFocusGeneration = generation;
        _wardrobePopupAutomaticFocusPending = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (_wardrobePopupAutomaticFocusGeneration != generation)
                {
                    return;
                }

                if (IsCurrentWardrobePopupMotion(generation)
                    && WardrobePopup.IsOpen)
                {
                    FocusSelectedWardrobeItem();
                }

                if (_wardrobePopupAutomaticFocusGeneration == generation)
                {
                    _wardrobePopupAutomaticFocusPending = false;
                    _wardrobePopupFocusBaselineEstablished =
                        WardrobePopup.IsOpen
                        && WardrobePopupSurface.IsKeyboardFocusWithin;
                }
            });
    }

    private static void FindWardrobeButtons(
        DependencyObject parent,
        ref WpfButton? first,
        ref WpfButton? selected)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child =
                VisualTreeHelper.GetChild(parent, index);
            if (child is WpfButton button
                && button.Tag is WardrobeItemPresentation item)
            {
                first ??= button;
                if (item.IsSelected)
                {
                    selected = button;
                    return;
                }
            }

            FindWardrobeButtons(child, ref first, ref selected);
            if (selected is not null)
            {
                return;
            }
        }
    }

    private void OnWardrobeItemClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not WpfButton
            {
                Tag: WardrobeItemPresentation item,
            })
        {
            return;
        }

        _ = SelectWardrobeItem(item.Id);
        e.Handled = true;
    }

    private bool SelectWardrobeItem(string? id)
    {
        if (_wardrobeMenuState is null)
        {
            return false;
        }

        bool changed = _wardrobeMenuState.Select(id);
        _selectedEyeAccessoryId =
            _wardrobeMenuState.SelectedAccessoryId;
        RefreshWardrobePopupItems();
        EyeAccessorySelectionRequested?.Invoke(
            this,
            new EyeAccessorySelectionRequestedEventArgs(id));
        if (WardrobePopup.IsOpen)
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                FocusSelectedWardrobeItem);
        }

        return changed;
    }

    private void OnWardrobePreviousPageClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_wardrobeMenuState?.MovePreviousPage() == true)
        {
            RefreshWardrobePopupItems();
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                FocusSelectedWardrobeItem);
        }

        e.Handled = true;
    }

    private void OnWardrobeNextPageClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_wardrobeMenuState?.MoveNextPage() == true)
        {
            RefreshWardrobePopupItems();
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                FocusSelectedWardrobeItem);
        }

        e.Handled = true;
    }

    private void OnWardrobePopupPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseWardrobeAndReturnToTrigger();
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp
            && _wardrobeMenuState?.MovePreviousPage() == true)
        {
            RefreshWardrobePopupItems();
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                FocusSelectedWardrobeItem);
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown
            && _wardrobeMenuState?.MoveNextPage() == true)
        {
            RefreshWardrobePopupItems();
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                FocusSelectedWardrobeItem);
            e.Handled = true;
        }
    }

    private void OnWardrobePopupMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        bool moved = e.Delta > 0
            ? _wardrobeMenuState?.MovePreviousPage() == true
            : _wardrobeMenuState?.MoveNextPage() == true;
        if (moved)
        {
            RefreshWardrobePopupItems();
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                FocusSelectedWardrobeItem);
        }

        e.Handled = true;
    }

    private void OnWardrobePopupMouseEnter(
        object sender,
        MouseEventArgs e)
    {
        _interactionPopupHideTimer.Stop();
        CancelWardrobePopupClose(
            WardrobePopupDismissReason.PointerLeave);
        TryArmWardrobePopupPointerInteraction();
        CancelPointerFocus();
    }

    private void OnWardrobePopupMouseMove(
        object sender,
        MouseEventArgs e) =>
        TryArmWardrobePopupPointerInteraction();

    private void OnWardrobePopupMouseLeave(
        object sender,
        MouseEventArgs e)
    {
        ScheduleWardrobePopupClose(
            WardrobePopupDismissReason.PointerLeave);
    }

    private void OnWardrobePopupKeyboardFocusWithinChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _wardrobePopupFocusBaselineEstablished = true;
            CancelWardrobePopupClose(
                WardrobePopupDismissReason.FocusLoss);
            return;
        }

        if (_wardrobePopupAutomaticFocusPending
            || !_wardrobePopupFocusBaselineEstablished)
        {
            // Popup creation can publish an initial false value before the
            // queued focus request establishes a real focus baseline.
            return;
        }

        _wardrobePopupFocusBaselineEstablished = false;
        ScheduleWardrobePopupClose(
            WardrobePopupDismissReason.FocusLoss);
    }

    private void ScheduleWardrobePopupClose(
        WardrobePopupDismissReason reason)
    {
        if (!WardrobePopup.IsOpen)
        {
            return;
        }

        if (reason == WardrobePopupDismissReason.PointerLeave
            && (!_wardrobePopupRevealSettled
                || !_wardrobePopupPointerInteractionArmed))
        {
            return;
        }

        if (reason == WardrobePopupDismissReason.FocusLoss
            && (_wardrobePopupAutomaticFocusPending
                || WardrobePopupSurface.IsKeyboardFocusWithin))
        {
            return;
        }

        _wardrobePopupDismissReasons |= reason;
        _wardrobePopupHideMotionGeneration =
            _wardrobePopupMotionGeneration;
        if (!_wardrobePopupHideTimer.IsEnabled)
        {
            _wardrobePopupHideTimer.Start();
        }
    }

    private void OnWardrobePopupHideElapsed(object? sender, EventArgs e)
    {
        _wardrobePopupHideTimer.Stop();
        WardrobePopupDismissReason reasons =
            _wardrobePopupDismissReasons;
        _wardrobePopupDismissReasons = WardrobePopupDismissReason.None;
        if (!IsCurrentWardrobePopupMotion(
                _wardrobePopupHideMotionGeneration)
            || !WardrobePopup.IsOpen)
        {
            return;
        }

        bool pointerLeaveRequiresDismiss =
            reasons.HasFlag(WardrobePopupDismissReason.PointerLeave)
            && _wardrobePopupRevealSettled
            && _wardrobePopupPointerInteractionArmed
            && !IsPhysicalCursorOverWardrobePopup();
        bool focusLossRequiresDismiss =
            reasons.HasFlag(WardrobePopupDismissReason.FocusLoss)
            && !WardrobePopupSurface.IsKeyboardFocusWithin;
        if (!pointerLeaveRequiresDismiss
            && !focusLossRequiresDismiss)
        {
            return;
        }

        DismissWardrobePopup(restoreFocus: false);
    }

    private void BeginWardrobePopupPointerDismissSession()
    {
        CancelWardrobePopupClose();
        _wardrobePopupRevealSettled = false;
        _wardrobePopupPointerInteractionArmed = false;
        _wardrobePopupAutomaticFocusPending = false;
        _wardrobePopupFocusBaselineEstablished = false;
        _wardrobePopupAutomaticFocusGeneration = 0;
        _wardrobePopupOpenCursor = GetWardrobePopupCursorPosition();
    }

    private void ResetWardrobePopupPointerDismissSession()
    {
        CancelWardrobePopupClose();
        _wardrobePopupRevealSettled = false;
        _wardrobePopupPointerInteractionArmed = false;
        _wardrobePopupAutomaticFocusPending = false;
        _wardrobePopupFocusBaselineEstablished = false;
        _wardrobePopupAutomaticFocusGeneration = 0;
        _wardrobePopupOpenCursor = null;
    }

    private void TryArmWardrobePopupPointerInteraction()
    {
        Point? cursor = GetWardrobePopupCursorPosition();
        if (!_wardrobePopupRevealSettled
            || _wardrobePopupPointerInteractionArmed
            || _wardrobePopupOpenCursor is not Point openCursor
            || cursor is not Point currentCursor
            || currentCursor == openCursor)
        {
            return;
        }

        _wardrobePopupPointerInteractionArmed = true;
    }

    private Point? GetWardrobePopupCursorPosition()
    {
        if (_wardrobePopupCursorProviderForTest is not null)
        {
            return _wardrobePopupCursorProviderForTest();
        }

        return NativeMethods.GetPhysicalCursorPos(out var cursor)
            ? new Point(cursor.X, cursor.Y)
            : null;
    }

    private bool IsPhysicalCursorOverWardrobePopup()
    {
        Point? cursor = GetWardrobePopupCursorPosition();
        if (cursor is null)
        {
            return WardrobePopupHost.IsMouseOver;
        }

        try
        {
            Point local = WardrobePopupHost.PointFromScreen(cursor.Value);
            return new Rect(
                new Point(),
                WardrobePopupHost.RenderSize).Contains(local);
        }
        catch (InvalidOperationException)
        {
            return WardrobePopupHost.IsMouseOver;
        }
    }

    private void CancelWardrobePopupClose(
        WardrobePopupDismissReason reason)
    {
        _wardrobePopupDismissReasons &= ~reason;
        if (_wardrobePopupDismissReasons == WardrobePopupDismissReason.None)
        {
            _wardrobePopupHideTimer.Stop();
        }
    }

    private void CancelWardrobePopupClose()
    {
        _wardrobePopupHideTimer.Stop();
        _wardrobePopupDismissReasons = WardrobePopupDismissReason.None;
    }

    private static void ValidateCareLevel(double level, string parameterName)
    {
        if (!double.IsFinite(level) || level < 0 || level > 100)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Care levels must be finite values from 0 through 100.");
        }
    }

    private static PetDragMotionEventArgs CreateMotionEventArgs(
        NativeMethods.NativePoint cursor,
        int windowX,
        int windowY,
        Vector delta,
        Vector totalDelta,
        Vector velocity,
        long pressInteractionSessionId)
    {
        return new PetDragMotionEventArgs(
            new Point(cursor.X, cursor.Y),
            new Point(windowX, windowY),
            delta,
            totalDelta,
            velocity,
            pressInteractionSessionId);
    }

    private void OnPetContextMenuOpened(object sender, RoutedEventArgs e)
    {
        RefreshContextMenuState();
    }

    private void RefreshContextMenuState()
    {
        AlwaysOnTopMenuItem.IsChecked = _alwaysOnTop;
        StartWithWindowsMenuItem.IsChecked = _startWithWindows;
    }

    private void OnAlwaysOnTopClicked(object sender, RoutedEventArgs e)
    {
        AlwaysOnTopToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnStartWithWindowsClicked(object sender, RoutedEventArgs e)
    {
        StartWithWindowsToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnManualUpdateClicked(object sender, RoutedEventArgs e)
    {
        ManualUpdateRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPostcardsClicked(object sender, RoutedEventArgs e)
    {
        PostcardsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDiaryClicked(object sender, RoutedEventArgs e)
    {
        DiaryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnMemoriesClicked(object sender, RoutedEventArgs e)
    {
        MemoriesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnMemorySkipClicked(object sender, RoutedEventArgs e)
    {
        SkipActiveMemoryPlayback();
        e.Handled = true;
    }

    private void OnBehaviorsClicked(object sender, RoutedEventArgs e)
    {
        BehaviorsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnVisibilityClicked(object sender, RoutedEventArgs e)
    {
        VisibilityToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnResetPositionClicked(object sender, RoutedEventArgs e)
    {
        ResetPositionRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
