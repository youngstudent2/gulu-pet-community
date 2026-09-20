using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuluPet.Dialogue;

namespace GuluPet.Behavior;

public sealed class BehaviorCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly IReadOnlyDictionary<BehaviorId, BehaviorDefinition> _definitions;

    public BehaviorCatalog(IEnumerable<BehaviorDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var materialized = definitions.ToArray();
        if (materialized.Length == 0)
        {
            throw new InvalidDataException("At least one behavior definition is required.");
        }

        var map = new Dictionary<BehaviorId, BehaviorDefinition>();
        foreach (var definition in materialized)
        {
            ValidateDefinition(definition);
            if (!map.TryAdd(definition.Id, definition))
            {
                throw new InvalidDataException(
                    $"Duplicate behavior id '{definition.Id}'.");
            }
        }

        _definitions = map;
    }

    public IReadOnlyCollection<BehaviorDefinition> Definitions =>
        _definitions.Values.ToArray();

    public BehaviorDefinition this[BehaviorId id] => _definitions[id];

    public bool TryGet(BehaviorId id, out BehaviorDefinition? definition) =>
        _definitions.TryGetValue(id, out definition);

    public static async Task<BehaviorCatalog> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<BehaviorCatalogDocument>(
            stream,
            JsonOptions,
            cancellationToken);
        if (document is null || document.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported or missing behavior catalog schema in '{path}'.");
        }

        return new BehaviorCatalog(document.Behaviors);
    }

    public static string Serialize(IEnumerable<BehaviorDefinition> definitions) =>
        JsonSerializer.Serialize(
            new BehaviorCatalogDocument
            {
                SchemaVersion = 1,
                Behaviors = definitions.ToArray(),
            },
            JsonOptions);

    private static void ValidateDefinition(BehaviorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Family);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ClipFamily);
        ArgumentNullException.ThrowIfNull(definition.Tags);
        ArgumentNullException.ThrowIfNull(definition.AllowedStates);
        ArgumentNullException.ThrowIfNull(definition.Animation);
        ArgumentNullException.ThrowIfNull(definition.Animation.Variants);
        ArgumentNullException.ThrowIfNull(definition.BubbleCues);
        ArgumentNullException.ThrowIfNull(definition.Completion);
        ArgumentNullException.ThrowIfNull(definition.Utility);
        ArgumentNullException.ThrowIfNull(definition.AutomaticEligibility);

        if (definition.AllowedStates.Count == 0)
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Id}' has no allowed states.");
        }

        ValidateAnimationVariants(definition);

        var clips = definition.Animation.RequiredClipIds().ToArray();
        if (clips.Length == 0)
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Id}' has no animation clips.");
        }

        if (definition.MinimumCommitTime < TimeSpan.Zero ||
            definition.MaximumDuration <= TimeSpan.Zero ||
            definition.Cooldown < TimeSpan.Zero)
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Id}' has an invalid duration or cooldown.");
        }

        if (definition.Queueable &&
            (definition.Completion.Kind == BehaviorCompletionKind.External ||
             definition.MaximumDuration > TimeSpan.FromSeconds(20)))
        {
            throw new InvalidDataException(
                $"Queueable behavior '{definition.Id}' must be finite and no longer than 20 seconds.");
        }

        if (definition.Completion.Kind == BehaviorCompletionKind.Duration &&
            (definition.Completion.Duration is null ||
             definition.Completion.Duration <= TimeSpan.Zero ||
             definition.Completion.Duration > definition.MaximumDuration))
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Id}' has an invalid duration completion policy.");
        }

        if (definition.Completion.Kind == BehaviorCompletionKind.LoopCount &&
            definition.Completion.LoopCount is null or <= 0)
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Id}' has an invalid loop-count completion policy.");
        }

        if (definition.BubbleCues.Count > 3)
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Id}' contains more than three bubble cues.");
        }

        var orderedCues = definition.BubbleCues
            .Select(static (cue, index) => (Cue: cue, Index: index))
            .OrderBy(static item => item.Cue.At)
            .ThenBy(static item => item.Index)
            .ToArray();
        for (var index = 0; index < orderedCues.Length; index++)
        {
            var cue = orderedCues[index].Cue;
            if (cue.At < TimeSpan.Zero ||
                cue.At > definition.MaximumDuration ||
                cue.VisibleFor <= TimeSpan.Zero)
            {
                throw new InvalidDataException(
                    $"Behavior '{definition.Id}' contains an invalid bubble cue.");
            }

            var hasDialogue = !string.IsNullOrWhiteSpace(cue.DialogueId);
            var hasPool = !string.IsNullOrWhiteSpace(cue.DialoguePoolId);
            if (hasDialogue == hasPool)
            {
                throw new InvalidDataException(
                    $"Behavior '{definition.Id}' bubble cues require exactly one dialogue source.");
            }

            if (hasPool &&
                (!DialogueSemantics.IsValidScene(cue.DialoguePoolId) ||
                 !DialogueSemantics.IsValidAction(cue.Action)))
            {
                throw new InvalidDataException(
                    $"Behavior '{definition.Id}' pool bubble cue requires " +
                    "valid semantic scene and action values.");
            }

            if (cue.Annotation is not null &&
                !CatDialogueAnnotation.IsValid(cue.Annotation))
            {
                throw new InvalidDataException(
                    $"Behavior '{definition.Id}' contains an invalid cat dialogue annotation.");
            }

            if (index > 0 &&
                cue.At - orderedCues[index - 1].Cue.At <
                TimeSpan.FromMilliseconds(800))
            {
                throw new InvalidDataException(
                    $"Behavior '{definition.Id}' bubble cues must be at least 800ms apart.");
            }
        }

        if (definition.DisturbanceLevel is < BehaviorDisturbanceLevel.SilentMicro
            or > BehaviorDisturbanceLevel.Strong)
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Id}' has an invalid disturbance level.");
        }

        string[] invalidTimeSlots = definition.AutomaticEligibility.TimeSlots
            .Where(static slot =>
                slot is not "morning" and
                not "noon" and
                not "evening" and
                not "late")
            .ToArray();
        if (invalidTimeSlots.Length > 0 ||
            definition.AutomaticEligibility.MinimumIdleFor < TimeSpan.Zero ||
            (definition.AutomaticEligibility.MatchAny &&
             definition.AutomaticEligibility.TimeSlots.Count == 0 &&
             definition.AutomaticEligibility.MinimumIdleFor is null))
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Id}' has an invalid automatic eligibility rule.");
        }

        if (definition.Utility.BaseScore is < 0 or > 100 ||
            definition.Utility.MaxConsecutiveRuns < 0)
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Id}' has an invalid utility rule.");
        }
    }

    private static void ValidateAnimationVariants(BehaviorDefinition definition)
    {
        BehaviorAnimationPlan animation = definition.Animation;
        if (animation.Variants.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(animation.VariantId))
            {
                throw new InvalidDataException(
                    $"Behavior '{definition.Id}' declares a default animation " +
                    "variant id without alternative variants.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(animation.VariantId))
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Id}' animation variants require a " +
                "default variant id.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal)
        {
            animation.VariantId,
        };
        ClipRoleMask defaultRoles = Roles(
            animation.EnterClipId,
            animation.PerformClipId,
            animation.LoopClipId,
            animation.ExitClipId);
        foreach (BehaviorAnimationVariant variant in animation.Variants)
        {
            if (variant is null || string.IsNullOrWhiteSpace(variant.Id))
            {
                throw new InvalidDataException(
                    $"Behavior '{definition.Id}' contains an animation variant " +
                    "without an id.");
            }

            if (!ids.Add(variant.Id))
            {
                throw new InvalidDataException(
                    $"Behavior '{definition.Id}' contains duplicate animation " +
                    $"variant id '{variant.Id}'.");
            }

            ClipRoleMask variantRoles = Roles(
                variant.EnterClipId,
                variant.PerformClipId,
                variant.LoopClipId,
                variant.ExitClipId);
            if (variantRoles != defaultRoles)
            {
                throw new InvalidDataException(
                    $"Behavior '{definition.Id}' animation variant " +
                    $"'{variant.Id}' must define the same clip roles as the " +
                    "default variant.");
            }
        }
    }

    private static ClipRoleMask Roles(
        string? enterClipId,
        string? performClipId,
        string? loopClipId,
        string? exitClipId)
    {
        var roles = ClipRoleMask.None;
        if (!string.IsNullOrWhiteSpace(enterClipId))
        {
            roles |= ClipRoleMask.Enter;
        }

        if (!string.IsNullOrWhiteSpace(performClipId))
        {
            roles |= ClipRoleMask.Perform;
        }

        if (!string.IsNullOrWhiteSpace(loopClipId))
        {
            roles |= ClipRoleMask.Loop;
        }

        if (!string.IsNullOrWhiteSpace(exitClipId))
        {
            roles |= ClipRoleMask.Exit;
        }

        return roles;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    [Flags]
    private enum ClipRoleMask
    {
        None = 0,
        Enter = 1 << 0,
        Perform = 1 << 1,
        Loop = 1 << 2,
        Exit = 1 << 3,
    }

    private sealed class BehaviorCatalogDocument
    {
        public int SchemaVersion { get; init; }

        public IReadOnlyList<BehaviorDefinition> Behaviors { get; init; } = [];
    }
}
