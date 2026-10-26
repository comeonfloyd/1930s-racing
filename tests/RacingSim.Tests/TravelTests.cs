namespace RacingSim.Tests;

public class TravelTests
{
    [Fact]
    public void DistanceFromMilanToMonzaIsAboutFifteenKm()
    {
        var data = TestData.Load();

        var plan = Travel.Plan(data.Cities["milan"], data.Circuits["monza"]);

        Assert.InRange(plan.DistanceKm, 10, 20);
        Assert.Equal(2, plan.Days);
        Assert.False(plan.CrossesAtlantic);
    }

    [Fact]
    public void GoingToIndianapolisCrossesTheAtlantic()
    {
        var data = TestData.Load();

        var plan = Travel.Plan(data.Circuits["monaco"], data.Circuits["indianapolis"]);

        Assert.True(plan.CrossesAtlantic);
        Assert.True(plan.Days > Travel.AtlanticCrossingDays + 2);
        Assert.True(plan.Cost > Travel.AtlanticCrossingCost);
    }
}
