using System.Globalization;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuluPet.Update;

namespace GuluPet.Tests;

internal static class ManualUpdateServiceTests
{
    private static readonly Uri ManifestUri =
        UpdateEndpoints.StableManifestUri;

    private static readonly Uri PackageUri =
        PackageUriForVersion("1.2.0");

    private const string PublishedAtUtc =
        "2026-08-09T02:03:04.0000000Z";

    public static void RunAll()
    {
        RunAsync(
            nameof(NullManifestExplicitlyDisablesNetwork),
            NullManifestExplicitlyDisablesNetwork);
        RunAsync(
            nameof(OnlyFixedHttpsManifestEndpointIsAccepted),
            OnlyFixedHttpsManifestEndpointIsAccepted);
        RunAsync(
            nameof(SignedCurrentVersionIsUpToDate),
            SignedCurrentVersionIsUpToDate);
        RunAsync(
            nameof(SignedNewVersionReturnsValidatedPackage),
            SignedNewVersionReturnsValidatedPackage);
        RunAsync(
            nameof(BadSignatureNeverReturnsUpdateAvailable),
            BadSignatureNeverReturnsUpdateAvailable);
        RunAsync(
            nameof(StrictManifestRejectsUnknownDuplicateAndNonCanonicalTime),
            StrictManifestRejectsUnknownDuplicateAndNonCanonicalTime);
        RunAsync(
            nameof(HttpAndCrossHostPackagesAreRejected),
            HttpAndCrossHostPackagesAreRejected);
        RunAsync(
            nameof(ManifestRedirectIsRejected),
            ManifestRedirectIsRejected);
        RunAsync(
            nameof(OversizedManifestHeadersAndBodiesAreRejected),
            OversizedManifestHeadersAndBodiesAreRejected);
        RunAsync(
            nameof(HangingManifestBodyHonorsWholeCheckTimeout),
            HangingManifestBodyHonorsWholeCheckTimeout);
        RunAsync(
            nameof(CallerCancellationIsNotConvertedToFailure),
            CallerCancellationIsNotConvertedToFailure);
        RunAsync(
            nameof(ValidPackageStagesWithStreamingProgress),
            ValidPackageStagesWithStreamingProgress);
        RunAsync(
            nameof(PackageHashAndSizeMismatchesAreRejected),
            PackageHashAndSizeMismatchesAreRejected);
        RunAsync(
            nameof(PackageRedirectIsRejected),
            PackageRedirectIsRejected);
        RunAsync(
            nameof(HangingPackageHonorsStageTimeout),
            HangingPackageHonorsStageTimeout);
        RunAsync(
            nameof(CallerCancellationCleansOperationWorkspace),
            CallerCancellationCleansOperationWorkspace);
        RunAsync(
            nameof(UnsafeArchivesAreRejectedAndCleaned),
            UnsafeArchivesAreRejectedAndCleaned);
        Run(
            nameof(StagingMustBeNewSiblingOnRealDirectories),
            StagingMustBeNewSiblingOnRealDirectories);
        Run(
            nameof(StaleOperationWorkspaceCleanupIsExactAndAgeBounded),
            StaleOperationWorkspaceCleanupIsExactAndAgeBounded);
        Run(
            nameof(ExpiredInstallLogCleanupIsExactAndAgeBounded),
            ExpiredInstallLogCleanupIsExactAndAgeBounded);
        Run(
            nameof(PostUpdateReadyArgumentIsStrictAndSingle),
            PostUpdateReadyArgumentIsStrictAndSingle);
    }

    private static async Task NullManifestExplicitlyDisablesNetwork()
    {
        var handler = new DelegateHttpMessageHandler(
            (_, _) => throw new InvalidOperationException(
                "Network access was not expected."));
        using var service = new ManualUpdateService(
            manifestUri: null,
            currentVersion: new Version(1, 0, 0),
            messageHandler: handler);

        UpdateCheckResult result = await service.CheckAsync();

        BehaviorTestCheck.Equal(UpdateCheckStatus.NotConfigured, result.Status);
        BehaviorTestCheck.Equal(0, handler.CallCount);
        BehaviorTestCheck.Equal(
            "https://updates.example.invalid/stable/manifest.json",
            UpdateEndpoints.StableManifestUri.AbsoluteUri);
    }

    private static async Task OnlyFixedHttpsManifestEndpointIsAccepted()
    {
        Uri[] invalidUris =
        [
            new("http://updates.example.invalid/stable/manifest.json"),
            new("https://other.example.invalid/stable/manifest.json"),
            new("https://updates.example.invalid/testing/manifest.json"),
            new("https://updates.example.invalid:444/stable/manifest.json"),
        ];

        foreach (Uri invalidUri in invalidUris)
        {
            var handler = new DelegateHttpMessageHandler(
                (_, _) => throw new InvalidOperationException(
                    "An invalid endpoint must fail before network access."));
            using var service = new ManualUpdateService(
                invalidUri,
                new Version(1, 0, 0),
                handler);

            UpdateCheckResult result = await service.CheckAsync();

            BehaviorTestCheck.Equal(UpdateCheckStatus.Failed, result.Status);
            BehaviorTestCheck.Equal(0, handler.CallCount);
        }
    }

    private static async Task SignedCurrentVersionIsUpToDate()
    {
        using ECDsa signer = CreateSigner();
        byte[] package = CreateValidPackage();
        string manifest = CreateManifest(
            signer,
            version: "1.0.0",
            package);
        using var service = CreateService(
            signer,
            _ => JsonResponse(manifest),
            currentVersion: new Version(1, 0, 0));

        UpdateCheckResult result = await service.CheckAsync();

        BehaviorTestCheck.Equal(UpdateCheckStatus.UpToDate, result.Status);
        BehaviorTestCheck.Equal("1.0.0", result.AvailableVersion!);
        BehaviorTestCheck.Null(result.Package);
    }

