using GuluPet.Memories;

namespace GuluPet.Tests;

internal static class MemoryProgressStoreTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    public static void RunAll()
    {
        Run(
            nameof(MissingStateStartsAtZero),
            MissingStateStartsAtZero);
        Run(
            nameof(SaveRoundTripsPendingAndCompletion),
            SaveRoundTripsPendingAndCompletion);
        Run(
            nameof(CorruptStateIsQuarantinedBeforeRecovery),
            CorruptStateIsQuarantinedBeforeRecovery);
        Run(
            nameof(SavesAtomicallyReplaceWholeDocuments),
            SavesAtomicallyReplaceWholeDocuments);
    }

    private static void MissingStateStartsAtZero()
    {
        WithStore(
            (store, _) =>
            {
                MemoryProgressState state = store.Load();
                BehaviorTestCheck.Equal(
                    MemoryProgressState.CurrentSchemaVersion,
                    state.SchemaVersion);
                BehaviorTestCheck.Equal(
                    0L,
                    state.AcceptedInteractionCount);
                BehaviorTestCheck.Null(state.Pending);
                BehaviorTestCheck.Equal(0, state.Completed.Count);
                BehaviorTestCheck.True(state.UpdatedAtUtc != default);
            });
    }

    private static void SaveRoundTripsPendingAndCompletion()
    {
        WithStore(
            (store, directory) =>
            {
                var tracker = new MemoryProgressTracker();
                for (var index = 1; index <= 10; index++)
                {
                    tracker.RecordAcceptedInteraction(
                        BaseTime.AddSeconds(index));
                }

                store.Save(tracker.Current);
                MemoryProgressState pending = store.Load();
                BehaviorTestCheck.Equal(
                    10L,
                    pending.AcceptedInteractionCount);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    BehaviorTestCheck.NotNull(pending.Pending).MemoryId);

                var restarted = new MemoryProgressTracker(
                    initialState: pending);
                MemoryProgressState completed =
                    restarted.RecordNaturalPlaybackCompleted(
                        "memory-01",
                        BaseTime.AddSeconds(20));
                store.Save(completed);

                MemoryProgressState loaded = store.Load();
                BehaviorTestCheck.Null(loaded.Pending);
                BehaviorTestCheck.Equal(1, loaded.Completed.Count);
                BehaviorTestCheck.Equal(
                    "memory-01",
                    loaded.Completed[0].MemoryId);
                BehaviorTestCheck.Equal(
                    0,
                    Directory.GetFiles(directory, "*.tmp").Length);
            });
    }

    private static void CorruptStateIsQuarantinedBeforeRecovery()
    {
        WithStore(
            (store, directory) =>
            {
                string[] invalidDocuments =
                [
                    "{",
                    """
                    {
                      "schemaVersion": 2,
                      "acceptedInteractionCount": 10,
                      "pending": null,
                      "completed": [],
                      "updatedAtUtc": "2026-07-30T12:00:00Z"
                    }
                    """,
                    """
                    {
                      "schemaVersion": 1,
                      "acceptedInteractionCount": -1,
                      "pending": null,
                      "completed": [],
                      "updatedAtUtc": "2026-07-30T12:00:00Z"
                    }
                    """,
                    """
                    {
                      "schemaVersion": 1,
                      "acceptedInteractionCount": 0,
                      "pending": null,
                      "completed": [],
                      "updatedAtUtc": "2026-07-30T12:00:00Z",
                      "unexpected": true
                    }
                    """,
                ];

                for (var index = 0; index < invalidDocuments.Length; index++)
                {
                    File.WriteAllText(
                        store.StatePath,
                        invalidDocuments[index]);
                    MemoryProgressState recovered = store.Load();
                    BehaviorTestCheck.Equal(
                        0L,
                        recovered.AcceptedInteractionCount);
                    BehaviorTestCheck.False(File.Exists(store.StatePath));
                    BehaviorTestCheck.Equal(
                        index + 1,
                        Directory.GetFiles(
                            directory,
                            "memories.corrupt-*.json").Length);
                }
            });
    }

    private static void SavesAtomicallyReplaceWholeDocuments()
    {
        WithStore(
            (store, directory) =>
            {
                Task[] saves = Enumerable.Range(1, 9)
                    .Select(index => Task.Run(
                        () => store.Save(
                            new MemoryProgressState
                            {
                                AcceptedInteractionCount = index,
                                UpdatedAtUtc =
                                    BaseTime.AddSeconds(index),
                            })))
                    .ToArray();
                Task.WaitAll(saves);

                MemoryProgressState loaded = store.Load();
                BehaviorTestCheck.True(
                    loaded.AcceptedInteractionCount is >= 1 and <= 9);
                BehaviorTestCheck.True(
                    File.ReadAllText(store.StatePath).Contains(
                        "\"schemaVersion\": 1",
                        StringComparison.Ordinal));
                BehaviorTestCheck.Equal(
                    0,
                    Directory.GetFiles(directory, "*.tmp").Length);
            });
    }

    private static void WithStore(
        Action<MemoryProgressStore, string> assertion)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.MemoryProgressStore.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new MemoryProgressStore(
                Path.Combine(directory, "memories.json"));
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
            $"PASS {nameof(MemoryProgressStoreTests)}.{name}");
    }
}
