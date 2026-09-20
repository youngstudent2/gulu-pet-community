using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GuluPet.Diagnostics;

internal sealed record JsonlRetentionFileState(
    DateTimeOffset OldestTimestampUtc,
    DateTimeOffset NewestTimestampUtc);

internal static class JsonlRetentionMaintenance
{
    private const int ReadBufferBytes = 64 * 1024;
    private const int InitialLineBufferBytes = 16 * 1024;

    // Runtime records are normally only a few KiB. This prevents one damaged
    // legacy line from turning retention maintenance into an unbounded rent.
    internal const int MaximumBufferedLineBytes = 1024 * 1024;

    // The timestamp is parsed only from a root JSON property. Once that value
    // has been read, damaged trailing JSON can still expire by its timestamp.
    // Lines without a usable root timestamp are discarded fail closed.
    internal static bool TryPruneFile(
        string path,
        string timestampPropertyName,
        DateTimeOffset cutoffUtc,
        DateTimeOffset maximumTimestampUtc,
        long maximumFileBytes,
        out JsonlRetentionFileState? state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timestampPropertyName);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFileBytes, 1);
        if (maximumTimestampUtc < cutoffUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumTimestampUtc));
        }

        state = null;
        if (!File.Exists(path))
        {
            return true;
        }

        if (IsReparsePoint(path))
        {
            return false;
        }

        byte[] propertyNameUtf8 = Encoding.UTF8.GetBytes(
            timestampPropertyName);
        FileSnapshot original = CaptureSnapshot(path);
        if (original.Length > maximumFileBytes)
        {
            ReplaceWithBoundedSuffix(
                path,
                original,
                maximumFileBytes);
            original = CaptureSnapshot(path);
        }

        FileInspection inspection = InspectFile(
            path,
            propertyNameUtf8,
            cutoffUtc,
            maximumTimestampUtc,
            maximumFileBytes);
        ThrowIfSourceChanged(path, original);
        if (!inspection.RequiresRewrite)
        {
            state = inspection.State;
            return true;
        }

        if (inspection.State is null)
        {
            DeleteControlledFile(path);
            return true;
        }

        state = RewriteRetainedLines(
            path,
            propertyNameUtf8,
            cutoffUtc,
            maximumTimestampUtc,
            maximumFileBytes,
            original);
        if (state is null)
        {
            DeleteControlledFile(path);
        }

        return true;
    }

    internal static bool NeedsLineSeparator(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        ThrowIfReparsePoint(path);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.RandomAccess);
        if (stream.Length == 0)
        {
            return false;
        }

        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() != (byte)'\n';
    }

    internal static void DeleteOrphanedTemporaryFiles(
        string controlledPath)
    {
        string fullPath = Path.GetFullPath(controlledPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        ThrowIfDirectoryPathContainsReparsePoint(directory);
        string controlledFileName = Path.GetFileName(fullPath);
        string prefix = $".{controlledFileName}.retention-";
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(
                directory,
                $"{prefix}*.tmp",
                SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string candidate in candidates)
        {
            string name = Path.GetFileName(candidate);
            const int guidTextLength = 32;
            if (name.Length != prefix.Length + guidTextLength + 4
                || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(
                    name.AsSpan(prefix.Length, guidTextLength),
                    "N",
                    out _))
            {
                continue;
            }

            try
            {
                FileAttributes attributes = File.GetAttributes(candidate);
                if ((attributes & (FileAttributes.ReparsePoint
                                   | FileAttributes.Directory)) != 0)
                {
                    continue;
                }

                // Active maintenance owns its temp with FileShare.None. Only
                // an exclusively reopenable ordinary file is an orphan; using
                // DeleteOnClose avoids a separate check/delete race.
                using var orphan = new FileStream(
                    candidate,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // An active, protected, or concurrently changed file stays.
            }
        }
    }

    internal static void DeleteControlledFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        ThrowIfReparsePoint(path);
        File.Delete(path);
    }

    internal static void ThrowIfReparsePoint(string path)
    {
        if (File.Exists(path) && IsReparsePoint(path))
        {
            throw new IOException(
                $"Refusing to access a reparse-point log file: {path}");
        }
    }

    internal static void ThrowIfDirectoryPathContainsReparsePoint(
        string directoryPath)
    {
        DirectoryInfo? current = new DirectoryInfo(
            Path.GetFullPath(directoryPath));
        while (current is not null)
        {
            if (current.Exists
                && (current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "Refusing to access a log directory through a " +
                    $"reparse point: {current.FullName}");
            }

            current = current.Parent;
        }
    }

    private static FileInspection InspectFile(
        string path,
        byte[] timestampPropertyNameUtf8,
        DateTimeOffset cutoffUtc,
        DateTimeOffset maximumTimestampUtc,
        long maximumFileBytes)
    {
        JsonlRetentionFileState? state = null;
        bool requiresRewrite = false;
        long retainedBytes = 0;
        int maximumLineBytes = checked((int)Math.Min(
            MaximumBufferedLineBytes,
            maximumFileBytes));

        // Construct the stream before renting. A failed open must not strand
        // a shared-pool buffer.
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: ReadBufferBytes,
            FileOptions.SequentialScan);
        byte[]? readBuffer = null;
        byte[]? lineBuffer = null;
        try
        {
            readBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
            lineBuffer = ArrayPool<byte>.Shared.Rent(
                Math.Min(InitialLineBufferBytes, maximumLineBytes));
            int lineLength = 0;
            bool lineHasData = false;
            bool lineOverflow = false;
            while (true)
            {
                int read = stream.Read(readBuffer, 0, readBuffer.Length);
                if (read == 0)
                {
                    break;
                }

                for (int index = 0; index < read; index++)
                {
                    byte value = readBuffer[index];
                    if (value == (byte)'\n')
                    {
                        IncludeCurrentLine(includeNewline: true);
                        ResetLine();
                        continue;
                    }

                    lineHasData = true;
                    if (lineOverflow)
                    {
                        continue;
                    }

                    if (lineLength >= maximumLineBytes)
                    {
                        lineOverflow = true;
                        continue;
                    }

                    EnsureLineCapacity();
                    lineBuffer[lineLength++] = value;
                }
            }

            if (lineHasData || lineOverflow)
            {
                IncludeCurrentLine(includeNewline: false);
            }

            requiresRewrite |= stream.Length > maximumFileBytes
                               || retainedBytes > maximumFileBytes;

            void IncludeCurrentLine(bool includeNewline)
            {
                if (lineOverflow
                    || !TryReadRootTimestamp(
                        lineBuffer.AsSpan(0, lineLength),
                        timestampPropertyNameUtf8,
                        out DateTimeOffset timestampUtc)
                    || timestampUtc < cutoffUtc
                    || timestampUtc > maximumTimestampUtc)
                {
                    requiresRewrite = true;
                    return;
                }

                state = IncludeTimestamp(state, timestampUtc);
                retainedBytes = AddSaturating(
                    retainedBytes,
                    lineLength + (includeNewline ? 1 : 0));
            }

            void EnsureLineCapacity()
            {
                if (lineLength < lineBuffer.Length)
                {
                    return;
                }

                int expandedLength = Math.Min(
                    maximumLineBytes,
                    checked(lineBuffer.Length * 2));
                byte[] expanded = ArrayPool<byte>.Shared.Rent(expandedLength);
                lineBuffer.AsSpan(0, lineLength).CopyTo(expanded);
                ArrayPool<byte>.Shared.Return(lineBuffer);
                lineBuffer = expanded;
            }

            void ResetLine()
            {
                lineLength = 0;
                lineHasData = false;
                lineOverflow = false;
            }
        }
        finally
        {
            if (readBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(readBuffer);
            }

            if (lineBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(lineBuffer);
            }
        }

        return new FileInspection(state, requiresRewrite);
    }

    private static JsonlRetentionFileState? RewriteRetainedLines(
        string path,
        byte[] timestampPropertyNameUtf8,
        DateTimeOffset cutoffUtc,
        DateTimeOffset maximumTimestampUtc,
        long maximumFileBytes,
        FileSnapshot original)
    {
        ThrowIfReparsePoint(path);
        ThrowIfSourceChanged(path, original);
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The retained JSONL path has no parent directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.retention-{Guid.NewGuid():N}.tmp");
        int maximumLineBytes = checked((int)Math.Min(
            MaximumBufferedLineBytes,
            maximumFileBytes));
        byte[]? readBuffer = null;
        byte[]? lineBuffer = null;
        try
        {
            using (var source = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       bufferSize: ReadBufferBytes,
                       FileOptions.SequentialScan))
            using (var destination = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: ReadBufferBytes,
                       FileOptions.None))
            {
                readBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
                lineBuffer = ArrayPool<byte>.Shared.Rent(
                    Math.Min(InitialLineBufferBytes, maximumLineBytes));
                int lineLength = 0;
                bool lineHasData = false;
                bool lineOverflow = false;
                while (true)
                {
                    int read = source.Read(
                        readBuffer,
                        0,
                        readBuffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    for (int index = 0; index < read; index++)
                    {
                        byte value = readBuffer[index];
                        if (value == (byte)'\n')
                        {
                            WriteCurrentLine(includeNewline: true);
                            ResetLine();
                            continue;
                        }

                        lineHasData = true;
                        if (lineOverflow)
                        {
                            continue;
                        }

                        if (lineLength >= maximumLineBytes)
                        {
                            lineOverflow = true;
                            continue;
                        }

                        EnsureLineCapacity();
                        lineBuffer[lineLength++] = value;
                    }
                }

                if (lineHasData || lineOverflow)
                {
                    WriteCurrentLine(includeNewline: false);
                }

                destination.Flush(flushToDisk: true);

                void EnsureLineCapacity()
                {
                    if (lineLength < lineBuffer.Length)
                    {
                        return;
                    }

                    int expandedLength = Math.Min(
                        maximumLineBytes,
                        checked(lineBuffer.Length * 2));
                    byte[] expanded = ArrayPool<byte>.Shared.Rent(
                        expandedLength);
                    lineBuffer.AsSpan(0, lineLength).CopyTo(expanded);
                    ArrayPool<byte>.Shared.Return(lineBuffer);
                    lineBuffer = expanded;
                }

                void WriteCurrentLine(bool includeNewline)
                {
                    if (!lineOverflow
                        && TryReadRootTimestamp(
                            lineBuffer.AsSpan(0, lineLength),
                            timestampPropertyNameUtf8,
                            out DateTimeOffset timestampUtc)
                        && timestampUtc >= cutoffUtc
                        && timestampUtc <= maximumTimestampUtc)
                    {
                        destination.Write(lineBuffer, 0, lineLength);
                        if (includeNewline)
                        {
                            destination.WriteByte((byte)'\n');
                        }
                    }
                }

                void ResetLine()
                {
                    lineLength = 0;
                    lineHasData = false;
                    lineOverflow = false;
                }
            }

            ReturnBuffers();
            TrimFileToMaximumBytes(temporaryPath, maximumFileBytes);
            FileInspection retainedInspection = InspectFile(
                temporaryPath,
                timestampPropertyNameUtf8,
                cutoffUtc,
                maximumTimestampUtc,
                maximumFileBytes);
            if (retainedInspection.RequiresRewrite)
            {
                throw new InvalidDataException(
                    "The retained JSONL temporary file is invalid.");
            }

            ThrowIfSourceChanged(path, original);
            ThrowIfReparsePoint(path);
            try
            {
                File.Replace(
                    temporaryPath,
                    path,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                // Only use the non-atomic compatibility path when Replace is
                // unavailable, never after a generic I/O race or failure.
                ThrowIfSourceChanged(path, original);
                File.Move(temporaryPath, path, overwrite: true);
            }

            File.SetLastWriteTimeUtc(path, original.LastWriteTimeUtc);
            return retainedInspection.State;
        }
        finally
        {
            ReturnBuffers();
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        void ReturnBuffers()
        {
            if (readBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(readBuffer);
                readBuffer = null;
            }

            if (lineBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(lineBuffer);
                lineBuffer = null;
            }
        }
    }

    private static void ReplaceWithBoundedSuffix(
        string path,
        FileSnapshot original,
        long maximumFileBytes)
    {
        ThrowIfReparsePoint(path);
        ThrowIfSourceChanged(path, original);
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The retained JSONL path has no parent directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.retention-{Guid.NewGuid():N}.tmp");
        byte[]? buffer = null;
        try
        {
            using (var source = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       bufferSize: ReadBufferBytes,
                       FileOptions.SequentialScan))
            using (var destination = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1,
                       FileOptions.WriteThrough))
            {
                long suffixStart = source.Length - maximumFileBytes;
                source.Position = suffixStart - 1;
                if (source.ReadByte() != (byte)'\n')
                {
                    source.Position = suffixStart;
                    while (true)
                    {
                        int value = source.ReadByte();
                        if (value < 0)
                        {
                            suffixStart = source.Length;
                            break;
                        }

                        if (value == (byte)'\n')
                        {
                            suffixStart = source.Position;
                            break;
                        }
                    }
                }

                source.Position = suffixStart;
                buffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
                while (source.Position < source.Length)
                {
                    int read = source.Read(
                        buffer,
                        0,
                        (int)Math.Min(
                            buffer.Length,
                            source.Length - source.Position));
                    if (read == 0)
                    {
                        break;
                    }

                    destination.Write(buffer, 0, read);
                }

                destination.Flush(flushToDisk: true);
            }

            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = null;
            }

            ThrowIfSourceChanged(path, original);
            try
            {
                File.Replace(
                    temporaryPath,
                    path,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                ThrowIfSourceChanged(path, original);
                File.Move(temporaryPath, path, overwrite: true);
            }

            File.SetLastWriteTimeUtc(path, original.LastWriteTimeUtc);
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool TryReadRootTimestamp(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> timestampPropertyNameUtf8,
        out DateTimeOffset timestampUtc)
    {
        timestampUtc = default;
        try
        {
            var reader = new Utf8JsonReader(
                line,
                isFinalBlock: false,
                state: default);
            if (!reader.Read()
                || reader.TokenType != JsonTokenType.StartObject
                || reader.CurrentDepth != 0)
            {
                return false;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName
                    && reader.CurrentDepth == 1
                    && reader.ValueTextEquals(timestampPropertyNameUtf8))
                {
                    if (!reader.Read()
                        || reader.TokenType != JsonTokenType.String
                        || !reader.TryGetDateTimeOffset(out timestampUtc))
                    {
                        timestampUtc = default;
                        return false;
                    }

                    timestampUtc = timestampUtc.ToUniversalTime();
                    return true;
                }

                if (reader.TokenType == JsonTokenType.EndObject
                    && reader.CurrentDepth == 0)
                {
                    return false;
                }
            }
        }
        catch (JsonException)
        {
            // Invalid JSON or UTF-8 before the root timestamp is fail closed.
        }
        catch (DecoderFallbackException)
        {
            // Keep this explicit even though Utf8JsonReader normally reports
            // malformed UTF-8 as JsonException.
        }

        timestampUtc = default;
        return false;
    }

    private static void TrimFileToMaximumBytes(
        string path,
        long maximumFileBytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: ReadBufferBytes,
            FileOptions.None);
        if (stream.Length <= maximumFileBytes)
        {
            return;
        }

        long originalLength = stream.Length;
        long suffixStart = originalLength - maximumFileBytes;
        stream.Position = suffixStart - 1;
        if (stream.ReadByte() != (byte)'\n')
        {
            stream.Position = suffixStart;
            while (true)
            {
                int value = stream.ReadByte();
                if (value < 0)
                {
                    stream.SetLength(0);
                    stream.Flush(flushToDisk: true);
                    return;
                }

                if (value == (byte)'\n')
                {
                    suffixStart = stream.Position;
                    break;
                }
            }
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        try
        {
            long readPosition = suffixStart;
            long writePosition = 0;
            while (readPosition < originalLength)
            {
                stream.Position = readPosition;
                int read = stream.Read(
                    buffer,
                    0,
                    (int)Math.Min(buffer.Length, originalLength - readPosition));
                if (read == 0)
                {
                    break;
                }

                stream.Position = writePosition;
                stream.Write(buffer, 0, read);
                readPosition += read;
                writePosition += read;
            }

            stream.SetLength(writePosition);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static long AddSaturating(long left, int right)
    {
        if (left > long.MaxValue - right)
        {
            return long.MaxValue;
        }

        return left + right;
    }

    private static JsonlRetentionFileState IncludeTimestamp(
        JsonlRetentionFileState? state,
        DateTimeOffset timestampUtc)
    {
        timestampUtc = timestampUtc.ToUniversalTime();
        return state is null
            ? new JsonlRetentionFileState(timestampUtc, timestampUtc)
            : new JsonlRetentionFileState(
                timestampUtc < state.OldestTimestampUtc
                    ? timestampUtc
                    : state.OldestTimestampUtc,
                timestampUtc > state.NewestTimestampUtc
                    ? timestampUtc
                    : state.NewestTimestampUtc);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static FileSnapshot CaptureSnapshot(string path)
    {
        var file = new FileInfo(path);
        return new FileSnapshot(file.Length, file.LastWriteTimeUtc);
    }

    private static void ThrowIfSourceChanged(
        string path,
        FileSnapshot expected)
    {
        if (!File.Exists(path) || IsReparsePoint(path))
        {
            throw new IOException(
                "The retained JSONL source changed during maintenance.");
        }

        FileSnapshot current = CaptureSnapshot(path);
        if (current.Length != expected.Length
            || current.LastWriteTimeUtc != expected.LastWriteTimeUtc)
        {
            throw new IOException(
                "The retained JSONL source changed during maintenance.");
        }
    }

    private sealed record FileInspection(
        JsonlRetentionFileState? State,
        bool RequiresRewrite);

    private sealed record FileSnapshot(
        long Length,
        DateTime LastWriteTimeUtc);
}
