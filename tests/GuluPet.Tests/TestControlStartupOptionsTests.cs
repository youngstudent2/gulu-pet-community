using GuluPet.Diagnostics;
using GuluPet.Testing;

namespace GuluPet.Tests;

internal static class TestControlStartupOptionsTests
{
    public static void RunAll()
    {
        Run(nameof(UsesProtocolPipeByDefault), UsesProtocolPipeByDefault);
        Run(nameof(AcceptsSafeExplicitPipe), AcceptsSafeExplicitPipe);
        Run(
            nameof(IgnoresCustomPipeOutsideExplicitTestMode),
            IgnoresCustomPipeOutsideExplicitTestMode);
        Run(nameof(RejectsInvalidCustomPipes), RejectsInvalidCustomPipes);
    }

    private static void UsesProtocolPipeByDefault()
    {
        BehaviorTestCheck.Equal(
            TestControlProtocol.PipeName,
            TestControlStartupOptions.ResolvePipeName(
                ["--test-control", "--start-hidden"],
                commandLineTestControlEnabled: true));
    }

    private static void AcceptsSafeExplicitPipe()
    {
        const string expected = "GuluPet.TestControl.Smoke.1234.abc_def-01";

        BehaviorTestCheck.Equal(
            expected,
            TestControlStartupOptions.ResolvePipeName(
                [
                    "--test-control",
                    "--TEST-CONTROL-PIPE",
                    expected,
                    "--isolated-test-instance"
                ],
                commandLineTestControlEnabled: true));
    }

    private static void IgnoresCustomPipeOutsideExplicitTestMode()
    {
        BehaviorTestCheck.Equal(
            TestControlProtocol.PipeName,
            TestControlStartupOptions.ResolvePipeName(
                ["--test-control-pipe", "GuluPet.Unused"],
                commandLineTestControlEnabled: false));
    }

    private static void RejectsInvalidCustomPipes()
    {
        BehaviorTestCheck.Throws<ArgumentException>(() =>
            TestControlStartupOptions.ResolvePipeName(
                ["--test-control", "--test-control-pipe"],
                commandLineTestControlEnabled: true));
        BehaviorTestCheck.Throws<ArgumentException>(() =>
            TestControlStartupOptions.ResolvePipeName(
                ["--test-control", "--test-control-pipe", "--start-hidden"],
                commandLineTestControlEnabled: true));
        BehaviorTestCheck.Throws<ArgumentException>(() =>
            TestControlStartupOptions.ResolvePipeName(
                ["--test-control", "--test-control-pipe", @"unsafe\pipe"],
                commandLineTestControlEnabled: true));
        BehaviorTestCheck.Throws<ArgumentException>(() =>
            TestControlStartupOptions.ResolvePipeName(
                ["--test-control", "--test-control-pipe", "anonymous"],
                commandLineTestControlEnabled: true));
        BehaviorTestCheck.Throws<ArgumentException>(() =>
            TestControlStartupOptions.ResolvePipeName(
                [
                    "--test-control",
                    "--test-control-pipe",
                    "GuluPet.One",
                    "--test-control-pipe",
                    "GuluPet.Two"
                ],
                commandLineTestControlEnabled: true));
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(TestControlStartupOptionsTests)}.{name}");
    }
}
