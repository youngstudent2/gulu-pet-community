using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GuluPet.Behavior;
using GuluPet.Diagnostics;
using GuluPet.Domain;

namespace GuluPet.Tests;

internal static class BehaviorTickLogWriterTests
{
    public static void RunAll()
    {
        Run(
            nameof(DisabledWriterDoesNotCreateLog),
            DisabledWriterDoesNotCreateLog);
        Run(
            nameof(EnabledWriterRecordsCompleteDecisionWithoutRawInputData),
            EnabledWriterRecordsCompleteDecisionWithoutRawInputData);
        Run(
            nameof(WriterRotatesBoundedLog),
            WriterRotatesBoundedLog);
        Run(
            nameof(InitialMaintenancePrunesLegacyRecordsByTimestamp),
            InitialMaintenancePrunesLegacyRecordsByTimestamp);
        Run(
            nameof(UtcDayAndSizeRotationRemainAgeBounded),
            UtcDayAndSizeRotationRemainAgeBounded);
        Run(
            nameof(TimestampedMalformedLineCannotPersistPastRetention),
            TimestampedMalformedLineCannotPersistPastRetention);
        Run(
            nameof(InvalidUtf8AndNestedTimestampsFailClosed),
            InvalidUtf8AndNestedTimestampsFailClosed);
        Run(
            nameof(HourlyMaintenanceExpiresBeforeSeventyThreeHours),
            HourlyMaintenanceExpiresBeforeSeventyThreeHours);
        Run(
            nameof(ClockRollbackPrunesRecordsThatBecomeFuture),
            ClockRollbackPrunesRecordsThatBecomeFuture);
        Run(
            nameof(DueMaintenanceIsSingleFlightBackgroundAndSurvivesDisable),
            DueMaintenanceIsSingleFlightBackgroundAndSurvivesDisable);
        Run(
            nameof(AsyncPreparationCleansWhileDisabledAndSetEnabledIsDiskFree),
            AsyncPreparationCleansWhileDisabledAndSetEnabledIsDiskFree);
        Run(
            nameof(AppendRepairsMissingTerminalNewline),
            AppendRepairsMissingTerminalNewline);
        Run(
            nameof(OversizedLegacyContentIsHardBounded),
            OversizedLegacyContentIsHardBounded);
        Run(
            nameof(PreparationDeletesOnlyExclusiveExactOrphanTemps),
            PreparationDeletesOnlyExclusiveExactOrphanTemps);
        Run(
            nameof(MaintenanceFailureRetriesOnlyAfterBackoff),
            MaintenanceFailureRetriesOnlyAfterBackoff);
        Run(
            nameof(DesktopStartupDoesNotAwaitRetentionPreparation),
            DesktopStartupDoesNotAwaitRetentionPreparation);
    }

    private static void DisabledWriterDoesNotCreateLog()
    {
        WithLogPath(
            path =>
            {
                var writer = new BehaviorTickLogWriter(path);
                writer.Write(CreateTrace());
                BehaviorTestCheck.False(File.Exists(path));
            });
    }

    private static void EnabledWriterRecordsCompleteDecisionWithoutRawInputData()
    {
        WithLogPath(
            path =>
            {
                var writer = new BehaviorTickLogWriter(path);
                writer.PrepareRetention();
                writer.SetEnabled(true);
                writer.Write(CreateTrace());

                string line = File.ReadLines(path).Single();
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                BehaviorTestCheck.Equal(
                    1,
                    root.GetProperty("schemaVersion").GetInt32());
                BehaviorTestCheck.Equal(
                    "active_blink",
                    root.GetProperty("finalResult")
                        .GetProperty("behaviorId")
                        .GetString());
                BehaviorTestCheck.Equal(
                    "queued",
                    root.GetProperty("finalResult")
                        .GetProperty("outcome")
                        .GetString());
                BehaviorTestCheck.Equal(
                    "weighted-random-draw-from-top-five;queue:Enqueued",
                    root.GetProperty("finalResult")
                        .GetProperty("reason")
                        .GetString());
                BehaviorTestCheck.Equal(
                    2,
                    root.GetProperty("candidates").GetArrayLength());
                JsonElement selected = root.GetProperty("candidates")[0];
                BehaviorTestCheck.Close(
                    42,
                    selected.GetProperty("score").GetDouble());
                BehaviorTestCheck.Close(
                    2,
                    selected.GetProperty("components")
                        .GetProperty("emotion")
                        .GetDouble());
                BehaviorTestCheck.Equal(
                    "cooldown",
                    root.GetProperty("candidates")[1]
                        .GetProperty("rejectionReason")
                        .GetString());

                BehaviorTestCheck.False(
                    line.Contains(
                        "keyboardCountSinceStart",
                        StringComparison.OrdinalIgnoreCase));
                BehaviorTestCheck.False(
                    line.Contains(
                        "mouseClickCountSinceStart",
                        StringComparison.OrdinalIgnoreCase));
                BehaviorTestCheck.False(
                    line.Contains(
                        "windowTitle",
                        StringComparison.OrdinalIgnoreCase));
            });
    }