    private static async Task SignedNewVersionReturnsValidatedPackage()
    {
        using ECDsa signer = CreateSigner();
        byte[] package = CreateValidPackage();
        string expectedHash = Sha256(package);
        string manifest = CreateManifest(signer, "1.2.0", package);
        using var service = CreateService(
            signer,
            _ => JsonResponse(manifest));

        UpdateCheckResult result = await service.CheckAsync();

        BehaviorTestCheck.Equal(
            UpdateCheckStatus.UpdateAvailable,
            result.Status);
        BehaviorTestCheck.Equal("1.2.0", result.AvailableVersion!);
        BehaviorTestCheck.Equal(PackageUri, result.DownloadUri!);
        BehaviorTestCheck.Equal("Release notes", result.ReleaseNotes!);
        UpdatePackageDescriptor descriptor =
            BehaviorTestCheck.NotNull(result.Package);
        BehaviorTestCheck.Equal(package.LongLength, descriptor.PackageSize);
        BehaviorTestCheck.Equal(expectedHash, descriptor.PackageSha256);
        BehaviorTestCheck.Equal(
            PublishedAtUtc,
            descriptor.PublishedAtUtc.UtcDateTime.ToString(
                "O",
                CultureInfo.InvariantCulture));
    }

    private static async Task BadSignatureNeverReturnsUpdateAvailable()
    {
        using ECDsa signer = CreateSigner();
        byte[] package = CreateValidPackage();
        string manifest = CreateManifest(signer, "1.2.0", package);
        using JsonDocument document = JsonDocument.Parse(manifest);
        var values = ReadManifestValues(document);
        byte[] signature = Convert.FromBase64String(values.PackageSignature);
        signature[0] ^= 0x80;
        manifest = CreateRawManifest(
            values with
            {
                PackageSignature = Convert.ToBase64String(signature),
            });
        using var service = CreateService(
            signer,
            _ => JsonResponse(manifest));

        UpdateCheckResult result = await service.CheckAsync();

        BehaviorTestCheck.Equal(UpdateCheckStatus.Failed, result.Status);
        BehaviorTestCheck.Null(result.Package);
    }

    private static async Task StrictManifestRejectsUnknownDuplicateAndNonCanonicalTime()
    {
        using ECDsa signer = CreateSigner();
        byte[] package = CreateValidPackage();
        string valid = CreateManifest(signer, "1.2.0", package);
        string unknown = valid[..^1] + ",\"unexpected\":true}";
        string duplicate = valid.Replace(
            "\"schemaVersion\":1",
            "\"schemaVersion\":1,\"schemaVersion\":1",
            StringComparison.Ordinal);

        using JsonDocument document = JsonDocument.Parse(valid);
        ManifestValues values = ReadManifestValues(document);
        string nonCanonicalTime = CreateRawManifest(
            values with
            {
                PublishedAtUtc =
                    "2026-08-09T02:03:04.0000000+00:00",
            });

        foreach (string invalid in new[] { unknown, duplicate, nonCanonicalTime })
        {
            using var service = CreateService(
                signer,
                _ => JsonResponse(invalid));
            UpdateCheckResult result = await service.CheckAsync();
            BehaviorTestCheck.Equal(UpdateCheckStatus.Failed, result.Status);
        }
    }

    private static async Task HttpAndCrossHostPackagesAreRejected()
    {
        using ECDsa signer = CreateSigner();
        byte[] package = CreateValidPackage();

        foreach (Uri invalidPackageUri in new[]
        {
            new Uri(
                "http://updates.example.invalid/releases/1.2.0/" +
                "GuluPet-1.2.0-win-x64.zip"),
            new Uri(
                "https://downloads.example.invalid/releases/1.2.0/" +
                "GuluPet-1.2.0-win-x64.zip"),
            new Uri("https://updates.example.invalid/stable/GuluPet-1.2.0.zip"),
            new Uri(
                "https://updates.example.invalid/releases/1.2.0/" +
                "GuluPet-1.2.0-win-x64.zip?mirror=1"),
        })
        {
            string manifest = CreateManifest(
                signer,
                "1.2.0",
                package,
                packageUri: invalidPackageUri);
            using var service = CreateService(
                signer,
                _ => JsonResponse(manifest));

            UpdateCheckResult result = await service.CheckAsync();

            BehaviorTestCheck.Equal(UpdateCheckStatus.Failed, result.Status);
        }
    }

    private static async Task ManifestRedirectIsRejected()
    {
        using ECDsa signer = CreateSigner();
        using var service = CreateService(
            signer,
            request => new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                RequestMessage = request,
                Headers =
                {
                    Location = new Uri(
                        "https://updates.example.invalid/stable/manifest-v2.json"),
                },
            });

        UpdateCheckResult result = await service.CheckAsync();

        BehaviorTestCheck.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    private static async Task OversizedManifestHeadersAndBodiesAreRejected()
    {
        using ECDsa signer = CreateSigner();
        var headerHandler = new DelegateHttpMessageHandler(
            (request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new DeclaredLengthContent(
                    ManualUpdateService.ManifestSizeLimitBytes + 1),
            }));
        using (var service = new ManualUpdateService(
            ManifestUri,
            new Version(1, 0, 0),
            headerHandler,
            signer.ExportSubjectPublicKeyInfoPem()))
        {
            UpdateCheckResult result = await service.CheckAsync();
            BehaviorTestCheck.Equal(UpdateCheckStatus.Failed, result.Status);
        }

