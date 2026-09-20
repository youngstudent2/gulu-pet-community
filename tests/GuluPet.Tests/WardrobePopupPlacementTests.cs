using GuluPet.Platform;

namespace GuluPet.Tests;

internal static class WardrobePopupPlacementTests
{
    public static void RunAll()
    {
        Run(nameof(PlacesBelowAndCentersOnPet), PlacesBelowAndCentersOnPet);
        Run(
            nameof(FlipsAboveWhenBottomSpaceIsInsufficient),
            FlipsAboveWhenBottomSpaceIsInsufficient);
        Run(
            nameof(ClampsAtBothHorizontalWorkAreaEdges),
            ClampsAtBothHorizontalWorkAreaEdges);
        Run(
            nameof(SupportsNegativeDipCoordinates),
            SupportsNegativeDipCoordinates);
        Run(
            nameof(FitsInsideAWorkAreaNarrowerThanRequested),
            FitsInsideAWorkAreaNarrowerThanRequested);
        Run(
            nameof(VisibleSurfaceKeepsBottomPreferenceWithShadowHost),
            VisibleSurfaceKeepsBottomPreferenceWithShadowHost);
        Run(nameof(RejectsInvalidDipValues), RejectsInvalidDipValues);
    }

    private static void PlacesBelowAndCentersOnPet()
    {
        WardrobePopupPlacementResult placement =
            WardrobePopupPlacement.Calculate(
                new WardrobePopupDipRectangle(800, 300, 280, 260),
                new WardrobePopupDipRectangle(0, 0, 1_920, 1_040),
                popupWidthDips: 600,
                popupHeightDips: 112);

        BehaviorTestCheck.Equal(
            WardrobePopupPlacementSide.BelowPet,
            placement.Side);
        AssertRectangle(
            new WardrobePopupDipRectangle(640, 570, 600, 112),
            placement.BoundsDips);
    }

    private static void FlipsAboveWhenBottomSpaceIsInsufficient()
    {
        WardrobePopupPlacementResult placement =
            WardrobePopupPlacement.Calculate(
                new WardrobePopupDipRectangle(800, 800, 280, 200),
                new WardrobePopupDipRectangle(0, 0, 1_920, 1_040),
                popupWidthDips: 600,
                popupHeightDips: 112);

        BehaviorTestCheck.Equal(
            WardrobePopupPlacementSide.AbovePet,
            placement.Side);
        AssertRectangle(
            new WardrobePopupDipRectangle(640, 678, 600, 112),
            placement.BoundsDips);
    }

    private static void ClampsAtBothHorizontalWorkAreaEdges()
    {
        WardrobePopupDipRectangle workArea =
            new(100, 40, 1_500, 900);
        WardrobePopupPlacementResult left =
            WardrobePopupPlacement.Calculate(
                new WardrobePopupDipRectangle(100, 240, 280, 260),
                workArea,
                popupWidthDips: 600,
                popupHeightDips: 112);
        WardrobePopupPlacementResult right =
            WardrobePopupPlacement.Calculate(
                new WardrobePopupDipRectangle(1_400, 240, 280, 260),
                workArea,
                popupWidthDips: 600,
                popupHeightDips: 112);

        BehaviorTestCheck.Close(112, left.BoundsDips.Left);
        BehaviorTestCheck.Close(988, right.BoundsDips.Left);
        AssertContained(left.BoundsDips, workArea);
        AssertContained(right.BoundsDips, workArea);
    }

    private static void SupportsNegativeDipCoordinates()
    {
        WardrobePopupDipRectangle workArea =
            new(-1_920, -120, 1_920, 1_040);
        WardrobePopupPlacementResult placement =
            WardrobePopupPlacement.Calculate(
                new WardrobePopupDipRectangle(-1_850, 280, 280, 260),
                workArea,
                popupWidthDips: 600,
                popupHeightDips: 112);

        BehaviorTestCheck.True(placement.BoundsDips.Left < 0);
        AssertContained(placement.BoundsDips, workArea);
    }

    private static void FitsInsideAWorkAreaNarrowerThanRequested()
    {
        WardrobePopupDipRectangle workArea = new(0, 0, 500, 700);
        WardrobePopupPlacementResult placement =
            WardrobePopupPlacement.Calculate(
                new WardrobePopupDipRectangle(110, 220, 280, 260),
                workArea,
                popupWidthDips: 600,
                popupHeightDips: 112);

        BehaviorTestCheck.Close(12, placement.BoundsDips.Left);
        BehaviorTestCheck.Close(476, placement.BoundsDips.Width);
        AssertContained(placement.BoundsDips, workArea);
    }

    private static void VisibleSurfaceKeepsBottomPreferenceWithShadowHost()
    {
        WardrobePopupDipRectangle workArea =
            new(0, 0, 1_920, 1_040);
        WardrobePopupHostPlacementResult placement =
            WardrobePopupPlacement.CalculateWithShadowHost(
                new WardrobePopupDipRectangle(820, 650, 280, 200),
                workArea,
                surfaceWidthDips: 640,
                surfaceHeightDips: 132,
                shadowPaddingDips: 24);

        BehaviorTestCheck.Equal(
            WardrobePopupPlacementSide.BelowPet,
            placement.Side);
        BehaviorTestCheck.Close(
            860,
            placement.SurfaceBoundsDips.Top);
        BehaviorTestCheck.Close(
            10,
            placement.SurfaceBoundsDips.Top - 850);
        BehaviorTestCheck.Close(
            688,
            placement.HostBoundsDips.Width);
        BehaviorTestCheck.Close(
            180,
            placement.HostBoundsDips.Height);
        AssertContained(placement.HostBoundsDips, workArea);
    }

    private static void RejectsInvalidDipValues()
    {
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => WardrobePopupPlacement.Calculate(
                new WardrobePopupDipRectangle(0, 0, 280, 260),
                new WardrobePopupDipRectangle(0, 0, 1_920, 1_040),
                popupWidthDips: double.NaN,
                popupHeightDips: 112));
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => WardrobePopupPlacement.Calculate(
                new WardrobePopupDipRectangle(
                    double.PositiveInfinity,
                    0,
                    280,
                    260),
                new WardrobePopupDipRectangle(0, 0, 1_920, 1_040),
                popupWidthDips: 600,
                popupHeightDips: 112));
    }

    private static void AssertRectangle(
        WardrobePopupDipRectangle expected,
        WardrobePopupDipRectangle actual)
    {
        BehaviorTestCheck.Close(expected.Left, actual.Left);
        BehaviorTestCheck.Close(expected.Top, actual.Top);
        BehaviorTestCheck.Close(expected.Width, actual.Width);
        BehaviorTestCheck.Close(expected.Height, actual.Height);
    }

    private static void AssertContained(
        WardrobePopupDipRectangle candidate,
        WardrobePopupDipRectangle workArea)
    {
        BehaviorTestCheck.True(candidate.Left >= workArea.Left);
        BehaviorTestCheck.True(candidate.Top >= workArea.Top);
        BehaviorTestCheck.True(candidate.Right <= workArea.Right);
        BehaviorTestCheck.True(candidate.Bottom <= workArea.Bottom);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(WardrobePopupPlacementTests)}.{name}");
    }
}
