using GuluPet.Behavior;
using GuluPet.Domain;

namespace GuluPet.Tests;

internal static class BehaviorUtilityEngineTests
{
    public static void RunAll()
    {
        Run(
            nameof(EvaluateIsPureAndProducesStableTopFive),
            EvaluateIsPureAndProducesStableTopFive);
        Run(
            nameof(PickBuildsNoReplacementOrderAndCommitOwnsMutation),
            PickBuildsNoReplacementOrderAndCommitOwnsMutation);
        Run(
            nameof(ProbabilityCapsAndSingleCandidateAreNormalized),
            ProbabilityCapsAndSingleCandidateAreNormalized);
        Run(
            nameof(ScoringBreakdownAndTotalClampingAreObservable),
            ScoringBreakdownAndTotalClampingAreObservable);
        Run(
            nameof(HardFiltersReportStableReasons),
            HardFiltersReportStableReasons);
        Run(
            nameof(FullscreenDisturbanceFilterExemptsDirectInteraction),
            FullscreenDisturbanceFilterExemptsDirectInteraction);
        Run(
            nameof(AutomaticStateLifecycleUsesGenericContextEligibility),
            AutomaticStateLifecycleUsesGenericContextEligibility);
        Run(
            nameof(RecentPenaltiesAppearOnlyAfterCommit),
            RecentPenaltiesAppearOnlyAfterCommit);
        Run(
            nameof(DailyOpportunityCooldownBecomesRecoveringScore),
            DailyOpportunityCooldownBecomesRecoveringScore);
        Run(
            nameof(RandomEndpointsAndNamedStreamsAreDeterministic),
            RandomEndpointsAndNamedStreamsAreDeterministic);
        Run(
            nameof(ClockRollbackBeforeCommitFailsWithoutSideEffects),
            ClockRollbackBeforeCommitFailsWithoutSideEffects);
    }

    private static void EvaluateIsPureAndProducesStableTopFive()
    {
        var random = new SequenceRandomSource();
        var engine = CreateEngine(
            random,
            Definition("foxtrot", 10),
            Definition("delta", 30),
            Definition("bravo", 50),
            Definition("echo", 20),
            Definition("alpha", 50),
            Definition("charlie", 40));

        var plan = engine.Evaluate(Input());

        BehaviorTestCheck.Equal(0, random.Calls);
        BehaviorTestCheck.False(plan.RandomConsumed);
        BehaviorTestCheck.Equal(0, engine.GetHistorySnapshot().CommitCount);
        BehaviorTestCheck.Equal(5, plan.TopCandidates.Count);
        BehaviorTestCheck.SequenceEqual(
            new[]
            {
                new BehaviorId("alpha"),
                new BehaviorId("bravo"),
                new BehaviorId("charlie"),
                new BehaviorId("delta"),
                new BehaviorId("echo"),
            },
            plan.TopCandidates.Select(static candidate => candidate.BehaviorId));
        BehaviorTestCheck.SequenceEqual(
            Enumerable.Range(1, 6),
            plan.AllEvaluations.Select(static candidate => candidate.Rank));
        BehaviorTestCheck.Close(
            1,
            plan.TopCandidates.Sum(static candidate => candidate.Probability));
        BehaviorTestCheck.True(
            plan.TopCandidates.All(static candidate =>
                candidate.Probability <= 0.45 + 0.000_001));
        BehaviorTestCheck.Close(
            0,
            Find(plan, "foxtrot").Probability);

        var repeated = engine.Evaluate(Input());
        BehaviorTestCheck.Equal(0, random.Calls);
        BehaviorTestCheck.SequenceEqual(
            plan.AllEvaluations.Select(static candidate =>
                (
                    candidate.BehaviorId,
                    candidate.IsEligible,
                    candidate.RejectionReason,
                    candidate.Breakdown,
                    candidate.Rank,
                    candidate.Probability)),
            repeated.AllEvaluations.Select(static candidate =>
                (
                    candidate.BehaviorId,
                    candidate.IsEligible,
                    candidate.RejectionReason,
                    candidate.Breakdown,
                    candidate.Rank,
                    candidate.Probability)));
        BehaviorTestCheck.False(repeated.RandomConsumed);
        BehaviorTestCheck.Equal(0, engine.GetHistorySnapshot().CommitCount);
    }