        byte[] oversized = new byte[
            ManualUpdateService.ManifestSizeLimitBytes + 1];
        Array.Fill(oversized, (byte)' ');
        var bodyHandler = new DelegateHttpMessageHandler(
            (request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(
                    new NonSeekableMemoryStream(oversized)),
            }));
        using (var service = new ManualUpdateService(
            ManifestUri,
            new Version(1, 0, 0),
            bodyHandler,
            signer.ExportSubjectPublicKeyInfoPem()))
        {
            UpdateCheckResult result = await service.CheckAsync();
            BehaviorTestCheck.Equal(UpdateCheckStatus.Failed, result.Status);
        }
    }

    private static async Task HangingManifestBodyHonorsWholeCheckTimeout()
    {
        using ECDsa signer = CreateSigner();
        var handler = new DelegateHttpMessageHandler(
            (request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(new HangingStream()),
            }));
        using var service = new ManualUpdateService(
            ManifestUri,
            new Version(1, 0, 0),
            handler,
            signer.ExportSubjectPublicKeyInfoPem(),
            checkTimeout: TimeSpan.FromMilliseconds(50));

        UpdateCheckResult result = await service.CheckAsync();

        BehaviorTestCheck.Equal(UpdateCheckStatus.Failed, result.Status);
        BehaviorTestCheck.True(
            result.ErrorMessage?.Contains("超时", StringComparison.Ordinal) == true);
    }

    private static async Task CallerCancellationIsNotConvertedToFailure()
    {
        using ECDsa signer = CreateSigner();
        var handler = new DelegateHttpMessageHandler(
            (request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(new HangingStream()),
            }));
        using var service = new ManualUpdateService(
            ManifestUri,
            new Version(1, 0, 0),
            handler,
            signer.ExportSubjectPublicKeyInfoPem());
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(30));

        await ThrowsAsync<OperationCanceledException>(
            () => service.CheckAsync(cancellation.Token));
    }

    private static async Task ValidPackageStagesWithStreamingProgress()
    {
        using ECDsa signer = CreateSigner();
        byte[] package = CreateValidPackage(includeNestedFile: true);
        string manifest = CreateManifest(signer, "1.2.0", package);
        using var service = CreatePackageService(signer, manifest, package);
        UpdateCheckResult update = await service.CheckAsync();
        using var layout = new TemporaryLayout();
        var progressValues = new List<UpdatePreparationProgress>();
        var progress = new InlineProgress<UpdatePreparationProgress>(
            value => progressValues.Add(value));

        StagedUpdateResult staged = await service.StageUpdateAsync(
            update,
            layout.InstallationDirectory,
            layout.StagingDirectory,
            progress);

        BehaviorTestCheck.Equal(
            Path.GetFullPath(layout.StagingDirectory),
            staged.StagingDirectory);
        BehaviorTestCheck.True(
            File.Exists(Path.Combine(staged.StagingDirectory, "GuluPet.exe")));
        BehaviorTestCheck.True(
            File.Exists(Path.Combine(staged.StagingDirectory, "GuluPet.dll")));
        BehaviorTestCheck.True(
            File.Exists(Path.Combine(
                staged.StagingDirectory,
                "Assets",
                "content.bin")));
        BehaviorTestCheck.True(progressValues.Count >= 2);
        BehaviorTestCheck.Equal(
            UpdatePreparationPhase.Downloading,
            progressValues[0].Phase);
        BehaviorTestCheck.Equal(0L, progressValues[0].BytesProcessed);
        BehaviorTestCheck.Equal(
            UpdatePreparationPhase.Ready,
            progressValues[^1].Phase);
        BehaviorTestCheck.Close(100, progressValues[^1].Percentage);
        BehaviorTestCheck.True(progressValues.Any(value =>
            value.Phase == UpdatePreparationPhase.Verifying));
        BehaviorTestCheck.True(progressValues.Any(value =>
            value.Phase == UpdatePreparationPhase.Extracting));
        BehaviorTestCheck.True(progressValues
            .Zip(progressValues.Skip(1))
            .All(pair => pair.First.Percentage <= pair.Second.Percentage));
        BehaviorTestCheck.True(progressValues
            .Where(value => value.Phase == UpdatePreparationPhase.Downloading)
            .All(value => value.Percentage <= 70));
        BehaviorTestCheck.Equal(
            0,
            Directory.GetFiles(
                layout.RootDirectory,
                ".gulupet-update-*.download").Length);
        BehaviorTestCheck.False(Directory.Exists(Path.Combine(
            layout.RootDirectory,
            ManualUpdateService.OperationWorkspaceDirectoryName)));
    }

    private static async Task PackageHashAndSizeMismatchesAreRejected()
    {
        using ECDsa signer = CreateSigner();
        byte[] package = CreateValidPackage();
        var cases = new[]
        {
            new
            {
                Size = (long?)package.LongLength,
                Hash = new string('0', 64),
            },
            new
            {
                Size = (long?)(package.LongLength + 1),
                Hash = Sha256(package),
            },
            new
            {
                Size = (long?)(package.LongLength - 1),
                Hash = Sha256(package),
            },
        };

        foreach (var invalid in cases)
        {
            string manifest = CreateManifest(
                signer,
                "1.2.0",
                package,
                packageSize: invalid.Size,
                packageSha256: invalid.Hash);
            using var service = CreatePackageService(
                signer,
                manifest,
                package,
                declareContentLength: false);
            UpdateCheckResult update = await service.CheckAsync();
            BehaviorTestCheck.Equal(
                UpdateCheckStatus.UpdateAvailable,
                update.Status);
            using var layout = new TemporaryLayout();

            await ThrowsAsync<InvalidDataException>(() =>
                service.StageUpdateAsync(
                    update,
                    layout.InstallationDirectory,
                    layout.StagingDirectory));

            BehaviorTestCheck.False(
                Directory.Exists(layout.StagingDirectory));
        }
    }

    private static async Task PackageRedirectIsRejected()
    {
        using ECDsa signer = CreateSigner();
        byte[] package = CreateValidPackage();
        string manifest = CreateManifest(signer, "1.2.0", package);
        using var service = CreateService(
            signer,
            request => request.RequestUri == ManifestUri
                ? JsonResponse(manifest, request)
                : new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    RequestMessage = request,
                    Headers =
                    {
                        Location = new Uri(
                            "https://updates.example.invalid/stable/other.zip"),
                    },
                });
        UpdateCheckResult update = await service.CheckAsync();
        using var layout = new TemporaryLayout();

        await ThrowsAsync<InvalidDataException>(() =>
            service.StageUpdateAsync(
                update,
                layout.InstallationDirectory,
                layout.StagingDirectory));

        BehaviorTestCheck.False(Directory.Exists(layout.StagingDirectory));
    }

    private static async Task HangingPackageHonorsStageTimeout()
    {
        using ECDsa signer = CreateSigner();
        byte[] package = CreateValidPackage();
        string manifest = CreateManifest(signer, "1.2.0", package);
        var handler = new DelegateHttpMessageHandler(
            (request, _) => Task.FromResult(
                request.RequestUri == ManifestUri
                    ? JsonResponse(manifest, request)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        RequestMessage = request,
                        Content = new StreamContent(new HangingStream()),
                    }));
        using var service = new ManualUpdateService(
            ManifestUri,
            new Version(1, 0, 0),
            handler,
            signer.ExportSubjectPublicKeyInfoPem(),
            stageTimeout: TimeSpan.FromMilliseconds(50));
        UpdateCheckResult update = await service.CheckAsync();
        using var layout = new TemporaryLayout();

        await ThrowsAsync<TimeoutException>(() =>
            service.StageUpdateAsync(
                update,
                layout.InstallationDirectory,
                layout.StagingDirectory));

        BehaviorTestCheck.False(Directory.Exists(layout.StagingDirectory));
        BehaviorTestCheck.Equal(
            0,
            Directory.GetFiles(
                layout.RootDirectory,
                ".gulupet-update-*.download").Length);
        BehaviorTestCheck.False(Directory.Exists(Path.Combine(
            layout.RootDirectory,
            ManualUpdateService.OperationWorkspaceDirectoryName)));
    }

    private static async Task CallerCancellationCleansOperationWorkspace()
    {
        using ECDsa signer = CreateSigner();
        byte[] package = CreateValidPackage();
        string manifest = CreateManifest(signer, "1.2.0", package);
        var handler = new DelegateHttpMessageHandler(
            (request, _) => Task.FromResult(
                request.RequestUri == ManifestUri
                    ? JsonResponse(manifest, request)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        RequestMessage = request,
                        Content = new StreamContent(new HangingStream()),
                    }));
        using var service = new ManualUpdateService(
            ManifestUri,
            new Version(1, 0, 0),
            handler,
            signer.ExportSubjectPublicKeyInfoPem());
        UpdateCheckResult update = await service.CheckAsync();
        using var layout = new TemporaryLayout();
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(50));

        await ThrowsAsync<OperationCanceledException>(() =>
            service.StageUpdateAsync(
                update,
                layout.InstallationDirectory,
                layout.StagingDirectory,
                cancellationToken: cancellation.Token));

        BehaviorTestCheck.False(Directory.Exists(layout.StagingDirectory));
        BehaviorTestCheck.False(Directory.Exists(Path.Combine(
            layout.RootDirectory,
            ManualUpdateService.OperationWorkspaceDirectoryName)));
    }

    private static async Task UnsafeArchivesAreRejectedAndCleaned()
    {
        using ECDsa signer = CreateSigner();
        string absoluteEscapePath = Path.Combine(
            Path.GetTempPath(),
            $"GuluPetUpdateEscape-{Guid.NewGuid():N}.txt");
        string absoluteZipPath = absoluteEscapePath.Replace('\\', '/');
        try
        {
            foreach (byte[] package in new[]
            {
                CreateMaliciousPackage("../escaped.txt", symbolicLink: false),
                CreateMaliciousPackage(absoluteZipPath, symbolicLink: false),
                CreateMaliciousPackage("link", symbolicLink: true),
                CreatePackageWithoutRuntime(),
            })
            {
                string manifest = CreateManifest(signer, "1.2.0", package);
                using var service = CreatePackageService(
                    signer,
                    manifest,
                    package);
                UpdateCheckResult update = await service.CheckAsync();
                using var layout = new TemporaryLayout();

                await ThrowsAsync<InvalidDataException>(() =>
                    service.StageUpdateAsync(
                        update,
                        layout.InstallationDirectory,
                        layout.StagingDirectory));

                BehaviorTestCheck.False(
                    Directory.Exists(layout.StagingDirectory));
                BehaviorTestCheck.False(
                    File.Exists(Path.Combine(
                        layout.RootDirectory,
                        "escaped.txt")));
                BehaviorTestCheck.False(File.Exists(absoluteEscapePath));
            }
        }
        finally
        {
            if (File.Exists(absoluteEscapePath))
            {
                File.Delete(absoluteEscapePath);
            }
        }
    }

    private static void StagingMustBeNewSiblingOnRealDirectories()
    {
        using ECDsa signer = CreateSigner();
        byte[] package = CreateValidPackage();
        string manifest = CreateManifest(signer, "1.2.0", package);
        using var service = CreatePackageService(signer, manifest, package);
        UpdateCheckResult update = service.CheckAsync().GetAwaiter().GetResult();
        using var layout = new TemporaryLayout();
        Directory.CreateDirectory(layout.StagingDirectory);

        BehaviorTestCheck.Throws<IOException>(() =>
            service.StageUpdateAsync(
                    update,
                    layout.InstallationDirectory,
                    layout.StagingDirectory)
                .GetAwaiter()
                .GetResult());

        string otherParent = Path.Combine(
            Path.GetTempPath(),
            $"GuluPetUpdateOther-{Guid.NewGuid():N}",
            "staging");
        BehaviorTestCheck.Throws<ArgumentException>(() =>
            service.StageUpdateAsync(
                    update,
                    layout.InstallationDirectory,
                    otherParent)
                .GetAwaiter()
                .GetResult());
    }

    private static void StaleOperationWorkspaceCleanupIsExactAndAgeBounded()
    {
        using var layout = new TemporaryLayout();
        var now = new DateTimeOffset(
            2026,
            8,
            9,
            12,
            0,
            0,
            TimeSpan.Zero);
        string workspace = Path.Combine(
            layout.RootDirectory,
            ManualUpdateService.OperationWorkspaceDirectoryName);
        string nonMatching = Path.Combine(
            layout.RootDirectory,
            ".gulupet-update-operation-user.work");
        Directory.CreateDirectory(nonMatching);
        File.WriteAllText(
            Path.Combine(nonMatching, "keep.txt"),
            "user-owned");
        Directory.SetLastWriteTimeUtc(
            nonMatching,
            now.UtcDateTime.Subtract(TimeSpan.FromDays(30)));

        CreateOwnedOperationWorkspace(workspace);
        Directory.SetLastWriteTimeUtc(
            workspace,
            now.UtcDateTime
                .Subtract(ManualUpdateService.StaleOperationWorkspaceAge)
                .AddMinutes(1));

        BehaviorTestCheck.Equal(
            0,
            ManualUpdateService.CleanupStaleOperationWorkspaces(
                layout.InstallationDirectory,
                now));
        BehaviorTestCheck.True(Directory.Exists(workspace));
        BehaviorTestCheck.True(Directory.Exists(nonMatching));

        Directory.SetLastWriteTimeUtc(
            workspace,
            now.UtcDateTime
                .Subtract(ManualUpdateService.StaleOperationWorkspaceAge)
                .Subtract(TimeSpan.FromMinutes(1)));
        string markerPath = Path.Combine(
            workspace,
            ManualUpdateService.OperationWorkspaceMarkerFileName);
        using (var activeLease = new FileStream(
            markerPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read))
        {
            BehaviorTestCheck.Equal(
                0,
                ManualUpdateService.CleanupStaleOperationWorkspaces(
                    layout.InstallationDirectory,
                    now));
            BehaviorTestCheck.True(Directory.Exists(workspace));
        }

        BehaviorTestCheck.Equal(
            1,
            ManualUpdateService.CleanupStaleOperationWorkspaces(
                layout.InstallationDirectory,
                now));
        BehaviorTestCheck.False(Directory.Exists(workspace));
        BehaviorTestCheck.True(File.Exists(Path.Combine(
            nonMatching,
            "keep.txt")));

        Directory.CreateDirectory(workspace);
        File.WriteAllText(
            Path.Combine(
                workspace,
                ManualUpdateService.OperationWorkspaceMarkerFileName),
            "GuluPet.Update.",
            Encoding.ASCII);
        Directory.SetLastWriteTimeUtc(
            workspace,
            now.UtcDateTime.Subtract(TimeSpan.FromDays(30)));
        BehaviorTestCheck.Equal(
            1,
            ManualUpdateService.CleanupStaleOperationWorkspaces(
                layout.InstallationDirectory,
                now));
        BehaviorTestCheck.False(Directory.Exists(workspace));

        Directory.CreateDirectory(workspace);
        File.WriteAllText(
            Path.Combine(workspace, "keep.txt"),
            "unknown-owner");
        Directory.SetLastWriteTimeUtc(
            workspace,
            now.UtcDateTime.Subtract(TimeSpan.FromDays(30)));
        BehaviorTestCheck.Equal(
            0,
            ManualUpdateService.CleanupStaleOperationWorkspaces(
                layout.InstallationDirectory,
                now));
        BehaviorTestCheck.True(File.Exists(Path.Combine(
            workspace,
            "keep.txt")));
        Directory.Delete(workspace, recursive: true);

        string reparseTarget = Path.Combine(
            layout.RootDirectory,
            "reparse-target");
        Directory.CreateDirectory(reparseTarget);
        string sentinel = Path.Combine(reparseTarget, "keep.txt");
        File.WriteAllText(sentinel, "do-not-follow");
        CreateDirectoryReparsePoint(workspace, reparseTarget);
        try
        {
            BehaviorTestCheck.Equal(
                0,
                ManualUpdateService.CleanupStaleOperationWorkspaces(
                    layout.InstallationDirectory,
                    now));
            BehaviorTestCheck.True(Directory.Exists(workspace));
            BehaviorTestCheck.True(File.Exists(sentinel));
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: false);
            }
        }
    }

    private static void PostUpdateReadyArgumentIsStrictAndSingle()
    {
        Guid id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        string eventName = PostUpdateReadySignal.CreateEventName(id);
        PostUpdateReadySignal signal = BehaviorTestCheck.NotNull(
            PostUpdateReadySignal.Parse(
            [
                "--isolated-test-instance",
                PostUpdateReadySignal.ArgumentPrefix + eventName,
            ]));
        BehaviorTestCheck.Equal(eventName, signal.EventName);
        BehaviorTestCheck.Null(PostUpdateReadySignal.Parse(["--other"]));

        string[] invalidArguments =
        [
            "--post-update-ready-event",
            "--post-update-ready-event=Global\\GuluPet.Update.Ready." +
                id.ToString("N"),
            "--post-update-ready-event=Local\\GuluPet.Update.Ready." +
                id.ToString("D"),
            "--post-update-ready-event=Local\\GuluPet.Update.Ready." +
                id.ToString("N").ToUpperInvariant(),
        ];
        foreach (string invalid in invalidArguments)
        {
            BehaviorTestCheck.Throws<FormatException>(() =>
                PostUpdateReadySignal.Parse([invalid]));
        }

        BehaviorTestCheck.Throws<FormatException>(() =>
            PostUpdateReadySignal.Parse(
            [
                PostUpdateReadySignal.ArgumentPrefix + eventName,
                PostUpdateReadySignal.ArgumentPrefix + eventName,
            ]));

        Guid liveId = Guid.NewGuid();
        string liveEventName = PostUpdateReadySignal.CreateEventName(liveId);
        using var readyEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            liveEventName,
            out bool createdNew);
        BehaviorTestCheck.True(createdNew);
        PostUpdateReadySignal liveSignal = BehaviorTestCheck.NotNull(
            PostUpdateReadySignal.Parse(
            [
                PostUpdateReadySignal.ArgumentPrefix + liveEventName,
            ]));
        liveSignal.SignalExisting();
        BehaviorTestCheck.True(readyEvent.WaitOne(0));
    }

    private static void ExpiredInstallLogCleanupIsExactAndAgeBounded()
    {
        using var layout = new TemporaryLayout();
        var now = new DateTimeOffset(
            2026,
            8,
            16,
            12,
            0,
            0,
            TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        string updatesRoot = Path.Combine(layout.RootDirectory, "Updates");
        Directory.CreateDirectory(updatesRoot);

        Guid expiredId = Guid.NewGuid();
        string expiredDirectory = Path.Combine(
            updatesRoot,
            expiredId.ToString("N"));
        Directory.CreateDirectory(expiredDirectory);
        string expiredLog = Path.Combine(
            expiredDirectory,
            $"update-install-{expiredId:N}.log");
        File.WriteAllText(expiredLog, "expired");
        File.SetLastWriteTimeUtc(
            expiredLog,
            now.UtcDateTime
                .Subtract(ManualUpdateService.UpdateInstallLogRetention)
                .Subtract(TimeSpan.FromMinutes(1)));
        string journal = Path.Combine(
            expiredDirectory,
            $"update-journal-{expiredId:N}.json");
        string result = Path.Combine(
            expiredDirectory,
            $"update-result-{expiredId:N}.json");
        string installer = Path.Combine(
            expiredDirectory,
            "Install-GuluPetUpdate.ps1");
        string unrelated = Path.Combine(expiredDirectory, "keep.log");
        foreach (string sentinel in new[]
                 {
                     journal,
                     result,
                     installer,
                     unrelated,
                 })
        {
            File.WriteAllText(sentinel, "keep");
            File.SetLastWriteTimeUtc(
                sentinel,
                now.UtcDateTime.Subtract(TimeSpan.FromDays(30)));
        }

        Guid recentId = Guid.NewGuid();
        string recentDirectory = Path.Combine(
            updatesRoot,
            recentId.ToString("N"));
        Directory.CreateDirectory(recentDirectory);
        string recentLog = Path.Combine(
            recentDirectory,
            $"update-install-{recentId:N}.log");
        File.WriteAllText(recentLog, "recent");
        File.SetLastWriteTimeUtc(
            recentLog,
            now.UtcDateTime
                .Subtract(ManualUpdateService.UpdateInstallLogRetention)
                .Add(TimeSpan.FromMinutes(1)));

        Guid boundaryId = Guid.NewGuid();
        string boundaryDirectory = Path.Combine(
            updatesRoot,
            boundaryId.ToString("N"));
        Directory.CreateDirectory(boundaryDirectory);
        string boundaryLog = Path.Combine(
            boundaryDirectory,
            $"update-install-{boundaryId:N}.log");
        File.WriteAllText(boundaryLog, "boundary");
        File.SetLastWriteTimeUtc(
            boundaryLog,
            now.UtcDateTime.Subtract(
                ManualUpdateService.UpdateInstallLogRetention));

        string invalidDirectory = Path.Combine(updatesRoot, "not-an-operation");
        Directory.CreateDirectory(invalidDirectory);
        string invalidLog = Path.Combine(
            invalidDirectory,
            "update-install-not-an-operation.log");
        File.WriteAllText(invalidLog, "invalid");
        File.SetLastWriteTimeUtc(
            invalidLog,
            now.UtcDateTime.Subtract(TimeSpan.FromDays(30)));

        Guid mismatchedDirectoryId = Guid.NewGuid();
        Guid mismatchedLogId = Guid.NewGuid();
        string mismatchedDirectory = Path.Combine(
            updatesRoot,
            mismatchedDirectoryId.ToString("N"));
        Directory.CreateDirectory(mismatchedDirectory);
        string mismatchedLog = Path.Combine(
            mismatchedDirectory,
            $"update-install-{mismatchedLogId:N}.log");
        File.WriteAllText(mismatchedLog, "mismatched");
        File.SetLastWriteTimeUtc(
            mismatchedLog,
            now.UtcDateTime.Subtract(TimeSpan.FromDays(30)));

        string reparseTarget = Path.Combine(
            layout.RootDirectory,
            "update-log-reparse-target");
        Directory.CreateDirectory(reparseTarget);
        Guid reparseId = Guid.NewGuid();
        string reparseLog = Path.Combine(
            reparseTarget,
            $"update-install-{reparseId:N}.log");
        File.WriteAllText(reparseLog, "do-not-follow");
        File.SetLastWriteTimeUtc(
            reparseLog,
            now.UtcDateTime.Subtract(TimeSpan.FromDays(30)));
        string reparseDirectory = Path.Combine(
            updatesRoot,
            reparseId.ToString("N"));
        CreateDirectoryReparsePoint(reparseDirectory, reparseTarget);

        string ancestorTarget = Path.Combine(
            layout.RootDirectory,
            "update-log-ancestor-target");
        string linkedUpdatesRoot = Path.Combine(
            ancestorTarget,
            "GuluPet",
            "Updates");
        Guid ancestorId = Guid.NewGuid();
        string ancestorOperation = Path.Combine(
            linkedUpdatesRoot,
            ancestorId.ToString("N"));
        Directory.CreateDirectory(ancestorOperation);
        string ancestorLog = Path.Combine(
            ancestorOperation,
            $"update-install-{ancestorId:N}.log");
        File.WriteAllText(ancestorLog, "do-not-follow-ancestor");
        File.SetLastWriteTimeUtc(
            ancestorLog,
            now.UtcDateTime.Subtract(TimeSpan.FromDays(30)));
        string ancestorLink = Path.Combine(
            layout.RootDirectory,
            "linked-local-app-data");
        CreateDirectoryReparsePoint(ancestorLink, ancestorTarget);
        string updatesRootThroughReparse = Path.Combine(
            ancestorLink,
            "GuluPet",
            "Updates");

        try
        {
            BehaviorTestCheck.Equal(
                1,
                ManualUpdateService.CleanupExpiredUpdateInstallLogs(
                    updatesRoot,
                    timeProvider));
            BehaviorTestCheck.False(File.Exists(expiredLog));
            BehaviorTestCheck.True(Directory.Exists(expiredDirectory));
            BehaviorTestCheck.True(File.Exists(journal));
            BehaviorTestCheck.True(File.Exists(result));
            BehaviorTestCheck.True(File.Exists(installer));
            BehaviorTestCheck.True(File.Exists(unrelated));
            BehaviorTestCheck.True(File.Exists(recentLog));
            BehaviorTestCheck.True(File.Exists(boundaryLog));
            BehaviorTestCheck.True(File.Exists(invalidLog));
            BehaviorTestCheck.True(File.Exists(mismatchedLog));
            BehaviorTestCheck.True(File.Exists(reparseLog));
            BehaviorTestCheck.Equal(
                0,
                ManualUpdateService.CleanupExpiredUpdateInstallLogs(
                    updatesRootThroughReparse,
                    timeProvider));
            BehaviorTestCheck.True(File.Exists(ancestorLog));
        }
        finally
        {
            if (Directory.Exists(reparseDirectory))
            {
                Directory.Delete(reparseDirectory, recursive: false);
            }
            if (Directory.Exists(ancestorLink))
            {
                Directory.Delete(ancestorLink, recursive: false);
            }
        }
    }

    private static ManualUpdateService CreateService(
        ECDsa signer,
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        Version? currentVersion = null)
    {
        var handler = new DelegateHttpMessageHandler(
            (request, _) => Task.FromResult(responder(request)));
        return new ManualUpdateService(
            ManifestUri,
            currentVersion ?? new Version(1, 0, 0),
            handler,
            signer.ExportSubjectPublicKeyInfoPem());
    }

    private static ManualUpdateService CreatePackageService(
        ECDsa signer,
        string manifest,
        byte[] package,
        bool declareContentLength = true) =>
        CreateService(
            signer,
            request => request.RequestUri == ManifestUri
                ? JsonResponse(manifest, request)
                : request.RequestUri == PackageUri
                    ? BinaryResponse(
                        package,
                        request,
                        declareContentLength)
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        RequestMessage = request,
                    });

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpRequestMessage? request = null) =>
        new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage BinaryResponse(
        byte[] bytes,
        HttpRequestMessage request,
        bool declareContentLength) =>
        new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = declareContentLength
                ? new ByteArrayContent(bytes)
                : new StreamContent(new NonSeekableMemoryStream(bytes)),
        };

    private static ECDsa CreateSigner() =>
        ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private static string CreateManifest(
        ECDsa signer,
        string version,
        byte[] package,
        Uri? packageUri = null,
        long? packageSize = null,
        string? packageSha256 = null)
    {
        Uri resolvedPackageUri = packageUri ?? PackageUriForVersion(version);
        long resolvedSize = packageSize ?? package.LongLength;
        string resolvedHash = packageSha256 ?? Sha256(package);
        string signature = Convert.ToBase64String(signer.SignData(
            CreateCanonicalPayload(
                version,
                PublishedAtUtc,
                resolvedPackageUri,
                resolvedSize,
                resolvedHash),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return CreateRawManifest(new ManifestValues(
            1,
            version,
            PublishedAtUtc,
            resolvedPackageUri.AbsoluteUri,
            resolvedSize,
            resolvedHash,
            signature,
            "Release notes"));
    }

    private static byte[] CreateCanonicalPayload(
        string version,
        string publishedAtUtc,
        Uri packageUri,
        long packageSize,
        string packageSha256)
    {
        string canonical =
            $"GuluPet.Update.v1\n{version}\n{publishedAtUtc}\n" +
            $"{packageUri.AbsoluteUri}\n" +
            packageSize.ToString(CultureInfo.InvariantCulture) + "\n" +
            packageSha256 + "\n";
        return Encoding.UTF8.GetBytes(canonical);
    }

    private static string CreateRawManifest(ManifestValues values) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schemaVersion"] = values.SchemaVersion,
            ["version"] = values.Version,
            ["publishedAtUtc"] = values.PublishedAtUtc,
            ["packageUrl"] = values.PackageUrl,
            ["packageSize"] = values.PackageSize,
            ["packageSha256"] = values.PackageSha256,
            ["packageSignature"] = values.PackageSignature,
            ["releaseNotes"] = values.ReleaseNotes,
        });

    private static ManifestValues ReadManifestValues(JsonDocument document)
    {
        JsonElement root = document.RootElement;
        return new ManifestValues(
            root.GetProperty("schemaVersion").GetInt32(),
            root.GetProperty("version").GetString()!,
            root.GetProperty("publishedAtUtc").GetString()!,
            root.GetProperty("packageUrl").GetString()!,
            root.GetProperty("packageSize").GetInt64(),
            root.GetProperty("packageSha256").GetString()!,
            root.GetProperty("packageSignature").GetString()!,
            root.GetProperty("releaseNotes").GetString()!);
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static Uri PackageUriForVersion(string version) =>
        new(
            $"https://updates.example.invalid/releases/{version}/" +
            $"GuluPet-{version}-win-x64.zip");

    private static byte[] CreateValidPackage(bool includeNestedFile = false)
    {
        using var destination = new MemoryStream();
        using (var archive = new ZipArchive(
            destination,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            WriteEntry(archive, "GuluPet.exe", "exe");
            WriteEntry(archive, "GuluPet.dll", "dll");
            if (includeNestedFile)
            {
                WriteEntry(archive, "Assets/content.bin", "content");
            }
        }

        return destination.ToArray();
    }

    private static void CreateOwnedOperationWorkspace(string workspace)
    {
        Directory.CreateDirectory(workspace);
        File.WriteAllText(
            Path.Combine(
                workspace,
                ManualUpdateService.OperationWorkspaceMarkerFileName),
            ManualUpdateService.OperationWorkspaceMarkerText,
            Encoding.ASCII);
        File.WriteAllText(
            Path.Combine(
                workspace,
                ManualUpdateService.OperationPackageFileName),
            "partial-package");
        string extraction = Path.Combine(
            workspace,
            ManualUpdateService.OperationStagingDirectoryName);
        Directory.CreateDirectory(extraction);
        File.WriteAllText(
            Path.Combine(extraction, "partial.bin"),
            "partial-extraction");
    }

    private static void CreateDirectoryReparsePoint(
        string linkPath,
        string targetPath)
    {
        try
        {
            _ = Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException
                  or IOException
                  or PlatformNotSupportedException)
        {
            string commandInterpreter = Environment.GetEnvironmentVariable(
                "ComSpec")
                ?? Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.System),
                    "cmd.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = commandInterpreter,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in new[]
            {
                "/d",
                "/c",
                "mklink",
                "/J",
                linkPath,
                targetPath,
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Could not start the junction test helper.");
            process.WaitForExit();
            if (process.ExitCode != 0 || !Directory.Exists(linkPath))
            {
                throw new InvalidOperationException(
                    "Could not create a directory reparse point for the " +
                    "update cleanup test.");
            }
        }
    }

    private static byte[] CreateMaliciousPackage(
        string maliciousPath,
        bool symbolicLink)
    {
        using var destination = new MemoryStream();
        using (var archive = new ZipArchive(
            destination,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            WriteEntry(archive, "GuluPet.exe", "exe");
            WriteEntry(archive, "GuluPet.dll", "dll");
            ZipArchiveEntry entry = WriteEntry(
                archive,
                maliciousPath,
                "malicious");
            if (symbolicLink)
            {
                entry.ExternalAttributes = (0xA000 | 0x1FF) << 16;
            }
        }

        return destination.ToArray();
    }

    private static byte[] CreatePackageWithoutRuntime()
    {
        using var destination = new MemoryStream();
        using (var archive = new ZipArchive(
            destination,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            WriteEntry(archive, "readme.txt", "missing runtime");
        }

        return destination.ToArray();
    }

    private static ZipArchiveEntry WriteEntry(
        ZipArchive archive,
        string path,
        string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(
            path,
            CompressionLevel.NoCompression);
        using Stream stream = entry.Open();
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
        return entry;
    }

    private static async Task<T> ThrowsAsync<T>(Func<Task> action)
        where T : Exception
    {
        try
        {
            await action();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new InvalidOperationException(
            $"Expected exception '{typeof(T).Name}'.");
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(ManualUpdateServiceTests)}.{name}");
    }

    private static void RunAsync(string name, Func<Task> test) =>
        Run(name, () => test().GetAwaiter().GetResult());

    private sealed record ManifestValues(
        int SchemaVersion,
        string Version,
        string PublishedAtUtc,
        string PackageUrl,
        long PackageSize,
        string PackageSha256,
        string PackageSignature,
        string ReleaseNotes);

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> _send;

        public DelegateHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return _send(request, cancellationToken);
        }
    }

    private sealed class DeclaredLengthContent : HttpContent
    {
        private readonly long _length;

        public DeclaredLengthContent(long length)
        {
            _length = length;
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }

    private sealed class NonSeekableMemoryStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableMemoryStream(byte[] contents)
        {
            _inner = new MemoryStream(contents, writable: false);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(Action<T> report)
        {
            _report = report;
        }

        public void Report(T value) => _report(value);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TemporaryLayout : IDisposable
    {
        public TemporaryLayout()
        {
            RootDirectory = Path.Combine(
                Path.GetTempPath(),
                $"GuluPetUpdateTests-{Guid.NewGuid():N}");
            InstallationDirectory = Path.Combine(
                RootDirectory,
                "current");
            StagingDirectory = Path.Combine(
                RootDirectory,
                "staging");
            Directory.CreateDirectory(InstallationDirectory);
        }

        public string RootDirectory { get; }

        public string InstallationDirectory { get; }

        public string StagingDirectory { get; }

        public void Dispose()
        {
            string resolvedRoot = Path.GetFullPath(RootDirectory);
            string expectedPrefix = Path.GetFullPath(Path.GetTempPath());
            if (!resolvedRoot.StartsWith(
                    expectedPrefix,
                    StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolvedRoot).StartsWith(
                    "GuluPetUpdateTests-",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected test directory.");
            }

            if (Directory.Exists(resolvedRoot))
            {
                Directory.Delete(resolvedRoot, recursive: true);
            }
        }
    }
}
