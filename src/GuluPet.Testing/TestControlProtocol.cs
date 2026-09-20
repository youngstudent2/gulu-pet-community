using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuluPet.Testing;

public static class TestControlProtocol
{
    public const int Version = 1;

    public const string PipeName = "GuluPet.TestControl.v1";

    public const int MaxRequestBytes = 16 * 1024;

    public const int MaxResponseBytes = 1024 * 1024;

    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static bool ValidateRequest(
        TestControlRequest? request,
        out string? errorCode,
        out string? error)
    {
        if (request is null)
        {
            errorCode = TestControlErrorCodes.InvalidRequest;
            error = "Request is required.";
            return false;
        }

        if (request.ProtocolVersion != Version)
        {
            errorCode = TestControlErrorCodes.UnsupportedVersion;
            error = $"Unsupported protocol version {request.ProtocolVersion}; expected {Version}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Command) ||
            !TestControlCommands.All.Contains(request.Command.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            errorCode = TestControlErrorCodes.InvalidCommand;
            error = $"Unknown command '{request.Command}'.";
            return false;
        }

        string command = request.Command.Trim();
        if (command.Equals(
                TestControlCommands.Play,
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Action))
            {
                errorCode = TestControlErrorCodes.MissingAction;
                error = "The play command requires an action.";
                return false;
            }

            if (HasBehaviorArguments(request) ||
                HasPostcardArguments(request))
            {
                return UnexpectedArguments(out errorCode, out error);
            }
        }
        else if (command.Equals(
                     TestControlCommands.Enqueue,
                     StringComparison.OrdinalIgnoreCase) ||
                 command.Equals(
                     TestControlCommands.StartIfIdle,
                     StringComparison.OrdinalIgnoreCase) ||
                 command.Equals(
                     TestControlCommands.BehaviorPreview,
                     StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.BehaviorId))
            {
                errorCode = TestControlErrorCodes.MissingBehaviorId;
                error = $"The {command} command requires a behavior id.";
                return false;
            }

            if (!IsValidBehaviorId(request.BehaviorId))
            {
                errorCode = TestControlErrorCodes.InvalidBehaviorId;
                error =
                    "Behavior ids must contain 1 through 96 lowercase ASCII " +
                    "letters, digits, '_' or '-'.";
                return false;
            }

            if (request.Action is not null ||
                request.Seed is not null ||
                request.After is not null ||
                HasPostcardArguments(request))
            {
                return UnexpectedArguments(out errorCode, out error);
            }
        }
        else if (command.Equals(
                     TestControlCommands.BehaviorTick,
                     StringComparison.OrdinalIgnoreCase))
        {
            if (request.Seed < 0)
            {
                errorCode = TestControlErrorCodes.InvalidSeed;
                error = "The behavior-tick seed must be zero or greater.";
                return false;
            }

            if (request.Action is not null ||
                request.BehaviorId is not null ||
                request.After is not null ||
                HasPostcardArguments(request))
            {
                return UnexpectedArguments(out errorCode, out error);
            }
        }
        else if (command.Equals(
                     TestControlCommands.BehaviorEvents,
                     StringComparison.OrdinalIgnoreCase))
        {
            if (request.After < 0)
            {
                errorCode = TestControlErrorCodes.InvalidAfter;
                error = "The behavior-events cursor must be zero or greater.";
                return false;
            }

            if (request.Action is not null ||
                request.BehaviorId is not null ||
                request.Seed is not null ||
                HasPostcardArguments(request))
            {
                return UnexpectedArguments(out errorCode, out error);
            }
        }
        else if (command.Equals(
                     TestControlCommands.MemoryAddInteraction,
                     StringComparison.OrdinalIgnoreCase))
        {
            if (request.Count is null)
            {
                errorCode = TestControlErrorCodes.MissingCount;
                error =
                    "The memory-add-interaction command requires a count.";
                return false;
            }

            if (request.Count is <= 0 or > 10)
            {
                errorCode = TestControlErrorCodes.InvalidCount;
                error =
                    "The memory interaction count must be from 1 through 10.";
                return false;
            }

            if (HasNonPostcardArguments(request) ||
                request.PostcardId is not null)
            {
                return UnexpectedArguments(out errorCode, out error);
            }
        }
        else if (command.Equals(
                     TestControlCommands.PostcardShow,
                     StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.PostcardId))
            {
                errorCode = TestControlErrorCodes.MissingPostcardId;
                error = "The postcard-show command requires a postcard id.";
                return false;
            }

