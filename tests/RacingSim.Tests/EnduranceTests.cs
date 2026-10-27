namespace RacingSim.Tests;

public class EnduranceTests
{
    private static (RaceEvent Event, Circuit Circuit, IReadOnlyList<RaceEntry> Grid) LeMans(int year, string team = "alfa-rossa")
    {
        var data = TestData.Load();
        var career = new Career(data, team, year);
        var ev = career.CurrentSeason.Events.Single(e => e.IsEndurance);
        Assert.Equal(EntryResult.Entered, career.Enter(ev.Id));
        return (ev, data.Circuits[ev.CircuitId], career.GridFor(ev));
    }

    [Fact]
    public void LeMansIsRunEveryJuneButNineteenThirtySix()
    {
        var data = TestData.Load();
        foreach (var (year, season) in data.Seasons)
        {
            var races = season.Events.Where(e => e.IsEndurance).ToList();
            if (year == 1936)
            {
                Assert.Empty(races);
                continue;
            }
            var race = Assert.Single(races);
            Assert.Equal("lemans", race.CircuitId);
            Assert.Equal(6, race.Date.Month);
            Assert.Equal(24, race.Hours);
        }
    }

    [Fact]
    public void TheRaceStopsAfterTwentyFourHoursAndTheMostLapsWin()
    {
        var (ev, circuit, grid) = LeMans(1933);
        var race = new LiveRace(ev, circuit, grid, 5);
        race.RunToEnd();
        Assert.Equal(24 * 3600, race.FlagTime);
        var result = race.Result().Classification;
        var winner = result[0];
        Assert.Null(winner.Retirement);
        Assert.All(result, c => Assert.True(c.LapsCompleted <= winner.LapsCompleted));
        // Between about 1800 and 3200 km in the day, as the winners of the time ran.
        Assert.InRange(winner.LapsCompleted * circuit.LapKm, 1800, 3200);
    }

    [Fact]
    public void ACarRunningWhenTheClockRunsOutIsClassified()
    {
        // A breakdown on the lap that runs past the 24 hours does not count: the car was running at the end.
        foreach (var seed in Enumerable.Range(0, 40))
        {
            var (ev, circuit, grid) = LeMans(1933);
            var race = new LiveRace(ev, circuit, grid, seed);
            race.RunToEnd();
            Assert.All(race.Result().Classification.Where(c => c.Retirement is { } r),
                c => Assert.True(race.Messages.Last(m => m.DriverId == c.Entry.Driver.Id).Time < 24 * 3600,
                    $"seed {seed}: {c.Entry.Names} retired after the clock"));
            Assert.Null(race.Result().Classification[0].Retirement);
        }
    }

    [Fact]
    public void EveryCarHasTwoDriversWhoTakeTurns()
    {
        var (ev, circuit, grid) = LeMans(1932);
        Assert.All(grid.Where(e => e.Team.Id == "alfa-rossa"), e => Assert.NotNull(e.CoDriver));
        var race = new LiveRace(ev, circuit, grid, 3);
        race.RunToEnd();
        var shared = grid.Where(e => e.CoDriver is not null).ToList();
        Assert.NotEmpty(shared);
        // Both drivers of the cars that ran long take the wheel.
        foreach (var entry in shared.Where(e => race.Result().Classification.Single(c => c.Entry == e).LapsCompleted > 60))
        {
            // Messages are keyed by the car's first driver and name whoever is at the wheel.
            Assert.Contains(race.Messages, m => m.DriverId == entry.Driver.Id && m.Text == $"{entry.CoDriver!.Name} takes over the car");
            Assert.Contains(race.Messages, m => m.DriverId == entry.Driver.Id && m.Text == $"{entry.Driver.Name} takes over the car");
        }
    }

