using GuluPet.Platform;

namespace GuluPet.Presentation;

public enum MemoryPlaybackPhase
{
    Loading,
    Ready,
    Playing,
    Ended,
    Failed,
    Closed,
}

public sealed record MemoryLoadingProgress(
    double Fraction,
    string StatusText);

internal sealed record MemoryPlaybackPreparationResult(
    bool Succeeded,
    string? UserMessage,
    Exception? Error)
{
    internal static MemoryPlaybackPreparationResult Success { get; } =
        new(true, null, null);
}

internal interface IMemoryPlaybackSession : IDisposable
{
    event EventHandler? PhaseChanged;

    event EventHandler? NaturalPlaybackCompleted;

    event EventHandler? RetryRequested;

    MemoryPlaybackPhase Phase { get; }

    bool IsVisible { get; }

    Task<MemoryPlaybackResult> Completion { get; }

    void ReportLoadingProgress(MemoryLoadingProgress progress);

    void ConfigureVideoDimensions(
        IReadOnlyList<MemoryVideoDimensions> dimensions);

    Task<MemoryPlaybackPreparationResult> PrepareAsync(
        IProgress<MemoryLoadingProgress>? progress,
        CancellationToken cancellationToken);

    void MarkReady();

    void MarkLoadingFailed(string userMessage, Exception? error = null);
}

internal interface IMemoryPlaybackPresenter : IDisposable
{
    IMemoryPlaybackSession Open(
        IReadOnlyList<Uri> videoSources,
        string title,
        CancellationToken cancellationToken);

    void CloseCurrent();
}

internal sealed class WpfMemoryPlaybackPresenter(PetWindow owner)
    : IMemoryPlaybackPresenter
{
    private readonly PetWindow _owner = owner
        ?? throw new ArgumentNullException(nameof(owner));
    private MemoryPlaybackWindow? _window;

    public IMemoryPlaybackSession Open(
        IReadOnlyList<Uri> videoSources,
        string title,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(videoSources);
        if (_window is not null)
        {
            throw new InvalidOperationException(
                "A memory playback window is already open.");
        }

        var window = new MemoryPlaybackWindow(
            _owner,
            videoSources,
            title,
            cancellationToken);
        _window = window;
        window.Closed += OnWindowClosed;
        window.Show();
        window.QueueReposition();
        return window;
    }

    public void CloseCurrent()
    {
        MemoryPlaybackWindow? window = _window;
        if (window is null)
        {
            return;
        }

        if (!window.Dispatcher.CheckAccess())
        {
            window.Dispatcher.Invoke(CloseCurrent);
            return;
        }

        window.Close();
    }

    public void Dispose()
    {
        CloseCurrent();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is MemoryPlaybackWindow window)
        {
            window.Closed -= OnWindowClosed;
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        }
    }
}
