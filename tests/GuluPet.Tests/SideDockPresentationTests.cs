using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GuluPet.Platform;
using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class SideDockPresentationTests
{
    public static void RunAll()
    {
        Run(
            nameof(AssetMatrixDistinguishesBothEdgesHoverAndBlink),
            AssetMatrixDistinguishesBothEdgesHoverAndBlink);
        Run(
            nameof(LeftDockHoverBlinkAndExitRestorePresentation),
            LeftDockHoverBlinkAndExitRestorePresentation);
        Run(
            nameof(RightDockHoverBlinkAndExitRestorePresentation),
            RightDockHoverBlinkAndExitRestorePresentation);
    }

    private static void AssetMatrixDistinguishesBothEdgesHoverAndBlink()
    {
        BehaviorTestCheck.SequenceEqual(
            new[]
            {
                "left_rest_open.png",
                "left_rest_closed.png",
                "left_hover_open.png",
                "left_hover_closed.png",
                "right_rest_open.png",
                "right_rest_closed.png",
                "right_hover_open.png",
                "right_hover_closed.png",
            },
            from edge in new[] { SideDockEdge.Left, SideDockEdge.Right }
            from hovered in new[] { false, true }
            from blinkClosed in new[] { false, true }
            select PetWindow.ResolveSideDockAssetFileName(
                edge,
                hovered,
                blinkClosed));
    }

    private static void LeftDockHoverBlinkAndExitRestorePresentation() =>
        DockHoverBlinkAndExitRestorePresentation(SideDockEdge.Left);

    private static void RightDockHoverBlinkAndExitRestorePresentation() =>
        DockHoverBlinkAndExitRestorePresentation(SideDockEdge.Right);

    private static void DockHoverBlinkAndExitRestorePresentation(
        SideDockEdge edge)
    {
        RunSta(
            () =>
            {
                var window = new PetWindow();
                try
                {
                    _ = new WindowInteropHelper(window).EnsureHandle();
                    WindowRectangle original =
                        WindowPlacementService.GetRectangle(window);
                    var restoreBounds = new WindowRectangle(
                        original.Left,
                        original.Top,
                        original.Width,
                        original.Height);
                    var dockBounds = new WindowRectangle(
                        checked(original.Left + 24),
                        checked(original.Top + 18),
                        original.Width,
                        original.Height);
                    var candidate = new SideDockCandidate
                    {
                        Edge = edge,
                        DockBounds = dockBounds,
                        RestoreBounds = restoreBounds,
                        MonitorBounds = new WindowRectangle(
                            original.Left - 1_000,
                            original.Top - 1_000,
                            3_000,
                            3_000),
                        WorkArea = new WindowRectangle(
                            original.Left - 1_000,
                            original.Top - 1_000,
                            3_000,
                            3_000),
                    };
                    var normalFrame = new DrawingImage();
                    normalFrame.Freeze();
                    window.SetPetFrame(normalFrame);
                    int released = 0;
                    window.SideDockReleased += (_, _) => released++;

                    window.EnterSideDock(candidate);

                    BehaviorTestCheck.True(window.IsSideDocked);
                    BehaviorTestCheck.Equal(
                        (SideDockEdge?)edge,
                        window.CurrentSideDockEdge);
                    BehaviorTestCheck.False(window.IsSideDockHovered);
                    BehaviorTestCheck.False(window.IsSideDockBlinkClosed);
                    AssertDockAsset(window, edge, "rest", "open");
                    BehaviorTestCheck.Equal(
                        Visibility.Collapsed,
                        window.PetImage.Visibility);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.SideDockImage.Visibility);
                    BehaviorTestCheck.False(
                        window.IsInteractionPopupOpenForTest);
                    AssertOrigin(dockBounds, window);

                    window.SetSideDockHoverForTest(hovered: true);
                    BehaviorTestCheck.True(window.IsSideDockHovered);
                    AssertDockAsset(window, edge, "hover", "open");
                    window.AdvanceSideDockBlinkForTest();
                    BehaviorTestCheck.True(window.IsSideDockBlinkClosed);
                    AssertDockAsset(window, edge, "hover", "closed");
                    window.AdvanceSideDockBlinkForTest();
                    BehaviorTestCheck.False(window.IsSideDockBlinkClosed);
                    AssertDockAsset(window, edge, "hover", "open");

                    window.SetSideDockHoverForTest(hovered: false);
                    BehaviorTestCheck.False(window.IsSideDockHovered);
                    AssertDockAsset(window, edge, "rest", "open");
                    BehaviorTestCheck.True(
                        ReferenceEquals(normalFrame, window.PetImage.Source),
                        "Side-dock frames must not replace the normal " +
                        "behavior frame.");
                    BehaviorTestCheck.False(
                        window.IsInteractionPopupOpenForTest);

                    window.ExitSideDock();

                    BehaviorTestCheck.False(window.IsSideDocked);
                    BehaviorTestCheck.Null(window.CurrentSideDockEdge);
                    BehaviorTestCheck.Null(
                        window.CurrentSideDockAssetFileName);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.PetImage.Visibility);
                    BehaviorTestCheck.Equal(
                        Visibility.Collapsed,
                        window.SideDockImage.Visibility);
                    BehaviorTestCheck.True(
                        ReferenceEquals(normalFrame, window.PetImage.Source));
                    BehaviorTestCheck.Equal(1, released);
                    AssertOrigin(restoreBounds, window);

                    window.ExitSideDock();
                    BehaviorTestCheck.Equal(1, released);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void AssertDockAsset(
        PetWindow window,
        SideDockEdge edge,
        string posture,
        string eyes)
    {
        string side = edge == SideDockEdge.Left ? "left" : "right";
        BehaviorTestCheck.Equal(
            $"{side}_{posture}_{eyes}.png",
            window.CurrentSideDockAssetFileName);
        BehaviorTestCheck.True(
            window.SideDockImage.Source is not null,
            $"Side-dock asset '{window.CurrentSideDockAssetFileName}' " +
            "was not loaded.");
    }

    private static void AssertOrigin(
        WindowRectangle expected,
        PetWindow window)
    {
        WindowRectangle actual = WindowPlacementService.GetRectangle(window);
        BehaviorTestCheck.Equal(expected.Left, actual.Left);
        BehaviorTestCheck.Equal(expected.Top, actual.Top);
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
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException(
                "The STA side-dock presentation test timed out.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(SideDockPresentationTests)}.{name}");
    }
}
