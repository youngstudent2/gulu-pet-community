using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuluPet.Accessories;
using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class EyeAccessoryContentTests
{
    private static readonly JsonSerializerOptions FixtureJsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static void RunAll()
    {
        Run(
            nameof(CommunityRuntimeOmitsOptionalAccessoryPack),
            CommunityRuntimeOmitsOptionalAccessoryPack);
        Run(
            nameof(LegacyV1ContentNormalizesToFront),
            LegacyV1ContentNormalizesToFront);
        Run(
            nameof(V2FiveVariantManifestLoads),
            V2FiveVariantManifestLoads);
        Run(
            nameof(V2MissingFrontFailsClosed),
            V2MissingFrontFailsClosed);
        Run(
            nameof(V2ImageAndVariantsTogetherFailClosed),
            V2ImageAndVariantsTogetherFailClosed);
        Run(
            nameof(UnknownVariantNameFailsClosed),
            UnknownVariantNameFailsClosed);
        Run(
            nameof(PoseV2VisibleFrameRequiresViewState),
            PoseV2VisibleFrameRequiresViewState);
        Run(
            nameof(UnknownViewStateFailsClosed),
            UnknownViewStateFailsClosed);
        Run(
            nameof(HiddenFrameViewStateFailsClosed),
            HiddenFrameViewStateFailsClosed);
        Run(
            nameof(IncorrectImageShaFailsClosed),
            IncorrectImageShaFailsClosed);
        Run(
            nameof(IncorrectPoseShaFailsClosed),
            IncorrectPoseShaFailsClosed);
        Run(
            nameof(MissingImageFailsClosed),
            MissingImageFailsClosed);
        Run(
            nameof(MissingPoseFailsClosed),
            MissingPoseFailsClosed);
        Run(
            nameof(UnknownJsonMemberFailsClosed),
            UnknownJsonMemberFailsClosed);
        Run(
            nameof(DuplicateJsonMemberFailsClosed),
            DuplicateJsonMemberFailsClosed);
        Run(
            nameof(HiddenFramePoseDataFailsClosed),
            HiddenFramePoseDataFailsClosed);
        Run(
            nameof(OutOfRangeRotationFailsClosed),
            OutOfRangeRotationFailsClosed);
    }

    private static void CommunityRuntimeOmitsOptionalAccessoryPack()
    {
        string assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets");
        BehaviorTestCheck.False(
            Directory.Exists(Path.Combine(assetsRoot, "Accessories")));

        ValidatedRuntimeContent content = RuntimeContentContract
            .LoadAndValidateAsync(assetsRoot, decodePixelWidth: 0)
            .GetAwaiter()
            .GetResult();
        try
        {
            BehaviorTestCheck.Null(content.EyeAccessories);
            BehaviorTestCheck.Null(content.EyeAccessoryPoses);
        }
        finally
        {
            content.Animations.Dispose();
        }
    }

    private static void LegacyV1ContentNormalizesToFront()
    {
        using var fixture = AccessoryFixture.Create(
            EyeAccessoryCatalog.LegacySchema,
            EyeAccessoryPoseCatalog.LegacyPoseSchema);

        EyeAccessoryCatalog accessories =
            EyeAccessoryCatalog.Load(fixture.AccessoriesRoot);
        EyeAccessoryDefinition accessory = accessories["accessory-01"];
        BehaviorTestCheck.Equal(1, accessory.Variants.Count);
        BehaviorTestCheck.True(
            accessory.TryGetVariant(
                EyeAccessoryViewState.Front,
                out EyeAccessoryVariantDefinition? front));
        BehaviorTestCheck.Equal(accessory.ImagePath, front!.ImagePath);

        EyeAccessoryPoseFrame frame =
            EyeAccessoryPoseCatalog.Load(fixture.AccessoriesRoot)["clip-000"][0];
        BehaviorTestCheck.Equal(EyeAccessoryViewState.Front, frame.ViewState);
    }

    private static void V2FiveVariantManifestLoads()
    {
        using var fixture = AccessoryFixture.Create();
        JsonObject manifest = ReadObject(fixture.ManifestPath);
        JsonObject accessory = GetFirstManifestAccessory(manifest);
        JsonObject front = GetFirstManifestImage(manifest);
        accessory.Remove("image");
        accessory["variants"] = new JsonObject
        {
            ["front"] = front.DeepClone(),
            ["threeQuarterLeft"] = CreateVariantImage(
                fixture,
                front,
                "images/accessory-01-three-quarter-left.png"),
            ["threeQuarterRight"] = CreateVariantImage(
                fixture,
                front,
                "images/accessory-01-three-quarter-right.png"),
            ["sideLeft"] = CreateVariantImage(
                fixture,
                front,
                "images/accessory-01-side-left.png"),
            ["sideRight"] = CreateVariantImage(
                fixture,
                front,
                "images/accessory-01-side-right.png"),
        };
        WriteObject(fixture.ManifestPath, manifest);

        EyeAccessoryDefinition loaded =
            EyeAccessoryCatalog.Load(fixture.AccessoriesRoot)["accessory-01"];
        BehaviorTestCheck.Equal(5, loaded.Variants.Count);
        foreach (EyeAccessoryViewState state in Enum.GetValues<
                     EyeAccessoryViewState>())
        {
            BehaviorTestCheck.True(loaded.TryGetVariant(state, out _));
        }
    }

    private static void V2MissingFrontFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        JsonObject manifest = ReadObject(fixture.ManifestPath);
        JsonObject accessory = GetFirstManifestAccessory(manifest);
        JsonObject image = GetFirstManifestImage(manifest);
        accessory.Remove("image");
        accessory["variants"] = new JsonObject
        {
            ["sideLeft"] = CreateVariantImage(
                fixture,
                image,
                "images/accessory-01-side-left.png"),
        };
        WriteObject(fixture.ManifestPath, manifest);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void V2ImageAndVariantsTogetherFailClosed()
    {
        using var fixture = AccessoryFixture.Create();
        JsonObject manifest = ReadObject(fixture.ManifestPath);
        JsonObject accessory = GetFirstManifestAccessory(manifest);
        accessory["variants"] = new JsonObject
        {
            ["front"] = GetFirstManifestImage(manifest).DeepClone(),
        };
        WriteObject(fixture.ManifestPath, manifest);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void UnknownVariantNameFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        JsonObject manifest = ReadObject(fixture.ManifestPath);
        JsonObject accessory = GetFirstManifestAccessory(manifest);
        JsonObject image = GetFirstManifestImage(manifest);
        accessory.Remove("image");
        accessory["variants"] = new JsonObject
        {
            ["front"] = image.DeepClone(),
            ["profile"] = image.DeepClone(),
        };
        WriteObject(fixture.ManifestPath, manifest);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void PoseV2VisibleFrameRequiresViewState()
    {
        using var fixture = AccessoryFixture.Create();
        JsonObject pose = ReadObject(fixture.FirstPosePath);
        GetFirstPoseFrame(pose).Remove("viewState");
        WriteFirstPoseAndRefreshHash(fixture, pose);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryPoseCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void UnknownViewStateFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        JsonObject pose = ReadObject(fixture.FirstPosePath);
        GetFirstPoseFrame(pose)["viewState"] = "profile";
        WriteFirstPoseAndRefreshHash(fixture, pose);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryPoseCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void HiddenFrameViewStateFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        JsonObject pose = CreateCanonicalHiddenFirstPose(fixture);
        GetFirstPoseFrame(pose)["viewState"] = "sideLeft";
        WriteFirstPoseAndRefreshHash(fixture, pose);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryPoseCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void IncorrectImageShaFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        JsonObject manifest = ReadObject(fixture.ManifestPath);
        JsonObject image = GetFirstManifestImage(manifest);
        image["sha256"] = new string('0', 64);
        WriteObject(fixture.ManifestPath, manifest);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void IncorrectPoseShaFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        JsonObject index = ReadObject(fixture.PoseIndexPath);
        JsonObject firstClip = GetRequiredArray(index, "clips")[0]
            as JsonObject
            ?? throw new InvalidDataException(
                "Fixture pose index first clip is unavailable.");
        firstClip["poseSha256"] = new string('0', 64);
        WriteObject(fixture.PoseIndexPath, index);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryPoseCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void MissingImageFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        File.Delete(fixture.FirstImagePath);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void MissingPoseFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        File.Delete(fixture.FirstPosePath);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryPoseCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void UnknownJsonMemberFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        JsonObject manifest = ReadObject(fixture.ManifestPath);
        manifest["unexpectedField"] = true;
        WriteObject(fixture.ManifestPath, manifest);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void DuplicateJsonMemberFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        string json = File.ReadAllText(fixture.ManifestPath);
        string marker =
            $"\"accessoryCount\": {EyeAccessoryCatalog.RequiredAccessoryCount},";
        int markerIndex = json.IndexOf(marker, StringComparison.Ordinal);
        BehaviorTestCheck.True(markerIndex >= 0);
        json = json.Insert(
            markerIndex + marker.Length,
            $"{Environment.NewLine}  {marker}");
        File.WriteAllText(fixture.ManifestPath, json);

        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => EyeAccessoryCatalog.Load(fixture.AccessoriesRoot));
    }

    private static void HiddenFramePoseDataFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        foreach (Action<JsonObject> corruptFrame in new Action<JsonObject>[]
                 {
                     static frame =>
                     {
                         frame["leftEye"] = new JsonObject
                         {
                             ["x"] = 100.0,
                             ["y"] = 120.0,
                         };
                         frame["rightEye"] = new JsonObject
                         {
                             ["x"] = 220.0,
                             ["y"] = 120.0,
                         };
                     },
                     static frame => frame["headWidth"] = 1.0,
                     static frame => frame["rotationDegrees"] = 1.0,
                 })
        {
            JsonObject pose = CreateCanonicalHiddenFirstPose(fixture);
            JsonObject frame = GetFirstPoseFrame(pose);
            corruptFrame(frame);
            WriteFirstPoseAndRefreshHash(fixture, pose);

            _ = BehaviorTestCheck.Throws<InvalidDataException>(
                () => EyeAccessoryPoseCatalog.Load(fixture.AccessoriesRoot));
        }
    }

    private static void OutOfRangeRotationFailsClosed()
    {
        using var fixture = AccessoryFixture.Create();
        foreach (double rotation in new[] { 181.0, -181.0, 1.0e308 })
        {
            JsonObject pose = ReadObject(fixture.FirstPosePath);
            GetFirstPoseFrame(pose)["rotationDegrees"] = rotation;
            WriteFirstPoseAndRefreshHash(fixture, pose);

            _ = BehaviorTestCheck.Throws<InvalidDataException>(
                () => EyeAccessoryPoseCatalog.Load(fixture.AccessoriesRoot));
        }
    }

    private static JsonObject CreateCanonicalHiddenFirstPose(
        AccessoryFixture fixture)
    {
        JsonObject pose = ReadObject(fixture.FirstPosePath);
        JsonObject frame = GetFirstPoseFrame(pose);
        frame["visible"] = false;
        frame["leftEye"] = null;
        frame["rightEye"] = null;
        frame["headWidth"] = 0.0;
        frame["rotationDegrees"] = 0.0;
        if (frame.ContainsKey("viewState"))
        {
            frame["viewState"] = null;
        }
        frame["hiddenReason"] = "test-hidden";
        return pose;
    }

    private static JsonObject GetFirstPoseFrame(JsonObject pose) =>
        GetRequiredArray(pose, "frames")[0] as JsonObject
        ?? throw new InvalidDataException(
            "Fixture pose first frame is unavailable.");

    private static void WriteFirstPoseAndRefreshHash(
        AccessoryFixture fixture,
        JsonObject pose)
    {
        WriteObject(fixture.FirstPosePath, pose);
        JsonObject index = ReadObject(fixture.PoseIndexPath);
        JsonObject firstClip = GetRequiredArray(index, "clips")[0]
            as JsonObject
            ?? throw new InvalidDataException(
                "Fixture pose index first clip is unavailable.");
        firstClip["poseSha256"] = ComputeSha256(fixture.FirstPosePath);
        WriteObject(fixture.PoseIndexPath, index);
    }

    private static void AssertFinitePoint(EyeAccessoryPoint? point)
    {
        if (point is not { } value)
        {
            return;
        }

        BehaviorTestCheck.True(double.IsFinite(value.X));
        BehaviorTestCheck.True(double.IsFinite(value.Y));
        BehaviorTestCheck.True(value.X >= 0);
        BehaviorTestCheck.True(value.Y >= 0);
        BehaviorTestCheck.True(
            value.X <= EyeAccessoryPoseCatalog.RequiredCanvasSize);
        BehaviorTestCheck.True(
            value.Y <= EyeAccessoryPoseCatalog.RequiredCanvasSize);
    }

    private static JsonObject GetFirstManifestImage(JsonObject manifest)
    {
        JsonObject firstAccessory = GetFirstManifestAccessory(manifest);
        return firstAccessory["image"] as JsonObject
            ?? throw new InvalidDataException(
                "Fixture manifest first accessory image is unavailable.");
    }

    private static JsonObject GetFirstManifestAccessory(JsonObject manifest) =>
        GetRequiredArray(manifest, "accessories")[0] as JsonObject
        ?? throw new InvalidDataException(
            "Fixture manifest first accessory is unavailable.");

    private static JsonObject CreateVariantImage(
        AccessoryFixture fixture,
        JsonObject source,
        string file)
    {
        string path = Path.Combine(
            fixture.AccessoriesRoot,
            file.Replace('/', Path.DirectorySeparatorChar));
        WritePng(path);
        var image = (JsonObject)source.DeepClone();
        image["file"] = file;
        image["sha256"] = ComputeSha256(path);
        image["sizeBytes"] = new FileInfo(path).Length;
        return image;
    }

    private static JsonArray GetRequiredArray(JsonObject root, string name) =>
        root[name] as JsonArray
        ?? throw new InvalidDataException(
            $"Fixture JSON array '{name}' is unavailable.");

    private static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidDataException(
            $"Fixture JSON '{path}' is not an object.");

    private static void WriteObject(string path, JsonObject value) =>
        File.WriteAllText(path, value.ToJsonString(FixtureJsonOptions));

    private static string ComputeSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void WritePng(string path)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Fixture PNG path has no directory."));
        byte[] pixels =
        [
            0, 0, 0, 0,
            40, 80, 220, 255,
            120, 180, 255, 192,
            220, 230, 240, 255,
        ];
        BitmapSource bitmap = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride: 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(EyeAccessoryContentTests)}.{name}");
    }

    private sealed class AccessoryFixture : IDisposable
    {
        private AccessoryFixture(
            string root,
            string manifestSchema,
            string poseSchema)
        {
            Root = root;
            ManifestSchema = manifestSchema;
            PoseSchema = poseSchema;
            AssetsRoot = Path.Combine(root, "Assets");
            AccessoriesRoot = Path.Combine(AssetsRoot, "Accessories");
            ManifestPath = Path.Combine(
                AccessoriesRoot,
                EyeAccessoryCatalog.ManifestFileName);
            PoseIndexPath = Path.Combine(
                AccessoriesRoot,
                EyeAccessoryPoseCatalog.IndexFileName);
            FirstImagePath = Path.Combine(
                AccessoriesRoot,
                "images",
                "accessory-01.png");
            FirstPosePath = Path.Combine(
                AccessoriesRoot,
                "poses",
                "clip-000.json");
        }

        public string Root { get; }

        private string ManifestSchema { get; }

        private string PoseSchema { get; }

        public string AssetsRoot { get; }

        public string AccessoriesRoot { get; }

        public string ManifestPath { get; }

        public string PoseIndexPath { get; }

        public string FirstImagePath { get; }

        public string FirstPosePath { get; }

        public static AccessoryFixture Create(
            string? manifestSchema = null,
            string? poseSchema = null)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                $"GuluPet.EyeAccessoryContent.{Guid.NewGuid():N}");
            var fixture = new AccessoryFixture(
                root,
                manifestSchema ?? EyeAccessoryCatalog.CurrentSchema,
                poseSchema ?? EyeAccessoryPoseCatalog.CurrentPoseSchema);
            try
            {
                Directory.CreateDirectory(fixture.AccessoriesRoot);
                fixture.WriteManifest();
                fixture.WritePoseCatalog();
                _ = EyeAccessoryCatalog.Load(fixture.AccessoriesRoot);
                _ = EyeAccessoryPoseCatalog.Load(fixture.AccessoriesRoot);
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private void WriteManifest()
        {
            var accessories = new List<object>(
                EyeAccessoryCatalog.RequiredAccessoryCount);
            for (var index = 1;
                 index <= EyeAccessoryCatalog.RequiredAccessoryCount;
                 index++)
            {
                string file = $"images/accessory-{index:00}.png";
                string path = Path.Combine(
                    AccessoriesRoot,
                    file.Replace('/', Path.DirectorySeparatorChar));
                WritePng(path);
                accessories.Add(new
                {
                    id = $"accessory-{index:00}",
                    displayName = $"测试饰品 {index}",
                    selectionOrder = index,
                    image = new
                    {
                        file,
                        width = 2,
                        height = 2,
                        leftEye = new { x = 0.5, y = 1.0 },
                        rightEye = new { x = 1.5, y = 1.0 },
                        headWidthPixels = 2.0,
                        sha256 = ComputeSha256(path),
                        sizeBytes = new FileInfo(path).Length,
                    },
                });
            }

            File.WriteAllText(
                ManifestPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schema = ManifestSchema,
                        contentVersion = "test-v1",
                        accessoryCount =
                            EyeAccessoryCatalog.RequiredAccessoryCount,
                        accessories,
                    },
                    FixtureJsonOptions));
        }

        private void WritePoseCatalog()
        {
            string poseDirectory = Path.Combine(AccessoriesRoot, "poses");
            Directory.CreateDirectory(poseDirectory);
            var clips = new List<object>(
                EyeAccessoryPoseCatalog.RequiredClipCount);
            for (var index = 0;
                 index < EyeAccessoryPoseCatalog.RequiredClipCount;
                 index++)
            {
                string clipId = $"clip-{index:000}";
                string poseFile = $"poses/{clipId}.json";
                string posePath = Path.Combine(
                    AccessoriesRoot,
                    poseFile.Replace('/', Path.DirectorySeparatorChar));
                var frame = new Dictionary<string, object?>
                {
                    ["frame"] = 0,
                    ["visible"] = true,
                    ["leftEye"] = new { x = 100.0, y = 120.0 },
                    ["rightEye"] = new { x = 220.0, y = 120.0 },
                    ["headWidth"] = 240.0,
                    ["rotationDegrees"] = 0.0,
                    ["hiddenReason"] = null,
                };
                if (string.Equals(
                        PoseSchema,
                        EyeAccessoryPoseCatalog.CurrentPoseSchema,
                        StringComparison.Ordinal))
                {
                    frame["viewState"] = "front";
                }

                File.WriteAllText(
                    posePath,
                    JsonSerializer.Serialize(
                        new
                        {
                            schema = PoseSchema,
                            clipId,
                            frameCount = 1,
                            canvasSize =
                                EyeAccessoryPoseCatalog.RequiredCanvasSize,
                            frames = new[] { frame },
                        },
                        FixtureJsonOptions));
                clips.Add(new
                {
                    clipId,
                    frameCount = 1,
                    poseFile,
                    poseSha256 = ComputeSha256(posePath),
                });
            }

            File.WriteAllText(
                PoseIndexPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schema = EyeAccessoryPoseCatalog.CurrentIndexSchema,
                        clips,
                    },
                    FixtureJsonOptions));
        }
    }
}
