using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GuluPet.Sensing;

namespace GuluPet.Platform;

internal interface IApplicationCategoryProcessClassifier
{
    string Classify(string processName);
}

internal sealed class ApplicationCategoryProcessClassifier
    : IApplicationCategoryProcessClassifier
{
    public string Classify(string processName) =>
        ApplicationCategoryClassifier.Classify(processName);
}

internal sealed class WindowsForegroundApplicationSource(
    IApplicationCategoryProcessClassifier classifier)
    : IForegroundApplicationSource
{
    private readonly IApplicationCategoryProcessClassifier _classifier =
        classifier ?? throw new ArgumentNullException(nameof(classifier));
    private readonly int _currentProcessId = Environment.ProcessId;

    public ForegroundApplicationReading Read()
    {
        IntPtr window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return new ForegroundApplicationReading(
                "unknown",
                false,
                false);
        }

        _ = NativeMethods.GetWindowThreadProcessId(
            window,
            out uint processId);
        if (processId == 0)
        {
            return new ForegroundApplicationReading(
                "unknown",
                false,
                false);
        }

        if (processId == _currentProcessId)
        {
            return new ForegroundApplicationReading(
                "unknown",
                false,
                true);
        }

        try
        {
            using Process process = Process.GetProcessById(
                checked((int)processId));
            // The raw process name exists only as a local value for immediate
            // categorization. It is never emitted, logged, or persisted.
            string category = _classifier.Classify(process.ProcessName);
            return new ForegroundApplicationReading(
                category,
                true,
                false);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or InvalidOperationException
                  or Win32Exception
                  or NotSupportedException
                  or UnauthorizedAccessException)
        {
            return new ForegroundApplicationReading(
                "unknown",
                false,
                false);
        }
    }
}

internal sealed class WindowsIdleTimeSource : IIdleTimeSource
{
    public bool TryGetIdleTime(out TimeSpan idleFor)
    {
        var information = new NativeMethods.LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>(),
        };
        if (!NativeMethods.GetLastInputInfo(ref information))
        {
            idleFor = TimeSpan.Zero;
            return false;
        }

        uint currentTick = unchecked((uint)NativeMethods.GetTickCount64());
        uint elapsed = unchecked(currentTick - information.TickCount);
        idleFor = TimeSpan.FromMilliseconds(elapsed);
        return true;
    }
}

internal interface IWindowsSessionLockNativeApi
{
    bool TryRead(out bool isLocked);
}

internal sealed class WindowsSessionLockNativeApi
    : IWindowsSessionLockNativeApi
{
    private const uint DesktopSwitchDesktop = 0x0100;
    private const int ErrorAccessDenied = 5;

    public bool TryRead(out bool isLocked)
    {
        IntPtr desktop = NativeMethods.OpenInputDesktop(
            0,
            inherit: false,
            DesktopSwitchDesktop);
        if (desktop == IntPtr.Zero)
        {
            // Access to the secure desktop is denied while the interactive
            // session is locked. Other failures are reported as unavailable
            // instead of being guessed as a user state.
            isLocked = Marshal.GetLastWin32Error() == ErrorAccessDenied;
            return isLocked;
        }

        try
        {
            isLocked = !NativeMethods.SwitchDesktop(desktop);
            return true;
        }
        finally
        {
            _ = NativeMethods.CloseDesktop(desktop);
        }
    }
}

internal sealed class WindowsSessionLockSource(
    IWindowsSessionLockNativeApi? native = null) : ISessionLockSource
{
    private readonly IWindowsSessionLockNativeApi _native =
        native ?? new WindowsSessionLockNativeApi();

    public SessionLockReading Read()
    {
        try
        {
            return _native.TryRead(out bool isLocked)
                ? new SessionLockReading(isLocked, true)
                : new SessionLockReading(false, false);
        }
        catch (Exception exception)
            when (exception is DllNotFoundException
                  or EntryPointNotFoundException
                  or PlatformNotSupportedException)
        {
            return new SessionLockReading(false, false);
        }
    }
}
