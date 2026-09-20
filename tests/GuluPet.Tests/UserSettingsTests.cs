using GuluPet.Persistence;
using GuluPet.Diary;

namespace GuluPet.Tests;

internal static class UserSettingsTests
{
    public static void RunAll()
    {
        Run(
            nameof(LegacyJsonClearsVirtualizedCoordinates),
            LegacyJsonClearsVirtualizedCoordinates);
        Run(
            nameof(CurrentPhysicalCoordinateVersionPreservesCoordinates),
            CurrentPhysicalCoordinateVersionPreservesCoordinates);
        Run(
            nameof(TickScoreLoggingSettingRoundTrips),
            TickScoreLoggingSettingRoundTrips);
        Run(
            nameof(EyeAccessorySelectionRoundTrips),
            EyeAccessorySelectionRoundTrips);
        Run(
            nameof(DiaryReadTrackingRoundTrips),
            DiaryReadTrackingRoundTrips);
        Run(
            nameof(LegacyDiaryHistoryLeavesOnlyLatestUnread),
            LegacyDiaryHistoryLeavesOnlyLatestUnread);
        Run(
            nameof(EmptyDiaryHistoryHasNoUnreadPages),
            EmptyDiaryHistoryHasNoUnreadPages);
        Run(
            nameof(ViewedDiaryPagesBecomeReadOneAtATime),
            ViewedDiaryPagesBecomeReadOneAtATime);
    }

    private static void LegacyJsonClearsVirtualizedCoordinates()
    {
        WithSettingsFile(
            """
            {
              "AlwaysOnTop": false,
              "StartWithWindows": true,
              "WindowLeft": 6650,
              "WindowTop": 436
            }
            """,
            settings =>
            {
                BehaviorTestCheck.Equal(
                    UserSettings.CurrentWindowCoordinateSpaceVersion,
                    settings.WindowCoordinateSpaceVersion);
                BehaviorTestCheck.Null(settings.WindowLeft);
                BehaviorTestCheck.Null(settings.WindowTop);
                BehaviorTestCheck.False(settings.AlwaysOnTop);
                BehaviorTestCheck.True(settings.StartWithWindows);
                BehaviorTestCheck.False(
                    settings.DiaryReadTrackingInitialized);
                BehaviorTestCheck.Equal(0, settings.ReadDiaryDates.Count);
            });
    }

    private static void CurrentPhysicalCoordinateVersionPreservesCoordinates()
    {
        WithSettingsFile(
            $$"""
            {
              "AlwaysOnTop": true,
              "StartWithWindows": false,
              "WindowLeft": -1520.5,
              "WindowTop": 490.25,
              "WindowCoordinateSpaceVersion": {{UserSettings.CurrentWindowCoordinateSpaceVersion}}
            }
            """,
            settings =>
            {
                BehaviorTestCheck.Equal(
                    UserSettings.CurrentWindowCoordinateSpaceVersion,
                    settings.WindowCoordinateSpaceVersion);
                BehaviorTestCheck.Equal(-1520.5, settings.WindowLeft);
                BehaviorTestCheck.Equal(490.25, settings.WindowTop);
                BehaviorTestCheck.True(settings.AlwaysOnTop);
                BehaviorTestCheck.False(settings.StartWithWindows);
            });
    }

    private static void TickScoreLoggingSettingRoundTrips()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.UserSettings.{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new UserSettingsStore(path);
            UserSettings settings = UserSettings.CreateDefault();
            settings.TickScoreLoggingEnabled = true;
            store.Save(settings);

            BehaviorTestCheck.True(
                store.Load().TickScoreLoggingEnabled);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void EyeAccessorySelectionRoundTrips()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.UserSettings.{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new UserSettingsStore(path);
            UserSettings settings = UserSettings.CreateDefault();
            settings.SelectedEyeAccessoryId = "neon-cyber-visor";
            store.Save(settings);

            BehaviorTestCheck.Equal(
                "neon-cyber-visor",
                store.Load().SelectedEyeAccessoryId);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void DiaryReadTrackingRoundTrips()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.UserSettings.{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new UserSettingsStore(path);
            UserSettings settings = UserSettings.CreateDefault();
            settings.DiaryReadTrackingInitialized = true;
            settings.ReadDiaryDates =
            [
                new DateOnly(2026, 8, 8),
                new DateOnly(2026, 8, 10),
            ];
            store.Save(settings);

            UserSettings restored = store.Load();
            BehaviorTestCheck.True(
                restored.DiaryReadTrackingInitialized);
            BehaviorTestCheck.Equal(2, restored.ReadDiaryDates.Count);
            BehaviorTestCheck.Equal(
                new DateOnly(2026, 8, 10),
                restored.ReadDiaryDates[1]);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void LegacyDiaryHistoryLeavesOnlyLatestUnread()
    {
        DateOnly[] diaryDates =
        [
            new DateOnly(2026, 8, 7),
            new DateOnly(2026, 8, 10),
            new DateOnly(2026, 8, 9),
        ];

        DiaryReadTrackingState state = DiaryReadTracker.Initialize(
            isInitialized: false,
            readDates: [],
            diaryDates);

        BehaviorTestCheck.True(state.IsInitialized);
        BehaviorTestCheck.Equal(
            1,
            DiaryReadTracker.GetUnreadCount(state, diaryDates));
        BehaviorTestCheck.True(
            DiaryReadTracker.IsUnread(state, new DateOnly(2026, 8, 10)));
        BehaviorTestCheck.False(
            DiaryReadTracker.IsUnread(state, new DateOnly(2026, 8, 9)));
    }

    private static void EmptyDiaryHistoryHasNoUnreadPages()
    {
        DiaryReadTrackingState state = DiaryReadTracker.Initialize(
            isInitialized: false,
            readDates: null,
            diaryDates: []);

        BehaviorTestCheck.True(state.IsInitialized);
        BehaviorTestCheck.Equal(
            0,
            DiaryReadTracker.GetUnreadCount(state, []));
    }

    private static void ViewedDiaryPagesBecomeReadOneAtATime()
    {
        DateOnly first = new(2026, 8, 10);
        DateOnly second = first.AddDays(1);
        DiaryReadTrackingState state = DiaryReadTracker.Initialize(
            isInitialized: true,
            readDates: [],
            diaryDates: [first, second]);

        state = DiaryReadTracker.MarkRead(state, first);
        BehaviorTestCheck.Equal(
            1,
            DiaryReadTracker.GetUnreadCount(state, [first, second]));
        BehaviorTestCheck.False(DiaryReadTracker.IsUnread(state, first));
        BehaviorTestCheck.True(DiaryReadTracker.IsUnread(state, second));

        state = DiaryReadTracker.MarkRead(state, second);
        BehaviorTestCheck.Equal(
            0,
            DiaryReadTracker.GetUnreadCount(state, [first, second]));
    }

    private static void WithSettingsFile(
        string json,
        Action<UserSettings> assertion)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.UserSettings.{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, json);
            var store = new UserSettingsStore(path);
            UserSettings settings = store.Load();
            assertion(settings);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(UserSettingsTests)}.{name}");
    }
}
