namespace GuluPet.TestCli;

public sealed class ActionProbeReport
{
    public string Command { get; init; } = "probe-actions";

    public bool Ok { get; init; }

    public bool CommandRejected { get; init; }

    public string? ErrorCode { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public int HoldMilliseconds { get; init; }

    public int PollMilliseconds { get; init; }

    public string? RestoredAction { get; init; }

    public IReadOnlyList<ActionProbeResult> Results { get; init; } = [];

    public string? Error { get; init; }
}

public sealed class ActionProbeResult
{
    public required string Action { get; init; }

    public bool Ok { get; init; }

    public bool CommandRejected { get; init; }

    public string? ErrorCode { get; init; }

    public int ExpectedFrameCount { get; init; }

    public double ExpectedFps { get; init; }

    public bool ExpectedLoop { get; init; }

    public bool ObservedRequestedAction { get; init; }

    public bool FrameIndicesInRange { get; init; }

    public bool MetadataConsistent { get; init; }

    public IReadOnlyList<int> DistinctFrameIndices { get; init; } = [];

    public IReadOnlyList<ActionProbeSample> Samples { get; init; } = [];

    public string? Error { get; init; }
}

public sealed class ActionProbeSample
{
    public DateTimeOffset CapturedAtUtc { get; init; }

    public string? CurrentAction { get; init; }

    public int FrameIndex { get; init; }

    public int FrameCount { get; init; }

    public double Fps { get; init; }

    public bool Loop { get; init; }

    public string? AssetStatus { get; init; }
}
