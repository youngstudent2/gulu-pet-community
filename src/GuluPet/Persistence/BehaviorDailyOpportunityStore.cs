using System.IO;
using System.Text.Json;
using GuluPet.Behavior;

namespace GuluPet.Persistence;

public sealed class BehaviorDailyOpportunityStore
    : IBehaviorDailyOpportunityLedger
{
    private const int SchemaVersion = 2;
    private const string StateFileName = "daily-behavior-opportunities.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private Dictionary<string, StoredDailyOpportunityUsage>? _usage;

    public BehaviorDailyOpportunityStore()
        : this(Path.Combine(
            global::GuluPet.AppIdentity.LocalDataDirectory,
            StateFileName))
    {
    }

    public BehaviorDailyOpportunityStore(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        StatePath = Path.GetFullPath(statePath);
    }

    public string StatePath { get; }

    public BehaviorDailyOpportunityUsage Read(
        string key,
        DateOnly localDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            Dictionary<string, StoredDailyOpportunityUsage> usage =
                LoadUnderLock();
            return usage.TryGetValue(
                       key,
                       out StoredDailyOpportunityUsage? stored) &&
                   stored.LocalDate == localDate
                ? stored.ToUsage()
                : new BehaviorDailyOpportunityUsage(
                    localDate,
                    AcceptedCount: 0,
                    LastAcceptedAt: null);
        }
    }

    public bool TryRecordAccepted(
        string key,
        DateOnly localDate,
        DateTimeOffset acceptedAt,
        int maximumAcceptedCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumAcceptedCount,
            1);
        if (DateOnly.FromDateTime(acceptedAt.Date) != localDate)
        {
            throw new ArgumentException(
                "The acceptance timestamp must belong to the local date.",
                nameof(acceptedAt));
        }

        lock (_gate)
        {
            Dictionary<string, StoredDailyOpportunityUsage> usage =
                LoadUnderLock();
            StoredDailyOpportunityUsage current =
                usage.TryGetValue(
                    key,
                    out StoredDailyOpportunityUsage? stored) &&
                stored.LocalDate == localDate
                    ? stored
                    : new StoredDailyOpportunityUsage
                    {
                        LocalDate = localDate,
                    };
            if (current.AcceptedCount >= maximumAcceptedCount)
            {
                return false;
            }

            var updated = new Dictionary<string, StoredDailyOpportunityUsage>(
                usage,
                StringComparer.Ordinal)
            {
                [key] = new StoredDailyOpportunityUsage
                {
                    LocalDate = localDate,
                    AcceptedCount = checked(current.AcceptedCount + 1),
                    LastAcceptedAt = acceptedAt,
                },
            };
            try
            {
                SaveUnderLock(updated);
                _usage = updated;
                return true;
            }
            catch (IOException)
            {
                _usage = updated;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                _usage = updated;
                return false;
            }
        }
    }

    private Dictionary<string, StoredDailyOpportunityUsage> LoadUnderLock()
    {
        if (_usage is not null)
        {
            return _usage;
        }

        try
        {
            if (File.Exists(StatePath))
            {
                string json = File.ReadAllText(StatePath);
                DailyOpportunityState? state =
                    JsonSerializer.Deserialize<DailyOpportunityState>(
                        json,
                        SerializerOptions);
                if (state?.SchemaVersion == SchemaVersion &&
                    state.Opportunities is not null)
                {
                    _usage = state.Opportunities
                        .Where(static pair =>
                            !string.IsNullOrWhiteSpace(pair.Key) &&
                            pair.Value is
                            {
                                AcceptedCount: >= 0
                            })
                        .ToDictionary(
                            static pair => pair.Key,
                            static pair => pair.Value,
                            StringComparer.Ordinal);
                    return _usage;
                }

                LegacyDailyOpportunityState? legacy =
                    JsonSerializer.Deserialize<LegacyDailyOpportunityState>(
                        json,
                        SerializerOptions);
                if (legacy?.CompletedOn is not null)
                {
                    _usage = legacy.CompletedOn
                        .Where(static pair =>
                            !string.IsNullOrWhiteSpace(pair.Key))
                        .ToDictionary(
                            static pair => pair.Key,
                            static pair =>
                                new StoredDailyOpportunityUsage
                                {
                                    LocalDate = pair.Value,
                                    AcceptedCount = 1,
                                    LastAcceptedAt = null,
                                },
                            StringComparer.Ordinal);
                    return _usage;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }

        _usage = new Dictionary<string, StoredDailyOpportunityUsage>(
            StringComparer.Ordinal);
        return _usage;
    }

    private void SaveUnderLock(
        IReadOnlyDictionary<string, StoredDailyOpportunityUsage> usage)
    {
        string? directory = Path.GetDirectoryName(StatePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "The daily opportunity state path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(StatePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var state = new DailyOpportunityState
            {
                SchemaVersion = SchemaVersion,
                Opportunities =
                    new Dictionary<string, StoredDailyOpportunityUsage>(
                    usage,
                    StringComparer.Ordinal),
            };
            string json = JsonSerializer.Serialize(state, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(StatePath))
            {
                try
                {
                    File.Replace(
                        temporaryPath,
                        StatePath,
                        null,
                        ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, StatePath, overwrite: true);
                }
                catch (IOException)
                {
                    File.Move(temporaryPath, StatePath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, StatePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed class DailyOpportunityState
    {
        public int SchemaVersion { get; init; }

        public Dictionary<string, StoredDailyOpportunityUsage> Opportunities
        {
            get;
            init;
        } =
            new(StringComparer.Ordinal);
    }

    private sealed class LegacyDailyOpportunityState
    {
        public Dictionary<string, DateOnly>? CompletedOn { get; init; }
    }

    private sealed record StoredDailyOpportunityUsage
    {
        public DateOnly LocalDate { get; init; }

        public int AcceptedCount { get; init; }

        public DateTimeOffset? LastAcceptedAt { get; init; }

        public BehaviorDailyOpportunityUsage ToUsage() =>
            new(LocalDate, AcceptedCount, LastAcceptedAt);
    }
}
