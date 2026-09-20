namespace GuluPet.Platform;

internal readonly record struct PhysicalWindowOrigin(int Left, int Top);

internal static class PhysicalDragPlacement
{
    private const double LogicalDpi = 96;

    internal static PhysicalWindowOrigin Calculate(
        int cursorX,
        int cursorY,
        double anchorDipX,
        double anchorDipY,
        uint dpiX,
        uint dpiY)
    {
        if (!double.IsFinite(anchorDipX))
        {
            throw new ArgumentOutOfRangeException(
                nameof(anchorDipX),
                "The horizontal drag anchor must be finite.");
        }

        if (!double.IsFinite(anchorDipY))
        {
            throw new ArgumentOutOfRangeException(
                nameof(anchorDipY),
                "The vertical drag anchor must be finite.");
        }

        if (dpiX == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dpiX),
                "Horizontal DPI must be greater than zero.");
        }

        if (dpiY == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dpiY),
                "Vertical DPI must be greater than zero.");
        }

        int physicalAnchorX = ConvertToInt(anchorDipX * dpiX / LogicalDpi);
        int physicalAnchorY = ConvertToInt(anchorDipY * dpiY / LogicalDpi);
        return new PhysicalWindowOrigin(
            SubtractSaturated(cursorX, physicalAnchorX),
            SubtractSaturated(cursorY, physicalAnchorY));
    }

    internal static int DipsToPhysicalPixels(double dips, uint dpi)
    {
        if (!double.IsFinite(dips))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dips),
                "The DIP value must be finite.");
        }

        if (dpi == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dpi),
                "DPI must be greater than zero.");
        }

        return ConvertToInt(dips * dpi / LogicalDpi);
    }

    private static int ConvertToInt(double value)
    {
        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static int SubtractSaturated(int left, int right)
    {
        long result = (long)left - right;
        return result >= int.MaxValue
            ? int.MaxValue
            : result <= int.MinValue
                ? int.MinValue
                : (int)result;
    }
}
