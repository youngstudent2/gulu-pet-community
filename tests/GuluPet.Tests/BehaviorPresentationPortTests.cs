using System.Text.Json;
using System.Windows.Threading;
using GuluPet.Animation;
using GuluPet.Behavior;
using GuluPet.Domain;
using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class BehaviorPresentationPortTests
{
    public static void RunAll()
    {
        Run(
            nameof(SynchronousFirstFrameMapsToLogicalPlayback),
            SynchronousFirstFrameMapsToLogicalPlayback);
        Run(
            nameof(ContinueSameClipTransfersOwnershipWithoutRestart),
            ContinueSameClipTransfersOwnershipWithoutRestart);
        Run(
            nameof(ReentrantInteractionIsDroppedWhileCurrentBehaviorStarts),
            ReentrantInteractionIsDroppedWhileCurrentBehaviorStarts);
    }

    private static void SynchronousFirstFrameMapsToLogicalPlayback()
    {
        using var fixture = CreateFixture();
        var logicalFrames = new List<BehaviorPlaybackToken>();
        fixture.Port.FirstFramePresented += logicalFrames.Add;
        var token = new BehaviorPlaybackToken(41);

        BehaviorTestCheck.True(
            fixture.Port.TryStartPlayback(
                Request(token, continueIfSameClip: false)));
        BehaviorTestCheck.SequenceEqual([token], logicalFrames);
        BehaviorTestCheck.Equal("blink_slow", fixture.Player.CurrentAction);

        fixture.Port.StopPlayback(token);
        BehaviorTestCheck.Null(fixture.Player.CurrentAction);
    }

    private static void ContinueSameClipTransfersOwnershipWithoutRestart()
    {
        using var fixture = CreateFixture();
        var first = new BehaviorPlaybackToken(51);
        var second = new BehaviorPlaybackToken(52);
        var logicalFrames = new List<BehaviorPlaybackToken>();
        fixture.Port.FirstFramePresented += logicalFrames.Add;

        BehaviorTestCheck.True(
            fixture.Port.TryStartPlayback(
                Request(first, continueIfSameClip: false)));
        long physicalToken = fixture.Player.CurrentPlaybackToken;
        BehaviorTestCheck.True(
            fixture.Port.TryStartPlayback(
                Request(second, continueIfSameClip: true)));

        BehaviorTestCheck.Equal(
            physicalToken,
            fixture.Player.CurrentPlaybackToken);
        BehaviorTestCheck.SequenceEqual([first, second], logicalFrames);
        fixture.Port.StopPlayback(first);
        BehaviorTestCheck.Equal("blink_slow", fixture.Player.CurrentAction);
        fixture.Port.StopPlayback(second);
        BehaviorTestCheck.Null(fixture.Player.CurrentAction);
    }

    private static void ReentrantInteractionIsDroppedWhileCurrentBehaviorStarts()
    {
        using var fixture = CreateFixture();
        BehaviorRuntimeCoordinator? coordinator = null;
        BehaviorMutationResult? nested = null;
        bool nestedTriggered = false;
        fixture.Port.FirstFramePresented += playbackToken =>
        {
            BehaviorRuntimeCoordinator runtime =
                BehaviorTestCheck.NotNull(coordinator);
            BehaviorTestCheck.True(
                runtime.NotifyFirstFrame(playbackToken));
            if (nestedTriggered ||
                runtime.Sessions.Snapshot?.BehaviorId !=
                new BehaviorId("petting_nuzzle"))
            {
                return;
            }

            nestedTriggered = true;
            nested = runtime.TriggerClick();
        };

        coordinator = new BehaviorRuntimeCoordinator(
            new BehaviorCatalog(ReentrantDefinitions()),
            fixture.Port,
            new FakeBubblePort());
        coordinator.Start();

        BehaviorMutationResult outer =
            coordinator.TriggerInteractionTag("trigger:petting");
        BehaviorMutationResult nestedResult =
            BehaviorTestCheck.NotNull(nested);

        BehaviorTestCheck.True(outer.Accepted, outer.RejectionReason);
        BehaviorTestCheck.False(nestedResult.Accepted);
        BehaviorTestCheck.Equal(
            "behavior-busy",
            nestedResult.RejectionReason);
        BehaviorTestCheck.Equal(
            new BehaviorId("petting_nuzzle"),
            coordinator.Sessions.Snapshot!.BehaviorId);
        BehaviorTestCheck.Equal(
            "pet_headbutt_step_through",
            fixture.Player.CurrentAction);
        BehaviorInteractionLeaseSnapshot lease =
            BehaviorTestCheck.NotNull(
                coordinator.GetSnapshot().InteractionLease);
        BehaviorTestCheck.Equal("petting", lease.Key);
        BehaviorTestCheck.Equal(
            outer.RequestId!.Value,
            lease.RequestId);
        BehaviorTestCheck.False(
            coordinator.ReadEventsAfter(0).Events.Any(entry =>
                entry.Kind == BehaviorEventKind.SessionTerminated &&
                entry.RequestId == outer.RequestId));

        BehaviorPlaybackToken playback =
            BehaviorTestCheck.NotNull(
                coordinator.Sessions.CurrentPlaybackToken);
        fixture.Port.StopPlayback(playback);
        BehaviorTestCheck.Null(fixture.Player.CurrentAction);
        BehaviorTestCheck.Equal(
            new BehaviorId("petting_nuzzle"),
            coordinator.Sessions.Snapshot!.BehaviorId);

        BehaviorTestCheck.True(
            coordinator.NotifyClipCompleted(playback));
        BehaviorRuntimeEvent terminal = BehaviorTestCheck.NotNull(
            coordinator.ReadEventsAfter(0).Events.LastOrDefault(entry =>
                entry.Kind == BehaviorEventKind.SessionTerminated &&
                entry.RequestId == outer.RequestId));
        BehaviorTestCheck.Equal(
            BehaviorTerminalStatus.Completed,
            terminal.TerminalStatus!.Value);
        BehaviorTestCheck.Equal(
            new BehaviorId("idle_fallback"),
            coordinator.Sessions.Snapshot!.BehaviorId);
    }

    private static Fixture CreateFixture()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.BehaviorPresentation.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            WriteClip(root, "idle_breathe", loop: true);
            WriteClip(root, "pet_headbutt_step_through", loop: false);
            WriteClip(root, "blink_slow", loop: false);
            WriteClip(root, "lick_nose", loop: false);

            AnimationCatalog catalog = AnimationCatalog
                .LoadAsync(root)
                .GetAwaiter()
                .GetResult();
            var player = new AnimationPlayer(
                catalog,
                Dispatcher.CurrentDispatcher);
            var port = new AnimationPlayerBehaviorPort(player, catalog);
            return new Fixture(root, catalog, player, port);
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    private static void WriteClip(string root, string name, bool loop)
    {
        string clipDirectory = Path.Combine(root, name);
        Directory.CreateDirectory(clipDirectory);
        TestAnimatedWebp.Write(Path.Combine(clipDirectory, "animation.webp"));
        File.WriteAllText(
            Path.Combine(clipDirectory, "clip.json"),
            JsonSerializer.Serialize(
                new
                {
                    name,
                    frameCount = TestAnimatedWebp.FrameCount,
                    fps = 24,
                    loop,
                    events = Array.Empty<object>(),
                    placeholder = false,
                    assetStatus = "community-test-fixture",
                    knownIssues = Array.Empty<string>(),
                }));
    }

    private static BehaviorPlaybackRequest Request(
        BehaviorPlaybackToken token,
        bool continueIfSameClip) =>
        new()
        {
            SessionToken = new BehaviorSessionToken(1),
            PlaybackToken = token,
            Role = BehaviorClipRole.Perform,
            ClipId = "blink_slow",
            ContinueIfSameClip = continueIfSameClip,
        };

    private static IReadOnlyList<BehaviorDefinition> ReentrantDefinitions() =>
    [
        Definition(
            "idle_fallback",
            "idle_breathe",
            ["state:fallback"],
            queueable: false,
            fallback: true,
            completion: new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.External,
            }),
        Definition(
            "petting_nuzzle",
            "pet_headbutt_step_through",
            ["trigger:petting"]),
        Definition(
            "click_blink",
            "blink_slow",
            ["trigger:click"],
            queueable: false,
            completion: new BehaviorCompletionPolicy
            {
                Kind = BehaviorCompletionKind.External,
            }),
        Definition(
            "safe_micro_feedback",
            "lick_nose",
            ["fallback:interaction"]),
    ];

    private static BehaviorDefinition Definition(
        string id,
        string clip,
        IReadOnlyList<string> tags,
        bool queueable = true,
        bool fallback = false,
        BehaviorCompletionPolicy? completion = null) =>
        new()
        {
            Id = new BehaviorId(id),
            DisplayName = id,
            Family = id,
            ClipFamily = clip,
            Tags = tags,
            AllowedStates = [StablePetState.Normal],
            Animation = fallback
                ? new BehaviorAnimationPlan { LoopClipId = clip }
                : new BehaviorAnimationPlan { PerformClipId = clip },
            Completion = completion ??
                         new BehaviorCompletionPolicy
                         {
                             Kind = BehaviorCompletionKind.ClipEnd,
                         },
            MaximumDuration = fallback
                ? TimeSpan.FromDays(1)
                : TimeSpan.FromSeconds(10),
            Utility = new BehaviorUtilityRule
            {
                BaseScore = 50,
                MaxConsecutiveRuns = 3,
            },
            Queueable = queueable,
            IsStateFallback = fallback,
        };

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(BehaviorPresentationPortTests)}.{name}");
    }

    private sealed class Fixture(
        string root,
        AnimationCatalog catalog,
        AnimationPlayer player,
        AnimationPlayerBehaviorPort port) : IDisposable
    {
        public AnimationPlayer Player { get; } = player;

        public AnimationPlayerBehaviorPort Port { get; } = port;

        public void Dispose()
        {
            Port.Dispose();
            Player.Dispose();
            catalog.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeBubblePort : IBehaviorBubbleSessionPort
    {
        public void Show(BehaviorBubblePresentation presentation)
        {
        }

        public void Hide(BehaviorSessionToken ownerSessionToken)
        {
        }
    }
}
