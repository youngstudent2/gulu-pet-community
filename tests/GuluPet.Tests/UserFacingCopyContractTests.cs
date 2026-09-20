using System.Text.RegularExpressions;
using GuluPet.Accessories;
using GuluPet.Behavior;
using GuluPet.Memories;
using GuluPet.Postcards;

namespace GuluPet.Tests;

internal static partial class UserFacingCopyContractTests
{
    private static readonly string[] ForbiddenTerms =
    [
        "DeepSeek",
        "API Key",
        "只读",
        "日志",
        "Tick",
        "评分日志",
        "行为预览服务",
        "ID：",
        "动画：",
        "提示词",
        "内置凭据",
        "运行时记录",
        "报告编号",
        "日志目录",
        "测试接口",
        "Top5",
        "候选资格",
        "节流",
        "冷却",
        "本地事实",
        "模型",
        "错误码",
    ];

    private static readonly string[] UnambiguousEnglishTerms =
    [
        "DeepSeek",
        "API Key",
        "Top5",
        "Tick score",
        "behavior id",
    ];

    public static void RunAll()
    {
        Run(
            nameof(XamlUsesWarmProductLanguage),
            XamlUsesWarmProductLanguage);
        Run(
            nameof(DynamicCopyDoesNotLeakImplementationDetails),
            DynamicCopyDoesNotLeakImplementationDetails);
        Run(
            nameof(DataDrivenCopyUsesWarmProductLanguage),
            DataDrivenCopyUsesWarmProductLanguage);
        Run(
            nameof(MessageBoxesDoNotExposeRawFailures),
            MessageBoxesDoNotExposeRawFailures);
        Run(
            nameof(DeveloperDiagnosticsStayOutOfOrdinaryMenus),
            DeveloperDiagnosticsStayOutOfOrdinaryMenus);
    }

