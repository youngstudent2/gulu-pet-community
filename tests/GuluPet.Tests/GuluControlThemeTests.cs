using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuluPet.Diary;
using GuluPet.Presentation;
using SkiaSharp;

namespace GuluPet.Tests;

internal static class GuluControlThemeTests
{
    public static void RunAll()
    {
        Run(
            nameof(AppMergesGlobalPetControlTheme),
            AppMergesGlobalPetControlTheme);
        Run(
            nameof(UpdateWindowUsesDeterminateThemedProgress),
            UpdateWindowUsesDeterminateThemedProgress);
        Run(
            nameof(CancelledUpdateKeepsStoppingCopyUntilTerminalStage),
            CancelledUpdateKeepsStoppingCopyUntilTerminalStage);
        Run(
            nameof(UpdateWindowKeepsCancelButtonInsideContent),
            UpdateWindowKeepsCancelButtonInsideContent);
        Run(
            nameof(PostcardGalleryUsesChapterProgressWithoutModalDetail),
            PostcardGalleryUsesChapterProgressWithoutModalDetail);
        Run(
            nameof(MemoryPlaybackUsesOpaqueVideoChrome),
            MemoryPlaybackUsesOpaqueVideoChrome);
        Run(
            nameof(PawAssetIsPackagedAndTransparent),
            PawAssetIsPackagedAndTransparent);
        Run(
            nameof(TemplatesPreserveScrollingAndExposePetMarkers),
            TemplatesPreserveScrollingAndExposePetMarkers);
        Run(
            nameof(SavesThemedDiaryPreviewWhenRequested),
            SavesThemedDiaryPreviewWhenRequested);
    }

