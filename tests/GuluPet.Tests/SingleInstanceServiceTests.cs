using GuluPet.Platform;

namespace GuluPet.Tests;

internal static class SingleInstanceServiceTests
{
    public static void RunAll()
    {
        Run(
            nameof(CustomIsolationKeysOwnIndependentNamespaces),
            CustomIsolationKeysOwnIndependentNamespaces);
        Run(
            nameof(IsolationKeyRequiresIsolatedTestMode),
            IsolationKeyRequiresIsolatedTestMode);
    }

    private static void CustomIsolationKeysOwnIndependentNamespaces()
    {
        string prefix = $"GuluPet.Tests.{Guid.NewGuid():N}";
        using var first = new SingleInstanceService(
            isolatedTestInstance: true,
            isolatedInstanceKey: $"{prefix}.first");
        using var duplicate = new SingleInstanceService(
            isolatedTestInstance: true,
            isolatedInstanceKey: $"{prefix}.first");
        using var independent = new SingleInstanceService(
            isolatedTestInstance: true,
            isolatedInstanceKey: $"{prefix}.second");

        BehaviorTestCheck.True(first.TryAcquire(static () => { }));
        BehaviorTestCheck.False(duplicate.TryAcquire(static () => { }));
        BehaviorTestCheck.True(independent.TryAcquire(static () => { }));
    }

    private static void IsolationKeyRequiresIsolatedTestMode()
    {
        BehaviorTestCheck.Throws<ArgumentException>(() =>
            _ = new SingleInstanceService(
                isolatedTestInstance: false,
                isolatedInstanceKey: "GuluPet.Tests.Invalid"));
        BehaviorTestCheck.Throws<ArgumentException>(() =>
            _ = new SingleInstanceService(
                isolatedTestInstance: true,
                isolatedInstanceKey: ""));
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(SingleInstanceServiceTests)}.{name}");
    }
}
