using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using GuluPet.Behavior;

namespace GuluPet.Diagnostics;

public sealed class BehaviorTickLogWriter : IBehaviorTickTraceSink
{
    public const string LogFileName = "behavior-tick-scores.jsonl";
    public const long DefaultMaximumFileBytes = 32L * 1024 * 1024;
    public const int DefaultArchiveCount = 4;
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(72);
    private static readonly TimeSpan MaintenanceFailureRetryInterval =
        TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly object _gate = new();
    private readonly long _maximumFileBytes;
    private readonly int _archiveCount;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;
    private readonly Dictionary<string, JsonlRetentionFileState> _fileStates =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _enabled;
    private bool _retentionPreparationInProgress;
    private Task? _retentionPreparationTask;
    private bool _initialMaintenanceCompleted;
    private long? _lastMaintenanceUtcHour;
    private DateOnly? _lastRotationUtcDate;
    private DateTimeOffset? _nextMaintenanceAttemptUtc;

    public BehaviorTickLogWriter()
        : this(
            Path.Combine(
                global::GuluPet.AppIdentity.LocalDataDirectory,
                "Logs",
                LogFileName))
    {
    }

    internal BehaviorTickLogWriter(
        string logPath,
        long maximumFileBytes = DefaultMaximumFileBytes,
        int archiveCount = DefaultArchiveCount,
        TimeProvider? timeProvider = null,
        TimeSpan? retention = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(archiveCount);
        if (retention is { } requestedRetention
            && requestedRetention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        LogPath = Path.GetFullPath(logPath);
        _maximumFileBytes = maximumFileBytes;
        _archiveCount = archiveCount;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _retention = retention ?? DefaultRetention;
    }

    public string LogPath { get; }

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled);
    }

    public string? LastWriteError { get; private set; }

    /// <summary>
    /// Performs the potentially expensive legacy scan. Desktop startup
    /// schedules it on a worker even when logging is disabled.
    /// </summary>
    public void PrepareRetention()
    {
        DateTimeOffset nowUtc = _timeProvider
            .GetUtcNow()
            .ToUniversalTime();
        lock (_gate)
        {
            try
            {
                EnsureLogDirectory();
                DeleteOrphanedTemporaryFiles();
                EnsureInitialMaintenance(nowUtc);
                MaintainForUtcHour(nowUtc);
                _nextMaintenanceAttemptUtc = null;
                LastWriteError = null;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException)
            {
                RecordMaintenanceFailure(nowUtc, exception);
                throw;
            }
        }
    }

    public Task PrepareRetentionAsync()
    {
        lock (_gate)
        {
            if (_retentionPreparationInProgress
                && _retentionPreparationTask is not null)
            {
                return _retentionPreparationTask;
            }

            Volatile.Write(ref _retentionPreparationInProgress, true);
            _retentionPreparationTask = Task.Run(() =>
            {
                try
                {
                    PrepareRetention();
                }
                finally
                {
                    Volatile.Write(
                        ref _retentionPreparationInProgress,
                        false);
                }
            });
            return _retentionPreparationTask;
        }
    }

    public void SetEnabled(bool enabled)
    {
        // This setter is called by WPF controls and must never wait for or scan
        // disk. An unprepared caller schedules maintenance on its first Write
        // and intentionally drops that trace.
        Volatile.Write(ref _enabled, enabled);
    }

    public void Write(BehaviorTickTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (!Volatile.Read(ref _enabled)
            || Volatile.Read(ref _retentionPreparationInProgress))
        {
            return;
        }

        lock (_gate)
        {
            if (!Volatile.Read(ref _enabled)
                || Volatile.Read(ref _retentionPreparationInProgress))
            {
                return;
            }

            try
            {
                DateTimeOffset recordedAtUtc = _timeProvider
                    .GetUtcNow()
                    .ToUniversalTime();
                if (IsMaintenanceDue(recordedAtUtc))
                {
                    if (_nextMaintenanceAttemptUtc is { } retryAtUtc
                        && recordedAtUtc < retryAtUtc)
                    {
                        return;
                    }

                    // Behavior traces originate on WPF's DispatcherTimer.
                    // Any due scan/rewrite is single-flight background work;
                    // this tick is intentionally dropped to keep UI O(1).
                    ScheduleRetentionPreparation();
                    return;
                }

                EnsureLogDirectory();
                string json = JsonSerializer.Serialize(
                    CreateEntry(trace, recordedAtUtc),
                    SerializerOptions);
                int pendingBytes = Encoding.UTF8.GetByteCount(json)
                                   + Encoding.UTF8.GetByteCount(
                                       Environment.NewLine);
                if (pendingBytes > _maximumFileBytes)
                {
                    LastWriteError =
                        "The tick-score record exceeds the log size limit.";
                    return;
                }

                bool needsSeparator =
                    JsonlRetentionMaintenance.NeedsLineSeparator(LogPath);
                RotateIfNeeded(pendingBytes + (needsSeparator ? 1 : 0));
                needsSeparator =
                    JsonlRetentionMaintenance.NeedsLineSeparator(LogPath);
                JsonlRetentionMaintenance.ThrowIfReparsePoint(LogPath);
                File.AppendAllText(
                    LogPath,
                    (needsSeparator ? "\n" : string.Empty)
                    + json
                    + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                RecordTimestamp(LogPath, recordedAtUtc);
                _nextMaintenanceAttemptUtc = null;
                LastWriteError = null;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException)
            {
                DateTimeOffset failureUtc = _timeProvider
                    .GetUtcNow()
                    .ToUniversalTime();
                RecordMaintenanceFailure(failureUtc, exception);
                Debug.WriteLine(
                    $"GuluPet tick-score logging failed: {exception}");
            }
        }
    }

    public void OpenLocation()
    {
        lock (_gate)
        {
            string directory = GetLogDirectory();
            Directory.CreateDirectory(directory);
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
            };
            startInfo.ArgumentList.Add(
                File.Exists(LogPath) ? $"/select,{LogPath}" : directory);
            Process.Start(startInfo);
        }
    }

    private object CreateEntry(
        BehaviorTickTrace trace,
        DateTimeOffset recordedAtUtc)
    {
        BehaviorTickResult result = trace.Result;
        SoftQueueEnqueueResult? queue = result.QueueResult;
        bool enqueued = queue?.Disposition is
            SoftQueueEnqueueDisposition.Enqueued or
            SoftQueueEnqueueDisposition.Refreshed or
            SoftQueueEnqueueDisposition.ReplacedActiveTick;
        string selectionReason = result.Evaluation.RandomConsumed
            ? "weighted-random-draw-from-top-five"
            : result.SelectedBehaviorId is null
                ? "no-positive-eligible-candidate"
                : "only-positive-eligible-top-candidate";
        string queueReason = queue?.Diagnostic
                             ?? queue?.Disposition.ToString()
                             ?? "queue-result-missing";
        string outcome;
        string reason;
        if (result.SelectedBehaviorId is null)
        {
            outcome = "no-selection";
            reason = selectionReason;
        }
        else if (enqueued)
        {
            outcome = "queued";
            reason = $"{selectionReason};queue:{queueReason}";
        }
        else
        {
            outcome = "suppressed";
            reason = $"{selectionReason};queue:{queueReason}";
        }

        return new
        {
            schemaVersion = 1,
            source = "activeTick",
            recordedAtUtc,
            evaluatedAtMonotonicMilliseconds =
                trace.EvaluatedAt.TotalMilliseconds,
            context = new
            {
                trace.Context.Revision,
                trace.Context.LocalNow,
                trace.Context.ApplicationCategory,
                trace.Context.ApplicationCategoryAvailable,
                applicationStableForSeconds =
                    trace.Context.ApplicationStableFor.TotalSeconds,
                trace.Context.KeyboardPerMinute,
                trace.Context.MouseClicksPerMinute,
                idleForSeconds = trace.Context.IdleFor.TotalSeconds,
                trace.Context.IsBusy,
                trace.Context.IsFullscreen,
                trace.Context.IsLocked,
                trace.Context.WeatherKind,
                trace.Context.WeatherAvailable,
                trace.Context.TemperatureCelsius,
            },
            state = new
            {
                stableState = trace.State.StableState.ToString(),
                phase = trace.State.Phase.ToString(),
                trace.State.Revision,
            },
            emotions = new
            {
                trace.Emotions.Energy,
                trace.Emotions.Sleepiness,
                trace.Emotions.Boredom,
                trace.Emotions.Curiosity,
                trace.Emotions.Happiness,
                trace.Emotions.Stress,
                trace.Emotions.Affection,
            },
            selection = new
            {
                result.Evaluation.RandomConsumed,
                topFive = result.Evaluation.TopCandidates.Select(
                    static candidate => new
                    {
                        behaviorId = candidate.BehaviorId.Value,
                        score = candidate.Breakdown.Total,
                        candidate.Rank,
                        candidate.Probability,
                    }),
                attemptOrder = result.AttemptOrder.Select(
                    static id => id.Value),
                randomlySelectedBehaviorId =
                    result.SelectedBehaviorId?.Value,
            },
            finalResult = new
            {
                behaviorId = enqueued
                    ? result.SelectedBehaviorId?.Value
                    : null,
                outcome,
                reason,
                selectionReason,
                queueReason,
                queueDisposition = queue?.Disposition.ToString(),
                queue?.Diagnostic,
                queue?.RequestId,
                queue?.GroupId,
                result.WasDue,
                nextDueAtMonotonicMilliseconds =
                    result.NextDueAt?.TotalMilliseconds,
            },
            candidates = result.Evaluation.AllEvaluations.Select(
                static candidate => new
                {
                    behaviorId = candidate.BehaviorId.Value,
                    eligible = candidate.IsEligible,
                    candidate.RejectionReason,
                    candidate.Rank,
                    candidate.Probability,
                    score = candidate.Breakdown.Total,
                    components = new
                    {
                        candidate.Breakdown.Base,
                        candidate.Breakdown.Emotion,
                        candidate.Breakdown.Time,
                        candidate.Breakdown.Activity,
                        candidate.Breakdown.Application,
                        candidate.Breakdown.Weather,
                        candidate.Breakdown.TriggerBoost,
                        candidate.Breakdown.TimeSinceLastRun,
                        candidate.Breakdown.Relationship,
                        candidate.Breakdown.SameBehaviorPenalty,
                        candidate.Breakdown.SameFamilyPenalty,
                        candidate.Breakdown.SameClipPenalty,
                        candidate.Breakdown.DisturbUserPenalty,
                    },
                }),
        };
    }

    private void RotateIfNeeded(int pendingBytes)
    {
        if (!File.Exists(LogPath))
        {
            return;
        }

        long currentLength = new FileInfo(LogPath).Length;
        if (currentLength > _maximumFileBytes)
        {
            JsonlRetentionMaintenance.DeleteControlledFile(LogPath);
            _fileStates.Remove(LogPath);
            return;
        }

        if (currentLength + pendingBytes <= _maximumFileBytes)
        {
            return;
        }

        RotateActiveFile();
    }

    private void EnsureLogDirectory()
    {
        string directory = GetLogDirectory();
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

        foreach (string path in GetControlledPaths())
        {
            RefreshFileState(path, nowUtc);
        }

        _initialMaintenanceCompleted = true;
        _lastMaintenanceUtcHour = GetUtcHour(nowUtc);
        _lastRotationUtcDate = DateOnly.FromDateTime(nowUtc.UtcDateTime);
    }

    private void DeleteOrphanedTemporaryFiles()
    {
        foreach (string path in GetControlledPaths())
        {
            JsonlRetentionMaintenance.DeleteOrphanedTemporaryFiles(path);
        }
    }

    private void MaintainForUtcHour(DateTimeOffset nowUtc)
    {
        long currentHour = GetUtcHour(nowUtc);
        DateOnly currentDate = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        if (_lastMaintenanceUtcHour == currentHour)
        {
            return;
        }

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

        // With the normal active-tick cadence, expired records remain on disk
        // for less than one extra hour: under 73 hours total, not per-write
        // rewrites of the large legacy files.
        _lastMaintenanceUtcHour = currentHour;
        _nextMaintenanceAttemptUtc = null;
    }

    private void RefreshFileState(string path, DateTimeOffset nowUtc)
    {
        DateTimeOffset cutoffUtc = CalculateCutoff(nowUtc);
        if (JsonlRetentionMaintenance.TryPruneFile(
                path,
                "recordedAtUtc",
                cutoffUtc,
                nowUtc.ToUniversalTime(),
                _maximumFileBytes,
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
        if (!File.Exists(LogPath))
        {
            _fileStates.Remove(LogPath);
            return;
        }

        JsonlRetentionMaintenance.ThrowIfReparsePoint(LogPath);
        if (new FileInfo(LogPath).Length == 0)
        {
            JsonlRetentionMaintenance.DeleteControlledFile(LogPath);
            _fileStates.Remove(LogPath);
            return;
        }

        if (_archiveCount == 0)
        {
            JsonlRetentionMaintenance.DeleteControlledFile(LogPath);
            _fileStates.Remove(LogPath);
            return;
        }

        string oldest = GetArchivePath(_archiveCount);
        JsonlRetentionMaintenance.DeleteControlledFile(oldest);
        _fileStates.Remove(oldest);
        for (int index = _archiveCount - 1; index >= 1; index--)
        {
            MoveControlledFile(
                GetArchivePath(index),
                GetArchivePath(index + 1));
        }

        MoveControlledFile(LogPath, GetArchivePath(1));
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
        yield return LogPath;
        for (int index = 1; index <= _archiveCount; index++)
        {
            yield return GetArchivePath(index);
        }
    }

    private DateTimeOffset CalculateCutoff(DateTimeOffset nowUtc) =>
        nowUtc.ToUniversalTime() - _retention;

    private bool IsMaintenanceDue(DateTimeOffset nowUtc) =>
        !_initialMaintenanceCompleted
        || _lastMaintenanceUtcHour != GetUtcHour(nowUtc);

    private void ScheduleRetentionPreparation()
    {
        Task preparation = PrepareRetentionAsync();
        _ = preparation.ContinueWith(
            static failed => _ = failed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted
            | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RecordMaintenanceFailure(
        DateTimeOffset nowUtc,
        Exception exception)
    {
        LastWriteError = exception.Message;
        _nextMaintenanceAttemptUtc =
            nowUtc.ToUniversalTime() + MaintenanceFailureRetryInterval;
        Debug.WriteLine(
            $"GuluPet tick-score retention maintenance failed: " +
            exception);
    }

    private static long GetUtcHour(DateTimeOffset value) =>
        value.ToUniversalTime().Ticks / TimeSpan.TicksPerHour;

    private string GetArchivePath(int index) =>
        Path.Combine(
            GetLogDirectory(),
            $"{Path.GetFileNameWithoutExtension(LogPath)}.{index}" +
            Path.GetExtension(LogPath));

    private string GetLogDirectory() =>
        Path.GetDirectoryName(LogPath)
        ?? throw new InvalidOperationException(
            "The tick-score log path has no parent directory.");
}
