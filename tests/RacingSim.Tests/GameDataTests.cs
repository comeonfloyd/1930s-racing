namespace RacingSim.Tests;

public class GameDataTests
{
    [Fact]
    public void LoadsTheMvpVenues()
    {
        var data = TestData.Load();

        Assert.Equal(
            new[] { "indianapolis", "monaco", "montlhery", "monza", "nurburgring", "spa" },
            data.Circuits.Keys.Order());
    }

    [Fact]
    public void AlfaRossaAndBugettiArePlayable()
    {
        var data = TestData.Load();

        Assert.Equal(new[] { "alfa-rossa", "bugetti" }, data.Teams.Values.Where(t => t.Playable).Select(t => t.Id).Order());
    }

    [Fact]
    public void SeasonEventsAreInDateOrder()
    {
        foreach (var season in TestData.Load().Seasons.Values)
            Assert.Equal(season.Events.OrderBy(e => e.Date), season.Events);
    }

    [Fact]
    public void RejectsEventAtUnknownCircuit()
    {
        var season = new Season(1930, new[] { new RaceEvent("x", "X", "nowhere", new DateOnly(1930, 5, 1), 10, 0, 0, 0) });

        Assert.Throws<InvalidDataException>(() => new GameData(
            Array.Empty<Circuit>(), Array.Empty<City>(), Array.Empty<Driver>(), Array.Empty<Team>(), new[] { season }));
    }
}
