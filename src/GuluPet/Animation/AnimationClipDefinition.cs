namespace GuluPet.Animation;

public sealed class AnimationClipDefinition
{
    public required string Name { get; init; }

    public required int FrameCount { get; init; }

    public required double Fps { get; init; }

    public required bool Loop { get; init; }

    public required bool Placeholder { get; init; }

    public required string AssetStatus { get; init; }

    public string? SourceBatch { get; init; }

    public string? SourceStage { get; init; }

    public IReadOnlyList<int> SourceFrameIndices { get; init; } = [];

    public IReadOnlyList<string> KnownIssues { get; init; } = [];

    public string? PreviewStage { get; init; }

    public AnimationSourceApprovalProvenance? SourceApproval { get; init; }

    public IReadOnlyList<AnimationFrameEvent> Events { get; init; } = [];
}

public sealed class AnimationSourceApprovalProvenance
{
    public required string Schema { get; init; }

    public required string ApprovalId { get; init; }

    public required string ApprovalDecision { get; init; }

    public required string ApprovalRecord { get; init; }

    public required string ApprovalRecordSha256 { get; init; }

    public required string CandidateManifest { get; init; }

    public required string CandidateManifestSha256 { get; init; }

    public required string SourceReview { get; init; }

    public required string SourceReviewSha256 { get; init; }

    public required string FrameHashes { get; init; }

    public required string FrameHashesSha256 { get; init; }

    public required string PixelSequenceSha256 { get; init; }

    public required string CandidateClipJson { get; init; }

    public required string CandidateClipJsonSha256 { get; init; }
}

public sealed class AnimationFrameEvent
{
    public int Frame { get; init; }

    public required string Type { get; init; }

    public string? Value { get; init; }
}
