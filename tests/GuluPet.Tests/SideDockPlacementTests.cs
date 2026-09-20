using GuluPet.Platform;

namespace GuluPet.Tests;

internal static class SideDockPlacementTests
{
    public static void RunAll()
    {
        Run(
            nameof(DocksWithinThresholdAndRejectsJustOutside),
            DocksWithinThresholdAndRejectsJustOutside);
        Run(
            nameof(LeftAndRightDockBoundsAreMirrored),
            LeftAndRightDockBoundsAreMirrored);
        Run(
            nameof(ThresholdAndVisibleStripScaleWithDpi),
            ThresholdAndVisibleStripScaleWithDpi);
        Run(
            nameof(SharedMonitorSeamIsRejectedWhileOuterEdgesDock),
            SharedMonitorSeamIsRejectedWhileOuterEdgesDock);
        Run(
            nameof(VerticalTaskbarEdgesAreRejected),
            VerticalTaskbarEdgesAreRejected);
        Run(
            nameof(BottomTaskbarDoesNotBlockSideDocking),
            BottomTaskbarDoesNotBlockSideDocking);
        Run(
            nameof(ClampsTopInsideAnOffsetWorkArea),
            ClampsTopInsideAnOffsetWorkArea);
        Run(
            nameof(ClampsBottomInsideTheWorkArea),
            ClampsBottomInsideTheWorkArea);
        Run(
            nameof(SupportsNegativeDesktopCoordinates),
            SupportsNegativeDesktopCoordinates);
        Run(
            nameof(IgnoresTopAndBottomEdges),
            IgnoresTopAndBottomEdges);
    }

    private static void DocksWithinThresholdAndRejectsJustOutside()
    {
        var monitor = new WindowRectangle(0, 0, 1_920, 1_080);
        var workArea = new WindowRectangle(0, 0, 1_920, 1_040);
        WindowRectangle[] monitors = [monitor];

        BehaviorTestCheck.True(
            SideDockPlacement.TryCalculate(
                new WindowRectangle(36, 300, 280, 260),
                monitor,
                workArea,
                dpiX: 96,
                allMonitorBounds: monitors,
                out SideDockCandidate? atThreshold));
        BehaviorTestCheck.Equal(
            SideDockEdge.Left,
            RequireCandidate(atThreshold).Edge);

        BehaviorTestCheck.False(
            SideDockPlacement.TryCalculate(
                new WindowRectangle(37, 300, 280, 260),
                monitor,
                workArea,
                dpiX: 96,
                allMonitorBounds: monitors,
                out SideDockCandidate? outsideThreshold));
        BehaviorTestCheck.Null(outsideThreshold);

        BehaviorTestCheck.True(
            SideDockPlacement.TryCalculate(
                new WindowRectangle(1_920 - 280 - 36, 300, 280, 260),
                monitor,
                workArea,
                dpiX: 96,
                allMonitorBounds: monitors,
                out SideDockCandidate? rightAtThreshold));
        BehaviorTestCheck.Equal(
            SideDockEdge.Right,
            RequireCandidate(rightAtThreshold).Edge);
    }

    private static void LeftAndRightDockBoundsAreMirrored()
    {
        var monitor = new WindowRectangle(100, 40, 1_920, 1_080);
        var workArea = new WindowRectangle(100, 40, 1_920, 1_040);
        WindowRectangle[] monitors = [monitor];
        var leftRelease = new WindowRectangle(124, 310, 280, 260);
        var rightRelease = new WindowRectangle(1_716, 310, 280, 260);

        SideDockCandidate left = Calculate(
            leftRelease,
            monitor,
            workArea,
            dpiX: 96,
            allMonitorBounds: monitors);
        SideDockCandidate right = Calculate(
            rightRelease,
            monitor,
            workArea,
            dpiX: 96,
            allMonitorBounds: monitors);

        BehaviorTestCheck.Equal(SideDockEdge.Left, left.Edge);
        BehaviorTestCheck.Equal(SideDockEdge.Right, right.Edge);
        BehaviorTestCheck.Equal(leftRelease, left.RestoreBounds);
        BehaviorTestCheck.Equal(rightRelease, right.RestoreBounds);
        BehaviorTestCheck.Equal(monitor, left.MonitorBounds);
        BehaviorTestCheck.Equal(monitor, right.MonitorBounds);
        BehaviorTestCheck.Equal(workArea, left.WorkArea);
        BehaviorTestCheck.Equal(workArea, right.WorkArea);
        BehaviorTestCheck.Equal(leftRelease.Width, left.DockBounds.Width);
        BehaviorTestCheck.Equal(leftRelease.Height, left.DockBounds.Height);
        BehaviorTestCheck.Equal(rightRelease.Width, right.DockBounds.Width);
        BehaviorTestCheck.Equal(rightRelease.Height, right.DockBounds.Height);

        long leftVisible = Right(left.DockBounds) - monitor.Left;
        long rightVisible = Right(monitor) - right.DockBounds.Left;
        BehaviorTestCheck.Equal(leftVisible, rightVisible);
        BehaviorTestCheck.True(
            leftVisible is >= 140 and <= 160,
            $"Expected about 150 visible pixels, got {leftVisible}.");
        BehaviorTestCheck.Equal(
            (long)monitor.Left - (left.DockBounds.Width - leftVisible),
            (long)left.DockBounds.Left);
        BehaviorTestCheck.Equal(
            Right(monitor) - rightVisible,
            (long)right.DockBounds.Left);
        BehaviorTestCheck.Equal(left.DockBounds.Top, right.DockBounds.Top);
    }

