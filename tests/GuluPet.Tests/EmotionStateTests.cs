using GuluPet.Domain;

namespace GuluPet.Tests;

internal static class EmotionStateTests
{
    public static void RunAll()
    {
        Run(
            nameof(ShortTermEmotionsRegressTowardDefaultsWithoutAffection),
            ShortTermEmotionsRegressTowardDefaultsWithoutAffection);
        Run(
            nameof(RegressionIsIndependentOfObservationFrequency),
            RegressionIsIndependentOfObservationFrequency);
        Run(
            nameof(EmotionDeltasClampAtValidBounds),
            EmotionDeltasClampAtValidBounds);
        Run(
            nameof(RegressionRejectsInvalidElapsedTimeAndState),
            RegressionRejectsInvalidElapsedTimeAndState);
    }

    private static void ShortTermEmotionsRegressTowardDefaultsWithoutAffection()
    {
        var emotions = ExtremeEmotions();

        emotions.RegressShortTerm(EmotionState.ShortTermRegressionHalfLife);

        BehaviorTestCheck.Close(82.5, emotions.Energy);
        BehaviorTestCheck.Close(10, emotions.Sleepiness);
        BehaviorTestCheck.Close(57.5, emotions.Boredom);
        BehaviorTestCheck.Close(25, emotions.Curiosity);
        BehaviorTestCheck.Close(80, emotions.Happiness);
        BehaviorTestCheck.Close(55, emotions.Stress);
        BehaviorTestCheck.Close(88, emotions.Affection);
    }

    private static void RegressionIsIndependentOfObservationFrequency()
    {
        EmotionState once = ExtremeEmotions();
        EmotionState partitioned = ExtremeEmotions();
        TimeSpan halfLife = EmotionState.ShortTermRegressionHalfLife;

        once.RegressShortTerm(halfLife);
        partitioned.RegressShortTerm(halfLife / 2);
        partitioned.RegressShortTerm(halfLife / 2);

        BehaviorTestCheck.Close(once.Energy, partitioned.Energy);
        BehaviorTestCheck.Close(once.Sleepiness, partitioned.Sleepiness);
        BehaviorTestCheck.Close(once.Boredom, partitioned.Boredom);
        BehaviorTestCheck.Close(once.Curiosity, partitioned.Curiosity);
        BehaviorTestCheck.Close(once.Happiness, partitioned.Happiness);
        BehaviorTestCheck.Close(once.Stress, partitioned.Stress);
        BehaviorTestCheck.Close(once.Affection, partitioned.Affection);
    }

    private static void EmotionDeltasClampAtValidBounds()
    {
        var emotions = new EmotionState();
        emotions.Apply(
            new EmotionDelta(
                Energy: 1_000,
                Sleepiness: -1_000,
                Boredom: 1_000,
                Curiosity: -1_000,
                Happiness: 1_000,
                Stress: -1_000,
                Affection: 1_000));

        BehaviorTestCheck.Close(100, emotions.Energy);
        BehaviorTestCheck.Close(0, emotions.Sleepiness);
        BehaviorTestCheck.Close(100, emotions.Boredom);
        BehaviorTestCheck.Close(0, emotions.Curiosity);
        BehaviorTestCheck.Close(100, emotions.Happiness);
        BehaviorTestCheck.Close(0, emotions.Stress);
        BehaviorTestCheck.Close(100, emotions.Affection);

        emotions.Apply(
            new EmotionDelta(
                Energy: -1_000,
                Sleepiness: 1_000,
                Boredom: -1_000,
                Curiosity: 1_000,
                Happiness: -1_000,
                Stress: 1_000,
                Affection: -1_000));

        BehaviorTestCheck.Close(0, emotions.Energy);
        BehaviorTestCheck.Close(100, emotions.Sleepiness);
        BehaviorTestCheck.Close(0, emotions.Boredom);
        BehaviorTestCheck.Close(100, emotions.Curiosity);
        BehaviorTestCheck.Close(0, emotions.Happiness);
        BehaviorTestCheck.Close(100, emotions.Stress);
        BehaviorTestCheck.Close(0, emotions.Affection);
    }

    private static void RegressionRejectsInvalidElapsedTimeAndState()
    {
        var emotions = new EmotionState();
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => emotions.RegressShortTerm(TimeSpan.FromTicks(-1)));

        emotions.Energy = double.NaN;
        _ = BehaviorTestCheck.Throws<InvalidOperationException>(
            () => emotions.RegressShortTerm(TimeSpan.FromSeconds(1)));
    }

    private static EmotionState ExtremeEmotions() =>
        new()
        {
            Energy = 100,
            Sleepiness = 0,
            Boredom = 100,
            Curiosity = 0,
            Happiness = 100,
            Stress = 100,
            Affection = 88,
        };

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(EmotionStateTests)}.{name}");
    }
}
