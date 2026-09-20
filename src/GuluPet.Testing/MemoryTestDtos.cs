namespace GuluPet.Testing;

/// <summary>
/// Wire-only memory diagnostics. This deliberately contains no WPF or
/// memory-domain types so the test client remains portable.
/// </summary>
public sealed class TestMemoryStatus
{
    public DateTimeOffset CapturedAtUtc { get; init; } =
        DateTimeOffset.UtcNow;

    public long AcceptedInteractionCount { get; init; }

    public long AcceptedInteractionsUntilNextUnlock { get; init; }

    public string? PendingMemoryId { get; init; }

    public IReadOnlyList<string> CompletedMemoryIds { get; init; } = [];

    /// <summary>
    /// True while the application-level companion playback operation exists,
    /// including loading, ready, playing, ended-hold, and failed-hold phases.
    /// </summary>
    public bool PlaybackTaskActive { get; init; }

    public string? PlayingMemoryId { get; init; }

    /// <summary>
    /// Loading, ready, playing, ended, failed, or null when no playback
    /// session exists.
    /// </summary>
    public string? PlaybackPhase { get; init; }

    /// <summary>
    /// True while the independent memory playback window is visible.
    /// </summary>
    public bool PresentationActive { get; init; }

    public string? LastPlaybackOutcome { get; init; }

    public string? LastPlaybackError { get; init; }
}
