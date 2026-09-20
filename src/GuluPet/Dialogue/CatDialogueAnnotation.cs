using System.Globalization;

namespace GuluPet.Dialogue;

public static class CatDialogueAnnotation
{
    public const int MaximumMeaningfulTextElements = 40;

    public static bool IsValid(string? annotation)
    {
        if (string.IsNullOrWhiteSpace(annotation) ||
            !string.Equals(
            annotation,
            annotation.Trim(),
            StringComparison.Ordinal) ||
            annotation.IndexOfAny(['（', '）', '(', ')']) >= 0 ||
            annotation.Any(char.IsWhiteSpace))
        {
            return false;
        }

        int meaningfulTextElements = 0;
        TextElementEnumerator enumerator =
            StringInfo.GetTextElementEnumerator(annotation);
        while (enumerator.MoveNext())
        {
            string textElement = (string)enumerator.Current;
            if (!IsPunctuation(
                    CharUnicodeInfo.GetUnicodeCategory(textElement, 0)))
            {
                meaningfulTextElements++;
            }
        }

        return meaningfulTextElements is >= 1
            and <= MaximumMeaningfulTextElements;
    }

    private static bool IsPunctuation(UnicodeCategory category) =>
        category is
            UnicodeCategory.ConnectorPunctuation or
            UnicodeCategory.DashPunctuation or
            UnicodeCategory.OpenPunctuation or
            UnicodeCategory.ClosePunctuation or
            UnicodeCategory.InitialQuotePunctuation or
            UnicodeCategory.FinalQuotePunctuation or
            UnicodeCategory.OtherPunctuation;
}
