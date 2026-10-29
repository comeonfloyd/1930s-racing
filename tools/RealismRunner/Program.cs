using System.Text.RegularExpressions;
using RacingSim;

// Runs real 1930s races many times with the field of their year and compares the spread of
// the results with what really happened: how many starters finished, the winner's margin, how
// far back third place was, how many finishers ran the full distance, and how long stops took.
// Real figures are from Leif Snellman's goldenera.fi race reports. The 1934 Italian Grand Prix
// ran on a shorter, chicaned Monza (116 laps of 4.31 km), so its gaps do not compare directly.
// Usage: dotnet run --project tools/RealismRunner -- [runs]
var runs = int.TryParse(args.FirstOrDefault(), out var r) ? r : 200;
var data = GameData.FromDirectory(FindDataDir());

// (event, starters, finishers, on the lead lap, gap 1-2 s, gap 1-3 s, winner km/h)
var real = new (string Id, int Starters, int Finishers, int LeadLap, double Second, double Third, double Speed)[]
{
    ("1930-monaco", 17, 6, 5, 23.8, 497.8, 86.3),
    ("1933-monaco", 18, 8, 2, 120.0, 140.6, 91.8),
    ("1933-tripoli", 29, 14, 3, 0.2, 91.8, 168.6),
    ("1934-tripoli", 26, 11, 9, 0.2, 22.2, 186.1),
    ("1936-tripoli", 26, 11, 4, 4.4, 133.2, 207.6),
    ("1938-tripoli", 27, 11, 8, 278.5, 313.6, 205.1),
    ("1935-germany", 20, 12, 6, 98.6, 142.9, 121.1),
    ("1937-germany", 26, 11, 6, 46.2, 61.3, 133.2),
    ("1934-italy", 15, 9, 1, 0, 0, 105.1),
    ("1933-lemans", 29, 13, 1, 10, 0, 131.0),
    ("1930-indianapolis", 38, 13, 1, 0, 0, 161.6),
    ("1938-indianapolis", 33, 9, 1, 0, 0, 188.6),
};

Console.WriteLine($"{"race",-14} {"",4} {"start",5} {"fin%",5} {"lead lap%",9} {"1-2 gap",8} {"1-3 gap",8} {"km/h",6} {"stop s",7}");
var only = args.FirstOrDefault(a => a.StartsWith("--only="))?.Split('=')[1].Split(',');
foreach (var rr in real.Where(x => only is null || only.Contains(x.Id)))
{
    var ev = data.Seasons.Values.SelectMany(s => s.Events).Single(e => e.Id == rr.Id);
    var circuit = data.Circuits[ev.CircuitId];
    var fin = new List<double>(); var lead = new List<double>(); var g2 = new List<double>(); var g3 = new List<double>();
    var speed = new List<double>(); var stops = new List<double>(); var starters = 0; var noFinishers = 0;
    Career career = null!;
    for (var seed = 0; seed < runs; seed++)
    {
        career = new Career(data, "alfa-rossa", ev.Date.Year, seed);
        career.Enter(ev.Id);
        var grid = career.GridFor(ev);
        starters = grid.Count;
        var race = new LiveRace(ev, circuit, grid, seed);
        race.RunToEnd();
        var res = race.Result().Classification;
        if (seed < 2 && args.Contains("--show"))
        {
            foreach (var c in res.Take(6))
                Console.WriteLine($"   {c.Entry.Driver.Name,-22} {c.Entry.Team.Car.Name,-22} spd {c.Entry.Driver.Skills.Speed} con {c.Entry.Driver.Skills.Consistency} ideal {RaceSim.AverageSpeedKmh(c.Entry, circuit):0.0} laps {c.LapsCompleted} t {c.TotalTime:0} {(c.Retirement is null ? "" : "OUT")}");
            Console.WriteLine();
        }
        var finished = res.Where(c => c.Finished).ToList();
        fin.Add(finished.Count / (double)res.Count);
        if (finished.Count == 0)
        {
            noFinishers++;
            continue;
        }
        var w = finished[0];
        lead.Add(finished.Count(c => c.LapsCompleted == w.LapsCompleted) / (double)finished.Count);
        if (finished.Count > 1) g2.Add(Gap(w, finished[1]));
        if (finished.Count > 2) g3.Add(Gap(w, finished[2]));
        speed.Add(circuit.LapKm * w.LapsCompleted / w.TotalTime * 3600);
        foreach (var m in race.Messages)
            if (Regex.Match(m.Text, @"pits for .*?, (\d+) s") is { Success: true } match)
                stops.Add(double.Parse(match.Groups[1].Value));
    }
    double LapTime(CarResult c) => c.TotalTime / c.LapsCompleted;
    double Gap(CarResult w, CarResult c) => c.LapsCompleted < w.LapsCompleted
        ? c.TotalTime - w.TotalTime + (w.LapsCompleted - c.LapsCompleted) * LapTime(c) : c.TotalTime - w.TotalTime;
    if (args.Contains("--fit"))
    {
        // The real winner's car is the quickest car on the grid; print what the speed formula sees.
        var top = career.GridFor(ev).MaxBy(e => RaceSim.AverageSpeedKmh(e, circuit))!;
        Console.WriteLine($"FIT {rr.Id} {top.Team.Car.Horsepower} {top.Team.Car.WeightKg} {top.Team.Car.Handling} {top.Driver.Skills.Speed} {circuit.StraightShare} {RaceSim.AverageSpeedKmh(top, circuit):0.0} {speed.Average():0.0} {rr.Speed}");
    }
    Console.WriteLine($"{rr.Id,-14} {"sim",4} {starters,5} {Pct(fin),5} {Pct(lead),9} {Med(g2),8} {Med(g3),8} {speed.Average(),6:0} {(stops.Count > 0 ? stops.Average() : 0),7:0}   ({ev.Laps} laps{(noFinishers > 0 ? $", no finisher in {noFinishers}" : "")})");
    Console.WriteLine($"{"",-14} {"real",4} {rr.Starters,5} {rr.Finishers * 100 / rr.Starters + "%",5} {rr.LeadLap * 100 / rr.Finishers + "%",9} {Secs(rr.Second),8} {Secs(rr.Third),8} {rr.Speed,6:0}");
}

static string Pct(List<double> v) => $"{v.Average() * 100:0}%";
static string Med(List<double> v) => v.Count == 0 ? "-" : Secs(v.Order().ElementAt(v.Count / 2));
static string Secs(double s) => s <= 0 ? "lap+" : s < 60 ? $"{s:0.0}s" : $"{(int)(s / 60)}m{s % 60:00}s";

static string FindDataDir()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "game", "data");
        if (Directory.Exists(candidate))
            return candidate;
    }
    throw new DirectoryNotFoundException("Could not find game/data above the program folder");
}
