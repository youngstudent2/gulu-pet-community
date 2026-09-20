using System.Text.Json;
using System.IO.Pipes;
using GuluPet.Testing;

namespace GuluPet.Tests;

internal static class TestControlProtocolTests
{
    public static void RunAll()
    {
        Run(nameof(RequestRoundTripsAsCamelCaseJson), RequestRoundTripsAsCamelCaseJson);
        Run(nameof(BehaviorRequestFieldsRoundTripAsCamelCaseJson), BehaviorRequestFieldsRoundTripAsCamelCaseJson);
        Run(nameof(PostcardRequestFieldsRoundTripAsCamelCaseJson), PostcardRequestFieldsRoundTripAsCamelCaseJson);
        Run(nameof(ResponseSnapshotRoundTripsWithoutLosingDiagnostics), ResponseSnapshotRoundTripsWithoutLosingDiagnostics);
        Run(nameof(BehaviorContractsRoundTripAsPureWireDtos), BehaviorContractsRoundTripAsPureWireDtos);
        Run(nameof(PostcardContractRoundTripsAsPureWireDto), PostcardContractRoundTripsAsPureWireDto);
        Run(nameof(MemoryContractRoundTripsAsPureWireDto), MemoryContractRoundTripsAsPureWireDto);
        Run(nameof(OptionalResponseFieldsAreOmitted), OptionalResponseFieldsAreOmitted);
        Run(nameof(ValidationAcceptsEveryWhitelistedCommand), ValidationAcceptsEveryWhitelistedCommand);
        Run(nameof(ValidationRejectsInvalidRequestsWithStableCodes), ValidationRejectsInvalidRequestsWithStableCodes);
        Run(nameof(CommandAndErrorContractsStayStable), CommandAndErrorContractsStayStable);
        Run(nameof(MalformedJsonIsRejected), MalformedJsonIsRejected);
        Run(nameof(WireRoundTripsNewlineTerminatedJson), WireRoundTripsNewlineTerminatedJson);
        Run(nameof(WireRejectsTruncatedAndOversizedMessages), WireRejectsTruncatedAndOversizedMessages);
        Run(nameof(ClientCommunicatesOverAnIsolatedCurrentUserPipe), ClientCommunicatesOverAnIsolatedCurrentUserPipe);
    }

    private static void RequestRoundTripsAsCamelCaseJson()
    {
        var original = new TestControlRequest
        {
            Command = TestControlCommands.Play,
            Action = "blink",
        };

        string json = TestControlJson.Serialize(original);
        TestControlRequest roundTripped =
            TestControlJson.Deserialize<TestControlRequest>(json);

        Check.Contains("\"protocolVersion\":1", json);
        Check.Contains("\"command\":\"play\"", json);
        Check.Contains("\"action\":\"blink\"", json);
        Check.DoesNotContain("\"ProtocolVersion\"", json);
        Check.Equal(TestControlProtocol.Version, roundTripped.ProtocolVersion);
        Check.Equal(TestControlCommands.Play, roundTripped.Command);
        Check.Equal("blink", roundTripped.Action!);

        TestControlRequest caseInsensitive =
            TestControlJson.Deserialize<TestControlRequest>(
                """{"PROTOCOLVERSION":1,"COMMAND":"status"}""");
        Check.Equal(TestControlCommands.Status, caseInsensitive.Command);
    }

    private static void BehaviorRequestFieldsRoundTripAsCamelCaseJson()
    {
        var requests = new[]
        {
            new TestControlRequest
            {
                Command = TestControlCommands.Enqueue,
                BehaviorId = "slow_blink",
            },
            new TestControlRequest
            {
                Command = TestControlCommands.BehaviorTick,
                Seed = 2_147_483_647,
            },
            new TestControlRequest
            {
                Command = TestControlCommands.BehaviorEvents,
                After = 9_223_372_036_854_775_000,
            },
        };

        string behaviorIdJson = TestControlJson.Serialize(requests[0]);
        string seedJson = TestControlJson.Serialize(requests[1]);
        string afterJson = TestControlJson.Serialize(requests[2]);

        Check.Contains("\"behaviorId\":\"slow_blink\"", behaviorIdJson);
        Check.Contains("\"seed\":2147483647", seedJson);
        Check.Contains("\"after\":9223372036854775000", afterJson);
        Check.DoesNotContain("\"BehaviorId\"", behaviorIdJson);
        Check.Equal(
            "slow_blink",
            TestControlJson.Deserialize<TestControlRequest>(behaviorIdJson)
                .BehaviorId!);
        Check.Equal(
            int.MaxValue,
            TestControlJson.Deserialize<TestControlRequest>(seedJson)
                .Seed!.Value);
        Check.Equal(
            9_223_372_036_854_775_000L,
            TestControlJson.Deserialize<TestControlRequest>(afterJson)
                .After!.Value);
    }

