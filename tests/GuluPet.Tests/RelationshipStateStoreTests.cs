using GuluPet.Domain;
using GuluPet.Persistence;

namespace GuluPet.Tests;

internal static class RelationshipStateStoreTests
{
    public static void RunAll()
    {
        Run(
            nameof(MissingStateUsesCompatibleDefault),
            MissingStateUsesCompatibleDefault);
        Run(
            nameof(RelationshipStagesUseStableIdsAtBoundaries),
            RelationshipStagesUseStableIdsAtBoundaries);
        Run(
            nameof(AtomicReplacementSurvivesStoreRestart),
            AtomicReplacementSurvivesStoreRestart);
        Run(
            nameof(CorruptStateIsQuarantinedBeforeDefaulting),
            CorruptStateIsQuarantinedBeforeDefaulting);
    }

    private static void MissingStateUsesCompatibleDefault()
    {
        WithTemporaryStore(
            store =>
            {
                RelationshipState state = store.Load();

                BehaviorTestCheck.Equal(
                    RelationshipState.CurrentSchemaVersion,
                    state.SchemaVersion);
                BehaviorTestCheck.Close(
                    EmotionState.DefaultAffection,
                    state.Affection);
                BehaviorTestCheck.Equal(
                    RelationshipStages.Familiar,
                    state.Stage);
                BehaviorTestCheck.True(state.UpdatedAtUtc != default);
                BehaviorTestCheck.False(File.Exists(store.StatePath));
            });
    }

    private static void RelationshipStagesUseStableIdsAtBoundaries()
    {
        BehaviorTestCheck.Equal(
            RelationshipStages.New,
            RelationshipStages.FromAffection(0));
        BehaviorTestCheck.Equal(
            RelationshipStages.New,
            RelationshipStages.FromAffection(24.999));
        BehaviorTestCheck.Equal(
            RelationshipStages.Familiar,
            RelationshipStages.FromAffection(25));
        BehaviorTestCheck.Equal(
            RelationshipStages.Familiar,
            RelationshipStages.FromAffection(49.999));
        BehaviorTestCheck.Equal(
            RelationshipStages.Close,
            RelationshipStages.FromAffection(50));
        BehaviorTestCheck.Equal(
            RelationshipStages.Close,
            RelationshipStages.FromAffection(74.999));
        BehaviorTestCheck.Equal(
            RelationshipStages.Family,
            RelationshipStages.FromAffection(75));
        BehaviorTestCheck.Equal(
            RelationshipStages.Family,
            RelationshipStages.FromAffection(100));
    }

    private static void AtomicReplacementSurvivesStoreRestart()
    {
        WithTemporaryStore(
            store =>
            {
                DateTimeOffset firstAt =
                    new(2026, 8, 1, 1, 0, 0, TimeSpan.Zero);
                DateTimeOffset secondAt = firstAt + TimeSpan.FromHours(1);
                store.Save(RelationshipState.Create(82, firstAt));
                store.Save(RelationshipState.Create(58, secondAt));

                var restarted = new RelationshipStateStore(store.StatePath);
                RelationshipState loaded = restarted.Load();

                BehaviorTestCheck.Close(58, loaded.Affection);
                BehaviorTestCheck.Equal(
                    RelationshipStages.Close,
                    loaded.Stage);
                BehaviorTestCheck.Equal(secondAt, loaded.UpdatedAtUtc);
                string directory = Path.GetDirectoryName(store.StatePath)!;
                BehaviorTestCheck.Equal(
                    0,
                    Directory.GetFiles(directory, ".*.tmp").Length);
            });
    }

    private static void CorruptStateIsQuarantinedBeforeDefaulting()
    {
        WithTemporaryStore(
            store =>
            {
                const string corruptContents =
                    "{\"schemaVersion\":1,\"affection\":95";
                string directory = Path.GetDirectoryName(store.StatePath)!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(store.StatePath, corruptContents);

                RelationshipState recovered = store.Load();

                BehaviorTestCheck.Close(
                    EmotionState.DefaultAffection,
                    recovered.Affection);
                BehaviorTestCheck.False(File.Exists(store.StatePath));
                string[] quarantined = Directory.GetFiles(
                    directory,
                    "relationship.corrupt-*.json");
                BehaviorTestCheck.Equal(1, quarantined.Length);
                BehaviorTestCheck.Equal(
                    corruptContents,
                    File.ReadAllText(quarantined[0]));
            });
    }

    private static void WithTemporaryStore(
        Action<RelationshipStateStore> test)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.RelationshipState.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            test(
                new RelationshipStateStore(
                    Path.Combine(root, "relationship.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(RelationshipStateStoreTests)}.{name}");
    }
}
