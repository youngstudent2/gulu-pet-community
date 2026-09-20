using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class BubbleMessageLayoutTests
{
    public static void RunAll()
    {
        Run(
            nameof(ShortMediumAndLongMessagesUseDifferentWidths),
            ShortMediumAndLongMessagesUseDifferentWidths);
        Run(
            nameof(MessageWidthIsRoundedAndClamped),
            MessageWidthIsRoundedAndClamped);
        Run(
            nameof(InvalidMeasuredWidthsFailClosed),
            InvalidMeasuredWidthsFailClosed);
    }

    private static void ShortMediumAndLongMessagesUseDifferentWidths()
    {
        double shortWidth = BubbleWindow.MeasureMessageTextWidth("喵~");
        double mediumWidth = BubbleWindow.MeasureMessageTextWidth(
            "喵~嗷~喵~~(≧ω≦)/");
        double longWidth = BubbleWindow.MeasureMessageTextWidth(
            string.Concat(Enumerable.Repeat(
                "喵！！！喵、嗷...呜呜~呼噜呼噜~(T ^ T) ",
                8)));

        BehaviorTestCheck.Equal(
            BubbleWindow.MinimumMessageTextWidth,
            shortWidth);
        BehaviorTestCheck.True(shortWidth < mediumWidth);
        BehaviorTestCheck.True(mediumWidth < longWidth);
        BehaviorTestCheck.Equal(
            BubbleWindow.MaximumMessageTextWidth,
            longWidth);
    }

    private static void MessageWidthIsRoundedAndClamped()
    {
        BehaviorTestCheck.Equal(
            147d,
            BubbleWindow.ClampMessageTextWidth(146.1));
        BehaviorTestCheck.Equal(
            BubbleWindow.MinimumMessageTextWidth,
            BubbleWindow.ClampMessageTextWidth(0));
        BehaviorTestCheck.Equal(
            BubbleWindow.MaximumMessageTextWidth,
            BubbleWindow.ClampMessageTextWidth(
                BubbleWindow.MaximumMessageTextWidth + 1));
    }

    private static void InvalidMeasuredWidthsFailClosed()
    {
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => BubbleWindow.ClampMessageTextWidth(double.NaN));
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => BubbleWindow.ClampMessageTextWidth(-1));
        _ = BehaviorTestCheck.Throws<ArgumentException>(
            () => BubbleWindow.MeasureMessageTextWidth(" "));
        _ = BehaviorTestCheck.Throws<ArgumentOutOfRangeException>(
            () => BubbleWindow.MeasureMessageTextWidth("喵~", 0));
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(BubbleMessageLayoutTests)}.{name}");
    }
}
