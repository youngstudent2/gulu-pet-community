namespace GuluPet.Behavior;

public sealed record BehaviorContextTriggerSignal(
    string Tag,
    BehaviorRequestSource Source,
    TimeSpan? TimeToLive = null,
    BehaviorDailyOpportunity? DailyOpportunity = null);

/// <summary>
/// Converts immutable sensing snapshots into debounced behavior trigger
/// signals. The router stores only transition state; it never records key
/// contents, window titles, process arguments, or raw pointer coordinates.
/// </summary>
public sealed class BehaviorContextTriggerRouter
{
    public const int DailyOpportunityMaximumAcceptedCount = 15;

    private static readonly TimeSpan CalendarSlotOpportunityTtl =
        TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DaywideOpportunityTtl =
        TimeSpan.FromHours(6);
    private static readonly TimeSpan DailyOpportunityMinimumInterval =
        TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DailyOpportunityRetryInterval =
        TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ApplicationThrottle =
        TimeSpan.FromMinutes(8);
    private static readonly TimeSpan UserStateThrottle =
        TimeSpan.FromMinutes(5);

    private readonly IBehaviorDailyOpportunityLedger _dailyLedger;
    private readonly Dictionary<string, DateTimeOffset> _lastEmitted =
        new(StringComparer.Ordinal);
    private BehaviorContextSnapshot? _previous;
    private DateTimeOffset? _busySince;
    private bool _work25Emitted;
    private bool _work50Emitted;

    public BehaviorContextTriggerRouter(
        IBehaviorDailyOpportunityLedger? dailyLedger = null)
    {
        _dailyLedger =
            dailyLedger ?? new InMemoryBehaviorDailyOpportunityLedger();
    }

    public IReadOnlyList<BehaviorContextTriggerSignal> Observe(
        BehaviorContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_previous is not null &&
            context.Revision <= _previous.Revision)
        {
            return [];
        }

        var signals = new List<BehaviorContextTriggerSignal>();
        bool dailyOpportunityAvailable = IsActiveWorkContext(context);
        if (dailyOpportunityAvailable)
        {
            ObserveCalendar(context, signals);
            ObserveWeather(context, signals);
        }

