namespace RacingSim;

/// <summary>
/// A team of the player's own making: a small garage with a bought customer car, a patron's
/// modest backing and no drivers or staff yet. It can start in any scenario's year.
/// </summary>
public static class OwnTeam
{
    public const string Id = "own-team";

    /// <summary>The garage's money at the start, before the settings.</summary>
    public const int StartingCash = 5000;

    /// <summary>
    /// Nobody has heard of the team yet: a patron's backing, and small sponsors' offers. Its
    /// patron's backing grows with its <see cref="Career.Standing"/>, as results come.
    /// </summary>
    public const int Prestige = 25;

    /// <summary>Where a garage of each nationality is based.</summary>
    public static readonly IReadOnlyDictionary<string, string> Bases = new Dictionary<string, string>
    {
        ["Italy"] = "milan",
        ["France"] = "paris",
        ["Germany"] = "stuttgart",
        ["United Kingdom"] = "london",
    };

    /// <summary>The racing colours a garage may paint its cars, as hex.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Colours =
    [
        ("Racing red", "#a3192b"),
        ("French blue", "#2a4fa0"),
        ("British green", "#1f5a33"),
        ("Belgian yellow", "#d9b42a"),
        ("Black", "#262222"),
        ("Cream", "#e6dcc0"),
    ];

    /// <summary>The garage team, starting with the customer car of its first year.</summary>
    public static Team Create(GameData data, string name, string nationality, string colour, int year) =>
        new(Id, string.IsNullOrWhiteSpace(name) ? "Scuderia Nuova" : name.Trim(), nationality, Bases[nationality],
            StartingCash, Prestige, Playable: true, DriverIds: [], Car: CustomerCar(data, year), FirstYear: year, Colour: colour);

    /// <summary>The customer car a private team can buy in a year: what the privateers race.</summary>
    public static Car CustomerCar(GameData data, int year) =>
        data.Teams.Values.First(t => t.Privateer && data.Cities[t.BaseCityId].Region == Region.Europe).CarFor(year);

    /// <summary>A customer car's price, a year or two old from the factory: dearer the more power it has.</summary>
    public static int CustomerCarCost(Car car) => (int)(Math.Round((1000 + 4 * car.Horsepower) / 50.0) * 50);
}

public sealed partial class Career
{
    /// <summary>True for a team of the player's own making.</summary>
    public bool IsOwnTeam => Team.Id == OwnTeam.Id;

    /// <summary>A customer car the garage could buy now, when it differs from its car and meets this year's rules.</summary>
    public Car? CustomerCarForSale =>
        IsOwnTeam && OwnTeam.CustomerCar(_data, Today.Year) is { } car && car.Name != Car.Name && Formula.Breach(car, Today.Year) is null ? car : null;

    /// <summary>Buys this year's customer car: it replaces the team's car at once, and its tuning starts afresh.</summary>
    public ProjectResult BuyCustomerCar()
    {
        if (CustomerCarForSale is not { } car)
            return ProjectResult.NoNewModel;
        if (NewCarUnderway)
            return ProjectResult.NewCarUnderway;
        var cost = OwnTeam.CustomerCarCost(car);
        if (cost > Cash)
            return ProjectResult.NotEnoughMoney;
        Pay(MoneyKind.Development, $"Customer car: {car.Name}", -cost);
        Car = car;
        DesignBase = car;
        TeethingFaults = 0;
        _upgradesDone.Clear();
        _news.Add(new NewsItem(Today, $"The {car.Name} arrives from the factory: {car.Horsepower} hp, {car.WeightKg} kg."));
        return ProjectResult.Queued;
    }
}
