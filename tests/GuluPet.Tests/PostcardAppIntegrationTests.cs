using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class PostcardAppIntegrationTests
{
    public static void RunAll()
    {
        Run(
            nameof(TestStatusPreservesControllerSnapshot),
            TestStatusPreservesControllerSnapshot);
    }

    private static void TestStatusPreservesControllerSnapshot()
    {
        DateTimeOffset capturedAt =
            new(2026, 7, 28, 14, 30, 0, TimeSpan.Zero);
        var snapshot = new PostcardFeatureSnapshot(
            capturedAt,
            TotalInputCount: 2_450,
            InputsUntilNextUnlock: 550,
            AvailablePostcardCount: 3,
            UnlockedPostcardIds:
            [
                "great_wall",
                "canton_tower",
            ],
            UnacknowledgedPostcardIds: ["canton_tower"],
            PopupVisible: true,
            PopupPostcardId: "canton_tower",
            GalleryVisible: true,
            GalleryPostcardId: "great_wall")
        {
            OutingPhase = GuluPet.Postcards.PostcardOutingPhase.Traveling,
            OutingStartedAtUtc = capturedAt,
            OutingReadyAtUtc = capturedAt.AddMinutes(30),
            OutingRemainingSeconds = 1_800,
            NextPostcardId = "shanghai_bund",
        };

        GuluPet.Testing.TestPostcardStatus status =
            App.CreateTestPostcardStatus(snapshot);

        BehaviorTestCheck.Equal(capturedAt, status.CapturedAtUtc);
        BehaviorTestCheck.Equal("traveling", status.OutingPhase);
        BehaviorTestCheck.Equal(
            capturedAt,
            status.OutingStartedAtUtc!.Value);
        BehaviorTestCheck.Equal(
            capturedAt.AddMinutes(30),
            status.OutingReadyAtUtc!.Value);
        BehaviorTestCheck.Equal(
            1_800L,
            status.OutingRemainingSeconds);
        BehaviorTestCheck.Equal(
            "shanghai_bund",
            status.NextPostcardId);
        BehaviorTestCheck.Equal(3, status.AvailablePostcardCount);
        BehaviorTestCheck.SequenceEqual(
            new[] { "great_wall", "canton_tower" },
            status.UnlockedPostcardIds);
        BehaviorTestCheck.SequenceEqual(
            new[] { "canton_tower" },
            status.UnacknowledgedPostcardIds);
        BehaviorTestCheck.True(status.PopupVisible);
        BehaviorTestCheck.Equal(
            "canton_tower",
            status.PopupPostcardId);
        BehaviorTestCheck.True(status.GalleryVisible);
        BehaviorTestCheck.Equal(
            "great_wall",
            status.GalleryPostcardId);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(PostcardAppIntegrationTests)}.{name}");
    }
}
