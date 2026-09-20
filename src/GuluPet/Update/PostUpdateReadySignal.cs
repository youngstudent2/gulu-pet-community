namespace GuluPet.Update;

public sealed class PostUpdateReadySignal
{
    public const string ArgumentPrefix = "--post-update-ready-event=";

    public const string EventNamePrefix = "Local\\GuluPet.Update.Ready.";

    private PostUpdateReadySignal(string eventName)
    {
        EventName = eventName;
    }

    public string EventName { get; }

    public static string CreateEventName(Guid updateId) =>
        EventNamePrefix + updateId.ToString("N");

    /// <summary>
    /// Returns null when the option is absent. A malformed or duplicate update
    /// readiness option throws so startup can fail closed.
    /// </summary>
    public static PostUpdateReadySignal? Parse(
        IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? option = null;

        foreach (string argument in arguments)
        {
            if (!argument.StartsWith(
                    "--post-update-ready-event",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (option is not null)
            {
                throw new FormatException(
                    "The post-update readiness option may appear only once.");
            }

            option = argument;
        }

        if (option is null)
        {
            return null;
        }

        if (!option.StartsWith(ArgumentPrefix, StringComparison.Ordinal))
        {
            throw new FormatException(
                "The post-update readiness option has an invalid format.");
        }

        string eventName = option[ArgumentPrefix.Length..];
        if (!eventName.StartsWith(EventNamePrefix, StringComparison.Ordinal))
        {
            throw new FormatException(
                "The post-update readiness event name is not trusted.");
        }

        string identifier = eventName[EventNamePrefix.Length..];
        if (identifier.Length != 32
            || identifier.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f'))
            || !Guid.TryParseExact(identifier, "N", out Guid updateId)
            || !string.Equals(
                eventName,
                CreateEventName(updateId),
                StringComparison.Ordinal))
        {
            throw new FormatException(
                "The post-update readiness event ID is invalid.");
        }

        return new PostUpdateReadySignal(eventName);
    }

    /// <summary>
    /// Opens and signals an event created by the updater. This method never
    /// creates a new event, so an absent updater handshake fails closed.
    /// </summary>
    public void SignalExisting()
    {
        using EventWaitHandle readyEvent = EventWaitHandle.OpenExisting(
            EventName);
        if (!readyEvent.Set())
        {
            throw new InvalidOperationException(
                "The post-update readiness event could not be signaled.");
        }
    }
}
