using System.Text.Json;

namespace RacingSim.Tests;

public class SpeedProfileTests
{
    private static readonly Car P3 = new("Alfa Rossa P3", 215, 700, 15, 0.9);
    private static readonly Car W125 = new("Mercator-Benz W125", 560, 750, 15, 0.9);

    private static TrackLine Line(string circuitId)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestData.DataDir, "tracks", $"{circuitId}.json")));
        var points = doc.RootElement.GetProperty("points").EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList();
        return new TrackLine(TrackLine.Smooth(points, 16), TestData.Load().Circuits[circuitId].LapKm);
    }

    [Fact]
    public void LapStartsAndEndsOnTheLineAndOnlyGoesForward()
    {
        var profile = new SpeedProfile(Line("monaco"), P3, CircuitType.Street);
        Assert.Equal(0, profile.DistanceAt(0));
        Assert.Equal(1, profile.DistanceAt(1));
        var last = 0.0;
        for (var t = 0.001; t < 1; t += 0.001)
        {
            var d = profile.DistanceAt(t);
            Assert.True(d >= last, $"Went backwards at {t}");
            last = d;
        }
    }

    [Fact]
    public void BrakesForTheHairpinsAndFliesOnTheStraights()
    {
        var profile = new SpeedProfile(Line("monaco"), P3, CircuitType.Street);
        Assert.InRange(profile.SlowestKmh, 20, 60);
        Assert.InRange(profile.TopSpeedKmh, 140, 230);
    }

    [Fact]
    public void TopSpeedsAreOfThePeriod()
    {
        // The long straights of Tripoli: about 230 km/h for an Alfa P3, 300 for a 1937 Mercedes.
        var line = Line("tripoli");
        Assert.InRange(new SpeedProfile(line, P3, CircuitType.RoadCourse).TopSpeedKmh, 200, 250);
        Assert.InRange(new SpeedProfile(line, W125, CircuitType.RoadCourse).TopSpeedKmh, 280, 330);
    }

    [Fact]
    public void BankedOvalIsTakenFasterThanAFlatOne()
    {
        var line = Line("indianapolis");
        Assert.True(new SpeedProfile(line, P3, CircuitType.BrickOval).SlowestKmh
                    > new SpeedProfile(line, P3, CircuitType.RoadCourse).SlowestKmh + 10);
    }

    [Fact]
    public void MatchingTakesTheRaceLapTime()
    {
        var line = Line("nurburgring");
        foreach (var seconds in new[] { 600.0, 660, 720 })
            Assert.InRange(SpeedProfile.Matching(line, W125, CircuitType.MountainRoads, seconds).LapSeconds, seconds - 1, seconds + 1);
    }

    [Fact]
    public void StandingStartLapIsSlowerAndStartsFromRest()
    {
        var line = Line("monza");
        var flying = new SpeedProfile(line, P3, CircuitType.RoadCourse);
        var standing = new SpeedProfile(line, P3, CircuitType.RoadCourse, standingStart: true);
        Assert.Equal(0, standing.SpeedKmhAt(0));
        Assert.InRange(standing.LapSeconds - flying.LapSeconds, 2, 15);
        Assert.Equal(flying.SpeedKmhAt(0.5), standing.SpeedKmhAt(0.5), 3);
    }

    [Fact]
    public void EveryCircuitHasAProfile()
    {
        foreach (var circuit in TestData.Load().Circuits.Values)
        {
            var profile = new SpeedProfile(Line(circuit.Id), P3, circuit.Type);
            var kmh = circuit.LapKm / profile.LapSeconds * 3600;
            Assert.InRange(kmh, 70, 240);
        }
    }

    [Fact]
    public void LiveCarSaysWhenTheLapStartedFromRest()
    {
        var data = TestData.Load();
        var ev = data.Seasons.Values.SelectMany(s => s.Events).First(e => !e.IsEndurance);
        var race = new LiveRace(ev, data.Circuits[ev.CircuitId], RaceSim.FullGrid(data), 1);
        race.AdvanceTo(10);
        Assert.All(race.Cars(), c => Assert.True(c.StandingStart));
        var car = race.Cars().Where(c => c.Running).MaxBy(c => c.LapSeconds)!;
        Assert.True(car.LapSeconds > 30);
        race.AdvanceTo(car.LapSeconds * 1.5);
        Assert.False(race.Cars().Single(c => c.Entry.Driver.Id == car.Entry.Driver.Id).StandingStart);
    }
}
