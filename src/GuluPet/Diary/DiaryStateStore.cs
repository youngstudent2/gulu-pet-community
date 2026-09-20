using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuluPet.Diary;

/// <summary>
/// Persists all diary source data and generated entries locally. Replacement
/// uses an explicit same-directory backup so Windows File.Replace never
/// receives an empty backup path.
/// </summary>
public sealed class DiaryStateStore
{
    private const string StateFileName = "diary.json";
    private const string BackupFileName = "diary.backup.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly object _gate = new();

    public DiaryStateStore()
        : this(Path.Combine(
            global::GuluPet.AppIdentity.LocalDataDirectory,
            StateFileName))
    {
    }

    public DiaryStateStore(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        StatePath = Path.GetFullPath(statePath);
    }

    public string StatePath { get; }

    public DiaryState Load()
    {
        lock (_gate)
        {
            string backupPath = GetBackupPath();
            if (!File.Exists(StatePath) && File.Exists(backupPath))
            {
                File.Move(backupPath, StatePath);
            }

            if (!File.Exists(StatePath))
            {
                return DiaryState.CreateDefault();
            }

            try
            {
                return ReadAndValidate(StatePath);
            }
            catch (Exception exception)
                when (IsStateFailure(exception))
            {
                if (TryQuarantine(StatePath) && File.Exists(backupPath))
                {
                    try
                    {
                        DiaryState recovered = ReadAndValidate(backupPath);
                        File.Move(backupPath, StatePath);
                        return recovered;
                    }
                    catch (Exception backupException)
                        when (IsStateFailure(backupException))
                    {
                        _ = TryQuarantine(backupPath);
                    }
                }

                if (!File.Exists(StatePath) && !File.Exists(backupPath))
                {
                    return DiaryState.CreateDefault();
                }

                throw new InvalidDataException(
                    $"Diary state '{StatePath}' could not be loaded or " +
                    "preserved for recovery.",
                    exception);
            }
        }
    }

    public void Save(DiaryState state)
    {
        DiaryState snapshot = DiaryState.ValidateAndSnapshot(state);
        lock (_gate)
        {
            string directory = Path.GetDirectoryName(StatePath)
                ?? throw new InvalidOperationException(
                    "The diary state path has no parent directory.");
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(StatePath)}.{Guid.NewGuid():N}.tmp");
            string backupPath = GetBackupPath();
            try
            {
                string json = JsonSerializer.Serialize(
                    snapshot,
                    SerializerOptions);
                WriteDurably(temporaryPath, json);
                if (File.Exists(StatePath))
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    File.Replace(
                        temporaryPath,
                        StatePath,
                        backupPath,
                        ignoreMetadataErrors: true);
                    File.Delete(backupPath);
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
    }

    private DiaryState ReadAndValidate(string path)
    {
        string json = File.ReadAllText(path);
        DiaryState state = JsonSerializer.Deserialize<DiaryState>(
                json,
                SerializerOptions)
            ?? throw new InvalidDataException(
                "The diary state document is null.");
        return DiaryState.ValidateAndSnapshot(state);
    }

    private string GetBackupPath()
    {
        string directory = Path.GetDirectoryName(StatePath)
            ?? throw new InvalidOperationException(
                "The diary state path has no parent directory.");
        return Path.Combine(directory, BackupFileName);
    }

    private static bool IsStateFailure(Exception exception) =>
        exception is JsonException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException;

    private static bool TryQuarantine(string path)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            string directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException(
                    "The diary state path has no parent directory.");
            string destination = Path.Combine(
                directory,
                $"diary.corrupt-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-" +
                $"{Guid.NewGuid():N}.json");
            File.Move(path, destination);
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
