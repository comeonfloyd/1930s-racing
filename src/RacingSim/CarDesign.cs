using System.Text.Json.Serialization;

namespace RacingSim;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EngineLayout { Straight6, Straight8, V8, V12, V16 }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Supercharging { None, Single, TwoStage }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Frame { Ladder, BoxedLadder, Tubular }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Suspension { BeamAxles, IndependentFront, AllIndependent }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Brakes { Mechanical, Hydraulic }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Body { OpenWheel, Streamlined }

/// <summary>
/// A car of the team's own design: the engine (layout, size, supercharging) and the chassis
/// (frame, suspension, brakes, body). <see cref="CarDesigner.Build"/> turns it into the
/// car's figures for the year it is built.
/// </summary>
public sealed record CarDesign(
    EngineLayout Layout,
    double Litres,
    Supercharging Supercharging,
    Frame Frame,
    Suspension Suspension,
    Brakes Brakes,
    Body Body);

/// <summary>
/// How a design becomes a car. Engineering moves on through the decade: each part has a first
/// year, and engines give more power per litre every year. The numbers are calibrated so the
/// real cars come out close to their figures in teams.json, e.g. a 1934 supercharged 3.4-litre
/// straight-8 on a boxed frame with independent front suspension is about a Mercedes W25.
/// </summary>
public static class CarDesigner
{
    public const double MinLitres = 1.5, MaxLitres = 7.0, LitreStep = 0.25;

    /// <summary>From 1938 the 3-litre formula also set a minimum weight; designs are ballasted up to it.</summary>
    public const int MinWeightFrom1938 = 850;

    public static int FirstYear(EngineLayout layout) => layout switch
    {
        EngineLayout.V16 => 1931,
        EngineLayout.V8 => 1933,
        EngineLayout.V12 => 1935,
        _ => 1930,
    };

    public static int FirstYear(Supercharging s) => s == Supercharging.TwoStage ? 1937 : 1930;

    public static int FirstYear(Frame f) => f switch { Frame.BoxedLadder => 1932, Frame.Tubular => 1934, _ => 1930 };

    public static int FirstYear(Suspension s) => s switch { Suspension.IndependentFront => 1934, Suspension.AllIndependent => 1935, _ => 1930 };

    public static int FirstYear(Brakes b) => b == Brakes.Hydraulic ? 1934 : 1930;

    public static int FirstYear(Body b) => b == Body.Streamlined ? 1934 : 1930;

    /// <summary>The parts not yet invented in a year, by name; empty when the design can be built.</summary>
    public static IReadOnlyList<string> TooEarly(CarDesign d, int year)
    {
        var late = new List<string>();
        void Check(int first, string name)
        {
            if (year < first)
                late.Add($"{name} (from {first})");
        }
        Check(FirstYear(d.Layout), Name(d.Layout));
        Check(FirstYear(d.Supercharging), Name(d.Supercharging));
        Check(FirstYear(d.Frame), Name(d.Frame));
        Check(FirstYear(d.Suspension), Name(d.Suspension));
        Check(FirstYear(d.Brakes), Name(d.Brakes));
        Check(FirstYear(d.Body), Name(d.Body));
        return late;
    }

    public static string Name(EngineLayout l) => l switch
    {
        EngineLayout.Straight6 => "Straight-6",
        EngineLayout.Straight8 => "Straight-8",
        _ => l.ToString(),
    };

    public static string Name(Supercharging s) => s switch
    {
        Supercharging.None => "Unsupercharged",
        Supercharging.Single => "Supercharged",
        _ => "Two-stage supercharger",
    };

    public static string Name(Frame f) => f switch
    {
        Frame.Ladder => "Ladder frame",
        Frame.BoxedLadder => "Boxed ladder frame",
        _ => "Tubular frame",
    };

    public static string Name(Suspension s) => s switch
    {
        Suspension.BeamAxles => "Beam axles",
        Suspension.IndependentFront => "Independent front",
        _ => "All-independent",
    };

    public static string Name(Brakes b) => b == Brakes.Hydraulic ? "Hydraulic brakes" : "Cable brakes";

    public static string Name(Body b) => b == Body.Streamlined ? "Streamlined body" : "Open-wheel body";

