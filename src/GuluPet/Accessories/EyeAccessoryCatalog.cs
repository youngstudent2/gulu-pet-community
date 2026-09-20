using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;

namespace GuluPet.Accessories;

/// <summary>
/// Strict, immutable catalog of the nineteen selectable runtime eye accessories.
/// Loading validates identity, ordering, paths, file sizes, hashes, PNG
/// dimensions, alpha content, and per-image registration points.
/// </summary>
public sealed class EyeAccessoryCatalog
{
    public const string LegacySchema = "gulu-eye-accessory-manifest-v1";
    public const string CurrentSchema = "gulu-eye-accessory-manifest-v2";
    public const string ManifestFileName = "manifest.json";
    public const int RequiredAccessoryCount = 19;

    private readonly IReadOnlyDictionary<string, EyeAccessoryDefinition> _byId;

    private EyeAccessoryCatalog(
        string rootPath,
        string manifestPath,
        string contentVersion,
        IReadOnlyList<EyeAccessoryDefinition> accessories)
    {
        RootPath = rootPath;
        ManifestPath = manifestPath;
        ContentVersion = contentVersion;
        Accessories = accessories;
        _byId = accessories.ToDictionary(
            static accessory => accessory.Id,
            StringComparer.Ordinal);
    }

    public string RootPath { get; }

    public string ManifestPath { get; }

    public string ContentVersion { get; }

    public IReadOnlyList<EyeAccessoryDefinition> Accessories { get; }

    public int Count => Accessories.Count;

    public EyeAccessoryDefinition this[string id] =>
        _byId.TryGetValue(id, out EyeAccessoryDefinition? accessory)
            ? accessory
            : throw new KeyNotFoundException(
                $"Unknown eye accessory '{id}'.");

    public bool TryGet(
        string? id,
        out EyeAccessoryDefinition? accessory)
    {
        accessory = null;
        return !string.IsNullOrWhiteSpace(id)
            && _byId.TryGetValue(id, out accessory);
    }

    public static EyeAccessoryCatalog LoadFromAssets(string assetsRoot) =>
        Load(EyeAccessoryContentReader.ResolveFromAssetsRoot(assetsRoot));

    public static Task<EyeAccessoryCatalog> LoadFromAssetsAsync(
        string assetsRoot,
        CancellationToken cancellationToken = default) =>
        LoadAsync(
            EyeAccessoryContentReader.ResolveFromAssetsRoot(assetsRoot),
            cancellationToken);

    public static EyeAccessoryCatalog Load(string accessoriesRoot)
    {
        string root = EyeAccessoryContentReader.ResolveAccessoriesRoot(
            accessoriesRoot);
        string manifestPath = Path.Combine(root, ManifestFileName);
        ManifestDocument document =
            EyeAccessoryContentReader.ReadJson<ManifestDocument>(
                manifestPath,
                "eye accessory manifest");
        return CreateValidated(
            root,
            manifestPath,
            document,
            static validation =>
            {
                EyeAccessoryContentReader.ValidatePng(
                    validation.Path,
                    validation.Width,
                    validation.Height,
                    validation.Sha256,
                    validation.SizeBytes,
                    validation.Owner);
                return Task.CompletedTask;
            },
            CancellationToken.None).GetAwaiter().GetResult();
    }

