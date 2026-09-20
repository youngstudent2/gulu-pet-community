using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuluPet.Update;

public static class UpdateEndpoints
{
    public const string StableManifestUrl =
        "https://updates.example.invalid/stable/manifest.json";

    public const string UpdateHost = "updates.example.invalid";

    public static Uri StableManifestUri { get; } = new(StableManifestUrl);

    public static string DefaultPublicKeyPath => Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Update",
        "update-public-key.pem");

    internal static bool IsAllowedUpdateUri(Uri? uri) =>
        uri is
        {
            IsAbsoluteUri: true,
            IsDefaultPort: true,
        }
        && string.Equals(
            uri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.Ordinal)
        && string.Equals(
            uri.IdnHost,
            UpdateHost,
            StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment);

    internal static bool IsStableManifestUri(Uri? uri) =>
        uri is not null
        && Uri.Compare(
            StableManifestUri,
            uri,
            UriComponents.AbsoluteUri,
            UriFormat.UriEscaped,
            StringComparison.Ordinal) == 0;

    internal static bool IsExpectedPackageUri(
        Uri? uri,
        string version) =>
        IsAllowedUpdateUri(uri)
        && uri is not null
        && string.IsNullOrEmpty(uri.Query)
        && string.Equals(
            uri.AbsolutePath,
            $"/releases/{version}/GuluPet-{version}-win-x64.zip",
            StringComparison.Ordinal);
}

public enum UpdateCheckStatus
{
    NotConfigured,
    UpToDate,
    UpdateAvailable,
    Failed,
}

public sealed record UpdatePackageDescriptor(
    string Version,
    DateTimeOffset PublishedAtUtc,
    Uri PackageUri,
    long PackageSize,
    string PackageSha256,
    string PackageSignature)
{
    public byte[] GetCanonicalSignaturePayload()
    {
        string publishedAt = PublishedAtUtc.UtcDateTime.ToString(
            "O",
            CultureInfo.InvariantCulture);
        string canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"GuluPet.Update.v1\n{Version}\n{publishedAt}\n" +
            $"{PackageUri.AbsoluteUri}\n{PackageSize}\n{PackageSha256}\n");
        return Encoding.UTF8.GetBytes(canonical);
    }
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? AvailableVersion = null,
    Uri? DownloadUri = null,
    string? ReleaseNotes = null,
    string? ErrorMessage = null,
    UpdatePackageDescriptor? Package = null);

public enum UpdatePreparationPhase
{
    Downloading,
    Verifying,
    Extracting,
    Finalizing,
    Ready,
}

public sealed record UpdatePreparationProgress(
    UpdatePreparationPhase Phase,
    long BytesProcessed,
    long TotalBytes,
    double Percentage);

public sealed record StagedUpdateResult(
    string Version,
    string StagingDirectory,
    long PackageSize,
    string PackageSha256);

public sealed class ManualUpdateService : IDisposable
{
    public const int ManifestSizeLimitBytes = 64 * 1024;

    public const long PackageSizeLimitBytes = 512L * 1024 * 1024;

    public const long ExtractedSizeLimitBytes = 1024L * 1024 * 1024;

    public const int ArchiveEntryLimit = 20_000;

    public static readonly TimeSpan DefaultCheckTimeout =
        TimeSpan.FromSeconds(15);

    public static readonly TimeSpan DefaultStageTimeout =
        TimeSpan.FromHours(2);

    internal static readonly TimeSpan StaleOperationWorkspaceAge =
        TimeSpan.FromHours(24);

    internal static readonly TimeSpan UpdateInstallLogRetention =
        TimeSpan.FromHours(72);

    internal const string OperationWorkspaceDirectoryName =
        ".gulupet-update-operation.work";

    internal const string OperationWorkspaceMarkerFileName =
        ".gulupet-operation-owner";

    internal const string OperationWorkspaceMarkerText =
        "GuluPet.Update.OperationWorkspace.v1\n";

    internal const string OperationPackageFileName =
        "package.zip.download";

    internal const string OperationStagingDirectoryName =
        "staging.work";

    private const int ManifestSchemaVersion = 1;
    private const int ReleaseNotesLengthLimit = 16 * 1024;
    private const int CopyBufferSize = 128 * 1024;
    private const int OperationWorkspaceEntryLimit = 1_000_000;

    private static readonly HashSet<string> RequiredManifestProperties =
        new(StringComparer.Ordinal)
        {
            "schemaVersion",
            "version",
            "publishedAtUtc",
            "packageUrl",
            "packageSize",
            "packageSha256",
            "packageSignature",
            "releaseNotes",
        };

    private readonly Uri? _manifestUri;
    private readonly Version _currentVersion;
    private readonly HttpClient _httpClient;
    private readonly string? _publicKeyPem;
    private readonly TimeSpan _checkTimeout;
    private readonly TimeSpan _stageTimeout;
    private bool _disposed;

