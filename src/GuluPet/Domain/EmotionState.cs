namespace GuluPet.Domain;

public sealed class EmotionState
{
    public const double DefaultEnergy = 65;

    public const double DefaultSleepiness = 20;

    public const double DefaultBoredom = 15;

    public const double DefaultCuriosity = 50;

    public const double DefaultHappiness = 60;

    public const double DefaultStress = 10;

    public const double DefaultAffection = 40;

    public static TimeSpan ShortTermRegressionHalfLife { get; } =
        TimeSpan.FromMinutes(30);

    public double Energy { get; set; } = DefaultEnergy;

    public double Sleepiness { get; set; } = DefaultSleepiness;

    public double Boredom { get; set; } = DefaultBoredom;

    public double Curiosity { get; set; } = DefaultCuriosity;

    public double Happiness { get; set; } = DefaultHappiness;

    public double Stress { get; set; } = DefaultStress;

    public double Affection { get; set; } = DefaultAffection;

    public void Apply(EmotionDelta delta)
    {
        Energy = Clamp(Energy + delta.Energy);
        Sleepiness = Clamp(Sleepiness + delta.Sleepiness);
        Boredom = Clamp(Boredom + delta.Boredom);
        Curiosity = Clamp(Curiosity + delta.Curiosity);
        Happiness = Clamp(Happiness + delta.Happiness);
        Stress = Clamp(Stress + delta.Stress);
        Affection = Clamp(Affection + delta.Affection);
    }

    public void RegressShortTerm(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsed),
                elapsed,
                "Emotion regression elapsed time cannot be negative.");
        }

        if (elapsed == TimeSpan.Zero)
        {
            return;
        }

        double retained = Math.Pow(
            0.5,
            elapsed.TotalSeconds /
            ShortTermRegressionHalfLife.TotalSeconds);
        Energy = Regress(Energy, DefaultEnergy, retained);
        Sleepiness = Regress(Sleepiness, DefaultSleepiness, retained);
        Boredom = Regress(Boredom, DefaultBoredom, retained);
        Curiosity = Regress(Curiosity, DefaultCuriosity, retained);
        Happiness = Regress(Happiness, DefaultHappiness, retained);
        Stress = Regress(Stress, DefaultStress, retained);
    }

    public EmotionState CloneNormalized() =>
        new()
        {
            Energy = Clamp(Energy),
            Sleepiness = Clamp(Sleepiness),
            Boredom = Clamp(Boredom),
            Curiosity = Clamp(Curiosity),
            Happiness = Clamp(Happiness),
            Stress = Clamp(Stress),
            Affection = Clamp(Affection)
        };

    private static double Regress(
        double value,
        double baseline,
        double retained)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException(
                "Emotion values must be finite before regression.");
        }

        return Clamp(baseline + ((value - baseline) * retained));
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
}

public readonly record struct EmotionDelta(
    double Energy = 0,
    double Sleepiness = 0,
    double Boredom = 0,
    double Curiosity = 0,
    double Happiness = 0,
    double Stress = 0,
    double Affection = 0);
