using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace GuluPet.Memories;

internal sealed record MemoryMediaPackageSegment(
    string FileName,
    long SizeBytes,
    string Sha256);

/// <summary>
/// Strict index over the versioned memory-video manifest. The catalog and
/// manifest inventories must agree before the first playback is offered, and
/// selected files are size/hash checked immediately before decoder preflight.
/// </summary>
internal sealed class MemoryMediaPackageIndex
{
    private const string ExpectedSchema = "gulu-memory-video-pack-v2";
    private const int HashBufferSize = 128 * 1024;

    private readonly string _videoDirectory;
    private readonly IReadOnlyDictionary<string, MemoryMediaPackageSegment>
        _segments;

    private MemoryMediaPackageIndex(
        string videoDirectory,
        IReadOnlyDictionary<string, MemoryMediaPackageSegment> segments)
    {
        _videoDirectory = videoDirectory;
        _segments = segments;
    }

    internal static MemoryMediaPackageIndex Load(
        MemoryCatalog catalog,
        string videoDirectory)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(videoDirectory);
        string root = Path.GetFullPath(videoDirectory);
        string manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                "Memory video manifest is missing.",
                manifestPath);
        }

        using FileStream stream = File.OpenRead(manifestPath);
        using JsonDocument document = JsonDocument.Parse(
            stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
        JsonElement rootElement = document.RootElement;
        if (rootElement.ValueKind != JsonValueKind.Object
            || !rootElement.TryGetProperty("schema", out JsonElement schema)
            || schema.GetString() != ExpectedSchema
            || !rootElement.TryGetProperty(
                "memories",
                out JsonElement memories)
            || memories.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Memory video manifest has an unsupported schema.");
        }

        var segments = new Dictionary<string, MemoryMediaPackageSegment>(
            StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement memory in memories.EnumerateArray())
        {
            if (memory.ValueKind != JsonValueKind.Object
                || !memory.TryGetProperty(
                    "segments",
                    out JsonElement declaredSegments)
                || declaredSegments.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "Memory video manifest contains an invalid memory entry.");
            }

            foreach (JsonElement segment in declaredSegments.EnumerateArray())
            {
                MemoryMediaPackageSegment parsed = ParseSegment(segment);
                if (!segments.TryAdd(parsed.FileName, parsed))
                {
                    throw new InvalidDataException(
                        $"Memory video '{parsed.FileName}' is declared more " +
                        "than once.");
                }
            }
        }

        string[] expected = catalog.Definitions
            .SelectMany(static memory => memory.VideoFileNames)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] declared = segments.Keys
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!expected.SequenceEqual(
                declared,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Memory catalog and video manifest inventories do not match.");
        }

        return new MemoryMediaPackageIndex(root, segments);
    }

    internal async Task ValidateFileAsync(
        Uri source,
        CancellationToken cancellationToken,
        IProgress<long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        (string fullPath, string relative, MemoryMediaPackageSegment segment) =
            ResolveSegment(source);

        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length != segment.SizeBytes)
        {
            throw new InvalidDataException(
                $"Memory media '{relative}' failed its size check.");
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: HashBufferSize,
            useAsync: true);
        if (stream.Length != segment.SizeBytes)
        {
            throw new InvalidDataException(
                $"Memory media '{relative}' failed its size check.");
        }

        progress?.Report(0);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(HashBufferSize);
        byte[] hash;
        long bytesHashed = 0;
        try
        {
            using IncrementalHash hasher = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            while (true)
            {
                int bytesRead = await stream.ReadAsync(
                        buffer.AsMemory(0, HashBufferSize),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                hasher.AppendData(buffer, 0, bytesRead);
                bytesHashed = checked(bytesHashed + bytesRead);
                progress?.Report(bytesHashed);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (bytesHashed != segment.SizeBytes)
            {
                throw new InvalidDataException(
                    $"Memory media '{relative}' failed its size check.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            hash = hasher.GetHashAndReset();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        string actual = Convert.ToHexString(hash);
        if (!string.Equals(
                actual,
                segment.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Memory media '{relative}' failed its integrity check.");
        }
    }

    internal long GetExpectedSizeBytes(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ResolveSegment(source).Segment.SizeBytes;
    }

    private (
        string FullPath,
        string Relative,
        MemoryMediaPackageSegment Segment) ResolveSegment(Uri source)
    {
        if (!source.IsFile)
        {
            throw new InvalidDataException(
                "Memory media must use a local file URI.");
        }

        string fullPath = Path.GetFullPath(source.LocalPath);
        string relative = Path.GetRelativePath(_videoDirectory, fullPath);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || !string.Equals(
                Path.GetFileName(relative),
                relative,
                StringComparison.Ordinal)
            || !_segments.TryGetValue(
                relative,
                out MemoryMediaPackageSegment? segment)
            || segment is null)
        {
            throw new InvalidDataException(
                "Memory media is outside the verified package.");
        }

        return (fullPath, relative, segment);
    }

    private static MemoryMediaPackageSegment ParseSegment(
        JsonElement segment)
    {
        if (segment.ValueKind != JsonValueKind.Object
            || !segment.TryGetProperty("file", out JsonElement fileElement)
            || !segment.TryGetProperty(
                "sizeBytes",
                out JsonElement sizeElement)
            || !segment.TryGetProperty(
                "sha256",
                out JsonElement hashElement))
        {
            throw new InvalidDataException(
                "Memory video manifest contains an incomplete segment.");
        }

        string? fileName = fileElement.GetString();
        string? sha256 = hashElement.GetString();
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(
                Path.GetFileName(fileName),
                fileName,
                StringComparison.Ordinal)
            || !fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || !sizeElement.TryGetInt64(out long sizeBytes)
            || sizeBytes < 1
            || string.IsNullOrWhiteSpace(sha256)
            || sha256.Length != 64
            || sha256.Any(static character =>
                !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                "Memory video manifest contains invalid segment metadata.");
        }

        return new MemoryMediaPackageSegment(
            fileName,
            sizeBytes,
            sha256);
    }
}
