namespace GuluPet.Dialogue;

public static class BubbleReadingTime
{
    public static TimeSpan Calculate(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        int lineBreak = message.IndexOf('\n');
        string readingText = lineBreak >= 0
            ? message[(lineBreak + 1)..]
            : message;
        int readableCharacters = readingText.Count(
            character =>
                !char.IsWhiteSpace(character) &&
                !char.IsPunctuation(character) &&
                !char.IsSymbol(character));
        int sentencePauses = readingText.Count(
            character => character is '，' or '。' or '！' or '？');
        double seconds = Math.Clamp(
            2.45 +
            readableCharacters * 0.095 +
            sentencePauses * 0.18 +
            (lineBreak >= 0 ? 0.35 : 0),
            3,
            7);
        return TimeSpan.FromSeconds(seconds);
    }
}
