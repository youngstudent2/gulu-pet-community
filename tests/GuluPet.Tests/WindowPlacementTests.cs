using GuluPet.Platform;
using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class WindowPlacementTests
{
    public static void RunAll()
    {
        Run(
            nameof(ConvertsDipAnchorAtOneHundredFiftyPercent),
            ConvertsDipAnchorAtOneHundredFiftyPercent);
        Run(
            nameof(ConvertsDipAnchorAtOneHundredPercent),
            ConvertsDipAnchorAtOneHundredPercent);
        Run(
            nameof(CrossingFromOneHundredFiftyToOneHundredPercentUsesPhysicalCoordinates),
            CrossingFromOneHundredFiftyToOneHundredPercentUsesPhysicalCoordinates);
        Run(
            nameof(CrossingFromOneHundredToOneHundredFiftyPercentUsesPhysicalCoordinates),
            CrossingFromOneHundredToOneHundredFiftyPercentUsesPhysicalCoordinates);
        Run(
            nameof(CrossingHorizontalMonitorBoundaryRemainsTwoPhysicalPixels),
            CrossingHorizontalMonitorBoundaryRemainsTwoPhysicalPixels);
        Run(
            nameof(CrossingHorizontalMonitorBoundaryInReverseRemainsTwoPhysicalPixels),
            CrossingHorizontalMonitorBoundaryInReverseRemainsTwoPhysicalPixels);
        Run(
            nameof(SupportsNegativeDesktopCoordinates),
            SupportsNegativeDesktopCoordinates);
        Run(
            nameof(SupportsVerticalAndTwoDimensionalMonitorLayouts),
            SupportsVerticalAndTwoDimensionalMonitorLayouts);
        Run(
            nameof(RejectsZeroDpi),
            RejectsZeroDpi);
        Run(
            nameof(RejectsNonFiniteAnchor),
            RejectsNonFiniteAnchor);
        RunMemoryWindowPlacementOnly();
        Run(
            nameof(MemoryLandscapeUsesNativeResolution),
            MemoryLandscapeUsesNativeResolution);
        Run(
            nameof(MemoryPortraitFitsWorkAreaWithoutChangingAspect),
            MemoryPortraitFitsWorkAreaWithoutChangingAspect);
        Run(
            nameof(MemoryThreeByFourAndFourByThreePreserveAspect),
            MemoryThreeByFourAndFourByThreePreserveAspect);
        Run(
            nameof(MemoryMoveCentersTheRestoredPet),
            MemoryMoveCentersTheRestoredPet);
        Run(
            nameof(MemoryMoveUsesTheTargetMonitorDpiForTheRestoredPet),
            MemoryMoveUsesTheTargetMonitorDpiForTheRestoredPet);
        Run(
            nameof(MemoryRestoreLatchWaitsOutDelayedDpiCallbacks),
            MemoryRestoreLatchWaitsOutDelayedDpiCallbacks);
    }

    public static void RunMemoryWindowPlacementOnly()
    {
        Run(
            nameof(MemoryWindowInitialPlacementChoosesLargerSide),
            MemoryWindowInitialPlacementChoosesLargerSide);
        Run(
            nameof(MemoryWindowKeepsPreferredSideWhenItFits),
            MemoryWindowKeepsPreferredSideWhenItFits);
        Run(
            nameof(MemoryWindowFlipsWhenPreferredSideCannotFit),
            MemoryWindowFlipsWhenPreferredSideCannotFit);
        Run(
            nameof(MemoryWindowScalesWithoutCoveringPetInNarrowWorkArea),
            MemoryWindowScalesWithoutCoveringPetInNarrowWorkArea);
        Run(
            nameof(MemoryResponsiveWindowClampsHeightWithoutShrinkingWidth),
            MemoryResponsiveWindowClampsHeightWithoutShrinkingWidth);
        Run(
            nameof(MemoryResponsiveWindowClampsSideWidthWithoutShrinkingHeight),
            MemoryResponsiveWindowClampsSideWidthWithoutShrinkingHeight);
        Run(
            nameof(MemoryWindowCentersVerticallyAndClampsToWorkArea),
            MemoryWindowCentersVerticallyAndClampsToWorkArea);
        Run(
            nameof(MemoryWindowUsesDpiScaledGapWithNegativeCoordinates),
            MemoryWindowUsesDpiScaledGapWithNegativeCoordinates);
        Run(
            nameof(MemoryWindowStaysInsideWorkAreaWhenClearanceIsImpossible),
            MemoryWindowStaysInsideWorkAreaWhenClearanceIsImpossible);
        Run(
            nameof(MemoryPlaybackWindowSizingAdaptsCommonOrientations),
            MemoryPlaybackWindowSizingAdaptsCommonOrientations);
        Run(
            nameof(MemoryPlaybackWindowSizingKeepsPortraitVideoReadable),
            MemoryPlaybackWindowSizingKeepsPortraitVideoReadable);
        Run(
            nameof(MemoryPlaybackWindowSizingRejectsInvalidDimensions),
            MemoryPlaybackWindowSizingRejectsInvalidDimensions);
    }

    private static void ConvertsDipAnchorAtOneHundredFiftyPercent()
    {
        PhysicalWindowOrigin origin = PhysicalDragPlacement.Calculate(
            cursorX: 3_600,
            cursorY: 1_200,
            anchorDipX: 20,
            anchorDipY: 10,
            dpiX: 144,
            dpiY: 144);

        BehaviorTestCheck.Equal(3_570, origin.Left);
        BehaviorTestCheck.Equal(1_185, origin.Top);
    }

    private static void ConvertsDipAnchorAtOneHundredPercent()
    {
        PhysicalWindowOrigin origin = PhysicalDragPlacement.Calculate(
            cursorX: 4_200,
            cursorY: 900,
            anchorDipX: 20,
            anchorDipY: 10,
            dpiX: 96,
            dpiY: 96);

        BehaviorTestCheck.Equal(4_180, origin.Left);
        BehaviorTestCheck.Equal(890, origin.Top);
    }

    private static void CrossingFromOneHundredFiftyToOneHundredPercentUsesPhysicalCoordinates()
    {
        PhysicalWindowOrigin before = PhysicalDragPlacement.Calculate(
            cursorX: 3_800,
            cursorY: 900,
            anchorDipX: 16,
            anchorDipY: 8,
            dpiX: 144,
            dpiY: 144);
        PhysicalWindowOrigin after = PhysicalDragPlacement.Calculate(
            cursorX: 4_000,
            cursorY: 900,
            anchorDipX: 16,
            anchorDipY: 8,
            dpiX: 96,
            dpiY: 96);

        BehaviorTestCheck.Equal(3_776, before.Left);
        BehaviorTestCheck.Equal(888, before.Top);
        BehaviorTestCheck.Equal(3_984, after.Left);
        BehaviorTestCheck.Equal(892, after.Top);
    }

    private static void CrossingFromOneHundredToOneHundredFiftyPercentUsesPhysicalCoordinates()
    {
        PhysicalWindowOrigin before = PhysicalDragPlacement.Calculate(
            cursorX: 4_000,
            cursorY: 900,
            anchorDipX: 16,
            anchorDipY: 8,
            dpiX: 96,
            dpiY: 96);
        PhysicalWindowOrigin after = PhysicalDragPlacement.Calculate(
            cursorX: 3_800,
            cursorY: 900,
            anchorDipX: 16,
            anchorDipY: 8,
            dpiX: 144,
            dpiY: 144);

        BehaviorTestCheck.Equal(3_984, before.Left);
        BehaviorTestCheck.Equal(892, before.Top);
        BehaviorTestCheck.Equal(3_776, after.Left);
        BehaviorTestCheck.Equal(888, after.Top);
    }

    private static void CrossingHorizontalMonitorBoundaryRemainsTwoPhysicalPixels()
    {
        PhysicalWindowOrigin before = PhysicalDragPlacement.Calculate(
            cursorX: 3_839,
            cursorY: 700,
            anchorDipX: 0,
            anchorDipY: 0,
            dpiX: 144,
            dpiY: 144);
        PhysicalWindowOrigin after = PhysicalDragPlacement.Calculate(
            cursorX: 3_841,
            cursorY: 700,
            anchorDipX: 0,
            anchorDipY: 0,
            dpiX: 96,
            dpiY: 96);

        BehaviorTestCheck.Equal(2, after.Left - before.Left);
        BehaviorTestCheck.Equal(0, after.Top - before.Top);
    }

    private static void CrossingHorizontalMonitorBoundaryInReverseRemainsTwoPhysicalPixels()
    {
        PhysicalWindowOrigin before = PhysicalDragPlacement.Calculate(
            cursorX: 3_841,
            cursorY: 700,
            anchorDipX: 0,
            anchorDipY: 0,
            dpiX: 96,
            dpiY: 96);
        PhysicalWindowOrigin after = PhysicalDragPlacement.Calculate(
            cursorX: 3_839,
            cursorY: 700,
            anchorDipX: 0,
            anchorDipY: 0,
            dpiX: 144,
            dpiY: 144);

        BehaviorTestCheck.Equal(-2, after.Left - before.Left);
        BehaviorTestCheck.Equal(0, after.Top - before.Top);
    }

    private static void SupportsNegativeDesktopCoordinates()
    {
        PhysicalWindowOrigin origin = PhysicalDragPlacement.Calculate(
            cursorX: -1_500,
            cursorY: 500,
            anchorDipX: 16,
            anchorDipY: 8,
            dpiX: 120,
            dpiY: 120);

        BehaviorTestCheck.Equal(-1_520, origin.Left);
        BehaviorTestCheck.Equal(490, origin.Top);
    }

    private static void SupportsVerticalAndTwoDimensionalMonitorLayouts()
    {
        PhysicalWindowOrigin before = PhysicalDragPlacement.Calculate(
            cursorX: 301,
            cursorY: 2_159,
            anchorDipX: 0,
            anchorDipY: 0,
            dpiX: 144,
            dpiY: 144);
        PhysicalWindowOrigin after = PhysicalDragPlacement.Calculate(
            cursorX: 303,
            cursorY: 2_161,
            anchorDipX: 0,
            anchorDipY: 0,
            dpiX: 120,
            dpiY: 120);

        BehaviorTestCheck.Equal(2, after.Left - before.Left);
        BehaviorTestCheck.Equal(2, after.Top - before.Top);
    }

    private static void RejectsZeroDpi()
    {
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => PhysicalDragPlacement.Calculate(
                cursorX: 100,
                cursorY: 100,
                anchorDipX: 10,
                anchorDipY: 10,
                dpiX: 0,
                dpiY: 96));
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => PhysicalDragPlacement.Calculate(
                cursorX: 100,
                cursorY: 100,
                anchorDipX: 10,
                anchorDipY: 10,
                dpiX: 96,
                dpiY: 0));
    }

    private static void RejectsNonFiniteAnchor()
    {
        foreach ((double x, double y) in new (double X, double Y)[]
                 {
                     (double.NaN, 0),
                     (double.PositiveInfinity, 0),
                     (0, double.NegativeInfinity),
                 })
        {
            _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
                () => PhysicalDragPlacement.Calculate(
                    cursorX: 100,
                    cursorY: 100,
                    anchorDipX: x,
                    anchorDipY: y,
                    dpiX: 96,
                    dpiY: 96));
        }
    }

    private static void MemoryWindowInitialPlacementChoosesLargerSide()
    {
        var petBounds = new WindowRectangle(300, 390, 280, 260);
        var workArea = new WindowRectangle(0, 0, 1_920, 1_040);

        MemoryWindowPlacementResult placement =
            MemoryWindowPlacement.Calculate(
                petBounds,
                workArea,
                desiredWindowWidth: 640,
                desiredWindowHeight: 360,
                gapDips: 16,
                dpiX: 96);

        BehaviorTestCheck.Equal(MemoryWindowSide.Right, placement.Side);
        BehaviorTestCheck.Equal(
            new WindowRectangle(596, 340, 640, 360),
            placement.Bounds);
    }

    private static void MemoryWindowKeepsPreferredSideWhenItFits()
    {
        var petBounds = new WindowRectangle(820, 390, 280, 260);
        var workArea = new WindowRectangle(0, 0, 1_920, 1_040);

        MemoryWindowPlacementResult placement =
            MemoryWindowPlacement.Calculate(
                petBounds,
                workArea,
                desiredWindowWidth: 640,
                desiredWindowHeight: 360,
                gapDips: 16,
                dpiX: 96,
                preferredSide: MemoryWindowSide.Left);

        BehaviorTestCheck.Equal(MemoryWindowSide.Left, placement.Side);
        BehaviorTestCheck.Equal(
            new WindowRectangle(164, 340, 640, 360),
            placement.Bounds);
    }

    private static void MemoryWindowFlipsWhenPreferredSideCannotFit()
    {
        var petBounds = new WindowRectangle(400, 390, 280, 260);
        var workArea = new WindowRectangle(0, 0, 1_920, 1_040);

        MemoryWindowPlacementResult placement =
            MemoryWindowPlacement.Calculate(
                petBounds,
                workArea,
                desiredWindowWidth: 640,
                desiredWindowHeight: 360,
                gapDips: 16,
                dpiX: 96,
                preferredSide: MemoryWindowSide.Left);

        BehaviorTestCheck.Equal(MemoryWindowSide.Right, placement.Side);
        BehaviorTestCheck.Equal(
            new WindowRectangle(696, 340, 640, 360),
            placement.Bounds);
    }

    private static void MemoryWindowScalesWithoutCoveringPetInNarrowWorkArea()
    {
        var petBounds = new WindowRectangle(360, 220, 180, 160);
        var workArea = new WindowRectangle(0, 0, 900, 600);

        MemoryWindowPlacementResult placement =
            MemoryWindowPlacement.Calculate(
                petBounds,
                workArea,
                desiredWindowWidth: 640,
                desiredWindowHeight: 360,
                gapDips: 20,
                dpiX: 96);

        BehaviorTestCheck.Equal(MemoryWindowSide.Right, placement.Side);
        BehaviorTestCheck.Equal(
            new WindowRectangle(560, 205, 340, 191),
            placement.Bounds);
        BehaviorTestCheck.True(
            placement.Bounds.Left >= petBounds.Left
                + petBounds.Width
                + 20);
        BehaviorTestCheck.Close(
            16d / 9d,
            placement.Bounds.Width / (double)placement.Bounds.Height,
            tolerance: 0.01);
    }

    private static void MemoryResponsiveWindowClampsHeightWithoutShrinkingWidth()
    {
        var petBounds = new WindowRectangle(300, 220, 180, 160);
        var workArea = new WindowRectangle(0, 0, 1_000, 500);

        MemoryWindowPlacementResult placement =
            MemoryWindowPlacement.CalculateResponsive(
                petBounds,
                workArea,
                desiredWindowWidth: 360,
                desiredWindowHeight: 674,
                gapDips: 20,
                dpiX: 96);

        BehaviorTestCheck.Equal(MemoryWindowSide.Right, placement.Side);
        BehaviorTestCheck.Equal(
            new WindowRectangle(500, 0, 360, 500),
            placement.Bounds);
        BehaviorTestCheck.Equal(360, placement.Bounds.Width);
        BehaviorTestCheck.Equal(workArea.Height, placement.Bounds.Height);
        BehaviorTestCheck.True(
            placement.Bounds.Left
                >= petBounds.Left + petBounds.Width + 20);
        BehaviorTestCheck.True(
            placement.Bounds.Left >= workArea.Left
                && placement.Bounds.Top >= workArea.Top
                && placement.Bounds.Left + placement.Bounds.Width
                    <= workArea.Left + workArea.Width
                && placement.Bounds.Top + placement.Bounds.Height
                    <= workArea.Top + workArea.Height);
    }

    private static void MemoryResponsiveWindowClampsSideWidthWithoutShrinkingHeight()
    {
        var petBounds = new WindowRectangle(320, 310, 220, 180);
        var workArea = new WindowRectangle(0, 0, 900, 800);

        MemoryWindowPlacementResult placement =
            MemoryWindowPlacement.CalculateResponsive(
                petBounds,
                workArea,
                desiredWindowWidth: 360,
                desiredWindowHeight: 674,
                gapDips: 16,
                dpiX: 96);

        BehaviorTestCheck.Equal(MemoryWindowSide.Right, placement.Side);
        BehaviorTestCheck.Equal(
            new WindowRectangle(556, 63, 344, 674),
            placement.Bounds);
        BehaviorTestCheck.Equal(344, placement.Bounds.Width);
        BehaviorTestCheck.Equal(674, placement.Bounds.Height);
        BehaviorTestCheck.True(
            placement.Bounds.Left
                >= petBounds.Left + petBounds.Width + 16);
        BehaviorTestCheck.True(
            placement.Bounds.Left >= workArea.Left
                && placement.Bounds.Top >= workArea.Top
                && placement.Bounds.Left + placement.Bounds.Width
                    <= workArea.Left + workArea.Width
                && placement.Bounds.Top + placement.Bounds.Height
                    <= workArea.Top + workArea.Height);
    }

    private static void MemoryWindowCentersVerticallyAndClampsToWorkArea()
    {
        var workArea = new WindowRectangle(100, 50, 1_400, 700);

        MemoryWindowPlacementResult centered =
            MemoryWindowPlacement.Calculate(
                new WindowRectangle(900, 300, 280, 260),
                workArea,
                desiredWindowWidth: 640,
                desiredWindowHeight: 360,
                gapDips: 16,
                dpiX: 96);
        MemoryWindowPlacementResult clamped =
            MemoryWindowPlacement.Calculate(
                new WindowRectangle(900, -30, 280, 260),
                workArea,
                desiredWindowWidth: 640,
                desiredWindowHeight: 360,
                gapDips: 16,
                dpiX: 96);

        BehaviorTestCheck.Equal(250, centered.Bounds.Top);
        BehaviorTestCheck.Equal(workArea.Top, clamped.Bounds.Top);
    }

    private static void MemoryWindowUsesDpiScaledGapWithNegativeCoordinates()
    {
        var petBounds = new WindowRectangle(-700, 300, 420, 390);
        var workArea = new WindowRectangle(-2_560, -80, 2_560, 1_400);

        MemoryWindowPlacementResult placement =
            MemoryWindowPlacement.Calculate(
                petBounds,
                workArea,
                desiredWindowWidth: 640,
                desiredWindowHeight: 360,
                gapDips: 16,
                dpiX: 144);

        BehaviorTestCheck.Equal(MemoryWindowSide.Left, placement.Side);
        BehaviorTestCheck.Equal(
            new WindowRectangle(-1_364, 315, 640, 360),
            placement.Bounds);
        BehaviorTestCheck.Equal(
            24,
            petBounds.Left
                - (placement.Bounds.Left + placement.Bounds.Width));
    }

    private static void MemoryWindowStaysInsideWorkAreaWhenClearanceIsImpossible()
    {
        var petBounds = new WindowRectangle(0, 0, 600, 400);
        var workArea = new WindowRectangle(0, 0, 600, 400);

        MemoryWindowPlacementResult placement =
            MemoryWindowPlacement.Calculate(
                petBounds,
                workArea,
                desiredWindowWidth: 640,
                desiredWindowHeight: 360,
                gapDips: 16,
                dpiX: 96,
                preferredSide: MemoryWindowSide.Right);

        BehaviorTestCheck.Equal(MemoryWindowSide.Right, placement.Side);
        BehaviorTestCheck.Equal(
            new WindowRectangle(0, 31, 600, 337),
            placement.Bounds);
        BehaviorTestCheck.True(
            placement.Bounds.Left >= workArea.Left
                && placement.Bounds.Top >= workArea.Top
                && placement.Bounds.Left + placement.Bounds.Width
                    <= workArea.Left + workArea.Width
                && placement.Bounds.Top + placement.Bounds.Height
                    <= workArea.Top + workArea.Height);
    }

    private static void MemoryPlaybackWindowSizingAdaptsCommonOrientations()
    {
        var portrait = MemoryPlaybackWindowSizing.Calculate(
            naturalVideoWidth: 720,
            naturalVideoHeight: 1_280);
        var threeByFour = MemoryPlaybackWindowSizing.Calculate(
            naturalVideoWidth: 1_080,
            naturalVideoHeight: 1_440);
        var square = MemoryPlaybackWindowSizing.Calculate(
            naturalVideoWidth: 1_080,
            naturalVideoHeight: 1_080);
        var fourByThree = MemoryPlaybackWindowSizing.Calculate(
            naturalVideoWidth: 1_440,
            naturalVideoHeight: 1_080);
        var landscape = MemoryPlaybackWindowSizing.Calculate(
            naturalVideoWidth: 1_280,
            naturalVideoHeight: 720);

        BehaviorTestCheck.Close(328, portrait.WidthDips);
        BehaviorTestCheck.Close(540, portrait.HeightDips);
        BehaviorTestCheck.Close(402, threeByFour.WidthDips);
        BehaviorTestCheck.Close(540, threeByFour.HeightDips);
        BehaviorTestCheck.Close(528, square.WidthDips);
        BehaviorTestCheck.Close(540, square.HeightDips);
        BehaviorTestCheck.Close(528, fourByThree.WidthDips);
        BehaviorTestCheck.Close(414, fourByThree.HeightDips);
        BehaviorTestCheck.Close(528, landscape.WidthDips);
        BehaviorTestCheck.Close(319.5, landscape.HeightDips);
        BehaviorTestCheck.True(portrait.WidthDips < landscape.WidthDips);
        BehaviorTestCheck.True(portrait.HeightDips > landscape.HeightDips);
    }

    private static void MemoryPlaybackWindowSizingKeepsPortraitVideoReadable()
    {
        var portrait = MemoryPlaybackWindowSizing.Calculate(
            naturalVideoWidth: 720,
            naturalVideoHeight: 1_280);

        double mediaWidth = portrait.WidthDips
            - MemoryPlaybackWindowSizing.HorizontalChromeDips;
        double mediaHeight = portrait.HeightDips
            - MemoryPlaybackWindowSizing.VerticalChromeDips;
        double visibleVideoWidth = mediaHeight * 9d / 16d;
        double totalLetterboxWidth = mediaWidth - visibleVideoWidth;

        BehaviorTestCheck.Close(304, mediaWidth);
        BehaviorTestCheck.Close(504, mediaHeight);
        BehaviorTestCheck.Close(283.5, visibleVideoWidth);
        BehaviorTestCheck.True(
            totalLetterboxWidth <= 21,
            "A 9:16 memory should use almost all of the portrait frame width.");
    }

    private static void MemoryPlaybackWindowSizingRejectsInvalidDimensions()
    {
        foreach ((int width, int height) in new[]
                 {
                     (0, 1_080),
                     (1_920, 0),
                     (-1, 1_080),
                     (1_920, -1),
                 })
        {
            _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
                () => MemoryPlaybackWindowSizing.Calculate(width, height));
        }
    }

    private static void MemoryLandscapeUsesNativeResolution()
    {
        var petBounds = new WindowRectangle(820, 390, 280, 260);
        var workArea = new WindowRectangle(0, 0, 1_920, 1_040);

        WindowRectangle memoryBounds =
            MemoryVideoPlacement.FitVideoAroundAnchor(
                petBounds,
                workArea,
                naturalWidth: 1_280,
                naturalHeight: 720);

        BehaviorTestCheck.Equal(
            new WindowRectangle(320, 160, 1_280, 720),
            memoryBounds);
    }

    private static void MemoryPortraitFitsWorkAreaWithoutChangingAspect()
    {
        var petBounds = new WindowRectangle(820, 390, 280, 260);
        var workArea = new WindowRectangle(0, 0, 1_920, 1_040);

        WindowRectangle memoryBounds =
            MemoryVideoPlacement.FitVideoAroundAnchor(
                petBounds,
                workArea,
                naturalWidth: 720,
                naturalHeight: 1_280);

        BehaviorTestCheck.Equal(
            new WindowRectangle(668, 0, 585, 1_040),
            memoryBounds);
        BehaviorTestCheck.Close(
            9d / 16d,
            memoryBounds.Width / (double)memoryBounds.Height,
            tolerance: 0.001);
    }

    private static void MemoryMoveCentersTheRestoredPet()
    {
        var videoBounds = new WindowRectangle(500, 100, 1_280, 720);
        var workArea = new WindowRectangle(0, 0, 1_920, 1_040);

        WindowRectangle petBounds =
            MemoryVideoPlacement.CenterPetOnVideo(
                videoBounds,
                workArea,
                petWidth: 280,
                petHeight: 260);

        BehaviorTestCheck.Equal(
            new WindowRectangle(1_000, 330, 280, 260),
            petBounds);
    }

    private static void MemoryThreeByFourAndFourByThreePreserveAspect()
    {
        var petBounds = new WindowRectangle(820, 390, 280, 260);
        var workArea = new WindowRectangle(0, 0, 1_920, 1_040);

        WindowRectangle portrait =
            MemoryVideoPlacement.FitVideoAroundAnchor(
                petBounds,
                workArea,
                naturalWidth: 1_080,
                naturalHeight: 1_440);
        WindowRectangle landscape =
            MemoryVideoPlacement.FitVideoAroundAnchor(
                petBounds,
                workArea,
                naturalWidth: 1_440,
                naturalHeight: 1_080);

        BehaviorTestCheck.Close(
            3d / 4d,
            portrait.Width / (double)portrait.Height,
            tolerance: 0.001);
        BehaviorTestCheck.Close(
            4d / 3d,
            landscape.Width / (double)landscape.Height,
            tolerance: 0.001);
        BehaviorTestCheck.True(
            portrait.Width <= workArea.Width
            && portrait.Height <= workArea.Height
            && landscape.Width <= workArea.Width
            && landscape.Height <= workArea.Height);
    }

    private static void MemoryMoveUsesTheTargetMonitorDpiForTheRestoredPet()
    {
        var videoBounds = new WindowRectangle(500, 100, 1_280, 720);
        var workArea = new WindowRectangle(0, 0, 2_880, 1_560);

        WindowRectangle atOneHundredFiftyPercent =
            MemoryVideoPlacement.CenterPetOnVideoAtDpi(
                videoBounds,
                workArea,
                PetWindow.NormalPetWidth,
                PetWindow.NormalPetHeight,
                dpiX: 144,
                dpiY: 144);
        WindowRectangle atOneHundredPercent =
            MemoryVideoPlacement.CenterPetOnVideoAtDpi(
                videoBounds,
                workArea,
                PetWindow.NormalPetWidth,
                PetWindow.NormalPetHeight,
                dpiX: 96,
                dpiY: 96);

        BehaviorTestCheck.Equal(420, atOneHundredFiftyPercent.Width);
        BehaviorTestCheck.Equal(390, atOneHundredFiftyPercent.Height);
        BehaviorTestCheck.Equal(280, atOneHundredPercent.Width);
        BehaviorTestCheck.Equal(260, atOneHundredPercent.Height);
    }

    private static void MemoryRestoreLatchWaitsOutDelayedDpiCallbacks()
    {
        var originalBounds = new WindowRectangle(
            Left: 3_396,
            Top: 1_674,
            Width: 420,
            Height: 390);
        var displacedBounds = new WindowRectangle(
            Left: 5_396,
            Top: 244,
            Width: 280,
            Height: 260);
        var latch = new MemoryWindowRestoreLatch();

        latch.Begin(originalBounds);

        BehaviorTestCheck.True(latch.IsActive);
        BehaviorTestCheck.Equal(
            originalBounds,
            latch.TargetBounds!.Value);
        BehaviorTestCheck.False(
            latch.ObserveSettledBounds(
                displacedBounds,
                dpiCallbackPending: false));
        BehaviorTestCheck.False(
            latch.ObserveSettledBounds(
                originalBounds,
                dpiCallbackPending: true));
        BehaviorTestCheck.False(
            latch.ObserveSettledBounds(
                originalBounds,
                dpiCallbackPending: false));

        latch.NoteDpiChange();

        BehaviorTestCheck.False(
            latch.ObserveSettledBounds(
                originalBounds,
                dpiCallbackPending: false));
        BehaviorTestCheck.True(
            latch.ObserveSettledBounds(
                originalBounds,
                dpiCallbackPending: false));

        latch.Complete();

        BehaviorTestCheck.False(latch.IsActive);
        BehaviorTestCheck.False(
            latch.ObserveSettledBounds(
                originalBounds,
                dpiCallbackPending: false));
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(WindowPlacementTests)}.{name}");
    }
}
