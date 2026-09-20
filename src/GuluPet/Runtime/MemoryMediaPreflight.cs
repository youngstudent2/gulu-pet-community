using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using GuluPet.Memories;
using GuluPet.Platform;

namespace GuluPet.Runtime;

internal sealed record MemoryMediaPreflightResult(
    bool Succeeded,
    string? ErrorCategory,
    string? UserMessage,
    IReadOnlyList<MemoryVideoDimensions>? VideoDimensions = null)
{
    internal static MemoryMediaPreflightResult Success { get; } =
        new(true, null, null, Array.Empty<MemoryVideoDimensions>());

    internal static MemoryMediaPreflightResult SuccessWithDimensions(
        IReadOnlyList<MemoryVideoDimensions> dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        if (dimensions.Count == 0)
        {
            throw new ArgumentException(
                "At least one decoded video dimension is required.",
                nameof(dimensions));
        }

        return new MemoryMediaPreflightResult(
            true,
            null,
            null,
            dimensions.ToArray());
    }
}

internal enum MemoryMediaPreflightStage
{
    PackageIntegrity,
    Decoder,
    Completed,
}

internal sealed record MemoryMediaPreflightProgress(
    double Fraction,
    MemoryMediaPreflightStage Stage,
    int SourceNumber,
    int SourceCount);

internal interface IMemoryMediaPreflight
{
    Task<MemoryMediaPreflightResult> ValidateAsync(
        IReadOnlyList<Uri> sources,
        CancellationToken cancellationToken);

    Task<MemoryMediaPreflightResult> ValidateAsync(
        IReadOnlyList<Uri> sources,
        IProgress<MemoryMediaPreflightProgress>? progress,
        CancellationToken cancellationToken) =>
        ValidateAsync(sources, cancellationToken);
}

internal sealed class PassThroughMemoryMediaPreflight
    : IMemoryMediaPreflight
{
    public Task<MemoryMediaPreflightResult> ValidateAsync(
        IReadOnlyList<Uri> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MemoryMediaPreflightResult.Success);
    }

    public async Task<MemoryMediaPreflightResult> ValidateAsync(
        IReadOnlyList<Uri> sources,
        IProgress<MemoryMediaPreflightProgress>? progress,
        CancellationToken cancellationToken)
    {
        MemoryMediaPreflightResult result = await ValidateAsync(
            sources,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(
            new MemoryMediaPreflightProgress(
                1,
                MemoryMediaPreflightStage.Completed,
                sources.Count,
                sources.Count));
        return result;
    }
}

internal sealed class WpfMemoryMediaPreflight : IMemoryMediaPreflight
{
    private const double IntegrityFractionPerSource = 0.85;
    private static readonly TimeSpan DecoderTimeout =
        TimeSpan.FromSeconds(5);
    private readonly Dispatcher _dispatcher;
    private readonly MemoryMediaPackageIndex _package;
    private readonly Func<
        Uri,
        CancellationToken,
        Task<MemoryMediaPreflightResult>>? _decoderProbeOverride;

    internal WpfMemoryMediaPreflight(
        Dispatcher dispatcher,
        MemoryMediaPackageIndex package)
    {
        _dispatcher = dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
        _package = package
            ?? throw new ArgumentNullException(nameof(package));
    }

