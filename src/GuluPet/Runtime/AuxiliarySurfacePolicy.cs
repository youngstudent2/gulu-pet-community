namespace GuluPet.Runtime;

internal enum AuxiliarySurface
{
    Postcards,
    Diary,
    Feedback,
    Behaviors,
    Memories,
    MemoryOffer,
    MemoryPlayback,
}

internal static class AuxiliarySurfacePolicy
{
    private static readonly AuxiliarySurface[] AllSurfaces =
        Enum.GetValues<AuxiliarySurface>();

    internal static IReadOnlyList<AuxiliarySurface> GetSurfacesToClose(
        AuxiliarySurface opening) =>
        AllSurfaces
            .Where(surface => surface != opening)
            .ToArray();

    internal static IReadOnlyList<AuxiliarySurface> GetAllSurfaces() =>
        AllSurfaces.ToArray();
}
