using GuluPet.Testing;

namespace GuluPet.TestCli;

internal static class BehaviorCommandRequestFactory
{
    public static TestControlRequest Create(CliArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return arguments.Command switch
        {
            TestControlCommands.Play => new TestControlRequest
            {
                Command = arguments.Command,
                Action = arguments.CommandArguments[0],
            },
            TestControlCommands.Enqueue or
            TestControlCommands.StartIfIdle or
            TestControlCommands.BehaviorPreview =>
                new TestControlRequest
                {
                    Command = arguments.Command,
                    BehaviorId = arguments.CommandArguments[0],
                },
            TestControlCommands.BehaviorTick => CreateTickRequest(arguments),
            TestControlCommands.BehaviorEvents => CreateEventsRequest(arguments),
            TestControlCommands.MemoryAddInteraction =>
                CreateMemoryAddInteractionRequest(arguments),
            TestControlCommands.PostcardShow =>
                CreatePostcardShowRequest(arguments),
            _ => new TestControlRequest
            {
                Command = arguments.Command,
            },
        };
    }

    private static TestControlRequest CreateTickRequest(CliArguments arguments)
    {
        BehaviorTickOptions options =
            BehaviorTickOptions.Parse(arguments.CommandArguments);
        return new TestControlRequest
        {
            Command = arguments.Command,
            Seed = options.Seed,
        };
    }

    private static TestControlRequest CreateEventsRequest(CliArguments arguments)
    {
        BehaviorEventsOptions options =
            BehaviorEventsOptions.Parse(arguments.CommandArguments);
        return new TestControlRequest
        {
            Command = arguments.Command,
            After = options.After,
        };
    }

    private static TestControlRequest CreateMemoryAddInteractionRequest(
        CliArguments arguments)
    {
        MemoryAddInteractionOptions options =
            MemoryAddInteractionOptions.Parse(arguments.CommandArguments);
        return new TestControlRequest
        {
            Command = arguments.Command,
            Count = options.Count,
        };
    }

    private static TestControlRequest CreatePostcardShowRequest(
        CliArguments arguments)
    {
        PostcardShowOptions options =
            PostcardShowOptions.Parse(arguments.CommandArguments);
        return new TestControlRequest
        {
            Command = arguments.Command,
            PostcardId = options.PostcardId,
        };
    }
}

internal sealed class BehaviorTickOptions
{
    public int? Seed { get; private init; }

    public static BehaviorTickOptions Parse(IReadOnlyList<string> args)
    {
        int? seed = null;

        for (var index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (!argument.Equals("--seed", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliUsageException(
                    $"Unknown behavior-tick option '{argument}'.");
            }

            if (seed is not null)
            {
                throw new CliUsageException(
                    "The --seed option may only be specified once.");
            }

            index++;
            if (index >= args.Count)
            {
                throw new CliUsageException("--seed requires a value.");
            }

            seed = CliArguments.ReadInteger(
                args[index],
                "--seed",
                0,
                int.MaxValue);
        }

        return new BehaviorTickOptions { Seed = seed };
    }
}

internal sealed class BehaviorEventsOptions
{
    public long? After { get; private init; }

    public static BehaviorEventsOptions Parse(IReadOnlyList<string> args)
    {
        long? after = null;

        for (var index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (!argument.Equals("--after", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliUsageException(
                    $"Unknown behavior-events option '{argument}'.");
            }

            if (after is not null)
            {
                throw new CliUsageException(
                    "The --after option may only be specified once.");
            }

            index++;
            if (index >= args.Count)
            {
                throw new CliUsageException("--after requires a value.");
            }

            after = CliArguments.ReadLong(
                args[index],
                "--after",
                0,
                long.MaxValue);
        }

        return new BehaviorEventsOptions { After = after };
    }
}

internal sealed class MemoryAddInteractionOptions
{
    public required long Count { get; init; }

    public static MemoryAddInteractionOptions Parse(
        IReadOnlyList<string> args)
    {
        long? count = null;
        for (var index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (!argument.Equals("--count", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliUsageException(
                    $"Unknown memory-add-interaction option '{argument}'.");
            }

            if (count is not null)
            {
                throw new CliUsageException(
                    "The --count option may only be specified once.");
            }

            index++;
            if (index >= args.Count)
            {
                throw new CliUsageException("--count requires a value.");
            }

            count = CliArguments.ReadLong(
                args[index],
                "--count",
                1,
                10);
        }

        if (count is null)
        {
            throw new CliUsageException(
                "Usage: gulu-test memory-add-interaction --count N");
        }

        return new MemoryAddInteractionOptions { Count = count.Value };
    }
}

internal sealed class PostcardShowOptions
{
    public required string PostcardId { get; init; }

    public static PostcardShowOptions Parse(IReadOnlyList<string> args)
    {
        string? postcardId = null;
        for (var index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (!argument.Equals(
                    "--postcard-id",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CliUsageException(
                    $"Unknown postcard-show option '{argument}'.");
            }

            if (postcardId is not null)
            {
                throw new CliUsageException(
                    "The --postcard-id option may only be specified once.");
            }

            index++;
            if (index >= args.Count)
            {
                throw new CliUsageException("--postcard-id requires a value.");
            }

            postcardId = args[index];
        }

        if (postcardId is null)
        {
            throw new CliUsageException(
                "Usage: gulu-test postcard-show --postcard-id ID");
        }

        if (!TestControlProtocol.IsValidPostcardId(postcardId))
        {
            throw new CliUsageException(
                "Postcard ids must contain 1 through 96 lowercase ASCII " +
                "letters, digits, '_' or '-'.");
        }

        return new PostcardShowOptions { PostcardId = postcardId };
    }
}
