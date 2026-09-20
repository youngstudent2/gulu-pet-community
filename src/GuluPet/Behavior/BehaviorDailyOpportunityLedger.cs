namespace GuluPet.Behavior;

public sealed record BehaviorDailyOpportunity(
    string Key,
    DateOnly LocalDate,
    DateTimeOffset ObservedAt,
    int AcceptedCount,
    DateTimeOffset? LastAcceptedAt);

public sealed record BehaviorDailyOpportunityUsage(
    DateOnly LocalDate,
    int AcceptedCount,
    DateTimeOffset? LastAcceptedAt);

public interface IBehaviorDailyOpportunityLedger
{
    BehaviorDailyOpportunityUsage Read(
        string key,
        DateOnly localDate);

    bool TryRecordAccepted(
        string key,
        DateOnly localDate,
        DateTimeOffset acceptedAt,
        int maximumAcceptedCount);
}

public sealed class InMemoryBehaviorDailyOpportunityLedger
    : IBehaviorDailyOpportunityLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<string, BehaviorDailyOpportunityUsage> _usage =
        new(StringComparer.Ordinal);

    public BehaviorDailyOpportunityUsage Read(
        string key,
        DateOnly localDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            return _usage.TryGetValue(
                       key,
                       out BehaviorDailyOpportunityUsage? usage) &&
                   usage.LocalDate == localDate
                ? usage
                : new BehaviorDailyOpportunityUsage(
                    localDate,
                    AcceptedCount: 0,
                    LastAcceptedAt: null);
        }
    }

    public bool TryRecordAccepted(
        string key,
        DateOnly localDate,
        DateTimeOffset acceptedAt,
        int maximumAcceptedCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumAcceptedCount,
            1);
        if (DateOnly.FromDateTime(acceptedAt.Date) != localDate)
        {
            throw new ArgumentException(
                "The acceptance timestamp must belong to the local date.",
                nameof(acceptedAt));
        }

        lock (_gate)
        {
            BehaviorDailyOpportunityUsage current = ReadUnderLock(
                key,
                localDate);
            if (current.AcceptedCount >= maximumAcceptedCount)
            {
                return false;
            }

            _usage[key] = current with
            {
                AcceptedCount = checked(current.AcceptedCount + 1),
                LastAcceptedAt = acceptedAt,
            };
            return true;
        }
    }

    private BehaviorDailyOpportunityUsage ReadUnderLock(
        string key,
        DateOnly localDate) =>
        _usage.TryGetValue(
            key,
            out BehaviorDailyOpportunityUsage? usage) &&
        usage.LocalDate == localDate
            ? usage
            : new BehaviorDailyOpportunityUsage(
                localDate,
                AcceptedCount: 0,
                LastAcceptedAt: null);
}
