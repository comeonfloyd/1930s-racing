namespace RacingSim;

public enum MoneyKind
{
    Wages,
    Travel,
    StartMoney,
    Prize,
    Backing,
    Development,
    Sponsor,
}

/// <summary>One line in the team's books. Costs are negative.</summary>
public sealed record Transaction(DateOnly Date, MoneyKind Kind, string Text, int Amount);

/// <summary>
/// The money rules. All amounts are first-pass placeholders in pounds of the day, to be
/// balanced with the season-runner later.
/// </summary>
public static class Finance
{
    /// <summary>Share of an event's prize fund for 1st, 2nd, 3rd and 4th place.</summary>
    public static readonly IReadOnlyList<double> PrizeShares = [0.5, 0.25, 0.15, 0.1];

    /// <summary>Weekly retainer for a driver under contract: the faster, the dearer.</summary>
    public static int DriverWage(Driver driver) => 5 + (int)Math.Round(1.5 * driver.Skills.Speed);

    /// <summary>Paid once to sign a driver: four weeks of their wage.</summary>
    public static int SigningFee(Driver driver) => 4 * DriverWage(driver);

    /// <summary>Weekly cost of the works: mechanics, rent, parts.</summary>
    public const int WorksCost = 45;

    public static int WeeklyCosts(IEnumerable<Driver> drivers) => WorksCost + drivers.Sum(DriverWage);

    /// <summary>
    /// Weekly money from the maker or a patron behind the team, as most 1930s teams had.
    /// It grows with prestige: two pounds a week per point.
    /// </summary>
    public static int WeeklyBacking(int prestige) => 2 * prestige;

    /// <summary>The Reich's weekly grant to a German works, on top of the maker's backing.</summary>
    public const int StateGrant = 150;

    /// <summary>Prize money for a finishing position (1 = winner), or 0 outside the prizes.</summary>
    public static int Prize(RaceEvent ev, int position) =>
        position >= 1 && position <= PrizeShares.Count ? (int)Math.Round(ev.PrizeFund * PrizeShares[position - 1]) : 0;
}
