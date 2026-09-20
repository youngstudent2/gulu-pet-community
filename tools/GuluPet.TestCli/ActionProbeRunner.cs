using System.Diagnostics;
using GuluPet.Testing;

namespace GuluPet.TestCli;

internal sealed class ActionProbeRunner
{
    private readonly TestControlClient _client;
    private readonly TimeSpan _requestTimeout;

    public ActionProbeRunner(TestControlClient client, TimeSpan requestTimeout)
    {
        _client = client;
        _requestTimeout = requestTimeout;
    }

    public async Task<ActionProbeReport> RunAsync(
        ProbeOptions options,
        CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var clipsResponse = await SendAsync(
            TestControlCommands.Clips,
            cancellationToken: cancellationToken);
        if (!clipsResponse.Ok)
        {
            return FailedReport(
                startedAtUtc,
                options,
                clipsResponse.Error ?? "The app rejected the clips command.",
                commandRejected: true,
                clipsResponse.ErrorCode);
        }

        var availableClips = clipsResponse.Clips ?? [];
        if (availableClips.Count == 0)
        {
            return FailedReport(
                startedAtUtc,
                options,
                "The app reported no animation clips.");
        }

        var requestedActions = options.Actions.Count > 0
            ? options.Actions
            : availableClips.Select(clip => clip.Name).ToArray();
        if (requestedActions.Count == 0)
        {
            return FailedReport(startedAtUtc, options, "The app reported no animation clips.");
        }

        var initialStatusResponse = await SendAsync(
            TestControlCommands.Status,
            cancellationToken: cancellationToken);
        if (!initialStatusResponse.Ok)
        {
            return FailedReport(
                startedAtUtc,
                options,
                initialStatusResponse.Error
                    ?? "The app rejected the initial status command.",
                commandRejected: true,
                initialStatusResponse.ErrorCode);
        }

        var originalAction = ResolveRestoreAction(
            initialStatusResponse.Snapshot,
            availableClips);
        if (string.IsNullOrWhiteSpace(originalAction))
        {
            return FailedReport(
                startedAtUtc,
                options,
                "The app did not expose an action that can be restored.");
        }

        var beginResponse = await SendAsync(
            TestControlCommands.BeginProbe,
            cancellationToken: cancellationToken);
        if (!beginResponse.Ok)
        {
            return FailedReport(
                startedAtUtc,
                options,
                beginResponse.Error ?? "The app rejected the begin-probe command.",
                commandRejected: true,
                beginResponse.ErrorCode);
        }

        ActionProbeReport report;
        TestControlResponse restoreResponse;
        TestControlResponse endResponse;
        try
        {
            report = await ProbeActionsAsync(
                startedAtUtc,
                availableClips,
                requestedActions,
                options,
                cancellationToken);
        }
        finally
        {
            // Cleanup deliberately ignores the caller cancellation token. Once
            // begin-probe succeeds, restoring the original action and ending
            // the probe session must both be attempted.
            try
            {
                restoreResponse = await SendAsync(
                    TestControlCommands.Play,
                    originalAction,
                    CancellationToken.None);
            }
            finally
            {
                endResponse = await SendAsync(
                    TestControlCommands.EndProbe,
                    cancellationToken: CancellationToken.None);
            }
        }

        if (!restoreResponse.Ok)
        {
            report = WithCleanupFailure(
                report,
                restoreResponse.ErrorCode,
                restoreResponse.Error
                    ?? $"The app rejected restoring '{originalAction}'.");
        }
        else
        {
            report = WithRestoredAction(report, originalAction);
        }

        if (!endResponse.Ok)
        {
            return WithCleanupFailure(
                report,
                endResponse.ErrorCode,
                endResponse.Error ?? "The app rejected the end-probe command.");
        }

        return report;
    }

    internal static string? ResolveRestoreAction(
        TestRuntimeSnapshot? snapshot,
        IReadOnlyList<TestClipInfo> availableClips)
    {
        if (!string.IsNullOrWhiteSpace(snapshot?.CurrentAction))
        {
            return snapshot.CurrentAction.Trim();
        }

        return availableClips.FirstOrDefault(clip =>
                   clip.Name.Equals(
                       "idle_breathe",
                       StringComparison.OrdinalIgnoreCase))
               ?.Name
               ?? availableClips.FirstOrDefault()?.Name;
    }

