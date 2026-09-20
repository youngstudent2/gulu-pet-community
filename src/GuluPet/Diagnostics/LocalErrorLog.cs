using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GuluPet.Diagnostics;

public sealed record LocalErrorRecord
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;

    [JsonRequired]
    public Guid EventId { get; init; }

    [JsonRequired]
    public DateTimeOffset OccurredAtUtc { get; init; }

    [JsonRequired]
    public required string Component { get; init; }

    [JsonRequired]
    public required string Operation { get; init; }

    [JsonRequired]
    public required string Code { get; init; }

    public string? ExceptionType { get; init; }

    public string? Message { get; init; }

    public string? StackTrace { get; init; }

    [JsonRequired]
    public required string AppVersion { get; init; }

    [JsonRequired]
    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Release-safe, error-only JSONL diagnostics. It deliberately has no info or
/// telemetry API, and every value passes through local redaction before disk.
/// </summary>
public sealed partial class LocalErrorLog
{
    private const long DefaultMaximumFileBytes = 2 * 1024 * 1024;
    private const long DefaultMaximumDirectoryBytes = 50 * 1024 * 1024;
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(3);
    private static readonly HashSet<string> AllowedContextKeys = new(
        [
            "statusCode",
            "failureCode",
            "model",
            "reportId",
            "diaryDate",
            "attemptCount",
        ],
        StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly long _maximumFileBytes;
    private readonly long _maximumDirectoryBytes;
    private readonly TimeSpan _retention;

    public LocalErrorLog()
        : this(Path.Combine(
            global::GuluPet.AppIdentity.LocalDataDirectory,
            "Logs"))
    {
    }

    public LocalErrorLog(
        string directoryPath,
        TimeProvider? timeProvider = null,
        long maximumFileBytes = DefaultMaximumFileBytes,
        long maximumDirectoryBytes = DefaultMaximumDirectoryBytes,
        TimeSpan? retention = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (maximumFileBytes <= 0
            || maximumDirectoryBytes < maximumFileBytes
            || retention is { } requestedRetention
                && requestedRetention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFileBytes),
                "Error-log limits must be positive and internally consistent.");
        }

        DirectoryPath = Path.GetFullPath(directoryPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maximumFileBytes = maximumFileBytes;
        _maximumDirectoryBytes = maximumDirectoryBytes;
        _retention = retention ?? DefaultRetention;
        TryCleanup(_timeProvider.GetUtcNow());
    }

    public string DirectoryPath { get; }

    public void Write(
        string component,
        string operation,
        string code,
        Exception? exception = null,
        IReadOnlyDictionary<string, string>? context = null)
    {
        try
        {
            ValidateLabel(component, nameof(component));
            ValidateLabel(operation, nameof(operation));
            ValidateLabel(code, nameof(code));
            DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
            var record = new LocalErrorRecord
            {
                EventId = Guid.NewGuid(),
                OccurredAtUtc = nowUtc,
                Component = Sanitize(component, 80),
                Operation = Sanitize(operation, 100),
                Code = Sanitize(code, 120),
                ExceptionType = exception?.GetType().FullName,
                Message = exception is null
                    ? null
                    : Sanitize(exception.Message, 1000),
                StackTrace = exception?.StackTrace is { } stack
                    ? Sanitize(stack, 12000)
                    : null,
                AppVersion = Assembly.GetEntryAssembly()?
                    .GetName()
                    .Version?
                    .ToString() ?? "unknown",
                Context = SanitizeContext(context),
            };
            string line = JsonSerializer.Serialize(record, JsonOptions) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(line);

            lock (_gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                Cleanup(nowUtc);
                string path = SelectWritablePath(nowUtc, bytes.Length);
                using var stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
        }
        catch
        {
            // Diagnostics must never alter application behavior or create an
            // exception loop when the disk is unavailable.
        }
    }

    public IReadOnlyList<string> GetRecentLogFiles(TimeSpan age)
    {
        if (age <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(age));
        }

        lock (_gate)
        {
            if (!Directory.Exists(DirectoryPath))
            {
                return [];
            }

            DateTimeOffset cutoff = _timeProvider.GetUtcNow() - age;
            return Directory.GetFiles(DirectoryPath, "errors-*.jsonl")
                .Where(path => File.GetLastWriteTimeUtc(path) >= cutoff.UtcDateTime)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyList<LocalErrorRecord> ReadRecentRecords(
        TimeSpan age,
        int maximumCount = 500,
        IReadOnlySet<Guid>? excludedEventIds = null)
    {
        if (maximumCount is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var records = new List<LocalErrorRecord>();
        foreach (string path in GetRecentLogFiles(age))
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    try
                    {
                        LocalErrorRecord? record =
                            JsonSerializer.Deserialize<LocalErrorRecord>(
                                line,
                                JsonOptions);
                        if (record is not null)
                        {
                            records.Add(record);
                        }
                    }
                    catch (JsonException)
                    {
                        // A partially written line must not block reporting
                        // the remaining valid error events.
                    }
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // The UI will still be able to upload any other readable
                // rotated files and can open the directory for manual help.
            }
        }

        DateTimeOffset cutoff = _timeProvider.GetUtcNow() - age;
        return records
            .Where(record => record.OccurredAtUtc >= cutoff)
            .Where(record => excludedEventIds is null
                || !excludedEventIds.Contains(record.EventId))
            .OrderByDescending(static record => record.OccurredAtUtc)
            .ThenBy(static record => record.EventId)
            .Take(maximumCount)
            .OrderBy(static record => record.OccurredAtUtc)
            .ThenBy(static record => record.EventId)
            .ToArray();
    }

    public void OpenFolder()
    {
        Directory.CreateDirectory(DirectoryPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{DirectoryPath}\"",
            UseShellExecute = true,
        });
    }

