using System.IO;

namespace GuluPet.Diary;

/// <summary>
/// Serializes durable diary mutations. A candidate state is persisted before
/// it becomes visible so a failed write cannot create an in-memory-only event.
/// </summary>
public sealed class DiaryLedger
{
    public const int MaximumRetryableDays = 90;
    private readonly object _gate = new();
    private readonly DiaryStateStore? _store;
    private DiaryState _state;

    public DiaryLedger(
        DiaryStateStore? store = null,
        DiaryState? initialState = null)
    {
        _store = store;
        _state = DiaryState.ValidateAndSnapshot(
            initialState ?? store?.Load() ?? DiaryState.CreateDefault());
    }

    public DiaryState Current
    {
        get
        {
            lock (_gate)
            {
                return DiaryState.ValidateAndSnapshot(_state);
            }
        }
    }

    public void AddRuntime(
        DateOnly date,
        long elapsedSeconds,
        DateTimeOffset observedAtUtc)
    {
        if (date == default)
        {
            throw new ArgumentOutOfRangeException(nameof(date));
        }

        if (elapsedSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }

        AddRuntimeSegments(
            [new DiaryRuntimeSegment(date, elapsedSeconds)],
            observedAtUtc);
    }

    public void AddRuntimeSegments(
        IReadOnlyList<DiaryRuntimeSegment> segments,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (observedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(observedAtUtc));
        }

