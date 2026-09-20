using System.IO;
using GuluPet.Care;

namespace GuluPet.Tests;

internal static class CareStateStoreTests
{
    public static void RunAll()
    {
        Run(
            nameof(MissingStateUsesFullDefaultsWithoutCreatingAFile),
            MissingStateUsesFullDefaultsWithoutCreatingAFile);
        Run(
            nameof(AtomicReplacementSurvivesStoreRestart),
            AtomicReplacementSurvivesStoreRestart);
        Run(
            nameof(CorruptStateIsQuarantinedBeforeDefaulting),
            CorruptStateIsQuarantinedBeforeDefaulting);
        Run(
            nameof(StrictJsonRejectsMissingAndUnknownMembers),
            StrictJsonRejectsMissingAndUnknownMembers);
        Run(
            nameof(InvalidSaveCannotReplaceTheLastValidState),
            InvalidSaveCannotReplaceTheLastValidState);
    }

    private static void MissingStateUsesFullDefaultsWithoutCreatingAFile()
    {
        WithTemporaryStore(
            store =>
            {
                CareState state = store.Load();

                BehaviorTestCheck.Equal(
                    CareState.CurrentSchemaVersion,
                    state.SchemaVersion);
                BehaviorTestCheck.Close(
                    CareState.FullLevel,
                    state.FoodLevel);
                BehaviorTestCheck.Close(
                    CareState.FullLevel,
                    state.WaterLevel);
                BehaviorTestCheck.False(File.Exists(store.StatePath));
            });
    }

    private static void AtomicReplacementSurvivesStoreRestart()
    {
        WithTemporaryStore(
            store =>
            {
                store.Save(CareState.Create(82, 73));
                store.Save(CareState.Create(58, 41));

                var restarted = new CareStateStore(store.StatePath);
                CareState loaded = restarted.Load();

                BehaviorTestCheck.Close(58, loaded.FoodLevel);
                BehaviorTestCheck.Close(41, loaded.WaterLevel);
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
                    "{\"schemaVersion\":1,\"foodLevel\":95";
                string directory = Path.GetDirectoryName(store.StatePath)!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(store.StatePath, corruptContents);

                CareState recovered = store.Load();

                BehaviorTestCheck.Close(
                    CareState.FullLevel,
                    recovered.FoodLevel);
                BehaviorTestCheck.Close(
                    CareState.FullLevel,
                    recovered.WaterLevel);
                BehaviorTestCheck.False(File.Exists(store.StatePath));
                string[] quarantined = Directory.GetFiles(
                    directory,
                    "care.corrupt-*.json");
                BehaviorTestCheck.Equal(1, quarantined.Length);
                BehaviorTestCheck.Equal(
                    corruptContents,
                    File.ReadAllText(quarantined[0]));
            });
    }

    private static void StrictJsonRejectsMissingAndUnknownMembers()
    {
        VerifyInvalidJsonIsQuarantined(
            "{\"schemaVersion\":1,\"foodLevel\":50}");
        VerifyInvalidJsonIsQuarantined(
            "{\"schemaVersion\":1,\"foodLevel\":50," +
            "\"waterLevel\":60,\"offlineElapsedSeconds\":1}");
    }

    private static void InvalidSaveCannotReplaceTheLastValidState()
    {
        WithTemporaryStore(
            store =>
            {
                store.Save(CareState.Create(48, 57));
                _ = BehaviorTestCheck.Throws<InvalidDataException>(
                    () => store.Save(
                        new CareState
                        {
                            FoodLevel = double.NaN,
                            WaterLevel = 12,
                        }));

                CareState loaded = new CareStateStore(store.StatePath).Load();
                BehaviorTestCheck.Close(48, loaded.FoodLevel);
                BehaviorTestCheck.Close(57, loaded.WaterLevel);
            });
    }

    private static void VerifyInvalidJsonIsQuarantined(string contents)
    {
        WithTemporaryStore(
            store =>
            {
                string directory = Path.GetDirectoryName(store.StatePath)!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(store.StatePath, contents);

                CareState recovered = store.Load();

                BehaviorTestCheck.Close(
                    CareState.FullLevel,
                    recovered.FoodLevel);
                BehaviorTestCheck.Close(
                    CareState.FullLevel,
                    recovered.WaterLevel);
                BehaviorTestCheck.False(File.Exists(store.StatePath));
                string[] quarantined = Directory.GetFiles(
                    directory,
                    "care.corrupt-*.json");
                BehaviorTestCheck.Equal(1, quarantined.Length);
                BehaviorTestCheck.Equal(
                    contents,
                    File.ReadAllText(quarantined[0]));
            });
    }

    private static void WithTemporaryStore(Action<CareStateStore> test)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.CareState.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            test(
                new CareStateStore(
                    Path.Combine(root, "care.json")));
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
            $"PASS {nameof(CareStateStoreTests)}.{name}");
    }
}
