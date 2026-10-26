using RacingSim;

// Runs one race headless and prints the result and a lap chart.
// Usage: dotnet run --project tools/RaceRunner -- [eventId] [seed] [--no-fatal]
var eventId = args.FirstOrDefault(a => !a.StartsWith("--") && !int.TryParse(a, out _)) ?? "1930-monza";
var seed = args.Select(a => int.TryParse(a, out var n) ? n : (int?)null).FirstOrDefault(n => n is not null) ?? 1930;
var settings = new RaceSettings(FatalCrashes: !args.Contains("--no-fatal"));

var data = GameData.FromDirectory(FindDataDir());
var ev = data.Seasons.Values.SelectMany(s => s.Events).FirstOrDefault(e => e.Id == eventId)
    ?? throw new ArgumentException($"Unknown event {eventId}");
var circuit = data.Circuits[ev.CircuitId];
var result = RaceSim.Run(ev, circuit, RaceSim.FullGrid(data), seed, settings);

Console.WriteLine($"{ev.Name}, {circuit.Name}, {ev.Date:d MMMM yyyy}");
Console.WriteLine($"{ev.Laps} laps of {circuit.LapKm} km, seed {seed}");
Console.WriteLine();

var winner = result.Classification[0];
for (var i = 0; i < result.Classification.Count; i++)
{
    var car = result.Classification[i];
    var name = $"{car.Entry.Driver.Name} ({car.Entry.Team.Name})";
    string status;
    if (car.Retirement is { } r)
        status = $"out on lap {r.Lap}: {Describe(r.Cause)}";
    else if (i == 0)
        status = $"{Clock(car.TotalTime)}  avg {circuit.LapKm * car.LapsCompleted / car.TotalTime * 3600:F1} km/h";
    else if (car.LapsCompleted < winner.LapsCompleted)
        status = $"+{winner.LapsCompleted - car.LapsCompleted} laps";
    else
        status = $"+{Clock(car.TotalTime - winner.TotalTime)}";
    Console.WriteLine($"{(car.Finished ? (i + 1).ToString() : "-"),3}  {name,-40} {status}");
}

Console.WriteLine();
Console.WriteLine("Lap chart (position after each lap; columns are laps)");
var drivers = result.Classification.Select(c => c.Entry.Driver).ToList();
var step = Math.Max(1, ev.Laps / 20);
var shownLaps = Enumerable.Range(1, ev.Laps).Where(l => l == 1 || l % step == 0 || l == ev.Laps).ToList();
Console.WriteLine($"{"",-24}" + string.Concat(shownLaps.Select(l => $"{l,4}")));
foreach (var d in drivers)
{
    var cells = shownLaps.Select(l =>
    {
        var car = result.Classification.First(c => c.Entry.Driver.Id == d.Id);
        return car.LapsCompleted >= l ? $"{result.Positions[l - 1].ToList().IndexOf(d.Id) + 1,4}" : "   .";
    });
    Console.WriteLine($"{d.Name,-24}" + string.Concat(cells));
}

static string Describe(RetirementCause cause) => cause switch
{
    RetirementCause.MechanicalFailure => "mechanical failure",
    RetirementCause.Crash => "crashed",
    RetirementCause.FatalCrash => "fatal crash",
    _ => cause.ToString(),
};

static string Clock(double seconds)
{
    var t = TimeSpan.FromSeconds(seconds);
    return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t:mm\\:ss\\.f}" : $"{t:m\\:ss\\.f}";
}

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
