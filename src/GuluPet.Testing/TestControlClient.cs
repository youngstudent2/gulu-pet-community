using System.IO.Pipes;

namespace GuluPet.Testing;

public sealed class TestControlClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    public TestControlClient(string pipeName = TestControlProtocol.PipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name is required.", nameof(pipeName));
        }

        PipeName = pipeName;
    }

    public string PipeName { get; }

    public async Task<TestControlResponse> SendAsync(
        TestControlRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var effectiveTimeout = timeout ?? DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Timeout must be greater than zero.");
        }

        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellation.CancelAfter(effectiveTimeout);
        var effectiveCancellationToken = linkedCancellation.Token;

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(effectiveCancellationToken).ConfigureAwait(false);
            await TestControlWire.WriteAsync(
                    pipe,
                    request,
                    TestControlProtocol.MaxRequestBytes,
                    effectiveCancellationToken)
                .ConfigureAwait(false);
            var response = await TestControlWire.ReadAsync<TestControlResponse>(
                    pipe,
                    TestControlProtocol.MaxResponseBytes,
                    effectiveCancellationToken)
                .ConfigureAwait(false);

            if (response.ProtocolVersion != TestControlProtocol.Version)
            {
                throw new TestControlProtocolException(
                    $"Server returned protocol version {response.ProtocolVersion}; " +
                    $"expected {TestControlProtocol.Version}.");
            }

            return response;
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TestControlUnavailableException(
                $"Timed out connecting to or waiting for '{PipeName}' after " +
                $"{effectiveTimeout.TotalMilliseconds:0} ms.",
                exception);
        }
        catch (IOException exception)
        {
            throw new TestControlUnavailableException(
                $"Could not communicate with the local GuluPet test pipe '{PipeName}'.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new TestControlUnavailableException(
                $"Access to the local GuluPet test pipe '{PipeName}' was denied.",
                exception);
        }
    }
}

public sealed class TestControlUnavailableException : Exception
{
    public TestControlUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
