namespace GuluPet.Diary;

public sealed record DiaryRuntimeSegment(
    DateOnly Date,
    long ElapsedSeconds);

public sealed record DiaryRuntimeObservation(
    long Sequence,
    TimeSpan MonotonicElapsed,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<DiaryRuntimeSegment> Segments);

/// <summary>
/// Converts monotonic online time into diary-day segments. The owner accepts
/// an observation only after its segments were persisted, preventing a failed
/// store write from silently losing online time.
/// </summary>
public sealed class DiaryRuntimeAccumulator
{
    private readonly DiaryDatePolicy _datePolicy;
    private TimeSpan _acceptedElapsed;
    private long _nextSequence;
    private bool _suspended;

    public DiaryRuntimeAccumulator(
        DiaryDatePolicy datePolicy,
        TimeSpan initialMonotonicElapsed = default)
    {
        _datePolicy = datePolicy
            ?? throw new ArgumentNullException(nameof(datePolicy));
        if (initialMonotonicElapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialMonotonicElapsed));
        }

        _acceptedElapsed = initialMonotonicElapsed;
    }

    public bool IsSuspended => _suspended;

    public DiaryRuntimeObservation Observe(
        DateTimeOffset nowUtc,
        TimeSpan monotonicElapsed)
    {
        if (monotonicElapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(monotonicElapsed));
        }

        long sequence = checked(++_nextSequence);
        if (_suspended || monotonicElapsed <= _acceptedElapsed)
        {
            return new DiaryRuntimeObservation(
                sequence,
                monotonicElapsed,
                nowUtc,
                []);
        }

        TimeSpan elapsed = monotonicElapsed - _acceptedElapsed;
        DateTimeOffset startsAtUtc = nowUtc - elapsed;
        var secondsByDate = new Dictionary<DateOnly, long>();
        DateTimeOffset cursor = startsAtUtc;
        while (cursor < nowUtc)
        {
            DateOnly date = _datePolicy.GetActivityDate(cursor);
            DiaryDayPeriod period = _datePolicy.GetPeriod(date);
            DateTimeOffset segmentEnd = period.EndsAtUtc < nowUtc
                ? period.EndsAtUtc
                : nowUtc;
            if (segmentEnd <= cursor)
            {
                segmentEnd = nowUtc;
            }

            long seconds = Math.Max(
                0,
                (long)Math.Round(
                    (segmentEnd - cursor).TotalSeconds,
                    MidpointRounding.AwayFromZero));
            if (seconds > 0)
            {
                secondsByDate[date] = checked(
                    secondsByDate.GetValueOrDefault(date) + seconds);
            }

            cursor = segmentEnd;
        }

        return new DiaryRuntimeObservation(
            sequence,
            monotonicElapsed,
            nowUtc,
            secondsByDate
                .OrderBy(static item => item.Key)
                .Select(static item => new DiaryRuntimeSegment(
                    item.Key,
                    item.Value))
                .ToArray());
    }

    public void Accept(DiaryRuntimeObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Sequence != _nextSequence)
        {
            throw new InvalidOperationException(
                "Only the latest diary runtime observation can be accepted.");
        }

        _acceptedElapsed = observation.MonotonicElapsed;
    }

    public void Suspend(TimeSpan monotonicElapsed)
    {
        if (monotonicElapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(monotonicElapsed));
        }

        _acceptedElapsed = monotonicElapsed;
        _suspended = true;
    }

    public void Resume(TimeSpan monotonicElapsed)
    {
        if (monotonicElapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(monotonicElapsed));
        }

        _acceptedElapsed = monotonicElapsed;
        _suspended = false;
    }
}
