namespace RacingSim.Tests;

public class WorldTests
{
    [Fact]
    public void HistoryHappensWithItsDatesAndARandomFewEachSeason()
    {
        var data = TestData.Load();
        var career = new Career(data, "alfa-rossa", 1930, seed: 3);
        var news = career.WorldIn(1935);
        Assert.Contains(news, n => n.Event.Id == "sanctions" && n.Start == new DateOnly(1935, 11, 18));
        Assert.Equal(World.RandomPerSeason, news.Count(n => n.Event.Date is null));
        // The same career always sees the same world; another may not.
        Assert.Equal(news, new Career(data, "alfa-rossa", 1930, seed: 3).WorldIn(1935));
        var seasons = Enumerable.Range(1, 30).Select(seed => new Career(data, "alfa-rossa", 1930, seed: seed).WorldIn(1932)
            .Where(n => n.Event.Date is null).Select(n => n.Event.Id).First()).Distinct().Count();
        Assert.True(seasons > 3);
    }

    [Fact]
    public void EffectsAreStrongerOrWeakerFromCareerToCareer()
    {
        var data = TestData.Load();
        var factors = Enumerable.Range(1, 20)
            .Select(seed => new Career(data, "alfa-rossa", 1930, seed: seed).WorldIn(1935).Single(n => n.Event.Id == "sanctions").Factor)
            .ToList();
        Assert.True(factors.Distinct().Count() > 5);
        Assert.All(factors, f => Assert.InRange(f, 1 + 0.15 * (1 - World.Jitter) - 1e-9, 1 + 0.15 * (1 + World.Jitter) + 1e-9));
    }

    [Fact]
    public void SanctionsMakeItalianWorkDearerButNotFrench()
    {
        var data = TestData.Load();
        var day = new DateOnly(1936, 1, 15);
        var alfa = new Career(data, "alfa-rossa", 1936, seed: 3);
        var bugetti = new Career(data, "bugetti", 1936, seed: 3);
        Assert.True(alfa.WorldFactor(WorldEffect.Prices, day) > 1.05);
        Assert.Equal(1, bugetti.WorldFactor(WorldEffect.Prices, day), 6);
        // A country's news touches only its races.
        var german = new Career(data, "alfa-rossa", 1934, seed: 3);
        Assert.True(german.WorldFactor(WorldEffect.StartMoney, new DateOnly(1934, 6, 1), "Germany")
                    > german.WorldFactor(WorldEffect.StartMoney, new DateOnly(1934, 6, 1), "Italy"));
    }

    [Fact]
    public void NewsBreaksInTheTeamNewsAndChangesTheMoney()
    {
        var data = TestData.Load();
        var career = new Career(data, "alfa-rossa", 1933, seed: 3);
        var before = career.WeeklyBacking;
        career.AdvanceTo(new DateOnly(1933, 3, 1));
        Assert.Contains(career.News, n => n.Text.Contains("WORLD: The Italian state takes over failing industry"));
        Assert.True(career.WeeklyBacking > before);
    }
}
