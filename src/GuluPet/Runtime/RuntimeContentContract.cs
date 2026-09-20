using System.IO;
using System.Windows.Media;
using GuluPet.Accessories;
using GuluPet.Animation;
using GuluPet.Behavior;
using GuluPet.Dialogue;
using GuluPet.Postcards;

namespace GuluPet.Runtime;

public sealed record ValidatedRuntimeContent(
    AnimationCatalog Animations,
    BehaviorCatalog Behaviors,
    DialogueCatalog Dialogues,
    PostcardCatalog? Postcards = null,
    EyeAccessoryCatalog? EyeAccessories = null,
    EyeAccessoryPoseCatalog? EyeAccessoryPoses = null);

/// <summary>
/// Validates the small, replaceable content contract used by the community
/// edition. The catalog is intentionally data driven: it has no dependency on
/// the original private clip inventory or optional feature packs.
/// </summary>
public static class RuntimeContentContract
{
    public const double DefaultFramesPerSecond = 24;
    public const string RequiredAnimationFormat = "animated-webp";

    public static async Task<ValidatedRuntimeContent> LoadAndValidateAsync(
        string assetsRoot,
        int decodePixelWidth = 480,
        CancellationToken cancellationToken = default,
        bool validateEveryFrame = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsRoot);

        RuntimeContentDescriptor descriptor =
            await RuntimeContentDescriptorLoader.LoadAsync(
                Path.Combine(assetsRoot, "runtime-content.json"),
                cancellationToken);
        int effectiveDecodePixelWidth = validateEveryFrame
            ? 0
            : decodePixelWidth == 0
                ? 0
                : Math.Min(decodePixelWidth, descriptor.FrameSize);

        Task<AnimationCatalog> animationTask = AnimationCatalog.LoadAsync(
            Path.Combine(assetsRoot, "Animations"),
            effectiveDecodePixelWidth,
            cancellationToken);
        Task<BehaviorCatalog> behaviorTask = BehaviorCatalog.LoadAsync(
            Path.Combine(assetsRoot, "Data", "behaviors.json"),
            cancellationToken);
        Task<DialogueCatalog> dialogueTask = DialogueCatalog.LoadAsync(
            Path.Combine(assetsRoot, "Data", "dialogues.json"),
            cancellationToken);

        await Task.WhenAll(animationTask, behaviorTask, dialogueTask);
        AnimationCatalog animations = await animationTask;
        BehaviorCatalog behaviors = await behaviorTask;
        DialogueCatalog dialogues = await dialogueTask;
        Validate(behaviors, animations, dialogues, descriptor);

        if (validateEveryFrame)
        {
            ValidateAllAnimationFrames(
                animations,
                descriptor.FrameSize,
                cancellationToken);
        }

