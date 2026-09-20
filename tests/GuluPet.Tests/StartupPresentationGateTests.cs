namespace GuluPet.Tests;

internal static class StartupPresentationGateTests
{
    public static void RunAll()
    {
        Run(
            nameof(PendingActivationIsConsumedExactlyOnceAfterSuccess),
            PendingActivationIsConsumedExactlyOnceAfterSuccess);
        Run(
            nameof(FailedInitializationClearsPendingActivation),
            FailedInitializationClearsPendingActivation);
    }

    private static void PendingActivationIsConsumedExactlyOnceAfterSuccess()
    {
        var gate = new StartupPresentationGate();

        BehaviorTestCheck.False(gate.RequestActivation());
        BehaviorTestCheck.False(gate.IsRuntimeReady);
        BehaviorTestCheck.True(gate.MarkRuntimeReady());
        BehaviorTestCheck.True(gate.IsRuntimeReady);
        BehaviorTestCheck.False(gate.MarkRuntimeReady());
        BehaviorTestCheck.True(gate.RequestActivation());
    }

    private static void FailedInitializationClearsPendingActivation()
    {
        var gate = new StartupPresentationGate();

        BehaviorTestCheck.False(gate.RequestActivation());
        gate.MarkInitializationFailed();

        BehaviorTestCheck.False(gate.IsRuntimeReady);
        BehaviorTestCheck.False(gate.MarkRuntimeReady());
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(StartupPresentationGateTests)}.{name}");
    }
}
