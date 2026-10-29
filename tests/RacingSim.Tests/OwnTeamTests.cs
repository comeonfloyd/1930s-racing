namespace RacingSim.Tests;

public class OwnTeamTests
{
    private static Career Garage(int year = 1930, string nationality = "United Kingdom", int seed = 2)
    {
        var data = TestData.Load();
        return new Career(data, OwnTeam.Create(data, "Bentley Boys", nationality, "#1f5a33", year), year, seed);
    }

    [Fact]
    public void AGarageStartsSmallWithACustomerCarAndNoDrivers()
    {
        var career = Garage();
        Assert.True(career.IsOwnTeam);
        Assert.Equal("Bentley Boys", career.Team.Name);
        Assert.Equal(OwnTeam.StartingCash, career.Cash);
        Assert.Equal("Bugetti Type 35B", career.Car.Name);
        Assert.Empty(career.Drivers);
        Assert.Equal(EntryResult.NoDrivers, career.Enter("1930-monaco"));
        Assert.Null(career.NextModel);
        // The shared data is untouched.
        Assert.False(TestData.Load().Teams.ContainsKey(OwnTeam.Id));
    }

    [Fact]
    public void AGarageSignsADriverRacesAndSavesWithItsOwnTeam()
    {
        var data = TestData.Load();
        var career = Garage();
        var driver = career.FreeAgents.Last();
        Assert.Equal(SignResult.Signed, career.Sign(driver.Id));
        var ev = career.CurrentSeason.Events.First(e => career.Enter(e.Id) == EntryResult.Entered);
        var race = career.PrepareRace();
        race.RunToEnd();
        career.FinishRace(race);
        Assert.Contains(career.ResultOf(ev)!.Result.Classification, c => c.Entry.Team.Id == OwnTeam.Id);

        var loaded = Career.FromJson(data, career.ToJson());
        Assert.Equal("Bentley Boys", loaded.Team.Name);
        Assert.Equal("#1f5a33", loaded.Team.Colour);
        Assert.Equal(career.Cash, loaded.Cash);
        Assert.Contains(loaded.ResultOf(ev)!.Result.Classification, c => c.Entry.Team.Id == OwnTeam.Id);
    }

    [Fact]
    public void ACustomerCarCanBeBoughtWhenTheFactoriesSellANewOne()
    {
        var career = Garage(1932);
        Assert.Equal("Alfa Rossa 8C Monza", career.Car.Name);
        Assert.Null(career.CustomerCarForSale);
        career.AdvanceTo(new DateOnly(1932, 12, 31));
        career.StartNextSeason();
        career.StartNextSeason();
        var forSale = career.CustomerCarForSale;
        Assert.NotNull(forSale);
        var cash = career.Cash;
        Assert.Equal(ProjectResult.Queued, career.BuyCustomerCar());
        Assert.Equal(forSale, career.Car);
        Assert.Equal(cash - OwnTeam.CustomerCarCost(forSale), career.Cash);
        Assert.Null(career.CustomerCarForSale);
    }

    [Fact]
    public void TheReichDoesNotFundAGermanGarage()
    {
        var career = Garage(1934, "Germany");
        Assert.Equal(Finance.WeeklyBacking(OwnTeam.Prestige), career.WeeklyBacking);
    }
}
