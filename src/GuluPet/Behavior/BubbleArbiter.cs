namespace GuluPet.Behavior;

public enum BubbleRequestKind
{
    Automatic,
    Interaction,
    ManualPreview
}

public sealed record BubbleDisplayRequest
{
    public required BehaviorSessionToken Owner { get; init; }

    public required BubbleRequestKind Kind { get; init; }

    public required string DialogueId { get; init; }

    public required string NormalizedTextKey { get; init; }

    public required int Priority { get; init; }

    public required TimeSpan VisibleFor { get; init; }

    public required BehaviorContextSnapshot Context { get; init; }

    public bool RequiredInteractionFeedback { get; init; }
}

public sealed record BubbleDecision(
    bool Allowed,
    string? RejectionReason,
    TimeSpan EvaluatedAt);

public sealed class BubbleArbiter
{
    private static readonly TimeSpan AutomaticWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan InteractionWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DialogueCooldown = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan TextCooldown = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PostInteractionQuiet = TimeSpan.FromSeconds(20);

    private readonly IMonotonicClock _clock;
    private readonly Queue<TimeSpan> _automaticDisplays = [];
    private readonly Queue<TimeSpan> _interactionDisplays = [];
    private readonly Dictionary<string, TimeSpan> _dialogueDisplays =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TimeSpan> _textDisplays =
        new(StringComparer.Ordinal);
    private TimeSpan? _lastAutomaticDisplay;
    private TimeSpan? _lastInteractionDisplay;
    private BehaviorSessionToken? _authorizedTimelineOwner;
    private BehaviorSessionToken? _activeOwner;
    private TimeSpan _activeUntil;
    private int _activePriority;

    public BubbleArbiter(IMonotonicClock? clock = null)
    {
        _clock = clock ?? SystemMonotonicClock.Instance;
    }

    public BubbleDecision Evaluate(BubbleDisplayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DialogueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NormalizedTextKey);
        ArgumentNullException.ThrowIfNull(request.Context);
        if (request.VisibleFor <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Bubble visibility must be positive.");
        }

        var now = _clock.Elapsed;
        Prune(now);

        if (_activeOwner is not null &&
            now < _activeUntil &&
            request.Priority <= _activePriority)
        {
            return Reject("bubble-active", now);
        }

        if (request.Kind == BubbleRequestKind.ManualPreview)
        {
            return Allow(now);
        }

        if (request.Kind == BubbleRequestKind.Interaction &&
            request.RequiredInteractionFeedback)
        {
            return Allow(now);
        }

        if (_textDisplays.TryGetValue(request.NormalizedTextKey, out var textAt) &&
            now - textAt < TextCooldown)
        {
            return Reject("text-cooldown", now);
        }

        if (_authorizedTimelineOwner == request.Owner)
        {
            return Allow(now);
        }

        if (_dialogueDisplays.TryGetValue(request.DialogueId, out var dialogueAt) &&
            now - dialogueAt < DialogueCooldown)
        {
            return Reject("dialogue-cooldown", now);
        }

        if (request.Kind == BubbleRequestKind.Interaction)
        {
            return _interactionDisplays.Count >= 4
                ? Reject("interaction-budget", now)
                : Allow(now);
        }

        if (_lastInteractionDisplay is { } interactionAt &&
            now - interactionAt < PostInteractionQuiet)
        {
            return Reject("post-interaction-quiet", now);
        }

        var policy = GetAutomaticPolicy(request.Context);
        if (_lastAutomaticDisplay is { } automaticAt &&
            now - automaticAt < policy.MinimumInterval)
        {
            return Reject("automatic-minimum-interval", now);
        }

