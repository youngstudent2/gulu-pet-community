using GuluPet.Behavior;

namespace GuluPet.Tests;

internal static class BubbleArbiterTests
{
    public static void RunAll()
    {
        Run(nameof(EvaluateIsPureUntilCommitted), EvaluateIsPureUntilCommitted);
        Run(nameof(InteractionBudgetAllowsFourPerThirtySeconds), InteractionBudgetAllowsFourPerThirtySeconds);
        Run(nameof(RequiredInteractionFeedbackBypassesSuppression), RequiredInteractionFeedbackBypassesSuppression);
        Run(nameof(RepeatedRequiredInteractionBypassesSemanticCooldownAfterReading), RepeatedRequiredInteractionBypassesSemanticCooldownAfterReading);
        Run(nameof(InteractionCreatesTwentySecondAutomaticQuietPeriod), InteractionCreatesTwentySecondAutomaticQuietPeriod);
        Run(nameof(BusyAndGameUseStricterAutomaticBudgets), BusyAndGameUseStricterAutomaticBudgets);
        Run(nameof(DialogueAndNormalizedTextHaveIndependentCooldowns), DialogueAndNormalizedTextHaveIndependentCooldowns);
        Run(nameof(SameSessionAutomaticCuesShareOneBudgetAuthorization), SameSessionAutomaticCuesShareOneBudgetAuthorization);
        Run(nameof(SameSessionBypassesLineButNotSemanticCooldown), SameSessionBypassesLineButNotSemanticCooldown);
        Run(nameof(WallClockChangesDoNotAffectBubbleBudgets), WallClockChangesDoNotAffectBubbleBudgets);
        Run(nameof(ManualPreviewSkipsBudgetsButRespectsOccupancy), ManualPreviewSkipsBudgetsButRespectsOccupancy);
        Run(nameof(VisibleBubbleRejectsSamePriorityReplacement), VisibleBubbleRejectsSamePriorityReplacement);
        Run(nameof(HigherPriorityBubbleCanReplaceAndReleaseEndsOccupancy), HigherPriorityBubbleCanReplaceAndReleaseEndsOccupancy);
    }

    private static void EvaluateIsPureUntilCommitted()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        var request = Request(1, BubbleRequestKind.Automatic, "a", "text-a");

