using GuluPet.Presentation;

namespace GuluPet.Tests;

internal static class SettingsWindowContractTests
{
    public static void RunAll()
    {
        Run(nameof(HasNoUserApiConfigurationSurface),
            HasNoUserApiConfigurationSurface);
        Run(nameof(HasThemedFeedbackInputAndHonestActions),
            HasThemedFeedbackInputAndHonestActions);
    }

    private static void HasThemedFeedbackInputAndHonestActions()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "SettingsWindow.xaml"));

        BehaviorTestCheck.True(
            xaml.Contains("x:Name=\"FeedbackTextBox\"", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            xaml.Contains("MaxLength=\"2000\"", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            xaml.Contains("x:Key=\"FeedbackButtonStyle\"", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            xaml.Contains("Content=\"发送反馈与排查信息\"", StringComparison.Ordinal));
        BehaviorTestCheck.True(
            xaml.Contains("Content=\"打开排查文件夹\"", StringComparison.Ordinal));
        BehaviorTestCheck.False(
            xaml.Contains("把问题告诉我们", StringComparison.Ordinal));
    }

    private static void HasNoUserApiConfigurationSurface()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "SettingsWindow.xaml"));
        string codeBehind = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "SettingsWindow.xaml.cs"));

        string[] forbidden =
        [
            "PasswordBox",
            "API Key",
            "OnSaveApiKeyClicked",
            "SaveApiKey",
            "加密保存",
            "替换密钥",
        ];
        foreach (string value in forbidden)
        {
            BehaviorTestCheck.False(
                xaml.Contains(value, StringComparison.OrdinalIgnoreCase));
            BehaviorTestCheck.False(
                codeBehind.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        Type[] constructorParameters = typeof(SettingsWindow)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        BehaviorTestCheck.Equal(2, constructorParameters.Length);
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
            $"PASS {nameof(SettingsWindowContractTests)}.{name}");
    }
}