    private static void WriterRotatesBoundedLog()
    {
        WithLogPath(
            path =>
            {
                const long maximumFileBytes = 32 * 1024;
                var writer = new BehaviorTickLogWriter(
                    path,
                    maximumFileBytes: maximumFileBytes,
                    archiveCount: 1);
                writer.PrepareRetention();
                writer.SetEnabled(true);
                for (int index = 0; index < 40; index++)
                {
                    writer.Write(CreateTrace());
                }

                BehaviorTestCheck.True(File.Exists(path));
                string archivePath = Path.Combine(
                    Path.GetDirectoryName(path)!,
                    "behavior-tick-scores.1.jsonl");
                BehaviorTestCheck.True(File.Exists(archivePath));
                BehaviorTestCheck.True(File.ReadLines(path).Any());
                BehaviorTestCheck.True(File.ReadLines(archivePath).Any());
                BehaviorTestCheck.True(
                    new FileInfo(path).Length <= maximumFileBytes);
                BehaviorTestCheck.True(
                    new FileInfo(archivePath).Length <= maximumFileBytes);
            });
    }

    private static void InitialMaintenancePrunesLegacyRecordsByTimestamp()
    {
        WithLogPath(
            path =>
            {
                var now = new DateTimeOffset(
                    2026,
                    8,
                    16,
                    12,
                    0,
                    0,
                    TimeSpan.Zero);
                DateTimeOffset cutoff = now - TimeSpan.FromHours(72);
                string archive = Path.Combine(
                    Path.GetDirectoryName(path)!,
                    "behavior-tick-scores.1.jsonl");
                string staleArchive = Path.Combine(
                    Path.GetDirectoryName(path)!,
                    "behavior-tick-scores.2.jsonl");
                string unrelated = Path.Combine(
                    Path.GetDirectoryName(path)!,
                    "behavior-tick-scores.user.jsonl");
                File.WriteAllLines(
                    path,
                    [
                        RetentionEntry(cutoff - TimeSpan.FromTicks(1)),
                        RetentionEntry(cutoff),
                        "{malformed-json",
                        RetentionEntry(now + TimeSpan.FromTicks(1)),
                        RetentionEntry(now + TimeSpan.FromDays(1)),
                    ]);
                File.WriteAllLines(
                    archive,
                    [
                        RetentionEntry(cutoff - TimeSpan.FromDays(2)),
                        RetentionEntry(now - TimeSpan.FromHours(1)),
                    ]);
                File.WriteAllText(
                    staleArchive,
                    RetentionEntry(cutoff - TimeSpan.FromDays(5)) + "\n");
                File.WriteAllText(unrelated, "user-owned-sentinel");
                DateTime restoredMtime = now.UtcDateTime.AddDays(-20);
                File.SetLastWriteTimeUtc(path, restoredMtime);

                var writer = new BehaviorTickLogWriter(
                    path,
                    timeProvider: new MutableTimeProvider(now));
                writer.PrepareRetention();

                BehaviorTestCheck.SequenceEqual(
                    [cutoff],
                    File.ReadLines(path).Select(ReadRecordedAtUtc));
                BehaviorTestCheck.SequenceEqual(
                    [now - TimeSpan.FromHours(1)],
                    File.ReadLines(archive).Select(ReadRecordedAtUtc));
                BehaviorTestCheck.False(File.Exists(staleArchive));
                BehaviorTestCheck.Equal(
                    "user-owned-sentinel",
                    File.ReadAllText(unrelated));
                BehaviorTestCheck.Equal(
                    restoredMtime,
                    File.GetLastWriteTimeUtc(path));
                BehaviorTestCheck.Equal(
                    0,
                    Directory.GetFiles(
                        Path.GetDirectoryName(path)!,
                        "*.retention-*.tmp").Length);
            });
    }

