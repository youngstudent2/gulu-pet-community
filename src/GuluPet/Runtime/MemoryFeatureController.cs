using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using GuluPet.Memories;
using GuluPet.Presentation;

namespace GuluPet.Runtime;

internal sealed record MemoryFeatureSnapshot(
    DateTimeOffset CapturedAtUtc,
    long AcceptedInteractionCount,
    long AcceptedInteractionsUntilNextUnlock,
    string? PendingMemoryId,
    IReadOnlyList<string> CompletedMemoryIds,
    bool IsPlaying,
    string? PlayingMemoryId,
    string? PlaybackPhase,
    bool PresentationActive,
    string? LastPlaybackOutcome,
    string? LastPlaybackError);

internal sealed record MemoryDiaryUnlockSnapshot(
    string Id,
    string Title,
    string Description,
    DateTimeOffset UnlockedAtUtc);

internal enum MemoryTestInteractionBatchResult
{
    Added,
    PresentationBusy,
    CrossesNextUnlock,
}

/// <summary>
/// Owns the application-level memory presentation session. Memory playback is
/// deliberately outside the pet behavior state machine: a separate companion
/// window stays beside the live pet, while this controller serializes unlock,
/// preflight, first natural completion, replay, and close semantics.
/// </summary>
internal sealed class MemoryFeatureController : IDisposable
{
    private const double PreflightProgressCeiling = 0.92d;

    private readonly MemoryCatalog _catalog;
    private readonly PetWindow _petWindow;
    private readonly PetController _petController;
    private readonly Dispatcher _dispatcher;
    private readonly string _videoDirectory;
    private readonly string _thumbnailDirectory;
    private readonly MemoryProgressStore? _store;
    private readonly MemoryProgressTracker _progress;
    private readonly bool _isolatedTestInstance;
    private readonly IMemoryOfferPrompt _offerPrompt;
    private readonly IMemoryMediaPreflight _mediaPreflight;
    private readonly IMemoryPlaybackPresenter _playbackPresenter;
    private readonly CancellationTokenSource _lifetime = new();

    private object? _playbackOperation;
    private IMemoryPlaybackSession? _playbackSession;
    private string? _playingMemoryId;
    private MemoryPlaybackPhase? _playbackPhase;
    private string? _lastPlaybackOutcome;
    private string? _lastPlaybackError;
    private string? _deferredPendingMemoryId;
    private string? _failedMemoryId;
    private bool _presentationVisible;
    private bool _progressDirty;
    private bool _offerVisible;
    private bool _disposed;

    internal MemoryFeatureController(
        PetWindow petWindow,
        PetController petController,
        string assetsRoot,
        bool isolatedTestInstance,
        MemoryCatalog? catalog = null,
        MemoryProgressStore? store = null,
        IMemoryOfferPrompt? offerPrompt = null,
        IMemoryMediaPreflight? mediaPreflight = null,
        IMemoryPlaybackPresenter? playbackPresenter = null)
    {
        _petWindow = petWindow
            ?? throw new ArgumentNullException(nameof(petWindow));
        _petController = petController
            ?? throw new ArgumentNullException(nameof(petController));
        _dispatcher = petWindow.Dispatcher;
        _catalog = catalog ?? MemoryCatalog.Default;
        _isolatedTestInstance = isolatedTestInstance;

        string memoryAssets = ResolveMemoryAssetsRoot(assetsRoot);
        _videoDirectory = Path.Combine(memoryAssets, "Videos");
        _thumbnailDirectory = Path.Combine(memoryAssets, "Thumbnails");
        ValidatePackagedVideos(_catalog, _videoDirectory);
        ValidatePackagedThumbnails(_catalog, _thumbnailDirectory);
        _offerPrompt = offerPrompt
            ?? (isolatedTestInstance
                ? new AutomaticMemoryOfferPrompt()
                : new WpfMemoryOfferPrompt(petWindow));
        _mediaPreflight = mediaPreflight
            ?? (isolatedTestInstance
                ? new PassThroughMemoryMediaPreflight()
                : new WpfMemoryMediaPreflight(
                    _dispatcher,
                    MemoryMediaPackageIndex.Load(
                        _catalog,
                        _videoDirectory)));
        _playbackPresenter = playbackPresenter
            ?? new WpfMemoryPlaybackPresenter(petWindow);

        MemoryProgressStore? usableStore = isolatedTestInstance
            ? null
            : store ?? new MemoryProgressStore();
        MemoryProgressState initialState =
            MemoryProgressState.CreateDefault();
        if (usableStore is not null)
        {
            try
            {
                initialState = usableStore.Load();
            }
            catch (Exception exception)
                when (exception is IOException
                      or UnauthorizedAccessException
                      or InvalidDataException)
            {
                Debug.WriteLine(
                    $"Unable to load GuluPet memory progress: {exception}");
                // MemoryProgressStore only throws after it cannot preserve
                // the unreadable document. Do not overwrite the user's only
                // recovery copy during this session.
                usableStore = null;
            }
        }

        try
        {
            _progress = new MemoryProgressTracker(
                _catalog,
                initialState);
        }
        catch (InvalidDataException exception)
        {
            Debug.WriteLine(
                $"Unable to reconcile GuluPet memory progress: {exception}");
            if (usableStore is not null
                && !TryQuarantineCatalogMismatch(usableStore))
            {
                usableStore = null;
            }

            _progress = new MemoryProgressTracker(
                _catalog,
                MemoryProgressState.CreateDefault());
        }

        _store = usableStore;
        _petController.AcceptedActiveInteraction +=
            OnAcceptedActiveInteraction;
    }

