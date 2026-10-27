namespace RacingSim;

/// <summary>
/// A new design is a gamble. On the drawing board its figures are only estimates, and the car
/// that comes out of the works can be better or, more often, worse; a better chief engineer
/// narrows the guess. And it arrives with teething troubles nobody has found yet, each costing
/// reliability until a test day or a race shows it up and the works fixes it.
/// </summary>
public static class Prototype
{
    /// <summary>How far the built car can stray from the estimate, as a share: wider with a weaker engineer.</summary>
    public static double Spread(int engineerSkill) => Math.Clamp(0.16 - 0.006 * engineerSkill, 0.04, 0.16);

    /// <summary>The estimate errs on the hopeful side: on average a car comes out this share of the spread worse.</summary>
    public const double Optimism = 0.2;

    /// <summary>Reliability each teething trouble costs until it is found and fixed.</summary>
    public const double FaultReliability = 0.04;

    /// <summary>Chance a test day shows up one of the faults, and that a race shows up each one.</summary>
    public const double TestFindChance = 0.6;
    public const double RaceFindChance = 0.35;

    private static readonly string[] Faults =
    [
        "a cracked oil pipe", "a weak half-shaft", "fuel starvation in long bends", "plugs that oiled up",
        "a gear selector that jumps out of gear", "brakes that pull to one side", "a frame that flexes at the scuttle",
        "a supercharger drive that shears", "a water pump that leaks", "a fuel tank that splits on its mounts",
        "steering that shimmies over bumps", "a clutch that slips when hot",
    ];

    /// <summary>
    /// What a figure can come out as, for the designer: the estimate give or take the spread,
    /// further on the bad side (lower power, more weight) because the estimate is hopeful.
    /// </summary>
    public static (int Low, int High) Range(int estimate, double spread, double weight = 1, bool higherIsBetter = true)
    {
        var (down, up) = higherIsBetter ? (1 + Optimism, 1 - Optimism) : (1 - Optimism, 1 + Optimism);
        return ((int)Math.Round(estimate * (1 - spread * weight * down)), (int)Math.Round(estimate * (1 + spread * weight * up)));
    }

    /// <summary>The teething troubles a design will most likely have: one, plus one for each part new to racing.</summary>
    public static int ExpectedFaults(CarDesign d, int year, int engineerSkill)
    {
        var novel = new[]
        {
            CarDesigner.FirstYear(d.Layout), CarDesigner.FirstYear(d.Supercharging), CarDesigner.FirstYear(d.Frame),
            CarDesigner.FirstYear(d.Suspension), CarDesigner.FirstYear(d.Brakes), CarDesigner.FirstYear(d.Body),
        }.Count(first => first > 1930 && year - first <= 1);
        var faults = 1 + novel
                     + (d.Supercharging == Supercharging.TwoStage ? 1 : 0)
                     + (d.Layout == EngineLayout.V16 ? 1 : 0)
                     + (d.Litres > 5 ? 1 : 0)
                     - (engineerSkill >= 15 ? 1 : 0);
        return Math.Clamp(faults, 1, 6);
    }

    /// <summary>
    /// The car that actually comes out of the works: the estimate moved by chance within the
    /// spread, and its teething troubles (which are already counted in its reliability).
    /// </summary>
    public static (Car Car, int Faults) Build(Car estimate, CarDesign d, int year, int engineerSkill, Random rng, bool teething = true)
    {
        var spread = Spread(engineerSkill);
        double Roll(double weight, double bias) => spread * weight * (2 * rng.NextDouble() - 1 - bias);
        var faults = teething ? Math.Clamp(ExpectedFaults(d, year, engineerSkill) + rng.Next(2), 1, 6) : 0;
        var car = estimate with
        {
            Horsepower = (int)Math.Round(estimate.Horsepower * (1 + Roll(1, Optimism))),
            WeightKg = (int)Math.Round(estimate.WeightKg * (1 - Roll(0.5, Optimism))),
            Handling = Math.Clamp(estimate.Handling + (int)Math.Round(Roll(12, Optimism)), 8, DrawingOffice.MaxHandling),
            Reliability = Math.Round(Math.Max(0.3, estimate.Reliability * (1 + Roll(0.5, Optimism)) - faults * FaultReliability), 2),
        };
        return (car, faults);
    }

    public static string Fault(Random rng) => Faults[rng.Next(Faults.Length)];
}

public sealed partial class Career
{
    /// <summary>Teething troubles in the team's own design not yet found.</summary>
    public int TeethingFaults { get; private set; }

    /// <summary>How far the designer's figures may be out, given the chief engineer.</summary>
    public double DesignSpread => Prototype.Spread(SkillOf(StaffRole.Engineer));

    public int ExpectedFaults(CarDesign design, int year) => Prototype.ExpectedFaults(design, year, SkillOf(StaffRole.Engineer));

    /// <summary>The car a design turns out to be, rolled when it is ordered so a reload cannot change it.</summary>
    private (Car Car, int Faults) BuildPrototype(DesignPreview preview, CarDesign design)
    {
        var rng = new Random(Seeds.Mix(Seed, Today.DayNumber * 13 + preview.Cost + (preview.Indy ? 1 : 0)));
        // The Indianapolis car is not tested between races, so it carries no teething troubles to find.
        return Prototype.Build(preview.Car, design, preview.Year, SkillOf(StaffRole.Engineer), rng, teething: !preview.Indy);
    }

    /// <summary>A test day or a race may show up a teething trouble, which the works then fixes.</summary>
    private bool FindFault(Random rng, double chance, DateOnly date, string how)
    {
        if (TeethingFaults == 0 || rng.NextDouble() >= chance)
            return false;
        TeethingFaults--;
        Car = Car with { Reliability = Math.Round(Math.Min(DrawingOffice.MaxReliability, Car.Reliability + Prototype.FaultReliability), 4) };
        _news.Add(new NewsItem(date, $"{how} showed up {Prototype.Fault(rng)} on the {Car.Name}; the works fixes it. "
                                     + (TeethingFaults > 0 ? $"{TeethingFaults} teething trouble{(TeethingFaults == 1 ? "" : "s")} left to find." : "No teething troubles left.")));
        return true;
    }

    /// <summary>After a race the team ran its Grand Prix car in, each fault may have shown itself.</summary>
    private void LearnFromRace(RaceRecord record)
    {
        if (!record.Entered || TeethingFaults == 0 || record.Event.IsEndurance || RaceCarFor(record.Event) != Car)
            return;
        var rng = new Random(Seeds.Mix(Seed, RaceSeed(record.Event) + 17));
        for (var i = TeethingFaults; i > 0; i--)
            FindFault(rng, Prototype.RaceFindChance, record.Event.Date, $"The {record.Event.Name}");
    }
}
