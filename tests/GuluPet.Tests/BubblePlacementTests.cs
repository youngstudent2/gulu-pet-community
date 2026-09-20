using GuluPet.Platform;

namespace GuluPet.Tests;

internal static class BubblePlacementTests
{
    public static void RunAll()
    {
        Run(
            nameof(ClampsFourKBubbleInsideTheAnchorMonitor),
            ClampsFourKBubbleInsideTheAnchorMonitor);
        Run(
            nameof(UsesOneHundredPercentTargetDpiOnTheSecondMonitor),
            UsesOneHundredPercentTargetDpiOnTheSecondMonitor);
        Run(
            nameof(CentersShortMediumAndLongBubblesOnThePet),
            CentersShortMediumAndLongBubblesOnThePet);
        Run(
            nameof(ClampsCenteredBubbleAtBothWorkAreaEdges),
            ClampsCenteredBubbleAtBothWorkAreaEdges);
        Run(
            nameof(SupportsNegativeMonitorCoordinates),
            SupportsNegativeMonitorCoordinates);
        Run(
            nameof(ClampsAgainstOffsetWorkArea),
            ClampsAgainstOffsetWorkArea);
        Run(
            nameof(UsesActualNativeSizeForTheCorrectionPass),
            UsesActualNativeSizeForTheCorrectionPass);
        Run(
            nameof(NativeBubbleMoveNeverResizesTheWpfWindow),
            NativeBubbleMoveNeverResizesTheWpfWindow);
        Run(
            nameof(RejectsInvalidDimensions),
            RejectsInvalidDimensions);
    }

    private static void ClampsFourKBubbleInsideTheAnchorMonitor()
    {
        BubblePlacementResult placement =
            BubblePlacement.CalculateFromDips(
                new WindowRectangle(3_396, 1_674, 420, 390),
                new WindowRectangle(0, 0, 3_840, 2_088),
                bubbleWidthDip: 220,
                bubbleHeightDip: 64,
                dpiX: 144,
                dpiY: 144);

        BehaviorTestCheck.Equal(
            new WindowRectangle(3_441, 1_614, 330, 96),
            placement.Bounds);
        AssertHorizontallyCentered(
            placement.Bounds,
            new WindowRectangle(3_396, 1_674, 420, 390));
        AssertContained(
            placement.Bounds,
            new WindowRectangle(0, 0, 3_840, 2_088));
    }

    private static void UsesOneHundredPercentTargetDpiOnTheSecondMonitor()
    {
        WindowRectangle workArea =
            new(3_840, 0, 2_560, 1_392);
        BubblePlacementResult placement =
            BubblePlacement.CalculateFromDips(
                new WindowRectangle(3_900, 900, 280, 260),
                workArea,
                bubbleWidthDip: 220,
                bubbleHeightDip: 64,
                dpiX: 96,
                dpiY: 96);

        BehaviorTestCheck.Equal(220, placement.Bounds.Width);
        BehaviorTestCheck.Equal(64, placement.Bounds.Height);
        AssertHorizontallyCentered(
            placement.Bounds,
            new WindowRectangle(3_900, 900, 280, 260));
        AssertContained(placement.Bounds, workArea);
    }

    private static void CentersShortMediumAndLongBubblesOnThePet()
    {
        WindowRectangle anchor = new(500, 400, 300, 260);
        WindowRectangle workArea = new(0, 0, 1_920, 1_040);

        BubblePlacementResult shortBubble =
            BubblePlacement.CalculatePhysical(
                anchor,
                workArea,
                bubbleWidth: 140,
                bubbleHeight: 64,
                dpiX: 96,
                dpiY: 96);
        BubblePlacementResult mediumBubble =
            BubblePlacement.CalculatePhysical(
                anchor,
                workArea,
                bubbleWidth: 230,
                bubbleHeight: 72,
                dpiX: 96,
                dpiY: 96);
        BubblePlacementResult longBubble =
            BubblePlacement.CalculatePhysical(
                anchor,
                workArea,
                bubbleWidth: 320,
                bubbleHeight: 80,
                dpiX: 96,
                dpiY: 96);

        BehaviorTestCheck.Equal(580, shortBubble.Bounds.Left);
        BehaviorTestCheck.Equal(535, mediumBubble.Bounds.Left);
        BehaviorTestCheck.Equal(490, longBubble.Bounds.Left);
        AssertHorizontallyCentered(shortBubble.Bounds, anchor);
        AssertHorizontallyCentered(mediumBubble.Bounds, anchor);
        AssertHorizontallyCentered(longBubble.Bounds, anchor);
    }