    private static void UtcDayAndSizeRotationRemainAgeBounded()
    {
        WithLogPath(
            path =>
            {
                var clock = new MutableTimeProvider(
                    new DateTimeOffset(
                        2026,
                        8,
                        16,
                        23,
                        59,
                        0,
                        TimeSpan.Zero));
                var writer = new BehaviorTickLogWriter(
                    path,
                    maximumFileBytes: 32 * 1024,
                    archiveCount: 4,
                    timeProvider: clock);
                writer.PrepareRetention();
                writer.SetEnabled(true);
                for (int index = 0; index < 20; index++)
                {
                    writer.Write(CreateTrace());
                }

                clock.UtcNow = clock.UtcNow.AddMinutes(2);
                writer.PrepareRetentionAsync().GetAwaiter().GetResult();
                for (int index = 0; index < 20; index++)
                {
                    writer.Write(CreateTrace());
                }
                BehaviorTestCheck.True(File.Exists(path));
                BehaviorTestCheck.True(File.Exists(Path.Combine(
                    Path.GetDirectoryName(path)!,
                    "behavior-tick-scores.1.jsonl")));
                BehaviorTestCheck.True(File.Exists(Path.Combine(
                    Path.GetDirectoryName(path)!,
                    "behavior-tick-scores.2.jsonl")));

                clock.UtcNow = clock.UtcNow.AddDays(4);
                writer.PrepareRetentionAsync().GetAwaiter().GetResult();
                writer.Write(CreateTrace());
                DateTimeOffset cutoff = clock.UtcNow - TimeSpan.FromHours(72);
                string[] controlledFiles =
                    [
                        path,
                        .. Enumerable.Range(1, 4).Select(index => Path.Combine(
                            Path.GetDirectoryName(path)!,
                            $"behavior-tick-scores.{index}.jsonl")),
                    ];
                DateTimeOffset[] retained = controlledFiles
                    .Where(File.Exists)
                    .SelectMany(File.ReadLines)
                    .Select(ReadRecordedAtUtc)
                    .ToArray();
                BehaviorTestCheck.True(retained.Length > 0);
                BehaviorTestCheck.True(
                    retained.All(timestamp => timestamp >= cutoff));
                BehaviorTestCheck.True(
                    controlledFiles.Count(File.Exists) <= 5);

                var restarted = new BehaviorTickLogWriter(
                    path,
                    maximumFileBytes: 32 * 1024,
                    archiveCount: 4,
                    timeProvider: clock);
                restarted.PrepareRetention();
                BehaviorTestCheck.True(
                    controlledFiles
                        .Where(File.Exists)
                        .SelectMany(File.ReadLines)
                        .Select(ReadRecordedAtUtc)
                        .All(timestamp => timestamp >= cutoff));
            });
    }

    private static void TimestampedMalformedLineCannotPersistPastRetention()
    {
        WithLogPath(
            path =>
            {
                var clock = new MutableTimeProvider(
                    new DateTimeOffset(
                        2026,
                        8,
                        16,
                        12,
                        0,
                        0,
                        TimeSpan.Zero));
                string malformed =
                    $"{{\"recordedAtUtc\":\"{clock.UtcNow:O}\",broken";
                File.WriteAllText(path, malformed + "\n");
                var writer = new BehaviorTickLogWriter(
                    path,
                    timeProvider: clock);
                writer.PrepareRetention();
                BehaviorTestCheck.Equal(malformed, File.ReadLines(path).Single());

                clock.UtcNow = clock.UtcNow.AddHours(73);
                var restarted = new BehaviorTickLogWriter(
                    path,
                    timeProvider: clock);
                restarted.PrepareRetention();
                BehaviorTestCheck.False(File.Exists(path));
            });
    }