    private static void PostcardRequestFieldsRoundTripAsCamelCaseJson()
    {
        var startOuting = new TestControlRequest
        {
            Command = TestControlCommands.PostcardStartOuting,
        };
        var show = new TestControlRequest
        {
            Command = TestControlCommands.PostcardShow,
            PostcardId = "great_wall",
        };

        string outingJson = TestControlJson.Serialize(startOuting);
        string postcardIdJson = TestControlJson.Serialize(show);

        Check.Contains(
            "\"command\":\"postcard-start-outing\"",
            outingJson);
        Check.Contains("\"postcardId\":\"great_wall\"", postcardIdJson);
        Check.DoesNotContain("\"PostcardId\"", postcardIdJson);
        Check.Equal(
            TestControlCommands.PostcardStartOuting,
            TestControlJson.Deserialize<TestControlRequest>(outingJson)
                .Command);
        Check.Equal(
            "great_wall",
            TestControlJson.Deserialize<TestControlRequest>(postcardIdJson)
                .PostcardId!);
    }

    private static void ResponseSnapshotRoundTripsWithoutLosingDiagnostics()
    {
        var capturedAt =
            new DateTimeOffset(2026, 7, 25, 12, 34, 56, TimeSpan.Zero);
        var response = new TestControlResponse
        {
            Ok = true,
            Message = "status",
            Snapshot = new TestRuntimeSnapshot
            {
                CapturedAtUtc = capturedAt,
                ProcessId = 12345,
                Version = "0.1.0",
                CurrentAction = "sleep",
                FrameIndex = 7,
                FrameCount = 16,
                Fps = 6,
                Loop = true,
                AssetStatus = "runtime-preview",
                Visible = false,
                RequestedVisible = true,
                HiddenReason = "fullscreen",
                Topmost = false,
                Window = new TestWindowRect
                {
                    Left = 20.5,
                    Top = 30.25,
                    Width = 320,
                    Height = 280,
                    CoordinateSpace = "physicalPixels",
                    DpiAwareness = "perMonitorV2",
                    Dpi = 144,
                    MonitorBounds = new TestPhysicalRect
                    {
                        Left = 0,
                        Top = 0,
                        Width = 3_840,
                        Height = 2_160,
                    },
                    WorkArea = new TestPhysicalRect
                    {
                        Left = 0,
                        Top = 0,
                        Width = 3_840,
                        Height = 2_088,
                    },
                },
                BehaviorState = "sleeping",
                Emotions = new Dictionary<string, double>
                {
                    ["happiness"] = 62.5,
                    ["sleepiness"] = 84,
                },
                AppBasePath = @"D:\artifacts\gulu",
            },
            Clips =
            [
                new TestClipInfo
                {
                    Name = "sleep",
                    FrameCount = 16,
                    Fps = 6,
                    Loop = true,
                    AssetStatus = "runtime-preview",
                    Placeholder = false,
                    SourceBatch = "video-batch-v001",
                    SourceStage = "transparent-v001",
                    SourceFrameIndices = [0, 8, 16],
                    KnownIssues = ["tail-jitter"],
                },
            ],
        };

        string json = TestControlJson.Serialize(response);
        TestControlResponse roundTripped =
            TestControlJson.Deserialize<TestControlResponse>(json);

        Check.True(roundTripped.Ok);
        Check.NotNull(roundTripped.Snapshot);
        Check.Equal(capturedAt, roundTripped.Snapshot!.CapturedAtUtc);
        Check.Equal(12345, roundTripped.Snapshot.ProcessId);
        Check.Equal("sleep", roundTripped.Snapshot.CurrentAction!);
        Check.Equal(7, roundTripped.Snapshot.FrameIndex);
        Check.Equal(16, roundTripped.Snapshot.FrameCount);
        Check.Equal(6d, roundTripped.Snapshot.Fps);
        Check.True(roundTripped.Snapshot.Loop);
        Check.False(roundTripped.Snapshot.Visible);
        Check.True(roundTripped.Snapshot.RequestedVisible);
        Check.Equal("fullscreen", roundTripped.Snapshot.HiddenReason!);
        Check.NotNull(roundTripped.Snapshot.Window);
        Check.Equal(20.5, roundTripped.Snapshot.Window!.Left);
        Check.Equal(
            "physicalPixels",
            roundTripped.Snapshot.Window.CoordinateSpace!);
        Check.Equal(
            "perMonitorV2",
            roundTripped.Snapshot.Window.DpiAwareness!);
        Check.Equal(144, roundTripped.Snapshot.Window.Dpi);
        Check.NotNull(roundTripped.Snapshot.Window.MonitorBounds);
        Check.Equal(
            3_840d,
            roundTripped.Snapshot.Window.MonitorBounds!.Width);
        Check.Equal(
            2_160d,
            roundTripped.Snapshot.Window.MonitorBounds.Height);
        Check.NotNull(roundTripped.Snapshot.Window.WorkArea);
        Check.Equal(0d, roundTripped.Snapshot.Window.WorkArea!.Left);
        Check.Equal(
            2_088d,
            roundTripped.Snapshot.Window.WorkArea.Height);
        Check.Contains("\"coordinateSpace\":\"physicalPixels\"", json);
        Check.Contains("\"dpiAwareness\":\"perMonitorV2\"", json);
        Check.Contains("\"dpi\":144", json);
        Check.Contains("\"monitorBounds\":", json);
        Check.Contains("\"workArea\":", json);
        Check.Equal(62.5, roundTripped.Snapshot.Emotions["happiness"]);
        Check.NotNull(roundTripped.Clips);
        Check.Equal(1, roundTripped.Clips!.Count);
        Check.Equal(
            "video-batch-v001",
            roundTripped.Clips[0].SourceBatch!);
        Check.SequenceEqual(
            new[] { 0, 8, 16 },
            roundTripped.Clips[0].SourceFrameIndices);
        Check.Equal("tail-jitter", roundTripped.Clips[0].KnownIssues.Single());
    }

