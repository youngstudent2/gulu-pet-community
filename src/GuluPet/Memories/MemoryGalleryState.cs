using System.Collections.ObjectModel;
using System.IO;

namespace GuluPet.Memories;

internal enum MemoryGalleryEntryKind
{
    Completed,
    Pending,
}

/// <summary>
/// A single visible card in the memory gallery. Gallery entries are projected
/// only from durable completed or pending progress; locked catalog definitions
/// are deliberately never represented here.
/// </summary>
internal sealed record MemoryGalleryEntry(
    string MemoryId,
    int SequenceNumber,
    string Title,
    string Description,
    string ThumbnailPath,
    MemoryGalleryEntryKind Kind,
    bool IsEnabled,
    bool IsPlaying,
    bool HasFailed)
{
    public bool IsPending => Kind == MemoryGalleryEntryKind.Pending;

    public string SequenceLabel => $"回忆 {SequenceNumber:00}";

    public string StatusLabel => IsPending ? "新回忆" : "已收好";

    public string ActionLabel =>
        HasFailed
            ? IsPlaying
                ? "打开失败"
                : "再试一次"
            : IsPlaying
                ? "播放中"
                : IsPending
                    ? "看看"
                    : "重温";

    public string AutomationName => $"{ActionLabel}「{Title}」";

    public string AutomationHelpText =>
        IsPending
            ? "播放这段刚刚解锁的回忆"
            : "重新播放这段已经看过的回忆";
}

/// <summary>
/// Immutable, user-visible projection of memory progress for the gallery.
/// </summary>
internal sealed record MemoryGalleryState(
    IReadOnlyList<MemoryGalleryEntry> Entries,
    int CompletedCount,
    bool HasPending,
    bool IsBusy,
    string SummaryText,
    string StatusText)
{
    internal static MemoryGalleryState Empty { get; } = new(
        Array.Empty<MemoryGalleryEntry>(),
        CompletedCount: 0,
        HasPending: false,
        IsBusy: false,
        SummaryText: "还没有回忆",
        StatusText: "往后的日子里，会慢慢收好更多回忆");

    internal static MemoryGalleryState Create(
        MemoryCatalog catalog,
        MemoryProgressState progress,
        string thumbnailDirectory,
        bool playbackBusy,
        string? playingMemoryId = null,
        string? failedMemoryId = null,
        string? failureMessage = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(progress);
        if (string.IsNullOrWhiteSpace(thumbnailDirectory))
        {
            throw new ArgumentException(
                "A thumbnail directory is required.",
                nameof(thumbnailDirectory));
        }

        MemoryProgressState snapshot =
            MemoryProgressState.ValidateAndSnapshot(progress);
        var completedIds = new HashSet<string>(
            snapshot.Completed.Select(static item => item.MemoryId),
            StringComparer.Ordinal);
        if (completedIds.Count != snapshot.Completed.Count)
        {
            throw new InvalidDataException(
                "Completed memories cannot contain duplicate ids.");
        }

        string? pendingMemoryId = snapshot.Pending?.MemoryId;
        if (pendingMemoryId is not null && completedIds.Contains(pendingMemoryId))
        {
            throw new InvalidDataException(
                "A memory cannot be both pending and completed.");
        }

        var visibleIds = new HashSet<string>(completedIds, StringComparer.Ordinal);
        if (pendingMemoryId is not null)
        {
            visibleIds.Add(pendingMemoryId);
        }

        MemoryDefinition[] visibleDefinitions = catalog.Definitions
            .Where(definition => visibleIds.Contains(definition.Id))
            .ToArray();
        if (visibleDefinitions.Length != visibleIds.Count)
        {
            throw new InvalidDataException(
                "Visible memories must exist in the packaged catalog.");
        }

        ValidateFeedbackIdentity(
            playingMemoryId,
            visibleIds,
            nameof(playingMemoryId));
        ValidateFeedbackIdentity(
            failedMemoryId,
            visibleIds,
            nameof(failedMemoryId));
        if (!playbackBusy && playingMemoryId is not null)
        {
            throw new InvalidDataException(
                "A playing memory requires a busy playback state.");
        }

        string resolvedThumbnailDirectory =
            Path.GetFullPath(thumbnailDirectory);
        var entries = visibleDefinitions
            .Select(definition =>
                new MemoryGalleryEntry(
                    definition.Id,
                    definition.SequenceNumber,
                    definition.Title,
                    definition.Description,
                    Path.Combine(
                        resolvedThumbnailDirectory,
                        definition.ThumbnailFileName),
                    string.Equals(
                        definition.Id,
                        pendingMemoryId,
                        StringComparison.Ordinal)
                            ? MemoryGalleryEntryKind.Pending
                            : MemoryGalleryEntryKind.Completed,
                    IsEnabled: !playbackBusy,
                    IsPlaying: string.Equals(
                        definition.Id,
                        playingMemoryId,
                        StringComparison.Ordinal),
                    HasFailed: string.Equals(
                        definition.Id,
                        failedMemoryId,
                        StringComparison.Ordinal)))
            .ToArray();

        MemoryGalleryEntry? playingEntry = entries.FirstOrDefault(
            static entry => entry.IsPlaying);
        MemoryGalleryEntry? failedEntry = entries.FirstOrDefault(
            static entry => entry.HasFailed);
        string statusText = BuildStatusText(
            entries,
            playbackBusy,
            playingEntry,
            failedEntry,
            failureMessage);
        string summaryText = BuildSummaryText(
            completedIds.Count,
            pendingMemoryId is not null);

        return new MemoryGalleryState(
            new ReadOnlyCollection<MemoryGalleryEntry>(entries),
            completedIds.Count,
            pendingMemoryId is not null,
            playbackBusy,
            summaryText,
            statusText);
    }

    private static void ValidateFeedbackIdentity(
        string? memoryId,
        IReadOnlySet<string> visibleIds,
        string parameterName)
    {
        if (memoryId is null || visibleIds.Contains(memoryId))
        {
            return;
        }

        throw new InvalidDataException(
            $"{parameterName} must refer to a completed or pending memory.");
    }

    private static string BuildSummaryText(
        int completedCount,
        bool hasPending)
    {
        if (completedCount == 0 && !hasPending)
        {
            return "还没有回忆";
        }

        if (completedCount == 0)
        {
            return "新回忆 1 段";
        }

        return hasPending
            ? $"已收好 {completedCount} 段 · 新回忆 1 段"
            : $"已收好 {completedCount} 段";
    }

    private static string BuildStatusText(
        IReadOnlyList<MemoryGalleryEntry> entries,
        bool playbackBusy,
        MemoryGalleryEntry? playingEntry,
        MemoryGalleryEntry? failedEntry,
        string? failureMessage)
    {
        if (failedEntry is not null)
        {
            return string.IsNullOrWhiteSpace(failureMessage)
                ? $"「{failedEntry.Title}」刚刚没能打开，再试一次吧"
                : failureMessage.Trim();
        }

        if (playingEntry is not null)
        {
            return $"正在播放「{playingEntry.Title}」";
        }

        if (playbackBusy)
        {
            return "咕噜正在整理回忆，等一下呀";
        }

        if (entries.Any(static entry => entry.IsPending))
        {
            return "有一段新回忆在等妈咪";
        }

        return entries.Count == 0
            ? "往后的日子里，会慢慢收好更多回忆"
            : "挑一段回忆，再陪咕噜看一遍";
    }
}
