namespace RacingSim;

public enum Upgrade
{
    Engine,
    Lightening,
    Handling,
    Reliability,
}

/// <summary>One kind of work the drawing office can do on the car, with its base cost and time.</summary>
public sealed record UpgradeKind(Upgrade Id, string Name, string Effect, int BaseCost, int Weeks, string RivalNews);

/// <summary>A project in the drawing office: paid for when queued, done after its weeks of work.</summary>
public sealed record Project(Upgrade Upgrade, int Cost, int Weeks)
{
    public int WeeksDone { get; init; }
    public double Progress => Weeks == 0 ? 1 : WeeksDone / (double)Weeks;
}

/// <summary>
/// The car's development: work is done one project at a time, in queue order, and every finished
/// project of a kind makes the next one of that kind dearer. Numbers are first-pass placeholders.
/// </summary>
public static class DrawingOffice
{
    public const int HorsepowerStep = 8;
    public const int WeightStep = 20;
    public const int HandlingStep = 1;
    public const double ReliabilityStep = 0.04;
    public const double MaxReliability = 0.95;
    public const int MinWeightKg = 650;
    public const int MaxHandling = 20;

    /// <summary>Each finished project of a kind raises the next one's cost by this factor.</summary>
    public const double CostGrowth = 1.3;

    public static readonly IReadOnlyList<UpgradeKind> Kinds =
    [
        new(Upgrade.Engine, "Engine tuning", $"+{HorsepowerStep} hp", 1800, 5, "more power from a reworked engine"),
        new(Upgrade.Lightening, "Lighter chassis", $"−{WeightStep} kg", 1500, 6, "a lighter chassis"),
        new(Upgrade.Handling, "Suspension and steering", $"+{HandlingStep} handling", 1400, 5, "new suspension and steering"),
        new(Upgrade.Reliability, "Reliability work", $"+{ReliabilityStep * 100:0}% reliability", 1000, 4, "a stronger, more reliable car"),
    ];

    /// <summary>
    /// Chance each week that a rival works finishes a piece of work: about four a season for a
    /// team with £10,000, fewer for a poorer one.
    /// </summary>
    public static double RivalWeeklyChance(int startingCash) => Math.Clamp(startingCash / 130000.0, 0.02, 0.15);

    public static UpgradeKind Kind(Upgrade upgrade) => Kinds.First(k => k.Id == upgrade);

    public static int Cost(Upgrade upgrade, int timesDone) =>
        (int)(Math.Round(Kind(upgrade).BaseCost * Math.Pow(CostGrowth, timesDone) / 50) * 50);

    /// <summary>False when the car is already at the limit for this kind of work.</summary>
    public static bool CanImprove(Car car, Upgrade upgrade) => upgrade switch
    {
        Upgrade.Lightening => car.WeightKg - WeightStep >= MinWeightKg,
        Upgrade.Handling => car.Handling + HandlingStep <= MaxHandling,
        Upgrade.Reliability => car.Reliability < MaxReliability,
        _ => true,
    };

    public static Car Apply(Car car, Upgrade upgrade) => upgrade switch
    {
        Upgrade.Engine => car with { Horsepower = car.Horsepower + HorsepowerStep },
        Upgrade.Lightening => car with { WeightKg = Math.Max(MinWeightKg, car.WeightKg - WeightStep) },
        Upgrade.Handling => car with { Handling = Math.Min(MaxHandling, car.Handling + HandlingStep) },
        Upgrade.Reliability => car with { Reliability = Math.Min(MaxReliability, car.Reliability + ReliabilityStep) },
        _ => car,
    };
}
