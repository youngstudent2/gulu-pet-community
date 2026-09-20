using GuluPet.Domain;

namespace GuluPet.Behavior;

public sealed record BehaviorUtilityEvaluationInput
{
    public required StablePetState State { get; init; }

    public required BehaviorRequestSource Source { get; init; }

    public required EmotionState Emotions { get; init; }

    public BehaviorContextSnapshot Context { get; init; } =
        BehaviorContextSnapshot.Empty;

    public IReadOnlyCollection<BehaviorId>? CandidateBehaviorIds { get; init; }
}

public sealed record BehaviorUtilityHistorySnapshot
{
    public required int CommitCount { get; init; }

    public required IReadOnlyDictionary<BehaviorId, TimeSpan> LastCommittedAt
    {
        get;
        init;
    }

    public required IReadOnlyList<BehaviorId> RecentBehaviors { get; init; }

    public required IReadOnlyList<string> RecentFamilies { get; init; }

    public required IReadOnlyList<string> RecentClipFamilies { get; init; }

    public required BehaviorId? LastBehaviorId { get; init; }

    public required int ConsecutiveRuns { get; init; }
}

public sealed class BehaviorUtilityEngine
{
    private const double Temperature = 18d;
    private const double ActiveProbabilityCap = 0.45d;
    private const double InteractionProbabilityCap = 0.55d;
    private const double ActiveMinimumScore = 10d;

