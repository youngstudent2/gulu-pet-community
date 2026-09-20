using System.IO;
using System.Text.Json.Serialization;

namespace GuluPet.Accessories;

/// <summary>
/// Strict pose catalog for every runtime animation clip. The index and each
/// referenced pose document are hash-bound; frames are zero-based, complete,
/// and expressed in the shared 320-pixel canonical canvas.
/// </summary>
public sealed class EyeAccessoryPoseCatalog
{
    public const string CurrentIndexSchema =
        "gulu-eye-accessory-pose-index-v1";
    public const string LegacyPoseSchema = "gulu-eye-accessory-pose-v1";
    public const string CurrentPoseSchema = "gulu-eye-accessory-pose-v2";
    public const string IndexFileName = "pose-index.json";
    public const int RequiredClipCount = 141;
    public const int RequiredCanvasSize = 320;

    private readonly IReadOnlyDictionary<string, EyeAccessoryClipPose> _byClipId;

    private EyeAccessoryPoseCatalog(
        string rootPath,
        string indexPath,
        IReadOnlyList<EyeAccessoryClipPose> poses)
    {
        RootPath = rootPath;
        IndexPath = indexPath;
        Poses = poses;
        _byClipId = poses.ToDictionary(
            static pose => pose.ClipId,
            StringComparer.Ordinal);
    }

    public string RootPath { get; }

    public string IndexPath { get; }

    public IReadOnlyList<EyeAccessoryClipPose> Poses { get; }

    public IEnumerable<string> ClipIds => Poses.Select(static pose => pose.ClipId);

    public int Count => Poses.Count;

    public EyeAccessoryClipPose this[string clipId] =>
        _byClipId.TryGetValue(clipId, out EyeAccessoryClipPose? pose)
            ? pose
            : throw new KeyNotFoundException(
                $"Unknown eye accessory pose clip '{clipId}'.");

    public bool TryGet(
        string? clipId,
        out EyeAccessoryClipPose? pose)
    {
        pose = null;
        return !string.IsNullOrWhiteSpace(clipId)
            && _byClipId.TryGetValue(clipId, out pose);
    }

    public static EyeAccessoryPoseCatalog LoadFromAssets(
        string assetsRoot,
        IEnumerable<string>? expectedClipIds = null) =>
        Load(
            EyeAccessoryContentReader.ResolveFromAssetsRoot(assetsRoot),
            expectedClipIds);

    public static Task<EyeAccessoryPoseCatalog> LoadFromAssetsAsync(
        string assetsRoot,
        IEnumerable<string>? expectedClipIds = null,
        CancellationToken cancellationToken = default) =>
        LoadAsync(
            EyeAccessoryContentReader.ResolveFromAssetsRoot(assetsRoot),
            expectedClipIds,
            cancellationToken);

    public static EyeAccessoryPoseCatalog Load(
        string accessoriesRoot,
        IEnumerable<string>? expectedClipIds = null)
    {
        string root = EyeAccessoryContentReader.ResolveAccessoriesRoot(
            accessoriesRoot);
        string indexPath = Path.Combine(root, IndexFileName);
        PoseIndexDocument document =
            EyeAccessoryContentReader.ReadJson<PoseIndexDocument>(
                indexPath,
                "eye accessory pose index");
        return CreateValidated(
            root,
            indexPath,
            document,
            expectedClipIds,
            static (path, hash, description, _) => Task.FromResult(
                EyeAccessoryContentReader.ReadHashedJson<PoseDocument>(
                    path,
                    hash,
                    description)),
            CancellationToken.None).GetAwaiter().GetResult();
    }

