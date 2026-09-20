using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuluPet.Memories;

/// <summary>
/// Persists memory progress independently from user preferences. Saves use a
/// same-directory temporary file followed by atomic replacement. Unreadable
/// state is preserved under a corrupt-file name before progress starts over.
/// </summary>
public sealed class MemoryProgressStore
{
    private const string StateFileName = "memories.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly object _gate = new();

    public MemoryProgressStore()
        : this(Path.Combine(
            global::GuluPet.AppIdentity.LocalDataDirectory,
            StateFileName))
    {
    }

    public MemoryProgressStore(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        StatePath = Path.GetFullPath(statePath);
    }

    public string StatePath { get; }

    public MemoryProgressState Load()
    {
        lock (_gate)
        {
            if (!File.Exists(StatePath))
            {
                return MemoryProgressState.CreateDefault();
            }

            try
            {
                string json = File.ReadAllText(StatePath);
                MemoryProgressState state =
                    JsonSerializer.Deserialize<MemoryProgressState>(
                        json,
                        SerializerOptions)
                    ?? throw new InvalidDataException(
                        "The memory state document is null.");
                return MemoryProgressState.ValidateAndSnapshot(state);
            }
            catch (Exception exception)
                when (exception is JsonException
                      or InvalidDataException
                      or IOException
                      or UnauthorizedAccessException)
            {
                if (TryQuarantine())
                {
                    return MemoryProgressState.CreateDefault();
                }

                throw new InvalidDataException(
                    $"Memory state '{StatePath}' could not be loaded or " +
                    "preserved for recovery.",
                    exception);
            }
        }
    }

    public void Save(MemoryProgressState state)
    {
        MemoryProgressState snapshot =
            MemoryProgressState.ValidateAndSnapshot(state);
        lock (_gate)
        {
            string? directory = Path.GetDirectoryName(StatePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException(
                    "The memory state path has no parent directory.");
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
                    "The memory state path has no parent directory.");
            string corruptPath = Path.Combine(
                directory,
                $"memories.corrupt-" +
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

        try
        {
            File.Replace(
                temporaryPath,
                StatePath,
                destinationBackupFileName: null,
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
