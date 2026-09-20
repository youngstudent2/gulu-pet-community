using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuluPet.Postcards;

namespace GuluPet.Tests;

internal static class PostcardCatalogTests
{
    public static void RunAll()
    {
        Run(
            nameof(LoadsSortsAndResolvesImagesFromAssets),
            LoadsSortsAndResolvesImagesFromAssets);
        Run(
            nameof(AsyncLoadUsesTheSameStrictContract),
            AsyncLoadUsesTheSameStrictContract);
        Run(
            nameof(RejectsDuplicateIdsAndOrders),
            RejectsDuplicateIdsAndOrders);
        Run(
            nameof(RejectsNonContiguousUnlockOrders),
            RejectsNonContiguousUnlockOrders);
        Run(
            nameof(RejectsImagePathOutsidePostcardDirectory),
            RejectsImagePathOutsidePostcardDirectory);
        Run(
            nameof(RejectsMissingAndUndecodableImages),
            RejectsMissingAndUndecodableImages);
        Run(
            nameof(RejectsUnknownSchemaAndProperties),
            RejectsUnknownSchemaAndProperties);
        Run(
            nameof(RejectsMissingRepeatedAndNonContiguousChapterBlocks),
            RejectsMissingRepeatedAndNonContiguousChapterBlocks);
        Run(
            nameof(UnlockProjectionUsesDurableIdsNotLegacyInputOrPrefixShape),
            UnlockProjectionUsesDurableIdsNotLegacyInputOrPrefixShape);
        Run(
            nameof(DynamicChapterProgressSupportsFifteenChapters),
            DynamicChapterProgressSupportsFifteenChapters);
        Run(
            nameof(CommunityPackageOmitsOptionalPostcardPack),
            CommunityPackageOmitsOptionalPostcardPack);
    }

    private static void LoadsSortsAndResolvesImagesFromAssets()
    {
        WithPostcardAssets(
            """
            {
              "schemaVersion": 1,
              "postcards": [
                {
                  "id": "guangzhou-tower",
                  "title": "广州塔",
                  "location": "广东广州",
                  "imagePath": "images/guangzhou-tower.png",
                  "unlockOrder": 2
                },
                {
                  "id": "great-wall",
                  "title": "中国长城",
                  "location": "北京",
                  "imagePath": "images/great-wall.png",
                  "unlockOrder": 1,
                  "altText": "咕噜在长城"
                }
              ]
            }
            """,
            ["images/guangzhou-tower.png", "images/great-wall.png"],
            assetsRoot =>
            {
                PostcardCatalog catalog =
                    PostcardCatalog.LoadFromAssets(assetsRoot);
                BehaviorTestCheck.Equal(2, catalog.Count);
                BehaviorTestCheck.SequenceEqual(
                    ["great-wall", "guangzhou-tower"],
                    catalog.Definitions.Select(
                        static definition => definition.Id));
                BehaviorTestCheck.True(
                    Path.IsPathFullyQualified(
                        catalog["great-wall"].ImagePath));
                BehaviorTestCheck.Equal(
                    "咕噜在长城",
                    catalog["great-wall"].AltText);
                BehaviorTestCheck.Equal(
                    "广州塔，广东广州",
                    catalog["guangzhou-tower"].AltText);
                BehaviorTestCheck.True(
                    catalog.TryGet("great-wall", out _));
                BehaviorTestCheck.False(
                    catalog.TryGet("missing", out _));
            });
    }

    private static void AsyncLoadUsesTheSameStrictContract()
    {
        WithPostcardAssets(
            ValidCatalog("great-wall", "images/great-wall.png", 1),
            ["images/great-wall.png"],
            assetsRoot =>
            {
                PostcardCatalog catalog =
                    PostcardCatalog.LoadFromAssetsAsync(assetsRoot)
                        .GetAwaiter()
                        .GetResult();
                BehaviorTestCheck.Equal("great-wall", catalog.Definitions[0].Id);
            });
    }

    private static void RejectsDuplicateIdsAndOrders()
    {
        WithPostcardAssets(
            """
            {
              "schemaVersion": 1,
              "postcards": [
                {
                  "id": "same",
                  "title": "甲",
                  "location": "甲地",
                  "imagePath": "images/a.png",
                  "unlockOrder": 1
                },
                {
                  "id": "same",
                  "title": "乙",
                  "location": "乙地",
                  "imagePath": "images/b.png",
                  "unlockOrder": 2
                }
              ]
            }
            """,
            ["images/a.png", "images/b.png"],
            assetsRoot => BehaviorTestCheck.Throws<InvalidDataException>(
                () => PostcardCatalog.LoadFromAssets(assetsRoot)));

        WithPostcardAssets(
            """
            {
              "schemaVersion": 1,
              "postcards": [
                {
                  "id": "first",
                  "title": "甲",
                  "location": "甲地",
                  "imagePath": "images/a.png",
                  "unlockOrder": 1
                },
                {
                  "id": "second",
                  "title": "乙",
                  "location": "乙地",
                  "imagePath": "images/b.png",
                  "unlockOrder": 1
                }
              ]
            }
            """,
            ["images/a.png", "images/b.png"],
            assetsRoot => BehaviorTestCheck.Throws<InvalidDataException>(
                () => PostcardCatalog.LoadFromAssets(assetsRoot)));
    }

