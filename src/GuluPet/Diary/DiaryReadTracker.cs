namespace GuluPet.Diary;

/// <summary>
/// Immutable result of reconciling user-local diary read state.
/// </summary>
public sealed record DiaryReadTrackingState(
    bool IsInitialized,
    IReadOnlyList<DateOnly> ReadDates);

/// <summary>
/// Pure diary-read bookkeeping. Persistence remains the caller's concern.
/// </summary>
public static class DiaryReadTracker
{
    /// <summary>
    /// Reconciles persisted state with the current diary inventory. On the
    /// first upgrade, every historical page except the latest is treated as
    /// read, leaving at most one unread page.
    /// </summary>
    public static DiaryReadTrackingState Initialize(
        bool isInitialized,
        IEnumerable<DateOnly>? readDates,
        IEnumerable<DateOnly> diaryDates)
    {
        ArgumentNullException.ThrowIfNull(diaryDates);

        DateOnly[] normalizedReadDates = Normalize(readDates);
        if (isInitialized)
        {
            return new DiaryReadTrackingState(true, normalizedReadDates);
        }

        DateOnly[] existingDiaryDates = diaryDates
            .Distinct()
            .Order()
            .ToArray();
        if (existingDiaryDates.Length > 0)
        {
            DateOnly latestDiaryDate = existingDiaryDates[^1];
            normalizedReadDates = Normalize(
                normalizedReadDates
                .Where(date => date != latestDiaryDate)
                .Concat(existingDiaryDates[..^1]));
        }

        return new DiaryReadTrackingState(true, normalizedReadDates);
    }

    public static DiaryReadTrackingState MarkRead(
        DiaryReadTrackingState state,
        DateOnly displayedDate)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new DiaryReadTrackingState(
            true,
            Normalize((state.ReadDates ?? []).Append(displayedDate)));
    }

    public static int GetUnreadCount(
        DiaryReadTrackingState state,
        IEnumerable<DateOnly> diaryDates)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(diaryDates);

        var readDates = new HashSet<DateOnly>(state.ReadDates ?? []);
        return diaryDates
            .Distinct()
            .Count(date => !readDates.Contains(date));
    }

    public static bool IsUnread(
        DiaryReadTrackingState state,
        DateOnly diaryDate)
    {
        ArgumentNullException.ThrowIfNull(state);
        return !(state.ReadDates ?? []).Contains(diaryDate);
    }

    private static DateOnly[] Normalize(IEnumerable<DateOnly>? dates) =>
        (dates ?? [])
        .Distinct()
        .Order()
        .ToArray();
}
