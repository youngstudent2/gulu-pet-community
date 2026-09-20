namespace GuluPet.Dialogue;

public sealed record DialogueSelectionContext(
    string Scene,
    string Action,
    string Mood,
    string RelationshipStage);

public sealed class DialogueSelector
{
    private readonly DialogueCatalog _catalog;
    private readonly Random _random;

    public DialogueSelector(
        DialogueCatalog catalog,
        Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _catalog = catalog;
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// Returns compatible lines in weighted random order without recording any
    /// display history. The presentation arbiter is the sole owner of display
    /// budgets and cooldown commits.
    /// </summary>
    public IReadOnlyList<DialogueLine> SelectCandidates(
        DialogueSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Mood);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.RelationshipStage);

        IReadOnlyList<DialogueLine> candidates =
            SelectBestMatchBand(context);
        return candidates
            .Select(
                line => new WeightedCandidate(
                    line,
                    -Math.Log(Math.Max(double.Epsilon, _random.NextDouble())) /
                    Math.Max(1, line.Weight)))
            .OrderBy(static candidate => candidate.Key)
            .Select(static candidate => candidate.Line)
            .ToArray();
    }

    private IReadOnlyList<DialogueLine> SelectBestMatchBand(
        DialogueSelectionContext context)
    {
        DialogueLine[] exact = _catalog.Lines
            .Where(line => Matches(line, context, matchMood: true, matchStage: true))
            .ToArray();
        if (exact.Length > 0)
        {
            return exact;
        }

        DialogueLine[] sameRelationship = _catalog.Lines
            .Where(line => Matches(line, context, matchMood: false, matchStage: true))
            .OrderBy(
                line => DialogueSemantics.MoodDistance(
                    context.Mood,
                    line.Mood))
            .Take(10)
            .ToArray();
        if (sameRelationship.Length > 0)
        {
            return sameRelationship;
        }

        DialogueLine[] adjacentRelationship = _catalog.Lines
            .Where(line => Matches(line, context, matchMood: true, matchStage: false))
            .OrderBy(
                line => line.RelationshipStages.Min(
                    stage => DialogueSemantics.RelationshipDistance(
                        context.RelationshipStage,
                        stage)))
            .Take(10)
            .ToArray();
        if (adjacentRelationship.Length > 0)
        {
            return adjacentRelationship;
        }

        return _catalog.Lines
            .Where(
                line =>
                    string.Equals(
                        line.Scene,
                        context.Scene,
                        StringComparison.OrdinalIgnoreCase))
            .OrderBy(
                line =>
                    (string.Equals(
                         line.Action,
                         context.Action,
                         StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : 10) +
                    DialogueSemantics.MoodDistance(context.Mood, line.Mood) +
                    line.RelationshipStages.Min(
                        stage => DialogueSemantics.RelationshipDistance(
                            context.RelationshipStage,
                            stage)))
            .Take(10)
            .ToArray();
    }

    private static bool Matches(
        DialogueLine line,
        DialogueSelectionContext context,
        bool matchMood,
        bool matchStage)
    {
        return string.Equals(
                   line.Scene,
                   context.Scene,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   line.Action,
                   context.Action,
                   StringComparison.OrdinalIgnoreCase) &&
               (!matchMood ||
                string.Equals(
                    line.Mood,
                    context.Mood,
                    StringComparison.OrdinalIgnoreCase)) &&
               (!matchStage ||
                line.RelationshipStages.Contains(
                    context.RelationshipStage,
                    StringComparer.OrdinalIgnoreCase));
    }

    private sealed record WeightedCandidate(DialogueLine Line, double Key);
}
