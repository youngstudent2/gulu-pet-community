using GuluPet.Sensing;

namespace GuluPet.Platform;

internal interface IGlobalInputHookNativeApi
{
    void EnsureMessageQueue();

    int GetCurrentThreadId();

    IntPtr InstallKeyboardHook(NativeMethods.HookProcedure procedure);

    IntPtr InstallMouseHook(NativeMethods.HookProcedure procedure);

    bool Unhook(IntPtr hookHandle);

    bool PostQuit(int threadId);

    int GetMessage();

    IntPtr CallNext(
        int code,
        IntPtr message,
        IntPtr eventData);
}

internal sealed class WindowsGlobalInputHookNativeApi
    : IGlobalInputHookNativeApi
{
    private const int WhKeyboardLowLevel = 13;
    private const int WhMouseLowLevel = 14;
    private const uint WmQuit = 0x0012;

    public void EnsureMessageQueue() =>
        _ = NativeMethods.PeekMessage(
            out _,
            IntPtr.Zero,
            0,
            0,
            0);

    public int GetCurrentThreadId() =>
        unchecked((int)NativeMethods.GetCurrentThreadId());

    public IntPtr InstallKeyboardHook(
        NativeMethods.HookProcedure procedure) =>
        NativeMethods.SetWindowsHookEx(
            WhKeyboardLowLevel,
            procedure,
            NativeMethods.GetModuleHandle(null),
            0);

    public IntPtr InstallMouseHook(
        NativeMethods.HookProcedure procedure) =>
        NativeMethods.SetWindowsHookEx(
            WhMouseLowLevel,
            procedure,
            NativeMethods.GetModuleHandle(null),
            0);

    public bool Unhook(IntPtr hookHandle) =>
        NativeMethods.UnhookWindowsHookEx(hookHandle);

    public bool PostQuit(int threadId) =>
        NativeMethods.PostThreadMessage(
            unchecked((uint)threadId),
            WmQuit,
            UIntPtr.Zero,
            IntPtr.Zero);

    public int GetMessage() =>
        NativeMethods.GetMessage(
            out _,
            IntPtr.Zero,
            0,
            0);

    public IntPtr CallNext(
        int code,
        IntPtr message,
        IntPtr eventData) =>
        NativeMethods.CallNextHookEx(
            IntPtr.Zero,
            code,
            message,
            eventData);
}

internal sealed class GlobalInputActivitySource : IInputActivitySource
{
    private const int WmKeyDown = 0x0100;
    private const int WmSystemKeyDown = 0x0104;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;

    private readonly object _lifecycleGate = new();
    private readonly IGlobalInputHookNativeApi _native;
    private Thread? _hookThread;
    private ManualResetEventSlim? _ready;
    private NativeMethods.HookProcedure? _keyboardProcedure;
    private NativeMethods.HookProcedure? _mouseProcedure;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private long _keyboardCount;
    private long _mouseClickCount;
    private int _hookThreadId;
    private int _available;
    private int _stopRequested;
    private bool _disposed;

    internal GlobalInputActivitySource()
        : this(new WindowsGlobalInputHookNativeApi())
    {
    }

    internal GlobalInputActivitySource(
        IGlobalInputHookNativeApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public void Start()
    {
        ManualResetEventSlim ready;
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hookThread is { IsAlive: true })
            {
                return;
            }

            _ready?.Dispose();
            ready = new ManualResetEventSlim(false);
            _ready = ready;
            Volatile.Write(ref _stopRequested, 0);
            _hookThread = new Thread(RunHookLoop)
            {
                IsBackground = true,
                Name = "GuluPet global input counter",
            };
            _hookThread.Start();
        }

