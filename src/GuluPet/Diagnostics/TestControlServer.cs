using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using GuluPet.Testing;

namespace GuluPet.Diagnostics;

internal sealed class TestControlServer : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly string _pipeName;
    private readonly Func<
        TestControlRequest,
        CancellationToken,
        Task<TestControlResponse>> _handler;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private NamedPipeServerStream? _activePipe;
    private Task? _runTask;
    private bool _disposed;

    public TestControlServer(
        string pipeName,
        Func<
            TestControlRequest,
            CancellationToken,
            Task<TestControlResponse>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(handler);

        _pipeName = pipeName;
        _handler = handler;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runTask is not null)
        {
            return;
        }

        _runTask = Task.Run(() => RunAsync(_shutdown.Token));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        lock (_gate)
        {
            _activePipe?.Dispose();
            _activePipe = null;
        }

        _ = DisposeCancellationSourceWhenStoppedAsync(
            _runTask ?? Task.CompletedTask,
            _shutdown);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                lock (_gate)
                {
                    if (_disposed)
                    {
                        pipe.Dispose();
                        return;
                    }

                    _activePipe = pipe;
                }

                await pipe.WaitForConnectionAsync(cancellationToken);
                using var requestCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                requestCancellation.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    await ServeConnectionAsync(
                        pipe,
                        requestCancellation.Token);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    Debug.WriteLine(
                        "GuluPet test-control client timed out before " +
                        "completing its request.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException exception)
            {
                Debug.WriteLine($"GuluPet test-control pipe I/O error: {exception}");
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"GuluPet test-control server error: {exception}");
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_activePipe, pipe))
                    {
                        _activePipe = null;
                    }
                }

                pipe?.Dispose();
            }
        }
    }

    private async Task ServeConnectionAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        TestControlResponse response;
        try
        {
            string? line = await ReadLineLimitedAsync(stream, cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                response = InvalidRequest("Request body is empty.");
            }
            else
            {
                TestControlRequest? request = JsonSerializer.Deserialize<TestControlRequest>(
                    line,
                    TestControlProtocol.JsonOptions);
                response = request is null
                    ? InvalidRequest("Request body is invalid.")
                    : await _handler(request, cancellationToken);
            }
        }
        catch (JsonException exception)
        {
            response = InvalidRequest($"Invalid JSON: {exception.Message}");
        }
        catch (InvalidDataException exception)
        {
            response = InvalidRequest(exception.Message);
        }
        catch (DecoderFallbackException)
        {
            response = InvalidRequest("Request is not valid UTF-8.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"GuluPet test-control handler error: {exception}");
            response = new TestControlResponse
            {
                Ok = false,
                ErrorCode = TestControlErrorCodes.InternalError,
                Error = "The test-control request failed.",
            };
        }

        string json = JsonSerializer.Serialize(
            response,
            TestControlProtocol.JsonOptions);
        byte[] responseBytes = StrictUtf8.GetBytes(json + "\n");
        if (responseBytes.Length > TestControlProtocol.MaxResponseBytes)
        {
            throw new InvalidDataException(
                $"Response exceeds {TestControlProtocol.MaxResponseBytes} bytes.");
        }

        await stream.WriteAsync(responseBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<string?> ReadLineLimitedAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var result = new byte[TestControlProtocol.MaxRequestBytes];
        var buffer = new byte[1];
        int length = 0;

        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                if (length == 0)
                {
                    return null;
                }

                throw new InvalidDataException(
                    "Request must end with a newline.");
            }

            byte value = buffer[0];
            if (value == (byte)'\n')
            {
                return DecodeRequest(result, length);
            }

            if (length >= result.Length)
            {
                throw new InvalidDataException(
                    $"Request exceeds {TestControlProtocol.MaxRequestBytes} bytes.");
            }

            result[length++] = value;
        }
    }

    private static string DecodeRequest(byte[] bytes, int length)
    {
        if (length > 0 && bytes[length - 1] == (byte)'\r')
        {
            length--;
        }

        string result = StrictUtf8.GetString(bytes, 0, length);
        return result.Length > 0 && result[0] == '\uFEFF'
            ? result[1..]
            : result;
    }

    private static async Task DisposeCancellationSourceWhenStoppedAsync(
        Task runTask,
        CancellationTokenSource cancellationSource)
    {
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch
        {
            // RunAsync owns diagnostics; disposal must not throw on the UI thread.
        }
        finally
        {
            cancellationSource.Dispose();
        }
    }

    private static TestControlResponse InvalidRequest(string message) =>
        new()
        {
            ProtocolVersion = TestControlProtocol.Version,
            Ok = false,
            ErrorCode = "invalid_request",
            Error = message,
        };
}
