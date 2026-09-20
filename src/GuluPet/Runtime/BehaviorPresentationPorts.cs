using GuluPet.Animation;
using GuluPet.Behavior;
using GuluPet.Dialogue;
using GuluPet.Domain;
using GuluPet.Presentation;

namespace GuluPet.Runtime;

internal sealed class AnimationPlayerBehaviorPort :
    IBehaviorAnimationSessionPort,
    IDisposable
{
    private readonly AnimationPlayer _player;
    private readonly AnimationCatalog _catalog;
    private BehaviorPlaybackRequest? _pendingStart;
    private BehaviorPlaybackToken? _logicalPlayback;
    private long _physicalPlayback;
    private bool _disposed;

    public AnimationPlayerBehaviorPort(
        AnimationPlayer player,
        AnimationCatalog catalog)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _player.FirstFramePresented += OnFirstFramePresented;
        _player.PlaybackCompleted += OnPlaybackCompleted;
        _player.LoopBoundaryReached += OnLoopBoundaryReached;
    }

    public event Action<BehaviorPlaybackToken>? FirstFramePresented;

    public event Action<BehaviorPlaybackToken>? PlaybackCompleted;

    public event Action<BehaviorPlaybackToken>? LoopBoundaryReached;

    private bool HasClip(string clipId) =>
        !string.IsNullOrWhiteSpace(clipId) &&
        _catalog.TryGet(clipId, out _);

    public bool IsClipAvailable(string clipId) => HasClip(clipId);

    public bool TryStartPlayback(BehaviorPlaybackRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (!HasClip(request.ClipId))
        {
            return false;
        }

        if (request.ContinueIfSameClip &&
            string.Equals(
                _player.CurrentAction,
                request.ClipId,
                StringComparison.OrdinalIgnoreCase) &&
            _player.CurrentPlaybackToken > 0)
        {
            _logicalPlayback = request.PlaybackToken;
            _physicalPlayback = _player.CurrentPlaybackToken;
            FirstFramePresented?.Invoke(request.PlaybackToken);
            return true;
        }

        _pendingStart = request;
        try
        {
            bool accepted = _player.TryPlay(request.ClipId);
            if (!accepted)
            {
                ClearMappingIfOwnedBy(request.PlaybackToken);
            }

            return accepted;
        }
        finally
        {
            _pendingStart = null;
        }
    }

    public void StopPlayback(BehaviorPlaybackToken playbackToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_logicalPlayback != playbackToken)
        {
            return;
        }

        ClearMapping();
        _player.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.FirstFramePresented -= OnFirstFramePresented;
        _player.PlaybackCompleted -= OnPlaybackCompleted;
        _player.LoopBoundaryReached -= OnLoopBoundaryReached;
        ClearMapping();
    }

    private void OnFirstFramePresented(
        object? sender,
        AnimationPlaybackEvent animationEvent)
    {
        if (_pendingStart is not { } pending ||
            !string.Equals(
                pending.ClipId,
                animationEvent.ClipId,
                StringComparison.OrdinalIgnoreCase))
        {
            if (_physicalPlayback != animationEvent.PlaybackToken)
            {
                ClearMapping();
            }

            return;
        }

        _logicalPlayback = pending.PlaybackToken;
        _physicalPlayback = animationEvent.PlaybackToken;
        FirstFramePresented?.Invoke(pending.PlaybackToken);
    }

    private void OnPlaybackCompleted(
        object? sender,
        AnimationPlaybackEvent animationEvent)
    {
        if (!TryTakeLogical(animationEvent.PlaybackToken, out var logical))
        {
            return;
        }

        PlaybackCompleted?.Invoke(logical);
    }

    private void OnLoopBoundaryReached(
        object? sender,
        AnimationPlaybackEvent animationEvent)
    {
        if (!TryTakeLogical(animationEvent.PlaybackToken, out var logical))
        {
            return;
        }

        LoopBoundaryReached?.Invoke(logical);
    }

    private bool TryTakeLogical(
        long physicalPlayback,
        out BehaviorPlaybackToken logical)
    {
        if (_logicalPlayback is { } current &&
            _physicalPlayback == physicalPlayback)
        {
            logical = current;
            ClearMapping();
            return true;
        }

        logical = null!;
        return false;
    }

    private void ClearMapping()
    {
        _logicalPlayback = null;
        _physicalPlayback = 0;
    }

    private void ClearMappingIfOwnedBy(BehaviorPlaybackToken playbackToken)
    {
        if (_logicalPlayback == playbackToken)
        {
            ClearMapping();
        }
    }
}