    [Fact]
    public void NoCarRefuelsWithinTwentyFourLapsOfTheLastFill()
    {
        foreach (var seed in Enumerable.Range(0, 5))
        {
            var (ev, circuit, grid) = LeMans(1934);
            var race = new LiveRace(ev, circuit, grid, seed);
            race.RunToEnd();
            foreach (var entry in grid)
            {
                var fills = race.Messages
                    .Where(m => m.DriverId == entry.Driver.Id && m.Text.Contains("pits for fuel"))
                    .Select(m => m.Lap)
                    .ToList();
                Assert.True(fills.Count > 0 || race.Result().Classification.Single(c => c.Entry == entry).LapsCompleted < 30);
                foreach (var (a, b) in fills.Zip(fills.Skip(1)))
                    Assert.True(b - a >= LiveRace.MinLapsBetweenRefuels, $"{entry.Names} filled on laps {a} and {b}");
            }
        }
    }

    [Fact]
    public void ADriverLeftOutByThePitWallStillComesInForFuel()
    {
        foreach (var seed in Enumerable.Range(0, 10))
        {
            var (ev, circuit, grid) = LeMans(1933);
            var race = new LiveRace(ev, circuit, grid, seed, manualTeams: ["alfa-rossa"]);
            race.RunToEnd();
            Assert.DoesNotContain(race.Result().Classification,
                c => c.Entry.Team.Id == "alfa-rossa" && c.Retirement?.Cause == RetirementCause.OutOfFuel);
        }
    }

    [Fact]
    public void TheRaceStartsInDaylightAndRunsThroughTheNight()
    {
        var (ev, circuit, grid) = LeMans(1931);
        var race = new LiveRace(ev, circuit, grid, 1);
        Assert.Equal(16, LiveRace.HourOfDay(0));
        Assert.False(race.IsNightAt(0));                 // 4 pm
        Assert.True(race.IsNightAt(7 * 3600));           // 11 pm
        Assert.True(race.IsNightAt(11 * 3600));          // 3 am
        Assert.False(race.IsNightAt(14 * 3600));         // 6 am
        // A Grand Prix has no night.
        var gp = TestData.Load().Seasons[1931].Events.First(e => !e.IsEndurance);
        Assert.False(new LiveRace(gp, TestData.Load().Circuits[gp.CircuitId], grid, 1).IsNightAt(7 * 3600));
    }

    [Fact]
    public void ATeamNeedsASportsCarAndTwoDriversToEnter()
    {
        var data = TestData.Load();
        // Bugetti built no sports car for Le Mans in 1930.
        var bugetti = new Career(data, "bugetti", 1930);
        var race = bugetti.CurrentSeason.Events.Single(e => e.IsEndurance);
        Assert.Null(bugetti.SportsCarFor(race));
        Assert.Equal(EntryResult.NoSportsCar, bugetti.Enter(race.Id));

        var alfa = new Career(data, "alfa-rossa", 1931);
        var ev = alfa.CurrentSeason.Events.Single(e => e.IsEndurance);
        Assert.NotNull(alfa.SportsCarFor(ev));
        Assert.Equal(EntryResult.Entered, alfa.Enter(ev.Id));
        // The player's cars run the sports car, not the Grand Prix car, and the real field is there too.
        var grid = alfa.GridFor(ev);
        Assert.All(grid.Where(e => e.Team.Id == "alfa-rossa"), e => Assert.Equal(alfa.SportsCarFor(ev), e.Team.Car));
        Assert.Contains(grid, e => e.Team.Id != "alfa-rossa");
        // No driver is in two cars.
        var drivers = grid.SelectMany(e => e.Drivers).Select(d => d.Id).ToList();
        Assert.Equal(drivers.Count, drivers.Distinct().Count());
    }

    [Fact]
    public void ASharedCarSavesBothDrivers()
    {
        var data = TestData.Load();
        var career = new Career(data, "alfa-rossa", 1931);
        var ev = career.CurrentSeason.Events.Single(e => e.IsEndurance);
        career.Enter(ev.Id);
        career.AdvanceTo(ev.Date);
        var race = career.PrepareRace();
        race.RunToEnd();
        career.FinishRace(race);
        var loaded = Career.FromJson(data, career.ToJson());
        var record = loaded.Results.Single(r => r.Event.Id == ev.Id);
        Assert.Contains(record.Result.Classification, c => c.Entry.Team.Id == "alfa-rossa" && c.Entry.CoDriver is not null);
    }
}
