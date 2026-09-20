using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuluPet.Persistence;

public sealed record PendingErrorReport(
    Guid InstallationId,
    Guid ReportId,
    DateTimeOffset CreatedAtUtc,
    string ContentSha256,
    byte[] Body,
    IReadOnlyList<ReportedErrorEventReceipt> Receipts);

/// <summary>
/// Keeps the exact bytes of an ambiguous in-flight report under DPAPI until a
/// 202 response is observed. Retrying those same bytes makes the server's
/// idempotency key meaningful when the original response was lost.
/// </summary>
public sealed class PendingErrorReportStore
{
    private const string FileName = "pending-error-report.bin";
    private const int MaximumBodyBytes = 1024 * 1024;
    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("GuluPet.ErrorReport.Pending.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web);
    private readonly object _gate = new();

    public PendingErrorReportStore()
        : this(Path.Combine(
            global::GuluPet.AppIdentity.LocalDataDirectory,
            FileName))
    {
    }

    public PendingErrorReportStore(string pendingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pendingPath);
        PendingPath = Path.GetFullPath(pendingPath);
    }

    public string PendingPath { get; }

    public PendingErrorReport? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(PendingPath))
            {
                return null;
            }

            byte[] ciphertext = File.ReadAllBytes(PendingPath);
            byte[]? plaintext = null;
            try
            {
                plaintext = ProtectedData.Unprotect(
                    ciphertext,
                    OptionalEntropy,
                    DataProtectionScope.CurrentUser);
                PendingErrorReport? report =
                    JsonSerializer.Deserialize<PendingErrorReport>(
                        plaintext,
                        JsonOptions);
                Validate(report);
                return report! with
                {
                    Body = report!.Body.ToArray(),
                    Receipts = report.Receipts.ToArray(),
                };
            }
            catch (Exception exception)
                when (exception is CryptographicException or JsonException)
            {
                throw new InvalidDataException(
                    "The encrypted pending error report is invalid for the " +
                    "current Windows user.",
                    exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(ciphertext);
                if (plaintext is not null)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
        }
    }

    public void Save(PendingErrorReport report)
    {
        Validate(report);
        PendingErrorReport snapshot = report with
        {
            Body = report.Body.ToArray(),
            Receipts = report.Receipts.ToArray(),
        };
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(
            snapshot,
            JsonOptions);
        byte[]? ciphertext = null;
        try
        {
            ciphertext = ProtectedData.Protect(
                plaintext,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
            SaveCiphertext(ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (ciphertext is not null)
            {
                CryptographicOperations.ZeroMemory(ciphertext);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(PendingPath))
            {
                File.Delete(PendingPath);
            }
        }
    }

    private void SaveCiphertext(byte[] ciphertext)
    {
        lock (_gate)
        {
            string directory = Path.GetDirectoryName(PendingPath)
                ?? throw new InvalidOperationException(
                    "The pending report path has no parent directory.");
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(PendingPath)}.{Guid.NewGuid():N}.tmp");
            string backupPath = Path.Combine(
                directory,
                ".pending-error-report.backup.bin");
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(ciphertext);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(PendingPath))
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    File.Replace(
                        temporaryPath,
                        PendingPath,
                        backupPath,
                        ignoreMetadataErrors: true);
                    File.Delete(backupPath);
                }
                else
                {
                    File.Move(temporaryPath, PendingPath);
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

    private static void Validate(PendingErrorReport? report)
    {
        if (report is null
            || report.InstallationId == Guid.Empty
            || report.ReportId == Guid.Empty
            || report.CreatedAtUtc == default
            || report.Body is null
            || report.Body.Length is < 1 or > MaximumBodyBytes
            || report.Receipts is null
            || report.Receipts.Count > 500
            || report.Receipts.Any(receipt =>
                receipt is null
                || receipt.EventId == Guid.Empty
                || receipt.OccurredAtUtc == default)
            || report.Receipts.Select(receipt => receipt.EventId)
                .Distinct()
                .Count() != report.Receipts.Count
            || string.IsNullOrWhiteSpace(report.ContentSha256)
            || report.ContentSha256.Length != 64
            || !report.ContentSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "The pending error report is malformed.");
        }

        string actualHash = Convert.ToHexString(
                SHA256.HashData(report.Body))
            .ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actualHash),
                Encoding.ASCII.GetBytes(
                    report.ContentSha256.ToLowerInvariant())))
        {
            throw new InvalidDataException(
                "The pending error report hash does not match its body.");
        }
    }
}
