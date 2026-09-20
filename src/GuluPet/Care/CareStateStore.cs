using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuluPet.Care;

/// <summary>
/// Persists care state with a durable same-directory temporary write followed
/// by atomic replacement. Invalid documents are quarantined before defaults
/// are returned.
/// </summary>
public sealed class CareStateStore
{
    private const string StateFileName = "care.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly object _gate = new();

    public CareStateStore()
        : this(
            Path.Combine(
                global::GuluPet.AppIdentity.LocalDataDirectory,
                StateFileName))
    {
    }

    public CareStateStore(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        StatePath = Path.GetFullPath(statePath);
    }

    public string StatePath { get; }

    public CareState Load()
    {
        lock (_gate)
        {
            if (!File.Exists(StatePath))
            {
                return CareState.CreateDefault();
            }

            try
            {
                string json = File.ReadAllText(StatePath);
                CareState state = JsonSerializer.Deserialize<CareState>(
                        json,
                        SerializerOptions)
                    ?? throw new InvalidDataException(
                        "The care state document is null.");
                return CareState.ValidateAndSnapshot(state);
            }
            catch (Exception exception)
                when (exception is JsonException
                      or InvalidDataException
                      or IOException
                      or UnauthorizedAccessException)
            {
                if (TryQuarantine())
                {
                    return CareState.CreateDefault();
                }

                throw new InvalidDataException(
                    $"Care state '{StatePath}' could not be loaded or " +
                    "preserved for recovery.",
                    exception);
            }
        }
    }

    public void Save(CareState state)
    {
        CareState snapshot = CareState.ValidateAndSnapshot(state);
        lock (_gate)
        {
            string? directory = Path.GetDirectoryName(StatePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException(
                    "The care state path has no parent directory.");
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(StatePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                string json = JsonSerializer.Serialize(
                    snapshot,
                    SerializerOptions);
                WriteDurably(temporaryPath, json);
                Commit(temporaryPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private bool TryQuarantine()
    {
        if (!File.Exists(StatePath))
        {
            return true;
        }

        try
        {
            string directory = Path.GetDirectoryName(StatePath)
                ?? throw new InvalidOperationException(
                    "The care state path has no parent directory.");
            string corruptPath = Path.Combine(
                directory,
                $"care.corrupt-" +
                $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-" +
                $"{Guid.NewGuid():N}.json");
            File.Move(StatePath, corruptPath);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or InvalidOperationException)
        {
            return false;
        }
    }

    private void Commit(string temporaryPath)
    {
        if (!File.Exists(StatePath))
        {
            File.Move(temporaryPath, StatePath);
            return;
        }

        // GuluPet targets Windows, where File.Replace is the atomic
        // same-volume replacement primitive. Never delete the previous valid
        // care document before its replacement is guaranteed.
        File.Replace(
            temporaryPath,
            StatePath,
            destinationBackupFileName: null,
            ignoreMetadataErrors: true);
    }

    private static void WriteDurably(string path, string contents)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using (var writer = new StreamWriter(
                   stream,
                   new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                   bufferSize: 16 * 1024,
                   leaveOpen: true))
        {
            writer.Write(contents);
            writer.Flush();
        }

        stream.Flush(flushToDisk: true);
    }
}
