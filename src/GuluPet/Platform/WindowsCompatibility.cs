namespace GuluPet.Platform;

internal static class WindowsCompatibility
{
    internal const int MinimumSupportedBuild = 17763;
    internal const int Windows11InitialBuild = 22000;

    private static readonly Version MinimumSupportedVersion =
        new(10, 0, MinimumSupportedBuild);
    private static readonly Version Windows11InitialVersion =
        new(10, 0, Windows11InitialBuild);

    internal static bool IsCurrentOperatingSystemSupported =>
        OperatingSystem.IsWindowsVersionAtLeast(
            10,
            0,
            MinimumSupportedBuild);

    internal static bool SupportsCurrentDwmRoundedCornerPreference =>
        OperatingSystem.IsWindowsVersionAtLeast(
            10,
            0,
            Windows11InitialBuild);

    internal static bool IsSupported(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version >= MinimumSupportedVersion;
    }

    internal static bool SupportsDwmRoundedCornerPreference(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version >= Windows11InitialVersion;
    }
}