    internal WpfMemoryMediaPreflight(
        MemoryMediaPackageIndex package,
        Func<
            Uri,
            CancellationToken,
            Task<MemoryMediaPreflightResult>> decoderProbe)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _package = package
            ?? throw new ArgumentNullException(nameof(package));
        _decoderProbeOverride = decoderProbe
            ?? throw new ArgumentNullException(nameof(decoderProbe));
    }

    public Task<MemoryMediaPreflightResult> ValidateAsync(
        IReadOnlyList<Uri> sources,
        CancellationToken cancellationToken) =>
        ValidateAsync(sources, progress: null, cancellationToken);

    public async Task<MemoryMediaPreflightResult> ValidateAsync(
        IReadOnlyList<Uri> sources,
        IProgress<MemoryMediaPreflightProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            return Failed(
                "empty-media",
                "找不到要播放的视频，请重新打开这段回忆。");
        }

        long[] sourceSizes = new long[sources.Count];
        try
        {
            for (var index = 0; index < sources.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourceSizes[index] = _package.GetExpectedSizeBytes(
                    sources[index]);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or InvalidDataException)
        {
            return PackageFailure(exception);
        }

        var progressReporter = new MonotonicProgressReporter(
            progress,
            sources.Count);
        var decodedDimensions = new List<MemoryVideoDimensions>(sources.Count);
        var hasCompleteDimensions = true;
        progressReporter.ReportStarted();
        for (var index = 0; index < sources.Count; index++)
        {
            Uri source = sources[index];
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _package.ValidateFileAsync(
                    source,
                    cancellationToken,
                    progressReporter.CreateHashProgress(
                        index,
                        sourceSizes[index]));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is IOException
                      or UnauthorizedAccessException
                      or InvalidDataException)
            {
                return PackageFailure(exception);
            }

            progressReporter.ReportDecoderStarted(index);
            cancellationToken.ThrowIfCancellationRequested();
            MemoryMediaPreflightResult decoderResult;
            try
            {
                decoderResult = await ProbeDecoderCoreAsync(
                    source,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Memory decoder preflight failed unexpectedly: " +
                    exception);
                return Failed(
                    "decoder-failed",
                    "播放器无法打开这段视频，请再试一次。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!decoderResult.Succeeded)
            {
                return decoderResult;
            }

            if (decoderResult.VideoDimensions is { Count: 1 } dimensions)
            {
                decodedDimensions.Add(dimensions[0]);
            }
            else
            {
                hasCompleteDimensions = false;
            }

            if (index + 1 < sources.Count)
            {
                progressReporter.ReportDecoderCompleted(index);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progressReporter.ReportCompleted();
        return hasCompleteDimensions
            && decodedDimensions.Count == sources.Count
                ? MemoryMediaPreflightResult.SuccessWithDimensions(
                    decodedDimensions)
                : MemoryMediaPreflightResult.Success;
    }

    private Task<MemoryMediaPreflightResult> ProbeDecoderCoreAsync(
        Uri source,
        CancellationToken cancellationToken) =>
        _decoderProbeOverride is null
            ? ProbeDecoderAsync(source, cancellationToken)
            : _decoderProbeOverride(source, cancellationToken);

    private Task<MemoryMediaPreflightResult> ProbeDecoderAsync(
        Uri source,
        CancellationToken cancellationToken)
    {
        if (!_dispatcher.CheckAccess())
        {
            return _dispatcher.InvokeAsync(
                    () => ProbeDecoderAsync(source, cancellationToken),
                    DispatcherPriority.Send)
                .Task
                .Unwrap();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var completion =
            new TaskCompletionSource<MemoryMediaPreflightResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new MediaPlayer
        {
            IsMuted = true,
            Volume = 0,
        };
        var timer = new DispatcherTimer(
            DecoderTimeout,
            DispatcherPriority.Background,
            delegate
            {
                completion.TrySetResult(
                    Failed(
                        "decoder-timeout",
                        "播放器检查超时，请再试一次。"));
            },
            _dispatcher);
        EventHandler? opened = null;
        EventHandler<ExceptionEventArgs>? failed = null;
        opened = (_, _) =>
        {
            if (player.NaturalVideoWidth < 1
                || player.NaturalVideoHeight < 1)
            {
                completion.TrySetResult(
                    Failed(
                        "invalid-video",
                        "播放器无法识别这段视频，请再试一次。"));
                return;
            }

            completion.TrySetResult(
                MemoryMediaPreflightResult.SuccessWithDimensions(
                [
                    new MemoryVideoDimensions(
                        player.NaturalVideoWidth,
                        player.NaturalVideoHeight),
                ]));
        };
        failed = (_, eventArgs) =>
        {
            System.Diagnostics.Debug.WriteLine(
                $"Memory decoder preflight failed: {eventArgs.ErrorException}");
            completion.TrySetResult(
                Failed(
                    "decoder-failed",
                    "播放器无法打开这段视频，请再试一次。"));
        };
        player.MediaOpened += opened;
        player.MediaFailed += failed;
        CancellationTokenRegistration cancellation =
            cancellationToken.Register(
                () => _dispatcher.BeginInvoke(
                    DispatcherPriority.Send,
                    () => completion.TrySetCanceled(cancellationToken)));

        try
        {
            timer.Start();
            player.Open(source);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Memory decoder preflight could not start: {exception}");
            completion.TrySetResult(
                Failed(
                    "decoder-open",
                    "播放器无法启动，请再试一次。"));
        }

        return AwaitAndDisposeAsync();

        async Task<MemoryMediaPreflightResult> AwaitAndDisposeAsync()
        {
            try
            {
                return await completion.Task;
            }
            finally
            {
                cancellation.Dispose();
                timer.Stop();
                player.MediaOpened -= opened;
                player.MediaFailed -= failed;
                player.Close();
            }
        }
    }

    private static MemoryMediaPreflightResult Failed(
        string category,
        string message) =>
        new(false, category, message);

    private static MemoryMediaPreflightResult PackageFailure(
        Exception exception)
    {
        System.Diagnostics.Debug.WriteLine(
            $"Memory package preflight failed: {exception}");
        return Failed(
            "package-integrity",
            "视频文件无法读取，请再试一次。");
    }

    private sealed class MonotonicProgressReporter
    {
        private readonly object _gate = new();
        private readonly IProgress<MemoryMediaPreflightProgress>? _progress;
        private readonly int _sourceCount;
        private double _lastFraction;

        internal MonotonicProgressReporter(
            IProgress<MemoryMediaPreflightProgress>? progress,
            int sourceCount)
        {
            _progress = progress;
            _sourceCount = sourceCount;
        }

        internal void ReportStarted() =>
            Report(
                0,
                MemoryMediaPreflightStage.PackageIntegrity,
                sourceNumber: 1);

        internal IProgress<long>? CreateHashProgress(
            int sourceIndex,
            long sourceSize) =>
            _progress is null
                ? null
                : new InlineProgress<long>(bytesHashed =>
                {
                    long boundedBytes = Math.Clamp(
                        bytesHashed,
                        0,
                        sourceSize);
                    double sourceFraction = sourceSize == 0
                        ? 0
                        : (double)boundedBytes / sourceSize;
                    double fraction =
                        (sourceIndex
                         + (IntegrityFractionPerSource * sourceFraction))
                        / _sourceCount;
                    Report(
                        fraction,
                        MemoryMediaPreflightStage.PackageIntegrity,
                        sourceIndex + 1);
                });

        internal void ReportDecoderStarted(int sourceIndex) =>
            Report(
                (sourceIndex + IntegrityFractionPerSource) / _sourceCount,
                MemoryMediaPreflightStage.Decoder,
                sourceIndex + 1);

        internal void ReportDecoderCompleted(int sourceIndex) =>
            Report(
                (sourceIndex + 1d) / _sourceCount,
                MemoryMediaPreflightStage.Decoder,
                sourceIndex + 1);

        internal void ReportCompleted() =>
            Report(
                1,
                MemoryMediaPreflightStage.Completed,
                _sourceCount);

        private void Report(
            double fraction,
            MemoryMediaPreflightStage stage,
            int sourceNumber)
        {
            if (_progress is null)
            {
                return;
            }

            MemoryMediaPreflightProgress update;
            lock (_gate)
            {
                fraction = Math.Clamp(fraction, _lastFraction, 1);
                _lastFraction = fraction;
                update = new MemoryMediaPreflightProgress(
                    fraction,
                    stage,
                    sourceNumber,
                    _sourceCount);
            }

            _progress.Report(update);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
