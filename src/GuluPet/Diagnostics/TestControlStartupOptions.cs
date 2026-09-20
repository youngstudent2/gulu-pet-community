using GuluPet.Testing;

namespace GuluPet.Diagnostics;

internal static class TestControlStartupOptions
{
    private const string PipeOption = "--test-control-pipe";
    private const int MaxPipeNameLength = 128;

    public static string ResolvePipeName(
        IReadOnlyList<string> arguments,
        bool commandLineTestControlEnabled)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // A custom server endpoint is deliberately limited to explicit
        // command-line test mode. Normal startup and the environment-variable
        // development path keep the stable protocol endpoint.
        if (!commandLineTestControlEnabled)
        {
            return TestControlProtocol.PipeName;
        }

        string? pipeName = null;
        for (int index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(
                    arguments[index],
                    PipeOption,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pipeName is not null)
            {
                throw new ArgumentException(
                    $"{PipeOption} may only be specified once.",
                    nameof(arguments));
            }

            if (index + 1 >= arguments.Count ||
                arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"{PipeOption} requires a pipe name.",
                    nameof(arguments));
            }

            pipeName = arguments[++index];
            if (!IsSafePipeName(pipeName))
            {
                throw new ArgumentException(
                    $"{PipeOption} requires 1 through {MaxPipeNameLength} " +
                    "ASCII letters, digits, '.', '_' or '-'.",
                    nameof(arguments));
            }
        }

        return pipeName ?? TestControlProtocol.PipeName;
    }

    private static bool IsSafePipeName(string? pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName) ||
            pipeName.Length > MaxPipeNameLength ||
            string.Equals(pipeName, "anonymous", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return pipeName.All(static character =>
            character is >= 'a' and <= 'z' or
                >= 'A' and <= 'Z' or
                >= '0' and <= '9' or
                '.' or '_' or '-');
    }
}
