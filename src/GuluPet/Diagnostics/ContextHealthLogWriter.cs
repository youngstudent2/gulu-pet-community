using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GuluPet.Diagnostics;

internal enum ContextHealthSource
{
    Input,
    ForegroundApplication,
    IdleTime,
    SessionLock,
    Weather,
}

internal enum ContextHealthOutcome
{
    Success,
    Failure,
    Recovered,
}

internal enum ContextHealthErrorCategory
{
    None,
    Unavailable,
    Timeout,
    HttpStatus,
    InvalidJson,
    InvalidData,
    Cancel,
    Unexpected,
}

internal readonly record struct ContextHealthEvent(
    DateTimeOffset Timestamp,
    ContextHealthSource Source,
    ContextHealthOutcome Outcome,
    ContextHealthErrorCategory ErrorCategory,
    int ConsecutiveFailures,
    double? LastSuccessAgeSeconds);

internal interface IContextHealthSink : IDisposable
{
    void Write(ContextHealthEvent healthEvent);
}

internal sealed class NullContextHealthSink : IContextHealthSink
{
    internal static NullContextHealthSink Instance { get; } = new();

    private NullContextHealthSink()
    {
    }

    public void Write(ContextHealthEvent healthEvent)
    {
    }

    public void Dispose()
    {
    }
}

internal sealed record ContextHealthLogOptions
{
    internal required string DirectoryPath { get; init; }

    internal string FileName { get; init; } = "context-health.jsonl";

    internal long MaximumFileBytes { get; init; } = 256 * 1024;

    internal int RetainedFileCount { get; init; } = 3;

    internal TimeSpan Retention { get; init; } = TimeSpan.FromHours(72);

    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath)
            || string.IsNullOrWhiteSpace(FileName)
            || Path.GetFileName(FileName) != FileName
            || MaximumFileBytes < 256
            || RetainedFileCount is < 1 or > 20
            || Retention <= TimeSpan.Zero
            || TimeProvider is null)
        {
            throw new InvalidOperationException(
                "Context health log options are invalid.");
        }
    }
}

