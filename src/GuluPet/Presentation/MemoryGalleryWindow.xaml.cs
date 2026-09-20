using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuluPet.Memories;

namespace GuluPet.Presentation;

public partial class MemoryGalleryWindow : Window
{
    private readonly ObservableCollection<MemoryGalleryCardItem> _items = [];
    private MemoryGalleryState _state = MemoryGalleryState.Empty;
    private bool _replayRequestInFlight;

    public MemoryGalleryWindow()
    {
        InitializeComponent();
        MemoryItems.ItemsSource = _items;
        ApplyState(MemoryGalleryState.Empty, preserveScrollPosition: false);
    }

    internal MemoryGalleryWindow(MemoryGalleryState state)
        : this()
    {
        Refresh(state);
    }

    public event EventHandler<MemoryReplayRequestedEventArgs>?
        MemoryReplayRequested;

    internal int VisibleMemoryCount => _items.Count;

    internal IReadOnlyList<string> VisibleMemoryIdsForTest =>
        _items.Select(static item => item.Entry.MemoryId).ToArray();

    internal IReadOnlyList<string> ActionLabelsForTest =>
        _items.Select(static item => item.Entry.ActionLabel).ToArray();

    internal string SummaryTextForTest => SummaryText.Text;

    internal string StatusTextForTest => StatusText.Text;

    internal bool ReplayRequestInFlightForTest => _replayRequestInFlight;

    internal void Refresh(MemoryGalleryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(
                () => Refresh(state),
                DispatcherPriority.DataBind);
            return;
        }

        ApplyState(state, preserveScrollPosition: true);
    }

    internal void ShowGallery()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(ShowGallery);
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        _ = Activate();
    }

    internal bool TryRequestReplayForTest(string memoryId)
    {
        MemoryGalleryCardItem? item = _items.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Entry.MemoryId,
                memoryId,
                StringComparison.Ordinal));
        return item is not null && TryRequestReplay(item);
    }

    private void ApplyState(
        MemoryGalleryState state,
        bool preserveScrollPosition)
    {
        double previousOffset = preserveScrollPosition
            ? GalleryScrollViewer.VerticalOffset
            : 0;
        _state = state;
        _replayRequestInFlight = false;
        _items.Clear();
        foreach (MemoryGalleryEntry entry in state.Entries)
        {
            _items.Add(new MemoryGalleryCardItem(entry));
        }

        SummaryText.Text = state.SummaryText;
        StatusText.Text = state.StatusText;
        bool isEmpty = _items.Count == 0;
        GalleryScrollViewer.Visibility = isEmpty
            ? Visibility.Collapsed
            : Visibility.Visible;
        EmptyState.Visibility = isEmpty
            ? Visibility.Visible
            : Visibility.Collapsed;
        MemoryItems.IsEnabled = !state.IsBusy;

        if (previousOffset > 0 && !isEmpty)
        {
            _ = Dispatcher.BeginInvoke(
                () => GalleryScrollViewer.ScrollToVerticalOffset(
                    previousOffset),
                DispatcherPriority.Loaded);
        }
    }

    private void OnMemoryClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            {
                Tag: MemoryGalleryCardItem item,
            })
        {
            _ = TryRequestReplay(item);
        }
    }

    private bool TryRequestReplay(MemoryGalleryCardItem item)
    {
        if (_state.IsBusy ||
            _replayRequestInFlight ||
            !item.Entry.IsEnabled)
        {
            return false;
        }

        EventHandler<MemoryReplayRequestedEventArgs>? handler =
            MemoryReplayRequested;
        if (handler is null)
        {
            StatusText.Text = "咕噜还没准备好，等她一下吧";
            return false;
        }

        _replayRequestInFlight = true;
        MemoryItems.IsEnabled = false;
        StatusText.Text = $"正在打开「{item.Entry.Title}」";
        try
        {
            handler.Invoke(
                this,
                new MemoryReplayRequestedEventArgs(item.Entry.MemoryId));
            return true;
        }
        catch
        {
            _replayRequestInFlight = false;
            MemoryItems.IsEnabled = !_state.IsBusy;
            StatusText.Text = "这段回忆刚刚没能打开，再试一次吧";
            throw;
        }
    }

    private sealed class MemoryGalleryCardItem
    {
        public MemoryGalleryCardItem(MemoryGalleryEntry entry)
        {
            Entry = entry;
            Thumbnail = TryLoadThumbnail(entry.ThumbnailPath);
        }

        public MemoryGalleryEntry Entry { get; }

        public ImageSource? Thumbnail { get; }

        public Visibility MissingThumbnailVisibility => Thumbnail is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        private static BitmapSource? TryLoadThumbnail(string path)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 720;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception exception)
                when (exception is IOException
                      or UnauthorizedAccessException
                      or FileFormatException
                      or NotSupportedException)
            {
                return null;
            }
        }
    }
}
