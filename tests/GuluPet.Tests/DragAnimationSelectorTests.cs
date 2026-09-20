using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class DragAnimationSelectorTests
{
    public static void RunAll()
    {
        Run(nameof(RequiresStableVelocityBeforeSwitching), RequiresStableVelocityBeforeSwitching);
        Run(nameof(HysteresisPreventsBoundaryTwitch), HysteresisPreventsBoundaryTwitch);
        Run(nameof(MapsCanonicalClipIds), MapsCanonicalClipIds);
    }

    private static void RequiresStableVelocityBeforeSwitching()
    {
        var selector = new DragAnimationSelector();
        selector.Begin();
        BehaviorTestCheck.False(
            selector.Observe(650, TimeSpan.FromMilliseconds(60)));
        BehaviorTestCheck.True(
            selector.Observe(650, TimeSpan.FromMilliseconds(60)));
        BehaviorTestCheck.Equal(DragHoldPose.Tense, selector.Current);
    }

    private static void HysteresisPreventsBoundaryTwitch()
    {
        var selector = new DragAnimationSelector();
        selector.Begin();
        _ = selector.Observe(650, TimeSpan.FromMilliseconds(120));
        BehaviorTestCheck.Equal(DragHoldPose.Tense, selector.Current);

        for (var index = 0; index < 10; index++)
        {
            BehaviorTestCheck.False(
                selector.Observe(
                    index % 2 == 0 ? 490 : 510,
                    TimeSpan.FromMilliseconds(40)));
        }

        BehaviorTestCheck.Equal(DragHoldPose.Tense, selector.Current);
        BehaviorTestCheck.True(
            selector.Observe(250, TimeSpan.FromMilliseconds(120)));
        BehaviorTestCheck.Equal(DragHoldPose.Relaxed, selector.Current);
    }

    private static void MapsCanonicalClipIds()
    {
        BehaviorTestCheck.Equal(
            "drag_hold_relaxed",
            DragAnimationSelector.ClipId(DragHoldPose.Relaxed));
        BehaviorTestCheck.Equal(
            "drag_hold_tense",
            DragAnimationSelector.ClipId(DragHoldPose.Tense));
        BehaviorTestCheck.Equal(
            "drag_hold_stretched",
            DragAnimationSelector.ClipId(DragHoldPose.Stretched));
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(DragAnimationSelectorTests)}.{name}");
    }
}
