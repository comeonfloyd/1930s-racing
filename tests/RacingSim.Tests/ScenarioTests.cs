namespace RacingSim.Tests;

public class ScenarioTests
{
    [Fact]
    public void ScenariosStartLaterWithTheirOwnTeamsAndBudgets()
    {
        var data = TestData.Load();
        Assert.Equal(1930, data.Scenarios[0].Year);
        var silver = data.Scenarios.Single(s => s.Id == "1934");
        var career = new Career(data, "mercator-benz", 1934, seed: 2, scenarioId: "1934");
        Assert.Equal(new DateOnly(1934, 1, 1), career.Today);
        Assert.Equal(silver.Teams.Single(t => t.TeamId == "mercator-benz").Cash, career.Cash);
        Assert.Equal("Mercator-Benz W25", career.Car.Name);
        Assert.Contains(career.Drivers, d => d.Id == "rudolf-caracciola");
        Assert.Equal("alfred-neubauer", career.StaffIn(StaffRole.Manager)?.Id);
        // Mercedes is the player's now, not a rival.
        Assert.DoesNotContain(career.RivalsIn(1934), t => t.Id == "mercator-benz");
        // A team that is not on offer that year still cannot be run.
        Assert.Throws<ArgumentException>(() => new Career(data, "mercator-benz", 1932));
        Assert.Throws<ArgumentException>(() => new Career(data, "masarelli", 1930));
    }

    [Fact]
    public void TheReichPaysAGermanWorksUnlessTheSettingsSayOtherwise()
    {
        var data = TestData.Load();
        var funded = new Career(data, "auto-unitas", 1934, seed: 2, scenarioId: "1934");
        var unfunded = new Career(data, "auto-unitas", 1934, seed: 2, new RaceSettings(StateFundedGermans: false), "1934");
        Assert.Equal(Finance.StateGrant, funded.WeeklyBacking - unfunded.WeeklyBacking);
        Assert.Equal(0, new Career(data, "alfa-rossa", 1934, seed: 2).WeeklyBacking
                        - new Career(data, "alfa-rossa", 1934, seed: 2, new RaceSettings(StateFundedGermans: false)).WeeklyBacking);
    }

    [Fact]
    public void AScenarioCareerSavesAndLoadsAfterItsFirstYear()
    {
        var data = TestData.Load();
        var career = new Career(data, "masarelli", 1938, seed: 2, scenarioId: "1938");
        career.AdvanceTo(new DateOnly(1938, 12, 31));
        career.StartNextSeason();
        var loaded = Career.FromJson(data, career.ToJson());
        Assert.Equal("1938", loaded.ScenarioId);
        Assert.Equal(career.Team.Id, loaded.Team.Id);
        Assert.Equal(career.Today, loaded.Today);
        Assert.Equal(career.Cash, loaded.Cash);
    }

    [Fact]
    public void EveryScenarioTeamCanRaceItsFirstSeason()
    {
        var data = TestData.Load();
        foreach (var scenario in data.Scenarios)
        foreach (var entry in scenario.Teams)
        {
            var career = new Career(data, entry.TeamId, scenario.Year, seed: 5, scenarioId: scenario.Id);
            Assert.NotEmpty(career.Drivers);
            var entered = career.CurrentSeason.Events.Count(ev => career.Enter(ev.Id) == EntryResult.Entered);
            Assert.True(entered >= 3, $"{entry.TeamId} in {scenario.Year} could enter only {entered} races");
        }
    }
}
