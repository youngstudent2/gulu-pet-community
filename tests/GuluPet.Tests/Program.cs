using GuluPet.Tests;

try
{
    if (args.Contains(
            "--update-only",
            StringComparer.OrdinalIgnoreCase))
    {
        ManualUpdateServiceTests.RunAll();
        UpdateProgressWindowMotionTests.RunAll();
        Console.WriteLine("All GuluPet update tests passed.");
        return 0;
    }

    if (args.Contains(
            "--relationship-only",
            StringComparer.OrdinalIgnoreCase))
    {
        EmotionStateTests.RunAll();
        RelationshipStateStoreTests.RunAll();
        BehaviorRuntimeCoordinatorTests.RunRelationshipOnly();
        PetControllerTests.RunRelationshipOnly();
        Console.WriteLine("All GuluPet relationship tests passed.");
        return 0;
    }

    if (args.Contains(
            "--behavior-catalog-only",
            StringComparer.OrdinalIgnoreCase))
    {
        BehaviorCatalogTests.RunAll();
        BehaviorCatalogDataTests.RunAll();
        CatDialogueAnnotationTests.RunAll();
        DialogueSelectorTests.RunAll();
        Console.WriteLine("All GuluPet behavior catalog tests passed.");
        return 0;
    }

    if (args.Contains(
            "--dialogue-only",
            StringComparer.OrdinalIgnoreCase))
    {
        BehaviorCatalogDataTests.RunAll();
        CatDialogueAnnotationTests.RunAll();
        DialogueSelectorTests.RunAll();
        BubbleArbiterTests.RunAll();
        RuntimeDataTests.RunAll();
        Console.WriteLine("All GuluPet dialogue tests passed.");
        return 0;
    }

    if (args.Contains(
            "--runtime-context-only",
            StringComparer.OrdinalIgnoreCase))
    {
        RuntimeContextMonitorTests.RunAll();
        PetControllerTests.RunRuntimeContextOnly();
        Console.WriteLine("All GuluPet runtime context tests passed.");
        return 0;
    }

    if (args.Contains(
            "--behavior-runtime-only",
            StringComparer.OrdinalIgnoreCase))
    {
        EmotionStateTests.RunAll();
        RelationshipStateStoreTests.RunAll();
        ClickBurstClassifierTests.RunAll();
        BehaviorSessionRunnerTests.RunAll();
        SoftBehaviorQueueTests.RunAll();
        BehaviorRuntimeCoordinatorTests.RunAll();
        BehaviorPresentationPortTests.RunAll();
        BehaviorTickLogWriterTests.RunAll();
        TestControlProtocolTests.RunAll();
        TestControlStartupOptionsTests.RunAll();
        SingleInstanceServiceTests.RunAll();
        TestCliBehaviorTests.RunAll();
        PetControllerTests.RunClickClassificationOnly();
        PetControllerTests.RunRelationshipOnly();
        Console.WriteLine("All GuluPet behavior runtime tests passed.");
        return 0;
    }

    if (args.Contains(
            "--behavior-utility-only",
            StringComparer.OrdinalIgnoreCase))
    {
        BehaviorUtilityEngineTests.RunAll();
        Console.WriteLine("All GuluPet behavior utility tests passed.");
        return 0;
    }

    if (args.Contains(
            "--pointer-gesture-only",
            StringComparer.OrdinalIgnoreCase))
    {
        PointerGestureRecognizerTests.RunAll();
        Console.WriteLine("All GuluPet pointer gesture tests passed.");
        return 0;
    }

    if (args.Contains(
            "--animation-lazy-only",
            StringComparer.OrdinalIgnoreCase))
    {
        AnimationLazyLoadingTests.RunAll();
        Console.WriteLine("All GuluPet animation lazy-loading tests passed.");
        return 0;
    }

    if (args.Contains(
            "--test-cli-only",
            StringComparer.OrdinalIgnoreCase))
    {
        TestCliBehaviorTests.RunAll();
        Console.WriteLine("All GuluPet test CLI tests passed.");
        return 0;
    }

    if (args.Contains(
            "--memory-preflight-only",
            StringComparer.OrdinalIgnoreCase))
    {
        MemoryMediaPreflightTests.RunAll();
        Console.WriteLine("All GuluPet memory preflight tests passed.");
        return 0;
    }

    if (args.Contains(
            "--runtime-content-only",
            StringComparer.OrdinalIgnoreCase))
    {
        RuntimeContentContractTests.RunAll();
        Console.WriteLine("All GuluPet runtime content tests passed.");
        return 0;
    }

    if (args.Contains(
            "--memory-progress-only",
            StringComparer.OrdinalIgnoreCase))
    {
        MemoryCatalogTests.RunAll();
        MemoryMediaPreflightTests.RunAll();
        MemoryProgressTrackerTests.RunAll();
        MemoryProgressStoreTests.RunAll();
        MemoryFeatureControllerTests.RunAll();
        MemoryGalleryWindowTests.RunAll();
        Console.WriteLine("All GuluPet memory progress tests passed.");
        return 0;
    }

    if (args.Contains(
            "--care-only",
            StringComparer.OrdinalIgnoreCase))
    {
        CareStateTrackerTests.RunAll();
        CareStateStoreTests.RunAll();
        PetCareFeatureControllerTests.RunAll();
        CareInteractionPresentationTests.RunAll();
        Console.WriteLine("All GuluPet care tests passed.");
        return 0;
    }

    if (args.Contains(
            "--accessory-content-only",
            StringComparer.OrdinalIgnoreCase))
    {
        EyeAccessoryContentTests.RunAll();
        Console.WriteLine("All GuluPet eye accessory content tests passed.");
        return 0;
    }

    if (args.Contains(
            "--accessory-only",
            StringComparer.OrdinalIgnoreCase))
    {
        EyeAccessoryContentTests.RunAll();
        WardrobeMenuStateTests.RunAll();
        WardrobePopupMotionTests.RunAll();
        Console.WriteLine("All GuluPet eye accessory tests passed.");
        return 0;
    }

    if (args.Contains(
            "--postcard-only",
            StringComparer.OrdinalIgnoreCase))
    {
        PostcardCatalogTests.RunAll();
        PostcardProgressTrackerTests.RunAll();
        PostcardProgressStoreTests.RunAll();
        PostcardAppIntegrationTests.RunAll();
        PostcardGalleryWindowTests.RunAll();
        GuluControlThemeTests.RunAll();
        Console.WriteLine("All GuluPet postcard tests passed.");
        return 0;
    }

    if (args.Contains(
            "--presentation-chrome-only",
            StringComparer.OrdinalIgnoreCase))
    {
        GuluControlThemeTests.RunAll();
        Console.WriteLine("All GuluPet presentation chrome tests passed.");
        return 0;
    }

    if (args.Contains(
            "--side-dock-only",
            StringComparer.OrdinalIgnoreCase))
    {
        SideDockPlacementTests.RunAll();
        SideDockPresentationTests.RunAll();
        PetControllerTests.RunSideDockOnly();
        Console.WriteLine("All GuluPet side-dock tests passed.");
        return 0;
    }

    if (args.Contains(
            "--diary-only",
            StringComparer.OrdinalIgnoreCase))
    {
        DiaryDatePolicyTests.RunAll();
        DiaryStateStoreTests.RunAll();
        DiaryGenerationCoordinatorTests.RunAll();
        HttpDiaryClientTests.RunAll();
        SettingsWindowContractTests.RunAll();
        LocalErrorLogTests.RunAll();
        LogReportCredentialStoreTests.RunAll();
        PendingErrorReportStoreTests.RunAll();
        ReportedErrorEventStoreTests.RunAll();
        ErrorLogReportUploaderTests.RunAll();
        DiaryWindowTests.RunAll();
        GuluControlThemeTests.RunAll();
        Console.WriteLine("All GuluPet diary tests passed.");
        return 0;
    }

    if (args.Contains(
            "--copy-only",
            StringComparer.OrdinalIgnoreCase))
    {
        SettingsWindowContractTests.RunAll();
        DiaryWindowTests.RunAll();
        BehaviorBrowserWindowTests.RunAll();
        NavigationMenuPresentationTests.RunAll();
        AuxiliarySurfacePolicyTests.RunAll();
        UserFacingCopyContractTests.RunAll();
        Console.WriteLine("All GuluPet user-facing copy tests passed.");
        return 0;
    }

    if (args.Contains(
            "--windows-compatibility-only",
            StringComparer.OrdinalIgnoreCase))
    {
        WindowsCompatibilityContractTests.RunAll();
        Console.WriteLine("All GuluPet Windows compatibility tests passed.");
        return 0;
    }

    if (args.Contains(
            "--release-contract-only",
            StringComparer.OrdinalIgnoreCase))
    {
        WindowsCompatibilityContractTests.RunAll();
        UserFacingCopyContractTests.RunAll();
        Console.WriteLine("All GuluPet release contract tests passed.");
        return 0;
    }

    if (args.Contains(
            "--memory-window-placement-only",
            StringComparer.OrdinalIgnoreCase))
    {
        WindowPlacementTests.RunMemoryWindowPlacementOnly();
        Console.WriteLine(
            "All GuluPet memory window placement tests passed.");
        return 0;
    }

    if (args.Contains(
            "--window-placement-only",
            StringComparer.OrdinalIgnoreCase))
    {
        WindowPlacementTests.RunAll();
        WardrobePopupPlacementTests.RunAll();
        SideDockPlacementTests.RunAll();
        SideDockPresentationTests.RunAll();
        BubblePlacementTests.RunAll();
        BubbleMessageLayoutTests.RunAll();
        UserSettingsTests.RunAll();
        Console.WriteLine("All GuluPet window placement tests passed.");
        return 0;
    }

    EmotionStateTests.RunAll();
    RelationshipStateStoreTests.RunAll();
    BehaviorCatalogTests.RunAll();
    BehaviorCatalogDataTests.RunAll();
    CatDialogueAnnotationTests.RunAll();
    DialogueSelectorTests.RunAll();
    HierarchicalPetStateMachineTests.RunAll();
    ClickBurstClassifierTests.RunAll();
    BehaviorSessionRunnerTests.RunAll();
    SoftBehaviorQueueTests.RunAll();
    BehaviorUtilityEngineTests.RunAll();
    BehaviorContextTriggerRouterTests.RunAll();
    ActiveTickSchedulerTests.RunAll();
    BubbleArbiterTests.RunAll();
    BehaviorEventJournalTests.RunAll();
    BehaviorTickLogWriterTests.RunAll();
    BehaviorRuntimeCoordinatorTests.RunAll();
    BehaviorPresentationPortTests.RunAll();
    AnimationTimelineTests.RunAll();
    AnimationLazyLoadingTests.RunAll();
    RuntimeContentContractTests.RunAll();
    RuntimeDataTests.RunAll();
    ManualUpdateServiceTests.RunAll();
    UpdateProgressWindowMotionTests.RunAll();
    EyeAccessoryContentTests.RunAll();
    WardrobeMenuStateTests.RunAll();
    WardrobePopupMotionTests.RunAll();
    PostcardCatalogTests.RunAll();
    PostcardProgressTrackerTests.RunAll();
    PostcardProgressStoreTests.RunAll();
    PostcardGalleryWindowTests.RunAll();
    MemoryCatalogTests.RunAll();
    MemoryMediaPreflightTests.RunAll();
    MemoryProgressTrackerTests.RunAll();
    MemoryProgressStoreTests.RunAll();
    MemoryFeatureControllerTests.RunAll();
    MemoryGalleryWindowTests.RunAll();
    CareStateTrackerTests.RunAll();
    CareStateStoreTests.RunAll();
    PetCareFeatureControllerTests.RunAll();
    CareInteractionPresentationTests.RunAll();
    WindowPlacementTests.RunAll();
    WardrobePopupPlacementTests.RunAll();
    SideDockPlacementTests.RunAll();
    SideDockPresentationTests.RunAll();
    BubblePlacementTests.RunAll();
    BubbleMessageLayoutTests.RunAll();
    UserSettingsTests.RunAll();
    StartupPresentationGateTests.RunAll();
    PostcardAppIntegrationTests.RunAll();
    DiaryDatePolicyTests.RunAll();
    DiaryStateStoreTests.RunAll();
    DiaryGenerationCoordinatorTests.RunAll();
    HttpDiaryClientTests.RunAll();
    SettingsWindowContractTests.RunAll();
    LocalErrorLogTests.RunAll();
    LogReportCredentialStoreTests.RunAll();
    PendingErrorReportStoreTests.RunAll();
    ReportedErrorEventStoreTests.RunAll();
    ErrorLogReportUploaderTests.RunAll();
    DiaryWindowTests.RunAll();
    GuluControlThemeTests.RunAll();
    BehaviorBrowserWindowTests.RunAll();
    NavigationMenuPresentationTests.RunAll();
    AuxiliarySurfacePolicyTests.RunAll();
    UserFacingCopyContractTests.RunAll();
    WindowsCompatibilityContractTests.RunAll();
    PetControllerTests.RunAll();
    RuntimeContextMonitorTests.RunAll();
    DragAnimationSelectorTests.RunAll();
    PointerGestureRecognizerTests.RunAll();
    TestControlProtocolTests.RunAll();
    TestControlStartupOptionsTests.RunAll();
    SingleInstanceServiceTests.RunAll();
    TestCliBehaviorTests.RunAll();
    Console.WriteLine("All GuluPet tests passed.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"TEST FAILURE: {exception.Message}");
    Console.Error.WriteLine(exception);
    return 1;
}
