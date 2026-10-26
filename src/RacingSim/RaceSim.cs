namespace RacingSim;

public enum RetirementCause
{
    MechanicalFailure,
    Crash,
    FatalCrash,
    OutOfFuel,
}

/// <summary>One car on the grid: who drives it and for which team.</summary>
public sealed record RaceEntry(Driver Driver, Team Team);

public sealed record Retirement(int Lap, RetirementCause Cause);

/// <summary>A car's race: every completed lap time in seconds, and how it ended.</summary>
public sealed record CarResult(RaceEntry Entry, IReadOnlyList<double> LapTimes, Retirement? Retirement)
{
    public int LapsCompleted => LapTimes.Count;
    public double TotalTime => LapTimes.Sum();
    public bool Finished => Retirement is null;
}

public sealed record RaceResult(RaceEvent Event, Circuit Circuit, IReadOnlyList<CarResult> Classification)
{
    /// <summary>Running order after each lap: Positions[lap - 1] lists driver ids, leader first.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Positions { get; init; } = [];
}

public sealed record RaceSettings(bool FatalCrashes = true);

/// <summary>
/// Runs a whole race at once with every car on the AI strategy (see <see cref="LiveRace"/> for
/// the race itself). It is deterministic for a given seed, so a race can be replayed exactly for
/// testing and bug reports.
/// </summary>
public static class RaceSim
{
    public static RaceResult Run(RaceEvent ev, Circuit circuit, IReadOnlyList<RaceEntry> grid, int seed, RaceSettings? settings = null)
    {
        var race = new LiveRace(ev, circuit, grid, seed, settings);
        race.RunToEnd();
        return race.Result();
    }

    /// <summary>Base speed for this car and driver on this circuit, before lap-to-lap variation.</summary>
    public static double AverageSpeedKmh(RaceEntry entry, Circuit circuit)
    {
        var car = entry.Team.Car;
        var powerToWeight = car.Horsepower / (car.WeightKg / 1000.0);
        return 25 + 70 * circuit.StraightShare
            + 0.18 * powerToWeight * (0.1 + circuit.StraightShare)
            + 1.2 * car.Handling * (1.2 - circuit.StraightShare)
            + 0.8 * entry.Driver.Skills.Speed;
    }

    /// <summary>Every driver of every team, in team order: the default grid for an event.</summary>
    public static IReadOnlyList<RaceEntry> FullGrid(GameData data) =>
        data.Teams.Values
            .SelectMany(t => t.DriverIds.Select(id => new RaceEntry(data.Drivers[id], t)))
            .ToList();
}