    private static void BehaviorContractsRoundTripAsPureWireDtos()
    {
        var capturedAt =
            new DateTimeOffset(2026, 7, 26, 8, 9, 10, TimeSpan.Zero);
        var response = new TestControlResponse
        {
            Ok = true,
            BehaviorStatus = new TestBehaviorStatus
            {
                CapturedAtUtc = capturedAt,
                RuntimePaused = true,
                KeyboardCountSinceStart = 12_345,
                MouseClickCountSinceStart = 2_345,
                StableState = "sleeping",
                LifecyclePhase = "sleepingLooping",
                CurrentBehaviorId = "sleep_loop",
                SessionToken = 41,
                PlaybackToken = 42,
                BehaviorPhase = "looping",
                ClipId = "sleep_loop",
                LogicalTimeMilliseconds = 1_250,
                Committed = true,
                NextCueIndex = 2,
                ActiveInteractionKey = "petting",
                ActiveInteractionTriggerTag = "trigger:petting",
                InteractionRequestId = 40,
                InteractionSessionToken = 41,
            },
            BehaviorEvaluation = new TestBehaviorEvaluationReport
            {
                CapturedAtUtc = capturedAt,
                ContextRevision = 88,
                Source = "activeTick",
                SelectedBehaviorId = "slow_blink",
                AttemptOrder = ["slow_blink", "look_around"],
                TopBehaviorIds = ["slow_blink", "look_around"],
                RandomConsumed = true,
                Evaluations =
                [
                    new TestBehaviorEvaluationItem
                    {
                        BehaviorId = "slow_blink",
                        Eligible = true,
                        Score = 73.25,
                        Rank = 1,
                        Probability = 0.6,
                        Components = new Dictionary<string, double>
                        {
                            ["base"] = 25,
                            ["emotion"] = 10.5,
                        },
                    },
                ],
            },
            BehaviorTick = new TestBehaviorTickResult
            {
                EvaluatedAtUtc = capturedAt,
                Seed = 123,
                Evaluated = true,
                SelectedBehaviorId = "slow_blink",
                Enqueued = true,
                QueueDisposition = "enqueued",
                NextIntervalSeconds = 37,
            },
            BehaviorMutation = new TestBehaviorMutationResult
            {
                Command = TestControlCommands.Enqueue,
                BehaviorId = "slow_blink",
                Accepted = true,
                Disposition = "enqueued",
                RequestId = 7,
                GroupId = 3,
                InteractionKey = "primary_click",
            },
            BehaviorQueue = new TestBehaviorQueueSnapshot
            {
                CapturedAtMilliseconds = 5_000,
                PendingCount = 1,
                ActiveGroupId = 2,
                ResumeAfterMilliseconds = 9_000,
                Groups =
                [
                    new TestBehaviorQueueGroup
                    {
                        GroupId = 3,
                        AcceptedCount = 1,
                        PendingItems =
                        [
                            new TestBehaviorRequestInfo
                            {
                                RequestId = 7,
                                BehaviorId = "slow_blink",
                                DedupeKey = "test:slow_blink",
                                CreatedAtMilliseconds = 4_900,
                                ExpiresAtMilliseconds = 64_900,
                            },
                        ],
                    },
                ],
                Evictions =
                [
                    new TestBehaviorQueueEviction
                    {
                        OccurredAtMilliseconds = 4_800,
                        GroupId = 1,
                        RequestIds = [1, 2],
                        BehaviorIds = ["idle", "look_around"],
                    },
                ],
                RecentDiagnostics =
                [
                    new TestBehaviorQueueDiagnostic
                    {
                        OccurredAtMilliseconds = 4_900,
                        Operation = "enqueue",
                        Outcome = "enqueued",
                        RequestId = 7,
                        GroupId = 3,
                    },
                ],
            },
            BehaviorEvents = new TestBehaviorEventPage
            {
                RequestedAfterSequence = 10,
                Events =
                [
                    new TestBehaviorEvent
                    {
                        Sequence = 11,
                        OccurredAtMilliseconds = 5_000,
                        Kind = "requestEnqueued",
                        BehaviorId = "slow_blink",
                        RequestId = 7,
                        Details = new Dictionary<string, string>
                        {
                            ["source"] = "testControl",
                        },
                    },
                ],
            },
        };

        string json = TestControlJson.Serialize(response);
        TestControlResponse roundTripped =
            TestControlJson.Deserialize<TestControlResponse>(json);

        Check.Contains("\"behaviorStatus\"", json);
        Check.Contains("\"behaviorEvaluation\"", json);
        Check.Contains("\"behaviorTick\"", json);
        Check.Contains("\"behaviorMutation\"", json);
        Check.Contains("\"behaviorQueue\"", json);
        Check.Contains("\"behaviorEvents\"", json);
        Check.Equal(
            "sleep_loop",
            roundTripped.BehaviorStatus!.CurrentBehaviorId!);
        Check.True(roundTripped.BehaviorStatus.RuntimePaused);
        Check.Equal(
            12_345L,
            roundTripped.BehaviorStatus.KeyboardCountSinceStart);
        Check.Equal(
            2_345L,
            roundTripped.BehaviorStatus.MouseClickCountSinceStart);
        Check.Equal(
            "petting",
            roundTripped.BehaviorStatus.ActiveInteractionKey!);
        Check.Equal(
            73.25,
            roundTripped.BehaviorEvaluation!.Evaluations.Single().Score);
        Check.Equal(123, roundTripped.BehaviorTick!.Seed!.Value);
        Check.Equal(7L, roundTripped.BehaviorMutation!.RequestId!.Value);
        Check.Equal(
            "primary_click",
            roundTripped.BehaviorMutation.InteractionKey!);
        Check.Equal(
            "slow_blink",
            roundTripped.BehaviorQueue!.Groups.Single()
                .PendingItems.Single().BehaviorId);
        Check.Equal(
            11L,
            roundTripped.BehaviorEvents!.Events.Single().Sequence);
        Check.False(
            typeof(TestBehaviorStatus).Assembly.GetReferencedAssemblies()
                .Any(reference =>
                    reference.Name?.Equals(
                        "GuluPet",
                        StringComparison.OrdinalIgnoreCase) == true),
            "Wire DTO assembly must not reference the main app assembly.");
    }

