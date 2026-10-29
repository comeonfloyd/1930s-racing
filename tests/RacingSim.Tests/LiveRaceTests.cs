namespace RacingSim.Tests;

public class LiveRaceTests
{
    private static LiveRace Race(string eventId, int seed, params string[] manualTeams)
    {
        var data = TestData.Load();
        var ev = data.Seasons[1930].Events.Single(e => e.Id == eventId);
        return new LiveRace(ev, data.Circuits[ev.CircuitId], RaceSim.FullGrid(data), seed, manualTeams: manualTeams);
    }

    private static IEnumerable<(string, double)> Times(RaceResult r) =>
        r.Classification.Select(c => (c.Entry.Driver.Id, c.TotalTime));

    [Fact]
    public void LappedCarsStopWhenTheWinnerTakesTheFlag()
    {
        var lapped = 0;
        foreach (var seed in Enumerable.Range(1, 8))
        {
            var race = Race("1930-monaco", seed);
            race.RunToEnd();
            var result = race.Result();
            var flag = race.FlagTime!.Value;
            Assert.Equal(flag, result.Classification[0].TotalTime, 6);
            foreach (var car in result.Classification.Where(c => c.Finished))
            {
                // Each finisher crossed the line once after the flag, and not again.
                Assert.True(car.TotalTime >= flag - 1e-6);
                Assert.True(car.TotalTime - car.LapTimes[^1] < flag);
                if (car.LapsCompleted < race.Event.Laps)
                    lapped++;
            }
        }
        Assert.True(lapped > 0, "no lapped finishers to check");
    }

    [Fact]
    public void StepSizeDoesNotChangeTheRace()
    {
        var once = Race("1930-belgium", 9);
        once.RunToEnd();

        var stepped = Race("1930-belgium", 9);
        for (var t = 0.0; !stepped.IsOver; t += 7.5)
            stepped.AdvanceTo(t);

        Assert.Equal(Times(once.Result()), Times(stepped.Result()));
    }

    [Fact]
    public void LongRacesNeedPitStopsAndAiCarsMakeThem()
    {
        // Spa is about 600 km, more than two tanks.
        var race = Race("1930-belgium", 4);
        race.RunToEnd();

        Assert.Contains(race.Messages, m => m.Text.Contains("pits"));
        Assert.DoesNotContain(race.Result().Classification, c => c.Retirement?.Cause == RetirementCause.OutOfFuel);
    }

    [Fact]
    public void APlayersDriverComesInForFuelIfNeverCalled()
    {
        var race = Race("1930-belgium", 4, "alfa-rossa");
        race.RunToEnd();
        Assert.DoesNotContain(race.Result().Classification,
            c => c.Entry.Team.Id == "alfa-rossa" && c.Retirement?.Cause == RetirementCause.OutOfFuel);
    }

    [Fact]
    public void CalledCarStopsAtTheEndOfItsLap()
    {
        var race = Race("1930-monza", 2, "alfa-rossa");
        var driver = TestData.Load().Teams["alfa-rossa"].DriverIds[0];

        race.AdvanceTo(600);
        race.CallPitStop(driver);
        var before = race.Cars().Single(c => c.Entry.Driver.Id == driver);
        Assert.True(before.PitCalled);

        var lapsBefore = (int)before.Distance;
        for (var t = 600.0; t < 3000; t += 1)
        {
            race.AdvanceTo(t);
            var car = race.Cars().Single(c => c.Entry.Driver.Id == driver);
            if (!car.Running || car.PitStops == 1)
                break;
        }
        var after = race.Cars().Single(c => c.Entry.Driver.Id == driver);
        if (after.Retirement is not null)
            return; // retired before reaching the pits: nothing to check on this seed

        Assert.Equal(1, after.PitStops);
        Assert.True(after.InPits);
        Assert.Equal(lapsBefore + 1, (int)after.Distance);
        Assert.InRange(after.TyreWear, 0, 0.1); // fresh tyres, worn only by the lap just started
    }

    [Fact]
    public void ManualCarsAreNotCalledInByTheAi()
    {
        // Monza is short enough to run without a stop, so only a call brings a car in.
        var race = Race("1930-monza", 6, "alfa-rossa");
        race.RunToEnd();

        var alfaDrivers = TestData.Load().Teams["alfa-rossa"].DriverIds.ToHashSet();
        Assert.DoesNotContain(race.Messages, m => alfaDrivers.Contains(m.DriverId) && m.Text.Contains("pits") && !m.Text.Contains("tyre"));
    }