    private async Task<ActionProbeReport> ProbeActionsAsync(
        DateTimeOffset startedAtUtc,
        IReadOnlyList<TestClipInfo> availableClips,
        IReadOnlyList<string> requestedActions,
        ProbeOptions options,
        CancellationToken cancellationToken)
    {
        var results = new List<ActionProbeResult>();
        foreach (var action in requestedActions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var clip = availableClips.FirstOrDefault(
                candidate => candidate.Name.Equals(action, StringComparison.OrdinalIgnoreCase));
            results.Add(await ProbeActionAsync(
                action,
                clip,
                options,
                cancellationToken));
        }

        return new ActionProbeReport
        {
            Ok = results.All(result => result.Ok),
            CommandRejected = results.Any(result => result.CommandRejected),
            ErrorCode = results
                .FirstOrDefault(result => result.CommandRejected)
                ?.ErrorCode,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            HoldMilliseconds = options.HoldMilliseconds,
            PollMilliseconds = options.PollMilliseconds,
            Results = results,
        };
    }

    private async Task<ActionProbeResult> ProbeActionAsync(
        string action,
        TestClipInfo? clip,
        ProbeOptions options,
        CancellationToken cancellationToken)
    {
        var samples = new List<ActionProbeSample>();
        var playResponse = await SendAsync(
            TestControlCommands.Play,
            action,
            cancellationToken);
        if (!playResponse.Ok)
        {
            return new ActionProbeResult
            {
                Action = action,
                Ok = false,
                CommandRejected = true,
                ErrorCode = playResponse.ErrorCode,
                ExpectedFrameCount = clip?.FrameCount ?? 0,
                ExpectedFps = clip?.Fps ?? 0,
                ExpectedLoop = clip?.Loop ?? false,
                Error = playResponse.Error ?? "The app rejected the play command.",
            };
        }

        AddSample(playResponse.Snapshot, samples);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < options.HoldMilliseconds)
        {
            await Task.Delay(options.PollMilliseconds, cancellationToken);
            var statusResponse = await SendAsync(
                TestControlCommands.Status,
                cancellationToken: cancellationToken);
            if (!statusResponse.Ok)
            {
                return new ActionProbeResult
                {
                    Action = action,
                    Ok = false,
                    CommandRejected = true,
                    ErrorCode = statusResponse.ErrorCode,
                    ExpectedFrameCount = clip?.FrameCount ?? 0,
                    ExpectedFps = clip?.Fps ?? 0,
                    ExpectedLoop = clip?.Loop ?? false,
                    Samples = samples,
                    Error = statusResponse.Error ?? "The app rejected the status command.",
                };
            }

            AddSample(statusResponse.Snapshot, samples);
        }