    private static void PostcardContractRoundTripsAsPureWireDto()
    {
        DateTimeOffset capturedAt =
            new(2026, 7, 28, 12, 30, 0, TimeSpan.Zero);
        var original = new TestControlResponse
        {
            Ok = true,
            Message = "postcard-status",
            PostcardStatus = new TestPostcardStatus
            {
                CapturedAtUtc = capturedAt,
                OutingPhase = "traveling",
                OutingStartedAtUtc = capturedAt,
                OutingReadyAtUtc = capturedAt.AddMinutes(30),
                OutingRemainingSeconds = 1_800,
                NextPostcardId = "shanghai_bund",
                AvailablePostcardCount = 3,
                UnlockedPostcardIds =
                [
                    "great_wall",
                    "canton_tower",
                ],
                UnacknowledgedPostcardIds = ["canton_tower"],
                PopupVisible = true,
                PopupPostcardId = "canton_tower",
                GalleryVisible = true,
                GalleryPostcardId = "great_wall",
            },
        };

        string json = TestControlJson.Serialize(original);
        TestControlResponse roundTripped =
            TestControlJson.Deserialize<TestControlResponse>(json);

        Check.Contains("\"postcardStatus\":", json);
        Check.Contains("\"outingPhase\":\"traveling\"", json);
        Check.Contains("\"outingRemainingSeconds\":1800", json);
        Check.Contains("\"popupPostcardId\":\"canton_tower\"", json);
        Check.NotNull(roundTripped.PostcardStatus);
        Check.Equal(
            capturedAt,
            roundTripped.PostcardStatus!.CapturedAtUtc);
        Check.Equal("traveling", roundTripped.PostcardStatus.OutingPhase);
        Check.Equal(
            capturedAt.AddMinutes(30),
            roundTripped.PostcardStatus.OutingReadyAtUtc!.Value);
        Check.Equal(
            1_800L,
            roundTripped.PostcardStatus.OutingRemainingSeconds);
        Check.Equal(
            "shanghai_bund",
            roundTripped.PostcardStatus.NextPostcardId!);
        Check.SequenceEqual(
            new[] { "great_wall", "canton_tower" },
            roundTripped.PostcardStatus.UnlockedPostcardIds);
        Check.SequenceEqual(
            new[] { "canton_tower" },
            roundTripped.PostcardStatus.UnacknowledgedPostcardIds);
        Check.True(roundTripped.PostcardStatus.PopupVisible);
        Check.Equal(
            "great_wall",
            roundTripped.PostcardStatus.GalleryPostcardId!);
    }

