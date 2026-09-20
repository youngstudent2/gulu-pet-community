using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuluPet.Persistence;

public sealed record PendingDiaryGeneration
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;

    [JsonRequired]
    public Guid InstallationId { get; init; }

    [JsonRequired]
    public Guid RequestId { get; init; }

    [JsonRequired]
    public DateOnly DiaryDate { get; init; }

    [JsonRequired]
    public DateTimeOffset CreatedAtUtc { get; init; }

    [JsonRequired]
    public required string ContentSha256 { get; init; }

    [JsonRequired]
    public required byte[] Body { get; init; }
}

/// <summary>
/// Durably stores the exact proxy request before its first network byte. A
/// timeout or process exit can therefore replay the same request ID and body
/// instead of charging for a second upstream generation.
/// </summary>
public sealed class PendingDiaryGenerationStore
{
    private const int MaximumBodyBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly object _gate = new();

    public PendingDiaryGenerationStore()
        : this(Path.Combine(
            global::GuluPet.AppIdentity.LocalDataDirectory,
            "PendingDiaryGenerations"))
    {
    }

    public PendingDiaryGenerationStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        DirectoryPath = Path.GetFullPath(directoryPath);
    }

    public string DirectoryPath { get; }

    public PendingDiaryGeneration? Load(DateOnly diaryDate)
    {
        ValidateDate(diaryDate);
        lock (_gate)
        {
            string path = GetPath(diaryDate);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                ValidateExistingPlainDirectory();
                ValidatePlainFile(path);
                PendingDiaryGeneration? pending = JsonSerializer.Deserialize<
                    PendingDiaryGeneration>(File.ReadAllBytes(path), JsonOptions);
                Validate(pending, diaryDate);
                return Snapshot(pending!);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The pending diary generation is invalid.",
                    exception);
            }
        }
    }

    public void SaveNew(PendingDiaryGeneration pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        Validate(pending, pending.DiaryDate);
        lock (_gate)
        {
            EnsurePlainDirectory();
            string destination = GetPath(pending.DiaryDate);
            if (File.Exists(destination))
            {
                throw new IOException(
                    "A pending diary generation already exists for this date.");
            }

            string temporary = Path.Combine(
                DirectoryPath,
                $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
            try
            {
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                    pending,
                    JsonOptions);
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(payload);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }

    public void Clear(DateOnly diaryDate, Guid requestId)
    {
        ValidateDate(diaryDate);
        if (requestId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        lock (_gate)
        {
            string path = GetPath(diaryDate);
            if (!File.Exists(path))
            {
                return;
            }

            PendingDiaryGeneration pending = Load(diaryDate)
                ?? throw new InvalidDataException(
                    "The pending diary generation disappeared unexpectedly.");
            if (pending.RequestId != requestId)
            {
                throw new InvalidDataException(
                    "Refusing to clear a different pending diary generation.");
            }

            ValidatePlainFile(path);
            File.Delete(path);
        }
    }

    /// <summary>
    /// Removes only validated, top-level files owned by this store that fall
    /// outside the retry window. Unknown names, malformed documents and
    /// reparse points are never deleted.
    /// </summary>
    public int PruneBefore(DateOnly oldestRetainedDate)
    {
        ValidateDate(oldestRetainedDate);
        lock (_gate)
        {
            if (!Directory.Exists(DirectoryPath))
            {
                return 0;
            }

            ValidateExistingPlainDirectory();
            var removed = 0;
            foreach (string candidatePath in Directory.EnumerateFiles(
                         DirectoryPath,
                         "diary-*.pending.json",
                         SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(candidatePath);
                if (!TryParseManagedFileName(fileName, out DateOnly date)
                    || date >= oldestRetainedDate
                    || !string.Equals(
                        Path.GetFullPath(candidatePath),
                        GetPath(date),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    ValidatePlainFile(candidatePath);
                    PendingDiaryGeneration? pending = Load(date);
                    if (pending is null)
                    {
                        continue;
                    }

                    Clear(date, pending.RequestId);
                    removed++;
                }
                catch (Exception exception)
                    when (exception is InvalidDataException
                          or IOException
                          or UnauthorizedAccessException)
                {
                    // A file that cannot prove it belongs to this store is
                    // user data for cleanup purposes and must remain intact.
                }
            }

            return removed;
        }
    }

    private void EnsurePlainDirectory()
    {
        Directory.CreateDirectory(DirectoryPath);
        var directory = new DirectoryInfo(DirectoryPath);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The pending diary directory cannot be a reparse point.");
        }
    }

    private void ValidateExistingPlainDirectory()
    {
        var directory = new DirectoryInfo(DirectoryPath);
        if (!directory.Exists
            || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The pending diary directory is unavailable or is a " +
                "reparse point.");
        }
    }

    private static void ValidatePlainFile(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || (attributes & FileAttributes.Directory) != 0)
        {
            throw new InvalidDataException(
                "A pending diary generation cannot be a reparse point.");
        }
    }

    private string GetPath(DateOnly diaryDate) => Path.Combine(
        DirectoryPath,
        $"diary-{diaryDate:yyyy-MM-dd}.pending.json");

    private static void Validate(
        PendingDiaryGeneration? pending,
        DateOnly expectedDate)
    {
        if (pending is null
            || pending.SchemaVersion != 1
            || pending.InstallationId == Guid.Empty
            || pending.RequestId == Guid.Empty
            || pending.DiaryDate != expectedDate
            || pending.CreatedAtUtc == default
            || pending.Body is null
            || pending.Body.Length is < 2 or > MaximumBodyBytes
            || pending.ContentSha256 is null
            || pending.ContentSha256.Length != 64
            || pending.ContentSha256.Any(character =>
                !char.IsAsciiHexDigit(character)
                || char.IsUpper(character)))
        {
            throw new InvalidDataException(
                "The pending diary generation is malformed.");
        }

        string actualHash = Convert.ToHexString(SHA256.HashData(pending.Body))
            .ToLowerInvariant();
        if (!string.Equals(
                actualHash,
                pending.ContentSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The pending diary generation body failed authentication.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(pending.Body);
            JsonElement root = document.RootElement;
            string expectedDateText = expectedDate.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out JsonElement schema)
                || schema.ValueKind != JsonValueKind.Number
                || schema.GetInt32() != 1
                || !root.TryGetProperty("requestId", out JsonElement requestId)
                || requestId.ValueKind != JsonValueKind.String
                || requestId.GetGuid() != pending.RequestId
                || !root.TryGetProperty("diaryDate", out JsonElement diaryDate)
                || diaryDate.ValueKind != JsonValueKind.String
                || !string.Equals(
                    diaryDate.GetString(),
                    expectedDateText,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The pending diary generation body is not bound to its " +
                    "request metadata.");
            }
        }
        catch (Exception exception)
            when (exception is JsonException
                  or FormatException
                  or InvalidOperationException)
        {
            throw new InvalidDataException(
                "The pending diary generation body is invalid.",
                exception);
        }
    }

    private static PendingDiaryGeneration Snapshot(
        PendingDiaryGeneration pending) =>
        pending with { Body = pending.Body.ToArray() };

    private static void ValidateDate(DateOnly diaryDate)
    {
        if (diaryDate == default)
        {
            throw new ArgumentOutOfRangeException(nameof(diaryDate));
        }
    }

    private static bool TryParseManagedFileName(
        string fileName,
        out DateOnly diaryDate)
    {
        const string prefix = "diary-";
        const string suffix = ".pending.json";
        const int dateLength = 10;
        diaryDate = default;
        if (fileName.Length != prefix.Length + dateLength + suffix.Length
            || !fileName.StartsWith(prefix, StringComparison.Ordinal)
            || !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        string dateText = fileName.Substring(prefix.Length, dateLength);
        return DateOnly.TryParseExact(
                dateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out diaryDate)
            && string.Equals(
                fileName,
                $"diary-{diaryDate:yyyy-MM-dd}.pending.json",
                StringComparison.Ordinal);
    }
}
