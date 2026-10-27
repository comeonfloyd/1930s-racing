namespace RacingSim;

public enum Upgrade
{
    Engine,
    Lightening,
    Handling,
    Reliability,
    NewCar,
}

/// <summary>One kind of work the drawing office can do on the car, with its base cost and time.</summary>
public sealed record UpgradeKind(Upgrade Id, string Name, string Effect, int BaseCost, int Weeks, string RivalNews);

/// <summary>A project in the drawing office: paid for when queued, done after its weeks of work.</summary>
public sealed record Project(Upgrade Upgrade, int Cost, int Weeks)
{
    public int WeeksDone { get; init; }

    /// <summary>For a new car: the year of the works model being built.</summary>
    public int ModelYear { get; init; }

    /// <summary>For a car of the team's own design: the design, and the car it makes.</summary>
    public CarDesign? Design { get; init; }
    public Car? Built { get; init; }

    /// <summary>The teething troubles the car of an own design will come out with.</summary>
    public int Faults { get; init; }

    /// <summary>The project is the team's Indianapolis car, built beside the Grand Prix car.</summary>
    public bool Indy { get; init; }
    public double Progress => Weeks == 0 ? 1 : WeeksDone / (double)Weeks;
}

/// <summary>
/// The car's development: work is done one project at a time, in queue order, and every finished
/// project of a kind makes the next one of that kind dearer. Numbers are first-pass placeholders.
/// </summary>
public static class DrawingOffice
{
    /// <summary>Engine work adds this share of the car's power, and at least <see cref="MinHorsepowerStep"/>.</summary>
    public const double HorsepowerShare = 0.05;
    public const int MinHorsepowerStep = 8;
    public const int WeightStep = 20;
    public const int HandlingStep = 1;
    public const double ReliabilityStep = 0.04;
    public const double MaxReliability = 0.95;
    public const int MinWeightKg = 680;
    public const int MaxHandling = 18;

    /// <summary>Each finished project of a kind raises the next one's cost by this factor.</summary>
    public const double CostGrowth = 1.5;

    public static readonly IReadOnlyList<UpgradeKind> Kinds =
    [
        new(Upgrade.Engine, "Engine tuning", $"+{HorsepowerShare * 100:0}% power", 1800, 5, "more power from a reworked engine"),
        new(Upgrade.Lightening, "Lighter chassis", $"−{WeightStep} kg", 1500, 6, "a lighter chassis"),
        new(Upgrade.Handling, "Suspension and steering", $"+{HandlingStep} handling", 1400, 5, "new suspension and steering"),
        new(Upgrade.Reliability, "Reliability work", $"+{ReliabilityStep * 100:0}% reliability", 1000, 4, "a stronger, more reliable car"),
        new(Upgrade.NewCar, "New car", "the works' next model", 6000, 14, "a new car"),
    ];

    /// <summary>Work the other works choose from each week; their new cars come from the data.</summary>
    public static IEnumerable<UpgradeKind> Tuning => Kinds.Where(k => k.Id != Upgrade.NewCar);

    public static int HorsepowerStep(Car car) => Math.Max(MinHorsepowerStep, (int)Math.Round(car.Horsepower * HorsepowerShare));

    /// <summary>
    /// Chance each week that a rival works finishes a piece of work: about six a season for a
    /// team with £10,000, fewer for a poorer one. Works the player keeps beating push harder:
    /// each win the player has had this season adds a tenth to the chance.
    /// </summary>
    public static double RivalWeeklyChance(int startingCash, int playerWins = 0) =>
        Math.Min(0.25, Math.Clamp(startingCash / 80000.0, 0.03, 0.15) * (1 + 0.1 * playerWins));

    /// <summary>
    /// A new car to new rules costs this share of the usual price when the old car is ruled out:
    /// the maker pays the rest.
    /// </summary>
    public const double NewRulesCarShare = 0.5;

    public static UpgradeKind Kind(Upgrade upgrade) => Kinds.First(k => k.Id == upgrade);

    public static int Cost(Upgrade upgrade, int timesDone) =>
        (int)(Math.Round(Kind(upgrade).BaseCost * Math.Pow(CostGrowth, timesDone) / 50) * 50);

    /// <summary>Engine work gets at most this much more power than the car was designed with.</summary>
    public const double MaxPowerGrowth = 1.25;

    /// <summary>
    /// False when the car is already at the limit for this kind of work. The design is the car
    /// as first built, which caps engine work. A new car is decided by the career, which knows
    /// the works' models (<see cref="Career.NextModel"/>).
    /// </summary>
    public static bool CanImprove(Car car, Upgrade upgrade, Car? design = null) => upgrade switch
    {
        Upgrade.Engine => design is null || car.Horsepower + HorsepowerStep(car) <= design.Horsepower * MaxPowerGrowth,
        Upgrade.Lightening => car.WeightKg - WeightStep >= MinWeightKg,
        Upgrade.Handling => car.Handling + HandlingStep <= MaxHandling,
        Upgrade.Reliability => car.Reliability < MaxReliability,
        Upgrade.NewCar => false,
        _ => true,
    };

    public static Car Apply(Car car, Upgrade upgrade) => upgrade switch
    {
        Upgrade.Engine => car with { Horsepower = car.Horsepower + HorsepowerStep(car) },
        Upgrade.Lightening => car with { WeightKg = Math.Max(MinWeightKg, car.WeightKg - WeightStep) },
        Upgrade.Handling => car with { Handling = Math.Min(MaxHandling, car.Handling + HandlingStep) },
        Upgrade.Reliability => car with { Reliability = Math.Min(MaxReliability, car.Reliability + ReliabilityStep) },
        _ => car,
    };
}