    private static void InvalidUtf8AndNestedTimestampsFailClosed()
    {
        WithLogPath(
            path =>
            {
                var now = new DateTimeOffset(
                    2026,
                    8,
                    16,
                    12,
                    0,
                    0,
                    TimeSpan.Zero);
                DateTimeOffset cutoff = now - TimeSpan.FromHours(72);
                string nestedBeforeStaleRoot =
                    $"{{\"nested\":{{\"recordedAtUtc\":\"{now:O}\"}}," +
                    $"\"recordedAtUtc\":\"{cutoff.AddTicks(-1):O}\"," +
                    "\"id\":\"nested-must-not-win\"}";
                string nestedBeforeCurrentRoot =
                    $"{{\"nested\":{{\"recordedAtUtc\":\"{cutoff.AddTicks(-1):O}\"}}," +
                    $"\"recordedAtUtc\":\"{now:O}\"," +
                    "\"id\":\"root-wins\"}";
                string malformedTail =
                    $"{{\"recordedAtUtc\":\"{now:O}\"," +
                    "\"id\":\"malformed-tail\",broken";
                var bytes = new List<byte>();
                AppendUtf8Line(bytes, nestedBeforeStaleRoot);
                AppendUtf8Line(bytes, nestedBeforeCurrentRoot);
                AppendUtf8Line(bytes, malformedTail);
                AppendUtf8Line(
                    bytes,
                    $"{{\"recordedAtUtc\":\"{now.AddTicks(1):O}\"," +
                    "\"id\":\"future-tick\"}");
                AppendUtf8Line(
                    bytes,
                    $"{{\"recordedAtUtc\":\"{now.AddDays(1):O}\"," +
                    "\"id\":\"future-day\"}");
                bytes.AddRange(Encoding.UTF8.GetBytes(
                    "{\"recordedAtUtc\":\"2026-08-16T12:00:"));
                bytes.Add(0xff);
                bytes.AddRange(Encoding.UTF8.GetBytes("\",\"id\":\"bad-utf8\"}\n"));
                File.WriteAllBytes(path, bytes.ToArray());

                var writer = new BehaviorTickLogWriter(
                    path,
                    timeProvider: new MutableTimeProvider(now));
                writer.PrepareRetention();

                string retained = File.ReadAllText(path);
                BehaviorTestCheck.False(retained.Contains(
                    "nested-must-not-win",
                    StringComparison.Ordinal));
                BehaviorTestCheck.True(retained.Contains(
                    "root-wins",
                    StringComparison.Ordinal));
                BehaviorTestCheck.True(retained.Contains(
                    "malformed-tail",
                    StringComparison.Ordinal));
                BehaviorTestCheck.False(retained.Contains(
                    "future-tick",
                    StringComparison.Ordinal));
                BehaviorTestCheck.False(retained.Contains(
                    "future-day",
                    StringComparison.Ordinal));
                BehaviorTestCheck.False(retained.Contains(
                    "bad-utf8",
                    StringComparison.Ordinal));
            });
    }

