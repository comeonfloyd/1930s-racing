using System.Text.Json.Serialization;

namespace RacingSim;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorldEffect { StartMoney, Prizes, Backing, Travel, Prices }

/// <summary>
/// A piece of world news from world.json. With a <see cref="Date"/> it is history, seen with
/// <see cref="Chance"/>; without one it is in the random pool that each season draws from.
/// <see cref="Factor"/> multiplies the <see cref="Effect"/> for <see cref="Weeks"/>, at races in
/// <see cref="Country"/> or for teams of <see cref="Nationality"/> (neither means everyone).
/// </summary>
public sealed record WorldEvent(
    string Id,
    string Headline,
    string Text,
    WorldEffect Effect,
    double Factor,
    int Weeks,
    DateOnly? Date = null,
    string? Country = null,
    string? Nationality = null,
    double Chance = 1);

public sealed record WorldData(IReadOnlyList<WorldEvent> Events);

/// <summary>A piece of world news as it happens in one career: when, until when, and how strongly.</summary>
public sealed record WorldNews(WorldEvent Event, DateOnly Start, DateOnly End, double Factor)
{
    public bool ActiveOn(DateOnly date) => Start <= date && date < End;

    /// <summary>What it does, in a few words, e.g. "start money −15% at races in Germany".</summary>
    public string EffectText
    {
        get
        {
            if (Math.Abs(Factor - 1) < 0.005)
                return "";
            var change = $"{(Factor > 1 ? "+" : "−")}{Math.Abs(Factor - 1) * 100:0}%";
            var what = Event.Effect switch
            {
                WorldEffect.StartMoney => "start money",
                WorldEffect.Prizes => "prizes",
                WorldEffect.Backing => "works backing",
                WorldEffect.Travel => "travel costs",
                _ => "works and drawing-office costs",
            };
            var where = Event.Country is { } c ? $" at races in {c}" : Event.Nationality is { } n ? $" for teams from {n}" : "";
            return $"{what} {change}{where}";
        }
    }
}

public static class World
{
    /// <summary>Random events each season draws from the pool.</summary>
    public const int RandomPerSeason = 2;

    /// <summary>How far a career's version of an event may differ in strength: ±40% of its change.</summary>
    public const double Jitter = 0.4;

    /// <summary>
    /// The world news of one season in a career, fixed by the career's seed: the historical
    /// events dated that year that this career sees, and a couple from the random pool on
    /// random dates between February and October.
    /// </summary>
    public static IReadOnlyList<WorldNews> Season(IEnumerable<WorldEvent> events, int year, int seed)
    {
        var rng = new Random(Seeds.Mix(seed, year * 101 + 7));
        var news = new List<WorldNews>();
        var all = events.ToList();
        foreach (var ev in all.Where(e => e.Date?.Year == year).OrderBy(e => e.Date))
        {
            var seen = rng.NextDouble() < ev.Chance;
            var strength = 1 + Jitter * (2 * rng.NextDouble() - 1);
            if (seen)
                news.Add(Make(ev, ev.Date!.Value, strength));
        }
        var pool = all.Where(e => e.Date is null).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        for (var i = 0; i < RandomPerSeason && pool.Count > 0; i++)
        {
            var ev = pool[rng.Next(pool.Count)];
            pool.Remove(ev);
            var start = new DateOnly(year, 2, 1).AddDays(rng.Next(240));
            news.Add(Make(ev, start, 1 + Jitter * (2 * rng.NextDouble() - 1)));
        }
        return news.OrderBy(n => n.Start).ToList();
    }

    private static WorldNews Make(WorldEvent ev, DateOnly start, double strength) =>
        new(ev, start, start.AddDays(7 * ev.Weeks), Math.Round(1 + (ev.Factor - 1) * strength, 3));
}

public sealed partial class Career
{
    private readonly Dictionary<int, IReadOnlyList<WorldNews>> _world = new();

    /// <summary>The world news of a year in this career.</summary>
    public IReadOnlyList<WorldNews> WorldIn(int year)
    {
        if (!_world.TryGetValue(year, out var news))
            _world[year] = news = World.Season(_data.World, year, Seed);
        return news;
    }

    /// <summary>World news in force on a day: this year's, and last year's that run on.</summary>
    public IEnumerable<WorldNews> WorldOn(DateOnly date) =>
        WorldIn(date.Year - 2).Concat(WorldIn(date.Year - 1)).Concat(WorldIn(date.Year)).Where(n => n.ActiveOn(date));

    /// <summary>How world news on a day changes an effect, at a race in a country or for a team of a nationality.</summary>
    public double WorldFactor(WorldEffect effect, DateOnly date, string? country = null) =>
        WorldOn(date)
            .Where(n => n.Event.Effect == effect
                        && (n.Event.Country is null || n.Event.Country == country)
                        && (n.Event.Nationality is null || n.Event.Nationality == Team.Nationality))
            .Aggregate(1.0, (f, n) => f * n.Factor);

    /// <summary>Puts world news that broke between two days into the team news.</summary>
    private void AnnounceWorld(DateOnly from, DateOnly to)
    {
        for (var year = from.Year; year <= to.Year; year++)
            foreach (var news in WorldIn(year).Where(n => n.Start > from && n.Start <= to))
                _news.Add(new NewsItem(news.Start, $"WORLD: {news.Event.Headline}. {news.Event.Text}"
                                                   + (news.EffectText.Length > 0 ? $" ({Capital(news.EffectText)}.)" : "")));
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
