using System.Text;
using GuluPet.Persistence;

namespace GuluPet.Tests;

internal static class ReportedErrorEventStoreTests
{
    public static void RunAll()
    {
        Run(nameof(MissingStoreReturnsEmpty), MissingStoreReturnsEmpty);
        Run(
            nameof(ReceiptsRoundTripAsDpapiCiphertext),
            ReceiptsRoundTripAsDpapiCiphertext);
        Run(
            nameof(RecordingMergesDeduplicatesPrunesAndLimits),
            RecordingMergesDeduplicatesPrunesAndLimits);
        Run(
            nameof(CorruptCiphertextFailsClosed),
            CorruptCiphertextFailsClosed);
    }

    private static void MissingStoreReturnsEmpty()
    {
        WithTemporaryStore(
            (store, _) =>
            {
                BehaviorTestCheck.Equal(0, store.Load().Count);
                BehaviorTestCheck.Equal(0, store.LoadEventIds().Count);
            });
    }

    private static void ReceiptsRoundTripAsDpapiCiphertext()
    {
        WithTemporaryStore(
            (store, _) =>
            {
                var receipt = new ReportedErrorEventReceipt(
                    Guid.Parse("cd6a0df8-ae2c-40f0-821b-0ac18a07d480"),
                    new DateTimeOffset(
                        2026,
                        8,
                        4,
                        9,
                        30,
                        0,
                        TimeSpan.Zero));

                store.RecordReported([receipt]);

                ReportedErrorEventReceipt loaded = store.Load().Single();
                BehaviorTestCheck.Equal(receipt, loaded);
                BehaviorTestCheck.True(
                    store.LoadEventIds().Contains(receipt.EventId));

                byte[] ciphertext = File.ReadAllBytes(store.ReceiptPath);
                string persistedText = Encoding.UTF8.GetString(ciphertext);
                BehaviorTestCheck.False(persistedText.Contains(
                    receipt.EventId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase));
                BehaviorTestCheck.False(persistedText.Contains(
                    "2026-08-04",
                    StringComparison.Ordinal));

                store.Clear();
                BehaviorTestCheck.Equal(0, store.Load().Count);
            });
    }

    private static void RecordingMergesDeduplicatesPrunesAndLimits()
    {
        WithTemporaryStore(
            (store, clock) =>
            {
                Guid duplicateId = Guid.Parse(
                    "286d83cc-6ba2-44df-8ca4-8a6c1080e0ab");
                var expired = new ReportedErrorEventReceipt(
                    Guid.Parse("fb1d7792-1356-4808-8dc8-8cf79f31bb21"),
                    clock.GetUtcNow().AddDays(-8));
                var duplicateOlder = new ReportedErrorEventReceipt(
                    duplicateId,
                    clock.GetUtcNow().AddHours(-5));
                var duplicateNewer = new ReportedErrorEventReceipt(
                    duplicateId,
                    clock.GetUtcNow().AddHours(-1));
                var limitedOut = new ReportedErrorEventReceipt(
                    Guid.Parse("d1fba8e3-f66b-4e56-be6a-bf1fa25861ae"),
                    clock.GetUtcNow().AddHours(-4));
                var retainedSecond = new ReportedErrorEventReceipt(
                    Guid.Parse("dfe5d026-0eae-4a23-8944-14f6969362df"),
                    clock.GetUtcNow().AddHours(-3));
                var retainedThird = new ReportedErrorEventReceipt(
                    Guid.Parse("8ef05354-2f9e-4cd9-b1e0-715d25a28599"),
                    clock.GetUtcNow().AddHours(-2));

                store.RecordReported(
                    [expired, duplicateOlder, limitedOut, retainedSecond]);
                store.RecordReported([duplicateNewer, retainedThird]);

                IReadOnlyList<ReportedErrorEventReceipt> loaded = store.Load();
                BehaviorTestCheck.Equal(3, loaded.Count);
                BehaviorTestCheck.False(
                    loaded.Any(receipt => receipt.EventId == expired.EventId));
                BehaviorTestCheck.False(
                    loaded.Any(receipt => receipt.EventId == limitedOut.EventId));
                BehaviorTestCheck.Equal(
                    duplicateNewer,
                    loaded.Single(receipt => receipt.EventId == duplicateId));
                BehaviorTestCheck.True(
                    loaded.Any(receipt =>
                        receipt.EventId == retainedSecond.EventId));
                BehaviorTestCheck.True(
                    loaded.Any(receipt =>
                        receipt.EventId == retainedThird.EventId));

                string directory = Path.GetDirectoryName(store.ReceiptPath)!;
                BehaviorTestCheck.Equal(
                    0,
                    Directory.GetFiles(directory, "*.tmp").Length);
                BehaviorTestCheck.Equal(
                    0,
                    Directory.GetFiles(directory, "*.backup").Length);
            },
            retention: TimeSpan.FromDays(7),
            maximumReceipts: 3);
    }

    private static void CorruptCiphertextFailsClosed()
    {
        WithTemporaryStore(
            (store, clock) =>
            {
                store.RecordReported(
                    [new ReportedErrorEventReceipt(
                        Guid.Parse("b34a45b1-a0ad-481b-a7b9-1ecae408241f"),
                        clock.GetUtcNow())]);
                byte[] ciphertext = File.ReadAllBytes(store.ReceiptPath);
                ciphertext[ciphertext.Length / 2] ^= 0x5a;
                File.WriteAllBytes(store.ReceiptPath, ciphertext);

                _ = BehaviorTestCheck.Throws<InvalidDataException>(
                    () => store.Load());
            });
    }

    private static void WithTemporaryStore(
        Action<ReportedErrorEventStore, FixedTimeProvider> test,
        TimeSpan? retention = null,
        int maximumReceipts = 20_000)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.ReportedErrorEvents.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
        try
        {
            test(
                new ReportedErrorEventStore(
                    Path.Combine(root, "receipts.bin"),
                    clock,
                    retention,
                    maximumReceipts),
                clock);
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
            $"PASS {nameof(ReportedErrorEventStoreTests)}.{name}");
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }
}
