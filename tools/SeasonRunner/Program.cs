using RacingSim;

// Plays many 1930 seasons headless to check the money balance: the team enters every race it
// can reach, in date order, and the AI runs its pit wall.
// With --develop the team also orders one project of each kind in January.
// Usage: dotnet run --project tools/SeasonRunner -- [teamId] [seasons] [year] [--develop]
var develop = args.Contains("--develop");
args = args.Where(a => !a.StartsWith("--")).ToArray();
var teamId = args.ElementAtOrDefault(0) ?? "alfa-rossa";
var seasons = int.TryParse(args.ElementAtOrDefault(1), out var n) ? n : 200;
var year = int.TryParse(args.ElementAtOrDefault(2), out var y) ? y : 1930;

var data = GameData.FromDirectory(FindDataDir());
var summaries = new List<SeasonSummary>();
var deaths = 0;
for (var seed = 0; seed < seasons; seed++)
{
    var career = new Career(data, teamId, year, seed);
    if (develop)
        foreach (var kind in DrawingOffice.Kinds)
            career.StartProject(kind.Id);
    foreach (var ev in career.CurrentSeason.Events)
        career.Enter(ev.Id);
    while (career.NextRace is not null)
    {
        var race = career.PrepareRace();
        race.RunToEnd();
        career.FinishRace(race);
    }
    career.AdvanceTo(new DateOnly(year, 12, 31));
    summaries.Add(career.Summary(year));
    deaths += career.DeadDrivers.Count;
}

var team = data.Teams[teamId];
Console.WriteLine($"{team.Name}, {year}, {seasons} seasons, entering every reachable race{(develop ? ", developing the car" : "")}");
Console.WriteLine($"  races per season  {summaries.Average(s => s.Races):0.0}");
Console.WriteLine($"  wins              {summaries.Average(s => s.Wins):0.00}");
Console.WriteLine($"  podium races      {summaries.Average(s => s.Podiums):0.00}");
Console.WriteLine($"  income            £{summaries.Average(s => s.Income):N0}");
Console.WriteLine($"  costs             £{summaries.Average(s => s.Costs):N0}");
Console.WriteLine($"  cash at year end  £{summaries.Average(s => s.CashAtEnd):N0} (start £{team.StartingCash:N0}; worst £{summaries.Min(s => s.CashAtEnd):N0}, best £{summaries.Max(s => s.CashAtEnd):N0})");
Console.WriteLine($"  drivers killed    {deaths / (double)seasons:0.00} per season, all teams");

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
