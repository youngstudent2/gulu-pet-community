using GuluPet.Animation;
using GuluPet.Behavior;
using GuluPet.Dialogue;
using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class RuntimeContentContractTests
{
    public static void RunAll()
    {
        Run(
            nameof(PublishedCommunityContentPassesContract),
            PublishedCommunityContentPassesContract);
        Run(
            nameof(DescriptorValidationRejectsInvalidValues),
            DescriptorValidationRejectsInvalidValues);
        Run(
            nameof(MissingClipsFailAsOneAggregatedContract),
            MissingClipsFailAsOneAggregatedContract);
        Run(
            nameof(LoopRoleMismatchIsReported),
            LoopRoleMismatchIsReported);
        Run(
            nameof(MissingDialogueReferencesAreReported),
            MissingDialogueReferencesAreReported);
        Run(
            nameof(MissingNormalFallbackIsReported),
            MissingNormalFallbackIsReported);
    }

    private static void PublishedCommunityContentPassesContract()
    {
        ValidatedRuntimeContent content = LoadCommunityContent();
        try
        {
            IReadOnlyList<string> violations =
                RuntimeContentContract.FindViolations(
                    content.Behaviors,
                    content.Animations,
                    content.Dialogues,
                    RuntimeContentDescriptor.CommunityContract);
            BehaviorTestCheck.Equal(
                string.Empty,
                string.Join(Environment.NewLine, violations));
            BehaviorTestCheck.Null(content.Postcards);
            BehaviorTestCheck.Null(content.EyeAccessories);
            BehaviorTestCheck.Null(content.EyeAccessoryPoses);
        }
        finally
        {
            content.Animations.Dispose();
        }
    }

    private static void DescriptorValidationRejectsInvalidValues()
    {
        RuntimeContentDescriptor invalid =
            RuntimeContentDescriptor.CommunityContract with
            {
                SchemaVersion = 2,
                ExpectedClipCount = 0,
                AnimationFormat = "png-sequence",
            };
        string report = string.Join(
            Environment.NewLine,
            RuntimeContentDescriptorLoader.FindViolations(invalid));

        True(report.Contains("schemaVersion must be 1", StringComparison.Ordinal));
        True(report.Contains("expectedClipCount must be positive", StringComparison.Ordinal));
        True(report.Contains("animationFormat must be", StringComparison.Ordinal));
    }

    private static void MissingClipsFailAsOneAggregatedContract()
    {
        ValidatedRuntimeContent content = LoadCommunityContent();
        try
        {
            BehaviorDefinition source = content.Behaviors.Definitions.Single();
            string[] missingClips =
            [
                "missing_community_clip_a",
                "missing_community_clip_b",
            ];
            var invalidCatalog = new BehaviorCatalog(
                missingClips.Select(
                    (clipId, index) => CloneDefinition(
                        source,
                        new BehaviorId($"missing_clip_{index}"),
                        new BehaviorAnimationPlan { LoopClipId = clipId },
                        isStateFallback: index == 0)));

            string report = string.Join(
                Environment.NewLine,
                RuntimeContentContract.FindViolations(
                    invalidCatalog,
                    content.Animations,
                    content.Dialogues));
            foreach (string clipId in missingClips)
            {
                True(report.Contains($"'{clipId}' is missing", StringComparison.Ordinal));
            }

            InvalidDataException exception =
                BehaviorTestCheck.Throws<InvalidDataException>(
                    () => RuntimeContentContract.Validate(
                        invalidCatalog,
                        content.Animations,
                        content.Dialogues));
            foreach (string clipId in missingClips)
            {
                True(exception.Message.Contains(clipId, StringComparison.Ordinal));
            }
        }
        finally
        {
            content.Animations.Dispose();
        }
    }

    private static void LoopRoleMismatchIsReported()
    {
        ValidatedRuntimeContent content = LoadCommunityContent();
        try
        {
            BehaviorDefinition source = content.Behaviors.Definitions.Single();
            BehaviorDefinition invalid = CloneDefinition(
                source,
                source.Id,
                new BehaviorAnimationPlan
                {
                    PerformClipId = "idle_breathe",
                },
                isStateFallback: true);
            var invalidCatalog = new BehaviorCatalog([invalid]);

            string report = string.Join(
                Environment.NewLine,
                RuntimeContentContract.FindViolations(
                    invalidCatalog,
                    content.Animations,
                    content.Dialogues));
            True(
                report.Contains(
                    "perform clip 'idle_breathe' must have loop=false",
                    StringComparison.Ordinal));
        }
        finally
        {
            content.Animations.Dispose();
        }
    }

    private static void MissingDialogueReferencesAreReported()
    {
        ValidatedRuntimeContent content = LoadCommunityContent();
        try
        {
            BehaviorDefinition source = content.Behaviors.Definitions.Single();
            BehaviorDefinition invalid = CloneDefinition(
                source,
                source.Id,
                source.Animation,
                bubbleCues:
                [
                    new BehaviorBubbleCue
                    {
                        At = TimeSpan.Zero,
                        DialogueId = "missing-dialogue",
                    },
                    new BehaviorBubbleCue
                    {
                        At = TimeSpan.FromSeconds(1),
                        DialoguePoolId = "work",
                        Action = "watch",
                    },
                ]);
            var invalidCatalog = new BehaviorCatalog([invalid]);

            string report = string.Join(
                Environment.NewLine,
                RuntimeContentContract.FindViolations(
                    invalidCatalog,
                    content.Animations,
                    content.Dialogues));
            True(report.Contains("missing dialogue 'missing-dialogue'", StringComparison.Ordinal));
            True(report.Contains("empty dialogue pool 'work'", StringComparison.Ordinal));
        }
        finally
        {
            content.Animations.Dispose();
        }
    }

    private static void MissingNormalFallbackIsReported()
    {
        ValidatedRuntimeContent content = LoadCommunityContent();
        try
        {
            BehaviorDefinition source = content.Behaviors.Definitions.Single();
            var invalidCatalog = new BehaviorCatalog(
            [
                CloneDefinition(
                    source,
                    source.Id,
                    source.Animation,
                    isStateFallback: false),
            ]);

            string report = string.Join(
                Environment.NewLine,
                RuntimeContentContract.FindViolations(
                    invalidCatalog,
                    content.Animations,
                    content.Dialogues));
            True(
                report.Contains(
                    "At least one Normal-state fallback behavior is required",
                    StringComparison.Ordinal));
        }
        finally
        {
            content.Animations.Dispose();
        }
    }

    private static ValidatedRuntimeContent LoadCommunityContent() =>
        RuntimeContentContract.LoadAndValidateAsync(
                Path.Combine(AppContext.BaseDirectory, "Assets"),
                decodePixelWidth: 0)
            .GetAwaiter()
            .GetResult();

    private static BehaviorDefinition CloneDefinition(
        BehaviorDefinition source,
        BehaviorId id,
        BehaviorAnimationPlan animation,
        IReadOnlyList<BehaviorBubbleCue>? bubbleCues = null,
        bool? isStateFallback = null) =>
        new()
        {
            Id = id,
            DisplayName = source.DisplayName,
            Family = source.Family,
            ClipFamily = source.ClipFamily,
            Tags = source.Tags,
            AllowedStates = source.AllowedStates,
            TargetState = source.TargetState,
            Animation = animation,
            BubbleCues = bubbleCues ?? source.BubbleCues,
            Completion = source.Completion,
            MinimumCommitTime = source.MinimumCommitTime,
            MaximumDuration = source.MaximumDuration,
            DisturbanceLevel = source.DisturbanceLevel,
            Cooldown = source.Cooldown,
            CooldownGroup = source.CooldownGroup,
            StartedEmotionEffect = source.StartedEmotionEffect,
            CompletedEmotionEffect = source.CompletedEmotionEffect,
            Utility = source.Utility,
            AutomaticEligibility = source.AutomaticEligibility,
            Queueable = source.Queueable,
            IsStateFallback = isStateFallback ?? source.IsStateFallback,
        };

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(RuntimeContentContractTests)}.{name}");
    }

    private static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected condition to be true.");
        }
    }
}
