namespace GuluPet.Accessories;

/// <summary>
/// A point in an accessory asset or canonical pose canvas. Coordinates use a
/// top-left origin, with X increasing rightward and Y increasing downward.
/// </summary>
public readonly record struct EyeAccessoryPoint(double X, double Y);

/// <summary>
/// The pre-baked view of an eye accessory for one animation frame. Left and
/// right refer to the direction the cat's muzzle points in the canonical
/// animation canvas.
/// </summary>
public enum EyeAccessoryViewState
{
    Front,
    ThreeQuarterLeft,
    ThreeQuarterRight,
    SideLeft,
    SideRight,
}

/// <summary>
/// One hash-bound image variant and its rigid registration geometry.
/// </summary>
public sealed record EyeAccessoryVariantDefinition(
    EyeAccessoryViewState ViewState,
    string ImageFile,
    string ImagePath,
    int Width,
    int Height,
    EyeAccessoryPoint LeftEye,
    EyeAccessoryPoint RightEye,
    double HeadWidthPixels,
    string Sha256,
    long SizeBytes);

/// <summary>
/// One selectable eye accessory after its manifest entry and image have passed
/// the runtime content checks.
/// </summary>
public sealed record EyeAccessoryDefinition(
    string Id,
    string DisplayName,
    int SelectionOrder,
    string ImageFile,
    string ImagePath,
    int Width,
    int Height,
    EyeAccessoryPoint LeftEye,
    EyeAccessoryPoint RightEye,
    double HeadWidthPixels,
    string Sha256,
    long SizeBytes,
    IReadOnlyDictionary<EyeAccessoryViewState, EyeAccessoryVariantDefinition>
        Variants)
{
    public EyeAccessoryVariantDefinition FrontVariant =>
        Variants[EyeAccessoryViewState.Front];

    public bool TryGetVariant(
        EyeAccessoryViewState viewState,
        out EyeAccessoryVariantDefinition? variant) =>
        Variants.TryGetValue(viewState, out variant);
}

/// <summary>
/// A single zero-based animation-frame pose in the canonical accessory canvas.
/// Hidden frames keep an explicit reason and never inherit the preceding pose.
/// </summary>
public sealed record EyeAccessoryPoseFrame(
    int Frame,
    bool Visible,
    EyeAccessoryViewState? ViewState,
    EyeAccessoryPoint? LeftEye,
    EyeAccessoryPoint? RightEye,
    double HeadWidth,
    double RotationDegrees,
    string? HiddenReason);

/// <summary>
/// The validated pose sequence for one runtime animation clip.
/// </summary>
public sealed class EyeAccessoryClipPose
{
    internal EyeAccessoryClipPose(
        string clipId,
        int frameCount,
        int canvasSize,
        string poseFile,
        string posePath,
        string poseSha256,
        IReadOnlyList<EyeAccessoryPoseFrame> frames)
    {
        ClipId = clipId;
        FrameCount = frameCount;
        CanvasSize = canvasSize;
        PoseFile = poseFile;
        PosePath = posePath;
        PoseSha256 = poseSha256;
        Frames = frames;
    }

    public string ClipId { get; }

    public int FrameCount { get; }

    public int CanvasSize { get; }

    public string PoseFile { get; }

    public string PosePath { get; }

    public string PoseSha256 { get; }

    public IReadOnlyList<EyeAccessoryPoseFrame> Frames { get; }

    public EyeAccessoryPoseFrame this[int frameIndex] => Frames[frameIndex];
}
