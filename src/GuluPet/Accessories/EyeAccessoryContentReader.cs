using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GuluPet.Accessories;

internal static class EyeAccessoryContentReader
{
    internal const int MaximumTextLength = 120;
    internal const long MaximumJsonBytes = 16L * 1024 * 1024;
    internal const long MaximumPngBytes = 64L * 1024 * 1024;
    internal const int MaximumImageDimension = 4096;

    internal static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 64,
    };

    internal static string ResolveAccessoriesRoot(string accessoriesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessoriesRoot);
        string root = Path.GetFullPath(accessoriesRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Eye accessory root not found: '{root}'.");
        }

        return root;
    }

    internal static string ResolveFromAssetsRoot(string assetsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsRoot);
        return Path.Combine(Path.GetFullPath(assetsRoot), "Accessories");
    }

    internal static T ReadJson<T>(string path, string description)
    {
        string fullPath = RequireJsonFile(path, description);
        try
        {
            using var stream = OpenBoundedRead(
                fullPath,
                MaximumJsonBytes,
                description);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                ?? throw new InvalidDataException(
                    $"The {description} document is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The {description} '{fullPath}' is not valid strict JSON.",
                exception);
        }
    }

    internal static async Task<T> ReadJsonAsync<T>(
        string path,
        string description,
        CancellationToken cancellationToken)
    {
        string fullPath = RequireJsonFile(path, description);
        try
        {
            await using FileStream stream = OpenBoundedRead(
                fullPath,
                MaximumJsonBytes,
                description,
                useAsync: true);
            T? document = await JsonSerializer.DeserializeAsync<T>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return document
                ?? throw new InvalidDataException(
                    $"The {description} document is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The {description} '{fullPath}' is not valid strict JSON.",
                exception);
        }
    }

    internal static T ReadHashedJson<T>(
        string path,
        string expectedSha256,
        string description)
    {
        string fullPath = RequireJsonFile(path, description);
        try
        {
            using FileStream stream = OpenBoundedRead(
                fullPath,
                MaximumJsonBytes,
                description);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream));
            EnsureHashMatches(expectedSha256, actualHash, description, fullPath);
            stream.Position = 0;
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                ?? throw new InvalidDataException(
                    $"The {description} document is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The {description} '{fullPath}' is not valid strict JSON.",
                exception);
        }
    }

    internal static async Task<T> ReadHashedJsonAsync<T>(
        string path,
        string expectedSha256,
        string description,
        CancellationToken cancellationToken)
    {
        string fullPath = RequireJsonFile(path, description);
        try
        {
            await using FileStream stream = OpenBoundedRead(
                fullPath,
                MaximumJsonBytes,
                description,
                useAsync: true);
            byte[] hash = await SHA256.HashDataAsync(
                    stream,
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureHashMatches(
                expectedSha256,
                Convert.ToHexString(hash),
                description,
                fullPath);
            stream.Position = 0;
            T? document = await JsonSerializer.DeserializeAsync<T>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return document
                ?? throw new InvalidDataException(
                    $"The {description} document is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The {description} '{fullPath}' is not valid strict JSON.",
                exception);
        }
    }

    internal static string ResolveRelativeFile(
        string root,
        string? relativePath,
        string requiredExtension,
        string owner)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Length > 240
            || !string.Equals(
                relativePath,
                relativePath.Trim(),
                StringComparison.Ordinal)
            || relativePath.Contains('\\', StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath)
            || Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidDataException(
                $"{owner} must use a safe forward-slash relative path.");
        }

        string[] segments = relativePath.Split('/');
        if (segments.Length == 0
            || segments.Any(static segment =>
                string.IsNullOrEmpty(segment)
                || segment is "." or ".."
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException(
                $"{owner} contains an unsafe path segment.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or NotSupportedException
                  or PathTooLongException)
        {
            throw new InvalidDataException(
                $"{owner} is not a valid relative path.",
                exception);
        }

        string relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{owner} leaves the eye accessory root.");
        }

        if (!Path.GetExtension(fullPath).Equals(
                requiredExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{owner} must reference a '{requiredExtension}' file.");
        }

        if (!File.Exists(fullPath))
        {
            throw new InvalidDataException(
                $"{owner} references missing file '{fullPath}'.");
        }

        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.Directory) != 0
            || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"{owner} must reference a regular file, not a directory or " +
                "reparse point.");
        }

        return fullPath;
    }

    internal static string ValidateSha256(string? value, string owner)
    {
        if (value is null
            || value.Length != 64
            || value.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')
                    and not (>= 'A' and <= 'F')))
        {
            throw new InvalidDataException(
                $"{owner} must be exactly 64 hexadecimal SHA-256 characters.");
        }

        return value.ToUpperInvariant();
    }

    internal static void ValidateId(string? value, string owner)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 96
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !value.All(static character =>
                character is >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '-'
                    or '_'))
        {
            throw new InvalidDataException(
                $"{owner} must be a lowercase ASCII id using only letters, " +
                "digits, '-' or '_'.");
        }
    }

    internal static string ValidateText(string? value, string owner)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumTextLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new InvalidDataException(
                $"{owner} must be non-empty, trimmed text no longer than " +
                $"{MaximumTextLength} characters.");
        }

        return value;
    }

    internal static EyeAccessoryPoint ValidatePoint(
        double x,
        double y,
        double maximumX,
        double maximumY,
        string owner)
    {
        if (!double.IsFinite(x)
            || !double.IsFinite(y)
            || x < 0
            || x > maximumX
            || y < 0
            || y > maximumY)
        {
            throw new InvalidDataException(
                $"{owner} must be finite and inside 0..{maximumX:0.###} by " +
                $"0..{maximumY:0.###}.");
        }

        return new EyeAccessoryPoint(x, y);
    }

    internal static void ValidatePng(
        string path,
        int expectedWidth,
        int expectedHeight,
        string expectedSha256,
        long expectedSizeBytes,
        string owner)
    {
        using FileStream stream = OpenBoundedRead(
            path,
            MaximumPngBytes,
            owner);
        ValidateExpectedSize(stream, expectedSizeBytes, owner, path);
        string actualHash = Convert.ToHexString(SHA256.HashData(stream));
        EnsureHashMatches(expectedSha256, actualHash, owner, path);
        stream.Position = 0;
        ValidateDecodedPng(
            stream,
            expectedWidth,
            expectedHeight,
            owner,
            path);
    }

    internal static async Task ValidatePngAsync(
        string path,
        int expectedWidth,
        int expectedHeight,
        string expectedSha256,
        long expectedSizeBytes,
        string owner,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenBoundedRead(
            path,
            MaximumPngBytes,
            owner,
            useAsync: true);
        ValidateExpectedSize(stream, expectedSizeBytes, owner, path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        EnsureHashMatches(
            expectedSha256,
            Convert.ToHexString(hash),
            owner,
            path);
        stream.Position = 0;
        ValidateDecodedPng(
            stream,
            expectedWidth,
            expectedHeight,
            owner,
            path);
    }

    private static string RequireJsonFile(string path, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"The {description} is required.",
                fullPath);
        }

        if (!Path.GetExtension(fullPath).Equals(
                ".json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The {description} must be a JSON file.");
        }

        return fullPath;
    }

    private static FileStream OpenBoundedRead(
        string path,
        long maximumBytes,
        string description,
        bool useAsync = false)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync);
        if (stream.Length <= 0 || stream.Length > maximumBytes)
        {
            stream.Dispose();
            throw new InvalidDataException(
                $"The {description} file must contain 1 through " +
                $"{maximumBytes} bytes.");
        }

        return stream;
    }

    private static void ValidateExpectedSize(
        Stream stream,
        long expectedSizeBytes,
        string owner,
        string path)
    {
        if (expectedSizeBytes <= 0
            || expectedSizeBytes > MaximumPngBytes
            || stream.Length != expectedSizeBytes)
        {
            throw new InvalidDataException(
                $"{owner} sizeBytes is '{expectedSizeBytes}', but '{path}' " +
                $"contains '{stream.Length}' bytes.");
        }
    }

    private static void EnsureHashMatches(
        string expectedSha256,
        string actualSha256,
        string owner,
        string path)
    {
        if (!string.Equals(
                expectedSha256,
                actualSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{owner} SHA-256 does not match '{path}'. Expected " +
                $"'{expectedSha256}', actual '{actualSha256}'.");
        }
    }

    private static void ValidateDecodedPng(
        Stream stream,
        int expectedWidth,
        int expectedHeight,
        string owner,
        string path)
    {
        try
        {
            var decoder = new PngBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1)
            {
                throw new InvalidDataException(
                    $"{owner} PNG must contain exactly one frame.");
            }

            BitmapSource source = decoder.Frames[0];
            if (source.PixelWidth != expectedWidth
                || source.PixelHeight != expectedHeight)
            {
                throw new InvalidDataException(
                    $"{owner} declares {expectedWidth}x{expectedHeight}, but " +
                    $"'{path}' decodes as {source.PixelWidth}x" +
                    $"{source.PixelHeight}.");
            }

            var converted = new FormatConvertedBitmap(
                source,
                PixelFormats.Bgra32,
                destinationPalette: null,
                alphaThreshold: 0);
            int stride = checked(converted.PixelWidth * 4);
            byte[] pixels = new byte[
                checked(stride * converted.PixelHeight)];
            converted.CopyPixels(pixels, stride, 0);
            bool hasTransparentPixel = false;
            bool hasVisiblePixel = false;
            for (var offset = 3; offset < pixels.Length; offset += 4)
            {
                byte alpha = pixels[offset];
                hasTransparentPixel |= alpha < byte.MaxValue;
                hasVisiblePixel |= alpha > 0;
                if (hasTransparentPixel && hasVisiblePixel)
                {
                    break;
                }
            }

            if (!hasTransparentPixel || !hasVisiblePixel)
            {
                throw new InvalidDataException(
                    $"{owner} PNG must contain both transparent and visible " +
                    "pixels.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException
                  or NotSupportedException
                  or ArgumentException
                  or InvalidOperationException
                  or OverflowException
                  or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidDataException(
                $"{owner} PNG '{path}' cannot be decoded safely.",
                exception);
        }
    }
}
