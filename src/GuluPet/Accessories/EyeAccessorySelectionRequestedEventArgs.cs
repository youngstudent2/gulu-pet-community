namespace GuluPet.Accessories;

/// <summary>
/// Requests a runtime eye-accessory selection. A null id removes the current
/// accessory.
/// </summary>
public sealed class EyeAccessorySelectionRequestedEventArgs(
    string? id) : EventArgs
{
    public string? Id { get; } = id;
}
