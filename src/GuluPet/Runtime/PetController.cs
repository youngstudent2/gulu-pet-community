using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GuluPet.Animation;
using GuluPet.Behavior;
using GuluPet.Dialogue;
using GuluPet.Diagnostics;
using GuluPet.Domain;
using GuluPet.Persistence;
using GuluPet.Platform;
using GuluPet.Presentation;
using GuluPet.Sensing;

namespace GuluPet.Runtime;

internal enum TestClipPlaybackResult
{
    Started,
    UnknownClip,
    TopLevelPresentationActive,
    SideDocked,
    PlaybackFailed,
}

internal sealed class CareInteractionCommittedEventArgs : EventArgs
{
    public CareInteractionCommittedEventArgs(
        string careAction,
        BehaviorId behaviorId,
        BehaviorSessionToken sessionToken,
        long requestId,
        TimeSpan committedAt)
    {
        CareAction = careAction;
        BehaviorId = behaviorId;
        SessionToken = sessionToken;
        RequestId = requestId;
        CommittedAt = committedAt;
    }

    public string CareAction { get; }

    public BehaviorId BehaviorId { get; }

    public BehaviorSessionToken SessionToken { get; }

    public long RequestId { get; }

    public TimeSpan CommittedAt { get; }
}

internal sealed class CareInteractionTerminatedEventArgs : EventArgs
{
    public CareInteractionTerminatedEventArgs(
        string careAction,
        BehaviorId behaviorId,
        BehaviorSessionToken sessionToken,
        long requestId,
        BehaviorTerminalStatus status,
        TimeSpan occurredAt,
        string? reason)
    {
        CareAction = careAction;
        BehaviorId = behaviorId;
        SessionToken = sessionToken;
        RequestId = requestId;
        Status = status;
        OccurredAt = occurredAt;
        Reason = reason;
    }

    public string CareAction { get; }

    public BehaviorId BehaviorId { get; }

    public BehaviorSessionToken SessionToken { get; }

    public long RequestId { get; }

    public BehaviorTerminalStatus Status { get; }

    public TimeSpan OccurredAt { get; }

    public string? Reason { get; }
}

public sealed class PetController : IDisposable
{
    private static readonly TimeSpan RelationshipSaveRetryInterval =
        TimeSpan.FromSeconds(30);

    private readonly PetWindow _petWindow;
    private readonly BubbleWindow _bubbleWindow;
    private readonly AnimationCatalog _animationCatalog;
    private readonly AnimationPlayer _animationPlayer;
    private readonly AnimationPlayerBehaviorPort _animationBehaviorPort;
    private readonly BubbleWindowBehaviorPort _bubbleBehaviorPort;
    private readonly BehaviorRuntimeCoordinator _behaviorRuntime;
    private readonly IMonotonicClock _behaviorClock;
    private readonly IBehaviorTickTraceSink? _behaviorTickTraceSink;
    private readonly RelationshipStateStore? _relationshipStateStore;
    private readonly TimeProvider _relationshipTimeProvider;
    private readonly BehaviorContextTriggerRouter _contextTriggerRouter;
    private readonly IRuntimeContextMonitor _runtimeContextMonitor;
    private readonly DispatcherTimer _behaviorAdvanceTimer;
    private readonly DragAnimationSelector _dragAnimationSelector = new();
    private readonly ActiveInteractionDeduplicator
        _activeInteractionDeduplicator = new();
    private bool _testProbeMode;
    private bool _externalPlaybackActive;
    private bool _topLevelPresentationRequested;
    private bool _topLevelPresentationActive;
    private bool _sideDockActive;
    private bool _sideDockOwnsRuntimePause;
    private bool _presentationVisible = true;
    private bool _dragBehaviorActive;
    private long _lastDragMotionTimestamp;
    private long _behaviorContextRevision;
    private TaskCompletionSource<bool>? _resumedBehaviorFirstFrameCompletion;
    private RelationshipState? _pendingRelationshipState;
    private TimeSpan? _relationshipPersistenceRetryAfter;
    private BehaviorContextSnapshot _latestSensingContext =
        BehaviorContextSnapshot.Empty;
    private bool _isFullscreen;
    private bool _started;
    private bool _disposed;