    public static async Task<EyeAccessoryPoseCatalog> LoadAsync(
        string accessoriesRoot,
        IEnumerable<string>? expectedClipIds = null,
        CancellationToken cancellationToken = default)
    {
        string root = EyeAccessoryContentReader.ResolveAccessoriesRoot(
            accessoriesRoot);
        string indexPath = Path.Combine(root, IndexFileName);
        PoseIndexDocument document =
            await EyeAccessoryContentReader.ReadJsonAsync<PoseIndexDocument>(
                    indexPath,
                    "eye accessory pose index",
                    cancellationToken)
                .ConfigureAwait(false);
        return await CreateValidated(
                root,
                indexPath,
                document,
                expectedClipIds,
                static (path, hash, description, cancellationToken) =>
                    EyeAccessoryContentReader
                        .ReadHashedJsonAsync<PoseDocument>(
                            path,
                            hash,
                            description,
                            cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<EyeAccessoryPoseCatalog> CreateValidated(
        string root,
        string indexPath,
        PoseIndexDocument document,
        IEnumerable<string>? expectedClipIds,
        Func<string, string, string, CancellationToken, Task<PoseDocument>>
            readPose,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                document.Schema,
                CurrentIndexSchema,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Eye accessory pose index schema must be " +
                $"'{CurrentIndexSchema}', but is '{document.Schema}'.");
        }

        if (document.Clips is null
            || document.Clips.Count != RequiredClipCount)
        {
            throw new InvalidDataException(
                $"Eye accessory pose index must contain exactly " +
                $"{RequiredClipCount} clip entries.");
        }

        HashSet<string>? expected = ValidateExpectedClipIds(expectedClipIds);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var posePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new ValidatedIndexEntry[document.Clips.Count];
        for (var index = 0; index < document.Clips.Count; index++)
        {
            PoseIndexEntryDocument item = document.Clips[index]
                ?? throw new InvalidDataException(
                    $"Eye accessory pose index item at index {index} is null.");
            string owner = $"Eye accessory pose index item at index {index}";
            EyeAccessoryContentReader.ValidateId(item.ClipId, $"{owner} clipId");
            string clipId = item.ClipId!;
            if (!ids.Add(clipId))
            {
                throw new InvalidDataException(
                    $"Eye accessory pose clipId '{clipId}' is duplicated.");
            }

            if (item.FrameCount < 1 || item.FrameCount > 10_000)
            {
                throw new InvalidDataException(
                    $"Eye accessory pose index clip '{clipId}' frameCount " +
                    "must be from 1 through 10000.");
            }

            string posePath = EyeAccessoryContentReader.ResolveRelativeFile(
                root,
                item.PoseFile,
                ".json",
                $"Eye accessory pose index clip '{clipId}' poseFile");
            if (!posePaths.Add(posePath))
            {
                throw new InvalidDataException(
                    $"Eye accessory pose file '{item.PoseFile}' is referenced " +
                    "more than once.");
            }

            string sha256 = EyeAccessoryContentReader.ValidateSha256(
                item.PoseSha256,
                $"Eye accessory pose index clip '{clipId}' poseSha256");
            entries[index] = new ValidatedIndexEntry(
                clipId,
                item.FrameCount,
                item.PoseFile!,
                posePath,
                sha256);
        }

        if (expected is not null)
        {
            string[] missing = expected.Except(ids, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] unexpected = ids.Except(expected, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0 || unexpected.Length > 0)
            {
                throw new InvalidDataException(
                    "Eye accessory pose index clip ids do not match the " +
                    "expected runtime inventory. " +
                    $"Missing: {FormatIds(missing)}. " +
                    $"Unexpected: {FormatIds(unexpected)}.");
            }
        }

        var poses = new EyeAccessoryClipPose[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatedIndexEntry entry = entries[index];
            PoseDocument poseDocument = await readPose(
                    entry.PosePath,
                    entry.PoseSha256,
                    $"eye accessory pose for '{entry.ClipId}'",
                    cancellationToken)
                .ConfigureAwait(false);
            poses[index] = ValidatePoseDocument(entry, poseDocument);
        }

        return new EyeAccessoryPoseCatalog(root, indexPath, poses);
    }

    private static HashSet<string>? ValidateExpectedClipIds(
        IEnumerable<string>? expectedClipIds)
    {
        if (expectedClipIds is null)
        {
            return null;
        }

        var expected = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (string? clipId in expectedClipIds)
        {
            EyeAccessoryContentReader.ValidateId(
                clipId,
                $"Expected clip id at index {index}");
            if (!expected.Add(clipId!))
            {
                throw new ArgumentException(
                    $"Expected clip id '{clipId}' is duplicated.",
                    nameof(expectedClipIds));
            }

            index++;
        }

        if (expected.Count != RequiredClipCount)
        {
            throw new ArgumentException(
                $"Expected clip inventory must contain exactly " +
                $"{RequiredClipCount} ids, but contains {expected.Count}.",
                nameof(expectedClipIds));
        }

        return expected;
    }

    private static EyeAccessoryClipPose ValidatePoseDocument(
        ValidatedIndexEntry entry,
        PoseDocument document)
    {
        bool isLegacySchema = string.Equals(
            document.Schema,
            LegacyPoseSchema,
            StringComparison.Ordinal);
        bool isCurrentSchema = string.Equals(
            document.Schema,
            CurrentPoseSchema,
            StringComparison.Ordinal);
        if (!isLegacySchema && !isCurrentSchema)
        {
            throw new InvalidDataException(
                $"Eye accessory pose '{entry.ClipId}' schema must be " +
                $"'{LegacyPoseSchema}' or '{CurrentPoseSchema}', but is " +
                $"'{document.Schema}'.");
        }

        if (!string.Equals(
                document.ClipId,
                entry.ClipId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Eye accessory pose file '{entry.PoseFile}' declares clipId " +
                $"'{document.ClipId}', expected '{entry.ClipId}'.");
        }

        if (document.FrameCount != entry.FrameCount)
        {
            throw new InvalidDataException(
                $"Eye accessory pose '{entry.ClipId}' declares frameCount " +
                $"{document.FrameCount}, but its index declares " +
                $"{entry.FrameCount}.");
        }

        if (document.CanvasSize != RequiredCanvasSize)
        {
            throw new InvalidDataException(
                $"Eye accessory pose '{entry.ClipId}' canvasSize must be " +
                $"{RequiredCanvasSize}, but is {document.CanvasSize}.");
        }

        if (document.Frames is null
            || document.Frames.Count != entry.FrameCount)
        {
            throw new InvalidDataException(
                $"Eye accessory pose '{entry.ClipId}' must contain exactly " +
                $"{entry.FrameCount} frames.");
        }

        var frames = new EyeAccessoryPoseFrame[entry.FrameCount];
        for (var index = 0; index < document.Frames.Count; index++)
        {
            PoseFrameDocument frame = document.Frames[index]
                ?? throw new InvalidDataException(
                    $"Eye accessory pose '{entry.ClipId}' frame at index " +
                    $"{index} is null.");
            if (frame.Frame != index)
            {
                throw new InvalidDataException(
                    $"Eye accessory pose '{entry.ClipId}' frames must be " +
                    $"ordered and contiguous from zero; item {index} declares " +
                    $"frame {frame.Frame}.");
            }

            frames[index] = ValidateFrame(
                entry.ClipId,
                frame,
                isLegacySchema);
        }

        return new EyeAccessoryClipPose(
            entry.ClipId,
            entry.FrameCount,
            RequiredCanvasSize,
            entry.PoseFile,
            entry.PosePath,
            entry.PoseSha256,
            frames);
    }

    private static EyeAccessoryPoseFrame ValidateFrame(
        string clipId,
        PoseFrameDocument frame,
        bool isLegacySchema)
    {
        string owner = $"Eye accessory pose '{clipId}' frame {frame.Frame}";
        if (!double.IsFinite(frame.HeadWidth)
            || frame.HeadWidth < 0
            || frame.HeadWidth > RequiredCanvasSize)
        {
            throw new InvalidDataException(
                $"{owner} headWidth must be finite and from 0 through " +
                $"{RequiredCanvasSize}.");
        }

        if (!double.IsFinite(frame.RotationDegrees)
            || Math.Abs(frame.RotationDegrees) > 180)
        {
            throw new InvalidDataException(
                $"{owner} rotationDegrees must be finite and from -180 " +
                "through 180.");
        }

        EyeAccessoryPoint? leftEye = ValidateOptionalPoint(
            frame.LeftEye,
            $"{owner} leftEye");
        EyeAccessoryPoint? rightEye = ValidateOptionalPoint(
            frame.RightEye,
            $"{owner} rightEye");
        if (leftEye.HasValue != rightEye.HasValue)
        {
            throw new InvalidDataException(
                $"{owner} must declare both eye points or set both to null.");
        }

        EyeAccessoryViewState? viewState = ValidateViewState(
            owner,
            frame,
            isLegacySchema);

        string? hiddenReason;
        if (frame.Visible)
        {
            if (leftEye is null || rightEye is null)
            {
                throw new InvalidDataException(
                    $"{owner} is visible and requires both eye points.");
            }

            if (leftEye == rightEye)
            {
                throw new InvalidDataException(
                    $"{owner} visible eye points must be distinct.");
            }

            if (frame.HeadWidth <= 0)
            {
                throw new InvalidDataException(
                    $"{owner} is visible and requires a positive headWidth.");
            }

            if (frame.HiddenReason is not null)
            {
                throw new InvalidDataException(
                    $"{owner} is visible and hiddenReason must be null.");
            }

            if (viewState is null)
            {
                throw new InvalidDataException(
                    $"{owner} is visible and requires a viewState.");
            }

            hiddenReason = null;
        }
        else
        {
            if (leftEye is not null || rightEye is not null)
            {
                throw new InvalidDataException(
                    $"{owner} is hidden and both eye points must be null.");
            }

            if (frame.HeadWidth != 0)
            {
                throw new InvalidDataException(
                    $"{owner} is hidden and headWidth must be zero.");
            }

            if (frame.RotationDegrees != 0)
            {
                throw new InvalidDataException(
                    $"{owner} is hidden and rotationDegrees must be zero.");
            }

            if (viewState is not null)
            {
                throw new InvalidDataException(
                    $"{owner} is hidden and viewState must be null.");
            }

            hiddenReason = EyeAccessoryContentReader.ValidateText(
                frame.HiddenReason,
                $"{owner} hiddenReason");
        }

        return new EyeAccessoryPoseFrame(
            frame.Frame,
            frame.Visible,
            viewState,
            leftEye,
            rightEye,
            frame.HeadWidth,
            frame.RotationDegrees,
            hiddenReason);
    }

    private static EyeAccessoryViewState? ValidateViewState(
        string owner,
        PoseFrameDocument frame,
        bool isLegacySchema)
    {
        if (isLegacySchema)
        {
            if (frame.HasViewState)
            {
                throw new InvalidDataException(
                    $"{owner} uses the legacy pose schema and cannot declare " +
                    "viewState.");
            }

            return frame.Visible ? EyeAccessoryViewState.Front : null;
        }

        if (!frame.HasViewState)
        {
            throw new InvalidDataException(
                $"{owner} uses the current pose schema and must declare " +
                "viewState.");
        }

        if (frame.ViewState is null)
        {
            return null;
        }

        return frame.ViewState switch
        {
            "front" => EyeAccessoryViewState.Front,
            "threeQuarterLeft" => EyeAccessoryViewState.ThreeQuarterLeft,
            "threeQuarterRight" => EyeAccessoryViewState.ThreeQuarterRight,
            "sideLeft" => EyeAccessoryViewState.SideLeft,
            "sideRight" => EyeAccessoryViewState.SideRight,
            _ => throw new InvalidDataException(
                $"{owner} viewState '{frame.ViewState}' is unknown."),
        };
    }

    private static EyeAccessoryPoint? ValidateOptionalPoint(
        PointDocument? point,
        string owner) =>
        point is null
            ? null
            : EyeAccessoryContentReader.ValidatePoint(
                point.X,
                point.Y,
                RequiredCanvasSize,
                RequiredCanvasSize,
                owner);

    private static string FormatIds(IReadOnlyList<string> ids) =>
        ids.Count == 0 ? "none" : string.Join(", ", ids);

    private sealed class PoseIndexDocument
    {
        [JsonRequired]
        public string? Schema { get; init; }

        [JsonRequired]
        public IReadOnlyList<PoseIndexEntryDocument?>? Clips { get; init; }
    }

    private sealed class PoseIndexEntryDocument
    {
        [JsonRequired]
        public string? ClipId { get; init; }

        [JsonRequired]
        public int FrameCount { get; init; }

        [JsonRequired]
        public string? PoseFile { get; init; }

        [JsonRequired]
        public string? PoseSha256 { get; init; }
    }

    private sealed class PoseDocument
    {
        [JsonRequired]
        public string? Schema { get; init; }

        [JsonRequired]
        public string? ClipId { get; init; }

        [JsonRequired]
        public int FrameCount { get; init; }

        [JsonRequired]
        public int CanvasSize { get; init; }

        [JsonRequired]
        public IReadOnlyList<PoseFrameDocument?>? Frames { get; init; }
    }

    private sealed class PoseFrameDocument
    {
        private string? _viewState;

        [JsonRequired]
        public int Frame { get; init; }

        [JsonRequired]
        public bool Visible { get; init; }

        public string? ViewState
        {
            get => _viewState;
            init
            {
                _viewState = value;
                HasViewState = true;
            }
        }

        [JsonIgnore]
        public bool HasViewState { get; private set; }

        [JsonRequired]
        public PointDocument? LeftEye { get; init; }

        [JsonRequired]
        public PointDocument? RightEye { get; init; }

        [JsonRequired]
        public double HeadWidth { get; init; }

        [JsonRequired]
        public double RotationDegrees { get; init; }

        [JsonRequired]
        public string? HiddenReason { get; init; }
    }

    private sealed class PointDocument
    {
        [JsonRequired]
        public double X { get; init; }

        [JsonRequired]
        public double Y { get; init; }
    }

    private sealed record ValidatedIndexEntry(
        string ClipId,
        int FrameCount,
        string PoseFile,
        string PosePath,
        string PoseSha256);
}
