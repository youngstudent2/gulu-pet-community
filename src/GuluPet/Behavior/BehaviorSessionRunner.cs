namespace GuluPet.Behavior;

public enum BehaviorClipRole
{
    Enter,
    Perform,
    Loop,
    Exit
}

public sealed record BehaviorPlaybackRequest
{
    public required BehaviorSessionToken SessionToken { get; init; }

    public required BehaviorPlaybackToken PlaybackToken { get; init; }

    public required BehaviorClipRole Role { get; init; }

    public required string ClipId { get; init; }

    public bool ContinueIfSameClip { get; init; }
}

public interface IBehaviorAnimationSessionPort
{
    bool IsClipAvailable(string clipId);

    bool TryStartPlayback(BehaviorPlaybackRequest request);

    void StopPlayback(BehaviorPlaybackToken playbackToken);
}

public sealed record BehaviorBubblePresentation
{
    public required BehaviorSessionToken SessionToken { get; init; }

    public required int CueIndex { get; init; }

    public required BehaviorBubbleCue Cue { get; init; }

    public required TimeSpan LogicalTime { get; init; }
}

public interface IBehaviorBubbleSessionPort
{
    void Show(BehaviorBubblePresentation presentation);

    void Hide(BehaviorSessionToken ownerSessionToken);
}

public sealed record BehaviorSessionCommitRecord
{
    public required BehaviorSessionToken SessionToken { get; init; }

    public required long RequestId { get; init; }

    public required BehaviorId BehaviorId { get; init; }

    public required TimeSpan CommittedAt { get; init; }
}

public sealed class BehaviorSessionRunnerOptions
{
    public TimeSpan FirstFrameTimeout { get; init; } =
        TimeSpan.FromSeconds(2);

    internal void Validate()
    {
        if (FirstFrameTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FirstFrameTimeout),
                "The first-frame timeout must be positive.");
        }
    }
}

/// <summary>
/// Executes one behavior transaction at a time. All timestamps come from an
/// injected monotonic clock and all presentation side effects go through ports,
/// so the runner can be tested without WPF, timers, or real animation assets.
/// </summary>
public sealed class BehaviorSessionRunner
{
    private readonly IMonotonicClock _clock;
    private readonly HierarchicalPetStateMachine _stateMachine;
    private readonly IBehaviorAnimationSessionPort _animation;
    private readonly IBehaviorBubbleSessionPort _bubbles;
    private readonly BehaviorSessionRunnerOptions _options;
    private readonly IRandomSource _animationVariantRandom;

    private long _nextSessionToken;
    private long _nextPlaybackToken;
    private Session? _session;
    private ActivePlayback? _playback;
    private string? _activeSleepAnimationVariantId;
    private bool _isVisible = true;

    public BehaviorSessionRunner(
        IMonotonicClock clock,
        HierarchicalPetStateMachine stateMachine,
        IBehaviorAnimationSessionPort animation,
        IBehaviorBubbleSessionPort bubbles,
        BehaviorSessionRunnerOptions? options = null,
        IRandomSource? animationVariantRandom = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _stateMachine =
            stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _animation = animation ?? throw new ArgumentNullException(nameof(animation));
        _bubbles = bubbles ?? throw new ArgumentNullException(nameof(bubbles));
        _options = options ?? new BehaviorSessionRunnerOptions();
        _options.Validate();
        _animationVariantRandom = animationVariantRandom ??
            new SystemRandomSource();
    }

    public event Action<BehaviorSessionCommitRecord>? Committed;

    public event Action<BehaviorTerminalRecord>? Terminated;

    public bool IsVisible => _isVisible;

    public bool HasActiveSession =>
        _session is { Terminal: null };

    public bool IsCommitted =>
        _session?.CommittedAt is not null;

    public long? CurrentRequestId =>
        _session is { Terminal: null } session
            ? session.Request.RequestId
            : null;

    public BehaviorPlaybackToken? CurrentPlaybackToken =>
        _playback?.Token;

    public BehaviorTerminalRecord? LastTerminal { get; private set; }

    public BehaviorSessionSnapshot? Snapshot
    {
        get
        {
            if (_session is null)
            {
                return null;
            }

            return new BehaviorSessionSnapshot
            {
                SessionToken = _session.Token,
                BehaviorId = _session.Definition.Id,
                Phase = _session.Terminal is not null
                    ? BehaviorPhase.Terminal
                    : _session.CommittedAt is null
                        ? BehaviorPhase.AwaitingFirstFrame
                        : ToBehaviorPhase(_playback?.Role),
                Committed = _session.CommittedAt is not null,
                ClipId = _playback?.ClipId,
                LogicalTime = LogicalTime(_clock.Elapsed),
                NextCueIndex = _session.NextCueIndex,
                Terminal = _session.Terminal,
            };
        }
    }

