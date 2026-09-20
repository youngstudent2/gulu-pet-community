using System.IO;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuluPet.Persistence;

public sealed record LogReportCredential(
    Guid InstallationId,
    string Token,
    bool RegistrationConfirmed = true);

/// <summary>
/// Persists the per-installation log-report credential as DPAPI CurrentUser
/// ciphertext. The bearer token never enters settings.json or local logs.
/// </summary>
public sealed class LogReportCredentialStore
{
    private const string FileName = "log-report-credential.bin";
    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("GuluPet.LogReport.Credential.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, object> PathGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate;

    public LogReportCredentialStore()
        : this(Path.Combine(
            global::GuluPet.AppIdentity.LocalDataDirectory,
            FileName))
    {
    }

    public LogReportCredentialStore(string credentialPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialPath);
        CredentialPath = Path.GetFullPath(credentialPath);
        _gate = PathGates.GetOrAdd(CredentialPath, static _ => new object());
    }

    public string CredentialPath { get; }

    public LogReportCredential? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(CredentialPath))
            {
                return null;
            }

            byte[] ciphertext = File.ReadAllBytes(CredentialPath);
            byte[]? plaintext = null;
            try
            {
                plaintext = ProtectedData.Unprotect(
                    ciphertext,
                    OptionalEntropy,
                    DataProtectionScope.CurrentUser);
                LogReportCredential? credential =
                    JsonSerializer.Deserialize<LogReportCredential>(
                        plaintext,
                        JsonOptions);
                Validate(credential);
                return credential;
            }
            catch (Exception exception)
                when (exception is CryptographicException or JsonException)
            {
                throw new InvalidDataException(
                    "The encrypted log-report credential is invalid for " +
                    "the current Windows user.",
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

    public void Save(LogReportCredential credential)
    {
        Validate(credential);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(
            credential,
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

    /// <summary>
    /// Returns the existing installation identity or durably creates the
    /// exact registration intent used by both diagnostics and diary calls.
    /// Stores targeting the same path share a process-wide lock so concurrent
    /// first use cannot create two different installation identities.
    /// </summary>
    public LogReportCredential GetOrCreateRegistrationIntent()
    {
        lock (_gate)
        {
            LogReportCredential? existing = Load();
            if (existing is not null)
            {
                return existing;
            }

            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            try
            {
                var proposed = new LogReportCredential(
                    Guid.NewGuid(),
                    Convert.ToBase64String(bytes)
                        .TrimEnd('=')
                        .Replace('+', '-')
                        .Replace('/', '_'),
                    RegistrationConfirmed: false);
                Save(proposed);
                return proposed;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(CredentialPath))
            {
                File.Delete(CredentialPath);
            }
        }
    }

    private void SaveCiphertext(byte[] ciphertext)
    {
        lock (_gate)
        {
            string directory = Path.GetDirectoryName(CredentialPath)
                ?? throw new InvalidOperationException(
                    "The credential path has no parent directory.");
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(CredentialPath)}.{Guid.NewGuid():N}.tmp");
            string backupPath = Path.Combine(
                directory,
                ".log-report-credential.backup.bin");
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

                if (File.Exists(CredentialPath))
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    File.Replace(
                        temporaryPath,
                        CredentialPath,
                        backupPath,
                        ignoreMetadataErrors: true);
                    File.Delete(backupPath);
                }
                else
                {
                    File.Move(temporaryPath, CredentialPath);
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

    private static void Validate(LogReportCredential? credential)
    {
        if (credential is null
            || credential.InstallationId == Guid.Empty
            || string.IsNullOrWhiteSpace(credential.Token)
            || credential.Token.Length is < 32 or > 128
            || credential.Token.Any(character =>
                !char.IsLetterOrDigit(character)
                && character is not '-' and not '_'))
        {
            throw new InvalidDataException(
                "The log-report credential is malformed.");
        }
    }
}