    private static int Cylinders(EngineLayout l) => l switch
    {
        EngineLayout.Straight6 => 6,
        EngineLayout.Straight8 or EngineLayout.V8 => 8,
        EngineLayout.V12 => 12,
        _ => 16,
    };

    /// <summary>Unsupercharged horsepower per litre in 1930, rising every year as engines rev higher.</summary>
    private static double PowerPerLitre(int year) => 44 * Math.Pow(1.045, year - 1930);

    private static double Boost(Supercharging s) => s switch { Supercharging.None => 1, Supercharging.Single => 1.65, _ => 2.2 };

    /// <summary>How hard each layout can be revved: the big V16s ran slow and lightly stressed.</summary>
    private static double LayoutPower(EngineLayout l) => l switch
    {
        EngineLayout.Straight6 or EngineLayout.V8 => 0.95,
        EngineLayout.V16 => 0.78,
        _ => 1,
    };

    private static double LayoutWeight(EngineLayout l) => l switch
    {
        EngineLayout.Straight6 => 0.85,
        EngineLayout.V8 => 0.9,
        EngineLayout.V12 => 1.15,
        EngineLayout.V16 => 1.3,
        _ => 1,
    };

    /// <summary>Horsepower the tyres of a year can put down before the car gets hard to handle.</summary>
    public static double TyreLimit(int year) => 200 + 30 * (year - 1930);

    /// <summary>The share of power beyond the tyre limit that still drives the car.</summary>
    public const double WheelspinShare = 0.35;

    /// <summary>The engine's own output, before wheelspin takes its share; for showing the player.</summary>
    public static int EngineHorsepower(CarDesign d, int year) =>
        (int)Math.Round(PowerPerLitre(year) * Boost(d.Supercharging) * LayoutPower(d.Layout) * 3 * Math.Pow(d.Litres / 3, 0.75)
                        * (d.Body == Body.Streamlined ? 1.05 : 1));

    /// <summary>
    /// The car a design makes when built in a year. An Indianapolis car is a "special" and, until
    /// the AAA dropped riding mechanics in 1938, a two-seater with the extra weight of the second seat.
    /// </summary>
    public static Car Build(CarDesign d, string teamName, int year, bool indy = false)
    {
        // Bigger cylinders cannot rev as high, so power grows more slowly than size (measured from 3 litres).
        var hp = PowerPerLitre(year) * Boost(d.Supercharging) * LayoutPower(d.Layout) * 3 * Math.Pow(d.Litres / 3, 0.75);
        if (d.Body == Body.Streamlined)
            hp *= 1.05;                       // less drag: worth about 5% more power on the straights
        var engineHp = hp;
        // Power beyond what the tyres of the year can put down mostly goes in wheelspin.
        var limit = TyreLimit(year);
        if (hp > limit)
            hp = limit + WheelspinShare * (hp - limit);

        // Engines get lighter for their size as the decade's alloys and castings improve.
        var engineKg = 75 * Math.Pow(d.Litres, 0.85) * LayoutWeight(d.Layout) * (1 - 0.04 * (year - 1930))
                       + d.Supercharging switch { Supercharging.None => 0, Supercharging.Single => 20, _ => 35 };
        var chassisKg = d.Frame switch { Frame.Ladder => 560, Frame.BoxedLadder => 520, _ => 470 }
                        + d.Suspension switch { Suspension.BeamAxles => 0, Suspension.IndependentFront => 10, _ => 20 }
                        + (d.Brakes == Brakes.Hydraulic ? 5 : 0)
                        + (d.Body == Body.Streamlined ? 20 : 0)
                        + (indy && year < Indianapolis.SingleSeaterYear ? Indianapolis.TwoSeaterKg : 0);
        var weight = (int)Math.Round(engineKg + chassisKg);
        if (year >= Formula.FirstLitreYear && !indy)
            weight = Math.Max(weight, MinWeightFrom1938);

        // A stiffer frame and independent wheels hold the road. Power beyond what the tyres of
        // the day can put down makes the car a handful, and the long V16 sits badly.
        var handling = 13
                       + (int)d.Frame
                       + (int)d.Suspension
                       + (d.Brakes == Brakes.Hydraulic ? 1 : 0)
                       - (int)Math.Floor(Math.Max(0, engineHp - limit) / 60)
                       - (d.Layout == EngineLayout.V16 ? 1 : 0)
                       - FrameFlex(d.Frame, engineHp / (d.Body == Body.Streamlined ? 1.05 : 1));
        handling = Math.Clamp(handling, 8, DrawingOffice.MaxHandling);

        // A new design is fragile, more so with parts new to the sport and highly stressed engines.
        var novel = new[]
        {
            FirstYear(d.Layout), FirstYear(d.Supercharging), FirstYear(d.Frame),
            FirstYear(d.Suspension), FirstYear(d.Brakes), FirstYear(d.Body),
        }.Count(first => first > 1930 && year - first <= 1);
        var reliability = 0.72 - 0.02 * novel
                          - (d.Supercharging == Supercharging.TwoStage ? 0.04 : 0)
                          - (d.Layout == EngineLayout.V16 ? 0.03 : 0)
                          - 0.03 * Math.Max(0, d.Litres - 3.5)    // big engines stress gearboxes
                          - 0.02 * Math.Max(0, engineHp - limit) / 40;  // and power beyond the tyres breaks things

        var cylinders = Cylinders(d.Layout);
        return new Car($"{teamName} {cylinders}C-{year % 100:00}{(indy ? " Special" : "")}", (int)Math.Round(hp), weight, handling,
            Math.Round(reliability, 2), d.Litres, Thirst(d));
    }

