namespace RacingSim.Tests;

public class DriverMarketTests
{
    private static Career NewCareer() => new(TestData.Load(), "alfa-rossa", 1930, seed: 4);

    [Fact]
    public void FreeAgentsAreDriversWithoutAWorksSeatThisYear()
    {
        var career = NewCareer();
        var free = career.FreeAgents.Select(d => d.Id).ToList();

        Assert.Contains("rene-dreyfus", free);
        Assert.DoesNotContain("achille-varzi", free);      // Masarelli works driver
        Assert.DoesNotContain("tazio-nuvolari", free);     // already ours
        Assert.DoesNotContain("raymond-sommer", free);     // starts racing in 1931
    }

    [Fact]
    public void SigningPaysTheFeeAndTheDriverRacesForTheTeam()
    {
        var career = NewCareer();
        var dreyfus = TestData.Load().Drivers["rene-dreyfus"];

        Assert.Equal(SignResult.Signed, career.Sign(dreyfus.Id));
        Assert.Equal(12000 - Finance.SigningFee(dreyfus), career.Cash);
        Assert.Contains(career.Drivers, d => d.Id == dreyfus.Id);
        Assert.DoesNotContain(career.FreeAgents, d => d.Id == dreyfus.Id);

        career.Enter("1930-monza");
        var ev = TestData.Load().Seasons[1930].Events.Single(e => e.Id == "1930-monza");
        Assert.Contains(career.GridFor(ev), e => e.Driver.Id == dreyfus.Id && e.Team.Id == "alfa-rossa");
    }

    [Fact]
    public void ATeamRunsAtMostFourCars()
    {
        var career = NewCareer();
        Assert.Equal(SignResult.Signed, career.Sign("rene-dreyfus"));

        Assert.Equal(SignResult.TeamFull, career.Sign("hans-stuck"));
    }

    [Fact]
    public void ReleasedDriversBecomeFreeAndTheLastDriverStays()
    {
        var career = NewCareer();
        Assert.True(career.Release("luigi-arcangeli"));
        Assert.Contains(career.FreeAgents, d => d.Id == "luigi-arcangeli");
        Assert.True(career.Release("giuseppe-campari"));

        Assert.False(career.Release("tazio-nuvolari"));
        Assert.Single(career.Drivers);
    }

    [Fact]
    public void WagesFollowTheRoster()
    {
        var career = NewCareer();
        var before = career.WeeklyCosts;
        career.Release("luigi-arcangeli");

        Assert.Equal(before - Finance.DriverWage(TestData.Load().Drivers["luigi-arcangeli"]), career.WeeklyCosts);
    }

    [Fact]
    public void TheRosterIsSaved()
    {
        var data = TestData.Load();
        var career = NewCareer();
        career.Release("luigi-arcangeli");
        career.Sign("rene-dreyfus");

        var loaded = Career.FromJson(data, career.ToJson());
        Assert.Equal(career.Drivers.Select(d => d.Id), loaded.Drivers.Select(d => d.Id));
    }
}