        return _automaticDisplays.Count >= policy.MaximumPerWindow
            ? Reject("automatic-budget", now)
            : Allow(now);
    }

    public BubbleDecision TryCommit(BubbleDisplayRequest request)
    {
        var decision = Evaluate(request);
        if (!decision.Allowed)
        {
            return decision;
        }

        var now = decision.EvaluatedAt;
        if (request.Kind == BubbleRequestKind.ManualPreview)
        {
            _activeOwner = request.Owner;
            _activeUntil = now + request.VisibleFor;
            _activePriority = request.Priority;
            return decision;
        }

        bool isTimelineContinuation =
            _authorizedTimelineOwner == request.Owner;
        if (request.Kind == BubbleRequestKind.Interaction)
        {
            if (!isTimelineContinuation)
            {
                _interactionDisplays.Enqueue(now);
            }

            _lastInteractionDisplay = now;
        }
        else
        {
            if (!isTimelineContinuation)
            {
                _automaticDisplays.Enqueue(now);
            }

            _lastAutomaticDisplay = now;
        }

        _dialogueDisplays[request.DialogueId] = now;
        _textDisplays[request.NormalizedTextKey] = now;
        _authorizedTimelineOwner = request.Owner;
        _activeOwner = request.Owner;
        _activeUntil = now + request.VisibleFor;
        _activePriority = request.Priority;
        return decision;
    }

    public void Release(BehaviorSessionToken owner)
    {
        if (_activeOwner != owner)
        {
            return;
        }

        _activeOwner = null;
        _activeUntil = TimeSpan.Zero;
        _activePriority = 0;
    }

    public BubbleBudgetSnapshot Snapshot(BehaviorContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = _clock.Elapsed;
        Prune(now);
        var policy = GetAutomaticPolicy(context);
        return new BubbleBudgetSnapshot
        {
            CapturedAt = now,
            AutomaticCount = _automaticDisplays.Count,
            AutomaticLimit = policy.MaximumPerWindow,
            InteractionCount = _interactionDisplays.Count,
            InteractionLimit = 4,
            LastAutomaticDisplay = _lastAutomaticDisplay,
            LastInteractionDisplay = _lastInteractionDisplay,
        };
    }

    private void Prune(TimeSpan now)
    {
        if (_activeOwner is not null && now >= _activeUntil)
        {
            _activeOwner = null;
            _activeUntil = TimeSpan.Zero;
            _activePriority = 0;
        }

        PruneQueue(_automaticDisplays, now - AutomaticWindow);
        PruneQueue(_interactionDisplays, now - InteractionWindow);
        PruneDictionary(_dialogueDisplays, now - DialogueCooldown);
        PruneDictionary(_textDisplays, now - TextCooldown);
    }

    private static void PruneQueue(Queue<TimeSpan> queue, TimeSpan threshold)
    {
        while (queue.Count > 0 && queue.Peek() <= threshold)
        {
            queue.Dequeue();
        }
    }

    private static void PruneDictionary(
        IDictionary<string, TimeSpan> dictionary,
        TimeSpan threshold)
    {
        foreach (var key in dictionary
                     .Where(entry => entry.Value <= threshold)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            dictionary.Remove(key);
        }
    }

    private static AutomaticBubblePolicy GetAutomaticPolicy(
        BehaviorContextSnapshot context)
    {
        if (context.ApplicationCategory.Equals(
                "game",
                StringComparison.OrdinalIgnoreCase))
        {
            return new(TimeSpan.FromSeconds(180), 1);
        }

        if (context.IsBusy &&
            (context.ApplicationCategory.Equals(
                 "office",
                 StringComparison.OrdinalIgnoreCase) ||
             context.ApplicationCategory.Equals(
                 "ide",
                 StringComparison.OrdinalIgnoreCase)))
        {
            return new(TimeSpan.FromSeconds(120), 2);
        }

        return new(TimeSpan.FromSeconds(45), 6);
    }

    private static BubbleDecision Allow(TimeSpan now) =>
        new(true, null, now);

    private static BubbleDecision Reject(string reason, TimeSpan now) =>
        new(false, reason, now);

    private sealed record AutomaticBubblePolicy(
        TimeSpan MinimumInterval,
        int MaximumPerWindow);
}

public sealed record BubbleBudgetSnapshot
{
    public required TimeSpan CapturedAt { get; init; }

    public required int AutomaticCount { get; init; }

    public required int AutomaticLimit { get; init; }

    public required int InteractionCount { get; init; }

    public required int InteractionLimit { get; init; }

    public TimeSpan? LastAutomaticDisplay { get; init; }

    public TimeSpan? LastInteractionDisplay { get; init; }
}
