namespace GuluPet.Platform;

public enum MemoryWindowSide
{
    Left,
    Right,
}

public readonly record struct MemoryWindowPlacementResult(
    WindowRectangle Bounds,
    MemoryWindowSide Side);

/// <summary>
/// Calculates a physical-pixel window rectangle beside the pet. Pet and work
/// area rectangles, along with the desired window size, are physical pixels;
/// only the requested gap is expressed in device-independent pixels.
/// </summary>
public static class MemoryWindowPlacement
{
    public static MemoryWindowPlacementResult Calculate(
        WindowRectangle petBounds,
        WindowRectangle workArea,
        int desiredWindowWidth,
        int desiredWindowHeight,
        double gapDips,
        uint dpiX,
        MemoryWindowSide? preferredSide = null)
    {
        Validate(
            petBounds,
            workArea,
            desiredWindowWidth,
            desiredWindowHeight,
            gapDips,
            dpiX);

        int gapPixels = PhysicalDragPlacement.DipsToPhysicalPixels(
            gapDips,
            dpiX);
        long workRight = Right(workArea);
        long petRight = Right(petBounds);
        long leftRegionRight = Math.Min(
            workRight,
            (long)petBounds.Left - gapPixels);
        long rightRegionLeft = Math.Max(
            workArea.Left,
            petRight + gapPixels);
        int leftSpace = AvailableLength(
            workArea.Left,
            leftRegionRight,
            workArea.Width);
        int rightSpace = AvailableLength(
            rightRegionLeft,
            workRight,
            workArea.Width);

        double heightScale = Math.Min(
            1d,
            workArea.Height / (double)desiredWindowHeight);
        int heightConstrainedWidth = ScaleDimension(
            desiredWindowWidth,
            heightScale);
        MemoryWindowSide side = SelectSide(
            leftSpace,
            rightSpace,
            heightConstrainedWidth,
            preferredSide);
        int availableWidth = side == MemoryWindowSide.Left
            ? leftSpace
            : rightSpace;

        // If neither side has even one free pixel, avoiding the pet is
        // geometrically impossible. Keep the window useful by fitting it to
        // the work area and let the final clamp provide the best effort.
        int widthConstraint = availableWidth > 0
            ? availableWidth
            : workArea.Width;
        double widthScale = widthConstraint
            / (double)desiredWindowWidth;
        double scale = Math.Min(1d, Math.Min(heightScale, widthScale));
        int width = Math.Min(
            workArea.Width,
            ScaleDimension(desiredWindowWidth, scale));
        int height = Math.Min(
            workArea.Height,
            ScaleDimension(desiredWindowHeight, scale));

        long requestedLeft = side == MemoryWindowSide.Left
            ? leftRegionRight - width
            : rightRegionLeft;
        int left = ClampOrigin(
            requestedLeft,
            workArea.Left,
            workArea.Width,
            width);
        long requestedTop = (long)petBounds.Top
            + ((long)petBounds.Height - height) / 2;
        int top = ClampOrigin(
            requestedTop,
            workArea.Top,
            workArea.Height,
            height);

        return new MemoryWindowPlacementResult(
            new WindowRectangle(left, top, width, height),
            side);
    }

