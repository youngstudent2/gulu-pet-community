namespace GuluPet.Platform;

public enum SideDockEdge
{
    Left,
    Right,
}

public sealed record SideDockCandidate
{
    public required SideDockEdge Edge { get; init; }

    public required WindowRectangle DockBounds { get; init; }

    public required WindowRectangle RestoreBounds { get; init; }

    public required WindowRectangle MonitorBounds { get; init; }

    public required WindowRectangle WorkArea { get; init; }
}

/// <summary>
/// Calculates physical-pixel placements for a pet that hides beyond a true
/// outer monitor edge. Shared monitor seams are rejected because the hidden
/// half would remain visible on the neighboring display.
/// </summary>
public static class SideDockPlacement
{
    internal const double SnapThresholdDips = 36;
    internal const double VisibleWidthDips = 150;

    public static bool TryCalculate(
        WindowRectangle releasedWindow,
        WindowRectangle monitorBounds,
        WindowRectangle workArea,
        uint dpiX,
        IReadOnlyList<WindowRectangle> allMonitorBounds,
        out SideDockCandidate? candidate)
    {
        Validate(
            releasedWindow,
            monitorBounds,
            workArea,
            dpiX,
            allMonitorBounds);

        int threshold = PhysicalDragPlacement.DipsToPhysicalPixels(
            SnapThresholdDips,
            dpiX);
        long leftDistance = Math.Max(
            0L,
            (long)releasedWindow.Left - monitorBounds.Left);
        long rightDistance = Math.Max(
            0L,
            Right(monitorBounds) - Right(releasedWindow));

        bool nearLeft = leftDistance <= threshold;
        bool nearRight = rightDistance <= threshold;
        if (!nearLeft && !nearRight)
        {
            candidate = null;
            return false;
        }

        SideDockEdge first = nearLeft &&
            (!nearRight || leftDistance <= rightDistance)
                ? SideDockEdge.Left
                : SideDockEdge.Right;
        if (TryCalculateForEdge(
                first,
                releasedWindow,
                monitorBounds,
                workArea,
                dpiX,
                allMonitorBounds,
                out candidate))
        {
            return true;
        }

        SideDockEdge second = first == SideDockEdge.Left
            ? SideDockEdge.Right
            : SideDockEdge.Left;
        return (second == SideDockEdge.Left ? nearLeft : nearRight) &&
            TryCalculateForEdge(
                second,
                releasedWindow,
                monitorBounds,
                workArea,
                dpiX,
                allMonitorBounds,
                out candidate);
    }

    internal static bool TryCalculateForEdge(
        SideDockEdge edge,
        WindowRectangle window,
        WindowRectangle monitorBounds,
        WindowRectangle workArea,
        uint dpiX,
        IReadOnlyList<WindowRectangle> allMonitorBounds,
        out SideDockCandidate? candidate)
    {
        Validate(
            window,
            monitorBounds,
            workArea,
            dpiX,
            allMonitorBounds);

        if (!HasUsablePhysicalEdge(edge, monitorBounds, workArea))
        {
            candidate = null;
            return false;
        }

        int requestedVisible = PhysicalDragPlacement.DipsToPhysicalPixels(
            VisibleWidthDips,
            dpiX);
        int visibleWidth = window.Width <= 1
            ? 1
            : Math.Clamp(requestedVisible, 1, window.Width - 1);
        int top = ClampOrigin(
            window.Top,
            workArea.Top,
            workArea.Height,
            window.Height);
        int dockLeft = edge switch
        {
            SideDockEdge.Left => checked(
                monitorBounds.Left - (window.Width - visibleWidth)),
            SideDockEdge.Right => checked(
                (int)Right(monitorBounds) - visibleWidth),
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };
        var dockBounds = new WindowRectangle(
            dockLeft,
            top,
            window.Width,
            window.Height);
        if (IntersectsAnotherMonitor(
                dockBounds,
                monitorBounds,
                allMonitorBounds))
        {
            candidate = null;
            return false;
        }

        int restoreLeft = ClampOrigin(
            window.Left,
            workArea.Left,
            workArea.Width,
            window.Width);
        candidate = new SideDockCandidate
        {
            Edge = edge,
            DockBounds = dockBounds,
            RestoreBounds = new WindowRectangle(
                restoreLeft,
                top,
                window.Width,
                window.Height),
            MonitorBounds = monitorBounds,
            WorkArea = workArea,
        };
        return true;
    }

    private static bool HasUsablePhysicalEdge(
        SideDockEdge edge,
        WindowRectangle monitorBounds,
        WindowRectangle workArea) =>
        edge switch
        {
            SideDockEdge.Left => workArea.Left == monitorBounds.Left,
            SideDockEdge.Right =>
                Right(workArea) == Right(monitorBounds),
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };

    private static bool IntersectsAnotherMonitor(
        WindowRectangle dockBounds,
        WindowRectangle currentMonitor,
        IReadOnlyList<WindowRectangle> allMonitorBounds)
    {
        foreach (WindowRectangle monitor in allMonitorBounds)
        {
            if (monitor == currentMonitor)
            {
                continue;
            }

            if (IntersectionWidth(dockBounds, monitor) > 0 &&
                IntersectionHeight(dockBounds, monitor) > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int ClampOrigin(
        int requested,
        int availableOrigin,
        int availableLength,
        int itemLength)
    {
        long maximum = Math.Max(
            (long)availableOrigin,
            (long)availableOrigin + availableLength - itemLength);
        return (int)Math.Clamp(
            (long)requested,
            availableOrigin,
            maximum);
    }

    private static long IntersectionWidth(
        WindowRectangle first,
        WindowRectangle second) =>
        Math.Max(
            0L,
            Math.Min(Right(first), Right(second)) -
            Math.Max((long)first.Left, second.Left));

    private static long IntersectionHeight(
        WindowRectangle first,
        WindowRectangle second) =>
        Math.Max(
            0L,
            Math.Min(Bottom(first), Bottom(second)) -
            Math.Max((long)first.Top, second.Top));

    private static long Right(WindowRectangle rectangle) =>
        (long)rectangle.Left + rectangle.Width;

    private static long Bottom(WindowRectangle rectangle) =>
        (long)rectangle.Top + rectangle.Height;

    private static void Validate(
        WindowRectangle window,
        WindowRectangle monitorBounds,
        WindowRectangle workArea,
        uint dpiX,
        IReadOnlyList<WindowRectangle> allMonitorBounds)
    {
        ArgumentNullException.ThrowIfNull(allMonitorBounds);
        ArgumentOutOfRangeException.ThrowIfLessThan(window.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(window.Height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(monitorBounds.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(monitorBounds.Height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workArea.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workArea.Height, 1);
        ArgumentOutOfRangeException.ThrowIfZero(dpiX);
    }
}