    internal static string Sanitize(string value, int maximumLength)
    {
        string sanitized = value;
        string userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            sanitized = sanitized.Replace(
                userProfile,
                "<user-profile>",
                StringComparison.OrdinalIgnoreCase);
        }

        string userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            sanitized = sanitized.Replace(
                userName,
                "<user>",
                StringComparison.OrdinalIgnoreCase);
        }

        sanitized = SecretPattern().Replace(
            sanitized,
            static match => $"{match.Groups[1].Value}<redacted>");
        sanitized = ApiKeyPattern().Replace(
            sanitized,
            "<api-key-redacted>");
        sanitized = WindowsPathPattern().Replace(
            sanitized,
            "<local-path>");
        sanitized = QuerySecretPattern().Replace(
            sanitized,
            static match => $"{match.Groups[1].Value}=<redacted>");
        sanitized = sanitized.Replace('\r', ' ').Replace('\n', ' ');
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }

    private IReadOnlyDictionary<string, string> SanitizeContext(
        IReadOnlyDictionary<string, string>? context)
    {
        if (context is null || context.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return context
            .Where(item => AllowedContextKeys.Contains(item.Key))
            .Take(16)
            .ToDictionary(
                static item => item.Key,
                static item => Sanitize(item.Value, 240),
                StringComparer.Ordinal);
    }

    private string SelectWritablePath(
        DateTimeOffset nowUtc,
        int incomingBytes)
    {
        string prefix = $"errors-{nowUtc:yyyyMMdd}";
        for (var index = 0; index < 1000; index++)
        {
            string path = Path.Combine(
                DirectoryPath,
                $"{prefix}-{index:00}.jsonl");
            long length = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (length + incomingBytes <= _maximumFileBytes)
            {
                return path;
            }
        }

        throw new IOException("The daily error-log rotation limit was reached.");
    }

    private void Cleanup(DateTimeOffset nowUtc)
    {
        FileInfo[] files = new DirectoryInfo(DirectoryPath)
            .GetFiles("errors-*.jsonl")
            .OrderBy(static file => file.LastWriteTimeUtc)
            .ToArray();
        DateTime cutoffUtc = (nowUtc - _retention).UtcDateTime;
        foreach (FileInfo file in files.Where(file =>
                     file.LastWriteTimeUtc < cutoffUtc))
        {
            file.Delete();
        }

        files = new DirectoryInfo(DirectoryPath)
            .GetFiles("errors-*.jsonl")
            .OrderBy(static file => file.LastWriteTimeUtc)
            .ToArray();
        long total = files.Sum(static file => file.Length);
        foreach (FileInfo file in files)
        {
            if (total <= _maximumDirectoryBytes)
            {
                break;
            }

            total -= file.Length;
            file.Delete();
        }
    }

    private void TryCleanup(DateTimeOffset nowUtc)
    {
        try
        {
            lock (_gate)
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Cleanup(nowUtc);
                }
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // Retention maintenance must never prevent the application from
            // starting when the diagnostics directory is unavailable.
        }
    }

    private static void ValidateLabel(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
        {
            throw new ArgumentException(
                "An error-log label is missing or too long.",
                parameterName);
        }
    }

    [GeneratedRegex(
        "(?i)((?:authorization\\s*[:=]\\s*)?bearer\\s+|(?:api[_-]?key|authorization|token|secret)\\s*[:=]\\s*)([^\\s,;]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    [GeneratedRegex(
        "(?i)\\bsk-[A-Za-z0-9_-]{12,}\\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex(
        "(?i)\\b[A-Z]:\\\\(?:[^\\r\\n\\t\\\"<>|]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(
        "(?i)(api[_-]?key|authorization|token|secret)=([^&\\s]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex QuerySecretPattern();
}
