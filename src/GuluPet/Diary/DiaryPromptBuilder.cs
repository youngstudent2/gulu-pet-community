using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GuluPet.Diary;

public sealed record DiaryGenerationInput(
    DateOnly Date,
    DiaryDayPeriod Period,
    long RuntimeSeconds,
    IReadOnlyList<DiaryInteractionCount> Interactions,
    IReadOnlyList<DiaryUnlockRecord> Postcards,
    IReadOnlyList<DiaryUnlockRecord> Memories,
    DiaryWeatherSnapshot? Weather);

public static class DiaryPromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public const string SystemPrompt =
        "你是 Windows 桌宠咕噜，一只真实的银渐层白猫，也是一只母猫。" +
        "你有点高冷，平时不太理人，不会为了讨好谁刻意撒娇。" +
        "你和妈咪已经相处很多年，非常了解她，对她有很深的喜欢与宽容；" +
        "但你只会用猫的方式克制地表达，不会直白说爱，也不会频繁卖萌。" +
        "只有和妈咪相处时，你偶尔才会不经意露出一点可爱、依赖或柔软，" +
        "随后又若无其事地恢复矜持。母猫与多年相处的设定用于保持人格一致，" +
        "不必在每篇日记里直接说明。要通过熟稔、克制、带一点纵容的语气" +
        "表现你对妈咪的了解和喜欢，不得为了表现了解而虚构妈咪的习惯、" +
        "工作、情绪或共同经历。不要把咕噜写成热情黏人、事事回应、" +
        "幼稚撒娇或过度感恩的宠物。" +
        "请严格根据用户提供的结构化事实写一篇日记，使用咕噜第一人称，" +
        "把用户称作“妈咪”，语气温暖、自然、有生活气息。" +
        "正文建议 180 至 320 个汉字，可以自然概括交互次数、累计在线时长、" +
        "天气、当天新解锁的明信片或回忆。没有互动但运行超过 30 分钟时，" +
        "应写出安静陪着妈咪也很开心的感受。可以加入少量不改变结构化事实的" +
        "氛围性想象，例如咕噜的姿态、心情或室内感受，让日记自然好读；" +
        "但不得新增妈咪主动触发的交互，不得改变交互类型或次数，不得虚构" +
        "地点、天气、明信片或回忆，也不得把彼此无关联的数据强行对应。" +
        "解锁回忆中的 desc 是逐段核对过的回忆视频画面描述；只能根据 desc" +
        "写回忆的具体内容，不得根据 id 或其他字段猜测画面。" +
        "运行时长只是咕噜在应用中的累计在线时间，不代表妈咪持续工作或" +
        "咕噜全程陪伴；天气只代表当日记录，不得自行推断持续时段。" +
        "缺失数据可以略去。不得使用 Markdown。" +
        "必须只输出一个 json 对象，格式示例：" +
        "{\"title\":\"窗边的一小段陪伴\",\"mood\":\"安心\",\"body\":\"今天……\"}。";

    public static DiaryGenerationInput CreateInput(
        DiaryDayState day,
        DiaryDatePolicy datePolicy)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(datePolicy);
        return new DiaryGenerationInput(
            day.Date,
            datePolicy.GetPeriod(day.Date),
            day.RuntimeSeconds,
            day.Interactions.Select(static item => item with { }).ToArray(),
            day.Postcards.Select(static item => item with { }).ToArray(),
            day.Memories.Select(static item => item with { }).ToArray(),
            day.Weather is null ? null : day.Weather with { });
    }

    public static string BuildUserPrompt(
        DiaryGenerationInput input,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(timeZone);

        object payload = new
        {
            diaryDate = input.Date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture),
            period = new
            {
                start = FormatLocal(input.Period.StartsAtUtc, timeZone),
                endExclusive = FormatLocal(input.Period.EndsAtUtc, timeZone),
            },
            runtimeMinutes = Math.Round(
                input.RuntimeSeconds / 60d,
                1,
                MidpointRounding.AwayFromZero),
            interactions = input.Interactions.Select(item => new
            {
                kind = item.Kind,
                label = DiaryInteractionKinds.GetLabel(item.Kind),
                count = item.Count,
            }),
            weather = input.Weather is null
                ? null
                : new
                {
                    kind = input.Weather.Kind,
                    label = FormatWeather(input.Weather.Kind),
                    temperatureCelsius = Math.Round(
                        input.Weather.TemperatureCelsius,
                        1,
                        MidpointRounding.AwayFromZero),
                },
            unlockedPostcards = input.Postcards.Select(item => new
            {
                id = item.Id,
                title = item.Title,
                location = item.Detail,
            }),
            unlockedMemories = input.Memories.Select(item => new
            {
                id = item.Id,
                desc = item.Description,
            }),
        };

        return "请根据下面这份 json 事实生成日记。空数组或 null 代表当天没有" +
            "该项；可以按 system 指令补充少量氛围性细节，但核心事实只能来自" +
            "下面的数据：\n" + JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string FormatWeather(string kind) => kind switch
    {
        "clear" => "晴朗",
        "cloudy" => "多云",
        "fog" => "有雾",
        "rain" => "下雨",
        "storm" => "雷雨",
        _ => "天气未知",
    };

    private static string FormatLocal(
        DateTimeOffset value,
        TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(value, timeZone)
            .ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
}