            if (!IsValidPostcardId(request.PostcardId))
            {
                errorCode = TestControlErrorCodes.InvalidPostcardId;
                error =
                    "Postcard ids must contain 1 through 96 lowercase ASCII " +
                    "letters, digits, '_' or '-'.";
                return false;
            }

            if (HasNonPostcardArguments(request) ||
                request.Count is not null)
            {
                return UnexpectedArguments(out errorCode, out error);
            }
        }
        else if (request.Action is not null ||
                 HasBehaviorArguments(request) ||
                 HasPostcardArguments(request))
        {
            return UnexpectedArguments(out errorCode, out error);
        }

        errorCode = null;
        error = null;
        return true;
    }

    public static bool IsValidBehaviorId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 96 ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        return value.All(static character =>
            character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '-');
    }

    public static bool IsValidPostcardId(string? value) =>
        IsValidBehaviorId(value);

    private static bool HasBehaviorArguments(TestControlRequest request) =>
        request.BehaviorId is not null ||
        request.Seed is not null ||
        request.After is not null;

    private static bool HasPostcardArguments(TestControlRequest request) =>
        request.Count is not null ||
        request.PostcardId is not null;

    private static bool HasNonPostcardArguments(TestControlRequest request) =>
        request.Action is not null ||
        HasBehaviorArguments(request);

    private static bool UnexpectedArguments(
        out string? errorCode,
        out string? error)
    {
        errorCode = TestControlErrorCodes.UnexpectedArgument;
        error = "The request contains arguments that are not valid for its command.";
        return false;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public static class TestControlCommands
{
    public const string Ping = "ping";
    public const string Status = "status";
    public const string Clips = "clips";
    public const string BeginProbe = "begin-probe";
    public const string EndProbe = "end-probe";
    public const string Play = "play";
    public const string Show = "show";
    public const string Hide = "hide";
    public const string ResetPosition = "reset-position";
    public const string BehaviorStatus = "behavior-status";
    public const string BehaviorOpenBrowser = "behavior-open-browser";
    public const string BehaviorEvaluate = "behavior-evaluate";
    public const string BehaviorTick = "behavior-tick";
    public const string Enqueue = "enqueue";
    public const string StartIfIdle = "start-if-idle";
    public const string BehaviorPreview = "behavior-preview";
    public const string Queue = "queue";
    public const string BehaviorEvents = "behavior-events";
    public const string MemoryStatus = "memory-status";
    public const string MemoryAddInteraction = "memory-add-interaction";
    public const string PostcardStatus = "postcard-status";
    public const string PostcardStartOuting = "postcard-start-outing";
    public const string PostcardCompleteOuting = "postcard-complete-outing";
    public const string PostcardClaimArrival = "postcard-claim-arrival";
    public const string PostcardOpenGallery = "postcard-open-gallery";
    public const string PostcardShow = "postcard-show";
    public const string PostcardDismissPopup = "postcard-dismiss-popup";

    public static IReadOnlyList<string> All { get; } =
    [
        Ping,
        Status,
        Clips,
        BeginProbe,
        EndProbe,
        Play,
        Show,
        Hide,
        ResetPosition,
        BehaviorStatus,
        BehaviorOpenBrowser,
        BehaviorEvaluate,
        BehaviorTick,
        Enqueue,
        StartIfIdle,
        BehaviorPreview,
        Queue,
        BehaviorEvents,
        MemoryStatus,
        MemoryAddInteraction,
        PostcardStatus,
        PostcardStartOuting,
        PostcardCompleteOuting,
        PostcardClaimArrival,
        PostcardOpenGallery,
        PostcardShow,
        PostcardDismissPopup,
    ];
}

public static class TestControlErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string UnsupportedVersion = "unsupported_version";
    public const string InvalidCommand = "invalid_command";
    public const string MissingAction = "missing_action";
    public const string MissingBehaviorId = "missing_behavior_id";
    public const string InvalidBehaviorId = "invalid_behavior_id";
    public const string InvalidSeed = "invalid_seed";
    public const string InvalidAfter = "invalid_after";
    public const string MissingCount = "missing_count";
    public const string InvalidCount = "invalid_count";
    public const string MissingPostcardId = "missing_postcard_id";
    public const string InvalidPostcardId = "invalid_postcard_id";
    public const string RequiresIsolatedInstance =
        "requires_isolated_instance";
    public const string UnexpectedArgument = "unexpected_argument";
    public const string UnknownAction = "unknown_action";
    public const string OperationBlocked = "operation_blocked";
    public const string InternalError = "internal_error";
}

