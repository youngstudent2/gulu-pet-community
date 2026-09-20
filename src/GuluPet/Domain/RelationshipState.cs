using System.IO;
using System.Text.Json.Serialization;

namespace GuluPet.Domain;

public static class RelationshipStages
{
    public const string New = "new";
    public const string Familiar = "familiar";
    public const string Close = "close";
    public const string Family = "family";

    public static string FromAffection(double affection)
    {
        if (!double.IsFinite(affection) || affection is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(affection),
                affection,
                "Relationship affection must be finite and between 0 and 100.");
        }

        return affection switch
        {
            < 25 => New,
            < 50 => Familiar,
            < 75 => Close,
            _ => Family,
        };
    }
}

/// <summary>
/// Durable relationship state. Short-term emotions intentionally do not cross
/// process boundaries.
/// </summary>
public sealed record RelationshipState
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonRequired]
    public double Affection { get; init; } = EmotionState.DefaultAffection;

    [JsonRequired]
    public DateTimeOffset UpdatedAtUtc { get; init; }

    [JsonIgnore]
    public string Stage => RelationshipStages.FromAffection(Affection);

    public static RelationshipState CreateDefault(
        DateTimeOffset? now = null) =>
        new()
        {
            UpdatedAtUtc = (now ?? DateTimeOffset.UtcNow).ToUniversalTime(),
        };

    public static RelationshipState Create(
        double affection,
        DateTimeOffset updatedAtUtc) =>
        ValidateAndSnapshot(
            new RelationshipState
            {
                Affection = affection,
                UpdatedAtUtc = updatedAtUtc,
            });

    internal static RelationshipState ValidateAndSnapshot(
        RelationshipState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported relationship state schema version " +
                $"'{state.SchemaVersion}'; expected " +
                $"'{CurrentSchemaVersion}'.");
        }

        if (!double.IsFinite(state.Affection) ||
            state.Affection is < 0 or > 100)
        {
            throw new InvalidDataException(
                "Relationship affection must be finite and between 0 and 100.");
        }

        if (state.UpdatedAtUtc == default)
        {
            throw new InvalidDataException(
                "Relationship state must declare updatedAtUtc.");
        }

        return state with
        {
            UpdatedAtUtc = state.UpdatedAtUtc.ToUniversalTime(),
        };
    }
}
