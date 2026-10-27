namespace RacingSim.Tests;

public class WeatherTests
{
    private static readonly Circuit Spa = TestData.Load().Circuits["spa"];

    [Fact]
    public void TheSameSeedGivesTheSameWeather()
    {
        for (var seed = 0; seed < 20; seed++)
            Assert.Equal(RaceWeather.Roll(Spa, seed, 10000).Showers, RaceWeather.Roll(Spa, seed, 10000).Showers);
    }

    [Fact]
    public void RainComesAboutAsOftenAsTheCircuitSays()
    {
        var wet = Enumerable.Range(0, 2000).Count(seed => RaceWeather.Roll(Spa, seed, 10000).AnyRain);
        Assert.InRange(wet / 2000.0, Spa.RainChance - 0.05, Spa.RainChance + 0.05);
        Assert.DoesNotContain(Enumerable.Range(0, 200), seed => RaceWeather.Roll(Spa with { RainChance = 0 }, seed, 10000).AnyRain);
    }

    [Fact]
    public void GoodWetDriversLoseLessInTheRain()
    {
        var drivers = TestData.Load().Drivers;
        var nuvolari = drivers["tazio-nuvolari"];
        var worst = drivers.Values.MinBy(d => d.Skills.Wet)!;
        Assert.True(RaceWeather.WetLapFactor(nuvolari) < RaceWeather.WetLapFactor(worst));
        Assert.True(RaceWeather.WetLapFactor(nuvolari) > 1);
        Assert.True(RaceWeather.WetCrashFactor(nuvolari) > 1);
    }

    [Fact]
    public void AWetRaceIsSlowerAndSaysWhenItRains()
    {
        var data = TestData.Load();
        var ev = data.Seasons[1930].Events.Single(e => e.Id == "1930-belgium");
        var circuit = data.Circuits[ev.CircuitId];
        var grid = RaceSim.FullGrid(data);
        LiveRace Run(double rain, int seed)
        {
            var race = new LiveRace(ev, circuit with { RainChance = rain }, grid, seed);
            race.RunToEnd();
            return race;
        }
        // Seed by seed, the same race in the rain is slower than in the dry.
        var seeds = Enumerable.Range(0, 30).Where(s => Run(1, s).Weather.Showers is [{ Start: 0 }]).Take(5).ToList();
        Assert.NotEmpty(seeds);
        foreach (var seed in seeds)
        {
            var wet = Run(1, seed);
            Assert.True(wet.FlagTime > Run(0, seed).FlagTime, $"seed {seed}: wet {wet.FlagTime} dry {Run(0, seed).FlagTime} showers {string.Join(",", wet.Weather.Showers)}");
            Assert.Contains(wet.Messages, m => m.Text == "The race starts in the rain.");
        }
    }

    [Fact]
    public void TheForecastIsOnlyForTheComingWeek()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1930);
        var events = career.CurrentSeason.Events;
        Assert.All(events.Where(e => e.Date.DayNumber - career.Today.DayNumber > 7), e => Assert.Null(career.ForecastFor(e)));
        var first = events.First();
        career.AdvanceTo(first.Date.AddDays(-3));
        Assert.Contains(career.ForecastFor(first), new[] { "Rain likely", "Dry" });
    }
}

public class InjuryTests
{
    [Fact]
    public void AnInjuredDriverMissesRacesUntilHeIsFit()
    {
        var data = TestData.Load();
        foreach (var seed in Enumerable.Range(0, 30))
        {
            var career = new Career(data, "alfa-rossa", 1930, seed);
            career.AdvanceTo(new DateOnly(1930, 12, 31));
            var hurt = career.Results
                .SelectMany(r => r.Result.Classification.Where(c => c.Retirement?.InjuryWeeks > 0).Select(c => (r.Event, Car: c)))
                .FirstOrDefault();
            if (hurt.Car is null)
                continue;

            var until = hurt.Event.Date.AddDays(7 * hurt.Car.Retirement!.InjuryWeeks);
            var id = hurt.Car.Entry.Driver.Id;
            Assert.False(career.FitOn(id, until.AddDays(-1)));
            Assert.True(career.FitOn(id, until));
            // He is not on the grid of any race he is still hurt for.
            foreach (var record in career.Results.Where(r => r.Event.Date > hurt.Event.Date && r.Event.Date < until))
                Assert.DoesNotContain(record.Result.Classification, c => c.Entry.Driver.Id == id);
            // And the save remembers.
            var loaded = Career.FromJson(data, career.ToJson());
            Assert.False(loaded.FitOn(id, until.AddDays(-1)));
            return;
        }
        Assert.Fail("No injuries in 30 seasons");
    }
}
