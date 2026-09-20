using GuluPet.Accessories;
using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class WardrobeMenuStateTests
{
    public static void RunAll()
    {
        Run(
            nameof(BuildsTwoTenItemPagesWithNoAccessoryFirst),
            BuildsTwoTenItemPagesWithNoAccessoryFirst);
        Run(
            nameof(InitialSelectionOpensItsPage),
            InitialSelectionOpensItsPage);
        Run(
            nameof(PageNavigationStopsAtBothBoundaries),
            PageNavigationStopsAtBothBoundaries);
        Run(
            nameof(SelectionUpdatesFlagsAndReturnsToItsPage),
            SelectionUpdatesFlagsAndReturnsToItsPage);
        Run(
            nameof(UnknownInitialSelectionFallsBackToNoAccessory),
            UnknownInitialSelectionFallsBackToNoAccessory);
    }

    private static void BuildsTwoTenItemPagesWithNoAccessoryFirst()
    {
        WardrobeMenuState state = CreateState(selectedId: null);

        BehaviorTestCheck.Equal(20, state.Entries.Count);
        BehaviorTestCheck.Equal(2, state.PageCount);
        BehaviorTestCheck.Equal(10, state.CurrentPageEntries.Count);
        BehaviorTestCheck.True(state.Entries[0].IsNone);
        BehaviorTestCheck.Equal(
            WardrobeMenuState.NoAccessoryDisplayName,
            state.Entries[0].DisplayName);
        BehaviorTestCheck.Null(state.Entries[0].ImagePath);
        BehaviorTestCheck.SequenceEqual(
            Enumerable.Range(1, 9).Select(Id),
            state.CurrentPageEntries.Skip(1).Select(static entry => entry.Id));
        BehaviorTestCheck.Equal(
            PathFor(1),
            state.CurrentPageEntries[1].ImagePath);

        BehaviorTestCheck.True(state.MoveNextPage());
        BehaviorTestCheck.Equal(10, state.CurrentPageEntries.Count);
        BehaviorTestCheck.SequenceEqual(
            Enumerable.Range(10, 10).Select(Id),
            state.CurrentPageEntries.Select(static entry => entry.Id));
    }

    private static void InitialSelectionOpensItsPage()
    {
        WardrobeMenuState state = CreateState(Id(14));

        BehaviorTestCheck.Equal(1, state.CurrentPageIndex);
        BehaviorTestCheck.Equal(2, state.CurrentPageNumber);
        BehaviorTestCheck.Equal(Id(14), state.SelectedAccessoryId);
        BehaviorTestCheck.True(
            state.CurrentPageEntries.Single(entry => entry.Id == Id(14))
                .IsSelected);
        BehaviorTestCheck.Equal(
            1,
            state.Entries.Count(static entry => entry.IsSelected));
    }

    private static void PageNavigationStopsAtBothBoundaries()
    {
        WardrobeMenuState state = CreateState(selectedId: null);

        BehaviorTestCheck.False(state.CanMovePreviousPage);
        BehaviorTestCheck.False(state.MovePreviousPage());
        BehaviorTestCheck.Equal(0, state.CurrentPageIndex);
        BehaviorTestCheck.True(state.MoveNextPage());
        BehaviorTestCheck.False(state.CanMoveNextPage);
        BehaviorTestCheck.False(state.MoveNextPage());
        BehaviorTestCheck.Equal(1, state.CurrentPageIndex);
        BehaviorTestCheck.True(state.MovePreviousPage());
        BehaviorTestCheck.Equal(0, state.CurrentPageIndex);
    }

    private static void SelectionUpdatesFlagsAndReturnsToItsPage()
    {
        WardrobeMenuState state = CreateState(selectedId: null);
        _ = state.MoveNextPage();

        BehaviorTestCheck.True(state.Select(Id(4)));
        BehaviorTestCheck.Equal(0, state.CurrentPageIndex);
        BehaviorTestCheck.Equal(Id(4), state.SelectedAccessoryId);
        BehaviorTestCheck.True(
            state.Entries.Single(entry => entry.Id == Id(4)).IsSelected);
        BehaviorTestCheck.False(state.Entries[0].IsSelected);
        BehaviorTestCheck.False(state.Select(Id(4)));

        BehaviorTestCheck.True(state.Select(accessoryId: null));
        BehaviorTestCheck.Null(state.SelectedAccessoryId);
        BehaviorTestCheck.True(state.Entries[0].IsSelected);
        BehaviorTestCheck.Equal(0, state.CurrentPageIndex);
    }

    private static void UnknownInitialSelectionFallsBackToNoAccessory()
    {
        WardrobeMenuState state = CreateState("retired-accessory");

        BehaviorTestCheck.Null(state.SelectedAccessoryId);
        BehaviorTestCheck.True(state.Entries[0].IsSelected);
        BehaviorTestCheck.Equal(0, state.CurrentPageIndex);
        _ = BehaviorTestCheck.Throws<ArgumentException>(
            () => state.Select("retired-accessory"));
    }

    private static WardrobeMenuState CreateState(string? selectedId) =>
        new(CreateAccessories(), selectedId);

    private static IReadOnlyList<EyeAccessoryDefinition> CreateAccessories() =>
        Enumerable.Range(1, EyeAccessoryCatalog.RequiredAccessoryCount)
            .Reverse()
            .Select(CreateAccessory)
            .ToArray();

    private static EyeAccessoryDefinition CreateAccessory(int order) =>
        new(
            Id(order),
            $"饰品 {order}",
            order,
            $"accessory-{order:00}.png",
            PathFor(order),
            Width: 100,
            Height: 50,
            new EyeAccessoryPoint(25, 25),
            new EyeAccessoryPoint(75, 25),
            HeadWidthPixels: 100,
            Sha256: new string('A', 64),
            SizeBytes: 1,
            new Dictionary<
                EyeAccessoryViewState,
                EyeAccessoryVariantDefinition>());

    private static string Id(int order) => $"accessory-{order:00}";

    private static string PathFor(int order) =>
        $"C:\\Accessories\\accessory-{order:00}.png";

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(WardrobeMenuStateTests)}.{name}");
    }
}