    public static async Task<EyeAccessoryCatalog> LoadAsync(
        string accessoriesRoot,
        CancellationToken cancellationToken = default)
    {
        string root = EyeAccessoryContentReader.ResolveAccessoriesRoot(
            accessoriesRoot);
        string manifestPath = Path.Combine(root, ManifestFileName);
        ManifestDocument document =
            await EyeAccessoryContentReader.ReadJsonAsync<ManifestDocument>(
                    manifestPath,
                    "eye accessory manifest",
                    cancellationToken)
                .ConfigureAwait(false);
        return await CreateValidated(
                root,
                manifestPath,
                document,
                validation => EyeAccessoryContentReader.ValidatePngAsync(
                    validation.Path,
                    validation.Width,
                    validation.Height,
                    validation.Sha256,
                    validation.SizeBytes,
                    validation.Owner,
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<EyeAccessoryCatalog> CreateValidated(
        string root,
        string manifestPath,
        ManifestDocument document,
        Func<ImageValidation, Task> validateImage,
        CancellationToken cancellationToken)
    {
        bool isLegacySchema = string.Equals(
            document.Schema,
            LegacySchema,
            StringComparison.Ordinal);
        bool isCurrentSchema = string.Equals(
            document.Schema,
            CurrentSchema,
            StringComparison.Ordinal);
        if (!isLegacySchema && !isCurrentSchema)
        {
            throw new InvalidDataException(
                $"Eye accessory manifest schema must be '{LegacySchema}' or " +
                $"'{CurrentSchema}', but is '{document.Schema}'.");
        }

        string contentVersion = EyeAccessoryContentReader.ValidateText(
            document.ContentVersion,
            "Eye accessory manifest contentVersion");
        if (document.AccessoryCount != RequiredAccessoryCount)
        {
            throw new InvalidDataException(
                $"Eye accessory manifest accessoryCount must be " +
                $"{RequiredAccessoryCount}, but is {document.AccessoryCount}.");
        }

        if (document.Accessories is null
            || document.Accessories.Count != RequiredAccessoryCount)
        {
            throw new InvalidDataException(
                $"Eye accessory manifest must contain exactly " +
                $"{RequiredAccessoryCount} accessory entries.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var imagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var definitions = new EyeAccessoryDefinition[RequiredAccessoryCount];
        for (var index = 0; index < document.Accessories.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AccessoryDocument item = document.Accessories[index]
                ?? throw new InvalidDataException(
                    $"Eye accessory manifest item at index {index} is null.");
            string owner = $"Eye accessory manifest item at index {index}";
            EyeAccessoryContentReader.ValidateId(item.Id, $"{owner} id");
            string id = item.Id!;
            if (!ids.Add(id))
            {
                throw new InvalidDataException(
                    $"Eye accessory id '{id}' is duplicated.");
            }

            string displayName = EyeAccessoryContentReader.ValidateText(
                item.DisplayName,
                $"Eye accessory '{id}' displayName");
            if (item.SelectionOrder < 1
                || item.SelectionOrder > RequiredAccessoryCount)
            {
                throw new InvalidDataException(
                    $"Eye accessory '{id}' selectionOrder must be from 1 " +
                    $"through {RequiredAccessoryCount}.");
            }

            IReadOnlyList<(EyeAccessoryViewState State, ImageDocument Image)>
                variantDocuments = ResolveVariantDocuments(
                    item,
                    id,
                    isLegacySchema);
            var variants = new Dictionary<
                EyeAccessoryViewState,
                EyeAccessoryVariantDefinition>();
            foreach ((EyeAccessoryViewState state, ImageDocument image) in
                     variantDocuments)
            {
                EyeAccessoryVariantDefinition variant =
                    await ValidateVariantAsync(
                            root,
                            id,
                            state,
                            image,
                            imagePaths,
                            validateImage)
                        .ConfigureAwait(false);
                variants.Add(state, variant);
            }

            EyeAccessoryVariantDefinition front =
                variants[EyeAccessoryViewState.Front];
            var readOnlyVariants = new ReadOnlyDictionary<
                EyeAccessoryViewState,
                EyeAccessoryVariantDefinition>(variants);
            definitions[index] = new EyeAccessoryDefinition(
                id,
                displayName,
                item.SelectionOrder,
                front.ImageFile,
                front.ImagePath,
                front.Width,
                front.Height,
                front.LeftEye,
                front.RightEye,
                front.HeadWidthPixels,
                front.Sha256,
                front.SizeBytes,
                readOnlyVariants);
        }

        EyeAccessoryDefinition[] ordered = definitions
            .OrderBy(static accessory => accessory.SelectionOrder)
            .ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            int expectedOrder = checked(index + 1);
            if (ordered[index].SelectionOrder != expectedOrder)
            {
                throw new InvalidDataException(
                    $"Eye accessory selectionOrder must contain every value " +
                    $"from 1 through {RequiredAccessoryCount}; order " +
                    $"{expectedOrder} is missing or duplicated.");
            }
        }

        return new EyeAccessoryCatalog(
            root,
            manifestPath,
            contentVersion,
            ordered);
    }

    private static IReadOnlyList<(
        EyeAccessoryViewState State,
        ImageDocument Image)> ResolveVariantDocuments(
        AccessoryDocument item,
        string id,
        bool isLegacySchema)
    {
        if (isLegacySchema)
        {
            if (item.Variants is not null)
            {
                throw new InvalidDataException(
                    $"Legacy eye accessory '{id}' cannot declare variants.");
            }

            ImageDocument image = item.Image
                ?? throw new InvalidDataException(
                    $"Legacy eye accessory '{id}' image is required.");
            return [(EyeAccessoryViewState.Front, image)];
        }

        bool hasImage = item.Image is not null;
        bool hasVariants = item.Variants is not null;
        if (hasImage == hasVariants)
        {
            throw new InvalidDataException(
                $"Eye accessory '{id}' must declare exactly one of image or " +
                "variants.");
        }

        if (item.Image is { } frontOnly)
        {
            return [(EyeAccessoryViewState.Front, frontOnly)];
        }

        VariantsDocument variants = item.Variants!;
        if (variants.Front is null)
        {
            throw new InvalidDataException(
                $"Eye accessory '{id}' variants.front is required.");
        }

        var resolved = new List<(
            EyeAccessoryViewState State,
            ImageDocument Image)>
        {
            (EyeAccessoryViewState.Front, variants.Front),
        };
        AddOptionalVariant(
            resolved,
            EyeAccessoryViewState.ThreeQuarterLeft,
            variants.ThreeQuarterLeft);
        AddOptionalVariant(
            resolved,
            EyeAccessoryViewState.ThreeQuarterRight,
            variants.ThreeQuarterRight);
        AddOptionalVariant(
            resolved,
            EyeAccessoryViewState.SideLeft,
            variants.SideLeft);
        AddOptionalVariant(
            resolved,
            EyeAccessoryViewState.SideRight,
            variants.SideRight);
        return resolved;
    }

    private static void AddOptionalVariant(
        ICollection<(EyeAccessoryViewState State, ImageDocument Image)> target,
        EyeAccessoryViewState state,
        ImageDocument? image)
    {
        if (image is not null)
        {
            target.Add((state, image));
        }
    }

    private static async Task<EyeAccessoryVariantDefinition>
        ValidateVariantAsync(
            string root,
            string id,
            EyeAccessoryViewState state,
            ImageDocument image,
            ISet<string> imagePaths,
            Func<ImageValidation, Task> validateImage)
    {
        string variantName = FormatViewState(state);
        string owner = $"Eye accessory '{id}' variant '{variantName}'";
        ValidateDimensions(image.Width, image.Height, owner);
        if (image.SizeBytes <= 0
            || image.SizeBytes > EyeAccessoryContentReader.MaximumPngBytes)
        {
            throw new InvalidDataException(
                $"{owner} sizeBytes must be from 1 through " +
                $"{EyeAccessoryContentReader.MaximumPngBytes}.");
        }

        string imagePath = EyeAccessoryContentReader.ResolveRelativeFile(
            root,
            image.File,
            ".png",
            $"{owner} file");
        if (!imagePaths.Add(imagePath))
        {
            throw new InvalidDataException(
                $"Eye accessory image file '{image.File}' is referenced " +
                "more than once.");
        }

        PointDocument leftEyeDocument = image.LeftEye
            ?? throw new InvalidDataException(
                $"{owner} leftEye is required.");
        PointDocument rightEyeDocument = image.RightEye
            ?? throw new InvalidDataException(
                $"{owner} rightEye is required.");
        EyeAccessoryPoint leftEye = EyeAccessoryContentReader.ValidatePoint(
            leftEyeDocument.X,
            leftEyeDocument.Y,
            image.Width,
            image.Height,
            $"{owner} leftEye");
        EyeAccessoryPoint rightEye = EyeAccessoryContentReader.ValidatePoint(
            rightEyeDocument.X,
            rightEyeDocument.Y,
            image.Width,
            image.Height,
            $"{owner} rightEye");
        if (leftEye == rightEye)
        {
            throw new InvalidDataException(
                $"{owner} eye registration points must be distinct.");
        }

        if (!double.IsFinite(image.HeadWidthPixels)
            || image.HeadWidthPixels <= 0
            || image.HeadWidthPixels > image.Width)
        {
            throw new InvalidDataException(
                $"{owner} headWidthPixels must be a finite value in " +
                $"(0, {image.Width}].");
        }

        string sha256 = EyeAccessoryContentReader.ValidateSha256(
            image.Sha256,
            $"{owner} sha256");
        await validateImage(
                new ImageValidation(
                    imagePath,
                    image.Width,
                    image.Height,
                    sha256,
                    image.SizeBytes,
                    owner))
            .ConfigureAwait(false);
        return new EyeAccessoryVariantDefinition(
            state,
            image.File!,
            imagePath,
            image.Width,
            image.Height,
            leftEye,
            rightEye,
            image.HeadWidthPixels,
            sha256,
            image.SizeBytes);
    }

    private static string FormatViewState(EyeAccessoryViewState state) =>
        state switch
        {
            EyeAccessoryViewState.Front => "front",
            EyeAccessoryViewState.ThreeQuarterLeft => "threeQuarterLeft",
            EyeAccessoryViewState.ThreeQuarterRight => "threeQuarterRight",
            EyeAccessoryViewState.SideLeft => "sideLeft",
            EyeAccessoryViewState.SideRight => "sideRight",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static void ValidateDimensions(int width, int height, string owner)
    {
        if (width < 1
            || width > EyeAccessoryContentReader.MaximumImageDimension
            || height < 1
            || height > EyeAccessoryContentReader.MaximumImageDimension)
        {
            throw new InvalidDataException(
                $"{owner} dimensions must each be from 1 " +
                $"through {EyeAccessoryContentReader.MaximumImageDimension}.");
        }
    }

    private sealed class ManifestDocument
    {
        [JsonRequired]
        public string? Schema { get; init; }

        [JsonRequired]
        public string? ContentVersion { get; init; }

        [JsonRequired]
        public int AccessoryCount { get; init; }

        [JsonRequired]
        public IReadOnlyList<AccessoryDocument?>? Accessories { get; init; }
    }

    private sealed class AccessoryDocument
    {
        [JsonRequired]
        public string? Id { get; init; }

        [JsonRequired]
        public string? DisplayName { get; init; }

        [JsonRequired]
        public int SelectionOrder { get; init; }

        public ImageDocument? Image { get; init; }

        public VariantsDocument? Variants { get; init; }
    }

    private sealed class VariantsDocument
    {
        public ImageDocument? Front { get; init; }

        public ImageDocument? ThreeQuarterLeft { get; init; }

        public ImageDocument? ThreeQuarterRight { get; init; }

        public ImageDocument? SideLeft { get; init; }

        public ImageDocument? SideRight { get; init; }
    }

    private sealed class ImageDocument
    {
        [JsonRequired]
        public string? File { get; init; }

        [JsonRequired]
        public int Width { get; init; }

        [JsonRequired]
        public int Height { get; init; }

        [JsonRequired]
        public PointDocument? LeftEye { get; init; }

        [JsonRequired]
        public PointDocument? RightEye { get; init; }

        [JsonRequired]
        public double HeadWidthPixels { get; init; }

        [JsonRequired]
        public string? Sha256 { get; init; }

        [JsonRequired]
        public long SizeBytes { get; init; }
    }

    private sealed class PointDocument
    {
        [JsonRequired]
        public double X { get; init; }

        [JsonRequired]
        public double Y { get; init; }
    }

    private sealed record ImageValidation(
        string Path,
        int Width,
        int Height,
        string Sha256,
        long SizeBytes,
        string Owner);
}
