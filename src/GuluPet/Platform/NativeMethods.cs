using System.Runtime.InteropServices;

namespace GuluPet.Platform;

internal static class NativeMethods
{
    internal const uint MonitorDefaultToNearest = 0x00000002;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const int DwmwaExtendedFrameBounds = 9;
    internal const int WmSettingChange = 0x001A;
    internal const int WmDisplayChange = 0x007E;
    internal const int WmDpiChanged = 0x02E0;
    private const uint DefaultDpi = 96;
    private static readonly IntPtr DpiAwarenessContextUnaware = new(-1);
    private static readonly IntPtr DpiAwarenessContextSystemAware = new(-2);
    private static readonly IntPtr DpiAwarenessContextPerMonitorAware = new(-3);
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    internal delegate IntPtr HookProcedure(
        int code,
        IntPtr message,
        IntPtr eventData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPhysicalCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LastInputInfo information);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr OpenInputDesktop(
        uint flags,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SwitchDesktop(IntPtr desktopHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseDesktop(IntPtr desktopHandle);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(
        int hookId,
        HookProcedure procedure,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(
        IntPtr hookHandle,
        int code,
        IntPtr message,
        IntPtr eventData);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        UIntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint minimumMessage,
        uint maximumMessage,
        uint removeMessage);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(
        IntPtr monitorHandle,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AreDpiAwarenessContextsEqual(
        IntPtr first,
        IntPtr second);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitorHandle,
        MonitorDpiType dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        out NativeRect value,
        int valueSize);

    [DllImport("kernel32.dll")]
    internal static extern ulong GetTickCount64();

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    internal static bool TryGetExtendedWindowRect(
        IntPtr windowHandle,
        out NativeRect rectangle)
    {
        int result = DwmGetWindowAttribute(
            windowHandle,
            DwmwaExtendedFrameBounds,
            out rectangle,
            Marshal.SizeOf<NativeRect>());
        return result == 0 || GetWindowRect(windowHandle, out rectangle);
    }

    internal static MonitorDpi GetWindowDpiOrDefault(IntPtr windowHandle)
    {
        uint dpi = windowHandle == IntPtr.Zero
            ? 0
            : GetDpiForWindow(windowHandle);
        dpi = dpi == 0 ? DefaultDpi : dpi;
        return new MonitorDpi(dpi, dpi);
    }

    internal static MonitorDpi GetMonitorDpiOrDefault(
        IntPtr monitorHandle,
        IntPtr fallbackWindowHandle = default)
    {
        if (monitorHandle != IntPtr.Zero)
        {
            try
            {
                int result = GetDpiForMonitor(
                    monitorHandle,
                    MonitorDpiType.Effective,
                    out uint dpiX,
                    out uint dpiY);
                if (result == 0 && dpiX > 0 && dpiY > 0)
                {
                    return new MonitorDpi(dpiX, dpiY);
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        return GetWindowDpiOrDefault(fallbackWindowHandle);
    }

    internal static string GetWindowDpiAwarenessName(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return "unknown";
        }

        try
        {
            IntPtr context = GetWindowDpiAwarenessContext(windowHandle);
            if (AreDpiAwarenessContextsEqual(
                    context,
                    DpiAwarenessContextPerMonitorAwareV2))
            {
                return "perMonitorV2";
            }

            if (AreDpiAwarenessContextsEqual(
                    context,
                    DpiAwarenessContextPerMonitorAware))
            {
                return "perMonitor";
            }

            if (AreDpiAwarenessContextsEqual(
                    context,
                    DpiAwarenessContextSystemAware))
            {
                return "systemAware";
            }

            if (AreDpiAwarenessContextsEqual(
                    context,
                    DpiAwarenessContextUnaware))
            {
                return "unaware";
            }
        }
        catch (EntryPointNotFoundException)
        {
        }

        return "unknown";
    }

    internal readonly record struct MonitorDpi(uint X, uint Y);

    private enum MonitorDpiType
    {
        Effective = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LastInputInfo
    {
        internal uint Size;
        internal uint TickCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMessage
    {
        internal IntPtr WindowHandle;
        internal uint Message;
        internal UIntPtr WordParameter;
        internal IntPtr LongParameter;
        internal uint Time;
        internal NativePoint Point;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct MonitorInfo
    {
        internal int Size;
        internal NativeRect Monitor;
        internal NativeRect WorkArea;
        internal uint Flags;
    }
}
