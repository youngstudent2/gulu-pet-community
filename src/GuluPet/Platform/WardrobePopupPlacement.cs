namespace GuluPet.Platform;

public enum WardrobePopupPlacementSide
{
    BelowPet,
    AbovePet,
}

/// <summary>
/// A screen-space rectangle expressed entirely in device-independent pixels.
/// </summary>
public readonly record struct WardrobePopupDipRectangle(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

public readonly record struct WardrobePopupPlacementResult(
    WardrobePopupDipRectangle BoundsDips,
    WardrobePopupPlacementSide Side);

public readonly record struct WardrobePopupHostPlacementResult(
    WardrobePopupDipRectangle HostBoundsDips,
    WardrobePopupDipRectangle SurfaceBoundsDips,
    WardrobePopupPlacementSide Side);

/// <summary>
/// Places the wardrobe item bar in screen-space DIPs. It prefers the space
/// below the pet, flips above when the bottom space is insufficient, and
/// clamps the bar inside the monitor work area with a small edge margin.
/// </summary>
public static class WardrobePopupPlacement
{
    public const double GapDips = 10;
    public const double WorkAreaMarginDips = 12;

    public static WardrobePopupPlacementResult Calculate(
        WardrobePopupDipRectangle petSurfaceBoundsDips,
        WardrobePopupDipRectangle workAreaDips,
        double popupWidthDips,
        double popupHeightDips)
    {
        ValidateRectangle(
            petSurfaceBoundsDips,
            nameof(petSurfaceBoundsDips));
        ValidateRectangle(workAreaDips, nameof(workAreaDips));
        ValidateDimension(popupWidthDips, nameof(popupWidthDips));
        ValidateDimension(popupHeightDips, nameof(popupHeightDips));

        double horizontalMargin = ResolveMargin(workAreaDips.Width);
        double verticalMargin = ResolveMargin(workAreaDips.Height);
        double width = Math.Min(
            popupWidthDips,
            workAreaDips.Width - horizontalMargin * 2);
        double height = Math.Min(
            popupHeightDips,
            workAreaDips.Height - verticalMargin * 2);

        double minimumLeft = workAreaDips.Left + horizontalMargin;
        double maximumLeft = workAreaDips.Right - horizontalMargin - width;
        double centeredLeft = petSurfaceBoundsDips.Left
            + (petSurfaceBoundsDips.Width - width) / 2;
        double left = Math.Clamp(centeredLeft, minimumLeft, maximumLeft);

        double minimumTop = workAreaDips.Top + verticalMargin;
        double maximumTop = workAreaDips.Bottom - verticalMargin - height;
        double belowTop = petSurfaceBoundsDips.Bottom + GapDips;
        bool fitsBelow = belowTop <= maximumTop;
        WardrobePopupPlacementSide side = fitsBelow
            ? WardrobePopupPlacementSide.BelowPet
            : WardrobePopupPlacementSide.AbovePet;
        double preferredTop = fitsBelow
            ? belowTop
            : petSurfaceBoundsDips.Top - GapDips - height;
        double top = Math.Clamp(preferredTop, minimumTop, maximumTop);

        return new WardrobePopupPlacementResult(
            new WardrobePopupDipRectangle(left, top, width, height),
            side);
    }

    /// <summary>
    /// Places the visible item bar while reserving transparent room around it
    /// for a popup shadow. The visible surface, rather than the shadow host,
    /// owns the ten-DIP relationship to the pet.
    /// </summary>
    public static WardrobePopupHostPlacementResult CalculateWithShadowHost(
        WardrobePopupDipRectangle petSurfaceBoundsDips,
        WardrobePopupDipRectangle workAreaDips,
        double surfaceWidthDips,
        double surfaceHeightDips,
        double shadowPaddingDips)
    {
        ValidateRectangle(
            petSurfaceBoundsDips,
            nameof(petSurfaceBoundsDips));
        ValidateRectangle(workAreaDips, nameof(workAreaDips));
        ValidateDimension(surfaceWidthDips, nameof(surfaceWidthDips));
        ValidateDimension(surfaceHeightDips, nameof(surfaceHeightDips));
        if (!double.IsFinite(shadowPaddingDips)
            || shadowPaddingDips < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shadowPaddingDips),
                "Wardrobe popup shadow padding must be finite and "
                + "non-negative.");
        }

        double horizontalPadding = Math.Min(
            shadowPaddingDips,
            Math.Max(0, (workAreaDips.Width - 1) / 2));
        double verticalPadding = Math.Min(
            shadowPaddingDips,
            Math.Max(0, (workAreaDips.Height - 1) / 2));
        WardrobePopupDipRectangle surfaceWorkArea = new(
            workAreaDips.Left + horizontalPadding,
            workAreaDips.Top + verticalPadding,
            workAreaDips.Width - horizontalPadding * 2,
            workAreaDips.Height - verticalPadding * 2);
        WardrobePopupPlacementResult surfacePlacement = Calculate(
            petSurfaceBoundsDips,
            surfaceWorkArea,
            surfaceWidthDips,
            surfaceHeightDips);
        WardrobePopupDipRectangle surface = surfacePlacement.BoundsDips;
        WardrobePopupDipRectangle host = new(
            surface.Left - horizontalPadding,
            surface.Top - verticalPadding,
            surface.Width + horizontalPadding * 2,
            surface.Height + verticalPadding * 2);
        return new WardrobePopupHostPlacementResult(
            host,
            surface,
            surfacePlacement.Side);
    }

    private static double ResolveMargin(double workAreaLengthDips) =>
        Math.Min(
            WorkAreaMarginDips,
            Math.Max(0, (workAreaLengthDips - 1) / 2));

    private static void ValidateRectangle(
        WardrobePopupDipRectangle rectangle,
        string parameterName)
    {
        if (!double.IsFinite(rectangle.Left)
            || !double.IsFinite(rectangle.Top)
            || !double.IsFinite(rectangle.Width)
            || !double.IsFinite(rectangle.Height)
            || rectangle.Width <= 0
            || rectangle.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Wardrobe rectangles must contain finite DIP values and " +
                "positive dimensions.");
        }
    }

    private static void ValidateDimension(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Wardrobe popup DIP dimensions must be finite and positive.");
        }
    }
}
