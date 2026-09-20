namespace GuluPet.Platform;

internal readonly record struct MemoryVideoDimensions
{
    internal MemoryVideoDimensions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
    }

    internal int Width { get; }

    internal int Height { get; }
}

internal readonly record struct MemoryPlaybackWindowSize(
    double WidthDips,
    double HeightDips);

/// <summary>
/// Chooses an orientation-aware memory window size in device-independent
/// pixels. The media keeps its complete native aspect ratio; the small
/// portrait width floor only reserves enough room for the playback controls.
/// </summary>
internal static class MemoryPlaybackWindowSizing
{
    internal const double HorizontalChromeDips = 24;
    internal const double VerticalChromeDips = 36;
    internal const double TargetMediaLongEdgeDips = 504;
    internal const double MinimumPortraitMediaWidthDips = 304;

    internal static MemoryPlaybackWindowSize Calculate(
        int naturalVideoWidth,
        int naturalVideoHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(naturalVideoWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(naturalVideoHeight, 1);

        double scale = TargetMediaLongEdgeDips
            / Math.Max(naturalVideoWidth, naturalVideoHeight);
        double mediaWidth = naturalVideoWidth * scale;
        double mediaHeight = naturalVideoHeight * scale;
        if (naturalVideoHeight > naturalVideoWidth)
        {
            mediaWidth = Math.Max(
                MinimumPortraitMediaWidthDips,
                mediaWidth);
        }

        return new MemoryPlaybackWindowSize(
            mediaWidth + HorizontalChromeDips,
            mediaHeight + VerticalChromeDips);
    }
}
