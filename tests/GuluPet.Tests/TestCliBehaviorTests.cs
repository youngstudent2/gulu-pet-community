using System.IO.Pipes;
using GuluPet.Runtime;
using GuluPet.TestCli;
using GuluPet.Testing;

namespace GuluPet.Tests;

internal static class TestCliBehaviorTests
{
    public static void RunAll()
    {
        Run(nameof(ProbeAcceptsValidFrameProgress), ProbeAcceptsValidFrameProgress);
        Run(nameof(ProbeRejectsInvalidFrameRangeAndMetadata), ProbeRejectsInvalidFrameRangeAndMetadata);
        Run(nameof(ProbeExitCodesDistinguishRejectionFromAssertion), ProbeExitCodesDistinguishRejectionFromAssertion);
        Run(nameof(WatchRequiresAValidSnapshot), WatchRequiresAValidSnapshot);
        Run(nameof(ProbeRestoreFallbackUsesAvailableRuntimeClip), ProbeRestoreFallbackUsesAvailableRuntimeClip);
        Run(nameof(ProbeFinallyRestoresAndEndsAfterFailure), ProbeFinallyRestoresAndEndsAfterFailure);
        Run(nameof(BehaviorCommandsCreateExactProtocolRequests), BehaviorCommandsCreateExactProtocolRequests);
        Run(nameof(BehaviorCommandsRejectInvalidCliArguments), BehaviorCommandsRejectInvalidCliArguments);
        Run(nameof(BehaviorCommandsSendAndReturnJsonExitCodes), BehaviorCommandsSendAndReturnJsonExitCodes);
        Run(nameof(PostcardCommandsCreateExactProtocolRequests), PostcardCommandsCreateExactProtocolRequests);
        Run(nameof(PostcardCommandsRejectInvalidCliArguments), PostcardCommandsRejectInvalidCliArguments);
        Run(nameof(PostcardCommandsSendAndReturnJsonExitCodes), PostcardCommandsSendAndReturnJsonExitCodes);
        Run(nameof(MemoryCommandsCreateExactProtocolRequests), MemoryCommandsCreateExactProtocolRequests);
        Run(nameof(MemoryCommandsRejectInvalidCliArguments), MemoryCommandsRejectInvalidCliArguments);
        Run(nameof(MemoryCommandsSendAndReturnJsonExitCodes), MemoryCommandsSendAndReturnJsonExitCodes);
    }

    private static void ProbeAcceptsValidFrameProgress()
    {
        TestClipInfo clip = CreateBlinkClip();
        ActionProbeResult result = ActionProbeRunner.EvaluateSamples(
            "blink",
            clip,
            [
                CreateSample("blink", 0, 5, 12, loop: false),
                CreateSample("blink", 1, 5, 12, loop: false),
            ]);

        Check.True(result.Ok);
        Check.True(result.ObservedRequestedAction);
        Check.True(result.FrameIndicesInRange);
        Check.True(result.MetadataConsistent);
        Check.SequenceEqual(new[] { 0, 1 }, result.DistinctFrameIndices);
    }

    private static void ProbeRejectsInvalidFrameRangeAndMetadata()
    {
        TestClipInfo clip = CreateBlinkClip();
        ActionProbeResult result = ActionProbeRunner.EvaluateSamples(
            "blink",
            clip,
            [
                CreateSample("blink", 100, 6, 99, loop: true),
                CreateSample("blink", 101, 6, 99, loop: true),
            ]);

        Check.False(result.Ok);
        Check.True(result.ObservedRequestedAction);
        Check.False(result.FrameIndicesInRange);
        Check.False(result.MetadataConsistent);
        Check.True(!string.IsNullOrWhiteSpace(result.Error));
    }

    private static void ProbeExitCodesDistinguishRejectionFromAssertion()
    {
        Check.Equal(
            CliExitCodes.Success,
            CliApplication.GetProbeExitCode(new ActionProbeReport { Ok = true }));
        Check.Equal(
            CliExitCodes.CommandRejected,
            CliApplication.GetProbeExitCode(
                new ActionProbeReport
                {
                    Ok = false,
                    CommandRejected = true,
                }));
        Check.Equal(
            CliExitCodes.ProbeFailed,
            CliApplication.GetProbeExitCode(new ActionProbeReport { Ok = false }));
    }

