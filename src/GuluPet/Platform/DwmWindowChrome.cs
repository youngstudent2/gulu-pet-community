using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GuluPet.Platform;

public static class DwmWindowChrome
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmWindowCornerRound = 2;

    public static readonly DependencyProperty UseRoundedCornersProperty =
        DependencyProperty.RegisterAttached(
            "UseRoundedCorners",
            typeof(bool),
            typeof(DwmWindowChrome),
            new PropertyMetadata(false, OnUseRoundedCornersChanged));

    public static bool GetUseRoundedCorners(DependencyObject element) =>
        (bool)element.GetValue(UseRoundedCornersProperty);

    public static void SetUseRoundedCorners(
        DependencyObject element,
        bool value) =>
        element.SetValue(UseRoundedCornersProperty, value);

    private static void OnUseRoundedCornersChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not Window window)
        {
            throw new ArgumentException(
                "DWM window chrome can only be applied to a Window.",
                nameof(dependencyObject));
        }

        window.SourceInitialized -= OnWindowSourceInitialized;
        if (eventArgs.NewValue is true)
        {
            window.SourceInitialized += OnWindowSourceInitialized;
            ApplyRoundedCorners(window);
        }
    }

    private static void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            ApplyRoundedCorners(window);
        }
    }

    private static void ApplyRoundedCorners(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        _ = TryApplyRoundedCorners(handle);
    }

    internal static bool TryApplyRoundedCorners(IntPtr handle)
    {
        if (handle == IntPtr.Zero ||
            !WindowsCompatibility.SupportsCurrentDwmRoundedCornerPreference)
        {
            return false;
        }

        var cornerPreference = DwmWindowCornerRound;
        try
        {
            return DwmSetWindowAttribute(
                handle,
                DwmwaWindowCornerPreference,
                ref cornerPreference,
                Marshal.SizeOf<int>()) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