    /// <summary>
    /// Fits a fixed-chrome companion window beside the pet without scaling
    /// both axes together. WPF headers and buttons do not scale with the
    /// window, so constraining only the overflowing axis preserves usable
    /// controls while the media element letterboxes its content as needed.
    /// </summary>
    public static MemoryWindowPlacementResult CalculateResponsive(
        WindowRectangle petBounds,
        WindowRectangle workArea,
        int desiredWindowWidth,
        int desiredWindowHeight,
        double gapDips,
        uint dpiX,
        MemoryWindowSide? preferredSide = null)
    {
        Validate(
            petBounds,
            workArea,
            desiredWindowWidth,
            desiredWindowHeight,
            gapDips,
            dpiX);

        int gapPixels = PhysicalDragPlacement.DipsToPhysicalPixels(
            gapDips,
            dpiX);
        long workRight = Right(workArea);
        long petRight = Right(petBounds);
        long leftRegionRight = Math.Min(
            workRight,
            (long)petBounds.Left - gapPixels);
        long rightRegionLeft = Math.Max(
            workArea.Left,
            petRight + gapPixels);
        int leftSpace = AvailableLength(
            workArea.Left,
            leftRegionRight,
            workArea.Width);
        int rightSpace = AvailableLength(
            rightRegionLeft,
            workRight,
            workArea.Width);
        MemoryWindowSide side = SelectSide(
            leftSpace,
            rightSpace,
            desiredWindowWidth,
            preferredSide);
        int availableWidth = side == MemoryWindowSide.Left
            ? leftSpace
            : rightSpace;
        int widthConstraint = availableWidth > 0
            ? availableWidth
            : workArea.Width;
        int width = Math.Max(
            1,
            Math.Min(
                desiredWindowWidth,
                Math.Min(workArea.Width, widthConstraint)));
        int height = Math.Max(
            1,
            Math.Min(desiredWindowHeight, workArea.Height));

        long requestedLeft = side == MemoryWindowSide.Left
            ? leftRegionRight - width
            : rightRegionLeft;
        int left = ClampOrigin(
            requestedLeft,
            workArea.Left,
            workArea.Width,
            width);
        long requestedTop = (long)petBounds.Top
            + ((long)petBounds.Height - height) / 2;
        int top = ClampOrigin(
            requestedTop,
            workArea.Top,
            workArea.Height,
            height);

        return new MemoryWindowPlacementResult(
            new WindowRectangle(left, top, width, height),
            side);
    }

    private static MemoryWindowSide SelectSide(
        int leftSpace,
        int rightSpace,
        int requestedWidth,
        MemoryWindowSide? preferredSide)
    {
        if (preferredSide is MemoryWindowSide preferred)
        {
            int preferredSpace = SpaceFor(
                preferred,
                leftSpace,
                rightSpace);
            if (preferredSpace >= requestedWidth)
            {
                return preferred;
            }

            MemoryWindowSide opposite = preferred == MemoryWindowSide.Left
                ? MemoryWindowSide.Right
                : MemoryWindowSide.Left;
            int oppositeSpace = SpaceFor(
                opposite,
                leftSpace,
                rightSpace);
            if (oppositeSpace >= requestedWidth
                || oppositeSpace > preferredSpace)
            {
                return opposite;
            }

            return preferred;
        }

        return leftSpace > rightSpace
            ? MemoryWindowSide.Left
            : MemoryWindowSide.Right;
    }

    private static int SpaceFor(
        MemoryWindowSide side,
        int leftSpace,
        int rightSpace) =>
        side switch
        {
            MemoryWindowSide.Left => leftSpace,
            MemoryWindowSide.Right => rightSpace,
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };

    private static int AvailableLength(
        long start,
        long end,
        int maximum) =>
        (int)Math.Clamp(end - start, 0L, maximum);

    private static int ScaleDimension(int dimension, double scale) =>
        Math.Max(
            1,
            (int)Math.Floor(dimension * scale));

    private static int ClampOrigin(
        long requested,
        int availableOrigin,
        int availableLength,
        int itemLength)
    {
        long maximum = (long)availableOrigin
            + availableLength
            - itemLength;
        return checked((int)Math.Clamp(
            requested,
            availableOrigin,
            maximum));
    }

    private static long Right(WindowRectangle rectangle) =>
        (long)rectangle.Left + rectangle.Width;

    private static void Validate(
        WindowRectangle petBounds,
        WindowRectangle workArea,
        int desiredWindowWidth,
        int desiredWindowHeight,
        double gapDips,
        uint dpiX)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(petBounds.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(petBounds.Height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workArea.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workArea.Height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            desiredWindowWidth,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            desiredWindowHeight,
            1);
        if (!double.IsFinite(gapDips) || gapDips < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gapDips),
                "The memory-window gap must be finite and non-negative.");
        }

        ArgumentOutOfRangeException.ThrowIfZero(dpiX);
    }
}
