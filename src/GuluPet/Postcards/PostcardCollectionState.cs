using System.IO;
using System.Text.Json.Serialization;

namespace GuluPet.Postcards;

public sealed record PostcardUnlockRecord
{
    public required string PostcardId { get; init; }

    public DateTimeOffset UnlockedAtUtc { get; init; }

    public DateTimeOffset? NotificationAcknowledgedAtUtc { get; init; }
}

/// <summary>
/// Durable notification state for postcards earned before a larger catalog
/// was installed. Keeping the exact ids lets the UI summarize the catalog
/// reconciliation without acknowledging unrelated, real-time unlocks.
/// </summary>
public sealed record PostcardCatalogSummaryRecord
{
    [JsonRequired]
    public IReadOnlyList<string> PostcardIds { get; init; } = [];

    [JsonRequired]
    public int TotalUnlockedCount { get; init; }

    [JsonRequired]
    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>
/// Durable, mergeable feedback for travel stamps earned after the full
/// postcard catalog is complete.
/// </summary>
public sealed record PostcardTravelEchoRecord
{
    [JsonRequired]
    public long NewlyEarnedStampCount { get; init; }

    [JsonRequired]
    public long TotalStampCount { get; init; }

    [JsonRequired]
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public enum PostcardOutingPhase
{
    AtHome,
    Traveling,
    WaitingAtDoor,
    CollectionComplete,
}

/// <summary>
/// One durable thirty-minute outing. <see cref="ArrivedAtUtc"/> latches the
/// completed trip so a later wall-clock rollback cannot move the pet back to
/// the traveling phase.
/// </summary>
public sealed record PostcardOutingRecord
{
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(30);

    [JsonRequired]
    public DateTimeOffset StartedAtUtc { get; init; }

    [JsonRequired]
    public DateTimeOffset ReadyAtUtc { get; init; }

    public DateTimeOffset? ArrivedAtUtc { get; init; }
}

public sealed record PostcardCollectionState
{
    public const int CurrentSchemaVersion = 2;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Retained only so schema-v1 documents can be migrated without silently
    /// discarding their historical counter. It no longer earns postcards.
    /// </summary>
    public long TotalInputCount { get; init; }

    public IReadOnlyList<PostcardUnlockRecord> Unlocked { get; init; } = [];

    public PostcardCatalogSummaryRecord? PendingCatalogSummary { get; init; }

    public PostcardTravelEchoRecord? PendingTravelEcho { get; init; }

    public PostcardOutingRecord? CurrentOuting { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public static PostcardCollectionState CreateDefault(
        DateTimeOffset? now = null) =>
        new()
        {
            UpdatedAtUtc = now ?? DateTimeOffset.UtcNow,
        };

    internal static PostcardCollectionState ValidateAndSnapshot(
        PostcardCollectionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported postcard state schema version " +
                $"'{state.SchemaVersion}'; expected " +
                $"'{CurrentSchemaVersion}'.");
        }

        if (state.TotalInputCount < 0)
        {
            throw new InvalidDataException(
                "Postcard total input count cannot be negative.");
        }

        if (state.UpdatedAtUtc == default)
        {
            throw new InvalidDataException(
                "Postcard state must declare updatedAtUtc.");
        }

        IReadOnlyList<PostcardUnlockRecord> unlocked =
            state.Unlocked ?? throw new InvalidDataException(
                "Postcard state must declare unlocked as an array.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var snapshot = new PostcardUnlockRecord[unlocked.Count];
        for (var index = 0; index < unlocked.Count; index++)
        {
            PostcardUnlockRecord record = unlocked[index]
                ?? throw new InvalidDataException(
                    $"Postcard unlock record at index {index} is null.");
            if (!PostcardCatalog.IsValidId(record.PostcardId))
            {
                throw new InvalidDataException(
                    $"Postcard unlock record at index {index} has invalid " +
                    $"id '{record.PostcardId}'.");
            }

            if (!ids.Add(record.PostcardId))
            {
                throw new InvalidDataException(
                    $"Postcard '{record.PostcardId}' is unlocked more than once.");
            }

            if (record.UnlockedAtUtc == default)
            {
                throw new InvalidDataException(
                    $"Postcard '{record.PostcardId}' has no unlock time.");
            }

            if (record.NotificationAcknowledgedAtUtc is { } acknowledgedAt
                && acknowledgedAt < record.UnlockedAtUtc)
            {
                throw new InvalidDataException(
                    $"Postcard '{record.PostcardId}' was acknowledged before " +
                    "it was unlocked.");
            }

            snapshot[index] = record with { };
        }

        PostcardCatalogSummaryRecord? pendingSummary =
            ValidatePendingCatalogSummary(
                state.PendingCatalogSummary,
                snapshot);
        PostcardTravelEchoRecord? pendingTravelEcho =
            ValidatePendingTravelEcho(state.PendingTravelEcho);
        PostcardOutingRecord? currentOuting = ValidateCurrentOuting(
            state.CurrentOuting,
            state.UpdatedAtUtc);
        return state with
        {
            Unlocked = snapshot,
            PendingCatalogSummary = pendingSummary,
            PendingTravelEcho = pendingTravelEcho,
            CurrentOuting = currentOuting,
        };
    }

