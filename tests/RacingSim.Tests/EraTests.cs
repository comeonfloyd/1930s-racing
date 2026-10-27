namespace RacingSim.Tests;

public class EraTests
{
    private static Career CareerIn(int year, string team = "alfa-rossa")
    {
        var career = new Career(TestData.Load(), team, 1930, seed: 3);
        while (career.Today.Year < year)
            career.StartNextSeason();
        return career;
    }

    private static RaceEvent Event(Career career, string id) => career.CurrentSeason.Events.Single(e => e.Id == id);

    private static IEnumerable<string> TeamsOnGrid(Career career, string eventId) =>
        career.GridFor(Event(career, eventId)).Select(e => e.Team.Id).Distinct();

    [Fact]
    public void WorksTeamsArriveAndLeaveInTheirRealYears()
    {
        var data = TestData.Load();
        var career = new Career(data, "alfa-rossa", 1933);
        Assert.DoesNotContain("mercator-benz", TeamsOnGrid(career, "1933-monaco"));
        Assert.DoesNotContain("auto-unitas", TeamsOnGrid(career, "1933-monaco"));

        career = new Career(data, "alfa-rossa", 1934);
        Assert.Contains("mercator-benz", TeamsOnGrid(career, "1934-monaco"));
        Assert.Contains("auto-unitas", TeamsOnGrid(career, "1934-monaco"));
    }

    [Fact]
    public void TeamsRaceOnTheirOwnSideOfTheAtlantic()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1930);
        Assert.Equal(new[] { "duesenhoff", "millard", "us-privateers" }, TeamsOnGrid(career, "1930-indianapolis").Order());
        Assert.DoesNotContain("millard", TeamsOnGrid(career, "1930-monaco"));
        Assert.DoesNotContain("us-privateers", TeamsOnGrid(career, "1930-monaco"));
    }

    [Fact]
    public void DriversWithoutAWorksSeatRacePrivately()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1930);
        var monaco = Event(career, "1930-monaco");
        var privateers = career.GridFor(monaco).Where(e => e.Team.Privateer).ToList();
        Assert.InRange(privateers.Count, 4, Career.MaxPrivateers);
        Assert.All(privateers, p => Assert.Contains(career.FreeAgents, d => d.Id == p.Driver.Id));

        var signed = privateers[0].Driver.Id;
        career.Sign(signed);
        Assert.DoesNotContain(career.GridFor(monaco), e => e.Driver.Id == signed);
    }

    [Fact]
    public void RivalsBringOutTheirNewCarsEachYear()
    {
        var career = CareerIn(1934);
        Assert.Equal("Mercator-Benz W25", career.CarOf("mercator-benz").Name);
        Assert.Equal("Alfa Rossa P2", career.Car.Name);
        Assert.Contains(career.News, n => n.Text.Contains("750 kg formula comes in"));
    }

    [Fact]
    public void A750KgCarIsNeededForEuropeanRacesFrom1934()
    {
        var career = CareerIn(1934);
        Assert.Equal(820, career.Car.WeightKg);
        Assert.Equal(EntryResult.CarNotEligible, career.Enter("1934-italy"));
        Assert.Equal(EntryResult.Entered, career.Enter("1934-indianapolis"));
    }

    [Fact]
    public void TheDrawingOfficeCanBuildTheNextWorksModel()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1931, seed: 3);
        var model = career.NextModel;
        Assert.NotNull(model);
        Assert.Equal((1932, "Alfa Rossa P3"), (model.Value.Year, model.Value.Car.Name));

        Assert.Equal(ProjectResult.Queued, career.StartProject(Upgrade.NewCar));
        Assert.Null(career.NextModel);
        career.AdvanceDays(7 * DrawingOffice.Kind(Upgrade.NewCar).Weeks);

        // Built for 1932, so it waits in the works until the new season.
        Assert.Equal("Alfa Rossa 8C Monza", career.Car.Name);
        Assert.Equal(1932, career.WaitingModelYear);
        Assert.Equal(ProjectResult.NoNewModel, career.StartProject(Upgrade.NewCar));
        Assert.Equal(1932, Career.FromJson(TestData.Load(), career.ToJson()).WaitingModelYear);

        career.AdvanceTo(new DateOnly(1932, 1, 1));
        Assert.Equal("Alfa Rossa P3", career.Car.Name);
        Assert.Equal(1932, career.ModelYear);
        Assert.Null(career.WaitingModelYear);
        Assert.Equal(ProjectResult.NoNewModel, career.StartProject(Upgrade.NewCar));
    }

    [Fact]
    public void TheNewsWarnsAYearAheadThatTheCarWillBeRuledOut()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1936);
        career.AdvanceTo(new DateOnly(1937, 1, 1));
        Assert.Contains(career.News, n => n.Date == new DateOnly(1937, 1, 1) && n.Text.StartsWith("Next year: The Alfa Rossa 12C-36 has a 4.1-litre engine"));
    }

    [Fact]
    public void WorksDriversRaceForTheirWorksAndAreNotFreeAgents()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1935);
        Assert.Contains(career.GridFor(Event(career, "1935-monaco")), e => e.Driver.Id == "bernd-rosemeyer" && e.Team.Id == "auto-unitas");
        Assert.DoesNotContain(career.FreeAgents, d => d.Id == "bernd-rosemeyer");
    }

    [Fact]
    public void EngineTuningGrowsWithThePowerOfTheCar()
    {
        Assert.Equal(8, DrawingOffice.HorsepowerStep(new Car("Small", 155, 800, 15, 0.7)));
        Assert.Equal(28, DrawingOffice.HorsepowerStep(new Car("Big", 560, 750, 15, 0.7)));
    }

    [Fact]
    public void TheMakerPaysHalfForACarToNewRules()
    {
        var data = TestData.Load();
        // In 1937 the 12C-36 is legal, but its 4.1 litres are ruled out by the 1938 rules.
        var career = new Career(data, "alfa-rossa", 1937);
        Assert.Equal(1938, career.NextModel!.Value.Year);
        Assert.True(career.MakerPaysHalf);
        Assert.Equal(DrawingOffice.Kind(Upgrade.NewCar).BaseCost / 2, career.ProjectCost(Upgrade.NewCar));

        // In 1931 the 8C Monza may race anywhere in 1932, so the P3 costs the full price.
        var early = new Career(data, "alfa-rossa", 1931);
        Assert.False(early.MakerPaysHalf);
        Assert.Equal(DrawingOffice.Kind(Upgrade.NewCar).BaseCost, early.ProjectCost(Upgrade.NewCar));
    }
}