    private static void WatchRequiresAValidSnapshot()
    {
        Check.False(CliApplication.IsValidRuntimeSnapshot(null));
        Check.False(
            CliApplication.IsValidRuntimeSnapshot(
                new TestRuntimeSnapshot
                {
                    ProcessId = 123,
                    CurrentAction = "idle",
                    FrameIndex = 2,
                    FrameCount = 2,
                    Fps = 6,
                }));
        Check.True(
            CliApplication.IsValidRuntimeSnapshot(
                new TestRuntimeSnapshot
                {
                    ProcessId = 123,
                    CurrentAction = "idle",
                    FrameIndex = 1,
                    FrameCount = 2,
                    Fps = 6,
                }));
    }

    private static void ProbeRestoreFallbackUsesAvailableRuntimeClip()
    {
        TestClipInfo blink = CreateBlinkClip();
        var idleBreathe = new TestClipInfo
        {
            Name = "idle_breathe",
            FrameCount = 121,
            Fps = RuntimeContentContract.DefaultFramesPerSecond,
            Loop = true,
            AssetStatus = "gulu-authorized-sample",
        };

        Check.Equal(
            "idle_breathe",
            ActionProbeRunner.ResolveRestoreAction(
                new TestRuntimeSnapshot
                {
                    ProcessId = Environment.ProcessId,
                    CurrentAction = "",
                },
                [blink, idleBreathe])!);
        Check.Equal(
            "blink",
            ActionProbeRunner.ResolveRestoreAction(null, [blink])!);
        Check.Equal(
            "sleep_loop",
            ActionProbeRunner.ResolveRestoreAction(
                new TestRuntimeSnapshot
                {
                    ProcessId = Environment.ProcessId,
                    CurrentAction = " sleep_loop ",
                },
                [idleBreathe])!);
    }

    private static void ProbeFinallyRestoresAndEndsAfterFailure()
    {
        string pipeName = $"GuluPet.CliTests.{Guid.NewGuid():N}";
        var requests = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task serverTask = Task.Run(async () =>
        {
            bool probeBegun = false;
            while (true)
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(timeout.Token);
                TestControlRequest request =
                    await TestControlWire.ReadAsync<TestControlRequest>(
                        server,
                        TestControlProtocol.MaxRequestBytes,
                        timeout.Token);
                requests.Add(
                    request.Action is null
                        ? request.Command
                        : $"{request.Command}:{request.Action}");

                TestControlResponse? response;
                switch (request.Command)
                {
                    case TestControlCommands.Clips:
                        response = new TestControlResponse
                        {
                            Ok = true,
                            Clips = [CreateBlinkClip()],
                        };
                        break;

                    case TestControlCommands.Status when !probeBegun:
                        response = new TestControlResponse
                        {
                            Ok = true,
                            Snapshot = new TestRuntimeSnapshot
                            {
                                ProcessId = Environment.ProcessId,
                                CurrentAction = "sleep",
                                FrameIndex = 3,
                                FrameCount = 16,
                                Fps = 6,
                                Loop = true,
                                AssetStatus = "runtime-preview",
                            },
                        };
                        break;

                    case TestControlCommands.BeginProbe:
                        probeBegun = true;
                        response = new TestControlResponse { Ok = true };
                        break;

                    case TestControlCommands.Play when request.Action == "blink":
                        response = new TestControlResponse
                        {
                            Ok = true,
                            Snapshot = new TestRuntimeSnapshot
                            {
                                ProcessId = Environment.ProcessId,
                                CurrentAction = "blink",
                                FrameIndex = 0,
                                FrameCount = 5,
                                Fps = 12,
                                Loop = false,
                                AssetStatus = "runtime-preview",
                            },
                        };
                        break;

                    case TestControlCommands.Status:
                        // Force the probe body to fail while the session is
                        // active. Disposing this connection without a response
                        // must still be followed by restore + end-probe.
                        response = null;
                        break;

                    case TestControlCommands.Play when request.Action == "sleep":
                        response = new TestControlResponse { Ok = true };
                        break;

                    case TestControlCommands.EndProbe:
                        response = new TestControlResponse { Ok = true };
                        break;

                    default:
                        response = new TestControlResponse
                        {
                            Ok = false,
                            ErrorCode = TestControlErrorCodes.InvalidCommand,
                            Error = $"Unexpected test command '{request.Command}'.",
                        };
                        break;
                }

                if (response is not null)
                {
                    await TestControlWire.WriteAsync(
                        server,
                        response,
                        TestControlProtocol.MaxResponseBytes,
                        timeout.Token);
                }

                if (request.Command == TestControlCommands.EndProbe)
                {
                    return;
                }
            }
        }, timeout.Token);

