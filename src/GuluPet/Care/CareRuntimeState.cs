namespace GuluPet.Care;

internal sealed record PendingCareRequest(
    long OperationId,
    string CareAction,
    long? TriggerRequestId,
    string? TriggerBehaviorId);

internal sealed record ActiveCareSession(
    long OperationId,
    string CareAction,
    long? TriggerRequestId,
    string? TriggerBehaviorId,
    long CareRequestId,
    string CareBehaviorId,
    long SessionToken);

internal sealed record CareRequestAttempt(
    long OperationId,
    string CareAction,
    PendingCareRequest? PreviousPending);

/// <summary>
/// Correlates interaction-bar requests with PetController's verified care
/// lifecycle. A provisional request is installed before calling
/// TriggerCareInteraction because first-frame commit can be synchronous.
/// </summary>
internal sealed class CareRuntimeState
{
    internal const string FeedAction = "feed";
    internal const string WaterAction = "water";

    private readonly CareStateTracker _care;
    private long _nextOperationId;

    internal CareRuntimeState(CareStateTracker care)
    {
        _care = care ?? throw new ArgumentNullException(nameof(care));
    }

    internal PendingCareRequest? Pending { get; private set; }

    internal ActiveCareSession? Active { get; private set; }

    internal bool InteractionBusy => Active is not null;

    internal CareRequestAttempt BeginRequest(string careAction)
    {
        ValidateAction(careAction);
        if (Active is not null)
        {
            throw new InvalidOperationException(
                "A committed care interaction is already active.");
        }

        PendingCareRequest? previous = Pending;
        long operationId = checked(++_nextOperationId);
        Pending = new PendingCareRequest(
            operationId,
            careAction,
            TriggerRequestId: null,
            TriggerBehaviorId: null);
        return new CareRequestAttempt(operationId, careAction, previous);
    }

    internal bool CompleteRequest(
        CareRequestAttempt attempt,
        bool accepted,
        long? triggerRequestId,
        string? triggerBehaviorId)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        bool validAcceptedResult = accepted
            && triggerRequestId is > 0
            && !string.IsNullOrWhiteSpace(triggerBehaviorId);

        if (Active is { } active
            && active.OperationId == attempt.OperationId)
        {
            if (validAcceptedResult)
            {
                Active = active with
                {
                    TriggerRequestId = triggerRequestId,
                    TriggerBehaviorId = triggerBehaviorId!.Trim(),
                };
            }

            // A verified synchronous commit is stronger evidence than a
            // contradictory trigger return. Never undo the refill.
            return validAcceptedResult;
        }

        if (Pending is not { } pending
            || pending.OperationId != attempt.OperationId)
        {
            return false;
        }

        if (!validAcceptedResult)
        {
            Pending = attempt.PreviousPending;
            return false;
        }

        Pending = pending with
        {
            TriggerRequestId = triggerRequestId,
            TriggerBehaviorId = triggerBehaviorId!.Trim(),
        };
        return true;
    }

    internal bool TryCommitVerified(
        string careAction,
        long careRequestId,
        string careBehaviorId,
        long sessionToken,
        out CareState state)
    {
        state = _care.Current;
        if (!IsKnownAction(careAction)
            || careRequestId <= 0
            || string.IsNullOrWhiteSpace(careBehaviorId)
            || sessionToken <= 0
            || Active is not null
            || Pending is not { } pending
            || !string.Equals(
                pending.CareAction,
                careAction,
                StringComparison.Ordinal))
        {
            return false;
        }

        Active = new ActiveCareSession(
            pending.OperationId,
            careAction,
            pending.TriggerRequestId,
            pending.TriggerBehaviorId,
            careRequestId,
            careBehaviorId.Trim(),
            sessionToken);
        Pending = null;
        state = string.Equals(
            careAction,
            FeedAction,
            StringComparison.Ordinal)
                ? _care.RefillFood()
                : _care.RefillWater();
        return true;
    }

    internal bool TryTerminateVerified(
        string careAction,
        long sessionToken)
    {
        if (Active is not { } active
            || active.SessionToken != sessionToken
            || !string.Equals(
                active.CareAction,
                careAction,
                StringComparison.Ordinal))
        {
            return false;
        }

        Active = null;
        return true;
    }

    internal bool TryCancelPendingVerifiedTermination(string careAction)
    {
        if (Active is not null
            || Pending is not { } pending
            || !string.Equals(
                pending.CareAction,
                careAction,
                StringComparison.Ordinal))
        {
            return false;
        }

        Pending = null;
        return true;
    }

    internal void Clear()
    {
        Pending = null;
        Active = null;
    }

    private static void ValidateAction(string careAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(careAction);
        if (!IsKnownAction(careAction))
        {
            throw new ArgumentException(
                "Care action must be exactly 'feed' or 'water'.",
                nameof(careAction));
        }
    }

    private static bool IsKnownAction(string careAction) =>
        string.Equals(careAction, FeedAction, StringComparison.Ordinal)
        || string.Equals(careAction, WaterAction, StringComparison.Ordinal);
}
