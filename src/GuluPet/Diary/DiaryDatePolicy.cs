namespace GuluPet.Diary;

/// <summary>
/// Maps wall-clock activity into the diary day that closes at 18:30. A diary
/// labelled D therefore covers D-1 18:30 (inclusive) through D 18:30
/// (exclusive). Generation is permitted from 18:30 through the following
/// morning at 08:30.
/// </summary>
public sealed class DiaryDatePolicy
{
    public static readonly TimeOnly DayBoundary = new(18, 30);
    public static readonly TimeOnly MorningWindowEnd = new(8, 30);

    private readonly TimeZoneInfo _timeZone;

    public DiaryDatePolicy(TimeZoneInfo? timeZone = null)
    {
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public TimeZoneInfo TimeZone => _timeZone;

    public DateOnly GetActivityDate(DateTimeOffset occurredAtUtc)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(
            occurredAtUtc,
            _timeZone);
        DateOnly calendarDate = DateOnly.FromDateTime(local.DateTime);
        return TimeOnly.FromDateTime(local.DateTime) >= DayBoundary
            ? calendarDate.AddDays(1)
            : calendarDate;
    }

    public bool TryGetGenerationTarget(
        DateTimeOffset observedAtUtc,
        out DateOnly targetDate)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(
            observedAtUtc,
            _timeZone);
        DateOnly calendarDate = DateOnly.FromDateTime(local.DateTime);
        TimeOnly localTime = TimeOnly.FromDateTime(local.DateTime);
        if (localTime >= DayBoundary)
        {
            targetDate = calendarDate;
            return true;
        }

        if (localTime <= MorningWindowEnd)
        {
            targetDate = calendarDate.AddDays(-1);
            return true;
        }

        targetDate = default;
        return false;
    }

    public DiaryDayPeriod GetPeriod(DateOnly diaryDate)
    {
        DateTime localStart = diaryDate
            .AddDays(-1)
            .ToDateTime(DayBoundary, DateTimeKind.Unspecified);
        DateTime localEnd = diaryDate
            .ToDateTime(DayBoundary, DateTimeKind.Unspecified);
        DateTime utcStart = TimeZoneInfo.ConvertTimeToUtc(
            localStart,
            _timeZone);
        DateTime utcEnd = TimeZoneInfo.ConvertTimeToUtc(
            localEnd,
            _timeZone);
        return new DiaryDayPeriod(
            new DateTimeOffset(utcStart, TimeSpan.Zero),
            new DateTimeOffset(utcEnd, TimeSpan.Zero));
    }

    public DateOnly GetCalendarDate(DateTimeOffset observedAtUtc)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(
            observedAtUtc,
            _timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public DateOnly GetLatestClosedDate(DateTimeOffset observedAtUtc)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(
            observedAtUtc,
            _timeZone);
        DateOnly calendarDate = DateOnly.FromDateTime(local.DateTime);
        return TimeOnly.FromDateTime(local.DateTime) >= DayBoundary
            ? calendarDate
            : calendarDate.AddDays(-1);
    }
}

public sealed record DiaryDayPeriod(
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);