        return EvaluateSamples(action, clip, samples);
    }

    internal static ActionProbeResult EvaluateSamples(
        string action,
        TestClipInfo? clip,
        IReadOnlyList<ActionProbeSample> samples)
    {
        var requestedSamples = samples
            .Where(sample => action.Equals(
                sample.CurrentAction,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var distinctFrames = requestedSamples
            .Select(sample => sample.FrameIndex)
            .Distinct()
            .Order()
            .ToArray();
        var firstRequestedSample = requestedSamples.FirstOrDefault();
        var expectedFrameCount = clip?.FrameCount
                                 ?? firstRequestedSample?.FrameCount
                                 ?? 0;
        var expectedFps = clip?.Fps
                          ?? firstRequestedSample?.Fps
                          ?? 0;
        var expectedLoop = clip?.Loop
                           ?? firstRequestedSample?.Loop
                           ?? false;
        var requiredDistinctFrames = expectedFrameCount > 1 ? 2 : 1;
        var observedAction = requestedSamples.Length > 0;
        var frameIndicesInRange = observedAction
                                  && expectedFrameCount > 0
                                  && requestedSamples.All(sample =>
                                      sample.FrameIndex >= 0
                                      && sample.FrameIndex < expectedFrameCount);
        var metadataConsistent = clip is not null
                                 && clip.FrameCount > 0
                                 && double.IsFinite(clip.Fps)
                                 && clip.Fps > 0
                                 && observedAction
                                 && requestedSamples.All(sample =>
                                     sample.FrameCount == clip.FrameCount
                                     && NearlyEqual(sample.Fps, clip.Fps)
                                     && sample.Loop == clip.Loop
                                     && string.Equals(
                                         sample.AssetStatus,
                                         clip.AssetStatus,
                                         StringComparison.OrdinalIgnoreCase));
        var frameProgressed = distinctFrames.Length >= requiredDistinctFrames;
        var ok = observedAction
                 && frameIndicesInRange
                 && metadataConsistent
                 && frameProgressed;
        var errors = new List<string>();
        if (!observedAction)
        {
            errors.Add("The requested action was not observed.");
        }
        else
        {
            if (!metadataConsistent)
            {
                errors.Add("Runtime animation metadata did not match the clips response.");
            }

            if (!frameIndicesInRange)
            {
                errors.Add(
                    $"Observed a frame index outside 0 through {expectedFrameCount - 1}.");
            }

            if (!frameProgressed)
            {
                errors.Add(
                    $"Observed {distinctFrames.Length} distinct frame(s); " +
                    $"{requiredDistinctFrames} required.");
            }
        }

        return new ActionProbeResult
        {
            Action = action,
            Ok = ok,
            ExpectedFrameCount = expectedFrameCount,
            ExpectedFps = expectedFps,
            ExpectedLoop = expectedLoop,
            ObservedRequestedAction = observedAction,
            FrameIndicesInRange = frameIndicesInRange,
            MetadataConsistent = metadataConsistent,
            DistinctFrameIndices = distinctFrames,
            Samples = samples,
            Error = ok ? null : string.Join(" ", errors),
        };
    }

    private static bool NearlyEqual(double left, double right) =>
        double.IsFinite(left)
        && double.IsFinite(right)
        && Math.Abs(left - right) <= 0.000_001;

    private Task<TestControlResponse> SendAsync(
        string command,
        string? action = null,
        CancellationToken cancellationToken = default) =>
        _client.SendAsync(
            new TestControlRequest
            {
                Command = command,
                Action = action,
            },
            _requestTimeout,
            cancellationToken);

    private static void AddSample(
        TestRuntimeSnapshot? snapshot,
        ICollection<ActionProbeSample> samples)
    {
        if (snapshot is null)
        {
            return;
        }

        samples.Add(new ActionProbeSample
        {
            CapturedAtUtc = snapshot.CapturedAtUtc,
            CurrentAction = snapshot.CurrentAction,
            FrameIndex = snapshot.FrameIndex,
            FrameCount = snapshot.FrameCount,
            Fps = snapshot.Fps,
            Loop = snapshot.Loop,
            AssetStatus = snapshot.AssetStatus,
        });
    }

    private static ActionProbeReport FailedReport(
        DateTimeOffset startedAtUtc,
        ProbeOptions options,
        string error,
        bool commandRejected = false,
        string? errorCode = null) =>
        new()
        {
            Ok = false,
            CommandRejected = commandRejected,
            ErrorCode = errorCode,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            HoldMilliseconds = options.HoldMilliseconds,
            PollMilliseconds = options.PollMilliseconds,
            Error = error,
        };

    private static ActionProbeReport WithCleanupFailure(
        ActionProbeReport report,
        string? errorCode,
        string error) =>
        new()
        {
            Ok = false,
            CommandRejected = true,
            ErrorCode = errorCode ?? report.ErrorCode,
            StartedAtUtc = report.StartedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            HoldMilliseconds = report.HoldMilliseconds,
            PollMilliseconds = report.PollMilliseconds,
            RestoredAction = report.RestoredAction,
            Results = report.Results,
            Error = string.IsNullOrWhiteSpace(report.Error)
                ? error
                : $"{report.Error} {error}",
        };

    private static ActionProbeReport WithRestoredAction(
        ActionProbeReport report,
        string restoredAction) =>
        new()
        {
            Ok = report.Ok,
            CommandRejected = report.CommandRejected,
            ErrorCode = report.ErrorCode,
            StartedAtUtc = report.StartedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            HoldMilliseconds = report.HoldMilliseconds,
            PollMilliseconds = report.PollMilliseconds,
            RestoredAction = restoredAction,
            Results = report.Results,
            Error = report.Error,
        };
}

internal sealed class ProbeOptions
{
    public int HoldMilliseconds { get; init; } = 750;

    public int PollMilliseconds { get; init; } = 50;

    public IReadOnlyList<string> Actions { get; init; } = [];

    public static ProbeOptions Parse(IReadOnlyList<string> args)
    {
        var holdMilliseconds = 750;
        var pollMilliseconds = 50;
        var actions = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--hold-ms":
                    holdMilliseconds = CliArguments.ReadInteger(
                        ReadValue(args, ref index, "--hold-ms"),
                        "--hold-ms",
                        100,
                        60_000);
                    break;
                case "--poll-ms":
                    pollMilliseconds = CliArguments.ReadInteger(
                        ReadValue(args, ref index, "--poll-ms"),
                        "--poll-ms",
                        10,
                        5_000);
                    break;
                default:
                    if (args[index].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new CliUsageException(
                            $"Unknown probe-actions option '{args[index]}'.");
                    }

                    actions.Add(args[index]);
                    break;
            }
        }

        if (pollMilliseconds >= holdMilliseconds)
        {
            throw new CliUsageException("--poll-ms must be less than --hold-ms.");
        }

        return new ProbeOptions
        {
            HoldMilliseconds = holdMilliseconds,
            PollMilliseconds = pollMilliseconds,
            Actions = actions,
        };
    }

    private static string ReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string option)
    {
        index++;
        if (index >= args.Count)
        {
            throw new CliUsageException($"{option} requires a value.");
        }

        return args[index];
    }
}
