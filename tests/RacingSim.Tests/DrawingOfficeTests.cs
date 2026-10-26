namespace RacingSim.Tests;

public class DrawingOfficeTests
{
    private static Career NewCareer() => new(TestData.Load(), "alfa-rossa", 1930, seed: 5);

    [Fact]
    public void AProjectIsPaidUpFrontAndImprovesTheCarWhenDone()
    {
        var career = NewCareer();
        var before = career.Car.Horsepower;

        Assert.Equal(ProjectResult.Queued, career.StartProject(Upgrade.Engine));
        Assert.Equal(12000 - DrawingOffice.Kind(Upgrade.Engine).BaseCost, career.Cash);
        Assert.Equal(before, career.Car.Horsepower);

        career.AdvanceDays(7 * DrawingOffice.Kind(Upgrade.Engine).Weeks);
        Assert.Equal(before + DrawingOffice.HorsepowerStep(career.Team.Car), career.Car.Horsepower);
        Assert.Empty(career.Projects);
        Assert.Contains(career.News, n => n.Text.StartsWith("Engine tuning finished"));
    }

    [Fact]
    public void ProjectsRunOneAtATimeInQueueOrder()
    {
        var career = NewCareer();
        career.StartProject(Upgrade.Engine);
        career.StartProject(Upgrade.Reliability);
        var engine = DrawingOffice.Kind(Upgrade.Engine).Weeks;
        var reliability = DrawingOffice.Kind(Upgrade.Reliability).Weeks;

        career.AdvanceDays(7 * (engine + reliability - 1));
        Assert.Equal(Upgrade.Reliability, Assert.Single(career.Projects).Upgrade);
        career.AdvanceDays(7);
        Assert.Empty(career.Projects);
    }

    [Fact]
    public void EachProjectOfAKindCostsMore()
    {
        var career = NewCareer();
        var first = career.ProjectCost(Upgrade.Handling);
        career.StartProject(Upgrade.Handling);

        Assert.True(career.ProjectCost(Upgrade.Handling) > first);
    }

    [Fact]
    public void CannotSpendMoneyTheTeamDoesNotHave()
    {
        var career = NewCareer();
        while (career.StartProject(Upgrade.Engine) == ProjectResult.Queued) { }

        Assert.Equal(ProjectResult.NotEnoughMoney, career.StartProject(Upgrade.Engine));
        Assert.True(career.Cash >= 0);
    }

    [Fact]
    public void ReliabilityStopsAtItsLimit()
    {
        var car = new Car("Test", 150, 800, 15, 0.93);

        Assert.True(DrawingOffice.CanImprove(car, Upgrade.Reliability));
        car = DrawingOffice.Apply(car, Upgrade.Reliability);
        Assert.Equal(DrawingOffice.MaxReliability, car.Reliability);
        Assert.False(DrawingOffice.CanImprove(car, Upgrade.Reliability));
    }

    [Fact]
    public void TheDevelopedCarIsTheOneThatRaces()
    {
        var career = NewCareer();
        career.StartProject(Upgrade.Engine);
        career.AdvanceTo(new DateOnly(1930, 2, 1));
        career.Enter("1930-monza");

        var ev = TestData.Load().Seasons[1930].Events.Single(e => e.Id == "1930-monza");
        var mine = career.GridFor(ev).Where(e => e.Team.Id == "alfa-rossa").ToList();
        Assert.NotEmpty(mine);
        Assert.All(mine, e => Assert.Equal(career.Car.Horsepower, e.Team.Car.Horsepower));
    }
}

public class RivalDevelopmentTests
{
    [Fact]
    public void RivalWorksImproveTheirCarsOverASeason()
    {
        var data = TestData.Load();
        var career = new Career(data, "alfa-rossa", 1930, seed: 2);
        career.AdvanceTo(new DateOnly(1930, 12, 31));

        var improved = data.Teams.Values.Where(t => t.Id != "alfa-rossa").Count(t => career.CarOf(t.Id) != t.Car);
        Assert.True(improved >= 2, $"only {improved} rival cars changed");
        Assert.Equal(data.Teams["alfa-rossa"].Car, career.CarOf("alfa-rossa"));
    }
}
