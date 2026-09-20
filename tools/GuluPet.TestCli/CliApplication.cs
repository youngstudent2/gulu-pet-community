using GuluPet.Testing;

namespace GuluPet.TestCli;

internal static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        CliArguments? parsed = null;
        CliJsonWriter? output = null;

        try
        {
            parsed = CliArguments.Parse(args);
            output = new CliJsonWriter(parsed.CompactJson);

            if (parsed.Command == "help")
            {
                output.Write(CreateHelp());
                return CliExitCodes.Success;
            }

            var client = new TestControlClient(parsed.PipeName);
            return parsed.Command switch
            {
                "watch" => await RunWatchAsync(client, parsed, output),
                "probe-actions" => await RunProbeAsync(client, parsed, output),
                _ => await RunSingleCommandAsync(client, parsed, output),
            };
        }
        catch (CliUsageException exception)
        {
            output ??= new CliJsonWriter(parsed?.CompactJson ?? false);
            output.Write(CreateError(
                CliExitCodes.UsageError,
                "usage_error",
                exception.Message));
            return CliExitCodes.UsageError;
        }
        catch (TestControlUnavailableException exception)
        {
            output ??= new CliJsonWriter(parsed?.CompactJson ?? false);
            output.Write(CreateError(
                CliExitCodes.AppUnavailable,
                "app_unavailable",
                exception.Message));
            return CliExitCodes.AppUnavailable;
        }
        catch (TestControlProtocolException exception)
        {
            output ??= new CliJsonWriter(parsed?.CompactJson ?? false);
            output.Write(CreateError(
                CliExitCodes.AppUnavailable,
                "protocol_error",
                exception.Message));
            return CliExitCodes.AppUnavailable;
        }
        catch (OperationCanceledException)
        {
            output ??= new CliJsonWriter(parsed?.CompactJson ?? false);
            output.Write(CreateError(
                CliExitCodes.AppUnavailable,
                "cancelled",
                "The operation was cancelled."));
            return CliExitCodes.AppUnavailable;
        }
        catch (Exception exception)
        {
            output ??= new CliJsonWriter(parsed?.CompactJson ?? false);
            output.Write(CreateError(
                CliExitCodes.AppUnavailable,
                "unexpected_error",
                $"The CLI could not complete the operation: {exception.Message}"));
            return CliExitCodes.AppUnavailable;
        }
    }

    private static async Task<int> RunSingleCommandAsync(
        TestControlClient client,
        CliArguments arguments,
        CliJsonWriter output)
    {
        TestControlRequest request =
            BehaviorCommandRequestFactory.Create(arguments);
        var response = await client.SendAsync(
            request,
            arguments.Timeout);
        output.Write(response);
        return response.Ok
            ? CliExitCodes.Success
            : CliExitCodes.CommandRejected;
    }

    private static async Task<int> RunProbeAsync(
        TestControlClient client,
        CliArguments arguments,
        CliJsonWriter output)
    {
        var options = ProbeOptions.Parse(arguments.CommandArguments);
        var runner = new ActionProbeRunner(client, arguments.Timeout);
        var report = await runner.RunAsync(options);
        output.Write(report);
        return GetProbeExitCode(report);
    }

    private static async Task<int> RunWatchAsync(
        TestControlClient client,
        CliArguments arguments,
        CliJsonWriter output)
    {
        var options = WatchOptions.Parse(arguments.CommandArguments);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var samples = new List<TestRuntimeSnapshot>();
        TestControlResponse? rejectedResponse = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.Seconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await client.SendAsync(
                new TestControlRequest
                {
                    Command = TestControlCommands.Status,
                },
                arguments.Timeout);
            if (!response.Ok)
            {
                rejectedResponse = response;
                break;
            }

            if (response.Snapshot is not null)
            {
                samples.Add(response.Snapshot);
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(
                    Math.Min(options.PollMilliseconds, remaining.TotalMilliseconds)));
        }

        var validSnapshotCount = samples.Count(IsValidRuntimeSnapshot);
        var ok = rejectedResponse is null && validSnapshotCount > 0;
        var errorCode = rejectedResponse?.ErrorCode;
        var error = rejectedResponse?.Error;
        if (rejectedResponse is null && validSnapshotCount == 0)
        {
            errorCode = "missing_valid_snapshot";
            error = "The app did not return a valid runtime snapshot.";
        }

        output.Write(new
        {
            command = "watch",
            ok,
            startedAtUtc,
            completedAtUtc = DateTimeOffset.UtcNow,
            seconds = options.Seconds,
            pollMilliseconds = options.PollMilliseconds,
            validSnapshotCount,
            samples,
            errorCode,
            error,
        });
        return ok
            ? CliExitCodes.Success
            : CliExitCodes.CommandRejected;
    }

    internal static int GetProbeExitCode(ActionProbeReport report) =>
        report.Ok
            ? CliExitCodes.Success
            : report.CommandRejected
                ? CliExitCodes.CommandRejected
                : CliExitCodes.ProbeFailed;

    internal static bool IsValidRuntimeSnapshot(TestRuntimeSnapshot? snapshot) =>
        snapshot is not null
        && snapshot.ProcessId > 0
        && !string.IsNullOrWhiteSpace(snapshot.CurrentAction)
        && snapshot.FrameCount > 0
        && snapshot.FrameIndex >= 0
        && snapshot.FrameIndex < snapshot.FrameCount
        && double.IsFinite(snapshot.Fps)
        && snapshot.Fps > 0;

    private static object CreateHelp() => new
    {
        ok = true,
        tool = "gulu-test",
        protocolVersion = TestControlProtocol.Version,
        defaultPipe = TestControlProtocol.PipeName,
        usage = "gulu-test [--json] [--compact] [--pipe NAME] " +
                "[--timeout-ms N] <command>",
        commands = new[]
        {
            "ping",
            "status",
            "clips",
            "play <action>",
            "show",
            "hide",
            "reset-position",
            "behavior-status",
            "behavior-open-browser",
            "behavior-evaluate",
            "behavior-tick [--seed N]",
            "enqueue <behavior-id>",
            "start-if-idle <behavior-id>",
            "behavior-preview <behavior-id>",
            "queue",
            "behavior-events [--after N]",
            "memory-status",
            "memory-add-interaction --count N",
            "postcard-status",
            "postcard-start-outing",
            "postcard-complete-outing",
            "postcard-claim-arrival",
            "postcard-open-gallery",
            "postcard-show --postcard-id ID",
            "postcard-dismiss-popup",
            "watch [seconds] [--poll-ms N]",
            "probe-actions [action ...] [--hold-ms N] [--poll-ms N]",
            "help",
        },
        notes = new[]
        {
            "JSON is the default output format; --json is accepted for explicit scripts.",
            "The app must be started with --test-control before the pipe is available.",
        },
        exitCodes = new Dictionary<int, string>
        {
            [CliExitCodes.Success] = "success",
            [CliExitCodes.UsageError] = "invalid CLI usage",
            [CliExitCodes.AppUnavailable] = "app unavailable or protocol failure",
            [CliExitCodes.CommandRejected] = "app rejected command",
            [CliExitCodes.ProbeFailed] = "one or more action probes failed",
        },
    };

    private static object CreateError(int exitCode, string errorCode, string error) => new
    {
        ok = false,
        exitCode,
        errorCode,
        error,
    };
}

internal sealed class WatchOptions
{
    public int Seconds { get; private init; } = 10;

    public int PollMilliseconds { get; private init; } = 250;

    public static WatchOptions Parse(IReadOnlyList<string> args)
    {
        var seconds = 10;
        var pollMilliseconds = 250;
        var secondsSet = false;

        for (var index = 0; index < args.Count; index++)
        {
            if (args[index].Equals("--poll-ms", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                if (index >= args.Count)
                {
                    throw new CliUsageException("--poll-ms requires a value.");
                }

                pollMilliseconds = CliArguments.ReadInteger(
                    args[index],
                    "--poll-ms",
                    25,
                    10_000);
                continue;
            }

            if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new CliUsageException($"Unknown watch option '{args[index]}'.");
            }

            if (secondsSet)
            {
                throw new CliUsageException("Usage: gulu-test watch [seconds] [--poll-ms N]");
            }

            seconds = CliArguments.ReadInteger(args[index], "seconds", 1, 3_600);
            secondsSet = true;
        }

        return new WatchOptions
        {
            Seconds = seconds,
            PollMilliseconds = pollMilliseconds,
        };
    }
}
