using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuluPet.Domain;

namespace GuluPet.Persistence;

/// <summary>
/// Persists long-term relationship state with a durable same-directory write
/// followed by atomic replacement. Invalid documents are quarantined before a
/// default relationship is returned.
/// </summary>
public sealed class RelationshipStateStore
{
    private const string StateFileName = "relationship.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly object _gate = new();

    public RelationshipStateStore()
        : this(
            Path.Combine(
                global::GuluPet.AppIdentity.LocalDataDirectory,
                StateFileName))
    {
    }

    public RelationshipStateStore(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        StatePath = Path.GetFullPath(statePath);
    }

    public string StatePath { get; }

    public RelationshipState Load()
    {
        lock (_gate)
        {
            if (!File.Exists(StatePath))
            {
                return RelationshipState.CreateDefault();
            }

            try
            {
                string json = File.ReadAllText(StatePath);
                RelationshipState state =
                    JsonSerializer.Deserialize<RelationshipState>(
                        json,
                        SerializerOptions)
                    ?? throw new InvalidDataException(
                        "The relationship state document is null.");
                return RelationshipState.ValidateAndSnapshot(state);
            }
            catch (Exception exception)
                when (exception is JsonException
                      or InvalidDataException
                      or IOException
                      or UnauthorizedAccessException)
            {
                if (TryQuarantine())
                {
                    return RelationshipState.CreateDefault();
                }

                throw new InvalidDataException(
                    $"Relationship state '{StatePath}' could not be loaded or " +
                    "preserved for recovery.",
                    exception);
            }
        }
    }

    public void Save(RelationshipState state)
    {
        RelationshipState snapshot =
            RelationshipState.ValidateAndSnapshot(state);
        lock (_gate)
        {
            string? directory = Path.GetDirectoryName(StatePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException(
                    "The relationship state path has no parent directory.");
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
                    "The relationship state path has no parent directory.");
            string corruptPath = Path.Combine(
                directory,
                $"relationship.corrupt-" +
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
        // same-volume replacement primitive. Do not fall back to a
        // delete-and-move sequence: the previous valid relationship must
        // survive if replacement cannot be guaranteed.
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