    internal PetController(
        PetWindow petWindow,
        AnimationCatalog animationCatalog,
        BehaviorCatalog behaviorCatalog,
        DialogueCatalog dialogueCatalog,
        IRuntimeContextMonitor? runtimeContextMonitor = null,
        IBehaviorDailyOpportunityLedger? dailyOpportunityLedger = null,
        IBehaviorTickTraceSink? behaviorTickTraceSink = null,
        IMonotonicClock? behaviorClock = null,
        BehaviorInteractionOptions? behaviorInteractionOptions = null,
        RelationshipStateStore? relationshipStateStore = null,
        TimeProvider? relationshipTimeProvider = null)
    {
        _petWindow = petWindow;
        _animationCatalog = animationCatalog;
        _behaviorClock = behaviorClock ?? SystemMonotonicClock.Instance;
        _relationshipTimeProvider =
            relationshipTimeProvider ?? TimeProvider.System;
        RelationshipStateStore? usableRelationshipStore =
            relationshipStateStore;
        RelationshipState initialRelationship =
            RelationshipState.CreateDefault(
                _relationshipTimeProvider.GetUtcNow());
        if (usableRelationshipStore is not null)
        {
            try
            {
                initialRelationship = usableRelationshipStore.Load();
            }
            catch (Exception exception)
                when (exception is IOException
                      or UnauthorizedAccessException
                      or InvalidDataException
                      or InvalidOperationException
                      or NotSupportedException)
            {
                // If an unreadable document could not be preserved, keep it
                // untouched and disable writes for this session. Relationship
                // recovery must never prevent the pet from starting.
                Debug.WriteLine(
                    "Unable to load GuluPet relationship state; persistence " +
                    $"is disabled for this session: {exception}");
                usableRelationshipStore = null;
            }
        }

        _relationshipStateStore = usableRelationshipStore;
        _contextTriggerRouter = new BehaviorContextTriggerRouter(
            dailyOpportunityLedger ??
            new BehaviorDailyOpportunityStore());
        _runtimeContextMonitor = runtimeContextMonitor
            ?? RuntimeContextMonitor.CreateDisabled();
        _runtimeContextMonitor.SnapshotChanged +=
            OnRuntimeContextSnapshotChanged;

        _animationPlayer = new AnimationPlayer(animationCatalog, petWindow.Dispatcher);
        _animationPlayer.FrameChanged += OnFrameChanged;
        _animationPlayer.AnimationCompleted += OnAnimationCompleted;
        _animationPlayer.LoopBoundaryReached += OnAnimationLoopBoundaryReached;

        _bubbleWindow = new BubbleWindow
        {
            Topmost = petWindow.Topmost,
        };
        _petWindow.SourceInitialized += OnPetWindowSourceInitialized;
        AttachBubbleOwnerIfReady();
        _animationBehaviorPort = new AnimationPlayerBehaviorPort(
            _animationPlayer,
            _animationCatalog);
        BehaviorRuntimeCoordinator? behaviorRuntime = null;
        _bubbleBehaviorPort = new BubbleWindowBehaviorPort(
            _bubbleWindow,
            _petWindow,
            dialogueCatalog,
            new DialogueSelector(dialogueCatalog),
            () => behaviorRuntime?.GetSnapshot(),
            () => _animationPlayer.CurrentAction ?? "idle_breathe");
        _behaviorRuntime = behaviorRuntime = new BehaviorRuntimeCoordinator(
            behaviorCatalog,
            _animationBehaviorPort,
            _bubbleBehaviorPort,
            initialEmotions: new EmotionState
            {
                Affection = initialRelationship.Affection,
            },
            clock: _behaviorClock,
            interactionOptions: behaviorInteractionOptions);
        _behaviorTickTraceSink = behaviorTickTraceSink;
        _behaviorRuntime.AffectionChanged += OnAffectionChanged;
        _behaviorRuntime.SessionCommitted += OnBehaviorSessionCommitted;
        _behaviorRuntime.InteractionSessionTerminated +=
            OnBehaviorInteractionSessionTerminated;
        _behaviorRuntime.ActiveTickEvaluated += OnActiveTickEvaluated;
        _behaviorRuntime.DailyOpportunityPresented +=
            OnDailyOpportunityPresented;
        _bubbleBehaviorPort.SetKindResolver(_behaviorRuntime.GetBubbleKind);
        _animationBehaviorPort.FirstFramePresented +=
            OnBehaviorFirstFramePresented;
        _animationBehaviorPort.PlaybackCompleted +=
            OnBehaviorPlaybackCompleted;
        _animationBehaviorPort.LoopBoundaryReached +=
            OnBehaviorLoopBoundaryReached;

        _petWindow.PetClicked += OnPetClicked;
        _petWindow.PetLongPressed += OnPetLongPressed;
        _petWindow.DragStarted += OnDragStarted;
        _petWindow.DragMoved += OnDragMoved;
        _petWindow.DragCompleted += OnDragCompleted;
        _petWindow.SideDockReleased += OnSideDockReleased;
        _petWindow.PointerGestureRecognized += OnPointerGestureRecognized;

        _behaviorAdvanceTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(50),
            DispatcherPriority.Background,
            OnBehaviorAdvanceTick,
            petWindow.Dispatcher);
    }

    internal event EventHandler? AcceptedActiveInteraction;

    internal event EventHandler<UserInteractionAcceptedEventArgs>?
        UserInteractionAccepted;

    internal event EventHandler<RuntimeContextObservedEventArgs>?
        RuntimeContextObserved;

    internal event EventHandler<CareInteractionCommittedEventArgs>?
        CareInteractionCommitted;

    internal event EventHandler<CareInteractionTerminatedEventArgs>?
        CareInteractionTerminated;

    public event EventHandler<AnimationPlaybackEvent>? FramePresented;

    public string RelationshipStage => _behaviorRuntime.RelationshipStage;

    internal BehaviorContextSnapshot CurrentRuntimeContext =>
        _latestSensingContext;

    internal PetController(
        PetWindow petWindow,
        ValidatedRuntimeContent content,
        IRuntimeContextMonitor runtimeContextMonitor,
        IBehaviorTickTraceSink? behaviorTickTraceSink = null,
        RelationshipStateStore? relationshipStateStore = null,
        TimeProvider? relationshipTimeProvider = null)
        : this(
            petWindow,
            (content ?? throw new ArgumentNullException(nameof(content))).Animations,
            content.Behaviors,
            content.Dialogues,
            runtimeContextMonitor
                ?? throw new ArgumentNullException(nameof(runtimeContextMonitor)),
            behaviorTickTraceSink: behaviorTickTraceSink,
            relationshipStateStore: relationshipStateStore,
            relationshipTimeProvider: relationshipTimeProvider)
    {
    }

    public void Start(bool suspendedForTopLevelPresentation = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _topLevelPresentationRequested =
            suspendedForTopLevelPresentation;
        _topLevelPresentationActive = suspendedForTopLevelPresentation;
        if (suspendedForTopLevelPresentation)
        {
            _behaviorRuntime.StartSuspendedForTopLevelPresentation();
        }
        else
        {
            _behaviorRuntime.Start();
        }

        _started = true;
        ApplyBehaviorContext(_runtimeContextMonitor.Current);
        _runtimeContextMonitor.Start();
        if (!suspendedForTopLevelPresentation)
        {
            _behaviorAdvanceTimer.Start();
        }

        UpdateBubbleSuppression();
    }

    public void SetPresentationVisible(bool isVisible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _presentationVisible = isVisible;
        UpdateBubbleSuppression();
        _behaviorRuntime.SetVisible(isVisible);
    }

    public void SetTopmost(bool isTopmost)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _bubbleWindow.Topmost = isTopmost;
    }

    public void SetFullscreen(bool isFullscreen)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _isFullscreen = isFullscreen;
        ApplyBehaviorContext(
            _latestSensingContext with
            {
                LocalNow = DateTimeOffset.Now,
            });
    }

    internal bool IsTopLevelPresentationRequestedOrActive =>
        _topLevelPresentationRequested || _topLevelPresentationActive;

    internal bool IsSideDockedForTest => _sideDockActive;

    internal void EnterSideDockForTest(SideDockCandidate candidate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(candidate);
        var dragStarted = new PetDragMotionEventArgs(
            new System.Windows.Point(
                candidate.RestoreBounds.Left,
                candidate.RestoreBounds.Top),
            new System.Windows.Point(
                candidate.RestoreBounds.Left,
                candidate.RestoreBounds.Top),
            default,
            default,
            default,
            pressInteractionSessionId: 4_200);
        OnDragStarted(_petWindow, dragStarted);
        if (!_dragBehaviorActive)
        {
            throw new InvalidOperationException(
                "The side-dock test could not begin a drag transaction.");
        }

        var dragCompleted = new PetDragMotionEventArgs(
            dragStarted.ScreenPosition,
            dragStarted.WindowPosition,
            default,
            default,
            default,
            dragStarted.PressInteractionSessionId,
            candidate);
        OnDragCompleted(_petWindow, dragCompleted);
        if (!_sideDockActive)
        {
            throw new InvalidOperationException(
                "Side docking is unavailable during top-level presentation.");
        }
    }

    internal void ExitSideDockForTest(bool restoreWindow = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ExitSideDock(restoreWindow, updateWindow: true);
    }

    internal TestClipPlaybackResult PlayTestClipForTest(string clipId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(clipId);
        if (_sideDockActive)
        {
            return TestClipPlaybackResult.SideDocked;
        }

        if (_topLevelPresentationRequested ||
            _topLevelPresentationActive)
        {
            return TestClipPlaybackResult.TopLevelPresentationActive;
        }

        if (!_animationCatalog.TryGet(clipId, out _))
        {
            return TestClipPlaybackResult.UnknownClip;
        }

        bool acquiredExternalPlayback = false;
        if (!_testProbeMode && !_externalPlaybackActive)
        {
            _behaviorRuntime.PauseForExternalControl();
            _externalPlaybackActive = true;
            acquiredExternalPlayback = true;
            UpdateBubbleSuppression();
        }

        if (!_animationPlayer.TryPlay(clipId))
        {
            if (acquiredExternalPlayback)
            {
                ReleaseExternalPlayback();
            }

            return TestClipPlaybackResult.PlaybackFailed;
        }

        return TestClipPlaybackResult.Started;
    }

    internal void SetTestProbeMode(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_testProbeMode == enabled)
        {
            return;
        }

        _testProbeMode = enabled;
        if (enabled)
        {
            _behaviorAdvanceTimer.Stop();
            _behaviorRuntime.PauseForExternalControl();
            _externalPlaybackActive = false;
            if (_sideDockActive)
            {
                // Test-probe mode takes ownership of the already-held pause.
                // Undocking must not resume behavior while the probe remains.
                _sideDockOwnsRuntimePause = false;
            }

            UpdateBubbleSuppression();
            return;
        }

        _externalPlaybackActive = false;
        if (_sideDockActive)
        {
            // Keep the coordinator paused and transfer ownership back to the
            // dock until the pet is explicitly released from the edge.
            _sideDockOwnsRuntimePause = true;
            UpdateBubbleSuppression();
            return;
        }

        UpdateBubbleSuppression();
        _behaviorRuntime.ResumeAfterExternalControl();
        _behaviorAdvanceTimer.Start();
    }

    internal PetRuntimeDiagnostics GetTestDiagnostics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        BehaviorCoordinatorSnapshot behavior = _behaviorRuntime.GetSnapshot();
        EmotionState emotions = behavior.Emotions;
        return new PetRuntimeDiagnostics(
            _animationPlayer.CurrentAction,
            _animationPlayer.CurrentFrameIndex,
            _animationPlayer.CurrentFrameCount,
            _animationPlayer.CurrentFps,
            _animationPlayer.CurrentLoop,
            _animationPlayer.CurrentAssetStatus,
            $"{behavior.State.StableState.ToString().ToLowerInvariant()}:" +
            behavior.State.Phase.ToString().ToLowerInvariant(),
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["energy"] = emotions.Energy,
                ["sleepiness"] = emotions.Sleepiness,
                ["boredom"] = emotions.Boredom,
                ["curiosity"] = emotions.Curiosity,
                ["happiness"] = emotions.Happiness,
                ["stress"] = emotions.Stress,
                ["affection"] = emotions.Affection,
            });
    }

    internal IReadOnlyList<AnimationRuntimeDiagnostics> GetTestClipDiagnostics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _animationCatalog.Names
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                AnimationClip clip = _animationCatalog[name];
                AnimationClipDefinition definition = clip.Definition;
                return new AnimationRuntimeDiagnostics(
                    definition.Name,
                    clip.Frames.Count,
                    definition.Fps,
                    definition.Loop,
                    definition.AssetStatus,
                    definition.Placeholder,
                    definition.SourceBatch,
                    definition.SourceStage,
                    definition.SourceFrameIndices,
                    definition.KnownIssues);
            })
            .ToArray();
    }

    internal BehaviorCoordinatorSnapshot GetBehaviorTestSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _behaviorRuntime.GetSnapshot();
    }

    internal BehaviorPlaybackToken? GetBehaviorTestPlaybackToken()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _behaviorRuntime.Sessions.CurrentPlaybackToken;
    }

    internal BehaviorSelectionPlan EvaluateBehaviorForTest()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _behaviorRuntime.EvaluateActiveTick();
    }

    internal BehaviorTickResult ForceBehaviorTickForTest(int? seed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sideDockActive)
        {
            return CreateSideDockBlockedTickResult();
        }

        return _behaviorRuntime.ForceActiveTick(seed);
    }

    internal BehaviorMutationResult EnqueueBehaviorForTest(string behaviorId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var resolvedBehaviorId = new BehaviorId(behaviorId);
        if (_sideDockActive)
        {
            return SideDockRejectedMutation(resolvedBehaviorId);
        }

        return _behaviorRuntime.EnqueueSoft(
            resolvedBehaviorId,
            BehaviorRequestSource.TestControl);
    }

    internal BehaviorMutationResult StartBehaviorIfIdleForTest(string behaviorId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var resolvedBehaviorId = new BehaviorId(behaviorId);
        if (_sideDockActive)
        {
            return SideDockRejectedMutation(resolvedBehaviorId);
        }

        return _behaviorRuntime.TryStartImmediate(
            resolvedBehaviorId,
            BehaviorRequestSource.TestControl);
    }

    internal BehaviorMutationResult StartBehaviorManualPreview(
        BehaviorId behaviorId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sideDockActive)
        {
            return SideDockRejectedMutation(behaviorId);
        }

        BehaviorMutationResult result =
            _behaviorRuntime.StartManualPreview(behaviorId);
        if (result.Accepted)
        {
            RaiseUserInteractionAccepted(
                UserInteractionKind.ManualBehavior,
                behaviorId.Value);
        }

        return result;
    }

    internal BehaviorEventPage ReadBehaviorEventsForTest(long afterSequence)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _behaviorRuntime.ReadEventsAfter(afterSequence);
    }

    internal BehaviorMutationResult TriggerClickForTest(
        long pressInteractionSessionId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sideDockActive)
        {
            return SideDockRejectedMutation();
        }

        BehaviorMutationResult result = _behaviorRuntime.TriggerClick();
        NotifyAcceptedActiveInteraction(
            result,
            UserInteractionKind.Click,
            pressInteractionSessionId);
        return result;
    }

    internal BehaviorMutationResult TriggerCareInteraction(string careAction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string triggerTag = careAction switch
        {
            "feed" => "trigger:feed",
            "water" => "trigger:water",
            _ => throw new ArgumentException(
                "Care action must be exactly 'feed' or 'water'.",
                nameof(careAction)),
        };
        if (_sideDockActive)
        {
            return SideDockRejectedMutation();
        }

        BehaviorMutationResult result =
            _behaviorRuntime.TriggerToolbarInteractionTag(triggerTag);
        NotifyAcceptedActiveInteraction(
            result,
            string.Equals(careAction, "feed", StringComparison.Ordinal)
                ? UserInteractionKind.Feed
                : UserInteractionKind.Water);
        return result;
    }

    internal BehaviorMutationResult TriggerPettingFromInteractionBar()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sideDockActive)
        {
            return SideDockRejectedMutation();
        }

        BehaviorMutationResult result =
            _behaviorRuntime.TriggerToolbarInteractionTag("trigger:petting");
        NotifyAcceptedActiveInteraction(
            result,
            UserInteractionKind.ToolbarPetting);
        return result;
    }

    internal void AdvanceBehaviorRuntimeForTest()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AdvanceBehaviorRuntime();
    }

    internal async Task SuspendAfterCurrentInteractionAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _topLevelPresentationRequested = true;
        try
        {
            if (_sideDockActive)
            {
                if (!_sideDockOwnsRuntimePause ||
                    _testProbeMode ||
                    _externalPlaybackActive ||
                    !_behaviorRuntime
                        .TryTransferExternalControlPauseToTopLevelPresentation())
                {
                    throw new InvalidOperationException(
                        "Side-dock pause ownership could not be transferred " +
                        "to the top-level presentation.");
                }

                _sideDockOwnsRuntimePause = false;
                ExitSideDock(restoreWindow: true, updateWindow: true);
            }
            else
            {
                await _behaviorRuntime.SuspendAfterCurrentInteractionAsync();
            }

            ObjectDisposedException.ThrowIf(_disposed, this);

            _topLevelPresentationActive = true;
            _behaviorAdvanceTimer.Stop();
            _bubbleBehaviorPort.HideImmediately();
            UpdateBubbleSuppression();
        }
        catch
        {
            _topLevelPresentationRequested = false;
            throw;
        }
    }

    internal Task ResumeBehaviorAfterTopLevelPresentationAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_topLevelPresentationActive)
        {
            return Task.CompletedTask;
        }

        if (_resumedBehaviorFirstFrameCompletion is { } pending)
        {
            return pending.Task;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _resumedBehaviorFirstFrameCompletion = completion;
        _behaviorRuntime.ResumeAfterTopLevelPresentation();
        _behaviorAdvanceTimer.Start();
        return completion.Task;
    }

    internal void CompleteTopLevelPresentation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _topLevelPresentationRequested = false;
        _topLevelPresentationActive = false;
        _resumedBehaviorFirstFrameCompletion = null;
        UpdateBubbleSuppression();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_sideDockActive || _petWindow.IsSideDocked)
        {
            // Disposal owns presentation cleanup, but must not restart a
            // runtime that is about to be torn down.
            _sideDockOwnsRuntimePause = false;
            ExitSideDock(restoreWindow: true, updateWindow: true);
        }

        TryPersistPendingRelationship(force: true);
        _disposed = true;
        _behaviorAdvanceTimer.Stop();
        _behaviorRuntime.CancelPendingTopLevelPresentationSuspension();
        _resumedBehaviorFirstFrameCompletion?.TrySetCanceled();
        _resumedBehaviorFirstFrameCompletion = null;
        _runtimeContextMonitor.SnapshotChanged -=
            OnRuntimeContextSnapshotChanged;
        _runtimeContextMonitor.Stop();
        _runtimeContextMonitor.Dispose();

        _petWindow.PetClicked -= OnPetClicked;
        _petWindow.PetLongPressed -= OnPetLongPressed;
        _petWindow.DragStarted -= OnDragStarted;
        _petWindow.DragMoved -= OnDragMoved;
        _petWindow.DragCompleted -= OnDragCompleted;
        _petWindow.SideDockReleased -= OnSideDockReleased;
        _petWindow.PointerGestureRecognized -= OnPointerGestureRecognized;
        _petWindow.SourceInitialized -= OnPetWindowSourceInitialized;

        _animationPlayer.FrameChanged -= OnFrameChanged;
        _animationPlayer.AnimationCompleted -= OnAnimationCompleted;
        _animationPlayer.LoopBoundaryReached -= OnAnimationLoopBoundaryReached;
        _animationBehaviorPort.FirstFramePresented -=
            OnBehaviorFirstFramePresented;
        _animationBehaviorPort.PlaybackCompleted -=
            OnBehaviorPlaybackCompleted;
        _animationBehaviorPort.LoopBoundaryReached -=
            OnBehaviorLoopBoundaryReached;
        _behaviorRuntime.ActiveTickEvaluated -= OnActiveTickEvaluated;
        _behaviorRuntime.DailyOpportunityPresented -=
            OnDailyOpportunityPresented;
        _behaviorRuntime.AffectionChanged -= OnAffectionChanged;
        _behaviorRuntime.SessionCommitted -= OnBehaviorSessionCommitted;
        _behaviorRuntime.InteractionSessionTerminated -=
            OnBehaviorInteractionSessionTerminated;
        _animationBehaviorPort.Dispose();
        _animationPlayer.Dispose();
        if (_bubbleWindow.IsLoaded)
        {
            _bubbleWindow.Close();
        }
    }

    private void OnFrameChanged(object? sender, System.Windows.Media.Imaging.BitmapSource frame)
    {
        if (_sideDockActive)
        {
            return;
        }

        _petWindow.SetPetFrame(frame);
        string? clipId = _animationPlayer.CurrentAction;
        if (clipId is null)
        {
            return;
        }

        FramePresented?.Invoke(
            this,
            new AnimationPlaybackEvent(
                _animationPlayer.CurrentPlaybackToken,
                clipId,
                _animationPlayer.CurrentFrameIndex));
    }

    private void OnRuntimeContextSnapshotChanged(
        object? sender,
        RuntimeContextSnapshotChangedEventArgs e)
    {
        if (_disposed || _petWindow.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (_petWindow.Dispatcher.CheckAccess())
        {
            ApplyBehaviorContext(e.Snapshot);
            return;
        }

        _ = _petWindow.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(
                () =>
                {
                    if (!_disposed)
                    {
                        ApplyBehaviorContext(e.Snapshot);
                    }
                }));
    }

    private void ApplyBehaviorContext(BehaviorContextSnapshot sensingContext)
    {
        _petWindow.Dispatcher.VerifyAccess();
        _latestSensingContext = sensingContext;
        RuntimeContextObserved?.Invoke(
            this,
            new RuntimeContextObservedEventArgs(sensingContext));
        sensingContext = sensingContext with
        {
            Revision = checked(++_behaviorContextRevision),
            IsFullscreen = _isFullscreen,
        };
        _behaviorRuntime.UpdateContext(sensingContext);
        if (!_started)
        {
            return;
        }

        IReadOnlyList<BehaviorContextTriggerSignal> signals =
            _contextTriggerRouter.Observe(sensingContext);
        if (_sideDockActive)
        {
            // Advance the edge detectors without replaying dock-time context
            // changes as behavior triggers after the pet is released.
            return;
        }

        foreach (BehaviorContextTriggerSignal signal in signals)
        {
            _behaviorRuntime.TriggerSoftTag(
                signal.Tag,
                signal.Source,
                signal.TimeToLive,
                signal.DailyOpportunity);
        }
    }

    private void OnDailyOpportunityPresented(
        BehaviorDailyOpportunity opportunity)
    {
        _contextTriggerRouter.MarkPresented(
            opportunity,
            DateTimeOffset.Now);
    }

    private void OnPetWindowSourceInitialized(object? sender, EventArgs e) =>
        AttachBubbleOwnerIfReady();

    private void AttachBubbleOwnerIfReady()
    {
        if (_bubbleWindow.Owner is not null ||
            new WindowInteropHelper(_petWindow).Handle == IntPtr.Zero)
        {
            return;
        }

        _bubbleWindow.Owner = _petWindow;
    }

    private void OnAnimationCompleted(object? sender, string action)
    {
        if (_sideDockActive)
        {
            return;
        }

        if (_testProbeMode &&
            !string.Equals(
                action,
                "idle_breathe",
                StringComparison.OrdinalIgnoreCase))
        {
            _animationPlayer.TryPlay("idle_breathe");
            return;
        }

        if (_externalPlaybackActive && !_testProbeMode)
        {
            ReleaseExternalPlayback();
        }
    }

    private void OnAnimationLoopBoundaryReached(
        object? sender,
        AnimationPlaybackEvent playback)
    {
        if (_sideDockActive)
        {
            return;
        }

        if (!_externalPlaybackActive || _testProbeMode)
        {
            return;
        }

        ReleaseExternalPlayback();
    }

    private void OnBehaviorFirstFramePresented(BehaviorPlaybackToken token)
    {
        if (_behaviorRuntime.NotifyFirstFrame(token))
        {
            _resumedBehaviorFirstFrameCompletion?.TrySetResult(true);
        }
    }

    private void OnBehaviorPlaybackCompleted(BehaviorPlaybackToken token)
    {
        _behaviorRuntime.NotifyClipCompleted(token);
    }

    private void OnBehaviorLoopBoundaryReached(BehaviorPlaybackToken token)
    {
        _behaviorRuntime.NotifyLoopBoundary(token);
    }

    private void OnBehaviorSessionCommitted(
        BehaviorSessionCommitRecord committed)
    {
        if (!TryGetCareAction(
                committed.BehaviorId,
                out string careAction,
                out string triggerTag))
        {
            return;
        }

        BehaviorInteractionLeaseSnapshot? lease =
            _behaviorRuntime.GetSnapshot().InteractionLease;
        if (lease is null ||
            !string.Equals(
                lease.TriggerTag,
                triggerTag,
                StringComparison.Ordinal) ||
            lease.RequestId != committed.RequestId ||
            lease.BehaviorId != committed.BehaviorId ||
            lease.SessionToken != committed.SessionToken)
        {
            return;
        }

        CareInteractionCommitted?.Invoke(
            this,
            new CareInteractionCommittedEventArgs(
                careAction,
                committed.BehaviorId,
                committed.SessionToken,
                committed.RequestId,
                committed.CommittedAt));
    }

    private void OnBehaviorInteractionSessionTerminated(
        BehaviorTerminalRecord terminal,
        BehaviorInteractionLeaseSnapshot lease)
    {
        if (lease.RequestId != terminal.RequestId ||
            lease.BehaviorId != terminal.BehaviorId ||
            (lease.SessionToken is { } leaseSession &&
             leaseSession != terminal.SessionToken) ||
            !TryGetCareActionForTrigger(
                lease.TriggerTag,
                out string careAction))
        {
            return;
        }

        CareInteractionTerminated?.Invoke(
            this,
            new CareInteractionTerminatedEventArgs(
                careAction,
                terminal.BehaviorId,
                terminal.SessionToken,
                terminal.RequestId,
                terminal.Status,
                terminal.OccurredAt,
                terminal.Reason));
    }

    private void OnPetClicked(object? sender, PetPressEventArgs e)
    {
        if (_sideDockActive)
        {
            return;
        }

        NotifyAcceptedActiveInteraction(
            _behaviorRuntime.TriggerClick(),
            UserInteractionKind.Click,
            e.PressInteractionSessionId);
    }

    private void OnPetLongPressed(object? sender, PetPressEventArgs e)
    {
        if (_sideDockActive)
        {
            return;
        }

        NotifyAcceptedActiveInteraction(
            _behaviorRuntime.TriggerLongPress(),
            UserInteractionKind.LongPress,
            e.PressInteractionSessionId);
    }

    private void OnDragStarted(object? sender, PetDragMotionEventArgs e)
    {
        if (_sideDockActive)
        {
            return;
        }

        _bubbleBehaviorPort.HideImmediately();
        _dragBehaviorActive = _behaviorRuntime.BeginDragging(out _);
        if (!_dragBehaviorActive)
        {
            return;
        }

        _dragAnimationSelector.Begin();
        _lastDragMotionTimestamp = Stopwatch.GetTimestamp();
        _animationPlayer.TryPlay(_dragAnimationSelector.CurrentClipId);
    }

    private void OnDragMoved(object? sender, PetDragMotionEventArgs e)
    {
        if (_sideDockActive || !_dragBehaviorActive)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        TimeSpan elapsed = _lastDragMotionTimestamp == 0
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(_lastDragMotionTimestamp, now);
        _lastDragMotionTimestamp = now;
        if (_dragAnimationSelector.Observe(
                e.SpeedPixelsPerSecond,
                elapsed))
        {
            _animationPlayer.TryPlay(_dragAnimationSelector.CurrentClipId);
        }
    }

    private void OnDragCompleted(object? sender, PetDragMotionEventArgs e)
    {
        _lastDragMotionTimestamp = 0;
        if (!_dragBehaviorActive)
        {
            return;
        }

        _dragBehaviorActive = false;
        if (e.SideDockCandidate is { } candidate &&
            TryEnterSideDock(candidate))
        {
            RaiseUserInteractionAccepted(
                candidate.Edge == SideDockEdge.Left
                    ? UserInteractionKind.DockLeft
                    : UserInteractionKind.DockRight);
            return;
        }

        NotifyAcceptedActiveInteraction(
            _behaviorRuntime.TriggerDragRelease(),
            UserInteractionKind.Drag,
            e.PressInteractionSessionId);
    }

    private void OnSideDockReleased(object? sender, EventArgs e)
    {
        if (_disposed || !_sideDockActive)
        {
            return;
        }

        // PetWindow has already restored its normal presentation before
        // raising this event (for example, when a new drag starts).
        ExitSideDock(restoreWindow: false, updateWindow: false);
    }

    private void OnPointerGestureRecognized(
        object? sender,
        PetPointerGestureEventArgs e)
    {
        if (_sideDockActive)
        {
            return;
        }

        string tag = e.Gesture switch
        {
            PetPointerGesture.Approach => "trigger:approach",
            PetPointerGesture.Hover => "trigger:hover",
            PetPointerGesture.Petting => "trigger:petting",
            PetPointerGesture.PettingEnded => "trigger:pet_end",
            PetPointerGesture.RapidPointer => "trigger:rapid_pointer",
            PetPointerGesture.CirclePointer => "trigger:circle_pointer",
            _ => throw new ArgumentOutOfRangeException(
                nameof(e),
                e.Gesture,
                "Unknown pointer gesture."),
        };
        BehaviorMutationResult result =
            _behaviorRuntime.TriggerInteractionTag(tag);
        if (!result.Accepted)
        {
            return;
        }

        RaiseUserInteractionAccepted(MapPointerGesture(e.Gesture));
        if (!IsCountedPointerGesture(e.Gesture)
            || !_activeInteractionDeduplicator
                .TryAcceptPointerFocusSession(
                    e.PointerFocusSessionId))
        {
            return;
        }

        AcceptedActiveInteraction?.Invoke(this, EventArgs.Empty);
    }

    private void OnBehaviorAdvanceTick(object? sender, EventArgs e)
    {
        AdvanceBehaviorRuntime();
    }

    private void AdvanceBehaviorRuntime()
    {
        TryPersistPendingRelationship(force: false);
        if (_testProbeMode || _sideDockActive || _topLevelPresentationActive)
        {
            return;
        }

        _behaviorRuntime.Advance();
    }

    private void OnAffectionChanged(double affection)
    {
        if (_relationshipStateStore is null)
        {
            return;
        }

        _pendingRelationshipState = RelationshipState.Create(
            affection,
            _relationshipTimeProvider.GetUtcNow());
        TryPersistPendingRelationship(force: true);
    }

    private void TryPersistPendingRelationship(bool force)
    {
        if (_relationshipStateStore is null ||
            _pendingRelationshipState is not { } pending)
        {
            return;
        }

        if (!force &&
            _relationshipPersistenceRetryAfter is { } retryAfter &&
            _behaviorClock.Elapsed < retryAfter)
        {
            return;
        }

        try
        {
            _relationshipStateStore.Save(pending);
            if (ReferenceEquals(_pendingRelationshipState, pending))
            {
                _pendingRelationshipState = null;
                _relationshipPersistenceRetryAfter = null;
            }
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or InvalidDataException
                  or InvalidOperationException
                  or NotSupportedException)
        {
            _relationshipPersistenceRetryAfter =
                _behaviorClock.Elapsed + RelationshipSaveRetryInterval;
            Debug.WriteLine(
                $"Unable to save GuluPet relationship state: {exception}");
        }
    }

    private void OnActiveTickEvaluated(BehaviorTickTrace trace)
    {
        try
        {
            _behaviorTickTraceSink?.Write(trace);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"GuluPet tick-score trace sink failed: {exception}");
        }
    }

    private bool TryEnterSideDock(SideDockCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (_sideDockActive)
        {
            return true;
        }

        if (_topLevelPresentationRequested || _topLevelPresentationActive)
        {
            return false;
        }

        BehaviorCoordinatorSnapshot snapshot =
            _behaviorRuntime.GetSnapshot();
        bool externalPlaybackOwnedPause =
            _externalPlaybackActive && !_testProbeMode;

        try
        {
            // Commit the visual/native move before closing the drag gate. If
            // an asset or SetWindowPos fails, the ordinary drag-release path
            // remains intact and can recover without crashing the UI thread.
            _petWindow.EnterSideDock(candidate);
        }
        catch (Exception error)
            when (error is Win32Exception or
                  InvalidDataException or
                  InvalidOperationException)
        {
            Trace.TraceWarning(
                "Side docking was skipped after presentation failed: {0}",
                error.Message);
            return false;
        }

        _sideDockOwnsRuntimePause =
            !_testProbeMode &&
            (externalPlaybackOwnedPause || !snapshot.Paused);
        _sideDockActive = true;
        _externalPlaybackActive = false;
        _dragBehaviorActive = false;
        _lastDragMotionTimestamp = 0;

        _behaviorAdvanceTimer.Stop();
        _behaviorRuntime.PauseForExternalControl();
        _ = _behaviorRuntime.TriggerDragRelease();
        _animationPlayer.Stop();
        _bubbleBehaviorPort.HideImmediately();
        UpdateBubbleSuppression();
        return true;
    }

    private void ExitSideDock(bool restoreWindow, bool updateWindow)
    {
        if (!_sideDockActive)
        {
            if (updateWindow && _petWindow.IsSideDocked)
            {
                _petWindow.ExitSideDock(restoreWindow);
            }

            return;
        }

        bool resumeOwnedPause = _sideDockOwnsRuntimePause;

        // Clear controller state before asking PetWindow to exit because its
        // SideDockReleased event is raised synchronously.
        _sideDockActive = false;
        _sideDockOwnsRuntimePause = false;
        if (updateWindow && _petWindow.IsSideDocked)
        {
            _petWindow.ExitSideDock(restoreWindow);
        }

        UpdateBubbleSuppression();
        if (!resumeOwnedPause ||
            _testProbeMode ||
            _externalPlaybackActive ||
            _topLevelPresentationRequested ||
            _topLevelPresentationActive)
        {
            return;
        }

        _behaviorRuntime.ResumeAfterExternalControl();
        if (_started)
        {
            _behaviorAdvanceTimer.Start();
        }
    }

    private BehaviorTickResult CreateSideDockBlockedTickResult()
    {
        BehaviorCoordinatorSnapshot snapshot =
            _behaviorRuntime.GetSnapshot();
        BehaviorSelectionPlan evaluation =
            snapshot.LastEvaluation ??
            new BehaviorSelectionPlan
            {
                AllEvaluations = Array.Empty<BehaviorEvaluation>(),
                TopCandidates = Array.Empty<BehaviorEvaluation>(),
            };
        return new BehaviorTickResult
        {
            WasDue = false,
            Evaluation = evaluation,
            SelectedBehaviorId = null,
            AttemptOrder = Array.Empty<BehaviorId>(),
            QueueResult = null,
            NextDueAt = snapshot.NextTickAt,
        };
    }

    private static BehaviorMutationResult SideDockRejectedMutation(
        BehaviorId? behaviorId = null) =>
        new()
        {
            Accepted = false,
            BehaviorId = behaviorId,
            RequestId = null,
            RejectionReason = "side-docked",
        };

    private void ReleaseExternalPlayback()
    {
        if (!_externalPlaybackActive)
        {
            return;
        }

        _externalPlaybackActive = false;
        UpdateBubbleSuppression();
        if (!_testProbeMode && !_sideDockActive)
        {
            _behaviorRuntime.ResumeAfterExternalControl();
        }
    }

    private void UpdateBubbleSuppression() =>
        _bubbleBehaviorPort.SetSuppressed(
             !_presentationVisible ||
             _testProbeMode ||
             _externalPlaybackActive ||
             _sideDockActive ||
             _topLevelPresentationActive);

    private void NotifyAcceptedActiveInteraction(
        BehaviorMutationResult result,
        UserInteractionKind interactionKind,
        long? pressInteractionSessionId = null)
    {
        if (!result.Accepted)
        {
            return;
        }

        if (pressInteractionSessionId is { } sessionId)
        {
            if (!_activeInteractionDeduplicator
                    .TryAcceptPressSession(sessionId))
            {
                return;
            }
        }

        RaiseUserInteractionAccepted(interactionKind);
        AcceptedActiveInteraction?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseUserInteractionAccepted(
        UserInteractionKind kind,
        string? detail = null) =>
        UserInteractionAccepted?.Invoke(
            this,
            new UserInteractionAcceptedEventArgs(
                kind,
                DateTimeOffset.UtcNow,
                detail));

    private static UserInteractionKind MapPointerGesture(
        PetPointerGesture gesture) => gesture switch
        {
            PetPointerGesture.Approach => UserInteractionKind.Approach,
            PetPointerGesture.Hover => UserInteractionKind.Hover,
            PetPointerGesture.Petting => UserInteractionKind.Petting,
            PetPointerGesture.PettingEnded => UserInteractionKind.PettingEnded,
            PetPointerGesture.RapidPointer => UserInteractionKind.RapidPointer,
            PetPointerGesture.CirclePointer => UserInteractionKind.CirclePointer,
            _ => throw new ArgumentOutOfRangeException(
                nameof(gesture),
                gesture,
                "Unknown pointer gesture."),
        };

    private static bool IsCountedPointerGesture(
        PetPointerGesture gesture) =>
        gesture is
            PetPointerGesture.Petting or
            PetPointerGesture.RapidPointer or
            PetPointerGesture.CirclePointer;

    private static bool TryGetCareAction(
        BehaviorId behaviorId,
        out string careAction,
        out string triggerTag)
    {
        (string Action, string Trigger)? resolved = behaviorId.Value switch
        {
            "care_feed_cat_treat" => ("feed", "trigger:feed"),
            "care_drink_water" => ("water", "trigger:water"),
            _ => null,
        };
        careAction = resolved?.Action ?? string.Empty;
        triggerTag = resolved?.Trigger ?? string.Empty;
        return resolved is not null;
    }

    private static bool TryGetCareActionForTrigger(
        string triggerTag,
        out string careAction)
    {
        careAction = triggerTag switch
        {
            "trigger:feed" => "feed",
            "trigger:water" => "water",
            _ => string.Empty,
        };
        return careAction.Length > 0;
    }

}
