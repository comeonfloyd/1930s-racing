namespace RacingSim;

public enum RetirementCause
{
    MechanicalFailure,
    Crash,
    FatalCrash,
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
/// Lap-by-lap race simulation. It is deterministic for a given seed, so a race can be replayed
/// exactly for testing and bug reports. All tuning numbers here are first-pass placeholders.
/// </summary>
public static class RaceSim
{
    public static RaceResult Run(RaceEvent ev, Circuit circuit, IReadOnlyList<RaceEntry> grid, int seed, RaceSettings? settings = null)
    {
        settings ??= new RaceSettings();
        var rng = new Random(seed);
        var laps = grid.Select(_ => new List<double>()).ToArray();
        var retired = new Retirement?[grid.Count];
        var positions = new List<IReadOnlyList<string>>();
        // Each car's form on the day: up to about 2% either way, so the fastest car does not always win.
        var form = grid.Select(_ => 1 + Gaussian(rng) * 0.01).ToArray();

        for (var lap = 1; lap <= ev.Laps; lap++)
        {
            for (var i = 0; i < grid.Count; i++)
            {
                if (retired[i] is not null)
                    continue;

                var entry = grid[i];
                if (rng.NextDouble() < FailureChancePerLap(entry, circuit))
                {
                    retired[i] = new Retirement(lap, RetirementCause.MechanicalFailure);
                    continue;
                }
                if (rng.NextDouble() < CrashChancePerLap(entry.Driver, circuit))
                {
                    var fatal = settings.FatalCrashes && rng.NextDouble() < circuit.Danger * 0.15;
                    retired[i] = new Retirement(lap, fatal ? RetirementCause.FatalCrash : RetirementCause.Crash);
                    continue;
                }
                laps[i].Add(LapTime(entry, circuit, rng) * form[i]);
            }

            positions.Add(Order(grid, laps).Select(i => grid[i].Driver.Id).ToList());
        }

        var classification = Order(grid, laps)
            .Select(i => new CarResult(grid[i], laps[i], retired[i]))
            .ToList();
        return new RaceResult(ev, circuit, classification) { Positions = positions };
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

    private static double LapTime(RaceEntry entry, Circuit circuit, Random rng)
    {
        var ideal = circuit.LapKm / AverageSpeedKmh(entry, circuit) * 3600;
        // Less consistent drivers spread their laps wider; wet skill comes in with weather later.
        var spread = (21 - entry.Driver.Skills.Consistency) * 0.005;
        return ideal * (1 + Math.Abs(Gaussian(rng)) * spread);
    }

    private static double FailureChancePerLap(RaceEntry entry, Circuit circuit)
    {
        // Reliability is the chance to survive 500 km; spread that risk over the laps.
        var perKm = -Math.Log(entry.Team.Car.Reliability) / 500;
        var sympathy = 1.3 - entry.Driver.Skills.MechanicalSympathy / 40.0;
        return 1 - Math.Exp(-perKm * circuit.LapKm * sympathy);
    }

    private static double CrashChancePerLap(Driver driver, Circuit circuit) =>
        circuit.Danger * 0.0015 * (1 + (driver.Skills.Bravery - 10) / 20.0) * (21 - driver.Skills.Consistency) / 10.0 * circuit.LapKm / 5;

    /// <summary>Most laps first, then least total time.</summary>
    private static IEnumerable<int> Order(IReadOnlyList<RaceEntry> grid, List<double>[] laps) =>
        Enumerable.Range(0, grid.Count)
            .OrderByDescending(i => laps[i].Count)
            .ThenBy(i => laps[i].Sum())
            .ThenBy(i => i);

    private static double Gaussian(Random rng)
    {
        var u1 = 1 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    /// <summary>Every driver of every team, in team order: the default grid for an event.</summary>
    public static IReadOnlyList<RaceEntry> FullGrid(GameData data) =>
        data.Teams.Values
            .SelectMany(t => t.DriverIds.Select(id => new RaceEntry(data.Drivers[id], t)))
            .ToList();
}
