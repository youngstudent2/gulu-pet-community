using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class CareInteractionPresentationTests
{
    public static void RunAll()
    {
        Run(
            nameof(DisplaysGeneratedIconsMetersAndTravelStatus),
            DisplaysGeneratedIconsMetersAndTravelStatus);
        Run(
            nameof(InteractionButtonsRaiseExactlyOneRequest),
            InteractionButtonsRaiseExactlyOneRequest);
        Run(
            nameof(PopupMouseRouteDoesNotBecomePetDragInput),
            PopupMouseRouteDoesNotBecomePetDragInput);
        InteractionPopupMotionTests.RunAll();
    }

    private static void DisplaysGeneratedIconsMetersAndTravelStatus()
    {
        RunSta(
            () =>
            {
                var window = new PetWindow();
                try
                {
                    BehaviorTestCheck.True(
                        window.FeedInteractionIcon.Source is not null);
                    BehaviorTestCheck.True(
                        window.WaterInteractionIcon.Source is not null);
                    BehaviorTestCheck.True(
                        window.TravelInteractionIcon.Source is not null);
                    BehaviorTestCheck.True(
                        window.PetInteractionIcon.Source is not null);
                    BehaviorTestCheck.True(
                        window.WardrobeInteractionIcon.Source is not null);
                    BehaviorTestCheck.Null(window.FindName("CarePropLayer"));
                    BehaviorTestCheck.Null(window.FindName("FeedPropImage"));
                    BehaviorTestCheck.Null(window.FindName("WaterPropImage"));

                    window.SetCareState(42, 18, interactionBusy: false);
                    BehaviorTestCheck.Close(21, window.FoodMeterFill.Width);
                    BehaviorTestCheck.Close(9, window.WaterMeterFill.Width);
                    BehaviorTestCheck.Equal("42%", window.FoodMeterText.Text);
                    BehaviorTestCheck.Equal("18%", window.WaterMeterText.Text);
                    BehaviorTestCheck.True(
                        window.FeedInteractionButton.IsEnabled);
                    BehaviorTestCheck.True(
                        window.WaterInteractionButton.IsEnabled);

                    window.SetTravelActionState(
                        "出游中",
                        "剩余 29:59",
                        isEnabled: false);
                    BehaviorTestCheck.Equal(
                        "出游中",
                        window.TravelInteractionText.Text);
                    BehaviorTestCheck.Equal(
                        "剩余 29:59",
                        window.TravelCountdownText.Text);
                    BehaviorTestCheck.Equal(
                        Visibility.Visible,
                        window.TravelCountdownText.Visibility);
                    BehaviorTestCheck.False(
                        window.TravelInteractionButton.IsEnabled);
                    BehaviorTestCheck.True(
                        window.WardrobeInteractionButton.IsEnabled);

                    window.SetTravelActionState(
                        "在家门口",
                        countdown: null,
                        isEnabled: true);
                    BehaviorTestCheck.Equal(
                        Visibility.Collapsed,
                        window.TravelCountdownText.Visibility);
                    BehaviorTestCheck.True(
                        window.TravelInteractionButton.IsEnabled);

                    window.SetCareState(42, 18, interactionBusy: true);
                    BehaviorTestCheck.False(
                        window.FeedInteractionButton.IsEnabled);
                    BehaviorTestCheck.False(
                        window.WaterInteractionButton.IsEnabled);
                    BehaviorTestCheck.False(
                        window.PetInteractionButton.IsEnabled);
                    BehaviorTestCheck.False(
                        window.TravelInteractionButton.IsEnabled);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void InteractionButtonsRaiseExactlyOneRequest()
    {
        RunSta(
            () =>
            {
                var window = new PetWindow();
                try
                {
                    var feed = 0;
                    var water = 0;
                    var travel = 0;
                    var petting = 0;
                    window.FeedRequested += (_, _) => feed++;
                    window.WaterRequested += (_, _) => water++;
                    window.TravelRequested += (_, _) => travel++;
                    window.PettingRequested += (_, _) => petting++;
                    window.SetCareState(100, 100, interactionBusy: false);
                    window.SetTravelActionState(
                        "出游",
                        countdown: null,
                        isEnabled: true);

                    RaiseClick(window.FeedInteractionButton);
                    RaiseClick(window.WaterInteractionButton);
                    RaiseClick(window.TravelInteractionButton);
                    RaiseClick(window.PetInteractionButton);

                    BehaviorTestCheck.Equal(1, feed);
                    BehaviorTestCheck.Equal(1, water);
                    BehaviorTestCheck.Equal(1, travel);
                    BehaviorTestCheck.Equal(1, petting);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void PopupMouseRouteDoesNotBecomePetDragInput()
    {
        RunSta(
            () =>
            {
                var window = new PetWindow();
                try
                {
                    var down = new MouseButtonEventArgs(
                        Mouse.PrimaryDevice,
                        Environment.TickCount,
                        MouseButton.Left)
                    {
                        RoutedEvent = Mouse.PreviewMouseDownEvent,
                        Source = window.FeedInteractionButton,
                    };
                    window.FeedInteractionButton.RaiseEvent(down);
                    BehaviorTestCheck.False(down.Handled);

                    var up = new MouseButtonEventArgs(
                        Mouse.PrimaryDevice,
                        Environment.TickCount,
                        MouseButton.Left)
                    {
                        RoutedEvent = Mouse.PreviewMouseUpEvent,
                        Source = window.FeedInteractionButton,
                    };
                    window.FeedInteractionButton.RaiseEvent(up);
                    BehaviorTestCheck.False(up.Handled);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void RaiseClick(System.Windows.Controls.Button button) =>
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

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

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(CareInteractionPresentationTests)}.{name}");
    }
}
