using System.IO;
using System.Text.Json;
using GuluPet.Behavior;

namespace GuluPet.Tests;

internal static class BehaviorCatalogTests
{
    public static void RunAll()
    {
        Run(nameof(CommunityCatalogLoadsWithIndependentBehaviorId), CommunityCatalogLoadsWithIndependentBehaviorId);
        Run(nameof(DuplicateBehaviorIdsFailClosed), DuplicateBehaviorIdsFailClosed);
        Run(nameof(QueueableExternalLoopFailsClosed), QueueableExternalLoopFailsClosed);
        Run(
            nameof(QueueableLifecycleUsesTwentySecondWatchdogCeiling),
            QueueableLifecycleUsesTwentySecondWatchdogCeiling);
        Run(nameof(BubbleTimelineValidationFailsClosed), BubbleTimelineValidationFailsClosed);
        Run(
            nameof(PoolBubbleSemanticsValidationFailsClosed),
            PoolBubbleSemanticsValidationFailsClosed);
        Run(
            nameof(BubbleAnnotationValidationFailsClosed),
            BubbleAnnotationValidationFailsClosed);
        Run(nameof(BehaviorIdJsonRoundTripsAsAString), BehaviorIdJsonRoundTripsAsAString);
        Run(
            nameof(AnimationVariantsRoundTripAndValidateShape),
            AnimationVariantsRoundTripAndValidateShape);
        Run(nameof(LegacyFallbackFieldFailsClosed), LegacyFallbackFieldFailsClosed);
    }

