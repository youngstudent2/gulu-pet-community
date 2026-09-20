using System.IO;

namespace GuluPet.Sensing;

internal static class ApplicationCategoryClassifier
{
    private static readonly IReadOnlyDictionary<string, string> ExactMappings =
        BuildMappings();

    internal static string Classify(string? processName)
    {
        string normalized = NormalizeProcessName(processName);
        if (normalized.Length == 0)
        {
            return "unknown";
        }

        if (ExactMappings.TryGetValue(normalized, out string? category))
        {
            return category;
        }

        if (normalized.EndsWith("-shipping", StringComparison.Ordinal)
            || normalized.Contains("unityplayer", StringComparison.Ordinal)
            || normalized.StartsWith("game-", StringComparison.Ordinal))
        {
            return "game";
        }

        return "unknown";
    }

    internal static string NormalizeCategory(string? category) =>
        category?.Trim().ToLowerInvariant() switch
        {
            "browser" => "browser",
            "media" or "media_player" => "media_player",
            "office" => "office",
            "ide" => "ide",
            "game" => "game",
            "communication" => "communication",
            "file_manager" => "file_manager",
            _ => "unknown",
        };

    private static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        string fileName = Path.GetFileName(processName.Trim());
        string withoutExtension = fileName.EndsWith(
            ".exe",
            StringComparison.OrdinalIgnoreCase)
                ? fileName[..^4]
                : fileName;
        return withoutExtension.ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, string> BuildMappings()
    {
        var mappings = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        Add(
            mappings,
            "browser",
            "chrome",
            "msedge",
            "firefox",
            "brave",
            "opera",
            "opera_gx",
            "vivaldi",
            "iexplore",
            "arc",
            "360se",
            "360chrome",
            "qqbrowser",
            "sogouexplorer",
            "liebao");
        Add(
            mappings,
            "media_player",
            "vlc",
            "potplayer",
            "potplayermini64",
            "mpv",
            "wmplayer",
            "foobar2000",
            "spotify",
            "cloudmusic",
            "qqmusic",
            "kugou",
            "kwmusic",
            "bilibili");
        Add(
            mappings,
            "office",
            "winword",
            "excel",
            "powerpnt",
            "outlook",
            "onenote",
            "msaccess",
            "wps",
            "et",
            "wpp",
            "acrobat",
            "acrord32",
            "foxitpdfreader");
        Add(
            mappings,
            "ide",
            "devenv",
            "code",
            "codium",
            "rider64",
            "idea64",
            "pycharm64",
            "webstorm64",
            "clion64",
            "goland64",
            "studio64",
            "androidstudio",
            "sublime_text",
            "notepad++");
        Add(
            mappings,
            "game",
            "steam",
            "steamwebhelper",
            "epicgameslauncher",
            "wegame",
            "riotclientservices",
            "battle.net",
            "genshinimpact",
            "yuanshen",
            "starrail",
            "zenlesszonezero",
            "eldenring",
            "cs2",
            "dota2",
            "valorant-win64-shipping",
            "overwatch");
        Add(
            mappings,
            "communication",
            "wechat",
            "wechatapp",
            "weixin",
            "qq",
            "tim",
            "telegram",
            "discord",
            "slack",
            "teams",
            "ms-teams",
            "feishu",
            "lark",
            "dingtalk",
            "zoom",
            "skype");
        Add(
            mappings,
            "file_manager",
            "explorer",
            "totalcmd",
            "totalcmd64",
            "onecommander",
            "files",
            "everything");
        return mappings;
    }

    private static void Add(
        IDictionary<string, string> mappings,
        string category,
        params string[] processNames)
    {
        foreach (string processName in processNames)
        {
            mappings.Add(processName, category);
        }
    }
}
