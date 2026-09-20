using System.Xml.Linq;
using GuluPet.Platform;

namespace GuluPet.Tests;

internal static class WindowsCompatibilityContractTests
{
    private const string Windows10And11CompatibilityId =
        "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}";

    public static void RunAll()
    {
        Run(
            nameof(SupportStartsAtWindows10Version1809),
            SupportStartsAtWindows10Version1809);
        Run(
            nameof(Windows11RoundedCornersAreOptional),
            Windows11RoundedCornersAreOptional);
        Run(
            nameof(InstallerAndProjectShareTheWindows10Floor),
            InstallerAndProjectShareTheWindows10Floor);
        Run(
            nameof(ApplicationManifestDeclaresWindows10And11),
            ApplicationManifestDeclaresWindows10And11);
        Run(
            nameof(StartupAndPostcardsUseCompatibilityGuards),
            StartupAndPostcardsUseCompatibilityGuards);
    }

    private static void SupportStartsAtWindows10Version1809()
    {
        BehaviorTestCheck.False(
            WindowsCompatibility.IsSupported(new Version(10, 0, 17134)));
        BehaviorTestCheck.True(
            WindowsCompatibility.IsSupported(new Version(10, 0, 17763)));
        BehaviorTestCheck.True(
            WindowsCompatibility.IsSupported(new Version(10, 0, 19045)));
        BehaviorTestCheck.True(
            WindowsCompatibility.IsSupported(new Version(10, 0, 22000)));
    }

    private static void Windows11RoundedCornersAreOptional()
    {
        BehaviorTestCheck.False(
            WindowsCompatibility.SupportsDwmRoundedCornerPreference(
                new Version(10, 0, 19045)));
        BehaviorTestCheck.True(
            WindowsCompatibility.SupportsDwmRoundedCornerPreference(
                new Version(10, 0, 22000)));
    }

    private static void InstallerAndProjectShareTheWindows10Floor()
    {
        string root = FindRepositoryRoot();
        string installer = File.ReadAllText(Path.Combine(
            root,
            "installer",
            "GuluPet.iss"));
        string project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "GuluPet.csproj"));

        BehaviorTestCheck.True(installer.Contains(
            "MinVersion=10.0.17763",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(installer.Contains(
            "MinVersion=10.0.22000",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(project.Contains(
            "<TargetFramework>net10.0-windows10.0.17763.0</TargetFramework>",
            StringComparison.Ordinal));
    }

    private static void ApplicationManifestDeclaresWindows10And11()
    {
        string root = FindRepositoryRoot();
        XDocument manifest = XDocument.Load(Path.Combine(
            root,
            "src",
            "GuluPet",
            "app.manifest"));
        XNamespace compatibility =
            "urn:schemas-microsoft-com:compatibility.v1";
        string[] supportedOperatingSystems = manifest
            .Descendants(compatibility + "supportedOS")
            .Select(element => element.Attribute("Id")?.Value ?? string.Empty)
            .ToArray();

        BehaviorTestCheck.SequenceEqual(
            new[] { Windows10And11CompatibilityId },
            supportedOperatingSystems);
    }

    private static void StartupAndPostcardsUseCompatibilityGuards()
    {
        string root = FindRepositoryRoot();
        string app = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "App.xaml.cs"));
        string chrome = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Platform",
            "DwmWindowChrome.cs"));
        string postcard = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GuluPet",
            "Presentation",
            "PostcardUnlockWindow.xaml.cs"));

        BehaviorTestCheck.True(app.Contains(
            "WindowsCompatibility.IsCurrentOperatingSystemSupported",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(app.Contains(
            "Windows 10 1809",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(chrome.Contains(
            "SupportsCurrentDwmRoundedCornerPreference",
            StringComparison.Ordinal));
        BehaviorTestCheck.True(postcard.Contains(
            "DwmWindowChrome.TryApplyRoundedCorners(handle)",
            StringComparison.Ordinal));
        BehaviorTestCheck.False(postcard.Contains(
            "DwmwaWindowCornerPreference",
            StringComparison.Ordinal));
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
            $"PASS {nameof(WindowsCompatibilityContractTests)}.{name}");
    }
}
