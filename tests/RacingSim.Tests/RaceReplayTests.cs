namespace RacingSim.Tests;

public class RaceReplayTests
{
    private static RaceReplay Monza(int seed)
    {
        var data = TestData.Load();
        var ev = data.Seasons[1930].Events.Single(e => e.Id == "1930-monza");
        return new RaceReplay(RaceSim.Run(ev, data.Circuits[ev.CircuitId], RaceSim.FullGrid(data), seed));
    }

    [Fact]
    public void EveryCarIsOnTheGridAtTheStart()
    {
        var replay = Monza(1);

        Assert.All(replay.At(0), c => Assert.Equal(0, c.Distance));
    }

    [Fact]
    public void WinnerHasCoveredTheFullDistanceAtTheEnd()
    {
        var replay = Monza(2);
        var winner = replay.Result.Classification[0];

        var end = replay.At(replay.Duration + 1);

        Assert.Equal(winner.Entry.Driver.Id, end[0].Entry.Driver.Id);
        Assert.Equal(35, end[0].Distance);
        Assert.True(end[0].Finished);
    }

    [Fact]
    public void CarsOnlyMoveForward()
    {
        var replay = Monza(3);
        var previous = replay.At(0).ToDictionary(c => c.Entry.Driver.Id, c => c.Distance);

        for (var t = 60.0; t < replay.Duration + 120; t += 60)
        {
            foreach (var car in replay.At(t))
            {
                Assert.True(car.Distance >= previous[car.Entry.Driver.Id] - 1e-9);
                previous[car.Entry.Driver.Id] = car.Distance;
            }
        }
    }

    [Fact]
    public void RetiredCarsStopPartWayThroughTheirLastLap()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var replay = Monza(seed);
            var retired = replay.Result.Classification.FirstOrDefault(c => !c.Finished);
            if (retired is null)
                continue;

            var end = replay.At(replay.Duration + 1).Single(c => c.Entry.Driver.Id == retired.Entry.Driver.Id);
            Assert.False(end.Running);
            Assert.Equal(retired.LapsCompleted + 0.5, end.Distance);
            Assert.Equal(retired.Retirement, end.Retirement);
            return;
        }
        Assert.Fail("No retirement in 50 races");
    }
}
