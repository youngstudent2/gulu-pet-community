using GuluPet.Behavior;

namespace GuluPet.Tests;

internal static class BehaviorSessionRunnerTests
{
    private static int _ran;

    public static void RunAll()
    {
        if (Interlocked.Exchange(ref _ran, 1) != 0)
        {
            return;
        }

        Run(
            nameof(EnterPerformLoopExitUseNewPlaybackTokensAndOneTerminal),
            EnterPerformLoopExitUseNewPlaybackTokensAndOneTerminal);
        Run(
            nameof(SleepEnterInterruptionRevokesAndCompletedEnterHoldsLoop),
            SleepEnterInterruptionRevokesAndCompletedEnterHoldsLoop);
        Run(
            nameof(WakeRequestedDuringSleepEnterSkipsStableLoop),
            WakeRequestedDuringSleepEnterSkipsStableLoop);
        Run(
            nameof(RestoredSleepingSessionStartsDirectlyAtLoop),
            RestoredSleepingSessionStartsDirectlyAtLoop);
        Run(
            nameof(SleepExitStartFailureThrowsInsteadOfReportingAccepted),
            SleepExitStartFailureThrowsInsteadOfReportingAccepted);
        Run(
            nameof(SleepExitRunsBeforeWakeBodyAndCompletesNormal),
            SleepExitRunsBeforeWakeBodyAndCompletesNormal);
        Run(
            nameof(AnimationVariantSelectionIsStickyForEveryBucket),
            AnimationVariantSelectionIsStickyForEveryBucket);
        Run(
            nameof(GenericAnimationVariantSticksAcrossSessionRoles),
            GenericAnimationVariantSticksAcrossSessionRoles);
        Run(
            nameof(SleepVariantSurvivesInterruptedLoopContinuation),
            SleepVariantSurvivesInterruptedLoopContinuation);
        Run(
            nameof(CompletedSleepClearsVariantForTheNextSession),
            CompletedSleepClearsVariantForTheNextSession);
        Run(
            nameof(WakeDuringVariantEnterUsesTheSameExit),
            WakeDuringVariantEnterUsesTheSameExit);
        Run(
            nameof(AnimationVariantsFailClosedBeforeRandomSelection),
            AnimationVariantsFailClosedBeforeRandomSelection);
        Run(
            nameof(FixedAnimationPlanDoesNotConsumeVariantRandom),
            FixedAnimationPlanDoesNotConsumeVariantRandom);
        Run(
            nameof(CueZeroUsesFirstFrameT0AndVisibleStallShowsOnlyLatest),
            CueZeroUsesFirstFrameT0AndVisibleStallShowsOnlyLatest);
        Run(
            nameof(HiddenCuesAreConsumedWithoutCatchUp),
            HiddenCuesAreConsumedWithoutCatchUp);
        Run(
            nameof(BubblePortFailureDoesNotBlockAnimationCompletion),
            BubblePortFailureDoesNotBlockAnimationCompletion);
        Run(
            nameof(FirstFrameAndSessionWatchdogsTerminateExactlyOnce),
            FirstFrameAndSessionWatchdogsTerminateExactlyOnce);
        Run(
            nameof(TryStartRejectsBusySessionWithoutInterruptingIt),
            TryStartRejectsBusySessionWithoutInterruptingIt);
    }

    private static void EnterPerformLoopExitUseNewPlaybackTokensAndOneTerminal()
    {
        var clock = new ManualDualClock();
        var machine = new HierarchicalPetStateMachine();
        var animation = new FakeAnimationPort("enter", "perform", "loop", "exit");
        var bubbles = new FakeBubblePort();
        var runner = CreateRunner(clock, machine, animation, bubbles);
        var terminals = new List<BehaviorTerminalRecord>();
        var commits = new List<BehaviorSessionCommitRecord>();
        runner.Terminated += terminals.Add;
        runner.Committed += commits.Add;

        BehaviorDefinition definition = Definition(
            "lifecycle",
            new BehaviorAnimationPlan
            {
                EnterClipId = "enter",
                PerformClipId = "perform",
                LoopClipId = "loop",
                ExitClipId = "exit",
            },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.LoopCount,
                LoopCount = 2,
            });

        BehaviorTestCheck.True(
            runner.TryStart(Request(1, definition.Id, clock), definition, out _));
        BehaviorTestCheck.Equal(
            BehaviorPhase.AwaitingFirstFrame,
            runner.Snapshot!.Phase);

        BehaviorPlaybackToken enter = CurrentToken(runner);
        BehaviorTestCheck.True(runner.NotifyFirstFrame(enter));
        BehaviorTestCheck.Equal(BehaviorPhase.Entering, runner.Snapshot!.Phase);
        BehaviorTestCheck.Equal(1, commits.Count);
        BehaviorTestCheck.Equal(1L, commits[0].RequestId);

