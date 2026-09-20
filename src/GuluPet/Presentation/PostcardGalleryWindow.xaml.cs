using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuluPet.Platform;
using GuluPet.Postcards;
using Button = System.Windows.Controls.Button;
using Forms = System.Windows.Forms;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace GuluPet.Presentation;

public partial class PostcardGalleryWindow : Window
{
    private const double DefaultMinimumWidth = 680;
    private const double DefaultMinimumHeight = 480;
    private static readonly TimeSpan ImageOpenFeedbackDuration =
        TimeSpan.FromSeconds(2.4);
    private readonly ObservableCollection<GalleryPostcardItem> _items = [];
    private readonly Action<string> _imageOpener;
    private readonly DispatcherTimer _imageOpenFeedbackTimer;
    private IReadOnlyList<PostcardDefinition> _unlockedPostcards = [];
    private IReadOnlyList<PostcardDefinition> _availablePostcards = [];
    private PostcardOutingStatus _outingStatus = new(
        PostcardOutingPhase.AtHome,
        StartedAtUtc: null,
        ReadyAtUtc: null,
        ArrivedAtUtc: null,
        TimeSpan.Zero,
        NextPostcardId: null);
    private int _availableCount;
    private int _currentChapterNumber = 1;
    private long _imageOpenFeedbackVersion;
    private bool _imageOpenFeedbackExitInProgress;
    private bool? _imageOpenFeedbackAnimationsEnabledForTest;

    public PostcardGalleryWindow()
        : this(OpenImageWithDefaultApplication)
    {
    }

    internal PostcardGalleryWindow(
        Action<string> imageOpener)
    {
        ArgumentNullException.ThrowIfNull(imageOpener);
        _imageOpener = imageOpener;
        InitializeComponent();
        _imageOpenFeedbackTimer = new DispatcherTimer(
            ImageOpenFeedbackDuration,
            DispatcherPriority.Background,
            OnImageOpenFeedbackTimerTick,
            Dispatcher);
        _imageOpenFeedbackTimer.Stop();
        PostcardItems.ItemsSource = _items;
        LoadGuluImages();
        UpdateProgress();
        UpdateEmptyState();
    }

    public int UnlockedCount => _unlockedPostcards.Count;

    public int AvailableCount => _availableCount;

    internal int VisiblePostcardCount => _items.Count;

    internal int CurrentChapterNumber => _currentChapterNumber;

    internal int AvailableChapterCount =>
        TryGetChapterPaging(
            out int chapterCount,
            out _)
            ? chapterCount
            : 0;

    internal long ImageOpenFeedbackVersionForTest =>
        _imageOpenFeedbackVersion;

    internal bool ImageOpenFeedbackExitInProgressForTest =>
        _imageOpenFeedbackExitInProgress;

    internal bool ImageOpenFeedbackHasAnimatedPropertiesForTest =>
        ImageOpenFeedbackBorder.HasAnimatedProperties
        || ImageOpenFeedbackTranslate.HasAnimatedProperties;

    internal TimeSpan ImageOpenFeedbackDwellForTest =>
        _imageOpenFeedbackTimer.Interval;

    internal void SetImageOpenFeedbackAnimationsEnabledForTest(
        bool? enabled) =>
        _imageOpenFeedbackAnimationsEnabledForTest = enabled;

    internal void ExpireImageOpenFeedbackForTest() =>
        BeginImageOpenFeedbackExit();

