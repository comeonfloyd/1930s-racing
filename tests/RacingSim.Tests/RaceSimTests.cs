namespace RacingSim.Tests;

public class RaceSimTests
{
    private static RaceResult Monza(int seed, RaceSettings? settings = null)
    {
        var data = TestData.Load();
        var ev = data.Seasons[1930].Events.Single(e => e.Id == "1930-monza");
        return RaceSim.Run(ev, data.Circuits[ev.CircuitId], RaceSim.FullGrid(data), seed, settings);
    }

    [Fact]
    public void SameSeedGivesTheSameRace()
    {
        var a = Monza(42);
        var b = Monza(42);

        Assert.Equal(a.Classification.Select(c => (c.Entry.Driver.Id, c.TotalTime)), b.Classification.Select(c => (c.Entry.Driver.Id, c.TotalTime)));
    }

    [Fact]
    public void EveryCarStartsAndFinishersRunFullDistance()
    {
        var result = Monza(7);

        Assert.Equal(RaceSim.FullGrid(TestData.Load()).Count, result.Classification.Count);
        Assert.All(result.Classification.Where(c => c.Finished), c => Assert.Equal(35, c.LapsCompleted));
        Assert.All(result.Classification.Where(c => !c.Finished), c => Assert.Equal(c.Retirement!.Lap - 1, c.LapsCompleted));
    }

    [Fact]
    public void ClassificationIsByLapsThenTime()
    {
        var c = Monza(3).Classification;

        for (var i = 1; i < c.Count; i++)
        {
            Assert.True(c[i - 1].LapsCompleted >= c[i].LapsCompleted);
            if (c[i - 1].LapsCompleted == c[i].LapsCompleted)
                Assert.True(c[i - 1].TotalTime <= c[i].TotalTime);
        }
    }

    [Fact]
    public void LapChartHasOneRowPerLap()
    {
        var result = Monza(5);

        Assert.Equal(35, result.Positions.Count);
        Assert.Equal(result.Classification[0].Entry.Driver.Id, result.Positions[^1][0]);
    }

    [Fact]
    public void MonzaAverageSpeedIsPlausibleForTheEra()
    {
        var winner = Monza(11).Classification[0];
        var kmh = 6.9 * winner.LapsCompleted / winner.TotalTime * 3600;

        Assert.InRange(kmh, 100, 170);
    }

    [Fact]
    public void NoDeathsWhenFatalCrashesAreOff()
    {
        var data = TestData.Load();
        var ev = data.Seasons[1930].Events.Single(e => e.Id == "1930-eifel");
        var settings = new RaceSettings(FatalCrashes: false);

        for (var seed = 0; seed < 300; seed++)
        {
            var result = RaceSim.Run(ev, data.Circuits[ev.CircuitId], RaceSim.FullGrid(data), seed, settings);
            Assert.DoesNotContain(result.Classification, c => c.Retirement?.Cause == RetirementCause.FatalCrash);
        }
    }
}
