namespace RacingSim.Tests;

public class SettingsTests
{
    [Fact]
    public void StartingMoneyScalesTheBudget()
    {
        var data = TestData.Load();
        var normal = new Career(data, "alfa-rossa", 1930);
        var tight = new Career(data, "alfa-rossa", 1930, settings: new RaceSettings(StartingMoney: 0.5));
        Assert.InRange(tight.Cash, normal.Cash / 2 - 100, normal.Cash / 2 + 100);
    }

    [Fact]
    public void RivalStrengthAndTheGermanWorksChangeTheirCarsOnly()
    {
        var data = TestData.Load();
        var normal = new Career(data, "alfa-rossa", 1937, seed: 2);
        var fair = new Career(data, "alfa-rossa", 1937, seed: 2, new RaceSettings(StateFundedGermans: false));
        var strong = new Career(data, "alfa-rossa", 1937, seed: 2, new RaceSettings(RivalStrength: 1.1));
        Assert.True(fair.CarOf("mercator-benz").Horsepower < normal.CarOf("mercator-benz").Horsepower);
        Assert.True(fair.CarOf("auto-unitas").Horsepower < normal.CarOf("auto-unitas").Horsepower);
        Assert.Equal(normal.CarOf("masarelli"), fair.CarOf("masarelli"));
        Assert.True(strong.CarOf("masarelli").Horsepower > normal.CarOf("masarelli").Horsepower);
        Assert.Equal(normal.Car, fair.Car);
        // Before the Reich's money the German teams are as they were.
        var early = new Career(data, "alfa-rossa", 1933, seed: 2, new RaceSettings(StateFundedGermans: false));
        Assert.Equal(new Career(data, "alfa-rossa", 1933, seed: 2).CarOf("mercator-benz"), early.CarOf("mercator-benz"));
    }

    [Fact]
    public void TheHandicapDoesNotGrowAsRivalsDevelop()
    {
        var data = TestData.Load();
        var fair = new Career(data, "alfa-rossa", 1936, seed: 2, new RaceSettings(StateFundedGermans: false));
        fair.AdvanceTo(new DateOnly(1936, 12, 1));
        var developed = fair.CarOf("mercator-benz");
        var normal = new Career(data, "alfa-rossa", 1936, seed: 2);
        normal.AdvanceTo(new DateOnly(1936, 12, 1));
        Assert.Equal(normal.CarOf("mercator-benz").Horsepower * RaceSettings.UnfundedGermanPower, developed.Horsepower, 1.0);
    }

    [Fact]
    public void FewerBreakdownsMeanMoreFinishers()
    {
        var data = TestData.Load();
        var ev = data.Seasons[1930].Events.Single(e => e.Id == "1930-belgium");
        int Finishers(double rate) => Enumerable.Range(1, 6).Sum(seed =>
        {
            var race = new LiveRace(ev, data.Circuits[ev.CircuitId], RaceSim.FullGrid(data), seed, new RaceSettings(false, FailureRate: rate));
            race.RunToEnd();
            return race.Result().Classification.Count(c => c.Finished);
        });
        Assert.True(Finishers(0.5) > Finishers(1.5));
    }

    [Fact]
    public void SettingsAreSaved()
    {
        var data = TestData.Load();
        var settings = new RaceSettings(false, 1.5, 0.9, 0.7, false);
        var career = new Career(data, "bugetti", 1930, settings: settings);
        Assert.Equal(settings, Career.FromJson(data, career.ToJson()).Settings);
    }
}
