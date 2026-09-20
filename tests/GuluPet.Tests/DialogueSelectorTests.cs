using GuluPet.Behavior;
using GuluPet.Dialogue;

namespace GuluPet.Tests;

internal static class DialogueSelectorTests
{
    public static void RunAll()
    {
        Run(nameof(ExactFourDimensionMatchReturnsOnlyTwoVariants), ExactFourDimensionMatchReturnsOnlyTwoVariants);
        Run(nameof(UnknownMoodFallsBackWithinSceneActionAndStage), UnknownMoodFallsBackWithinSceneActionAndStage);
        Run(nameof(SemanticKeyCollapsesVocalVariants), SemanticKeyCollapsesVocalVariants);
        Run(
            nameof(SceneResolverPreservesExplicitScenesAndPrioritizesSignals),
            SceneResolverPreservesExplicitScenesAndPrioritizesSignals);
        Run(
            nameof(ActionResolverReachesEveryActionAndPrefersCueMetadata),
            ActionResolverReachesEveryActionAndPrefersCueMetadata);
    }

    private static void ExactFourDimensionMatchReturnsOnlyTwoVariants()
    {
        DialogueCatalog catalog = LoadCatalog();
        var selector = new DialogueSelector(catalog, new Random(19));
        IReadOnlyList<DialogueLine> selected = selector.SelectCandidates(
            new DialogueSelectionContext(
                "work",
                "watch",
                "happy",
                "close"));

        BehaviorTestCheck.Equal(2, selected.Count);
        BehaviorTestCheck.True(selected.All(static line =>
            line.Scene == "work" &&
            line.Action == "watch" &&
            line.Mood == "happy" &&
            line.RelationshipStages.SequenceEqual(["close"])));
    }

    private static void UnknownMoodFallsBackWithinSceneActionAndStage()
    {
        DialogueCatalog catalog = LoadCatalog();
        var selector = new DialogueSelector(catalog, new Random(23));
        IReadOnlyList<DialogueLine> selected = selector.SelectCandidates(
            new DialogueSelectionContext(
                "rest",
                "settle",
                "unknown",
                "family"));

        BehaviorTestCheck.True(selected.Count > 0);
        BehaviorTestCheck.True(selected.All(static line =>
            line.Scene == "rest" &&
            line.Action == "settle" &&
            line.RelationshipStages.Contains("family")));
    }

    private static void SemanticKeyCollapsesVocalVariants()
    {
        DialogueCatalog catalog = LoadCatalog();
        DialogueLine[] variants = catalog.Lines
            .Where(static line =>
                line.Scene == "reunion" &&
                line.Action == "greet" &&
                line.Mood == "curious" &&
                line.RelationshipStages.Contains("familiar"))
            .ToArray();

        BehaviorTestCheck.Equal(2, variants.Length);
        BehaviorTestCheck.Equal(
            variants[0].SemanticKey,
            variants[1].SemanticKey);
        BehaviorTestCheck.False(variants[0].Text == variants[1].Text);
        BehaviorTestCheck.False(variants[0].Meaning == variants[1].Meaning);
    }

    private static void SceneResolverPreservesExplicitScenesAndPrioritizesSignals()
    {
        DateTimeOffset afternoon =
            new(2026, 8, 1, 14, 0, 0, TimeSpan.FromHours(8));
        var noisyContext = new BehaviorContextSnapshot
        {
            LocalNow = afternoon,
            ApplicationCategory = "ide",
            ApplicationCategoryAvailable = true,
            IdleFor = TimeSpan.FromMinutes(20),
            IsBusy = true,
            WeatherAvailable = true,
            WeatherKind = "rain",
        };
        foreach (string explicitScene in
                 new[] { "work", "rest", "weather", "reunion" })
        {
            BehaviorTestCheck.Equal(
                explicitScene,
                DialogueSemantics.ResolveScene(
                    explicitScene,
                    noisyContext));
        }
        BehaviorTestCheck.Equal(
            "weather",
            DialogueSemantics.ResolveScene(
                "companionship",
                noisyContext));

        var restingContext = noisyContext with
        {
            WeatherAvailable = false,
        };
        BehaviorTestCheck.Equal(
            "rest",
            DialogueSemantics.ResolveScene(
                "companionship",
                restingContext));

        var busyOfficeContext = restingContext with
        {
            IdleFor = TimeSpan.Zero,
        };
        BehaviorTestCheck.Equal(
            "work",
            DialogueSemantics.ResolveScene(
                "companionship",
                busyOfficeContext));
        BehaviorTestCheck.Equal(
            "companionship",
            DialogueSemantics.ResolveScene(
                "companionship",
                busyOfficeContext with { ApplicationCategory = "browser" }));
        BehaviorTestCheck.Equal(
            "companionship",
            DialogueSemantics.ResolveScene(
                "companionship",
                busyOfficeContext with { IsBusy = false }));
    }

    private static void ActionResolverReachesEveryActionAndPrefersCueMetadata()
    {
        IReadOnlyDictionary<string, string> clipExamples =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["greet"] = "return_head_raise",
                ["nuzzle"] = "pet_headbutt_step_through",
                ["play"] = "pounce_forward_recover",
                ["watch"] = "head_tilt",
                ["settle"] = "relaxed_breathe",
            };
        foreach ((string expected, string clipId) in clipExamples)
        {
            BehaviorTestCheck.Equal(
                expected,
                DialogueSemantics.ResolveAction(clipId));
        }

        BehaviorTestCheck.Equal(
            "greet",
            DialogueSemantics.ResolveCueAction(
                "GREET",
                "pounce_forward_recover"));
        BehaviorTestCheck.Equal(
            "play",
            DialogueSemantics.ResolveCueAction(
                cueAction: null,
                "pounce_forward_recover"));
    }

    private static DialogueCatalog LoadCatalog()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.DialogueSelector.{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """
                [
                  {
                    "id": "work-watch-a",
                    "text": "喵~",
                    "meaning": "认真看着",
                    "scene": "work",
                    "action": "watch",
                    "mood": "happy",
                    "relationshipStages": ["close"],
                    "type": "dialogue",
                    "weight": 1
                  },
                  {
                    "id": "work-watch-b",
                    "text": "呼噜~",
                    "meaning": "陪你工作",
                    "scene": "work",
                    "action": "watch",
                    "mood": "happy",
                    "relationshipStages": ["close"],
                    "type": "dialogue",
                    "weight": 1
                  },
                  {
                    "id": "rest-settle",
                    "text": "喵呜~",
                    "meaning": "一起休息",
                    "scene": "rest",
                    "action": "settle",
                    "mood": "calm",
                    "relationshipStages": ["family"],
                    "type": "dialogue",
                    "weight": 1
                  },
                  {
                    "id": "reunion-a",
                    "text": "喵喵~",
                    "meaning": "欢迎回来",
                    "scene": "reunion",
                    "action": "greet",
                    "mood": "curious",
                    "relationshipStages": ["familiar"],
                    "type": "dialogue",
                    "weight": 1
                  },
                  {
                    "id": "reunion-b",
                    "text": "喵呜！",
                    "meaning": "终于回来",
                    "scene": "reunion",
                    "action": "greet",
                    "mood": "curious",
                    "relationshipStages": ["familiar"],
                    "type": "dialogue",
                    "weight": 1
                  }
                ]
                """);
            return DialogueCatalog.LoadAsync(path).GetAwaiter().GetResult();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(DialogueSelectorTests)}.{name}");
    }
}
