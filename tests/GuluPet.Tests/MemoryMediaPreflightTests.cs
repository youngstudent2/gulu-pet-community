using System.Security.Cryptography;
using System.Text.Json;
using GuluPet.Memories;
using GuluPet.Platform;
using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class MemoryMediaPreflightTests
{
    public static void RunAll()
    {
        Run(
            nameof(PackageHashProgressTracksBytes),
            PackageHashProgressTracksBytes);
        Run(
            nameof(PreflightProgressIsMonotonicAndIncludesDecoder),
            PreflightProgressIsMonotonicAndIncludesDecoder);
        Run(
            nameof(IntegrityFailureNeverReportsDecoderOrCompletion),
            IntegrityFailureNeverReportsDecoderOrCompletion);
        Run(
            nameof(DecoderFailureNeverReportsCompletion),
            DecoderFailureNeverReportsCompletion);
        Run(
            nameof(CancellationStopsBeforeDecoderAndCompletion),
            CancellationStopsBeforeDecoderAndCompletion);
    }

    private static void PackageHashProgressTracksBytes()
    {
        using var fixture = new TemporaryPackage(400_013);
        var bytesHashed = new List<long>();

        fixture.Package.ValidateFileAsync(
                fixture.Sources[0],
                CancellationToken.None,
                new InlineProgress<long>(bytesHashed.Add))
            .GetAwaiter()
            .GetResult();

        BehaviorTestCheck.Equal(0L, bytesHashed[0]);
        BehaviorTestCheck.Equal(fixture.Sizes[0], bytesHashed[^1]);
        BehaviorTestCheck.True(
            bytesHashed.Count >= 5,
            "Expected multiple reports from incremental file reads.");
        BehaviorTestCheck.True(
            bytesHashed.Zip(
                    bytesHashed.Skip(1),
                    static (previous, current) => current >= previous)
                .All(static monotonic => monotonic));
        BehaviorTestCheck.True(
            bytesHashed.Any(value =>
                value > 0 && value < fixture.Sizes[0]));

        // Existing no-progress callers remain source-compatible.
        fixture.Package.ValidateFileAsync(
                fixture.Sources[0],
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static void PreflightProgressIsMonotonicAndIncludesDecoder()
    {
        using var fixture = new TemporaryPackage(300_001, 180_007);
        var updates = new List<MemoryMediaPreflightProgress>();
        var decoderCalls = 0;
        var preflight = new WpfMemoryMediaPreflight(
            fixture.Package,
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                decoderCalls++;
                MemoryVideoDimensions dimensions = decoderCalls == 1
                    ? new MemoryVideoDimensions(720, 1_280)
                    : new MemoryVideoDimensions(1_280, 720);
                return Task.FromResult(
                    MemoryMediaPreflightResult.SuccessWithDimensions(
                        [dimensions]));
            });

        MemoryMediaPreflightResult result = preflight.ValidateAsync(
                fixture.Sources,
                new InlineProgress<MemoryMediaPreflightProgress>(updates.Add),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        BehaviorTestCheck.True(result.Succeeded);
        BehaviorTestCheck.Equal(2, decoderCalls);
        IReadOnlyList<MemoryVideoDimensions> dimensions =
            BehaviorTestCheck.NotNull(result.VideoDimensions);
        BehaviorTestCheck.Equal(2, dimensions.Count);
        BehaviorTestCheck.Equal(
            new MemoryVideoDimensions(720, 1_280),
            dimensions[0]);
        BehaviorTestCheck.Equal(
            new MemoryVideoDimensions(1_280, 720),
            dimensions[1]);
        BehaviorTestCheck.True(updates.Count > 8);
        BehaviorTestCheck.Close(0, updates[0].Fraction);
        BehaviorTestCheck.Equal(
            MemoryMediaPreflightStage.PackageIntegrity,
            updates[0].Stage);
        BehaviorTestCheck.Equal(1, updates[0].SourceNumber);
        BehaviorTestCheck.True(
            updates.Zip(
                    updates.Skip(1),
                    static (previous, current) =>
                        current.Fraction >= previous.Fraction)
                .All(static monotonic => monotonic),
            "Preflight fractions must never move backwards.");
        BehaviorTestCheck.True(
            updates.Any(update =>
                update.Stage == MemoryMediaPreflightStage.PackageIntegrity
                && update.SourceNumber == 1
                && update.Fraction > 0
                && update.Fraction < 0.425));
        BehaviorTestCheck.True(
            updates.Any(update =>
                update.Stage == MemoryMediaPreflightStage.Decoder
                && update.SourceNumber == 1));
        BehaviorTestCheck.True(
            updates.Any(update =>
                update.Stage == MemoryMediaPreflightStage.Decoder
                && update.SourceNumber == 2
                && update.Fraction < 1));
        BehaviorTestCheck.Equal(
            new MemoryMediaPreflightProgress(
                1,
                MemoryMediaPreflightStage.Completed,
                2,
                2),
            updates[^1]);
    }

    private static void IntegrityFailureNeverReportsDecoderOrCompletion()
    {
        using var fixture = new TemporaryPackage(
            corruptFirstHash: true,
            270_019);
        var updates = new List<MemoryMediaPreflightProgress>();
        var decoderCalls = 0;
        var preflight = new WpfMemoryMediaPreflight(
            fixture.Package,
            (_, _) =>
            {
                decoderCalls++;
                return Task.FromResult(MemoryMediaPreflightResult.Success);
            });

        MemoryMediaPreflightResult result = preflight.ValidateAsync(
                fixture.Sources,
                new InlineProgress<MemoryMediaPreflightProgress>(updates.Add),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        BehaviorTestCheck.False(result.Succeeded);
        BehaviorTestCheck.Equal("package-integrity", result.ErrorCategory);
        BehaviorTestCheck.Equal(0, decoderCalls);
        BehaviorTestCheck.False(
            updates.Any(update =>
                update.Stage is MemoryMediaPreflightStage.Decoder
                    or MemoryMediaPreflightStage.Completed));
        BehaviorTestCheck.True(updates.Max(update => update.Fraction) < 1);
    }

    private static void DecoderFailureNeverReportsCompletion()
    {
        using var fixture = new TemporaryPackage(270_019);
        var updates = new List<MemoryMediaPreflightProgress>();
        var preflight = new WpfMemoryMediaPreflight(
            fixture.Package,
            (_, _) => Task.FromResult(
                new MemoryMediaPreflightResult(
                    false,
                    "decoder-failed",
                    "safe failure")));

        MemoryMediaPreflightResult result = preflight.ValidateAsync(
                fixture.Sources,
                new InlineProgress<MemoryMediaPreflightProgress>(updates.Add),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        BehaviorTestCheck.False(result.Succeeded);
        BehaviorTestCheck.Equal("decoder-failed", result.ErrorCategory);
        BehaviorTestCheck.True(
            updates.Any(update =>
                update.Stage == MemoryMediaPreflightStage.Decoder));
        BehaviorTestCheck.False(
            updates.Any(update =>
                update.Stage == MemoryMediaPreflightStage.Completed));
        BehaviorTestCheck.True(updates.Max(update => update.Fraction) < 1);
    }

    private static void CancellationStopsBeforeDecoderAndCompletion()
    {
        using var fixture = new TemporaryPackage(400_013);
        using var cancellation = new CancellationTokenSource();
        var updates = new List<MemoryMediaPreflightProgress>();
        var decoderCalls = 0;
        var preflight = new WpfMemoryMediaPreflight(
            fixture.Package,
            (_, _) =>
            {
                decoderCalls++;
                return Task.FromResult(MemoryMediaPreflightResult.Success);
            });
        var progress = new InlineProgress<MemoryMediaPreflightProgress>(
            update =>
            {
                updates.Add(update);
                if (update.Stage
                        == MemoryMediaPreflightStage.PackageIntegrity
                    && update.Fraction > 0)
                {
                    cancellation.Cancel();
                }
            });

        BehaviorTestCheck.Throws<OperationCanceledException>(
            () => preflight.ValidateAsync(
                    fixture.Sources,
                    progress,
                    cancellation.Token)
                .GetAwaiter()
                .GetResult());

        BehaviorTestCheck.Equal(0, decoderCalls);
        BehaviorTestCheck.False(
            updates.Any(update =>
                update.Stage is MemoryMediaPreflightStage.Decoder
                    or MemoryMediaPreflightStage.Completed));
        BehaviorTestCheck.True(updates.Max(update => update.Fraction) < 1);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(MemoryMediaPreflightTests)}.{name}");
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TemporaryPackage : IDisposable
    {
        internal TemporaryPackage(
            params int[] sizes)
            : this(corruptFirstHash: false, sizes)
        {
        }

        internal TemporaryPackage(
            bool corruptFirstHash,
            params int[] sizes)
        {
            if (sizes.Length == 0)
            {
                throw new ArgumentException(
                    "At least one source is required.",
                    nameof(sizes));
            }

            RootDirectory = Path.Combine(
                Path.GetTempPath(),
                $"GuluPetMemoryPreflightTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootDirectory);
            Sizes = sizes.Select(static size => (long)size).ToArray();
            string[] fileNames = sizes
                .Select((_, index) => $"memory-01-{index + 1:00}.mp4")
                .ToArray();
            var segments = new List<object>();
            Sources = new Uri[sizes.Length];
            for (var index = 0; index < sizes.Length; index++)
            {
                byte[] content = new byte[sizes[index]];
                new Random(1_000 + index).NextBytes(content);
                string path = Path.Combine(RootDirectory, fileNames[index]);
                File.WriteAllBytes(path, content);
                string hash = Convert.ToHexString(
                    SHA256.HashData(content));
                if (corruptFirstHash && index == 0)
                {
                    hash = new string('0', 64);
                }

                segments.Add(
                    new
                    {
                        file = fileNames[index],
                        sizeBytes = sizes[index],
                        sha256 = hash,
                    });
                Sources[index] = new Uri(path, UriKind.Absolute);
            }

            var catalog = new MemoryCatalog(
            [
                new MemoryDefinition(
                    "memory-01",
                    1,
                    10,
                    fileNames),
            ]);
            string manifest = JsonSerializer.Serialize(
                new
                {
                    schema = "gulu-memory-video-pack-v2",
                    memories = new[]
                    {
                        new
                        {
                            id = "memory-01",
                            segments,
                        },
                    },
                });
            File.WriteAllText(
                Path.Combine(RootDirectory, "manifest.json"),
                manifest);
            Package = MemoryMediaPackageIndex.Load(
                catalog,
                RootDirectory);
        }

        internal string RootDirectory { get; }

        internal MemoryMediaPackageIndex Package { get; }

        internal Uri[] Sources { get; }

        internal long[] Sizes { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
    }
}
