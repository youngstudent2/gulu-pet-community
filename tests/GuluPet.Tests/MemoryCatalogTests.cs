using System.IO;
using GuluPet.Memories;

namespace GuluPet.Tests;

internal static class MemoryCatalogTests
{
    public static void RunAll()
    {
        Run(
            nameof(DefaultCatalogHasSixteenOrderedTenInteractionMilestones),
            DefaultCatalogHasSixteenOrderedTenInteractionMilestones);
        Run(
            nameof(DefaultVideoFileNamesAreStableAndLocal),
            DefaultVideoFileNamesAreStableAndLocal);
        Run(
            nameof(DefaultMetadataAndThumbnailsAreSemanticAndStable),
            DefaultMetadataAndThumbnailsAreSemanticAndStable);
        Run(
            nameof(RejectsDuplicateAndUnsafeDefinitions),
            RejectsDuplicateAndUnsafeDefinitions);
        Run(
            nameof(CatalogCopiesVideoFileLists),
            CatalogCopiesVideoFileLists);
    }

    private static void
        DefaultCatalogHasSixteenOrderedTenInteractionMilestones()
    {
        MemoryCatalog catalog = MemoryCatalog.Default;

        BehaviorTestCheck.Equal(16, catalog.Count);
        BehaviorTestCheck.SequenceEqual(
            Enumerable.Range(1, 16)
                .Select(static sequence => $"memory-{sequence:00}"),
            catalog.Definitions.Select(static memory => memory.Id));
        BehaviorTestCheck.SequenceEqual(
            Enumerable.Range(1, 16)
                .Select(static sequence => sequence * 10L),
            catalog.Definitions.Select(
                static memory =>
                    memory.UnlockAtAcceptedInteractionCount));
        BehaviorTestCheck.Equal(
            10L,
            catalog["memory-01"].UnlockAtAcceptedInteractionCount);
        BehaviorTestCheck.Equal(
            160L,
            catalog["memory-16"].UnlockAtAcceptedInteractionCount);
    }

    private static void DefaultVideoFileNamesAreStableAndLocal()
    {
        string[] expectedVideoFileNames = Enumerable.Range(1, 16)
            .SelectMany(static sequence =>
                sequence == 8
                    ? Enumerable.Range(1, 9)
                        .Select(part => $"memory-08-{part:00}.mp4")
                    : [$"memory-{sequence:00}.mp4"])
            .ToArray();
        BehaviorTestCheck.SequenceEqual(
            expectedVideoFileNames,
            MemoryCatalog.Default.Definitions.SelectMany(
                static memory => memory.VideoFileNames));
        BehaviorTestCheck.True(
            MemoryCatalog.Default.Definitions.All(
                static memory =>
                    memory.VideoFileNames.All(videoFileName =>
                        string.Equals(
                            Path.GetFileName(videoFileName),
                            videoFileName,
                            StringComparison.Ordinal))));
        BehaviorTestCheck.True(
            MemoryCatalog.Default.TryGet(
                "memory-08",
                out MemoryDefinition? eighth));
        BehaviorTestCheck.SequenceEqual(
            Enumerable.Range(1, 9)
                .Select(static part => $"memory-08-{part:00}.mp4"),
            BehaviorTestCheck.NotNull(eighth).VideoFileNames);
        BehaviorTestCheck.False(
            MemoryCatalog.Default.TryGet("missing", out _));
    }