        // Read() has deterministic availability after normal hook setup.
        // A bounded wait keeps startup responsive if Windows is unhealthy.
        _ = ready.Wait(TimeSpan.FromSeconds(1));
    }

    public void Stop()
    {
        Thread? thread;
        ManualResetEventSlim? ready;
        lock (_lifecycleGate)
        {
            thread = _hookThread;
            ready = _ready;
        }

        if (thread is null)
        {
            return;
        }

        Volatile.Write(ref _stopRequested, 1);
        Volatile.Write(ref _available, 0);
        _ = ready?.Wait(TimeSpan.FromSeconds(1));
        int threadId = Volatile.Read(ref _hookThreadId);
        if (threadId != 0)
        {
            _ = _native.PostQuit(threadId);
        }

        if (thread != Thread.CurrentThread)
        {
            _ = thread.Join(TimeSpan.FromSeconds(2));
        }

        lock (_lifecycleGate)
        {
            if (!thread.IsAlive && ReferenceEquals(_hookThread, thread))
            {
                _hookThread = null;
            }
        }
    }

    public InputActivityReading Read() =>
        new(
            Interlocked.Read(ref _keyboardCount),
            Interlocked.Read(ref _mouseClickCount),
            Volatile.Read(ref _available) != 0);

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }
        Stop();
        lock (_lifecycleGate)
        {
            if (_hookThread is not { IsAlive: true })
            {
                _ready?.Dispose();
                _ready = null;
            }
        }
    }

    private void RunHookLoop()
    {
        try
        {
            // PostQuit is reliable only after the target owns a message
            // queue. Establish the queue before publishing the thread id.
            _native.EnsureMessageQueue();
            Volatile.Write(
                ref _hookThreadId,
                _native.GetCurrentThreadId());
            _keyboardProcedure = OnKeyboardHook;
            _mouseProcedure = OnMouseHook;
            _keyboardHook = _native.InstallKeyboardHook(
                _keyboardProcedure);
            if (_keyboardHook == IntPtr.Zero)
            {
                return;
            }

            _mouseHook = _native.InstallMouseHook(_mouseProcedure);
            if (_mouseHook == IntPtr.Zero
                || Volatile.Read(ref _stopRequested) != 0)
            {
                return;
            }

            Volatile.Write(ref _available, 1);
            _ready?.Set();
            while (_native.GetMessage() > 0
                   && Volatile.Read(ref _stopRequested) == 0)
            {
                // Low-level hook callbacks are dispatched as part of this
                // message loop. No key code or mouse position is inspected.
            }
        }
        catch
        {
            // Global input counts are optional context. Native hook failures
            // must not terminate the process.
        }
        finally
        {
            Volatile.Write(ref _available, 0);
            if (_keyboardHook != IntPtr.Zero)
            {
                _ = _native.Unhook(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }

            if (_mouseHook != IntPtr.Zero)
            {
                _ = _native.Unhook(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }

            _keyboardProcedure = null;
            _mouseProcedure = null;
            Volatile.Write(ref _hookThreadId, 0);
            _ready?.Set();
        }
    }

    private IntPtr OnKeyboardHook(
        int code,
        IntPtr message,
        IntPtr eventData)
    {
        if (code >= 0)
        {
            long value = message.ToInt64();
            if (value is WmKeyDown or WmSystemKeyDown)
            {
                // Only the aggregate count is retained. eventData contains
                // key details and is intentionally never marshalled.
                Interlocked.Increment(ref _keyboardCount);
            }
        }

        return _native.CallNext(
            code,
            message,
            eventData);
    }

    private IntPtr OnMouseHook(
        int code,
        IntPtr message,
        IntPtr eventData)
    {
        if (code >= 0)
        {
            long value = message.ToInt64();
            if (value is WmLeftButtonDown
                or WmRightButtonDown
                or WmMiddleButtonDown
                or WmXButtonDown)
            {
                // Mouse movement, wheel data and coordinates are ignored.
                Interlocked.Increment(ref _mouseClickCount);
            }
        }

        return _native.CallNext(
            code,
            message,
            eventData);
    }
}
