using GuluPet.Behavior;

namespace GuluPet.Tests;

internal static class HierarchicalPetStateMachineTests
{
    public static void RunAll()
    {
        Run(
            nameof(SleepEntryIsNotStableUntilEnterCompletes),
            SleepEntryIsNotStableUntilEnterCompletes);
        Run(
            nameof(SleepEntryInterruptionRevokesPreparedAndVisibleEntry),
            SleepEntryInterruptionRevokesPreparedAndVisibleEntry);
        Run(
            nameof(RestoringSleepingStartsDirectlyInLoop),
            RestoringSleepingStartsDirectlyInLoop);
        Run(
            nameof(SleepExitCompletesToNormalAndRejectsStaleCallbacks),
            SleepExitCompletesToNormalAndRejectsStaleCallbacks);
        Run(
            nameof(DraggingUsesAnExclusiveTokenizedTransaction),
            DraggingUsesAnExclusiveTokenizedTransaction);
    }

    private static void SleepEntryIsNotStableUntilEnterCompletes()
    {
        var machine = new HierarchicalPetStateMachine();

        PetStateTransitionToken token = machine.PrepareSleepEntry();
        BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);
        BehaviorTestCheck.Equal(PetLifecyclePhase.Idle, machine.Phase);
        BehaviorTestCheck.Equal(token, machine.PreparedSleepEntryToken!.Value);

        BehaviorTestCheck.True(machine.CommitSleepEntryFirstFrame(token));
        BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.SleepingEntering,
            machine.Phase);

        BehaviorTestCheck.True(machine.CompleteSleepEntry(token));
        BehaviorTestCheck.Equal(StablePetState.Sleeping, machine.StableState);
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.SleepingLooping,
            machine.Phase);
        BehaviorTestCheck.Null(machine.ActiveTransitionToken);
    }

    private static void SleepEntryInterruptionRevokesPreparedAndVisibleEntry()
    {
        var machine = new HierarchicalPetStateMachine();

        PetStateTransitionToken prepared = machine.PrepareSleepEntry();
        BehaviorTestCheck.True(machine.CancelSleepEntry(prepared));
        AssertNormalIdle(machine);
        BehaviorTestCheck.False(machine.CommitSleepEntryFirstFrame(prepared));

        PetStateTransitionToken visible = machine.PrepareSleepEntry();
        BehaviorTestCheck.True(machine.CommitSleepEntryFirstFrame(visible));
        BehaviorTestCheck.True(machine.CancelSleepEntry(visible));
        AssertNormalIdle(machine);
        BehaviorTestCheck.False(machine.CompleteSleepEntry(visible));
    }

    private static void RestoringSleepingStartsDirectlyInLoop()
    {
        var machine =
            new HierarchicalPetStateMachine(StablePetState.Sleeping);

        BehaviorTestCheck.Equal(StablePetState.Sleeping, machine.StableState);
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.SleepingLooping,
            machine.Phase);
        BehaviorTestCheck.Null(machine.PreparedSleepEntryToken);
        BehaviorTestCheck.Null(machine.ActiveTransitionToken);
    }

    private static void SleepExitCompletesToNormalAndRejectsStaleCallbacks()
    {
        var machine =
            new HierarchicalPetStateMachine(StablePetState.Sleeping);

        PetStateTransitionToken cancelled = machine.BeginSleepExit();
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.SleepingExiting,
            machine.Phase);
        BehaviorTestCheck.True(machine.CancelSleepExit(cancelled));
        BehaviorTestCheck.Equal(StablePetState.Sleeping, machine.StableState);
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.SleepingLooping,
            machine.Phase);
        BehaviorTestCheck.False(machine.CompleteSleepExit(cancelled));

        PetStateTransitionToken completed = machine.BeginSleepExit();
        BehaviorTestCheck.True(machine.CompleteSleepExit(completed));
        AssertNormalIdle(machine);
        BehaviorTestCheck.False(machine.CompleteSleepExit(completed));
    }

    private static void DraggingUsesAnExclusiveTokenizedTransaction()
    {
        var machine =
            new HierarchicalPetStateMachine(StablePetState.Sleeping);

        PetStateTransitionToken token = machine.BeginDragging();
        BehaviorTestCheck.Equal(StablePetState.Dragging, machine.StableState);
        BehaviorTestCheck.Equal(
            PetLifecyclePhase.DraggingFollowing,
            machine.Phase);
        BehaviorTestCheck.Throws<InvalidOperationException>(
            () => machine.BeginDragging());

        BehaviorTestCheck.True(machine.CompleteDragging(token));
        AssertNormalIdle(machine);
        BehaviorTestCheck.False(machine.CompleteDragging(token));
    }

    private static void AssertNormalIdle(
        HierarchicalPetStateMachine machine)
    {
        BehaviorTestCheck.Equal(StablePetState.Normal, machine.StableState);
        BehaviorTestCheck.Equal(PetLifecyclePhase.Idle, machine.Phase);
        BehaviorTestCheck.Null(machine.PreparedSleepEntryToken);
        BehaviorTestCheck.Null(machine.ActiveTransitionToken);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(HierarchicalPetStateMachineTests)}.{name}");
    }
}
