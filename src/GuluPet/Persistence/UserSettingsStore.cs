using System.IO;
using System.Text.Json;

namespace GuluPet.Persistence;

public sealed class UserSettingsStore
{
    private const string SettingsFileName = "settings.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public UserSettingsStore()
        : this(Path.Combine(
            global::GuluPet.AppIdentity.LocalDataDirectory,
            SettingsFileName))
    {
    }

    public UserSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        SettingsPath = Path.GetFullPath(settingsPath);
    }

    public string SettingsPath { get; }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return UserSettings.CreateDefault();
            }

            string json = File.ReadAllText(SettingsPath);
            UserSettings? settings = JsonSerializer.Deserialize<UserSettings>(
                json,
                SerializerOptions);
            settings ??= UserSettings.CreateDefault();
            settings.NormalizeWindowCoordinateSpace();
            return settings;
        }
        catch (IOException)
        {
            return UserSettings.CreateDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return UserSettings.CreateDefault();
        }
        catch (JsonException)
        {
            return UserSettings.CreateDefault();
        }
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.NormalizeWindowCoordinateSpace();

        string? directory = Path.GetDirectoryName(SettingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The settings path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(SettingsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            string json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(temporaryPath, json);

            if (File.Exists(SettingsPath))
            {
                try
                {
                    File.Replace(temporaryPath, SettingsPath, null, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, SettingsPath, overwrite: true);
                }
                catch (IOException)
                {
                    File.Move(temporaryPath, SettingsPath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