    private static void DefaultMetadataAndThumbnailsAreSemanticAndStable()
    {
        BehaviorTestCheck.SequenceEqual(
            new[]
            {
                "第一次追着你",
                "怀里的午后",
                "地板上的邀约",
                "翻肚皮的信任",
                "好奇的一口",
                "掌心下的安静",
                "靠近镜头",
                "逗猫棒时刻",
                "认真看你",
                "镜头前的小脸",
                "安心睡着",
                "懒洋洋的早晨",
                "眼睛里的你",
                "把下巴交给你",
                "摸摸肚皮",
                "沙发边的酣睡",
            },
            MemoryCatalog.Default.Definitions.Select(
                static memory => memory.Title));
        BehaviorTestCheck.SequenceEqual(
            new[]
            {
                "咕噜侧卧在木地板上的木椅旁，身体和尾巴伸向一侧，前爪收在胸前；镜头逐渐靠近，她始终留在原处，抬眼看向镜头后又略微垂下视线。",
                "咕噜仰卧在一名穿粉色上衣的人的腿上，腹部和两只后爪朝向镜头；对方多次抚摸她的腹侧和身体，咕噜抬腿遮住部分画面，最后移动到镜头前，毛发挡住大半视野。",
                "咕噜在木地板上追逐来回移动的绿色逗猫玩具，先侧躺着用前爪扑抓并张嘴咬线，随后起身转动、跟随玩具来到浅色拖鞋旁，最后低头看向停在脚边的细绳。",
                "一只手用绿色手柄的逗猫棒把黑色细绳送到咕噜面前。咕噜侧躺在木地板和木质家具旁，反复伸前爪拍打、抱住细绳并张嘴咬住，期间翻身抬爪，最后仍抓着玩具。",
                "咕噜站在室内地面与绿色地垫交界处，靠近画面，反复舔食一只手递到嘴边的浅色木勺上粉色糊状食物；木勺始终停在鼻尖前，她直到视频结束仍在进食。",
                "咕噜侧躺在浅色瓷砖地面，一只手持续轻抚她的头顶与耳侧，随后伸到鼻口前；她抬头贴近手指嗅闻，仍留在原处看向近处的手与镜头。",
                "镜头从贴近毛发的模糊画面拉开并来回晃动，露出半躺在浅色瓷砖上的咕噜；她抬眼看向镜头，后半段画面叠加蓝、绿、黄、红色舞台射灯特效，最后停在猫脸近景。",
                "九段短片中，咕噜在餐桌旁的花纹折叠椅上追看带绿色飘带的细绳，躺着拨抓咬住、坐起挥爪扑接，又嗅闻粉色花束并扶着椅臂看向镜头；其间穿插橘猫躺地抓玩具、靠窗仰躺的画面，末尾咕噜用双爪夹住飘带细绳。",
                "咕噜趴在带绿色花边的软垫边缘，两只前爪搭在边沿。镜头从近景持续推进到脸部特写，她保持原位并缓慢转动视线，最后鼻子、眼睛和胡须占据画面。",
                "咕噜坐在白色布料旁，一只手从侧后方托住她的脸颊和颈部。她先偏头看向一侧，随后正对镜头停留，最后向镜头靠近，让脸和胸前毛发占满画面。",
                "咕噜仰躺在浅色瓷砖地面上，两条后腿并拢伸向镜头。她反复弯曲和伸展其中一条后腿，并在原地改变姿势，最后收腿撑起身体并离开躺卧位置。",
                "咕噜仰躺在有绿色坐垫和花纹靠背的椅子上，露出白色腹部并看向镜头。她稍微扭动身体，把一只前爪举到脸边，随后向镜头伸出，最后爪子贴近镜头。",
                "咕噜趴在绿色布面沙发上，镜头始终贴近她的脸。她在镜头前缓慢低头、左右转头，再抬眼直视并把鼻尖凑近，最后仍伏在原处看向镜头。",
                "咕噜坐在绿色布面沙发上，一只手持续挠她的下巴并托住脸颊。她时而抬起下巴、眯起眼，随后把下巴落进掌心，低头贴着手停留。",
                "咕噜仰躺在绿色布面沙发上，露出腹部，一只手在她肚子上轻揉。片刻后她转过头，用两只前爪抱住手腕，把手拉到脸旁并张嘴轻咬，始终没有起身。",
                "咕噜在绿色布面沙发角落仰躺，起初闭眼蜷着前爪。一只手先抚摸她的腹部，又把手指伸到鼻尖和嘴边；她睁眼舔咬手指、抬爪拍抓，最后用前爪抱住手腕。",
            },
            MemoryCatalog.Default.Definitions.Select(
                static memory => memory.Description));
        BehaviorTestCheck.SequenceEqual(
            Enumerable.Range(1, 16)
                .Select(static sequence => $"memory-{sequence:00}.jpg"),
            MemoryCatalog.Default.Definitions.Select(
                static memory => memory.ThumbnailFileName));
    }

    private static void RejectsDuplicateAndUnsafeDefinitions()
    {
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new MemoryCatalog(
            [
                new MemoryDefinition("same", 1, 10, "memory-01.mp4"),
                new MemoryDefinition("same", 2, 20, "memory-02.mp4"),
            ]));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new MemoryCatalog(
            [
                new MemoryDefinition(
                    "memory-01",
                    1,
                    10,
                    @"..\outside.mp4"),
            ]));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new MemoryCatalog(
            [
                new MemoryDefinition(
                    "memory-01",
                    2,
                    10,
                    "memory-01.mp4"),
            ]));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new MemoryCatalog(
            [
                new MemoryDefinition(
                    "memory-01",
                    1,
                    10,
                    Array.Empty<string>()),
            ]));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new MemoryCatalog(
            [
                new MemoryDefinition(
                    "memory-01",
                    1,
                    10,
                    ["same.mp4", "same.mp4"]),
            ]));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new MemoryCatalog(
            [
                new MemoryDefinition(
                    "memory-01",
                    1,
                    10,
                    ["memory-01.mp4"],
                    "安全标题",
                    @"..\outside.jpg"),
            ]));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new MemoryCatalog(
            [
                new MemoryDefinition(
                    "memory-01",
                    1,
                    10,
                    ["memory-01.mp4"],
                    " ",
                    "memory-01.jpg"),
            ]));
        BehaviorTestCheck.Throws<InvalidDataException>(
            () => new MemoryCatalog(
            [
                new MemoryDefinition(
                    "memory-01",
                    1,
                    10,
                    ["memory-01.mp4"],
                    "安全标题",
                    " ",
                    "memory-01.jpg"),
            ]));
    }

    private static void CatalogCopiesVideoFileLists()
    {
        var mutableNames = new List<string> { "memory-01.mp4" };
        var catalog = new MemoryCatalog(
        [
            new MemoryDefinition(
                "memory-01",
                1,
                10,
                mutableNames),
        ]);

        mutableNames[0] = @"..\outside.mp4";

        BehaviorTestCheck.SequenceEqual(
            new[] { "memory-01.mp4" },
            catalog["memory-01"].VideoFileNames);
        BehaviorTestCheck.Throws<NotSupportedException>(
            () => ((IList<string>)catalog["memory-01"].VideoFileNames)[0] =
                "replacement.mp4");
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {nameof(MemoryCatalogTests)}.{name}");
    }
}
