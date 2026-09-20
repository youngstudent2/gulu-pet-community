using System.Collections.ObjectModel;
using System.IO;

namespace GuluPet.Memories;

/// <summary>
/// The fixed, ordered set of memories shipped with GuluPet.
/// </summary>
public sealed class MemoryCatalog
{
    private static readonly (string Title, string Description)[] DefaultMetadata =
    [
        (
            "第一次追着你",
            "咕噜侧卧在木地板上的木椅旁，身体和尾巴伸向一侧，前爪收在胸前；镜头逐渐靠近，她始终留在原处，抬眼看向镜头后又略微垂下视线。"),
        (
            "怀里的午后",
            "咕噜仰卧在一名穿粉色上衣的人的腿上，腹部和两只后爪朝向镜头；对方多次抚摸她的腹侧和身体，咕噜抬腿遮住部分画面，最后移动到镜头前，毛发挡住大半视野。"),
        (
            "地板上的邀约",
            "咕噜在木地板上追逐来回移动的绿色逗猫玩具，先侧躺着用前爪扑抓并张嘴咬线，随后起身转动、跟随玩具来到浅色拖鞋旁，最后低头看向停在脚边的细绳。"),
        (
            "翻肚皮的信任",
            "一只手用绿色手柄的逗猫棒把黑色细绳送到咕噜面前。咕噜侧躺在木地板和木质家具旁，反复伸前爪拍打、抱住细绳并张嘴咬住，期间翻身抬爪，最后仍抓着玩具。"),
        (
            "好奇的一口",
            "咕噜站在室内地面与绿色地垫交界处，靠近画面，反复舔食一只手递到嘴边的浅色木勺上粉色糊状食物；木勺始终停在鼻尖前，她直到视频结束仍在进食。"),
        (
            "掌心下的安静",
            "咕噜侧躺在浅色瓷砖地面，一只手持续轻抚她的头顶与耳侧，随后伸到鼻口前；她抬头贴近手指嗅闻，仍留在原处看向近处的手与镜头。"),
        (
            "靠近镜头",
            "镜头从贴近毛发的模糊画面拉开并来回晃动，露出半躺在浅色瓷砖上的咕噜；她抬眼看向镜头，后半段画面叠加蓝、绿、黄、红色舞台射灯特效，最后停在猫脸近景。"),
        (
            "逗猫棒时刻",
            "九段短片中，咕噜在餐桌旁的花纹折叠椅上追看带绿色飘带的细绳，躺着拨抓咬住、坐起挥爪扑接，又嗅闻粉色花束并扶着椅臂看向镜头；其间穿插橘猫躺地抓玩具、靠窗仰躺的画面，末尾咕噜用双爪夹住飘带细绳。"),
        (
            "认真看你",
            "咕噜趴在带绿色花边的软垫边缘，两只前爪搭在边沿。镜头从近景持续推进到脸部特写，她保持原位并缓慢转动视线，最后鼻子、眼睛和胡须占据画面。"),
        (
            "镜头前的小脸",
            "咕噜坐在白色布料旁，一只手从侧后方托住她的脸颊和颈部。她先偏头看向一侧，随后正对镜头停留，最后向镜头靠近，让脸和胸前毛发占满画面。"),
        (
            "安心睡着",
            "咕噜仰躺在浅色瓷砖地面上，两条后腿并拢伸向镜头。她反复弯曲和伸展其中一条后腿，并在原地改变姿势，最后收腿撑起身体并离开躺卧位置。"),
        (
            "懒洋洋的早晨",
            "咕噜仰躺在有绿色坐垫和花纹靠背的椅子上，露出白色腹部并看向镜头。她稍微扭动身体，把一只前爪举到脸边，随后向镜头伸出，最后爪子贴近镜头。"),
        (
            "眼睛里的你",
            "咕噜趴在绿色布面沙发上，镜头始终贴近她的脸。她在镜头前缓慢低头、左右转头，再抬眼直视并把鼻尖凑近，最后仍伏在原处看向镜头。"),
        (
            "把下巴交给你",
            "咕噜坐在绿色布面沙发上，一只手持续挠她的下巴并托住脸颊。她时而抬起下巴、眯起眼，随后把下巴落进掌心，低头贴着手停留。"),
        (
            "摸摸肚皮",
            "咕噜仰躺在绿色布面沙发上，露出腹部，一只手在她肚子上轻揉。片刻后她转过头，用两只前爪抱住手腕，把手拉到脸旁并张嘴轻咬，始终没有起身。"),
        (
            "沙发边的酣睡",
            "咕噜在绿色布面沙发角落仰躺，起初闭眼蜷着前爪。一只手先抚摸她的腹部，又把手指伸到鼻尖和嘴边；她睁眼舔咬手指、抬爪拍抓，最后用前爪抱住手腕。"),
    ];

    private static readonly MemoryCatalog DefaultCatalog =
        new(CreateDefaultDefinitions());

    private readonly IReadOnlyDictionary<string, MemoryDefinition> _byId;

    internal MemoryCatalog(IEnumerable<MemoryDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        MemoryDefinition[] ordered = definitions
            .OrderBy(static definition => definition.SequenceNumber)
            .ThenBy(static definition => definition.Id, StringComparer.Ordinal)
            .ToArray();
        ValidateDefinitions(ordered);
        ordered = ordered
            .Select(static definition =>
                definition with
                {
                    VideoFileNames =
                        new ReadOnlyCollection<string>(
                            definition.VideoFileNames.ToArray()),
                })
            .ToArray();
        Definitions = new ReadOnlyCollection<MemoryDefinition>(ordered);
        _byId = ordered.ToDictionary(
            static definition => definition.Id,
            StringComparer.Ordinal);
    }

    public static MemoryCatalog Default => DefaultCatalog;

