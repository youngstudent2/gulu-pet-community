using System.Text.Json;
using GuluPet.Diagnostics;

namespace GuluPet.Tests;

internal static class LocalErrorLogTests
{
    public static void RunAll()
    {
        Run(nameof(WritesOnlySanitizedErrorData), WritesOnlySanitizedErrorData);
        Run(nameof(RotatesAndReturnsRecentFiles), RotatesAndReturnsRecentFiles);
        Run(
            nameof(StartupCleanupKeepsOnlyRecentErrorLogs),
            StartupCleanupKeepsOnlyRecentErrorLogs);
    }

    private static void WritesOnlySanitizedErrorData()
    {
        WithTemporaryLog(
            log =>
            {
                string profile = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);
                var exception = new InvalidOperationException(
                    $"Authorization: Bearer sk-secret at " +
                    $"{Path.Combine(profile, "private", "file.txt")}");
                log.Write(
                    "diary",
                    "generate",
                    "http-401",
                    exception,
                    new Dictionary<string, string>
                    {
                        ["statusCode"] = "401",
                        ["prompt"] = "must never be included",
                    });

                string file = log.GetRecentLogFiles(TimeSpan.FromDays(1)).Single();
                string line = File.ReadAllText(file);
                BehaviorTestCheck.False(line.Contains("sk-secret", StringComparison.Ordinal));
                BehaviorTestCheck.False(line.Contains(profile, StringComparison.OrdinalIgnoreCase));
                BehaviorTestCheck.False(line.Contains("must never", StringComparison.Ordinal));
                using JsonDocument document = JsonDocument.Parse(line);
                BehaviorTestCheck.Equal(
                    "401",
                    document.RootElement
                        .GetProperty("context")
                        .GetProperty("statusCode")
                        .GetString());
            });
    }

    private static void RotatesAndReturnsRecentFiles()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.ErrorLog.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var log = new LocalErrorLog(
                root,
                maximumFileBytes: 500,
                maximumDirectoryBytes: 5000,
                retention: TimeSpan.FromDays(30));
            for (var index = 0; index < 5; index++)
            {
                log.Write(
                    "component",
                    "operation",
                    $"error-{index}",
                    new InvalidOperationException(new string('x', 120)));
            }

            BehaviorTestCheck.True(
                log.GetRecentLogFiles(TimeSpan.FromDays(1)).Count >= 2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void StartupCleanupKeepsOnlyRecentErrorLogs()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.ErrorLog.Retention.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));
        DateTime cutoffUtc = clock.GetUtcNow().AddDays(-3).UtcDateTime;
        string expiredPath = Path.Combine(root, "errors-expired.jsonl");
        string cutoffPath = Path.Combine(root, "errors-cutoff.jsonl");
        string recentPath = Path.Combine(root, "errors-recent.jsonl");
        string nonErrorLogPath = Path.Combine(root, "context-health.jsonl");
        string pendingReportPath = Path.Combine(root, "pending-error-report.bin");
        try
        {
            foreach (string path in
                     new[]
                     {
                         expiredPath,
                         cutoffPath,
                         recentPath,
                         nonErrorLogPath,
                         pendingReportPath,
                     })
            {
                File.WriteAllText(path, "sentinel");
            }

            File.SetLastWriteTimeUtc(expiredPath, cutoffUtc.AddSeconds(-1));
            File.SetLastWriteTimeUtc(cutoffPath, cutoffUtc);
            File.SetLastWriteTimeUtc(recentPath, cutoffUtc.AddSeconds(1));
            File.SetLastWriteTimeUtc(nonErrorLogPath, cutoffUtc.AddDays(-30));
            File.SetLastWriteTimeUtc(pendingReportPath, cutoffUtc.AddDays(-30));

            _ = new LocalErrorLog(root, clock);

            BehaviorTestCheck.False(File.Exists(expiredPath));
            BehaviorTestCheck.True(File.Exists(cutoffPath));
            BehaviorTestCheck.True(File.Exists(recentPath));
            BehaviorTestCheck.True(File.Exists(nonErrorLogPath));
            BehaviorTestCheck.True(File.Exists(pendingReportPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WithTemporaryLog(Action<LocalErrorLog> test)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.ErrorLog.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            test(new LocalErrorLog(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(LocalErrorLogTests)}.{name}");
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }
}
