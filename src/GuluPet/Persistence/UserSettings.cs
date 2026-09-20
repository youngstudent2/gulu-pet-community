namespace GuluPet.Persistence;

/// <summary>
/// Small, user-local settings document. Window coordinates are physical
/// desktop pixels so a saved position remains meaningful across WPF DPI modes.
/// </summary>
public sealed class UserSettings
{
    public const int CurrentWindowCoordinateSpaceVersion = 1;

    private List<DateOnly> _readDiaryDates = [];

    public bool AlwaysOnTop { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool TickScoreLoggingEnabled { get; set; }

    public string? SelectedEyeAccessoryId { get; set; }

    /// <summary>
    /// Distinguishes an upgraded installation from one that has not yet had
    /// its historical diary pages reconciled. Missing legacy JSON therefore
    /// remains intentionally false.
    /// </summary>
    public bool DiaryReadTrackingInitialized { get; set; }

    /// <summary>
    /// Diary dates treated as read, either through the one-time migration or
    /// because the user opened them. The setter tolerates a null value from
    /// hand-edited or older settings JSON.
    /// </summary>
    public List<DateOnly> ReadDiaryDates
    {
        get => _readDiaryDates;
        set => _readDiaryDates = value ?? [];
    }

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public int WindowCoordinateSpaceVersion { get; set; }

    public bool NormalizeWindowCoordinateSpace()
    {
        if (WindowCoordinateSpaceVersion
            == CurrentWindowCoordinateSpaceVersion)
        {
            return false;
        }

        WindowLeft = null;
        WindowTop = null;
        WindowCoordinateSpaceVersion =
            CurrentWindowCoordinateSpaceVersion;
        return true;
    }

    public static UserSettings CreateDefault() =>
        new()
        {
            WindowCoordinateSpaceVersion =
                CurrentWindowCoordinateSpaceVersion,
        };
}
