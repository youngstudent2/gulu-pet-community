using System.IO;
using GuluPet.Domain;

namespace GuluPet.Behavior;

public sealed record BehaviorMutationResult
{
    public required bool Accepted { get; init; }

    public required BehaviorId? BehaviorId { get; init; }

    public required long? RequestId { get; init; }

    public required string? RejectionReason { get; init; }

    public SoftQueueEnqueueResult? QueueResult { get; init; }

    public string? InteractionKey { get; init; }

}

public sealed record BehaviorTickResult
{
    public required bool WasDue { get; init; }

    public required BehaviorSelectionPlan Evaluation { get; init; }

    public required BehaviorId? SelectedBehaviorId { get; init; }

    public required IReadOnlyList<BehaviorId> AttemptOrder { get; init; }

    public SoftQueueEnqueueResult? QueueResult { get; init; }

    public required TimeSpan? NextDueAt { get; init; }
}

public sealed record BehaviorTickTrace
{
    public required TimeSpan EvaluatedAt { get; init; }

    public required BehaviorContextSnapshot Context { get; init; }

    public required EmotionState Emotions { get; init; }

    public required HierarchicalPetStateSnapshot State { get; init; }

    public required BehaviorTickResult Result { get; init; }
}

public sealed class BehaviorInteractionOptions
{
    public int RepeatedClickThreshold { get; init; } = 3;

    public TimeSpan RepeatedClickWindow { get; init; } =
        TimeSpan.FromMilliseconds(700);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            RepeatedClickThreshold,
            2);
        if (RepeatedClickWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RepeatedClickWindow),
                "The repeated-click window must be positive.");
        }
    }
}

public sealed record BehaviorCoordinatorSnapshot
{
    public required TimeSpan CapturedAt { get; init; }

    public required bool Started { get; init; }

    public required bool Paused { get; init; }

    public required BehaviorContextSnapshot Context { get; init; }

    public required EmotionState Emotions { get; init; }

    public required string RelationshipStage { get; init; }

    public required HierarchicalPetStateSnapshot State { get; init; }

    public required BehaviorSessionSnapshot? Session { get; init; }

    public required SoftBehaviorQueueSnapshot Queue { get; init; }

    public required TimeSpan? NextTickAt { get; init; }

    public required TimeSpan? LastTickInterval { get; init; }

    public required BehaviorSelectionPlan? LastEvaluation { get; init; }

    public required BehaviorUtilityHistorySnapshot UtilityHistory { get; init; }

    public required BehaviorTerminalRecord? LastTerminal { get; init; }

    public required BehaviorInteractionLeaseSnapshot? InteractionLease {
        get;
        init;
    }

    public required int CatalogBehaviorCount { get; init; }
}

public sealed record BehaviorInteractionLeaseSnapshot
{
    public required long Generation { get; init; }

    public required string Key { get; init; }

    public required string TriggerTag { get; init; }

    public required BehaviorId BehaviorId { get; init; }

    public required long RequestId { get; init; }

    public BehaviorSessionToken? SessionToken { get; init; }

    public required TimeSpan AcquiredAt { get; init; }

}

/// <summary>
/// Owns behavior arbitration on one dispatcher thread. Presentation is kept
/// behind ports, while queueing, utility selection, lifecycle state and timing
/// remain deterministic and unit-testable.
/// </summary>
public sealed class BehaviorRuntimeCoordinator
{
    private static readonly TimeSpan DefaultSoftTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan InteractionTtl = TimeSpan.FromSeconds(10);
    private const string AutomaticWakeTransitionTag =
        "transition:wake-from-sleep";

    private readonly BehaviorCatalog _catalog;
    private readonly EmotionState _emotions;
    private readonly IMonotonicClock _clock;
    private readonly BehaviorRandomStreams _randomStreams;
    private readonly BehaviorInteractionOptions _interactionOptions;
    private readonly HierarchicalPetStateMachine _stateMachine;
    private readonly SoftBehaviorQueue _queue;
    private readonly BehaviorUtilityEngine _utility;
    private readonly ActiveTickScheduler _tick;
    private readonly BehaviorSessionRunner _sessions;
    private readonly BehaviorEventJournal _journal;
    private readonly ClickBurstClassifier _clickBurst;
    private readonly Dictionary<long, SessionExecution> _executions = [];
    private readonly Dictionary<long, SessionExecution> _startingExecutions = [];
    private readonly Dictionary<long, InteractionLease>
        _interactionLeasesByRequestId = [];
    private readonly HashSet<long> _journaledSessionStarts = [];
    private readonly List<PendingAutomaticWake> _pendingAutomaticWakes = [];

    private TimeSpan _lastEmotionRegressionAt;
    private BehaviorContextSnapshot _context = BehaviorContextSnapshot.Empty;
    private BehaviorSelectionPlan? _lastEvaluation;
    private BehaviorRequest? _startingRequest;
    private ExecutionOrigin _startingOrigin;
    private PetStateTransitionToken? _dragTransition;
    private string? _pendingWakeInteractionTag;
    private bool _pendingWakeInteractionCanYieldToToolbar;
    private InteractionLease? _interactionLease;
    private long _nextRequestId;
    private long _nextInteractionLeaseGeneration;
    private int _mutationDepth;
    private bool _pumpRequested;
    private bool _pumping;
    private bool _started;
    private bool _paused;
    private bool _pausedForTopLevelPresentation;
    private TaskCompletionSource<bool>?
        _topLevelPresentationSuspensionCompletion;

    public BehaviorRuntimeCoordinator(
        BehaviorCatalog catalog,
        IBehaviorAnimationSessionPort animation,
        IBehaviorBubbleSessionPort bubbles,
        EmotionState? initialEmotions = null,
        IMonotonicClock? clock = null,
        BehaviorRandomStreams? randomStreams = null,
        HierarchicalPetStateMachine? stateMachine = null,
        BehaviorEventJournal? journal = null,
        BehaviorInteractionOptions? interactionOptions = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _clock = clock ?? SystemMonotonicClock.Instance;
        _emotions = (initialEmotions ?? new EmotionState()).CloneNormalized();
        _lastEmotionRegressionAt = _clock.Elapsed;
        _randomStreams = randomStreams ?? BehaviorRandomStreams.CreateSystem();
        _interactionOptions = interactionOptions ?? new BehaviorInteractionOptions();
        _interactionOptions.Validate();
        _clickBurst = new ClickBurstClassifier(
            _clock,
            _interactionOptions.RepeatedClickThreshold,
            _interactionOptions.RepeatedClickWindow);
        _stateMachine = stateMachine ?? new HierarchicalPetStateMachine();
        _queue = new SoftBehaviorQueue(
            _clock,
            _randomStreams.QueueEviction);
        _utility = new BehaviorUtilityEngine(
            _catalog,
            _randomStreams,
            _clock);
        _tick = new ActiveTickScheduler(
            _clock,
            _randomStreams.TickInterval);
        _sessions = new BehaviorSessionRunner(
            _clock,
            _stateMachine,
            animation ?? throw new ArgumentNullException(nameof(animation)),
            bubbles ?? throw new ArgumentNullException(nameof(bubbles)),
            animationVariantRandom: _randomStreams.AnimationVariant);
        _journal = journal ?? new BehaviorEventJournal(_clock);

        _sessions.Committed += OnSessionCommitted;
        _sessions.Terminated += OnSessionTerminated;
    }

    public BehaviorSessionRunner Sessions => _sessions;

    public event Action<BehaviorTickTrace>? ActiveTickEvaluated;

    public event Action<BehaviorDailyOpportunity>? DailyOpportunityPresented;

    internal event Action<double>? AffectionChanged;

    internal event Action<BehaviorSessionCommitRecord>? SessionCommitted;

    internal event Action<BehaviorTerminalRecord>? SessionTerminated;

    internal event Action<
        BehaviorTerminalRecord,
        BehaviorInteractionLeaseSnapshot>? InteractionSessionTerminated;

    public HierarchicalPetStateMachine StateMachine => _stateMachine;

    public string RelationshipStage =>
        RelationshipStages.FromAffection(_emotions.Affection);

    public void Start()
    {
        StartCore(pausedForTopLevelPresentation: false);
    }

    /// <summary>
    /// Starts the runtime without selecting a behavior. This is used when a
    /// higher-level presentation must be recovered before pet interaction is
    /// enabled, for example after an interrupted memory playback.
    /// </summary>
    public void StartSuspendedForTopLevelPresentation()
    {
        StartCore(pausedForTopLevelPresentation: true);
    }

    private void StartCore(bool pausedForTopLevelPresentation)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _paused = pausedForTopLevelPresentation;
        _pausedForTopLevelPresentation = pausedForTopLevelPresentation;
        if (pausedForTopLevelPresentation)
        {
            return;
        }

