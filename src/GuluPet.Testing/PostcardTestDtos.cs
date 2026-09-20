namespace GuluPet.Testing;

/// <summary>
/// Wire-only postcard diagnostics. This deliberately contains no WPF or
/// postcard-domain types so the test client remains portable.
/// </summary>
public sealed class TestPostcardStatus
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string OutingPhase { get; init; } = "atHome";

    public DateTimeOffset? OutingStartedAtUtc { get; init; }

    public DateTimeOffset? OutingReadyAtUtc { get; init; }

    public DateTimeOffset? OutingArrivedAtUtc { get; init; }

    public long OutingRemainingSeconds { get; init; }

    public string? NextPostcardId { get; init; }

    public int AvailablePostcardCount { get; init; }

    public IReadOnlyList<string> UnlockedPostcardIds { get; init; } = [];

    public IReadOnlyList<string> UnacknowledgedPostcardIds { get; init; } = [];

    public bool PopupVisible { get; init; }

    public string? PopupPostcardId { get; init; }

    public bool GalleryVisible { get; init; }

    public string? GalleryPostcardId { get; init; }
}
