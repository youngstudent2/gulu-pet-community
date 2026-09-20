using System.Text;
using GuluPet.Persistence;

namespace GuluPet.Tests;

internal static class LogReportCredentialStoreTests
{
    public static void RunAll()
    {
        Run(nameof(MissingCredentialReturnsNull), MissingCredentialReturnsNull);
        Run(
            nameof(CredentialRoundTripsAsDpapiCiphertext),
            CredentialRoundTripsAsDpapiCiphertext);
        Run(
            nameof(CorruptCiphertextFailsClosed),
            CorruptCiphertextFailsClosed);
        Run(
            nameof(ConcurrentStoresCreateOneInstallationIdentity),
            ConcurrentStoresCreateOneInstallationIdentity);
    }

    private static void MissingCredentialReturnsNull()
    {
        WithTemporaryStore(
            store => BehaviorTestCheck.Null(store.Load()));
    }

    private static void CredentialRoundTripsAsDpapiCiphertext()
    {
        WithTemporaryStore(
            store =>
            {
                var credential = new LogReportCredential(
                    Guid.Parse("f885247e-a539-4ee3-9d78-a865069f42f1"),
                    "log_report_token_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ");

                store.Save(credential);

                BehaviorTestCheck.Equal(credential, store.Load()!);
                byte[] persisted = File.ReadAllBytes(store.CredentialPath);
                string persistedText = Encoding.UTF8.GetString(persisted);
                BehaviorTestCheck.True(persisted.Length > 0);
                BehaviorTestCheck.False(
                    persistedText.Contains(
                        credential.Token,
                        StringComparison.Ordinal));
                BehaviorTestCheck.False(
                    persistedText.Contains(
                        credential.InstallationId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase));
                BehaviorTestCheck.False(
                    persistedText.TrimStart().StartsWith(
                        "{",
                        StringComparison.Ordinal));
            });
    }

    private static void CorruptCiphertextFailsClosed()
    {
        WithTemporaryStore(
            store =>
            {
                store.Save(new LogReportCredential(
                    Guid.Parse("f2d7c720-216c-4798-87f1-d9a08240e659"),
                    "log_report_token_ABCDEFGHIJKLMNOPQRSTUVWXYZ_9876543210"));
                byte[] ciphertext = File.ReadAllBytes(store.CredentialPath);
                ciphertext[ciphertext.Length / 2] ^= 0x5a;
                File.WriteAllBytes(store.CredentialPath, ciphertext);

                _ = BehaviorTestCheck.Throws<InvalidDataException>(
                    () => store.Load());
            });
    }

    private static void ConcurrentStoresCreateOneInstallationIdentity()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.LogReportCredential.Concurrent.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "credential.bin");
            LogReportCredential[] credentials = Enumerable.Range(0, 100)
                .AsParallel()
                .WithDegreeOfParallelism(16)
                .Select(_ => new LogReportCredentialStore(path)
                    .GetOrCreateRegistrationIntent())
                .ToArray();

            BehaviorTestCheck.Equal(
                1,
                credentials.Select(static value => value.InstallationId)
                    .Distinct()
                    .Count());
            BehaviorTestCheck.Equal(
                1,
                credentials.Select(static value => value.Token)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            BehaviorTestCheck.Equal(
                credentials[0],
                new LogReportCredentialStore(path).Load()!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WithTemporaryStore(
        Action<LogReportCredentialStore> test)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.LogReportCredential.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            test(new LogReportCredentialStore(
                Path.Combine(root, "credential.bin")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(LogReportCredentialStoreTests)}.{name}");
    }
}
