namespace GuluPet.Memories;

/// <summary>
/// One ordered memory that becomes eligible after a fixed number of accepted
/// interactions. <see cref="VideoFileNames"/> contains one or more stable
/// packaged file names in playback order; presentation code resolves them
/// beneath the memory assets directory. <see cref="ThumbnailFileName"/> is a
/// static preview used before playback so unlocking a memory never forces an
/// immediate video transition. <see cref="Description"/> is an objective
/// summary of the packaged video content for diary-generation facts; it is
/// intentionally separate from the short, user-facing <see cref="Title"/>.
/// </summary>
public sealed record MemoryDefinition(
    string Id,
    int SequenceNumber,
    long UnlockAtAcceptedInteractionCount,
    IReadOnlyList<string> VideoFileNames,
    string Title,
    string Description,
    string ThumbnailFileName)
{
    public MemoryDefinition(
        string id,
        int sequenceNumber,
        long unlockAtAcceptedInteractionCount,
        IReadOnlyList<string> videoFileNames)
        : this(
            id,
            sequenceNumber,
            unlockAtAcceptedInteractionCount,
            videoFileNames,
            $"回忆 {sequenceNumber:00}",
            $"咕噜的第 {sequenceNumber:00} 段回忆视频。",
            $"{id}.jpg")
    {
    }

    public MemoryDefinition(
        string id,
        int sequenceNumber,
        long unlockAtAcceptedInteractionCount,
        string videoFileName)
        : this(
            id,
            sequenceNumber,
            unlockAtAcceptedInteractionCount,
            [videoFileName],
            $"回忆 {sequenceNumber:00}",
            $"咕噜的第 {sequenceNumber:00} 段回忆视频。",
            $"{id}.jpg")
    {
    }

    public MemoryDefinition(
        string id,
        int sequenceNumber,
        long unlockAtAcceptedInteractionCount,
        IReadOnlyList<string> videoFileNames,
        string title,
        string thumbnailFileName)
        : this(
            id,
            sequenceNumber,
            unlockAtAcceptedInteractionCount,
            videoFileNames,
            title,
            $"{title}的视频片段。",
            thumbnailFileName)
    {
    }
}