/// <summary>
/// Writes an intentionally closed JSONL schema. Callers can provide only
/// aggregate source health fields; raw process, window, input, URL, message,
/// exception, and payload data have no representation in this API.
/// </summary>
internal sealed class ContextHealthLogWriter : IContextHealthSink
{
    private static readonly TimeSpan MaintenanceFailureRetryInterval =
        TimeSpan.FromHours(1);

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Indented = false,
        SkipValidation = false,
    };

    private readonly object _gate = new();
    private readonly ContextHealthLogOptions _options;
    private readonly string _activePath;
    private readonly Dictionary<string, JsonlRetentionFileState> _fileStates =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private bool _initialMaintenanceCompleted;
    private long? _lastMaintenanceUtcHour;
    private DateOnly? _lastRotationUtcDate;
    private DateTimeOffset? _nextMaintenanceAttemptUtc;

    internal ContextHealthLogWriter(ContextHealthLogOptions options)
    {
        _options = options
            ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _activePath = Path.Combine(
            Path.GetFullPath(_options.DirectoryPath),
            _options.FileName);
    }

    internal string ActivePath => _activePath;

    internal static ContextHealthLogWriter CreateDefault()
    {
        return new ContextHealthLogWriter(
            new ContextHealthLogOptions
            {
                DirectoryPath = Path.Combine(
                    global::GuluPet.AppIdentity.LocalDataDirectory,
                    "Logs"),
            });
    }

    public void Write(ContextHealthEvent healthEvent)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DateTimeOffset nowUtc = _options.TimeProvider
                .GetUtcNow()
                .ToUniversalTime();
            if (IsMaintenanceDue(nowUtc)
                && _nextMaintenanceAttemptUtc is { } retryAtUtc
                && nowUtc < retryAtUtc)
            {
                return;
            }

            try
            {
                EnsureLogDirectory();
                EnsureInitialMaintenance(nowUtc);
                MaintainForUtcHour(nowUtc);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException)
            {
                _nextMaintenanceAttemptUtc =
                    nowUtc + MaintenanceFailureRetryInterval;
                Debug.WriteLine(
                    $"GuluPet context-health retention maintenance failed: " +
                    exception);
                throw;
            }

            DateTimeOffset timestampUtc = healthEvent.Timestamp
                .ToUniversalTime();
            if (timestampUtc < CalculateCutoff(nowUtc)
                || timestampUtc > nowUtc)
            {
                return;
            }

            byte[] line = Serialize(healthEvent);
            if (line.Length > _options.MaximumFileBytes)
            {
                return;
            }

            bool needsSeparator =
                JsonlRetentionMaintenance.NeedsLineSeparator(_activePath);
            RotateIfNeeded(line.Length + (needsSeparator ? 1 : 0));
            needsSeparator =
                JsonlRetentionMaintenance.NeedsLineSeparator(_activePath);
            JsonlRetentionMaintenance.ThrowIfReparsePoint(_activePath);
            using var stream = new FileStream(
                _activePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4 * 1024,
                FileOptions.WriteThrough);
            if (needsSeparator)
            {
                stream.WriteByte((byte)'\n');
            }

            stream.Write(line);
            RecordTimestamp(_activePath, timestampUtc);
            _nextMaintenanceAttemptUtc = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private static byte[] Serialize(ContextHealthEvent healthEvent)
    {
        using var stream = new MemoryStream(256);
        using (var writer = new Utf8JsonWriter(stream, JsonOptions))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "timestamp",
                healthEvent.Timestamp.ToUniversalTime());
            writer.WriteString(
                "source",
                ToWireName(healthEvent.Source));
            writer.WriteString(
                "outcome",
                ToWireName(healthEvent.Outcome));
            writer.WriteString(
                "errorCategory",
                ToWireName(healthEvent.ErrorCategory));
            writer.WriteNumber(
                "consecutiveFailures",
                Math.Max(0, healthEvent.ConsecutiveFailures));
            if (healthEvent.LastSuccessAgeSeconds is double age
                && double.IsFinite(age))
            {
                writer.WriteNumber(
                    "lastSuccessAgeSeconds",
                    Math.Max(0, age));
            }
            else
            {
                writer.WriteNull("lastSuccessAgeSeconds");
            }

            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        long currentLength = File.Exists(_activePath)
            ? new FileInfo(_activePath).Length
            : 0;
        if (currentLength == 0
            || currentLength + incomingBytes <= _options.MaximumFileBytes)
        {
            return;
        }

        if (currentLength > _options.MaximumFileBytes)
        {
            JsonlRetentionMaintenance.DeleteControlledFile(_activePath);
            _fileStates.Remove(_activePath);
            return;
        }

        RotateActiveFile();
    }

    private void EnsureLogDirectory()
    {
        string directory = Path.GetDirectoryName(_activePath)!;
        JsonlRetentionMaintenance.ThrowIfDirectoryPathContainsReparsePoint(
            directory);
        Directory.CreateDirectory(directory);
        JsonlRetentionMaintenance.ThrowIfDirectoryPathContainsReparsePoint(
            directory);
    }

    private void EnsureInitialMaintenance(DateTimeOffset nowUtc)
    {
        if (_initialMaintenanceCompleted)
        {
            return;
        }

        DeleteOrphanedTemporaryFiles();
        foreach (string path in GetControlledPaths())
        {
            RefreshFileState(path, nowUtc);
        }

        _initialMaintenanceCompleted = true;
        _lastMaintenanceUtcHour = GetUtcHour(nowUtc);
        _lastRotationUtcDate = DateOnly.FromDateTime(nowUtc.UtcDateTime);
    }

    private void MaintainForUtcHour(DateTimeOffset nowUtc)
    {
        long currentHour = GetUtcHour(nowUtc);
        DateOnly currentDate = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        if (_lastMaintenanceUtcHour == currentHour)
        {
            return;
        }

        DeleteOrphanedTemporaryFiles();
        if (_lastRotationUtcDate is null
            || currentDate > _lastRotationUtcDate.Value)
        {
            RotateActiveFile();
            _lastRotationUtcDate = currentDate;
        }

        DateTimeOffset cutoffUtc = CalculateCutoff(nowUtc);
        foreach (string path in GetControlledPaths())
        {
            if (!_fileStates.TryGetValue(path, out JsonlRetentionFileState? state))
            {
                RefreshFileState(path, nowUtc);
                continue;
            }

            if (state.NewestTimestampUtc < cutoffUtc)
            {
                JsonlRetentionMaintenance.DeleteControlledFile(path);
                _fileStates.Remove(path);
            }
            else if (state.OldestTimestampUtc < cutoffUtc
                     || state.NewestTimestampUtc > nowUtc)
            {
                RefreshFileState(path, nowUtc);
            }
        }

        // Runtime writes trigger one sweep per UTC hour. This bounds normal
        // on-disk expiry lag to less than one hour (under 73 hours total).
        _lastMaintenanceUtcHour = currentHour;
        _nextMaintenanceAttemptUtc = null;
    }

    private void DeleteOrphanedTemporaryFiles()
    {
        foreach (string path in GetControlledPaths())
        {
            JsonlRetentionMaintenance.DeleteOrphanedTemporaryFiles(path);
        }
    }

    private void RefreshFileState(string path, DateTimeOffset nowUtc)
    {
        DateTimeOffset cutoffUtc = CalculateCutoff(nowUtc);
        if (JsonlRetentionMaintenance.TryPruneFile(
                path,
                "timestamp",
                cutoffUtc,
                nowUtc.ToUniversalTime(),
                _options.MaximumFileBytes,
                out JsonlRetentionFileState? state))
        {
            if (state is null)
            {
                _fileStates.Remove(path);
            }
            else
            {
                _fileStates[path] = state;
            }
        }
        else
        {
            _fileStates.Remove(path);
        }
    }

    private void RotateActiveFile()
    {
        if (!File.Exists(_activePath))
        {
            _fileStates.Remove(_activePath);
            return;
        }

        JsonlRetentionMaintenance.ThrowIfReparsePoint(_activePath);
        if (new FileInfo(_activePath).Length == 0)
        {
            JsonlRetentionMaintenance.DeleteControlledFile(_activePath);
            _fileStates.Remove(_activePath);
            return;
        }

        int archiveCount = _options.RetainedFileCount - 1;
        if (archiveCount == 0)
        {
            JsonlRetentionMaintenance.DeleteControlledFile(_activePath);
            _fileStates.Remove(_activePath);
            return;
        }

        string oldest = RotatedPath(archiveCount);
        JsonlRetentionMaintenance.DeleteControlledFile(oldest);
        _fileStates.Remove(oldest);
        for (int index = archiveCount - 1; index >= 1; index--)
        {
            MoveControlledFile(RotatedPath(index), RotatedPath(index + 1));
        }

        MoveControlledFile(_activePath, RotatedPath(1));
    }

    private void MoveControlledFile(string source, string destination)
    {
        if (!File.Exists(source))
        {
            _fileStates.Remove(source);
            return;
        }

        JsonlRetentionMaintenance.ThrowIfReparsePoint(source);
        JsonlRetentionMaintenance.ThrowIfReparsePoint(destination);
        File.Move(source, destination);
        if (_fileStates.Remove(source, out JsonlRetentionFileState? state))
        {
            _fileStates[destination] = state;
        }
        else
        {
            _fileStates.Remove(destination);
        }
    }

    private void RecordTimestamp(string path, DateTimeOffset timestampUtc)
    {
        timestampUtc = timestampUtc.ToUniversalTime();
        if (_fileStates.TryGetValue(path, out JsonlRetentionFileState? state))
        {
            _fileStates[path] = new JsonlRetentionFileState(
                timestampUtc < state.OldestTimestampUtc
                    ? timestampUtc
                    : state.OldestTimestampUtc,
                timestampUtc > state.NewestTimestampUtc
                    ? timestampUtc
                    : state.NewestTimestampUtc);
        }
        else
        {
            _fileStates[path] = new JsonlRetentionFileState(
                timestampUtc,
                timestampUtc);
        }
    }

    private IEnumerable<string> GetControlledPaths()
    {
        yield return _activePath;
        for (int index = 1;
             index < _options.RetainedFileCount;
             index++)
        {
            yield return RotatedPath(index);
        }
    }

    private DateTimeOffset CalculateCutoff(DateTimeOffset nowUtc) =>
        nowUtc.ToUniversalTime() - _options.Retention;

    private bool IsMaintenanceDue(DateTimeOffset nowUtc) =>
        !_initialMaintenanceCompleted
        || _lastMaintenanceUtcHour != GetUtcHour(nowUtc);

    private static long GetUtcHour(DateTimeOffset value) =>
        value.ToUniversalTime().Ticks / TimeSpan.TicksPerHour;

    private string RotatedPath(int index) =>
        _activePath + "." + index;

    private static string ToWireName(ContextHealthSource value) =>
        value switch
        {
            ContextHealthSource.Input => "input",
            ContextHealthSource.ForegroundApplication => "foreground",
            ContextHealthSource.IdleTime => "idle",
            ContextHealthSource.SessionLock => "session-lock",
            ContextHealthSource.Weather => "weather",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static string ToWireName(ContextHealthOutcome value) =>
        value switch
        {
            ContextHealthOutcome.Success => "success",
            ContextHealthOutcome.Failure => "failure",
            ContextHealthOutcome.Recovered => "recovered",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static string ToWireName(ContextHealthErrorCategory value) =>
        value switch
        {
            ContextHealthErrorCategory.None => "none",
            ContextHealthErrorCategory.Unavailable => "unavailable",
            ContextHealthErrorCategory.Timeout => "timeout",
            ContextHealthErrorCategory.HttpStatus => "http-status",
            ContextHealthErrorCategory.InvalidJson => "invalid-json",
            ContextHealthErrorCategory.InvalidData => "invalid-data",
            ContextHealthErrorCategory.Cancel => "cancel",
            ContextHealthErrorCategory.Unexpected => "unexpected",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
}
