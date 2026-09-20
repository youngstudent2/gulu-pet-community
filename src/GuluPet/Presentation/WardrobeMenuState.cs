using GuluPet.Accessories;

namespace GuluPet.Presentation;

internal sealed record WardrobeMenuEntry(
    int Index,
    string? Id,
    string DisplayName,
    string? ImagePath,
    bool IsSelected)
{
    public bool IsNone => Id is null;
}

/// <summary>
/// Small, UI-agnostic state holder for the two-page wardrobe item bar.
/// </summary>
internal sealed class WardrobeMenuState
{
    public const int ItemsPerPage = 10;
    public const string NoAccessoryDisplayName = "不佩戴";

    private WardrobeMenuEntry[] _entries;
    private WardrobeMenuEntry[] _currentPageEntries;

    public WardrobeMenuState(
        IReadOnlyList<EyeAccessoryDefinition> accessories,
        string? selectedAccessoryId)
    {
        ArgumentNullException.ThrowIfNull(accessories);
        if (accessories.Count != EyeAccessoryCatalog.RequiredAccessoryCount)
        {
            throw new ArgumentException(
                $"Wardrobe requires exactly " +
                $"{EyeAccessoryCatalog.RequiredAccessoryCount} accessories.",
                nameof(accessories));
        }

        if (accessories.Any(static accessory => accessory is null))
        {
            throw new ArgumentException(
                "Wardrobe accessories must be non-null.",
                nameof(accessories));
        }

        EyeAccessoryDefinition[] ordered = accessories
            .OrderBy(static accessory => accessory.SelectionOrder)
            .ToArray();
        if (ordered.Select(static accessory => accessory.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() != ordered.Length)
        {
            throw new ArgumentException(
                "Wardrobe accessories must have unique ids.",
                nameof(accessories));
        }

        _entries =
        [
            new WardrobeMenuEntry(
                Index: 0,
                Id: null,
                NoAccessoryDisplayName,
                ImagePath: null,
                IsSelected: false),
            .. ordered.Select(
                static (accessory, index) => new WardrobeMenuEntry(
                    Index: checked(index + 1),
                    accessory.Id,
                    accessory.DisplayName,
                    accessory.ImagePath,
                    IsSelected: false)),
        ];
        _currentPageEntries = [];

        int selectedIndex = FindEntryIndex(selectedAccessoryId);
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        ApplySelection(selectedIndex);
    }

    public IReadOnlyList<WardrobeMenuEntry> Entries => _entries;

    public IReadOnlyList<WardrobeMenuEntry> CurrentPageEntries =>
        _currentPageEntries;

    public string? SelectedAccessoryId { get; private set; }

    public int CurrentPageIndex { get; private set; }

    public int CurrentPageNumber => checked(CurrentPageIndex + 1);

    public int PageCount =>
        checked((_entries.Length + ItemsPerPage - 1) / ItemsPerPage);

    public bool CanMovePreviousPage => CurrentPageIndex > 0;

    public bool CanMoveNextPage => CurrentPageIndex + 1 < PageCount;

    public bool MovePreviousPage()
    {
        if (!CanMovePreviousPage)
        {
            return false;
        }

        SetCurrentPage(CurrentPageIndex - 1);
        return true;
    }

    public bool MoveNextPage()
    {
        if (!CanMoveNextPage)
        {
            return false;
        }

        SetCurrentPage(CurrentPageIndex + 1);
        return true;
    }

    /// <summary>
    /// Selects an entry and moves to the page containing it. Null selects the
    /// first, no-accessory entry. Returns whether the selection changed.
    /// </summary>
    public bool Select(string? accessoryId)
    {
        int selectedIndex = FindEntryIndex(accessoryId);
        if (selectedIndex < 0)
        {
            throw new ArgumentException(
                $"Unknown wardrobe accessory '{accessoryId}'.",
                nameof(accessoryId));
        }

        bool changed = !string.Equals(
            SelectedAccessoryId,
            accessoryId,
            StringComparison.Ordinal);
        ApplySelection(selectedIndex);
        return changed;
    }

    private int FindEntryIndex(string? accessoryId) =>
        Array.FindIndex(
            _entries,
            entry => string.Equals(
                entry.Id,
                accessoryId,
                StringComparison.Ordinal));

    private void ApplySelection(int selectedIndex)
    {
        WardrobeMenuEntry selected = _entries[selectedIndex];
        SelectedAccessoryId = selected.Id;
        _entries = _entries
            .Select(
                entry => entry with
                {
                    IsSelected = entry.Index == selected.Index,
                })
            .ToArray();
        SetCurrentPage(selectedIndex / ItemsPerPage);
    }

    private void SetCurrentPage(int pageIndex)
    {
        CurrentPageIndex = pageIndex;
        _currentPageEntries = _entries
            .Skip(checked(pageIndex * ItemsPerPage))
            .Take(ItemsPerPage)
            .ToArray();
    }
}