    private static void MemoryContractRoundTripsAsPureWireDto()
    {
        DateTimeOffset capturedAt =
            new(2026, 7, 30, 3, 30, 0, TimeSpan.Zero);
        var original = new TestControlResponse
        {
            Ok = true,
            Message = "memory-status",
            MemoryStatus = new TestMemoryStatus
            {
                CapturedAtUtc = capturedAt,
                AcceptedInteractionCount = 10,
                AcceptedInteractionsUntilNextUnlock = 0,
                PendingMemoryId = "memory-01",
                CompletedMemoryIds = [],
                PlaybackTaskActive = true,
                PlayingMemoryId = "memory-01",
                PlaybackPhase = "playing",
                PresentationActive = true,
                LastPlaybackOutcome = "failed",
                LastPlaybackError = "decoder unavailable",
            },
        };

        string json = TestControlJson.Serialize(original);
        TestControlResponse roundTripped =
            TestControlJson.Deserialize<TestControlResponse>(json);

        Check.Contains("\"memoryStatus\":", json);
        Check.Contains("\"acceptedInteractionCount\":10", json);
        Check.Contains("\"pendingMemoryId\":\"memory-01\"", json);
        Check.Contains("\"presentationActive\":true", json);
        Check.Contains("\"playbackPhase\":\"playing\"", json);
        Check.NotNull(roundTripped.MemoryStatus);
        Check.Equal(
            capturedAt,
            roundTripped.MemoryStatus!.CapturedAtUtc);
        Check.Equal(
            10L,
            roundTripped.MemoryStatus.AcceptedInteractionCount);
        Check.Equal(
            "memory-01",
            roundTripped.MemoryStatus.PendingMemoryId!);
        Check.True(roundTripped.MemoryStatus.PlaybackTaskActive);
        Check.Equal(
            "playing",
            roundTripped.MemoryStatus.PlaybackPhase!);
        Check.True(roundTripped.MemoryStatus.PresentationActive);
        Check.Equal(
            "failed",
            roundTripped.MemoryStatus.LastPlaybackOutcome!);
        Check.Equal(
            "decoder unavailable",
            roundTripped.MemoryStatus.LastPlaybackError!);
        Check.False(
            typeof(TestMemoryStatus).Assembly.GetReferencedAssemblies()
                .Any(reference =>
                    reference.Name?.Equals(
                        "GuluPet",
                        StringComparison.OrdinalIgnoreCase) == true),
            "Wire DTO assembly must not reference the main app assembly.");
    }

    private static void OptionalResponseFieldsAreOmitted()
    {
        string json =
            TestControlJson.Serialize(
                new TestControlResponse
                {
                    Ok = true,
                });

        Check.DoesNotContain("\"errorCode\"", json);
        Check.DoesNotContain("\"error\"", json);
        Check.DoesNotContain("\"message\"", json);
        Check.DoesNotContain("\"snapshot\"", json);
        Check.DoesNotContain("\"clips\"", json);
        Check.DoesNotContain("\"behaviorStatus\"", json);
        Check.DoesNotContain("\"behaviorEvaluation\"", json);
        Check.DoesNotContain("\"behaviorTick\"", json);
        Check.DoesNotContain("\"behaviorMutation\"", json);
        Check.DoesNotContain("\"behaviorQueue\"", json);
        Check.DoesNotContain("\"behaviorEvents\"", json);
        Check.DoesNotContain("\"memoryStatus\"", json);
        Check.DoesNotContain("\"postcardStatus\"", json);
    }

