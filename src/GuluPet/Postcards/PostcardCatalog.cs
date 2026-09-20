using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace GuluPet.Postcards;

public sealed class PostcardCatalog
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly IReadOnlyDictionary<string, PostcardDefinition> _byId;

    internal PostcardCatalog(IEnumerable<PostcardDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        PostcardDefinition[] ordered = definitions
            .OrderBy(static definition => definition.UnlockOrder)
            .ThenBy(static definition => definition.Id, StringComparer.Ordinal)
            .ToArray();
        ValidateDefinitions(ordered);
        Definitions = ordered;
        _byId = ordered.ToDictionary(
            static definition => definition.Id,
            StringComparer.Ordinal);
    }

    public IReadOnlyList<PostcardDefinition> Definitions { get; }

    public int Count => Definitions.Count;

    public PostcardDefinition this[string id] =>
        _byId.TryGetValue(id, out PostcardDefinition? definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown postcard '{id}'.");

    public bool TryGet(
        string id,
        out PostcardDefinition? definition)
    {
        definition = null;
        return !string.IsNullOrWhiteSpace(id)
            && _byId.TryGetValue(id, out definition);
    }

    /// <summary>
    /// Projects a durable collection onto this catalog. Records belonging to
    /// another catalog version are deliberately ignored, but retained by the
    /// caller's state. Known records are returned in the current catalog order
    /// even when catalog evolution leaves holes in the historical set.
    /// </summary>
    public IReadOnlyList<PostcardDefinition> GetUnlockedDefinitions(
        PostcardCollectionState state)
    {
        PostcardCollectionState snapshot =
            PostcardCollectionState.ValidateAndSnapshot(state);
        HashSet<string> unlockedIds = snapshot.Unlocked
            .Select(static record => record.PostcardId)
            .ToHashSet(StringComparer.Ordinal);
        return Definitions
            .Where(definition => unlockedIds.Contains(definition.Id))
            .ToArray();
    }

    public static PostcardCatalog LoadFromAssets(string assetsRoot) =>
        Load(GetCatalogPath(assetsRoot));

    public static Task<PostcardCatalog> LoadFromAssetsAsync(
        string assetsRoot,
        CancellationToken cancellationToken = default) =>
        LoadAsync(GetCatalogPath(assetsRoot), cancellationToken);

    public static PostcardCatalog Load(string catalogPath)
    {
        string fullCatalogPath = ResolveCatalogPath(catalogPath);
        try
        {
            string json = File.ReadAllText(fullCatalogPath);
            CatalogDocument document =
                JsonSerializer.Deserialize<CatalogDocument>(
                    json,
                    SerializerOptions)
                ?? throw new InvalidDataException(
                    "The postcard catalog document is null.");
            return CreateValidated(document, fullCatalogPath);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Postcard catalog '{fullCatalogPath}' is not valid JSON.",
                exception);
        }
    }

    public static async Task<PostcardCatalog> LoadAsync(
        string catalogPath,
        CancellationToken cancellationToken = default)
    {
        string fullCatalogPath = ResolveCatalogPath(catalogPath);
        try
        {
            await using var stream = new FileStream(
                fullCatalogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
            CatalogDocument document =
                await JsonSerializer.DeserializeAsync<CatalogDocument>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                ?? throw new InvalidDataException(
                    "The postcard catalog document is null.");
            cancellationToken.ThrowIfCancellationRequested();
            return CreateValidated(document, fullCatalogPath);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Postcard catalog '{fullCatalogPath}' is not valid JSON.",
                exception);
        }
    }

    internal static bool IsValidId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 96
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        return value.All(static character =>
            character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-'
                or '_');
    }

    private static string GetCatalogPath(string assetsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsRoot);
        return Path.Combine(
            Path.GetFullPath(assetsRoot),
            "Postcards",
            "catalog.json");
    }

    private static string ResolveCatalogPath(string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        return Path.GetFullPath(catalogPath);
    }

    private static PostcardCatalog CreateValidated(
        CatalogDocument document,
        string catalogPath)
    {
        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported postcard catalog schema version " +
                $"'{document.SchemaVersion}'; expected " +
                $"'{CurrentSchemaVersion}'.");
        }

        if (document.Postcards is null || document.Postcards.Count == 0)
        {
            throw new InvalidDataException(
                "The postcard catalog must contain at least one postcard.");
        }

        string catalogDirectory = Path.GetDirectoryName(catalogPath)
            ?? throw new InvalidDataException(
                "The postcard catalog has no parent directory.");
        var definitions = new PostcardDefinition[document.Postcards.Count];
        for (var index = 0; index < document.Postcards.Count; index++)
        {
            CatalogItem item = document.Postcards[index]
                ?? throw new InvalidDataException(
                    $"Postcard catalog item at index {index} is null.");
            ValidateText(item.Title, "title", index);
            ValidateText(item.Location, "location", index);
            if (!IsValidId(item.Id))
            {
                throw new InvalidDataException(
                    $"Postcard catalog item at index {index} has invalid id " +
                    $"'{item.Id}'.");
            }

            string imagePath = ResolveImagePath(
                catalogDirectory,
                item.ImagePath,
                item.Id);
            ValidateDecodableImage(imagePath, item.Id);
            string altText = string.IsNullOrWhiteSpace(item.AltText)
                ? $"{item.Title}，{item.Location}"
                : item.AltText.Trim();
            definitions[index] = new PostcardDefinition(
                item.Id,
                item.Title.Trim(),
                item.Location.Trim(),
                imagePath,
                item.UnlockOrder,
                altText,
                item.Chapter?.Trim() ?? string.Empty);
        }

        return new PostcardCatalog(definitions);
    }

    private static string ResolveImagePath(
        string catalogDirectory,
        string? relativeImagePath,
        string postcardId)
    {
        if (string.IsNullOrWhiteSpace(relativeImagePath))
        {
            throw new InvalidDataException(
                $"Postcard '{postcardId}' has no imagePath.");
        }

        string fullPath = Path.GetFullPath(
            Path.Combine(
                catalogDirectory,
                relativeImagePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
        string relativePath = Path.GetRelativePath(
            catalogDirectory,
            fullPath);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Postcard '{postcardId}' imagePath leaves the postcard " +
                "asset directory.");
        }

        string extension = Path.GetExtension(fullPath);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Postcard '{postcardId}' image must be PNG or JPEG.");
        }

        if (!File.Exists(fullPath))
        {
            throw new InvalidDataException(
                $"Postcard '{postcardId}' image '{fullPath}' does not exist.");
        }

        return fullPath;
    }

    private static void ValidateDecodableImage(
        string imagePath,
        string postcardId)
    {
        try
        {
            using var stream = new FileStream(
                imagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0
                || decoder.Frames[0].PixelWidth <= 0
                || decoder.Frames[0].PixelHeight <= 0)
            {
                throw new InvalidDataException(
                    "The decoded image contains no usable frame.");
            }
        }
        catch (Exception exception)
            when (exception is not InvalidDataException)
        {
            throw new InvalidDataException(
                $"Postcard '{postcardId}' image '{imagePath}' cannot be decoded.",
                exception);
        }
    }

    private static void ValidateDefinitions(
        IReadOnlyList<PostcardDefinition> definitions)
    {
        if (definitions.Count == 0)
        {
            throw new InvalidDataException(
                "The postcard catalog must contain at least one postcard.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < definitions.Count; index++)
        {
            PostcardDefinition definition = definitions[index]
                ?? throw new InvalidDataException(
                    $"Postcard definition at index {index} is null.");
            if (!IsValidId(definition.Id))
            {
                throw new InvalidDataException(
                    $"Postcard definition at index {index} has invalid id " +
                    $"'{definition.Id}'.");
            }

            if (!ids.Add(definition.Id))
            {
                throw new InvalidDataException(
                    $"Postcard id '{definition.Id}' is duplicated.");
            }

            int expectedOrder = checked(index + 1);
            if (definition.UnlockOrder != expectedOrder)
            {
                throw new InvalidDataException(
                    $"Postcard '{definition.Id}' has unlockOrder " +
                    $"'{definition.UnlockOrder}'; catalogs must use every " +
                    $"order from 1 through {definitions.Count}, so this item " +
                    $"must be '{expectedOrder}'.");
            }

            ValidateText(definition.Title, "title", index);
            ValidateText(definition.Location, "location", index);
            if (string.IsNullOrWhiteSpace(definition.ImagePath)
                || !Path.IsPathFullyQualified(definition.ImagePath))
            {
                throw new InvalidDataException(
                    $"Postcard '{definition.Id}' image path must be absolute.");
            }

            if (string.IsNullOrWhiteSpace(definition.AltText))
            {
                throw new InvalidDataException(
                    $"Postcard '{definition.Id}' alt text is required.");
            }

            if (!string.IsNullOrEmpty(definition.Chapter)
                && (string.IsNullOrWhiteSpace(definition.Chapter)
                    || definition.Chapter.Length > 40
                    || !string.Equals(
                        definition.Chapter,
                        definition.Chapter.Trim(),
                        StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"Postcard '{definition.Id}' has an invalid chapter.");
            }
        }

        const int cardsPerChapter = 20;
        bool requiresChapterBlocks = definitions.Count >= cardsPerChapter
            || definitions.Any(
                static definition => !string.IsNullOrWhiteSpace(
                    definition.Chapter));
        if (requiresChapterBlocks)
        {
            if (definitions.Count % cardsPerChapter != 0)
            {
                throw new InvalidDataException(
                    "A chaptered postcard catalog must contain complete " +
                    $"blocks of {cardsPerChapter} unlocks.");
            }

            int chapterCount = definitions.Count / cardsPerChapter;
            var chapterNames = new HashSet<string>(StringComparer.Ordinal);
            for (var chapterIndex = 0;
                 chapterIndex < chapterCount;
                 chapterIndex++)
            {
                int chapterStart = chapterIndex * cardsPerChapter;
                string chapter = definitions[chapterStart].Chapter;
                if (string.IsNullOrWhiteSpace(chapter)
                    || !chapterNames.Add(chapter))
                {
                    throw new InvalidDataException(
                        "A chaptered postcard catalog must contain distinct " +
                        "named chapters in unlock order.");
                }

                for (var offset = 0;
                     offset < cardsPerChapter;
                     offset++)
                {
                    PostcardDefinition definition =
                        definitions[chapterStart + offset];
                    if (!string.Equals(
                            definition.Chapter,
                            chapter,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Postcard '{definition.Id}' breaks chapter " +
                            $"'{chapter}' at position {offset + 1}; each " +
                            $"chapter must be one contiguous block of exactly " +
                            $"{cardsPerChapter} unlocks.");
                    }
                }
            }
        }
    }

    private static void ValidateText(
        string? value,
        string fieldName,
        int index)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 120)
        {
            throw new InvalidDataException(
                $"Postcard catalog item at index {index} has invalid " +
                $"{fieldName}.");
        }
    }

    private sealed class CatalogDocument
    {
        [JsonRequired]
        public int SchemaVersion { get; init; }

        [JsonRequired]
        public IReadOnlyList<CatalogItem?>? Postcards { get; init; }
    }

    private sealed class CatalogItem
    {
        [JsonRequired]
        public string Id { get; init; } = string.Empty;

        [JsonRequired]
        public string Title { get; init; } = string.Empty;

        [JsonRequired]
        public string Location { get; init; } = string.Empty;

        [JsonRequired]
        public string ImagePath { get; init; } = string.Empty;

        [JsonRequired]
        public int UnlockOrder { get; init; }

        public string? AltText { get; init; }

        public string? Chapter { get; init; }
    }
}
