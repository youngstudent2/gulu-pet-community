using GuluPet.Animation;
using GuluPet.Dialogue;
using GuluPet.Platform;
using GuluPet.Runtime;
using GuluPet.Update;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace GuluPet.Tests;

internal static class RuntimeDataTests
{
    public static void RunAll()
    {
        Run(nameof(AnimationAssetsLoad), AnimationAssetsLoad);
        Run(nameof(RuntimePreviewContractLoads), RuntimePreviewContractLoads);
        Run(
            nameof(LegacyAnimationArbitrationFieldsFailClosed),
            LegacyAnimationArbitrationFieldsFailClosed);
        Run(
            nameof(AnimationSourceApprovalLoadsAndRemainsOptional),
            AnimationSourceApprovalLoadsAndRemainsOptional);
        Run(
            nameof(AnimationSourceApprovalUnknownFieldsFailClosed),
            AnimationSourceApprovalUnknownFieldsFailClosed);
        Run(
            nameof(AnimationSourceApprovalInvalidValuesFailClosed),
            AnimationSourceApprovalInvalidValuesFailClosed);
        Run(nameof(DialogueAssetsLoad), DialogueAssetsLoad);
        Run(
            nameof(LegacyDialogueCooldownFieldFailsClosed),
            LegacyDialogueCooldownFieldFailsClosed);
        Run(nameof(FullscreenPolicyMatchesProductRules), FullscreenPolicyMatchesProductRules);
        Run(nameof(ManualUpdateIsOfflineUntilConfigured), ManualUpdateIsOfflineUntilConfigured);
    }

    private static void AnimationAssetsLoad()
    {
        string assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets");
        RuntimeContentDescriptor descriptor =
            RuntimeContentDescriptorLoader.LoadAsync(
                    Path.Combine(assetsRoot, "runtime-content.json"))
                .GetAwaiter()
                .GetResult();
        string animationsRoot = Path.Combine(assetsRoot, "Animations");
        using var catalog = AnimationCatalog.LoadAsync(
                animationsRoot,
                decodePixelWidth: 0)
            .GetAwaiter()
            .GetResult();

        Equal(descriptor.ExpectedClipCount, catalog.Names.Count());
        BehaviorTestCheck.SequenceEqual(
            ["idle_breathe"],
            catalog.Names.Order(StringComparer.Ordinal));

        foreach (string name in catalog.Names)
        {
            AnimationClip clip = catalog[name];
            Equal(clip.Definition.FrameCount, clip.Frames.Count);
            Equal(name, clip.Definition.Name);
            Equal(descriptor.FramesPerSecond, clip.Definition.Fps);
            True(!clip.Definition.Placeholder);
            Equal(descriptor.AssetStatus, clip.Definition.AssetStatus);
            Equal(121, clip.Definition.FrameCount);
            True(clip.Definition.Loop);
            Equal(0, clip.Definition.KnownIssues.Count);

            string[] runtimeFiles = Directory
                .EnumerateFiles(
                    Path.Combine(animationsRoot, name),
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OrderBy(static fileName => fileName, StringComparer.Ordinal)
                .ToArray()!;
            Equal("animation.webp,clip.json", string.Join(",", runtimeFiles));

            foreach (int sampleIndex in new[]
                     {
                         0,
                         clip.Frames.Count / 2,
                         clip.Frames.Count - 1,
                     })
            {
                BitmapSource frame = clip.Frames[sampleIndex];
                Equal(descriptor.FrameSize, frame.PixelWidth);
                Equal(descriptor.FrameSize, frame.PixelHeight);
                Equal(32, frame.Format.BitsPerPixel);
                True(frame.IsFrozen);
            }
        }

        Equal(RuntimeContentMode.Production, descriptor.Mode);
        Equal(
            RuntimeContentDescriptor.CommunityContract.ExpectedClipCount,
            descriptor.ExpectedClipCount);
        Equal(
            RuntimeContentContract.RequiredAnimationFormat,
            descriptor.AnimationFormat);
    }

    private static void DialogueAssetsLoad()
    {
        DialogueCatalog catalog = DialogueCatalog.LoadAsync(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "Data",
                    "dialogues.json"))
            .GetAwaiter()
            .GetResult();

        Equal(1, catalog.Lines.Count);
        Equal(
            catalog.Lines.Count,
            catalog.Lines
                .Select(static line => line.Id)
                .Distinct(StringComparer.Ordinal)
                .Count());
        True(catalog.Lines.All(static line =>
            !string.IsNullOrWhiteSpace(line.Text)));
        True(catalog.Lines.All(static line =>
            CatVocalization.IsValid(line.Text)));
        True(catalog.Lines.All(static line =>
            CatDialogueAnnotation.IsValid(line.Meaning)));
        True(catalog.Lines.All(static line =>
            line.Type == BubbleType.Dialogue));
        BehaviorTestCheck.SequenceEqual(
            ["new", "familiar", "close", "family"],
            catalog.Lines.Single().RelationshipStages);

        True(!CatVocalization.IsValid("我就在这里。"));
        True(!CatVocalization.IsValid("hello"));
        True(CatVocalization.IsValid("喵~"));
    }