    /// <summary>
    /// Fuel use against a typical car of the day (a supercharged 3-litre): bigger engines drink
    /// more, a supercharger more again, and a two-stage one burns alcohol by the gallon.
    /// </summary>
    public static double Thirst(CarDesign d) =>
        Math.Round(Math.Pow(d.Litres / 3, 0.6) * d.Supercharging switch { Supercharging.None => 0.85, Supercharging.Single => 1, _ => 1.25 }, 2);

    /// <summary>A plain frame twists under a big engine: power it was not built for costs handling.</summary>
    private static int FrameFlex(Frame frame, double engineHp) => frame switch
    {
        Frame.Ladder => (int)Math.Floor(Math.Max(0, engineHp - 220) / 50),
        Frame.BoxedLadder => (int)Math.Floor(Math.Max(0, engineHp - 330) / 60),
        _ => 0,
    };

    /// <summary>The handling a frame loses to the engine's power, for the designer to warn about.</summary>
    public static int FrameFlex(CarDesign d, int year) => FrameFlex(d.Frame, EngineHorsepower(d, year) / (d.Body == Body.Streamlined ? 1.05 : 1));

    /// <summary>What designing and building it costs, before the chief engineer's skill.</summary>
    public static int Cost(CarDesign d)
    {
        var cost = 4500 + 500 * d.Litres
                   + d.Layout switch { EngineLayout.Straight6 => 0, EngineLayout.Straight8 or EngineLayout.V8 => 500, EngineLayout.V12 => 1500, _ => 2500 }
                   + d.Supercharging switch { Supercharging.None => 0, Supercharging.Single => 600, _ => 1200 }
                   + d.Frame switch { Frame.Ladder => 0, Frame.BoxedLadder => 500, _ => 1000 }
                   + d.Suspension switch { Suspension.BeamAxles => 0, Suspension.IndependentFront => 800, _ => 1300 }
                   + (d.Brakes == Brakes.Hydraulic ? 300 : 0)
                   + (d.Body == Body.Streamlined ? 500 : 0);
        return (int)(Math.Round(cost / 50) * 50);
    }

    /// <summary>Weeks of drawing-office work, before a bigger drawing office speeds it up.</summary>
    public static int Weeks(CarDesign d) => 10 + Cost(d) / 1500;

