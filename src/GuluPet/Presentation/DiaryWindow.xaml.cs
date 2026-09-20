using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuluPet.Diary;

namespace GuluPet.Presentation;

public sealed class DiaryDateDisplayedEventArgs : EventArgs
{
    internal DiaryDateDisplayedEventArgs(DateOnly date)
    {
        Date = date;
    }

    public DateOnly Date { get; }
}

/// <summary>
/// Read-only PC diary browser. The window never mutates the ledger; callers
/// can refresh it after the diary controller publishes a state change.
/// </summary>
public partial class DiaryWindow : Window
{
    private DiaryLedger? _ledger;
    private IReadOnlyList<DiaryDateListItem> _dateItems = [];
    private HashSet<DateOnly> _readDiaryDates = [];
    private bool _isRefreshing;

    public DiaryWindow()
    {
        InitializeComponent();
        TryLoadFooterImage();
        ShowEmptyState();
    }

    public DiaryWindow(DiaryLedger ledger)
        : this()
    {
        BindLedger(ledger);
    }

    public DateOnly? SelectedDate =>
        DateList.SelectedItem is DiaryDateListItem item
            ? item.Date
            : null;

    public int DateCount => _dateItems.Count;

    internal int UnreadDateCount =>
        _dateItems.Count(static item => item.IsUnread);

    /// <summary>
    /// Raised only after a generated diary page is placed in the detail pane.
    /// Pending dates do not count as viewed diary pages.
    /// </summary>
    public event EventHandler<DiaryDateDisplayedEventArgs>?
        DiaryDateDisplayed;

    public void BindLedger(DiaryLedger ledger)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        Refresh();
    }

    public void Refresh()
    {
        DiaryLedger? ledger = _ledger;
        if (ledger is null)
        {
            Refresh(DiaryState.CreateDefault());
            return;
        }

        Refresh(ledger.Current);
    }

    public void Refresh(DiaryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Refresh(state));
            return;
        }

        DateOnly? previouslySelected = SelectedDate;
        DiaryDayState[] visibleDays = state.Days
            .Where(static day => day.Entry is not null
                || day.HasMeaningfulRecord)
            .OrderByDescending(static day => day.Date)
            .ToArray();
        _dateItems = visibleDays
            .Select(day => DiaryDateListItem.Create(
                day,
                _readDiaryDates.Contains(day.Date)))
            .ToArray();

        _isRefreshing = true;
        try
        {
            DateList.ItemsSource = _dateItems;
            DiaryCountText.Text = $"{visibleDays.Count(static day => day.Entry is not null)} 篇";
            bool isEmpty = _dateItems.Count == 0;
            DateList.Visibility = isEmpty
                ? Visibility.Collapsed
                : Visibility.Visible;
            NoDatesPanel.Visibility = isEmpty
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (isEmpty)
            {
                DateList.SelectedItem = null;
                ShowEmptyState();
                return;
            }

            DiaryDateListItem? selection = previouslySelected is null
                ? null
                : _dateItems.FirstOrDefault(
                    item => item.Date == previouslySelected.Value);
            selection ??= _dateItems.FirstOrDefault(
                static item => item.HasGeneratedEntry);
            selection ??= _dateItems[0];
            DateList.SelectedItem = selection;
            DateList.ScrollIntoView(selection);
            ShowDay(selection.Day);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public bool TrySelectDate(DateOnly date)
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(() => TrySelectDate(date));
        }

        DiaryDateListItem? item = _dateItems.FirstOrDefault(
            candidate => candidate.Date == date);
        if (item is null)
        {
            return false;
        }

        DateList.SelectedItem = item;
        DateList.ScrollIntoView(item);
        return true;
    }

    /// <summary>
    /// Refreshes read badges without rebuilding diary content or reporting the
    /// currently selected page as newly displayed a second time.
    /// </summary>
    public void RefreshReadDiaryDates(IEnumerable<DateOnly> readDates)
    {
        ArgumentNullException.ThrowIfNull(readDates);
        if (!Dispatcher.CheckAccess())
        {
            DateOnly[] snapshot = readDates.Distinct().ToArray();
            Dispatcher.Invoke(() => RefreshReadDiaryDates(snapshot));
            return;
        }

        var normalized = new HashSet<DateOnly>(readDates);
        if (_readDiaryDates.SetEquals(normalized))
        {
            return;
        }

        DateOnly? selectedDate = SelectedDate;
        _readDiaryDates = normalized;
        _dateItems = _dateItems
            .Select(item => item.WithReadState(
                _readDiaryDates.Contains(item.Date)))
            .ToArray();

        _isRefreshing = true;
        try
        {
            DateList.ItemsSource = _dateItems;
            if (selectedDate is { } date)
            {
                DateList.SelectedItem = _dateItems.FirstOrDefault(
                    item => item.Date == date);
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    internal void SaveRenderedPreview(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Dispatcher.VerifyAccess();
        if (Content is not FrameworkElement contentRoot)
        {
            throw new InvalidOperationException(
                "The diary window has no renderable content root.");
        }

        UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(contentRoot.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(contentRoot.ActualHeight));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(contentRoot);

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "The diary preview path has no parent directory.");
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        encoder.Save(stream);
        stream.Flush(flushToDisk: true);
    }

    private void OnDateSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        if (DateList.SelectedItem is DiaryDateListItem item)
        {
            ShowDay(item.Day);
        }
    }

    private void ShowDay(DiaryDayState day)
    {
        DiaryDetailPanel.DataContext = DiaryDayViewModel.Create(day);
        DiaryDetailPanel.Visibility = Visibility.Visible;
        DiaryScrollViewer.Visibility = Visibility.Visible;
        DiaryEmptyPanel.Visibility = Visibility.Collapsed;
        DiaryScrollViewer.ScrollToTop();
        if (day.Entry is not null)
        {
            DiaryDateDisplayed?.Invoke(
                this,
                new DiaryDateDisplayedEventArgs(day.Date));
        }
    }

    private void ShowEmptyState()
    {
        DiaryDetailPanel.DataContext = null;
        DiaryDetailPanel.Visibility = Visibility.Collapsed;
        DiaryScrollViewer.Visibility = Visibility.Collapsed;
        DiaryEmptyPanel.Visibility = Visibility.Visible;
        DiaryCountText.Text = "0 篇";
    }

    private void TryLoadFooterImage()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Diary",
            "diary-paper-footer.png");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            DiaryFooterImage.Source = bitmap;
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException)
        {
            DiaryFooterImage.Source = null;
        }
    }
}