    public IReadOnlyList<MemoryDefinition> Definitions { get; }

    public int Count => Definitions.Count;

    public MemoryDefinition this[string id] =>
        _byId.TryGetValue(id, out MemoryDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown memory '{id}'.");

    public bool TryGet(
        string? id,
        out MemoryDefinition? definition)
    {
        definition = null;
        return !string.IsNullOrWhiteSpace(id)
            && _byId.TryGetValue(id, out definition);
    }

    internal static bool IsValidId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 96
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        return value.All(static character =>
            character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-'
                or '_');
    }

    private static IEnumerable<MemoryDefinition> CreateDefaultDefinitions() =>
        DefaultMetadata
            .Select(static (metadata, index) =>
            {
                int sequence = index + 1;
                return
                new MemoryDefinition(
                    $"memory-{sequence:00}",
                    sequence,
                    sequence * 10L,
                    sequence == 8
                        ? Enumerable.Range(1, 9)
                            .Select(part =>
                                $"memory-08-{part:00}.mp4")
                            .ToArray()
                        : [$"memory-{sequence:00}.mp4"],
                    metadata.Title,
                    metadata.Description,
                    $"memory-{sequence:00}.jpg");
            });

    private static void ValidateDefinitions(
        IReadOnlyList<MemoryDefinition> definitions)
    {
        if (definitions.Count == 0)
        {
            throw new InvalidDataException(
                "The memory catalog must contain at least one memory.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var videoFileNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var thumbnailFileNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        long previousThreshold = 0;
        for (var index = 0; index < definitions.Count; index++)
        {
            MemoryDefinition definition = definitions[index]
                ?? throw new InvalidDataException(
                    $"Memory catalog item at index {index} is null.");
            int expectedSequence = index + 1;
            if (!IsValidId(definition.Id))
            {
                throw new InvalidDataException(
                    $"Memory catalog item at index {index} has invalid id " +
                    $"'{definition.Id}'.");
            }

            if (!ids.Add(definition.Id))
            {
                throw new InvalidDataException(
                    $"Memory '{definition.Id}' is declared more than once.");
            }

            if (definition.SequenceNumber != expectedSequence)
            {
                throw new InvalidDataException(
                    $"Memory '{definition.Id}' has sequence number " +
                    $"'{definition.SequenceNumber}'; expected " +
                    $"'{expectedSequence}'.");
            }

            if (definition.UnlockAtAcceptedInteractionCount
                <= previousThreshold)
            {
                throw new InvalidDataException(
                    $"Memory '{definition.Id}' must have an unlock threshold " +
                    "greater than the preceding memory.");
            }

            if (definition.VideoFileNames is null
                || definition.VideoFileNames.Count == 0)
            {
                throw new InvalidDataException(
                    $"Memory '{definition.Id}' must declare at least one " +
                    "video file.");
            }

            foreach (string videoFileName in definition.VideoFileNames)
            {
                ValidateVideoFileName(videoFileName, definition.Id);
                if (!videoFileNames.Add(videoFileName))
                {
                    throw new InvalidDataException(
                        $"Memory video '{videoFileName}' is declared more " +
                        "than once.");
                }
            }

            if (string.IsNullOrWhiteSpace(definition.Title)
                || !string.Equals(
                    definition.Title,
                    definition.Title.Trim(),
                    StringComparison.Ordinal)
                || definition.Title.Length > 40)
            {
                throw new InvalidDataException(
                    $"Memory '{definition.Id}' has invalid title " +
                    $"'{definition.Title}'.");
            }

            if (string.IsNullOrWhiteSpace(definition.Description)
                || !string.Equals(
                    definition.Description,
                    definition.Description.Trim(),
                    StringComparison.Ordinal)
                || definition.Description.Length > 320)
            {
                throw new InvalidDataException(
                    $"Memory '{definition.Id}' has invalid description.");
            }

            ValidateThumbnailFileName(
                definition.ThumbnailFileName,
                definition.Id);
            if (!thumbnailFileNames.Add(definition.ThumbnailFileName))
            {
                throw new InvalidDataException(
                    $"Memory thumbnail '{definition.ThumbnailFileName}' is " +
                    "declared more than once.");
            }

            previousThreshold =
                definition.UnlockAtAcceptedInteractionCount;
        }
    }

    private static void ValidateThumbnailFileName(
        string? thumbnailFileName,
        string memoryId)
    {
        if (string.IsNullOrWhiteSpace(thumbnailFileName)
            || !string.Equals(
                thumbnailFileName,
                thumbnailFileName.Trim(),
                StringComparison.Ordinal)
            || !string.Equals(
                Path.GetFileName(thumbnailFileName),
                thumbnailFileName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Memory '{memoryId}' has invalid thumbnail file name " +
                $"'{thumbnailFileName}'.");
        }

        string extension = Path.GetExtension(thumbnailFileName);
        if (!(extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
              || extension.Equals(
                  ".jpeg",
                  StringComparison.OrdinalIgnoreCase)
              || extension.Equals(
                  ".png",
                  StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Memory '{memoryId}' has invalid thumbnail file name " +
                $"'{thumbnailFileName}'.");
        }
    }

    private static void ValidateVideoFileName(
        string? videoFileName,
        string memoryId)
    {
        if (string.IsNullOrWhiteSpace(videoFileName)
            || !string.Equals(
                videoFileName,
                videoFileName.Trim(),
                StringComparison.Ordinal)
            || !string.Equals(
                Path.GetFileName(videoFileName),
                videoFileName,
                StringComparison.Ordinal)
            || !string.Equals(
                Path.GetExtension(videoFileName),
                ".mp4",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Memory '{memoryId}' has invalid video file name " +
                $"'{videoFileName}'.");
        }
    }
}