    private static void RejectsNonContiguousUnlockOrders()
    {
        WithPostcardAssets(
            """
            {
              "schemaVersion": 1,
              "postcards": [
                {
                  "id": "first",
                  "title": "甲",
                  "location": "甲地",
                  "imagePath": "images/a.png",
                  "unlockOrder": 1
                },
                {
                  "id": "third",
                  "title": "丙",
                  "location": "丙地",
                  "imagePath": "images/c.png",
                  "unlockOrder": 3
                }
              ]
            }
            """,
            ["images/a.png", "images/c.png"],
            assetsRoot => BehaviorTestCheck.Throws<InvalidDataException>(
                () => PostcardCatalog.LoadFromAssets(assetsRoot)));

        WithPostcardAssets(
            ValidCatalog("starts-at-two", "images/two.png", 2),
            ["images/two.png"],
            assetsRoot => BehaviorTestCheck.Throws<InvalidDataException>(
                () => PostcardCatalog.LoadFromAssets(assetsRoot)));
    }

    private static void RejectsImagePathOutsidePostcardDirectory()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string assetsRoot = Path.Combine(root, "Assets");
            string postcardRoot = Path.Combine(assetsRoot, "Postcards");
            Directory.CreateDirectory(postcardRoot);
            WritePng(Path.Combine(assetsRoot, "outside.png"));
            File.WriteAllText(
                Path.Combine(postcardRoot, "catalog.json"),
                ValidCatalog("outside", "../outside.png", 1));
            BehaviorTestCheck.Throws<InvalidDataException>(
                () => PostcardCatalog.LoadFromAssets(assetsRoot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RejectsMissingAndUndecodableImages()
    {
        WithPostcardAssets(
            ValidCatalog("missing", "images/missing.png", 1),
            [],
            assetsRoot => BehaviorTestCheck.Throws<InvalidDataException>(
                () => PostcardCatalog.LoadFromAssets(assetsRoot)));

        string root = CreateTemporaryDirectory();
        try
        {
            string assetsRoot = Path.Combine(root, "Assets");
            string postcardRoot = Path.Combine(assetsRoot, "Postcards");
            string images = Path.Combine(postcardRoot, "images");
            Directory.CreateDirectory(images);
            File.WriteAllText(
                Path.Combine(images, "broken.png"),
                "not an image");
            File.WriteAllText(
                Path.Combine(postcardRoot, "catalog.json"),
                ValidCatalog("broken", "images/broken.png", 1));
            BehaviorTestCheck.Throws<InvalidDataException>(
                () => PostcardCatalog.LoadFromAssets(assetsRoot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RejectsUnknownSchemaAndProperties()
    {
        WithPostcardAssets(
            ValidCatalog("future", "images/future.png", 1)
                .Replace(
                    "\"schemaVersion\": 1",
                    "\"schemaVersion\": 2",
                    StringComparison.Ordinal),
            ["images/future.png"],
            assetsRoot => BehaviorTestCheck.Throws<InvalidDataException>(
                () => PostcardCatalog.LoadFromAssets(assetsRoot)));

        string json = ValidCatalog("extra", "images/extra.png", 1);
        string withUnknownProperty = json.Replace(
            "\"unlockOrder\": 1",
            "\"unlockOrder\": 1, \"legacyStyle\": true",
            StringComparison.Ordinal);
        WithPostcardAssets(
            withUnknownProperty,
            ["images/extra.png"],
            assetsRoot => BehaviorTestCheck.Throws<InvalidDataException>(
                () => PostcardCatalog.LoadFromAssets(assetsRoot)));
    }

    private static void RejectsMissingRepeatedAndNonContiguousChapterBlocks()
    {
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new PostcardCatalog(CreateChapterDefinitions(
                static _ => string.Empty)));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new PostcardCatalog(CreateChapterDefinitions(
                static index => index >= 80
                    ? "章节 1"
                    : $"章节 {index / 20 + 1}")));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new PostcardCatalog(CreateChapterDefinitions(
                static index => index == 19
                    ? "章节 2"
                    : $"章节 {index / 20 + 1}")));
    }

    private static void UnlockProjectionUsesDurableIdsNotLegacyInputOrPrefixShape()
    {
        var catalog = new PostcardCatalog(
        [
            new PostcardDefinition(
                "first",
                "第一张",
                "中国",
                Path.GetFullPath("first.jpg"),
                1,
                "第一张明信片"),
            new PostcardDefinition(
                "second",
                "第二张",
                "中国",
                Path.GetFullPath("second.jpg"),
                2,
                "第二张明信片"),
        ]);
        var unlockedWithoutInput = new PostcardCollectionState
        {
            TotalInputCount = 0,
            Unlocked =
            [
                new PostcardUnlockRecord
                {
                    PostcardId = "first",
                    UnlockedAtUtc = new DateTimeOffset(
                        2026,
                        8,
                        1,
                        4,
                        0,
                        0,
                        TimeSpan.Zero),
                },
            ],
            UpdatedAtUtc = new DateTimeOffset(
                2026,
                8,
                1,
                4,
                0,
                0,
                TimeSpan.Zero),
        };

        BehaviorTestCheck.SequenceEqual(
            ["first"],
            catalog.GetUnlockedDefinitions(unlockedWithoutInput)
                .Select(static postcard => postcard.Id));

        PostcardCollectionState evolvedCatalogOrder = unlockedWithoutInput with
        {
            Unlocked =
            [
                new PostcardUnlockRecord
                {
                    PostcardId = "second",
                    UnlockedAtUtc = unlockedWithoutInput.UpdatedAtUtc,
                },
            ],
        };
        BehaviorTestCheck.SequenceEqual(
            ["second"],
            catalog.GetUnlockedDefinitions(evolvedCatalogOrder)
                .Select(static postcard => postcard.Id));
    }

    private static void DynamicChapterProgressSupportsFifteenChapters()
    {
        PostcardDefinition[] catalog = CreateChapterDefinitions(
            static index => $"章节 {index / 20 + 1}",
            count: 300).ToArray();
        var validatedCatalog = new PostcardCatalog(catalog);
        BehaviorTestCheck.Equal(300, validatedCatalog.Count);

        BehaviorTestCheck.True(
            PostcardChapterProgressFormatter.TryGetChapterCount(
                catalog,
                out int chapterCount));
        BehaviorTestCheck.Equal(15, chapterCount);

        PostcardChapterProgress lastChapter =
            PostcardChapterProgressFormatter.Create(catalog, 281);
        BehaviorTestCheck.Equal(15, lastChapter.ChapterNumber);
        BehaviorTestCheck.Equal(1, lastChapter.UnlockedInChapter);
        BehaviorTestCheck.False(lastChapter.IsCollectionComplete);

        PostcardChapterProgress complete =
            PostcardChapterProgressFormatter.Create(catalog, 300);
        BehaviorTestCheck.Equal(15, complete.ChapterNumber);
        BehaviorTestCheck.True(complete.IsCollectionComplete);
        BehaviorTestCheck.True(
            complete.NextChapterFeedback.Contains(
                "15 段",
                StringComparison.Ordinal));
    }

    private static void CommunityPackageOmitsOptionalPostcardPack()
    {
        string postcardRoot = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Postcards");
        BehaviorTestCheck.False(Directory.Exists(postcardRoot));
    }

    private static IEnumerable<PostcardDefinition> CreateChapterDefinitions(
        Func<int, string> chapterSelector,
        int count = 100) =>
        Enumerable.Range(0, count)
            .Select(index => new PostcardDefinition(
                $"card-{index + 1}",
                $"明信片 {index + 1}",
                "中国",
                Path.GetFullPath($"card-{index + 1}.jpg"),
                index + 1,
                $"咕噜的明信片 {index + 1}",
                chapterSelector(index)));

    private static string ValidCatalog(
        string id,
        string imagePath,
        int unlockOrder) =>
        $$"""
        {
          "schemaVersion": 1,
          "postcards": [
            {
              "id": "{{id}}",
              "title": "咕噜旅行",
              "location": "中国",
              "imagePath": "{{imagePath}}",
              "unlockOrder": {{unlockOrder}}
            }
          ]
        }
        """;

    private static void WithPostcardAssets(
        string catalogJson,
        IReadOnlyList<string> imagePaths,
        Action<string> assertion)
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string assetsRoot = Path.Combine(root, "Assets");
            string postcardRoot = Path.Combine(assetsRoot, "Postcards");
            Directory.CreateDirectory(postcardRoot);
            foreach (string imagePath in imagePaths)
            {
                WritePng(Path.Combine(postcardRoot, imagePath));
            }

            File.WriteAllText(
                Path.Combine(postcardRoot, "catalog.json"),
                catalogJson);
            assertion(assetsRoot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"GuluPet.PostcardCatalog.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WritePng(string path)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("PNG path has no directory."));
        byte[] pixels =
        [
            255, 255, 255, 255,
            180, 220, 255, 255,
            255, 220, 180, 255,
            230, 230, 230, 255,
        ];
        BitmapSource bitmap = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride: 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(PostcardCatalogTests)}.{name}");
    }
}
