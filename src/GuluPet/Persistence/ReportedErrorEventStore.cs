using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuluPet.Persistence;

public sealed record ReportedErrorEventReceipt(
    Guid EventId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Persists the identifiers of error events already acknowledged by the log
/// ingest service. Receipts are encrypted for the current Windows user and are
/// bounded to the period in which local error events can still be uploaded.
/// </summary>
public sealed class ReportedErrorEventStore
{
    private const string FileName = "reported-error-events.bin";
    private const int SchemaVersion = 1;
    private const int DefaultMaximumReceipts = 20_000;
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);
    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("GuluPet.ErrorReport.ReportedEvents.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;
    private readonly int _maximumReceipts;

    public ReportedErrorEventStore()
        : this(Path.Combine(
            global::GuluPet.AppIdentity.LocalDataDirectory,
            FileName))
    {
    }

    public ReportedErrorEventStore(
        string receiptPath,
        TimeProvider? timeProvider = null,
        TimeSpan? retention = null,
        int maximumReceipts = DefaultMaximumReceipts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptPath);
        ReceiptPath = Path.GetFullPath(receiptPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _retention = retention ?? DefaultRetention;
        _maximumReceipts = maximumReceipts;

        if (_retention <= TimeSpan.Zero || _retention > TimeSpan.FromDays(365))
        {
            throw new ArgumentOutOfRangeException(
                nameof(retention),
                "Receipt retention must be between zero and 365 days.");
        }

        if (_maximumReceipts is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumReceipts),
                "The receipt limit must be between 1 and 100000.");
        }
    }

    public string ReceiptPath { get; }

    public IReadOnlyList<ReportedErrorEventReceipt> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(ReceiptPath))
            {
                return [];
            }

            IReadOnlyList<ReportedErrorEventReceipt> stored = LoadCore();
            ReportedErrorEventReceipt[] pruned = Prune(stored);
            if (!ReceiptsEqual(stored, pruned))
            {
                SaveCore(pruned);
            }

            return pruned;
        }
    }

    public IReadOnlySet<Guid> LoadEventIds() =>
        Load()
            .Select(static receipt => receipt.EventId)
            .ToHashSet();

    public void RecordReported(
        IEnumerable<ReportedErrorEventReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);

        ReportedErrorEventReceipt[] additions = receipts.ToArray();
        ValidateReceipts(additions);

        lock (_gate)
        {
            IReadOnlyList<ReportedErrorEventReceipt> stored =
                File.Exists(ReceiptPath) ? LoadCore() : [];
            ReportedErrorEventReceipt[] merged = Prune(
                stored.Concat(additions));
            SaveCore(merged);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(ReceiptPath))
            {
                File.Delete(ReceiptPath);
            }
        }
    }

    private IReadOnlyList<ReportedErrorEventReceipt> LoadCore()
    {
        byte[] ciphertext = File.ReadAllBytes(ReceiptPath);
        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(
                ciphertext,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
            ReceiptEnvelope? envelope = JsonSerializer.Deserialize<ReceiptEnvelope>(
                plaintext,
                JsonOptions);
            if (envelope is null
                || envelope.SchemaVersion != SchemaVersion
                || envelope.Receipts is null)
            {
                throw new InvalidDataException(
                    "The reported error-event receipt file is malformed.");
            }

            ValidateReceipts(envelope.Receipts);
            return envelope.Receipts.ToArray();
        }
        catch (Exception exception)
            when (exception is CryptographicException or JsonException)
        {
            throw new InvalidDataException(
                "The encrypted reported error-event receipts are invalid " +
                "for the current Windows user.",
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

    private void SaveCore(
        IReadOnlyList<ReportedErrorEventReceipt> receipts)
    {
        ValidateReceipts(receipts);
        var envelope = new ReceiptEnvelope
        {
            SchemaVersion = SchemaVersion,
            Receipts = receipts.ToArray(),
        };
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            JsonOptions);
        byte[]? ciphertext = null;
        try
        {
            ciphertext = ProtectedData.Protect(
                plaintext,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
            SaveCiphertextCore(ciphertext);
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

    private void SaveCiphertextCore(byte[] ciphertext)
    {
        string directory = Path.GetDirectoryName(ReceiptPath)
            ?? throw new InvalidOperationException(
                "The reported event receipt path has no parent directory.");
        Directory.CreateDirectory(directory);
        string fileName = Path.GetFileName(ReceiptPath);
        string temporaryPath = Path.Combine(
            directory,
            $".{fileName}.{Guid.NewGuid():N}.tmp");
        string backupPath = Path.Combine(
            directory,
            $".{fileName}.backup");
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

            if (File.Exists(ReceiptPath))
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Replace(
                    temporaryPath,
                    ReceiptPath,
                    backupPath,
                    ignoreMetadataErrors: true);
                File.Delete(backupPath);
            }
            else
            {
                File.Move(temporaryPath, ReceiptPath);
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

    private ReportedErrorEventReceipt[] Prune(
        IEnumerable<ReportedErrorEventReceipt> receipts)
    {
        DateTimeOffset cutoff = _timeProvider.GetUtcNow() - _retention;
        return receipts
            .Where(receipt => receipt.OccurredAtUtc >= cutoff)
            .GroupBy(static receipt => receipt.EventId)
            .Select(static group => group
                .OrderByDescending(static receipt => receipt.OccurredAtUtc)
                .First())
            .OrderByDescending(static receipt => receipt.OccurredAtUtc)
            .ThenBy(static receipt => receipt.EventId)
            .Take(_maximumReceipts)
            .OrderBy(static receipt => receipt.OccurredAtUtc)
            .ThenBy(static receipt => receipt.EventId)
            .ToArray();
    }

    private static void ValidateReceipts(
        IEnumerable<ReportedErrorEventReceipt>? receipts)
    {
        if (receipts is null)
        {
            throw new InvalidDataException(
                "The reported error-event receipts are missing.");
        }

        foreach (ReportedErrorEventReceipt? receipt in receipts)
        {
            if (receipt is null
                || receipt.EventId == Guid.Empty
                || receipt.OccurredAtUtc == default)
            {
                throw new InvalidDataException(
                    "A reported error-event receipt is malformed.");
            }
        }
    }

    private static bool ReceiptsEqual(
        IReadOnlyList<ReportedErrorEventReceipt> left,
        IReadOnlyList<ReportedErrorEventReceipt> right) =>
        left.Count == right.Count && left.SequenceEqual(right);

    private sealed record ReceiptEnvelope
    {
        public int SchemaVersion { get; init; }

        public IReadOnlyList<ReportedErrorEventReceipt>? Receipts { get; init; }
    }
}
