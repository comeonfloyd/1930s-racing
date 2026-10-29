using System.Text.Json.Serialization;

namespace RacingSim;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SupplyKind { Fuel, Oil, Tyres, Plugs }

/// <summary>
/// A supplier from sponsors.json who pays a team to race on its product. <see cref="Fee"/> is
/// pounds a week to a team of ordinary standing; the rest is what the product does to the car.
/// </summary>
public sealed record Sponsor(
    string Id,
    string Name,
    SupplyKind Kind,
    string Country,
    int Fee,
    int WinBonus,
    int PodiumBonus,
    double Power = 1,
    double Thirst = 1,
    double Wear = 1,
    int Handling = 0,
    double Reliability = 0,
    int FirstYear = 1930,
    int LastYear = 1939,
    int MinStanding = 0,
    string Text = "")
{
    /// <summary>What the product does, in a few words, e.g. "power +2%, fuel use −3%".</summary>
    public string EffectText
    {
        get
        {
            static string Pct(double f) => $"{(f > 1 ? "+" : "−")}{Math.Abs(f - 1) * 100:0.#}%";
            var parts = new List<string>();
            if (Math.Abs(Power - 1) > 1e-9)
                parts.Add($"power {Pct(Power)}");
            if (Math.Abs(Thirst - 1) > 1e-9)
                parts.Add($"fuel use {Pct(Thirst)}");
            if (Math.Abs(Wear - 1) > 1e-9)
                parts.Add($"tyre wear {Pct(Wear)}");
            if (Handling != 0)
                parts.Add($"handling {(Handling > 0 ? "+" : "−")}{Math.Abs(Handling)}");
            if (Reliability > 0)
                parts.Add($"reliability +{Reliability * 100:0.#}%");
            return parts.Count == 0 ? "no change to the car" : string.Join(", ", parts);
        }
    }

    /// <summary>The car on this product.</summary>
    public Car Supply(Car car) => car with
    {
        Horsepower = (int)Math.Round(car.Horsepower * Power),
        Thirst = Math.Round(car.Thirst * Thirst, 3),
        Wear = Math.Round(car.Wear * Wear, 3),
        Handling = car.Handling + Handling,
        Reliability = Math.Min(0.99, Math.Round(car.Reliability + Reliability, 3)),
    };
}

/// <summary>A sponsor's offer to the team, or a signed deal: it runs to the end of <see cref="Year"/>.</summary>
public sealed record SponsorDeal(string SponsorId, int Year, int WeeklyFee, int WinBonus, int PodiumBonus);

/// <summary>How hard the team bargains with a race's organisers over start money.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Ask { More, Lots }

public enum NegotiationResult { Agreed, Refused, Offended, AlreadyAgreed, NotEntered }

public static class Sponsorship
{
    /// <summary>The standing that a sponsor's <see cref="Sponsor.Fee"/> is quoted for.</summary>
    public const int OrdinaryStanding = 60;

    /// <summary>A maker pays this much more to a team from its own country.</summary>
    public const double HomeFactor = 1.2;

    /// <summary>How far an offer may differ from the sponsor's usual rate, either way.</summary>
    public const double Jitter = 0.15;

    /// <summary>What each ask adds to the start money, if the organisers agree.</summary>
    public static double Raise(Ask ask) => ask == Ask.Lots ? 1.5 : 1.25;

    /// <summary>Organisers asked for too much pay a tenth less to make the point.</summary>
    public const double OffendedFactor = 0.9;

    /// <summary>
    /// Chance organisers agree: better for a team of standing, a star driver and a good team
    /// manager; worse at the great races, which have no shortage of entries, and for a big ask.
    /// </summary>
    public static double AgreeChance(Ask ask, int standing, int managerSkill, bool star, int eventPrestige) =>
        Math.Clamp(0.45 + (standing - OrdinaryStanding) / 100.0 + 0.015 * (managerSkill - Works.EmptyPostSkill)
                   + (star ? 0.1 : 0) - 0.04 * (eventPrestige - 7) - (ask == Ask.Lots ? 0.3 : 0), 0.05, 0.95);

    /// <summary>A driver fast enough that organisers will pay to see him.</summary>
    public const int StarSpeed = 18;
}

public sealed partial class Career
{
    private readonly Dictionary<SupplyKind, SponsorDeal> _deals = new();
    private readonly Dictionary<string, double> _startDeals = new();

    /// <summary>The team's standing with sponsors and organisers: its prestige, plus its wins and podiums of the past year.</summary>
    public int Standing
    {
        get
        {
            var since = Today.AddDays(-365);
            var places = _results.Where(r => r.Entered && r.Event.Date >= since)
                .Select(r => r.Result.Classification.Select((c, i) => (c, i))
                    .Where(x => x.c.Entry.Team.Id == Team.Id && x.c.Finished)
                    .Select(x => x.i + 1).DefaultIfEmpty(int.MaxValue).Min())
                .ToList();
            return Math.Min(100, Team.Prestige + 3 * places.Count(p => p == 1) + places.Count(p => p is 2 or 3));
        }
    }

    /// <summary>The deals in force this year, by kind.</summary>
    public IReadOnlyDictionary<SupplyKind, SponsorDeal> Deals => _deals;

    public Sponsor SponsorOf(SponsorDeal deal) => _data.Sponsors[deal.SponsorId];

    /// <summary>What the sponsors pay each week.</summary>
    public int WeeklySponsorship => _deals.Values.Sum(d => d.WeeklyFee);