    private static void PickBuildsNoReplacementOrderAndCommitOwnsMutation()
    {
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource(0, 1);
        var catalog = new BehaviorCatalog(
            new[]
            {
                Definition("alpha", 50, cooldown: TimeSpan.FromMinutes(1)),
                Definition("bravo", 50, cooldown: TimeSpan.FromMinutes(1)),
                Definition("charlie", 50, cooldown: TimeSpan.FromMinutes(1)),
            });
        var engine = new BehaviorUtilityEngine(catalog, clock, random);
        var input = Input(BehaviorRequestSource.Scheduled);

        var plan = engine.Evaluate(input);
        var result = engine.Pick(plan);

        BehaviorTestCheck.Equal(2, random.Calls);
        BehaviorTestCheck.True(result.Plan.RandomConsumed);
        BehaviorTestCheck.True(result.SelectedBehaviorId.HasValue);
        BehaviorTestCheck.Equal(
            new BehaviorId("alpha"),
            result.SelectedBehaviorId!.Value);
        BehaviorTestCheck.SequenceEqual(
            new[]
            {
                new BehaviorId("alpha"),
                new BehaviorId("charlie"),
                new BehaviorId("bravo"),
            },
            result.AttemptOrder);
        BehaviorTestCheck.Equal(
            result.AttemptOrder.Count,
            result.AttemptOrder.Distinct().Count());
        BehaviorTestCheck.Equal(0, engine.GetHistorySnapshot().CommitCount);
        BehaviorTestCheck.True(
            Find(engine.Evaluate(input), "alpha").IsEligible);

        engine.Commit(result.SelectedBehaviorId.Value);

        BehaviorTestCheck.Equal(1, engine.GetHistorySnapshot().CommitCount);
        var coolingDown = Find(engine.Evaluate(input), "alpha");
        BehaviorTestCheck.False(coolingDown.IsEligible);
        BehaviorTestCheck.Equal("cooldown", coolingDown.RejectionReason);

        clock.Advance(TimeSpan.FromMinutes(1));
        BehaviorTestCheck.True(
            Find(engine.Evaluate(input), "alpha").IsEligible);
    }

    private static void ProbabilityCapsAndSingleCandidateAreNormalized()
    {
        var activeEngine = CreateEngine(
            new SequenceRandomSource(),
            Definition("alpha", 100),
            Definition("bravo", 0),
            Definition("charlie", 0));
        var active = activeEngine.Evaluate(Input());
        BehaviorTestCheck.Close(
            1,
            active.TopCandidates.Sum(static candidate => candidate.Probability));
        BehaviorTestCheck.True(
            active.TopCandidates.All(static candidate =>
                candidate.Probability <= 0.45 + 0.000_001));

        var interaction = activeEngine.Evaluate(
            Input(BehaviorRequestSource.Interaction));
        BehaviorTestCheck.Close(
            1,
            interaction.TopCandidates.Sum(
                static candidate => candidate.Probability));
        BehaviorTestCheck.True(
            interaction.TopCandidates.All(static candidate =>
                candidate.Probability <= 0.55 + 0.000_001));

        var random = new SequenceRandomSource();
        var singleEngine = CreateEngine(
            random,
            Definition("only", 15));
        var singlePlan = singleEngine.Evaluate(Input());
        var singleResult = singleEngine.Pick(singlePlan);

        BehaviorTestCheck.Equal(1, singlePlan.TopCandidates.Count);
        BehaviorTestCheck.Close(
            1,
            singlePlan.TopCandidates[0].Probability);
        BehaviorTestCheck.Equal(0, random.Calls);
        BehaviorTestCheck.False(singleResult.Plan.RandomConsumed);
        BehaviorTestCheck.SequenceEqual(
            new[] { new BehaviorId("only") },
            singleResult.AttemptOrder);
        BehaviorTestCheck.Equal(
            0,
            singleEngine.GetHistorySnapshot().CommitCount);
    }

