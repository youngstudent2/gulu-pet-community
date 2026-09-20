namespace GuluPet.Diary;

public sealed class DiaryGenerationFailureObservedEventArgs : EventArgs
{
    internal DiaryGenerationFailureObservedEventArgs(
        DateOnly date,
        DiaryGenerationResult result)
    {
        Date = date;
        Result = result;
    }

    public DateOnly Date { get; }

    public DiaryGenerationResult Result { get; }
}

/// <summary>
/// Runs one idempotent generation pass. HTTP policy is expressed by the
/// generator result: transient proxy and balance failures remain retryable;
/// only definitive request-contract failures are terminal for the diary date.
/// </summary>
public sealed class DiaryGenerationCoordinator : IDisposable
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(30);
    public const int MaximumGenerationsPerPass = 8;

    private readonly DiaryLedger _ledger;
    private readonly DiaryDatePolicy _datePolicy;
    private readonly IDiaryGenerator _generator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public DiaryGenerationCoordinator(
        DiaryLedger ledger,
        DiaryDatePolicy datePolicy,
        IDiaryGenerator generator)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _datePolicy = datePolicy
            ?? throw new ArgumentNullException(nameof(datePolicy));
        _generator = generator
            ?? throw new ArgumentNullException(nameof(generator));
    }

    public event EventHandler<DiaryGenerationFailureObservedEventArgs>?
        FailureObserved;

    public async Task<int> CheckAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_datePolicy.TryGetGenerationTarget(
                nowUtc,
                out DateOnly targetDate)
            || !await _gate.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false))
        {
            return 0;
        }

        var generatedCount = 0;
        try
        {
            _generator.Reconcile(_ledger.Current, targetDate);
            IReadOnlyList<DiaryDayState> candidates =
                _ledger.GetGenerationCandidates(
                    targetDate,
                    nowUtc,
                    RetryInterval);
            foreach (DiaryDayState day in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DiaryGenerationInput input = DiaryPromptBuilder.CreateInput(
                    day,
                    _datePolicy);
                DiaryGenerationResult result = await _generator.GenerateAsync(
                        input,
                        cancellationToken)
                    .ConfigureAwait(false);
                bool acknowledge = false;
                switch (result.Outcome)
                {
                    case DiaryGenerationOutcome.Success:
                        _ledger.RecordGenerationSuccess(
                            day.Date,
                            result.Entry ?? throw new InvalidOperationException(
                                "A successful diary result has no entry."),
                            nowUtc);
                        generatedCount++;
                        acknowledge = true;
                        break;

                    case DiaryGenerationOutcome.RetryableFailure:
                        RecordFailure(
                            day.Date,
                            DiaryGenerationFailureDisposition.Retryable,
                            result,
                            nowUtc);
                        // A transient failure normally applies to the shared
                        // proxy or this installation. Stop this backlog pass;
                        // oldest-attempt ordering rotates the next slot.
                        return generatedCount;

                    case DiaryGenerationOutcome.TerminalFailure:
                        RecordFailure(
                            day.Date,
                            DiaryGenerationFailureDisposition.Terminal,
                            result,
                            nowUtc);
                        acknowledge = true;
                        break;

                    case DiaryGenerationOutcome.InsufficientBalance:
                        RecordFailure(
                            day.Date,
                            DiaryGenerationFailureDisposition.Retryable,
                            result.RetryAfter is null
                                ? result with
                                {
                                    RetryAfter = TimeSpan.FromHours(6),
                                }
                                : result,
                            nowUtc);
                        return generatedCount;

                    default:
                        throw new InvalidOperationException(
                            $"Unknown diary generation outcome '{result.Outcome}'.");
                }

                if (acknowledge)
                {
                    _generator.Acknowledge(input, result);
                }

                if (generatedCount >= MaximumGenerationsPerPass)
                {
                    return generatedCount;
                }
            }

            return generatedCount;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private void RecordFailure(
        DateOnly date,
        DiaryGenerationFailureDisposition disposition,
        DiaryGenerationResult result,
        DateTimeOffset nowUtc)
    {
        _ledger.RecordGenerationFailure(
            date,
            disposition,
            result.Code,
            nowUtc,
            result.RetryAfter is { } retryAfter
                ? nowUtc + retryAfter
                : null);
        FailureObserved?.Invoke(
            this,
            new DiaryGenerationFailureObservedEventArgs(date, result));
    }
}