    private static void HourlyMaintenanceExpiresBeforeSeventyThreeHours()
    {
        WithLogPath(
            path =>
            {
                var clock = new MutableTimeProvider(
                    new DateTimeOffset(
                        2026,
                        8,
                        13,
                        0,
                        1,
                        0,
                        TimeSpan.Zero));
                DateTimeOffset original = clock.UtcNow;
                var writer = new BehaviorTickLogWriter(
                    path,
                    timeProvider: clock);
                writer.PrepareRetention();
                writer.SetEnabled(true);
                writer.Write(CreateTrace());

                foreach (DateTimeOffset midnight in new[]
                {
                    new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
                })
                {
                    clock.UtcNow = midnight;
                    writer.PrepareRetentionAsync().GetAwaiter().GetResult();
                    writer.Write(CreateTrace());
                }

                clock.UtcNow = new DateTimeOffset(
                    2026,
                    8,
                    16,
                    0,
                    30,
                    0,
                    TimeSpan.Zero);
                writer.Write(CreateTrace());
                BehaviorTestCheck.True(
                    ReadAllControlledTimestamps(path).Contains(original));

                clock.UtcNow = new DateTimeOffset(
                    2026,
                    8,
                    16,
                    1,
                    0,
                    0,
                    TimeSpan.Zero);
                writer.PrepareRetentionAsync().GetAwaiter().GetResult();
                writer.Write(CreateTrace());
                DateTimeOffset[] retained = ReadAllControlledTimestamps(path);
                BehaviorTestCheck.False(retained.Contains(original));
                BehaviorTestCheck.True(
                    retained.All(timestamp =>
                        timestamp >= clock.UtcNow - TimeSpan.FromHours(72)));
                BehaviorTestCheck.True(
                    clock.UtcNow - original < TimeSpan.FromHours(73));
            });
    }

    private static void AsyncPreparationCleansWhileDisabledAndSetEnabledIsDiskFree()
    {
        WithLogPath(
            path =>
            {
                var now = new DateTimeOffset(
                    2026,
                    8,
                    16,
                    12,
                    0,
                    0,
                    TimeSpan.Zero);
                File.WriteAllText(
                    path,
                    RetentionEntry(now - TimeSpan.FromDays(5)) + "\n");
                int callerThread = Environment.CurrentManagedThreadId;
                var clock = new RecordingTimeProvider(now);
                var writer = new BehaviorTickLogWriter(
                    path,
                    timeProvider: clock);

                writer.SetEnabled(true);
                writer.SetEnabled(false);
                BehaviorTestCheck.Equal(0, clock.CallerThreads.Count);
                BehaviorTestCheck.True(File.Exists(path));

                writer.PrepareRetentionAsync().GetAwaiter().GetResult();
                BehaviorTestCheck.False(File.Exists(path));
                BehaviorTestCheck.True(clock.CallerThreads.Count > 0);
                BehaviorTestCheck.True(
                    clock.CallerThreads.All(thread => thread != callerThread));
                int callsAfterPreparation = clock.CallerThreads.Count;
                writer.SetEnabled(true);
                BehaviorTestCheck.Equal(
                    callsAfterPreparation,
                    clock.CallerThreads.Count);
            });
    }

    private static void ClockRollbackPrunesRecordsThatBecomeFuture()
    {
        WithLogPath(
            path =>
            {
                var clock = new MutableTimeProvider(
                    new DateTimeOffset(
                        2026,
                        8,
                        16,
                        12,
                        0,
                        0,
                        TimeSpan.Zero));
                DateTimeOffset beforeRollback = clock.UtcNow;
                var writer = new BehaviorTickLogWriter(
                    path,
                    timeProvider: clock);
                writer.PrepareRetention();
                writer.SetEnabled(true);
                writer.Write(CreateTrace());

                clock.UtcNow = beforeRollback - TimeSpan.FromHours(1);
                writer.PrepareRetentionAsync().GetAwaiter().GetResult();
                writer.Write(CreateTrace());

                BehaviorTestCheck.SequenceEqual(
                    [clock.UtcNow],
                    ReadAllControlledTimestamps(path));
            });
    }

