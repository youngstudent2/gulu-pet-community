using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class InteractionPopupMotionTests
{
    public static void RunAll()
    {
        Run(
            nameof(MotionVocabularyUsesExactDurationsAndSplines),
            MotionVocabularyUsesExactDurationsAndSplines);
        Run(
            nameof(ApproachDirectionFollowsActualLandingSide),
            ApproachDirectionFollowsActualLandingSide);
        Run(
            nameof(CustomPlacementKeepsSymmetricPetOverlap),
            CustomPlacementKeepsSymmetricPetOverlap);
        Run(
            nameof(ShadowMarginDoesNotInterceptInput),
            ShadowMarginDoesNotInterceptInput);
        Run(
            nameof(ButtonsAreInteractiveBeforeEntranceCompletes),
            ButtonsAreInteractiveBeforeEntranceCompletes);
        Run(
            nameof(NormalDismissCommitsFinalBaseValues),
            NormalDismissCommitsFinalBaseValues);
        Run(
            nameof(ReopenRejectsStaleDismissCompletion),
            ReopenRejectsStaleDismissCompletion);
        Run(
            nameof(EmergencyCloseIsImmediateAndClearsClocks),
            EmergencyCloseIsImmediateAndClearsClocks);
    }

    private static void MotionVocabularyUsesExactDurationsAndSplines()
    {
        BehaviorTestCheck.Equal(
            TimeSpan.FromMilliseconds(110),
            GuluMotion.QuickExit);
        BehaviorTestCheck.Equal(
            TimeSpan.FromMilliseconds(150),
            GuluMotion.QuickEntrance);
        BehaviorTestCheck.Equal(
            TimeSpan.FromMilliseconds(160),
            GuluMotion.FadeEntrance);
        BehaviorTestCheck.Equal(
            TimeSpan.FromMilliseconds(180),
            GuluMotion.MoveEntrance);
        BehaviorTestCheck.Equal(
            TimeSpan.FromMilliseconds(220),
            GuluMotion.EmphasizedEntrance);
        BehaviorTestCheck.True(GuluMotion.ShouldAnimate(
            clientAreaAnimationEnabled: true,
            highContrastEnabled: false));
        BehaviorTestCheck.False(GuluMotion.ShouldAnimate(
            clientAreaAnimationEnabled: false,
            highContrastEnabled: false));
        BehaviorTestCheck.False(GuluMotion.ShouldAnimate(
            clientAreaAnimationEnabled: true,
            highContrastEnabled: true));

        KeySpline easeOut = GuluMotion.CreateEaseOutSpline();
        AssertPoint(0.23, 1, easeOut.ControlPoint1);
        AssertPoint(0.32, 1, easeOut.ControlPoint2);
        KeySpline emphasized = GuluMotion.CreateEmphasizedSpline();
        AssertPoint(0.77, 0, emphasized.ControlPoint1);
        AssertPoint(0.175, 1, emphasized.ControlPoint2);

        DoubleAnimationUsingKeyFrames animation = GuluMotion.SplineTo(
            7,
            GuluMotion.QuickEntrance);
        BehaviorTestCheck.Equal(FillBehavior.Stop, animation.FillBehavior);
        BehaviorTestCheck.Equal(
            GuluMotion.QuickEntrance,
            animation.Duration.TimeSpan);
        BehaviorTestCheck.Equal(1, animation.KeyFrames.Count);
        var frame = (SplineDoubleKeyFrame)animation.KeyFrames[0];
        BehaviorTestCheck.Close(7, frame.Value);
        BehaviorTestCheck.Equal(
            GuluMotion.QuickEntrance,
            frame.KeyTime.TimeSpan);
        AssertPoint(0.23, 1, frame.KeySpline.ControlPoint1);
        AssertPoint(0.32, 1, frame.KeySpline.ControlPoint2);
    }

    private static void ApproachDirectionFollowsActualLandingSide()
    {
        BehaviorTestCheck.Close(
            -6,
            GuluMotion.ResolveHorizontalApproachOffset(
                petLeft: 100,
                petRight: 300,
                popupLeft: 292,
                popupRight: 420));
        BehaviorTestCheck.Close(
            6,
            GuluMotion.ResolveHorizontalApproachOffset(
                petLeft: 100,
                petRight: 300,
                popupLeft: -28,
                popupRight: 100));
    }

    private static void CustomPlacementKeepsSymmetricPetOverlap()
    {
        const double popupWidth = 176;
        const double targetWidth = 280;
        const double shadowPadding = 24;
        const double overlap = 8;
        CustomPopupPlacement[] placements = PetWindow.PlaceInteractionPopup(
            new Size(popupWidth, 340),
            new Size(targetWidth, 260),
            new Point());

        BehaviorTestCheck.Equal(2, placements.Length);
        BehaviorTestCheck.Equal(
            PopupPrimaryAxis.Horizontal,
            placements[0].PrimaryAxis);
        BehaviorTestCheck.Equal(
            PopupPrimaryAxis.Horizontal,
            placements[1].PrimaryAxis);

        double rightSurfaceLeft =
            placements[0].Point.X + shadowPadding;
        double leftSurfaceRight = placements[1].Point.X
            + popupWidth
            - shadowPadding;
        BehaviorTestCheck.Close(
            targetWidth - overlap,
            rightSurfaceLeft);
        BehaviorTestCheck.Close(overlap, leftSurfaceRight);
        BehaviorTestCheck.Close(
            overlap,
            targetWidth - rightSurfaceLeft);
        BehaviorTestCheck.Close(
            targetWidth - rightSurfaceLeft,
            leftSurfaceRight);
        BehaviorTestCheck.Close(
            0,
            placements[0].Point.Y + shadowPadding);
        BehaviorTestCheck.Close(
            0,
            placements[1].Point.Y + shadowPadding);
    }

    private static void ShadowMarginDoesNotInterceptInput()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope();
                PetWindow window = scope.Window;
                window.OpenInteractionPopupForTest(revealImmediately: true);
                window.Dispatcher.Invoke(
                    static () => { },
                    DispatcherPriority.Render);

                BehaviorTestCheck.Null(
                    window.InteractionPopupHost.Background);
                BehaviorTestCheck.True(
                    window.InteractionPopupSurface.Background is not null);
                BehaviorTestCheck.True(
                    window.InteractionPopupHost.InputHitTest(
                        new Point(2, 2)) is null,
                    "The shadow-only margin must not intercept pointer input.");
                BehaviorTestCheck.True(
                    window.InteractionPopupSurface.InputHitTest(
                        new Point(
                            window.InteractionPopupSurface.ActualWidth / 2,
                            window.InteractionPopupSurface.ActualHeight / 2))
                        is not null,
                    "The visible popup surface must remain pointer-active.");
            });
    }

    private static void ButtonsAreInteractiveBeforeEntranceCompletes()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope();
                PetWindow window = scope.Window;
                window.OpenInteractionPopupForTest();

                BehaviorTestCheck.True(
                    window.IsInteractionPopupOpenForTest);
                BehaviorTestCheck.True(
                    window.FeedInteractionButton.IsHitTestVisible);
                BehaviorTestCheck.True(
                    window.WaterInteractionButton.IsHitTestVisible);
                BehaviorTestCheck.True(
                    window.TravelInteractionButton.IsHitTestVisible);
                BehaviorTestCheck.True(
                    window.PetInteractionButton.IsHitTestVisible);
                BehaviorTestCheck.True(
                    window.WardrobeInteractionButton.IsHitTestVisible);
                BehaviorTestCheck.Close(1, window.CareMeterPanel.Opacity);

                PumpDispatcherUntil(
                    () => !window.InteractionPopupHasAnimatedPropertiesForTest
                        && window.InteractionPopupOpacityForTest >= 0.999);
                BehaviorTestCheck.Close(
                    1,
                    GetOpacityBaseValue(window));
                BehaviorTestCheck.Close(
                    0,
                    GetTranslateBaseValue(window));
            });
    }

    private static void NormalDismissCommitsFinalBaseValues()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope();
                PetWindow window = scope.Window;
                window.OpenInteractionPopupForTest(revealImmediately: true);
                window.DismissInteractionPopupForTest();

                BehaviorTestCheck.True(
                    window.IsInteractionPopupOpenForTest,
                    "Ordinary dismissal must keep the popup open while it exits.");
                BehaviorTestCheck.True(
                    window.IsInteractionPopupDismissInProgressForTest);
                PumpDispatcherUntil(
                    () => !window.IsInteractionPopupOpenForTest);

                BehaviorTestCheck.False(
                    window.InteractionPopupHasAnimatedPropertiesForTest);
                BehaviorTestCheck.Close(0, GetOpacityBaseValue(window));
                BehaviorTestCheck.Close(
                    6,
                    Math.Abs(GetTranslateBaseValue(window)));
            });
    }

    private static void ReopenRejectsStaleDismissCompletion()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope();
                PetWindow window = scope.Window;
                window.OpenInteractionPopupForTest(revealImmediately: true);
                window.DismissInteractionPopupForTest();
                long dismissGeneration =
                    window.InteractionPopupMotionGenerationForTest;

                window.OpenInteractionPopupForTest();
                BehaviorTestCheck.True(
                    window.InteractionPopupMotionGenerationForTest
                        > dismissGeneration);
                BehaviorTestCheck.False(
                    window.IsInteractionPopupDismissInProgressForTest);
                PumpDispatcherUntil(
                    () => !window.InteractionPopupHasAnimatedPropertiesForTest
                        && window.InteractionPopupOpacityForTest >= 0.999);

                PumpDispatcherFor(TimeSpan.FromMilliseconds(140));
                BehaviorTestCheck.True(
                    window.IsInteractionPopupOpenForTest,
                    "A stale exit completion must not close a reopened popup.");
                BehaviorTestCheck.Close(1, GetOpacityBaseValue(window));
                BehaviorTestCheck.Close(0, GetTranslateBaseValue(window));
            });
    }

    private static void EmergencyCloseIsImmediateAndClearsClocks()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope();
                PetWindow window = scope.Window;
                window.OpenInteractionPopupForTest();
                long openGeneration =
                    window.InteractionPopupMotionGenerationForTest;
                window.CloseInteractionPopupImmediatelyForTest();

                BehaviorTestCheck.False(
                    window.IsInteractionPopupOpenForTest);
                BehaviorTestCheck.True(
                    window.InteractionPopupMotionGenerationForTest
                        > openGeneration);
                BehaviorTestCheck.False(
                    window.InteractionPopupHasAnimatedPropertiesForTest);
                BehaviorTestCheck.Close(0, GetOpacityBaseValue(window));
                BehaviorTestCheck.Close(0, GetTranslateBaseValue(window));

                PumpDispatcherFor(GuluMotion.QuickEntrance);
                BehaviorTestCheck.False(
                    window.IsInteractionPopupOpenForTest,
                    "A queued entrance must not revive an emergency-closed popup.");
            });
    }

    private static double GetOpacityBaseValue(PetWindow window) =>
        (double)window.InteractionPopupSurface.GetAnimationBaseValue(
            UIElement.OpacityProperty);

    private static double GetTranslateBaseValue(PetWindow window) =>
        (double)window.InteractionPopupTranslate.GetAnimationBaseValue(
            System.Windows.Media.TranslateTransform.XProperty);

    private static void AssertPoint(double x, double y, Point actual)
    {
        BehaviorTestCheck.Close(x, actual.X);
        BehaviorTestCheck.Close(y, actual.Y);
    }

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    test();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void PumpDispatcherUntil(
        Func<bool> condition,
        int timeoutMilliseconds = 2_000)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException(
                    "Interaction popup motion did not settle in time.");
            }

            PumpDispatcherOnce();
        }
    }

    private static void PumpDispatcherFor(TimeSpan duration)
    {
        long deadline = Environment.TickCount64
            + Math.Max(1, (long)Math.Ceiling(duration.TotalMilliseconds));
        while (Environment.TickCount64 < deadline)
        {
            PumpDispatcherOnce();
        }
    }

    private static void PumpDispatcherOnce()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(5),
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(InteractionPopupMotionTests)}.{name}");
    }

    private sealed class MotionWindowScope : IDisposable
    {
        internal MotionWindowScope()
        {
            Window = new PetWindow
            {
                Left = 220,
                Top = 180,
                ShowActivated = false,
            };
            Window.SetInteractionPopupAnimationsEnabledForTest(true);
            Window.Show();
            Window.UpdateLayout();
        }

        internal PetWindow Window { get; }

        public void Dispose()
        {
            Window.SetInteractionPopupAnimationsEnabledForTest(null);
            Window.Close();
        }
    }
}
