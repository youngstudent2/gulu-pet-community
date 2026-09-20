using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuluPet.Runtime;

public enum RuntimeContentMode
{
    Production,
    Preview
}

public sealed record RuntimeContentDescriptor
{
    public int SchemaVersion { get; init; }

    public RuntimeContentMode Mode { get; init; }

    public required string ContentVersion { get; init; }

    public int ExpectedClipCount { get; init; }

    public double FramesPerSecond { get; init; }

    public int FrameSize { get; init; }

    public required string AnimationFormat { get; init; }

    public required string AssetStatus { get; init; }

    public IReadOnlyList<string> KnownIssues { get; init; } = [];

    public required string SourceReviewStatus { get; init; }

    public static RuntimeContentDescriptor CommunityContract { get; } = new()
    {
        SchemaVersion = 1,
        Mode = RuntimeContentMode.Production,
        ContentVersion = "gulu-idle-sample-v1",
        ExpectedClipCount = 1,
        FramesPerSecond = RuntimeContentContract.DefaultFramesPerSecond,
        FrameSize = 320,
        AnimationFormat = RuntimeContentContract.RequiredAnimationFormat,
        AssetStatus = "gulu-authorized-sample",
        KnownIssues = [],
        SourceReviewStatus = "redistributable",
    };

    // Kept as a source-compatible alias for extension projects created from
    // earlier snapshots. It no longer represents a private asset inventory.
    public static RuntimeContentDescriptor ProductionContract =>
        CommunityContract;
}

public static class RuntimeContentDescriptorLoader
{
    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    public static async Task<RuntimeContentDescriptor> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The runtime content descriptor is required.",
                path);
        }

        await using var stream = File.OpenRead(path);
        RuntimeContentDescriptor? descriptor =
            await JsonSerializer.DeserializeAsync<RuntimeContentDescriptor>(
                stream,
                JsonOptions,
                cancellationToken);
        if (descriptor is null)
        {
            throw new InvalidDataException(
                $"Runtime content descriptor is empty: {path}");
        }

        IReadOnlyList<string> violations = FindViolations(descriptor);
        if (violations.Count > 0)
        {
            throw new InvalidDataException(
                "Runtime content descriptor failed:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    violations.Select(static violation => $"- {violation}")));
        }

        return descriptor;
    }

    public static IReadOnlyList<string> FindViolations(
        RuntimeContentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var violations = new List<string>();

        if (descriptor.SchemaVersion != 1)
        {
            violations.Add(
                $"schemaVersion must be 1 but is {descriptor.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.ContentVersion))
        {
            violations.Add("contentVersion is required.");
        }

        if (descriptor.ExpectedClipCount < 1)
        {
            violations.Add("expectedClipCount must be positive.");
        }

        if (descriptor.FramesPerSecond is < 1 or > 120)
        {
            violations.Add("framesPerSecond must be between 1 and 120.");
        }

        if (descriptor.FrameSize is < 32 or > 2048)
        {
            violations.Add("frameSize must be between 32 and 2048 pixels.");
        }

        if (!string.Equals(
                descriptor.AnimationFormat,
                RuntimeContentContract.RequiredAnimationFormat,
                StringComparison.Ordinal))
        {
            violations.Add(
                $"animationFormat must be " +
                $"'{RuntimeContentContract.RequiredAnimationFormat}'.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.AssetStatus))
        {
            violations.Add("assetStatus is required.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.SourceReviewStatus))
        {
            violations.Add("sourceReviewStatus is required.");
        }

        if (descriptor.KnownIssues is null)
        {
            violations.Add("knownIssues must be an array.");
        }

        return violations;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