        _tick.Start();
        RequestPump();
    }

    public void PauseForExternalControl()
    {
        if (!_started || _paused)
        {
            return;
        }

        CancelPendingTopLevelPresentationSuspension();
        _paused = true;
        _pausedForTopLevelPresentation = false;
        _tick.Cancel();
        _clickBurst.Cancel();
        _pendingWakeInteractionTag = null;
        _pendingWakeInteractionCanYieldToToolbar = false;
        _pendingAutomaticWakes.Clear();
        _sessions.Interrupt(
            BehaviorTerminalStatus.Interrupted,
            "Behavior runtime paused for external test control.");
    }

    public void ResumeAfterExternalControl()
    {
        if (!_started || !_paused || _pausedForTopLevelPresentation)
        {
            return;
        }

        _paused = false;
        _tick.Start();
        RequestPump();
    }

    /// <summary>
    /// Transfers an active external-control pause to top-level presentation
    /// ownership without briefly resuming the behavior runtime.
    /// </summary>
    public bool TryTransferExternalControlPauseToTopLevelPresentation()
    {
        if (!_started || !_paused || _pausedForTopLevelPresentation)
        {
            return false;
        }

        _pausedForTopLevelPresentation = true;
        return true;
    }

    /// <summary>
    /// Requests a clean presentation boundary after the currently accepted
    /// interaction has fully finished. Sleep exit plus its deferred feedback
    /// count as one interaction chain. The runtime preserves queued work and
    /// pauses before another automatic or fallback behavior can start.
    /// </summary>
    public Task SuspendAfterCurrentInteractionAsync()
    {
        if (!_started)
        {
            throw new InvalidOperationException(
                "Behavior runtime must be started before requesting a " +
                "top-level presentation boundary.");
        }

        if (_paused)
        {
            if (_pausedForTopLevelPresentation)
            {
                return Task.CompletedTask;
            }

            throw new InvalidOperationException(
                "Behavior runtime is paused for external control.");
        }

        if (_topLevelPresentationSuspensionCompletion is { } existing)
        {
            return existing.Task;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _topLevelPresentationSuspensionCompletion = completion;
        _ = TryEnterTopLevelPresentationBoundary();
        return completion.Task;
    }

    public void ResumeAfterTopLevelPresentation()
    {
        if (!_started || !_paused || !_pausedForTopLevelPresentation)
        {
            return;
        }

        _pausedForTopLevelPresentation = false;
        _paused = false;
        _tick.Start();
        RequestPump();
    }

    public void CancelPendingTopLevelPresentationSuspension()
    {
        TaskCompletionSource<bool>? completion =
            _topLevelPresentationSuspensionCompletion;
        _topLevelPresentationSuspensionCompletion = null;
        completion?.TrySetCanceled();
    }

    public void SetVisible(bool visible) => _sessions.SetVisible(visible);

    public void UpdateContext(BehaviorContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Revision < _context.Revision)
        {
            throw new InvalidOperationException(
                "Behavior context revisions cannot move backwards.");
        }

        _context = context;
    }

    public BehaviorSelectionPlan Evaluate(
        BehaviorRequestSource source,
        IReadOnlyCollection<BehaviorId>? candidateBehaviorIds = null)
    {
        RefreshEmotions();
        var plan = _utility.Evaluate(
            new BehaviorUtilityEvaluationInput
            {
                State = _stateMachine.StableState,
                Source = source,
                Emotions = _emotions,
                Context = CurrentContext(),
                CandidateBehaviorIds = candidateBehaviorIds,
            });
        _lastEvaluation = plan;
        return plan;
    }

    public BehaviorSelectionPlan EvaluateActiveTick() =>
        Evaluate(
            BehaviorRequestSource.ActiveTick,
            FindByTag("trigger:active_tick"));

    public BehaviorMutationResult EnqueueSoft(
        BehaviorId behaviorId,
        BehaviorRequestSource source = BehaviorRequestSource.TestControl,
        TimeSpan? ttl = null,
        string? dedupeKey = null)
    {
        if (!_catalog.TryGet(behaviorId, out var definition) ||
            definition is null)
        {
            return RejectedMutation(behaviorId, "unknown-behavior");
        }

        if (!definition.Queueable)
        {
            return RejectedMutation(behaviorId, "behavior-not-queueable");
        }

        return EnqueueSoftCore(
            definition,
            source,
            ttl ?? DefaultSoftTtl,
            dedupeKey);
    }

    private BehaviorMutationResult EnqueueAutomaticSelection(
        BehaviorId behaviorId,
        BehaviorRequestSource source,
        TimeSpan ttl,
        string dedupeKey,
        BehaviorDailyOpportunity? dailyOpportunity = null)
    {
        if (!_catalog.TryGet(behaviorId, out var definition) ||
            definition is null)
        {
            return RejectedMutation(behaviorId, "unknown-behavior");
        }

        if (!definition.Queueable &&
            !BehaviorDefinitionSemantics
                .IsAutomaticExternalStateLifecycle(definition))
        {
            return RejectedMutation(
                behaviorId,
                "behavior-not-automatically-queueable");
        }

        return EnqueueSoftCore(
            definition,
            source,
            ttl,
            dedupeKey,
            dailyOpportunity);
    }

    private BehaviorMutationResult EnqueueSoftCore(
        BehaviorDefinition definition,
        BehaviorRequestSource source,
        TimeSpan ttl,
        string? dedupeKey,
        BehaviorDailyOpportunity? dailyOpportunity = null)
    {
        BehaviorId behaviorId = definition.Id;
        var request = CreateRequest(
            behaviorId,
            source,
            BehaviorSwitchMode.Queued,
            ttl,
            dedupeKey,
            dailyOpportunity: dailyOpportunity);
        _journal.Append(
            BehaviorEventKind.RequestReceived,
            behaviorId,
            request.RequestId);

        SoftQueueEnqueueResult queueResult;
        try
        {
            queueResult = _queue.Enqueue(request);
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            _journal.Append(
                BehaviorEventKind.RequestRejected,
                behaviorId,
                request.RequestId,
                reason: exception.Message);
            return new BehaviorMutationResult
            {
                Accepted = false,
                BehaviorId = behaviorId,
                RequestId = request.RequestId,
                RejectionReason = exception.Message,
            };
        }

        bool accepted = queueResult.Disposition is
            SoftQueueEnqueueDisposition.Enqueued or
            SoftQueueEnqueueDisposition.Refreshed or
            SoftQueueEnqueueDisposition.ReplacedActiveTick;
        _journal.Append(
            accepted
                ? queueResult.Disposition == SoftQueueEnqueueDisposition.Refreshed
                    ? BehaviorEventKind.RequestRefreshed
                    : BehaviorEventKind.RequestEnqueued
                : BehaviorEventKind.RequestRejected,
            behaviorId,
            request.RequestId,
            queueResult.GroupId,
            reason: accepted ? null : queueResult.Diagnostic);
        if (accepted)
        {
            RequestPump();
        }

        return new BehaviorMutationResult
        {
            Accepted = accepted,
            BehaviorId = behaviorId,
            RequestId = request.RequestId,
            RejectionReason = accepted ? null : queueResult.Diagnostic,
            QueueResult = queueResult,
        };
    }

    public BehaviorMutationResult TryStartImmediate(
        BehaviorId behaviorId,
        BehaviorRequestSource source = BehaviorRequestSource.TestControl)
    {
        if (source is
            BehaviorRequestSource.Interaction or
            BehaviorRequestSource.ForcedInteraction)
        {
            return RejectedMutation(
                behaviorId,
                "interaction-source-requires-trigger");
        }

        if (HasPendingTopLevelPresentationSuspension)
        {
            return RejectedMutation(
                behaviorId,
                "top-level-presentation-pending");
        }

        BehaviorMutationResult result = TryStartImmediateCore(
            behaviorId,
            source,
            interactionKey: null,
            interactionTriggerTag: null);
        if (result.Accepted)
        {
            _clickBurst.Cancel();
        }

        return result;
    }

    public BehaviorMutationResult StartManualPreview(
        BehaviorId behaviorId)
    {
        if (!_started || _paused)
        {
            return RejectedMutation(
                behaviorId,
                _paused ? "runtime-paused" : "runtime-not-started");
        }

        if (HasPendingTopLevelPresentationSuspension)
        {
            return RejectedMutation(
                behaviorId,
                "top-level-presentation-pending");
        }

        if (_dragTransition is not null)
        {
            return RejectedMutation(behaviorId, "dragging");
        }

        if (!_catalog.TryGet(behaviorId, out BehaviorDefinition? definition) ||
            definition is null)
        {
            return RejectedMutation(behaviorId, "unknown-behavior");
        }

        StablePetState previewState =
            definition.AllowedStates.Contains(StablePetState.Normal)
                ? StablePetState.Normal
                : definition.AllowedStates.Contains(StablePetState.Sleeping)
                    ? StablePetState.Sleeping
                    : StablePetState.Dragging;
        if (previewState == StablePetState.Dragging)
        {
            return RejectedMutation(
                behaviorId,
                "manual-preview-does-not-synthesize-dragging");
        }

        var request = CreateRequest(
            behaviorId,
            BehaviorRequestSource.ManualPreview,
            BehaviorSwitchMode.ImmediateIfIdle,
            Max(
                InteractionTtl,
                definition.MaximumDuration + TimeSpan.FromSeconds(2)));
        _journal.Append(
            BehaviorEventKind.RequestReceived,
            behaviorId,
            request.RequestId);

        BeginMutation();
        bool accepted = false;
        string? rejectionReason = null;
        try
        {
            _pendingWakeInteractionTag = null;
            _pendingWakeInteractionCanYieldToToolbar = false;
            _clickBurst.Cancel();
            _tick.NotifyInteractionHandled();
            if (_sessions.HasActiveSession)
            {
                _sessions.Interrupt(
                    BehaviorTerminalStatus.Superseded,
                    $"Manual preview switched to '{definition.Id}'.");
            }

            _stateMachine.Restore(previewState);
            accepted = TryStartSession(
                request,
                definition,
                ExecutionOrigin.Immediate,
                out rejectionReason);
        }
        finally
        {
            EndMutation();
        }

        if (!accepted)
        {
            _journal.Append(
                BehaviorEventKind.RequestRejected,
                behaviorId,
                request.RequestId,
                reason: rejectionReason);
        }

        return new BehaviorMutationResult
        {
            Accepted = accepted,
            BehaviorId = behaviorId,
            RequestId = request.RequestId,
            RejectionReason = accepted ? null : rejectionReason,
        };
    }

    private BehaviorMutationResult TryStartInteraction(
        BehaviorId behaviorId,
        BehaviorRequestSource source,
        string interactionKey,
        string interactionTriggerTag,
        bool canYieldToToolbar) =>
        TryStartImmediateCore(
            behaviorId,
            source,
            interactionKey,
            interactionTriggerTag,
            canYieldToToolbar);

    private BehaviorMutationResult TryStartImmediateCore(
        BehaviorId behaviorId,
        BehaviorRequestSource source,
        string? interactionKey,
        string? interactionTriggerTag,
        bool interactionCanYieldToToolbar = false)
    {
        if (!_catalog.TryGet(behaviorId, out var definition) ||
            definition is null)
        {
            return RejectedMutation(behaviorId, "unknown-behavior");
        }

        long? interactionLeaseGeneration = interactionKey is null
            ? null
            : checked(++_nextInteractionLeaseGeneration);
        var request = CreateRequest(
            behaviorId,
            source,
            BehaviorSwitchMode.ImmediateIfIdle,
            Max(InteractionTtl, definition.MaximumDuration + TimeSpan.FromSeconds(2)),
            interactionKey: interactionKey,
            interactionTriggerTag: interactionTriggerTag,
            interactionLeaseGeneration: interactionLeaseGeneration);
        _journal.Append(
            BehaviorEventKind.RequestReceived,
            behaviorId,
            request.RequestId,
            details: InteractionRequestDetails(request));

        if (!CanStartImmediate(out string? rejectionReason) ||
            !_sessions.CanStart(request, definition, out rejectionReason))
        {
            _journal.Append(
                BehaviorEventKind.RequestRejected,
                behaviorId,
                request.RequestId,
                reason: rejectionReason);
            return new BehaviorMutationResult
            {
                Accepted = false,
                BehaviorId = behaviorId,
                RequestId = request.RequestId,
                RejectionReason = rejectionReason,
                InteractionKey = interactionKey,
            };
        }

        if (interactionKey is not null &&
            interactionTriggerTag is not null &&
            interactionLeaseGeneration is { } generation)
        {
            AcquireInteractionLease(
                new InteractionLease(
                    generation,
                    interactionKey,
                    interactionTriggerTag,
                    behaviorId,
                    request.RequestId,
                    interactionCanYieldToToolbar,
                    _clock.Elapsed));
        }

        BeginMutation();
        bool accepted = false;
        try
        {
            if (_sessions.HasActiveSession)
            {
                if (CurrentExecutionOrigin() != ExecutionOrigin.Fallback)
                {
                    rejectionReason = "behavior-busy";
                    return RejectStartedImmediate(
                        request,
                        interactionKey,
                        rejectionReason);
                }

                _sessions.Interrupt(
                    BehaviorTerminalStatus.Superseded,
                    $"Idle fallback yielded to '{definition.Id}'.");
            }

            accepted = TryStartSession(
                request,
                definition,
                ExecutionOrigin.Immediate,
                out rejectionReason);
            if (!accepted)
            {
                ReleaseInteractionLease(
                    request.RequestId,
                    out _);
            }
        }
        catch
        {
            ReleaseInteractionLease(
                request.RequestId,
                out _);
            throw;
        }
        finally
        {
            EndMutation();
        }

        if (!accepted)
        {
            _journal.Append(
                BehaviorEventKind.RequestRejected,
                behaviorId,
                request.RequestId,
                reason: rejectionReason);
        }

        return new BehaviorMutationResult
        {
            Accepted = accepted,
            BehaviorId = behaviorId,
            RequestId = request.RequestId,
            RejectionReason = accepted ? null : rejectionReason,
            InteractionKey = interactionKey,
        };
    }

    private bool CanStartImmediate(out string? rejectionReason)
    {
        if (_dragTransition is not null)
        {
            rejectionReason = "dragging";
            return false;
        }

        if (_sessions.HasActiveSession &&
            CurrentExecutionOrigin() != ExecutionOrigin.Fallback)
        {
            rejectionReason = "behavior-busy";
            return false;
        }

        rejectionReason = null;
        return true;
    }

    private BehaviorMutationResult RejectStartedImmediate(
        BehaviorRequest request,
        string? interactionKey,
        string reason)
    {
        ReleaseInteractionLease(
            request.RequestId,
            out _);
        _journal.Append(
            BehaviorEventKind.RequestRejected,
            request.BehaviorId,
            request.RequestId,
            reason: reason);
        return new BehaviorMutationResult
        {
            Accepted = false,
            BehaviorId = request.BehaviorId,
            RequestId = request.RequestId,
            RejectionReason = reason,
            InteractionKey = interactionKey,
        };
    }

    public BehaviorMutationResult TriggerClick()
    {
        if (!_started || _paused)
        {
            return RejectedMutation(
                null,
                _paused ? "runtime-paused" : "runtime-not-started");
        }

        if (HasPendingTopLevelPresentationSuspension)
        {
            return RejectedMutation(
                null,
                "top-level-presentation-pending");
        }

        _ = TryDispatchExpiredClick();
        if (IsInteractionTriggerBusy())
        {
            _clickBurst.Cancel();
            return RejectedMutation(null, "behavior-busy");
        }

        _tick.NotifyInteractionHandled();
        ClickBurstObservation observation = _clickBurst.ObserveClick();
        if (observation == ClickBurstObservation.Pending)
        {
            return AcceptedPendingClickMutation();
        }

        return DispatchClassifiedClick("trigger:repeat_click");
    }

    public BehaviorMutationResult TriggerLongPress()
    {
        if (!_started || _paused)
        {
            return RejectedMutation(
                null,
                _paused ? "runtime-paused" : "runtime-not-started");
        }

        if (HasPendingTopLevelPresentationSuspension)
        {
            return RejectedMutation(
                null,
                "top-level-presentation-pending");
        }

        if (IsInteractionTriggerBusy())
        {
            _clickBurst.Cancel();
            return RejectedMutation(null, "behavior-busy");
        }

        _clickBurst.Cancel();
        _tick.NotifyInteractionHandled();
        if (_stateMachine.StableState == StablePetState.Sleeping)
        {
            _pendingWakeInteractionTag = "trigger:long_press";
            _pendingWakeInteractionCanYieldToToolbar = true;
            return RequestWakeForPendingInteraction();
        }

        const string tag = "trigger:long_press";
        return TriggerTaggedInteraction(
            tag,
            InteractionKeyForTag(tag),
            canYieldToToolbar: true);
    }

    public BehaviorMutationResult TriggerInteractionTag(string tag) =>
        TriggerInteractionTagCore(tag, allowPointerSupersession: false);

    /// <summary>
    /// Starts an explicit toolbar action. A toolbar choice may replace an
    /// ordinary pointer-reaction session (hover, click, or petting), but it
    /// never interrupts another care action, drag lifecycle, manual preview,
    /// or top-level presentation.
    /// </summary>
    public BehaviorMutationResult TriggerToolbarInteractionTag(string tag) =>
        TriggerInteractionTagCore(tag, allowPointerSupersession: true);

    private BehaviorMutationResult TriggerInteractionTagCore(
        string tag,
        bool allowPointerSupersession)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (!tag.StartsWith("trigger:", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Interaction tags must start with 'trigger:'.",
                nameof(tag));
        }

        if (!_started || _paused)
        {
            return RejectedMutation(
                null,
                _paused ? "runtime-paused" : "runtime-not-started");
        }

        if (HasPendingTopLevelPresentationSuspension)
        {
            return RejectedMutation(
                null,
                "top-level-presentation-pending");
        }

        BeginMutation();
        try
        {
            if (IsInteractionTriggerBusy() &&
                (!allowPointerSupersession ||
                 !TrySupersedePointerInteractionForToolbar(tag)))
            {
                _clickBurst.Cancel();
                return RejectedMutation(null, "behavior-busy");
            }

            _clickBurst.Cancel();
            _tick.NotifyInteractionHandled();
            if (_stateMachine.StableState == StablePetState.Sleeping)
            {
                _pendingWakeInteractionTag = tag;
                _pendingWakeInteractionCanYieldToToolbar =
                    !allowPointerSupersession;
                return RequestWakeForPendingInteraction();
            }

            return TriggerTaggedInteraction(
                tag,
                InteractionKeyForTag(tag),
                canYieldToToolbar: !allowPointerSupersession);
        }
        finally
        {
            EndMutation();
        }
    }

    public BehaviorMutationResult TriggerSoftTag(
        string tag,
        BehaviorRequestSource source,
        TimeSpan? timeToLive = null,
        BehaviorDailyOpportunity? dailyOpportunity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (source is
            BehaviorRequestSource.Interaction or
            BehaviorRequestSource.ForcedInteraction or
            BehaviorRequestSource.StateFallback or
            BehaviorRequestSource.StateContinuation)
        {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                "Soft context triggers require an automatic request source.");
        }
        if (dailyOpportunity is not null &&
            source is not
                (BehaviorRequestSource.Scheduled or
                 BehaviorRequestSource.Weather))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dailyOpportunity),
                "Daily opportunities require a scheduled or weather source.");
        }

        if (!_started || _paused)
        {
            return RejectedMutation(
                null,
                _paused ? "runtime-paused" : "runtime-not-started");
        }

        TimeSpan ttl = timeToLive ?? DefaultSoftTtl;
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive));
        }

        IReadOnlyCollection<BehaviorId> candidates = FindByTag(tag);
        if (candidates.Count == 0)
        {
            return RejectedMutation(null, $"unknown-trigger-tag:{tag}");
        }

        bool isAutomaticWake =
            candidates.Any(IsAutomaticWakeCandidate);
        if (isAutomaticWake)
        {
            _queue.DiscardPending(
                request => IsSleepLifecycleBehavior(request.BehaviorId),
                SoftQueueRemovalReason.SupersededByAutomaticWake);
            if (_stateMachine.StableState == StablePetState.Sleeping ||
                HasActiveSleepLifecycleSession())
            {
                return RequestAutomaticWake(
                    tag,
                    source,
                    ttl,
                    dailyOpportunity);
            }
        }

        BehaviorSelectionPlan plan = Evaluate(source, candidates);
        BehaviorSelectionResult selection = _utility.Pick(plan);
        foreach (BehaviorId behaviorId in selection.AttemptOrder)
        {
            string dedupeKey = dailyOpportunity is null
                ? $"{source}:{tag}"
                : $"daily:{dailyOpportunity.Key}:{dailyOpportunity.LocalDate.DayNumber}";
            BehaviorMutationResult result = EnqueueAutomaticSelection(
                behaviorId,
                source,
                ttl,
                dedupeKey,
                dailyOpportunity);
            if (result.Accepted)
            {
                return result;
            }

        }

        return RejectedMutation(null, $"no-eligible-behavior:{tag}");
    }

    public bool BeginDragging(out string? rejectionReason)
    {
        if (!_started || _paused)
        {
            rejectionReason = _paused ? "runtime-paused" : "runtime-not-started";
            return false;
        }

        if (HasPendingTopLevelPresentationSuspension)
        {
            rejectionReason = "top-level-presentation-pending";
            return false;
        }

        _clickBurst.Cancel();
        if (_dragTransition is not null)
        {
            rejectionReason = "already-dragging";
            return false;
        }

        try
        {
            BeginMutation();
            _tick.NotifyInteractionHandled();
            _pendingWakeInteractionTag = null;
            _pendingWakeInteractionCanYieldToToolbar = false;
            _sessions.Interrupt(
                BehaviorTerminalStatus.Interrupted,
                "Direct drag interaction started with exclusive priority.");
            _dragTransition = _stateMachine.BeginDragging();

            rejectionReason = null;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            rejectionReason = exception.Message;
            return false;
        }
        finally
        {
            EndMutation();
        }
    }

    public bool CompleteDragging(out string? rejectionReason)
    {
        if (_dragTransition is not { } token)
        {
            rejectionReason = "not-dragging";
            return false;
        }

        bool completed = _stateMachine.CompleteDragging(token);
        if (!completed)
        {
            RecoverStaleDragging();
            rejectionReason = "stale-drag-transition";
            return false;
        }

        _dragTransition = null;
        _tick.NotifyInteractionHandled();
        rejectionReason = null;
        RequestPump();
        return true;
    }

    public BehaviorMutationResult TriggerDragRelease()
    {
        if (_dragTransition is not { } token)
        {
            return RejectedMutation(null, "not-dragging");
        }

        if (_paused)
        {
            bool completed = _stateMachine.CompleteDragging(token);
            _dragTransition = null;
            if (!completed &&
                _stateMachine.StableState == StablePetState.Dragging)
            {
                _stateMachine.ForceNormal();
            }

            return RejectedMutation(
                null,
                completed ? "runtime-paused" : "stale-drag-transition");
        }

        bool dragCompleted = false;
        bool releaseAccepted = false;
        BeginMutation();
        try
        {
            if (!_stateMachine.CompleteDragging(token))
            {
                RecoverStaleDragging();
                return RejectedMutation(
                    null,
                    "stale-drag-transition");
            }

            dragCompleted = true;
            _dragTransition = null;
            _tick.NotifyInteractionHandled();

            BehaviorMutationResult release =
                TriggerTaggedInteraction(
                    "trigger:drag_release",
                    InteractionKeyForTag("trigger:drag_release"),
                    canYieldToToolbar: false);
            releaseAccepted = release.Accepted;
            return release;
        }
        finally
        {
            if (dragCompleted && !releaseAccepted)
            {
                RequestPump();
            }

            EndMutation();
        }
    }

    /// <summary>
    /// Completes pointer dragging and starts the release response before queued
    /// work can resume.
    /// </summary>
    public BehaviorMutationResult CompleteDraggingAndStart(
        BehaviorId behaviorId,
        BehaviorRequestSource source = BehaviorRequestSource.Interaction)
    {
        if (_dragTransition is not { } token)
        {
            return RejectedMutation(behaviorId, "not-dragging");
        }

        bool dragCompleted = false;
        bool releaseAccepted = false;
        BeginMutation();
        try
        {
            if (!_stateMachine.CompleteDragging(token))
            {
                RecoverStaleDragging();
                return RejectedMutation(
                    behaviorId,
                    "stale-drag-transition");
            }

            dragCompleted = true;
            _dragTransition = null;
            _tick.NotifyInteractionHandled();

            BehaviorMutationResult release =
                source is
                    BehaviorRequestSource.Interaction or
                    BehaviorRequestSource.ForcedInteraction
                    ? TryStartInteraction(
                        behaviorId,
                        source,
                        InteractionKeyForTag("trigger:drag_release"),
                        "trigger:drag_release",
                        canYieldToToolbar: false)
                    : TryStartImmediate(behaviorId, source);
            releaseAccepted = release.Accepted;
            return release;
        }
        finally
        {
            if (dragCompleted && !releaseAccepted)
            {
                RequestPump();
            }

            EndMutation();
        }
    }

    private void RecoverStaleDragging()
    {
        _dragTransition = null;
        if (_stateMachine.StableState == StablePetState.Dragging)
        {
            _stateMachine.ForceNormal();
        }

        _tick.NotifyInteractionHandled();
        RequestPump();
    }

    public BehaviorTickResult RunActiveTick(int? deterministicSeed = null)
    {
        bool wasDue = _tick.TryConsumeDueTick();
        return RunActiveTickCore(wasDue, deterministicSeed, scheduleNext: wasDue);
    }

    public BehaviorTickResult ForceActiveTick(int? deterministicSeed = null) =>
        RunActiveTickCore(
            wasDue: false,
            deterministicSeed,
            scheduleNext: true);

    public bool RequestExternalCompletion() =>
        _sessions.RequestExternalCompletion();

    public void Advance()
    {
        RefreshEmotions();
        if (!_started || _paused)
        {
            return;
        }

        _ = TryDispatchExpiredClick();
        _sessions.Advance();
        if (_tick.TryConsumeDueTick())
        {
            RunActiveTickCore(
                wasDue: true,
                deterministicSeed: null,
                scheduleNext: true);
        }

        RequestPump();
    }

    public bool NotifyFirstFrame(BehaviorPlaybackToken token) =>
        _sessions.NotifyFirstFrame(token);

    public bool NotifyClipCompleted(BehaviorPlaybackToken token) =>
        _sessions.NotifyClipCompleted(token);

    public bool NotifyLoopBoundary(BehaviorPlaybackToken token)
    {
        if (_sessions.CurrentPlaybackToken == token &&
            CurrentExecutionOrigin() == ExecutionOrigin.Fallback &&
            IsSoftQueueReady())
        {
            BeginMutation();
            try
            {
                return _sessions.Interrupt(
                    BehaviorTerminalStatus.Superseded,
                    "A queued soft behavior reached the fallback loop boundary.");
            }
            finally
            {
                EndMutation();
            }
        }

        return _sessions.NotifyClipCompleted(token);
    }

    public BubbleRequestKind GetBubbleKind(BehaviorSessionToken sessionToken)
    {
        if (_executions.TryGetValue(sessionToken.Value, out var execution))
        {
            if (execution.Request.Source ==
                BehaviorRequestSource.ManualPreview)
            {
                return BubbleRequestKind.ManualPreview;
            }

            return IsDirectInteraction(execution.Request.Source)
                ? BubbleRequestKind.Interaction
                : BubbleRequestKind.Automatic;
        }

        if (_startingRequest is { } starting &&
            _sessions.Snapshot?.SessionToken == sessionToken)
        {
            if (starting.Source == BehaviorRequestSource.ManualPreview)
            {
                return BubbleRequestKind.ManualPreview;
            }

            return IsDirectInteraction(starting.Source)
                ? BubbleRequestKind.Interaction
                : BubbleRequestKind.Automatic;
        }

        return BubbleRequestKind.Automatic;
    }

    public BehaviorCoordinatorSnapshot GetSnapshot()
    {
        RefreshEmotions();
        return new BehaviorCoordinatorSnapshot
        {
            CapturedAt = _clock.Elapsed,
            Started = _started,
            Paused = _paused,
            Context = CurrentContext(),
            Emotions = _emotions.CloneNormalized(),
            RelationshipStage = RelationshipStage,
            State = _stateMachine.Snapshot(),
            Session = _sessions.Snapshot,
            Queue = _queue.GetSnapshot(),
            NextTickAt = _tick.NextDueAt,
            LastTickInterval = _tick.LastInterval,
            LastEvaluation = _lastEvaluation,
            UtilityHistory = _utility.GetHistorySnapshot(),
            LastTerminal = _sessions.LastTerminal,
            InteractionLease = _interactionLease?.CreateSnapshot(),
            CatalogBehaviorCount = _catalog.Definitions.Count,
        };
    }

    public BehaviorEventPage ReadEventsAfter(long sequence) =>
        _journal.ReadAfter(sequence);

    private BehaviorTickResult RunActiveTickCore(
        bool wasDue,
        int? deterministicSeed,
        bool scheduleNext)
    {
        RefreshEmotions();
        TimeSpan evaluatedAt = _clock.Elapsed;
        BehaviorContextSnapshot context = CurrentContext();
        EmotionState emotions = _emotions.CloneNormalized();
        HierarchicalPetStateSnapshot state = _stateMachine.Snapshot();
        var candidates = FindByTag("trigger:active_tick");
        var plan = Evaluate(BehaviorRequestSource.ActiveTick, candidates);
        BehaviorSelectionResult selection = deterministicSeed is { } seed
            ? PickWithRandom(plan, new SystemRandomSource(new Random(seed)))
            : _utility.Pick(plan);

        SoftQueueEnqueueResult? queueResult = null;
        if (selection.SelectedBehaviorId is { } behaviorId)
        {
            BehaviorMutationResult mutation = EnqueueAutomaticSelection(
                behaviorId,
                BehaviorRequestSource.ActiveTick,
                DefaultSoftTtl,
                "active-tick");
            queueResult = mutation.QueueResult;
        }

        _journal.Append(
            BehaviorEventKind.TickEvaluated,
            selection.SelectedBehaviorId,
            reason: selection.SelectedBehaviorId is null
                ? "no-eligible-candidate"
                : null,
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["candidateCount"] = plan.TopCandidates.Count.ToString(),
                ["wasDue"] = wasDue.ToString(),
                ["seeded"] = (deterministicSeed is not null).ToString(),
            });

        if (scheduleNext)
        {
            _tick.NotifyTickCompleted();
        }

        var result = new BehaviorTickResult
        {
            WasDue = wasDue,
            Evaluation = selection.Plan,
            SelectedBehaviorId = selection.SelectedBehaviorId,
            AttemptOrder = selection.AttemptOrder,
            QueueResult = queueResult,
            NextDueAt = _tick.NextDueAt,
        };
        ActiveTickEvaluated?.Invoke(
            new BehaviorTickTrace
            {
                EvaluatedAt = evaluatedAt,
                Context = context,
                Emotions = emotions,
                State = state,
                Result = result,
            });
        return result;
    }

    private void RefreshEmotions()
    {
        TimeSpan now = _clock.Elapsed;
        if (now <= _lastEmotionRegressionAt)
        {
            // A monotonic source must never move backwards. Preserve both the
            // current emotions and the last trusted anchor so a bad reading
            // cannot manufacture elapsed time or double-apply regression.
            return;
        }

        TimeSpan elapsed = now - _lastEmotionRegressionAt;
        _emotions.RegressShortTerm(elapsed);
        _lastEmotionRegressionAt = now;
    }

    private void ApplyEmotionEffect(EmotionDelta effect)
    {
        RefreshEmotions();
        double previousAffection = _emotions.Affection;
        _emotions.Apply(effect);
        if (_emotions.Affection != previousAffection)
        {
            AffectionChanged?.Invoke(_emotions.Affection);
        }
    }

    private bool TryStartSession(
        BehaviorRequest request,
        BehaviorDefinition definition,
        ExecutionOrigin origin,
        out string? rejectionReason)
    {
        BehaviorRequest? previousStartingRequest = _startingRequest;
        ExecutionOrigin previousStartingOrigin = _startingOrigin;
        var startingExecution = new SessionExecution(request, origin);
        _startingExecutions[request.RequestId] = startingExecution;
        _startingRequest = request;
        _startingOrigin = origin;
        try
        {
            bool accepted = _sessions.TryStart(
                request,
                definition,
                out rejectionReason);
            if (!accepted)
            {
                return false;
            }

            if (_sessions.Snapshot is { Terminal: null } snapshot &&
                !_executions.ContainsKey(snapshot.SessionToken.Value))
            {
                _executions[snapshot.SessionToken.Value] =
                    startingExecution;
            }

            if (_sessions.Snapshot is { Terminal: null } started)
            {
                BindInteractionLease(
                    request.RequestId,
                    started.SessionToken);
                JournalSessionStarted(
                    started.SessionToken,
                    startingExecution,
                    started.Phase);
            }

            return true;
        }
        finally
        {
            if (_sessions.Snapshot is
                {
                    Terminal: not null
                } terminated)
            {
                _journaledSessionStarts.Remove(
                    terminated.SessionToken.Value);
            }

            _startingExecutions.Remove(request.RequestId);
            _startingRequest = previousStartingRequest;
            _startingOrigin = previousStartingOrigin;
        }
    }

    private void OnSessionCommitted(BehaviorSessionCommitRecord committed)
    {
        if (!_executions.TryGetValue(
                committed.SessionToken.Value,
                out var execution) &&
            _startingExecutions.TryGetValue(
                committed.RequestId,
                out var startingExecution))
        {
            execution = startingExecution;
            _executions[committed.SessionToken.Value] = execution;
        }

        if (execution is null)
        {
            return;
        }

        BindInteractionLease(
            execution.Request.RequestId,
            committed.SessionToken);
        JournalSessionStarted(
            committed.SessionToken,
            execution,
            BehaviorPhase.AwaitingFirstFrame);
        if (_catalog.TryGet(
                execution.Request.BehaviorId,
                out var definition) &&
            definition is not null &&
            execution.Request.Source !=
            BehaviorRequestSource.ManualPreview)
        {
            ApplyEmotionEffect(definition.StartedEmotionEffect);
            if (execution.Origin != ExecutionOrigin.Fallback)
            {
                _utility.Commit(definition.Id);
            }
        }

        _journal.Append(
            BehaviorEventKind.FirstFramePresented,
            committed.BehaviorId,
            execution.Request.RequestId,
            sessionToken: committed.SessionToken,
            phase: _sessions.Snapshot?.Phase);

        if (execution.Request.DailyOpportunity is { } dailyOpportunity)
        {
            DailyOpportunityPresented?.Invoke(dailyOpportunity);
        }

        SessionCommitted?.Invoke(committed);

        if ((_pendingWakeInteractionTag is not null ||
             _pendingAutomaticWakes.Count > 0) &&
            IsSleepLifecycleBehavior(committed.BehaviorId) &&
            !_sessions.RequestExternalCompletion())
        {
            throw new InvalidOperationException(
                "A pending wake request could not start the sleep exit lifecycle.");
        }
    }

    private void OnSessionTerminated(BehaviorTerminalRecord terminal)
    {
        SessionExecution? execution = null;
        if (_executions.Remove(terminal.SessionToken.Value, out var known))
        {
            execution = known;
        }
        else if (_startingExecutions.TryGetValue(
                     terminal.RequestId,
                     out var startingExecution))
        {
            execution = startingExecution;
        }

        if (execution is not null)
        {
            JournalSessionStarted(
                terminal.SessionToken,
                execution,
                BehaviorPhase.AwaitingFirstFrame);
        }

        if (execution is not null &&
            terminal.Status == BehaviorTerminalStatus.Completed &&
            execution.Request.Source !=
            BehaviorRequestSource.ManualPreview &&
            _catalog.TryGet(execution.Request.BehaviorId, out var definition) &&
            definition is not null)
        {
            ApplyEmotionEffect(definition.CompletedEmotionEffect);
        }

        ReleaseInteractionLease(
            terminal.RequestId,
            out InteractionLease? releasedInteractionLease);

        if (execution?.Origin == ExecutionOrigin.Soft)
        {
            _queue.CompleteActiveBehavior();
        }
        _journal.Append(
            BehaviorEventKind.SessionTerminated,
            terminal.BehaviorId,
            terminal.RequestId,
            sessionToken: terminal.SessionToken,
            terminalStatus: terminal.Status,
            reason: terminal.Reason,
            details: InteractionTerminalDetails(releasedInteractionLease));
        if (_startingRequest is null)
        {
            _journaledSessionStarts.Remove(terminal.SessionToken.Value);
        }

        if (releasedInteractionLease is not null)
        {
            InteractionSessionTerminated?.Invoke(
                terminal,
                releasedInteractionLease.CreateSnapshot());
        }

        SessionTerminated?.Invoke(terminal);

        DispatchPendingWakeInteraction(terminal);
        if (!TryEnterTopLevelPresentationBoundary())
        {
            DispatchPendingAutomaticWakes(terminal);
        }
        RequestPump();
    }

    private void JournalSessionStarted(
        BehaviorSessionToken sessionToken,
        SessionExecution execution,
        BehaviorPhase phase)
    {
        if (!_journaledSessionStarts.Add(sessionToken.Value))
        {
            return;
        }

        _journal.Append(
            BehaviorEventKind.SessionStarted,
            execution.Request.BehaviorId,
            execution.Request.RequestId,
            sessionToken: sessionToken,
            phase: phase);
    }

    private void RequestPump()
    {
        _pumpRequested = true;
        if (_mutationDepth == 0)
        {
            Pump();
        }
    }

    private void Pump()
    {
        if (_pumping || _mutationDepth > 0 || !_started || _paused)
        {
            return;
        }

        _pumping = true;
        try
        {
            var attempts = 0;
            do
            {
                _pumpRequested = false;
                if (++attempts > 32)
                {
                    throw new InvalidOperationException(
                        "Behavior execution pump exceeded its transition bound.");
                }

                if (TryEnterTopLevelPresentationBoundary())
                {
                    continue;
                }

                if (_dragTransition is not null)
                {
                    continue;
                }

                if (_sessions.HasActiveSession)
                {
                    continue;
                }

                BehaviorRequest? request = _queue.TryStartNext();
                if (request is not null)
                {
                    string? rejectionReason = null;
                    if (_catalog.TryGet(request.BehaviorId, out var definition) &&
                        definition is not null &&
                        TryStartSession(
                            request,
                            definition,
                            ExecutionOrigin.Soft,
                            out rejectionReason))
                    {
                        continue;
                    }

                    _queue.CompleteActiveBehavior();
                    _journal.Append(
                        BehaviorEventKind.RequestRejected,
                        request.BehaviorId,
                        request.RequestId,
                        reason: rejectionReason ?? "unknown-behavior");
                    _pumpRequested = true;
                    continue;
                }

                StartFallbackIfNeeded();
            }
            while (_pumpRequested);
        }
        finally
        {
            _pumping = false;
        }
    }

    private bool TryEnterTopLevelPresentationBoundary()
    {
        TaskCompletionSource<bool>? completion =
            _topLevelPresentationSuspensionCompletion;
        if (completion is null || _paused)
        {
            return false;
        }

        if (_dragTransition is not null ||
            _clickBurst.HasPending ||
            _pendingWakeInteractionTag is not null ||
            _interactionLease is not null)
        {
            return false;
        }

        if (_sessions.HasActiveSession &&
            CurrentExecutionOrigin() != ExecutionOrigin.Fallback)
        {
            return false;
        }

        _topLevelPresentationSuspensionCompletion = null;
        _paused = true;
        _pausedForTopLevelPresentation = true;
        _tick.Cancel();

        if (_sessions.HasActiveSession)
        {
            _sessions.Interrupt(
                BehaviorTerminalStatus.Superseded,
                "Idle fallback yielded to a top-level presentation.");
        }

        completion.TrySetResult(true);
        return true;
    }

    private void StartFallbackIfNeeded()
    {
        if (_sessions.HasActiveSession)
        {
            return;
        }

        BehaviorDefinition[] matches = _catalog.Definitions
            .Where(IsFallbackForCurrentState)
            .OrderBy(static candidate => candidate.Id.Value, StringComparer.Ordinal)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"Behavior content invariant failed: state " +
                $"'{_stateMachine.StableState}' must have exactly one fallback " +
                $"behavior, but found {matches.Length}.");
        }

        BehaviorDefinition definition = matches[0];
        var request = CreateRequest(
            definition.Id,
            BehaviorRequestSource.StateFallback,
            BehaviorSwitchMode.Queued,
            TimeSpan.FromDays(2),
            $"fallback:{_stateMachine.StableState}");
        _journal.Append(
            BehaviorEventKind.RequestReceived,
            definition.Id,
            request.RequestId);
        if (!TryStartSession(
                request,
                definition,
                ExecutionOrigin.Fallback,
                out string? rejectionReason))
        {
            throw new InvalidOperationException(
                $"State fallback '{definition.Id}' could not start: " +
                $"{rejectionReason ?? "unknown rejection"}.");
        }
    }

    private bool IsFallbackForCurrentState(BehaviorDefinition candidate)
    {
        if (!candidate.AllowedStates.Contains(_stateMachine.StableState))
        {
            return false;
        }

        return _stateMachine.StableState switch
        {
            StablePetState.Normal => candidate.IsStateFallback,
            StablePetState.Sleeping =>
                candidate.TargetState == StablePetState.Sleeping &&
                !string.IsNullOrWhiteSpace(
                    candidate.Animation.LoopClipId),
            StablePetState.Dragging => false,
            _ => false,
        };
    }

    private bool IsSoftQueueReady()
    {
        if (_clickBurst.HasPending)
        {
            return false;
        }

        SoftBehaviorQueueSnapshot snapshot = _queue.GetSnapshot();
        return snapshot.PendingCount > 0 &&
               snapshot.ActiveRequest is null &&
               (snapshot.ResumeAfter is null ||
                snapshot.CapturedAt >= snapshot.ResumeAfter);
    }

    private ExecutionOrigin CurrentExecutionOrigin()
    {
        BehaviorSessionSnapshot? snapshot = _sessions.Snapshot;
        if (snapshot is not null &&
            _executions.TryGetValue(
                snapshot.SessionToken.Value,
                out var execution))
        {
            return execution.Origin;
        }

        return snapshot is not null &&
               _startingRequest is not null
            ? _startingOrigin
            : ExecutionOrigin.None;
    }

    private bool TryDispatchExpiredClick()
    {
        if (!_clickBurst.TryExpireSingle())
        {
            return false;
        }

        if (!IsInteractionTriggerBusy())
        {
            _ = DispatchClassifiedClick("trigger:click");
        }

        return true;
    }

    private BehaviorMutationResult DispatchClassifiedClick(string tag)
    {
        if (_stateMachine.StableState == StablePetState.Sleeping)
        {
            _pendingWakeInteractionTag = tag;
            _pendingWakeInteractionCanYieldToToolbar = true;
            return RequestWakeForPendingInteraction();
        }

        return TriggerTaggedInteraction(
            tag,
            InteractionKeyForTag(tag),
            canYieldToToolbar: true);
    }

    private static BehaviorMutationResult AcceptedPendingClickMutation() =>
        new()
        {
            Accepted = true,
            BehaviorId = null,
            RequestId = null,
            RejectionReason = null,
            InteractionKey = "primary_click",
        };

    private BehaviorMutationResult RequestWakeForPendingInteraction()
    {
        if (TryRequestSleepExit())
        {
            return AcceptedCurrentSessionMutation();
        }

        RequestPump();
        if (TryRequestSleepExit())
        {
            return AcceptedCurrentSessionMutation();
        }

        if (_sessions.Snapshot is
            {
                Terminal: null
            } pendingSession &&
            IsSleepLifecycleBehavior(pendingSession.BehaviorId))
        {
            // A first frame has not committed yet. OnSessionCommitted requests
            // the external completion so the dedicated exit clip still owns
            // the wake transition.
            return AcceptedCurrentSessionMutation();
        }

        _pendingWakeInteractionTag = null;
        _pendingWakeInteractionCanYieldToToolbar = false;
        throw new InvalidOperationException(
            "Sleeping has no active sleep lifecycle capable of playing its exit clip.");
    }

    private BehaviorMutationResult RequestAutomaticWake(
        string tag,
        BehaviorRequestSource source,
        TimeSpan timeToLive,
        BehaviorDailyOpportunity? dailyOpportunity)
    {
        if (_pendingAutomaticWakes.Any(wake =>
                IsSameAutomaticWake(
                    wake,
                    tag,
                    source,
                    dailyOpportunity)))
        {
            return RejectedMutation(null, "automatic-wake-already-pending");
        }

        var pending = new PendingAutomaticWake(
            tag,
            source,
            AddSaturated(_clock.Elapsed, timeToLive),
            dailyOpportunity);
        if (_pendingAutomaticWakes.Count == 3)
        {
            _pendingAutomaticWakes.RemoveAt(0);
        }

        _pendingAutomaticWakes.Add(pending);

        if (TryRequestSleepExit())
        {
            return AcceptedCurrentSessionMutation();
        }

        RequestPump();
        if (TryRequestSleepExit())
        {
            return AcceptedCurrentSessionMutation();
        }

        if (_sessions.Snapshot is
            {
                Terminal: null
            } pendingSession &&
            IsSleepLifecycleBehavior(pendingSession.BehaviorId))
        {
            return AcceptedCurrentSessionMutation();
        }

        _pendingAutomaticWakes.Remove(pending);
        return RejectedMutation(null, "sleep-lifecycle-unavailable");
    }

    private BehaviorMutationResult TriggerTaggedInteraction(
        string tag,
        string interactionKey,
        bool canYieldToToolbar)
    {
        if (IsInteractionTriggerBusy())
        {
            return RejectedMutation(null, "behavior-busy");
        }

        IReadOnlyCollection<BehaviorId> candidates = RequireByTag(tag);
        var plan = Evaluate(BehaviorRequestSource.Interaction, candidates);
        BehaviorSelectionResult selection = _utility.Pick(plan);

        foreach (BehaviorId behaviorId in selection.AttemptOrder)
        {
            BehaviorMutationResult result = TryStartInteraction(
                behaviorId,
                BehaviorRequestSource.Interaction,
                interactionKey,
                tag,
                canYieldToToolbar);
            if (result.Accepted)
            {
                return result;
            }

        }

        BehaviorId fallback = RequireSingleByTag("fallback:interaction");
        return TryStartInteraction(
            fallback,
            BehaviorRequestSource.ForcedInteraction,
            interactionKey,
            tag,
            canYieldToToolbar);
    }

    private bool IsInteractionTriggerBusy()
    {
        if (_dragTransition is not null)
        {
            return true;
        }

        if (!_sessions.HasActiveSession ||
            CurrentExecutionOrigin() == ExecutionOrigin.Fallback)
        {
            return false;
        }

        return _stateMachine.Phase != PetLifecyclePhase.SleepingLooping ||
               _sessions.Snapshot is not { Terminal: null } session ||
               !IsSleepLifecycleBehavior(session.BehaviorId);
    }

    private bool TrySupersedePointerInteractionForToolbar(string targetTag)
    {
        if (_dragTransition is not null ||
            !_sessions.HasActiveSession ||
            _interactionLease is not { SessionToken: not null } lease ||
            !lease.CanYieldToToolbar ||
            !IsToolbarSupersedablePointerTag(lease.TriggerTag))
        {
            return false;
        }

        return _sessions.Interrupt(
            BehaviorTerminalStatus.Superseded,
            $"Pointer interaction '{lease.TriggerTag}' yielded to toolbar action '{targetTag}'.");
    }

    private static bool IsToolbarSupersedablePointerTag(string tag) =>
        tag is
            "trigger:approach" or
            "trigger:hover" or
            "trigger:petting" or
            "trigger:pet_end" or
            "trigger:rapid_pointer" or
            "trigger:circle_pointer" or
            "trigger:click" or
            "trigger:repeat_click" or
            "trigger:long_press";

    private bool HasPendingTopLevelPresentationSuspension =>
        _topLevelPresentationSuspensionCompletion is not null;

    private void DispatchPendingWakeInteraction(BehaviorTerminalRecord terminal)
    {
        if (_pendingWakeInteractionTag is not { } tag ||
            !IsSleepLifecycleBehavior(terminal.BehaviorId))
        {
            return;
        }

        if (terminal.Status != BehaviorTerminalStatus.Completed ||
            _stateMachine.StableState != StablePetState.Normal)
        {
            _pendingWakeInteractionTag = null;
            _pendingWakeInteractionCanYieldToToolbar = false;
            return;
        }

        bool canYieldToToolbar = _pendingWakeInteractionCanYieldToToolbar;
        _pendingWakeInteractionTag = null;
        _pendingWakeInteractionCanYieldToToolbar = false;
        TriggerTaggedInteraction(
            tag,
            InteractionKeyForTag(tag),
            canYieldToToolbar);
    }

    private void DispatchPendingAutomaticWakes(
        BehaviorTerminalRecord terminal)
    {
        if (_pendingAutomaticWakes.Count == 0 ||
            !IsSleepLifecycleBehavior(terminal.BehaviorId))
        {
            return;
        }

        PendingAutomaticWake[] pending =
            [.. _pendingAutomaticWakes];
        _pendingAutomaticWakes.Clear();
        if (terminal.Status != BehaviorTerminalStatus.Completed ||
            _stateMachine.StableState != StablePetState.Normal)
        {
            return;
        }

        foreach (PendingAutomaticWake wake in pending)
        {
            TimeSpan remaining = wake.ExpiresAt - _clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                continue;
            }

            TriggerSoftTag(
                wake.Tag,
                wake.Source,
                remaining,
                wake.DailyOpportunity);
        }
    }

    private static bool IsSameAutomaticWake(
        PendingAutomaticWake pending,
        string tag,
        BehaviorRequestSource source,
        BehaviorDailyOpportunity? dailyOpportunity)
    {
        if (pending.DailyOpportunity is { } existing &&
            dailyOpportunity is { } incoming)
        {
            return string.Equals(
                       existing.Key,
                       incoming.Key,
                       StringComparison.Ordinal) &&
                   existing.LocalDate == incoming.LocalDate;
        }

        return pending.DailyOpportunity is null &&
               dailyOpportunity is null &&
               pending.Source == source &&
               string.Equals(pending.Tag, tag, StringComparison.Ordinal);
    }

    private bool IsAutomaticWakeCandidate(BehaviorId behaviorId) =>
        _catalog.TryGet(behaviorId, out BehaviorDefinition? definition) &&
        definition is not null &&
        definition.Queueable &&
        definition.AllowedStates.Contains(StablePetState.Normal) &&
        definition.Tags.Contains(
            AutomaticWakeTransitionTag,
            StringComparer.Ordinal);

    private bool HasActiveSleepLifecycleSession() =>
        _sessions.Snapshot is
        {
            Terminal: null
        } session &&
        IsSleepLifecycleBehavior(session.BehaviorId);

    private bool IsSleepLifecycleBehavior(BehaviorId behaviorId) =>
        _catalog.TryGet(behaviorId, out BehaviorDefinition? definition) &&
        definition?.TargetState == StablePetState.Sleeping &&
        !string.IsNullOrWhiteSpace(definition.Animation.ExitClipId);

    private bool TryRequestSleepExit()
    {
        if (_sessions.Snapshot is not
            {
                Terminal: null
            } session ||
            !IsSleepLifecycleBehavior(session.BehaviorId))
        {
            return false;
        }

        return _stateMachine.Phase == PetLifecyclePhase.SleepingExiting ||
               _sessions.RequestExternalCompletion();
    }

    private BehaviorMutationResult AcceptedCurrentSessionMutation()
    {
        BehaviorSessionSnapshot? snapshot = _sessions.Snapshot;
        long? requestId = snapshot is not null &&
                          _executions.TryGetValue(
                              snapshot.SessionToken.Value,
                              out SessionExecution? execution)
            ? execution.Request.RequestId
            : null;
        return new BehaviorMutationResult
        {
            Accepted = true,
            BehaviorId = snapshot?.BehaviorId,
            RequestId = requestId,
            RejectionReason = null,
        };
    }

    private IReadOnlyCollection<BehaviorId> RequireByTag(string tag)
    {
        IReadOnlyCollection<BehaviorId> matches = FindByTag(tag);
        if (matches.Count == 0)
        {
            throw new InvalidDataException(
                $"Behavior content invariant failed: tag '{tag}' has no behaviors.");
        }

        return matches;
    }

    private BehaviorId RequireSingleByTag(string tag)
    {
        IReadOnlyCollection<BehaviorId> matches = RequireByTag(tag);
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"Behavior content invariant failed: tag '{tag}' must identify " +
                $"exactly one behavior, but found {matches.Count}.");
        }

        return matches.Single();
    }

    private IReadOnlyCollection<BehaviorId> FindByTag(string tag) =>
        _catalog.Definitions
            .Where(definition =>
                definition.Tags.Contains(tag, StringComparer.Ordinal))
            .Select(static definition => definition.Id)
            .ToArray();

    private static string InteractionKeyForTag(string tag) =>
        tag switch
        {
            "trigger:click" or "trigger:repeat_click" => "primary_click",
            "trigger:long_press" => "long_press",
            "trigger:petting" or "trigger:pet_end" => "petting",
            "trigger:drag_release" => "drag",
            _ => tag["trigger:".Length..],
        };

    private void AcquireInteractionLease(InteractionLease lease)
    {
        if (!_interactionLeasesByRequestId.TryAdd(lease.RequestId, lease))
        {
            throw new InvalidOperationException(
                $"Interaction request '{lease.RequestId}' already owns a lease.");
        }

        _interactionLease = lease;
    }

    private void BindInteractionLease(
        long requestId,
        BehaviorSessionToken sessionToken)
    {
        if (!_interactionLeasesByRequestId.TryGetValue(
                requestId,
                out InteractionLease? lease))
        {
            return;
        }

        if (lease.SessionToken is { } existing &&
            existing != sessionToken)
        {
            throw new InvalidOperationException(
                $"Interaction request '{requestId}' changed session ownership.");
        }

        lease.SessionToken = sessionToken;
    }

    private void ReleaseInteractionLease(
        long requestId,
        out InteractionLease? releasedLease)
    {
        if (!_interactionLeasesByRequestId.Remove(
                requestId,
                out releasedLease))
        {
            return;
        }

        bool wasCurrent =
            _interactionLease?.Generation == releasedLease.Generation &&
            _interactionLease.RequestId == releasedLease.RequestId;
        if (wasCurrent)
        {
            _interactionLease = null;
        }
    }

    private static IReadOnlyDictionary<string, string>? InteractionRequestDetails(
        BehaviorRequest request) =>
        request.InteractionKey is null
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["interactionKey"] = request.InteractionKey,
                ["interactionTriggerTag"] =
                    request.InteractionTriggerTag ?? string.Empty,
                ["interactionLeaseGeneration"] =
                    request.InteractionLeaseGeneration?.ToString() ??
                    string.Empty,
            };

    private static IReadOnlyDictionary<string, string>?
        InteractionTerminalDetails(InteractionLease? lease) =>
        lease is null
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["interactionKey"] = lease.Key,
                ["interactionTriggerTag"] = lease.TriggerTag,
            };

    private BehaviorRequest CreateRequest(
        BehaviorId behaviorId,
        BehaviorRequestSource source,
        BehaviorSwitchMode switchMode,
        TimeSpan ttl,
        string? dedupeKey = null,
        string? interactionKey = null,
        string? interactionTriggerTag = null,
        long? interactionLeaseGeneration = null,
        BehaviorDailyOpportunity? dailyOpportunity = null)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl));
        }

        var now = _clock.Elapsed;
        return new BehaviorRequest
        {
            RequestId = checked(++_nextRequestId),
            BehaviorId = behaviorId,
            Source = source,
            SwitchMode = switchMode,
            Priority = IsDirectInteraction(source) ? 100 : 50,
            CreatedAt = now,
            FirstCreatedAt = now,
            ExpiresAt = AddSaturated(now, ttl),
            DedupeKey = dedupeKey ?? $"{source}:{behaviorId}",
            ContextRevision = _context.Revision,
            InteractionKey = interactionKey,
            InteractionTriggerTag = interactionTriggerTag,
            InteractionLeaseGeneration = interactionLeaseGeneration,
            DailyOpportunity = dailyOpportunity,
        };
    }

    private BehaviorContextSnapshot CurrentContext() =>
        _context with
        {
            LocalNow = _context.LocalNow == default
                ? DateTimeOffset.Now
                : _context.LocalNow,
        };

    private void BeginMutation() => _mutationDepth++;

    private void EndMutation()
    {
        if (_mutationDepth <= 0)
        {
            throw new InvalidOperationException(
                "Behavior mutation depth became inconsistent.");
        }

        _mutationDepth--;
        if (_mutationDepth == 0 && _pumpRequested)
        {
            Pump();
        }
    }

    private static BehaviorSelectionResult PickWithRandom(
        BehaviorSelectionPlan plan,
        IRandomSource random)
    {
        var remaining = plan.TopCandidates
            .Where(static evaluation =>
                evaluation.IsEligible &&
                double.IsFinite(evaluation.Probability) &&
                evaluation.Probability > 0)
            .Select(static evaluation =>
                (evaluation.BehaviorId, Weight: evaluation.Probability))
            .ToList();
        var order = new List<BehaviorId>(remaining.Count);
        while (remaining.Count > 1)
        {
            double unit = random.NextUnit();
            if (!double.IsFinite(unit) || unit < 0 || unit >= 1)
            {
                throw new InvalidOperationException(
                    "Random sources must return a finite value in [0, 1).");
            }

            double roll = unit * remaining.Sum(static item => item.Weight);
            int index = remaining.Count - 1;
            for (int candidateIndex = 0;
                 candidateIndex < remaining.Count;
                 candidateIndex++)
            {
                roll -= remaining[candidateIndex].Weight;
                if (roll < 0)
                {
                    index = candidateIndex;
                    break;
                }
            }

            order.Add(remaining[index].BehaviorId);
            remaining.RemoveAt(index);
        }

        if (remaining.Count == 1)
        {
            order.Add(remaining[0].BehaviorId);
        }

        return new BehaviorSelectionResult
        {
            SelectedBehaviorId =
                order.Count == 0 ? (BehaviorId?)null : order[0],
            AttemptOrder = order,
            Plan = plan with { RandomConsumed = order.Count > 1 },
        };
    }

    private static BehaviorMutationResult RejectedMutation(
        BehaviorId? behaviorId,
        string reason) =>
        new()
        {
            Accepted = false,
            BehaviorId = behaviorId,
            RequestId = null,
            RejectionReason = reason,
        };

    private static bool IsDirectInteraction(BehaviorRequestSource source) =>
        source is
            BehaviorRequestSource.Interaction or
            BehaviorRequestSource.ForcedInteraction or
            BehaviorRequestSource.ManualPreview or
            BehaviorRequestSource.TestControl;

    private static TimeSpan AddSaturated(TimeSpan left, TimeSpan right) =>
        left > TimeSpan.MaxValue - right
            ? TimeSpan.MaxValue
            : left + right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;

    private sealed record SessionExecution(
        BehaviorRequest Request,
        ExecutionOrigin Origin);

    private sealed class InteractionLease
    {
        public InteractionLease(
            long generation,
            string key,
            string triggerTag,
            BehaviorId behaviorId,
            long requestId,
            bool canYieldToToolbar,
            TimeSpan acquiredAt)
        {
            Generation = generation;
            Key = key;
            TriggerTag = triggerTag;
            BehaviorId = behaviorId;
            RequestId = requestId;
            CanYieldToToolbar = canYieldToToolbar;
            AcquiredAt = acquiredAt;
        }

        public long Generation { get; }

        public string Key { get; }

        public string TriggerTag { get; }

        public BehaviorId BehaviorId { get; }

        public long RequestId { get; }

        public bool CanYieldToToolbar { get; }

        public BehaviorSessionToken? SessionToken { get; set; }

        public TimeSpan AcquiredAt { get; }

        public BehaviorInteractionLeaseSnapshot CreateSnapshot() =>
            new()
            {
                Generation = Generation,
                Key = Key,
                TriggerTag = TriggerTag,
                BehaviorId = BehaviorId,
                RequestId = RequestId,
                SessionToken = SessionToken,
                AcquiredAt = AcquiredAt,
            };
    }

    private sealed record PendingAutomaticWake(
        string Tag,
        BehaviorRequestSource Source,
        TimeSpan ExpiresAt,
        BehaviorDailyOpportunity? DailyOpportunity);

    private enum ExecutionOrigin
    {
        None,
        Fallback,
        Soft,
        Immediate
    }

}