    internal event EventHandler? StateChanged;

    internal event EventHandler<UserInteractionAcceptedEventArgs>?
        UserInteractionAccepted;

    internal bool HasPendingPlayback =>
        _progress.PendingDefinition is not null;

    internal bool IsPlaying => _playbackOperation is not null;

    internal bool IsOfferVisible => _offerVisible;

    internal bool CanInjectTestInteraction => _isolatedTestInstance;

    internal void SetPresentationVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        bool becameVisible = visible && !_presentationVisible;
        _presentationVisible = visible;
        if (becameVisible)
        {
            _ = TryPersistCurrent();
            TryOfferPendingPlayback();
        }
    }

    internal MemoryTestInteractionBatchResult
        AddAcceptedInteractionsForTest(
            long count,
            out MemoryFeatureSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        if (!_isolatedTestInstance)
        {
            throw new InvalidOperationException(
                "Memory interaction injection requires an isolated test " +
                "instance.");
        }

        if (count is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "Memory test interaction count must be between 1 and 10.");
        }

        if (_playbackOperation is not null ||
            _offerVisible ||
            _progress.PendingDefinition is not null)
        {
            snapshot = GetSnapshot();
            return MemoryTestInteractionBatchResult.PresentationBusy;
        }

        long untilNextUnlock =
            _progress.AcceptedInteractionsUntilNextUnlock;
        if (untilNextUnlock > 0 && count > untilNextUnlock)
        {
            snapshot = GetSnapshot();
            return MemoryTestInteractionBatchResult.CrossesNextUnlock;
        }

        for (long index = 0; index < count; index++)
        {
            RecordAcceptedInteraction();
        }

        snapshot = GetSnapshot();
        return MemoryTestInteractionBatchResult.Added;
    }

    internal MemoryFeatureSnapshot GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MemoryProgressState state = _progress.Current;
        return new MemoryFeatureSnapshot(
            DateTimeOffset.UtcNow,
            state.AcceptedInteractionCount,
            _progress.AcceptedInteractionsUntilNextUnlock,
            state.Pending?.MemoryId,
            state.Completed
                .Select(static record => record.MemoryId)
                .ToArray(),
            IsPlaying,
            _playingMemoryId,
            _playbackPhase?.ToString().ToLowerInvariant(),
            _playbackSession?.IsVisible == true,
            _lastPlaybackOutcome,
            _lastPlaybackError);
    }

    internal IReadOnlyList<MemoryDiaryUnlockSnapshot> GetDiaryUnlocks()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MemoryProgressState state = _progress.Current;
        IEnumerable<(string Id, DateTimeOffset UnlockedAtUtc)> records =
            state.Completed.Select(static item =>
                (item.MemoryId, item.UnlockedAtUtc));
        if (state.Pending is { } pending)
        {
            records = records.Append((pending.MemoryId, pending.UnlockedAtUtc));
        }

        return records
            .Where(record => _catalog.TryGet(record.Id, out _))
            .Select(record =>
            {
                _ = _catalog.TryGet(
                    record.Id,
                    out MemoryDefinition? definition);
                return new MemoryDiaryUnlockSnapshot(
                    record.Id,
                    definition!.Title,
                    definition.Description,
                    record.UnlockedAtUtc);
            })
            .OrderBy(static record => record.UnlockedAtUtc)
            .ToArray();
    }

    internal MemoryMenuState GetMenuState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MemoryProgressState state = _progress.Current;
        return MemoryMenuState.Create(
            _catalog,
            state.Completed
                .Select(static record => record.MemoryId)
                .ToArray(),
            state.Pending?.MemoryId,
            playbackBusy:
                _playbackOperation is not null || _offerVisible,
            failedMemoryId: _failedMemoryId);
    }

    internal MemoryGalleryState GetGalleryState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return MemoryGalleryState.Create(
            _catalog,
            _progress.Current,
            _thumbnailDirectory,
            playbackBusy:
                _playbackOperation is not null || _offerVisible,
            playingMemoryId: _playingMemoryId,
            failedMemoryId: _failedMemoryId,
            failureMessage: _lastPlaybackError);
    }

    internal bool TryReplayCompletedMemory(string memoryId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        if (_playbackOperation is not null
            || _offerVisible
            || !_catalog.TryGet(memoryId, out MemoryDefinition? memory)
            || memory is null)
        {
            return false;
        }

        if (string.Equals(
                _progress.Current.Pending?.MemoryId,
                memoryId,
                StringComparison.Ordinal))
        {
            _deferredPendingMemoryId = null;
            StartPlayback(memory, completesPending: true);
            UserInteractionAccepted?.Invoke(
                this,
                new UserInteractionAcceptedEventArgs(
                    UserInteractionKind.MemoryReplay,
                    DateTimeOffset.UtcNow,
                    memoryId));
            return true;
        }

        if (_progress.Current.Completed.Any(record =>
                string.Equals(
                    record.MemoryId,
                    memoryId,
                    StringComparison.Ordinal)))
        {
            StartPlayback(memory, completesPending: false);
            UserInteractionAccepted?.Invoke(
                this,
                new UserInteractionAcceptedEventArgs(
                    UserInteractionKind.MemoryReplay,
                    DateTimeOffset.UtcNow,
                    memoryId));
            return true;
        }

        return false;
    }

    internal bool ClosePlayback()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        if (_playbackOperation is null)
        {
            return false;
        }

        _playbackPresenter.CloseCurrent();
        return true;
    }

    internal bool CloseOffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        if (!_offerVisible)
        {
            return false;
        }

        _offerPrompt.CloseCurrent();
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        VerifyAccess();
        if (!TryPersistCurrent())
        {
            Debug.WriteLine(
                "GuluPet memory progress could not be flushed during exit.");
        }

        _disposed = true;
        _petController.AcceptedActiveInteraction -=
            OnAcceptedActiveInteraction;
        _lifetime.Cancel();
        _playbackPresenter.Dispose();
        _offerPrompt.Dispose();
        _lifetime.Dispose();
    }

    private void OnAcceptedActiveInteraction(
        object? sender,
        EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        VerifyAccess();
        RecordAcceptedInteraction();
    }

    private void RecordAcceptedInteraction()
    {
        if (_playbackOperation is not null || _offerVisible)
        {
            return;
        }

        if (_progress.PendingDefinition is not null)
        {
            if (TryPersistCurrent())
            {
                TryOfferPendingPlayback();
            }

            return;
        }

        // A naturally completed memory must become durable before another
        // accepted interaction can advance the next threshold. Otherwise a
        // crash could replay the old pending memory while preserving later
        // in-memory counts that were never representable on disk.
        if (_progressDirty && !TryPersistCurrent())
        {
            return;
        }

        MemoryProgressUpdate update;
        try
        {
            update = _progress.RecordAcceptedInteraction(
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
            when (exception is InvalidDataException
                  or InvalidOperationException
                  or ArgumentOutOfRangeException)
        {
            Debug.WriteLine(
                $"Unable to record GuluPet memory interaction: {exception}");
            return;
        }

        _progressDirty = true;
        bool persisted = TryPersistCurrent();
        RaiseStateChanged();
        if (update.NewlyUnlockedPending is not null && persisted)
        {
            TryOfferPendingPlayback();
        }
    }

    private void TryOfferPendingPlayback(bool force = false)
    {
        VerifyAccess();
        if (_disposed ||
            !_presentationVisible ||
            _playbackOperation is not null ||
            _offerVisible ||
            _progress.PendingDefinition is not { } memory)
        {
            return;
        }

        if (!force
            && string.Equals(
                _deferredPendingMemoryId,
                memory.Id,
                StringComparison.Ordinal))
        {
            return;
        }

        // A pending item loaded from disk is already durable and must remain
        // playable even if the state directory has since become read-only.
        // A newly unlocked item remains dirty until its marker is saved.
        if (_progressDirty && !TryPersistCurrent())
        {
            return;
        }

        _offerVisible = true;
        RaiseStateChanged();
        try
        {
            MemoryOfferDecision decision = _offerPrompt.Show(
                new MemoryOfferRequest(
                    memory.Title,
                    ResolveThumbnailPath(memory),
                    string.Equals(
                        _failedMemoryId,
                        memory.Id,
                        StringComparison.Ordinal)
                            ? _lastPlaybackError
                            : null));
            if (decision == MemoryOfferDecision.PlayNow)
            {
                _deferredPendingMemoryId = null;
            }
            else
            {
                _deferredPendingMemoryId = memory.Id;
            }
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or InvalidDataException
                  or InvalidOperationException)
        {
            _failedMemoryId = memory.Id;
            _lastPlaybackOutcome = "offer-failed";
            _lastPlaybackError =
                "这段回忆今天有点害羞，咕噜先替妈咪收好，晚点再来看吧。";
            _deferredPendingMemoryId = memory.Id;
            Debug.WriteLine(
                $"Unable to show GuluPet memory offer '{memory.Id}': " +
                exception);
        }
        finally
        {
            _offerVisible = false;
        }

        if (_deferredPendingMemoryId is null)
        {
            StartPlayback(memory, completesPending: true);
        }
        else
        {
            RaiseStateChanged();
        }
    }

    private void StartPlayback(
        MemoryDefinition memory,
        bool completesPending)
    {
        _lastPlaybackOutcome = null;
        _lastPlaybackError = null;
        _failedMemoryId = null;
        var operation = new object();
        _playbackOperation = operation;
        _playingMemoryId = memory.Id;
        RaiseStateChanged();
        _ = PlayMemoryAsync(
            memory,
            _lifetime.Token,
            operation,
            completesPending);
    }

    private async Task PlayMemoryAsync(
        MemoryDefinition memory,
        CancellationToken cancellationToken,
        object operation,
        bool completesPending)
    {
        IMemoryPlaybackSession? session = null;
        EventHandler? phaseChanged = null;
        EventHandler? naturalPlaybackCompleted = null;
        EventHandler? retryRequested = null;
        bool naturalCompletionRecorded = false;
        bool retryPlaybackRequested = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Uri> sources =
                ResolveVideoSources(memory);
            session = _playbackPresenter.Open(
                sources,
                memory.Title,
                cancellationToken);
            _playbackSession = session;
            _playbackPhase = session.Phase;

            phaseChanged = (_, _) =>
            {
                if (_disposed || !ReferenceEquals(_playbackSession, session))
                {
                    return;
                }

                _playbackPhase = session.Phase;
                RaiseStateChanged();
            };
            naturalPlaybackCompleted = (_, _) =>
            {
                if (_disposed
                    || naturalCompletionRecorded
                    || !ReferenceEquals(_playbackSession, session))
                {
                    return;
                }

                naturalCompletionRecorded = true;
                if (completesPending)
                {
                    _progress.RecordNaturalPlaybackCompleted(
                        memory.Id,
                        DateTimeOffset.UtcNow);
                    _progressDirty = true;
                    _ = TryPersistCurrent();
                }

                _lastPlaybackOutcome = "ended";
                _lastPlaybackError = null;
                _failedMemoryId = null;
                _deferredPendingMemoryId = null;
                RaiseStateChanged();
            };
            retryRequested = (sender, _) =>
            {
                if (_disposed
                    || sender is not IMemoryPlaybackSession retrySession
                    || retrySession.Phase != MemoryPlaybackPhase.Failed
                    || !ReferenceEquals(_playbackSession, retrySession))
                {
                    return;
                }

                retryPlaybackRequested = true;
            };
            session.PhaseChanged += phaseChanged;
            session.NaturalPlaybackCompleted += naturalPlaybackCompleted;
            session.RetryRequested += retryRequested;
            RaiseStateChanged();

            var progress = new InlineProgress<MemoryMediaPreflightProgress>(
                update =>
                {
                    if (ReferenceEquals(_playbackSession, session))
                    {
                        session.ReportLoadingProgress(
                            CreateLoadingProgress(update));
                    }
                });
            MemoryPlaybackResult? result = null;
            using (var loadingCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken))
            {
                Task<MemoryMediaPreflightResult> preflightTask =
                    _mediaPreflight.ValidateAsync(
                        sources,
                        progress,
                        loadingCancellation.Token);
                Task<MemoryPlaybackResult> completionTask =
                    session.Completion;
                Task firstCompleted = await Task.WhenAny(
                    preflightTask,
                    completionTask);
                if (ReferenceEquals(firstCompleted, completionTask))
                {
                    loadingCancellation.Cancel();
                    try
                    {
                        _ = await preflightTask;
                    }
                    catch (OperationCanceledException)
                        when (loadingCancellation.IsCancellationRequested)
                    {
                    }
                    catch (Exception exception)
                    {
                        Debug.WriteLine(
                            "Memory preflight faulted after its playback " +
                            $"window closed: {exception}");
                    }

                    result = await completionTask;
                }
                else
                {
                    MemoryMediaPreflightResult preflight =
                        await preflightTask;
                    if (completionTask.IsCompleted)
                    {
                        result = await completionTask;
                    }
                    else if (!preflight.Succeeded)
                    {
                        _lastPlaybackOutcome = "preflight-failed";
                        _lastPlaybackError = preflight.UserMessage
                            ?? "播放器没能检查这段视频，请再试一次。";
                        _failedMemoryId = memory.Id;
                        if (completesPending)
                        {
                            _deferredPendingMemoryId = memory.Id;
                        }

                        session.MarkLoadingFailed(_lastPlaybackError);
                    }
                    else
                    {
                        if (preflight.VideoDimensions is { Count: > 0 }
                            dimensions)
                        {
                            session.ConfigureVideoDimensions(dimensions);
                        }

                        session.ReportLoadingProgress(
                            new MemoryLoadingProgress(
                                PreflightProgressCeiling,
                                "正在准备播放器"));
                        var preparationProgress =
                            new InlineProgress<MemoryLoadingProgress>(
                                update =>
                                {
                                    if (ReferenceEquals(
                                        _playbackSession,
                                        session))
                                    {
                                        session.ReportLoadingProgress(
                                            CreatePreparationProgress(
                                                update));
                                    }
                                });
                        Task<MemoryPlaybackPreparationResult>
                            preparationTask = session.PrepareAsync(
                                preparationProgress,
                                loadingCancellation.Token);
                        firstCompleted = await Task.WhenAny(
                            preparationTask,
                            completionTask);
                        if (ReferenceEquals(
                            firstCompleted,
                            completionTask))
                        {
                            loadingCancellation.Cancel();
                            try
                            {
                                _ = await preparationTask;
                            }
                            catch (OperationCanceledException)
                                when (loadingCancellation
                                    .IsCancellationRequested)
                            {
                            }
                            catch (Exception exception)
                            {
                                Debug.WriteLine(
                                    "Memory player preparation faulted " +
                                    "after its playback window closed: " +
                                    exception);
                            }

                            result = await completionTask;
                        }
                        else
                        {
                            MemoryPlaybackPreparationResult preparation =
                                await preparationTask;
                            if (completionTask.IsCompleted)
                            {
                                result = await completionTask;
                            }
                            else if (!preparation.Succeeded)
                            {
                                _lastPlaybackOutcome =
                                    "preparation-failed";
                                _lastPlaybackError =
                                    preparation.UserMessage
                                    ?? "播放器没能准备好这段视频，请再试一次。";
                                _failedMemoryId = memory.Id;
                                if (completesPending)
                                {
                                    _deferredPendingMemoryId = memory.Id;
                                }

                                session.MarkLoadingFailed(
                                    _lastPlaybackError,
                                    preparation.Error);
                            }
                            else
                            {
                                session.ReportLoadingProgress(
                                    new MemoryLoadingProgress(
                                        1d,
                                        "可以播放"));
                                session.MarkReady();
                            }
                        }
                    }
                }
            }

            result ??= await session.Completion;
            if (retryPlaybackRequested)
            {
                _lastPlaybackOutcome = null;
                _lastPlaybackError = null;
                _failedMemoryId = null;
                _deferredPendingMemoryId = null;
            }
            else if (naturalCompletionRecorded)
            {
                _lastPlaybackOutcome = "ended";
                _lastPlaybackError = null;
                _failedMemoryId = null;
                _deferredPendingMemoryId = null;
            }
            else if (result.Outcome == MemoryPlaybackOutcome.Failed)
            {
                _lastPlaybackOutcome ??= "failed";
                _lastPlaybackError ??=
                    "播放器没能播放这段视频，请再试一次。";
                _failedMemoryId = memory.Id;
                if (completesPending)
                {
                    _deferredPendingMemoryId = memory.Id;
                }

                Debug.WriteLine(
                    $"GuluPet memory '{memory.Id}' failed to play: " +
                    result.Error);
            }
            else
            {
                _lastPlaybackOutcome = "cancelled";
                _lastPlaybackError = null;
                if (completesPending)
                {
                    _deferredPendingMemoryId = memory.Id;
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _lastPlaybackOutcome = "cancelled";
            _lastPlaybackError = null;
        }
        catch (Exception exception)
        {
            _lastPlaybackOutcome = "failed";
            _lastPlaybackError =
                "播放器没能打开这段视频，请再试一次。";
            _failedMemoryId = memory.Id;
            if (completesPending)
            {
                _deferredPendingMemoryId = memory.Id;
            }

            Debug.WriteLine(
                $"Unable to present GuluPet memory '{memory.Id}': " +
                exception);
        }
        finally
        {
            if (session is not null)
            {
                if (phaseChanged is not null)
                {
                    session.PhaseChanged -= phaseChanged;
                }

                if (naturalPlaybackCompleted is not null)
                {
                    session.NaturalPlaybackCompleted -=
                        naturalPlaybackCompleted;
                }

                if (retryRequested is not null)
                {
                    session.RetryRequested -= retryRequested;
                }

                session.Dispose();
            }

            if (ReferenceEquals(_playbackSession, session))
            {
                _playbackSession = null;
                _playbackPhase = null;
            }

            if (ReferenceEquals(_playbackOperation, operation))
            {
                _playingMemoryId = null;
                _playbackOperation = null;
                if (!_disposed)
                {
                    if (retryPlaybackRequested
                        && !cancellationToken.IsCancellationRequested)
                    {
                        StartPlayback(memory, completesPending);
                    }
                    else
                    {
                        RaiseStateChanged();
                    }
                }
            }
        }
    }

    private static MemoryLoadingProgress CreateLoadingProgress(
        MemoryMediaPreflightProgress progress)
    {
        string status = progress.Stage switch
        {
            MemoryMediaPreflightStage.PackageIntegrity =>
                $"检查视频 {progress.SourceNumber}/{progress.SourceCount}",
            MemoryMediaPreflightStage.Decoder =>
                $"检查播放器兼容性 {progress.SourceNumber}/{progress.SourceCount}",
            MemoryMediaPreflightStage.Completed => "正在准备播放器",
            _ => "检查视频",
        };
        double fraction = Math.Clamp(progress.Fraction, 0d, 1d)
            * PreflightProgressCeiling;
        return new MemoryLoadingProgress(fraction, status);
    }

    private static MemoryLoadingProgress CreatePreparationProgress(
        MemoryLoadingProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        double preparationFraction = Math.Clamp(
            progress.Fraction,
            0d,
            1d);
        double fraction = PreflightProgressCeiling
            + ((1d - PreflightProgressCeiling) * preparationFraction);
        return new MemoryLoadingProgress(
            fraction,
            string.IsNullOrWhiteSpace(progress.StatusText)
                ? "正在准备播放器"
                : progress.StatusText.Trim());
    }

    private bool TryPersistCurrent()
    {
        if (!_progressDirty)
        {
            return true;
        }

        if (_store is null)
        {
            if (_isolatedTestInstance)
            {
                _progressDirty = false;
                return true;
            }

            return false;
        }

        try
        {
            _store.Save(_progress.Current);
            _progressDirty = false;
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or InvalidDataException
                  or InvalidOperationException)
        {
            Debug.WriteLine(
                $"Unable to save GuluPet memory progress: {exception}");
            return false;
        }
    }

    private IReadOnlyList<Uri> ResolveVideoSources(
        MemoryDefinition memory) =>
        memory.VideoFileNames
            .Select(videoFileName =>
                new Uri(
                    ResolveVideoPath(memory.Id, videoFileName),
                    UriKind.Absolute))
            .ToArray();

    private string ResolveVideoPath(
        string memoryId,
        string videoFileName)
    {
        string videoPath = Path.GetFullPath(
            Path.Combine(
                _videoDirectory,
                videoFileName));
        string relative = Path.GetRelativePath(
            _videoDirectory,
            videoPath);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            !File.Exists(videoPath))
        {
            throw new FileNotFoundException(
                $"Memory video '{memoryId}' segment '{videoFileName}' is " +
                "not available.",
                videoPath);
        }

        return videoPath;
    }

    private string ResolveThumbnailPath(MemoryDefinition memory)
    {
        string path = Path.GetFullPath(
            Path.Combine(
                _thumbnailDirectory,
                memory.ThumbnailFileName));
        string relative = Path.GetRelativePath(
            _thumbnailDirectory,
            path);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || !File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Memory thumbnail '{memory.Id}' is not available.",
                path);
        }

        return path;
    }

    private void RaiseStateChanged() =>
        StateChanged?.Invoke(this, EventArgs.Empty);

    private void VerifyAccess() => _dispatcher.VerifyAccess();

    private static string ResolveMemoryAssetsRoot(string assetsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsRoot);
        string root = Path.GetFullPath(assetsRoot);
        string memoryRoot = Path.Combine(root, "Memories");
        if (!Directory.Exists(memoryRoot))
        {
            throw new DirectoryNotFoundException(
                $"Memory asset directory '{memoryRoot}' does not exist.");
        }

        return memoryRoot;
    }

    private static void ValidatePackagedVideos(
        MemoryCatalog catalog,
        string videoDirectory)
    {
        if (!Directory.Exists(videoDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Memory video directory '{videoDirectory}' does not exist.");
        }

        foreach (MemoryDefinition memory in catalog.Definitions)
        {
            foreach (string videoFileName in memory.VideoFileNames)
            {
                string path = Path.Combine(
                    videoDirectory,
                    videoFileName);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"Memory video '{memory.Id}' segment " +
                        $"'{videoFileName}' is missing.",
                        path);
                }
            }
        }
    }

    private static void ValidatePackagedThumbnails(
        MemoryCatalog catalog,
        string thumbnailDirectory)
    {
        if (!Directory.Exists(thumbnailDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Memory thumbnail directory '{thumbnailDirectory}' does " +
                "not exist.");
        }

        foreach (MemoryDefinition memory in catalog.Definitions)
        {
            string path = Path.Combine(
                thumbnailDirectory,
                memory.ThumbnailFileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Memory thumbnail '{memory.Id}' is missing.",
                    path);
            }
        }
    }

    private static bool TryQuarantineCatalogMismatch(
        MemoryProgressStore store)
    {
        if (!File.Exists(store.StatePath))
        {
            return true;
        }

        try
        {
            string directory = Path.GetDirectoryName(store.StatePath)
                ?? throw new InvalidOperationException(
                    "The memory state path has no parent directory.");
            string destination = Path.Combine(
                directory,
                $"memories.corrupt-" +
                $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-" +
                $"{Guid.NewGuid():N}.json");
            File.Move(store.StatePath, destination);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or InvalidOperationException)
        {
            Debug.WriteLine(
                $"Unable to preserve mismatched memory progress: {exception}");
            return false;
        }
    }

    private sealed class InlineProgress<T>(Action<T> report)
        : IProgress<T>
    {
        private readonly Action<T> _report = report
            ?? throw new ArgumentNullException(nameof(report));

        public void Report(T value) => _report(value);
    }
}
