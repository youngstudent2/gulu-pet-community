namespace GuluPet.Diary;

/// <summary>
/// Deterministic, offline diary generator used by the community build. It
/// summarizes only local counters and never opens a network connection.
/// </summary>
public sealed class LocalDiaryGenerator : IDiaryGenerator
{
    private readonly TimeProvider _timeProvider;

    public LocalDiaryGenerator(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<DiaryGenerationResult> GenerateAsync(
        DiaryGenerationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        long minutes = Math.Max(0, input.RuntimeSeconds / 60);
        int interactionCount = input.Interactions.Sum(static item => item.Count);
        string body = interactionCount == 0
            ? $"The companion was active for about {minutes} minutes. It was a quiet day."
            : $"The companion was active for about {minutes} minutes and recorded " +
              $"{interactionCount} interaction(s).";
        var entry = new GeneratedDiaryEntry
        {
            Title = input.Date.ToString("yyyy-MM-dd"),
            Mood = interactionCount == 0 ? "calm" : "cheerful",
            Body = body,
            Model = "local-template-v1",
            GeneratedAtUtc = _timeProvider.GetUtcNow(),
        };
        return Task.FromResult(DiaryGenerationResult.Success(entry));
    }
}
