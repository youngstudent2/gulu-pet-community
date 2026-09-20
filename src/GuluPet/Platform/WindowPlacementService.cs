using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace GuluPet.Platform;

public readonly record struct WindowPosition(double Left, double Top);

public readonly record struct WindowRectangle(
    int Left,
    int Top,
    int Width,
    int Height);

public readonly record struct WindowDisplayDiagnostics(
    WindowRectangle Window,
    string CoordinateSpace,
    string DpiAwareness,
    int Dpi,
    WindowRectangle? MonitorBounds,
    WindowRectangle? WorkArea);

public static class WindowPlacementService
{
    private const int DefaultMargin = 24;
    private const int MinimumVisiblePixels = 48;
    internal const uint BubbleMoveFlags =
        NativeMethods.SwpNoSize
        | NativeMethods.SwpNoZOrder
        | NativeMethods.SwpNoActivate;

    public static void Restore(Window window, double? savedLeft, double? savedTop)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero
            || !NativeMethods.TryGetExtendedWindowRect(
                handle,
                out NativeMethods.NativeRect current))
        {
            return;
        }

        Rectangle target = savedLeft is double left
            && savedTop is double top
            && double.IsFinite(left)
            && double.IsFinite(top)
                ? new Rectangle(
                    ConvertToInt(left),
                    ConvertToInt(top),
                    Math.Max(1, current.Width),
                    Math.Max(1, current.Height))
                : CreateDefaultRectangle(current.Width, current.Height);

        target = BringIntoVisibleWorkArea(target);
        Move(handle, target.X, target.Y);
    }

    public static void Reset(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero
            || !NativeMethods.TryGetExtendedWindowRect(
                handle,
                out NativeMethods.NativeRect current))
        {
            return;
        }

        Rectangle target = CreateDefaultRectangle(current.Width, current.Height);
        Move(handle, target.X, target.Y);
    }

    public static void EnsureVisible(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero
            || !NativeMethods.TryGetExtendedWindowRect(
                handle,
                out NativeMethods.NativeRect current))
        {
            return;
        }

        Rectangle target = BringIntoVisibleWorkArea(new Rectangle(
            current.Left,
            current.Top,
            Math.Max(1, current.Width),
            Math.Max(1, current.Height)));
        Move(handle, target.X, target.Y);
    }

    public static WindowPosition GetPosition(Window window)
    {
        WindowRectangle rectangle = GetRectangle(window);
        return new WindowPosition(rectangle.Left, rectangle.Top);
    }

    public static WindowRectangle GetRectangle(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero
            && NativeMethods.TryGetExtendedWindowRect(
                handle,
                out NativeMethods.NativeRect current))
        {
            return ToWindowRectangle(current);
        }

        return new WindowRectangle(
            ConvertToInt(window.Left),
            ConvertToInt(window.Top),
            Math.Max(
                1,
                ConvertToInt(
                    window.ActualWidth > 0
                        ? window.ActualWidth
                        : window.Width)),
            Math.Max(
                1,
                ConvertToInt(
                    window.ActualHeight > 0
                        ? window.ActualHeight
                        : window.Height)));
    }

    public static WindowDisplayDiagnostics GetDiagnostics(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        WindowRectangle rectangle = GetRectangle(window);
        IntPtr handle = new WindowInteropHelper(window).Handle;
        NativeMethods.MonitorDpi dpi =
            NativeMethods.GetWindowDpiOrDefault(handle);
        WindowRectangle? monitorBounds = null;
        WindowRectangle? workArea = null;
        IntPtr monitorHandle = NativeMethods.MonitorFromWindow(
            handle,
            NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };
        if (monitorHandle != IntPtr.Zero
            && NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            monitorBounds = ToWindowRectangle(monitorInfo.Monitor);
            workArea = ToWindowRectangle(monitorInfo.WorkArea);
        }

        return new WindowDisplayDiagnostics(
            rectangle,
            "physicalPixels",
            NativeMethods.GetWindowDpiAwarenessName(handle),
            (int)dpi.X,
            monitorBounds,
            workArea);
    }

    internal static bool TryCreateSideDockCandidate(
        Window window,
        NativeMethods.NativePoint cursor,
        out SideDockCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr handle = new WindowInteropHelper(window).Handle;
        IntPtr monitorHandle = NativeMethods.MonitorFromPoint(
            cursor,
            NativeMethods.MonitorDefaultToNearest);
        if (handle == IntPtr.Zero || monitorHandle == IntPtr.Zero)
        {
            candidate = null;
            return false;
        }

        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };
        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            candidate = null;
            return false;
        }

        NativeMethods.MonitorDpi dpi =
            NativeMethods.GetMonitorDpiOrDefault(monitorHandle, handle);
        return SideDockPlacement.TryCalculate(
            GetRectangle(window),
            ToWindowRectangle(monitorInfo.Monitor),
            ToWindowRectangle(monitorInfo.WorkArea),
            dpi.X,
            GetMonitorBounds(),
            out candidate);
    }

    internal static bool TryRefreshSideDockCandidate(
        Window window,
        SideDockCandidate current,
        out SideDockCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(current);
        IntPtr handle = new WindowInteropHelper(window).Handle;
        int x = current.Edge == SideDockEdge.Left
            ? current.MonitorBounds.Left + 1
            : checked(
                current.MonitorBounds.Left +
                current.MonitorBounds.Width - 2);
        int y = checked(
            current.MonitorBounds.Top +
            current.MonitorBounds.Height / 2);
        IntPtr monitorHandle = NativeMethods.MonitorFromPoint(
            new NativeMethods.NativePoint { X = x, Y = y },
            NativeMethods.MonitorDefaultToNearest);
        if (handle == IntPtr.Zero || monitorHandle == IntPtr.Zero)
        {
            candidate = null;
            return false;
        }

        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };
        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            candidate = null;
            return false;
        }

        WindowRectangle monitorBounds =
            ToWindowRectangle(monitorInfo.Monitor);
        NativeMethods.MonitorDpi dpi =
            NativeMethods.GetMonitorDpiOrDefault(monitorHandle, handle);
        WindowRectangle actualBounds = GetRectangle(window);
        var restoreReference = new WindowRectangle(
            current.RestoreBounds.Left,
            current.RestoreBounds.Top,
            actualBounds.Width,
            actualBounds.Height);
        return SideDockPlacement.TryCalculateForEdge(
            current.Edge,
            restoreReference,
            monitorBounds,
            ToWindowRectangle(monitorInfo.WorkArea),
            dpi.X,
            GetMonitorBounds(),
            out candidate);
    }

    internal static bool TryMoveTo(
        Window window,
        WindowRectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero &&
            NativeMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                bounds.Left,
                bounds.Top,
                0,
                0,
                BubbleMoveFlags);
    }

    public static bool PositionMemoryWindowNear(
        Window memoryWindow,
        Window anchor,
        double desiredWidthDips,
        double desiredHeightDips,
        MemoryWindowSide? preferredSide,
        out MemoryWindowSide placedSide)
    {
        ArgumentNullException.ThrowIfNull(memoryWindow);
        ArgumentNullException.ThrowIfNull(anchor);
        if (!double.IsFinite(desiredWidthDips) || desiredWidthDips <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(desiredWidthDips));
        }

        if (!double.IsFinite(desiredHeightDips) || desiredHeightDips <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(desiredHeightDips));
        }

        placedSide = preferredSide ?? MemoryWindowSide.Right;
        IntPtr memoryHandle = new WindowInteropHelper(memoryWindow).Handle;
        IntPtr anchorHandle = new WindowInteropHelper(anchor).Handle;
        if (memoryHandle == IntPtr.Zero
            || anchorHandle == IntPtr.Zero
            || !NativeMethods.TryGetExtendedWindowRect(
                anchorHandle,
                out NativeMethods.NativeRect anchorRectangle))
        {
            return false;
        }

        IntPtr monitorHandle = NativeMethods.MonitorFromWindow(
            anchorHandle,
            NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };
        if (monitorHandle == IntPtr.Zero
            || !NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return false;
        }

        NativeMethods.MonitorDpi dpi =
            NativeMethods.GetMonitorDpiOrDefault(
                monitorHandle,
                anchorHandle);
        WindowRectangle anchorBounds = ToWindowRectangle(anchorRectangle);
        WindowRectangle workArea = ToWindowRectangle(monitorInfo.WorkArea);
        MemoryWindowPlacementResult requested =
            MemoryWindowPlacement.CalculateResponsive(
                anchorBounds,
                workArea,
                Math.Max(
                    1,
                    PhysicalDragPlacement.DipsToPhysicalPixels(
                        desiredWidthDips,
                        dpi.X)),
                Math.Max(
                    1,
                    PhysicalDragPlacement.DipsToPhysicalPixels(
                        desiredHeightDips,
                        dpi.Y)),
                gapDips: 16,
                dpi.X,
                preferredSide);

        // Let WPF own the PerMonitorV2 size conversion, then place only the
        // native x/y coordinates. Supplying a physical size to SetWindowPos
        // here can make WM_DPICHANGED apply the monitor scale twice.
        memoryWindow.Width = requested.Bounds.Width * 96d / dpi.X;
        memoryWindow.Height = requested.Bounds.Height * 96d / dpi.Y;
        memoryWindow.UpdateLayout();

        MemoryWindowPlacementResult finalPlacement = requested;
        if (NativeMethods.TryGetExtendedWindowRect(
                memoryHandle,
                out NativeMethods.NativeRect actualRectangle))
        {
            finalPlacement = MemoryWindowPlacement.CalculateResponsive(
                anchorBounds,
                workArea,
                Math.Max(1, actualRectangle.Width),
                Math.Max(1, actualRectangle.Height),
                gapDips: 16,
                dpi.X,
                requested.Side);
        }

        placedSide = finalPlacement.Side;
        return NativeMethods.SetWindowPos(
            memoryHandle,
            IntPtr.Zero,
            finalPlacement.Bounds.Left,
            finalPlacement.Bounds.Top,
            0,
            0,
            BubbleMoveFlags);
    }

    public static bool PositionBubbleNear(Window bubble, Window anchor)
    {
        ArgumentNullException.ThrowIfNull(bubble);
        ArgumentNullException.ThrowIfNull(anchor);

        IntPtr bubbleHandle = new WindowInteropHelper(bubble).Handle;
        IntPtr anchorHandle = new WindowInteropHelper(anchor).Handle;
        if (bubbleHandle == IntPtr.Zero
            || anchorHandle == IntPtr.Zero
            || !NativeMethods.TryGetExtendedWindowRect(
                anchorHandle,
                out NativeMethods.NativeRect anchorRectangle))
        {
            return false;
        }

        IntPtr monitorHandle = NativeMethods.MonitorFromWindow(
            anchorHandle,
            NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };
        if (monitorHandle == IntPtr.Zero
            || !NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return false;
        }

        NativeMethods.MonitorDpi dpi =
            NativeMethods.GetMonitorDpiOrDefault(
                monitorHandle,
                anchorHandle);
        double bubbleWidthDip = ResolveDimension(
            bubble.ActualWidth,
            bubble.Width);
        double bubbleHeightDip = ResolveDimension(
            bubble.ActualHeight,
            bubble.Height);
        WindowRectangle anchorBounds = ToWindowRectangle(anchorRectangle);
        WindowRectangle workArea = ToWindowRectangle(monitorInfo.WorkArea);
        BubblePlacementResult placement =
            BubblePlacement.CalculateFromDips(
                anchorBounds,
                workArea,
                bubbleWidthDip,
                bubbleHeightDip,
                dpi.X,
                dpi.Y);

        // WPF owns the Width and SizeToContent dimensions. Passing physical
        // dimensions here makes PerMonitorV2 apply the target DPI a second
        // time while handling WM_DPICHANGED.
        if (!NativeMethods.SetWindowPos(
                bubbleHandle,
                IntPtr.Zero,
                placement.Bounds.Left,
                placement.Bounds.Top,
                0,
                0,
                BubbleMoveFlags))
        {
            return false;
        }

        // WM_DPICHANGED is normally handled synchronously. Clamp once more
        // using the native size WPF actually selected so a future layout or
        // platform change cannot leave part of the bubble on another monitor.
        if (!NativeMethods.TryGetExtendedWindowRect(
                bubbleHandle,
                out NativeMethods.NativeRect actualRectangle))
        {
            return true;
        }

        if (actualRectangle.Width == placement.Bounds.Width
            && actualRectangle.Height == placement.Bounds.Height)
        {
            return true;
        }

        BubblePlacementResult corrected =
            BubblePlacement.CalculatePhysical(
                anchorBounds,
                workArea,
                Math.Max(1, actualRectangle.Width),
                Math.Max(1, actualRectangle.Height),
                dpi.X,
                dpi.Y);
        return NativeMethods.SetWindowPos(
            bubbleHandle,
            IntPtr.Zero,
            corrected.Bounds.Left,
            corrected.Bounds.Top,
            0,
            0,
            BubbleMoveFlags);
    }

    private static Rectangle BringIntoVisibleWorkArea(Rectangle target)
    {
        Forms.Screen[] screens = Forms.Screen.AllScreens;
        Forms.Screen? screen = screens
            .OrderByDescending(candidate => IntersectionArea(
                candidate.WorkingArea,
                target))
            .FirstOrDefault();

        if (screen is null
            || IntersectionWidth(screen.WorkingArea, target) < MinimumVisiblePixels
            || IntersectionHeight(screen.WorkingArea, target) < MinimumVisiblePixels)
        {
            return CreateDefaultRectangle(target.Width, target.Height);
        }

        Rectangle workArea = screen.WorkingArea;
        int maxX = Math.Max(workArea.Left, workArea.Right - target.Width);
        int maxY = Math.Max(workArea.Top, workArea.Bottom - target.Height);
        int x = Math.Clamp(target.Left, workArea.Left, maxX);
        int y = Math.Clamp(target.Top, workArea.Top, maxY);
        return new Rectangle(x, y, target.Width, target.Height);
    }

    private static Rectangle CreateDefaultRectangle(int width, int height)
    {
        Rectangle workArea = (Forms.Screen.PrimaryScreen
            ?? Forms.Screen.AllScreens.First()).WorkingArea;
        int x = Math.Max(
            workArea.Left,
            workArea.Right - Math.Max(1, width) - DefaultMargin);
        int y = Math.Max(
            workArea.Top,
            workArea.Bottom - Math.Max(1, height) - DefaultMargin);
        return new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));
    }

    private static IReadOnlyList<WindowRectangle> GetMonitorBounds() =>
        Forms.Screen.AllScreens
            .Select(static screen => ToWindowRectangle(screen.Bounds))
            .ToArray();

    private static long IntersectionArea(Rectangle first, Rectangle second)
    {
        return (long)IntersectionWidth(first, second)
            * IntersectionHeight(first, second);
    }

    private static int IntersectionWidth(Rectangle first, Rectangle second)
    {
        return Math.Max(
            0,
            Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
    }

    private static int IntersectionHeight(Rectangle first, Rectangle second)
    {
        return Math.Max(
            0,
            Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
    }

    private static int ConvertToInt(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        return value >= int.MaxValue
            ? int.MaxValue
            : value <= int.MinValue
                ? int.MinValue
                : (int)Math.Round(value);
    }

    private static double ResolveDimension(
        double actual,
        double requested) =>
        double.IsFinite(actual) && actual > 0
            ? actual
            : double.IsFinite(requested) && requested > 0
                ? requested
                : 1;

    private static WindowRectangle ToWindowRectangle(
        NativeMethods.NativeRect rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            Math.Max(1, rectangle.Width),
            Math.Max(1, rectangle.Height));

    private static WindowRectangle ToWindowRectangle(Rectangle rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            Math.Max(1, rectangle.Width),
            Math.Max(1, rectangle.Height));

    private static void Move(IntPtr handle, int x, int y)
    {
        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            NativeMethods.SwpNoSize
                | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate);
    }
}
