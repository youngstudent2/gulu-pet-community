using GuluPet.Dialogue;
using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class CatDialogueAnnotationTests
{
    public static void RunAll()
    {
        Run(
            nameof(AcceptsNaturalInternalMeanings),
            AcceptsNaturalInternalMeanings);
        Run(
            nameof(RejectsLongNestedOrPaddedAnnotations),
            RejectsLongNestedOrPaddedAnnotations);
        Run(
            nameof(ReadingTimeScalesFromThreeToSevenSeconds),
            ReadingTimeScalesFromThreeToSevenSeconds);
        Run(
            nameof(ProductionDialogueDisplaysOnlyCatVocalizations),
            ProductionDialogueDisplaysOnlyCatVocalizations);
    }

    private static void AcceptsNaturalInternalMeanings()
    {
        BehaviorTestCheck.True(
            CatDialogueAnnotation.IsValid("陪你看会儿"));
        BehaviorTestCheck.True(
            CatDialogueAnnotation.IsValid("你在忙什么"));
        BehaviorTestCheck.True(
            CatDialogueAnnotation.IsValid(
                "有你就是家，陪你忙完，认真看着你，真的很开心。"));
    }

    private static void RejectsLongNestedOrPaddedAnnotations()
    {
        foreach (string annotation in
                 new[]
                 {
                     "",
                     new string('你', 41),
                     "（开心）",
                     " 开心",
                     "看 我",
                     "！！！",
                 })
        {
            BehaviorTestCheck.False(
                CatDialogueAnnotation.IsValid(annotation));
        }
    }

    private static void ReadingTimeScalesFromThreeToSevenSeconds()
    {
        TimeSpan shortMessage = BubbleReadingTime.Calculate("喵");
        TimeSpan naturalMessage = BubbleReadingTime.Calculate(
            "喵喵\n陪你忙完，认真看着你，真的很开心。");
        TimeSpan maximum = BubbleReadingTime.Calculate(new string('你', 100));

        BehaviorTestCheck.Equal(TimeSpan.FromSeconds(3), shortMessage);
        BehaviorTestCheck.True(naturalMessage > shortMessage);
        BehaviorTestCheck.True(naturalMessage < TimeSpan.FromSeconds(7));
        BehaviorTestCheck.Equal(TimeSpan.FromSeconds(7), maximum);
    }

    private static void ProductionDialogueDisplaysOnlyCatVocalizations()
    {
        DialogueCatalog catalog = DialogueCatalog.LoadAsync(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "Data",
                    "dialogues.json"))
            .GetAwaiter()
            .GetResult();

        foreach (DialogueLine line in catalog.Lines)
        {
            string message = BubbleWindowBehaviorPort.VisibleMessage(line);
            BehaviorTestCheck.Equal(line.Text, message);
            BehaviorTestCheck.True(CatVocalization.IsValid(message));
            BehaviorTestCheck.False(message.Contains('\n'));
            BehaviorTestCheck.False(
                message.Contains(line.Meaning, StringComparison.Ordinal));
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(CatDialogueAnnotationTests)}.{name}");
    }
}