    private static void XamlUsesWarmProductLanguage()
    {
        string root = FindRepositoryRoot();
        string presentationRoot = Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation");
        string[] xamlFiles = Directory.GetFiles(
            presentationRoot,
            "*.xaml",
            SearchOption.AllDirectories);

        foreach (string path in xamlFiles)
        {
            string text = File.ReadAllText(path);
            AssertNoForbiddenTerms(text, Path.GetRelativePath(root, path));
            AssertNoDashPunctuationInChineseLiterals(
                text,
                Path.GetRelativePath(root, path));
        }

        string diary = File.ReadAllText(Path.Combine(
            presentationRoot,
            "DiaryWindow.xaml"));
        string behaviors = File.ReadAllText(Path.Combine(
            presentationRoot,
            "BehaviorBrowserWindow.xaml"));
        string settings = File.ReadAllText(Path.Combine(
            presentationRoot,
            "SettingsWindow.xaml"));
        string pet = File.ReadAllText(Path.Combine(
            presentationRoot,
            "PetWindow.xaml"));
        string postcards = File.ReadAllText(Path.Combine(
            presentationRoot,
            "PostcardGalleryWindow.xaml"));
        string memory = File.ReadAllText(Path.Combine(
            presentationRoot,
            "MemoryOfferWindow.xaml"));
        BehaviorTestCheck.True(
            diary.Contains("写给妈咪", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            behaviors.Contains("咕噜的小动作", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            behaviors.Contains(
                "搜索或选择一个动作，点击即可预览。",
                StringComparison.Ordinal));
        BehaviorTestCheck.False(
            behaviors.Contains("勉强配合妈咪", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            settings.Contains("问题反馈", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            settings.Contains("发送反馈与排查信息", StringComparison.Ordinal));
        BehaviorTestCheck.False(
            pet.Contains("Header=\"陪陪咕噜\"", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            pet.Contains("x:Name=\"InteractionPopup\"", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            pet.Contains("Text=\"饱饱\"", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            pet.Contains("Text=\"水分\"", StringComparison.Ordinal));
        BehaviorTestCheck.False(
            pet.Contains("Text=\"肚子\"", StringComparison.Ordinal));
        BehaviorTestCheck.False(
            pet.Contains("Text=\"水碗\"", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            pet.Contains("Text=\"装扮\"", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            pet.Contains("Text=\"去逛逛\"", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            postcards.Contains("咕噜都会捎一张明信片回来", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            memory.Contains("我替妈咪找回来了", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            memory.Contains(
                "Style=\"{StaticResource MemoryOfferSecondaryButtonStyle}\"",
                StringComparison.Ordinal));
        BehaviorTestCheck.True(
            memory.Contains(
                "Style=\"{StaticResource MemoryOfferPrimaryButtonStyle}\"",
                StringComparison.Ordinal));
        BehaviorTestCheck.True(
            memory.Contains("IsCancel=\"True\"", StringComparison.Ordinal));
    }

    private static void DynamicCopyDoesNotLeakImplementationDetails()
    {
        string root = FindRepositoryRoot();
        string[] relativePaths =
        [
            "src/GuluPet/Presentation/DiaryWindow.xaml.cs",
            "src/GuluPet/Presentation/BehaviorBrowserWindow.xaml.cs",
            "src/GuluPet/Presentation/BehaviorTriggerDescriptionFormatter.cs",
            "src/GuluPet/Presentation/SettingsWindow.xaml.cs",
            "src/GuluPet/Presentation/PetWindow.xaml.cs",
            "src/GuluPet/Platform/TaskbarIconService.cs",
            "src/GuluPet/Diary/DiaryState.cs",
            "src/GuluPet/Postcards/PostcardGalleryProgress.cs",
            "src/GuluPet/Presentation/PostcardGalleryWindow.xaml.cs",
            "src/GuluPet/Presentation/PostcardUnlockWindow.xaml.cs",
            "src/GuluPet/Memories/MemoryMenuState.cs",
            "src/GuluPet/Runtime/MemoryMediaPreflight.cs",
            "src/GuluPet/Runtime/MemoryFeatureController.cs",
            "src/GuluPet/Runtime/PostcardFeatureController.cs",
        ];

        foreach (string relativePath in relativePaths)
        {
            string path = Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            string source = File.ReadAllText(path);
            foreach (Match match in ChineseStringLiteralRegex().Matches(source))
            {
                AssertNoForbiddenTerms(match.Value, relativePath);
                AssertNoDashPunctuation(match.Value, relativePath);
            }

            AssertNoUnambiguousEnglishTerms(source, relativePath);
        }
    }

    private static void DataDrivenCopyUsesWarmProductLanguage()
    {
        string assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets");
        BehaviorCatalog behaviors = BehaviorCatalog.LoadAsync(
                Path.Combine(assetsRoot, "Data", "behaviors.json"))
            .GetAwaiter()
            .GetResult();
        foreach (BehaviorDefinition behavior in behaviors.Definitions)
        {
            AssertWarmDataText(
                behavior.DisplayName,
                $"behavior {behavior.Id} displayName");
        }

        // Postcard and accessory packs are optional content that the
        // community package omits; validate them only when a fork ships them.
        if (Directory.Exists(Path.Combine(assetsRoot, "Postcards")))
        {
            PostcardCatalog postcards = PostcardCatalog.LoadFromAssets(assetsRoot);
            foreach (PostcardDefinition postcard in postcards.Definitions)
            {
                AssertWarmDataText(postcard.Title, $"postcard {postcard.Id} title");
                AssertWarmDataText(
                    postcard.Location,
                    $"postcard {postcard.Id} location");
                AssertWarmDataText(
                    postcard.Chapter,
                    $"postcard {postcard.Id} chapter");
                AssertWarmDataText(
                    postcard.AltText,
                    $"postcard {postcard.Id} altText");
            }
        }

        if (Directory.Exists(Path.Combine(assetsRoot, "Accessories")))
        {
            EyeAccessoryCatalog accessories =
                EyeAccessoryCatalog.LoadFromAssets(assetsRoot);
            foreach (EyeAccessoryDefinition accessory in accessories.Accessories)
            {
                AssertWarmDataText(
                    accessory.DisplayName,
                    $"eye accessory {accessory.Id} displayName");
            }
        }

        foreach (MemoryDefinition memory in MemoryCatalog.Default.Definitions)
        {
            AssertWarmDataText(memory.Title, $"memory {memory.Id} title");
        }
    }

    private static void MessageBoxesDoNotExposeRawFailures()
    {
        string root = FindRepositoryRoot();
        string appSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "App.xaml.cs"));
        BehaviorTestCheck.True(appSource.Contains(
            "当前已是最新版本",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(appSource.Contains(
            "未能检查新版本。当前版本仍可正常使用",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(appSource.Contains(
            "咕噜已经是最新的啦",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(appSource.Contains(
            "咕噜马上回来",
            StringComparison.Ordinal));
        MatchCollection calls = MessageBoxCallRegex().Matches(appSource);
        BehaviorTestCheck.True(calls.Count > 0);
        BehaviorTestCheck.Equal(
            Regex.Matches(appSource, "MessageBox\\.Show\\(").Count,
            calls.Count);
        foreach (Match call in calls)
        {
            string text = call.Value;
            BehaviorTestCheck.False(
                text.Contains("exception.Message", StringComparison.Ordinal));
            BehaviorTestCheck.False(
                text.Contains("result.ErrorMessage", StringComparison.Ordinal));
            AssertNoForbiddenTerms(text, "App.xaml.cs MessageBox");
        }
    }

    private static void DeveloperDiagnosticsStayOutOfOrdinaryMenus()
    {
        string root = FindRepositoryRoot();
        string petXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "PetWindow.xaml"));
        string taskbar = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Platform",
            "TaskbarIconService.cs"));

        string[] hiddenDiagnostics =
        [
            "TickScoreLoggingMenuItem",
            "记录 Tick 评分日志",
            "打开 Tick 评分日志",
        ];
        foreach (string value in hiddenDiagnostics)
        {
            BehaviorTestCheck.False(
                petXaml.Contains(value, StringComparison.Ordinal));
            BehaviorTestCheck.False(
                taskbar.Contains(value, StringComparison.Ordinal));
        }
    }

    private static void AssertNoForbiddenTerms(string text, string source)
    {
        foreach (string forbidden in ForbiddenTerms)
        {
            BehaviorTestCheck.False(
                text.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"{source} leaked user-facing term '{forbidden}'.");
        }
    }

    private static void AssertNoUnambiguousEnglishTerms(
        string sourceText,
        string source)
    {
        foreach (string forbidden in UnambiguousEnglishTerms)
        {
            BehaviorTestCheck.False(
                sourceText.Contains(
                    forbidden,
                    StringComparison.OrdinalIgnoreCase),
                $"{source} leaked user-facing term '{forbidden}'.");
        }
    }

    private static void AssertWarmDataText(string text, string source)
    {
        BehaviorTestCheck.True(
            !string.IsNullOrWhiteSpace(text),
            $"{source} is empty.");
        AssertNoForbiddenTerms(text, source);
        AssertNoDashPunctuation(text, source);
    }

    private static void AssertNoDashPunctuationInChineseLiterals(
        string source,
        string path)
    {
        foreach (Match match in ChineseStringLiteralRegex().Matches(source))
        {
            AssertNoDashPunctuation(match.Value, path);
        }
    }

    private static void AssertNoDashPunctuation(string text, string source)
    {
        BehaviorTestCheck.False(
            text.Contains('—') || text.Contains('–'),
            $"{source} uses mechanical dash punctuation in user-facing copy.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "GuluPetCommunity.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Unable to find the GuluPet repository root.");
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(UserFacingCopyContractTests)}.{name}");
    }

    [GeneratedRegex("(?:\\$)?\"[^\"\\r\\n]*[\\p{IsCJKUnifiedIdeographs}][^\"\\r\\n]*\"")]
    private static partial Regex ChineseStringLiteralRegex();

    [GeneratedRegex(
        "MessageBox\\.Show\\([\\s\\S]*?MessageBoxImage\\.[A-Za-z]+\\)")]
    private static partial Regex MessageBoxCallRegex();
}
