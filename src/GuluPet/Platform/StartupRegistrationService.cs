using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace GuluPet.Platform;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _valueName;
    private readonly string _launchCommand;

    public StartupRegistrationService(
        string valueName = global::GuluPet.AppIdentity.StartupValueName,
        string? executablePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        _valueName = valueName;
        _launchCommand = BuildLaunchCommand(executablePath);
    }

    public bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(_valueName) is string command
                && string.Equals(
                    command.Trim(),
                    _launchCommand,
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                RunKeyPath,
                writable: true);
            key.SetValue(_valueName, _launchCommand, RegistryValueKind.String);
            return;
        }

        using RegistryKey? existingKey = Registry.CurrentUser.OpenSubKey(
            RunKeyPath,
            writable: true);
        existingKey?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    private static string BuildLaunchCommand(string? explicitExecutablePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitExecutablePath))
        {
            return $"\"{Path.GetFullPath(explicitExecutablePath)}\" --startup";
        }

        string processPath = Path.GetFullPath(
            Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "Unable to determine the GuluPet executable path."));
        string? entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        bool isDotnetHost = string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);

        return isDotnetHost
            && !string.IsNullOrWhiteSpace(entryAssemblyPath)
            && string.Equals(
                Path.GetExtension(entryAssemblyPath),
                ".dll",
                StringComparison.OrdinalIgnoreCase)
                ? $"\"{processPath}\" \"{Path.GetFullPath(entryAssemblyPath)}\" --startup"
                : $"\"{processPath}\" --startup";
    }
}