        var runner = new ActionProbeRunner(
            new TestControlClient(pipeName),
            TimeSpan.FromSeconds(2));
        var options = new ProbeOptions
        {
            HoldMilliseconds = 100,
            PollMilliseconds = 10,
            Actions = ["blink"],
        };

        Check.ThrowsAnyAsync(
            () => runner.RunAsync(options),
            typeof(TestControlProtocolException),
            typeof(TestControlUnavailableException));
        serverTask.GetAwaiter().GetResult();

        Check.SequenceEqual(
            new[]
            {
                "clips",
                "status",
                "begin-probe",
                "play:blink",
                "status",
                "play:sleep",
                "end-probe",
            },
            requests);
    }

    private static void BehaviorCommandsCreateExactProtocolRequests()
    {
        TestControlRequest status = CreateBehaviorRequest("behavior-status");
        Check.Equal(TestControlCommands.BehaviorStatus, status.Command);
        Check.Null(status.BehaviorId);
        Check.Null(status.Seed);
        Check.Null(status.After);

        Check.Equal(
            TestControlCommands.BehaviorOpenBrowser,
            CreateBehaviorRequest("behavior-open-browser").Command);
        Check.Equal(
            TestControlCommands.BehaviorEvaluate,
            CreateBehaviorRequest("behavior-evaluate").Command);
        Check.Equal(
            TestControlCommands.Queue,
            CreateBehaviorRequest("queue").Command);

        TestControlRequest tick = CreateBehaviorRequest(
            "behavior-tick",
            "--seed",
            "2147483647");
        Check.Equal(TestControlCommands.BehaviorTick, tick.Command);
        Check.Equal(int.MaxValue, tick.Seed!.Value);
        Check.Null(tick.BehaviorId);
        Check.Null(tick.After);

        TestControlRequest unseededTick =
            CreateBehaviorRequest("behavior-tick");
        Check.Null(unseededTick.Seed);

        TestControlRequest enqueue =
            CreateBehaviorRequest("enqueue", "slow_blink");
        Check.Equal(TestControlCommands.Enqueue, enqueue.Command);
        Check.Equal("slow_blink", enqueue.BehaviorId!);
        Check.Null(enqueue.Seed);

        TestControlRequest startIfIdle =
            CreateBehaviorRequest("start-if-idle", "startled-hop");
        Check.Equal(TestControlCommands.StartIfIdle, startIfIdle.Command);
        Check.Equal("startled-hop", startIfIdle.BehaviorId!);

        TestControlRequest preview =
            CreateBehaviorRequest("behavior-preview", "wake_slow_blink");
        Check.Equal(TestControlCommands.BehaviorPreview, preview.Command);
        Check.Equal("wake_slow_blink", preview.BehaviorId!);

        TestControlRequest events = CreateBehaviorRequest(
            "behavior-events",
            "--after",
            "9223372036854775807");
        Check.Equal(TestControlCommands.BehaviorEvents, events.Command);
        Check.Equal(long.MaxValue, events.After!.Value);

        TestControlRequest allEvents =
            CreateBehaviorRequest("behavior-events");
        Check.Null(allEvents.After);
    }

    private static void BehaviorCommandsRejectInvalidCliArguments()
    {
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest("behavior-tick", "--seed"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest("behavior-tick", "--seed", "-1"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "behavior-tick",
                "--seed",
                "1",
                "--seed",
                "2"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest("behavior-tick", "--after", "1"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest("behavior-events", "--after"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "behavior-events",
                "--after",
                "9223372036854775808"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest("behavior-events", "--seed", "1"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest("enqueue"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest("enqueue", "SlowBlink"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest("start-if-idle", "sleep/loop"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest("queue", "unexpected"));
    }

    private static void BehaviorCommandsSendAndReturnJsonExitCodes()
    {
        var successResponse = new TestControlResponse
        {
            Ok = true,
            Message = "tick-evaluated",
            BehaviorTick = new TestBehaviorTickResult
            {
                Seed = 8128,
                Evaluated = true,
                SelectedBehaviorId = "slow_blink",
                Enqueued = true,
                QueueDisposition = "enqueued",
                NextIntervalSeconds = 44,
            },
        };
        CliInvocation success = InvokeCliAgainstSingleResponse(
            ["behavior-tick", "--seed", "8128"],
            successResponse);

        Check.Equal(CliExitCodes.Success, success.ExitCode);
        Check.Equal(TestControlCommands.BehaviorTick, success.Request.Command);
        Check.Equal(8128, success.Request.Seed!.Value);
        TestControlResponse emittedSuccess =
            TestControlJson.Deserialize<TestControlResponse>(success.Json);
        Check.True(emittedSuccess.Ok);
        Check.Equal(
            "slow_blink",
            emittedSuccess.BehaviorTick!.SelectedBehaviorId!);

        var rejectionResponse = new TestControlResponse
        {
            Ok = false,
            ErrorCode = TestControlErrorCodes.InvalidBehaviorId,
            Error = "Unknown behavior 'missing_behavior'.",
        };
        CliInvocation rejection = InvokeCliAgainstSingleResponse(
            ["enqueue", "missing_behavior"],
            rejectionResponse);

        Check.Equal(CliExitCodes.CommandRejected, rejection.ExitCode);
        Check.Equal(TestControlCommands.Enqueue, rejection.Request.Command);
        Check.Equal("missing_behavior", rejection.Request.BehaviorId!);
        TestControlResponse emittedRejection =
            TestControlJson.Deserialize<TestControlResponse>(rejection.Json);
        Check.False(emittedRejection.Ok);
        Check.Equal(
            TestControlErrorCodes.InvalidBehaviorId,
            emittedRejection.ErrorCode!);
    }

    private static void PostcardCommandsCreateExactProtocolRequests()
    {
        TestControlRequest status = CreateBehaviorRequest("postcard-status");
        Check.Equal(TestControlCommands.PostcardStatus, status.Command);
        Check.Null(status.Count);
        Check.Null(status.PostcardId);

        Check.Equal(
            TestControlCommands.PostcardStartOuting,
            CreateBehaviorRequest("postcard-start-outing").Command);
        Check.Equal(
            TestControlCommands.PostcardCompleteOuting,
            CreateBehaviorRequest("postcard-complete-outing").Command);
        Check.Equal(
            TestControlCommands.PostcardClaimArrival,
            CreateBehaviorRequest("postcard-claim-arrival").Command);

        Check.Equal(
            TestControlCommands.PostcardOpenGallery,
            CreateBehaviorRequest("postcard-open-gallery").Command);

        TestControlRequest show = CreateBehaviorRequest(
            "postcard-show",
            "--postcard-id",
            "great_wall");
        Check.Equal(TestControlCommands.PostcardShow, show.Command);
        Check.Equal("great_wall", show.PostcardId!);
        Check.Null(show.Count);

        Check.Equal(
            TestControlCommands.PostcardDismissPopup,
            CreateBehaviorRequest("postcard-dismiss-popup").Command);
    }

    private static void PostcardCommandsRejectInvalidCliArguments()
    {
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "postcard-start-outing",
                "unexpected"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest("postcard-show"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "postcard-show",
                "--postcard-id"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "postcard-show",
                "--postcard-id",
                "Great Wall"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "postcard-show",
                "--postcard-id",
                "great_wall",
                "--postcard-id",
                "canton_tower"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "postcard-open-gallery",
                "unexpected"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "postcard-dismiss-popup",
                "unexpected"));
    }

    private static void PostcardCommandsSendAndReturnJsonExitCodes()
    {
        var successResponse = new TestControlResponse
        {
            Ok = true,
            Message = "postcard-outing-started",
            PostcardStatus = new TestPostcardStatus
            {
                OutingPhase = "traveling",
                OutingRemainingSeconds = 1_800,
                NextPostcardId = "great_wall",
                AvailablePostcardCount = 3,
            },
        };
        CliInvocation success = InvokeCliAgainstSingleResponse(
            ["postcard-start-outing"],
            successResponse);

        Check.Equal(CliExitCodes.Success, success.ExitCode);
        Check.Equal(
            TestControlCommands.PostcardStartOuting,
            success.Request.Command);
        Check.Null(success.Request.Count);
        TestControlResponse emittedSuccess =
            TestControlJson.Deserialize<TestControlResponse>(success.Json);
        Check.True(emittedSuccess.Ok);
        Check.NotNull(emittedSuccess.PostcardStatus);
        Check.Equal(
            "traveling",
            emittedSuccess.PostcardStatus!.OutingPhase);

        var rejectionResponse = new TestControlResponse
        {
            Ok = false,
            ErrorCode = TestControlErrorCodes.InvalidPostcardId,
            Error = "Unknown postcard 'missing_postcard'.",
        };
        CliInvocation rejection = InvokeCliAgainstSingleResponse(
            [
                "postcard-show",
                "--postcard-id",
                "missing_postcard",
            ],
            rejectionResponse);

        Check.Equal(CliExitCodes.CommandRejected, rejection.ExitCode);
        Check.Equal(
            TestControlCommands.PostcardShow,
            rejection.Request.Command);
        Check.Equal("missing_postcard", rejection.Request.PostcardId!);
        TestControlResponse emittedRejection =
            TestControlJson.Deserialize<TestControlResponse>(rejection.Json);
        Check.False(emittedRejection.Ok);
        Check.Equal(
            TestControlErrorCodes.InvalidPostcardId,
            emittedRejection.ErrorCode!);
    }

    private static void MemoryCommandsCreateExactProtocolRequests()
    {
        TestControlRequest status = CreateBehaviorRequest("memory-status");
        Check.Equal(TestControlCommands.MemoryStatus, status.Command);
        Check.Null(status.Count);
        Check.Null(status.PostcardId);
        Check.Null(status.BehaviorId);

        TestControlRequest addInteraction = CreateBehaviorRequest(
            "memory-add-interaction",
            "--count",
            "10");
        Check.Equal(
            TestControlCommands.MemoryAddInteraction,
            addInteraction.Command);
        Check.Equal(10L, addInteraction.Count!.Value);
        Check.Null(addInteraction.PostcardId);
        Check.Null(addInteraction.BehaviorId);
    }

    private static void MemoryCommandsRejectInvalidCliArguments()
    {
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest("memory-add-interaction"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "memory-add-interaction",
                "--count"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "memory-add-interaction",
                "--count",
                "0"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "memory-add-interaction",
                "--count",
                "11"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "memory-add-interaction",
                "--count",
                "1",
                "--count",
                "2"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "memory-add-interaction",
                "--memory-id",
                "memory-01"));
        Check.Throws<CliUsageException>(
            () => CreateBehaviorRequest(
                "memory-status",
                "unexpected"));
    }

    private static void MemoryCommandsSendAndReturnJsonExitCodes()
    {
        var successResponse = new TestControlResponse
        {
            Ok = true,
            Message = "memory-interactions-added",
            MemoryStatus = new TestMemoryStatus
            {
                AcceptedInteractionCount = 10,
                AcceptedInteractionsUntilNextUnlock = 0,
                PendingMemoryId = "memory-01",
                CompletedMemoryIds = [],
                PlaybackTaskActive = true,
                PlayingMemoryId = "memory-01",
                PlaybackPhase = "ready",
                PresentationActive = true,
            },
        };
        CliInvocation success = InvokeCliAgainstSingleResponse(
            ["memory-add-interaction", "--count", "10"],
            successResponse);

        Check.Equal(CliExitCodes.Success, success.ExitCode);
        Check.Equal(
            TestControlCommands.MemoryAddInteraction,
            success.Request.Command);
        Check.Equal(10L, success.Request.Count!.Value);
        TestControlResponse emittedSuccess =
            TestControlJson.Deserialize<TestControlResponse>(success.Json);
        Check.True(emittedSuccess.Ok);
        Check.NotNull(emittedSuccess.MemoryStatus);
        Check.Equal(
            "memory-01",
            emittedSuccess.MemoryStatus!.PendingMemoryId!);
        Check.True(emittedSuccess.MemoryStatus.PresentationActive);
        Check.Equal(
            "ready",
            emittedSuccess.MemoryStatus.PlaybackPhase!);

        var rejectionResponse = new TestControlResponse
        {
            Ok = false,
            ErrorCode = TestControlErrorCodes.RequiresIsolatedInstance,
            Error =
                "Memory interaction injection requires an isolated test instance.",
        };
        CliInvocation rejection = InvokeCliAgainstSingleResponse(
            ["memory-add-interaction", "--count", "1"],
            rejectionResponse);

        Check.Equal(CliExitCodes.CommandRejected, rejection.ExitCode);
        Check.Equal(
            TestControlCommands.MemoryAddInteraction,
            rejection.Request.Command);
        Check.Equal(1L, rejection.Request.Count!.Value);
        TestControlResponse emittedRejection =
            TestControlJson.Deserialize<TestControlResponse>(rejection.Json);
        Check.False(emittedRejection.Ok);
        Check.Equal(
            TestControlErrorCodes.RequiresIsolatedInstance,
            emittedRejection.ErrorCode!);
    }

    private static TestControlRequest CreateBehaviorRequest(params string[] args)
    {
        CliArguments parsed = CliArguments.Parse(args);
        return BehaviorCommandRequestFactory.Create(parsed);
    }

    private static CliInvocation InvokeCliAgainstSingleResponse(
        IReadOnlyList<string> commandArguments,
        TestControlResponse response)
    {
        string pipeName = $"GuluPet.CliBehaviorTests.{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        TestControlRequest? receivedRequest = null;
        Task serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await server.WaitForConnectionAsync(timeout.Token);
            receivedRequest =
                await TestControlWire.ReadAsync<TestControlRequest>(
                    server,
                    TestControlProtocol.MaxRequestBytes,
                    timeout.Token);
            await TestControlWire.WriteAsync(
                server,
                response,
                TestControlProtocol.MaxResponseBytes,
                timeout.Token);
        }, timeout.Token);

        string[] args =
        [
            .. commandArguments,
            "--pipe",
            pipeName,
            "--timeout-ms",
            "5000",
            "--compact",
        ];
        TextWriter originalOutput = Console.Out;
        using var output = new StringWriter();
        int exitCode;
        try
        {
            Console.SetOut(output);
            exitCode = CliApplication.RunAsync(args).GetAwaiter().GetResult();
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        serverTask.GetAwaiter().GetResult();
        Check.NotNull(receivedRequest);
        return new CliInvocation(
            exitCode,
            output.ToString().Trim(),
            receivedRequest!);
    }

    private sealed record CliInvocation(
        int ExitCode,
        string Json,
        TestControlRequest Request);

    private static TestClipInfo CreateBlinkClip() =>
        new()
        {
            Name = "blink",
            FrameCount = 5,
            Fps = 12,
            Loop = false,
            AssetStatus = "runtime-preview",
        };

    private static ActionProbeSample CreateSample(
        string action,
        int frameIndex,
        int frameCount,
        double fps,
        bool loop) =>
        new()
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            CurrentAction = action,
            FrameIndex = frameIndex,
            FrameCount = frameCount,
            Fps = fps,
            Loop = loop,
            AssetStatus = "runtime-preview",
        };

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }

    private static class Check
    {
        public static void True(bool value)
        {
            if (!value)
            {
                throw new InvalidOperationException("Expected true.");
            }
        }

        public static void False(bool value) => True(!value);

        public static void Null(object? value)
        {
            if (value is not null)
            {
                throw new InvalidOperationException(
                    $"Expected null, got '{value}'.");
            }
        }

        public static void NotNull(object? value)
        {
            if (value is null)
            {
                throw new InvalidOperationException("Expected non-null.");
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

        public static void SequenceEqual<T>(
            IEnumerable<T> expected,
            IEnumerable<T> actual)
        {
            if (!expected.SequenceEqual(actual))
            {
                throw new InvalidOperationException("Sequences were not equal.");
            }
        }

        public static void ThrowsAnyAsync(
            Func<Task> action,
            params Type[] expectedExceptionTypes)
        {
            try
            {
                action().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                if (expectedExceptionTypes.Any(type => type.IsInstanceOfType(exception)))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Expected one of [{string.Join(", ", expectedExceptionTypes.Select(type => type.Name))}], " +
                    $"got {exception.GetType().Name}.",
                    exception);
            }

            throw new InvalidOperationException("Expected the operation to throw.");
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
    }
}