    private static void UpdateWindowUsesDeterminateThemedProgress()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "UpdateProgressWindow.xaml"));
        string codeBehind = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "UpdateProgressWindow.xaml.cs"));

        BehaviorTestCheck.True(xaml.Contains(
            "x:Key=\"UpdateProgressStyle\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(xaml.Contains(
            "x:Key=\"UpdateCancelButtonStyle\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(xaml.Contains(
            "x:Name=\"ProgressPercentText\"",
            StringComparison.Ordinal));
        var window = BehaviorTestCheck.NotNull(XDocument.Parse(xaml).Root);
        BehaviorTestCheck.Equal("350", window.Attribute("Height")?.Value);
        BehaviorTestCheck.Equal("350", window.Attribute("MinHeight")?.Value);
        BehaviorTestCheck.Equal("350", window.Attribute("MaxHeight")?.Value);
        BehaviorTestCheck.False(xaml.Contains(
            "IsIndeterminate=\"True\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(codeBehind.Contains(
            "IsIndeterminate = true",
            StringComparison.OrdinalIgnoreCase));
    }

    private static void CancelledUpdateKeepsStoppingCopyUntilTerminalStage()
    {
        BehaviorTestCheck.False(
            UpdateProgressWindow.ShouldApplyStage(
                cancelRequested: true,
                canCancel: true,
                currentPercent: 42,
                nextPercent: 68));
        BehaviorTestCheck.True(
            UpdateProgressWindow.ShouldApplyStage(
                cancelRequested: true,
                canCancel: false,
                currentPercent: 42,
                nextPercent: 42));
        BehaviorTestCheck.False(
            UpdateProgressWindow.ShouldApplyStage(
                cancelRequested: false,
                canCancel: true,
                currentPercent: 42,
                nextPercent: 41));
        BehaviorTestCheck.True(
            UpdateProgressWindow.ShouldApplyStage(
                cancelRequested: false,
                canCancel: true,
                currentPercent: 42,
                nextPercent: null));
    }

    private static void UpdateWindowKeepsCancelButtonInsideContent()
    {
        RunSta(
            () =>
            {
                var window = new UpdateProgressWindow
                {
                    Left = -20000,
                    Top = -20000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                };
                try
                {
                    window.SetStage(
                        "正在检查新版本…",
                        "正在连接更新服务。",
                        percent: 0);
                    window.Show();
                    window.UpdateLayout();

                    var content = BehaviorTestCheck.NotNull(
                        window.Content as FrameworkElement);
                    Point buttonBottom = window.CancelButton
                        .TransformToAncestor(content)
                        .Transform(
                            new Point(
                                0,
                                window.CancelButton.ActualHeight));
                    BehaviorTestCheck.True(
                        window.CancelButton.ActualHeight >= 40);
                    BehaviorTestCheck.True(
                        buttonBottom.Y <= content.ActualHeight + 0.5);
                    SaveVisualWhenRequested(
                        window,
                        "update-progress.png");
                }
                finally
                {
                    window.CloseForCompletion();
                }
            });
    }

    private static void AppMergesGlobalPetControlTheme()
    {
        string root = FindRepositoryRoot();
        string appXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "App.xaml"));
        string themeXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "Themes",
            "GuluControls.xaml"));

        BehaviorTestCheck.True(appXaml.Contains(
            "Presentation/Themes/GuluControls.xaml",
            StringComparison.Ordinal));

        string[] requiredContracts =
        [
            "TargetType=\"{x:Type ScrollBar}\"",
            "TargetType=\"{x:Type ProgressBar}\"",
            "GuluVerticalScrollBarTemplate",
            "GuluHorizontalScrollBarTemplate",
            "PART_Track",
            "PART_Indicator",
            "ScrollBar.PageUpCommand",
            "ScrollBar.PageDownCommand",
            "ScrollBar.PageLeftCommand",
            "ScrollBar.PageRightCommand",
            "PawThumbImage",
            "PawProgressMarker",
            "SystemParameters.HighContrast",
        ];
        foreach (string contract in requiredContracts)
        {
            BehaviorTestCheck.True(
                themeXaml.Contains(contract, StringComparison.Ordinal),
                $"The global pet-control theme is missing '{contract}'.");
        }

        string behaviorXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "BehaviorBrowserWindow.xaml"));
        BehaviorTestCheck.True(behaviorXaml.Contains(
            "ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"",
            StringComparison.Ordinal));
    }

    private static void PostcardGalleryUsesChapterProgressWithoutModalDetail()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "PostcardGalleryWindow.xaml"));
        string codeBehind = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "PostcardGalleryWindow.xaml.cs"));

        BehaviorTestCheck.True(xaml.Contains(
            "x:Name=\"NextPostcardProgress\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(xaml.Contains(
            "x:Name=\"ImageOpenFeedbackBorder\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(xaml.Contains(
            "PreviewOverlay",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(xaml.Contains(
            "PreviewBackdropButtonStyle",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(codeBehind.Contains(
            "UseShellExecute = true",
            StringComparison.Ordinal));
    }

    private static void MemoryPlaybackUsesOpaqueVideoChrome()
    {
        string root = FindRepositoryRoot();
        string playbackXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "MemoryPlaybackWindow.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Parse(playbackXaml);
        XElement playbackWindow = BehaviorTestCheck.NotNull(document.Root);
        XElement windowCard = BehaviorTestCheck.NotNull(
            document.Descendants(presentation + "Border")
                .SingleOrDefault(element =>
                    (string?)element.Attribute(x + "Name") == "WindowCard"));
        XElement layoutGrid = BehaviorTestCheck.NotNull(
            windowCard.Elements(presentation + "Grid").SingleOrDefault());
        string[] rowHeights = BehaviorTestCheck.NotNull(
                layoutGrid.Element(presentation + "Grid.RowDefinitions"))
            .Elements(presentation + "RowDefinition")
            .Select(element => element.Attribute("Height")?.Value ?? string.Empty)
            .ToArray();
        XElement titleText = BehaviorTestCheck.NotNull(
            document.Descendants(presentation + "TextBlock")
                .SingleOrDefault(element =>
                    (string?)element.Attribute(x + "Name") == "TitleText"));
        XElement headerBar = BehaviorTestCheck.NotNull(
            document.Descendants(presentation + "Grid")
                .SingleOrDefault(element =>
                    (string?)element.Attribute(x + "Name") == "HeaderBar"));
        XElement playbackActionOverlay = BehaviorTestCheck.NotNull(
            document.Descendants(presentation + "Grid")
                .SingleOrDefault(element =>
                    (string?)element.Attribute(x + "Name")
                        == "PlaybackActionOverlay"));
        XElement playbackActionButton = BehaviorTestCheck.NotNull(
            playbackActionOverlay.Descendants(presentation + "Button")
                .SingleOrDefault(element =>
                    (string?)element.Attribute(x + "Name") == "PrimaryButton"));
        XElement playbackActionStyle = BehaviorTestCheck.NotNull(
            document.Descendants(presentation + "Style")
                .SingleOrDefault(element =>
                    (string?)element.Attribute(x + "Key")
                        == "MemoryPlaybackActionButtonStyle"));
        XElement actionSurface = BehaviorTestCheck.NotNull(
            playbackActionStyle.Descendants(presentation + "Border")
                .SingleOrDefault(element =>
                    (string?)element.Attribute(x + "Name") == "ActionSurface"));

        BehaviorTestCheck.True(playbackXaml.Contains(
            "AllowsTransparency=\"False\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.Equal(
            "回忆",
            playbackWindow.Attribute("Title")?.Value);
        BehaviorTestCheck.True(
            (playbackWindow.Attribute("Title")?.Value.Length ?? int.MaxValue)
                <= 3);
        BehaviorTestCheck.Equal(
            "528",
            playbackWindow.Attribute("Width")?.Value);
        BehaviorTestCheck.Equal(
            "320",
            playbackWindow.Attribute("Height")?.Value);
        BehaviorTestCheck.Close(
            528,
            MemoryPlaybackWindow.PreferredWidth);
        BehaviorTestCheck.Close(
            320,
            MemoryPlaybackWindow.PreferredHeight);
        BehaviorTestCheck.True(playbackXaml.Contains(
            "Background=\"#FFFDF7EA\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(playbackXaml.Contains(
            "platform:DwmWindowChrome.UseRoundedCorners=\"True\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(playbackXaml.Contains(
            "DropShadowEffect",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(playbackXaml.Contains(
            "ScrubbingEnabled",
            StringComparison.Ordinal));
        BehaviorTestCheck.Equal("0", windowCard.Attribute("Margin")?.Value);
        BehaviorTestCheck.Equal(
            "12,0,12,8",
            windowCard.Attribute("Padding")?.Value);
        BehaviorTestCheck.Equal(
            "0",
            windowCard.Attribute("BorderThickness")?.Value);
        BehaviorTestCheck.Equal(
            "0",
            windowCard.Attribute("CornerRadius")?.Value);
        BehaviorTestCheck.False(playbackXaml.Contains(
            "CornerRadius=\"24\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(playbackXaml.Contains(
            "x:Key=\"MemoryCommandButtonStyle\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.Equal(2, rowHeights.Length);
        BehaviorTestCheck.Equal("28", rowHeights[0]);
        BehaviorTestCheck.Equal("*", rowHeights[1]);
        BehaviorTestCheck.Equal("回忆", titleText.Attribute("Text")?.Value);
        BehaviorTestCheck.True(
            (titleText.Attribute("Text")?.Value.Length ?? int.MaxValue) <= 3);
        BehaviorTestCheck.Equal(
            1,
            headerBar.Descendants(presentation + "TextBlock").Count());
        BehaviorTestCheck.Equal(
            "56",
            playbackActionButton.Attribute("Width")?.Value);
        BehaviorTestCheck.Equal(
            "56",
            playbackActionButton.Attribute("Height")?.Value);
        BehaviorTestCheck.Equal(
            "Center",
            playbackActionButton.Attribute("HorizontalAlignment")?.Value);
        BehaviorTestCheck.Equal(
            "Center",
            playbackActionButton.Attribute("VerticalAlignment")?.Value);
        BehaviorTestCheck.Equal(
            "{StaticResource MemoryPlaybackActionButtonStyle}",
            playbackActionButton.Attribute("Style")?.Value);
        BehaviorTestCheck.Equal(
            "Segoe MDL2 Assets",
            playbackActionButton.Attribute("FontFamily")?.Value);
        BehaviorTestCheck.Equal(
            "28",
            actionSurface.Attribute("CornerRadius")?.Value);
        BehaviorTestCheck.True(
            playbackActionOverlay.Ancestors(presentation + "Border")
                .Any(element => element.Attribute("Grid.Row")?.Value == "1"));
        BehaviorTestCheck.Equal(
            0,
            playbackActionOverlay.Descendants(presentation + "TextBlock").Count());
        BehaviorTestCheck.Equal(
            2,
            playbackActionOverlay.Elements().Count());
        BehaviorTestCheck.Equal(
            2,
            document.Descendants(presentation + "Button").Count());
        BehaviorTestCheck.True(playbackXaml.Contains(
            "SystemParameters.HighContrast",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(playbackXaml.Contains(
            "SystemColors.HighlightBrushKey",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(playbackXaml.Contains(
            "x:Key=\"MemoryPlaybackActionButtonStyle\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(playbackXaml.Contains(
            "x:Name=\"PhaseCaptionText\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(playbackXaml.Contains(
            "x:Name=\"SecondaryButton\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(playbackXaml.Contains(
            "Grid.Row=\"2\"",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(playbackXaml.Contains(
            "回忆看完啦",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(playbackXaml.Contains(
            "这段时光已经收进回忆里",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(playbackXaml.Contains(
            "咕噜还在这里，可以再看一次",
            StringComparison.Ordinal));
    }

    private static void PawAssetIsPackagedAndTransparent()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "UI",
            "paw-scroll.png");
        BehaviorTestCheck.True(
            File.Exists(path),
            "The packaged paw-scroll asset is missing.");

        using SKBitmap bitmap = BehaviorTestCheck.NotNull(SKBitmap.Decode(path));
        BehaviorTestCheck.Equal(96, bitmap.Width);
        BehaviorTestCheck.Equal(96, bitmap.Height);

        bool hasTransparentPixel = false;
        bool hasVisiblePixel = false;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                byte alpha = bitmap.GetPixel(x, y).Alpha;
                hasTransparentPixel |= alpha == 0;
                hasVisiblePixel |= alpha >= 240;
            }
        }

        BehaviorTestCheck.True(hasTransparentPixel);
        BehaviorTestCheck.True(hasVisiblePixel);
        BehaviorTestCheck.Equal((byte)0, bitmap.GetPixel(0, 0).Alpha);
        BehaviorTestCheck.True(bitmap.GetPixel(48, 48).Alpha >= 240);
    }

    private static void TemplatesPreserveScrollingAndExposePetMarkers()
    {
        RunSta(
            () =>
            {
                ResourceDictionary theme = LoadTheme();
                var root = new Grid
                {
                    Background = new SolidColorBrush(
                        Color.FromRgb(255, 248, 229)),
                    Resources =
                    {
                        MergedDictionaries =
                        {
                            theme,
                        },
                    },
                };
                root.RowDefinitions.Add(new RowDefinition());
                root.RowDefinitions.Add(
                    new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(
                    new RowDefinition { Height = GridLength.Auto });

                var list = new ListBox
                {
                    Height = 210,
                    Background = new SolidColorBrush(
                        Color.FromRgb(255, 252, 242)),
                    BorderThickness = new Thickness(0),
                };
                ScrollViewer.SetHorizontalScrollBarVisibility(
                    list,
                    ScrollBarVisibility.Disabled);
                ScrollViewer.SetVerticalScrollBarVisibility(
                    list,
                    ScrollBarVisibility.Visible);
                for (var index = 0; index < 40; index++)
                {
                    list.Items.Add($"第 {index + 1} 条咕噜记录");
                }
                root.Children.Add(list);

                var horizontal = new ScrollBar
                {
                    Orientation = Orientation.Horizontal,
                    Minimum = 0,
                    Maximum = 100,
                    Value = 35,
                    ViewportSize = 20,
                    Margin = new Thickness(0, 8, 0, 0),
                };
                Grid.SetRow(horizontal, 1);
                root.Children.Add(horizontal);

                var progress = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = 64,
                    Margin = new Thickness(0, 8, 0, 0),
                };
                Grid.SetRow(progress, 2);
                root.Children.Add(progress);

                var window = new Window
                {
                    Width = 320,
                    Height = 350,
                    Left = -10_000,
                    Top = -10_000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                    Background = new SolidColorBrush(
                        Color.FromRgb(255, 248, 229)),
                    Content = root,
                };

                try
                {
                    window.Show();
                    window.UpdateLayout();

                    ScrollViewer viewer = BehaviorTestCheck.NotNull(
                        FindVisualDescendant<ScrollViewer>(list));
                    ScrollBar vertical = BehaviorTestCheck.NotNull(
                        FindVisualDescendants<ScrollBar>(list)
                            .FirstOrDefault(static bar =>
                                bar.Orientation == Orientation.Vertical));
                    AssertPetScrollBar(
                        vertical,
                        ScrollBar.PageUpCommand,
                        ScrollBar.PageDownCommand);

                    viewer.ScrollToEnd();
                    window.UpdateLayout();
                    BehaviorTestCheck.True(
                        viewer.VerticalOffset > 0,
                        "The themed ListBox no longer scrolls vertically.");

                    AssertPetScrollBar(
                        horizontal,
                        ScrollBar.PageLeftCommand,
                        ScrollBar.PageRightCommand);

                    progress.ApplyTemplate();
                    BehaviorTestCheck.NotNull(
                        progress.Template.FindName("PART_Track", progress)
                            as FrameworkElement);
                    BehaviorTestCheck.NotNull(
                        progress.Template.FindName("PART_Indicator", progress)
                            as FrameworkElement);
                    Image marker = BehaviorTestCheck.NotNull(
                        progress.Template.FindName(
                            "PawProgressMarker",
                            progress) as Image);
                    BehaviorTestCheck.True(
                        marker.Source?.ToString().EndsWith(
                            "/Assets/UI/paw-scroll.png",
                            StringComparison.OrdinalIgnoreCase) == true);

                    SaveVisualWhenRequested(root, "controls-themed.png");
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void SavesThemedDiaryPreviewWhenRequested()
    {
        string? directory = Environment.GetEnvironmentVariable(
            "GULUPET_UI_CHROME_PREVIEW_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        RunSta(
            () =>
            {
                Directory.CreateDirectory(directory);
                DiaryLedger ledger = CreatePreviewLedger();
                var window = new DiaryWindow(ledger)
                {
                    Left = -10_000,
                    Top = -10_000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };
                window.Resources.MergedDictionaries.Add(LoadTheme());
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    window.SaveRenderedPreview(Path.Combine(
                        directory,
                        "diary-themed.png"));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static DiaryLedger CreatePreviewLedger()
    {
        var selectedDate = new DateOnly(2026, 8, 4);
        var now = new DateTimeOffset(
            2026,
            8,
            4,
            18,
            40,
            0,
            TimeSpan.FromHours(8));
        var ledger = new DiaryLedger();

        ledger.AddRuntime(selectedDate.AddDays(1), 3700, now.AddDays(1));
        for (var dayOffset = 0; dayOffset <= 10; dayOffset++)
        {
            DateOnly date = selectedDate.AddDays(-dayOffset);
            DateTimeOffset timestamp = now.AddDays(-dayOffset);
            ledger.AddRuntime(
                date,
                (long)TimeSpan.FromMinutes(70 + (dayOffset * 19)).TotalSeconds,
                timestamp);
            ledger.RecordInteraction(
                date,
                dayOffset % 2 == 0
                    ? DiaryInteractionKinds.Petting
                    : DiaryInteractionKinds.Click,
                timestamp.AddMinutes(-20));

            if (dayOffset is 2 or 5 or 8)
            {
                continue;
            }

            ledger.RecordGenerationSuccess(
                date,
                new GeneratedDiaryEntry
                {
                    Title = dayOffset == 0
                        ? "只是刚好待在这里"
                        : "窗边的位置还算不错",
                    Mood = dayOffset == 0 ? "若无其事" : "平静",
                    Body = dayOffset == 0
                        ? "今天妈咪在桌前忙了很久。我原本没打算理她，" +
                            "只是在旁边找了个不碍事的位置趴着。她经过时点了点我，" +
                            "又认真摸了好几次。算了，看在手心还算暖的份上，" +
                            "我没有躲开。后来我吃到一小份猫条，又背着轻轻的风" +
                            "出了趟门。天气很晴，窗边的光落在毛尖上。" +
                            "四个多小时里，她忙她的，我守我的。至于是不是特意" +
                            "陪她——才不是，只是这里待惯了而已。"
                        : "妈咪照常忙她的事情，我也照常选了一个看得见她的位置。" +
                            "偶尔得到一点关注，偶尔什么都不做，这样的一天也很好。",
                    Model = "local-template-v1",
                    GeneratedAtUtc = timestamp,
                },
                timestamp);
        }

        ledger.RecordWeather(
            selectedDate,
            new DiaryWeatherSnapshot
            {
                Kind = "clear",
                TemperatureCelsius = 28.6,
                ObservedAtUtc = now.AddHours(-1),
            },
            now);
        return ledger;
    }

    private static void SaveVisualWhenRequested(
        FrameworkElement visual,
        string fileName)
    {
        string? directory = Environment.GetEnvironmentVariable(
            "GULUPET_UI_CHROME_PREVIEW_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        visual.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);

        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(
            Path.Combine(directory, fileName),
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        encoder.Save(stream);
    }

    private static ResourceDictionary LoadTheme()
    {
        Application.ResourceAssembly ??= typeof(GuluPet.App).Assembly;
        string assemblyName = BehaviorTestCheck.NotNull(
            typeof(GuluPet.App).Assembly.GetName().Name);
        return BehaviorTestCheck.NotNull(
            Application.LoadComponent(
                new Uri(
                    $"/{assemblyName};component/Presentation/Themes/GuluControls.xaml",
                    UriKind.Relative)) as ResourceDictionary);
    }

    private static void AssertPetScrollBar(
        ScrollBar scrollBar,
        RoutedCommand expectedDecreaseCommand,
        RoutedCommand expectedIncreaseCommand)
    {
        scrollBar.ApplyTemplate();
        Track track = BehaviorTestCheck.NotNull(
            scrollBar.Template.FindName("PART_Track", scrollBar) as Track);
        BehaviorTestCheck.Equal(
            expectedDecreaseCommand,
            track.DecreaseRepeatButton.Command);
        BehaviorTestCheck.Equal(
            expectedIncreaseCommand,
            track.IncreaseRepeatButton.Command);

        track.Thumb.ApplyTemplate();
        Image marker = BehaviorTestCheck.NotNull(
            track.Thumb.Template.FindName(
                "PawThumbImage",
                track.Thumb) as Image);
        BehaviorTestCheck.True(
            marker.Source?.ToString().EndsWith(
                "/Assets/UI/paw-scroll.png",
                StringComparison.OrdinalIgnoreCase) == true);
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject =>
        FindVisualDescendants<T>(root).FirstOrDefault();

    private static IEnumerable<T> FindVisualDescendants<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GuluPetCommunity.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Unable to find the GuluPet repository root.");
    }

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
        Console.WriteLine($"PASS {nameof(GuluControlThemeTests)}.{name}");
    }
}