    private static void ValidationAcceptsEveryWhitelistedCommand()
    {
        foreach (string command in TestControlCommands.All)
        {
            TestControlRequest request = command switch
            {
                TestControlCommands.Play => new TestControlRequest
                {
                    Command = command,
                    Action = "idle",
                },
                TestControlCommands.Enqueue or
                TestControlCommands.StartIfIdle or
                TestControlCommands.BehaviorPreview =>
                    new TestControlRequest
                    {
                        Command = command,
                        BehaviorId = "slow_blink",
                    },
                TestControlCommands.BehaviorTick => new TestControlRequest
                {
                    Command = command,
                    Seed = 123,
                },
                TestControlCommands.BehaviorEvents => new TestControlRequest
                {
                    Command = command,
                    After = 42,
                },
                TestControlCommands.MemoryAddInteraction =>
                    new TestControlRequest
                    {
                        Command = command,
                        Count = 10,
                    },
                TestControlCommands.PostcardShow => new TestControlRequest
                {
                    Command = command,
                    PostcardId = "great_wall",
                },
                _ => new TestControlRequest
                {
                    Command = command,
                },
            };

            bool valid = TestControlProtocol.ValidateRequest(
                request,
                out string? errorCode,
                out string? error);

            Check.True(valid, $"Expected '{command}' to be valid.");
            Check.Null(errorCode);
            Check.Null(error);
        }

        Check.True(
            TestControlProtocol.ValidateRequest(
                new TestControlRequest
                {
                    Command = "  PLAY  ",
                    Action = "idle",
                },
                out _,
                out _),
            "Command matching should ignore casing and surrounding whitespace.");
        Check.True(TestControlProtocol.IsValidBehaviorId("sleep_loop-v2"));
        Check.True(TestControlProtocol.IsValidPostcardId("great_wall-v2"));
    }

