namespace GuluPet.Diagnostics;

internal sealed record AnimationRuntimeDiagnostics(
    string Name,
    int FrameCount,
    double Fps,
    bool Loop,
    string? AssetStatus,
    bool Placeholder,
    string? SourceBatch,
    string? SourceStage,
    IReadOnlyList<int> SourceFrameIndices,
    IReadOnlyList<string> KnownIssues);

internal sealed record PetRuntimeDiagnostics(
    string? CurrentAction,
    int FrameIndex,
    int FrameCount,
    double Fps,
    bool Loop,
    string? AssetStatus,
    string BehaviorState,
    IReadOnlyDictionary<string, double> Emotions);