internal sealed class BubbleWindowBehaviorPort : IBehaviorBubbleSessionPort
{
    private readonly BubbleWindow _window;
    private readonly PetWindow _anchor;
    private readonly DialogueSelector _selector;
    private readonly IReadOnlyDictionary<string, DialogueLine> _linesById;
    private readonly BubbleArbiter _arbiter;
    private readonly Func<BehaviorCoordinatorSnapshot?> _snapshot;
    private readonly Func<string> _currentAction;
    private Func<BehaviorSessionToken, BubbleRequestKind> _kind =
        static _ => BubbleRequestKind.Automatic;
    private BehaviorSessionToken? _owner;
    private bool _suppressed;

    public BubbleWindowBehaviorPort(
        BubbleWindow window,
        PetWindow anchor,
        DialogueCatalog catalog,
        DialogueSelector selector,
        Func<BehaviorCoordinatorSnapshot?> snapshot,
        Func<string> currentAction,
        BubbleArbiter? arbiter = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
        ArgumentNullException.ThrowIfNull(catalog);
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _snapshot =
            snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _currentAction =
            currentAction ?? throw new ArgumentNullException(nameof(currentAction));
        _arbiter = arbiter ?? new BubbleArbiter();
        _linesById = catalog.Lines.ToDictionary(
            static line => line.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    public void SetKindResolver(
        Func<BehaviorSessionToken, BubbleRequestKind> resolver) =>
        _kind = resolver ?? throw new ArgumentNullException(nameof(resolver));

    public void Show(BehaviorBubblePresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (_suppressed)
        {
            return;
        }

        foreach (DialogueLine line in ResolveCandidates(presentation.Cue))
        {
            string message = VisibleMessage(line);
            BubbleRequestKind kind = _kind(presentation.SessionToken);
            TimeSpan visibleFor =
                presentation.Cue.VisibleFor ??
                BubbleReadingTime.Calculate(message);
            BehaviorContextSnapshot context =
                _snapshot()?.Context ?? BehaviorContextSnapshot.Empty;
            var request = new BubbleDisplayRequest
            {
                Owner = presentation.SessionToken,
                Kind = kind,
                DialogueId = line.Id,
                NormalizedTextKey = line.SemanticKey,
                Priority = presentation.Cue.Priority,
                VisibleFor = visibleFor,
                Context = context,
                RequiredInteractionFeedback =
                    kind == BubbleRequestKind.Interaction &&
                    presentation.Cue.At == TimeSpan.Zero,
            };
            BubbleDecision decision = _arbiter.TryCommit(request);
            if (!decision.Allowed)
            {
                continue;
            }

            _owner = presentation.SessionToken;
            _window.ShowMessage(
                message,
                _anchor,
                visibleFor);
            return;
        }
    }

    internal static string VisibleMessage(DialogueLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Text;
    }

    public void Hide(BehaviorSessionToken ownerSessionToken)
    {
        if (_owner != ownerSessionToken)
        {
            return;
        }

        _owner = null;
        _arbiter.Release(ownerSessionToken);
        _window.HideImmediately();
    }

    public void SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        if (suppressed)
        {
            HideImmediately();
        }
    }

    public void HideImmediately()
    {
        if (_owner is { } owner)
        {
            _arbiter.Release(owner);
        }

        _owner = null;
        _window.HideImmediately();
    }

    private IReadOnlyList<DialogueLine> ResolveCandidates(
        BehaviorBubbleCue cue)
    {
        if (!string.IsNullOrWhiteSpace(cue.DialogueId))
        {
            return _linesById.TryGetValue(cue.DialogueId, out var line)
                ? [line]
                : [];
        }

        if (string.IsNullOrWhiteSpace(cue.DialoguePoolId))
        {
            return [];
        }

        BehaviorCoordinatorSnapshot? snapshot = _snapshot();
        BehaviorContextSnapshot context =
            snapshot?.Context ?? BehaviorContextSnapshot.Empty;
        return _selector.SelectCandidates(
            new DialogueSelectionContext(
                DialogueSemantics.ResolveScene(cue.DialoguePoolId, context),
                DialogueSemantics.ResolveCueAction(
                    cue.Action,
                    _currentAction()),
                DialogueSemantics.ResolveMood(
                    snapshot?.Emotions ?? new EmotionState()),
                snapshot?.RelationshipStage ?? RelationshipStages.Familiar));
    }

}
