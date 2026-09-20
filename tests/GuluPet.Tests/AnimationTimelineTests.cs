using GuluPet.Animation;

namespace GuluPet.Tests;

internal static class AnimationTimelineTests
{
    public static void RunAll()
    {
        Run(
            nameof(TwentyFourFpsMapsWallClockToExpectedSteps),
            TwentyFourFpsMapsWallClockToExpectedSteps);
        Run(
            nameof(FullClipBoundaryMatchesSourceDuration),
            FullClipBoundaryMatchesSourceDuration);
        Run(nameof(ResetStartsANewTimeline), ResetStartsANewTimeline);
        Run(nameof(NegativeAndRepeatedSamplesDoNotProduceSteps), NegativeAndRepeatedSamplesDoNotProduceSteps);
    }

    private static void TwentyFourFpsMapsWallClockToExpectedSteps()
    {
        var timeline = new AnimationTimeline();

        timeline.Reset(24);
        Equal(0L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(41.6)));
        Equal(1L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(41.7)));

        timeline.Reset(24);
        Equal(2L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(100)));

        timeline.Reset(24);
        Equal(12L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(500)));
    }

    private static void FullClipBoundaryMatchesSourceDuration()
    {
        var timeline = new AnimationTimeline();
        timeline.Reset(24);

        Equal(120L, timeline.ConsumeDueSteps(TimeSpan.FromSeconds(5)));
        Equal(
            1L,
            timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(5_041.7)));
    }

    private static void ResetStartsANewTimeline()
    {
        var timeline = new AnimationTimeline();
        timeline.Reset(24);

        Equal(2L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(100)));
        Equal(0L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(100)));

        timeline.Reset(24);
        Equal(2L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(100)));

        timeline.Reset(60);
        Equal(6L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(100)));
    }

    private static void NegativeAndRepeatedSamplesDoNotProduceSteps()
    {
        var timeline = new AnimationTimeline();
        timeline.Reset(24);

        Equal(0L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(-10)));
        Equal(2L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(100)));
        Equal(0L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(100)));
        Equal(0L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(50)));
        Equal(1L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(125)));
        Equal(0L, timeline.ConsumeDueSteps(TimeSpan.FromMilliseconds(125)));
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }

    private static void Equal(long expected, long actual)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException(
                $"Expected '{expected}', got '{actual}'.");
        }
    }
}