    private static void ScoringBreakdownAndTotalClampingAreObservable()
    {
        var scored = Definition(
            "scored",
            30,
            preferredApplications: ["editor"],
            preferredWeather: ["rain"]);
        var engine = CreateEngine(new SequenceRandomSource(), scored);
        var matchingContext = MiddayContext() with
        {
            ApplicationCategory = "editor",
            ApplicationCategoryAvailable = true,
            WeatherKind = "rain",
            WeatherAvailable = true,
        };

        var breakdown = Find(
            engine.Evaluate(
                Input(BehaviorRequestSource.Scheduled, matchingContext)),
            "scored").Breakdown;

        BehaviorTestCheck.Close(30, breakdown.Base);
        BehaviorTestCheck.Close(0, breakdown.Emotion);
        BehaviorTestCheck.Close(0, breakdown.Time);
        BehaviorTestCheck.Close(0, breakdown.Activity);
        BehaviorTestCheck.Close(15, breakdown.Application);
        BehaviorTestCheck.Close(5, breakdown.Weather);
        BehaviorTestCheck.Close(20, breakdown.TriggerBoost);
        BehaviorTestCheck.Close(25, breakdown.TimeSinceLastRun);
        BehaviorTestCheck.Close(0, breakdown.Relationship);
        BehaviorTestCheck.Close(95, breakdown.Total);

        var unavailable = Find(
            engine.Evaluate(
                Input(
                    BehaviorRequestSource.Scheduled,
                    MiddayContext())),
            "scored").Breakdown;
        BehaviorTestCheck.Close(0, unavailable.Application);
        BehaviorTestCheck.Close(0, unavailable.Weather);
        BehaviorTestCheck.Close(75, unavailable.Total);

        var highEngine = CreateEngine(
            new SequenceRandomSource(),
            Definition("high", 100));
        BehaviorTestCheck.Close(
            100,
            Find(highEngine.Evaluate(Input()), "high").Breakdown.Total);

        var lowEngine = CreateEngine(
            new SequenceRandomSource(),
            Definition(
                "low",
                0,
                disturbance: BehaviorDisturbanceLevel.Strong,
                preferredApplications: ["editor"],
                preferredWeather: ["rain"]));
        var mismatch = MiddayContext() with
        {
            IsBusy = true,
            ApplicationCategory = "game",
            ApplicationCategoryAvailable = true,
            WeatherKind = "clear",
            WeatherAvailable = true,
        };
        var low = Find(lowEngine.Evaluate(Input(context: mismatch)), "low");
        BehaviorTestCheck.Close(-12, low.Breakdown.Application);
        BehaviorTestCheck.Close(-5, low.Breakdown.Weather);
        BehaviorTestCheck.Close(30, low.Breakdown.DisturbUserPenalty);
        BehaviorTestCheck.Close(0, low.Breakdown.Total);
        BehaviorTestCheck.False(low.IsEligible);
        BehaviorTestCheck.Equal("score", low.RejectionReason);

        var taggedEngine = CreateEngine(
            new SequenceRandomSource(),
            Definition(
                "tagged",
                0,
                emotionTags: ["social_warm", "self_care"]));
        var warmEmotions = NeutralEmotions();
        warmEmotions.Affection = 100;
        var tagged = Find(
            taggedEngine.Evaluate(
                Input(
                    context: MiddayContext() with { IsBusy = true }) with
                {
                    Emotions = warmEmotions,
                }),
            "tagged").Breakdown;
        BehaviorTestCheck.Close(8, tagged.Emotion);
        BehaviorTestCheck.Close(6, tagged.Activity);
        BehaviorTestCheck.Close(10, tagged.Relationship);
    }

    private static void HardFiltersReportStableReasons()
    {
        var stateOnly = Definition(
            "state_only",
            30,
            allowedStates: [StablePetState.Sleeping]);
        var noisy = Definition(
            "noisy",
            30,
            disturbance: BehaviorDisturbanceLevel.Strong);
        var cooling = Definition(
            "cooling",
            30,
            cooldown: TimeSpan.FromMinutes(1));
        var repeat = Definition("repeat", 30, maxConsecutiveRuns: 1);
        var catalog = new BehaviorCatalog(
            new[] { stateOnly, noisy, cooling, repeat });
        var engine = new BehaviorUtilityEngine(
            catalog,
            new ManualDualClock(),
            new SequenceRandomSource());

        BehaviorTestCheck.Equal(
            "locked",
            Find(
                engine.Evaluate(
                    Input(
                        context: MiddayContext() with { IsLocked = true },
                        candidates: ["noisy"])),
                "noisy").RejectionReason);
        BehaviorTestCheck.Equal(
            "state",
            Find(
                engine.Evaluate(Input(candidates: ["state_only"])),
                "state_only").RejectionReason);
        BehaviorTestCheck.Equal(
            "fullscreen-disturbance",
            Find(
                engine.Evaluate(
                    Input(
                        context: MiddayContext() with
                        {
                            IsFullscreen = true,
                        },
                        candidates: ["noisy"])),
                "noisy").RejectionReason);

        engine.Commit("cooling");
        BehaviorTestCheck.Equal(
            "cooldown",
            Find(
                engine.Evaluate(
                    Input(
                        BehaviorRequestSource.Scheduled,
                        candidates: ["cooling"])),
                "cooling").RejectionReason);

        engine.Commit("repeat");
        BehaviorTestCheck.Equal(
            "repeat-limit",
            Find(
                engine.Evaluate(Input(candidates: ["repeat"])),
                "repeat").RejectionReason);
    }

