using System.Diagnostics;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GuluPet.Animation;

public sealed class AnimationPlayer : IDisposable
{
    private static readonly TimeSpan SchedulerInterval = TimeSpan.FromMilliseconds(8);
    private const long MaximumSequentialAdvancesPerTick = 120;

    private readonly AnimationCatalog _catalog;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _playbackClock = new();
    private readonly AnimationTimeline _timeline = new();
    private AnimationClip? _current;
    private int _frameIndex;
    private long _playbackVersion;
    private bool _disposed;

    public AnimationPlayer(AnimationCatalog catalog, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _catalog = catalog;
        _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher);
        _timer.Interval = SchedulerInterval;
        _timer.Tick += OnTick;
    }

    public string? CurrentAction => _current?.Definition.Name;

    public BitmapSource? CurrentFrame =>
        _current is null ? null : _current.Frames[_frameIndex];

    public int CurrentFrameIndex => _current is null ? -1 : _frameIndex;

    public int CurrentFrameCount => _current?.Frames.Count ?? 0;

    public double CurrentFps => _current?.Definition.Fps ?? 0;

    public bool CurrentLoop => _current?.Definition.Loop ?? false;

    public string? CurrentAssetStatus => _current?.Definition.AssetStatus;

    public long CurrentPlaybackToken => _current is null ? 0 : _playbackVersion;

    public event EventHandler<BitmapSource>? FrameChanged;

    public event EventHandler<AnimationFrameEvent>? FrameEventRaised;

    public event EventHandler<AnimationPlaybackEvent>? FirstFramePresented;

    public event EventHandler<AnimationPlaybackEvent>? LoopBoundaryReached;

    public event EventHandler<AnimationPlaybackEvent>? PlaybackCompleted;

    public event EventHandler<string>? AnimationCompleted;

    public bool TryPlay(string clipId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_catalog.TryGet(clipId, out var requested) || requested is null)
        {
            return false;
        }

        var playbackVersion = ++_playbackVersion;
        _current = requested;
        _frameIndex = 0;
        _timeline.Reset(requested.Definition.Fps);
        _playbackClock.Restart();
        PublishCurrentFrame();

        // Event handlers are allowed to synchronously stop or replace playback.
        if (_playbackVersion != playbackVersion || !ReferenceEquals(_current, requested))
        {
            return false;
        }

        FirstFramePresented?.Invoke(
            this,
            new AnimationPlaybackEvent(
                playbackVersion,
                requested.Definition.Name,
                _frameIndex));
        if (_playbackVersion != playbackVersion || !ReferenceEquals(_current, requested))
        {
            return false;
        }

        _timer.Start();
        return true;
    }

    public void Stop()
    {
        _playbackVersion++;
        _timer.Stop();
        _playbackClock.Reset();
        _timeline.Reset(1);
        _current = null;
        _frameIndex = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _playbackVersion++;
        _timer.Stop();
        _playbackClock.Reset();
        _timeline.Reset(1);
        _timer.Tick -= OnTick;
        _current = null;
        _frameIndex = 0;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_current is null)
        {
            _timer.Stop();
            _playbackClock.Reset();
            return;
        }

        var dueSteps = _timeline.ConsumeDueSteps(_playbackClock.Elapsed);
        if (dueSteps == 0)
        {
            return;
        }

        dueSteps = BoundCatchUp(dueSteps);
        var playbackVersion = _playbackVersion;

        for (long step = 0; step < dueSteps; step++)
        {
            if (!AdvanceOneFrame())
            {
                return;
            }

            if (_playbackVersion != playbackVersion)
            {
                return;
            }
        }
    }

    private bool AdvanceOneFrame()
    {
        if (_current is null)
        {
            return false;
        }

        _frameIndex++;
        if (_frameIndex < _current.Frames.Count)
        {
            PublishCurrentFrame();
            return true;
        }

        var completed = _current;
        var playbackVersion = _playbackVersion;

        if (completed.Definition.Loop)
        {
            LoopBoundaryReached?.Invoke(
                this,
                new AnimationPlaybackEvent(
                    playbackVersion,
                    completed.Definition.Name,
                    completed.Frames.Count - 1));
            if (_playbackVersion != playbackVersion || !ReferenceEquals(_current, completed))
            {
                return false;
            }

            _frameIndex = 0;
            PublishCurrentFrame();
            return true;
        }

        PlaybackCompleted?.Invoke(
            this,
            new AnimationPlaybackEvent(
                playbackVersion,
                completed.Definition.Name,
                completed.Frames.Count - 1));
        if (_playbackVersion != playbackVersion || !ReferenceEquals(_current, completed))
        {
            return false;
        }

        AnimationCompleted?.Invoke(this, completed.Definition.Name);
        if (_playbackVersion != playbackVersion || !ReferenceEquals(_current, completed))
        {
            return false;
        }

        _timer.Stop();
        _playbackClock.Stop();
        _frameIndex = completed.Frames.Count - 1;
        return false;
    }

    private long BoundCatchUp(long dueSteps)
    {
        if (_current is null || dueSteps <= MaximumSequentialAdvancesPerTick)
        {
            return dueSteps;
        }

        var skippedSteps = dueSteps - MaximumSequentialAdvancesPerTick;
        if (_current.Definition.Loop)
        {
            var frameCount = _current.Frames.Count;
            var offset = skippedSteps % frameCount;
            _frameIndex = (int)((_frameIndex + offset) % frameCount);
        }
        else
        {
            // Leave completion itself in the bounded sequential tail so that
            // completion semantics are still observed.
            var lastFrameIndex = _current.Frames.Count - 1;
            var skippableSteps = Math.Max(0, lastFrameIndex - _frameIndex);
            _frameIndex += (int)Math.Min(skippedSteps, skippableSteps);
        }

        return MaximumSequentialAdvancesPerTick;
    }

    private void PublishCurrentFrame()
    {
        if (_current is null)
        {
            return;
        }

        var frame = _current.Frames[_frameIndex];
        FrameChanged?.Invoke(this, frame);

        foreach (var frameEvent in _current.Definition.Events.Where(
                     frameEvent => frameEvent.Frame == _frameIndex))
        {
            FrameEventRaised?.Invoke(this, frameEvent);
        }
    }
}

public sealed class AnimationPlaybackEvent(
    long playbackToken,
    string clipId,
    int frameIndex) : EventArgs
{
    public long PlaybackToken { get; } = playbackToken;

    public string ClipId { get; } = clipId;

    public int FrameIndex { get; } = frameIndex;
}
