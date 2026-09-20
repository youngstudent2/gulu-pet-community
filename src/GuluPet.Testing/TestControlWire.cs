using System.Text.Json;

namespace GuluPet.Testing;

public static class TestControlWire
{
    private static readonly byte[] NewLine = [(byte)'\n'];

    public static async Task WriteAsync<T>(
        Stream stream,
        T value,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            value,
            TestControlProtocol.JsonOptions);
        if (payload.Length > maxBytes)
        {
            throw new TestControlProtocolException(
                $"Protocol message is {payload.Length} bytes; maximum is {maxBytes} bytes.");
        }

        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        using var payload = new MemoryStream();
        var buffer = new byte[Math.Min(4096, maxBytes + 1)];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new TestControlProtocolException(
                    "Connection closed before the newline-terminated protocol message was complete.");
            }

            var newlineIndex = Array.IndexOf(buffer, (byte)'\n', 0, bytesRead);
            var payloadBytes = newlineIndex >= 0 ? newlineIndex : bytesRead;
            if (payload.Length + payloadBytes > maxBytes)
            {
                throw new TestControlProtocolException(
                    $"Protocol message exceeds the {maxBytes}-byte limit.");
            }

            payload.Write(buffer, 0, payloadBytes);
            if (newlineIndex < 0)
            {
                continue;
            }

            var serialized = payload.ToArray();
            if (serialized.Length > 0 && serialized[^1] == (byte)'\r')
            {
                Array.Resize(ref serialized, serialized.Length - 1);
            }

            try
            {
                return JsonSerializer.Deserialize<T>(
                           serialized,
                           TestControlProtocol.JsonOptions)
                       ?? throw new TestControlProtocolException(
                           $"Protocol message did not contain a {typeof(T).Name} value.");
            }
            catch (JsonException exception)
            {
                throw new TestControlProtocolException(
                    $"Protocol message is not valid {typeof(T).Name} JSON.",
                    exception);
            }
        }
    }
}

public sealed class TestControlProtocolException : Exception
{
    public TestControlProtocolException(string message)
        : base(message)
    {
    }

    public TestControlProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
