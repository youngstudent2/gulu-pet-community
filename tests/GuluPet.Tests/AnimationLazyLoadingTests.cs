using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuluPet.Animation;
using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class AnimationLazyLoadingTests
{
    public static void RunAll()
    {
        Run(
            nameof(CatalogLoadDoesNotDecodeFrames),
            CatalogLoadDoesNotDecodeFrames);
        Run(
            nameof(FrameCacheUsesBoundedLeastRecentlyUsedEviction),
            FrameCacheUsesBoundedLeastRecentlyUsedEviction);
        Run(
            nameof(TryPlayPublishesFirstFrameSynchronously),
            TryPlayPublishesFirstFrameSynchronously);
        Run(
            nameof(DecodedFrameRemainsStableAfterCanvasReuse),
            DecodedFrameRemainsStableAfterCanvasReuse);
        Run(
            nameof(DeepValidationDecodesEveryFrame),
            DeepValidationDecodesEveryFrame);
        Run(
            nameof(DeepValidationReportsFrameCountMismatch),
            DeepValidationReportsFrameCountMismatch);
        Run(
            nameof(DeepValidationRejectsOpaqueThirtyTwoBitFrame),
            DeepValidationRejectsOpaqueThirtyTwoBitFrame);
    }

    private static void CatalogLoadDoesNotDecodeFrames()
    {
        using var fixture = Fixture.Create(frameCount: 3, cacheCapacity: 2);

        BehaviorTestCheck.Equal(0L, fixture.Catalog.DecodedFrameCount);
        BehaviorTestCheck.Equal(0, fixture.Catalog.CachedFrameCount);
        BehaviorTestCheck.Equal(3, fixture.Clip.Frames.Count);
        BehaviorTestCheck.Equal(0L, fixture.Catalog.DecodedFrameCount);

        var frame = fixture.Clip.Frames[0];

        BehaviorTestCheck.True(frame.IsFrozen);
        BehaviorTestCheck.Equal(1L, fixture.Catalog.DecodedFrameCount);
        BehaviorTestCheck.Equal(1, fixture.Catalog.CachedFrameCount);
    }

    private static void FrameCacheUsesBoundedLeastRecentlyUsedEviction()
    {
        using var fixture = Fixture.Create(frameCount: 3, cacheCapacity: 2);

        var first = fixture.Clip.Frames[0];
        var second = fixture.Clip.Frames[1];
        var firstAgain = fixture.Clip.Frames[0];

        BehaviorTestCheck.True(ReferenceEquals(first, firstAgain));
        BehaviorTestCheck.Equal(2L, fixture.Catalog.DecodedFrameCount);
        BehaviorTestCheck.Equal(2, fixture.Catalog.CachedFrameCount);

        _ = fixture.Clip.Frames[2];

        BehaviorTestCheck.Equal(3L, fixture.Catalog.DecodedFrameCount);
        BehaviorTestCheck.Equal(2, fixture.Catalog.CachedFrameCount);

        var secondAfterEviction = fixture.Clip.Frames[1];

        BehaviorTestCheck.False(ReferenceEquals(second, secondAfterEviction));
        BehaviorTestCheck.Equal(5L, fixture.Catalog.DecodedFrameCount);
        BehaviorTestCheck.Equal(2, fixture.Catalog.CachedFrameCount);
    }

    private static void TryPlayPublishesFirstFrameSynchronously()
    {
        using var fixture = Fixture.Create(frameCount: 3, cacheCapacity: 2);
        using var player = new AnimationPlayer(
            fixture.Catalog,
            Dispatcher.CurrentDispatcher);
        var events = new List<string>();
        player.FrameChanged += (_, _) => events.Add("frame");
        player.FirstFramePresented += (_, _) => events.Add("first");

        BehaviorTestCheck.True(player.TryPlay(fixture.Clip.Definition.Name));

        BehaviorTestCheck.SequenceEqual(["frame", "first"], events);
        BehaviorTestCheck.Equal(1L, fixture.Catalog.DecodedFrameCount);
        BehaviorTestCheck.True(
            ReferenceEquals(player.CurrentFrame, fixture.Clip.Frames[0]));
        BehaviorTestCheck.Equal(1L, fixture.Catalog.DecodedFrameCount);
    }

    private static void DecodedFrameRemainsStableAfterCanvasReuse()
    {
        using var fixture = Fixture.Create(frameCount: 3, cacheCapacity: 3);

        BitmapSource first = fixture.Clip.Frames[0];
        byte[] before = CopyPixels(first);
        _ = fixture.Clip.Frames[1];
        byte[] after = CopyPixels(first);

        BehaviorTestCheck.SequenceEqual(before, after);
    }

    private static void DeepValidationDecodesEveryFrame()
    {
        using var fixture = Fixture.Create(frameCount: 3, cacheCapacity: 2);

        RuntimeContentContract.ValidateAllAnimationFrames(
            fixture.Catalog,
            expectedFrameSize: 32);

        BehaviorTestCheck.Equal(3L, fixture.Catalog.DecodedFrameCount);
        BehaviorTestCheck.Equal(2, fixture.Catalog.CachedFrameCount);
    }

    private static void DeepValidationReportsFrameCountMismatch()
    {
        using var fixture = Fixture.Create(
            frameCount: 4,
            cacheCapacity: 2);

        try
        {
            RuntimeContentContract.ValidateAllAnimationFrames(
                fixture.Catalog,
                expectedFrameSize: 32);
            throw new InvalidOperationException(
                "A mismatched WebP frame count unexpectedly passed.");
        }
        catch (InvalidDataException exception)
        {
            BehaviorTestCheck.True(
                exception.Message.Contains(
                    "Animated WebP frame count mismatch",
                    StringComparison.Ordinal));
            BehaviorTestCheck.True(
                exception.Message.Contains(
                    "clip.json declares 4, codec reports 3.",
                    StringComparison.Ordinal));
        }
    }

    private static void DeepValidationRejectsOpaqueThirtyTwoBitFrame()
    {
        using var fixture = Fixture.Create(
            frameCount: TestAnimatedWebp.FrameCount,
            cacheCapacity: 1,
            transparentBackground: false);

        try
        {
            RuntimeContentContract.ValidateAllAnimationFrames(
                fixture.Catalog,
                expectedFrameSize: 32);
            throw new InvalidOperationException(
                "An opaque BGRA frame unexpectedly passed alpha validation.");
        }
        catch (InvalidDataException exception)
        {
            BehaviorTestCheck.True(
                exception.Message.Contains(
                    "Animation 'lazy' frame 1",
                    StringComparison.Ordinal));
            BehaviorTestCheck.True(
                exception.Message.Contains(
                    "transparent background and an opaque subject",
                    StringComparison.Ordinal));
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(AnimationLazyLoadingTests)}.{name}");
    }

    private static byte[] CopyPixels(BitmapSource source)
    {
        int stride = checked(source.PixelWidth * 4);
        var pixels = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            string root,
            AnimationCatalog catalog,
            AnimationClip clip)
        {
            Root = root;
            Catalog = catalog;
            Clip = clip;
        }

        public string Root { get; }

        public AnimationCatalog Catalog { get; }

        public AnimationClip Clip { get; }

        public static Fixture Create(
            int frameCount,
            int cacheCapacity,
            bool transparentBackground = true)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                $"GuluPet.AnimationLazy.{Guid.NewGuid():N}");
            string clipDirectory = Path.Combine(root, "lazy");
            Directory.CreateDirectory(clipDirectory);
            try
            {
                TestAnimatedWebp.Write(
                    Path.Combine(clipDirectory, "animation.webp"),
                    transparentBackground);

                File.WriteAllText(
                    Path.Combine(clipDirectory, "clip.json"),
                    JsonSerializer.Serialize(
                        new
                        {
                            name = "lazy",
                            frameCount,
                            fps = 24,
                            loop = false,
                            events = Array.Empty<object>(),
                            placeholder = false,
                            assetStatus = "production",
                            knownIssues = Array.Empty<string>(),
                        }));

                AnimationCatalog catalog = AnimationCatalog.LoadAsync(
                        root,
                        decodePixelWidth: 32,
                        frameCacheCapacity: cacheCapacity)
                    .GetAwaiter()
                    .GetResult();
                return new Fixture(root, catalog, catalog["lazy"]);
            }
            catch
            {
                Directory.Delete(root, recursive: true);
                throw;
            }
        }

        public void Dispose()
        {
            Catalog.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }
}
