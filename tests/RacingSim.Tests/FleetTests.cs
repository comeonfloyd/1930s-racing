namespace RacingSim.Tests;

public class FleetTests
{
    /// <summary>Plenty of money, so the tests can build every new model.</summary>
    private static readonly RaceSettings Rich = new(StartingMoney: 4);

    /// <summary>Has the drawing office build the works' next model and moves on to the year it races.</summary>
    private static void BuildNextModel(Career career)
    {
        var old = career.Car;
        Assert.Equal(ProjectResult.Queued, career.StartProject(Upgrade.NewCar));
        career.AdvanceTo(new DateOnly(career.Today.Year, 12, 31));
        career.StartNextSeason();
        Assert.NotEqual(old.Name, career.Car.Name);
    }

    /// <summary>Alfa Rossa in 1932, racing the new P3 with the 8C Monza kept.</summary>
    private static Career WithNewCar()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1931, seed: 2, Rich);
        BuildNextModel(career);
        return career;
    }

    [Fact]
    public void TheOldCarStaysInTheWorksAndADriverCanRaceIt()
    {
        var career = WithNewCar();
        var spare = Assert.Single(career.SpareCars);
        Assert.Equal("Alfa Rossa 8C Monza", spare.Name);

        var driver = career.Drivers[1];
        Assert.True(career.AssignCar(driver.Id, spare.Name));
        Assert.False(career.AssignCar(driver.Id, "No such car"));
        var ev = career.CurrentSeason.Events.First(e => career.Enter(e.Id) == EntryResult.Entered && e.Date.Month > 1);
        var grid = career.GridFor(ev).Where(e => e.Team.Id == "alfa-rossa").ToList();
        Assert.Equal(spare.Name, grid.Single(e => e.Driver.Id == driver.Id).Team.Car.Name);
        Assert.All(grid.Where(e => e.Driver.Id != driver.Id), e => Assert.Equal(career.Car.Name, e.Team.Car.Name));

        var loaded = Career.FromJson(TestData.Load(), career.ToJson());
        Assert.Equal(spare, Assert.Single(loaded.SpareCars));
        Assert.Equal(spare, loaded.CarOfDriver(driver.Id));
    }

    [Fact]
    public void SellingAKeptCarPaysAndPutsItsDriverBackInTheNewCar()
    {
        var career = WithNewCar();
        var spare = career.SpareCars[0];
        var driver = career.Drivers[0];
        career.AssignCar(driver.Id, spare.Name);
        var cash = career.Cash;
        Assert.True(career.SellSpare(spare.Name));
        Assert.Equal(cash + Career.SpareValue(spare), career.Cash);
        Assert.Empty(career.SpareCars);
        Assert.Null(career.CarOfDriver(driver.Id));
    }

    [Fact]
    public void ThePastLimitTheOldestKeptCarIsSold()
    {
        var data = TestData.Load();
        var career = new Career(data, "alfa-rossa", 1933, seed: 2, Rich);
        // The P3 2.9 for 1934, the 8C-35 for 1935 and the 12C-36 for 1936: three cars replaced.
        BuildNextModel(career);
        BuildNextModel(career);
        BuildNextModel(career);
        Assert.Equal(Career.MaxSpareCars, career.SpareCars.Count);
        Assert.Contains(career.Ledger, t => t.Text.StartsWith("Sold the old"));
    }

    [Fact]
    public void AKeptCarThatBreaksTheRulesIsNotRaced()
    {
        // The 1937 cars break the 1938 3-litre rules, so a driver given one races the new car.
        var career = new Career(TestData.Load(), "alfa-rossa", 1937, seed: 2, Rich);
        BuildNextModel(career);
        var spare = career.SpareCars.Last();
        Assert.NotNull(Formula.Breach(spare, 1938));
        var driver = career.Drivers[0];
        career.AssignCar(driver.Id, spare.Name);
        var ev = career.CurrentSeason.Events.First(e => !e.IsEndurance && career.Enter(e.Id) == EntryResult.Entered);
        Assert.Equal(career.Car.Name, career.GridFor(ev).Single(e => e.Driver.Id == driver.Id).Team.Car.Name);
    }
}
