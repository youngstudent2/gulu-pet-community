namespace GuluPet.Behavior;

public interface IRandomSource
{
    double NextUnit();
}

public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _random;

    public SystemRandomSource(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    public double NextUnit() => _random.NextDouble();
}