    public bool CanStart(
        BehaviorRequest request,
        BehaviorDefinition definition,
        out string? rejectionReason)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(definition);

        try
        {
            request.Validate();
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            rejectionReason = exception.Message;
            return false;
        }

        if (request.BehaviorId != definition.Id)
        {
            rejectionReason = "The request and definition behavior ids differ.";
            return false;
        }

        if (request.IsExpired(_clock.Elapsed))
        {
            rejectionReason = "The behavior request has expired.";
            return false;
        }

        if (!definition.AllowedStates.Contains(_stateMachine.StableState))
        {
            rejectionReason =
                $"Behavior '{definition.Id}' is not allowed in " +
                $"'{_stateMachine.StableState}'.";
            return false;
        }

        string[] requiredClips = definition.Animation.RequiredClipIds()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requiredClips.Length == 0)
        {
            rejectionReason = "The behavior has no animation clips.";
            return false;
        }

        string? missingClip = requiredClips.FirstOrDefault(
            clipId => !_animation.IsClipAvailable(clipId));
        if (missingClip is not null)
        {
            throw new InvalidOperationException(
                $"Behavior content invariant failed: required clip " +
                $"'{missingClip}' for '{definition.Id}' is unavailable.");
        }

        if (definition.TargetState == StablePetState.Sleeping &&
            _stateMachine.StableState == StablePetState.Normal &&
            string.IsNullOrWhiteSpace(definition.Animation.EnterClipId))
        {
            rejectionReason =
                "Entering Sleeping requires a dedicated enter clip.";
            return false;
        }

        rejectionReason = null;
        return true;
    }

    public bool TryStart(
        BehaviorRequest request,
        BehaviorDefinition definition,
        out string? rejectionReason)
    {
        if (HasActiveSession)
        {
            rejectionReason = "A behavior session is already active.";
            return false;
        }

        if (!CanStart(request, definition, out rejectionReason))
        {
            return false;
        }

        return StartCore(request, definition, out rejectionReason);
    }

    public bool NotifyFirstFrame(BehaviorPlaybackToken playbackToken)
    {
        if (!TryGetActivePlayback(playbackToken, out var session, out var playback) ||
            playback.FirstFrameSeen)
        {
            return false;
        }

        playback.FirstFrameSeen = true;
        playback.FirstFrameAt = _clock.Elapsed;

        if (session.CommittedAt is null)
        {
            if (!CommitStateOnFirstFrame(session, playback))
            {
                Terminate(
                    BehaviorTerminalStatus.Failed,
                    "The state transition could not commit on the first frame.");
                return false;
            }

            session.CommittedAt = _clock.Elapsed;
            Committed?.Invoke(
                new BehaviorSessionCommitRecord
                {
                    SessionToken = session.Token,
                    RequestId = session.Request.RequestId,
                    BehaviorId = session.Definition.Id,
                    CommittedAt = session.CommittedAt.Value,
                });
        }
        else if (!CommitLaterStatePhaseOnFirstFrame(session, playback))
        {
            Terminate(
                BehaviorTerminalStatus.Failed,
                "The state lifecycle phase could not start.");
            return false;
        }

        ProcessCueTimeline(
            session,
            LogicalTime(_clock.Elapsed),
            allowPresentation: _isVisible);
        return true;
    }

    public bool NotifyClipCompleted(BehaviorPlaybackToken playbackToken)
    {
        if (!TryGetActivePlayback(playbackToken, out var session, out var playback) ||
            !playback.FirstFrameSeen)
        {
            return false;
        }

        ProcessCueTimeline(
            session,
            LogicalTime(_clock.Elapsed),
            allowPresentation: _isVisible);
        _playback = null;

        switch (playback.Role)
        {
            case BehaviorClipRole.Enter:
                if (!CompleteEnterStatePhase(session))
                {
                    return Terminate(
                        BehaviorTerminalStatus.Failed,
                        "The enter state phase could not complete.");
                }

                if (session.CompletionConditionSatisfied &&
                    session.Definition.Completion.Kind ==
                    BehaviorCompletionKind.External)
                {
                    return BeginExitOrComplete(session);
                }

                return StartFirstAvailable(
                    session,
                    BehaviorClipRole.Perform,
                    BehaviorClipRole.Loop,
                    BehaviorClipRole.Exit);

            case BehaviorClipRole.Perform:
                if (!string.IsNullOrWhiteSpace(
                        session.Animation.LoopClipId))
                {
                    return StartPlayback(session, BehaviorClipRole.Loop);
                }

                return HandleBodyClipBoundary(session, playback.Role);

            case BehaviorClipRole.Loop:
                session.CompletedLoops++;
                return HandleBodyClipBoundary(session, playback.Role);

            case BehaviorClipRole.Exit:
                if (!CompleteExitStatePhase(session))
                {
                    return Terminate(
                        BehaviorTerminalStatus.Failed,
                        "The exit state phase could not complete.");
                }

                session.ExitCompleted = true;
                if (session.SleepExitStartedBeforeBody &&
                    !string.IsNullOrWhiteSpace(
                        session.Animation.PerformClipId))
                {
                    session.SleepExitStartedBeforeBody = false;
                    return StartPlayback(session, BehaviorClipRole.Perform);
                }

                return Terminate(BehaviorTerminalStatus.Completed, reason: null);

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public bool RequestExternalCompletion()
    {
        if (_session is not { Terminal: null } session ||
            session.Definition.Completion.Kind !=
            BehaviorCompletionKind.External ||
            session.CommittedAt is null)
        {
            return false;
        }

        session.CompletionConditionSatisfied = true;
        TryFinishSatisfiedBody(session);
        if (session.Terminal is
            {
                Status: BehaviorTerminalStatus.Failed
            } terminal)
        {
            throw new InvalidOperationException(
                $"External completion failed for behavior " +
                $"'{session.Definition.Id}': " +
                $"{terminal.Reason ?? "unknown playback failure"}.");
        }

        return true;
    }

    public void Advance()
    {
        if (_session is not { Terminal: null } session)
        {
            return;
        }

        var now = _clock.Elapsed;
        if (_playback is { FirstFrameSeen: false } playback &&
            now - playback.RequestedAt >= _options.FirstFrameTimeout)
        {
            Terminate(
                BehaviorTerminalStatus.TimedOut,
                $"Clip '{playback.ClipId}' did not display a first frame in time.");
            return;
        }

        if (session.CommittedAt is null)
        {
            return;
        }

        var logicalTime = LogicalTime(now);
        ProcessCueTimeline(
            session,
            logicalTime,
            allowPresentation: _isVisible);

        if (session.Terminal is not null)
        {
            return;
        }

        if (session.Definition.Completion.Kind ==
                BehaviorCompletionKind.Duration &&
            logicalTime >=
            Max(
                session.Definition.Completion.Duration ?? TimeSpan.Zero,
                session.Definition.MinimumCommitTime) &&
            (session.BodyBoundaryReached ||
             _playback?.Role is
                 BehaviorClipRole.Perform or BehaviorClipRole.Loop))
        {
            session.CompletionConditionSatisfied = true;
            TryFinishSatisfiedBody(session);
        }
        else if (session.CompletionConditionSatisfied)
        {
            TryFinishSatisfiedBody(session);
        }

        if (session.Terminal is null && IsWatchdogExpired(session, now))
        {
            Terminate(
                BehaviorTerminalStatus.TimedOut,
                $"Behavior exceeded its {session.Definition.MaximumDuration} watchdog.");
        }
    }

    /// <summary>
    /// Hidden time remains logical time, but cues crossed while hidden are
    /// consumed without presentation and are never replayed on restoration.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (_isVisible == visible)
        {
            return;
        }

        if (!visible)
        {
            _isVisible = false;
            if (_session is { Terminal: null } hidingSession)
            {
                HideOwnedBubble(hidingSession);
                ProcessCueTimeline(
                    hidingSession,
                    LogicalTime(_clock.Elapsed),
                    allowPresentation: false);
            }

            return;
        }

        if (_session is { Terminal: null } restoringSession)
        {
            ProcessCueTimeline(
                restoringSession,
                LogicalTime(_clock.Elapsed),
                allowPresentation: false);
        }

        _isVisible = true;
    }

    public bool Interrupt(
        BehaviorTerminalStatus status = BehaviorTerminalStatus.Interrupted,
        string? reason = null)
    {
        if (status is not BehaviorTerminalStatus.Interrupted and
            not BehaviorTerminalStatus.Superseded)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                "Only Interrupted or Superseded are valid interruption statuses.");
        }

        return Terminate(status, reason);
    }

    private bool StartCore(
        BehaviorRequest request,
        BehaviorDefinition definition,
        out string? rejectionReason)
    {
        var token = new BehaviorSessionToken(checked(++_nextSessionToken));
        ResolvedAnimationPlan animation = ResolveAnimationPlan(definition);
        var orderedCues = definition.BubbleCues
            .Select(static (cue, index) => new OrderedCue(index, cue))
            .OrderBy(static item => item.Cue.At)
            .ThenBy(static item => item.ConfigurationIndex)
            .ToArray();
        var session = new Session(
            token,
            request,
            definition,
            animation,
            _clock.Elapsed,
            orderedCues);

        _session = session;
        _playback = null;

        if (definition.TargetState == StablePetState.Sleeping &&
            _stateMachine.StableState == StablePetState.Normal)
        {
            try
            {
                session.StateTransitionToken =
                    _stateMachine.PrepareSleepEntry();
                session.IsSleepEntry = true;
            }
            catch (InvalidOperationException exception)
            {
                _activeSleepAnimationVariantId = null;
                _session = null;
                rejectionReason = exception.Message;
                return false;
            }
        }
        else if (definition.TargetState == StablePetState.Sleeping &&
                 _stateMachine.StableState == StablePetState.Sleeping)
        {
            session.IsSleepContinuation = true;
        }
        else if (definition.TargetState == StablePetState.Normal &&
                 _stateMachine.StableState == StablePetState.Sleeping)
        {
            session.IsSleepExit = true;
        }
        else if (definition.TargetState == StablePetState.Dragging &&
                 _stateMachine.StableState != StablePetState.Dragging)
        {
            session.IsDragEntry = true;
        }
        else if (definition.TargetState == StablePetState.Normal &&
                 _stateMachine.StableState == StablePetState.Dragging)
        {
            session.IsDragExit = true;
        }

        BehaviorClipRole? initialRole = SelectInitialRole(session);
        if (initialRole is null)
        {
            CancelPreparedState(session);
            ReconcileAnimationVariantAfterTerminal(session);
            _session = null;
            rejectionReason = "The animation plan has no runnable initial clip.";
            return false;
        }

        if (!StartPlayback(session, initialRole.Value))
        {
            rejectionReason = session.Terminal?.Reason ??
                "The animation port rejected the initial clip.";
            return false;
        }

        rejectionReason = null;
        return true;
    }

    private BehaviorClipRole? SelectInitialRole(Session session)
    {
        ResolvedAnimationPlan animation = session.Animation;
        if (session.IsSleepContinuation)
        {
            return !string.IsNullOrWhiteSpace(animation.LoopClipId)
                ? BehaviorClipRole.Loop
                : null;
        }

        if (session.IsSleepExit)
        {
            if (!string.IsNullOrWhiteSpace(animation.ExitClipId))
            {
                session.SleepExitStartedBeforeBody = true;
                return BehaviorClipRole.Exit;
            }

            if (!string.IsNullOrWhiteSpace(animation.PerformClipId))
            {
                return BehaviorClipRole.Perform;
            }
        }

        return FirstAvailableRole(
            session,
            BehaviorClipRole.Enter,
            BehaviorClipRole.Perform,
            BehaviorClipRole.Loop,
            BehaviorClipRole.Exit);
    }

    private bool StartFirstAvailable(
        Session session,
        params BehaviorClipRole[] roles)
    {
        BehaviorClipRole? role = FirstAvailableRole(session, roles);
        return role is null
            ? Terminate(BehaviorTerminalStatus.Completed, reason: null)
            : StartPlayback(session, role.Value);
    }

    private static BehaviorClipRole? FirstAvailableRole(
        Session session,
        params BehaviorClipRole[] roles)
    {
        foreach (BehaviorClipRole role in roles)
        {
            if (!string.IsNullOrWhiteSpace(ClipId(session.Animation, role)))
            {
                return role;
            }
        }

        return null;
    }

    private bool StartPlayback(Session session, BehaviorClipRole role)
    {
        string? clipId = ClipId(session.Animation, role);
        if (string.IsNullOrWhiteSpace(clipId))
        {
            return false;
        }

        var playback = new ActivePlayback(
            new BehaviorPlaybackToken(checked(++_nextPlaybackToken)),
            role,
            clipId,
            _clock.Elapsed);
        _playback = playback;

        bool accepted;
        try
        {
            accepted = _animation.TryStartPlayback(
                new BehaviorPlaybackRequest
                {
                    SessionToken = session.Token,
                    PlaybackToken = playback.Token,
                    Role = role,
                    ClipId = clipId,
                    ContinueIfSameClip =
                        session.Animation.ContinueIfSameClip,
                });
        }
        catch (Exception exception)
        {
            accepted = false;
            playback.StartFailureReason = exception.Message;
        }

        if (accepted)
        {
            // Animation ports may report the first frame synchronously. That
            // callback is also allowed to complete this session before
            // TryStartPlayback returns.
            return session.Terminal?.Status ==
                       BehaviorTerminalStatus.Completed ||
                   (ReferenceEquals(_session, session) &&
                    session.Terminal is null);
        }

        // TryStartPlayback may synchronously invoke the first-frame callback.
        // That callback can finish the session before the original animation
        // start returns false. Only the still-owning request may clear playback
        // state or terminate the session.
        if (ReferenceEquals(_session, session) &&
            ReferenceEquals(_playback, playback) &&
            session.Terminal is null)
        {
            _playback = null;
            Terminate(
                BehaviorTerminalStatus.Failed,
                playback.StartFailureReason ??
                $"Animation playback rejected clip '{clipId}'.");
        }

        return false;
    }

    private bool HandleBodyClipBoundary(
        Session session,
        BehaviorClipRole role)
    {
        session.BodyBoundaryReached = true;
        switch (session.Definition.Completion.Kind)
        {
            case BehaviorCompletionKind.ClipEnd:
                session.CompletionConditionSatisfied = true;
                break;

            case BehaviorCompletionKind.LoopCount:
                if (role == BehaviorClipRole.Perform)
                {
                    session.CompletedLoops++;
                }

                session.CompletionConditionSatisfied =
                    session.CompletedLoops >=
                    session.Definition.Completion.LoopCount.GetValueOrDefault();
                break;

            case BehaviorCompletionKind.Duration:
                session.CompletionConditionSatisfied =
                    LogicalTime(_clock.Elapsed) >=
                    session.Definition.Completion.Duration.GetValueOrDefault();
                break;

            case BehaviorCompletionKind.External:
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        if (session.CompletionConditionSatisfied &&
            LogicalTime(_clock.Elapsed) >=
            session.Definition.MinimumCommitTime)
        {
            return BeginExitOrComplete(session);
        }

        if (role == BehaviorClipRole.Loop ||
            session.Definition.Completion.Kind ==
            BehaviorCompletionKind.LoopCount)
        {
            return StartPlayback(session, role);
        }

        return true;
    }

    private bool TryFinishSatisfiedBody(Session session)
    {
        if (!session.CompletionConditionSatisfied ||
            LogicalTime(_clock.Elapsed) <
            session.Definition.MinimumCommitTime ||
            _playback?.Role is BehaviorClipRole.Enter or BehaviorClipRole.Exit)
        {
            return false;
        }

        if (_playback is { } playback)
        {
            SafeStop(playback.Token);
            _playback = null;
        }

        BeginExitOrComplete(session);
        return true;
    }

    private bool BeginExitOrComplete(Session session)
    {
        if (!session.ExitCompleted &&
            !string.IsNullOrWhiteSpace(
                session.Animation.ExitClipId))
        {
            return StartPlayback(session, BehaviorClipRole.Exit);
        }

        if ((session.IsSleepEntry || session.IsSleepContinuation) &&
            _stateMachine.StableState == StablePetState.Sleeping)
        {
            _stateMachine.ForceNormal();
            session.StateTransitionToken = null;
        }
        else if (session.IsSleepExit &&
                 _stateMachine.StableState == StablePetState.Sleeping)
        {
            _stateMachine.ForceNormal();
            session.StateTransitionToken = null;
        }

        return Terminate(BehaviorTerminalStatus.Completed, reason: null);
    }

    private bool CommitStateOnFirstFrame(
        Session session,
        ActivePlayback playback)
    {
        if (session.IsSleepEntry)
        {
            return playback.Role == BehaviorClipRole.Enter &&
                session.StateTransitionToken is { } token &&
                _stateMachine.CommitSleepEntryFirstFrame(token);
        }

        if (session.IsSleepExit)
        {
            if (playback.Role == BehaviorClipRole.Exit)
            {
                session.StateTransitionToken = _stateMachine.BeginSleepExit();
                return true;
            }

            session.StateTransitionToken = _stateMachine.BeginSleepExit();
            return _stateMachine.CompleteSleepExit(
                session.StateTransitionToken.Value);
        }

        if (session.IsDragEntry)
        {
            session.StateTransitionToken = _stateMachine.BeginDragging();
            return true;
        }

        if (session.IsDragExit)
        {
            PetStateTransitionToken? token =
                _stateMachine.ActiveTransitionToken;
            return token is not null &&
                _stateMachine.CompleteDragging(token.Value);
        }

        return true;
    }

    private bool CommitLaterStatePhaseOnFirstFrame(
        Session session,
        ActivePlayback playback)
    {
        if (playback.Role != BehaviorClipRole.Exit)
        {
            return true;
        }

        if ((session.IsSleepEntry || session.IsSleepContinuation) &&
            _stateMachine.StableState == StablePetState.Sleeping &&
            _stateMachine.Phase == PetLifecyclePhase.SleepingLooping)
        {
            session.StateTransitionToken = _stateMachine.BeginSleepExit();
        }

        return true;
    }

    private bool CompleteEnterStatePhase(Session session)
    {
        if (!session.IsSleepEntry)
        {
            return true;
        }

        return session.StateTransitionToken is { } token &&
            _stateMachine.CompleteSleepEntry(token);
    }

    private bool CompleteExitStatePhase(Session session)
    {
        if (session.StateTransitionToken is not { } token)
        {
            return !session.IsSleepExit && !session.IsSleepEntry;
        }

        if (_stateMachine.Phase != PetLifecyclePhase.SleepingExiting)
        {
            return true;
        }

        bool completed = _stateMachine.CompleteSleepExit(token);
        if (completed)
        {
            session.StateTransitionToken = null;
        }

        return completed;
    }

    private void ProcessCueTimeline(
        Session session,
        TimeSpan logicalTime,
        bool allowPresentation)
    {
        OrderedCue? lastDue = null;
        while (session.NextCueIndex < session.OrderedCues.Count &&
               session.OrderedCues[session.NextCueIndex].Cue.At <= logicalTime)
        {
            lastDue = session.OrderedCues[session.NextCueIndex];
            session.NextCueIndex++;
        }

        if (lastDue is not null)
        {
            session.ActiveCue = lastDue;
            if (allowPresentation && CueIsActive(lastDue, logicalTime))
            {
                SafeShow(
                    new BehaviorBubblePresentation
                    {
                        SessionToken = session.Token,
                        CueIndex = lastDue.ConfigurationIndex,
                        Cue = lastDue.Cue,
                        LogicalTime = logicalTime,
                    });
                session.BubbleIsPresented = true;
            }
            else
            {
                HideOwnedBubble(session);
            }
        }
        else if (session.ActiveCue is { } activeCue &&
                 !CueIsActive(activeCue, logicalTime))
        {
            HideOwnedBubble(session);
            session.ActiveCue = null;
        }
    }

    private static bool CueIsActive(OrderedCue cue, TimeSpan logicalTime) =>
        cue.Cue.VisibleFor is null ||
        logicalTime < cue.Cue.At + cue.Cue.VisibleFor.Value;

    private bool IsWatchdogExpired(Session session, TimeSpan now)
    {
        if (session.Definition.Completion.Kind ==
                BehaviorCompletionKind.External &&
            !session.Definition.Queueable)
        {
            if (_playback?.Role == BehaviorClipRole.Loop)
            {
                return false;
            }

            TimeSpan phaseStartedAt =
                _playback?.FirstFrameAt ??
                _playback?.RequestedAt ??
                session.CommittedAt ??
                session.CreatedAt;
            return now - phaseStartedAt >
                session.Definition.MaximumDuration;
        }

        return LogicalTime(now) > session.Definition.MaximumDuration;
    }

    private bool Terminate(
        BehaviorTerminalStatus status,
        string? reason)
    {
        if (_session is not { Terminal: null } session)
        {
            return false;
        }

        if (_playback is { } playback)
        {
            SafeStop(playback.Token);
            _playback = null;
        }

        HideOwnedBubble(session);
        ReconcileStateAfterTerminal(session, status);
        ReconcileAnimationVariantAfterTerminal(session);

        var terminal = new BehaviorTerminalRecord(
            session.Token,
            session.Request.RequestId,
            session.Definition.Id,
            status,
            _clock.Elapsed,
            reason);
        session.Terminal = terminal;
        LastTerminal = terminal;
        Terminated?.Invoke(terminal);
        return true;
    }

    private void ReconcileStateAfterTerminal(
        Session session,
        BehaviorTerminalStatus status)
    {
        if (session.IsSleepEntry &&
            session.StateTransitionToken is { } entryToken)
        {
            if (_stateMachine.Phase == PetLifecyclePhase.SleepingEntering ||
                _stateMachine.PreparedSleepEntryToken == entryToken)
            {
                _stateMachine.CancelSleepEntry(entryToken);
                return;
            }
        }

        if (session.StateTransitionToken is { } exitToken &&
            _stateMachine.Phase == PetLifecyclePhase.SleepingExiting)
        {
            if (status is BehaviorTerminalStatus.Failed or
                BehaviorTerminalStatus.TimedOut)
            {
                _stateMachine.ForceNormal();
            }
            else
            {
                _stateMachine.CancelSleepExit(exitToken);
            }
        }
    }

    private void CancelPreparedState(Session session)
    {
        if (session.IsSleepEntry &&
            session.StateTransitionToken is { } token)
        {
            _stateMachine.CancelSleepEntry(token);
        }
    }

    private bool TryGetActivePlayback(
        BehaviorPlaybackToken token,
        out Session session,
        out ActivePlayback playback)
    {
        if (_session is { Terminal: null } candidateSession &&
            _playback is { } candidatePlayback &&
            candidatePlayback.Token == token)
        {
            session = candidateSession;
            playback = candidatePlayback;
            return true;
        }

        session = null!;
        playback = null!;
        return false;
    }

    private TimeSpan LogicalTime(TimeSpan now)
    {
        if (_session?.CommittedAt is not { } committedAt)
        {
            return TimeSpan.Zero;
        }

        return now >= committedAt
            ? now - committedAt
            : TimeSpan.Zero;
    }

    private void HideOwnedBubble(Session session)
    {
        if (!session.BubbleIsPresented)
        {
            return;
        }

        try
        {
            _bubbles.Hide(session.Token);
        }
        catch
        {
            // A bubble presentation failure is isolated from animation and
            // behavior completion.
        }

        session.BubbleIsPresented = false;
    }

    private void SafeShow(BehaviorBubblePresentation presentation)
    {
        try
        {
            _bubbles.Show(presentation);
        }
        catch
        {
            // A bubble presentation failure is isolated from animation and
            // behavior completion.
        }
    }

    private void SafeStop(BehaviorPlaybackToken token)
    {
        try
        {
            _animation.StopPlayback(token);
        }
        catch
        {
            // Presentation cleanup must not prevent the behavior transaction
            // from reaching its exactly-once terminal state.
        }
    }

    private ResolvedAnimationPlan ResolveAnimationPlan(
        BehaviorDefinition definition)
    {
        BehaviorAnimationPlan animation = definition.Animation;
        if (animation.Variants.Count == 0)
        {
            return ResolvedAnimationPlan.FromDefault(animation);
        }

        var candidates = new List<ResolvedAnimationPlan>(
            animation.Variants.Count + 1)
        {
            ResolvedAnimationPlan.FromDefault(animation),
        };
        candidates.AddRange(
            animation.Variants.Select(variant =>
                ResolvedAnimationPlan.FromVariant(
                    variant,
                    animation.ContinueIfSameClip)));

        bool continuesSleeping =
            definition.TargetState == StablePetState.Sleeping &&
            _stateMachine.StableState == StablePetState.Sleeping;
        ResolvedAnimationPlan? selected = continuesSleeping &&
            _activeSleepAnimationVariantId is { } activeVariantId
                ? candidates.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.VariantId,
                        activeVariantId,
                        StringComparison.Ordinal))
                : null;
        selected ??= PickAnimationVariant(candidates);

        if (definition.TargetState == StablePetState.Sleeping)
        {
            _activeSleepAnimationVariantId = selected.VariantId;
        }

        return selected;
    }

    private ResolvedAnimationPlan PickAnimationVariant(
        IReadOnlyList<ResolvedAnimationPlan> candidates)
    {
        double unit = _animationVariantRandom.NextUnit();
        if (!double.IsFinite(unit) || unit < 0 || unit >= 1)
        {
            throw new InvalidOperationException(
                "Animation variant random sources must return a finite value " +
                "in [0, 1).");
        }

        int index = (int)(unit * candidates.Count);
        return candidates[index];
    }

    private void ReconcileAnimationVariantAfterTerminal(Session session)
    {
        if ((session.IsSleepEntry ||
             session.IsSleepContinuation ||
             session.IsSleepExit) &&
            _stateMachine.StableState != StablePetState.Sleeping)
        {
            _activeSleepAnimationVariantId = null;
        }
    }

    private static string? ClipId(
        ResolvedAnimationPlan animation,
        BehaviorClipRole role) =>
        role switch
        {
            BehaviorClipRole.Enter => animation.EnterClipId,
            BehaviorClipRole.Perform => animation.PerformClipId,
            BehaviorClipRole.Loop => animation.LoopClipId,
            BehaviorClipRole.Exit => animation.ExitClipId,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    private static BehaviorPhase ToBehaviorPhase(BehaviorClipRole? role) =>
        role switch
        {
            BehaviorClipRole.Enter => BehaviorPhase.Entering,
            BehaviorClipRole.Perform => BehaviorPhase.Performing,
            BehaviorClipRole.Loop => BehaviorPhase.Looping,
            BehaviorClipRole.Exit => BehaviorPhase.Exiting,
            null => BehaviorPhase.Pending,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;

    private sealed class Session
    {
        public Session(
            BehaviorSessionToken token,
            BehaviorRequest request,
            BehaviorDefinition definition,
            ResolvedAnimationPlan animation,
            TimeSpan createdAt,
            IReadOnlyList<OrderedCue> orderedCues)
        {
            Token = token;
            Request = request;
            Definition = definition;
            Animation = animation;
            CreatedAt = createdAt;
            OrderedCues = orderedCues;
        }

        public BehaviorSessionToken Token { get; }

        public BehaviorRequest Request { get; }

        public BehaviorDefinition Definition { get; }

        public ResolvedAnimationPlan Animation { get; }

        public TimeSpan CreatedAt { get; }

        public IReadOnlyList<OrderedCue> OrderedCues { get; }

        public TimeSpan? CommittedAt { get; set; }

        public int NextCueIndex { get; set; }

        public OrderedCue? ActiveCue { get; set; }

        public bool BubbleIsPresented { get; set; }

        public int CompletedLoops { get; set; }

        public bool CompletionConditionSatisfied { get; set; }

        public bool BodyBoundaryReached { get; set; }

        public bool IsSleepEntry { get; set; }

        public bool IsSleepContinuation { get; set; }

        public bool IsSleepExit { get; set; }

        public bool IsDragEntry { get; set; }

        public bool IsDragExit { get; set; }

        public bool SleepExitStartedBeforeBody { get; set; }

        public bool ExitCompleted { get; set; }

        public PetStateTransitionToken? StateTransitionToken { get; set; }

        public BehaviorTerminalRecord? Terminal { get; set; }
    }

    private sealed class ActivePlayback
    {
        public ActivePlayback(
            BehaviorPlaybackToken token,
            BehaviorClipRole role,
            string clipId,
            TimeSpan requestedAt)
        {
            Token = token;
            Role = role;
            ClipId = clipId;
            RequestedAt = requestedAt;
        }

        public BehaviorPlaybackToken Token { get; }

        public BehaviorClipRole Role { get; }

        public string ClipId { get; }

        public TimeSpan RequestedAt { get; }

        public bool FirstFrameSeen { get; set; }

        public TimeSpan? FirstFrameAt { get; set; }

        public string? StartFailureReason { get; set; }
    }

    private sealed record ResolvedAnimationPlan(
        string? VariantId,
        string? EnterClipId,
        string? PerformClipId,
        string? LoopClipId,
        string? ExitClipId,
        bool ContinueIfSameClip)
    {
        public static ResolvedAnimationPlan FromDefault(
            BehaviorAnimationPlan animation) =>
            new(
                animation.VariantId,
                animation.EnterClipId,
                animation.PerformClipId,
                animation.LoopClipId,
                animation.ExitClipId,
                animation.ContinueIfSameClip);

        public static ResolvedAnimationPlan FromVariant(
            BehaviorAnimationVariant variant,
            bool continueIfSameClip) =>
            new(
                variant.Id,
                variant.EnterClipId,
                variant.PerformClipId,
                variant.LoopClipId,
                variant.ExitClipId,
                continueIfSameClip);
    }

    private sealed record OrderedCue(
        int ConfigurationIndex,
        BehaviorBubbleCue Cue);
}
