using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GuluPet.Behavior;

namespace GuluPet.Presentation;

public sealed class BehaviorPreviewRequestedEventArgs(
    BehaviorId behaviorId) : EventArgs
{
    public BehaviorId BehaviorId { get; } = behaviorId;

    public BehaviorMutationResult? Result { get; set; }
}

public partial class BehaviorBrowserWindow : Window
{
    private const string AllFamilies = "全部小动作";
    private static readonly BehaviorFamilyOption[] KnownFamilies =
    [
        new("Contact", "偶尔亲近", 0),
        new("Play", "活泼玩耍", 1),
        new("Curiosity", "好奇观察", 2),
        new("Boundary", "有点小脾气", 3),
        new("Vocalize", "猫语回应", 4),
        new("SelfCare", "认真梳毛", 5),
        new("Rest", "懒懒休息", 6),
        new("Sleep", "困倦睡眠", 7),
        new("QuietCompany", "安静陪伴", 8),
        new("Lifecycle", "醒来舒展", 9),
    ];

    private readonly ObservableCollection<BehaviorBrowserItem> _items = [];
    private readonly ICollectionView _view;

    public BehaviorBrowserWindow(
        IEnumerable<BehaviorDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        InitializeComponent();

        BehaviorDefinition[] snapshot = definitions
            .OrderBy(
                static definition =>
                    GetFamilyOption(definition.Family).SortOrder)
            .ThenBy(static definition => definition.DisplayName, StringComparer.Ordinal)
            .ThenBy(static definition => definition.Id.Value, StringComparer.Ordinal)
            .ToArray();
        foreach (BehaviorDefinition definition in snapshot)
        {
            _items.Add(new BehaviorBrowserItem(definition));
        }

        BehaviorList.ItemsSource = _items;
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = MatchesCurrentFilter;
        FamilyFilter.Items.Add(
            new BehaviorFamilyOption(string.Empty, AllFamilies, -1));
        foreach (BehaviorFamilyOption family in snapshot
                     .Select(static definition => definition.Family)
                     .Distinct(StringComparer.Ordinal)
                     .Select(GetFamilyOption)
                     .OrderBy(static option => option.SortOrder)
                     .ThenBy(
                         static option => option.DisplayName,
                         StringComparer.Ordinal))
        {
            FamilyFilter.Items.Add(family);
        }

        FamilyFilter.SelectedIndex = 0;
        UpdateCount();
    }

    public event EventHandler<BehaviorPreviewRequestedEventArgs>?
        PreviewRequested;

    internal int VisibleBehaviorCount =>
        _view.Cast<object>().Count();

    internal IReadOnlyList<string> FamilyFilterLabelsForTest =>
        FamilyFilter.Items
            .Cast<BehaviorFamilyOption>()
            .Select(static option => option.DisplayName)
            .ToArray();

    internal IReadOnlyList<string> VisibleDetailsForTest =>
        _view.Cast<BehaviorBrowserItem>()
            .Select(static item => item.Detail)
            .ToArray();

    internal IReadOnlyList<string> VisibleTriggerDescriptionsForTest =>
        _view.Cast<BehaviorBrowserItem>()
            .Select(static item => item.TriggerDescription)
            .ToArray();

    internal static string DescribeRejectionForTest(string? reason) =>
        DescribeRejection(reason);

    internal void ApplyFilterForTest(
        string query,
        string? family = null)
    {
        SearchBox.Text = query ?? string.Empty;
        BehaviorFamilyOption selected = FamilyFilter.Items
            .Cast<BehaviorFamilyOption>()
            .FirstOrDefault(option =>
                string.IsNullOrWhiteSpace(family)
                    ? option.Key.Length == 0
                    : string.Equals(
                          option.Key,
                          family,
                          StringComparison.Ordinal) ||
                      string.Equals(
                          option.DisplayName,
                          family,
                          StringComparison.Ordinal))
            ?? throw new ArgumentException(
                $"Unknown behavior family filter '{family}'.",
                nameof(family));
        FamilyFilter.SelectedItem = selected;
        RefreshFilter();
    }

    private bool MatchesCurrentFilter(object item)
    {
        if (item is not BehaviorBrowserItem behavior)
        {
            return false;
        }

        string? family =
            (FamilyFilter.SelectedItem as BehaviorFamilyOption)?.Key;
        if (!string.IsNullOrWhiteSpace(family) &&
            !string.Equals(
                behavior.Family,
                family,
                StringComparison.Ordinal))
        {
            return false;
        }

        string query = SearchBox.Text.Trim();
        return query.Length == 0 ||
               behavior.SearchText.Contains(
                   query,
                   StringComparison.OrdinalIgnoreCase);
    }

    private void OnSearchTextChanged(
        object sender,
        TextChangedEventArgs e) =>
        RefreshFilter();

    private void OnFamilySelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        RefreshFilter();

    private void RefreshFilter()
    {
        if (!IsInitialized)
        {
            return;
        }

        _view.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        int visible = VisibleBehaviorCount;
        CountText.Text = visible == _items.Count
            ? $"这里藏着 {_items.Count} 个小动作"
            : $"找到了 {visible} 个小动作";
    }

    private void OnBehaviorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button
            {
                Tag: BehaviorBrowserItem item,
            })
        {
            return;
        }

        var request = new BehaviorPreviewRequestedEventArgs(item.Id);
        PreviewRequested?.Invoke(this, request);
        if (request.Result is null)
        {
            StatusText.Text = "咕噜还没准备好，等她一下吧";
            return;
        }

        StatusText.Text = request.Result.Accepted
            ? $"咕噜正在：{item.DisplayName}"
            : DescribeRejection(request.Result.RejectionReason);
    }

    private static string DescribeRejection(string? reason) =>
        reason switch
        {
            "dragging" => "先把咕噜放稳，再试一次吧",
            "runtime-paused" => "咕噜正忙着别的事，等她一下吧",
            "runtime-not-started" => "咕噜还没睡醒，等她一下吧",
            "unknown-behavior" => "这个小动作暂时找不到了",
            _ => "咕噜这会儿不想配合，过会儿再试吧",
        };

    private sealed class BehaviorBrowserItem
    {
        public BehaviorBrowserItem(BehaviorDefinition definition)
        {
            Id = definition.Id;
            DisplayName = definition.DisplayName;
            Family = definition.Family;
            FamilyDisplayName =
                GetFamilyOption(definition.Family).DisplayName;
            TriggerDescription =
                BehaviorTriggerDescriptionFormatter.Describe(definition);
            Detail = $"「{FamilyDisplayName}」里的小动作";
            AutomationName = $"看看咕噜的「{definition.DisplayName}」";
            SearchText =
                $"{definition.DisplayName} {FamilyDisplayName} " +
                TriggerDescription;
        }

        public BehaviorId Id { get; }

        public string DisplayName { get; }

        public string Family { get; }

        public string FamilyDisplayName { get; }

        public string TriggerDescription { get; }

        public string Detail { get; }

        public string AutomationName { get; }

        public string SearchText { get; }
    }

    private static BehaviorFamilyOption GetFamilyOption(string family) =>
        KnownFamilies.FirstOrDefault(option =>
            string.Equals(
                option.Key,
                family,
                StringComparison.Ordinal))
        ?? new BehaviorFamilyOption(
            family,
            "藏起来的小动作",
            int.MaxValue);

    private sealed record BehaviorFamilyOption(
        string Key,
        string DisplayName,
        int SortOrder)
    {
        public override string ToString() => DisplayName;
    }
}