internal sealed record DiaryDateListItem(
    DateOnly Date,
    DiaryDayState Day,
    string DayText,
    string MonthAndWeekday,
    string StatusText,
    bool HasGeneratedEntry,
    bool IsUnread,
    Visibility NewBadgeVisibility)
{
    internal static DiaryDateListItem Create(
        DiaryDayState day,
        bool isRead)
    {
        GeneratedDiaryEntry? entry = day.Entry;
        bool isUnread = entry is not null && !isRead;
        return new DiaryDateListItem(
            day.Date,
            day,
            day.Date.Day.ToString(CultureInfo.InvariantCulture),
            $"{day.Date:yyyy年M月} · {DiaryUiText.FormatWeekday(day.Date)}",
            entry?.Mood ?? "还没写完",
            entry is not null,
            isUnread,
            isUnread ? Visibility.Visible : Visibility.Collapsed);
    }

    internal DiaryDateListItem WithReadState(bool isRead) =>
        this with
        {
            IsUnread = HasGeneratedEntry && !isRead,
            NewBadgeVisibility = HasGeneratedEntry && !isRead
                ? Visibility.Visible
                : Visibility.Collapsed,
        };
}

internal sealed record DiaryInteractionViewModel(
    string Label,
    string CountText);

internal sealed record DiaryUnlockViewModel(
    string Title,
    string Detail,
    Visibility DetailVisibility)
{
    internal static DiaryUnlockViewModel Create(DiaryUnlockRecord record) =>
        new(
            record.Title,
            record.Detail?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(record.Detail)
                ? Visibility.Collapsed
                : Visibility.Visible);
}

internal sealed record DiaryDayViewModel
{
    public required string DateHeading { get; init; }

    public required string PeriodText { get; init; }

    public required string Title { get; init; }

    public required string Mood { get; init; }

    public required string Body { get; init; }

