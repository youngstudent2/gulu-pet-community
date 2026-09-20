using GuluPet.Behavior;
using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class BehaviorCatalogDataTests
{
    public static void RunAll()
    {
        Run(
            nameof(CommunityCatalogContainsMinimalFallback),
            CommunityCatalogContainsMinimalFallback);
        Run(
            nameof(CommunityBehaviorReferencesManifestClip),
            CommunityBehaviorReferencesManifestClip);
        Run(
            nameof(CommunityCatalogPassesRuntimeContract),
            CommunityCatalogPassesRuntimeContract);
    }

    private static void CommunityCatalogContainsMinimalFallback()
    {
        BehaviorCatalog catalog = LoadCatalog();
        BehaviorDefinition fallback = catalog.Definitions.Single();

        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            fallback.Id);
        BehaviorTestCheck.SequenceEqual(
            [StablePetState.Normal],
            fallback.AllowedStates);
        BehaviorTestCheck.True(fallback.IsStateFallback);
        BehaviorTestCheck.False(fallback.Queueable);
        BehaviorTestCheck.Equal(
            BehaviorCompletionKind.External,
            fallback.Completion.Kind);
        BehaviorTestCheck.Equal(0, fallback.BubbleCues.Count);
    }

    private static void CommunityBehaviorReferencesManifestClip()
    {
        ValidatedRuntimeContent content = LoadContent();
        try
        {
            BehaviorDefinition fallback = content.Behaviors.Definitions.Single();
            string[] requiredClips = fallback.Animation
                .RequiredClipIds()
                .ToArray();

            BehaviorTestCheck.SequenceEqual(["idle_breathe"], requiredClips);
            BehaviorTestCheck.True(
                content.Animations.TryGet(requiredClips[0], out var clip));
            BehaviorTestCheck.True(clip!.Definition.Loop);
            BehaviorTestCheck.Equal(
                RuntimeContentDescriptor.CommunityContract.FramesPerSecond,
                clip.Definition.Fps);
        }
        finally
        {
            content.Animations.Dispose();
        }
    }

    private static void CommunityCatalogPassesRuntimeContract()
    {
        ValidatedRuntimeContent content = LoadContent();
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
        }
        finally
        {
            content.Animations.Dispose();
        }
    }

    private static BehaviorCatalog LoadCatalog() =>
        BehaviorCatalog.LoadAsync(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "Data",
                    "behaviors.json"))
            .GetAwaiter()
            .GetResult();

    private static ValidatedRuntimeContent LoadContent() =>
        RuntimeContentContract.LoadAndValidateAsync(
                Path.Combine(AppContext.BaseDirectory, "Assets"),
                decodePixelWidth: 0)
            .GetAwaiter()
            .GetResult();

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(BehaviorCatalogDataTests)}.{name}");
    }
}
