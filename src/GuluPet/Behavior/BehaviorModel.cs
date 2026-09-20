using System.Text.Json;
using System.Text.Json.Serialization;
using GuluPet.Domain;

namespace GuluPet.Behavior;

[JsonConverter(typeof(BehaviorIdJsonConverter))]
public readonly record struct BehaviorId
{
    public BehaviorId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 96 ||
            value.Any(static character =>
                character is not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '_'
                and not '-'))
        {
            throw new ArgumentException(
                "Behavior ids must contain only lowercase ASCII letters, digits, '_' or '-'.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator BehaviorId(string value) => new(value);

    public static implicit operator string(BehaviorId id) => id.Value;
}

public sealed class BehaviorIdJsonConverter : JsonConverter<BehaviorId>
{
    public override BehaviorId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return new BehaviorId(
            value ?? throw new JsonException("Behavior id cannot be null."));
    }

    public override void Write(
        Utf8JsonWriter writer,
        BehaviorId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public enum StablePetState
{
    Normal,
    Sleeping,
    Dragging
}

public enum PetLifecyclePhase
{
    Idle,
    SleepingEntering,
    SleepingLooping,
    SleepingExiting,
    DraggingFollowing
}

public enum BehaviorRequestSource
{
    StateFallback,
    StateContinuation,
    ActiveTick,
    Weather,
    UserContext,
    Scheduled,
    Interaction,
    ForcedInteraction,
    ManualPreview,
    TestControl
}

public enum BehaviorSwitchMode
{
    Queued,
    ImmediateIfIdle
}

public enum BehaviorCompletionKind
{
    ClipEnd,
    Duration,
    LoopCount,
    External
}

public enum BehaviorPhase
{
    Pending,
    AwaitingFirstFrame,
    Entering,
    Performing,
    Looping,
    Exiting,
    Terminal
}

public enum BehaviorTerminalStatus
{
    Completed,
    Interrupted,
    Superseded,
    Expired,
    Failed,
    TimedOut
}

public enum BehaviorDisturbanceLevel
{
    SilentMicro = 0,
    Subtle = 1,
    Noticeable = 2,
    Strong = 3
}

public enum BehaviorEmotionTiming
{
    Started,
    Completed
}

public sealed record BehaviorRequest
{
    public required long RequestId { get; init; }

    public required BehaviorId BehaviorId { get; init; }

    public required BehaviorRequestSource Source { get; init; }

    public required BehaviorSwitchMode SwitchMode { get; init; }

    public required int Priority { get; init; }

    public required TimeSpan CreatedAt { get; init; }

    public required TimeSpan FirstCreatedAt { get; init; }

    public required TimeSpan ExpiresAt { get; init; }

    public required string DedupeKey { get; init; }

    public long ContextRevision { get; init; }

    public string? InteractionKey { get; init; }

    public string? InteractionTriggerTag { get; init; }

    public long? InteractionLeaseGeneration { get; init; }

    public BehaviorDailyOpportunity? DailyOpportunity { get; init; }

    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public bool IsExpired(TimeSpan now) => now >= ExpiresAt;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DedupeKey);
        if (CreatedAt < TimeSpan.Zero ||
            FirstCreatedAt < TimeSpan.Zero ||
            ExpiresAt <= CreatedAt ||
            FirstCreatedAt > CreatedAt)
        {
            throw new InvalidOperationException(
                "Behavior request timestamps are inconsistent.");
        }

        ArgumentNullException.ThrowIfNull(Parameters);

        bool hasInteractionMetadata =
            InteractionKey is not null ||
            InteractionTriggerTag is not null ||
            InteractionLeaseGeneration is not null;
        if (hasInteractionMetadata &&
            (string.IsNullOrWhiteSpace(InteractionKey) ||
             string.IsNullOrWhiteSpace(InteractionTriggerTag) ||
             InteractionLeaseGeneration is null or <= 0))
        {
            throw new InvalidOperationException(
                "Interaction request metadata must contain a key, trigger tag and positive lease generation.");
        }

        if (DailyOpportunity is { } dailyOpportunity)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dailyOpportunity.Key);
            if (Source is not
                (BehaviorRequestSource.Scheduled or
                 BehaviorRequestSource.Weather))
            {
                throw new InvalidOperationException(
                    "Daily opportunity metadata is only valid for scheduled or weather requests.");
            }

            if (dailyOpportunity.AcceptedCount < 0 ||
                DateOnly.FromDateTime(
                    dailyOpportunity.ObservedAt.Date) !=
                dailyOpportunity.LocalDate ||
                dailyOpportunity.LastAcceptedAt is { } lastAcceptedAt &&
                (DateOnly.FromDateTime(lastAcceptedAt.Date) !=
                     dailyOpportunity.LocalDate ||
                 lastAcceptedAt > dailyOpportunity.ObservedAt))
            {
                throw new InvalidOperationException(
                    "Daily opportunity metadata is inconsistent.");
            }
        }
    }
}