public sealed class TestControlRequest
{
    [JsonRequired]
    public int ProtocolVersion { get; init; } = TestControlProtocol.Version;

    public required string Command { get; init; }

    public string? Action { get; init; }

    public string? BehaviorId { get; init; }

    public int? Seed { get; init; }

    public long? After { get; init; }

    public long? Count { get; init; }

    public string? PostcardId { get; init; }
}

public sealed class TestControlResponse
{
    [JsonRequired]
    public int ProtocolVersion { get; init; } = TestControlProtocol.Version;

    public bool Ok { get; init; }

    public string? ErrorCode { get; init; }

    public string? Error { get; init; }

    public string? Message { get; init; }

    public TestRuntimeSnapshot? Snapshot { get; init; }

    public IReadOnlyList<TestClipInfo>? Clips { get; init; }

    public TestBehaviorStatus? BehaviorStatus { get; init; }

    public TestBehaviorEvaluationReport? BehaviorEvaluation { get; init; }

    public TestBehaviorTickResult? BehaviorTick { get; init; }

    public TestBehaviorMutationResult? BehaviorMutation { get; init; }

    public TestBehaviorQueueSnapshot? BehaviorQueue { get; init; }

    public TestBehaviorEventPage? BehaviorEvents { get; init; }

    public TestMemoryStatus? MemoryStatus { get; init; }

    public TestPostcardStatus? PostcardStatus { get; init; }
}

public sealed class TestRuntimeSnapshot
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public int ProcessId { get; init; }

    public string? Version { get; init; }

    public string? CurrentAction { get; init; }

    public int FrameIndex { get; init; }

    public int FrameCount { get; init; }

    public double Fps { get; init; }

    public bool Loop { get; init; }

    public string? AssetStatus { get; init; }

    public bool Visible { get; init; }

    public bool RequestedVisible { get; init; }

    public string? HiddenReason { get; init; }

    public bool Topmost { get; init; }

    public TestWindowRect? Window { get; init; }

    public string? BehaviorState { get; init; }

    public IReadOnlyDictionary<string, double> Emotions { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);

    public string? AppBasePath { get; init; }
}

public sealed class TestWindowRect
{
    public double Left { get; init; }

    public double Top { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public string? CoordinateSpace { get; init; }

    public string? DpiAwareness { get; init; }

    public int Dpi { get; init; }

    public TestPhysicalRect? MonitorBounds { get; init; }

    public TestPhysicalRect? WorkArea { get; init; }
}

public sealed class TestPhysicalRect
{
    public double Left { get; init; }

    public double Top { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }
}

public sealed class TestClipInfo
{
    public required string Name { get; init; }

    public int FrameCount { get; init; }

    public double Fps { get; init; }

    public bool Loop { get; init; }

    public string? AssetStatus { get; init; }

    public bool Placeholder { get; init; }

    public string? SourceBatch { get; init; }

    public string? SourceStage { get; init; }

    public IReadOnlyList<int> SourceFrameIndices { get; init; } = [];

    public IReadOnlyList<string> KnownIssues { get; init; } = [];
}