    private static readonly IReadOnlyDictionary<
        string,
        EmotionCoefficients> EmotionTagCoefficients =
        new Dictionary<string, EmotionCoefficients>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["socialwarm"] = new(2, -2, 0, 2, 6, -7, 8),
            ["independent"] = new(0, 0, 2, 2, 0, 2, -2),
            ["irritated"] = new(0, 0, 0, 0, -4, 10, -4),
            ["curious"] = new(3, -3, 4, 10, 2, -4, 0),
            ["play"] = new(8, -8, 7, 5, 3, -6, 1),
            ["rest"] = new(-5, 7, -1, -2, 0, -2, 0),
            ["sleep"] = new(-7, 12, 0, -4, 0, -5, 0),
            ["selfcare"] = new(-1, 1, 2, 0, 2, 3, 0),
        };

    private static readonly double[] SameBehaviorPenalties = [40, 25, 12];
    private static readonly double[] SameFamilyPenalties = [20, 10];
    private static readonly double[] SameClipPenalties = [18, 8];
    private static readonly double[] BusyDisturbancePenalties = [0, 6, 16, 30];
    private static readonly double[] LateDisturbancePenalties = [0, 2, 8, 18];

    private readonly BehaviorCatalog _catalog;
    private readonly IMonotonicClock _clock;
    private readonly IRandomSource _selectionRandom;
    private readonly Dictionary<BehaviorId, TimeSpan> _lastCommittedAt = [];
    private readonly Dictionary<string, TimeSpan> _lastCooldownGroupAt =
        new(StringComparer.Ordinal);
    private readonly List<BehaviorId> _recentBehaviors = [];
    private readonly List<string> _recentFamilies = [];
    private readonly List<string> _recentClipFamilies = [];
    private BehaviorId? _lastBehaviorId;
    private int _consecutiveRuns;
    private int _commitCount;
    private TimeSpan? _lastObservedAt;

    public BehaviorUtilityEngine(
        BehaviorCatalog catalog,
        IMonotonicClock? clock = null,
        IRandomSource? selectionRandom = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _clock = clock ?? SystemMonotonicClock.Instance;
        _selectionRandom = selectionRandom ?? new SystemRandomSource();
        ObserveClock();
    }

    public BehaviorUtilityEngine(
        BehaviorCatalog catalog,
        BehaviorRandomStreams randomStreams,
        IMonotonicClock? clock = null)
        : this(
            catalog,
            clock,
            (randomStreams
                ?? throw new ArgumentNullException(nameof(randomStreams)))
                .UtilitySelection)
    {
    }

    public BehaviorSelectionPlan Evaluate(BehaviorUtilityEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Emotions);
        ArgumentNullException.ThrowIfNull(input.Context);

        var now = ObserveClock();
        var definitions = ResolveCandidates(input.CandidateBehaviorIds);
        var rawEvaluations = definitions
            .Select(definition => EvaluateDefinition(definition, input, now))
            .ToArray();
        var rankedEligible = rawEvaluations
            .Where(static evaluation => evaluation.IsEligible)
            .OrderByDescending(static evaluation => evaluation.Breakdown.Total)
            .ThenBy(
                static evaluation => evaluation.BehaviorId.Value,
                StringComparer.Ordinal)
            .ToArray();
        var topRaw = rankedEligible.Take(5).ToArray();
        var probabilities = CalculateProbabilities(
            topRaw,
            IsDirectInteraction(input.Source)
                ? InteractionProbabilityCap
                : ActiveProbabilityCap);
        var ranks = rankedEligible
            .Select(
                static (evaluation, index) =>
                    (evaluation.BehaviorId, Rank: index + 1))
            .ToDictionary(static item => item.BehaviorId, static item => item.Rank);
        var probabilityById = topRaw
            .Select(
                (evaluation, index) =>
                    (evaluation.BehaviorId, Probability: probabilities[index]))
            .ToDictionary(
                static item => item.BehaviorId,
                static item => item.Probability);

        var finalized = rawEvaluations
            .Select(
                evaluation =>
                    evaluation with
                    {
                        Rank = ranks.GetValueOrDefault(evaluation.BehaviorId),
                        Probability = probabilityById.GetValueOrDefault(
                            evaluation.BehaviorId),
                    })
            .ToArray();
        var finalizedById = finalized.ToDictionary(
            static evaluation => evaluation.BehaviorId);
        var all = finalized
            .OrderBy(
                static evaluation =>
                    evaluation.IsEligible ? evaluation.Rank : int.MaxValue)
            .ThenBy(
                static evaluation => evaluation.BehaviorId.Value,
                StringComparer.Ordinal)
            .ToArray();
        var top = topRaw
            .Select(evaluation => finalizedById[evaluation.BehaviorId])
            .ToArray();

        return new BehaviorSelectionPlan
        {
            AllEvaluations = all,
            TopCandidates = top,
            RandomConsumed = false,
        };
    }

    public BehaviorSelectionResult Pick(BehaviorSelectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ObserveClock();

        var remaining = plan.TopCandidates
            .Where(static evaluation =>
                evaluation.IsEligible &&
                double.IsFinite(evaluation.Probability) &&
                evaluation.Probability > 0)
            .Select(
                static evaluation =>
                    new WeightedCandidate(
                        evaluation.BehaviorId,
                        evaluation.Probability))
            .ToList();
        var attemptOrder = new List<BehaviorId>(remaining.Count);
        var randomConsumed = false;

        while (remaining.Count > 1)
        {
            var randomValue = NextValidatedRandom();
            randomConsumed = true;
            var totalWeight = remaining.Sum(static candidate => candidate.Weight);
            var roll =
                Math.Min(randomValue, Math.BitDecrement(1d)) * totalWeight;
            var selectedIndex = remaining.Count - 1;
            for (var index = 0; index < remaining.Count; index++)
            {
                roll -= remaining[index].Weight;
                if (roll < 0)
                {
                    selectedIndex = index;
                    break;
                }
            }

            attemptOrder.Add(remaining[selectedIndex].BehaviorId);
            remaining.RemoveAt(selectedIndex);
        }

        if (remaining.Count == 1)
        {
            attemptOrder.Add(remaining[0].BehaviorId);
        }

        var selected = attemptOrder.Count == 0
            ? (BehaviorId?)null
            : attemptOrder[0];
        return new BehaviorSelectionResult
        {
            SelectedBehaviorId = selected,
            AttemptOrder = attemptOrder,
            Plan = plan with
            {
                RandomConsumed = plan.RandomConsumed || randomConsumed,
            },
        };
    }

    public void Commit(BehaviorId behaviorId)
    {
        if (!_catalog.TryGet(behaviorId, out var definition) ||
            definition is null)
        {
            throw new KeyNotFoundException(
                $"Unknown behavior id '{behaviorId}'.");
        }

        var now = ObserveClock();
        _lastCommittedAt[behaviorId] = now;
        if (!string.IsNullOrWhiteSpace(definition.CooldownGroup))
        {
            _lastCooldownGroupAt[definition.CooldownGroup] = now;
        }

        if (_lastBehaviorId == behaviorId)
        {
            _consecutiveRuns++;
        }
        else
        {
            _lastBehaviorId = behaviorId;
            _consecutiveRuns = 1;
        }

        Remember(_recentBehaviors, behaviorId, SameBehaviorPenalties.Length);
        Remember(
            _recentFamilies,
            definition.Family,
            SameFamilyPenalties.Length);
        Remember(
            _recentClipFamilies,
                definition.ClipFamily,
                SameClipPenalties.Length);
        _commitCount++;
    }

    public BehaviorUtilityHistorySnapshot GetHistorySnapshot() =>
        new()
        {
            CommitCount = _commitCount,
            LastCommittedAt =
                new Dictionary<BehaviorId, TimeSpan>(_lastCommittedAt),
            RecentBehaviors = _recentBehaviors.ToArray(),
            RecentFamilies = _recentFamilies.ToArray(),
            RecentClipFamilies = _recentClipFamilies.ToArray(),
            LastBehaviorId = _lastBehaviorId,
            ConsecutiveRuns = _consecutiveRuns,
        };

    private BehaviorEvaluation EvaluateDefinition(
        BehaviorDefinition definition,
        BehaviorUtilityEvaluationInput input,
        TimeSpan now)
    {
        var breakdown = BuildBreakdown(definition, input, now);
        string? rejectionReason = null;
        bool dailyOpportunityEvaluation =
            IsDailyOpportunityEvaluation(definition, input.Source);

        if (input.Context.IsLocked)
        {
            rejectionReason = "locked";
        }
        else if (!definition.AllowedStates.Contains(input.State))
        {
            rejectionReason = "state";
        }
        else if (!IsDirectInteraction(input.Source) &&
                 BehaviorDefinitionSemantics
                     .IsAutomaticExternalStateLifecycle(definition) &&
                 definition.TargetState == input.State)
        {
            rejectionReason = "target-state-already-active";
        }
        else if (!IsDirectInteraction(input.Source) &&
                 !MatchesAutomaticEligibility(
                     definition.AutomaticEligibility,
                     input.Context))
        {
            rejectionReason = "automatic-eligibility";
        }
        else if (!IsDirectInteraction(input.Source) &&
                 input.Context.IsFullscreen &&
                 definition.DisturbanceLevel >
                 BehaviorDisturbanceLevel.Subtle)
        {
            rejectionReason = "fullscreen-disturbance";
        }
        else if (!IsDirectInteraction(input.Source) &&
                  IsCoolingDown(definition, input.Source, now))
        {
            rejectionReason = "cooldown";
        }
        else if (!dailyOpportunityEvaluation &&
                 _lastBehaviorId == definition.Id &&
                 definition.Utility.MaxConsecutiveRuns > 0 &&
                 _consecutiveRuns >=
                 definition.Utility.MaxConsecutiveRuns)
        {
            rejectionReason = "repeat-limit";
        }
        else if (input.Source == BehaviorRequestSource.ActiveTick &&
                 breakdown.Total < ActiveMinimumScore)
        {
            rejectionReason = "score";
        }

        return new BehaviorEvaluation
        {
            BehaviorId = definition.Id,
            IsEligible = rejectionReason is null,
            RejectionReason = rejectionReason,
            Breakdown = breakdown,
        };
    }

    private BehaviorEvaluationBreakdown BuildBreakdown(
        BehaviorDefinition definition,
        BehaviorUtilityEvaluationInput input,
        TimeSpan now)
    {
        bool dailyOpportunityEvaluation =
            IsDailyOpportunityEvaluation(definition, input.Source);
        var baseScore = definition.Utility.BaseScore;
        var emotion = CalculateEmotion(definition, input.Emotions);
        var time = CalculateTime(definition, input.Context);
        var activity = CalculateActivity(definition, input.Context);
        var application = CalculateApplication(definition, input.Context);
        var weather = CalculateWeather(definition, input.Context);
        var triggerBoost = CalculateTriggerBoost(input.Source);
        var timeSinceLastRun = CalculateTimeSinceLastRun(
            definition,
            input.Source,
            now);
        var relationship = CalculateRelationship(definition, input.Emotions);
        var sameBehaviorPenalty = dailyOpportunityEvaluation
            ? 0
            : FindHistoryPenalty(
                _recentBehaviors,
                definition.Id,
                SameBehaviorPenalties);
        var sameFamilyPenalty = dailyOpportunityEvaluation
            ? 0
            : FindHistoryPenalty(
                _recentFamilies,
                definition.Family,
                SameFamilyPenalties);
        var sameClipPenalty = dailyOpportunityEvaluation
            ? 0
            : FindHistoryPenalty(
                _recentClipFamilies,
                definition.ClipFamily,
                SameClipPenalties);
        var disturbUserPenalty = CalculateDisturbancePenalty(
            definition,
            input.Context);
        var total = Math.Clamp(
            baseScore +
            emotion +
            time +
            activity +
            application +
            weather +
            triggerBoost +
            timeSinceLastRun +
            relationship -
            sameBehaviorPenalty -
            sameFamilyPenalty -
            sameClipPenalty -
            disturbUserPenalty,
            0,
            100);

        return new BehaviorEvaluationBreakdown
        {
            Base = baseScore,
            Emotion = emotion,
            Time = time,
            Activity = activity,
            Application = application,
            Weather = weather,
            TriggerBoost = triggerBoost,
            TimeSinceLastRun = timeSinceLastRun,
            Relationship = relationship,
            SameBehaviorPenalty = sameBehaviorPenalty,
            SameFamilyPenalty = sameFamilyPenalty,
            SameClipPenalty = sameClipPenalty,
            DisturbUserPenalty = disturbUserPenalty,
            Total = total,
        };
    }

    private IReadOnlyList<BehaviorDefinition> ResolveCandidates(
        IReadOnlyCollection<BehaviorId>? candidateIds)
    {
        if (candidateIds is null)
        {
            return _catalog.Definitions
                .OrderBy(
                    static definition => definition.Id.Value,
                    StringComparer.Ordinal)
                .ToArray();
        }

        var definitions = new List<BehaviorDefinition>();
        foreach (var id in candidateIds.Distinct())
        {
            if (!_catalog.TryGet(id, out var definition) ||
                definition is null)
            {
                throw new KeyNotFoundException(
                    $"Unknown behavior id '{id}'.");
            }

            definitions.Add(definition);
        }

        return definitions
            .OrderBy(
                static definition => definition.Id.Value,
                StringComparer.Ordinal)
            .ToArray();
    }

    private bool IsCoolingDown(
        BehaviorDefinition definition,
        BehaviorRequestSource source,
        TimeSpan now)
    {
        if (IsDailyOpportunityEvaluation(definition, source))
        {
            return false;
        }

        TimeSpan? lastCommitted = null;
        if (_lastCommittedAt.TryGetValue(definition.Id, out var behaviorAt))
        {
            lastCommitted = behaviorAt;
        }

        if (!string.IsNullOrWhiteSpace(definition.CooldownGroup) &&
            _lastCooldownGroupAt.TryGetValue(
                definition.CooldownGroup,
                out var groupAt) &&
            (lastCommitted is null || groupAt > lastCommitted))
        {
            lastCommitted = groupAt;
        }

        if (lastCommitted is null)
        {
            return false;
        }

        if (now < lastCommitted)
        {
            throw new InvalidOperationException(
                "The monotonic behavior clock cannot move backwards.");
        }

        return now - lastCommitted < definition.Cooldown;
    }

    private double CalculateTimeSinceLastRun(
        BehaviorDefinition definition,
        BehaviorRequestSource source,
        TimeSpan now)
    {
        if (!_lastCommittedAt.TryGetValue(definition.Id, out var lastCommitted))
        {
            return 25;
        }

        if (now < lastCommitted)
        {
            throw new InvalidOperationException(
                "The monotonic behavior clock cannot move backwards.");
        }

        if (definition.Cooldown <= TimeSpan.Zero)
        {
            return 25;
        }

        var elapsed = now - lastCommitted;
        if (IsDailyOpportunityEvaluation(definition, source))
        {
            return 25 * Math.Clamp(
                elapsed.TotalMilliseconds /
                definition.Cooldown.TotalMilliseconds,
                0,
                1);
        }

        if (elapsed <= definition.Cooldown)
        {
            return 0;
        }

        var fullyFreshAt = TimeSpan.FromTicks(
            checked(definition.Cooldown.Ticks * 5));
        if (elapsed >= fullyFreshAt)
        {
            return 25;
        }

        return 25 *
               (elapsed - definition.Cooldown).TotalMilliseconds /
               (definition.Cooldown.TotalMilliseconds * 4);
    }

    private static bool IsDailyOpportunityEvaluation(
        BehaviorDefinition definition,
        BehaviorRequestSource source) =>
        (source is
            BehaviorRequestSource.Scheduled or
            BehaviorRequestSource.Weather) &&
        BehaviorDefinitionSemantics.IsDailyOpportunityBehavior(definition);

    private static double CalculateEmotion(
        BehaviorDefinition definition,
        EmotionState emotions)
    {
        var total = 0d;
        foreach (var tag in definition.Utility.EmotionTags)
        {
            if (!EmotionTagCoefficients.TryGetValue(
                    NormalizeUtilityTag(tag),
                    out var coefficients))
            {
                continue;
            }

            total +=
                coefficients.Energy * NormalizeEmotion(emotions.Energy) +
                coefficients.Sleepiness * NormalizeEmotion(emotions.Sleepiness) +
                coefficients.Boredom * NormalizeEmotion(emotions.Boredom) +
                coefficients.Curiosity * NormalizeEmotion(emotions.Curiosity) +
                coefficients.Happiness * NormalizeEmotion(emotions.Happiness) +
                coefficients.Stress * NormalizeEmotion(emotions.Stress) +
                coefficients.Affection * NormalizeEmotion(emotions.Affection);
        }

        return Math.Clamp(total, -15, 15);
    }

    private static bool MatchesAutomaticEligibility(
        BehaviorAutomaticEligibilityRule rule,
        BehaviorContextSnapshot context)
    {
        var matches = new List<bool>(2);
        if (rule.TimeSlots.Count > 0)
        {
            string? currentSlot =
                BehaviorContextTriggerRouter.ResolveTimeSlot(
                    context.LocalNow.TimeOfDay);
            matches.Add(
                currentSlot is not null &&
                rule.TimeSlots.Contains(
                    currentSlot,
                    StringComparer.Ordinal));
        }

        if (rule.MinimumIdleFor is { } minimumIdle)
        {
            matches.Add(context.IdleFor >= minimumIdle);
        }

        return matches.Count == 0 ||
               (rule.MatchAny
                   ? matches.Any(static match => match)
                   : matches.All(static match => match));
    }

    private static double CalculateTime(
        BehaviorDefinition definition,
        BehaviorContextSnapshot context)
    {
        var hour = context.LocalNow.Hour;
        var late = hour >= 22 || hour < 6;
        var value = 0d;
        if (HasTag(definition, "Sleep"))
        {
            value += late ? 10 : hour is >= 6 and < 10 ? -8 : 0;
        }
        else if (HasTag(definition, "Rest"))
        {
            value += late
                ? 6
                : hour is >= 11 and < 14
                    ? 4
                    : 0;
        }

        if (HasTag(definition, "Play"))
        {
            value += late ? -10 : hour is >= 8 and < 22 ? 4 : 0;
        }

        return Math.Clamp(value, -10, 10);
    }

    private static double CalculateActivity(
        BehaviorDefinition definition,
        BehaviorContextSnapshot context)
    {
        var value = 0d;
        if (context.IsBusy)
        {
            if (HasTag(definition, "Rest") ||
                HasTag(definition, "SelfCare"))
            {
                value += 6;
            }

            if (HasTag(definition, "Play"))
            {
                value -= 15;
            }
        }
        else if (context.IdleFor >= TimeSpan.FromMinutes(10))
        {
            if (HasTag(definition, "Curious") ||
                HasTag(definition, "Play"))
            {
                value += 12;
            }
            else if (HasTag(definition, "Rest") ||
                     HasTag(definition, "Sleep"))
            {
                value += 4;
            }
        }

        return Math.Clamp(value, -25, 12);
    }

    private static double CalculateApplication(
        BehaviorDefinition definition,
        BehaviorContextSnapshot context)
    {
        if (!context.ApplicationCategoryAvailable ||
            definition.Utility.PreferredApplicationCategories.Count == 0)
        {
            return 0;
        }

        return definition.Utility.PreferredApplicationCategories.Contains(
            context.ApplicationCategory,
            StringComparer.OrdinalIgnoreCase)
                ? 15
                : -12;
    }

    private static double CalculateWeather(
        BehaviorDefinition definition,
        BehaviorContextSnapshot context)
    {
        if (!context.WeatherAvailable ||
            definition.Utility.PreferredWeatherKinds.Count == 0)
        {
            return 0;
        }

        return definition.Utility.PreferredWeatherKinds.Contains(
            context.WeatherKind,
            StringComparer.OrdinalIgnoreCase)
                ? 5
                : -5;
    }

    private static double CalculateTriggerBoost(BehaviorRequestSource source) =>
        source switch
        {
            BehaviorRequestSource.Interaction => 30,
            BehaviorRequestSource.ForcedInteraction => 30,
            BehaviorRequestSource.TestControl => 30,
            BehaviorRequestSource.Scheduled => 20,
            BehaviorRequestSource.UserContext => 16,
            BehaviorRequestSource.Weather => 16,
            _ => 0,
        };

    private static double CalculateRelationship(
        BehaviorDefinition definition,
        EmotionState emotions)
    {
        var affection = Math.Clamp(emotions.Affection, 0, 100) / 100d;
        if (HasTag(definition, "SocialWarm"))
        {
            return -5 + 15 * affection;
        }

        if (HasTag(definition, "Independent") ||
            HasTag(definition, "Irritated"))
        {
            return 5 - 10 * affection;
        }

        return 0;
    }

    private static double CalculateDisturbancePenalty(
        BehaviorDefinition definition,
        BehaviorContextSnapshot context)
    {
        var index = (int)definition.DisturbanceLevel;
        var busyPenalty = context.IsBusy
            ? BusyDisturbancePenalties[index]
            : 0;
        var hour = context.LocalNow.Hour;
        var latePenalty = hour >= 22 || hour < 6
            ? LateDisturbancePenalties[index]
            : 0;
        return Math.Min(30, Math.Max(busyPenalty, latePenalty));
    }

    private static IReadOnlyList<double> CalculateProbabilities(
        IReadOnlyList<BehaviorEvaluation> candidates,
        double requestedCap)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        if (candidates.Count == 1)
        {
            return [1d];
        }

        var maximumScore = candidates.Max(
            static candidate => candidate.Breakdown.Total);
        var softmaxWeights = candidates
            .Select(candidate =>
                Math.Exp(
                    (candidate.Breakdown.Total - maximumScore) /
                    Temperature))
            .ToArray();
        var softmaxTotal = softmaxWeights.Sum();
        var blended = softmaxWeights
            .Select(weight =>
                0.7d * weight / softmaxTotal +
                0.3d / candidates.Count)
            .ToArray();
        var feasibleCap = Math.Max(requestedCap, 1d / candidates.Count);
        return ApplyProbabilityCap(blended, feasibleCap);
    }

    private static IReadOnlyList<double> ApplyProbabilityCap(
        IReadOnlyList<double> probabilities,
        double cap)
    {
        var result = new double[probabilities.Count];
        var remaining = Enumerable.Range(0, probabilities.Count).ToList();
        var remainingMass = 1d;

        while (remaining.Count > 0)
        {
            var sourceMass = remaining.Sum(index => probabilities[index]);
            var newlyCapped = remaining
                .Where(index =>
                    remainingMass * probabilities[index] / sourceMass >
                    cap)
                .ToArray();
            if (newlyCapped.Length == 0)
            {
                foreach (var index in remaining)
                {
                    result[index] =
                        remainingMass * probabilities[index] / sourceMass;
                }

                break;
            }

            foreach (var index in newlyCapped)
            {
                result[index] = cap;
                remaining.Remove(index);
                remainingMass -= cap;
            }
        }

        var correction = 1d - result.Sum();
        if (Math.Abs(correction) > 1e-12)
        {
            var correctionIndex = Enumerable.Range(0, result.Length)
                .OrderByDescending(index => cap - result[index])
                .First();
            result[correctionIndex] += correction;
        }

        return result;
    }

    private static bool IsDirectInteraction(BehaviorRequestSource source) =>
        source is BehaviorRequestSource.Interaction
            or BehaviorRequestSource.ForcedInteraction;

    private static bool HasTag(
        BehaviorDefinition definition,
        string tag)
    {
        var normalizedTag = NormalizeUtilityTag(tag);
        return definition.Utility.EmotionTags.Any(
            candidate =>
                NormalizeUtilityTag(candidate) == normalizedTag);
    }

    private static string NormalizeUtilityTag(string tag) =>
        new(
            tag.Where(static character =>
                    char.IsAsciiLetterOrDigit(character))
                .Select(static character =>
                    char.ToLowerInvariant(character))
                .ToArray());

    private static double NormalizeEmotion(double value) =>
        (Math.Clamp(value, 0, 100) - 50) / 50d;

    private static double FindHistoryPenalty<T>(
        IReadOnlyList<T> history,
        T value,
        IReadOnlyList<double> penalties)
    {
        var comparer = EqualityComparer<T>.Default;
        for (var index = 0;
             index < history.Count && index < penalties.Count;
             index++)
        {
            if (comparer.Equals(history[index], value))
            {
                return penalties[index];
            }
        }

        return 0;
    }

    private static void Remember<T>(
        List<T> history,
        T value,
        int maximumCount)
    {
        history.Insert(0, value);
        if (history.Count > maximumCount)
        {
            history.RemoveRange(maximumCount, history.Count - maximumCount);
        }
    }

    private double NextValidatedRandom()
    {
        var value = _selectionRandom.NextUnit();
        if (value is < 0 or > 1 || double.IsNaN(value))
        {
            throw new InvalidOperationException(
                "The utility random source must return a value in the inclusive range 0 to 1.");
        }

        return value;
    }

    private TimeSpan ObserveClock()
    {
        var now = _clock.Elapsed;
        if (now < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The monotonic behavior clock cannot be negative.");
        }

        if (_lastObservedAt is { } previous && now < previous)
        {
            throw new InvalidOperationException(
                "The monotonic behavior clock cannot move backwards.");
        }

        _lastObservedAt = now;
        return now;
    }

    private readonly record struct WeightedCandidate(
        BehaviorId BehaviorId,
        double Weight);

    private readonly record struct EmotionCoefficients(
        double Energy,
        double Sleepiness,
        double Boredom,
        double Curiosity,
        double Happiness,
        double Stress,
        double Affection);

}
