namespace GuluPet.Postcards;

/// <summary>
/// A validated, packaged postcard. <see cref="ImagePath"/> is an absolute
/// path resolved from the catalog directory.
/// </summary>
public sealed record PostcardDefinition(
    string Id,
    string Title,
    string Location,
    string ImagePath,
    int UnlockOrder,
    string AltText,
    string Chapter = "");