    /// <summary>
    /// Creates a user-triggered update client. A null manifest URI explicitly
    /// disables update checks. Production callers should pass
    /// <see cref="UpdateEndpoints.StableManifestUri"/>.
    /// Construction performs no network access.
    /// </summary>
    public ManualUpdateService(
        Uri? manifestUri,
        Version? currentVersion = null,
        HttpMessageHandler? messageHandler = null,
        string? publicKeyPem = null,
        TimeSpan? checkTimeout = null,
        TimeSpan? stageTimeout = null)
    {
        _manifestUri = manifestUri;
        _currentVersion = NormalizeCurrentVersion(
            currentVersion
                ?? Assembly.GetEntryAssembly()?.GetName().Version
                ?? new Version(0, 1, 0));
        _publicKeyPem = string.IsNullOrWhiteSpace(publicKeyPem)
            ? null
            : publicKeyPem;
        _checkTimeout = ValidateCheckTimeout(checkTimeout);
        _stageTimeout = ValidateStageTimeout(stageTimeout);

        _httpClient = new HttpClient(
            messageHandler ?? CreateProductionHandler(),
            disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "GuluPetCommunity",
                DisplayVersion(_currentVersion)));
    }

    public async Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string currentVersion = DisplayVersion(_currentVersion);

        if (_manifestUri is null)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.NotConfigured,
                currentVersion);
        }

        if (!UpdateEndpoints.IsStableManifestUri(_manifestUri))
        {
            return Failed(
                currentVersion,
                "更新地址必须使用受信任的 HTTPS 主机。");
        }

        using var timeoutSource = new CancellationTokenSource(_checkTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                _manifestUri);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
            };

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linkedSource.Token).ConfigureAwait(false);
            EnsureNoRedirect(response, _manifestUri);
            if (!response.IsSuccessStatusCode)
            {
                return Failed(
                    currentVersion,
                    $"更新服务器返回了 {(int)response.StatusCode}。" );
            }

            if (response.Content.Headers.ContentLength is long contentLength
                && contentLength > ManifestSizeLimitBytes)
            {
                return Failed(currentVersion, "更新描述超过大小限制。");
            }

            await using Stream content = await response.Content.ReadAsStreamAsync(
                linkedSource.Token).ConfigureAwait(false);
            byte[] json = await ReadWithLimitAsync(
                content,
                ManifestSizeLimitBytes,
                linkedSource.Token).ConfigureAwait(false);
            ParsedManifest manifest = ParseManifest(json, _manifestUri);

            if (!VerifyPackageSignature(manifest.Package))
            {
                return Failed(currentVersion, "更新描述签名验证失败。");
            }

            if (manifest.AvailableVersion <= _currentVersion)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    currentVersion,
                    manifest.Package.Version);
            }

            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                currentVersion,
                manifest.Package.Version,
                manifest.Package.PackageUri,
                manifest.ReleaseNotes,
                Package: manifest.Package);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failed(currentVersion, "检查更新超时。");
        }
        catch (Exception exception)
        {
            return Failed(currentVersion, exception.Message);
        }
    }

    /// <summary>
    /// Downloads a previously validated package, verifies its exact length and
    /// SHA-256, and extracts it into a new staging directory next to the current
    /// installation directory. Existing staging content is never overwritten.
    /// </summary>
    public async Task<StagedUpdateResult> StageUpdateAsync(
        UpdateCheckResult result,
        string currentInstallationDirectory,
        string stagingDirectory,
        IProgress<UpdatePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentInstallationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);

        UpdatePackageDescriptor package = ValidatePackageForStaging(result);
        StagingPaths paths = ValidateStagingPaths(
            currentInstallationDirectory,
            stagingDirectory);
        string operationWorkspacePath = Path.Combine(
            paths.ParentDirectory,
            OperationWorkspaceDirectoryName);
        string temporaryPackagePath = Path.Combine(
            operationWorkspacePath,
            OperationPackageFileName);
        string workingStagingPath = Path.Combine(
            operationWorkspacePath,
            OperationStagingDirectoryName);
        FileStream? operationWorkspaceLease = null;
        bool operationWorkspaceOwned = false;
        using var timeoutSource = new CancellationTokenSource(_stageTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            operationWorkspaceLease = CreateOperationWorkspace(
                paths.ParentDirectory,
                operationWorkspacePath);
            operationWorkspaceOwned = true;

            await DownloadPackageAsync(
                package,
                temporaryPackagePath,
                progress,
                linkedSource.Token).ConfigureAwait(false);

            Directory.CreateDirectory(workingStagingPath);
            EnsureDirectoryIsNotReparsePoint(workingStagingPath);
            await Task.Run(
                () => ExtractPackage(
                    temporaryPackagePath,
                    workingStagingPath,
                    progress,
                    linkedSource.Token),
                linkedSource.Token).ConfigureAwait(false);
            progress?.Report(new UpdatePreparationProgress(
                UpdatePreparationPhase.Finalizing,
                1,
                1,
                98));
            Directory.Move(
                workingStagingPath,
                paths.StagingDirectory);
            EnsureDirectoryIsNotReparsePoint(paths.StagingDirectory);
            progress?.Report(new UpdatePreparationProgress(
                UpdatePreparationPhase.Ready,
                1,
                1,
                100));

            return new StagedUpdateResult(
                package.Version,
                paths.StagingDirectory,
                package.PackageSize,
                package.PackageSha256);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
            when (timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Downloading or staging the update exceeded the allowed time.",
                exception);
        }
        finally
        {
            operationWorkspaceLease?.Dispose();
            if (operationWorkspaceOwned)
            {
                TryDeleteOwnedOperationWorkspace(
                    paths.ParentDirectory,
                    operationWorkspacePath);
            }
        }
    }

    /// <summary>
    /// Removes the one reserved operation workspace only after it is stale,
    /// carries the updater's exact ownership marker, is not leased by another
    /// process, and contains no reparse points. Callers must hold the normal
    /// application single-instance lease before invoking this method.
    /// </summary>
    internal static int CleanupStaleOperationWorkspaces(
        string currentInstallationDirectory,
        DateTimeOffset? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            currentInstallationDirectory);
        string installationPath = NormalizeDirectoryPath(
            currentInstallationDirectory);
        if (!Directory.Exists(installationPath))
        {
            throw new DirectoryNotFoundException(
                $"Installation directory does not exist: {installationPath}");
        }

        string parentDirectory = Path.GetDirectoryName(installationPath)
            ?? throw new InvalidOperationException(
                "The installation directory has no parent.");
        EnsureDirectoryIsNotReparsePoint(parentDirectory);
        EnsureDirectoryIsNotReparsePoint(installationPath);

        string workspacePath = Path.Combine(
            parentDirectory,
            OperationWorkspaceDirectoryName);
        if (!IsDirectChild(parentDirectory, workspacePath)
            || !Directory.Exists(workspacePath)
            || HasReparsePoint(workspacePath))
        {
            return 0;
        }

        DateTime staleBeforeUtc = (utcNow ?? DateTimeOffset.UtcNow)
            .ToUniversalTime()
            .UtcDateTime
            .Subtract(StaleOperationWorkspaceAge);
        DateTime lastWriteUtc;
        try
        {
            lastWriteUtc = Directory.GetLastWriteTimeUtc(workspacePath);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        if (lastWriteUtc > staleBeforeUtc)
        {
            return 0;
        }

        return TryDeleteOwnedOperationWorkspace(
            parentDirectory,
            workspacePath)
                ? 1
                : 0;
    }

    /// <summary>
    /// Removes only expired per-operation installer logs from the supplied
    /// durable update-state root. Journals, results, scripts, directories, and
    /// any recovery state remain untouched.
    /// </summary>
    internal static int CleanupExpiredUpdateInstallLogs(
        string updatesRootDirectory,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updatesRootDirectory);
        string updatesRoot = NormalizeDirectoryPath(updatesRootDirectory);
        try
        {
            if (!Directory.Exists(updatesRoot)
                || HasReparsePointInPath(updatesRoot))
            {
                return 0;
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        string[] operationDirectories;
        try
        {
            operationDirectories = Directory.GetDirectories(
                updatesRoot,
                "*",
                SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        DateTime cutoffUtc = ((timeProvider ?? TimeProvider.System).GetUtcNow()
            - UpdateInstallLogRetention).UtcDateTime;
        var deletedCount = 0;
        foreach (string operationDirectory in operationDirectories)
        {
            try
            {
                string fullOperationDirectory = NormalizeDirectoryPath(
                    operationDirectory);
                string operationIdText = Path.GetFileName(
                    fullOperationDirectory);
                if (HasReparsePointInPath(fullOperationDirectory)
                    || !IsDirectChild(updatesRoot, fullOperationDirectory)
                    || !Guid.TryParseExact(
                        operationIdText,
                        "N",
                        out Guid operationId))
                {
                    continue;
                }

                string expectedLogPath = Path.Combine(
                    fullOperationDirectory,
                    $"update-install-{operationId:N}.log");
                if (!File.Exists(expectedLogPath)
                    || HasReparsePointInPath(expectedLogPath)
                    || File.GetLastWriteTimeUtc(expectedLogPath) >= cutoffUtc)
                {
                    continue;
                }

                File.Delete(expectedLogPath);
                deletedCount++;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // One inaccessible operation must not block other safe cleanup.
            }
        }

        return deletedCount;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    private static HttpMessageHandler CreateProductionHandler() =>
        new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
        };

    private static TimeSpan ValidateCheckTimeout(TimeSpan? timeout)
    {
        TimeSpan value = timeout ?? DefaultCheckTimeout;
        if (value <= TimeSpan.Zero || value > DefaultCheckTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                $"Update checks must time out within {DefaultCheckTimeout}.");
        }

        return value;
    }

    private static TimeSpan ValidateStageTimeout(TimeSpan? timeout)
    {
        TimeSpan value = timeout ?? DefaultStageTimeout;
        if (value <= TimeSpan.Zero || value > DefaultStageTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                $"Update staging must time out within {DefaultStageTimeout}.");
        }

        return value;
    }

    private static void EnsureNoRedirect(
        HttpResponseMessage response,
        Uri requestedUri)
    {
        int statusCode = (int)response.StatusCode;
        if (statusCode is >= 300 and <= 399)
        {
            throw new InvalidDataException("更新服务器不允许重定向。");
        }

        Uri? responseUri = response.RequestMessage?.RequestUri;
        if (responseUri is not null
            && Uri.Compare(
                requestedUri,
                responseUri,
                UriComponents.AbsoluteUri,
                UriFormat.UriEscaped,
                StringComparison.Ordinal) != 0)
        {
            throw new InvalidDataException("更新请求发生了重定向。");
        }
    }

    private static async Task<byte[]> ReadWithLimitAsync(
        Stream source,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var destination = new MemoryStream(
            Math.Min(maximumBytes, 32 * 1024));
        byte[] buffer = new byte[32 * 1024];

        while (true)
        {
            int read = await source.ReadAsync(
                buffer,
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    "更新描述超过大小限制。");
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static ParsedManifest ParseManifest(
        ReadOnlyMemory<byte> json,
        Uri manifestUri)
    {
        using JsonDocument document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("更新描述必须是 JSON 对象。");
        }

        var properties = new Dictionary<string, JsonElement>(
            StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!RequiredManifestProperties.Contains(property.Name)
                || !properties.TryAdd(property.Name, property.Value))
            {
                throw new InvalidDataException(
                    $"更新描述包含未知或重复字段：{property.Name}。");
            }
        }

        if (properties.Count != RequiredManifestProperties.Count)
        {
            throw new InvalidDataException("更新描述缺少必需字段。");
        }

        if (!properties["schemaVersion"].TryGetInt32(out int schemaVersion)
            || schemaVersion != ManifestSchemaVersion)
        {
            throw new InvalidDataException("更新描述版本不受支持。");
        }

        string versionText = RequiredString(properties, "version", 64);
        if (!TryParseNormalizedVersion(
                versionText,
                out Version availableVersion))
        {
            throw new InvalidDataException("更新描述中的版本号无效。");
        }

        string publishedAtText = RequiredString(
            properties,
            "publishedAtUtc",
            64);
        if (!DateTimeOffset.TryParseExact(
                publishedAtText,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset publishedAt)
            || publishedAt.Offset != TimeSpan.Zero
            || !string.Equals(
                publishedAtText,
                publishedAt.UtcDateTime.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("更新时间必须是 UTC round-trip 时间。");
        }

        string packageUrl = RequiredString(properties, "packageUrl", 2048);
        if (!Uri.TryCreate(packageUrl, UriKind.Absolute, out Uri? packageUri)
            || !string.Equals(
                packageUrl,
                packageUri.AbsoluteUri,
                StringComparison.Ordinal)
            || !UpdateEndpoints.IsExpectedPackageUri(
                packageUri,
                versionText)
            || !string.Equals(
                packageUri.IdnHost,
                manifestUri.IdnHost,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "更新包地址必须使用相同的受信任 HTTPS 主机。");
        }

        if (!properties["packageSize"].TryGetInt64(out long packageSize)
            || packageSize <= 0
            || packageSize > PackageSizeLimitBytes)
        {
            throw new InvalidDataException("更新包大小无效或超过限制。");
        }

        string packageSha256 = RequiredString(
            properties,
            "packageSha256",
            64);
        if (packageSha256.Length != 64
            || packageSha256.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new InvalidDataException(
                "更新包 SHA-256 必须是小写十六进制。");
        }

        string packageSignature = RequiredString(
            properties,
            "packageSignature",
            128);
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(packageSignature);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("更新签名不是有效的 Base64。", exception);
        }

        if (signature.Length != 64
            || !string.Equals(
                Convert.ToBase64String(signature),
                packageSignature,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("更新签名格式无效。");
        }

        string releaseNotes = RequiredString(
            properties,
            "releaseNotes",
            ReleaseNotesLengthLimit,
            allowEmpty: true);
        var package = new UpdatePackageDescriptor(
            versionText,
            publishedAt.ToUniversalTime(),
            packageUri,
            packageSize,
            packageSha256,
            packageSignature);
        return new ParsedManifest(
            availableVersion,
            package,
            releaseNotes);
    }

    private static string RequiredString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        int maximumLength,
        bool allowEmpty = false)
    {
        JsonElement value = properties[name];
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"更新描述字段 {name} 必须是字符串。");
        }

        string? text = value.GetString();
        if (text is null
            || (!allowEmpty && text.Length == 0)
            || text.Length > maximumLength)
        {
            throw new InvalidDataException($"更新描述字段 {name} 长度无效。");
        }

        return text;
    }

    private bool VerifyPackageSignature(UpdatePackageDescriptor package)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(package.PackageSignature);
        }
        catch (FormatException)
        {
            return false;
        }

        using ECDsa verifier = LoadVerifier();
        return verifier.VerifyData(
            package.GetCanonicalSignaturePayload(),
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private ECDsa LoadVerifier()
    {
        string pem = _publicKeyPem
            ?? File.ReadAllText(UpdateEndpoints.DefaultPublicKeyPath, Encoding.UTF8);
        if (!pem.Contains(
                "-----BEGIN PUBLIC KEY-----",
                StringComparison.Ordinal)
            || pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "更新公钥必须是 PEM SubjectPublicKeyInfo 公钥。");
        }

        var verifier = ECDsa.Create();
        try
        {
            verifier.ImportFromPem(pem);
            ECParameters parameters = verifier.ExportParameters(
                includePrivateParameters: false);
            if (verifier.KeySize != 256
                || !string.Equals(
                    parameters.Curve.Oid.Value,
                    ECCurve.NamedCurves.nistP256.Oid.Value,
                    StringComparison.Ordinal))
            {
                throw new CryptographicException(
                    "更新公钥必须使用 ECDSA P-256。");
            }

            return verifier;
        }
        catch
        {
            verifier.Dispose();
            throw;
        }
    }

    private UpdatePackageDescriptor ValidatePackageForStaging(
        UpdateCheckResult result)
    {
        if (result.Status != UpdateCheckStatus.UpdateAvailable
            || result.Package is null
            || result.DownloadUri is null
            || !Uri.Equals(result.DownloadUri, result.Package.PackageUri)
            || !string.Equals(
                result.AvailableVersion,
                result.Package.Version,
                StringComparison.Ordinal)
            || !TryParseNormalizedVersion(
                result.Package.Version,
                out Version availableVersion)
            || availableVersion <= _currentVersion
            || !ValidatePackageDescriptor(result.Package)
            || !VerifyPackageSignature(result.Package))
        {
            throw new InvalidOperationException(
                "There is no validated update package to stage.");
        }

        return result.Package;
    }

    private static bool ValidatePackageDescriptor(
        UpdatePackageDescriptor package) =>
        UpdateEndpoints.IsExpectedPackageUri(
            package.PackageUri,
            package.Version)
        && package.PackageSize is > 0 and <= PackageSizeLimitBytes
        && package.PackageSha256.Length == 64
        && package.PackageSha256.All(character =>
            character is (>= '0' and <= '9') or (>= 'a' and <= 'f'))
        && package.PublishedAtUtc.Offset == TimeSpan.Zero;

    private static StagingPaths ValidateStagingPaths(
        string currentInstallationDirectory,
        string stagingDirectory)
    {
        string installationPath = NormalizeDirectoryPath(
            currentInstallationDirectory);
        string stagingPath = NormalizeDirectoryPath(stagingDirectory);
        if (!Directory.Exists(installationPath))
        {
            throw new DirectoryNotFoundException(
                $"Installation directory does not exist: {installationPath}");
        }

        string? installationParent = Path.GetDirectoryName(installationPath);
        string? stagingParent = Path.GetDirectoryName(stagingPath);
        if (installationParent is null
            || stagingParent is null
            || !string.Equals(
                installationParent,
                stagingParent,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                installationPath,
                stagingPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The staging directory must be a distinct sibling of the " +
                "current installation directory.",
                nameof(stagingDirectory));
        }

        if (Directory.Exists(stagingPath) || File.Exists(stagingPath))
        {
            throw new IOException(
                "The staging path already exists and will not be overwritten.");
        }

        EnsureDirectoryIsNotReparsePoint(installationParent);
        EnsureDirectoryIsNotReparsePoint(installationPath);

        return new StagingPaths(
            installationPath,
            stagingPath,
            installationParent);
    }

    private static string NormalizeDirectoryPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private async Task DownloadPackageAsync(
        UpdatePackageDescriptor package,
        string temporaryPackagePath,
        IProgress<UpdatePreparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            package.PackageUri);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/zip"));

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        EnsureNoRedirect(response, package.PackageUri);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long contentLength
            && (contentLength > PackageSizeLimitBytes
                || contentLength != package.PackageSize))
        {
            throw new InvalidDataException(
                "更新包 Content-Length 与签名描述不一致。");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(
            cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            temporaryPackagePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hasher = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        byte[] buffer = new byte[CopyBufferSize];
        long totalRead = 0;
        double lastReportedPercentage = 0;
        progress?.Report(new UpdatePreparationProgress(
            UpdatePreparationPhase.Downloading,
            0,
            package.PackageSize,
            0));

        while (true)
        {
            int read = await source.ReadAsync(
                buffer,
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > package.PackageSize
                || totalRead > PackageSizeLimitBytes)
            {
                throw new InvalidDataException(
                    "更新包超过签名描述的大小。");
            }

            hasher.AppendData(buffer, 0, read);
            await destination.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken).ConfigureAwait(false);
            double percentage = totalRead * 70d / package.PackageSize;
            if (totalRead == package.PackageSize
                || percentage - lastReportedPercentage >= 0.25)
            {
                lastReportedPercentage = percentage;
                progress?.Report(new UpdatePreparationProgress(
                    UpdatePreparationPhase.Downloading,
                    totalRead,
                    package.PackageSize,
                    percentage));
            }
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (totalRead != package.PackageSize)
        {
            throw new InvalidDataException(
                "更新包大小与签名描述不一致。");
        }

        progress?.Report(new UpdatePreparationProgress(
            UpdatePreparationPhase.Verifying,
            totalRead,
            package.PackageSize,
            72));

        string actualSha256 = Convert.ToHexString(hasher.GetHashAndReset())
            .ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actualSha256),
                Encoding.ASCII.GetBytes(package.PackageSha256)))
        {
            throw new InvalidDataException("更新包 SHA-256 验证失败。");
        }

        progress?.Report(new UpdatePreparationProgress(
            UpdatePreparationPhase.Verifying,
            totalRead,
            package.PackageSize,
            75));
    }

    private static void ExtractPackage(
        string packagePath,
        string stagingDirectory,
        IProgress<UpdatePreparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count == 0
            || archive.Entries.Count > ArchiveEntryLimit)
        {
            throw new InvalidDataException("更新包文件数量无效。");
        }

        string stagingPrefix = Path.EndsInDirectorySeparator(stagingDirectory)
            ? stagingDirectory
            : stagingDirectory + Path.DirectorySeparatorChar;
        var validatedEntries = new List<ValidatedArchiveEntry>(
            archive.Entries.Count);
        var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long extractedBytes = 0;
        bool hasExecutable = false;
        bool hasAssembly = false;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatedArchiveEntry validated = ValidateArchiveEntry(
                entry,
                stagingDirectory,
                stagingPrefix);
            if (!targetPaths.Add(validated.TargetPath))
            {
                throw new InvalidDataException(
                    "更新包包含重复的目标路径。");
            }

            if (!validated.IsDirectory)
            {
                extractedBytes = checked(extractedBytes + entry.Length);
                if (extractedBytes > ExtractedSizeLimitBytes)
                {
                    throw new InvalidDataException(
                        "更新包解压后的大小超过限制。");
                }

                hasExecutable |= string.Equals(
                    validated.RelativePath,
                    "GuluPet.exe",
                    StringComparison.OrdinalIgnoreCase);
                hasAssembly |= string.Equals(
                    validated.RelativePath,
                    "GuluPet.dll",
                    StringComparison.OrdinalIgnoreCase);
            }

            validatedEntries.Add(validated);
        }

        if (!hasExecutable || !hasAssembly)
        {
            throw new InvalidDataException(
                "更新包缺少 GuluPet.exe 或 GuluPet.dll。");
        }

        long copiedBytes = 0;
        double lastReportedPercentage = 75;
        progress?.Report(new UpdatePreparationProgress(
            UpdatePreparationPhase.Extracting,
            0,
            extractedBytes,
            75));

        foreach (ValidatedArchiveEntry validated in validatedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (validated.IsDirectory)
            {
                Directory.CreateDirectory(validated.TargetPath);
                EnsurePathHasNoReparsePoints(
                    stagingDirectory,
                    validated.TargetPath);
                continue;
            }

            string? targetParent = Path.GetDirectoryName(validated.TargetPath);
            if (targetParent is null)
            {
                throw new InvalidDataException("更新包目标路径无效。");
            }

            Directory.CreateDirectory(targetParent);
            EnsurePathHasNoReparsePoints(
                stagingDirectory,
                targetParent);
            using Stream source = validated.Entry.Open();
            using var destination = new FileStream(
                validated.TargetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.SequentialScan);
            CopyArchiveEntry(
                source,
                destination,
                validated.Entry.Length,
                copied =>
                {
                    copiedBytes = checked(copiedBytes + copied);
                    double percentage = extractedBytes == 0
                        ? 98
                        : 75 + copiedBytes * 23d / extractedBytes;
                    if (copiedBytes == extractedBytes
                        || percentage - lastReportedPercentage >= 0.25)
                    {
                        lastReportedPercentage = percentage;
                        progress?.Report(new UpdatePreparationProgress(
                            UpdatePreparationPhase.Extracting,
                            copiedBytes,
                            extractedBytes,
                            percentage));
                    }
                },
                cancellationToken);
        }
    }

    private static ValidatedArchiveEntry ValidateArchiveEntry(
        ZipArchiveEntry entry,
        string stagingDirectory,
        string stagingPrefix)
    {
        string fullName = entry.FullName;
        if (string.IsNullOrEmpty(fullName)
            || fullName.Length > 1024
            || fullName.IndexOf('\0') >= 0
            || fullName.StartsWith('/')
            || fullName.StartsWith('\\')
            || Path.IsPathRooted(fullName))
        {
            throw new InvalidDataException(
                "更新包包含绝对或空路径。");
        }

        string normalized = fullName.Replace('\\', '/');
        string[] segments = normalized.Split('/');
        bool isDirectory = normalized.EndsWith('/');
        int segmentCount = isDirectory
            ? segments.Length - 1
            : segments.Length;
        if (segmentCount == 0
            || segments.Take(segmentCount).Any(segment =>
                segment.Length == 0
                || segment is "." or ".."
                || IsUnsafeWindowsPathSegment(segment)))
        {
            throw new InvalidDataException(
                "更新包包含不安全的相对路径。");
        }

        if (IsSymbolicLinkOrSpecialFile(entry))
        {
            throw new InvalidDataException(
                "更新包不允许符号链接或特殊文件。");
        }

        if (isDirectory && entry.Length != 0)
        {
            throw new InvalidDataException("更新包目录项不能包含数据。");
        }

        string relativePath = string.Join(
            Path.DirectorySeparatorChar,
            segments.Take(segmentCount));
        string targetPath = Path.GetFullPath(
            Path.Combine(stagingDirectory, relativePath));
        if (!targetPath.StartsWith(
                stagingPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新包路径越过了暂存目录。");
        }

        return new ValidatedArchiveEntry(
            entry,
            relativePath,
            targetPath,
            isDirectory);
    }

    private static bool IsUnsafeWindowsPathSegment(string segment)
    {
        if (segment.Length > 255
            || segment.EndsWith(' ')
            || segment.EndsWith('.')
            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return true;
        }

        string deviceName = segment.Split('.', 2)[0];
        return deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || IsNumberedDeviceName(deviceName, "COM")
            || IsNumberedDeviceName(deviceName, "LPT");
    }

    private static bool IsNumberedDeviceName(
        string value,
        string prefix) =>
        value.Length == 4
        && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && value[3] is >= '1' and <= '9';

    private static bool IsSymbolicLinkOrSpecialFile(ZipArchiveEntry entry)
    {
        const int unixFileTypeMask = 0xF000;
        const int unixRegularFile = 0x8000;
        const int unixDirectory = 0x4000;
        int unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        int unixFileType = unixMode & unixFileTypeMask;
        if (unixFileType != 0
            && unixFileType != unixRegularFile
            && unixFileType != unixDirectory)
        {
            return true;
        }

        return (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
    }

    private static void CopyArchiveEntry(
        Stream source,
        Stream destination,
        long expectedLength,
        Action<int> copiedCallback,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[CopyBufferSize];
        long copied = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            copied += read;
            if (copied > expectedLength)
            {
                throw new InvalidDataException(
                    "更新包条目大小与 ZIP 目录不一致。");
            }

            destination.Write(buffer, 0, read);
            copiedCallback(read);
        }

        if (copied != expectedLength)
        {
            throw new InvalidDataException(
                "更新包条目未完整解压。");
        }
    }

    private static FileStream CreateOperationWorkspace(
        string parentDirectory,
        string workspacePath)
    {
        if (!IsDirectChild(parentDirectory, workspacePath)
            || !string.Equals(
                Path.GetFileName(workspacePath),
                OperationWorkspaceDirectoryName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The update operation workspace path is not reserved.");
        }

        if (File.Exists(workspacePath))
        {
            throw new IOException(
                "The reserved update operation workspace is a file.");
        }

        if (Directory.Exists(workspacePath)
            && !TryDeleteOwnedOperationWorkspace(
                parentDirectory,
                workspacePath))
        {
            throw new IOException(
                "The reserved update operation workspace is active or " +
                "is not owned by GuluPet.");
        }

        Directory.CreateDirectory(workspacePath);
        try
        {
            EnsureDirectoryIsNotReparsePoint(workspacePath);
            string markerPath = Path.Combine(
                workspacePath,
                OperationWorkspaceMarkerFileName);
            var lease = new FileStream(
                markerPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            try
            {
                byte[] marker = Encoding.ASCII.GetBytes(
                    OperationWorkspaceMarkerText);
                lease.Write(marker, 0, marker.Length);
                lease.Flush(flushToDisk: true);
                lease.Position = 0;
                return lease;
            }
            catch
            {
                lease.Dispose();
                TryDeleteOwnedWorkspaceMarker(markerPath);
                throw;
            }
        }
        catch
        {
            TryDeleteEmptyDirectory(workspacePath);
            throw;
        }
    }

    private static bool TryDeleteOwnedOperationWorkspace(
        string parentDirectory,
        string workspacePath)
    {
        if (!IsDirectChild(parentDirectory, workspacePath)
            || !string.Equals(
                Path.GetFileName(workspacePath),
                OperationWorkspaceDirectoryName,
                StringComparison.Ordinal)
            || !Directory.Exists(workspacePath)
            || HasReparsePoint(workspacePath))
        {
            return false;
        }

        string markerPath = Path.Combine(
            workspacePath,
            OperationWorkspaceMarkerFileName);
        try
        {
            if (!File.Exists(markerPath))
            {
                // A process can be terminated between creating the reserved
                // directory and creating its marker. Only an exactly named,
                // empty, non-reparse directory is safe to reclaim here.
                return TryDeleteEmptyDirectory(workspacePath);
            }

            if (!IsExactOperationWorkspaceMarker(markerPath))
            {
                // A zero-length or prefix marker is the other crash-only
                // creation state. It is owned only when it is the workspace's
                // sole entry; arbitrary invalid markers and user content are
                // never removed.
                if (!IsIncompleteOperationWorkspaceMarker(markerPath)
                    || !TryCollectOperationWorkspaceTree(
                        workspacePath,
                        out List<string> incompleteFiles,
                        out List<string> incompleteDirectories)
                    || incompleteFiles.Count != 1
                    || incompleteDirectories.Count != 1
                    || !string.Equals(
                        incompleteFiles[0],
                        markerPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                File.Delete(markerPath);
                return TryDeleteEmptyDirectory(workspacePath);
            }

            if (!TryCollectOperationWorkspaceTree(
                    workspacePath,
                    out List<string> files,
                    out List<string> directories))
            {
                return false;
            }

            foreach (string file in files.Where(path =>
                         !string.Equals(
                             path,
                             markerPath,
                             StringComparison.OrdinalIgnoreCase)))
            {
                FileAttributes attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                File.Delete(file);
            }

            foreach (string directory in directories
                         .Where(path => !string.Equals(
                             path,
                             workspacePath,
                             StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(static path => path.Length))
            {
                FileAttributes attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                Directory.Delete(directory, recursive: false);
            }

            if (HasReparsePoint(markerPath)
                || HasReparsePoint(workspacePath))
            {
                return false;
            }

            File.Delete(markerPath);
            Directory.Delete(workspacePath, recursive: false);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsExactOperationWorkspaceMarker(string markerPath)
    {
        if (!File.Exists(markerPath)
            || HasReparsePoint(markerPath))
        {
            return false;
        }

        byte[] expected = Encoding.ASCII.GetBytes(
            OperationWorkspaceMarkerText);
        using var marker = new FileStream(
            markerPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        if (marker.Length != expected.Length)
        {
            return false;
        }

        byte[] actual = new byte[expected.Length];
        marker.ReadExactly(actual);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool IsIncompleteOperationWorkspaceMarker(
        string markerPath)
    {
        if (!File.Exists(markerPath)
            || HasReparsePoint(markerPath))
        {
            return false;
        }

        byte[] expected = Encoding.ASCII.GetBytes(
            OperationWorkspaceMarkerText);
        using var marker = new FileStream(
            markerPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        if (marker.Length >= expected.Length)
        {
            return false;
        }

        byte[] actual = new byte[checked((int)marker.Length)];
        marker.ReadExactly(actual);
        return actual.AsSpan().SequenceEqual(
            expected.AsSpan(0, actual.Length));
    }

    private static bool TryCollectOperationWorkspaceTree(
        string workspacePath,
        out List<string> files,
        out List<string> directories)
    {
        files = [];
        directories = [workspacePath];
        var pending = new Stack<string>();
        pending.Push(workspacePath);
        int entryCount = 0;

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            if (HasReparsePoint(directory))
            {
                return false;
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                entryCount++;
                if (entryCount > OperationWorkspaceEntryLimit)
                {
                    return false;
                }

                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(entry);
                    pending.Push(entry);
                }
                else
                {
                    files.Add(entry);
                }
            }
        }

        return true;
    }

    private static bool TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !HasReparsePoint(path))
            {
                Directory.Delete(path, recursive: false);
                return true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }

    private static void TryDeleteOwnedWorkspaceMarker(string markerPath)
    {
        try
        {
            if (File.Exists(markerPath) && !HasReparsePoint(markerPath))
            {
                File.Delete(markerPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsDirectChild(
        string expectedParent,
        string path) =>
        string.Equals(
            NormalizeDirectoryPath(expectedParent),
            Path.GetDirectoryName(NormalizeDirectoryPath(path)),
            StringComparison.OrdinalIgnoreCase);

    private static void EnsurePathHasNoReparsePoints(
        string rootDirectory,
        string targetDirectory)
    {
        string root = NormalizeDirectoryPath(rootDirectory);
        string current = NormalizeDirectoryPath(targetDirectory);
        if (!string.Equals(root, current, StringComparison.OrdinalIgnoreCase)
            && !current.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Update extraction path escaped the staging directory.");
        }

        while (true)
        {
            EnsureDirectoryIsNotReparsePoint(current);
            if (string.Equals(root, current, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidDataException(
                    "Update extraction path has no staging parent.");
        }
    }

    private static void EnsureDirectoryIsNotReparsePoint(string path)
    {
        if (HasReparsePoint(path))
        {
            throw new InvalidDataException(
                $"Update path must not be a reparse point: {path}");
        }
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static bool HasReparsePointInPath(string path)
    {
        string current = NormalizeDirectoryPath(path);
        while (true)
        {
            if (HasReparsePoint(current))
            {
                return true;
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent)
                || string.Equals(
                    parent,
                    current,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            current = parent;
        }
    }

    private static bool TryParseNormalizedVersion(
        string? value,
        out Version version)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 64
            || value.StartsWith('v')
            || value.StartsWith('V')
            || value.Any(char.IsWhiteSpace))
        {
            version = new Version(0, 0);
            return false;
        }

        const int componentCount = 3;
        if (value.Count(character => character == '.') != 2
            || !Version.TryParse(value, out Version? parsed)
            || !string.Equals(
                parsed.ToString(componentCount),
                value,
                StringComparison.Ordinal))
        {
            version = new Version(0, 0);
            return false;
        }

        version = parsed;
        return true;
    }

    private static string DisplayVersion(Version version)
        => version.ToString(3);

    private static Version NormalizeCurrentVersion(Version version) =>
        new(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0));

    private static UpdateCheckResult Failed(
        string currentVersion,
        string message) =>
        new(
            UpdateCheckStatus.Failed,
            currentVersion,
            ErrorMessage: message);

    private sealed record ParsedManifest(
        Version AvailableVersion,
        UpdatePackageDescriptor Package,
        string ReleaseNotes);

    private sealed record StagingPaths(
        string InstallationDirectory,
        string StagingDirectory,
        string ParentDirectory);

    private sealed record ValidatedArchiveEntry(
        ZipArchiveEntry Entry,
        string RelativePath,
        string TargetPath,
        bool IsDirectory);
}
