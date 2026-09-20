using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;

namespace GuluPet.Diary;

public static class DiaryInteractionKinds
{
    public const string Click = "click";
    public const string LongPress = "long_press";
    public const string Drag = "drag";
    public const string DockLeft = "dock_left";
    public const string DockRight = "dock_right";
    public const string Approach = "approach";
    public const string Hover = "hover";
    public const string Petting = "petting";
    public const string PettingEnded = "petting_ended";
    public const string ToolbarPetting = "toolbar_petting";
    public const string RapidPointer = "rapid_pointer";
    public const string CirclePointer = "circle_pointer";
    public const string Feed = "feed";
    public const string Water = "water";
    public const string OutingStart = "outing_start";
    public const string PostcardClaim = "postcard_claim";
    public const string MemoryReplay = "memory_replay";
    public const string ManualBehavior = "manual_behavior";

    private static readonly IReadOnlyDictionary<string, string> Labels =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Click] = "轻轻点了咕噜一下",
                [LongPress] = "轻轻按住咕噜一会儿",
                [Drag] = "抱着咕噜挪了挪",
                [DockLeft] = "把咕噜放到屏幕左边",
                [DockRight] = "把咕噜放到屏幕右边",
                [Approach] = "把鼠标靠近咕噜",
                [Hover] = "在咕噜身边停了一会儿",
                [Petting] = "摸摸咕噜",
                [PettingEnded] = "把手收了回来",
                [ToolbarPetting] = "又摸了摸咕噜",
                [RapidPointer] = "飞快逗了逗咕噜",
                [CirclePointer] = "绕着咕噜逗她",
                [Feed] = "给咕噜喂了猫条",
                [Water] = "给咕噜添水",
                [OutingStart] = "让咕噜出去逛逛",
                [PostcardClaim] = "收下咕噜带回的明信片",
                [MemoryReplay] = "又看了一遍咕噜的回忆",
                [ManualBehavior] = "请咕噜做了个小动作",
            });

    public static bool IsKnown(string? kind) =>
        kind is not null && Labels.ContainsKey(kind);

    public static string GetLabel(string kind) =>
        Labels.TryGetValue(kind, out string? label)
            ? label
            : throw new ArgumentException(
                $"Unknown diary interaction kind '{kind}'.",
                nameof(kind));
}

public enum DiaryGenerationFailureDisposition
{
    Retryable,
    Terminal,
    InsufficientBalance,
}

public sealed record DiaryInteractionCount
{
    [JsonRequired]
    public required string Kind { get; init; }

    [JsonRequired]
    public int Count { get; init; }
}

public sealed record DiaryUnlockRecord
{
    [JsonRequired]
    public required string Id { get; init; }

    [JsonRequired]
    public required string Title { get; init; }

    public string? Detail { get; init; }

    public string? Description { get; init; }

    [JsonRequired]
    public DateTimeOffset UnlockedAtUtc { get; init; }
}

public sealed record DiaryWeatherSnapshot
{
    [JsonRequired]
    public required string Kind { get; init; }

    [JsonRequired]
    public double TemperatureCelsius { get; init; }

    [JsonRequired]
    public DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record GeneratedDiaryEntry
{
    [JsonRequired]
    public required string Title { get; init; }

    [JsonRequired]
    public required string Mood { get; init; }

    [JsonRequired]
    public required string Body { get; init; }

    [JsonRequired]
    public required string Model { get; init; }

    [JsonRequired]
    public DateTimeOffset GeneratedAtUtc { get; init; }
}

public sealed record DiaryGenerationFailure
{
    [JsonRequired]
    public DiaryGenerationFailureDisposition Disposition { get; init; }

    [JsonRequired]
    public required string Code { get; init; }

    [JsonRequired]
    public int AttemptCount { get; init; }

    [JsonRequired]
    public DateTimeOffset LastAttemptAtUtc { get; init; }

    public DateTimeOffset? RetryNotBeforeUtc { get; init; }
}

public sealed record DiaryDayState
{
    [JsonRequired]
    public DateOnly Date { get; init; }

    [JsonRequired]
    public long RuntimeSeconds { get; init; }

