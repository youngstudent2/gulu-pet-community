using System.Security.Cryptography;
using System.Text;
using GuluPet.Persistence;

namespace GuluPet.Tests;

internal static class PendingErrorReportStoreTests
{
    public static void RunAll()
    {
        Run(nameof(MissingReportReturnsNull), MissingReportReturnsNull);
        Run(
            nameof(ReportRoundTripsAsDpapiCiphertext),
            ReportRoundTripsAsDpapiCiphertext);
        Run(nameof(InvalidHashFailsClosed), InvalidHashFailsClosed);
        Run(
            nameof(CorruptCiphertextFailsClosed),
            CorruptCiphertextFailsClosed);
    }

    private static void MissingReportReturnsNull()
    {
        WithTemporaryStore(
            store => BehaviorTestCheck.Null(store.Load()));
    }

    private static void ReportRoundTripsAsDpapiCiphertext()
    {
        WithTemporaryStore(
            store =>
            {
                byte[] body = Encoding.UTF8.GetBytes(
                    "{\"sentinel\":\"pending-report-plaintext\"}");
                var report = CreateReport(body);

                store.Save(report);

                PendingErrorReport loaded = store.Load()
                    ?? throw new InvalidOperationException(
                        "The pending report was not loaded.");
                BehaviorTestCheck.Equal(report.InstallationId, loaded.InstallationId);
                BehaviorTestCheck.Equal(report.ReportId, loaded.ReportId);
                BehaviorTestCheck.Equal(report.CreatedAtUtc, loaded.CreatedAtUtc);
                BehaviorTestCheck.Equal(report.ContentSha256, loaded.ContentSha256);
                BehaviorTestCheck.True(report.Body.SequenceEqual(loaded.Body));

                byte[] ciphertext = File.ReadAllBytes(store.PendingPath);
                string persistedText = Encoding.UTF8.GetString(ciphertext);
                BehaviorTestCheck.False(
                    persistedText.Contains(
                        "pending-report-plaintext",
                        StringComparison.Ordinal));
                BehaviorTestCheck.False(
                    persistedText.Contains(
                        report.ReportId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase));

                store.Clear();
                BehaviorTestCheck.Null(store.Load());
            });
    }

    private static void InvalidHashFailsClosed()
    {
        WithTemporaryStore(
            store =>
            {
                PendingErrorReport report = CreateReport([1, 2, 3]);
                _ = BehaviorTestCheck.Throws<InvalidDataException>(
                    () => store.Save(report with
                    {
                        ContentSha256 = new string('0', 64),
                    }));
                BehaviorTestCheck.Null(store.Load());
            });
    }

    private static void CorruptCiphertextFailsClosed()
    {
        WithTemporaryStore(
            store =>
            {
                store.Save(CreateReport([4, 5, 6]));
                byte[] ciphertext = File.ReadAllBytes(store.PendingPath);
                ciphertext[ciphertext.Length / 2] ^= 0x5a;
                File.WriteAllBytes(store.PendingPath, ciphertext);

                _ = BehaviorTestCheck.Throws<InvalidDataException>(
                    () => store.Load());
            });
    }

    private static PendingErrorReport CreateReport(byte[] body) =>
        new(
            Guid.Parse("77f27ca8-80fb-4d85-9f94-c57a1c5a1dce"),
            Guid.Parse("329b4e25-90f4-4098-bc60-c4e76e666fed"),
            new DateTimeOffset(2026, 8, 3, 10, 30, 0, TimeSpan.Zero),
            Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
            body,
            [new ReportedErrorEventReceipt(
                Guid.Parse("1b880010-fc6a-4717-a42b-198b24473944"),
                new DateTimeOffset(
                    2026,
                    8,
                    3,
                    10,
                    20,
                    0,
                    TimeSpan.Zero))]);

    private static void WithTemporaryStore(
        Action<PendingErrorReportStore> test)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.PendingErrorReport.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            test(new PendingErrorReportStore(
                Path.Combine(root, "pending.bin")));
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
            $"PASS {nameof(PendingErrorReportStoreTests)}.{name}");
    }
}
