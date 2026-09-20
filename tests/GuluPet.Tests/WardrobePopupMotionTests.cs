using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GuluPet.Accessories;
using GuluPet.Platform;
using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class WardrobePopupMotionTests
{
    public static void RunAll()
    {
        Run(
            nameof(ExitContractUsesSharedSplineAndDirectionalOffsets),
            ExitContractUsesSharedSplineAndDirectionalOffsets);
        Run(
            nameof(ClickOpenThenStationaryKeepsWardrobeOpen),
            ClickOpenThenStationaryKeepsWardrobeOpen);
        Run(
            nameof(SyntheticLeaveDuringEntranceKeepsWardrobeOpen),
            SyntheticLeaveDuringEntranceKeepsWardrobeOpen);
        Run(
            nameof(PhysicalPointerLeaveDismissesDespiteRetainedFocus),
            PhysicalPointerLeaveDismissesDespiteRetainedFocus);
        Run(
            nameof(UnarmedFocusLossDuringEntranceDismissesWardrobe),
            UnarmedFocusLossDuringEntranceDismissesWardrobe);
        Run(
            nameof(DeactivationClosesUnarmedEntranceImmediately),
            DeactivationClosesUnarmedEntranceImmediately);
        Run(
            nameof(ToggleExitDisablesInputAndCommitsClosedBaseValues),
            ToggleExitDisablesInputAndCommitsClosedBaseValues);
        Run(
            nameof(ReopenRetargetsAndRejectsStaleDismissCompletion),
            ReopenRetargetsAndRejectsStaleDismissCompletion);
        Run(
            nameof(EscapeRouteClosesImmediatelyAndClearsClocks),
            EscapeRouteClosesImmediatelyAndClearsClocks);
        Run(
            nameof(DisabledMotionClosesImmediately),
            DisabledMotionClosesImmediately);
    }

    private static void ExitContractUsesSharedSplineAndDirectionalOffsets()
    {
        BehaviorTestCheck.Equal(
            TimeSpan.FromMilliseconds(110),
            GuluMotion.QuickExit);
        BehaviorTestCheck.Equal(
            TimeSpan.FromMilliseconds(260),
            PetWindow.WardrobePopupHideDelayForTest);
        BehaviorTestCheck.Close(
            -8,
            PetWindow.ResolveWardrobePopupDismissOffset(
                WardrobePopupPlacementSide.BelowPet));
        BehaviorTestCheck.Close(
            8,
            PetWindow.ResolveWardrobePopupDismissOffset(
                WardrobePopupPlacementSide.AbovePet));

        DoubleAnimationUsingKeyFrames animation = GuluMotion.SplineTo(
            -8,
            GuluMotion.QuickExit);
        BehaviorTestCheck.Equal(FillBehavior.Stop, animation.FillBehavior);
        BehaviorTestCheck.Equal(
            GuluMotion.QuickExit,
            animation.Duration.TimeSpan);
        BehaviorTestCheck.Equal(1, animation.KeyFrames.Count);
        var frame = (SplineDoubleKeyFrame)animation.KeyFrames[0];
        BehaviorTestCheck.Close(-8, frame.Value);
        AssertPoint(0.23, 1, frame.KeySpline.ControlPoint1);
        AssertPoint(0.32, 1, frame.KeySpline.ControlPoint2);
    }

    private static void ClickOpenThenStationaryKeepsWardrobeOpen()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope(showActivated: true);
                PetWindow window = scope.Window;
                window.OpenInteractionPopupForTest(revealImmediately: true);
                BehaviorTestCheck.True(
                    window.IsInteractionPopupOpenForTest);

                scope.SetCursorAtCenter(
                    window.WardrobeInteractionButton);
                window.WardrobeInteractionButton.RaiseEvent(
                    new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpDispatcherUntil(
                    () => window.WardrobePopupRevealSettledForTest
                        && !window.WardrobePopupHasAnimatedPropertiesForTest
                        && window.WardrobePopupHasKeyboardFocusForTest);

                BehaviorTestCheck.False(
                    window.IsInteractionPopupOpenForTest);
                BehaviorTestCheck.True(window.IsWardrobePopupOpenForTest);
                BehaviorTestCheck.False(
                    window.WardrobePopupPointerInteractionArmedForTest);

                // A late MouseLeave/focus teardown from the replaced
                // interaction popup must not target the new wardrobe popup.
                RaiseMouseEvent(
                    window.InteractionPopupHost,
                    Mouse.MouseLeaveEvent);
                PumpDispatcherFor(TimeSpan.FromMilliseconds(550));

                BehaviorTestCheck.True(
                    window.IsWardrobePopupOpenForTest,
                    "A clicked wardrobe must stay open while the pointer "
                    + "remains stationary.");
                BehaviorTestCheck.False(
                    window.IsWardrobePopupDismissInProgressForTest);
                BehaviorTestCheck.False(
                    window.WardrobePopupHideTimerEnabledForTest);
                BehaviorTestCheck.True(
                    window.WardrobePopupIsHitTestVisibleForTest);
                BehaviorTestCheck.True(
                    window.WardrobePopupHasKeyboardFocusForTest,
                    "Queued popup focus must not deactivate its parent window.");
                BehaviorTestCheck.True(
                    window.IsActive,
                    "The activated parent window must remain active while the popup is focused.");
                BehaviorTestCheck.False(
                    window.WardrobePopupHasAnimatedPropertiesForTest);
                BehaviorTestCheck.Close(1, GetOpacityBaseValue(window));
                BehaviorTestCheck.Close(0, GetTranslateBaseValue(window));
            });
    }

    private static void SyntheticLeaveDuringEntranceKeepsWardrobeOpen()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope();
                PetWindow window = scope.Window;

                window.OpenWardrobePopupForTest();
                BehaviorTestCheck.True(window.IsWardrobePopupOpenForTest);
                BehaviorTestCheck.False(
                    window.WardrobePopupRevealSettledForTest);

                RaiseMouseEvent(
                    window.WardrobePopupHost,
                    Mouse.MouseLeaveEvent);
                BehaviorTestCheck.False(
                    window.WardrobePopupHideTimerEnabledForTest);
                PumpDispatcherFor(TimeSpan.FromMilliseconds(550));

                BehaviorTestCheck.True(
                    window.IsWardrobePopupOpenForTest,
                    "An entrance-generated MouseLeave must not dismiss "
                    + "the wardrobe.");
                BehaviorTestCheck.True(
                    window.WardrobePopupRevealSettledForTest);
                BehaviorTestCheck.False(
                    window.WardrobePopupPointerInteractionArmedForTest);
                BehaviorTestCheck.False(
                    window.IsWardrobePopupDismissInProgressForTest);
                BehaviorTestCheck.True(
                    window.WardrobePopupIsHitTestVisibleForTest);
            });
    }

    private static void PhysicalPointerLeaveDismissesDespiteRetainedFocus()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope(showActivated: true);
                PetWindow window = scope.Window;
                ClickOpenWardrobe(scope);
                PumpDispatcherUntil(
                    () => window.WardrobePopupRevealSettledForTest
                        && window.WardrobePopupHasKeyboardFocusForTest);
                window.SetWardrobePopupPlacementSideForTest(
                    WardrobePopupPlacementSide.BelowPet);
                scope.SetCursorAtCenter(window.WardrobePopupHost);
                RaiseMouseEvent(
                    window.WardrobePopupHost,
                    Mouse.MouseMoveEvent);
                BehaviorTestCheck.True(
                    window.WardrobePopupPointerInteractionArmedForTest);
                BehaviorTestCheck.True(
                    window.WardrobePopupHasKeyboardFocusForTest);

                scope.SetCursorOutsideWindows();
                RaiseMouseEvent(
                    window.WardrobePopupHost,
                    Mouse.MouseLeaveEvent);
                BehaviorTestCheck.True(
                    window.WardrobePopupHideTimerEnabledForTest);
                PumpDispatcherUntil(
                    () => window.IsWardrobePopupDismissInProgressForTest);

                BehaviorTestCheck.True(
                    window.IsWardrobePopupOpenForTest,
                    "Pointer dismissal must retain the popup HWND while it exits.");
                BehaviorTestCheck.True(
                    window.IsWardrobePopupDismissInProgressForTest);
                BehaviorTestCheck.True(
                    window.WardrobePopupHasKeyboardFocusForTest,
                    "Automatic keyboard focus must not block a physical pointer leave.");
                BehaviorTestCheck.False(
                    window.WardrobePopupIsHitTestVisibleForTest);
                BehaviorTestCheck.True(
                    window.WardrobePopupHasAnimatedPropertiesForTest);
                PumpDispatcherUntil(
                    () => !window.IsWardrobePopupOpenForTest);
            });
    }

    private static void UnarmedFocusLossDuringEntranceDismissesWardrobe()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope(showActivated: true);
                PetWindow window = scope.Window;
                ClickOpenWardrobe(scope);
                PumpDispatcherUntil(
                    () => window.WardrobePopupHasKeyboardFocusForTest);

                BehaviorTestCheck.False(
                    window.WardrobePopupRevealSettledForTest,
                    "The focus-loss scenario must occur during the entrance.");
                BehaviorTestCheck.False(
                    window.WardrobePopupPointerInteractionArmedForTest);

                window.PetSurface.Focusable = true;
                BehaviorTestCheck.True(
                    window.PetSurface.Focus(),
                    "The test must transfer keyboard focus to a real visible element.");
                PumpDispatcherUntil(
                    () => !window.WardrobePopupHasKeyboardFocusForTest);
                BehaviorTestCheck.True(
                    window.WardrobePopupHideTimerEnabledForTest,
                    "A genuine focus loss must schedule dismissal before pointer arming.");
                PumpDispatcherUntil(
                    () => window.IsWardrobePopupDismissInProgressForTest);

                BehaviorTestCheck.True(window.IsWardrobePopupOpenForTest);
                BehaviorTestCheck.False(
                    window.WardrobePopupIsHitTestVisibleForTest);
                PumpDispatcherUntil(
                    () => !window.IsWardrobePopupOpenForTest);
            });
    }

    private static void DeactivationClosesUnarmedEntranceImmediately()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope(showActivated: true);
                PetWindow window = scope.Window;
                ClickOpenWardrobe(scope);
                BehaviorTestCheck.False(
                    window.WardrobePopupRevealSettledForTest);
                BehaviorTestCheck.False(
                    window.WardrobePopupPointerInteractionArmedForTest);

                var focusTarget = new Window
                {
                    Width = 1,
                    Height = 1,
                    Left = -10_000,
                    Top = -10_000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                try
                {
                    focusTarget.Show();
                    _ = focusTarget.Activate();
                    BehaviorTestCheck.False(
                        window.IsWardrobePopupOpenForTest,
                        "Window deactivation must close an entering wardrobe immediately.");

                    BehaviorTestCheck.False(
                        window.IsWardrobePopupDismissInProgressForTest);
                    BehaviorTestCheck.False(
                        window.WardrobePopupHasAnimatedPropertiesForTest);
                    BehaviorTestCheck.True(GetHitTestBaseValue(window));
                }
                finally
                {
                    focusTarget.Close();
                }
            });
    }

    private static void ToggleExitDisablesInputAndCommitsClosedBaseValues()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope();
                PetWindow window = scope.Window;
                OpenAndSettle(window);
                window.SetWardrobePopupPlacementSideForTest(
                    WardrobePopupPlacementSide.AbovePet);
                long openGeneration =
                    window.WardrobePopupMotionGenerationForTest;

                window.WardrobeInteractionButton.RaiseEvent(
                    new RoutedEventArgs(ButtonBase.ClickEvent));

                BehaviorTestCheck.True(
                    window.IsWardrobePopupOpenForTest);
                BehaviorTestCheck.True(
                    window.IsWardrobePopupDismissInProgressForTest);
                BehaviorTestCheck.True(
                    window.WardrobePopupMotionGenerationForTest
                        > openGeneration);
                BehaviorTestCheck.False(
                    window.WardrobePopupIsHitTestVisibleForTest);
                PumpDispatcherUntil(
                    () => !window.IsWardrobePopupOpenForTest);

                BehaviorTestCheck.False(
                    window.WardrobePopupHasAnimatedPropertiesForTest);
                BehaviorTestCheck.True(GetHitTestBaseValue(window));
                BehaviorTestCheck.Close(1, GetOpacityBaseValue(window));
                BehaviorTestCheck.Close(0, GetTranslateBaseValue(window));
            });
    }

    private static void ReopenRetargetsAndRejectsStaleDismissCompletion()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope();
                PetWindow window = scope.Window;
                OpenAndSettle(window);
                window.DismissWardrobePopupForTest();
                long dismissGeneration =
                    window.WardrobePopupMotionGenerationForTest;
                PumpDispatcherFor(TimeSpan.FromMilliseconds(40));
                double dismissOpacity =
                    window.WardrobePopupOpacityForTest;
                double dismissTranslateY =
                    window.WardrobePopupTranslateYForTest;
                BehaviorTestCheck.True(
                    dismissOpacity is > 0 and < 1,
                    "The wardrobe exit must reach an intermediate opacity before reversal.");
                BehaviorTestCheck.True(
                    Math.Abs(dismissTranslateY) is > 0 and < 8,
                    "The wardrobe exit must reach an intermediate offset before reversal.");

                window.OpenWardrobePopupForTest();

                BehaviorTestCheck.True(
                    window.WardrobePopupMotionGenerationForTest
                        > dismissGeneration);
                BehaviorTestCheck.True(
                    window.IsWardrobePopupOpenForTest);
                BehaviorTestCheck.False(
                    window.IsWardrobePopupDismissInProgressForTest);
                BehaviorTestCheck.True(
                    window.WardrobePopupIsHitTestVisibleForTest);
                BehaviorTestCheck.True(
                    window.WardrobePopupHasAnimatedPropertiesForTest);
                BehaviorTestCheck.Close(1, GetOpacityBaseValue(window));
                BehaviorTestCheck.Close(0, GetTranslateBaseValue(window));
                BehaviorTestCheck.Close(
                    dismissOpacity,
                    window.WardrobePopupOpacityForTest,
                    tolerance: 0.12);
                BehaviorTestCheck.Close(
                    dismissTranslateY,
                    window.WardrobePopupTranslateYForTest,
                    tolerance: 1);

                PumpDispatcherFor(TimeSpan.FromMilliseconds(90));
                BehaviorTestCheck.True(
                    window.IsWardrobePopupOpenForTest,
                    "A stale exit completion must not close a reversing wardrobe.");
                PumpDispatcherUntil(
                    () => !window.WardrobePopupHasAnimatedPropertiesForTest
                        && window.WardrobePopupOpacityForTest >= 0.999
                        && Math.Abs(window.WardrobePopupTranslateYForTest)
                            < 0.001);

                BehaviorTestCheck.True(
                    window.IsWardrobePopupOpenForTest,
                    "A stale exit completion must not close a reopened wardrobe.");
                BehaviorTestCheck.Close(1, GetOpacityBaseValue(window));
                BehaviorTestCheck.Close(0, GetTranslateBaseValue(window));
            });
    }

    private static void EscapeRouteClosesImmediatelyAndClearsClocks()
    {
        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope();
                PetWindow window = scope.Window;
                OpenAndSettle(window);
                long openGeneration =
                    window.WardrobePopupMotionGenerationForTest;
                PresentationSource source = PresentationSource.FromVisual(window)
                    ?? throw new InvalidOperationException(
                        "The shown wardrobe test window has no presentation source.");
                var args = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    source,
                    Environment.TickCount,
                    Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                };

                window.RaiseEvent(args);

                BehaviorTestCheck.True(args.Handled);
                BehaviorTestCheck.False(
                    window.IsWardrobePopupOpenForTest);
                BehaviorTestCheck.True(
                    window.WardrobePopupMotionGenerationForTest
                        > openGeneration);
                BehaviorTestCheck.False(
                    window.WardrobePopupHasAnimatedPropertiesForTest);
                BehaviorTestCheck.True(GetHitTestBaseValue(window));
                BehaviorTestCheck.Close(1, GetOpacityBaseValue(window));
                BehaviorTestCheck.Close(0, GetTranslateBaseValue(window));

                PumpDispatcherFor(GuluMotion.MoveEntrance);
                BehaviorTestCheck.False(
                    window.IsWardrobePopupOpenForTest,
                    "A queued wardrobe entrance must not survive Escape.");
            });
    }

    private static void DisabledMotionClosesImmediately()
    {
        BehaviorTestCheck.False(GuluMotion.ShouldAnimate(
            clientAreaAnimationEnabled: false,
            highContrastEnabled: false));
        BehaviorTestCheck.False(GuluMotion.ShouldAnimate(
            clientAreaAnimationEnabled: true,
            highContrastEnabled: true));

        RunSta(
            () =>
            {
                using var scope = new MotionWindowScope();
                PetWindow window = scope.Window;
                OpenAndSettle(window);
                window.SetWardrobePopupAnimationsEnabledForTest(false);

                window.DismissWardrobePopupForTest();

                BehaviorTestCheck.False(
                    window.IsWardrobePopupOpenForTest);
                BehaviorTestCheck.False(
                    window.IsWardrobePopupDismissInProgressForTest);
                BehaviorTestCheck.False(
                    window.WardrobePopupHasAnimatedPropertiesForTest);
                BehaviorTestCheck.True(GetHitTestBaseValue(window));
                BehaviorTestCheck.Close(1, GetOpacityBaseValue(window));
                BehaviorTestCheck.Close(0, GetTranslateBaseValue(window));
            });
    }

    private static void OpenAndSettle(PetWindow window)
    {
        window.OpenWardrobePopupForTest();
        BehaviorTestCheck.True(window.IsWardrobePopupOpenForTest);
        PumpDispatcherUntil(
            () => !window.WardrobePopupHasAnimatedPropertiesForTest
                && window.WardrobePopupOpacityForTest >= 0.999
                && Math.Abs(window.WardrobePopupTranslateYForTest) < 0.001);
        BehaviorTestCheck.Close(1, GetOpacityBaseValue(window));
        BehaviorTestCheck.Close(0, GetTranslateBaseValue(window));
    }

    private static void ClickOpenWardrobe(MotionWindowScope scope)
    {
        PetWindow window = scope.Window;
        window.OpenInteractionPopupForTest(revealImmediately: true);
        BehaviorTestCheck.True(window.IsInteractionPopupOpenForTest);
        scope.SetCursorAtCenter(window.WardrobeInteractionButton);
        window.WardrobeInteractionButton.RaiseEvent(
            new RoutedEventArgs(ButtonBase.ClickEvent));
        BehaviorTestCheck.True(window.IsWardrobePopupOpenForTest);
    }

    private static void RaiseMouseEvent(
        UIElement target,
        RoutedEvent routedEvent)
    {
        var args = new MouseEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount)
        {
            RoutedEvent = routedEvent,
        };
        target.RaiseEvent(args);
    }

    private static double GetOpacityBaseValue(PetWindow window) =>
        (double)window.WardrobePopupSurface.GetAnimationBaseValue(
            UIElement.OpacityProperty);

    private static double GetTranslateBaseValue(PetWindow window) =>
        (double)window.WardrobePopupTranslate.GetAnimationBaseValue(
            TranslateTransform.YProperty);

    private static bool GetHitTestBaseValue(PetWindow window)
    {
        object value = window.WardrobePopupHost.ReadLocalValue(
            UIElement.IsHitTestVisibleProperty);
        return value == DependencyProperty.UnsetValue || (bool)value;
    }

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
                    "Wardrobe popup motion did not settle in time.");
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
            $"PASS {nameof(WardrobePopupMotionTests)}.{name}");
    }

    private sealed class MotionWindowScope : IDisposable
    {
        private Point _cursorPosition = new(1_200, 800);

        internal MotionWindowScope(bool showActivated = false)
        {
            Window = new PetWindow
            {
                Left = 220,
                Top = 180,
                ShowActivated = showActivated,
            };
            Window.SetWardrobePopupAnimationsEnabledForTest(true);
            Window.SetWardrobePopupCursorProviderForTest(
                () => _cursorPosition);
            Window.ConfigureEyeAccessoryMenu(
                CreateAccessories(),
                selectedId: null);
            Window.Show();
            if (showActivated)
            {
                _ = Window.Activate();
            }

            Window.UpdateLayout();
        }

        internal PetWindow Window { get; }

        internal void SetCursorAtCenter(FrameworkElement element)
        {
            element.UpdateLayout();
            _cursorPosition = element.PointToScreen(
                new Point(
                    element.ActualWidth / 2,
                    element.ActualHeight / 2));
        }

        internal void SetCursorOutsideWindows() =>
            _cursorPosition = new Point(-10_000, -10_000);

        public void Dispose()
        {
            Window.SetWardrobePopupCursorProviderForTest(null);
            Window.SetWardrobePopupAnimationsEnabledForTest(null);
            Window.Close();
        }
    }

    private static IReadOnlyList<EyeAccessoryDefinition> CreateAccessories()
    {
        string missingRoot = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.WardrobeMotionTests.{Guid.NewGuid():N}");
        return Enumerable.Range(1, EyeAccessoryCatalog.RequiredAccessoryCount)
            .Select(
                index => new EyeAccessoryDefinition(
                    Id: $"wardrobe-motion-{index:00}",
                    DisplayName: $"测试装扮 {index}",
                    SelectionOrder: index,
                    ImageFile: $"missing-{index:00}.png",
                    ImagePath: Path.Combine(
                        missingRoot,
                        $"missing-{index:00}.png"),
                    Width: 1,
                    Height: 1,
                    LeftEye: new EyeAccessoryPoint(0, 0),
                    RightEye: new EyeAccessoryPoint(1, 0),
                    HeadWidthPixels: 1,
                    Sha256: new string('0', 64),
                    SizeBytes: 0,
                    Variants:
                        new Dictionary<
                            EyeAccessoryViewState,
                            EyeAccessoryVariantDefinition>()))
            .ToArray();
    }
}