    /// <summary>A sensible first design for a year: the parts of the day, sized to the rules.</summary>
    public static CarDesign Default(int year) => new(
        EngineLayout.Straight8,
        year >= Formula.FirstLitreYear ? 3.0 : year >= Formula.FirstWeightYear ? 3.25 : 2.5,
        Supercharging.Single,
        year >= 1934 ? Frame.Tubular : year >= 1932 ? Frame.BoxedLadder : Frame.Ladder,
        year >= 1934 ? Suspension.IndependentFront : Suspension.BeamAxles,
        year >= 1934 ? Brakes.Hydraulic : Brakes.Mechanical,
        Body.OpenWheel);
}

/// <summary>What a design would cost the team and give it, before it is ordered.</summary>
/// <summary>
/// What a design would cost the team and give it, before it is ordered. <see cref="Car"/> is the
/// drawing office's estimate: the real car can come out up to <see cref="Spread"/> either side,
/// with about <see cref="Faults"/> teething troubles.
/// </summary>
public sealed record DesignPreview(Car Car, int Cost, int Weeks, int Year, IReadOnlyList<string> TooEarly, string? Breach, bool Indy = false,
    double Spread = 0, int Faults = 0, IReadOnlyList<Part>? Unresearched = null);

public sealed partial class Career
{
    /// <summary>The team's own design as first built, when the car is one; null for a works model.</summary>
    public Car? DesignBase { get; private set; }

    /// <summary>A car of the team's own design finished before its season, waiting for 1 January.</summary>
    public Car? WaitingCar { get; private set; }

    /// <summary>The teething troubles the waiting car will bring.</summary>
    private int _waitingFaults;

    /// <summary>A new Grand Prix car is being built, or waits for next season (an Indianapolis car does not count).</summary>
    public bool NewCarUnderway => GrandPrixCarInQueue || WaitingModelYear is not null;

    private bool GrandPrixCarInQueue => _projects.Any(p => p.Upgrade == Upgrade.NewCar && !p.Indy);

    /// <summary>
    /// What a design costs, how long it takes after the work already queued, and the car it makes
    /// in the year it is finished: a car finished after the new year is built with next year's
    /// engineering and waits for that season.
    /// </summary>
    public DesignPreview Preview(CarDesign design, bool indy = false)
    {
        var weeks = Works.ProjectWeeks(CarDesigner.Weeks(design), Level(Facility.DrawingOffice));
        var queued = _projects.Sum(p => p.Weeks - p.WeeksDone);
        var ready = Today.AddDays(7 * (queued + weeks));
        var year = HasSeason(ready.Year) ? ready.Year : Today.Year;
        var car = CarDesigner.Build(design, Team.Name, year, indy);
        var cost = (int)(Math.Round(CarDesigner.Cost(design) * Works.EngineerCostFactor(SkillOf(StaffRole.Engineer)) / 50) * 50);
        var breach = indy ? Indianapolis.Breach(design, car.Name, year) : Formula.Breach(car, year);
        return new DesignPreview(car, cost, weeks, year, CarDesigner.TooEarly(design, year), breach, indy,
            DesignSpread, indy ? 0 : ExpectedFaults(design, year), Unresearched(design));
    }

    private bool HasSeason(int year) => _data.Seasons.ContainsKey(year);

    /// <summary>
    /// Pays for the team's own design and puts it in the drawing-office queue. An Indianapolis car
    /// is built alongside the Grand Prix car and must meet the 500's rules of its year.
    /// </summary>
    public ProjectResult StartDesign(CarDesign design, bool indy = false)
    {
        if (indy ? IndyCarUnderway : NewCarUnderway)
            return ProjectResult.NewCarUnderway;
        var preview = Preview(design, indy);
        if (preview.TooEarly.Count > 0)
            return ProjectResult.PartsNotInvented;
        if (indy && preview.Breach is not null)
            return ProjectResult.BreaksRules;
        if (preview.Unresearched?.Count > 0)
            return ProjectResult.NotResearched;
        if (preview.Cost > Cash)
            return ProjectResult.NotEnoughMoney;
        Pay(MoneyKind.Development, $"Own design: {preview.Car.Name}", -preview.Cost);
        var (built, faults) = BuildPrototype(preview, design);
        _projects.Add(new Project(Upgrade.NewCar, preview.Cost, preview.Weeks) { ModelYear = preview.Year, Built = built, Design = design, Indy = indy, Faults = faults });
        return ProjectResult.Queued;
    }
}
