namespace GuluPet.Behavior;

internal static class BehaviorDefinitionSemantics
{
    private static readonly HashSet<string> InteractionTriggerTags =
        new(StringComparer.Ordinal)
        {
            "trigger:click",
            "trigger:repeat_click",
            "trigger:long_press",
            "trigger:petting",
            "trigger:pet_end",
            "trigger:drag_release",
            "trigger:hover",
            "trigger:approach",
            "trigger:rapid_pointer",
            "trigger:circle_pointer",
            "trigger:wake",
        };

    public static bool RequiresFirstFrameInteractionBubble(
        BehaviorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return
            definition.Tags.Contains(
                "fallback:interaction",
                StringComparer.Ordinal) ||
            definition.Tags.Any(InteractionTriggerTags.Contains);
    }

    public static bool IsAutomaticExternalStateLifecycle(
        BehaviorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return
            !definition.Queueable &&
            !definition.IsStateFallback &&
            definition.TargetState is not null &&
            definition.Completion.Kind == BehaviorCompletionKind.External &&
            !string.IsNullOrWhiteSpace(definition.Animation.EnterClipId) &&
            !string.IsNullOrWhiteSpace(definition.Animation.LoopClipId) &&
            !string.IsNullOrWhiteSpace(definition.Animation.ExitClipId) &&
            definition.Tags.Any(static tag =>
                tag.StartsWith("trigger:", StringComparison.Ordinal));
    }

    public static bool IsDailyOpportunityBehavior(
        BehaviorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Tags.Any(static tag =>
            tag.StartsWith(
                "trigger:time:",
                StringComparison.Ordinal) ||
            tag.StartsWith(
                "trigger:weather:",
                StringComparison.Ordinal));
    }
}
