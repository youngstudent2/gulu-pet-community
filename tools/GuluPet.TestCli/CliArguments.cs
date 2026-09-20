using GuluPet.Testing;

namespace GuluPet.TestCli;

internal sealed class CliArguments
{
    private CliArguments()
    {
    }

    public string PipeName { get; private init; } = TestControlProtocol.PipeName;

    public TimeSpan Timeout { get; private init; } = TestControlClient.DefaultTimeout;

    public bool CompactJson { get; private init; }

    public string Command { get; private init; } = "help";

    public IReadOnlyList<string> CommandArguments { get; private init; } = [];

    public static CliArguments Parse(string[] args)
    {
        var pipeName = TestControlProtocol.PipeName;
        var timeout = TestControlClient.DefaultTimeout;
        var compactJson = false;
        var helpRequested = false;
        var remaining = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument.ToLowerInvariant())
            {
                case "--json":
                case "--pretty":
                    break;
                case "--compact":
                    compactJson = true;
                    break;
                case "--help":
                case "-h":
                    helpRequested = true;
                    break;
                case "--pipe":
                    pipeName = ReadValue(args, ref index, "--pipe");
                    if (string.IsNullOrWhiteSpace(pipeName))
                    {
                        throw new CliUsageException("--pipe requires a non-empty pipe name.");
                    }

                    break;
                case "--timeout-ms":
                    var timeoutValue = ReadInteger(
                        ReadValue(args, ref index, "--timeout-ms"),
                        "--timeout-ms",
                        1,
                        60_000);
                    timeout = TimeSpan.FromMilliseconds(timeoutValue);
                    break;
                default:
                    remaining.Add(argument);
                    break;
            }
        }

        var command = helpRequested || remaining.Count == 0
            ? "help"
            : remaining[0].ToLowerInvariant();
        var commandArguments = helpRequested || remaining.Count == 0
            ? Array.Empty<string>()
            : remaining.Skip(1).ToArray();

        var supportedCommands = new[]
        {
            "help",
            TestControlCommands.Ping,
            TestControlCommands.Status,
            TestControlCommands.Clips,
            TestControlCommands.Play,
            TestControlCommands.Show,
            TestControlCommands.Hide,
            TestControlCommands.ResetPosition,
            TestControlCommands.BehaviorStatus,
            TestControlCommands.BehaviorOpenBrowser,
            TestControlCommands.BehaviorEvaluate,
            TestControlCommands.BehaviorTick,
            TestControlCommands.Enqueue,
            TestControlCommands.StartIfIdle,
            TestControlCommands.BehaviorPreview,
            TestControlCommands.Queue,
            TestControlCommands.BehaviorEvents,
            TestControlCommands.MemoryStatus,
            TestControlCommands.MemoryAddInteraction,
            TestControlCommands.PostcardStatus,
            TestControlCommands.PostcardStartOuting,
            TestControlCommands.PostcardCompleteOuting,
            TestControlCommands.PostcardClaimArrival,
            TestControlCommands.PostcardOpenGallery,
            TestControlCommands.PostcardShow,
            TestControlCommands.PostcardDismissPopup,
            "watch",
            "probe-actions",
        };
        if (!supportedCommands.Contains(command, StringComparer.Ordinal))
        {
            throw new CliUsageException($"Unknown command '{command}'.");
        }

        ValidateSimpleCommandArguments(command, commandArguments);

        return new CliArguments
        {
            PipeName = pipeName,
            Timeout = timeout,
            CompactJson = compactJson,
            Command = command,
            CommandArguments = commandArguments,
        };
    }

    public static int ReadInteger(
        string value,
        string option,
        int minimum,
        int maximum)
    {
        if (!int.TryParse(value, out var parsed) || parsed < minimum || parsed > maximum)
        {
            throw new CliUsageException(
                $"{option} must be an integer from {minimum} through {maximum}.");
        }

        return parsed;
    }

    public static long ReadLong(
        string value,
        string option,
        long minimum,
        long maximum)
    {
        if (!long.TryParse(value, out long parsed) ||
            parsed < minimum ||
            parsed > maximum)
        {
            throw new CliUsageException(
                $"{option} must be an integer from {minimum} through {maximum}.");
        }

        return parsed;
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        index++;
        if (index >= args.Length)
        {
            throw new CliUsageException($"{option} requires a value.");
        }

        return args[index];
    }

    private static void ValidateSimpleCommandArguments(
        string command,
        IReadOnlyList<string> commandArguments)
    {
        if (command is TestControlCommands.Play or
            TestControlCommands.Enqueue or
            TestControlCommands.StartIfIdle or
            TestControlCommands.BehaviorPreview)
        {
            if (commandArguments.Count != 1 ||
                string.IsNullOrWhiteSpace(commandArguments[0]))
            {
                string valueName =
                    command == TestControlCommands.Play
                        ? "action"
                        : "behavior-id";
                throw new CliUsageException(
                    $"Usage: gulu-test {command} <{valueName}>");
            }

            if (command != TestControlCommands.Play &&
                !TestControlProtocol.IsValidBehaviorId(commandArguments[0]))
            {
                throw new CliUsageException(
                    "Behavior ids must contain 1 through 96 lowercase ASCII " +
                    "letters, digits, '_' or '-'.");
            }

            return;
        }

        if (command is "watch" or "probe-actions" ||
            command == TestControlCommands.BehaviorTick ||
            command == TestControlCommands.BehaviorEvents ||
            command == TestControlCommands.MemoryAddInteraction ||
            command == TestControlCommands.PostcardShow)
        {
            return;
        }

        if (commandArguments.Count != 0)
        {
            throw new CliUsageException(
                $"The '{command}' command does not accept positional arguments.");
        }
    }
}

internal sealed class CliUsageException : Exception
{
    public CliUsageException(string message)
        : base(message)
    {
    }
}
