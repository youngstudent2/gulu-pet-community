using System.Runtime.ExceptionServices;
using GuluPet.Diary;
using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class DiaryWindowTests
{
    public static void RunAll()
    {
        Run(nameof(BrowsesGeneratedAndPendingDates),
            BrowsesGeneratedAndPendingDates);
        Run(nameof(FormatsReadOnlyDiaryFacts), FormatsReadOnlyDiaryFacts);
        Run(
            nameof(ReportsDisplayedDiaryAndRefreshesUnreadBadges),
            ReportsDisplayedDiaryAndRefreshesUnreadBadges);
    }

    private static void BrowsesGeneratedAndPendingDates()
    {
        RunSta(
            () =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                var generatedDate = new DateOnly(2026, 8, 2);
                var pendingDate = generatedDate.AddDays(1);
                var ledger = new DiaryLedger();
                ledger.RecordInteraction(
                    generatedDate,
                    DiaryInteractionKinds.Click,
                    now.AddDays(-1));
                ledger.RecordGenerationSuccess(
                    generatedDate,
                    CreateEntry(now.AddDays(-1)),
                    now.AddDays(-1));
                ledger.AddRuntime(pendingDate, 1801, now);

                var displayedDates = new List<DateOnly>();
                var window = new DiaryWindow();
                try
                {
                    window.DiaryDateDisplayed += (_, e) =>
                        displayedDates.Add(e.Date);
                    window.BindLedger(ledger);
                    BehaviorTestCheck.Equal(2, window.DateCount);
                    BehaviorTestCheck.Equal(
                        generatedDate,
                        window.SelectedDate!.Value);
                    BehaviorTestCheck.Equal(1, displayedDates.Count);
                    BehaviorTestCheck.Equal(
                        generatedDate,
                        displayedDates[0]);
                    BehaviorTestCheck.True(window.TrySelectDate(pendingDate));
                    BehaviorTestCheck.Equal(
                        pendingDate,
                        window.SelectedDate!.Value);
                    BehaviorTestCheck.Equal(1, displayedDates.Count);
                    BehaviorTestCheck.False(
                        window.TrySelectDate(pendingDate.AddDays(7)));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void FormatsReadOnlyDiaryFacts()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DiaryDayState day = DiaryDayState.Create(
            new DateOnly(2026, 8, 3),
            now) with
        {
            RuntimeSeconds = 4 * 3600 + 42 * 60,
            Interactions =
            [
                new DiaryInteractionCount
                {
                    Kind = DiaryInteractionKinds.Petting,
                    Count = 5,
                },
            ],
            Weather = new DiaryWeatherSnapshot
            {
                Kind = "clear",
                TemperatureCelsius = 28.6,
                ObservedAtUtc = now,
            },
            Entry = CreateEntry(now),
        };

        DiaryDayViewModel viewModel = DiaryDayViewModel.Create(day);
        BehaviorTestCheck.Equal("一篇温暖的日记", viewModel.Title);
        BehaviorTestCheck.Equal("安心", viewModel.Mood);
        BehaviorTestCheck.Equal("晴朗 · 28.6℃", viewModel.WeatherText);
        BehaviorTestCheck.Equal("4 小时 42 分钟", viewModel.RuntimeText);
        BehaviorTestCheck.Equal("5 次", viewModel.InteractionTotalText);
        BehaviorTestCheck.Equal("摸摸咕噜", viewModel.Interactions.Single().Label);
    }

    private static void ReportsDisplayedDiaryAndRefreshesUnreadBadges()
    {
        RunSta(
            () =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                DateOnly olderDate = new(2026, 8, 10);
                DateOnly latestDate = olderDate.AddDays(1);
                var ledger = new DiaryLedger();
                ledger.RecordGenerationSuccess(
                    olderDate,
                    CreateEntry(now.AddDays(-1)),
                    now.AddDays(-1));
                ledger.RecordGenerationSuccess(
                    latestDate,
                    CreateEntry(now),
                    now);

                var displayedDates = new List<DateOnly>();
                var window = new DiaryWindow();
                try
                {
                    window.DiaryDateDisplayed += (_, e) =>
                        displayedDates.Add(e.Date);
                    window.RefreshReadDiaryDates([olderDate]);
                    window.BindLedger(ledger);

                    BehaviorTestCheck.Equal(1, displayedDates.Count);
                    BehaviorTestCheck.Equal(latestDate, displayedDates[0]);
                    BehaviorTestCheck.Equal(1, window.UnreadDateCount);

                    window.RefreshReadDiaryDates([olderDate, latestDate]);
                    BehaviorTestCheck.Equal(0, window.UnreadDateCount);
                    BehaviorTestCheck.Equal(1, displayedDates.Count);

                    BehaviorTestCheck.True(
                        window.TrySelectDate(olderDate));
                    BehaviorTestCheck.Equal(2, displayedDates.Count);
                    BehaviorTestCheck.Equal(olderDate, displayedDates[1]);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static GeneratedDiaryEntry CreateEntry(
        DateTimeOffset generatedAtUtc) =>
        new()
        {
            Title = "一篇温暖的日记",
            Mood = "安心",
            Body = "我今天安静地陪着主人。",
            Model = "local-template-v1",
            GeneratedAtUtc = generatedAtUtc,
        };

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    test();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(DiaryWindowTests)}.{name}");
    }
}