    public required string WeatherText { get; init; }

    public required string RuntimeText { get; init; }

    public required string InteractionTotalText { get; init; }

    public required IReadOnlyList<DiaryInteractionViewModel> Interactions
    {
        get;
        init;
    }

    public required Visibility InteractionSectionVisibility { get; init; }

    public required string PostcardHeading { get; init; }

    public required IReadOnlyList<DiaryUnlockViewModel> Postcards
    {
        get;
        init;
    }

    public required string NoPostcardsText { get; init; }

    public required Visibility NoPostcardsVisibility { get; init; }

    public required string MemoryHeading { get; init; }

    public required IReadOnlyList<DiaryUnlockViewModel> Memories
    {
        get;
        init;
    }

    public required string NoMemoriesText { get; init; }

    public required Visibility NoMemoriesVisibility { get; init; }

    public required string AttributionText { get; init; }

    internal static DiaryDayViewModel Create(DiaryDayState day)
    {
        GeneratedDiaryEntry? entry = day.Entry;
        DiaryInteractionViewModel[] interactions = day.Interactions
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.Kind, StringComparer.Ordinal)
            .Select(static item => new DiaryInteractionViewModel(
                DiaryInteractionKinds.GetLabel(item.Kind),
                $"{item.Count:N0} 次"))
            .ToArray();
        DiaryUnlockViewModel[] postcards = day.Postcards
            .Select(static record => DiaryUnlockViewModel.Create(record))
            .ToArray();
        DiaryUnlockViewModel[] memories = day.Memories
            .Select(static record => DiaryUnlockViewModel.Create(record))
            .ToArray();

        return new DiaryDayViewModel
        {
            DateHeading = $"{day.Date:yyyy年M月d日} · {DiaryUiText.FormatWeekday(day.Date)}",
            PeriodText = $"这一页记着  {day.Date.AddDays(-1):M月d日}傍晚到{day.Date:M月d日}傍晚",
            Title = entry?.Title ?? "这页还在酝酿",
            Mood = entry?.Mood ?? "还没写完",
            Body = entry?.Body
                ?? "今天的小事已经收好了。等我想好怎么写，再给妈咪看。",
            WeatherText = DiaryUiText.FormatWeather(day.Weather),
            RuntimeText = DiaryUiText.FormatRuntime(day.RuntimeSeconds),
            InteractionTotalText = $"{day.TotalInteractionCount:N0} 次",
            Interactions = interactions,
            InteractionSectionVisibility = interactions.Length == 0
                ? Visibility.Collapsed
                : Visibility.Visible,
            PostcardHeading = $"带回的明信片 · {postcards.Length:N0}",
            Postcards = postcards,
            NoPostcardsText = "今天没有新的明信片，下一封也许正在路上",
            NoPostcardsVisibility = postcards.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed,
            MemoryHeading = $"想起的回忆 · {memories.Length:N0}",
            Memories = memories,
            NoMemoriesText = "今天没有新的回忆，熟悉的日子也很好",
            NoMemoriesVisibility = memories.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed,
            AttributionText = entry is null
                ? "今天的小事，我先替妈咪收好了"
                : "咕噜写给妈咪",
        };
    }
}

internal static class DiaryUiText
{
    internal static string FormatWeekday(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).DayOfWeek switch
        {
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            _ => "星期日",
        };

    internal static string FormatWeather(DiaryWeatherSnapshot? weather)
    {
        if (weather is null)
        {
            return "那时没留意";
        }

        string label = DiaryPromptBuilder.FormatWeather(weather.Kind);
        return $"{label} · {weather.TemperatureCelsius:0.#}℃";
    }

    internal static string FormatRuntime(long runtimeSeconds)
    {
        if (runtimeSeconds <= 0)
        {
            return "0 分钟";
        }

        if (runtimeSeconds < 60)
        {
            return "不足 1 分钟";
        }

        long totalMinutes = runtimeSeconds / 60;
        long hours = totalMinutes / 60;
        long minutes = totalMinutes % 60;
        if (hours == 0)
        {
            return $"{minutes:N0} 分钟";
        }

        return minutes == 0
            ? $"{hours:N0} 小时"
            : $"{hours:N0} 小时 {minutes:N0} 分钟";
    }
}