    [JsonRequired]
    public IReadOnlyList<DiaryInteractionCount> Interactions { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<DiaryUnlockRecord> Postcards { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<DiaryUnlockRecord> Memories { get; init; } = [];

    public DiaryWeatherSnapshot? Weather { get; init; }

    public GeneratedDiaryEntry? Entry { get; init; }

    public DiaryGenerationFailure? GenerationFailure { get; init; }

    [JsonRequired]
    public DateTimeOffset UpdatedAtUtc { get; init; }

    [JsonIgnore]
    public int TotalInteractionCount => Interactions.Sum(
        static interaction => interaction.Count);

    [JsonIgnore]
    public bool HasMeaningfulRecord =>
        RuntimeSeconds > (long)TimeSpan.FromMinutes(30).TotalSeconds
        || TotalInteractionCount > 0
        || Postcards.Count > 0
        || Memories.Count > 0;

    [JsonIgnore]
    public bool IsGenerationTerminal =>
        GenerationFailure?.Disposition is
            DiaryGenerationFailureDisposition.Terminal or
            DiaryGenerationFailureDisposition.InsufficientBalance;

    public static DiaryDayState Create(
        DateOnly date,
        DateTimeOffset nowUtc) =>
        new()
        {
            Date = date,
            UpdatedAtUtc = nowUtc,
        };
}

public sealed record DiaryState
{
    public const int CurrentSchemaVersion = 1;
    private static readonly TimeSpan LegacyBalanceRetryDelay =
        TimeSpan.FromHours(6);

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonRequired]
    public IReadOnlyList<DiaryDayState> Days { get; init; } = [];

    [JsonRequired]
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public static DiaryState CreateDefault(DateTimeOffset? nowUtc = null) =>
        new()
        {
            UpdatedAtUtc = nowUtc ?? DateTimeOffset.UtcNow,
        };

    internal static DiaryState ValidateAndSnapshot(DiaryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported diary state schema version " +
                $"'{state.SchemaVersion}'; expected " +
                $"'{CurrentSchemaVersion}'.");
        }

        if (state.UpdatedAtUtc == default)
        {
            throw new InvalidDataException(
                "Diary state must declare updatedAtUtc.");
        }

        IReadOnlyList<DiaryDayState> days = state.Days
            ?? throw new InvalidDataException(
                "Diary state must declare days as an array.");
        var dates = new HashSet<DateOnly>();
        var snapshots = new DiaryDayState[days.Count];
        for (var index = 0; index < days.Count; index++)
        {
            DiaryDayState day = days[index]
                ?? throw new InvalidDataException(
                    $"Diary day at index {index} is null.");
            if (day.Date == default || !dates.Add(day.Date))
            {
                throw new InvalidDataException(
                    $"Diary day at index {index} has a missing or duplicate date.");
            }

            snapshots[index] = ValidateDay(day);
        }

        return state with
        {
            Days = snapshots
                .OrderBy(static day => day.Date)
                .ToArray(),
        };
    }

    private static DiaryDayState ValidateDay(DiaryDayState day)
    {
        const long maximumRuntimeSeconds = 25 * 60 * 60;
        if (day.RuntimeSeconds is < 0 or > maximumRuntimeSeconds)
        {
            throw new InvalidDataException(
                $"Diary runtime for '{day.Date:yyyy-MM-dd}' is invalid.");
        }

        if (day.UpdatedAtUtc == default)
        {
            throw new InvalidDataException(
                $"Diary day '{day.Date:yyyy-MM-dd}' has no update time.");
        }

        DiaryInteractionCount[] interactions = ValidateInteractions(
            day.Date,
            day.Interactions);
        DiaryUnlockRecord[] postcards = ValidateUnlocks(
            day.Date,
            "postcard",
            day.Postcards);
        DiaryUnlockRecord[] memories = ValidateUnlocks(
            day.Date,
            "memory",
            day.Memories);
        DiaryWeatherSnapshot? weather = ValidateWeather(day.Weather);
        GeneratedDiaryEntry? entry = ValidateEntry(day.Entry);
        DiaryGenerationFailure? failure = ValidateFailure(
            day.GenerationFailure);
        if (entry is not null && failure is not null)
        {
            throw new InvalidDataException(
                $"Generated diary '{day.Date:yyyy-MM-dd}' cannot retain a failure.");
        }

        return day with
        {
            Interactions = interactions,
            Postcards = postcards,
            Memories = memories,
            Weather = weather,
            Entry = entry,
            GenerationFailure = failure,
        };
    }

