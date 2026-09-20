using System.Collections;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace GuluPet.Animation;

internal sealed class AnimatedWebpFrameList : IReadOnlyList<BitmapSource>
{
    private readonly string _path;
    private readonly AnimationFrameCache _cache;

    public AnimatedWebpFrameList(
        string path,
        int frameCount,
        AnimationFrameCache cache)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 1);

        _path = Path.GetFullPath(path);
        Count = frameCount;
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public int Count { get; }

    public BitmapSource this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _cache.Get(_path, index, Count);
        }
    }

    public IEnumerator<BitmapSource> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class AnimationFrameCache : IDisposable
{
    private sealed record Entry(
        BitmapSource Frame,
        LinkedListNode<string> Recency);

    private readonly object _gate = new();
    private readonly int _decodePixelWidth;
    private readonly Dictionary<string, Entry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recency = [];
    private WebpDecodeSession? _decodeSession;
    private long _decodedFrameCount;
    private bool _disposed;

    public AnimationFrameCache(int capacity, int decodePixelWidth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(decodePixelWidth);
        Capacity = capacity;
        _decodePixelWidth = decodePixelWidth;
    }

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long DecodedFrameCount
    {
        get
        {
            lock (_gate)
            {
                return _decodedFrameCount;
            }
        }
    }

    public BitmapSource Get(
        string path,
        int frameIndex,
        int expectedFrameCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedFrameCount, 1);

        string fullPath = Path.GetFullPath(path);
        string requestedKey = CreateKey(fullPath, frameIndex);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(requestedKey, out Entry? cached))
            {
                Touch(cached);
                return cached.Frame;
            }

            EnsureDecodeSession(fullPath, expectedFrameCount, frameIndex);
            while (_decodeSession!.LastDecodedFrame < frameIndex)
            {
                int nextIndex = _decodeSession.LastDecodedFrame + 1;
                BitmapSource frame = _decodeSession.DecodeNext(
                    _decodePixelWidth);
                Add(CreateKey(fullPath, nextIndex), frame);
                _decodedFrameCount++;
            }

            if (!_entries.TryGetValue(requestedKey, out cached))
            {
                throw new InvalidOperationException(
                    $"Decoded WebP frame {frameIndex} was not retained in the cache. " +
                    $"Increase the cache above the largest supported forward seek.");
            }

            Touch(cached);
            return cached.Frame;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _decodeSession?.Dispose();
            _decodeSession = null;
            _entries.Clear();
            _recency.Clear();
        }
    }

    private static string CreateKey(string path, int frameIndex) =>
        string.Concat(
            path,
            "\u001F",
            frameIndex.ToString(CultureInfo.InvariantCulture));

    private void EnsureDecodeSession(
        string path,
        int expectedFrameCount,
        int requestedFrame)
    {
        bool wrongClip = _decodeSession is null ||
                         !string.Equals(
                             _decodeSession.Path,
                             path,
                             StringComparison.OrdinalIgnoreCase);
        bool requiresRewind = !wrongClip &&
                              requestedFrame <= _decodeSession!.LastDecodedFrame;
        if (!wrongClip && !requiresRewind)
        {
            return;
        }

        _decodeSession?.Dispose();
        _decodeSession = new WebpDecodeSession(path, expectedFrameCount);
    }

    private void Add(string key, BitmapSource frame)
    {
        if (_entries.TryGetValue(key, out Entry? existing))
        {
            Touch(existing);
            return;
        }

        var node = _recency.AddFirst(key);
        _entries.Add(key, new Entry(frame, node));
        EvictOverflow();
    }

    private void Touch(Entry entry)
    {
        _recency.Remove(entry.Recency);
        _recency.AddFirst(entry.Recency);
    }

    private void EvictOverflow()
    {
        while (_entries.Count > Capacity)
        {
            LinkedListNode<string>? oldest = _recency.Last;
            if (oldest is null)
            {
                throw new InvalidOperationException(
                    "Animation frame cache recency index is empty.");
            }

            _recency.RemoveLast();
            if (!_entries.Remove(oldest.Value))
            {
                throw new InvalidOperationException(
                    "Animation frame cache indexes are inconsistent.");
            }
        }
    }
}

internal sealed class WebpDecodeSession : IDisposable
{
    private readonly SKData _data;
    private readonly SKCodec _codec;
    private readonly SKBitmap _canvas;
    private bool _disposed;

    public WebpDecodeSession(string path, int expectedFrameCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedFrameCount, 1);

        Path = System.IO.Path.GetFullPath(path);
        try
        {
            byte[] bytes = File.ReadAllBytes(Path);
            _data = SKData.CreateCopy(bytes)
                ?? throw new InvalidDataException(
                    $"Could not allocate WebP data: {Path}");
            _codec = SKCodec.Create(_data)
                ?? throw new InvalidDataException(
                    $"Could not decode animated WebP: {Path}");
        }
        catch
        {
            _codec?.Dispose();
            _data?.Dispose();
            throw;
        }

        if (_codec.FrameCount != expectedFrameCount)
        {
            int actualFrameCount = _codec.FrameCount;
            _codec.Dispose();
            _data.Dispose();
            throw new InvalidDataException(
                $"Animated WebP frame count mismatch for '{Path}': " +
                $"clip.json declares {expectedFrameCount}, codec reports " +
                $"{actualFrameCount}.");
        }

        if (_codec.Info.Width < 1 || _codec.Info.Height < 1)
        {
            _codec.Dispose();
            _data.Dispose();
            throw new InvalidDataException(
                $"Animated WebP has invalid dimensions: {Path}");
        }

        var canvasInfo = new SKImageInfo(
            _codec.Info.Width,
            _codec.Info.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        _canvas = new SKBitmap(canvasInfo);
        _canvas.Erase(SKColors.Transparent);
    }

    public string Path { get; }

    public int LastDecodedFrame { get; private set; } = -1;

    public BitmapSource DecodeNext(int decodePixelWidth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(decodePixelWidth);

        int frameIndex = LastDecodedFrame + 1;
        if (frameIndex >= _codec.FrameCount)
        {
            throw new InvalidOperationException(
                $"Animated WebP has no frame {frameIndex}: {Path}");
        }

        var options = new SKCodecOptions(frameIndex, LastDecodedFrame);
        SKCodecResult result = _codec.GetPixels(
            _canvas.Info,
            _canvas.GetPixels(),
            _canvas.RowBytes,
            options);
        if (result != SKCodecResult.Success)
        {
            throw new InvalidDataException(
                $"Animated WebP frame {frameIndex} failed to decode " +
                $"with result '{result}': {Path}");
        }

        LastDecodedFrame = frameIndex;
        return CreateFrozenBitmapSource(_canvas, decodePixelWidth);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _canvas.Dispose();
        _codec.Dispose();
        _data.Dispose();
    }

    private static BitmapSource CreateFrozenBitmapSource(
        SKBitmap bitmap,
        int decodePixelWidth)
    {
        int stride = bitmap.RowBytes;
        var pixels = new byte[checked(stride * bitmap.Height)];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);

        BitmapSource source = BitmapSource.Create(
            bitmap.Width,
            bitmap.Height,
            96,
            96,
            PixelFormats.Pbgra32,
            palette: null,
            pixels,
            stride);
        source.Freeze();

        if (decodePixelWidth == 0 || decodePixelWidth >= bitmap.Width)
        {
            return source;
        }

        double scale = (double)decodePixelWidth / bitmap.Width;
        var resized = new TransformedBitmap(
            source,
            new ScaleTransform(scale, scale));
        resized.Freeze();
        return resized;
    }
}
