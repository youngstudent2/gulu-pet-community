namespace GuluPet.Platform;

internal readonly record struct BubblePlacementResult(
    WindowRectangle Bounds);

internal static class BubblePlacement
{
    private const double VerticalOverlapDip = 24;
    private const double MarginDip = 8;

    internal static BubblePlacementResult CalculateFromDips(
        WindowRectangle anchor,
        WindowRectangle workArea,
        double bubbleWidthDip,
        double bubbleHeightDip,
        uint dpiX,
        uint dpiY)
    {
        ValidateDipDimension(bubbleWidthDip, nameof(bubbleWidthDip));
        ValidateDipDimension(bubbleHeightDip, nameof(bubbleHeightDip));

        int bubbleWidth = Math.Max(
            1,
            PhysicalDragPlacement.DipsToPhysicalPixels(
                bubbleWidthDip,
                dpiX));
        int bubbleHeight = Math.Max(
            1,
            PhysicalDragPlacement.DipsToPhysicalPixels(
                bubbleHeightDip,
                dpiY));
        return CalculatePhysical(
            anchor,
            workArea,
            bubbleWidth,
            bubbleHeight,
            dpiX,
            dpiY);
    }

    internal static BubblePlacementResult CalculatePhysical(
        WindowRectangle anchor,
        WindowRectangle workArea,
        int bubbleWidth,
        int bubbleHeight,
        uint dpiX,
        uint dpiY)
    {
        ValidateRectangle(anchor, nameof(anchor));
        ValidateRectangle(workArea, nameof(workArea));
        if (bubbleWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bubbleWidth),
                "Bubble width must be greater than zero.");
        }

        if (bubbleHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bubbleHeight),
                "Bubble height must be greater than zero.");
        }

        int verticalOverlap =
            PhysicalDragPlacement.DipsToPhysicalPixels(
                VerticalOverlapDip,
                dpiY);
        int horizontalMargin =
            PhysicalDragPlacement.DipsToPhysicalPixels(
                MarginDip,
                dpiX);
        int verticalMargin =
            PhysicalDragPlacement.DipsToPhysicalPixels(
                MarginDip,
                dpiY);

        long minimumLeft = (long)workArea.Left + horizontalMargin;
        long maximumLeft = Math.Max(
            minimumLeft,
            (long)workArea.Left
                + workArea.Width
                - bubbleWidth
                - horizontalMargin);
        long minimumTop = (long)workArea.Top + verticalMargin;
        long maximumTop = Math.Max(
            minimumTop,
            (long)workArea.Top
                + workArea.Height
                - bubbleHeight
                - verticalMargin);

        int centeredLeft = ConvertToInt(
            (long)anchor.Left
            + ((long)anchor.Width - bubbleWidth) / 2.0);
        int left = ClampToInt(
            centeredLeft,
            minimumLeft,
            maximumLeft);
        int top = ClampToInt(
            (long)anchor.Top - bubbleHeight + verticalOverlap,
            minimumTop,
            maximumTop);
        return new BubblePlacementResult(
            new WindowRectangle(
                left,
                top,
                bubbleWidth,
                bubbleHeight));
    }

    private static void ValidateDipDimension(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Bubble DIP dimensions must be finite and greater than zero.");
        }
    }

    private static void ValidateRectangle(
        WindowRectangle rectangle,
        string parameterName)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Window rectangles must have positive dimensions.");
        }
    }

    private static int ClampToInt(
        long value,
        long minimum,
        long maximum)
    {
        long clamped = Math.Clamp(value, minimum, maximum);
        return clamped >= int.MaxValue
            ? int.MaxValue
            : clamped <= int.MinValue
                ? int.MinValue
                : (int)clamped;
    }

    private static int ConvertToInt(double value) =>
        value >= int.MaxValue
            ? int.MaxValue
            : value <= int.MinValue
                ? int.MinValue
                : (int)Math.Round(
                    value,
                    MidpointRounding.AwayFromZero);
}