        BehaviorTestCheck.True(arbiter.Evaluate(request).Allowed);
        BehaviorTestCheck.True(arbiter.Evaluate(request).Allowed);
        BehaviorTestCheck.Equal(0, arbiter.Snapshot(request.Context).AutomaticCount);
        BehaviorTestCheck.True(arbiter.TryCommit(request).Allowed);
        BehaviorTestCheck.Equal(1, arbiter.Snapshot(request.Context).AutomaticCount);
    }

    private static void InteractionBudgetAllowsFourPerThirtySeconds()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        for (var index = 0; index < 4; index++)
        {
            BehaviorTestCheck.True(
                arbiter.TryCommit(
                    Request(
                        index + 1,
                        BubbleRequestKind.Interaction,
                        $"dialogue-{index}",
                        $"text-{index}")).Allowed);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        var rejected = arbiter.TryCommit(
            Request(10, BubbleRequestKind.Interaction, "fifth", "fifth"));
        BehaviorTestCheck.False(rejected.Allowed);
        BehaviorTestCheck.Equal("interaction-budget", rejected.RejectionReason!);

        clock.Advance(TimeSpan.FromSeconds(27));
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(11, BubbleRequestKind.Interaction, "after", "after")).Allowed);
    }

    private static void InteractionCreatesTwentySecondAutomaticQuietPeriod()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(1, BubbleRequestKind.Interaction, "touch", "touch")).Allowed);

        clock.Advance(TimeSpan.FromMilliseconds(19_999));
        var rejected = arbiter.Evaluate(
            Request(2, BubbleRequestKind.Automatic, "auto", "auto"));
        BehaviorTestCheck.Equal("post-interaction-quiet", rejected.RejectionReason!);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(3, BubbleRequestKind.Automatic, "auto", "auto")).Allowed);
    }

    private static void RequiredInteractionFeedbackBypassesSuppression()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        for (var index = 0; index < 4; index++)
        {
            BehaviorTestCheck.True(
                arbiter.TryCommit(
                    Request(
                        index + 1,
                        BubbleRequestKind.Interaction,
                        $"dialogue-{index}",
                        $"text-{index}")).Allowed);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        BubbleDecision required = arbiter.TryCommit(
            Request(
                10,
                BubbleRequestKind.Interaction,
                "required-dialogue",
                "required-text",
                requiredInteractionFeedback: true));

        BehaviorTestCheck.True(required.Allowed);
        BehaviorTestCheck.Equal(
            5,
            arbiter.Snapshot(BehaviorContextSnapshot.Empty).InteractionCount);
    }

    private static void RepeatedRequiredInteractionBypassesSemanticCooldownAfterReading()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        BubbleDisplayRequest first = Request(
            1,
            BubbleRequestKind.Interaction,
            "first-variant",
            "same-semantic-cell",
            requiredInteractionFeedback: true);
        BehaviorTestCheck.True(arbiter.TryCommit(first).Allowed);

        clock.Advance(TimeSpan.FromMilliseconds(499));
        BubbleDecision stillReading = arbiter.Evaluate(
            Request(
                2,
                BubbleRequestKind.Interaction,
                "second-variant",
                "same-semantic-cell",
                requiredInteractionFeedback: true));
        BehaviorTestCheck.False(stillReading.Allowed);
        BehaviorTestCheck.Equal("bubble-active", stillReading.RejectionReason!);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(
                    2,
                    BubbleRequestKind.Interaction,
                    "second-variant",
                    "same-semantic-cell",
                    requiredInteractionFeedback: true)).Allowed);
    }

    private static void BusyAndGameUseStricterAutomaticBudgets()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        var busy = new BehaviorContextSnapshot
        {
            ApplicationCategory = "office",
            IsBusy = true,
        };
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(1, BubbleRequestKind.Automatic, "busy-1", "busy-1", busy)).Allowed);
        clock.Advance(TimeSpan.FromSeconds(119));
        BehaviorTestCheck.Equal(
            "automatic-minimum-interval",
            arbiter.Evaluate(
                Request(2, BubbleRequestKind.Automatic, "busy-2", "busy-2", busy))
                .RejectionReason!);
        clock.Advance(TimeSpan.FromSeconds(1));
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(3, BubbleRequestKind.Automatic, "busy-2", "busy-2", busy)).Allowed);
        clock.Advance(TimeSpan.FromSeconds(120));
        BehaviorTestCheck.Equal(
            "automatic-budget",
            arbiter.Evaluate(
                Request(4, BubbleRequestKind.Automatic, "busy-3", "busy-3", busy))
                .RejectionReason!);

        var gameClock = new ManualDualClock();
        var gameArbiter = new BubbleArbiter(gameClock);
        var game = new BehaviorContextSnapshot { ApplicationCategory = "game" };
        BehaviorTestCheck.True(
            gameArbiter.TryCommit(
                Request(5, BubbleRequestKind.Automatic, "game-1", "game-1", game)).Allowed);
        gameClock.Advance(TimeSpan.FromSeconds(180));
        BehaviorTestCheck.Equal(
            "automatic-budget",
            gameArbiter.Evaluate(
                Request(6, BubbleRequestKind.Automatic, "game-2", "game-2", game))
                .RejectionReason!);
    }

    private static void DialogueAndNormalizedTextHaveIndependentCooldowns()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(1, BubbleRequestKind.Interaction, "same-id", "first")).Allowed);
        clock.Advance(TimeSpan.FromSeconds(31));

        BehaviorTestCheck.Equal(
            "dialogue-cooldown",
            arbiter.Evaluate(
                Request(2, BubbleRequestKind.Interaction, "same-id", "second"))
                .RejectionReason!);
        BehaviorTestCheck.Equal(
            "text-cooldown",
            arbiter.Evaluate(
                Request(3, BubbleRequestKind.Interaction, "other-id", "first"))
                .RejectionReason!);
    }

    private static void SameSessionAutomaticCuesShareOneBudgetAuthorization()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        var game = new BehaviorContextSnapshot { ApplicationCategory = "game" };

        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(
                    1,
                    BubbleRequestKind.Automatic,
                    "timeline-first",
                    "timeline-first",
                    game)).Allowed);
        clock.Advance(TimeSpan.FromSeconds(1));
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(
                    1,
                    BubbleRequestKind.Automatic,
                    "timeline-second",
                    "timeline-second",
                    game)).Allowed);

        BubbleBudgetSnapshot snapshot = arbiter.Snapshot(game);
        BehaviorTestCheck.Equal(1, snapshot.AutomaticCount);
        BehaviorTestCheck.Equal(
            TimeSpan.FromSeconds(1),
            snapshot.LastAutomaticDisplay!.Value);

        var nextSession = Request(
            2,
            BubbleRequestKind.Automatic,
            "next-session",
            "next-session",
            game);
        BehaviorTestCheck.Equal(
            "bubble-active",
            arbiter.Evaluate(nextSession).RejectionReason!);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        BehaviorTestCheck.Equal(
            "automatic-minimum-interval",
            arbiter.Evaluate(nextSession).RejectionReason!);

        clock.Advance(TimeSpan.FromSeconds(179.5));
        BehaviorTestCheck.Equal(
            "automatic-budget",
            arbiter.Evaluate(nextSession).RejectionReason!);
    }

    private static void SameSessionBypassesLineButNotSemanticCooldown()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);

        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(
                    1,
                    BubbleRequestKind.Interaction,
                    "shared-dialogue",
                    "shared-text")).Allowed);
        clock.Advance(TimeSpan.FromSeconds(1));
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(
                    1,
                    BubbleRequestKind.Interaction,
                    "shared-dialogue",
                    "second-text")).Allowed);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        BubbleDecision repeatedMeaning = arbiter.TryCommit(
                Request(
                    1,
                    BubbleRequestKind.Interaction,
                    "second-dialogue",
                    "shared-text"));
        BehaviorTestCheck.False(repeatedMeaning.Allowed);
        BehaviorTestCheck.Equal(
            "text-cooldown",
            repeatedMeaning.RejectionReason!);
        BehaviorTestCheck.Equal(
            1,
            arbiter.Snapshot(BehaviorContextSnapshot.Empty).InteractionCount);

        BehaviorTestCheck.Equal(
            "dialogue-cooldown",
            arbiter.Evaluate(
                Request(
                    2,
                    BubbleRequestKind.Interaction,
                    "shared-dialogue",
                    "third-text")).RejectionReason!);
        BehaviorTestCheck.Equal(
            "text-cooldown",
            arbiter.Evaluate(
                Request(
                    3,
                    BubbleRequestKind.Interaction,
                    "third-dialogue",
                    "shared-text")).RejectionReason!);
    }

    private static void WallClockChangesDoNotAffectBubbleBudgets()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(1, BubbleRequestKind.Automatic, "a", "a")).Allowed);

        clock.AdjustWallClock(TimeSpan.FromDays(-10));
        clock.AdvanceMonotonicOnly(TimeSpan.FromSeconds(45));
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(2, BubbleRequestKind.Automatic, "b", "b")).Allowed);
    }

    private static void ManualPreviewSkipsBudgetsButRespectsOccupancy()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(
                    1,
                    BubbleRequestKind.ManualPreview,
                    "same-dialogue",
                    "same-text")).Allowed);

        BubbleDecision overlapping = arbiter.TryCommit(
            Request(
                2,
                BubbleRequestKind.ManualPreview,
                "other-dialogue",
                "other-text"));
        BehaviorTestCheck.False(overlapping.Allowed);
        BehaviorTestCheck.Equal("bubble-active", overlapping.RejectionReason!);

        clock.Advance(TimeSpan.FromMilliseconds(500));
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(
                    2,
                    BubbleRequestKind.ManualPreview,
                    "other-dialogue",
                    "other-text")).Allowed);

        BubbleBudgetSnapshot snapshot =
            arbiter.Snapshot(BehaviorContextSnapshot.Empty);
        BehaviorTestCheck.Equal(0, snapshot.InteractionCount);
        BehaviorTestCheck.Equal(0, snapshot.AutomaticCount);
    }

    private static void VisibleBubbleRejectsSamePriorityReplacement()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        BubbleDisplayRequest first = Request(
            1,
            BubbleRequestKind.Interaction,
            "first",
            "first") with
        {
            VisibleFor = TimeSpan.FromSeconds(5),
            Priority = 50,
        };
        BehaviorTestCheck.True(arbiter.TryCommit(first).Allowed);

        clock.Advance(TimeSpan.FromSeconds(2));
        BubbleDecision blocked = arbiter.Evaluate(
            Request(
                2,
                BubbleRequestKind.Interaction,
                "second",
                "second") with
            {
                Priority = 50,
            });
        BehaviorTestCheck.False(blocked.Allowed);
        BehaviorTestCheck.Equal("bubble-active", blocked.RejectionReason!);

        clock.Advance(TimeSpan.FromSeconds(3));
        BehaviorTestCheck.True(
            arbiter.Evaluate(
                Request(
                    2,
                    BubbleRequestKind.Interaction,
                    "second",
                    "second")).Allowed);
    }

    private static void HigherPriorityBubbleCanReplaceAndReleaseEndsOccupancy()
    {
        var clock = new ManualDualClock();
        var arbiter = new BubbleArbiter(clock);
        BehaviorTestCheck.True(
            arbiter.TryCommit(
                Request(
                    1,
                    BubbleRequestKind.Automatic,
                    "automatic",
                    "automatic") with
                {
                    VisibleFor = TimeSpan.FromSeconds(7),
                    Priority = 20,
                }).Allowed);

        BubbleDisplayRequest important = Request(
            2,
            BubbleRequestKind.Interaction,
            "important",
            "important") with
        {
            VisibleFor = TimeSpan.FromSeconds(6),
            Priority = 90,
        };
        BehaviorTestCheck.True(arbiter.TryCommit(important).Allowed);
        arbiter.Release(important.Owner);
        BehaviorTestCheck.True(
            arbiter.Evaluate(
                Request(
                    3,
                    BubbleRequestKind.Interaction,
                    "after-release",
                    "after-release")).Allowed);
    }

    private static BubbleDisplayRequest Request(
        long token,
        BubbleRequestKind kind,
        string dialogueId,
        string textKey,
        BehaviorContextSnapshot? context = null,
        bool requiredInteractionFeedback = false) =>
        new()
        {
            Owner = new BehaviorSessionToken(token),
            Kind = kind,
            DialogueId = dialogueId,
            NormalizedTextKey = textKey,
            Priority = 20,
            VisibleFor = TimeSpan.FromMilliseconds(500),
            Context = context ?? BehaviorContextSnapshot.Empty,
            RequiredInteractionFeedback = requiredInteractionFeedback,
        };

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
}
