using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuluPet.Postcards;

/// <summary>
/// Persists postcard progress independently from user preferences. Saves use
/// a same-directory temporary file followed by an atomic replacement.
/// </summary>
public sealed class PostcardProgressStore
{
    private const string StateFileName = "postcards.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly object _gate = new();

    public PostcardProgressStore()
        : this(Path.Combine(
            global::GuluPet.AppIdentity.LocalDataDirectory,
            StateFileName))
    {
    }

    public PostcardProgressStore(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        StatePath = Path.GetFullPath(statePath);
    }

    public string StatePath { get; }

    public PostcardCollectionState Load()
    {
        lock (_gate)
        {
            if (!File.Exists(StatePath))
            {
                return PostcardCollectionState.CreateDefault();
            }

            try
            {
                string json = File.ReadAllText(StatePath);
                PostcardCollectionState state = DeserializeState(json);
                return PostcardCollectionState.ValidateAndSnapshot(state);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"Postcard state '{StatePath}' is not valid JSON.",
                    exception);
            }
        }
    }

    public void Save(PostcardCollectionState state)
    {
        PostcardCollectionState snapshot =
            PostcardCollectionState.ValidateAndSnapshot(state);
        lock (_gate)
        {
            string? directory = Path.GetDirectoryName(StatePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException(
                    "The postcard state path has no parent directory.");
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

    private static PostcardCollectionState DeserializeState(string json)
    {
        int schemaVersion = ReadSchemaVersion(json);
        if (schemaVersion == PostcardCollectionState.CurrentSchemaVersion)
        {
            return JsonSerializer.Deserialize<PostcardCollectionState>(
                       json,
                       SerializerOptions)
                   ?? throw new InvalidDataException(
                       "The postcard state document is null.");
        }

        if (schemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported postcard state schema version " +
                $"'{schemaVersion}'; expected '1' or " +
                $"'{PostcardCollectionState.CurrentSchemaVersion}'.");
        }

        LegacyPostcardCollectionState state =
            JsonSerializer.Deserialize<LegacyPostcardCollectionState>(
                json,
                SerializerOptions)
            ?? throw new InvalidDataException(
                "The legacy postcard state document is null.");
        if (state.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported legacy postcard state schema version " +
                $"'{state.SchemaVersion}'.");
        }

        return new PostcardCollectionState
        {
            SchemaVersion = PostcardCollectionState.CurrentSchemaVersion,
            // Kept only as historical data. The outing tracker never derives
            // unlocks from this migrated value.
            TotalInputCount = state.TotalInputCount,
            Unlocked = state.Unlocked,
            PendingCatalogSummary = state.PendingCatalogSummary,
            PendingTravelEcho = state.PendingTravelEcho,
            CurrentOuting = null,
            UpdatedAtUtc = state.UpdatedAtUtc,
        };
    }

    private static int ReadSchemaVersion(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "The postcard state document must be a JSON object.");
        }

        foreach (JsonProperty property in document.RootElement
                     .EnumerateObject())
        {
            if (!property.Name.Equals(
                    "schemaVersion",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Number
                || !property.Value.TryGetInt32(out int schemaVersion))
            {
                throw new InvalidDataException(
                    "Postcard state schemaVersion must be an integer.");
            }

            return schemaVersion;
        }

        throw new InvalidDataException(
            "Postcard state must declare schemaVersion.");
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
            // Some Windows file systems reject Replace for files without the
            // required metadata. A same-volume overwrite is the narrow
            // fallback and the temporary file still prevents partial JSON.
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

    private sealed record LegacyPostcardCollectionState
    {
        [JsonRequired]
        public int SchemaVersion { get; init; }

        public long TotalInputCount { get; init; }

        public IReadOnlyList<PostcardUnlockRecord> Unlocked { get; init; } = [];

        public PostcardCatalogSummaryRecord? PendingCatalogSummary { get; init; }

        public PostcardTravelEchoRecord? PendingTravelEcho { get; init; }

        public DateTimeOffset UpdatedAtUtc { get; init; }
    }
}
