using GuluPet.Behavior;
using GuluPet.Persistence;

namespace GuluPet.Tests;

internal static class BehaviorContextTriggerRouterTests
{
    public static void RunAll()
    {
        Run(nameof(RoutesCalendarApplicationAndWeather), RoutesCalendarApplicationAndWeather);
        Run(nameof(RoutesApplicationWhenCategoryBecomesStable), RoutesApplicationWhenCategoryBecomesStable);
        Run(nameof(RoutesAwayReturnAndBusyMilestones), RoutesAwayReturnAndBusyMilestones);
        Run(nameof(DailyTriggersWaitForActiveWorkAndRecover), DailyTriggersWaitForActiveWorkAndRecover);
        Run(nameof(DailyTriggersStopAtFifteenPresentedBehaviors), DailyTriggersStopAtFifteenPresentedBehaviors);
        Run(nameof(UnpresentedDailyAttemptsUseBoundedRetry), UnpresentedDailyAttemptsUseBoundedRetry);
        Run(nameof(DailyTriggersAvoidFullscreenIdleAndEntertainment), DailyTriggersAvoidFullscreenIdleAndEntertainment);
        Run(nameof(PersistsAcceptedDailyOpportunities), PersistsAcceptedDailyOpportunities);
        Run(nameof(PersistenceFailureDoesNotReportSuccess), PersistenceFailureDoesNotReportSuccess);
        Run(nameof(MigratesLegacyDailyOpportunities), MigratesLegacyDailyOpportunities);
        Run(nameof(IgnoresDuplicateRevisions), IgnoresDuplicateRevisions);
    }

    private static void RoutesApplicationWhenCategoryBecomesStable()
    {
        var router = new BehaviorContextTriggerRouter();
        DateTimeOffset start = new(
            2026,
            7,
            28,
            10,
            0,
            0,
            TimeSpan.FromHours(8));

        string[] changing = router.Observe(
                Snapshot(1, start) with
                {
                    ApplicationCategory = "office",
                    ApplicationCategoryAvailable = true,
                    ApplicationStableFor = TimeSpan.Zero,
                })
            .Select(static signal => signal.Tag)
            .ToArray();
        BehaviorTestCheck.False(
            changing.Contains("trigger:app:office"));

        string[] stable = router.Observe(
                Snapshot(2, start.AddSeconds(16)) with
                {
                    ApplicationCategory = "office",
                    ApplicationCategoryAvailable = true,
                    ApplicationStableFor = TimeSpan.FromSeconds(16),
                })
            .Select(static signal => signal.Tag)
            .ToArray();
        BehaviorTestCheck.True(
            stable.Contains("trigger:app:office"));

        string[] stillStable = router.Observe(
                Snapshot(3, start.AddSeconds(30)) with
                {
                    ApplicationCategory = "office",
                    ApplicationCategoryAvailable = true,
                    ApplicationStableFor = TimeSpan.FromSeconds(30),
                })
            .Select(static signal => signal.Tag)
            .ToArray();
        BehaviorTestCheck.False(
            stillStable.Contains("trigger:app:office"));
    }

    private static void RoutesCalendarApplicationAndWeather()
    {
        var router = new BehaviorContextTriggerRouter();
        BehaviorContextSnapshot context = Snapshot(
            revision: 1,
            localNow: new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.FromHours(8))) with
        {
            ApplicationCategory = "browser",
            ApplicationCategoryAvailable = true,
            ApplicationStableFor = TimeSpan.FromSeconds(20),
            WeatherKind = "clear",
            WeatherAvailable = true,
            TemperatureCelsius = 31,
        };

        string[] tags = router.Observe(context)
            .Select(static signal => signal.Tag)
            .ToArray();