    private static void ThresholdAndVisibleStripScaleWithDpi()
    {
        var monitor = new WindowRectangle(0, 0, 2_880, 1_620);
        var workArea = new WindowRectangle(0, 0, 2_880, 1_560);
        WindowRectangle[] monitors = [monitor];

        SideDockCandidate inside = Calculate(
            new WindowRectangle(54, 480, 420, 390),
            monitor,
            workArea,
            dpiX: 144,
            allMonitorBounds: monitors);
        long visible = Right(inside.DockBounds) - monitor.Left;
        BehaviorTestCheck.Equal(SideDockEdge.Left, inside.Edge);
        BehaviorTestCheck.True(
            visible is >= 215 and <= 235,
            $"Expected about 225 visible pixels at 150% DPI, got {visible}.");

        BehaviorTestCheck.False(
            SideDockPlacement.TryCalculate(
                new WindowRectangle(55, 480, 420, 390),
                monitor,
                workArea,
                dpiX: 144,
                allMonitorBounds: monitors,
                out SideDockCandidate? outside));
        BehaviorTestCheck.Null(outside);
    }

    private static void SharedMonitorSeamIsRejectedWhileOuterEdgesDock()
    {
        var leftMonitor = new WindowRectangle(-1_920, 0, 1_920, 1_080);
        var rightMonitor = new WindowRectangle(0, 0, 1_920, 1_080);
        var leftWorkArea = new WindowRectangle(-1_920, 0, 1_920, 1_040);
        var rightWorkArea = new WindowRectangle(0, 0, 1_920, 1_040);
        WindowRectangle[] monitors = [leftMonitor, rightMonitor];

        BehaviorTestCheck.False(
            SideDockPlacement.TryCalculate(
                new WindowRectangle(-280 - 20, 300, 280, 260),
                leftMonitor,
                leftWorkArea,
                dpiX: 96,
                allMonitorBounds: monitors,
                out SideDockCandidate? leftSeam));
        BehaviorTestCheck.Null(leftSeam);

        BehaviorTestCheck.False(
            SideDockPlacement.TryCalculate(
                new WindowRectangle(20, 300, 280, 260),
                rightMonitor,
                rightWorkArea,
                dpiX: 96,
                allMonitorBounds: monitors,
                out SideDockCandidate? rightSeam));
        BehaviorTestCheck.Null(rightSeam);

        SideDockCandidate outerLeft = Calculate(
            new WindowRectangle(-1_920 + 20, 300, 280, 260),
            leftMonitor,
            leftWorkArea,
            dpiX: 96,
            allMonitorBounds: monitors);
        SideDockCandidate outerRight = Calculate(
            new WindowRectangle(1_920 - 280 - 20, 300, 280, 260),
            rightMonitor,
            rightWorkArea,
            dpiX: 96,
            allMonitorBounds: monitors);
        BehaviorTestCheck.Equal(SideDockEdge.Left, outerLeft.Edge);
        BehaviorTestCheck.Equal(SideDockEdge.Right, outerRight.Edge);
    }

    private static void VerticalTaskbarEdgesAreRejected()
    {
        var monitor = new WindowRectangle(0, 0, 1_920, 1_080);
        WindowRectangle[] monitors = [monitor];

        BehaviorTestCheck.False(
            SideDockPlacement.TryCalculate(
                new WindowRectangle(20, 300, 280, 260),
                monitor,
                new WindowRectangle(48, 0, 1_872, 1_040),
                dpiX: 96,
                allMonitorBounds: monitors,
                out SideDockCandidate? leftTaskbar));
        BehaviorTestCheck.Null(leftTaskbar);

        BehaviorTestCheck.False(
            SideDockPlacement.TryCalculate(
                new WindowRectangle(1_920 - 280 - 20, 300, 280, 260),
                monitor,
                new WindowRectangle(0, 0, 1_872, 1_040),
                dpiX: 96,
                allMonitorBounds: monitors,
                out SideDockCandidate? rightTaskbar));
        BehaviorTestCheck.Null(rightTaskbar);
    }

