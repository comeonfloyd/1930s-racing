namespace RacingSim;

/// <summary>Where one car is at a moment of the race.</summary>
/// <param name="Distance">Laps covered so far, e.g. 3.25 is a quarter of the way round lap 4.</param>
public sealed record CarPosition(RaceEntry Entry, double Distance, bool Running, Retirement? Retirement, bool Finished);

/// <summary>
/// Turns a finished <see cref="RaceResult"/> into positions at any race time, so a view can
/// animate it. Within a lap a car moves at even speed; a retired car stops halfway round the
/// lap it retired on.
/// </summary>
public sealed class RaceReplay
{
    private readonly RaceResult _result;
    private readonly double[][] _lapEnds;

    public RaceReplay(RaceResult result)
    {
        _result = result;
        _lapEnds = result.Classification.Select(c => CumulativeSum(c.LapTimes)).ToArray();
        Duration = result.Classification.Where(c => c.LapsCompleted > 0).Select(c => c.TotalTime).DefaultIfEmpty(0).Max();
    }

    public RaceResult Result => _result;

    /// <summary>Race time in seconds when the last car still running takes the flag.</summary>
    public double Duration { get; }

    /// <summary>Every car's position at a race time in seconds, leader first.</summary>
    public IReadOnlyList<CarPosition> At(double time)
    {
        var cars = new List<CarPosition>();
        for (var i = 0; i < _result.Classification.Count; i++)
        {
            var car = _result.Classification[i];
            var ends = _lapEnds[i];
            var total = ends.Length == 0 ? 0 : ends[^1];

            if (car.Finished && time >= total)
            {
                cars.Add(new CarPosition(car.Entry, car.LapsCompleted, false, null, true));
                continue;
            }

            // A retiring car is assumed to stop halfway through its last lap.
            var lastLap = ends.Length == 0 ? AverageLap(car) : car.LapTimes[^1];
            var stopTime = total + lastLap / 2;
            if (car.Retirement is not null && time >= stopTime)
            {
                cars.Add(new CarPosition(car.Entry, car.LapsCompleted + 0.5, false, car.Retirement, false));
                continue;
            }

            var lap = Array.FindIndex(ends, e => e > time);
            if (lap < 0)
            {
                // Still on the lap it will retire on.
                var into = (time - total) / lastLap;
                cars.Add(new CarPosition(car.Entry, car.LapsCompleted + into, true, null, false));
                continue;
            }
            var start = lap == 0 ? 0 : ends[lap - 1];
            var fraction = (time - start) / car.LapTimes[lap];
            cars.Add(new CarPosition(car.Entry, lap + fraction, true, null, false));
        }

        return cars.OrderByDescending(c => c.Distance).ToList();
    }

    private double AverageLap(CarResult car)
    {
        var laps = _result.Classification.SelectMany(c => c.LapTimes).ToList();
        return laps.Count == 0 ? 60 : laps.Average();
    }

    private static double[] CumulativeSum(IReadOnlyList<double> values)
    {
        var sums = new double[values.Count];
        double running = 0;
        for (var i = 0; i < values.Count; i++)
            sums[i] = running += values[i];
        return sums;
    }
}