    private static void CommunityCatalogLoadsWithIndependentBehaviorId()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Data",
            "behaviors.json");
        BehaviorCatalog catalog = BehaviorCatalog.LoadAsync(path)
            .GetAwaiter()
            .GetResult();

        BehaviorDefinition fallback = catalog.Definitions.Single();
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fallback.Id);
        BehaviorTestCheck.Equal("idle", fallback.ClipFamily);
        BehaviorTestCheck.Equal(
            "idle_breathe",
            fallback.Animation.LoopClipId!);
        BehaviorTestCheck.False(
            string.Equals(
                fallback.Id.Value,
                fallback.Animation.LoopClipId,
                StringComparison.Ordinal));
        BehaviorTestCheck.True(fallback.IsStateFallback);
        BehaviorTestCheck.False(fallback.Queueable);
    }

    private static void DuplicateBehaviorIdsFailClosed()
    {
        var definition = CreateFiniteDefinition("same");
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => _ = new BehaviorCatalog([definition, definition]));
    }

    private static void QueueableExternalLoopFailsClosed()
    {
        var definition = CreateFiniteDefinition("bad_external");
        definition = new BehaviorDefinition
        {
            Id = definition.Id,
            DisplayName = definition.DisplayName,
            Family = definition.Family,
            ClipFamily = definition.ClipFamily,
            Animation = new BehaviorAnimationPlan { LoopClipId = "loop" },
            Completion =
                new BehaviorCompletionPolicy
                {
                    Kind = BehaviorCompletionKind.External,
                },
            Queueable = true,
        };

        BehaviorTestCheck.Throws<InvalidDataException>(
            () => _ = new BehaviorCatalog([definition]));
    }

    private static void QueueableLifecycleUsesTwentySecondWatchdogCeiling()
    {
        BehaviorDefinition Valid(TimeSpan watchdog) =>
            new()
            {
                Id = "finite_lifecycle",
                DisplayName = "finite_lifecycle",
                Family = "test",
                ClipFamily = "test",
                Animation = new BehaviorAnimationPlan
                {
                    EnterClipId = "enter",
                    LoopClipId = "loop",
                    ExitClipId = "exit",
                },
                Completion = new BehaviorCompletionPolicy
                {
                    Kind = BehaviorCompletionKind.LoopCount,
                    LoopCount = 1,
                },
                MaximumDuration = watchdog,
                Queueable = true,
            };

        _ = new BehaviorCatalog([Valid(TimeSpan.FromSeconds(18))]);
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => _ = new BehaviorCatalog(
                [Valid(TimeSpan.FromSeconds(20.001))]));
    }

    private static void BubbleTimelineValidationFailsClosed()
    {
        var tooClose = CreateFiniteDefinition(
            "bad_cues",
            [
                new BehaviorBubbleCue
                {
                    At = TimeSpan.Zero,
                    DialogueId = "a",
                    Annotation = "测试",
                },
                new BehaviorBubbleCue
                {
                    At = TimeSpan.FromMilliseconds(799),
                    DialoguePoolId = "companionship",
                    Action = "watch",
                    Annotation = "测试",
                },
            ]);
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => _ = new BehaviorCatalog([tooClose]));

        var beyondEnd = CreateFiniteDefinition(
            "late_cue",
            [
                new BehaviorBubbleCue
                {
                    At = TimeSpan.FromSeconds(3),
                    DialogueId = "late",
                    Annotation = "测试",
                },
            ],
            maximumDuration: TimeSpan.FromSeconds(2));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => _ = new BehaviorCatalog([beyondEnd]));
    }

    private static void PoolBubbleSemanticsValidationFailsClosed()
    {
        BehaviorDefinition WithSemantics(string scene, string? action) =>
            CreateFiniteDefinition(
                $"pool_semantics_{scene}_{action ?? "missing"}",
                [
                    new BehaviorBubbleCue
                    {
                        At = TimeSpan.Zero,
                        DialoguePoolId = scene,
                        Action = action,
                    },
                ]);

        BehaviorTestCheck.Throws<InvalidDataException>(
            () => _ = new BehaviorCatalog(
                [WithSemantics("companionship", action: null)]));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => _ = new BehaviorCatalog(
                [WithSemantics("companionship", "dance")]));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => _ = new BehaviorCatalog(
                [WithSemantics("neutral", "watch")]));

        BehaviorDefinition valid = WithSemantics("companionship", "watch");
        _ = new BehaviorCatalog([valid]);
        string serialized = BehaviorCatalog.Serialize([valid]);
        BehaviorTestCheck.True(
            serialized.Contains(
                "\"action\": \"watch\"",
                StringComparison.Ordinal));
    }

    private static void BubbleAnnotationValidationFailsClosed()
    {
        foreach (string annotation in
                 new[]
                 {
                     "",
                     new string('你', 41),
                     "（开心）",
                     " 开心",
                     "看 我",
                     "！！！",
                 })
        {
            var definition = CreateFiniteDefinition(
                $"bad_annotation_{annotation.Length}",
                [
                    new BehaviorBubbleCue
                    {
                        At = TimeSpan.Zero,
                        DialoguePoolId = "companionship",
                        Action = "settle",
                        Annotation = annotation,
                        VisibleFor = TimeSpan.FromSeconds(2.5),
                    },
                ]);
            BehaviorTestCheck.Throws<InvalidDataException>(
                () => _ = new BehaviorCatalog([definition]));
        }

        _ = new BehaviorCatalog(
            [
                CreateFiniteDefinition(
                    "valid_annotation",
                    [
                        new BehaviorBubbleCue
                        {
                            At = TimeSpan.Zero,
                            DialoguePoolId = "companionship",
                            Action = "watch",
                            Annotation = "你在忙什么",
                            VisibleFor = TimeSpan.FromSeconds(2.5),
                        },
                    ]),
            ]);

        BehaviorDefinition withoutAnnotation = CreateFiniteDefinition(
            "missing_annotation",
            [
                new BehaviorBubbleCue
                {
                    At = TimeSpan.Zero,
                    DialoguePoolId = "companionship",
                    Action = "settle",
                    VisibleFor = TimeSpan.FromSeconds(2.5),
                },
            ]);
        _ = new BehaviorCatalog([withoutAnnotation]);
        string serialized = BehaviorCatalog.Serialize([withoutAnnotation]);
        BehaviorTestCheck.False(
            serialized.Contains(
                "\"annotation\"",
                StringComparison.Ordinal));
        string path = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.OptionalAnnotation.{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, serialized);
            BehaviorCatalog loaded = BehaviorCatalog.LoadAsync(path)
                .GetAwaiter()
                .GetResult();
            BehaviorTestCheck.Null(
                loaded[new BehaviorId("missing_annotation")]
                    .BubbleCues[0]
                    .Annotation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void BehaviorIdJsonRoundTripsAsAString()
    {
        var catalog = new BehaviorCatalog([CreateFiniteDefinition("json_id")]);
        var json = BehaviorCatalog.Serialize(catalog.Definitions);
        BehaviorTestCheck.True(
            json.Contains("\"id\": \"json_id\"", StringComparison.Ordinal));
    }

    private static void AnimationVariantsRoundTripAndValidateShape()
    {
        BehaviorDefinition definition = CreateVariantDefinition(
            new BehaviorAnimationPlan
            {
                VariantId = "a",
                EnterClipId = "sleep_enter",
                LoopClipId = "sleep_loop",
                ExitClipId = "sleep_exit",
                Variants =
                [
                    new BehaviorAnimationVariant
                    {
                        Id = "b",
                        EnterClipId = "sleep_enter_b",
                        LoopClipId = "sleep_loop_b",
                        ExitClipId = "sleep_exit_b",
                    },
                    new BehaviorAnimationVariant
                    {
                        Id = "c",
                        EnterClipId = "sleep_enter_c",
                        LoopClipId = "sleep_loop_c",
                        ExitClipId = "sleep_exit_c",
                    },
                ],
            });
        var catalog = new BehaviorCatalog([definition]);
        string json = BehaviorCatalog.Serialize(catalog.Definitions);
        BehaviorTestCheck.True(
            json.Contains("\"variantId\": \"a\"", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            json.Contains("\"variants\": [", StringComparison.Ordinal));

        string path = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.AnimationVariants.{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            BehaviorCatalog loaded = BehaviorCatalog.LoadAsync(path)
                .GetAwaiter()
                .GetResult();
            BehaviorAnimationPlan animation =
                loaded[new BehaviorId("variant_sleep")].Animation;
            BehaviorTestCheck.Equal("a", animation.VariantId!);
            BehaviorTestCheck.Equal(2, animation.Variants.Count);
            BehaviorTestCheck.SequenceEqual(
                new[]
                {
                    "sleep_enter",
                    "sleep_loop",
                    "sleep_exit",
                    "sleep_enter_b",
                    "sleep_loop_b",
                    "sleep_exit_b",
                    "sleep_enter_c",
                    "sleep_loop_c",
                    "sleep_exit_c",
                },
                animation.RequiredClipIds());
        }
        finally
        {
            File.Delete(path);
        }

        BehaviorTestCheck.Throws<InvalidDataException>(
            () => _ = new BehaviorCatalog(
            [
                CreateVariantDefinition(
                    new BehaviorAnimationPlan
                    {
                        VariantId = "a",
                        EnterClipId = "enter_a",
                        LoopClipId = "loop_a",
                        ExitClipId = "exit_a",
                        Variants =
                        [
                            new BehaviorAnimationVariant
                            {
                                Id = "b",
                                EnterClipId = "enter_b",
                                LoopClipId = "loop_b",
                            },
                        ],
                    }),
            ]));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => _ = new BehaviorCatalog(
            [
                CreateVariantDefinition(
                    new BehaviorAnimationPlan
                    {
                        VariantId = "a",
                        EnterClipId = "enter_a",
                        LoopClipId = "loop_a",
                        ExitClipId = "exit_a",
                        Variants =
                        [
                            new BehaviorAnimationVariant
                            {
                                Id = "a",
                                EnterClipId = "enter_b",
                                LoopClipId = "loop_b",
                                ExitClipId = "exit_b",
                            },
                        ],
                    }),
            ]));
    }

    private static void LegacyFallbackFieldFailsClosed()
    {
        string json = BehaviorCatalog.Serialize(
            [CreateFiniteDefinition("legacy_field")]);
        json = json.Replace(
            "\"displayName\": \"legacy_field\",",
            "\"displayName\": \"legacy_field\",\n" +
            "      \"fallbackBehaviorId\": \"idle_fallback\",",
            StringComparison.Ordinal);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.BehaviorCatalog.{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            BehaviorTestCheck.Throws<JsonException>(
                () => BehaviorCatalog.LoadAsync(path).GetAwaiter().GetResult());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static BehaviorDefinition CreateFiniteDefinition(
        string id,
        IReadOnlyList<BehaviorBubbleCue>? cues = null,
        TimeSpan? maximumDuration = null) =>
        new()
        {
            Id = id,
            DisplayName = id,
            Family = "test",
            ClipFamily = "test_clip",
            Animation = new BehaviorAnimationPlan { PerformClipId = "clip" },
            BubbleCues = cues ?? [],
            Completion =
                new BehaviorCompletionPolicy
                {
                    Kind = BehaviorCompletionKind.ClipEnd,
                },
            MaximumDuration = maximumDuration ?? TimeSpan.FromSeconds(2),
        };

    private static BehaviorDefinition CreateVariantDefinition(
        BehaviorAnimationPlan animation) =>
        new()
        {
            Id = "variant_sleep",
            DisplayName = "variant_sleep",
            Family = "test",
            ClipFamily = "sleep",
            Animation = animation,
            Completion = new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.External,
            },
            MaximumDuration = TimeSpan.FromMinutes(1),
            Queueable = false,
        };

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }

}