        ObserveApplication(context, signals);
        ObserveUserState(context, signals);
        _previous = context;
        return signals;
    }

    public bool MarkPresented(
        BehaviorDailyOpportunity opportunity,
        DateTimeOffset presentedAt)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        DateOnly presentedDate =
            DateOnly.FromDateTime(presentedAt.Date);

        return _dailyLedger.TryRecordAccepted(
            opportunity.Key,
            presentedDate,
            presentedAt,
            DailyOpportunityMaximumAcceptedCount);
    }

    private void ObserveCalendar(
        BehaviorContextSnapshot context,
        ICollection<BehaviorContextTriggerSignal> signals)
    {
        DateTimeOffset now = context.LocalNow;
        string? slot = ResolveTimeSlot(now.TimeOfDay);
        if (slot is not null)
        {
            EmitDailyOpportunity(
                $"time:{slot}",
                $"trigger:time:{slot}",
                BehaviorRequestSource.Scheduled,
                now,
                CalendarSlotOpportunityTtl,
                signals);
        }

        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            EmitDailyOpportunity(
                "time:weekend",
                "trigger:time:weekend",
                BehaviorRequestSource.Scheduled,
                now,
                DaywideOpportunityTtl,
                signals);
        }

        if (ChinaWorkCalendar2026.IsHoliday(DateOnly.FromDateTime(now.Date)))
        {
            EmitDailyOpportunity(
                "time:holiday",
                "trigger:time:holiday",
                BehaviorRequestSource.Scheduled,
                now,
                DaywideOpportunityTtl,
                signals);
        }
    }

    private void ObserveApplication(
        BehaviorContextSnapshot context,
        ICollection<BehaviorContextTriggerSignal> signals)
    {
        if (!context.ApplicationCategoryAvailable ||
            context.ApplicationStableFor < TimeSpan.FromSeconds(15))
        {
            return;
        }

        string? category = NormalizeApplicationCategory(
            context.ApplicationCategory);
        if (category is null)
        {
            return;
        }

        string? previousCategory =
            _previous is { ApplicationCategoryAvailable: true }
                ? NormalizeApplicationCategory(
                    _previous.ApplicationCategory)
                : null;
        bool crossedStableBoundary =
            _previous is null ||
            previousCategory != category ||
            _previous.ApplicationStableFor < TimeSpan.FromSeconds(15);
        if (crossedStableBoundary)
        {
            EmitThrottled(
                $"app:{category}",
                $"trigger:app:{category}",
                BehaviorRequestSource.UserContext,
                context.LocalNow,
                ApplicationThrottle,
                signals);
        }
    }

    private void ObserveWeather(
        BehaviorContextSnapshot context,
        ICollection<BehaviorContextTriggerSignal> signals)
    {
        if (!context.WeatherAvailable)
        {
            return;
        }

        string? kind = ResolveDailyWeatherKind(context);
        if (kind is not null)
        {
            EmitDailyOpportunity(
                "weather:daily",
                $"trigger:weather:{kind}",
                BehaviorRequestSource.Weather,
                context.LocalNow,
                DaywideOpportunityTtl,
                signals);
        }
    }

    private void ObserveUserState(
        BehaviorContextSnapshot context,
        ICollection<BehaviorContextTriggerSignal> signals)
    {
        TimeSpan priorIdle = _previous?.IdleFor ?? TimeSpan.Zero;
        if (priorIdle < TimeSpan.FromMinutes(5) &&
            context.IdleFor >= TimeSpan.FromMinutes(5))
        {
            EmitThrottled(
                "user:away",
                "trigger:user:away",
                BehaviorRequestSource.UserContext,
                context.LocalNow,
                UserStateThrottle,
                signals);
        }

        if (priorIdle >= TimeSpan.FromMinutes(5) &&
            context.IdleFor < TimeSpan.FromSeconds(30))
        {
            EmitThrottled(
                "user:return",
                "trigger:user:return",
                BehaviorRequestSource.UserContext,
                context.LocalNow,
                UserStateThrottle,
                signals);
        }

        if (priorIdle < TimeSpan.FromMinutes(2) &&
            context.IdleFor >= TimeSpan.FromMinutes(2))
        {
            EmitThrottled(
                "user:idle",
                "trigger:user:idle",
                BehaviorRequestSource.UserContext,
                context.LocalNow,
                UserStateThrottle,
                signals);
        }

        bool previouslyBusy = _previous?.IsBusy ?? false;
        if (context.IsBusy)
        {
            _busySince ??= context.LocalNow;
            if (!previouslyBusy)
            {
                EmitThrottled(
                    "user:busy",
                    "trigger:user:busy",
                    BehaviorRequestSource.UserContext,
                    context.LocalNow,
                    UserStateThrottle,
                    signals);
            }

            TimeSpan busyFor = context.LocalNow - _busySince.Value;
            if (!_work25Emitted && busyFor >= TimeSpan.FromMinutes(25))
            {
                _work25Emitted = true;
                EmitThrottled(
                    "user:work25",
                    "trigger:user:work25",
                    BehaviorRequestSource.UserContext,
                    context.LocalNow,
                    TimeSpan.FromMinutes(20),
                    signals);
            }

            if (!_work50Emitted && busyFor >= TimeSpan.FromMinutes(50))
            {
                _work50Emitted = true;
                EmitThrottled(
                    "user:work50",
                    "trigger:user:work50",
                    BehaviorRequestSource.UserContext,
                    context.LocalNow,
                    TimeSpan.FromMinutes(40),
                    signals);
            }
        }
        else
        {
            _busySince = null;
            _work25Emitted = false;
            _work50Emitted = false;
        }

        if (_previous?.IsLocked == true && !context.IsLocked)
        {
            EmitThrottled(
                "user:resume",
                "trigger:user:resume",
                BehaviorRequestSource.UserContext,
                context.LocalNow,
                UserStateThrottle,
                signals);
        }
    }

    private void EmitDailyOpportunity(
        string key,
        string tag,
        BehaviorRequestSource source,
        DateTimeOffset observedAt,
        TimeSpan timeToLive,
        ICollection<BehaviorContextTriggerSignal> signals)
    {
        DateOnly localDate = DateOnly.FromDateTime(observedAt.Date);
        BehaviorDailyOpportunityUsage usage = _dailyLedger.Read(
            key,
            localDate);
        if (usage.AcceptedCount >= DailyOpportunityMaximumAcceptedCount)
        {
            return;
        }

        if (usage.LastAcceptedAt is { } lastAcceptedAt)
        {
            if (observedAt < lastAcceptedAt ||
                observedAt - lastAcceptedAt <
                DailyOpportunityMinimumInterval)
            {
                return;
            }
        }

        string attemptKey = $"daily-attempt:{key}:{localDate.DayNumber}";
        if (_lastEmitted.TryGetValue(
                attemptKey,
                out DateTimeOffset lastAttempt))
        {
            if (observedAt < lastAttempt ||
                observedAt - lastAttempt <
                DailyOpportunityRetryInterval)
            {
                return;
            }
        }

        _lastEmitted[attemptKey] = observedAt;
        signals.Add(
            new BehaviorContextTriggerSignal(
                tag,
                source,
                timeToLive,
                new BehaviorDailyOpportunity(
                    key,
                    localDate,
                    observedAt,
                    usage.AcceptedCount,
                    usage.LastAcceptedAt)));
    }

    private void EmitThrottled(
        string key,
        string tag,
        BehaviorRequestSource source,
        DateTimeOffset now,
        TimeSpan throttle,
        ICollection<BehaviorContextTriggerSignal> signals)
    {
        if (_lastEmitted.TryGetValue(key, out DateTimeOffset last) &&
            now >= last &&
            now - last < throttle)
        {
            return;
        }

        if (_lastEmitted.TryGetValue(key, out last) && now < last)
        {
            return;
        }

        _lastEmitted[key] = now;
        signals.Add(new BehaviorContextTriggerSignal(tag, source));
    }

    internal static string? ResolveTimeSlot(TimeSpan time)
    {
        if (time >= TimeSpan.FromHours(6.5) &&
            time < TimeSpan.FromHours(9))
        {
            return "morning";
        }

        if (time >= TimeSpan.FromHours(11.5) &&
            time < TimeSpan.FromHours(14))
        {
            return "noon";
        }

        if (time >= TimeSpan.FromHours(18) &&
            time < TimeSpan.FromHours(22.5))
        {
            return "evening";
        }

        if (time >= TimeSpan.FromHours(22.5) ||
            time < TimeSpan.FromHours(6.5))
        {
            return "late";
        }

        return null;
    }

    internal static bool IsActiveWorkContext(
        BehaviorContextSnapshot context)
    {
        if (context.IsLocked ||
            context.IsFullscreen ||
            context.IdleFor >= TimeSpan.FromSeconds(90))
        {
            return false;
        }

        string? category = context.ApplicationCategoryAvailable &&
                           context.ApplicationStableFor >=
                           TimeSpan.FromSeconds(15)
            ? NormalizeApplicationCategory(context.ApplicationCategory)
            : null;
        if (category is "game" or "media_player")
        {
            return false;
        }

        return category is
                   "browser" or
                   "office" or
                   "ide" or
                   "communication" or
                   "file_manager" ||
               (category is null && context.IsBusy);
    }

    private static string? NormalizeApplicationCategory(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "browser" => "browser",
            "office" => "office",
            "ide" => "ide",
            "media" or "media_player" or "mediaplayer" => "media_player",
            "game" => "game",
            "communication" => "communication",
            "file_manager" or "filemanager" => "file_manager",
            _ => null,
        };

    private static string? NormalizeWeatherKind(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "clear" => "clear",
            "cloudy" or "overcast" => "cloudy",
            "rain" or "drizzle" or "snow" => "rain",
            "storm" or "thunderstorm" => "storm",
            "fog" => "fog",
            _ => null,
        };

    private static string? ResolveDailyWeatherKind(
        BehaviorContextSnapshot context)
    {
        string? kind = NormalizeWeatherKind(context.WeatherKind);
        if (kind is "storm" or "rain" or "fog")
        {
            return kind;
        }

        if (context.TemperatureCelsius >= 30)
        {
            return "hot";
        }

        if (context.TemperatureCelsius <= 12)
        {
            return "cold";
        }

        return kind;
    }
}

public static class ChinaWorkCalendar2026
{
    private static readonly (DateOnly Start, DateOnly End)[] Holidays =
    [
        (new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3)),
        (new DateOnly(2026, 2, 15), new DateOnly(2026, 2, 23)),
        (new DateOnly(2026, 4, 4), new DateOnly(2026, 4, 6)),
        (new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 5)),
        (new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 21)),
        (new DateOnly(2026, 9, 25), new DateOnly(2026, 9, 27)),
        (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7)),
    ];

    public static bool IsHoliday(DateOnly date) =>
        Holidays.Any(range => date >= range.Start && date <= range.End);
}