    private static void ValidationRejectsInvalidRequestsWithStableCodes()
    {
        CheckInvalid(null, TestControlErrorCodes.InvalidRequest);
        CheckInvalid(
            new TestControlRequest
            {
                ProtocolVersion = TestControlProtocol.Version + 1,
                Command = TestControlCommands.Ping,
            },
            TestControlErrorCodes.UnsupportedVersion);
        CheckInvalid(
            new TestControlRequest
            {
                Command = " ",
            },
            TestControlErrorCodes.InvalidCommand);
        CheckInvalid(
            new TestControlRequest
            {
                Command = "run-arbitrary-command",
            },
            TestControlErrorCodes.InvalidCommand);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.Play,
                Action = " ",
            },
            TestControlErrorCodes.MissingAction);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.Enqueue,
            },
            TestControlErrorCodes.MissingBehaviorId);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.StartIfIdle,
                BehaviorId = "Sleep Loop",
            },
            TestControlErrorCodes.InvalidBehaviorId);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.BehaviorTick,
                Seed = -1,
            },
            TestControlErrorCodes.InvalidSeed);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.BehaviorEvents,
                After = -1,
            },
            TestControlErrorCodes.InvalidAfter);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.MemoryAddInteraction,
            },
            TestControlErrorCodes.MissingCount);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.MemoryAddInteraction,
                Count = 0,
            },
            TestControlErrorCodes.InvalidCount);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.MemoryAddInteraction,
                Count = 11,
            },
            TestControlErrorCodes.InvalidCount);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.MemoryAddInteraction,
                Count = 1,
                PostcardId = "great_wall",
            },
            TestControlErrorCodes.UnexpectedArgument);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.PostcardShow,
            },
            TestControlErrorCodes.MissingPostcardId);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.PostcardShow,
                PostcardId = "Great Wall",
            },
            TestControlErrorCodes.InvalidPostcardId);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.Status,
                BehaviorId = "slow_blink",
            },
            TestControlErrorCodes.UnexpectedArgument);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.Play,
                Action = "idle",
                Seed = 3,
            },
            TestControlErrorCodes.UnexpectedArgument);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.Enqueue,
                BehaviorId = "slow_blink",
                Action = "idle",
            },
            TestControlErrorCodes.UnexpectedArgument);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.BehaviorTick,
                After = 1,
            },
            TestControlErrorCodes.UnexpectedArgument);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.PostcardStatus,
                Count = 1,
            },
            TestControlErrorCodes.UnexpectedArgument);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.PostcardStartOuting,
                Count = 1,
            },
            TestControlErrorCodes.UnexpectedArgument);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.PostcardShow,
                PostcardId = "great_wall",
                Count = 1,
            },
            TestControlErrorCodes.UnexpectedArgument);
        CheckInvalid(
            new TestControlRequest
            {
                Command = TestControlCommands.Play,
                Action = "idle",
                PostcardId = "great_wall",
            },
            TestControlErrorCodes.UnexpectedArgument);
        Check.False(TestControlProtocol.IsValidBehaviorId(""));
        Check.False(TestControlProtocol.IsValidBehaviorId(" slow_blink"));
        Check.False(TestControlProtocol.IsValidBehaviorId("slow/blink"));
        Check.False(TestControlProtocol.IsValidBehaviorId(new string('a', 97)));
        Check.False(TestControlProtocol.IsValidPostcardId("Great_Wall"));
        Check.False(TestControlProtocol.IsValidPostcardId("great/wall"));
    }

    private static void CommandAndErrorContractsStayStable()
    {
        Check.SequenceEqual(
            new[]
            {
                "ping",
                "status",
                "clips",
                "begin-probe",
                "end-probe",
                "play",
                "show",
                "hide",
                "reset-position",
                "behavior-status",
                "behavior-open-browser",
                "behavior-evaluate",
                "behavior-tick",
                "enqueue",
                "start-if-idle",
                "behavior-preview",
                "queue",
                "behavior-events",
                "memory-status",
                "memory-add-interaction",
                "postcard-status",
                "postcard-start-outing",
                "postcard-complete-outing",
                "postcard-claim-arrival",
                "postcard-open-gallery",
                "postcard-show",
                "postcard-dismiss-popup",
            },
            TestControlCommands.All);
        Check.Equal(
            TestControlCommands.All.Count,
            TestControlCommands.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Check.Equal("invalid_request", TestControlErrorCodes.InvalidRequest);
        Check.Equal("unsupported_version", TestControlErrorCodes.UnsupportedVersion);
        Check.Equal("invalid_command", TestControlErrorCodes.InvalidCommand);
        Check.Equal("missing_action", TestControlErrorCodes.MissingAction);
        Check.Equal(
            "missing_behavior_id",
            TestControlErrorCodes.MissingBehaviorId);
        Check.Equal(
            "invalid_behavior_id",
            TestControlErrorCodes.InvalidBehaviorId);
        Check.Equal("invalid_seed", TestControlErrorCodes.InvalidSeed);
        Check.Equal("invalid_after", TestControlErrorCodes.InvalidAfter);
        Check.Equal("missing_count", TestControlErrorCodes.MissingCount);
        Check.Equal("invalid_count", TestControlErrorCodes.InvalidCount);
        Check.Equal(
            "missing_postcard_id",
            TestControlErrorCodes.MissingPostcardId);
        Check.Equal(
            "invalid_postcard_id",
            TestControlErrorCodes.InvalidPostcardId);
        Check.Equal(
            "requires_isolated_instance",
            TestControlErrorCodes.RequiresIsolatedInstance);
        Check.Equal(
            "unexpected_argument",
            TestControlErrorCodes.UnexpectedArgument);
        Check.Equal("unknown_action", TestControlErrorCodes.UnknownAction);
        Check.Equal(
            "operation_blocked",
            TestControlErrorCodes.OperationBlocked);
        Check.Equal("internal_error", TestControlErrorCodes.InternalError);
        Check.True(TestControlProtocol.MaxRequestBytes > 0);
        Check.True(TestControlProtocol.MaxResponseBytes >
            TestControlProtocol.MaxRequestBytes);
    }

    private static void MalformedJsonIsRejected()
    {
        Check.Throws<JsonException>(
            () => TestControlJson.Deserialize<TestControlRequest>("{"));
        Check.Throws<JsonException>(
            () => TestControlJson.Deserialize<TestControlRequest>("null"));
        Check.Throws<JsonException>(
            () => TestControlJson.Deserialize<TestControlRequest>(
                """{"command":"ping"}"""));
        Check.Throws<JsonException>(
            () => TestControlJson.Deserialize<TestControlResponse>(
                """{"ok":true}"""));
    }

    private static void WireRoundTripsNewlineTerminatedJson()
    {
        using var stream = new MemoryStream();
        var request = new TestControlRequest
        {
            Command = TestControlCommands.Play,
            Action = "groom",
        };

        TestControlWire.WriteAsync(
                stream,
                request,
                TestControlProtocol.MaxRequestBytes)
            .GetAwaiter()
            .GetResult();

        byte[] bytes = stream.ToArray();
        Check.True(bytes.Length > 1);
        Check.Equal((byte)'\n', bytes[^1]);

        stream.Position = 0;
        TestControlRequest roundTripped =
            TestControlWire.ReadAsync<TestControlRequest>(
                    stream,
                    TestControlProtocol.MaxRequestBytes)
                .GetAwaiter()
                .GetResult();
        Check.Equal(TestControlCommands.Play, roundTripped.Command);
        Check.Equal("groom", roundTripped.Action!);
    }

    private static void WireRejectsTruncatedAndOversizedMessages()
    {
        using var truncated =
            new MemoryStream("""{"protocolVersion":1,"command":"ping"}"""u8.ToArray());
        Check.ThrowsAsync<TestControlProtocolException>(
            () => TestControlWire.ReadAsync<TestControlRequest>(
                truncated,
                TestControlProtocol.MaxRequestBytes));

        using var oversized = new MemoryStream();
        Check.ThrowsAsync<TestControlProtocolException>(
            () => TestControlWire.WriteAsync(
                oversized,
                new TestControlRequest
                {
                    Command = TestControlCommands.Play,
                    Action = new string('x', 64),
                },
                maxBytes: 16));
    }

    private static void ClientCommunicatesOverAnIsolatedCurrentUserPipe()
    {
        string pipeName = $"GuluPet.Tests.{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        Task serverTask = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await server.WaitForConnectionAsync(timeout.Token);
            TestControlRequest request =
                await TestControlWire.ReadAsync<TestControlRequest>(
                    server,
                    TestControlProtocol.MaxRequestBytes,
                    timeout.Token);
            Check.Equal(TestControlCommands.Status, request.Command);
            await TestControlWire.WriteAsync(
                server,
                new TestControlResponse
                {
                    Ok = true,
                    Message = "isolated-test",
                    Snapshot = new TestRuntimeSnapshot
                    {
                        ProcessId = Environment.ProcessId,
                        CurrentAction = "idle",
                        FrameIndex = 2,
                        FrameCount = 12,
                        Fps = 6,
                        Loop = true,
                        Visible = false,
                        RequestedVisible = false,
                        Topmost = true,
                    },
                },
                TestControlProtocol.MaxResponseBytes,
                timeout.Token);
        });

        var client = new TestControlClient(pipeName);
        TestControlResponse response =
            client.SendAsync(
                    new TestControlRequest
                    {
                        Command = TestControlCommands.Status,
                    },
                    TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult();
        serverTask.GetAwaiter().GetResult();

        Check.True(response.Ok);
        Check.Equal("isolated-test", response.Message!);
        Check.NotNull(response.Snapshot);
        Check.Equal("idle", response.Snapshot!.CurrentAction!);
        Check.Equal(2, response.Snapshot.FrameIndex);
    }

    private static void CheckInvalid(
        TestControlRequest? request,
        string expectedErrorCode)
    {
        bool valid = TestControlProtocol.ValidateRequest(
            request,
            out string? errorCode,
            out string? error);

        Check.False(valid);
        Check.Equal(expectedErrorCode, errorCode!);
        Check.True(!string.IsNullOrWhiteSpace(error));
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }

    private static class Check
    {
        public static void True(bool value, string? message = null)
        {
            if (!value)
            {
                throw new InvalidOperationException(message ?? "Expected true.");
            }
        }

        public static void False(bool value, string? message = null) =>
            True(!value, message ?? "Expected false.");

        public static void Null(object? value)
        {
            if (value is not null)
            {
                throw new InvalidOperationException($"Expected null, got '{value}'.");
            }
        }

        public static void NotNull(object? value)
        {
            if (value is null)
            {
                throw new InvalidOperationException("Expected a non-null value.");
            }
        }

        public static void Equal<T>(T expected, T actual)
            where T : notnull
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Expected '{expected}', got '{actual}'.");
            }
        }

        public static void Contains(string expected, string actual)
        {
            if (!actual.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Expected JSON to contain '{expected}'.");
            }
        }

        public static void DoesNotContain(string unexpected, string actual)
        {
            if (actual.Contains(unexpected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Expected JSON not to contain '{unexpected}'.");
            }
        }

        public static void SequenceEqual<T>(
            IEnumerable<T> expected,
            IEnumerable<T> actual)
        {
            if (!expected.SequenceEqual(actual))
            {
                throw new InvalidOperationException("Sequences were not equal.");
            }
        }

        public static void Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name}.");
        }

        public static void ThrowsAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                action().GetAwaiter().GetResult();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name}.");
        }
    }
}
