using System.Windows;
using System.Windows.Media.Animation;

namespace GuluPet.Presentation;

/// <summary>
/// Shared native-WPF motion vocabulary for short, interruptible UI feedback.
/// </summary>
internal static class GuluMotion
{
    internal static readonly TimeSpan QuickExit =
        TimeSpan.FromMilliseconds(110);

    internal static readonly TimeSpan QuickEntrance =
        TimeSpan.FromMilliseconds(150);

    internal static readonly TimeSpan FadeEntrance =
        TimeSpan.FromMilliseconds(160);

    internal static readonly TimeSpan MoveEntrance =
        TimeSpan.FromMilliseconds(180);

    internal static readonly TimeSpan EmphasizedEntrance =
        TimeSpan.FromMilliseconds(220);

    internal static bool ShouldAnimate(
        bool clientAreaAnimationEnabled,
        bool highContrastEnabled) =>
        clientAreaAnimationEnabled && !highContrastEnabled;

    internal static KeySpline CreateEaseOutSpline() =>
        new(0.23, 1, 0.32, 1);

    internal static KeySpline CreateEmphasizedSpline() =>
        new(0.77, 0, 0.175, 1);

    internal static DoubleAnimationUsingKeyFrames SplineTo(
        double value,
        TimeSpan duration) =>
        SplineTo(value, duration, CreateEaseOutSpline());

    internal static DoubleAnimationUsingKeyFrames SplineTo(
        double value,
        TimeSpan duration,
        KeySpline spline)
    {
        ArgumentNullException.ThrowIfNull(spline);
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Motion values must be finite.");
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Motion duration must be positive.");
        }

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(duration),
            FillBehavior = FillBehavior.Stop,
        };
        animation.KeyFrames.Add(
            new SplineDoubleKeyFrame(
                value,
                KeyTime.FromTimeSpan(duration),
                spline));
        return animation;
    }

    internal static double ResolveHorizontalApproachOffset(
        double petLeft,
        double petRight,
        double popupLeft,
        double popupRight)
    {
        ValidateBounds(petLeft, petRight, nameof(petLeft));
        ValidateBounds(popupLeft, popupRight, nameof(popupLeft));
        double petCenter = petLeft + (petRight - petLeft) / 2;
        double popupCenter = popupLeft + (popupRight - popupLeft) / 2;
        return popupCenter < petCenter ? 6 : -6;
    }

    private static void ValidateBounds(
        double left,
        double right,
        string parameterName)
    {
        if (!double.IsFinite(left)
            || !double.IsFinite(right)
            || right < left)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Motion bounds must be finite and ordered.");
        }
    }
}
