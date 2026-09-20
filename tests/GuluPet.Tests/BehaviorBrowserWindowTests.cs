using System.Runtime.ExceptionServices;
using GuluPet.Behavior;
using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class BehaviorBrowserWindowTests
{
    private static readonly string[] ForbiddenUserFacingTerms =
    [
        "ID：",
        "动画：",
        "trigger:",
        "Tick",
        "Top5",
        "评分",
        "权重",
        "冷却",
        "节流",
        "候选",
        "运行时",
        "runtime",
        "测试接口",
        "自动候选资格",
        "AND",
        "OR",
        "DeepSeek",
        "API",
        "只读",
        "—",
        "–",
    ];

    public static void RunAll()
    {
        Run(
            nameof(ListsAndFiltersEveryPackagedBehavior),
            ListsAndFiltersEveryPackagedBehavior);
        Run(
            nameof(UsesWarmChineseFamilyLabels),
            UsesWarmChineseFamilyLabels);
        Run(
            nameof(UsesThemedFamilyFilter),
            UsesThemedFamilyFilter);
        Run(
            nameof(EveryPackagedBehaviorHasFriendlyCopy),
            EveryPackagedBehaviorHasFriendlyCopy);
        Run(
            nameof(PackagedTriggersNeverLeakTags),
            PackagedTriggersNeverLeakTags);
        Run(
            nameof(UsesWarmFallbackCopy),
            UsesWarmFallbackCopy);
        Run(
            nameof(NeverEchoesInternalRejectionReasons),
            NeverEchoesInternalRejectionReasons);
    }

    private static void ListsAndFiltersEveryPackagedBehavior()
    {
        RunSta(
            () =>
            {
                BehaviorCatalog catalog = LoadPackagedCatalog();
                var window = new BehaviorBrowserWindow(catalog.Definitions);
                try
                {
                    BehaviorTestCheck.Equal(
                        catalog.Definitions.Count,
                        window.VisibleBehaviorCount);

                    window.ApplyFilterForTest("安静陪在身边");
                    BehaviorTestCheck.Equal(1, window.VisibleBehaviorCount);

                    window.ApplyFilterForTest(
                        string.Empty,
                        "安静陪伴");
                    BehaviorTestCheck.Equal(1, window.VisibleBehaviorCount);

                    window.ApplyFilterForTest("wake_slow_blink");
                    BehaviorTestCheck.Equal(0, window.VisibleBehaviorCount);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void UsesWarmChineseFamilyLabels()
    {
        RunSta(
            () =>
            {
                BehaviorCatalog catalog = LoadPackagedCatalog();
                var window = new BehaviorBrowserWindow(catalog.Definitions);
                try
                {
                    string[] expectedLabels =
                    [
                        "全部小动作",
                        "安静陪伴",
                    ];
                    BehaviorTestCheck.SequenceEqual(
                        expectedLabels,
                        window.FamilyFilterLabelsForTest);

                    window.ApplyFilterForTest(
                        string.Empty,
                        "安静陪伴");
                    BehaviorTestCheck.Equal(1, window.VisibleBehaviorCount);
                    BehaviorTestCheck.True(
                        window.VisibleDetailsForTest.All(detail =>
                            string.Equals(
                                detail,
                                "「安静陪伴」里的小动作",
                                StringComparison.Ordinal)));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void UsesThemedFamilyFilter()
    {
        RunSta(
            () =>
            {
                BehaviorCatalog catalog = LoadPackagedCatalog();
                var window = new BehaviorBrowserWindow(catalog.Definitions);
                try
                {
                    var expectedStyle = BehaviorTestCheck.NotNull(
                        window.Resources["BehaviorFamilyComboBoxStyle"]
                            as System.Windows.Style);
                    BehaviorTestCheck.NotNull(
                        window.Resources["BehaviorCardTextStyle"]
                            as System.Windows.Style);
                    BehaviorTestCheck.NotNull(
                        window.Resources["BehaviorCardBadgeStyle"]
                            as System.Windows.Style);
                    BehaviorTestCheck.NotNull(
                        window.Resources["BehaviorCardBadgeTextStyle"]
                            as System.Windows.Style);
                    BehaviorTestCheck.Equal(
                        expectedStyle,
                        window.FamilyFilter.Style);
                    BehaviorTestCheck.NotNull(window.FamilyFilter.Template);
                    window.FamilyFilter.ApplyTemplate();
                    BehaviorTestCheck.NotNull(
                        window.FamilyFilter.Template.FindName(
                            "DropDownToggle",
                            window.FamilyFilter)
                        as System.Windows.Controls.Primitives.ToggleButton);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void EveryPackagedBehaviorHasFriendlyCopy()
    {
        BehaviorCatalog catalog = LoadPackagedCatalog();
        foreach (BehaviorDefinition definition in catalog.Definitions)
        {
            AssertFriendly(definition.DisplayName, definition.Id.Value);
            string description =
                BehaviorTriggerDescriptionFormatter.Describe(definition);
            BehaviorTestCheck.True(
                !string.IsNullOrWhiteSpace(description),
                $"{definition.Id} has no user-facing description.");
            AssertFriendly(description, definition.Id.Value);
        }
    }

    private static void PackagedTriggersNeverLeakTags()
    {
        BehaviorCatalog catalog = LoadPackagedCatalog();
        foreach (BehaviorDefinition definition in catalog.Definitions)
        {
            string description =
                BehaviorTriggerDescriptionFormatter.Describe(definition);
            BehaviorTestCheck.False(
                description.Contains("trigger:", StringComparison.Ordinal),
                $"{definition.Id} leaks raw trigger tags.");
        }
    }

    private static void UsesWarmFallbackCopy()
    {
        BehaviorCatalog catalog = LoadPackagedCatalog();

        string idleFallback = Describe(catalog, "idle_fallback");
        BehaviorTestCheck.True(
            idleFallback.Contains(
                "安静陪在妈咪身边",
                StringComparison.Ordinal));
    }

    private static void NeverEchoesInternalRejectionReasons()
    {
        BehaviorTestCheck.Equal(
            "先把咕噜放稳，再试一次吧",
            BehaviorBrowserWindow.DescribeRejectionForTest("dragging"));
        BehaviorTestCheck.Equal(
            "咕噜这会儿不想配合，过会儿再试吧",
            BehaviorBrowserWindow.DescribeRejectionForTest(
                "internal-secret-reason"));
        BehaviorTestCheck.False(
            BehaviorBrowserWindow.DescribeRejectionForTest(
                    "internal-secret-reason")
                .Contains(
                    "internal-secret-reason",
                    StringComparison.Ordinal));
    }

    private static void AssertFriendly(string text, string source)
    {
        foreach (string forbidden in ForbiddenUserFacingTerms)
        {
            BehaviorTestCheck.False(
                text.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"{source} leaked user-facing technical copy '{forbidden}'.");
        }
    }

    private static string Describe(
        BehaviorCatalog catalog,
        string behaviorId)
    {
        BehaviorDefinition definition = catalog[new BehaviorId(behaviorId)];
        return BehaviorTriggerDescriptionFormatter.Describe(definition);
    }

    private static BehaviorCatalog LoadPackagedCatalog() =>
        BehaviorCatalog.LoadAsync(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "Data",
                    "behaviors.json"))
            .GetAwaiter()
            .GetResult();

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    test();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(BehaviorBrowserWindowTests)}.{name}");
    }
}
