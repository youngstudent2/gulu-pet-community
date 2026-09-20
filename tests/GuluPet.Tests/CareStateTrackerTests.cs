using System.IO;
using GuluPet.Care;

namespace GuluPet.Tests;

internal static class CareStateTrackerTests
{
    public static void RunAll()
    {
        Run(
            nameof(DefaultsStartFullAndDoNotDecayWithoutAdvance),
            DefaultsStartFullAndDoNotDecayWithoutAdvance);
        Run(
            nameof(ExplicitOnlineTimeUsesEightAndSixHourDurations),
            ExplicitOnlineTimeUsesEightAndSixHourDurations);
        Run(
            nameof(SequentialAdvanceClampsLevelsAtZero),
            SequentialAdvanceClampsLevelsAtZero);
        Run(
            nameof(RefillsOnlyTheRequestedLevel),
            RefillsOnlyTheRequestedLevel);
        Run(
            nameof(InvalidStateAndElapsedTimeAreRejected),
            InvalidStateAndElapsedTimeAreRejected);
    }

    private static void DefaultsStartFullAndDoNotDecayWithoutAdvance()
    {
        var tracker = new CareStateTracker();

        CareState first = tracker.Current;
        CareState second = tracker.Current;
        var restarted = new CareStateTracker(first);

        BehaviorTestCheck.Equal(
            CareState.CurrentSchemaVersion,
            first.SchemaVersion);
        BehaviorTestCheck.Close(CareState.FullLevel, first.FoodLevel);
        BehaviorTestCheck.Close(CareState.FullLevel, first.WaterLevel);
        BehaviorTestCheck.Close(first.FoodLevel, second.FoodLevel);
        BehaviorTestCheck.Close(first.WaterLevel, second.WaterLevel);
        BehaviorTestCheck.Close(
            first.FoodLevel,
            restarted.Current.FoodLevel);
        BehaviorTestCheck.Close(
            first.WaterLevel,
            restarted.Current.WaterLevel);
    }

    private static void ExplicitOnlineTimeUsesEightAndSixHourDurations()
    {
        var tracker = new CareStateTracker();

        CareState afterThreeHours = tracker.AdvanceOnline(
            TimeSpan.FromHours(3));

        BehaviorTestCheck.Equal(
            TimeSpan.FromHours(8),
            CareStateTracker.FoodDepletionDuration);
        BehaviorTestCheck.Equal(
            TimeSpan.FromHours(6),
            CareStateTracker.WaterDepletionDuration);
        BehaviorTestCheck.Close(62.5, afterThreeHours.FoodLevel);
        BehaviorTestCheck.Close(50, afterThreeHours.WaterLevel);

        CareState afterSixHours = tracker.AdvanceOnline(
            TimeSpan.FromHours(3));
        BehaviorTestCheck.Close(25, afterSixHours.FoodLevel);
        BehaviorTestCheck.Close(0, afterSixHours.WaterLevel);
    }

    private static void SequentialAdvanceClampsLevelsAtZero()
    {
        var tracker = new CareStateTracker(CareState.Create(12.5, 25));

        CareState unchanged = tracker.AdvanceOnline(TimeSpan.Zero);
        BehaviorTestCheck.Close(12.5, unchanged.FoodLevel);
        BehaviorTestCheck.Close(25, unchanged.WaterLevel);

        CareState depleted = tracker.AdvanceOnline(TimeSpan.FromHours(24));
        BehaviorTestCheck.Close(0, depleted.FoodLevel);
        BehaviorTestCheck.Close(0, depleted.WaterLevel);

        CareState stillEmpty = tracker.AdvanceOnline(TimeSpan.MaxValue);
        BehaviorTestCheck.Close(0, stillEmpty.FoodLevel);
        BehaviorTestCheck.Close(0, stillEmpty.WaterLevel);
    }

    private static void RefillsOnlyTheRequestedLevel()
    {
        var tracker = new CareStateTracker(CareState.Create(10, 20));

        CareState foodRefilled = tracker.RefillFood();
        BehaviorTestCheck.Close(
            CareState.FullLevel,
            foodRefilled.FoodLevel);
        BehaviorTestCheck.Close(20, foodRefilled.WaterLevel);

        CareState afterOneHour = tracker.AdvanceOnline(
            TimeSpan.FromHours(1));
        BehaviorTestCheck.Close(87.5, afterOneHour.FoodLevel);
        BehaviorTestCheck.Close(20d - (100d / 6d), afterOneHour.WaterLevel);

        CareState waterRefilled = tracker.RefillWater();
        BehaviorTestCheck.Close(87.5, waterRefilled.FoodLevel);
        BehaviorTestCheck.Close(
            CareState.FullLevel,
            waterRefilled.WaterLevel);
    }

    private static void InvalidStateAndElapsedTimeAreRejected()
    {
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => new CareStateTracker().AdvanceOnline(
                TimeSpan.FromTicks(-1)));
        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => CareState.Create(double.NaN, 50));
        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => CareState.Create(50, double.PositiveInfinity));
        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => CareState.Create(-0.001, 50));
        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => CareState.Create(50, 100.001));
        _ = BehaviorTestCheck.Throws<InvalidDataException>(
            () => new CareStateTracker(
                new CareState
                {
                    SchemaVersion = CareState.CurrentSchemaVersion + 1,
                }));
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(CareStateTrackerTests)}.{name}");
    }
}