    /// <summary>
    /// This year's offers for the kinds the team has no deal for yet. They follow the team's
    /// standing, so a run of wins brings better offers; the career's seed sets each sponsor's mood.
    /// </summary>
    public IReadOnlyList<SponsorDeal> SponsorOffers()
    {
        var standing = Standing;
        var year = Today.Year;
        return _data.Sponsors.Values
            .Where(s => !_deals.ContainsKey(s.Kind) && s.FirstYear <= year && year <= s.LastYear && standing >= s.MinStanding)
            .Select(s =>
            {
                var rng = new Random(Seeds.Mix(Seed, year * 977 + s.Id.Aggregate(17, (h, ch) => unchecked(h * 31 + ch))));
                var factor = (double)standing / Sponsorship.OrdinaryStanding * (1 + Sponsorship.Jitter * (2 * rng.NextDouble() - 1))
                             * (s.Country == Team.Nationality ? Sponsorship.HomeFactor : 1);
                return new SponsorDeal(s.Id, year, (int)Math.Round(s.Fee * factor), Round10(s.WinBonus * factor), Round10(s.PodiumBonus * factor));
            })
            .OrderBy(d => _data.Sponsors[d.SponsorId].Kind)
            .ThenByDescending(d => d.WeeklyFee)
            .ToList();
    }

    private static int Round10(double amount) => (int)Math.Round(amount / 10) * 10;

    /// <summary>Signs an offer: the team races on that product until the end of the year.</summary>
    public bool SignSponsor(string sponsorId)
    {
        var offer = SponsorOffers().FirstOrDefault(o => o.SponsorId == sponsorId);
        if (offer is null)
            return false;
        var sponsor = _data.Sponsors[sponsorId];
        _deals[sponsor.Kind] = offer;
        _news.Add(new NewsItem(Today, $"{Team.Name} sign with {sponsor.Name} for {Today.Year}: £{offer.WeeklyFee} a week, £{offer.WinBonus} a win. {sponsor.Text}"));
        return true;
    }

    /// <summary>A car on the team's sponsors' products.</summary>
    public Car Supplied(Car car) => _deals.Values.Aggregate(car, (c, d) => SponsorOf(d).Supply(c));

    /// <summary>Contracts end with the year.</summary>
    private void EndDeals(int year)
    {
        var ended = _deals.Values.Where(d => d.Year < year).ToList();
        foreach (var deal in ended)
            _deals.Remove(SponsorOf(deal).Kind);
        if (ended.Count > 0)
            _news.Add(new NewsItem(new DateOnly(year, 1, 1), $"The sponsors' contracts for {year - 1} have ended ({string.Join(", ", ended.Select(d => SponsorOf(d).Name))}). New offers are in."));
    }

    /// <summary>Win and podium bonuses for a finished race.</summary>
    private int PaySponsorBonuses(RaceEvent ev, RaceResult result)
    {
        var earned = 0;
        for (var i = 0; i < Math.Min(3, result.Classification.Count); i++)
        {
            var car = result.Classification[i];
            if (car.Entry.Team.Id != Team.Id || !car.Finished)
                continue;
            foreach (var deal in _deals.Values)
            {
                var bonus = i == 0 ? deal.WinBonus : deal.PodiumBonus;
                if (bonus > 0)
                    earned += Pay(MoneyKind.Sponsor, $"{SponsorOf(deal).Name} bonus, {Place(i + 1)} at {ev.Name}", bonus);
            }
        }
        return earned;
    }

    /// <summary>The start money agreed with a race's organisers, as a share of their offer; 1 when not bargained.</summary>
    public double StartDeal(RaceEvent ev) => _startDeals.GetValueOrDefault(ev.Id, 1);

    public bool Bargained(RaceEvent ev) => _startDeals.ContainsKey(ev.Id);

    /// <summary>The chance the organisers of an entered race agree to an ask.</summary>
    public double AgreeChance(RaceEvent ev, Ask ask) =>
        Sponsorship.AgreeChance(ask, Standing, SkillOf(StaffRole.Manager), Drivers.Any(d => d.Skills.Speed >= Sponsorship.StarSpeed), ev.Prestige);

    /// <summary>
    /// Asks an entered race's organisers for more start money, once. They agree, refuse and
    /// keep their offer, or, asked for a lot, take offence and pay a tenth less.
    /// </summary>
    public NegotiationResult Negotiate(string eventId, Ask ask)
    {
        var ev = _entries.FirstOrDefault(e => e.Id == eventId);
        if (ev is null || HasRun(ev))
            return NegotiationResult.NotEntered;
        if (Bargained(ev))
            return NegotiationResult.AlreadyAgreed;
        var roll = new Random(Seeds.Mix(RaceSeed(ev), 4111)).NextDouble();
        NegotiationResult result;
        if (roll < AgreeChance(ev, ask))
        {
            _startDeals[ev.Id] = Sponsorship.Raise(ask);
            result = NegotiationResult.Agreed;
        }
        else if (ask == Ask.Lots)
        {
            _startDeals[ev.Id] = Sponsorship.OffendedFactor;
            result = NegotiationResult.Offended;
        }
        else
        {
            _startDeals[ev.Id] = 1;
            result = NegotiationResult.Refused;
        }
        _news.Add(new NewsItem(Today, result switch
        {
            NegotiationResult.Agreed => $"The {ev.Name} organisers agree to £{StartMoney(ev)} a car.",
            NegotiationResult.Offended => $"The {ev.Name} organisers take offence and cut their offer to £{StartMoney(ev)} a car.",
            _ => $"The {ev.Name} organisers will not go above £{StartMoney(ev)} a car.",
        }));
        return result;
    }
}
