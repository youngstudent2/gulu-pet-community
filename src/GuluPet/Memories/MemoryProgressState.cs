using System.IO;
using System.Text.Json.Serialization;

namespace GuluPet.Memories;

public sealed record PendingMemoryPlayback
{
    public required string MemoryId { get; init; }

    public DateTimeOffset UnlockedAtUtc { get; init; }
}

public sealed record CompletedMemoryPlayback
{
    public required string MemoryId { get; init; }

    public DateTimeOffset UnlockedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }
}

/// <summary>
/// Durable memory progress. A pending playback is intentionally singular:
/// accepted interactions are not recorded while it exists.
/// </summary>
public sealed record MemoryProgressState
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public long AcceptedInteractionCount { get; init; }

    public PendingMemoryPlayback? Pending { get; init; }

    public IReadOnlyList<CompletedMemoryPlayback> Completed { get; init; } = [];

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public static MemoryProgressState CreateDefault(
        DateTimeOffset? now = null) =>
        new()
        {
            UpdatedAtUtc = now ?? DateTimeOffset.UtcNow,
        };

    internal static MemoryProgressState ValidateAndSnapshot(
        MemoryProgressState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported memory state schema version " +
                $"'{state.SchemaVersion}'; expected " +
                $"'{CurrentSchemaVersion}'.");
        }

        if (state.AcceptedInteractionCount < 0)
        {
            throw new InvalidDataException(
                "Memory accepted interaction count cannot be negative.");
        }

        if (state.UpdatedAtUtc == default)
        {
            throw new InvalidDataException(
                "Memory state must declare updatedAtUtc.");
        }

        IReadOnlyList<CompletedMemoryPlayback> completed =
            state.Completed ?? throw new InvalidDataException(
                "Memory state must declare completed as an array.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var completedSnapshot =
            new CompletedMemoryPlayback[completed.Count];
        for (var index = 0; index < completed.Count; index++)
        {
            CompletedMemoryPlayback record = completed[index]
                ?? throw new InvalidDataException(
                    $"Completed memory record at index {index} is null.");
            ValidateRecordIdentity(record.MemoryId, "completed", index);
            if (!ids.Add(record.MemoryId))
            {
                throw new InvalidDataException(
                    $"Memory '{record.MemoryId}' is completed more than once.");
            }

            if (record.UnlockedAtUtc == default)
            {
                throw new InvalidDataException(
                    $"Completed memory '{record.MemoryId}' has no unlock time.");
            }

            if (record.CompletedAtUtc == default)
            {
                throw new InvalidDataException(
                    $"Completed memory '{record.MemoryId}' has no completion " +
                    "time.");
            }

            if (record.CompletedAtUtc < record.UnlockedAtUtc)
            {
                throw new InvalidDataException(
                    $"Memory '{record.MemoryId}' completed before it was " +
                    "unlocked.");
            }

            if (state.UpdatedAtUtc < record.CompletedAtUtc)
            {
                throw new InvalidDataException(
                    $"Memory state was updated before '{record.MemoryId}' " +
                    "completed.");
            }

            completedSnapshot[index] = record with { };
        }

        PendingMemoryPlayback? pendingSnapshot = null;
        if (state.Pending is { } pending)
        {
            ValidateRecordIdentity(pending.MemoryId, "pending", index: null);
            if (!ids.Add(pending.MemoryId))
            {
                throw new InvalidDataException(
                    $"Memory '{pending.MemoryId}' cannot be both pending and " +
                    "completed.");
            }

            if (pending.UnlockedAtUtc == default)
            {
                throw new InvalidDataException(
                    $"Pending memory '{pending.MemoryId}' has no unlock time.");
            }

            if (state.UpdatedAtUtc < pending.UnlockedAtUtc)
            {
                throw new InvalidDataException(
                    $"Memory state was updated before '{pending.MemoryId}' " +
                    "was unlocked.");
            }

            pendingSnapshot = pending with { };
        }

        return state with
        {
            Pending = pendingSnapshot,
            Completed = completedSnapshot,
        };
    }

    private static void ValidateRecordIdentity(
        string? memoryId,
        string recordKind,
        int? index)
    {
        if (MemoryCatalog.IsValidId(memoryId))
        {
            return;
        }

        string location = index is { } value
            ? $" at index {value}"
            : string.Empty;
        throw new InvalidDataException(
            $"{recordKind} memory record{location} has invalid id " +
            $"'{memoryId}'.");
    }
}