    public void SetPostcards(
        IEnumerable<PostcardDefinition> unlockedPostcards,
        int availableCount)
    {
        ArgumentNullException.ThrowIfNull(unlockedPostcards);
        ArgumentOutOfRangeException.ThrowIfNegative(availableCount);

        PostcardDefinition[] snapshot = unlockedPostcards
            .OrderBy(postcard => postcard.UnlockOrder)
            .ThenBy(postcard => postcard.Id, StringComparer.Ordinal)
            .ToArray();
        if (snapshot.Length > availableCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableCount),
                "Available postcards cannot be fewer than unlocked postcards.");
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(
                () => SetPostcards(snapshot, availableCount));
            return;
        }

        ApplyPostcards(
            snapshot,
            available: [],
            availableCount: availableCount,
            outingStatus: _outingStatus);
    }

    public void SetPostcards(
        IEnumerable<PostcardDefinition> unlockedPostcards,
        IEnumerable<PostcardDefinition> availablePostcards,
        PostcardOutingStatus outingStatus)
    {
        ArgumentNullException.ThrowIfNull(unlockedPostcards);
        ArgumentNullException.ThrowIfNull(availablePostcards);
        ArgumentNullException.ThrowIfNull(outingStatus);
        PostcardDefinition[] unlocked = unlockedPostcards
            .OrderBy(static postcard => postcard.UnlockOrder)
            .ThenBy(static postcard => postcard.Id, StringComparer.Ordinal)
            .ToArray();
        PostcardDefinition[] available = availablePostcards
            .OrderBy(static postcard => postcard.UnlockOrder)
            .ThenBy(static postcard => postcard.Id, StringComparer.Ordinal)
            .ToArray();
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(
                () => SetPostcards(
                    unlocked,
                    available,
                    outingStatus));
            return;
        }

        var availableIds = available
            .Select(static postcard => postcard.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (availableIds.Count != available.Length
            || unlocked.Select(static postcard => postcard.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() != unlocked.Length
            || unlocked.Any(postcard => !availableIds.Contains(postcard.Id)))
        {
            throw new ArgumentException(
                "Unlocked postcards must be unique entries in the available catalog.",
                nameof(unlockedPostcards));
        }

        ApplyPostcards(
            unlocked,
            available,
            available.Length,
            outingStatus);
    }

    private void ApplyPostcards(
        IReadOnlyList<PostcardDefinition> unlocked,
        IReadOnlyList<PostcardDefinition> available,
        int availableCount,
        PostcardOutingStatus outingStatus)
    {
        bool unlockedChanged = !HaveSameIds(
            _unlockedPostcards,
            unlocked);
        bool catalogChanged = !HaveSameIds(
            _availablePostcards,
            available);

        _unlockedPostcards = unlocked;
        _availablePostcards = available;
        _availableCount = availableCount;
        _outingStatus = outingStatus;

        if (unlockedChanged || catalogChanged)
        {
            ClampCurrentChapter();
            SynchronizeVisibleItems();
        }
        else
        {
            UpdateChapterNavigation();
        }

        UpdateProgress();
        UpdateEmptyState();
    }

    private static bool HaveSameIds(
        IReadOnlyList<PostcardDefinition> left,
        IReadOnlyList<PostcardDefinition> right) =>
        left.Count == right.Count
        && left.Select(static postcard => postcard.Id)
            .SequenceEqual(
                right.Select(static postcard => postcard.Id),
                StringComparer.Ordinal);

    public void SetOutingStatus(PostcardOutingStatus outingStatus)
    {
        ArgumentNullException.ThrowIfNull(outingStatus);
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetOutingStatus(outingStatus));
            return;
        }

        _outingStatus = outingStatus;
        UpdateProgress();
        UpdateEmptyState();
    }

    public void ShowGallery(Window? anchor = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowGallery(anchor));
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        bool restoreMaximized = WindowState == WindowState.Maximized;
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        UpdateLayout();
        PositionOnAnchorMonitor(anchor);
        if (restoreMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        Activate();
        QueueFirstPaintRefresh();
    }

    private void QueueFirstPaintRefresh()
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(
                () =>
                {
                    if (!IsVisible)
                    {
                        return;
                    }

                    // The gallery is populated before Show() and then moved
                    // with a native DPI-aware SetWindowPos call. On some
                    // multi-monitor WPF setups the first compositor surface
                    // remains the blank window background until the first
                    // input event. Re-invalidating after the show/move call
                    // gives the window a real first paint without requiring
                    // the user to click it.
                    GalleryRoot.InvalidateMeasure();
                    GalleryRoot.InvalidateArrange();
                    GalleryRoot.InvalidateVisual();
                    PostcardItems.InvalidateVisual();
                    UpdateLayout();
                }));
    }

    public void OpenPostcard(string postcardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postcardId);
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OpenPostcard(postcardId));
            return;
        }

        int index = FindUnlockedIndex(postcardId);
        if (index >= 0)
        {
            OpenPostcardImage(_unlockedPostcards[index]);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.PageUp:
                MoveChapter(-1);
                e.Handled = true;
                return;

            case Key.PageDown:
                MoveChapter(1);
                e.Handled = true;
                return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        HideImageOpenFeedback();
        _imageOpenFeedbackTimer.Tick -= OnImageOpenFeedbackTimerTick;
        base.OnClosed(e);
    }

    private void SynchronizeVisibleItems()
    {
        IEnumerable<PostcardDefinition> visible = _unlockedPostcards;
        if (TryGetChapterPaging(out _, out _))
        {
            visible = _unlockedPostcards
                .Where(postcard =>
                    PostcardChapterProgressFormatter.GetChapterNumber(postcard)
                        == _currentChapterNumber);
        }

        _items.Clear();
        foreach (PostcardDefinition definition in visible)
        {
            _items.Add(new GalleryPostcardItem(
                definition,
                TryLoadBitmap(
                    definition.ImagePath,
                    decodePixelWidth: 520)));
        }

        GalleryScrollViewer.ScrollToTop();
        UpdateChapterNavigation();
    }

    private void ClampCurrentChapter()
    {
        if (!TryGetChapterPaging(
                out _,
                out IReadOnlyList<int> unlockedChapters)
            || unlockedChapters.Count == 0)
        {
            _currentChapterNumber = 1;
            return;
        }

        if (unlockedChapters.Contains(_currentChapterNumber))
        {
            return;
        }

        _currentChapterNumber = unlockedChapters
            .Where(chapter => chapter <= _currentChapterNumber)
            .DefaultIfEmpty(unlockedChapters[0])
            .Max();
    }

    private void UpdateChapterNavigation()
    {
        bool canPage = TryGetChapterPaging(
            out int chapterCount,
            out IReadOnlyList<int> unlockedChapters);
        bool hasUnlockedChapter = canPage && unlockedChapters.Count > 0;
        ChapterNavigation.Visibility = hasUnlockedChapter
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!hasUnlockedChapter)
        {
            return;
        }

        string chapterName = _availablePostcards[
            (_currentChapterNumber - 1)
            * PostcardChapterProgressFormatter.CardsPerChapter].Chapter;
        ChapterPageText.Text =
            $"第 {_currentChapterNumber:N0} 章 · {chapterName}";
        NextPostcardProgress.Maximum =
            PostcardChapterProgressFormatter.CardsPerChapter;
        NextPostcardProgress.Value = Math.Min(
            _items.Count,
            PostcardChapterProgressFormatter.CardsPerChapter);
        NextProgressText.Text =
            $"{_items.Count:N0}/" +
            $"{PostcardChapterProgressFormatter.CardsPerChapter:N0}";
        int pageIndex = FindChapterPageIndex(
            unlockedChapters,
            _currentChapterNumber);
        PreviousChapterButton.IsEnabled = pageIndex > 0;
        NextChapterButton.IsEnabled = pageIndex >= 0
            && pageIndex + 1 < unlockedChapters.Count;
        AutomationProperties.SetName(
            ChapterNavigation,
            $"明信片章节，第 {_currentChapterNumber:N0} 章，" +
            $"共 {chapterCount:N0} 章，{chapterName}");
        AutomationProperties.SetHelpText(
            ChapterNavigation,
            NextChapterButton.IsEnabled
                ? "可以查看上一章或下一章；也可以按 PageUp 或 PageDown"
                : "尚未收集的章节不会显示");
        AutomationProperties.SetName(
            NextPostcardProgress,
            $"第 {_currentChapterNumber:N0} 章已收好 {_items.Count:N0} 张，" +
            $"共 {PostcardChapterProgressFormatter.CardsPerChapter:N0} 张");
    }

    private bool TryGetChapterPaging(
        out int chapterCount,
        out IReadOnlyList<int> unlockedChapters)
    {
        unlockedChapters = [];
        if (!PostcardChapterProgressFormatter.TryGetChapterCount(
                _availablePostcards,
                out chapterCount))
        {
            return false;
        }

        var availableIds = _availablePostcards
            .Select(static postcard => postcard.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (_unlockedPostcards.Any(postcard =>
                !availableIds.Contains(postcard.Id)
                || !HasChapter(postcard)))
        {
            chapterCount = 0;
            return false;
        }

        unlockedChapters = _unlockedPostcards
            .Select(PostcardChapterProgressFormatter.GetChapterNumber)
            .Distinct()
            .Order()
            .ToArray();

        return true;
    }

    private void MoveChapter(int delta)
    {
        if (!TryGetChapterPaging(
                out _,
                out IReadOnlyList<int> unlockedChapters)
            || unlockedChapters.Count == 0)
        {
            return;
        }

        int currentIndex = FindChapterPageIndex(
            unlockedChapters,
            _currentChapterNumber);
        if (currentIndex < 0)
        {
            ClampCurrentChapter();
            currentIndex = FindChapterPageIndex(
                unlockedChapters,
                _currentChapterNumber);
        }

        int nextIndex = Math.Clamp(
            currentIndex + delta,
            0,
            unlockedChapters.Count - 1);
        if (nextIndex == currentIndex)
        {
            return;
        }

        _currentChapterNumber = unlockedChapters[nextIndex];
        SynchronizeVisibleItems();
        UpdateEmptyState();
    }

    private static int FindChapterPageIndex(
        IReadOnlyList<int> chapters,
        int chapterNumber)
    {
        for (var index = 0; index < chapters.Count; index++)
        {
            if (chapters[index] == chapterNumber)
            {
                return index;
            }
        }

        return -1;
    }

    private int FindUnlockedIndex(string postcardId) =>
        _unlockedPostcards
            .Select((postcard, index) => (postcard, index))
            .Where(candidate => string.Equals(
                candidate.postcard.Id,
                postcardId,
                StringComparison.Ordinal))
            .Select(static candidate => candidate.index)
            .DefaultIfEmpty(-1)
            .First();

    private void UpdateProgress()
    {
        PostcardGalleryProgress progress = PostcardGalleryProgressFormatter.Create(
            _outingStatus,
            _unlockedPostcards.Count,
            _availableCount);
        PostcardChapterProgress? chapter = GetChapterProgress();
        string chapterFeedback = chapter is null || progress.IsComplete
            ? string.Empty
            : chapter.NextChapterFeedback;
        string footer = string.IsNullOrWhiteSpace(chapterFeedback)
            ? progress.Footer
            : chapterFeedback;
        NextPostcardLabelText.Text = FormatOutingStatus(_outingStatus);
        FooterHintText.Text = footer;
        if (progress.IsComplete)
        {
            EmptyProgressText.Text = progress.Detail;
            AutomationProperties.SetHelpText(
                NextPostcardProgress,
                footer);
            return;
        }

        EmptyProgressText.Text = _availableCount == 0
            ? "咕噜还在挑下一张明信片"
            : progress.Footer;
        AutomationProperties.SetHelpText(
            NextPostcardProgress,
            _availableCount == 0
                ? "咕噜还没有带回新的明信片"
                : footer);
    }

    private static string FormatOutingStatus(PostcardOutingStatus outing)
    {
        return outing.Phase switch
        {
            PostcardOutingPhase.AtHome => "等待下次出游",
            PostcardOutingPhase.Traveling =>
                $"出游中 · {FormatRemainingTime(outing.Remaining)}",
            PostcardOutingPhase.WaitingAtDoor => "明信片待收",
            PostcardOutingPhase.CollectionComplete => "全部收好",
            _ => throw new InvalidOperationException(
                $"Unknown postcard outing phase '{outing.Phase}'."),
        };
    }

    private static string FormatRemainingTime(TimeSpan remaining)
    {
        int totalSeconds = checked((int)Math.Clamp(
            Math.Ceiling(remaining.TotalSeconds),
            0,
            PostcardOutingRecord.Duration.TotalSeconds));
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private void UpdateEmptyState()
    {
        bool isEmpty = _unlockedPostcards.Count == 0;
        EmptyState.Visibility = isEmpty
            ? Visibility.Visible
            : Visibility.Collapsed;
        GalleryScrollViewer.Visibility = isEmpty
            ? Visibility.Collapsed
            : Visibility.Visible;
        bool isComplete = _unlockedPostcards.Count >= _availableCount
            && _availableCount > 0;
        UnlockedCountText.Text = isComplete
            ? $"{_unlockedPostcards.Count:N0} 张 · 已收齐"
            : $"已收 {_unlockedPostcards.Count:N0} 张";
        AutomationProperties.SetName(
            UnlockedCountText,
            $"已经收好 {_unlockedPostcards.Count:N0} 张明信片");
    }

    private PostcardChapterProgress? GetChapterProgress()
    {
        if (!PostcardChapterProgressFormatter.TryGetChapterCount(
                _availablePostcards,
                out _))
        {
            return null;
        }

        bool isCatalogPrefix = _unlockedPostcards
            .Select(static postcard => postcard.Id)
            .SequenceEqual(
                _availablePostcards.Take(_unlockedPostcards.Count)
                    .Select(static postcard => postcard.Id),
                StringComparer.Ordinal);
        return isCatalogPrefix
            ? PostcardChapterProgressFormatter.Create(
                _availablePostcards,
                _unlockedPostcards.Count)
            : null;
    }

    private void LoadGuluImages()
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
        if (path is null)
        {
            return;
        }

        BitmapSource? image = TryLoadBitmap(path, decodePixelWidth: 236);
        GalleryGuluImage.Source = image;
        EmptyGuluImage.Source = image;
    }

    private void OpenPostcardImage(PostcardDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!TryResolveOpenableImagePath(
                definition.ImagePath,
                out string imagePath))
        {
            ShowImageOpenFeedback("图片暂时打不开");
            return;
        }

        try
        {
            _imageOpener(imagePath);
            HideImageOpenFeedback();
        }
        catch (Exception exception)
            when (exception is Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            Debug.WriteLine(
                $"Unable to open postcard image '{imagePath}': {exception}");
            ShowImageOpenFeedback("图片暂时打不开");
        }
    }

    private static bool TryResolveOpenableImagePath(
        string path,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path)
            || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }

        string extension = Path.GetExtension(fullPath);
        return File.Exists(fullPath)
            && (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));
    }

    internal static ProcessStartInfo CreateImageOpenStartInfo(
        string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        return new ProcessStartInfo
        {
            FileName = imagePath,
            UseShellExecute = true,
        };
    }

    private static void OpenImageWithDefaultApplication(string imagePath)
    {
        Process? process = Process.Start(CreateImageOpenStartInfo(imagePath));
        process?.Dispose();
    }

    private void PositionOnAnchorMonitor(Window? anchor)
    {
        IntPtr windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        Rectangle workArea;
        NativeMethods.MonitorDpi dpi;
        if (anchor is not null)
        {
            IntPtr anchorHandle = new WindowInteropHelper(anchor).Handle;
            if (anchorHandle != IntPtr.Zero
                && NativeMethods.TryGetExtendedWindowRect(
                    anchorHandle,
                    out NativeMethods.NativeRect anchorRect))
            {
                var anchorBounds = new Rectangle(
                    anchorRect.Left,
                    anchorRect.Top,
                    Math.Max(1, anchorRect.Width),
                    Math.Max(1, anchorRect.Height));
                workArea = Forms.Screen
                    .FromRectangle(anchorBounds)
                    .WorkingArea;
                dpi = NativeMethods.GetWindowDpiOrDefault(anchorHandle);
                CenterInWorkArea(windowHandle, workArea, dpi);
                return;
            }
        }

        NativeMethods.GetPhysicalCursorPos(out NativeMethods.NativePoint point);
        IntPtr monitorHandle = NativeMethods.MonitorFromPoint(
            point,
            NativeMethods.MonitorDefaultToNearest);
        workArea = Forms.Screen
            .FromPoint(new System.Drawing.Point(point.X, point.Y))
            .WorkingArea;
        dpi = NativeMethods.GetMonitorDpiOrDefault(
            monitorHandle,
            windowHandle);
        CenterInWorkArea(windowHandle, workArea, dpi);
    }

    private void CenterInWorkArea(
        IntPtr windowHandle,
        Rectangle workArea,
        NativeMethods.MonitorDpi dpi)
    {
        int marginX = DipToPixels(16, dpi.X);
        int marginY = DipToPixels(16, dpi.Y);
        int maximumWidth = Math.Max(1, workArea.Width - 2 * marginX);
        int maximumHeight = Math.Max(1, workArea.Height - 2 * marginY);
        double maximumDipWidth = PixelsToDip(maximumWidth, dpi.X);
        double maximumDipHeight = PixelsToDip(maximumHeight, dpi.Y);
        MinWidth = Math.Min(DefaultMinimumWidth, maximumDipWidth);
        MinHeight = Math.Min(DefaultMinimumHeight, maximumDipHeight);
        MaxWidth = maximumDipWidth;
        MaxHeight = maximumDipHeight;

        double desiredWidth = ActualWidth > 0
            ? ActualWidth
            : Width;
        double desiredHeight = ActualHeight > 0
            ? ActualHeight
            : Height;
        Width = Math.Clamp(desiredWidth, MinWidth, MaxWidth);
        Height = Math.Clamp(desiredHeight, MinHeight, MaxHeight);
        UpdateLayout();

        int width = Math.Min(
            DipToPixels(ActualWidth > 0 ? ActualWidth : Width, dpi.X),
            maximumWidth);
        int height = Math.Min(
            DipToPixels(ActualHeight > 0 ? ActualHeight : Height, dpi.Y),
            maximumHeight);
        int x = workArea.Left + (workArea.Width - width) / 2;
        int y = workArea.Top + (workArea.Height - height) / 2;

        _ = NativeMethods.SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            x,
            y,
            width,
            height,
            NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate);

        if (!NativeMethods.TryGetExtendedWindowRect(
                windowHandle,
                out NativeMethods.NativeRect actual))
        {
            return;
        }

        int actualMaximumX = Math.Max(
            workArea.Left + marginX,
            workArea.Right - marginX - Math.Max(1, actual.Width));
        int actualMaximumY = Math.Max(
            workArea.Top + marginY,
            workArea.Bottom - marginY - Math.Max(1, actual.Height));
        int correctedX = Math.Clamp(
            actual.Left,
            workArea.Left + marginX,
            actualMaximumX);
        int correctedY = Math.Clamp(
            actual.Top,
            workArea.Top + marginY,
            actualMaximumY);
        if (correctedX != actual.Left || correctedY != actual.Top)
        {
            _ = NativeMethods.SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                correctedX,
                correctedY,
                0,
                0,
                NativeMethods.SwpNoSize
                    | NativeMethods.SwpNoZOrder
                    | NativeMethods.SwpNoActivate);
        }
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
            if (decodePixelWidth > 0)
            {
                image.DecodePixelWidth = decodePixelWidth;
            }
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

    private void OnThumbnailClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.Tag is not GalleryPostcardItem item)
        {
            return;
        }

        int index = FindUnlockedIndex(item.Definition.Id);
        if (index >= 0)
        {
            OpenPostcardImage(_unlockedPostcards[index]);
        }
    }

    private void ShowImageOpenFeedback(string message)
    {
        _imageOpenFeedbackTimer.Stop();
        bool wasVisible =
            ImageOpenFeedbackBorder.Visibility == Visibility.Visible;
        double startOpacity = wasVisible
            ? ImageOpenFeedbackBorder.Opacity
            : 0;
        double startY = wasVisible
            ? ImageOpenFeedbackTranslate.Y
            : 8;
        long version = ++_imageOpenFeedbackVersion;
        _imageOpenFeedbackExitInProgress = false;
        ImageOpenFeedbackText.Text = message;
        AutomationProperties.SetName(ImageOpenFeedbackText, message);
        RemoveImageOpenFeedbackMotionClocks();
        ImageOpenFeedbackBorder.Visibility = Visibility.Visible;
        if (ShouldAnimateImageOpenFeedback())
        {
            ImageOpenFeedbackBorder.Opacity = startOpacity;
            ImageOpenFeedbackTranslate.Y = startY;
            BeginImageOpenFeedbackEntrance(version);
        }
        else
        {
            CommitImageOpenFeedbackEntrance(version);
        }

        _imageOpenFeedbackTimer.Start();
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            () =>
            {
                if (version == _imageOpenFeedbackVersion
                    && ImageOpenFeedbackBorder.Visibility == Visibility.Visible)
                {
                    RaiseImageOpenFeedbackAnnouncement();
                }
            });
    }

    private void HideImageOpenFeedback()
    {
        _imageOpenFeedbackTimer.Stop();
        _imageOpenFeedbackVersion++;
        _imageOpenFeedbackExitInProgress = false;
        RemoveImageOpenFeedbackMotionClocks();
        ImageOpenFeedbackBorder.Opacity = 0;
        ImageOpenFeedbackTranslate.Y = 8;
        ImageOpenFeedbackBorder.Visibility = Visibility.Collapsed;
    }

    private void BeginImageOpenFeedbackEntrance(long version)
    {
        var opacityAnimation = GuluMotion.SplineTo(
            1,
            GuluMotion.FadeEntrance);
        opacityAnimation.Completed += (_, _) =>
            CommitImageOpenFeedbackOpacity(version, opacity: 1);
        var translateAnimation = GuluMotion.SplineTo(
            0,
            GuluMotion.MoveEntrance);
        translateAnimation.Completed += (_, _) =>
            CommitImageOpenFeedbackTranslate(version, y: 0);

        ImageOpenFeedbackBorder.BeginAnimation(
            OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);
        ImageOpenFeedbackTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            translateAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void BeginImageOpenFeedbackExit()
    {
        _imageOpenFeedbackTimer.Stop();
        if (ImageOpenFeedbackBorder.Visibility != Visibility.Visible)
        {
            HideImageOpenFeedback();
            return;
        }

        double startOpacity = ImageOpenFeedbackBorder.Opacity;
        double startY = ImageOpenFeedbackTranslate.Y;
        long version = ++_imageOpenFeedbackVersion;
        _imageOpenFeedbackExitInProgress = true;
        RemoveImageOpenFeedbackMotionClocks();
        ImageOpenFeedbackBorder.Opacity = startOpacity;
        ImageOpenFeedbackTranslate.Y = startY;
        if (!ShouldAnimateImageOpenFeedback())
        {
            CompleteImageOpenFeedbackExit(version);
            return;
        }

        var opacityAnimation = GuluMotion.SplineTo(
            0,
            GuluMotion.QuickExit);
        var translateAnimation = GuluMotion.SplineTo(
            8,
            GuluMotion.QuickExit);
        translateAnimation.Completed += (_, _) =>
            CompleteImageOpenFeedbackExit(version);

        ImageOpenFeedbackBorder.BeginAnimation(
            OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);
        ImageOpenFeedbackTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            translateAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private bool ShouldAnimateImageOpenFeedback() =>
        _imageOpenFeedbackAnimationsEnabledForTest
        ?? GuluMotion.ShouldAnimate(
            SystemParameters.ClientAreaAnimation,
            SystemParameters.HighContrast);

    private void CommitImageOpenFeedbackEntrance(long version)
    {
        if (!IsCurrentImageOpenFeedback(version, exiting: false))
        {
            return;
        }

        RemoveImageOpenFeedbackMotionClocks();
        ImageOpenFeedbackBorder.Opacity = 1;
        ImageOpenFeedbackTranslate.Y = 0;
    }

    private void CommitImageOpenFeedbackOpacity(
        long version,
        double opacity)
    {
        if (!IsCurrentImageOpenFeedback(version, exiting: false))
        {
            return;
        }

        ImageOpenFeedbackBorder.BeginAnimation(OpacityProperty, null);
        ImageOpenFeedbackBorder.Opacity = opacity;
    }

    private void CommitImageOpenFeedbackTranslate(long version, double y)
    {
        if (!IsCurrentImageOpenFeedback(version, exiting: false))
        {
            return;
        }

        ImageOpenFeedbackTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            null);
        ImageOpenFeedbackTranslate.Y = y;
    }

    private void CompleteImageOpenFeedbackExit(long version)
    {
        if (!IsCurrentImageOpenFeedback(version, exiting: true))
        {
            return;
        }

        RemoveImageOpenFeedbackMotionClocks();
        ImageOpenFeedbackBorder.Opacity = 0;
        ImageOpenFeedbackTranslate.Y = 8;
        ImageOpenFeedbackBorder.Visibility = Visibility.Collapsed;
        _imageOpenFeedbackExitInProgress = false;
    }

    private bool IsCurrentImageOpenFeedback(long version, bool exiting) =>
        version == _imageOpenFeedbackVersion
        && _imageOpenFeedbackExitInProgress == exiting
        && ImageOpenFeedbackBorder.Visibility == Visibility.Visible;

    private void RemoveImageOpenFeedbackMotionClocks()
    {
        ImageOpenFeedbackBorder.BeginAnimation(OpacityProperty, null);
        ImageOpenFeedbackTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            null);
    }

    private void RaiseImageOpenFeedbackAnnouncement()
    {
        try
        {
            var peer = new TextBlockAutomationPeer(ImageOpenFeedbackText);
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (InvalidOperationException)
        {
            // The open attempt already failed. Accessibility notification
            // failure must not replace the truthful in-window result.
        }
    }

    private void OnImageOpenFeedbackTimerTick(
        object? sender,
        EventArgs e) =>
        BeginImageOpenFeedbackExit();

    private void OnPreviousChapterClicked(
        object sender,
        RoutedEventArgs e) =>
        MoveChapter(-1);

    private void OnNextChapterClicked(
        object sender,
        RoutedEventArgs e) =>
        MoveChapter(1);

    private static bool HasChapter(PostcardDefinition definition) =>
        definition.UnlockOrder > 0
        && !string.IsNullOrWhiteSpace(definition.Chapter);

    private static string GetPostcardPositionLabel(
        PostcardDefinition definition) =>
        HasChapter(definition)
            ? PostcardChapterProgressFormatter.FormatCardPosition(definition)
            : $"第 {definition.UnlockOrder:00} 次远行";

    private sealed class GalleryPostcardItem
    {
        public GalleryPostcardItem(
            PostcardDefinition definition,
            BitmapSource? thumbnail)
        {
            Definition = definition;
            Thumbnail = thumbnail;
        }

        public PostcardDefinition Definition { get; }

        public BitmapSource? Thumbnail { get; }

        public Visibility FailureVisibility => Thumbnail is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        public string AutomationName =>
            $"用系统图片查看器打开明信片：{Definition.Title}，" +
            $"{Definition.Location}，" +
            GetPostcardPositionLabel(Definition);

        public string AutomationHelpText =>
            HasChapter(Definition)
                ? $"在系统图片查看器中打开「{Definition.Chapter}」第 " +
                  $"{PostcardChapterProgressFormatter.GetPositionInChapter(Definition)}" +
                  $" 张明信片，共 " +
                  $"{PostcardChapterProgressFormatter.CardsPerChapter:N0} 张"
                : "在系统图片查看器中打开这张明信片";

        public string ChapterName => HasChapter(Definition)
            ? Definition.Chapter
            : "远行";

        public string ChapterPosition =>
            HasChapter(Definition)
                ? $"{PostcardChapterProgressFormatter.GetPositionInChapter(Definition)}/" +
                  $"{PostcardChapterProgressFormatter.CardsPerChapter}"
                : $"第 {Definition.UnlockOrder:00} 次";
    }
}