    private static void FullscreenDisturbanceFilterExemptsDirectInteraction()
    {
        BehaviorDefinition strong = Definition(
            "strong",
            40,
            disturbance: BehaviorDisturbanceLevel.Strong);
        var engine = CreateEngine(new SequenceRandomSource(), strong);
        BehaviorContextSnapshot fullscreen =
            MiddayContext() with { IsFullscreen = true };

        BehaviorEvaluation automatic = Find(
            engine.Evaluate(
                Input(BehaviorRequestSource.Scheduled, fullscreen)),
            strong.Id);
        BehaviorTestCheck.False(automatic.IsEligible);
        BehaviorTestCheck.Equal(
            "fullscreen-disturbance",
            automatic.RejectionReason!);

        BehaviorEvaluation interaction = Find(
            engine.Evaluate(
                Input(BehaviorRequestSource.Interaction, fullscreen)),
            strong.Id);
        BehaviorTestCheck.True(
            interaction.IsEligible,
            interaction.RejectionReason);
    }

    private static void AutomaticStateLifecycleUsesGenericContextEligibility()
    {
        BehaviorDefinition lifecycle = new()
        {
            Id = "automatic_state",
            DisplayName = "automatic_state",
            Family = "sleep",
            ClipFamily = "sleep",
            Tags = ["trigger:active_tick"],
            AllowedStates = [StablePetState.Normal, StablePetState.Sleeping],
            TargetState = StablePetState.Sleeping,
            Animation = new BehaviorAnimationPlan
            {
                EnterClipId = "enter",
                LoopClipId = "loop",
                ExitClipId = "exit",
            },
            Completion = new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.External,
            },
            MaximumDuration = TimeSpan.FromSeconds(8),
            Queueable = false,
            AutomaticEligibility = new BehaviorAutomaticEligibilityRule
            {
                TimeSlots = ["late"],
                MinimumIdleFor = TimeSpan.FromMinutes(5),
                MatchAny = true,
            },
        };
        BehaviorUtilityEngine engine = CreateEngine(
            new SequenceRandomSource(),
            lifecycle);

        BehaviorEvaluation midday = Find(
            engine.Evaluate(Input()),
            lifecycle.Id);
        BehaviorTestCheck.False(midday.IsEligible);
        BehaviorTestCheck.Equal(
            "automatic-eligibility",
            midday.RejectionReason);

        BehaviorContextSnapshot late = MiddayContext() with
        {
            LocalNow = MiddayContext().LocalNow.AddHours(11),
        };
        BehaviorTestCheck.True(
            Find(engine.Evaluate(Input(context: late)), lifecycle.Id)
                .IsEligible);

        BehaviorContextSnapshot idle = MiddayContext() with
        {
            IdleFor = TimeSpan.FromMinutes(5),
        };
        BehaviorTestCheck.True(
            Find(engine.Evaluate(Input(context: idle)), lifecycle.Id)
                .IsEligible);

