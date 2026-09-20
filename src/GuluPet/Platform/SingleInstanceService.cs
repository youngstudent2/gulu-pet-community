using System.Security.Cryptography;
using System.Text;

namespace GuluPet.Platform;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName =
        @"Local\" + global::GuluPet.AppIdentity.ObjectNamespace + ".SingleInstance.v1";
    private const string ActivationEventName =
        @"Local\" + global::GuluPet.AppIdentity.ObjectNamespace + ".Activate.v1";
    private const string TestMutexName =
        @"Local\" + global::GuluPet.AppIdentity.ObjectNamespace + ".TestInstance.SingleInstance.v1";
    private const string TestActivationEventName =
        @"Local\" + global::GuluPet.AppIdentity.ObjectNamespace + ".TestInstance.Activate.v1";
    private readonly string _mutexName;
    private readonly string _activationEventName;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _registeredWait;
    private bool _ownsMutex;
    private bool _disposed;

    public SingleInstanceService(
        bool isolatedTestInstance = false,
        string? isolatedInstanceKey = null)
    {
        if (!isolatedTestInstance)
        {
            if (isolatedInstanceKey is not null)
            {
                throw new ArgumentException(
                    "An isolated instance key requires isolated test mode.",
                    nameof(isolatedInstanceKey));
            }

            _mutexName = MutexName;
            _activationEventName = ActivationEventName;
            return;
        }

        if (isolatedInstanceKey is null)
        {
            _mutexName = TestMutexName;
            _activationEventName = TestActivationEventName;
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(isolatedInstanceKey);
        string namespaceSuffix = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(isolatedInstanceKey))
                .AsSpan(0, 16));
        _mutexName =
            $@"Local\{AppIdentity.ObjectNamespace}.TestInstance.{namespaceSuffix}.SingleInstance.v1";
        _activationEventName =
            $@"Local\{AppIdentity.ObjectNamespace}.TestInstance.{namespaceSuffix}.Activate.v1";
    }

    public bool TryAcquire(
        Action activationCallback,
        bool notifyExistingInstance = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(activationCallback);
        if (_mutex is not null)
        {
            throw new InvalidOperationException("Single-instance acquisition was already attempted.");
        }

        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            _activationEventName);
        _mutex = new Mutex(
            initiallyOwned: true,
            _mutexName,
            out bool createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            if (notifyExistingInstance)
            {
                _activationEvent.Set();
            }

            return false;
        }

        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => activationCallback(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registeredWait?.Unregister(null);
        _registeredWait = null;
        _activationEvent?.Dispose();
        _activationEvent = null;

        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The process is already exiting; there is nothing to recover.
            }
        }

        _mutex?.Dispose();
        _mutex = null;
        _ownsMutex = false;
    }
}
