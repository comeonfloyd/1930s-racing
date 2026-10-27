using RacingSim;

// Plays whole careers, 1930 to 1939, headless, with a simple manager: enter every race the
// cars can reach, keep two drivers or more, build the works' next car when it is better and
// affordable, lighten the car when the rules demand it, and spend spare money on tuning.
// Prints, for each year, the average wins, cash and car across all careers.
// Usage: dotnet run --project tools/CareerRunner -- [teamId] [careers]
// --idle: never develop the car, to compare against.
var idle = args.Contains("--idle");
// --money: also print the average money in and out by kind, per year.
var money = args.Contains("--money");
// --works: also build up the facilities and test before races when there is money to spare.
var works = args.Contains("--works");
// --races=YEAR: print each race of that year in the first career, with the field's cars.
var racesYear = args.FirstOrDefault(a => a.StartsWith("--races="))?.Split('=')[1] is { } ry ? int.Parse(ry) : 0;
args = args.Where(a => !a.StartsWith("--")).ToArray();
var teamId = args.ElementAtOrDefault(0) ?? "alfa-rossa";
var careers = int.TryParse(args.ElementAtOrDefault(1), out var n) ? n : 20;

var data = GameData.FromDirectory(FindDataDir());
var years = data.Seasons.Keys.Order().ToList();
var rows = years.ToDictionary(y => y, _ => new List<(SeasonSummary Summary, Car Car, int Drivers)>());
var ledgers = new List<Transaction>();
var broke = 0;
var lostDrivers = 0;
for (var seed = 0; seed < careers; seed++)
{
    var career = new Career(data, teamId, years[0], seed);
    while (true)
    {
        while (true)
        {
            // Plan and enter again before each race: a new car may make more races open.
            if (!idle)
                Plan(career, works);
            foreach (var ev in career.CurrentSeason.Events)
                career.Enter(ev.Id);
            if (career.NextRace is null)
                break;
            var race = career.PrepareRace();
            race.HandOverPitWall(career.Team.Id);
            race.RunToEnd();
            var record = career.FinishRace(race);
            if (seed == 0 && record.Event.Date.Year == racesYear)
            {
                Console.WriteLine($"{record.Event.Name}: " + string.Join(", ", record.Result.Classification.Take(6).Select(c =>
                    $"{c.Entry.Driver.Name} ({c.Entry.Team.Car.Name} {c.Entry.Team.Car.Horsepower}/{c.Entry.Team.Car.WeightKg}/h{c.Entry.Team.Car.Handling}/r{c.Entry.Team.Car.Reliability:0.00}){(c.Finished ? "" : " out")}")));
            }
        }
        career.AdvanceTo(new DateOnly(career.Today.Year, 12, 31));
        var year = career.Today.Year;
        var summary = career.Summary(year);
        rows[year].Add((summary, career.Car, career.Drivers.Count));
        lostDrivers += summary.DriversLost.Count;
        if (career.Cash < 0)
        {
            ledgers.AddRange(career.Ledger);
            broke++;
            break;
        }
        if (!career.HasNextSeason)
        {
            ledgers.AddRange(career.Ledger);
            break;
        }
        career.StartNextSeason();
    }
}

var team = data.Teams[teamId];
Console.WriteLine($"{team.Name}, {careers} careers from {years[0]}");
Console.WriteLine("year  careers  races  wins  podiums  cash at end (min)    car (most common)");
foreach (var (year, list) in rows.Where(r => r.Value.Count > 0))
{
    var car = list.GroupBy(r => r.Car.Name).OrderByDescending(g => g.Count()).First();
    Console.WriteLine($"{year}  {list.Count,7}  {list.Average(r => r.Summary.Races),5:0.0}  {list.Average(r => r.Summary.Wins),4:0.0}  {list.Average(r => r.Summary.Podiums),7:0.0}  " +
                      $"£{list.Average(r => r.Summary.CashAtEnd),8:N0} (£{list.Min(r => r.Summary.CashAtEnd):N0})  {car.Key}, {car.Average(r => r.Car.Horsepower):0} hp, {car.Average(r => r.Car.WeightKg):0} kg, h{car.Average(r => r.Car.Handling):0.0}, r{car.Average(r => r.Car.Reliability):0.00}");
}
if (money)
{
    Console.WriteLine();
    Console.WriteLine("year  " + string.Join("", Enum.GetNames<MoneyKind>().Select(k => $"{k,12}")));
    foreach (var year in years)
    {
        var books = ledgers.Where(t => t.Date.Year == year).ToList();
        Console.WriteLine($"{year}  " + string.Join("", Enum.GetValues<MoneyKind>().Select(k => $"{books.Where(t => t.Kind == k).Sum(t => t.Amount) / careers,12:N0}")));
    }
}
Console.WriteLine($"careers that went broke: {broke}; own drivers killed: {lostDrivers / (double)careers:0.0} per career");

static void Plan(Career career, bool works)
{
    // Keep at least two cars racing.
    while (career.Drivers.Count < 2 && career.FreeAgents.FirstOrDefault(d => Finance.SigningFee(d) < career.Cash / 4) is { } driver)
        career.Sign(driver.Id);

    var reserve = 2500;
    if (works)
    {
        // The cheapest next level first, keeping money for the car; a test day when it is cheap enough.
        var next = Enum.GetValues<Facility>()
            .Where(f => career.Level(f) < Works.MaxLevel(f))
            .OrderBy(f => Works.BuildCost(f, career.Level(f) + 1))
            .FirstOrDefault(f => f != Facility.TestTrack || career.Level(Facility.Workshop) == 3);
        if (career.Building is null && career.Level(next) < Works.MaxLevel(next) && Works.BuildCost(next, career.Level(next) + 1) + reserve + 2000 < career.Cash)
            career.Build(next);
        if (career.TestCost + reserve < career.Cash)
            career.TestDay();
    }
    // Build the next model when it is clearly quicker, or when the rules will soon rule the car out.
    var year = career.Today.Year;
    var wanted = career.NextModel is { } model
        && (Better(model.Car, career.Car) || Formula.Breach(career.Car, year + 1) is not null && Formula.Breach(model.Car, year + 1) is null);
    if (wanted && career.ProjectCost(Upgrade.NewCar) + reserve < career.Cash)
    {
        career.StartProject(Upgrade.NewCar);
        wanted = false;
    }
    // Saving up for a new car: no tuning until it is paid for.
    if (wanted)
        reserve += career.ProjectCost(Upgrade.NewCar);

    // Under the 750 kg rule a heavy car must lose weight before it can race in Europe.
    var weight = career.Car.WeightKg;
    while (year >= Formula.FirstWeightYear && year < Formula.FirstLitreYear && weight > Formula.MaxWeightKg
           && career.Projects.All(p => p.Upgrade != Upgrade.NewCar)
           && career.StartProject(Upgrade.Lightening) == ProjectResult.Queued)
        weight -= DrawingOffice.WeightStep;

    // Then tuning while there is money to spare, keeping about two months of work queued,
    // the cheapest job first (so the kinds take turns as each gets dearer).
    while (career.Projects.Sum(p => p.Weeks - p.WeeksDone) < 8)
    {
        var options = DrawingOffice.Tuning.Select(k => k.Id).OrderBy(career.ProjectCost);
        if (!options.Any(u => career.ProjectCost(u) + reserve < career.Cash && career.StartProject(u) == ProjectResult.Queued))
            break;
    }
}

static bool Better(Car next, Car now) =>
    next.Horsepower / (double)next.WeightKg > now.Horsepower / (double)now.WeightKg * 1.05;

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