    private static void BottomTaskbarDoesNotBlockSideDocking()
    {
        var monitor = new WindowRectangle(0, 0, 1_920, 1_080);
        var workArea = new WindowRectangle(0, 0, 1_920, 1_040);

        SideDockCandidate candidate = Calculate(
            new WindowRectangle(20, 300, 280, 260),
            monitor,
            workArea,
            dpiX: 96,
            allMonitorBounds: [monitor]);

        BehaviorTestCheck.Equal(SideDockEdge.Left, candidate.Edge);
        BehaviorTestCheck.Equal(300, candidate.DockBounds.Top);
    }

    private static void ClampsTopInsideAnOffsetWorkArea()
    {
        var monitor = new WindowRectangle(0, 0, 1_920, 1_080);
        var workArea = new WindowRectangle(0, 40, 1_920, 1_000);

        SideDockCandidate candidate = Calculate(
            new WindowRectangle(20, -80, 280, 260),
            monitor,
            workArea,
            dpiX: 96,
            allMonitorBounds: [monitor]);

        BehaviorTestCheck.Equal(SideDockEdge.Left, candidate.Edge);
        BehaviorTestCheck.Equal(workArea.Top, candidate.DockBounds.Top);
        BehaviorTestCheck.Equal(workArea.Top, candidate.RestoreBounds.Top);
    }

    private static void ClampsBottomInsideTheWorkArea()
    {
        var monitor = new WindowRectangle(0, 0, 1_920, 1_080);
        var workArea = new WindowRectangle(0, 20, 1_920, 1_020);

        SideDockCandidate candidate = Calculate(
            new WindowRectangle(1_920 - 280 - 20, 980, 280, 260),
            monitor,
            workArea,
            dpiX: 96,
            allMonitorBounds: [monitor]);
        int expectedTop = workArea.Top + workArea.Height - 260;

        BehaviorTestCheck.Equal(SideDockEdge.Right, candidate.Edge);
        BehaviorTestCheck.Equal(expectedTop, candidate.DockBounds.Top);
        BehaviorTestCheck.Equal(expectedTop, candidate.RestoreBounds.Top);
    }

    private static void SupportsNegativeDesktopCoordinates()
    {
        var monitor = new WindowRectangle(-2_560, -120, 2_560, 1_440);
        var workArea = new WindowRectangle(-2_560, -80, 2_560, 1_400);

        SideDockCandidate candidate = Calculate(
            new WindowRectangle(-2_560 + 18, -200, 280, 260),
            monitor,
            workArea,
            dpiX: 96,
            allMonitorBounds: [monitor]);

        BehaviorTestCheck.Equal(SideDockEdge.Left, candidate.Edge);
        BehaviorTestCheck.True(candidate.DockBounds.Left < monitor.Left);
        BehaviorTestCheck.Equal(workArea.Top, candidate.DockBounds.Top);
        BehaviorTestCheck.Equal(monitor, candidate.MonitorBounds);
        BehaviorTestCheck.Equal(workArea, candidate.WorkArea);
    }

    private static void IgnoresTopAndBottomEdges()
    {
        var monitor = new WindowRectangle(0, 0, 1_920, 1_080);
        var workArea = new WindowRectangle(0, 0, 1_920, 1_040);
        WindowRectangle[] monitors = [monitor];

        BehaviorTestCheck.False(
            SideDockPlacement.TryCalculate(
                new WindowRectangle(800, 20, 280, 260),
                monitor,
                workArea,
                dpiX: 96,
                allMonitorBounds: monitors,
                out SideDockCandidate? top));
        BehaviorTestCheck.Null(top);
        BehaviorTestCheck.False(
            SideDockPlacement.TryCalculate(
                new WindowRectangle(800, 1_040 - 260 - 20, 280, 260),
                monitor,
                workArea,
                dpiX: 96,
                allMonitorBounds: monitors,
                out SideDockCandidate? bottom));
        BehaviorTestCheck.Null(bottom);
    }

    private static SideDockCandidate Calculate(
        WindowRectangle releasedWindow,
        WindowRectangle monitorBounds,
        WindowRectangle workArea,
        uint dpiX,
        IReadOnlyList<WindowRectangle> allMonitorBounds)
    {
        BehaviorTestCheck.True(
            SideDockPlacement.TryCalculate(
                releasedWindow,
                monitorBounds,
                workArea,
                dpiX,
                allMonitorBounds,
                out SideDockCandidate? candidate));
        return RequireCandidate(candidate);
    }

    private static SideDockCandidate RequireCandidate(
        SideDockCandidate? candidate) =>
        candidate ?? throw new InvalidOperationException(
            "Expected a side-dock candidate.");

    private static long Right(WindowRectangle rectangle) =>
        (long)rectangle.Left + rectangle.Width;

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(SideDockPlacementTests)}.{name}");
    }
}
