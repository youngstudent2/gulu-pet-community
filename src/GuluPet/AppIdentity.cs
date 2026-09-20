using System.IO;

namespace GuluPet;

/// <summary>
/// Centralizes the public application's stable operating-system identity.
/// Forks can change these values once instead of hunting through persistence,
/// startup, single-instance, and installer integrations.
/// </summary>
public static class AppIdentity
{
    public const string DisplayName = "Gulu Pet Community";
    public const string ExecutableFileName = "GuluPet.Community.exe";
    public const string DataDirectoryName = "GuluPetCommunity";
    public const string StartupValueName = "GuluPetCommunity";
    public const string ObjectNamespace = "GuluPet.Community.DesktopPet";

    public static string LocalDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DataDirectoryName);
}
