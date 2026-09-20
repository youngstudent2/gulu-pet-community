using System.IO;
using System.Text.Json.Serialization;

namespace GuluPet.Care;

/// <summary>
/// Durable food and water levels. Elapsed time is intentionally absent from
/// the document: only explicit online-time observations may deplete care.
/// </summary>
public sealed record CareState
{
    public const int CurrentSchemaVersion = 1;
    public const double FullLevel = 100;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonRequired]
    public double FoodLevel { get; init; } = FullLevel;

    [JsonRequired]
    public double WaterLevel { get; init; } = FullLevel;

    public static CareState CreateDefault() => new();

    public static CareState Create(
        double foodLevel,
        double waterLevel) =>
        ValidateAndSnapshot(
            new CareState
            {
                FoodLevel = foodLevel,
                WaterLevel = waterLevel,
            });

    internal static CareState ValidateAndSnapshot(CareState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported care state schema version " +
                $"'{state.SchemaVersion}'; expected " +
                $"'{CurrentSchemaVersion}'.");
        }

        ValidateLevel(state.FoodLevel, "food");
        ValidateLevel(state.WaterLevel, "water");
        return state with { };
    }

    private static void ValidateLevel(double level, string name)
    {
        if (!double.IsFinite(level) || level is < 0 or > FullLevel)
        {
            throw new InvalidDataException(
                $"Care {name} level must be finite and between 0 and " +
                $"{FullLevel}.");
        }
    }
}
