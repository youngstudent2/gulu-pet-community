using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace GuluPet.Platform;

public sealed class FullscreenStateChangedEventArgs(bool isFullscreen) : EventArgs
{
    public bool IsFullscreen { get; } = isFullscreen;
}

public static class FullscreenPresentationPolicy
{
    public static bool ShouldShowPet(
        bool userWantsPetVisible,
        bool alwaysOnTop,
        bool foregroundIsFullscreen)
    {
        return userWantsPetVisible && (alwaysOnTop || !foregroundIsFullscreen);
    }
}

public sealed class FullscreenMonitor : IDisposable
{
    private const int EdgeTolerancePixels = 2;
    private readonly DispatcherTimer _timer;
    private bool _lastValue;
    private bool _hasValue;
    private bool _disposed;

    public FullscreenMonitor(TimeSpan? pollInterval = null)
    {
        _timer = new DispatcherTimer(
            DispatcherPriority.Background)
        {
            Interval = pollInterval ?? TimeSpan.FromMilliseconds(750),
        };
        _timer.Tick += OnTimerTick;
    }

    public event EventHandler<FullscreenStateChangedEventArgs>? FullscreenStateChanged;

    public bool IsFullscreen => _lastValue;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Poll();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    public static bool IsForegroundWindowFullscreen()
    {
        IntPtr windowHandle = NativeMethods.GetForegroundWindow();
        if (windowHandle == IntPtr.Zero
            || windowHandle == NativeMethods.GetShellWindow()
            || windowHandle == NativeMethods.GetDesktopWindow()
            || NativeMethods.IsIconic(windowHandle))
        {
            return false;
        }

        IntPtr monitorHandle = NativeMethods.MonitorFromWindow(
            windowHandle,
            NativeMethods.MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.MonitorInfo monitorInfo = new()
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };
        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo)
            || !NativeMethods.TryGetExtendedWindowRect(
                windowHandle,
                out NativeMethods.NativeRect windowRectangle))
        {
            return false;
        }

        NativeMethods.NativeRect monitorRectangle = monitorInfo.Monitor;
        return windowRectangle.Left <= monitorRectangle.Left + EdgeTolerancePixels
            && windowRectangle.Top <= monitorRectangle.Top + EdgeTolerancePixels
            && windowRectangle.Right >= monitorRectangle.Right - EdgeTolerancePixels
            && windowRectangle.Bottom >= monitorRectangle.Bottom - EdgeTolerancePixels;
    }

    private void OnTimerTick(object? sender, EventArgs e) => Poll();

    private void Poll()
    {
        bool currentValue = IsForegroundWindowFullscreen();
        if (_hasValue && currentValue == _lastValue)
        {
            return;
        }

        _hasValue = true;
        _lastValue = currentValue;
        FullscreenStateChanged?.Invoke(
            this,
            new FullscreenStateChangedEventArgs(currentValue));
    }
}