    private static void RuntimePreviewContractLoads()
    {
        ValidatedRuntimeContent content =
            RuntimeContentContract.LoadAndValidateAsync(
                    Path.Combine(AppContext.BaseDirectory, "Assets"),
                    decodePixelWidth: 0,
                    validateEveryFrame: true)
                .GetAwaiter()
                .GetResult();
        try
        {
            RuntimeContentDescriptor descriptor =
                RuntimeContentDescriptor.CommunityContract;
            Equal(descriptor.ExpectedClipCount, content.Animations.Names.Count());
            Equal(1, content.Behaviors.Definitions.Count);
            Equal(1, content.Dialogues.Lines.Count);
            Equal(
                descriptor.FrameSize,
                content.Animations["idle_breathe"].Frames[0].PixelWidth);
            True(content.Postcards is null);
            True(content.EyeAccessories is null);
            True(content.EyeAccessoryPoses is null);
        }
        finally
        {
            content.Animations.Dispose();
        }
    }

    private static void LegacyAnimationArbitrationFieldsFailClosed()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.AnimationCatalog.{Guid.NewGuid():N}");
        string clipDirectory = Path.Combine(root, "legacy");
        Directory.CreateDirectory(clipDirectory);
        try
        {
            TestAnimatedWebp.Write(
                Path.Combine(clipDirectory, "animation.webp"));
            File.WriteAllText(
                Path.Combine(clipDirectory, "clip.json"),
                """
                {
                  "name": "legacy",
                  "frameCount": 3,
                  "fps": 24,
                  "loop": false,
                  "priority": 10,
                  "placeholder": false,
                  "assetStatus": "production"
                }
                """);

            BehaviorTestCheck.Throws<JsonException>(
                () => AnimationCatalog.LoadAsync(
                        root,
                        decodePixelWidth: 0)
                    .GetAwaiter()
                    .GetResult());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void LegacyDialogueCooldownFieldFailsClosed()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.DialogueCatalog.{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """
                [
                  {
                    "id": "legacy",
                    "text": "喵~",
                    "meaning": "我在这里陪你。",
                    "scene": "companionship",
                    "action": "settle",
                    "mood": "calm",
                    "relationshipStages": ["familiar"],
                    "type": "dialogue",
                    "weight": 1,
                    "cooldownSeconds": 900
                  }
                ]
                """);
            BehaviorTestCheck.Throws<JsonException>(
                () => DialogueCatalog.LoadAsync(path)
                    .GetAwaiter()
                    .GetResult());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AnimationSourceApprovalLoadsAndRemainsOptional()
    {
        string root = CreateAnimationCatalogFixture(
            ("legacy", null),
            ("approved", CreateValidSourceApproval()));
        try
        {
            using AnimationCatalog catalog = AnimationCatalog.LoadAsync(
                    root,
                    decodePixelWidth: 0)
                .GetAwaiter()
                .GetResult();

            True(catalog["legacy"].Definition.SourceApproval is null);
            AnimationSourceApprovalProvenance? approval =
                catalog["approved"].Definition.SourceApproval;
            NotNull(approval);
            Equal(
                "gulu-runtime-source-approval-provenance-v1",
                approval!.Schema);
            Equal("approved_for_personal_runtime", approval.ApprovalDecision);
            Equal("approval-v1", approval.ApprovalId);
            Equal(new string('A', 64), approval.PixelSequenceSha256);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AnimationSourceApprovalUnknownFieldsFailClosed()
    {
        JsonObject approval = CreateValidSourceApproval();
        approval["unexpectedField"] = "must-fail";
        string root = CreateAnimationCatalogFixture(("unknown", approval));
        try
        {
            _ = BehaviorTestCheck.Throws<JsonException>(
                () => AnimationCatalog.LoadAsync(
                        root,
                        decodePixelWidth: 0)
                    .GetAwaiter()
                    .GetResult());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AnimationSourceApprovalInvalidValuesFailClosed()
    {
        foreach ((string Field, string Value) invalid in new[]
                 {
                     ("schema", "unknown-schema"),
                     ("approvalDecision", "pending"),
                     ("approvalRecord", " "),
                     ("candidateManifestSha256", new string('a', 64)),
                 })
        {
            JsonObject approval = CreateValidSourceApproval();
            approval[invalid.Field] = invalid.Value;
            string root = CreateAnimationCatalogFixture((invalid.Field, approval));
            try
            {
                _ = BehaviorTestCheck.Throws<InvalidDataException>(
                    () => AnimationCatalog.LoadAsync(
                            root,
                            decodePixelWidth: 0)
                        .GetAwaiter()
                        .GetResult());
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateAnimationCatalogFixture(
        params (string Name, JsonObject? SourceApproval)[] clips)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.AnimationCatalog.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        foreach ((string name, JsonObject? sourceApproval) in clips)
        {
            string clipDirectory = Path.Combine(root, name);
            Directory.CreateDirectory(clipDirectory);
            TestAnimatedWebp.Write(
                Path.Combine(clipDirectory, "animation.webp"));

            var definition = new JsonObject
            {
                ["name"] = name,
                ["frameCount"] = 3,
                ["fps"] = 24,
                ["loop"] = false,
                ["placeholder"] = false,
                ["assetStatus"] = "production",
            };
            if (sourceApproval is not null)
            {
                definition["sourceApproval"] = sourceApproval;
            }

            File.WriteAllText(
                Path.Combine(clipDirectory, "clip.json"),
                definition.ToJsonString());
        }

        return root;
    }

    private static JsonObject CreateValidSourceApproval() =>
        new()
        {
            ["schema"] = "gulu-runtime-source-approval-provenance-v1",
            ["approvalId"] = "approval-v1",
            ["approvalDecision"] = "approved_for_personal_runtime",
            ["approvalRecord"] = "asset-pipeline\\audits\\approval.json",
            ["approvalRecordSha256"] = new string('A', 64),
            ["candidateManifest"] = "asset-pipeline\\processed\\manifest.json",
            ["candidateManifestSha256"] = new string('A', 64),
            ["sourceReview"] = "asset-pipeline\\work\\source-review.json",
            ["sourceReviewSha256"] = new string('A', 64),
            ["frameHashes"] = "asset-pipeline\\processed\\frame-hashes.json",
            ["frameHashesSha256"] = new string('A', 64),
            ["pixelSequenceSha256"] = new string('A', 64),
            ["candidateClipJson"] = "asset-pipeline\\processed\\clip.json",
            ["candidateClipJsonSha256"] = new string('A', 64),
        };

    private static void FullscreenPolicyMatchesProductRules()
    {
        True(FullscreenPresentationPolicy.ShouldShowPet(
            userWantsPetVisible: true,
            alwaysOnTop: true,
            foregroundIsFullscreen: true));
        True(!FullscreenPresentationPolicy.ShouldShowPet(
            userWantsPetVisible: true,
            alwaysOnTop: false,
            foregroundIsFullscreen: true));
        True(FullscreenPresentationPolicy.ShouldShowPet(
            userWantsPetVisible: true,
            alwaysOnTop: false,
            foregroundIsFullscreen: false));
        True(!FullscreenPresentationPolicy.ShouldShowPet(
            userWantsPetVisible: false,
            alwaysOnTop: true,
            foregroundIsFullscreen: false));
    }

    private static void ManualUpdateIsOfflineUntilConfigured()
    {
        using var service = new ManualUpdateService(
            manifestUri: null,
            currentVersion: new Version(0, 1, 0));
        var result = service.CheckAsync().GetAwaiter().GetResult();
        Equal(UpdateCheckStatus.NotConfigured, result.Status);
        Equal("0.1.0", result.CurrentVersion);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void NotNull(object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected a non-null value.");
        }
    }

    private static void Equal<T>(T expected, T actual)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected '{expected}', got '{actual}'.");
        }
    }
}
