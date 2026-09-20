using GuluPet.Behavior;

namespace GuluPet.Tests;

internal static class ClickBurstClassifierTests
{
    public static void RunAll()
    {
        Run(
            nameof(SingleClickExpiresOnlyAfterInclusiveWindow),
            SingleClickExpiresOnlyAfterInclusiveWindow);
        Run(
            nameof(ThresholdClickResolvesRepeatedImmediately),
            ThresholdClickResolvesRepeatedImmediately);
        Run(
            nameof(CancelDiscardsPendingBurst),
            CancelDiscardsPendingBurst);
        Run(
            nameof(ExpiredBurstMustBeConsumedBeforeNewInput),
            ExpiredBurstMustBeConsumedBeforeNewInput);
    }

    private static void SingleClickExpiresOnlyAfterInclusiveWindow()
    {
        var clock = new ManualDualClock();
        var classifier = Create(clock);

        BehaviorTestCheck.Equal(
            ClickBurstObservation.Pending,
            classifier.ObserveClick());
        BehaviorTestCheck.True(classifier.HasPending);

        clock.Advance(TimeSpan.FromMilliseconds(700));
        BehaviorTestCheck.False(classifier.TryExpireSingle());
        BehaviorTestCheck.True(classifier.HasPending);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        BehaviorTestCheck.True(classifier.TryExpireSingle());
        BehaviorTestCheck.False(classifier.HasPending);
        BehaviorTestCheck.False(classifier.TryExpireSingle());
    }

    private static void ThresholdClickResolvesRepeatedImmediately()
    {
        var clock = new ManualDualClock();
        var classifier = Create(clock);

        BehaviorTestCheck.Equal(
            ClickBurstObservation.Pending,
            classifier.ObserveClick());
        clock.Advance(TimeSpan.FromMilliseconds(200));
        BehaviorTestCheck.Equal(
            ClickBurstObservation.Pending,
            classifier.ObserveClick());
        clock.Advance(TimeSpan.FromMilliseconds(500));
        BehaviorTestCheck.Equal(
            ClickBurstObservation.Repeated,
            classifier.ObserveClick());

        BehaviorTestCheck.False(classifier.HasPending);
        BehaviorTestCheck.False(classifier.TryExpireSingle());
    }

    private static void CancelDiscardsPendingBurst()
    {
        var classifier = Create(new ManualDualClock());

        _ = classifier.ObserveClick();
        BehaviorTestCheck.True(classifier.Cancel());
        BehaviorTestCheck.False(classifier.HasPending);
        BehaviorTestCheck.False(classifier.Cancel());
    }

    private static void ExpiredBurstMustBeConsumedBeforeNewInput()
    {
        var clock = new ManualDualClock();
        var classifier = Create(clock);
        _ = classifier.ObserveClick();
        clock.Advance(TimeSpan.FromMilliseconds(701));

        _ = BehaviorTestCheck.Throws<InvalidOperationException>(
            () => classifier.ObserveClick());
        BehaviorTestCheck.True(classifier.TryExpireSingle());
        BehaviorTestCheck.Equal(
            ClickBurstObservation.Pending,
            classifier.ObserveClick());
    }

    private static ClickBurstClassifier Create(ManualDualClock clock) =>
        new(
            clock,
            repeatedClickThreshold: 3,
            classificationWindow: TimeSpan.FromMilliseconds(700));

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(ClickBurstClassifierTests)}.{name}");
    }
}
