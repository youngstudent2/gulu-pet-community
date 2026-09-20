using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class AuxiliarySurfacePolicyTests
{
    public static void RunAll()
    {
        Run(
            nameof(OpeningOneSurfaceClosesEveryOtherSurface),
            OpeningOneSurfaceClosesEveryOtherSurface);
    }

    private static void OpeningOneSurfaceClosesEveryOtherSurface()
    {
        AuxiliarySurface[] surfaces =
            Enum.GetValues<AuxiliarySurface>();
        foreach (AuxiliarySurface opening in surfaces)
        {
            IReadOnlyList<AuxiliarySurface> closing =
                AuxiliarySurfacePolicy.GetSurfacesToClose(opening);
            BehaviorTestCheck.Equal(surfaces.Length - 1, closing.Count);
            BehaviorTestCheck.False(closing.Contains(opening));
            BehaviorTestCheck.SequenceEqual(
                surfaces.Where(surface => surface != opening),
                closing);
        }

        BehaviorTestCheck.SequenceEqual(
            surfaces,
            AuxiliarySurfacePolicy.GetAllSurfaces());
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(AuxiliarySurfacePolicyTests)}.{name}");
    }
}
