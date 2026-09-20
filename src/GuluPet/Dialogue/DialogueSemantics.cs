using GuluPet.Behavior;
using GuluPet.Domain;

namespace GuluPet.Dialogue;

public static class DialogueSemantics
{
    public static IReadOnlyList<string> Scenes { get; } =
        ["companionship", "work", "rest", "weather", "reunion"];

    public static IReadOnlyList<string> Actions { get; } =
        ["greet", "nuzzle", "play", "watch", "settle"];

    public static IReadOnlyList<string> Moods { get; } =
        ["calm", "curious", "happy", "sleepy", "uneasy"];

    public static IReadOnlyList<string> RelationshipStages { get; } =
        [
            GuluPet.Domain.RelationshipStages.New,
            GuluPet.Domain.RelationshipStages.Familiar,
            GuluPet.Domain.RelationshipStages.Close,
            GuluPet.Domain.RelationshipStages.Family,
        ];

    public static bool IsValidScene(string? value) =>
        Contains(Scenes, value);

    public static bool IsValidAction(string? value) =>
        Contains(Actions, value);

    public static bool IsValidMood(string? value) =>
        Contains(Moods, value);

    public static bool IsValidRelationshipStage(string? value) =>
        Contains(RelationshipStages, value);

    public static string ResolveScene(
        string? cueScene,
        BehaviorContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsValidScene(cueScene) &&
            !string.Equals(
                cueScene,
                "companionship",
                StringComparison.OrdinalIgnoreCase))
        {
            return cueScene!.ToLowerInvariant();
        }

        if (context.WeatherAvailable &&
            !string.Equals(
                context.WeatherKind,
                "clear",
                StringComparison.OrdinalIgnoreCase))
        {
            return "weather";
        }

        if (context.IdleFor >= TimeSpan.FromMinutes(15) ||
            context.LocalNow.Hour is >= 22 or < 7)
        {
            return "rest";
        }

        if (context.IsBusy &&
            (context.ApplicationCategory.Equals(
                 "office",
                 StringComparison.OrdinalIgnoreCase) ||
             context.ApplicationCategory.Equals(
                 "ide",
                 StringComparison.OrdinalIgnoreCase)))
        {
            return "work";
        }

        return "companionship";
    }

    public static string ResolveAction(string? clipId)
    {
        string value = clipId?.ToLowerInvariant() ?? string.Empty;
        if (ContainsAny(value, "greet", "wave", "hello", "return", "head_raise"))
        {
            return "greet";
        }

        if (ContainsAny(value, "nuzzle", "rub", "pet", "headbutt", "flop"))
        {
            return "nuzzle";
        }

        if (ContainsAny(
                value,
                "play",
                "pounce",
                "chase",
                "paw",
                "jump",
                "hop",
                "boxing"))
        {
            return "play";
        }

        if (ContainsAny(
                value,
                "watch",
                "look",
                "peek",
                "sniff",
                "listen",
                "tilt",
                "attention"))
        {
            return "watch";
        }

        return "settle";
    }

    public static string ResolveCueAction(
        string? cueAction,
        string? clipId) =>
        IsValidAction(cueAction)
            ? cueAction!.ToLowerInvariant()
            : ResolveAction(clipId);

    public static string ResolveMood(EmotionState emotions)
    {
        ArgumentNullException.ThrowIfNull(emotions);
        var scores = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["uneasy"] = Math.Max(emotions.Stress - 20, emotions.Boredom - 55),
            ["sleepy"] = Math.Max(emotions.Sleepiness - 35, 35 - emotions.Energy),
            ["happy"] = Math.Max(emotions.Happiness - 60, emotions.Affection - 60),
            ["curious"] = emotions.Curiosity - 55,
            ["calm"] = 15 - emotions.Stress,
        };
        return scores.MaxBy(static entry => entry.Value).Key;
    }

    public static int MoodDistance(string left, string right) =>
        Distance(Moods, left, right);

    public static int RelationshipDistance(string left, string right) =>
        Distance(RelationshipStages, left, right);

    private static bool Contains(
        IReadOnlyList<string> values,
        string? value) =>
        value is not null &&
        values.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static bool ContainsAny(
        string value,
        params string[] fragments) =>
        fragments.Any(
            fragment => value.Contains(fragment, StringComparison.Ordinal));

    private static int Distance(
        IReadOnlyList<string> values,
        string left,
        string right)
    {
        int leftIndex = IndexOf(values, left);
        int rightIndex = IndexOf(values, right);
        return leftIndex < 0 || rightIndex < 0
            ? values.Count
            : Math.Abs(leftIndex - rightIndex);
    }

    private static int IndexOf(
        IReadOnlyList<string> values,
        string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(
                    values[index],
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