        BehaviorEvaluation alreadyActive = Find(
            engine.Evaluate(
                Input(context: late) with
                {
                    State = StablePetState.Sleeping,
                }),
            lifecycle.Id);
        BehaviorTestCheck.False(alreadyActive.IsEligible);
        BehaviorTestCheck.Equal(
            "target-state-already-active",
            alreadyActive.RejectionReason);
    }

    private static void RecentPenaltiesAppearOnlyAfterCommit()
    {
        var random = new SequenceRandomSource();
        var engine = CreateEngine(
            random,
            Definition(
                "alpha",
                50,
                cooldown: TimeSpan.FromMinutes(1),
                family: "shared_family",
                clipFamily: "shared_clip"));
        var input = Input(BehaviorRequestSource.Interaction);
        var before = Find(engine.Evaluate(input), "alpha").Breakdown;
        var picked = engine.Pick(engine.Evaluate(input));
        var afterPick = Find(engine.Evaluate(input), "alpha").Breakdown;

        BehaviorTestCheck.Close(0, before.SameBehaviorPenalty);
        BehaviorTestCheck.Close(0, before.SameFamilyPenalty);
        BehaviorTestCheck.Close(0, before.SameClipPenalty);
        BehaviorTestCheck.Close(0, afterPick.SameBehaviorPenalty);
        BehaviorTestCheck.Equal(0, random.Calls);
        BehaviorTestCheck.Equal(0, engine.GetHistorySnapshot().CommitCount);

        var selected = picked.SelectedBehaviorId ??
            throw new InvalidOperationException(
                "Expected the only eligible candidate to be selected.");
        engine.Commit(selected);
        var afterCommit = Find(engine.Evaluate(input), "alpha").Breakdown;
        var history = engine.GetHistorySnapshot();

        BehaviorTestCheck.Close(40, afterCommit.SameBehaviorPenalty);
        BehaviorTestCheck.Close(20, afterCommit.SameFamilyPenalty);
        BehaviorTestCheck.Close(18, afterCommit.SameClipPenalty);
        BehaviorTestCheck.Equal(1, history.CommitCount);
        BehaviorTestCheck.Equal(new BehaviorId("alpha"), history.LastBehaviorId);
        BehaviorTestCheck.Equal(1, history.ConsecutiveRuns);
        BehaviorTestCheck.SequenceEqual(
            new[] { new BehaviorId("alpha") },
            history.RecentBehaviors);
    }

    private static void DailyOpportunityCooldownBecomesRecoveringScore()
    {
        var clock = new ManualDualClock();
        BehaviorDefinition daily = Definition(
            "daily",
            30,
            cooldown: TimeSpan.FromHours(2),
            maxConsecutiveRuns: 1,
            tags: ["trigger:weather:clear"]);
        BehaviorDefinition sibling = Definition(
            "sibling",
            30,
            cooldown: TimeSpan.FromHours(2),
            tags: ["trigger:weather:clear"]);
        BehaviorDefinition otherSibling = Definition(
            "other_sibling",
            30,
            cooldown: TimeSpan.FromHours(2),
            tags: ["trigger:weather:clear"]);
        var dailyEngine = new BehaviorUtilityEngine(
            new BehaviorCatalog([daily, sibling, otherSibling]),
            clock,
            new SequenceRandomSource());
        var input = Input(
            BehaviorRequestSource.Weather,
            candidates: ["daily", "sibling", "other_sibling"]);

        BehaviorEvaluation fullyRecoveredBeforeCommit =
            Find(dailyEngine.Evaluate(input), daily.Id);
        BehaviorTestCheck.Close(
            25,
            fullyRecoveredBeforeCommit.Breakdown.TimeSinceLastRun);
        dailyEngine.Commit(daily.Id);

        BehaviorEvaluation justPlayed =
            Find(dailyEngine.Evaluate(input), daily.Id);
        BehaviorEvaluation unplayed =
            Find(dailyEngine.Evaluate(input), sibling.Id);
        BehaviorTestCheck.True(
            justPlayed.IsEligible,
            justPlayed.RejectionReason);
        BehaviorTestCheck.Close(
            0,
            justPlayed.Breakdown.SameBehaviorPenalty);
        BehaviorTestCheck.Close(
            0,
            justPlayed.Breakdown.SameFamilyPenalty);
        BehaviorTestCheck.Close(
            0,
            justPlayed.Breakdown.SameClipPenalty);
        BehaviorTestCheck.Close(
            0,
            justPlayed.Breakdown.TimeSinceLastRun);
        BehaviorTestCheck.True(
            justPlayed.Breakdown.Total <
            unplayed.Breakdown.Total);
        BehaviorTestCheck.True(
            justPlayed.Probability <
            unplayed.Probability);
        BehaviorEvaluation activeTickStillUsesHardCooldown = Find(
            dailyEngine.Evaluate(
                Input(
                    BehaviorRequestSource.ActiveTick,
                    candidates: ["daily"])),
            daily.Id);
        BehaviorTestCheck.False(
            activeTickStillUsesHardCooldown.IsEligible);
        BehaviorTestCheck.Equal(
            "cooldown",
            activeTickStillUsesHardCooldown.RejectionReason);

        clock.Advance(TimeSpan.FromHours(1));
        BehaviorEvaluation halfwayRecovered =
            Find(dailyEngine.Evaluate(input), daily.Id);
        BehaviorTestCheck.Close(
            12.5,
            halfwayRecovered.Breakdown.TimeSinceLastRun);
        BehaviorTestCheck.True(
            halfwayRecovered.Breakdown.Total >
            justPlayed.Breakdown.Total);
        clock.Advance(TimeSpan.FromHours(1));
        BehaviorEvaluation fullyRecovered =
            Find(dailyEngine.Evaluate(input), daily.Id);
        BehaviorTestCheck.Close(
            25,
            fullyRecovered.Breakdown.TimeSinceLastRun);
        BehaviorTestCheck.True(
            fullyRecovered.IsEligible,
            fullyRecovered.RejectionReason);
        BehaviorTestCheck.Close(
            fullyRecoveredBeforeCommit.Breakdown.Total,
            fullyRecovered.Breakdown.Total);

        var ordinaryClock = new ManualDualClock();
        BehaviorDefinition ordinary = Definition(
            "ordinary",
            30,
            cooldown: TimeSpan.FromHours(2));
        var ordinaryEngine = new BehaviorUtilityEngine(
            new BehaviorCatalog([ordinary]),
            ordinaryClock,
            new SequenceRandomSource());
        ordinaryEngine.Commit(ordinary.Id);
        BehaviorEvaluation blocked = Find(
            ordinaryEngine.Evaluate(
                Input(
                    BehaviorRequestSource.Scheduled,
                    candidates: ["ordinary"])),
            ordinary.Id);
        BehaviorTestCheck.False(blocked.IsEligible);
        BehaviorTestCheck.Equal(
            "cooldown",
            blocked.RejectionReason);
    }

    private static void RandomEndpointsAndNamedStreamsAreDeterministic()
    {
        var tickRandom = new SequenceRandomSource(0);
        var utilityRandom = new SequenceRandomSource(0);
        var evictionRandom = new SequenceRandomSource();
        var streams = new BehaviorRandomStreams(
            tickRandom,
            utilityRandom,
            evictionRandom);
        var catalog = new BehaviorCatalog(
            new[]
            {
                Definition("alpha", 30),
                Definition("bravo", 30),
            });
        var engine = new BehaviorUtilityEngine(
            catalog,
            streams,
            new ManualDualClock());

        var plan = engine.Evaluate(Input());
        BehaviorTestCheck.Equal(0, tickRandom.Calls);
        BehaviorTestCheck.Equal(0, utilityRandom.Calls);
        BehaviorTestCheck.Equal(0, evictionRandom.Calls);
        var result = engine.Pick(plan);
        BehaviorTestCheck.Equal(0, tickRandom.Calls);
        BehaviorTestCheck.Equal(1, utilityRandom.Calls);
        BehaviorTestCheck.Equal(0, evictionRandom.Calls);
        BehaviorTestCheck.Equal(
            new BehaviorId("alpha"),
            result.SelectedBehaviorId!.Value);

        var invalidRandom = new SequenceRandomSource(double.NaN);
        var invalidEngine = new BehaviorUtilityEngine(
            catalog,
            new ManualDualClock(),
            invalidRandom);
        var invalidPlan = invalidEngine.Evaluate(Input());
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => invalidEngine.Pick(invalidPlan));
        BehaviorTestCheck.Equal(1, invalidRandom.Calls);
        BehaviorTestCheck.Equal(
            0,
            invalidEngine.GetHistorySnapshot().CommitCount);
    }

    private static void ClockRollbackBeforeCommitFailsWithoutSideEffects()
    {
        var clock = new RewindableMonotonicClock();
        var random = new SequenceRandomSource(0);
        var catalog = new BehaviorCatalog(
            new[]
            {
                Definition("alpha", 30),
                Definition("bravo", 30),
            });
        var engine = new BehaviorUtilityEngine(catalog, clock, random);

        clock.Set(TimeSpan.FromSeconds(10));
        var plan = engine.Evaluate(Input());
        BehaviorTestCheck.Equal(0, random.Calls);
        BehaviorTestCheck.Equal(0, engine.GetHistorySnapshot().CommitCount);

        clock.Set(TimeSpan.FromSeconds(9));
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => engine.Evaluate(Input()));
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => engine.Pick(plan));
        BehaviorTestCheck.Equal(0, random.Calls);
        BehaviorTestCheck.Equal(0, engine.GetHistorySnapshot().CommitCount);

        clock.Set(TimeSpan.FromSeconds(10));
        var result = engine.Pick(plan);
        var selected = result.SelectedBehaviorId ??
            throw new InvalidOperationException(
                "Expected a selected behavior after clock recovery.");
        BehaviorTestCheck.Equal(1, random.Calls);
        BehaviorTestCheck.Equal(0, engine.GetHistorySnapshot().CommitCount);

        engine.Commit(selected);
        var committedHistory = engine.GetHistorySnapshot();
        BehaviorTestCheck.Equal(1, committedHistory.CommitCount);

        clock.Set(TimeSpan.FromSeconds(9));
        var other = selected == new BehaviorId("alpha")
            ? new BehaviorId("bravo")
            : new BehaviorId("alpha");
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => engine.Commit(other));
        var historyAfterRejectedCommit = engine.GetHistorySnapshot();
        BehaviorTestCheck.Equal(1, historyAfterRejectedCommit.CommitCount);
        BehaviorTestCheck.Equal(
            1,
            historyAfterRejectedCommit.LastCommittedAt.Count);
        BehaviorTestCheck.True(
            historyAfterRejectedCommit.LastCommittedAt.ContainsKey(selected));
    }

    private static BehaviorUtilityEngine CreateEngine(
        SequenceRandomSource random,
        params BehaviorDefinition[] definitions) =>
        new(
            new BehaviorCatalog(definitions),
            new ManualDualClock(),
            random);

    private static BehaviorUtilityEvaluationInput Input(
        BehaviorRequestSource source = BehaviorRequestSource.ActiveTick,
        BehaviorContextSnapshot? context = null,
        IReadOnlyCollection<BehaviorId>? candidates = null) =>
        new()
        {
            State = StablePetState.Normal,
            Source = source,
            Emotions = NeutralEmotions(),
            Context = context ?? MiddayContext(),
            CandidateBehaviorIds = candidates,
        };

    private static EmotionState NeutralEmotions() =>
        new()
        {
            Energy = 50,
            Sleepiness = 50,
            Boredom = 50,
            Curiosity = 50,
            Happiness = 50,
            Stress = 50,
            Affection = 50,
        };

    private static BehaviorContextSnapshot MiddayContext() =>
        new()
        {
            LocalNow = new DateTimeOffset(
                2026,
                7,
                26,
                12,
                0,
                0,
                TimeSpan.FromHours(8)),
        };

    private static BehaviorEvaluation Find(
        BehaviorSelectionPlan plan,
        BehaviorId behaviorId) =>
        plan.AllEvaluations.Single(candidate =>
            candidate.BehaviorId == behaviorId);

    private static BehaviorDefinition Definition(
        string id,
        double baseScore,
        TimeSpan? cooldown = null,
        int maxConsecutiveRuns = 0,
        BehaviorDisturbanceLevel disturbance =
            BehaviorDisturbanceLevel.SilentMicro,
        IReadOnlyList<string>? emotionTags = null,
        IReadOnlyList<string>? preferredApplications = null,
        IReadOnlyList<string>? preferredWeather = null,
        IReadOnlyList<StablePetState>? allowedStates = null,
        string? family = null,
        string? clipFamily = null,
        IReadOnlyList<string>? tags = null) =>
        new()
        {
            Id = id,
            DisplayName = id,
            Family = family ?? id,
            ClipFamily = clipFamily ?? id,
            Tags = tags ?? [],
            AllowedStates = allowedStates ?? [StablePetState.Normal],
            Animation = new BehaviorAnimationPlan
            {
                PerformClipId = $"{id}_clip",
            },
            Completion = new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.ClipEnd,
            },
            MaximumDuration = TimeSpan.FromSeconds(2),
            DisturbanceLevel = disturbance,
            Cooldown = cooldown ?? TimeSpan.Zero,
            Utility = new BehaviorUtilityRule
            {
                BaseScore = baseScore,
                EmotionTags = emotionTags ?? [],
                PreferredApplicationCategories = preferredApplications ?? [],
                PreferredWeatherKinds = preferredWeather ?? [],
                MaxConsecutiveRuns = maxConsecutiveRuns,
            },
        };

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }

}

internal sealed class RewindableMonotonicClock : IMonotonicClock
{
    public TimeSpan Elapsed { get; private set; }

    public void Set(TimeSpan elapsed) => Elapsed = elapsed;
}