    private static void AppendRepairsMissingTerminalNewline()
    {
        WithLogPath(
            path =>
            {
                var now = new DateTimeOffset(
                    2026,
                    8,
                    16,
                    12,
                    0,
                    0,
                    TimeSpan.Zero);
                File.WriteAllText(path, RetentionEntry(now));
                var writer = new BehaviorTickLogWriter(
                    path,
                    timeProvider: new MutableTimeProvider(now));
                writer.PrepareRetention();
                writer.SetEnabled(true);
                writer.Write(CreateTrace());

                string[] lines = File.ReadAllLines(path);
                BehaviorTestCheck.Equal(2, lines.Length);
                foreach (string line in lines)
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    BehaviorTestCheck.Equal(
                        JsonValueKind.Object,
                        document.RootElement.ValueKind);
                }
            });
    }

    private static void DueMaintenanceIsSingleFlightBackgroundAndSurvivesDisable()
    {
        WithLogPath(
            path =>
            {
                var initialNow = new DateTimeOffset(
                    2026,
                    8,
                    16,
                    12,
                    0,
                    0,
                    TimeSpan.Zero);
                var clock = new BlockingWorkerTimeProvider(initialNow);
                var writer = new BehaviorTickLogWriter(
                    path,
                    timeProvider: clock);
                writer.PrepareRetention();
                writer.SetEnabled(true);

                string padding = new('x', 2000);
                string stale = JsonSerializer.Serialize(new
                {
                    recordedAtUtc = initialNow - TimeSpan.FromDays(5),
                    padding,
                });
                string fresh = JsonSerializer.Serialize(new
                {
                    recordedAtUtc = initialNow,
                    padding,
                });
                File.WriteAllLines(
                    path,
                    Enumerable.Range(0, 2048)
                        .Select(index => index % 2 == 0 ? stale : fresh));

                clock.UtcNow = initialNow.AddHours(1).AddSeconds(2);
                clock.BlockWorker = true;
                var watch = Stopwatch.StartNew();
                writer.Write(CreateTrace());
                watch.Stop();
                BehaviorTestCheck.True(
                    watch.Elapsed < TimeSpan.FromMilliseconds(250),
                    $"Due maintenance blocked Write for {watch.Elapsed}.");
                BehaviorTestCheck.True(clock.WorkerEntered.Wait(
                    TimeSpan.FromSeconds(5)));

                Task maintenance = writer.PrepareRetentionAsync();
                watch.Restart();
                writer.Write(CreateTrace());
                writer.SetEnabled(false);
                watch.Stop();
                BehaviorTestCheck.True(
                    watch.Elapsed < TimeSpan.FromMilliseconds(250),
                    "A concurrent Write or SetEnabled waited for maintenance.");

                clock.ReleaseWorker.Set();
                maintenance.GetAwaiter().GetResult();
                DateTimeOffset[] retained = File.ReadLines(path)
                    .Select(ReadRecordedAtUtc)
                    .ToArray();
                BehaviorTestCheck.Equal(1024, retained.Length);
                BehaviorTestCheck.True(
                    retained.All(timestamp => timestamp == initialNow));
            });
    }

    private static void OversizedLegacyContentIsHardBounded()
    {
        WithLogPath(
            path =>
            {
                const long maximumFileBytes = 1024;
                var now = new DateTimeOffset(
                    2026,
                    8,
                    16,
                    12,
                    0,
                    0,
                    TimeSpan.Zero);
                var lines = Enumerable.Range(0, 30)
                    .Select(index => RetentionEntry(
                        now - TimeSpan.FromMinutes(30 - index)))
                    .ToList();
                lines.Insert(
                    15,
                    $"{{\"recordedAtUtc\":\"{now:O}\"," +
                    $"\"padding\":\"{new string('x', 2048)}\"}}");
                File.WriteAllLines(path, lines);

                var writer = new BehaviorTickLogWriter(
                    path,
                    maximumFileBytes: maximumFileBytes,
                    timeProvider: new MutableTimeProvider(now));
                writer.PrepareRetention();

                BehaviorTestCheck.True(File.Exists(path));
                BehaviorTestCheck.True(
                    new FileInfo(path).Length <= maximumFileBytes);
                string[] retained = File.ReadAllLines(path);
                BehaviorTestCheck.True(retained.Length > 0);
                BehaviorTestCheck.True(retained.All(line =>
                    line.Length < JsonlRetentionMaintenance.MaximumBufferedLineBytes));
                BehaviorTestCheck.Equal(
                    now - TimeSpan.FromMinutes(1),
                    ReadRecordedAtUtc(retained[^1]));
            });
    }

    private static void MaintenanceFailureRetriesOnlyAfterBackoff()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.TickLog.Backoff.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string blockedDirectory = Path.Combine(root, "logs");
        File.WriteAllText(blockedDirectory, "blocks-directory-creation");
        string path = Path.Combine(
            blockedDirectory,
            BehaviorTickLogWriter.LogFileName);
        try
        {
            var clock = new MutableTimeProvider(
                new DateTimeOffset(
                    2026,
                    8,
                    16,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));
            var writer = new BehaviorTickLogWriter(path, timeProvider: clock);
            writer.SetEnabled(true);
            _ = BehaviorTestCheck.Throws<IOException>(
                () => writer.PrepareRetentionAsync().GetAwaiter().GetResult());
            BehaviorTestCheck.True(writer.LastWriteError is not null);

            File.Delete(blockedDirectory);
            writer.Write(CreateTrace());
            BehaviorTestCheck.False(File.Exists(path));

            clock.UtcNow = clock.UtcNow.AddHours(1);
            var watch = Stopwatch.StartNew();
            writer.Write(CreateTrace());
            watch.Stop();
            BehaviorTestCheck.True(
                watch.Elapsed < TimeSpan.FromMilliseconds(250));
            writer.PrepareRetentionAsync().GetAwaiter().GetResult();
            writer.Write(CreateTrace());
            BehaviorTestCheck.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void PreparationDeletesOnlyExclusiveExactOrphanTemps()
    {
        WithLogPath(
            path =>
            {
                string directory = Path.GetDirectoryName(path)!;
                string guid = Guid.NewGuid().ToString("N");
                string exact = Path.Combine(
                    directory,
                    $".{BehaviorTickLogWriter.LogFileName}.retention-{guid}.tmp");
                string similar = Path.Combine(
                    directory,
                    $".{BehaviorTickLogWriter.LogFileName}.retention-not-{guid}.tmp");
                string otherLog = Path.Combine(
                    directory,
                    $".context-health.jsonl.retention-{guid}.tmp");
                string child = Path.Combine(directory, "child");
                Directory.CreateDirectory(child);
                string nested = Path.Combine(
                    child,
                    $".{BehaviorTickLogWriter.LogFileName}.retention-{guid}.tmp");
                File.WriteAllText(exact, "active-sentinel");
                File.WriteAllText(similar, "similar-sentinel");
                File.WriteAllText(otherLog, "other-log-sentinel");
                File.WriteAllText(nested, "nested-sentinel");
                var writer = new BehaviorTickLogWriter(path);

                using (var active = new FileStream(
                           exact,
                           FileMode.Open,
                           FileAccess.ReadWrite,
                           FileShare.None))
                {
                    writer.PrepareRetention();
                    BehaviorTestCheck.True(File.Exists(exact));
                }

                writer.PrepareRetention();
                BehaviorTestCheck.False(File.Exists(exact));
                BehaviorTestCheck.Equal(
                    "similar-sentinel",
                    File.ReadAllText(similar));
                BehaviorTestCheck.Equal(
                    "other-log-sentinel",
                    File.ReadAllText(otherLog));
                BehaviorTestCheck.Equal(
                    "nested-sentinel",
                    File.ReadAllText(nested));
            });
    }

    private static void DesktopStartupDoesNotAwaitRetentionPreparation()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "GuluPet",
            "App.xaml.cs"));
        int enable = source.IndexOf(
            "_tickScoreLogWriter.SetEnabled(",
            StringComparison.Ordinal);
        int schedule = source.IndexOf(
            "_ = PrepareTickScoreRetentionAsync(_tickScoreLogWriter);",
            StringComparison.Ordinal);
        int window = source.IndexOf(
            "_petWindow = new PetWindow();",
            StringComparison.Ordinal);
        BehaviorTestCheck.True(enable >= 0);
        BehaviorTestCheck.True(schedule > enable);
        BehaviorTestCheck.True(window > schedule);
        BehaviorTestCheck.False(source.Contains(
            "await PrepareTickScoreRetentionAsync(_tickScoreLogWriter)",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(source.Contains(
            "await writer.PrepareRetentionAsync();",
            StringComparison.Ordinal));
    }

    private static string RetentionEntry(DateTimeOffset recordedAtUtc) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            source = "activeTick",
            recordedAtUtc,
        });

    private static DateTimeOffset ReadRecordedAtUtc(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement
            .GetProperty("recordedAtUtc")
            .GetDateTimeOffset()
            .ToUniversalTime();
    }

    private static void AppendUtf8Line(List<byte> destination, string line)
    {
        destination.AddRange(Encoding.UTF8.GetBytes(line));
        destination.Add((byte)'\n');
    }

    private static DateTimeOffset[] ReadAllControlledTimestamps(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        string fileName = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        string[] controlledPaths =
        [
            path,
            .. Enumerable.Range(1, BehaviorTickLogWriter.DefaultArchiveCount)
                .Select(index => Path.Combine(
                    directory,
                    $"{fileName}.{index}{extension}")),
        ];
        return controlledPaths
            .Where(File.Exists)
            .SelectMany(File.ReadLines)
            .Select(ReadRecordedAtUtc)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "GuluPetCommunity.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the GuluPet repository root.");
    }

    private static BehaviorTickTrace CreateTrace()
    {
        var selected = new BehaviorEvaluation
        {
            BehaviorId = new BehaviorId("active_blink"),
            IsEligible = true,
            RejectionReason = null,
            Rank = 1,
            Probability = 0.65,
            Breakdown = new BehaviorEvaluationBreakdown
            {
                Base = 40,
                Emotion = 2,
                Total = 42,
            },
        };
        var rejected = new BehaviorEvaluation
        {
            BehaviorId = new BehaviorId("active_stretch"),
            IsEligible = false,
            RejectionReason = "cooldown",
            Breakdown = new BehaviorEvaluationBreakdown
            {
                Base = 30,
                Total = 30,
            },
        };
        var plan = new BehaviorSelectionPlan
        {
            AllEvaluations = [selected, rejected],
            TopCandidates = [selected],
            RandomConsumed = true,
        };
        var result = new BehaviorTickResult
        {
            WasDue = true,
            Evaluation = plan,
            SelectedBehaviorId = selected.BehaviorId,
            AttemptOrder = [selected.BehaviorId],
            QueueResult = new SoftQueueEnqueueResult
            {
                Disposition = SoftQueueEnqueueDisposition.Enqueued,
                RequestId = 12,
                GroupId = 3,
                RandomConsumed = false,
            },
            NextDueAt = TimeSpan.FromSeconds(55),
        };
        return new BehaviorTickTrace
        {
            EvaluatedAt = TimeSpan.FromSeconds(10),
            Context = new BehaviorContextSnapshot
            {
                Revision = 9,
                LocalNow = new DateTimeOffset(
                    2026,
                    7,
                    30,
                    12,
                    0,
                    0,
                    TimeSpan.FromHours(8)),
                ApplicationCategory = "office",
                ApplicationCategoryAvailable = true,
                KeyboardPerMinute = 80,
                MouseClicksPerMinute = 12,
                KeyboardCountSinceStart = 12_345,
                MouseClickCountSinceStart = 2_345,
                WeatherKind = "sunny",
                WeatherAvailable = true,
                TemperatureCelsius = 31,
            },
            Emotions = new EmotionState(),
            State = new HierarchicalPetStateSnapshot
            {
                StableState = StablePetState.Normal,
                Phase = PetLifecyclePhase.Idle,
                Revision = 4,
            },
            Result = result,
        };
    }

    private static void WithLogPath(Action<string> test)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.TickLog.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            test(Path.Combine(directory, BehaviorTickLogWriter.LogFileName));
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
            $"PASS {nameof(BehaviorTickLogWriterTests)}.{name}");
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RecordingTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public List<int> CallerThreads { get; } = [];

        public override DateTimeOffset GetUtcNow()
        {
            lock (CallerThreads)
            {
                CallerThreads.Add(Environment.CurrentManagedThreadId);
            }

            return utcNow;
        }
    }

    private sealed class BlockingWorkerTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;

        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public bool BlockWorker { get; set; }

        public ManualResetEventSlim WorkerEntered { get; } = new(false);

        public ManualResetEventSlim ReleaseWorker { get; } = new(false);

        public override DateTimeOffset GetUtcNow()
        {
            if (BlockWorker
                && Environment.CurrentManagedThreadId != _ownerThread)
            {
                WorkerEntered.Set();
                if (!ReleaseWorker.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "The retention test did not release its worker.");
                }
            }

            return UtcNow;
        }
    }
}
