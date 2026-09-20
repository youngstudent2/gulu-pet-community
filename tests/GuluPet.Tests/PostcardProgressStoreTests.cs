using System.IO;
using GuluPet.Postcards;

namespace GuluPet.Tests;

internal static class PostcardProgressStoreTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 8, 1, 4, 0, 0, TimeSpan.Zero);

    public static void RunAll()
    {
        Run(
            nameof(MissingStateReturnsSchemaTwoAtHome),
            MissingStateReturnsSchemaTwoAtHome);
        Run(
            nameof(SaveRoundTripsOutingAndAtomicallyReplaces),
            SaveRoundTripsOutingAndAtomicallyReplaces);
        Run(
            nameof(WaitingAtDoorSurvivesRestartAndClockRollback),
            WaitingAtDoorSurvivesRestartAndClockRollback);
        Run(
            nameof(SchemaOneMigratesWithoutCreatingUnlocks),
            SchemaOneMigratesWithoutCreatingUnlocks);
        Run(
            nameof(MigrationPreservesRealUnlockMetadata),
            MigrationPreservesRealUnlockMetadata);
        Run(
            nameof(MigratedStateWritesBackAsSchemaTwo),
            MigratedStateWritesBackAsSchemaTwo);
        Run(
            nameof(RejectsCorruptFutureAndUnknownDocuments),
            RejectsCorruptFutureAndUnknownDocuments);
        Run(
            nameof(RejectsInvalidSchemaTwoOuting),
            RejectsInvalidSchemaTwoOuting);
        Run(
            nameof(ConcurrentSavesRemainWholeSchemaTwoDocuments),
            ConcurrentSavesRemainWholeSchemaTwoDocuments);
    }

    private static void MissingStateReturnsSchemaTwoAtHome()
    {
        WithStore(
            (store, _) =>
            {
                PostcardCollectionState state = store.Load();

                BehaviorTestCheck.Equal(2, state.SchemaVersion);
                BehaviorTestCheck.Equal(0L, state.TotalInputCount);
                BehaviorTestCheck.Equal(0, state.Unlocked.Count);
                BehaviorTestCheck.True(state.CurrentOuting is null);
                BehaviorTestCheck.True(state.UpdatedAtUtc != default);
            });
    }

    private static void SaveRoundTripsOutingAndAtomicallyReplaces()
    {
        WithStore(
            (store, directory) =>
            {
                var traveling = new PostcardCollectionState
                {
                    TotalInputCount = 12_345,
                    CurrentOuting = new PostcardOutingRecord
                    {
                        StartedAtUtc = BaseTime,
                        ReadyAtUtc = BaseTime.AddMinutes(30),
                    },
                    UpdatedAtUtc = BaseTime,
                };
                store.Save(traveling);

                PostcardCollectionState loaded = store.Load();
                BehaviorTestCheck.Equal(2, loaded.SchemaVersion);
                BehaviorTestCheck.Equal(12_345L, loaded.TotalInputCount);
                BehaviorTestCheck.Equal(
                    BaseTime.AddMinutes(30),
                    loaded.CurrentOuting!.ReadyAtUtc);
                BehaviorTestCheck.True(
                    loaded.CurrentOuting.ArrivedAtUtc is null);

                var waitingAtDoor = traveling with
                {
                    CurrentOuting = traveling.CurrentOuting with
                    {
                        ArrivedAtUtc = BaseTime.AddMinutes(30),
                    },
                    UpdatedAtUtc = BaseTime.AddMinutes(30),
                };
                store.Save(waitingAtDoor);

                loaded = store.Load();
                BehaviorTestCheck.Equal(
                    BaseTime.AddMinutes(30),
                    loaded.CurrentOuting!.ArrivedAtUtc!.Value);
                BehaviorTestCheck.Equal(
                    0,
                    Directory.GetFiles(directory, "*.tmp").Length);
            });
    }

    private static void WaitingAtDoorSurvivesRestartAndClockRollback()
    {
        WithStore(
            (store, _) =>
            {
                var waitingAtDoor = new PostcardCollectionState
                {
                    CurrentOuting = new PostcardOutingRecord
                    {
                        StartedAtUtc = BaseTime,
                        ReadyAtUtc = BaseTime.AddMinutes(30),
                        ArrivedAtUtc = BaseTime.AddMinutes(31),
                    },
                    UpdatedAtUtc = BaseTime.AddMinutes(31),
                };
                store.Save(waitingAtDoor);

                var restartedTracker = new PostcardProgressTracker(
                    CreateCatalog(2),
                    store.Load());
                PostcardOutingStatus status =
                    restartedTracker.GetOutingStatus(
                        BaseTime.AddMinutes(-5));

                BehaviorTestCheck.Equal(
                    PostcardOutingPhase.WaitingAtDoor,
                    status.Phase);
                BehaviorTestCheck.Equal(
                    BaseTime.AddMinutes(31),
                    status.ArrivedAtUtc!.Value);
                BehaviorTestCheck.Equal("card-1", status.NextPostcardId!);
            });
    }

    private static void SchemaOneMigratesWithoutCreatingUnlocks()
    {
        WithStore(
            (store, _) =>
            {
                File.WriteAllText(
                    store.StatePath,
                    """
                    {
                      "schemaVersion": 1,
                      "totalInputCount": 50000,
                      "unlocked": [
                        {
                          "postcardId": "card-1",
                          "unlockedAtUtc": "2026-08-01T04:00:00Z"
                        }
                      ],
                      "updatedAtUtc": "2026-08-01T04:00:00Z"
                    }
                    """);

                PostcardCollectionState migrated = store.Load();
                var tracker = new PostcardProgressTracker(
                    CreateCatalog(3),
                    migrated);

                BehaviorTestCheck.Equal(2, migrated.SchemaVersion);
                BehaviorTestCheck.Equal(50_000L, migrated.TotalInputCount);
                BehaviorTestCheck.SequenceEqual(
                    ["card-1"],
                    migrated.Unlocked.Select(
                        static record => record.PostcardId));
                BehaviorTestCheck.True(migrated.CurrentOuting is null);
                BehaviorTestCheck.SequenceEqual(
                    ["card-1"],
                    tracker.UnlockedDefinitions.Select(
                        static postcard => postcard.Id));
                BehaviorTestCheck.Equal(
                    "card-2",
                    tracker.GetOutingStatus(BaseTime).NextPostcardId!);

                PostcardProgressUpdate ignored = tracker.ObserveSessionCounts(
                    long.MaxValue,
                    long.MaxValue,
                    BaseTime.AddMinutes(1));
                BehaviorTestCheck.Equal(0, ignored.NewlyUnlocked.Count);
            });
    }

    private static void MigrationPreservesRealUnlockMetadata()
    {
        WithStore(
            (store, _) =>
            {
                File.WriteAllText(
                    store.StatePath,
                    """
                    {
                      "schemaVersion": 1,
                      "totalInputCount": 2000,
                      "unlocked": [
                        {
                          "postcardId": "card-1",
                          "unlockedAtUtc": "2026-08-01T04:00:00Z",
                          "notificationAcknowledgedAtUtc": "2026-08-01T04:01:00Z"
                        },
                        {
                          "postcardId": "card-2",
                          "unlockedAtUtc": "2026-08-01T04:02:00Z"
                        }
                      ],
                      "pendingCatalogSummary": {
                        "postcardIds": ["card-2"],
                        "totalUnlockedCount": 2,
                        "createdAtUtc": "2026-08-01T04:03:00Z"
                      },
                      "pendingTravelEcho": {
                        "newlyEarnedStampCount": 1,
                        "totalStampCount": 1,
                        "createdAtUtc": "2026-08-01T04:03:00Z"
                      },
                      "updatedAtUtc": "2026-08-01T04:03:00Z"
                    }
                    """);

                PostcardCollectionState migrated = store.Load();

                BehaviorTestCheck.Equal(2, migrated.Unlocked.Count);
                BehaviorTestCheck.Equal(
                    BaseTime.AddMinutes(1),
                    migrated.Unlocked[0].NotificationAcknowledgedAtUtc!.Value);
                BehaviorTestCheck.SequenceEqual(
                    ["card-2"],
                    migrated.PendingCatalogSummary!.PostcardIds);
                BehaviorTestCheck.Equal(
                    1L,
                    migrated.PendingTravelEcho!.NewlyEarnedStampCount);
            });
    }

    private static void MigratedStateWritesBackAsSchemaTwo()
    {
        WithStore(
            (store, _) =>
            {
                File.WriteAllText(
                    store.StatePath,
                    """
                    {
                      "schemaVersion": 1,
                      "totalInputCount": 999999,
                      "unlocked": [],
                      "updatedAtUtc": "2026-08-01T04:00:00Z"
                    }
                    """);

                PostcardCollectionState migrated = store.Load();
                store.Save(migrated);

                string rewritten = File.ReadAllText(store.StatePath);
                BehaviorTestCheck.True(rewritten.Contains(
                    "\"schemaVersion\": 2",
                    StringComparison.Ordinal));
                BehaviorTestCheck.False(rewritten.Contains(
                    "\"currentOuting\": {",
                    StringComparison.Ordinal));
                BehaviorTestCheck.Equal(999_999L, store.Load().TotalInputCount);
            });
    }

    private static void RejectsCorruptFutureAndUnknownDocuments()
    {
        WithStore(
            (store, _) =>
            {
                File.WriteAllText(store.StatePath, "{");
                BehaviorTestCheck.Throws<InvalidDataException>(
                    () => store.Load());

                File.WriteAllText(
                    store.StatePath,
                    """
                    {
                      "schemaVersion": 3,
                      "unlocked": [],
                      "updatedAtUtc": "2026-08-01T04:00:00Z"
                    }
                    """);
                BehaviorTestCheck.Throws<InvalidDataException>(
                    () => store.Load());

                File.WriteAllText(
                    store.StatePath,
                    """
                    {
                      "schemaVersion": 1,
                      "totalInputCount": 0,
                      "unlocked": [],
                      "mystery": true,
                      "updatedAtUtc": "2026-08-01T04:00:00Z"
                    }
                    """);
                BehaviorTestCheck.Throws<InvalidDataException>(
                    () => store.Load());

                File.WriteAllText(
                    store.StatePath,
                    """
                    {
                      "schemaVersion": 2,
                      "totalInputCount": 0,
                      "unlocked": [],
                      "mystery": true,
                      "updatedAtUtc": "2026-08-01T04:00:00Z"
                    }
                    """);
                BehaviorTestCheck.Throws<InvalidDataException>(
                    () => store.Load());
            });
    }

    private static void RejectsInvalidSchemaTwoOuting()
    {
        WithStore(
            (store, _) =>
            {
                File.WriteAllText(
                    store.StatePath,
                    """
                    {
                      "schemaVersion": 2,
                      "totalInputCount": 0,
                      "unlocked": [],
                      "currentOuting": {
                        "startedAtUtc": "2026-08-01T04:00:00Z",
                        "readyAtUtc": "2026-08-01T04:29:00Z"
                      },
                      "updatedAtUtc": "2026-08-01T04:00:00Z"
                    }
                    """);

                BehaviorTestCheck.Throws<InvalidDataException>(
                    () => store.Load());
            });
    }

    private static void ConcurrentSavesRemainWholeSchemaTwoDocuments()
    {
        WithStore(
            (store, _) =>
            {
                Task[] saves = Enumerable.Range(1, 12)
                    .Select(index => Task.Run(
                        () => store.Save(
                            new PostcardCollectionState
                            {
                                TotalInputCount = index,
                                UpdatedAtUtc = BaseTime.AddSeconds(index),
                            })))
                    .ToArray();
                Task.WaitAll(saves);

                PostcardCollectionState loaded = store.Load();
                BehaviorTestCheck.True(
                    loaded.TotalInputCount is >= 1 and <= 12);
                BehaviorTestCheck.Equal(2, loaded.SchemaVersion);
                BehaviorTestCheck.True(File.ReadAllText(store.StatePath).Contains(
                    "\"schemaVersion\": 2",
                    StringComparison.Ordinal));
            });
    }

    private static PostcardCatalog CreateCatalog(int count) =>
        new(
            Enumerable.Range(1, count)
                .Select(index => new PostcardDefinition(
                    $"card-{index}",
                    $"明信片 {index}",
                    "中国",
                    Path.GetFullPath($"card-{index}.jpg"),
                    index,
                    $"咕噜的明信片 {index}")));

    private static void WithStore(
        Action<PostcardProgressStore, string> assertion)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.PostcardProgressStore.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new PostcardProgressStore(
                Path.Combine(directory, "postcards.json"));
            assertion(store, directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(PostcardProgressStoreTests)}.{name}");
    }
}