    private static DiaryInteractionCount[] ValidateInteractions(
        DateOnly date,
        IReadOnlyList<DiaryInteractionCount>? interactions)
    {
        if (interactions is null)
        {
            throw new InvalidDataException(
                $"Diary day '{date:yyyy-MM-dd}' has no interaction array.");
        }
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        var snapshot = new DiaryInteractionCount[interactions.Count];
        for (var index = 0; index < interactions.Count; index++)
        {
            DiaryInteractionCount item = interactions[index]
                ?? throw new InvalidDataException(
                    $"Diary interaction at index {index} is null.");
            if (!DiaryInteractionKinds.IsKnown(item.Kind)
                || !kinds.Add(item.Kind)
                || item.Count <= 0)
            {
                throw new InvalidDataException(
                    $"Diary interaction '{item.Kind}' is invalid or duplicated.");
            }

            snapshot[index] = item with { };
        }

        return snapshot
            .OrderBy(static item => item.Kind, StringComparer.Ordinal)
            .ToArray();
    }

    private static DiaryUnlockRecord[] ValidateUnlocks(
        DateOnly date,
        string kind,
        IReadOnlyList<DiaryUnlockRecord>? records)
    {
        if (records is null)
        {
            throw new InvalidDataException(
                $"Diary day '{date:yyyy-MM-dd}' has no {kind} array.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var snapshot = new DiaryUnlockRecord[records.Count];
        for (var index = 0; index < records.Count; index++)
        {
            DiaryUnlockRecord record = records[index]
                ?? throw new InvalidDataException(
                    $"Diary {kind} at index {index} is null.");
            if (!IsShortText(record.Id, 128)
                || !IsShortText(record.Title, 160)
                || (record.Detail is not null
                    && !IsShortText(record.Detail, 240))
                || (record.Description is not null
                    && !IsShortText(record.Description, 320))
                || record.UnlockedAtUtc == default
                || !ids.Add(record.Id))
            {
                throw new InvalidDataException(
                    $"Diary {kind} '{record.Id}' is invalid or duplicated.");
            }

            snapshot[index] = record with { };
        }

        return snapshot
            .OrderBy(static record => record.UnlockedAtUtc)
            .ThenBy(static record => record.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static DiaryWeatherSnapshot? ValidateWeather(
        DiaryWeatherSnapshot? weather)
    {
        if (weather is null)
        {
            return null;
        }

        if (!IsShortText(weather.Kind, 40)
            || !double.IsFinite(weather.TemperatureCelsius)
            || weather.TemperatureCelsius is < -100 or > 100
            || weather.ObservedAtUtc == default)
        {
            throw new InvalidDataException(
                "Diary weather snapshot is invalid.");
        }

        return weather with { };
    }

    private static GeneratedDiaryEntry? ValidateEntry(
        GeneratedDiaryEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }

        if (!IsShortText(entry.Title, 80)
            || !IsShortText(entry.Mood, 24)
            || !IsShortText(entry.Body, 4000)
            || !IsShortText(entry.Model, 120)
            || entry.GeneratedAtUtc == default)
        {
            throw new InvalidDataException(
                "Generated diary entry is invalid.");
        }

        return entry with { };
    }

    private static DiaryGenerationFailure? ValidateFailure(
        DiaryGenerationFailure? failure)
    {
        if (failure is null)
        {
            return null;
        }

        if (!Enum.IsDefined(failure.Disposition)
            || !IsShortText(failure.Code, 120)
            || failure.AttemptCount <= 0
            || failure.LastAttemptAtUtc == default
            || failure.RetryNotBeforeUtc is { } retryNotBeforeUtc
                && retryNotBeforeUtc < failure.LastAttemptAtUtc)
        {
            throw new InvalidDataException(
                "Diary generation failure is invalid.");
        }

        // Earlier schema-v1 clients treated an upstream balance outage as
        // terminal. Keep the enum value readable for compatibility, but
        // normalize persisted legacy failures so those dates can recover
        // after a configured provider becomes available again.
        if (failure.Disposition
            == DiaryGenerationFailureDisposition.InsufficientBalance)
        {
            DateTimeOffset legacyRetryNotBeforeUtc =
                failure.RetryNotBeforeUtc
                ?? (failure.LastAttemptAtUtc
                        <= DateTimeOffset.MaxValue - LegacyBalanceRetryDelay
                    ? failure.LastAttemptAtUtc + LegacyBalanceRetryDelay
                    : DateTimeOffset.MaxValue);
            return failure with
            {
                Disposition = DiaryGenerationFailureDisposition.Retryable,
                RetryNotBeforeUtc = legacyRetryNotBeforeUtc,
            };
        }

        return failure with { };
    }

    private static bool IsShortText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= maximumLength;
}
