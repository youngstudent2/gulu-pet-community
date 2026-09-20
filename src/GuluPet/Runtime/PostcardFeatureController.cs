using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using GuluPet.Postcards;
using GuluPet.Presentation;

namespace GuluPet.Runtime;

internal sealed record PostcardFeatureSnapshot(
    DateTimeOffset CapturedAtUtc,
    long TotalInputCount,
    long InputsUntilNextUnlock,
    int AvailablePostcardCount,
    IReadOnlyList<string> UnlockedPostcardIds,
    IReadOnlyList<string> UnacknowledgedPostcardIds,
    bool PopupVisible,
    string? PopupPostcardId,
    bool GalleryVisible,
    string? GalleryPostcardId)
{
    internal PostcardOutingPhase OutingPhase { get; init; }

    internal DateTimeOffset? OutingStartedAtUtc { get; init; }

    internal DateTimeOffset? OutingReadyAtUtc { get; init; }

    internal DateTimeOffset? OutingArrivedAtUtc { get; init; }

    internal long OutingRemainingSeconds { get; init; }

    internal string? NextPostcardId { get; init; }
}

internal sealed record PostcardDiaryUnlockSnapshot(
    string Id,
    string Title,
    string Location,
    DateTimeOffset UnlockedAtUtc);

/// <summary>
/// Application-level orchestration for postcard progress, presentation and
/// persistence. The postcard domain remains independent from behavior state,
/// while this controller owns the WPF windows and the non-owning sensing
/// subscription.
/// </summary>
internal sealed class PostcardFeatureController : IDisposable
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OutingRefreshInterval =
        TimeSpan.FromSeconds(1);

    private readonly PostcardCatalog _catalog;
    private readonly PetWindow _petWindow;
    private readonly Dispatcher _dispatcher;
    private readonly bool _isolatedTestInstance;
    private readonly TimeProvider _timeProvider;
    private readonly PostcardProgressTracker _progress;
    private readonly PostcardProgressStore? _store;
    private readonly PostcardUnlockWindow _unlockWindow;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _outingTimer;
    private readonly LinkedList<PostcardDefinition> _revealQueue = [];
    private readonly Queue<PostcardDefinition>
        _revealsAwaitingPersistence = [];
    private PostcardGalleryWindow? _galleryWindow;
    private bool _presentationVisible;
    private bool _progressDirty;
    private bool _currentPopupIsTestPreview;
    private bool _disposed;

    internal PostcardFeatureController(
        PostcardCatalog catalog,
        PetWindow petWindow,
        bool isolatedTestInstance,
        TimeProvider? timeProvider = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _petWindow = petWindow
            ?? throw new ArgumentNullException(nameof(petWindow));
        _dispatcher = petWindow.Dispatcher;
        _isolatedTestInstance = isolatedTestInstance;
        _timeProvider = timeProvider ?? TimeProvider.System;

        PostcardProgressStore? store = isolatedTestInstance
            ? null
            : new PostcardProgressStore();
        PostcardCollectionState initialState =
            PostcardCollectionState.CreateDefault();
        if (store is not null)
        {
            try
            {
                initialState = store.Load();
            }
            catch (Exception exception)
                when (IsPersistenceFailure(exception))
            {
                Debug.WriteLine(
                    $"Unable to load GuluPet postcard progress: {exception}");
                if (!TryQuarantine(store))
                {
                    // Keep the unreadable document untouched rather than
                    // overwriting the user's only recovery copy.
                    store = null;
                }
            }
        }

        try
        {
            _progress = new PostcardProgressTracker(
                catalog,
                initialState);
        }
        catch (InvalidDataException exception)
        {
            Debug.WriteLine(
                $"Unable to reconcile GuluPet postcard progress: {exception}");
            if (store is not null && !TryQuarantine(store))
            {
                store = null;
            }

            initialState = PostcardCollectionState.CreateDefault();
            _progress = new PostcardProgressTracker(
                catalog,
                initialState);
        }

        _store = store;
        _unlockWindow = new PostcardUnlockWindow();
        _unlockWindow.Dismissed += OnPostcardDismissed;
        _saveTimer = new DispatcherTimer(
            SaveInterval,
            DispatcherPriority.Background,
            OnSaveTimerTick,
            _dispatcher);
        _saveTimer.Start();

        _outingTimer = new DispatcherTimer(
            OutingRefreshInterval,
            DispatcherPriority.Background,
            OnOutingTimerTick,
            _dispatcher);
        _petWindow.TravelRequested += OnTravelRequested;

        foreach (PostcardDefinition postcard in CreateStartupRevealPlan(
                     catalog,
                     _progress.Current))
        {
            _revealQueue.AddLast(postcard);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool outingDurable = AdvanceOutingAndPersist(now);
        RefreshOutingUi(now, outingDurable);
        _outingTimer.Start();
    }

    internal event EventHandler? StateChanged;

    internal event EventHandler<UserInteractionAcceptedEventArgs>?
        UserInteractionAccepted;

    internal bool CanInjectTestInput => false;

    internal bool CanControlOutingForTest => _isolatedTestInstance;

    internal void SetPresentationVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        if (_presentationVisible == visible)
        {
            if (visible)
            {
                TryShowNextReveal();
            }
            return;
        }

        _presentationVisible = visible;
        if (!visible)
        {
            PostcardDefinition? deferred = _unlockWindow.Defer();
            if (deferred is not null && !_currentPopupIsTestPreview)
            {
                _revealQueue.AddFirst(deferred);
            }

            _currentPopupIsTestPreview = false;
        }
        else
        {
            TryShowNextReveal();
        }

        RaiseStateChanged();
    }

    internal void ShowGallery()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        EnsureGallery();
        RefreshGallery();
        _galleryWindow!.ShowGallery(_petWindow);
        RaiseStateChanged();
    }

    internal void CloseGallery()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        if (_galleryWindow is null)
        {
            return;
        }

        PostcardGalleryWindow galleryWindow = _galleryWindow;
        _galleryWindow = null;
        galleryWindow.Closed -= OnGalleryClosed;
        galleryWindow.Close();
        RaiseStateChanged();
    }

    internal bool OpenGalleryPostcard(string postcardId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        if (!_progress.Current.Unlocked.Any(record =>
                string.Equals(
                    record.PostcardId,
                    postcardId,
                    StringComparison.Ordinal)))
        {
            return false;
        }

        ShowGallery();
        _galleryWindow!.OpenPostcard(postcardId);
        RaiseStateChanged();
        return true;
    }

    internal bool ShowPostcardForTest(string postcardId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        if (!_presentationVisible
            || !_catalog.TryGet(
                postcardId,
                out PostcardDefinition? postcard)
            || postcard is null)
        {
            return false;
        }

        PostcardDefinition? deferred = _unlockWindow.Defer();
        if (deferred is not null && !_currentPopupIsTestPreview)
        {
            _revealQueue.AddFirst(deferred);
        }

        _currentPopupIsTestPreview = true;
        _unlockWindow.ShowPostcard(
            postcard,
            _catalog.Count,
            _petWindow);
        RaiseStateChanged();
        return true;
    }

    internal bool DismissPopupForTest()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        if (!_unlockWindow.IsPresenting)
        {
            return false;
        }

        _unlockWindow.Dismiss();
        return true;
    }

    internal PostcardProgressUpdate AddInputForTest(long count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        throw new InvalidOperationException(
            "Postcard input injection is retired; use the outing test controls.");
    }

    internal PostcardOutingUpdate StartOutingForTest()
    {
        EnsureOutingTestControl();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        PostcardOutingUpdate update = _progress.StartOuting(now);
        _progressDirty = true;
        if (!TryPersist())
        {
            throw new InvalidOperationException(
                "Unable to persist the test postcard outing.");
        }

        RefreshOutingUi(now);
        return update;
    }

    internal PostcardOutingUpdate CompleteOutingForTest()
    {
        EnsureOutingTestControl();
        PostcardOutingRecord outing = _progress.Current.CurrentOuting
            ?? throw new InvalidOperationException(
                "There is no postcard outing to complete.");
        PostcardOutingUpdate update = _progress.MarkArrived(
            outing.ReadyAtUtc);
        if (update.Changed)
        {
            _progressDirty = true;
        }

        if (!TryPersist())
        {
            throw new InvalidOperationException(
                "Unable to persist the completed test postcard outing.");
        }

        RefreshOutingUi(outing.ReadyAtUtc);
        return update;
    }

    internal bool ClaimArrivalForTest()
    {
        EnsureOutingTestControl();
        PostcardOutingRecord outing = _progress.Current.CurrentOuting
            ?? throw new InvalidOperationException(
                "There is no postcard waiting at the door.");
        DateTimeOffset claimedAtUtc = outing.ArrivedAtUtc
            ?? throw new InvalidOperationException(
                "The postcard outing has not arrived yet.");
        bool claimed = TryClaimWaitingPostcard(claimedAtUtc);
        RefreshOutingUi(claimedAtUtc, claimed);
        return claimed;
    }

    internal PostcardFeatureSnapshot GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        PostcardCollectionState state = _progress.Current;
        PostcardOutingStatus outing = _progress.GetOutingStatus(now);
        IReadOnlyList<PostcardDefinition> unlockedDefinitions =
            _progress.UnlockedDefinitions;
        HashSet<string> unacknowledgedIds = state.Unlocked
            .Where(static record =>
                record.NotificationAcknowledgedAtUtc is null)
            .Select(static record => record.PostcardId)
            .ToHashSet(StringComparer.Ordinal);
        return new PostcardFeatureSnapshot(
            now,
            state.TotalInputCount,
            _progress.InputsUntilNextUnlock,
            _catalog.Count,
            unlockedDefinitions
                .Select(static postcard => postcard.Id)
                .ToArray(),
            unlockedDefinitions
                .Where(postcard => unacknowledgedIds.Contains(postcard.Id))
                .Select(static postcard => postcard.Id)
                .ToArray(),
            _unlockWindow.IsPresenting,
            _unlockWindow.CurrentPostcard?.Id,
            _galleryWindow?.IsVisible == true,
            GalleryPostcardId: null)
        {
            OutingPhase = outing.Phase,
            OutingStartedAtUtc = outing.StartedAtUtc,
            OutingReadyAtUtc = outing.ReadyAtUtc,
            OutingArrivedAtUtc = outing.ArrivedAtUtc,
            OutingRemainingSeconds = checked((long)Math.Ceiling(
                Math.Max(0, outing.Remaining.TotalSeconds))),
            NextPostcardId = outing.NextPostcardId,
        };
    }

    internal IReadOnlyList<PostcardDiaryUnlockSnapshot>
        GetDiaryUnlocks()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var definitions = _catalog.Definitions.ToDictionary(
            static postcard => postcard.Id,
            StringComparer.Ordinal);
        return _progress.Current.Unlocked
            .Where(record => definitions.ContainsKey(record.PostcardId))
            .Select(record =>
            {
                PostcardDefinition definition = definitions[record.PostcardId];
                return new PostcardDiaryUnlockSnapshot(
                    record.PostcardId,
                    definition.Title,
                    definition.Location,
                    record.UnlockedAtUtc);
            })
            .OrderBy(static record => record.UnlockedAtUtc)
            .ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        VerifyAccess();
        _presentationVisible = false;
        PostcardDefinition? deferred = _unlockWindow.Defer();
        if (deferred is not null && !_currentPopupIsTestPreview)
        {
            _revealQueue.AddFirst(deferred);
        }
        _currentPopupIsTestPreview = false;

        _outingTimer.Stop();
        _outingTimer.Tick -= OnOutingTimerTick;
        _petWindow.TravelRequested -= OnTravelRequested;
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimerTick;

        if (!TryPersist())
        {
            Debug.WriteLine(
                "GuluPet postcard progress could not be flushed during exit.");
        }

        _disposed = true;
        _unlockWindow.Dismissed -= OnPostcardDismissed;
        _unlockWindow.Close();
        if (_galleryWindow is not null)
        {
            _galleryWindow.Closed -= OnGalleryClosed;
            _galleryWindow.Close();
            _galleryWindow = null;
        }
    }

    private void OnTravelRequested(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        VerifyAccess();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        PostcardOutingStatus status = _progress.GetOutingStatus(now);
        bool durable = true;
        switch (status.Phase)
        {
            case PostcardOutingPhase.AtHome:
                durable = TryStartOutingDurably(now);
                break;

            case PostcardOutingPhase.Traveling:
                durable = AdvanceOutingAndPersist(now);
                break;

            case PostcardOutingPhase.WaitingAtDoor:
                durable = TryClaimWaitingPostcard(now);
                break;

            case PostcardOutingPhase.CollectionComplete:
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown postcard outing phase '{status.Phase}'.");
        }

        if (durable && status.Phase is PostcardOutingPhase.AtHome
            or PostcardOutingPhase.WaitingAtDoor)
        {
            UserInteractionKind kind = status.Phase
                == PostcardOutingPhase.AtHome
                    ? UserInteractionKind.OutingStart
                    : UserInteractionKind.PostcardClaim;
            UserInteractionAccepted?.Invoke(
                this,
                new UserInteractionAcceptedEventArgs(kind, now));
        }

        RefreshOutingUi(now, durable);
    }

    private void OnOutingTimerTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool durable = AdvanceOutingAndPersist(now);
        RefreshOutingUi(now, durable);
    }

    private bool AdvanceOutingAndPersist(DateTimeOffset now)
    {
        PostcardOutingStatus status = _progress.GetOutingStatus(now);
        if (status.Phase == PostcardOutingPhase.Traveling
            && status.Remaining <= TimeSpan.Zero)
        {
            PostcardOutingUpdate arrived = _progress.MarkArrived(now);
            if (arrived.Changed)
            {
                _progressDirty = true;
            }
        }

        return !_progressDirty || TryPersist();
    }

    private bool TryClaimWaitingPostcard(DateTimeOffset claimedAtUtc)
    {
        if (_store is null && !_isolatedTestInstance)
        {
            Debug.WriteLine(
                "Cannot claim a GuluPet postcard without a durable store.");
            return false;
        }

        PostcardClaimPlan plan = _progress.PreviewClaimArrival(claimedAtUtc);
        Action<PostcardCollectionState> persist = _store is null
            ? static _ => { }
            : _store.Save;
        bool committed = TryPersistAndCommitClaim(
            _progress,
            plan,
            persist,
            postcard => _revealQueue.AddLast(postcard));
        if (!committed)
        {
            return false;
        }

        _progressDirty = false;
        RefreshGallery();
        TryShowNextReveal();
        RaiseStateChanged();
        return true;
    }

    private bool TryStartOutingDurably(DateTimeOffset startedAtUtc)
    {
        if (_store is null && !_isolatedTestInstance)
        {
            Debug.WriteLine(
                "Cannot start a GuluPet postcard outing without a durable store.");
            return false;
        }

        Action<PostcardCollectionState> persist = _store is null
            ? static _ => { }
            : _store.Save;
        bool committed = TryPersistAndCommitOutingStart(
            _progress,
            startedAtUtc,
            persist,
            out _);
        if (committed)
        {
            _progressDirty = false;
        }

        return committed;
    }

    internal static bool TryPersistAndCommitOutingStart(
        PostcardProgressTracker tracker,
        DateTimeOffset startedAtUtc,
        Action<PostcardCollectionState> persist,
        out PostcardOutingUpdate? update)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(persist);
        PostcardOutingStartPlan plan = tracker.PreviewStartOuting(startedAtUtc);
        try
        {
            persist(plan.CandidateState);
        }
        catch (Exception exception)
            when (IsPersistenceFailure(exception))
        {
            Debug.WriteLine(
                $"Unable to persist GuluPet postcard outing start: {exception}");
            update = null;
            return false;
        }

        update = tracker.CommitStartOuting(plan);
        return true;
    }

    internal static bool TryPersistAndCommitClaim(
        PostcardProgressTracker tracker,
        PostcardClaimPlan plan,
        Action<PostcardCollectionState> persist,
        Action<PostcardDefinition> publish)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(persist);
        ArgumentNullException.ThrowIfNull(publish);
        try
        {
            persist(plan.CandidateState);
        }
        catch (Exception exception)
            when (IsPersistenceFailure(exception))
        {
            Debug.WriteLine(
                $"Unable to persist GuluPet postcard claim: {exception}");
            return false;
        }

        PostcardClaimResult committed = tracker.CommitClaimArrival(plan);
        publish(committed.Postcard);
        return true;
    }

    private void RefreshOutingUi(
        DateTimeOffset now,
        bool waitingStateIsDurable = true)
    {
        PostcardOutingStatus status = _progress.GetOutingStatus(now);
        if (status.Phase == PostcardOutingPhase.AtHome &&
            !waitingStateIsDurable)
        {
            UpdateGalleryOutingStatus(status);
            _petWindow.SetTravelActionState(
                "去逛逛",
                countdown: null,
                isEnabled: true,
                tooltip: "咕噜刚才没能收好行囊，再点一次试试吧");
            return;
        }

        if (status.Phase == PostcardOutingPhase.WaitingAtDoor
            && !waitingStateIsDurable)
        {
            UpdateGalleryOutingStatus(status with
            {
                Phase = PostcardOutingPhase.Traveling,
                ArrivedAtUtc = null,
                Remaining = TimeSpan.Zero,
            });
            _petWindow.SetTravelActionState(
                "在外面逛逛",
                "00:00",
                isEnabled: false,
                tooltip: "咕噜还在门口放好明信片，再等一小会儿");
            return;
        }

        UpdateGalleryOutingStatus(status);
        switch (status.Phase)
        {
            case PostcardOutingPhase.AtHome:
                _petWindow.SetTravelActionState(
                    "去逛逛",
                    countdown: null,
                    isEnabled: true,
                    tooltip: "出去逛逛，大约半小时后带一张新明信片回家");
                break;

            case PostcardOutingPhase.Traveling:
                _petWindow.SetTravelActionState(
                    "在外面逛逛",
                    FormatRemaining(status.Remaining),
                    isEnabled: false,
                    tooltip: status.ReadyAtUtc is { } readyAtUtc
                        ? $"预计 {readyAtUtc.ToLocalTime():HH:mm} 回到家门口"
                        : "咕噜正在外面逛逛");
                break;

            case PostcardOutingPhase.WaitingAtDoor:
                _petWindow.SetTravelActionState(
                    "在家门口",
                    countdown: null,
                    isEnabled: true,
                    tooltip: "点击收下咕噜带回的新明信片");
                break;

            case PostcardOutingPhase.CollectionComplete:
                _petWindow.SetTravelActionState(
                    "一路收好",
                    countdown: null,
                    isEnabled: false,
                    tooltip: "咕噜带回的风景都收好啦");
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown postcard outing phase '{status.Phase}'.");
        }
    }

    internal static string FormatRemaining(TimeSpan remaining)
    {
        double boundedSeconds = Math.Clamp(
            Math.Ceiling(remaining.TotalSeconds),
            0,
            PostcardOutingRecord.Duration.TotalSeconds);
        int totalSeconds = checked((int)boundedSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private void OnSaveTimerTick(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            bool persisted = TryPersist();
            RefreshOutingUi(_timeProvider.GetUtcNow(), persisted);
        }
    }

    private bool TryPersist()
    {
        if (_progressDirty)
        {
            if (_store is null && !_isolatedTestInstance)
            {
                return false;
            }

            try
            {
                _store?.Save(_progress.Current);
                _progressDirty = false;
            }
            catch (Exception exception)
                when (IsPersistenceFailure(exception))
            {
                Debug.WriteLine(
                    $"Unable to save GuluPet postcard progress: {exception}");
                return false;
            }
        }

        bool collectionChanged = false;
        while (_revealsAwaitingPersistence.TryDequeue(
                   out PostcardDefinition? postcard))
        {
            _revealQueue.AddLast(postcard);
            collectionChanged = true;
        }

        if (collectionChanged)
        {
            RefreshGallery();
        }
        else
        {
            UpdateGalleryOutingStatus();
        }

        TryShowNextReveal();
        RaiseStateChanged();
        return true;
    }

    private void OnPostcardDismissed(
        object? sender,
        PostcardDismissedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        bool dismissedTestPreview = _currentPopupIsTestPreview;
        _currentPopupIsTestPreview = false;
        PostcardCollectionState current = _progress.Current;
        if (!dismissedTestPreview
            && e.IsTravelEcho
            && current.PendingTravelEcho is { } pendingTravelEcho
            && IsTravelEchoForTotal(
                e.Postcard,
                pendingTravelEcho.TotalStampCount))
        {
            DateTimeOffset acknowledgedAtUtc = _timeProvider.GetUtcNow();
            if (pendingTravelEcho.CreatedAtUtc > acknowledgedAtUtc)
            {
                acknowledgedAtUtc = pendingTravelEcho.CreatedAtUtc;
            }

            _progress.AcknowledgeTravelEcho(
                pendingTravelEcho.TotalStampCount,
                acknowledgedAtUtc);
            _progressDirty = true;
            _ = TryPersist();
        }
        else if (!dismissedTestPreview
            && ShouldAcknowledgeCatalogSummary(e, current))
        {
            DateTimeOffset acknowledgedAtUtc = _timeProvider.GetUtcNow();
            if (current.PendingCatalogSummary!.CreatedAtUtc
                > acknowledgedAtUtc)
            {
                acknowledgedAtUtc =
                    current.PendingCatalogSummary.CreatedAtUtc;
            }

            _progress.AcknowledgeCatalogSummary(acknowledgedAtUtc);
            _progressDirty = true;
            _ = TryPersist();
        }
        else if (!dismissedTestPreview
                 && ShouldAcknowledgeDismissal(e, current))
        {
            PostcardUnlockRecord record = current.Unlocked.Single(item =>
                string.Equals(
                    item.PostcardId,
                    e.Postcard.Id,
                    StringComparison.Ordinal));
            DateTimeOffset acknowledgedAtUtc = _timeProvider.GetUtcNow();
            if (record.UnlockedAtUtc > acknowledgedAtUtc)
            {
                acknowledgedAtUtc = record.UnlockedAtUtc;
            }

            _progress.AcknowledgeNotification(
                e.Postcard.Id,
                acknowledgedAtUtc);
            _progressDirty = true;
            _ = TryPersist();
        }

        TryShowNextReveal();
        RaiseStateChanged();
    }

    private void TryShowNextReveal()
    {
        if (_disposed
            || !_presentationVisible
            || _unlockWindow.IsPresenting
            || _revealQueue.First is not { } first)
        {
            return;
        }

        _currentPopupIsTestPreview = false;
        _unlockWindow.ShowPostcard(
            first.Value,
            _catalog.Count,
            _petWindow);
        _revealQueue.RemoveFirst();
        RaiseStateChanged();
    }

    private void EnsureGallery()
    {
        if (_galleryWindow is not null)
        {
            return;
        }

        _galleryWindow = new PostcardGalleryWindow();
        _galleryWindow.Closed += OnGalleryClosed;
    }

    private void OnGalleryClosed(object? sender, EventArgs e)
    {
        if (_galleryWindow is not null)
        {
            _galleryWindow.Closed -= OnGalleryClosed;
            _galleryWindow = null;
        }

        RaiseStateChanged();
    }

    private void RefreshGallery()
    {
        if (_galleryWindow is null)
        {
            return;
        }

        _galleryWindow.SetPostcards(
            _progress.UnlockedDefinitions,
            _catalog.Definitions,
            _progress.GetOutingStatus(_timeProvider.GetUtcNow()));
    }

    private void UpdateGalleryOutingStatus(
        PostcardOutingStatus? status = null)
    {
        if (_galleryWindow is not null)
        {
            _galleryWindow.SetOutingStatus(
                status ?? _progress.GetOutingStatus(
                    _timeProvider.GetUtcNow()));
        }
    }

    private void EnsureOutingTestControl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        if (!_isolatedTestInstance)
        {
            throw new InvalidOperationException(
                "Postcard outing test controls require an isolated instance.");
        }
    }

    internal static PostcardDefinition CreateTravelEcho(
        long newlyEarnedStampCount,
        long totalStampCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            newlyEarnedStampCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalStampCount);
        if (newlyEarnedStampCount > totalStampCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newlyEarnedStampCount),
                "New travel stamps cannot exceed the total stamp count.");
        }

        return new(
            $"travel-echo-{totalStampCount}",
            $"咕噜又带回 {newlyEarnedStampCount:N0} 枚小印章",
            $"一路已经收好 {totalStampCount:N0} 枚",
            string.Empty,
            0,
            $"咕噜这次又带回 {newlyEarnedStampCount:N0} 枚小印章，" +
            $"一路已经有 {totalStampCount:N0} 枚了。",
            "咕噜的小收获");
    }

    internal static PostcardDefinition CreateCatalogBatchSummary(
        PostcardCatalogSummaryRecord summary) =>
        new(
            "catalog-batch-summary",
            $"咕噜整理好了 {summary.PostcardIds.Count:N0} 张明信片",
            $"明信片册已经有 {summary.TotalUnlockedCount:N0} 张",
            string.Empty,
            0,
            $"咕噜把 {summary.PostcardIds.Count:N0} 张旧旅途一起放进了明信片册，" +
            $"现在已经收好 {summary.TotalUnlockedCount:N0} 张。",
            "旅行手账添了新页");

    internal static IReadOnlyList<PostcardDefinition> CreateStartupRevealPlan(
        PostcardCatalog catalog,
        PostcardCollectionState state,
        bool includeCatalogSummary = true)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        PostcardCollectionState snapshot =
            PostcardCollectionState.ValidateAndSnapshot(state);
        var pendingSummaryIds = snapshot.PendingCatalogSummary?.PostcardIds
            .ToHashSet(StringComparer.Ordinal)
            ?? [];
        IReadOnlyDictionary<string, PostcardUnlockRecord> records =
            snapshot.Unlocked.ToDictionary(
                static record => record.PostcardId,
                StringComparer.Ordinal);
        var plan = catalog.GetUnlockedDefinitions(snapshot)
            .Where(postcard =>
                records[postcard.Id].NotificationAcknowledgedAtUtc is null
                && !pendingSummaryIds.Contains(postcard.Id))
            .ToList();
        if (includeCatalogSummary
            && snapshot.PendingCatalogSummary is { } summary)
        {
            plan.Add(CreateCatalogBatchSummary(summary));
        }

        if (snapshot.PendingTravelEcho is { } travelEcho)
        {
            plan.Add(CreateTravelEcho(
                travelEcho.NewlyEarnedStampCount,
                travelEcho.TotalStampCount));
        }

        return plan.ToArray();
    }

    internal static bool ShouldAcknowledgeDismissal(
        PostcardDismissedEventArgs dismissed,
        PostcardCollectionState state) =>
        !dismissed.IsTravelEcho
        && !dismissed.IsCatalogSummary
        && state.Unlocked.Any(record => string.Equals(
            record.PostcardId,
            dismissed.Postcard.Id,
            StringComparison.Ordinal));

    internal static bool ShouldAcknowledgeCatalogSummary(
        PostcardDismissedEventArgs dismissed,
        PostcardCollectionState state) =>
        dismissed.IsCatalogSummary
        && !dismissed.Automatic
        && state.PendingCatalogSummary is not null;

    private void ReplacePendingCatalogSummaryReveal()
    {
        PostcardCatalogSummaryRecord summary =
            _progress.Current.PendingCatalogSummary
            ?? throw new InvalidOperationException(
                "Catalog reconciliation did not create a summary.");
        if (_unlockWindow.CurrentPostcard is { } current
            && PostcardUnlockWindow.IsCatalogSummary(current)
            && !_currentPopupIsTestPreview)
        {
            _ = _unlockWindow.Defer();
            _currentPopupIsTestPreview = false;
        }

        LinkedListNode<PostcardDefinition>? node = _revealQueue.First;
        while (node is not null)
        {
            LinkedListNode<PostcardDefinition>? next = node.Next;
            if (PostcardUnlockWindow.IsCatalogSummary(node.Value))
            {
                _revealQueue.Remove(node);
            }

            node = next;
        }

        int awaitingCount = _revealsAwaitingPersistence.Count;
        for (var index = 0; index < awaitingCount; index++)
        {
            PostcardDefinition pending =
                _revealsAwaitingPersistence.Dequeue();
            if (!PostcardUnlockWindow.IsCatalogSummary(pending))
            {
                _revealsAwaitingPersistence.Enqueue(pending);
            }
        }

        _revealsAwaitingPersistence.Enqueue(
            CreateCatalogBatchSummary(summary));
    }

    private void ReplacePendingTravelEchoReveal()
    {
        PostcardTravelEchoRecord echo = _progress.Current.PendingTravelEcho
            ?? throw new InvalidOperationException(
                "Travel stamp progress did not create a pending echo.");
        if (_unlockWindow.CurrentPostcard is { } current
            && PostcardUnlockWindow.IsTravelEcho(current)
            && !_currentPopupIsTestPreview)
        {
            _ = _unlockWindow.Defer();
            _currentPopupIsTestPreview = false;
        }

        LinkedListNode<PostcardDefinition>? node = _revealQueue.First;
        while (node is not null)
        {
            LinkedListNode<PostcardDefinition>? next = node.Next;
            if (PostcardUnlockWindow.IsTravelEcho(node.Value))
            {
                _revealQueue.Remove(node);
            }

            node = next;
        }

        int awaitingCount = _revealsAwaitingPersistence.Count;
        for (var index = 0; index < awaitingCount; index++)
        {
            PostcardDefinition pending =
                _revealsAwaitingPersistence.Dequeue();
            if (!PostcardUnlockWindow.IsTravelEcho(pending))
            {
                _revealsAwaitingPersistence.Enqueue(pending);
            }
        }

        _revealsAwaitingPersistence.Enqueue(
            CreateTravelEcho(
                echo.NewlyEarnedStampCount,
                echo.TotalStampCount));
    }

    private static bool IsTravelEchoForTotal(
        PostcardDefinition postcard,
        long totalStampCount) =>
        string.Equals(
            postcard.Id,
            $"travel-echo-{totalStampCount}",
            StringComparison.Ordinal);

    private void RaiseStateChanged() =>
        StateChanged?.Invoke(this, EventArgs.Empty);

    private void VerifyAccess() => _dispatcher.VerifyAccess();

    private static bool TryQuarantine(PostcardProgressStore store)
    {
        string statePath = store.StatePath;
        if (!File.Exists(statePath))
        {
            return true;
        }

        try
        {
            string directory = Path.GetDirectoryName(statePath)
                ?? throw new InvalidOperationException(
                    "The postcard state path has no parent directory.");
            string fileName =
                $"postcards.corrupt-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-" +
                $"{Guid.NewGuid():N}.json";
            File.Move(
                statePath,
                Path.Combine(directory, fileName));
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or InvalidOperationException)
        {
            Debug.WriteLine(
                $"Unable to preserve unreadable postcard progress: {exception}");
            return false;
        }
    }

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException;

}
