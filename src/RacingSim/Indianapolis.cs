namespace RacingSim;

/// <summary>
/// The Indianapolis 500 ran to the AAA's own rules, not the European formula. From 1930 to 1937
/// it was the "junk formula": engines of at most 366 cubic inches (6 litres), no superchargers,
/// and two seats for a riding mechanic, to bring the factories' production engines back. In
/// 1938 the AAA took up the international rules: 3 litres supercharged or 4.5 litres without,
/// and the riding mechanic went. So a European team needs a separate car for the 500 until 1938;
/// after that its Grand Prix car may run. Simplified: minimum weights and fuel rules are left out.
/// </summary>
public static class Indianapolis
{
    public const int SingleSeaterYear = 1938;
    public const double JunkFormulaMaxLitres = 6.0;
    public const double MaxSuperchargedLitres = 3.0;
    public const double MaxUnsuperchargedLitres = 4.5;

    /// <summary>The second seat, the wider body and the mechanic's share of the frame.</summary>
    public const int TwoSeaterKg = 40;

    /// <summary>A ready-made special from an American builder: dearer than nothing, cheaper than a design.</summary>
    public const int BuyCost = 3500;
    public const int BuyWeeks = 8;
    public const string Builder = "Millard";

    public static bool Is(Circuit circuit) => circuit.Region == Region.Usa;

    public static string Name(int year) => year < SingleSeaterYear ? "Junk formula" : "International formula";

    public static string Rule(int year) => year < SingleSeaterYear
        ? $"two-seaters, no supercharger, at most {JunkFormulaMaxLitres:0.0} litres"
        : $"{MaxSuperchargedLitres:0.0} litres supercharged or {MaxUnsuperchargedLitres:0.0} without";

    /// <summary>Why a design may not run in the 500 of a year, or null if it may.</summary>
    public static string? Breach(CarDesign d, string name, int year)
    {
        var supercharged = d.Supercharging != Supercharging.None;
        if (year < SingleSeaterYear)
        {
            if (supercharged)
                return $"The {name} is supercharged; the {Name(year).ToLowerInvariant()} of the 500 bans superchargers.";
            if (d.Litres > JunkFormulaMaxLitres)
                return $"The {name} has a {d.Litres:0.0#}-litre engine; the 500 allows {JunkFormulaMaxLitres:0.0}.";
            return null;
        }
        var max = supercharged ? MaxSuperchargedLitres : MaxUnsuperchargedLitres;
        return d.Litres > max
            ? $"The {name} has a {d.Litres:0.0#}-litre {(supercharged ? "supercharged" : "unsupercharged")} engine; the 500 allows {max:0.0}."
            : null;
    }

    /// <summary>From 1938 a Grand Prix car to the 3-litre formula may run in the 500 too.</summary>
    public static bool GrandPrixCarMayRun(Car car, int year) =>
        year >= SingleSeaterYear && car.EngineLitres <= MaxSuperchargedLitres;

    /// <summary>What an American builder sells a European team for the 500 in a year.</summary>
    public static CarDesign Stock(int year) => year < SingleSeaterYear
        ? new(EngineLayout.Straight8, 4.5, Supercharging.None, year >= 1932 ? Frame.BoxedLadder : Frame.Ladder,
            Suspension.BeamAxles, year >= 1934 ? Brakes.Hydraulic : Brakes.Mechanical, Body.OpenWheel)
        : new(EngineLayout.Straight8, 3.0, Supercharging.Single, Frame.BoxedLadder,
            Suspension.BeamAxles, Brakes.Hydraulic, Body.OpenWheel);

    /// <summary>A sensible first design of the team's own for the 500 of a year.</summary>
    public static CarDesign Default(int year) => Stock(year) with
    {
        Litres = year < SingleSeaterYear ? 5.0 : 3.0,
        Frame = year >= 1934 ? Frame.Tubular : year >= 1932 ? Frame.BoxedLadder : Frame.Ladder,
    };
}

/// <summary>A special bought from an American builder, on its way across the Atlantic.</summary>
public sealed record IndyOrder(Car Car, CarDesign Design, DateOnly Arrives);

public sealed partial class Career
{
    /// <summary>The team's car for the Indianapolis 500, or null when it has none.</summary>
    public Car? IndyCar { get; private set; }

    /// <summary>What the Indianapolis car is built from, to check it against each year's rules.</summary>
    public CarDesign? IndyDesign { get; private set; }

    /// <summary>A special bought and on its way.</summary>
    public IndyOrder? IndyOrder { get; private set; }

    public bool IndyCarUnderway => IndyOrder is not null || _projects.Any(p => p.Indy);

    /// <summary>The special an American builder would sell now, and the car it would be.</summary>
    public Car IndyCarForSale => CarDesigner.Build(Indianapolis.Stock(Today.Year), Indianapolis.Builder, Today.Year, indy: true);

    /// <summary>
    /// The car the team races at an event: at Indianapolis its special if that meets the year's
    /// rules, else from 1938 its Grand Prix car if that does, else none.
    /// </summary>
    public Car? RaceCarFor(RaceEvent ev)
    {
        var year = ev.Date.Year;
        if (!Indianapolis.Is(_data.Circuits[ev.CircuitId]))
            return Car;
        if (IndyCar is { } special && IndyDesign is { } design && Indianapolis.Breach(design, special.Name, year) is null)
            return special;
        return Indianapolis.GrandPrixCarMayRun(Car, year) ? Car : null;
    }

    /// <summary>Buys a special for the 500; it arrives some weeks later.</summary>
    public ProjectResult BuyIndyCar()
    {
        if (IndyCarUnderway)
            return ProjectResult.NewCarUnderway;
        if (Indianapolis.BuyCost > Cash)
            return ProjectResult.NotEnoughMoney;
        var car = IndyCarForSale;
        Pay(MoneyKind.Development, $"Bought for Indianapolis: {car.Name}", -Indianapolis.BuyCost);
        IndyOrder = new IndyOrder(car, Indianapolis.Stock(Today.Year), Today.AddDays(7 * Indianapolis.BuyWeeks));
        return ProjectResult.Queued;
    }

    private void DeliverIndyOrder(DateOnly date)
    {
        if (IndyOrder is not { } order || order.Arrives > date)
            return;
        IndyOrder = null;
        IndyCarArrives(order.Car, order.Design, order.Arrives, $"The {order.Car.Name} has arrived from America");
    }

    private void IndyCarArrives(Car car, CarDesign design, DateOnly date, string what)
    {
        IndyCar = car;
        IndyDesign = design;
        _news.Add(new NewsItem(date, $"{what}: {car.Horsepower} hp, {car.WeightKg} kg, {car.EngineLitres:0.0#} litres, reliability {car.Reliability:P0}."));
    }
}