public sealed class BehaviorDefinition
{
    public required BehaviorId Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Family { get; init; }

    public required string ClipFamily { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyList<StablePetState> AllowedStates { get; init; } =
        [StablePetState.Normal];

    public StablePetState? TargetState { get; init; }

    public required BehaviorAnimationPlan Animation { get; init; }

    public IReadOnlyList<BehaviorBubbleCue> BubbleCues { get; init; } = [];

    public required BehaviorCompletionPolicy Completion { get; init; }

    public TimeSpan MinimumCommitTime { get; init; }

    public TimeSpan MaximumDuration { get; init; } = TimeSpan.FromSeconds(8);

    public BehaviorDisturbanceLevel DisturbanceLevel { get; init; }

    public TimeSpan Cooldown { get; init; }

    public string? CooldownGroup { get; init; }

    public EmotionDelta StartedEmotionEffect { get; init; }

    public EmotionDelta CompletedEmotionEffect { get; init; }

    public BehaviorUtilityRule Utility { get; init; } = new();

    public BehaviorAutomaticEligibilityRule AutomaticEligibility { get; init; } =
        new();

    public bool Queueable { get; init; } = true;

    public bool IsStateFallback { get; init; }
}

public sealed class BehaviorAnimationPlan
{
    /// <summary>
    /// Identifies the default clip combination when alternative session
    /// variants are declared. Existing fixed plans leave this null.
    /// </summary>
    public string? VariantId { get; init; }

    public string? EnterClipId { get; init; }

    public string? PerformClipId { get; init; }

    public string? LoopClipId { get; init; }

    public string? ExitClipId { get; init; }

    public bool ContinueIfSameClip { get; init; }

    /// <summary>
    /// Alternative clip combinations selected once when a behavior session
    /// starts. Every variant must define the same clip roles as the default
    /// plan so a lifecycle can never mix poses between phases.
    /// </summary>
    public IReadOnlyList<BehaviorAnimationVariant> Variants { get; init; } = [];

    public IEnumerable<string> RequiredClipIds()
    {
        if (!string.IsNullOrWhiteSpace(EnterClipId))
        {
            yield return EnterClipId;
        }

        if (!string.IsNullOrWhiteSpace(PerformClipId))
        {
            yield return PerformClipId;
        }

        if (!string.IsNullOrWhiteSpace(LoopClipId))
        {
            yield return LoopClipId;
        }

        if (!string.IsNullOrWhiteSpace(ExitClipId))
        {
            yield return ExitClipId;
        }

        foreach (BehaviorAnimationVariant variant in Variants)
        {
            foreach (string clipId in variant.RequiredClipIds())
            {
                yield return clipId;
            }
        }
    }
}

public sealed class BehaviorAnimationVariant
{
    public required string Id { get; init; }

    public string? EnterClipId { get; init; }

    public string? PerformClipId { get; init; }

    public string? LoopClipId { get; init; }

    public string? ExitClipId { get; init; }

    public IEnumerable<string> RequiredClipIds()
    {
        if (!string.IsNullOrWhiteSpace(EnterClipId))
        {
            yield return EnterClipId;
        }

        if (!string.IsNullOrWhiteSpace(PerformClipId))
        {
            yield return PerformClipId;
        }

        if (!string.IsNullOrWhiteSpace(LoopClipId))
        {
            yield return LoopClipId;
        }

        if (!string.IsNullOrWhiteSpace(ExitClipId))
        {
            yield return ExitClipId;
        }
    }
}

public sealed class BehaviorCompletionPolicy
{
    public BehaviorCompletionKind Kind { get; init; } = BehaviorCompletionKind.ClipEnd;

    public TimeSpan? Duration { get; init; }

    public int? LoopCount { get; init; }
}

public sealed class BehaviorBubbleCue
{
    public TimeSpan At { get; init; }

    public string? DialogueId { get; init; }

