namespace GuluPet.Dialogue;

public enum BubbleType
{
    Reaction,
    Dialogue,
    Thought,
}

public sealed class DialogueLine
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    public required string Meaning { get; init; }

    public required string Scene { get; init; }

    public required string Action { get; init; }

    public required string Mood { get; init; }

    public required IReadOnlyList<string> RelationshipStages { get; init; }

    public BubbleType Type { get; init; } = BubbleType.Dialogue;

    public int Weight { get; init; } = 1;

    public string SemanticKey =>
        string.Join(
            '|',
            Scene,
            Action,
            Mood,
            string.Join(',', RelationshipStages));
}