        BehaviorTestCheck.True(tags.Contains("trigger:time:morning"));
        BehaviorTestCheck.True(tags.Contains("trigger:time:holiday"));
        BehaviorTestCheck.True(tags.Contains("trigger:app:browser"));
        BehaviorTestCheck.True(tags.Contains("trigger:weather:hot"));
        BehaviorTestCheck.False(tags.Contains("trigger:weather:clear"));
        BehaviorTestCheck.True(
            router.Observe(
                    context with
                    {
                        Revision = 2,
                        LocalNow = context.LocalNow.AddMinutes(1),
                    })
                .Any(static signal =>
                signal.Tag == "trigger:time:morning" &&
                signal.TimeToLive == TimeSpan.FromMinutes(30)));
        BehaviorTestCheck.True(
            router.Observe(
                    context with
                    {
                        Revision = 3,
                        LocalNow = context.LocalNow.AddMinutes(2),
                    })
                .Any(static signal =>
                signal.Tag == "trigger:weather:hot" &&
                signal.TimeToLive == TimeSpan.FromHours(6)));
    }

    private static void RoutesAwayReturnAndBusyMilestones()
    {
        var router = new BehaviorContextTriggerRouter();
        DateTimeOffset start = new(
            2026,
            7,
            28,
            10,
            0,
            0,
            TimeSpan.FromHours(8));
        _ = router.Observe(Snapshot(1, start));

        string[] away = router.Observe(
                Snapshot(2, start.AddMinutes(5)) with
                {
                    IdleFor = TimeSpan.FromMinutes(5),
                })
            .Select(static signal => signal.Tag)
            .ToArray();
        BehaviorTestCheck.True(away.Contains("trigger:user:away"));
        BehaviorTestCheck.True(away.Contains("trigger:user:idle"));

        string[] returned = router.Observe(
                Snapshot(3, start.AddMinutes(6)))
            .Select(static signal => signal.Tag)
            .ToArray();
        BehaviorTestCheck.True(returned.Contains("trigger:user:return"));

        _ = router.Observe(
            Snapshot(4, start.AddMinutes(7)) with { IsBusy = true });
        string[] work25 = router.Observe(
                Snapshot(5, start.AddMinutes(32)) with { IsBusy = true })
            .Select(static signal => signal.Tag)
            .ToArray();
        BehaviorTestCheck.True(work25.Contains("trigger:user:work25"));

        string[] work50 = router.Observe(
                Snapshot(6, start.AddMinutes(57)) with { IsBusy = true })
            .Select(static signal => signal.Tag)
            .ToArray();
        BehaviorTestCheck.True(work50.Contains("trigger:user:work50"));
    }

    private static void IgnoresDuplicateRevisions()
    {
        var router = new BehaviorContextTriggerRouter();
        BehaviorContextSnapshot context = ActiveWorkSnapshot(
            revision: 10,
            localNow: new DateTimeOffset(
                2026,
                7,
                28,
                12,
                0,
                0,
                TimeSpan.FromHours(8)));
        BehaviorTestCheck.True(router.Observe(context).Count > 0);
        BehaviorTestCheck.Equal(0, router.Observe(context).Count);
        BehaviorTestCheck.Equal(
            0,
            router.Observe(context with { Revision = 9 }).Count);
    }

    private static void DailyTriggersWaitForActiveWorkAndRecover()
    {
        var ledger = new InMemoryBehaviorDailyOpportunityLedger();
        var router = new BehaviorContextTriggerRouter(ledger);
        DateTimeOffset morning = new(
            2026,
            7,
            29,
            7,
            30,
            0,
            TimeSpan.FromHours(8));
        BehaviorContextSnapshot weather = Snapshot(1, morning) with
        {
            WeatherAvailable = true,
            WeatherKind = "clear",
            TemperatureCelsius = 27,
        };

        BehaviorContextTriggerSignal[] beforeWork =
            router.Observe(weather).ToArray();
        BehaviorTestCheck.False(
            beforeWork.Any(static signal =>
                signal.Source is
                    BehaviorRequestSource.Scheduled or
                    BehaviorRequestSource.Weather));

        BehaviorContextTriggerSignal[] duringWork = router.Observe(
                ActiveWorkSnapshot(2, morning.AddMinutes(1)) with
                {
                    WeatherAvailable = true,
                    WeatherKind = "clear",
                    TemperatureCelsius = 27,
                })
            .Where(static signal => signal.DailyOpportunity is not null)
            .ToArray();
        BehaviorTestCheck.True(
            duringWork.Any(static signal =>
                signal.Tag == "trigger:time:morning"));
        BehaviorTestCheck.True(
            duringWork.Any(static signal =>
                signal.Tag == "trigger:weather:clear"));
        foreach (BehaviorContextTriggerSignal signal in duringWork)
        {
            BehaviorTestCheck.True(
                router.MarkPresented(
                    signal.DailyOpportunity!,
                    signal.DailyOpportunity!.ObservedAt));
        }

        BehaviorContextTriggerSignal[] tooSoon = router.Observe(
                ActiveWorkSnapshot(3, morning.AddMinutes(2)) with
                {
                    WeatherAvailable = true,
                    WeatherKind = "clear",
                    TemperatureCelsius = 27,
                })
            .Where(static signal => signal.DailyOpportunity is not null)
            .ToArray();
        BehaviorTestCheck.Equal(0, tooSoon.Length);

        BehaviorContextTriggerSignal[] recovered = router.Observe(
                ActiveWorkSnapshot(4, morning.AddMinutes(31)) with
                {
                    WeatherAvailable = true,
                    WeatherKind = "clear",
                    TemperatureCelsius = 27,
                })
            .Where(static signal => signal.DailyOpportunity is not null)
            .ToArray();
        BehaviorTestCheck.True(
            recovered.Any(static signal =>
                signal.Tag == "trigger:time:morning" &&
                signal.DailyOpportunity?.AcceptedCount == 1));
        BehaviorTestCheck.True(
            recovered.Any(static signal =>
                signal.Tag == "trigger:weather:clear" &&
                signal.DailyOpportunity?.AcceptedCount == 1));

        BehaviorContextTriggerSignal[] nextDay = router.Observe(
                ActiveWorkSnapshot(5, morning.AddDays(1)) with
                {
                    WeatherAvailable = true,
                    WeatherKind = "clear",
                    TemperatureCelsius = 27,
                })
            .Where(static signal => signal.DailyOpportunity is not null)
            .ToArray();
        BehaviorTestCheck.True(
            nextDay.Any(static signal =>
                signal.Tag == "trigger:time:morning"));
        BehaviorTestCheck.True(
            nextDay.Any(static signal =>
                signal.Tag == "trigger:weather:clear" &&
                signal.DailyOpportunity?.AcceptedCount == 0));
    }

    private static void DailyTriggersStopAtFifteenPresentedBehaviors()
    {
        var ledger = new InMemoryBehaviorDailyOpportunityLedger();
        var router = new BehaviorContextTriggerRouter(ledger);
        DateTimeOffset saturday = new(
            2026,
            8,
            1,
            10,
            0,
            0,
            TimeSpan.FromHours(8));
        long revision = 1;

        for (var accepted = 0;
             accepted <
             BehaviorContextTriggerRouter
                 .DailyOpportunityMaximumAcceptedCount;
             accepted++)
        {
            DateTimeOffset now = saturday.AddMinutes(30 * accepted);
            BehaviorContextTriggerSignal signal = router.Observe(
                    ActiveWorkSnapshot(revision++, now))
                .Single(static candidate =>
                    candidate.Tag == "trigger:time:weekend");
            BehaviorTestCheck.Equal(
                accepted,
                signal.DailyOpportunity?.AcceptedCount ?? -1);

            if (accepted == 0)
            {
                BehaviorContextTriggerSignal unpresentedRetry =
                    router.Observe(
                            ActiveWorkSnapshot(
                                revision++,
                                now.AddMinutes(1)))
                        .Single(static candidate =>
                            candidate.Tag == "trigger:time:weekend");
                BehaviorTestCheck.Equal(
                    0,
                    unpresentedRetry.DailyOpportunity?.AcceptedCount ?? -1);
                BehaviorTestCheck.Equal(
                    0,
                    ledger.Read(
                            "time:weekend",
                            signal.DailyOpportunity!.LocalDate)
                        .AcceptedCount);
            }

            BehaviorTestCheck.True(
                router.MarkPresented(
                    signal.DailyOpportunity!,
                    now));
        }

        BehaviorTestCheck.False(
            router.Observe(
                    ActiveWorkSnapshot(
                        revision++,
                        saturday.AddHours(8)))
                .Any(static signal =>
                    signal.Tag == "trigger:time:weekend"));
        BehaviorTestCheck.Equal(
            BehaviorContextTriggerRouter
                .DailyOpportunityMaximumAcceptedCount,
            ledger.Read(
                    "time:weekend",
                    DateOnly.FromDateTime(saturday.Date))
                .AcceptedCount);

        BehaviorContextTriggerSignal nextDay = router.Observe(
                ActiveWorkSnapshot(
                    revision,
                    saturday.AddDays(1)))
            .Single(static signal =>
                signal.Tag == "trigger:time:weekend");
        BehaviorTestCheck.Equal(
            0,
            nextDay.DailyOpportunity?.AcceptedCount ?? -1);
    }

    private static void UnpresentedDailyAttemptsUseBoundedRetry()
    {
        var router = new BehaviorContextTriggerRouter();
        DateTimeOffset saturday = new(
            2026,
            8,
            1,
            10,
            0,
            0,
            TimeSpan.FromHours(8));

        BehaviorContextTriggerSignal first = router.Observe(
                ActiveWorkSnapshot(1, saturday))
            .Single(static signal =>
                signal.Tag == "trigger:time:weekend");
        BehaviorTestCheck.Equal(
            0,
            first.DailyOpportunity?.AcceptedCount ?? -1);
        BehaviorTestCheck.False(
            router.Observe(
                    ActiveWorkSnapshot(
                        2,
                        saturday.AddSeconds(59)))
                .Any(static signal =>
                    signal.Tag == "trigger:time:weekend"));
        BehaviorTestCheck.True(
            router.Observe(
                    ActiveWorkSnapshot(
                        3,
                        saturday.AddMinutes(1)))
                .Any(static signal =>
                    signal.Tag == "trigger:time:weekend"));
    }

    private static void DailyTriggersAvoidFullscreenIdleAndEntertainment()
    {
        var router = new BehaviorContextTriggerRouter();
        DateTimeOffset morning = new(
            2026,
            7,
            29,
            8,
            0,
            0,
            TimeSpan.FromHours(8));

        BehaviorContextSnapshot[] blocked =
        [
            ActiveWorkSnapshot(1, morning) with { IsFullscreen = true },
            ActiveWorkSnapshot(2, morning.AddSeconds(1)) with
            {
                IdleFor = TimeSpan.FromSeconds(90),
            },
            ActiveWorkSnapshot(3, morning.AddSeconds(2)) with
            {
                ApplicationCategory = "game",
            },
            ActiveWorkSnapshot(4, morning.AddSeconds(3)) with
            {
                ApplicationCategory = "media_player",
            },
            ActiveWorkSnapshot(5, morning.AddSeconds(4)) with
            {
                IsLocked = true,
            },
        ];
        foreach (BehaviorContextSnapshot context in blocked)
        {
            BehaviorTestCheck.False(
                router.Observe(context)
                    .Any(static signal =>
                        signal.DailyOpportunity is not null));
        }

        BehaviorTestCheck.True(
            router.Observe(
                    ActiveWorkSnapshot(6, morning.AddSeconds(5)))
                .Any(static signal =>
                    signal.DailyOpportunity is not null));
    }

    private static void PersistsAcceptedDailyOpportunities()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"gulu-daily-opportunity-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "state.json");
        try
        {
            var first = new BehaviorDailyOpportunityStore(path);
            var date = new DateOnly(2026, 7, 29);
            DateTimeOffset firstAccepted = new(
                2026,
                7,
                29,
                9,
                0,
                0,
                TimeSpan.FromHours(8));
            DateTimeOffset secondAccepted = firstAccepted.AddHours(1);
            BehaviorTestCheck.Equal(
                0,
                first.Read("weather:daily", date).AcceptedCount);
            BehaviorTestCheck.True(
                first.TryRecordAccepted(
                    "weather:daily",
                    date,
                    firstAccepted,
                    maximumAcceptedCount: 15));
            BehaviorTestCheck.True(
                first.TryRecordAccepted(
                    "weather:daily",
                    date,
                    secondAccepted,
                    maximumAcceptedCount: 15));

            var reloaded = new BehaviorDailyOpportunityStore(path);
            BehaviorDailyOpportunityUsage usage =
                reloaded.Read("weather:daily", date);
            BehaviorTestCheck.Equal(2, usage.AcceptedCount);
            BehaviorTestCheck.Equal(
                secondAccepted,
                usage.LastAcceptedAt);
            BehaviorTestCheck.Equal(
                0,
                reloaded.Read(
                        "weather:daily",
                        date.AddDays(1))
                    .AcceptedCount);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void MigratesLegacyDailyOpportunities()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"gulu-daily-opportunity-legacy-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "state.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                path,
                """
                {
                  "CompletedOn": {
                    "weather:daily": "2026-07-29"
                  }
                }
                """);

            var store = new BehaviorDailyOpportunityStore(path);
            BehaviorDailyOpportunityUsage migrated = store.Read(
                "weather:daily",
                new DateOnly(2026, 7, 29));
            BehaviorTestCheck.Equal(1, migrated.AcceptedCount);
            BehaviorTestCheck.Equal(
                null,
                migrated.LastAcceptedAt);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void PersistenceFailureDoesNotReportSuccess()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"gulu-daily-opportunity-failure-{Guid.NewGuid():N}");
        string blocker = Path.Combine(directory, "not-a-directory");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(blocker, "block");
            var store = new BehaviorDailyOpportunityStore(
                Path.Combine(blocker, "state.json"));
            var date = new DateOnly(2026, 7, 29);
            DateTimeOffset presentedAt = new(
                2026,
                7,
                29,
                9,
                0,
                0,
                TimeSpan.FromHours(8));

            BehaviorTestCheck.False(
                store.TryRecordAccepted(
                    "weather:daily",
                    date,
                    presentedAt,
                    maximumAcceptedCount: 15));
            BehaviorTestCheck.Equal(
                1,
                store.Read("weather:daily", date).AcceptedCount);
            BehaviorTestCheck.Equal(
                presentedAt,
                store.Read("weather:daily", date).LastAcceptedAt);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static BehaviorContextSnapshot Snapshot(
        long revision,
        DateTimeOffset localNow) =>
        new()
        {
            Revision = revision,
            LocalNow = localNow,
        };

    private static BehaviorContextSnapshot ActiveWorkSnapshot(
        long revision,
        DateTimeOffset localNow) =>
        Snapshot(revision, localNow) with
        {
            ApplicationCategory = "office",
            ApplicationCategoryAvailable = true,
            ApplicationStableFor = TimeSpan.FromSeconds(20),
        };

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(BehaviorContextTriggerRouterTests)}.{name}");
    }
}