        if (segments.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            DiaryState candidate = _state;
            foreach (DiaryRuntimeSegment segment in segments)
            {
                if (segment.Date == default || segment.ElapsedSeconds <= 0)
                {
                    throw new ArgumentException(
                        "Diary runtime segments require a date and positive " +
                        "elapsed seconds.",
                        nameof(segments));
                }

                DiaryDayState day = candidate.Days.FirstOrDefault(
                    item => item.Date == segment.Date)
                    ?? DiaryDayState.Create(segment.Date, observedAtUtc);
                DiaryDayState changed = day with
                {
                    RuntimeSeconds = checked(
                        day.RuntimeSeconds + segment.ElapsedSeconds),
                    UpdatedAtUtc = observedAtUtc,
                };
                candidate = candidate with
                {
                    Days = candidate.Days
                        .Where(item => item.Date != changed.Date)
                        .Append(changed)
                        .ToArray(),
                    UpdatedAtUtc = observedAtUtc,
                };
            }

            Commit(candidate);
        }
    }

    public void RecordInteraction(
        DateOnly date,
        string kind,
        DateTimeOffset occurredAtUtc)
    {
        if (!DiaryInteractionKinds.IsKnown(kind))
        {
            throw new ArgumentException(
                $"Unknown diary interaction kind '{kind}'.",
                nameof(kind));
        }

        MutateDay(
            date,
            occurredAtUtc,
            day =>
            {
                var counts = day.Interactions.ToDictionary(
                    static item => item.Kind,
                    static item => item.Count,
                    StringComparer.Ordinal);
                counts[kind] = checked(counts.GetValueOrDefault(kind) + 1);
                return day with
                {
                    Interactions = counts
                        .Select(static item => new DiaryInteractionCount
                        {
                            Kind = item.Key,
                            Count = item.Value,
                        })
                        .ToArray(),
                };
            });
    }

    public bool RecordPostcard(
        DateOnly date,
        DiaryUnlockRecord postcard,
        DateTimeOffset observedAtUtc) =>
        RecordUnlock(
            date,
            postcard,
            observedAtUtc,
            isPostcard: true);

    public bool RecordMemory(
        DateOnly date,
        DiaryUnlockRecord memory,
        DateTimeOffset observedAtUtc) =>
        RecordUnlock(
            date,
            memory,
            observedAtUtc,
            isPostcard: false);

    public void RecordWeather(
        DateOnly calendarDate,
        DiaryWeatherSnapshot weather,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(weather);
        MutateDay(
            calendarDate,
            observedAtUtc,
            day =>
            {
                if (day.Entry is not null
                    || day.Weather?.ObservedAtUtc >= weather.ObservedAtUtc)
                {
                    return day;
                }

                return day with
                {
                    Weather = weather,
                };
            });
    }

    public void RecordGenerationSuccess(
        DateOnly date,
        GeneratedDiaryEntry entry,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(entry);
        MutateDay(
            date,
            observedAtUtc,
            day =>
            {
                if (day.Entry is not null)
                {
                    throw new InvalidOperationException(
                        $"Diary '{date:yyyy-MM-dd}' is already generated.");
                }

                return day with
                {
                    Entry = entry,
                    GenerationFailure = null,
                };
            });
    }

    public void RecordGenerationFailure(
        DateOnly date,
        DiaryGenerationFailureDisposition disposition,
        string code,
        DateTimeOffset attemptedAtUtc,
        DateTimeOffset? retryNotBeforeUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (retryNotBeforeUtc is { } notBefore
            && notBefore < attemptedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(retryNotBeforeUtc));
        }
        MutateDay(
            date,
            attemptedAtUtc,
            day =>
            {
                if (day.Entry is not null)
                {
                    return day;
                }

                int attempts = checked(
                    (day.GenerationFailure?.AttemptCount ?? 0) + 1);
                return day with
                {
                    GenerationFailure = new DiaryGenerationFailure
                    {
                        Disposition = disposition,
                        Code = code,
                        AttemptCount = attempts,
                        LastAttemptAtUtc = attemptedAtUtc,
                        RetryNotBeforeUtc = retryNotBeforeUtc,
                    },
                };
            });
    }

    public IReadOnlyList<DiaryDayState> GetGenerationCandidates(
        DateOnly targetDate,
        DateTimeOffset nowUtc,
        TimeSpan retryInterval)
    {
        if (targetDate == default)
        {
            throw new ArgumentOutOfRangeException(nameof(targetDate));
        }

        if (retryInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryInterval));
        }

        lock (_gate)
        {
            DateOnly oldestEligibleDate = GetOldestEligibleDate(targetDate);
            return _state.Days
                .Where(day =>
                    // Retry backlog is an intentionally finite recovery
                    // window. Older failures remain as local history, but are
                    // archived from network generation by this cutoff.
                    day.Date >= oldestEligibleDate
                    &&
                    (day.Date == targetDate
                        || (day.Date < targetDate
                            && day.GenerationFailure?.Disposition
                                == DiaryGenerationFailureDisposition.Retryable))
                    && day.Entry is null
                    && day.HasMeaningfulRecord
                    && !day.IsGenerationTerminal
                    && (day.GenerationFailure is null
                        || day.GenerationFailure.Disposition
                            != DiaryGenerationFailureDisposition.Retryable
                        || nowUtc - day.GenerationFailure.LastAttemptAtUtc
                            >= retryInterval
                            && (day.GenerationFailure.RetryNotBeforeUtc is null
                                || nowUtc
                                    >= day.GenerationFailure.RetryNotBeforeUtc)))
                // A fresh current-day diary must not be starved by an old
                // retry backlog. Failed dates then rotate by oldest attempt
                // so one rate-limited date cannot monopolize every slot.
                .OrderBy(static day => day.GenerationFailure is null ? 0 : 1)
                .ThenBy(static day =>
                    day.GenerationFailure?.LastAttemptAtUtc
                    ?? DateTimeOffset.MinValue)
                .ThenBy(static day => day.Date)
                .Take(MaximumRetryableDays)
                .Select(static day => day with
                {
                    Interactions = day.Interactions
                        .Select(static item => item with { })
                        .ToArray(),
                    Postcards = day.Postcards
                        .Select(static item => item with { })
                        .ToArray(),
                    Memories = day.Memories
                        .Select(static item => item with { })
                        .ToArray(),
                })
                .ToArray();
        }
    }

    internal static DateOnly GetOldestEligibleDate(DateOnly targetDate)
    {
        if (targetDate == default)
        {
            throw new ArgumentOutOfRangeException(nameof(targetDate));
        }

        return DateOnly.FromDayNumber(Math.Max(
            DateOnly.MinValue.DayNumber,
            targetDate.DayNumber - (MaximumRetryableDays - 1)));
    }

    private bool RecordUnlock(
        DateOnly date,
        DiaryUnlockRecord unlock,
        DateTimeOffset observedAtUtc,
        bool isPostcard)
    {
        ArgumentNullException.ThrowIfNull(unlock);
        lock (_gate)
        {
            DiaryDayState? existingDay = _state.Days.FirstOrDefault(day =>
                (isPostcard ? day.Postcards : day.Memories).Any(record =>
                    string.Equals(record.Id, unlock.Id, StringComparison.Ordinal)));
            if (existingDay is not null)
            {
                IReadOnlyList<DiaryUnlockRecord> records = isPostcard
                    ? existingDay.Postcards
                    : existingDay.Memories;
                DiaryUnlockRecord existing = records.Single(record =>
                    string.Equals(record.Id, unlock.Id, StringComparison.Ordinal));
                string? description = string.IsNullOrWhiteSpace(unlock.Description)
                    ? existing.Description
                    : unlock.Description;
                if (string.Equals(
                    existing.Description,
                    description,
                    StringComparison.Ordinal))
                {
                    return false;
                }

                DiaryUnlockRecord enriched = existing with
                {
                    Description = description,
                };
                DiaryUnlockRecord[] updatedRecords = records
                    .Select(record => string.Equals(
                            record.Id,
                            unlock.Id,
                            StringComparison.Ordinal)
                        ? enriched
                        : record)
                    .ToArray();
                DiaryDayState enrichedDay = isPostcard
                    ? existingDay with { Postcards = updatedRecords }
                    : existingDay with { Memories = updatedRecords };
                Commit(ReplaceDay(enrichedDay, observedAtUtc));
                return true;
            }

            DiaryDayState day = FindOrCreateDay(date, observedAtUtc);
            DiaryDayState changed = isPostcard
                ? day with
                {
                    Postcards = day.Postcards.Append(unlock).ToArray(),
                }
                : day with
                {
                    Memories = day.Memories.Append(unlock).ToArray(),
                };
            Commit(ReplaceDay(changed, observedAtUtc));
            return true;
        }
    }

    private void MutateDay(
        DateOnly date,
        DateTimeOffset observedAtUtc,
        Func<DiaryDayState, DiaryDayState> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (observedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(observedAtUtc));
        }

        lock (_gate)
        {
            DiaryDayState day = FindOrCreateDay(date, observedAtUtc);
            DiaryDayState changed = mutation(day);
            if (ReferenceEquals(changed, day) || changed == day)
            {
                return;
            }

            Commit(ReplaceDay(changed, observedAtUtc));
        }
    }

    private DiaryDayState FindOrCreateDay(
        DateOnly date,
        DateTimeOffset observedAtUtc) =>
        _state.Days.FirstOrDefault(day => day.Date == date)
        ?? DiaryDayState.Create(date, observedAtUtc);

    private DiaryState ReplaceDay(
        DiaryDayState changed,
        DateTimeOffset observedAtUtc)
    {
        DiaryDayState updatedDay = changed with
        {
            UpdatedAtUtc = observedAtUtc,
        };
        return _state with
        {
            Days = _state.Days
                .Where(day => day.Date != updatedDay.Date)
                .Append(updatedDay)
                .ToArray(),
            UpdatedAtUtc = observedAtUtc,
        };
    }

    private void Commit(DiaryState candidate)
    {
        DiaryState snapshot = DiaryState.ValidateAndSnapshot(candidate);
        _store?.Save(snapshot);
        _state = snapshot;
    }
}