        return new ValidatedRuntimeContent(animations, behaviors, dialogues);
    }

    public static void Validate(
        BehaviorCatalog behaviors,
        AnimationCatalog animations,
        DialogueCatalog dialogues) =>
        Validate(
            behaviors,
            animations,
            dialogues,
            RuntimeContentDescriptor.CommunityContract);

    public static void Validate(
        BehaviorCatalog behaviors,
        AnimationCatalog animations,
        DialogueCatalog dialogues,
        RuntimeContentDescriptor descriptor)
    {
        IReadOnlyList<string> violations = FindViolations(
            behaviors,
            animations,
            dialogues,
            descriptor);
        if (violations.Count == 0)
        {
            return;
        }

        throw new InvalidDataException(
            "Gulu Pet Community content validation failed:" +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                violations.Select(static violation => $"- {violation}")));
    }

    public static IReadOnlyList<string> FindViolations(
        BehaviorCatalog behaviors,
        AnimationCatalog animations,
        DialogueCatalog dialogues) =>
        FindViolations(
            behaviors,
            animations,
            dialogues,
            RuntimeContentDescriptor.CommunityContract);

    public static IReadOnlyList<string> FindViolations(
        BehaviorCatalog behaviors,
        AnimationCatalog animations,
        DialogueCatalog dialogues,
        RuntimeContentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(behaviors);
        ArgumentNullException.ThrowIfNull(animations);
        ArgumentNullException.ThrowIfNull(dialogues);
        ArgumentNullException.ThrowIfNull(descriptor);

        var violations = new List<string>();
        violations.AddRange(
            RuntimeContentDescriptorLoader.FindViolations(descriptor)
                .Select(static violation =>
                    $"Runtime content descriptor: {violation}"));

        string[] animationNames = animations.Names
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (animationNames.Length != descriptor.ExpectedClipCount)
        {
            violations.Add(
                $"Descriptor declares {descriptor.ExpectedClipCount} clips, " +
                $"but {animationNames.Length} were loaded.");
        }

        foreach (string clipId in animationNames)
        {
            AnimationClipDefinition definition = animations[clipId].Definition;
            if (Math.Abs(definition.Fps - descriptor.FramesPerSecond) > 0.001)
            {
                violations.Add(
                    $"Animation '{clipId}' uses {definition.Fps:0.###} fps; " +
                    $"expected {descriptor.FramesPerSecond:0.###}.");
            }

            if (definition.FrameCount != animations[clipId].Frames.Count)
            {
                violations.Add(
                    $"Animation '{clipId}' declares {definition.FrameCount} " +
                    $"frames but contains {animations[clipId].Frames.Count}.");
            }
        }

        IReadOnlyDictionary<string, DialogueLine> dialogueById = dialogues.Lines
            .ToDictionary(static line => line.Id, StringComparer.OrdinalIgnoreCase);
        ILookup<string, DialogueLine> dialoguePools = dialogues.Lines
            .ToLookup(static line => line.Scene, StringComparer.OrdinalIgnoreCase);

        foreach (BehaviorDefinition behavior in behaviors.Definitions)
        {
            ValidateClipRole(
                behavior,
                "enter",
                behavior.Animation.EnterClipId,
                expectedLoop: false,
                animations,
                violations);
            ValidateClipRole(
                behavior,
                "perform",
                behavior.Animation.PerformClipId,
                expectedLoop: false,
                animations,
                violations);
            ValidateClipRole(
                behavior,
                "loop",
                behavior.Animation.LoopClipId,
                expectedLoop: true,
                animations,
                violations);
            ValidateClipRole(
                behavior,
                "exit",
                behavior.Animation.ExitClipId,
                expectedLoop: false,
                animations,
                violations);

            foreach (BehaviorAnimationVariant variant in behavior.Animation.Variants)
            {
                ValidateClipRole(
                    behavior,
                    $"variant '{variant.Id}' enter",
                    variant.EnterClipId,
                    false,
                    animations,
                    violations);
                ValidateClipRole(
                    behavior,
                    $"variant '{variant.Id}' perform",
                    variant.PerformClipId,
                    false,
                    animations,
                    violations);
                ValidateClipRole(
                    behavior,
                    $"variant '{variant.Id}' loop",
                    variant.LoopClipId,
                    true,
                    animations,
                    violations);
                ValidateClipRole(
                    behavior,
                    $"variant '{variant.Id}' exit",
                    variant.ExitClipId,
                    false,
                    animations,
                    violations);
            }

            foreach (BehaviorBubbleCue cue in behavior.BubbleCues)
            {
                if (!string.IsNullOrWhiteSpace(cue.DialogueId)
                    && !dialogueById.ContainsKey(cue.DialogueId))
                {
                    violations.Add(
                        $"Behavior '{behavior.Id}' references missing dialogue " +
                        $"'{cue.DialogueId}'.");
                }

                if (!string.IsNullOrWhiteSpace(cue.DialoguePoolId)
                    && !dialoguePools.Contains(cue.DialoguePoolId))
                {
                    violations.Add(
                        $"Behavior '{behavior.Id}' references empty dialogue pool " +
                        $"'{cue.DialoguePoolId}'.");
                }
            }
        }

        if (!behaviors.Definitions.Any(static definition =>
                definition.IsStateFallback
                && definition.AllowedStates.Contains(StablePetState.Normal)))
        {
            violations.Add("At least one Normal-state fallback behavior is required.");
        }

        return violations
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static void ValidateAllAnimationFrames(
        AnimationCatalog animations,
        int expectedFrameSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(animations);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedFrameSize, 1);
        int stride = checked(expectedFrameSize * 4);
        var pixelBuffer = new byte[checked(stride * expectedFrameSize)];

        foreach (string clipName in animations.Names.Order(
                     StringComparer.OrdinalIgnoreCase))
        {
            AnimationClip clip = animations[clipName];
            for (var frameIndex = 0; frameIndex < clip.Frames.Count; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = clip.Frames[frameIndex];
                if (frame.PixelWidth != expectedFrameSize
                    || frame.PixelHeight != expectedFrameSize)
                {
                    throw new InvalidDataException(
                        $"Animation '{clipName}' frame {frameIndex + 1} must be " +
                        $"{expectedFrameSize}x{expectedFrameSize}.");
                }

                if (!frame.Format.Equals(PixelFormats.Bgra32)
                    && !frame.Format.Equals(PixelFormats.Pbgra32))
                {
                    throw new InvalidDataException(
                        $"Animation '{clipName}' frame {frameIndex + 1} must decode " +
                        "to a 32-bit BGRA image.");
                }

                frame.CopyPixels(pixelBuffer, stride, 0);
                bool transparent = false;
                bool visible = false;
                for (var offset = 3; offset < pixelBuffer.Length; offset += 4)
                {
                    transparent |= pixelBuffer[offset] == 0;
                    visible |= pixelBuffer[offset] >= 250;
                }

                if (!transparent || !visible)
                {
                    throw new InvalidDataException(
                        $"Animation '{clipName}' frame {frameIndex + 1} must contain " +
                        "a transparent background and an opaque subject.");
                }
            }
        }
    }

    private static void ValidateClipRole(
        BehaviorDefinition behavior,
        string role,
        string? clipId,
        bool expectedLoop,
        AnimationCatalog animations,
        ICollection<string> violations)
    {
        if (string.IsNullOrWhiteSpace(clipId))
        {
            return;
        }

        if (!animations.TryGet(clipId, out AnimationClip? clip) || clip is null)
        {
            violations.Add(
                $"Behavior '{behavior.Id}' {role} clip '{clipId}' is missing.");
            return;
        }

        if (clip.Definition.Loop != expectedLoop)
        {
            violations.Add(
                $"Behavior '{behavior.Id}' {role} clip '{clipId}' must have " +
                $"loop={expectedLoop.ToString().ToLowerInvariant()}.");
        }
    }
}
