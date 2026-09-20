namespace GuluPet.Behavior;

public readonly record struct PetStateTransitionToken(long Value)
{
    public override string ToString() => Value.ToString();
}

public sealed record HierarchicalPetStateSnapshot
{
    public required StablePetState StableState { get; init; }

    public required PetLifecyclePhase Phase { get; init; }

    public required long Revision { get; init; }

    public PetStateTransitionToken? PreparedSleepEntryToken { get; init; }

    public PetStateTransitionToken? ActiveTransitionToken { get; init; }
}

/// <summary>
/// Owns the mutually-exclusive semantic pet state and the lifecycle subphase.
/// A sleep entry is prepared before playback, committed when its first frame is
/// visible, and becomes stable Sleeping only after the enter clip completes.
/// </summary>
public sealed class HierarchicalPetStateMachine
{
    private long _nextTransitionToken;
    private long _revision;
    private PetStateTransitionToken? _preparedSleepEntryToken;
    private PetStateTransitionToken? _activeTransitionToken;

    public HierarchicalPetStateMachine(
        StablePetState restoredState = StablePetState.Normal)
    {
        Restore(restoredState);
    }

    public StablePetState StableState { get; private set; }

    public PetLifecyclePhase Phase { get; private set; }

    public long Revision => _revision;

    public PetStateTransitionToken? PreparedSleepEntryToken =>
        _preparedSleepEntryToken;

    public PetStateTransitionToken? ActiveTransitionToken =>
        _activeTransitionToken;

    public HierarchicalPetStateSnapshot Snapshot() =>
        new()
        {
            StableState = StableState,
            Phase = Phase,
            Revision = Revision,
            PreparedSleepEntryToken = PreparedSleepEntryToken,
            ActiveTransitionToken = ActiveTransitionToken,
        };

    /// <summary>
    /// Reserves a sleep transition without changing the public state or phase.
    /// The returned token becomes active only after the enter clip's first frame.
    /// </summary>
    public PetStateTransitionToken PrepareSleepEntry()
    {
        Ensure(
            StableState == StablePetState.Normal &&
            Phase == PetLifecyclePhase.Idle &&
            _preparedSleepEntryToken is null &&
            _activeTransitionToken is null,
            "Sleep entry can only be prepared from idle Normal.");

        var token = NextToken();
        _preparedSleepEntryToken = token;
        Touch();
        return token;
    }

    public bool CommitSleepEntryFirstFrame(PetStateTransitionToken token)
    {
        if (_preparedSleepEntryToken != token ||
            StableState != StablePetState.Normal ||
            Phase != PetLifecyclePhase.Idle)
        {
            return false;
        }

        _preparedSleepEntryToken = null;
        _activeTransitionToken = token;
        Phase = PetLifecyclePhase.SleepingEntering;
        Touch();
        return true;
    }

    public bool CompleteSleepEntry(PetStateTransitionToken token)
    {
        if (_activeTransitionToken != token ||
            Phase != PetLifecyclePhase.SleepingEntering)
        {
            return false;
        }

        _activeTransitionToken = null;
        StableState = StablePetState.Sleeping;
        Phase = PetLifecyclePhase.SleepingLooping;
        Touch();
        return true;
    }

    /// <summary>
    /// Cancels either a prepared or already-visible sleep enter. An enter hard
    /// cut always returns to Normal rather than pretending sleep was reached.
    /// </summary>
    public bool CancelSleepEntry(PetStateTransitionToken token)
    {
        if (_preparedSleepEntryToken == token)
        {
            _preparedSleepEntryToken = null;
            Touch();
            return true;
        }

        if (_activeTransitionToken != token ||
            Phase != PetLifecyclePhase.SleepingEntering)
        {
            return false;
        }

        _activeTransitionToken = null;
        StableState = StablePetState.Normal;
        Phase = PetLifecyclePhase.Idle;
        Touch();
        return true;
    }

    public PetStateTransitionToken BeginSleepExit()
    {
        Ensure(
            StableState == StablePetState.Sleeping &&
            Phase == PetLifecyclePhase.SleepingLooping &&
            _preparedSleepEntryToken is null &&
            _activeTransitionToken is null,
            "Sleep exit can only begin from stable Sleeping.");

        var token = NextToken();
        _activeTransitionToken = token;
        Phase = PetLifecyclePhase.SleepingExiting;
        Touch();
        return token;
    }

    public bool CompleteSleepExit(PetStateTransitionToken token)
    {
        if (_activeTransitionToken != token ||
            Phase != PetLifecyclePhase.SleepingExiting)
        {
            return false;
        }

        _activeTransitionToken = null;
        StableState = StablePetState.Normal;
        Phase = PetLifecyclePhase.Idle;
        Touch();
        return true;
    }

    /// <summary>
    /// Cancelling an exit leaves the semantic state Sleeping and resumes its
    /// stable loop. A subsequent forced state transition may replace it.
    /// </summary>
    public bool CancelSleepExit(PetStateTransitionToken token)
    {
        if (_activeTransitionToken != token ||
            Phase != PetLifecyclePhase.SleepingExiting)
        {
            return false;
        }

        _activeTransitionToken = null;
        StableState = StablePetState.Sleeping;
        Phase = PetLifecyclePhase.SleepingLooping;
        Touch();
        return true;
    }

    public PetStateTransitionToken BeginDragging()
    {
        Ensure(
            StableState != StablePetState.Dragging,
            "A drag transaction is already active.");

        InvalidateTransitions();
        var token = NextToken();
        _activeTransitionToken = token;
        StableState = StablePetState.Dragging;
        Phase = PetLifecyclePhase.DraggingFollowing;
        Touch();
        return token;
    }

    public bool CompleteDragging(PetStateTransitionToken token)
    {
        if (_activeTransitionToken != token ||
            StableState != StablePetState.Dragging ||
            Phase != PetLifecyclePhase.DraggingFollowing)
        {
            return false;
        }

        _activeTransitionToken = null;
        StableState = StablePetState.Normal;
        Phase = PetLifecyclePhase.Idle;
        Touch();
        return true;
    }

    /// <summary>
    /// Restores a persisted stable state. Sleeping resumes directly in Looping;
    /// it never synthesizes or replays an enter transition.
    /// </summary>
    public void Restore(StablePetState state)
    {
        InvalidateTransitions();
        StableState = state;
        Phase = state switch
        {
            StablePetState.Normal => PetLifecyclePhase.Idle,
            StablePetState.Sleeping => PetLifecyclePhase.SleepingLooping,
            StablePetState.Dragging => PetLifecyclePhase.DraggingFollowing,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
        Touch();
    }

    public void ForceNormal()
    {
        InvalidateTransitions();
        StableState = StablePetState.Normal;
        Phase = PetLifecyclePhase.Idle;
        Touch();
    }

    private PetStateTransitionToken NextToken() =>
        new(checked(++_nextTransitionToken));

    private void InvalidateTransitions()
    {
        _preparedSleepEntryToken = null;
        _activeTransitionToken = null;
    }

    private void Touch() => _revision = checked(_revision + 1);

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