    [Fact]
    public void PushingIsFasterThanNursingButHarderOnTyres()
    {
        var data = TestData.Load();
        var driver = data.Teams["alfa-rossa"].DriverIds[0];
        var laps = new Dictionary<PaceOrder, List<double>> { [PaceOrder.Push] = new(), [PaceOrder.Nurse] = new() };
        var wear = new Dictionary<PaceOrder, List<double>> { [PaceOrder.Push] = new(), [PaceOrder.Nurse] = new() };
        for (var seed = 0; seed < 40; seed++)
        {
            foreach (var pace in new[] { PaceOrder.Push, PaceOrder.Nurse })
            {
                var race = Race("1930-monza", seed, "alfa-rossa");
                race.SetPace(driver, pace);
                race.AdvanceTo(1800);
                var car = race.Cars().Single(c => c.Entry.Driver.Id == driver);
                if (!car.Running)
                    continue;
                laps[pace].Add(car.Distance);
                wear[pace].Add(car.TyreWear / car.Distance);
            }
        }

        Assert.True(laps[PaceOrder.Push].Average() > laps[PaceOrder.Nurse].Average(), "pushing should cover more ground in the same time");
        Assert.True(wear[PaceOrder.Push].Average() > wear[PaceOrder.Nurse].Average(), "pushing should wear tyres faster per lap");
    }

    [Fact]
    public void PlannedStopsBringTheCarInAtTheEndOfThoseLaps()
    {
        var driver = TestData.Load().Teams["alfa-rossa"].DriverIds[0];
        var checkedOne = false;
        foreach (var seed in Enumerable.Range(1, 6))
        {
            var race = Race("1930-monza", seed, "alfa-rossa");
            var stops = race.EvenStops(driver, 2);
            Assert.Equal(2, stops.Count);
            race.SetPlannedStops(driver, stops);
            race.RunToEnd();
            var car = race.Cars().Single(c => c.Entry.Driver.Id == driver);
            if (car.Retirement is not null && car.Retirement.Lap <= stops[^1] + 1)
                continue;
            // A stop at the end of lap n is told on lap n + 1, as the car sets off again.
            var told = race.Messages.Where(m => m.DriverId == driver && m.Text.Contains("pits")).Select(m => m.Lap).ToList();
            Assert.Equal(stops.Select(l => l + 1), told);
            Assert.Empty(car.PlannedStops!);
            Assert.True(car.PitSeconds > 0);
            checkedOne = true;
        }
        Assert.True(checkedOne, "every seed retired the car before its stops");
    }

    [Fact]
    public void EvenStopsSplitTheRaceIntoEqualStints()
    {
        var race = Race("1930-monza", 1, "alfa-rossa");
        var driver = TestData.Load().Teams["alfa-rossa"].DriverIds[0];
        var laps = race.Event.Laps;
        var one = race.EvenStops(driver, 1);
        Assert.Single(one);
        Assert.InRange(one[0], laps / 2 - 1, laps / 2 + 1);
        Assert.Empty(race.EvenStops(driver, 0));
    }

    [Fact]
    public void ATyresOnlyStopAddsNoFuel()
    {
        var race = Race("1930-monza", 3, "alfa-rossa");
        var driver = TestData.Load().Teams["alfa-rossa"].DriverIds[0];
        race.SetPitPlan(driver, new PitPlan(FuelPlan.None, Tyres: true));
        race.AdvanceTo(600);
        race.CallPitStop(driver);
        var fuelBefore = race.Cars().Single(c => c.Entry.Driver.Id == driver).Fuel;
        for (var t = 600.0; t < 3000; t += 1)
        {
            race.AdvanceTo(t);
            var car = race.Cars().Single(c => c.Entry.Driver.Id == driver);
            if (!car.Running || car.PitStops == 1)
                break;
        }
        var after = race.Cars().Single(c => c.Entry.Driver.Id == driver);
        if (after.Retirement is not null)
            return;
        Assert.True(after.Fuel < fuelBefore);
        Assert.Contains(race.Messages, m => m.DriverId == driver && m.Text.Contains("tyres only"));
    }

    [Fact]
    public void TheTimingKnowsLastAndBestLapsAndTheGrid()
    {
        var race = Race("1930-monza", 5);
        race.RunToEnd();
        var cars = race.Cars();
        var result = race.Result();
        Assert.Equal(Enumerable.Range(1, cars.Count), cars.Select(c => c.Grid).Order());
        foreach (var car in cars.Where(c => c.LapsDone > 0))
        {
            var laps = result.Classification.Single(r => r.Entry.Driver.Id == car.Entry.Driver.Id).LapTimes;
            Assert.Equal(laps.Min(), car.BestLap);
            Assert.Equal(laps[^1], car.LastLap);
            Assert.Equal(laps.Min(), laps[car.BestLapNumber - 1]);
        }
    }
}