    public string? DialoguePoolId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Action { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Annotation { get; init; }

    public TimeSpan? VisibleFor { get; init; }

    public int Priority { get; init; }
}

public sealed class BehaviorUtilityRule
{
    public double BaseScore { get; init; } = 25;

    public IReadOnlyList<string> EmotionTags { get; init; } = [];

    public IReadOnlyList<string> PreferredApplicationCategories { get; init; } = [];

    public IReadOnlyList<string> PreferredWeatherKinds { get; init; } = [];

    public int MaxConsecutiveRuns { get; init; } = 2;
}

/// <summary>
/// Optional context gates applied only to automatic utility selections. Empty
/// rules allow every context. Multiple configured constraints are all required
/// unless <see cref="MatchAny"/> is set.
/// </summary>
public sealed class BehaviorAutomaticEligibilityRule
{
    public IReadOnlyList<string> TimeSlots { get; init; } = [];

    public TimeSpan? MinimumIdleFor { get; init; }

    public bool MatchAny { get; init; }
}

public sealed record BehaviorSessionToken(long Value)
{
    public override string ToString() => Value.ToString();
}

public sealed record BehaviorPlaybackToken(long Value)
{
    public override string ToString() => Value.ToString();
}

public sealed record BehaviorTerminalRecord(
    BehaviorSessionToken SessionToken,
    long RequestId,
    BehaviorId BehaviorId,
    BehaviorTerminalStatus Status,
    TimeSpan OccurredAt,
    string? Reason);

public sealed record BehaviorSessionSnapshot
{
    public required BehaviorSessionToken SessionToken { get; init; }

    public required BehaviorId BehaviorId { get; init; }

    public required BehaviorPhase Phase { get; init; }

    public required bool Committed { get; init; }

    public required string? ClipId { get; init; }

    public required TimeSpan LogicalTime { get; init; }

    public required int NextCueIndex { get; init; }

    public required BehaviorTerminalRecord? Terminal { get; init; }
}

public sealed record BehaviorContextSnapshot
{
    public long Revision { get; init; }

    public DateTimeOffset LocalNow { get; init; }

    public string ApplicationCategory { get; init; } = "unknown";

    public bool ApplicationCategoryAvailable { get; init; }

    public TimeSpan ApplicationStableFor { get; init; }

    public double KeyboardPerMinute { get; init; }

    public double MouseClicksPerMinute { get; init; }

    public long KeyboardCountSinceStart { get; init; }

    public long MouseClickCountSinceStart { get; init; }

    public TimeSpan IdleFor { get; init; }

    public bool IsBusy { get; init; }

    public bool IsFullscreen { get; init; }

    public bool IsLocked { get; init; }

    public string WeatherKind { get; init; } = "unavailable";

    public bool WeatherAvailable { get; init; }

    public double TemperatureCelsius { get; init; }

    public static BehaviorContextSnapshot Empty { get; } = new();
}

public sealed record BehaviorEvaluationBreakdown
{
    public double Base { get; init; }

    public double Emotion { get; init; }

    public double Time { get; init; }

    public double Activity { get; init; }

    public double Application { get; init; }

    public double Weather { get; init; }

    public double TriggerBoost { get; init; }

    public double TimeSinceLastRun { get; init; }

    public double Relationship { get; init; }

    public double SameBehaviorPenalty { get; init; }

    public double SameFamilyPenalty { get; init; }

    public double SameClipPenalty { get; init; }

    public double DisturbUserPenalty { get; init; }

    public double Total { get; init; }
}

public sealed record BehaviorEvaluation
{
    public required BehaviorId BehaviorId { get; init; }

    public required bool IsEligible { get; init; }

    public required string? RejectionReason { get; init; }

    public required BehaviorEvaluationBreakdown Breakdown { get; init; }

    public int Rank { get; init; }

    public double Probability { get; init; }
}

public sealed record BehaviorSelectionPlan
{
    public required IReadOnlyList<BehaviorEvaluation> AllEvaluations { get; init; }

    public required IReadOnlyList<BehaviorEvaluation> TopCandidates { get; init; }

    public bool RandomConsumed { get; init; }
}

public sealed record BehaviorSelectionResult
{
    public required BehaviorId? SelectedBehaviorId { get; init; }

    public required IReadOnlyList<BehaviorId> AttemptOrder { get; init; }

    public required BehaviorSelectionPlan Plan { get; init; }
}