        BehaviorTestCheck.True(runner.NotifyClipCompleted(enter));
        BehaviorPlaybackToken perform = CurrentToken(runner);
        BehaviorTestCheck.True(runner.NotifyFirstFrame(perform));
        BehaviorTestCheck.Equal(BehaviorPhase.Performing, runner.Snapshot!.Phase);
        BehaviorTestCheck.True(runner.NotifyClipCompleted(perform));

        BehaviorPlaybackToken loopOne = CurrentToken(runner);
        BehaviorTestCheck.True(runner.NotifyFirstFrame(loopOne));
        BehaviorTestCheck.True(runner.NotifyClipCompleted(loopOne));
        BehaviorPlaybackToken loopTwo = CurrentToken(runner);
        BehaviorTestCheck.True(runner.NotifyFirstFrame(loopTwo));
        BehaviorTestCheck.True(runner.NotifyClipCompleted(loopTwo));

        BehaviorPlaybackToken exit = CurrentToken(runner);
        BehaviorTestCheck.True(runner.NotifyFirstFrame(exit));
        BehaviorTestCheck.Equal(BehaviorPhase.Exiting, runner.Snapshot!.Phase);
        BehaviorTestCheck.True(runner.NotifyClipCompleted(exit));

        BehaviorTestCheck.False(runner.HasActiveSession);
        BehaviorTestCheck.Equal(1, terminals.Count);
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Completed,
            terminals[0].Status);
        BehaviorTestCheck.Equal(1L, terminals[0].RequestId);
        BehaviorTestCheck.False(runner.NotifyClipCompleted(exit));
        BehaviorTestCheck.False(runner.Interrupt());
        BehaviorTestCheck.SequenceEqual(
            new long[] { 1, 2, 3, 4, 5 },
            animation.Requests.Select(static request =>
                request.PlaybackToken.Value));
        BehaviorTestCheck.SequenceEqual(
            new[]
            {
                BehaviorClipRole.Enter,
                BehaviorClipRole.Perform,
                BehaviorClipRole.Loop,
                BehaviorClipRole.Loop,
                BehaviorClipRole.Exit,
            },
            animation.Requests.Select(static request => request.Role));
    }

    private static void SleepEnterInterruptionRevokesAndCompletedEnterHoldsLoop()
    {
        var clock = new ManualDualClock();
        var machine = new HierarchicalPetStateMachine();
        var animation = new FakeAnimationPort(
            "sleep_enter",
            "sleep_loop",
            "sleep_exit");
        var bubbles = new FakeBubblePort();
        var runner = CreateRunner(clock, machine, animation, bubbles);
        var terminals = new List<BehaviorTerminalRecord>();
        runner.Terminated += terminals.Add;

        BehaviorDefinition definition = Definition(
            "sleep",
            new BehaviorAnimationPlan
            {
                EnterClipId = "sleep_enter",
                LoopClipId = "sleep_loop",
                ExitClipId = "sleep_exit",
            },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.External,
            },
            targetState: StablePetState.Sleeping,
            queueable: false,
            maximumDuration: TimeSpan.FromSeconds(2));

        BehaviorTestCheck.True(
            runner.TryStart(Request(1, definition.Id, clock), definition, out _));
        BehaviorPlaybackToken interruptedEnter = CurrentToken(runner);
        BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);
        BehaviorTestCheck.Equal(PetLifecyclePhase.Idle, machine.Phase);

        BehaviorTestCheck.True(runner.NotifyFirstFrame(interruptedEnter));
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.SleepingEntering,
            machine.Phase);
        BehaviorTestCheck.True(
            runner.Interrupt(
                BehaviorTerminalStatus.Interrupted,
                "direct interaction"));
        BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);
        BehaviorTestCheck.Equal(PetLifecyclePhase.Idle, machine.Phase);
        BehaviorTestCheck.False(
            runner.NotifyClipCompleted(interruptedEnter));

        BehaviorTestCheck.True(
            runner.TryStart(Request(2, definition.Id, clock), definition, out _));
        BehaviorPlaybackToken enter = CurrentToken(runner);
        runner.NotifyFirstFrame(enter);
        runner.NotifyClipCompleted(enter);
        BehaviorTestCheck.Equal(StablePetState.Sleeping, machine.StableState);
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.SleepingLooping,
            machine.Phase);

        BehaviorPlaybackToken loop = CurrentToken(runner);
        runner.NotifyFirstFrame(loop);
        clock.Advance(TimeSpan.FromSeconds(10));
        runner.Advance();
        BehaviorTestCheck.True(runner.HasActiveSession);

        BehaviorTestCheck.True(runner.RequestExternalCompletion());
        BehaviorPlaybackToken exit = CurrentToken(runner);
        BehaviorTestCheck.True(runner.NotifyFirstFrame(exit));
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.SleepingExiting,
            machine.Phase);
        BehaviorTestCheck.True(runner.NotifyClipCompleted(exit));
        BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);
        BehaviorTestCheck.Equal(PetLifecyclePhase.Idle, machine.Phase);
        BehaviorTestCheck.Equal(2, terminals.Count);
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Interrupted,
            terminals[0].Status);
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Completed,
            terminals[1].Status);
    }

    private static void RestoredSleepingSessionStartsDirectlyAtLoop()
    {
        var clock = new ManualDualClock();
        var machine =
            new HierarchicalPetStateMachine(StablePetState.Sleeping);
        var animation =
            new FakeAnimationPort("sleep_enter", "sleep_loop", "sleep_exit");
        var runner = CreateRunner(
            clock,
            machine,
            animation,
            new FakeBubblePort());
        BehaviorDefinition definition = Definition(
            "sleep_resume",
            new BehaviorAnimationPlan
            {
                EnterClipId = "sleep_enter",
                LoopClipId = "sleep_loop",
                ExitClipId = "sleep_exit",
            },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.External,
            },
            targetState: StablePetState.Sleeping,
            allowedStates: [StablePetState.Sleeping],
            queueable: false);

        BehaviorTestCheck.True(
            runner.TryStart(Request(1, definition.Id, clock), definition, out _));
        BehaviorTestCheck.Equal(1, animation.Requests.Count);
        BehaviorTestCheck.Equal(
            BehaviorClipRole.Loop,
            animation.Requests[0].Role);
        runner.NotifyFirstFrame(CurrentToken(runner));
        BehaviorTestCheck.Equal(StablePetState.Sleeping, machine.StableState);
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.SleepingLooping,
            machine.Phase);

        BehaviorTestCheck.True(runner.RequestExternalCompletion());
        BehaviorPlaybackToken exit = CurrentToken(runner);
        runner.NotifyFirstFrame(exit);
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.SleepingExiting,
            machine.Phase);
        runner.NotifyClipCompleted(exit);
        BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);
        BehaviorTestCheck.Equal(PetLifecyclePhase.Idle, machine.Phase);
    }

    private static void WakeRequestedDuringSleepEnterSkipsStableLoop()
    {
        var clock = new ManualDualClock();
        var machine = new HierarchicalPetStateMachine();
        var animation = new FakeAnimationPort(
            "sleep_enter",
            "sleep_loop",
            "sleep_exit");
        var runner = CreateRunner(
            clock,
            machine,
            animation,
            new FakeBubblePort());
        BehaviorDefinition definition = Definition(
            "sleep",
            new BehaviorAnimationPlan
            {
                EnterClipId = "sleep_enter",
                LoopClipId = "sleep_loop",
                ExitClipId = "sleep_exit",
            },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.External,
            },
            targetState: StablePetState.Sleeping,
            queueable: false);

        BehaviorTestCheck.True(
            runner.TryStart(Request(1, definition.Id, clock), definition, out _));
        BehaviorPlaybackToken enter = CurrentToken(runner);
        BehaviorTestCheck.True(runner.NotifyFirstFrame(enter));
        BehaviorTestCheck.True(runner.RequestExternalCompletion());
        BehaviorTestCheck.True(runner.NotifyClipCompleted(enter));
        BehaviorTestCheck.SequenceEqual(
            new[] { BehaviorClipRole.Enter, BehaviorClipRole.Exit },
            animation.Requests.Select(static request => request.Role));

        BehaviorPlaybackToken exit = CurrentToken(runner);
        BehaviorTestCheck.True(runner.NotifyFirstFrame(exit));
        BehaviorTestCheck.True(runner.NotifyClipCompleted(exit));
        BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Completed,
            runner.LastTerminal!.Status);
    }

    private static void SleepExitStartFailureThrowsInsteadOfReportingAccepted()
    {
        var clock = new ManualDualClock();
        var machine =
            new HierarchicalPetStateMachine(StablePetState.Sleeping);
        var animation =
            new FakeAnimationPort("sleep_loop", "sleep_exit");
        var runner = CreateRunner(
            clock,
            machine,
            animation,
            new FakeBubblePort());
        BehaviorDefinition definition = Definition(
            "sleep_resume",
            new BehaviorAnimationPlan
            {
                LoopClipId = "sleep_loop",
                ExitClipId = "sleep_exit",
            },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.External,
            },
            targetState: StablePetState.Sleeping,
            allowedStates: [StablePetState.Sleeping],
            queueable: false);

        BehaviorTestCheck.True(
            runner.TryStart(Request(1, definition.Id, clock), definition, out _));
        BehaviorTestCheck.True(runner.NotifyFirstFrame(CurrentToken(runner)));
        animation.RejectedClips.Add("sleep_exit");

        InvalidOperationException failure =
            BehaviorTestCheck.Throws<InvalidOperationException>(
                () => runner.RequestExternalCompletion());

        BehaviorTestCheck.True(
            failure.Message.Contains(
                "External completion failed",
                StringComparison.Ordinal));
        BehaviorTestCheck.False(runner.HasActiveSession);
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Failed,
            runner.LastTerminal!.Status);
        BehaviorTestCheck.Equal(
            StablePetState.Sleeping,
            machine.StableState);
    }

    private static void SleepExitRunsBeforeWakeBodyAndCompletesNormal()
    {
        var clock = new ManualDualClock();
        var machine =
            new HierarchicalPetStateMachine(StablePetState.Sleeping);
        var animation = new FakeAnimationPort("sleep_exit", "blink");
        var runner = CreateRunner(
            clock,
            machine,
            animation,
            new FakeBubblePort());
        BehaviorDefinition definition = Definition(
            "wake",
            new BehaviorAnimationPlan
            {
                ExitClipId = "sleep_exit",
                PerformClipId = "blink",
            },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.ClipEnd,
            },
            targetState: StablePetState.Normal,
            allowedStates: [StablePetState.Sleeping]);

        BehaviorTestCheck.True(
            runner.TryStart(Request(1, definition.Id, clock), definition, out _));
        BehaviorPlaybackToken exit = CurrentToken(runner);
        BehaviorTestCheck.Equal(StablePetState.Sleeping, machine.StableState);
        runner.NotifyFirstFrame(exit);
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.SleepingExiting,
            machine.Phase);
        runner.NotifyClipCompleted(exit);
        BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);
        BehaviorTestCheck.Equal(
            BehaviorClipRole.Perform,
            animation.Requests[^1].Role);

        BehaviorPlaybackToken blink = CurrentToken(runner);
        runner.NotifyFirstFrame(blink);
        runner.NotifyClipCompleted(blink);
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Completed,
            runner.LastTerminal!.Status);
        BehaviorTestCheck.SequenceEqual(
            new[] { "sleep_exit", "blink" },
            animation.Requests.Select(static request => request.ClipId));
    }

    private static void AnimationVariantSelectionIsStickyForEveryBucket()
    {
        foreach ((double randomValue, string suffix) in
                 new[]
                 {
                     (0.0, ""),
                     (0.34, "_b"),
                     (0.999_999, "_c"),
                 })
        {
            var clock = new ManualDualClock();
            var machine = new HierarchicalPetStateMachine();
            var animation = new FakeAnimationPort(AllVariantSleepClips());
            var random = new SequenceRandomSource(randomValue);
            var runner = CreateRunner(
                clock,
                machine,
                animation,
                new FakeBubblePort(),
                animationVariantRandom: random);
            BehaviorDefinition definition = VariantSleepDefinition();

            BehaviorTestCheck.True(
                runner.TryStart(
                    Request(1, definition.Id, clock),
                    definition,
                    out _));
            BehaviorTestCheck.Equal(
                $"sleep_enter{suffix}",
                runner.Snapshot!.ClipId!);
            BehaviorPlaybackToken enter = CurrentToken(runner);
            BehaviorTestCheck.True(runner.NotifyFirstFrame(enter));
            BehaviorTestCheck.True(runner.NotifyClipCompleted(enter));
            BehaviorTestCheck.Equal(
                $"sleep_loop{suffix}",
                runner.Snapshot!.ClipId!);
            BehaviorPlaybackToken loop = CurrentToken(runner);
            BehaviorTestCheck.True(runner.NotifyFirstFrame(loop));
            BehaviorTestCheck.True(runner.RequestExternalCompletion());
            BehaviorTestCheck.Equal(
                $"sleep_exit{suffix}",
                runner.Snapshot!.ClipId!);
            BehaviorPlaybackToken exit = CurrentToken(runner);
            BehaviorTestCheck.True(runner.NotifyFirstFrame(exit));
            BehaviorTestCheck.True(runner.NotifyClipCompleted(exit));

            BehaviorTestCheck.Equal(1, random.Calls);
            BehaviorTestCheck.SequenceEqual(
                new[]
                {
                    $"sleep_enter{suffix}",
                    $"sleep_loop{suffix}",
                    $"sleep_exit{suffix}",
                },
                animation.Requests.Select(static request => request.ClipId));
            BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);
        }
    }

    private static void GenericAnimationVariantSticksAcrossSessionRoles()
    {
        var clock = new ManualDualClock();
        var animation = new FakeAnimationPort(
            "enter_a",
            "perform_a",
            "exit_a",
            "enter_b",
            "perform_b",
            "exit_b");
        var random = new SequenceRandomSource(0.75);
        var runner = CreateRunner(
            clock,
            new HierarchicalPetStateMachine(),
            animation,
            new FakeBubblePort(),
            animationVariantRandom: random);
        BehaviorDefinition definition = Definition(
            "generic_variant",
            new BehaviorAnimationPlan
            {
                VariantId = "a",
                EnterClipId = "enter_a",
                PerformClipId = "perform_a",
                ExitClipId = "exit_a",
                Variants =
                [
                    new BehaviorAnimationVariant
                    {
                        Id = "b",
                        EnterClipId = "enter_b",
                        PerformClipId = "perform_b",
                        ExitClipId = "exit_b",
                    },
                ],
            },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.ClipEnd,
            });

        BehaviorTestCheck.True(
            runner.TryStart(Request(1, definition.Id, clock), definition, out _));
        BehaviorPlaybackToken enter = CurrentToken(runner);
        runner.NotifyFirstFrame(enter);
        runner.NotifyClipCompleted(enter);
        BehaviorPlaybackToken perform = CurrentToken(runner);
        runner.NotifyFirstFrame(perform);
        runner.NotifyClipCompleted(perform);
        BehaviorPlaybackToken exit = CurrentToken(runner);
        runner.NotifyFirstFrame(exit);
        runner.NotifyClipCompleted(exit);

        BehaviorTestCheck.SequenceEqual(
            new[] { "enter_b", "perform_b", "exit_b" },
            animation.Requests.Select(static request => request.ClipId));
        BehaviorTestCheck.Equal(1, random.Calls);
    }

    private static void SleepVariantSurvivesInterruptedLoopContinuation()
    {
        var clock = new ManualDualClock();
        var machine = new HierarchicalPetStateMachine();
        var animation = new FakeAnimationPort(AllVariantSleepClips());
        var random = new SequenceRandomSource(0.5);
        var runner = CreateRunner(
            clock,
            machine,
            animation,
            new FakeBubblePort(),
            animationVariantRandom: random);
        BehaviorDefinition definition = VariantSleepDefinition();

        BehaviorTestCheck.True(
            runner.TryStart(Request(1, definition.Id, clock), definition, out _));
        BehaviorPlaybackToken enter = CurrentToken(runner);
        runner.NotifyFirstFrame(enter);
        runner.NotifyClipCompleted(enter);
        BehaviorPlaybackToken firstLoop = CurrentToken(runner);
        runner.NotifyFirstFrame(firstLoop);
        BehaviorTestCheck.Equal("sleep_loop_b", runner.Snapshot!.ClipId!);
        BehaviorTestCheck.True(
            runner.Interrupt(
                BehaviorTerminalStatus.Interrupted,
                "external control pause"));
        BehaviorTestCheck.Equal(StablePetState.Sleeping, machine.StableState);
        BehaviorTestCheck.Equal(1, random.Calls);

        BehaviorTestCheck.True(
            runner.TryStart(Request(2, definition.Id, clock), definition, out _));
        BehaviorTestCheck.Equal("sleep_loop_b", runner.Snapshot!.ClipId!);
        BehaviorTestCheck.Equal(1, random.Calls);
        BehaviorPlaybackToken resumedLoop = CurrentToken(runner);
        runner.NotifyFirstFrame(resumedLoop);
        BehaviorTestCheck.True(runner.RequestExternalCompletion());
        BehaviorTestCheck.Equal("sleep_exit_b", runner.Snapshot!.ClipId!);
        BehaviorPlaybackToken exit = CurrentToken(runner);
        runner.NotifyFirstFrame(exit);
        runner.NotifyClipCompleted(exit);
        BehaviorTestCheck.Equal(1, random.Calls);
        BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);
    }

    private static void CompletedSleepClearsVariantForTheNextSession()
    {
        var clock = new ManualDualClock();
        var machine = new HierarchicalPetStateMachine();
        var animation = new FakeAnimationPort(AllVariantSleepClips());
        var random = new SequenceRandomSource(0.5, 0.9);
        var runner = CreateRunner(
            clock,
            machine,
            animation,
            new FakeBubblePort(),
            animationVariantRandom: random);
        BehaviorDefinition definition = VariantSleepDefinition();

        BehaviorTestCheck.True(
            runner.TryStart(Request(1, definition.Id, clock), definition, out _));
        BehaviorPlaybackToken enter = CurrentToken(runner);
        runner.NotifyFirstFrame(enter);
        runner.NotifyClipCompleted(enter);
        BehaviorPlaybackToken loop = CurrentToken(runner);
        runner.NotifyFirstFrame(loop);
        runner.RequestExternalCompletion();
        BehaviorPlaybackToken exit = CurrentToken(runner);
        runner.NotifyFirstFrame(exit);
        runner.NotifyClipCompleted(exit);
        BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);

        BehaviorTestCheck.True(
            runner.TryStart(Request(2, definition.Id, clock), definition, out _));
        BehaviorTestCheck.Equal("sleep_enter_c", runner.Snapshot!.ClipId!);
        BehaviorTestCheck.Equal(2, random.Calls);
    }

    private static void WakeDuringVariantEnterUsesTheSameExit()
    {
        var clock = new ManualDualClock();
        var animation = new FakeAnimationPort(AllVariantSleepClips());
        var random = new SequenceRandomSource(0.9);
        var runner = CreateRunner(
            clock,
            new HierarchicalPetStateMachine(),
            animation,
            new FakeBubblePort(),
            animationVariantRandom: random);
        BehaviorDefinition definition = VariantSleepDefinition();

        BehaviorTestCheck.True(
            runner.TryStart(Request(1, definition.Id, clock), definition, out _));
        BehaviorPlaybackToken enter = CurrentToken(runner);
        runner.NotifyFirstFrame(enter);
        BehaviorTestCheck.True(runner.RequestExternalCompletion());
        runner.NotifyClipCompleted(enter);

        BehaviorTestCheck.SequenceEqual(
            new[] { "sleep_enter_c", "sleep_exit_c" },
            animation.Requests.Select(static request => request.ClipId));
        BehaviorTestCheck.Equal(1, random.Calls);
    }

    private static void AnimationVariantsFailClosedBeforeRandomSelection()
    {
        var clock = new ManualDualClock();
        var available = AllVariantSleepClips()
            .Where(static clip => clip != "sleep_exit_c")
            .ToArray();
        var random = new SequenceRandomSource(0.1);
        var runner = CreateRunner(
            clock,
            new HierarchicalPetStateMachine(),
            new FakeAnimationPort(available),
            new FakeBubblePort(),
            animationVariantRandom: random);
        BehaviorDefinition definition = VariantSleepDefinition();

        InvalidOperationException failure =
            BehaviorTestCheck.Throws<InvalidOperationException>(
                () => runner.TryStart(
                    Request(1, definition.Id, clock),
                    definition,
                    out _));
        BehaviorTestCheck.True(
            failure.Message.Contains("sleep_exit_c", StringComparison.Ordinal));
        BehaviorTestCheck.Equal(0, random.Calls);
    }

    private static void FixedAnimationPlanDoesNotConsumeVariantRandom()
    {
        var clock = new ManualDualClock();
        var random = new SequenceRandomSource();
        var runner = CreateRunner(
            clock,
            new HierarchicalPetStateMachine(),
            new FakeAnimationPort("fixed"),
            new FakeBubblePort(),
            animationVariantRandom: random);
        BehaviorDefinition definition = Definition(
            "fixed",
            new BehaviorAnimationPlan { PerformClipId = "fixed" },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.ClipEnd,
            });

        BehaviorTestCheck.True(
            runner.TryStart(Request(1, definition.Id, clock), definition, out _));
        BehaviorTestCheck.Equal(0, random.Calls);
    }

    private static void CueZeroUsesFirstFrameT0AndVisibleStallShowsOnlyLatest()
    {
        var clock = new ManualDualClock();
        var animation = new FakeAnimationPort("talk");
        var bubbles = new FakeBubblePort();
        var runner = CreateRunner(
            clock,
            new HierarchicalPetStateMachine(),
            animation,
            bubbles);
        BehaviorDefinition definition = Definition(
            "talk",
            new BehaviorAnimationPlan { PerformClipId = "talk" },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.ClipEnd,
            },
            bubbleCues:
            [
                Cue(0, "cue-0"),
                Cue(1000, "cue-1"),
                Cue(2500, "cue-2"),
            ],
            maximumDuration: TimeSpan.FromSeconds(6));

        runner.TryStart(Request(1, definition.Id, clock), definition, out _);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        runner.NotifyFirstFrame(CurrentToken(runner));
        BehaviorTestCheck.Equal(TimeSpan.Zero, runner.Snapshot!.LogicalTime);
        BehaviorTestCheck.SequenceEqual(
            new[] { 0 },
            bubbles.Shows.Select(static show => show.CueIndex));

        clock.Advance(TimeSpan.FromMilliseconds(2600));
        runner.Advance();
        BehaviorTestCheck.SequenceEqual(
            new[] { 0, 2 },
            bubbles.Shows.Select(static show => show.CueIndex),
            "A visible UI stall must skip intermediate cues.");
        BehaviorTestCheck.Equal(
            TimeSpan.FromMilliseconds(2600),
            bubbles.Shows[^1].LogicalTime);
    }

    private static void HiddenCuesAreConsumedWithoutCatchUp()
    {
        var clock = new ManualDualClock();
        var bubbles = new FakeBubblePort();
        var runner = CreateRunner(
            clock,
            new HierarchicalPetStateMachine(),
            new FakeAnimationPort("talk"),
            bubbles);
        BehaviorDefinition definition = Definition(
            "hidden_talk",
            new BehaviorAnimationPlan { PerformClipId = "talk" },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.ClipEnd,
            },
            bubbleCues:
            [
                Cue(0, "cue-0"),
                Cue(1000, "cue-1"),
                Cue(2500, "cue-2"),
            ],
            maximumDuration: TimeSpan.FromSeconds(6));

        runner.TryStart(Request(1, definition.Id, clock), definition, out _);
        runner.NotifyFirstFrame(CurrentToken(runner));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        runner.SetVisible(false);
        clock.Advance(TimeSpan.FromSeconds(3));
        runner.Advance();
        runner.SetVisible(true);
        runner.Advance();

        BehaviorTestCheck.SequenceEqual(
            new[] { 0 },
            bubbles.Shows.Select(static show => show.CueIndex));
        BehaviorTestCheck.Equal(3, runner.Snapshot!.NextCueIndex);
        BehaviorTestCheck.Equal(1, bubbles.Hides.Count);
    }

    private static void FirstFrameAndSessionWatchdogsTerminateExactlyOnce()
    {
        var firstFrameClock = new ManualDualClock();
        var firstFrameRunner = CreateRunner(
            firstFrameClock,
            new HierarchicalPetStateMachine(),
            new FakeAnimationPort("still"),
            new FakeBubblePort(),
            firstFrameTimeout: TimeSpan.FromSeconds(1));
        BehaviorDefinition definition = Definition(
            "watchdog",
            new BehaviorAnimationPlan { PerformClipId = "still" },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.ClipEnd,
            },
            maximumDuration: TimeSpan.FromSeconds(2));

        firstFrameRunner.TryStart(
            Request(1, definition.Id, firstFrameClock),
            definition,
            out _);
        BehaviorTestCheck.False(firstFrameRunner.Snapshot!.Committed);
        firstFrameClock.Advance(TimeSpan.FromSeconds(1));
        firstFrameRunner.Advance();
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.TimedOut,
            firstFrameRunner.LastTerminal!.Status);
        BehaviorTestCheck.False(firstFrameRunner.Snapshot!.Committed);

        var sessionClock = new ManualDualClock();
        var sessionRunner = CreateRunner(
            sessionClock,
            new HierarchicalPetStateMachine(),
            new FakeAnimationPort("still"),
            new FakeBubblePort());
        var terminals = new List<BehaviorTerminalRecord>();
        sessionRunner.Terminated += terminals.Add;
        sessionRunner.TryStart(
            Request(2, definition.Id, sessionClock),
            definition,
            out _);
        BehaviorPlaybackToken playback = CurrentToken(sessionRunner);
        sessionRunner.NotifyFirstFrame(playback);
        sessionClock.Advance(TimeSpan.FromMilliseconds(2001));
        sessionRunner.Advance();
        sessionRunner.Advance();

        BehaviorTestCheck.Equal(1, terminals.Count);
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.TimedOut,
            terminals[0].Status);
        BehaviorTestCheck.False(sessionRunner.NotifyClipCompleted(playback));
        BehaviorTestCheck.False(sessionRunner.Interrupt());
    }

    private static void BubblePortFailureDoesNotBlockAnimationCompletion()
    {
        var clock = new ManualDualClock();
        var bubbles = new FakeBubblePort
        {
            ThrowOnShow = true,
            ThrowOnHide = true,
        };
        var runner = CreateRunner(
            clock,
            new HierarchicalPetStateMachine(),
            new FakeAnimationPort("talk"),
            bubbles);
        BehaviorDefinition definition = Definition(
            "bubble_failure",
            new BehaviorAnimationPlan { PerformClipId = "talk" },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.ClipEnd,
            },
            bubbleCues: [Cue(0, "cue-0")]);

        runner.TryStart(Request(1, definition.Id, clock), definition, out _);
        BehaviorPlaybackToken playback = CurrentToken(runner);
        BehaviorTestCheck.True(runner.NotifyFirstFrame(playback));
        BehaviorTestCheck.True(runner.NotifyClipCompleted(playback));
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Completed,
            runner.LastTerminal!.Status);
    }

    private static void TryStartRejectsBusySessionWithoutInterruptingIt()
    {
        var clock = new ManualDualClock();
        var animation = new FakeAnimationPort("old", "new");
        var runner = CreateRunner(
            clock,
            new HierarchicalPetStateMachine(),
            animation,
            new FakeBubblePort());
        var terminals = new List<BehaviorTerminalRecord>();
        runner.Terminated += terminals.Add;
        BehaviorDefinition oldDefinition = Definition(
            "old_behavior",
            new BehaviorAnimationPlan { PerformClipId = "old" },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.ClipEnd,
            });
        BehaviorDefinition newDefinition = Definition(
            "new_behavior",
            new BehaviorAnimationPlan { PerformClipId = "new" },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.ClipEnd,
            });

        runner.TryStart(
            Request(1, oldDefinition.Id, clock),
            oldDefinition,
            out _);
        BehaviorPlaybackToken oldPlayback = CurrentToken(runner);
        runner.NotifyFirstFrame(oldPlayback);
        BehaviorSessionToken oldSession = runner.Snapshot!.SessionToken;

        BehaviorTestCheck.False(
            runner.TryStart(
                Request(2, newDefinition.Id, clock),
                newDefinition,
                out string? rejectionReason));
        BehaviorTestCheck.Equal(
            "A behavior session is already active.",
            rejectionReason);
        BehaviorTestCheck.Equal(oldSession, runner.Snapshot!.SessionToken);
        BehaviorTestCheck.Equal(0, terminals.Count);
        BehaviorTestCheck.True(runner.NotifyClipCompleted(oldPlayback));
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Completed,
            runner.LastTerminal!.Status);
    }

    private static BehaviorSessionRunner CreateRunner(
        ManualDualClock clock,
        HierarchicalPetStateMachine machine,
        FakeAnimationPort animation,
        FakeBubblePort bubbles,
        TimeSpan? firstFrameTimeout = null,
        IRandomSource? animationVariantRandom = null) =>
        new(
            clock,
            machine,
            animation,
            bubbles,
            new BehaviorSessionRunnerOptions
            {
                FirstFrameTimeout =
                    firstFrameTimeout ?? TimeSpan.FromSeconds(2),
            },
            animationVariantRandom);

    private static BehaviorDefinition VariantSleepDefinition() =>
        Definition(
            "variant_sleep",
            new BehaviorAnimationPlan
            {
                VariantId = "a",
                EnterClipId = "sleep_enter",
                LoopClipId = "sleep_loop",
                ExitClipId = "sleep_exit",
                Variants =
                [
                    new BehaviorAnimationVariant
                    {
                        Id = "b",
                        EnterClipId = "sleep_enter_b",
                        LoopClipId = "sleep_loop_b",
                        ExitClipId = "sleep_exit_b",
                    },
                    new BehaviorAnimationVariant
                    {
                        Id = "c",
                        EnterClipId = "sleep_enter_c",
                        LoopClipId = "sleep_loop_c",
                        ExitClipId = "sleep_exit_c",
                    },
                ],
            },
            new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.External,
            },
            targetState: StablePetState.Sleeping,
            allowedStates: [StablePetState.Normal, StablePetState.Sleeping],
            queueable: false,
            maximumDuration: TimeSpan.FromMinutes(5));

    private static string[] AllVariantSleepClips() =>
    [
        "sleep_enter",
        "sleep_loop",
        "sleep_exit",
        "sleep_enter_b",
        "sleep_loop_b",
        "sleep_exit_b",
        "sleep_enter_c",
        "sleep_loop_c",
        "sleep_exit_c",
    ];

    private static BehaviorRequest Request(
        long requestId,
        BehaviorId behaviorId,
        ManualDualClock clock) =>
        new()
        {
            RequestId = requestId,
            BehaviorId = behaviorId,
            Source = BehaviorRequestSource.Interaction,
            SwitchMode = BehaviorSwitchMode.ImmediateIfIdle,
            Priority = 80,
            CreatedAt = clock.Elapsed,
            FirstCreatedAt = clock.Elapsed,
            ExpiresAt = clock.Elapsed + TimeSpan.FromMinutes(1),
            DedupeKey = $"test:{requestId}",
        };

    private static BehaviorDefinition Definition(
        string id,
        BehaviorAnimationPlan animation,
        BehaviorCompletionPolicy completion,
        StablePetState? targetState = null,
        IReadOnlyList<StablePetState>? allowedStates = null,
        IReadOnlyList<BehaviorBubbleCue>? bubbleCues = null,
        bool queueable = true,
        TimeSpan? maximumDuration = null) =>
        new()
        {
            Id = new BehaviorId(id),
            DisplayName = id,
            Family = "test",
            ClipFamily = "test",
            AllowedStates = allowedStates ?? [StablePetState.Normal],
            TargetState = targetState,
            Animation = animation,
            BubbleCues = bubbleCues ?? [],
            Completion = completion,
            MaximumDuration =
                maximumDuration ?? TimeSpan.FromSeconds(8),
            Queueable = queueable,
        };

    private static BehaviorBubbleCue Cue(
        int atMilliseconds,
        string dialogueId) =>
        new()
        {
            At = TimeSpan.FromMilliseconds(atMilliseconds),
            DialogueId = dialogueId,
            Annotation = "测试",
        };

    private static BehaviorPlaybackToken CurrentToken(
        BehaviorSessionRunner runner) =>
        runner.CurrentPlaybackToken ??
        throw new InvalidOperationException("Expected an active playback token.");

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(BehaviorSessionRunnerTests)}.{name}");
    }

    private sealed class FakeAnimationPort : IBehaviorAnimationSessionPort
    {
        public FakeAnimationPort(params string[] available)
        {
            Available = new HashSet<string>(
                available,
                StringComparer.Ordinal);
        }

        public HashSet<string> Available { get; }

        public List<BehaviorPlaybackRequest> Requests { get; } = [];

        public List<BehaviorPlaybackToken> Stops { get; } = [];

        public HashSet<string> RejectedClips { get; } =
            new(StringComparer.Ordinal);

        public bool IsClipAvailable(string clipId) =>
            Available.Contains(clipId);

        public bool TryStartPlayback(BehaviorPlaybackRequest request)
        {
            Requests.Add(request);
            return !RejectedClips.Contains(request.ClipId);
        }

        public void StopPlayback(BehaviorPlaybackToken playbackToken) =>
            Stops.Add(playbackToken);
    }

    private sealed class FakeBubblePort : IBehaviorBubbleSessionPort
    {
        public List<BehaviorBubblePresentation> Shows { get; } = [];

        public List<BehaviorSessionToken> Hides { get; } = [];

        public bool ThrowOnShow { get; init; }

        public bool ThrowOnHide { get; init; }

        public void Show(BehaviorBubblePresentation presentation)
        {
            Shows.Add(presentation);
            if (ThrowOnShow)
            {
                throw new InvalidOperationException("Synthetic bubble show failure.");
            }
        }

        public void Hide(BehaviorSessionToken ownerSessionToken)
        {
            Hides.Add(ownerSessionToken);
            if (ThrowOnHide)
            {
                throw new InvalidOperationException("Synthetic bubble hide failure.");
            }
        }
    }
}
