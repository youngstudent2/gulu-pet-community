using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuluPet.Dialogue;

public sealed class DialogueCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private DialogueCatalog(IReadOnlyList<DialogueLine> lines)
    {
        Lines = lines;
    }

    public IReadOnlyList<DialogueLine> Lines { get; }

    public static async Task<DialogueCatalog> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var lines = await JsonSerializer.DeserializeAsync<List<DialogueLine>>(
            stream,
            JsonOptions,
            cancellationToken);

        if (lines is null || lines.Count == 0)
        {
            throw new InvalidDataException($"Dialogue catalog is empty: {path}");
        }

        var duplicateId = lines
            .GroupBy(static line => line.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicateId is not null)
        {
            throw new InvalidDataException($"Duplicate dialogue id: {duplicateId.Key}");
        }

        var invalidLine = lines.FirstOrDefault(
            static line => !CatVocalization.IsValid(line.Text));
        if (invalidLine is not null)
        {
            throw new InvalidDataException(
                $"Dialogue '{invalidLine.Id}' is not a valid cat vocalization.");
        }

        foreach (DialogueLine line in lines)
        {
            if (!DialogueSemantics.IsValidScene(line.Scene) ||
                !DialogueSemantics.IsValidAction(line.Action) ||
                !DialogueSemantics.IsValidMood(line.Mood) ||
                line.RelationshipStages.Count == 0 ||
                line.RelationshipStages.Any(
                    static stage =>
                        !DialogueSemantics.IsValidRelationshipStage(stage)) ||
                !CatDialogueAnnotation.IsValid(line.Meaning))
            {
                throw new InvalidDataException(
                    $"Dialogue '{line.Id}' has invalid semantic dimensions.");
            }
        }

        return new DialogueCatalog(lines);
    }
}
