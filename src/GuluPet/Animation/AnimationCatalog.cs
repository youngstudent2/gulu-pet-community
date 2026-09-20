using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuluPet.Animation;

public sealed class AnimationCatalog : IDisposable
{
    public const int DefaultFrameCacheCapacity = 160;

    private const string SourceApprovalSchema =
        "gulu-runtime-source-approval-provenance-v1";
    private const string ApprovedForPersonalRuntime =
        "approved_for_personal_runtime";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly IReadOnlyDictionary<string, AnimationClip> _clips;
    private readonly AnimationFrameCache _frameCache;
    private bool _disposed;

    private AnimationCatalog(
        IReadOnlyDictionary<string, AnimationClip> clips,
        AnimationFrameCache frameCache)
    {
        _clips = clips;
        _frameCache = frameCache;
    }

    public IEnumerable<string> Names => _clips.Keys;

    public AnimationClip this[string name]
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _clips[name];
        }
    }

    public bool TryGet(string name, out AnimationClip? clip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _clips.TryGetValue(name, out clip);
    }

    internal int CachedFrameCount => _frameCache.Count;

    internal long DecodedFrameCount => _frameCache.DecodedFrameCount;

    internal int FrameCacheCapacity => _frameCache.Capacity;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _frameCache.Dispose();
    }

    public static async Task<AnimationCatalog> LoadAsync(
        string rootDirectory,
        int decodePixelWidth = 480,
        CancellationToken cancellationToken = default,
        int frameCacheCapacity = DefaultFrameCacheCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(decodePixelWidth);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCacheCapacity, 1);

        if (!Directory.Exists(rootDirectory))
        {
            throw new DirectoryNotFoundException($"Animation root not found: {rootDirectory}");
        }

        var clips = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
        var frameCache = new AnimationFrameCache(
            frameCacheCapacity,
            decodePixelWidth);

        foreach (var clipDirectory in Directory.EnumerateDirectories(rootDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var definitionPath = Path.Combine(clipDirectory, "clip.json");
            if (!File.Exists(definitionPath))
            {
                continue;
            }

            await using var definitionStream = File.OpenRead(definitionPath);
            var definition = await JsonSerializer.DeserializeAsync<AnimationClipDefinition>(
                definitionStream,
                JsonOptions,
                cancellationToken);

            if (definition is null || string.IsNullOrWhiteSpace(definition.Name))
            {
                throw new InvalidDataException($"Invalid animation definition: {definitionPath}");
            }

            if (definition.FrameCount < 1)
            {
                throw new InvalidDataException(
                    $"Animation '{definition.Name}' must declare a positive frameCount.");
            }

            ValidateSourceApproval(definition, definitionPath);

            string webpPath = Path.Combine(clipDirectory, "animation.webp");
            if (!File.Exists(webpPath))
            {
                throw new InvalidDataException(
                    $"Animation '{definition.Name}' does not contain animation.webp.");
            }

            if (Directory.EnumerateFiles(
                    clipDirectory,
                    "*.png",
                    SearchOption.TopDirectoryOnly).Any())
            {
                throw new InvalidDataException(
                    $"Animation '{definition.Name}' still contains runtime PNG frames.");
            }

            var frames = new AnimatedWebpFrameList(
                webpPath,
                definition.FrameCount,
                frameCache);

            if (!clips.TryAdd(definition.Name, new AnimationClip(definition, frames)))
            {
                throw new InvalidDataException($"Duplicate animation name: {definition.Name}");
            }
        }

        if (clips.Count == 0)
        {
            throw new InvalidDataException($"No animation clips were found under: {rootDirectory}");
        }

        return new AnimationCatalog(clips, frameCache);
    }

    private static void ValidateSourceApproval(
        AnimationClipDefinition definition,
        string definitionPath)
    {
        AnimationSourceApprovalProvenance? approval = definition.SourceApproval;
        if (approval is null)
        {
            return;
        }

        string owner = $"Animation '{definition.Name}' sourceApproval";
        if (!string.Equals(
                approval.Schema,
                SourceApprovalSchema,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{owner} schema must be '{SourceApprovalSchema}' in " +
                $"'{definitionPath}'.");
        }

        if (!string.Equals(
                approval.ApprovalDecision,
                ApprovedForPersonalRuntime,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{owner} approvalDecision must be " +
                $"'{ApprovedForPersonalRuntime}' in '{definitionPath}'.");
        }

        ValidateRequiredText(approval.ApprovalId, "approvalId", owner);
        ValidateRequiredPath(approval.ApprovalRecord, "approvalRecord", owner);
        ValidateSha256(
            approval.ApprovalRecordSha256,
            "approvalRecordSha256",
            owner);
        ValidateRequiredPath(
            approval.CandidateManifest,
            "candidateManifest",
            owner);
        ValidateSha256(
            approval.CandidateManifestSha256,
            "candidateManifestSha256",
            owner);
        ValidateRequiredPath(approval.SourceReview, "sourceReview", owner);
        ValidateSha256(
            approval.SourceReviewSha256,
            "sourceReviewSha256",
            owner);
        ValidateRequiredPath(approval.FrameHashes, "frameHashes", owner);
        ValidateSha256(
            approval.FrameHashesSha256,
            "frameHashesSha256",
            owner);
        ValidateSha256(
            approval.PixelSequenceSha256,
            "pixelSequenceSha256",
            owner);
        ValidateRequiredPath(
            approval.CandidateClipJson,
            "candidateClipJson",
            owner);
        ValidateSha256(
            approval.CandidateClipJsonSha256,
            "candidateClipJsonSha256",
            owner);
    }

    private static void ValidateRequiredPath(
        string? value,
        string field,
        string owner) =>
        ValidateRequiredText(value, field, owner);

    private static void ValidateRequiredText(
        string? value,
        string field,
        string owner)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new InvalidDataException(
                $"{owner} {field} must be non-empty, trimmed text.");
        }
    }

    private static void ValidateSha256(
        string? value,
        string field,
        string owner)
    {
        if (value is null
            || value.Length != 64
            || value.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'A' and <= 'F')))
        {
            throw new InvalidDataException(
                $"{owner} {field} must be exactly 64 uppercase hexadecimal " +
                "SHA-256 characters.");
        }
    }
}
