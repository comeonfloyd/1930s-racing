namespace RacingSim;

public enum MoneyKind
{
    Wages,
    Travel,
    StartMoney,
    Prize,
    Backing,
    Development,
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

    /// <summary>Weekly retainer for each driver under contract.</summary>
    public const int DriverWage = 25;

    /// <summary>Weekly cost of the works: mechanics, rent, parts.</summary>
    public const int WorksCost = 60;

    public static int WeeklyCosts(int drivers) => WorksCost + DriverWage * drivers;

    /// <summary>
    /// Weekly money from the maker or a patron behind the team, as most 1930s teams had.
    /// It grows with prestige: a pound a week per point.
    /// </summary>
    public static int WeeklyBacking(int prestige) => prestige;

    /// <summary>Prize money for a finishing position (1 = winner), or 0 outside the prizes.</summary>
    public static int Prize(RaceEvent ev, int position) =>
        position >= 1 && position <= PrizeShares.Count ? (int)Math.Round(ev.PrizeFund * PrizeShares[position - 1]) : 0;
}
