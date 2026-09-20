namespace GuluPet.Runtime;

internal enum UserInteractionKind
{
    Click,
    LongPress,
    Drag,
    DockLeft,
    DockRight,
    Approach,
    Hover,
    Petting,
    PettingEnded,
    ToolbarPetting,
    RapidPointer,
    CirclePointer,
    Feed,
    Water,
    OutingStart,
    PostcardClaim,
    MemoryReplay,
    ManualBehavior,
}

internal sealed class UserInteractionAcceptedEventArgs : EventArgs
{
    internal UserInteractionAcceptedEventArgs(
        UserInteractionKind kind,
        DateTimeOffset occurredAtUtc,
        string? detail = null)
    {
        Kind = kind;
        OccurredAtUtc = occurredAtUtc;
        Detail = detail;
    }

    internal UserInteractionKind Kind { get; }

    internal DateTimeOffset OccurredAtUtc { get; }

    internal string? Detail { get; }
}

internal sealed class RuntimeContextObservedEventArgs(
    GuluPet.Behavior.BehaviorContextSnapshot snapshot) : EventArgs
{
    internal GuluPet.Behavior.BehaviorContextSnapshot Snapshot { get; } =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));
}
