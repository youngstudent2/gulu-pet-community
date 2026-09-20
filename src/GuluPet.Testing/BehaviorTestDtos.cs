namespace GuluPet.Testing;

/// <summary>
/// Wire-only behavior diagnostics. These DTOs deliberately use primitive
/// values and strings so GuluPet.Testing stays independent of the WPF app and
/// its behavior runtime assembly.
/// </summary>
public sealed class TestBehaviorStatus
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool RuntimePaused { get; init; }

    public long ContextRevision { get; init; }

    public DateTimeOffset LocalNow { get; init; }

    public string ApplicationCategory { get; init; } = "unknown";

    public bool ApplicationCategoryAvailable { get; init; }

    public double ApplicationStableMilliseconds { get; init; }

    public double KeyboardPerMinute { get; init; }

    public double MouseClicksPerMinute { get; init; }

    public long KeyboardCountSinceStart { get; init; }

    public long MouseClickCountSinceStart { get; init; }

    public double IdleMilliseconds { get; init; }

    public bool IsBusy { get; init; }

    public bool IsFullscreen { get; init; }

    public bool IsLocked { get; init; }

    public string WeatherKind { get; init; } = "unavailable";

    public bool WeatherAvailable { get; init; }

    public double TemperatureCelsius { get; init; }

    public string StableState { get; init; } = "normal";

    public string LifecyclePhase { get; init; } = "idle";

    public string? CurrentBehaviorId { get; init; }

    public long? SessionToken { get; init; }

    public long? PlaybackToken { get; init; }

    public string? BehaviorPhase { get; init; }

    public string? ClipId { get; init; }

    public double LogicalTimeMilliseconds { get; init; }

    public bool Committed { get; init; }

    public int NextCueIndex { get; init; }

    public string? TerminalStatus { get; init; }

    public string? TerminalReason { get; init; }

    public string? ActiveInteractionKey { get; init; }

    public string? ActiveInteractionTriggerTag { get; init; }

    public long? InteractionRequestId { get; init; }

    public long? InteractionSessionToken { get; init; }
}

public sealed class TestBehaviorEvaluationReport
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public long ContextRevision { get; init; }

    public string Source { get; init; } = "activeTick";

    public string? SelectedBehaviorId { get; init; }

    public IReadOnlyList<string> AttemptOrder { get; init; } = [];

    public IReadOnlyList<string> TopBehaviorIds { get; init; } = [];

    public bool RandomConsumed { get; init; }

    public IReadOnlyList<TestBehaviorEvaluationItem> Evaluations { get; init; } =
        [];
}

public sealed class TestBehaviorEvaluationItem
{
    public required string BehaviorId { get; init; }

    public bool Eligible { get; init; }

    public string? RejectionReason { get; init; }

    public double Score { get; init; }

    public int Rank { get; init; }

    public double Probability { get; init; }

    public IReadOnlyDictionary<string, double> Components { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);
}

public sealed class TestBehaviorTickResult
{
    public DateTimeOffset EvaluatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public int? Seed { get; init; }

    public bool Evaluated { get; init; }

    public string? SelectedBehaviorId { get; init; }

    public bool Enqueued { get; init; }

    public string? QueueDisposition { get; init; }

    public string? SuppressionReason { get; init; }

    public int? NextIntervalSeconds { get; init; }
}

public sealed class TestBehaviorMutationResult
{
    public required string Command { get; init; }

    public required string BehaviorId { get; init; }

    public bool Accepted { get; init; }

    public string? Disposition { get; init; }

    public long? RequestId { get; init; }

    public long? GroupId { get; init; }

    public long? SessionToken { get; init; }

    public string? Diagnostic { get; init; }

    public string? InteractionKey { get; init; }

}

public sealed class TestBehaviorQueueSnapshot
{
    public double CapturedAtMilliseconds { get; init; }

    public int PendingCount { get; init; }

    public TestBehaviorRequestInfo? ActiveRequest { get; init; }

    public long? ActiveGroupId { get; init; }

    public double? ResumeAfterMilliseconds { get; init; }

    public IReadOnlyList<TestBehaviorQueueGroup> Groups { get; init; } = [];

    public IReadOnlyList<TestBehaviorQueueEviction> Evictions { get; init; } = [];

    public IReadOnlyList<TestBehaviorQueueDiagnostic> RecentDiagnostics {
        get;
        init;
    } = [];
}

public sealed class TestBehaviorQueueGroup
{
    public long GroupId { get; init; }

    public int AcceptedCount { get; init; }

    public bool HasStarted { get; init; }

    public bool Closed { get; init; }

    public bool Active { get; init; }

    public IReadOnlyList<TestBehaviorRequestInfo> PendingItems { get; init; } = [];
}

public sealed class TestBehaviorRequestInfo
{
    public long RequestId { get; init; }

    public required string BehaviorId { get; init; }

    public string Source { get; init; } = "activeTick";

    public string SwitchMode { get; init; } = "soft";

    public int Priority { get; init; }

    public double CreatedAtMilliseconds { get; init; }

    public double ExpiresAtMilliseconds { get; init; }

    public required string DedupeKey { get; init; }

    public long ContextRevision { get; init; }
}

public sealed class TestBehaviorQueueEviction
{
    public double OccurredAtMilliseconds { get; init; }

    public long GroupId { get; init; }

    public IReadOnlyList<long> RequestIds { get; init; } = [];

    public IReadOnlyList<string> BehaviorIds { get; init; } = [];
}

public sealed class TestBehaviorQueueDiagnostic
{
    public double OccurredAtMilliseconds { get; init; }

    public required string Operation { get; init; }

    public required string Outcome { get; init; }

    public long? RequestId { get; init; }

    public long? GroupId { get; init; }

    public string? Detail { get; init; }
}

public sealed class TestBehaviorEventPage
{
    public long RequestedAfterSequence { get; init; }

    public long? TruncatedBeforeSequence { get; init; }

    public IReadOnlyList<TestBehaviorEvent> Events { get; init; } = [];
}

public sealed class TestBehaviorEvent
{
    public long Sequence { get; init; }

    public double OccurredAtMilliseconds { get; init; }

    public required string Kind { get; init; }

    public string? BehaviorId { get; init; }

    public long? RequestId { get; init; }

    public long? GroupId { get; init; }

    public long? SessionToken { get; init; }

    public string? Phase { get; init; }

    public string? TerminalStatus { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyDictionary<string, string> Details { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
