namespace GuluPet.Dialogue;

public static class CatVocalization
{
    private static readonly string[] AllowedEmoticons =
    [
        "￣へ￣",
        "(≧ω≦)/",
        "(T ^ T)",
        "(=^ω^=)",
        "(>ω<)",
        "(つω⊂)",
        "(・ω・)",
        "(￣ω￣)",
        "(╥ω╥)",
        "(=ω=)",
        "(≧▽≦)",
        "(ノωヽ)",
        "(＾ω＾)",
        "(⊙ω⊙)",
        "(；ω；)",
        "(¬ω¬)",
    ];

    private static readonly string[] VocalTokens =
    [
        "呼噜",
        "喵",
        "嗷",
        "呜",
    ];

    private static readonly string[] PunctuationTokens =
    [
        "...",
        "~",
        "！",
        "、",
    ];

    public static bool IsValid(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        bool hasVocalization = false;
        int emoticonCount = 0;
        int index = 0;

        while (index < text.Length)
        {
            if (TryConsume(text, ref index, AllowedEmoticons))
            {
                emoticonCount++;
                if (emoticonCount > 1)
                {
                    return false;
                }

                continue;
            }

            if (TryConsume(text, ref index, VocalTokens))
            {
                hasVocalization = true;
                continue;
            }

            if (TryConsume(text, ref index, PunctuationTokens))
            {
                continue;
            }

            return false;
        }

        return hasVocalization;
    }

    public static bool ContainsEmoticon(string? text)
    {
        return text is not null
            && AllowedEmoticons.Any(
                emoticon => text.Contains(emoticon, StringComparison.Ordinal));
    }

    private static bool TryConsume(
        string text,
        ref int index,
        IReadOnlyList<string> tokens)
    {
        ReadOnlySpan<char> remaining = text.AsSpan(index);
        foreach (string token in tokens)
        {
            if (!remaining.StartsWith(token, StringComparison.Ordinal))
            {
                continue;
            }

            index += token.Length;
            return true;
        }

        return false;
    }
}
