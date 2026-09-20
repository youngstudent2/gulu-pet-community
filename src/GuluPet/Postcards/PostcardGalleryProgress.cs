namespace GuluPet.Postcards;

/// <summary>Text and values shared by the gallery's collection progress UI.</summary>
public sealed record PostcardGalleryProgress(
    string Label,
    double Progress,
    double Maximum,
    string Detail,
    string Footer,
    bool IsComplete);

public static class PostcardGalleryProgressFormatter
{
    public static PostcardGalleryProgress Create(
        PostcardOutingStatus outing,
        int unlockedCount,
        int availableCount)
    {
        ArgumentNullException.ThrowIfNull(outing);
        ArgumentOutOfRangeException.ThrowIfNegative(unlockedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(availableCount);
        if (unlockedCount > availableCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unlockedCount),
                "Unlocked postcards cannot exceed the catalog.");
        }

        double maximum = PostcardOutingRecord.Duration.TotalSeconds;
        return outing.Phase switch
        {
            PostcardOutingPhase.AtHome => new(
                "下一封来信",
                Progress: 0,
                maximum,
                "行囊收好啦 · 大约半小时后回家",
                "摸摸咕噜，再点一下「去逛逛」，她会带一张明信片回来",
                IsComplete: false),
            PostcardOutingPhase.Traveling => new(
                "咕噜在外面逛逛",
                Math.Clamp(
                    maximum - Math.Ceiling(outing.Remaining.TotalSeconds),
                    0,
                    maximum),
                maximum,
                $"咕噜回家的脚步还有 {FormatRemaining(outing.Remaining)}",
                "她正在外面逛逛，回家后会在门口等妈咪",
                IsComplete: false),
            PostcardOutingPhase.WaitingAtDoor => new(
                "咕噜回到家啦",
                maximum,
                maximum,
                "明信片等妈咪收下",
                "点一下咕噜身边的「在家门口」，把新明信片收好",
                IsComplete: false),
            PostcardOutingPhase.CollectionComplete => new(
                "一路风景都收好啦",
                maximum,
                maximum,
                $"{availableCount:N0} 张明信片都在这里",
                "咕噜带回的每一张风景，都和妈咪好好收在一起",
                IsComplete: true),
            _ => throw new InvalidOperationException(
                $"Unknown postcard outing phase '{outing.Phase}'."),
        };
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        double seconds = Math.Clamp(
            Math.Ceiling(remaining.TotalSeconds),
            0,
            PostcardOutingRecord.Duration.TotalSeconds);
        int totalSeconds = checked((int)seconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }
}

public sealed record PostcardChapterProgress(
    int ChapterNumber,
    string ChapterName,
    int UnlockedInChapter,
    int CardsPerChapter,
    string Label,
    string NextChapterFeedback,
    bool IsCollectionComplete);

/// <summary>Formats progressive twenty-card travel chapters.</summary>
public static class PostcardChapterProgressFormatter
{
    public const int CardsPerChapter = 20;

    public static bool TryGetChapterCount(
        IReadOnlyList<PostcardDefinition> catalog,
        out int chapterCount)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        chapterCount = 0;
        if (catalog.Count == 0 || catalog.Count % CardsPerChapter != 0)
        {
            return false;
        }

        var chapterNames = new HashSet<string>(StringComparer.Ordinal);
        int candidateCount = catalog.Count / CardsPerChapter;
        for (var chapterIndex = 0;
             chapterIndex < candidateCount;
             chapterIndex++)
        {
            int chapterStart = chapterIndex * CardsPerChapter;
            string chapterName = catalog[chapterStart].Chapter;
            if (string.IsNullOrWhiteSpace(chapterName)
                || !chapterNames.Add(chapterName))
            {
                return false;
            }

            for (var offset = 0; offset < CardsPerChapter; offset++)
            {
                PostcardDefinition postcard = catalog[chapterStart + offset];
                if (postcard.UnlockOrder != chapterStart + offset + 1
                    || !string.Equals(
                        postcard.Chapter,
                        chapterName,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        chapterCount = candidateCount;
        return true;
    }

    public static PostcardChapterProgress Create(
        IReadOnlyList<PostcardDefinition> catalog,
        int unlockedCount)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentOutOfRangeException.ThrowIfNegative(unlockedCount);
        if (!TryGetChapterCount(catalog, out int chapterCount))
        {
            throw new ArgumentException(
                "Chapter progress requires complete, contiguous " +
                $"{CardsPerChapter}-card chapters.",
                nameof(catalog));
        }

        if (unlockedCount > catalog.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unlockedCount),
                "Unlocked postcards cannot exceed the catalog.");
        }

        if (unlockedCount == catalog.Count)
        {
            PostcardDefinition final = catalog[^1];
            return new(
                chapterCount,
                final.Chapter,
                CardsPerChapter,
                CardsPerChapter,
                FormatChapterLabel(
                    chapterCount,
                    final.Chapter,
                    CardsPerChapter),
                $"{chapterCount:N0} 段远方都收进明信片册啦",
                true);
        }

        int chapterIndex = unlockedCount / CardsPerChapter;
        int unlockedInChapter = unlockedCount % CardsPerChapter;
        PostcardDefinition current = catalog[chapterIndex * CardsPerChapter];
        string feedback;
        if (unlockedInChapter == 0)
        {
            feedback = unlockedCount == 0
                ? $"下一封会从第 1 章「{current.Chapter}」寄来"
                : $"上一章已经收好，下一封会走进第 {chapterIndex + 1} 章" +
                  $"「{current.Chapter}」";
        }
        else if (chapterIndex + 1 < chapterCount)
        {
            feedback =
                $"进入第{chapterIndex + 2}章还差" +
                $"{CardsPerChapter - unlockedInChapter:N0}次出游";
        }
        else
        {
            feedback =
                $"最后再等 {CardsPerChapter - unlockedInChapter:N0} 封来信，" +
                "一路风景就都到家啦";
        }

        return new(
            chapterIndex + 1,
            current.Chapter,
            unlockedInChapter,
            CardsPerChapter,
            FormatChapterLabel(
                chapterIndex + 1,
                current.Chapter,
                unlockedInChapter),
            feedback,
            false);
    }

    public static int GetChapterNumber(PostcardDefinition postcard)
    {
        ArgumentNullException.ThrowIfNull(postcard);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            postcard.UnlockOrder);
        return (postcard.UnlockOrder - 1) / CardsPerChapter + 1;
    }

    public static int GetPositionInChapter(PostcardDefinition postcard)
    {
        ArgumentNullException.ThrowIfNull(postcard);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            postcard.UnlockOrder);
        return (postcard.UnlockOrder - 1) % CardsPerChapter + 1;
    }

    public static string FormatCardPosition(PostcardDefinition postcard) =>
        FormatChapterLabel(
            GetChapterNumber(postcard),
            postcard.Chapter,
            GetPositionInChapter(postcard));

    private static string FormatChapterLabel(
        int chapterNumber,
        string chapterName,
        int position) =>
        position == 0
            ? $"第 {chapterNumber} 章「{chapterName}」· 等第一封来信"
            : $"第 {chapterNumber} 章「{chapterName}」· 已收好 {position} 张";
}
