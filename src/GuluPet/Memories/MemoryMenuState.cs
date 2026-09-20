using System.Collections.ObjectModel;
using System.IO;

namespace GuluPet.Memories;

public sealed class MemoryReplayRequestedEventArgs : EventArgs
{
    public MemoryReplayRequestedEventArgs(string memoryId)
    {
        if (!MemoryCatalog.IsValidId(memoryId))
        {
            throw new ArgumentException(
                "A valid memory id is required.",
                nameof(memoryId));
        }

        MemoryId = memoryId;
    }

    public string MemoryId { get; }
}

internal sealed record MemoryMenuEntry(
    string MemoryId,
    string Header,
    bool IsEnabled,
    bool IsPending);

internal sealed record MemoryMenuState(
    string Header,
    IReadOnlyList<MemoryMenuEntry> Entries)
{
    internal static MemoryMenuState Create(
        MemoryCatalog catalog,
        IReadOnlyList<string> completedMemoryIds,
        bool playbackBusy) =>
        Create(
            catalog,
            completedMemoryIds,
            pendingMemoryId: null,
            playbackBusy,
            failedMemoryId: null);

    internal static MemoryMenuState Create(
        MemoryCatalog catalog,
        IReadOnlyList<string> completedMemoryIds,
        string? pendingMemoryId,
        bool playbackBusy,
        string? failedMemoryId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(completedMemoryIds);

        var completed = new HashSet<string>(
            completedMemoryIds,
            StringComparer.Ordinal);
        if (completed.Count != completedMemoryIds.Count)
        {
            throw new InvalidDataException(
                "Completed memories cannot contain duplicate ids.");
        }

        var entries = catalog.Definitions
            .Where(definition => completed.Contains(definition.Id))
            .Select(definition =>
                new MemoryMenuEntry(
                    definition.Id,
                    string.Equals(
                        failedMemoryId,
                        definition.Id,
                        StringComparison.Ordinal)
                            ? $"再看一次 · {definition.Title}"
                            : definition.Title,
                    IsEnabled: !playbackBusy,
                    IsPending: false))
            .ToList();
        if (entries.Count != completed.Count)
        {
            throw new InvalidDataException(
                "Completed memories must exist in the packaged catalog.");
        }

        if (pendingMemoryId is not null)
        {
            if (!catalog.TryGet(
                    pendingMemoryId,
                    out MemoryDefinition? pending)
                || pending is null
                || completed.Contains(pendingMemoryId))
            {
                throw new InvalidDataException(
                    "A pending memory must exist in the catalog and cannot " +
                    "already be completed.");
            }

            entries.Add(
                new MemoryMenuEntry(
                    pending.Id,
                    string.Equals(
                        failedMemoryId,
                        pending.Id,
                        StringComparison.Ordinal)
                            ? $"再试一次 · {pending.Title}"
                            : $"等妈咪来看 · {pending.Title}",
                    IsEnabled: !playbackBusy,
                    IsPending: true));
        }

        string pendingSuffix = pendingMemoryId is null
            ? string.Empty
            : "，还有 1 段等妈咪";
        return new MemoryMenuState(
            $"咕噜的回忆 · 已收好 {completed.Count} 段{pendingSuffix}",
            new ReadOnlyCollection<MemoryMenuEntry>(entries));
    }
}