    private static PostcardOutingRecord? ValidateCurrentOuting(
        PostcardOutingRecord? outing,
        DateTimeOffset updatedAtUtc)
    {
        if (outing is null)
        {
            return null;
        }

        if (outing.StartedAtUtc == default || outing.ReadyAtUtc == default)
        {
            throw new InvalidDataException(
                "A postcard outing must declare start and ready times.");
        }

        if (outing.ReadyAtUtc - outing.StartedAtUtc
            != PostcardOutingRecord.Duration)
        {
            throw new InvalidDataException(
                "A postcard outing must last exactly thirty minutes.");
        }

        if (outing.ArrivedAtUtc is { } arrivedAtUtc
            && arrivedAtUtc < outing.ReadyAtUtc)
        {
            throw new InvalidDataException(
                "A postcard outing cannot arrive before its ready time.");
        }

        DateTimeOffset latestOutingTime =
            outing.ArrivedAtUtc ?? outing.StartedAtUtc;
        if (updatedAtUtc < latestOutingTime)
        {
            throw new InvalidDataException(
                "Postcard state cannot predate its current outing.");
        }

        return outing with { };
    }

    private static PostcardTravelEchoRecord? ValidatePendingTravelEcho(
        PostcardTravelEchoRecord? echo)
    {
        if (echo is null)
        {
            return null;
        }

        if (echo.NewlyEarnedStampCount <= 0
            || echo.TotalStampCount <= 0
            || echo.NewlyEarnedStampCount > echo.TotalStampCount)
        {
            throw new InvalidDataException(
                "A pending travel echo has invalid new or total stamp counts.");
        }

        if (echo.CreatedAtUtc == default)
        {
            throw new InvalidDataException(
                "A pending travel echo must declare createdAtUtc.");
        }

        return echo with { };
    }

    private static PostcardCatalogSummaryRecord?
        ValidatePendingCatalogSummary(
            PostcardCatalogSummaryRecord? summary,
            IReadOnlyList<PostcardUnlockRecord> unlocked)
    {
        if (summary is null)
        {
            return null;
        }

        IReadOnlyList<string> postcardIds = summary.PostcardIds
            ?? throw new InvalidDataException(
                "A pending postcard catalog summary must declare postcardIds.");
        if (postcardIds.Count == 0)
        {
            throw new InvalidDataException(
                "A pending postcard catalog summary cannot be empty.");
        }

        if (summary.TotalUnlockedCount <= 0
            || summary.TotalUnlockedCount < postcardIds.Count)
        {
            throw new InvalidDataException(
                "A pending postcard catalog summary has an invalid total " +
                "unlocked count.");
        }

        if (summary.CreatedAtUtc == default)
        {
            throw new InvalidDataException(
                "A pending postcard catalog summary must declare createdAtUtc.");
        }

        IReadOnlyDictionary<string, PostcardUnlockRecord> unlockedById =
            unlocked.ToDictionary(
                static record => record.PostcardId,
                StringComparer.Ordinal);
        var summaryIds = new HashSet<string>(StringComparer.Ordinal);
        var snapshot = new string[postcardIds.Count];
        for (var index = 0; index < postcardIds.Count; index++)
        {
            string postcardId = postcardIds[index];
            if (!PostcardCatalog.IsValidId(postcardId)
                || !summaryIds.Add(postcardId))
            {
                throw new InvalidDataException(
                    $"Pending postcard catalog summary item at index {index} " +
                    $"has invalid or duplicated id '{postcardId}'.");
            }

            if (!unlockedById.TryGetValue(
                    postcardId,
                    out PostcardUnlockRecord? record))
            {
                throw new InvalidDataException(
                    $"Pending postcard catalog summary item '{postcardId}' " +
                    "is not unlocked.");
            }

            if (record.NotificationAcknowledgedAtUtc is not null)
            {
                throw new InvalidDataException(
                    $"Pending postcard catalog summary item '{postcardId}' " +
                    "is already acknowledged.");
            }

            if (summary.CreatedAtUtc < record.UnlockedAtUtc)
            {
                throw new InvalidDataException(
                    $"Pending postcard catalog summary item '{postcardId}' " +
                    "was unlocked after the summary was created.");
            }

            snapshot[index] = postcardId;
        }

        return summary with
        {
            PostcardIds = snapshot,
        };
    }
}