    private static void ClampsCenteredBubbleAtBothWorkAreaEdges()
    {
        WindowRectangle workArea = new(100, 50, 1_000, 800);

        BubblePlacementResult leftEdge =
            BubblePlacement.CalculatePhysical(
                new WindowRectangle(100, 300, 200, 220),
                workArea,
                bubbleWidth: 300,
                bubbleHeight: 72,
                dpiX: 96,
                dpiY: 96);
        BubblePlacementResult rightEdge =
            BubblePlacement.CalculatePhysical(
                new WindowRectangle(950, 300, 200, 220),
                workArea,
                bubbleWidth: 300,
                bubbleHeight: 72,
                dpiX: 96,
                dpiY: 96);

        BehaviorTestCheck.Equal(108, leftEdge.Bounds.Left);
        BehaviorTestCheck.Equal(792, rightEdge.Bounds.Left);
        AssertContained(leftEdge.Bounds, workArea);
        AssertContained(rightEdge.Bounds, workArea);
    }

    private static void SupportsNegativeMonitorCoordinates()
    {
        WindowRectangle workArea =
            new(-2_560, -120, 2_560, 1_392);
        BubblePlacementResult placement =
            BubblePlacement.CalculateFromDips(
                new WindowRectangle(-2_520, 40, 350, 325),
                workArea,
                bubbleWidthDip: 220,
                bubbleHeightDip: 72,
                dpiX: 120,
                dpiY: 120);

        BehaviorTestCheck.True(placement.Bounds.Left < 0);
        AssertContained(placement.Bounds, workArea);
    }

    private static void ClampsAgainstOffsetWorkArea()
    {
        WindowRectangle workArea =
            new(80, 60, 1_840, 980);
        BubblePlacementResult placement =
            BubblePlacement.CalculateFromDips(
                new WindowRectangle(1_760, 70, 280, 260),
                workArea,
                bubbleWidthDip: 220,
                bubbleHeightDip: 80,
                dpiX: 96,
                dpiY: 96);

        BehaviorTestCheck.Equal(1_692, placement.Bounds.Left);
        BehaviorTestCheck.Equal(68, placement.Bounds.Top);
        AssertContained(placement.Bounds, workArea);
    }

    private static void UsesActualNativeSizeForTheCorrectionPass()
    {
        WindowRectangle workArea =
            new(0, 0, 3_840, 2_088);
        BubblePlacementResult placement =
            BubblePlacement.CalculatePhysical(
                new WindowRectangle(3_396, 1_674, 420, 390),
                workArea,
                bubbleWidth: 495,
                bubbleHeight: 96,
                dpiX: 144,
                dpiY: 144);

        BehaviorTestCheck.Equal(3_333, placement.Bounds.Left);
        BehaviorTestCheck.Equal(495, placement.Bounds.Width);
        AssertContained(placement.Bounds, workArea);
    }

    private static void NativeBubbleMoveNeverResizesTheWpfWindow()
    {
        BehaviorTestCheck.True(
            (WindowPlacementService.BubbleMoveFlags
             & NativeMethods.SwpNoSize) != 0);
    }

    private static void RejectsInvalidDimensions()
    {
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => BubblePlacement.CalculateFromDips(
                new WindowRectangle(0, 0, 100, 100),
                new WindowRectangle(0, 0, 1_920, 1_080),
                bubbleWidthDip: double.NaN,
                bubbleHeightDip: 64,
                dpiX: 96,
                dpiY: 96));
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => BubblePlacement.CalculatePhysical(
                new WindowRectangle(0, 0, 100, 100),
                new WindowRectangle(0, 0, 1_920, 1_080),
                bubbleWidth: 0,
                bubbleHeight: 64,
                dpiX: 96,
                dpiY: 96));
    }

    private static void AssertContained(
        WindowRectangle candidate,
        WindowRectangle workArea)
    {
        BehaviorTestCheck.True(candidate.Left >= workArea.Left);
        BehaviorTestCheck.True(candidate.Top >= workArea.Top);
        BehaviorTestCheck.True(
            (long)candidate.Left + candidate.Width
            <= (long)workArea.Left + workArea.Width);
        BehaviorTestCheck.True(
            (long)candidate.Top + candidate.Height
            <= (long)workArea.Top + workArea.Height);
    }

    private static void AssertHorizontallyCentered(
        WindowRectangle candidate,
        WindowRectangle anchor)
    {
        long doubledCenterDelta = Math.Abs(
            ((long)candidate.Left * 2 + candidate.Width)
            - ((long)anchor.Left * 2 + anchor.Width));
        BehaviorTestCheck.True(doubledCenterDelta <= 1);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(BubblePlacementTests)}.{name}");
    }
}
