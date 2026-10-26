namespace RacingSim;

public enum EntryResult
{
    Entered,
    AlreadyEntered,
    EventPassed,
    CannotArriveInTime,
    UnknownEvent,
    NoDrivers,
    CarNotEligible,
}

public enum SignResult
{
    Signed,
    NotAvailable,
    TeamFull,
    NotEnoughMoney,
}

public enum ProjectResult
{
    Queued,
    NotEnoughMoney,
    AtLimit,
    NoNewModel,
}

/// <summary>A dated line of team news, such as a finished drawing-office project.</summary>
public sealed record NewsItem(DateOnly Date, string Text);

/// <summary>A race that has been run, entered by the player or not, and what it paid the player.</summary>
public sealed record RaceRecord(RaceEvent Event, RaceResult Result, bool Entered, int Earned)
{
    public CarResult Winner => Result.Classification[0];
}

/// <summary>What the player's team did in one season.</summary>
public sealed record SeasonSummary(int Year, int Races, int Wins, int Podiums, int CashAtStart, int Income, int Costs, int CashAtEnd, IReadOnlyList<string> DriversLost);

/// <summary>
/// The player's running career: which team they run, today's date, which events they have
/// entered, the money, and every race run so far. Entries are planned in date order from the
/// team's base, so the cars can only be where the previous entry left them.
///
/// Time moves forward with <see cref="AdvanceTo"/>: weekly costs are paid and races the player
/// did not enter are run in the background. An entered race is run with <see cref="PrepareRace"/>
/// and <see cref="FinishRace"/>, so the player can watch it and give orders in between.
/// </summary>
public sealed partial class Career
{
    private readonly GameData _data;
    private readonly SortedSet<RaceEvent> _entries = new(Comparer<RaceEvent>.Create((a, b) =>
        a.Date != b.Date ? a.Date.CompareTo(b.Date) : string.CompareOrdinal(a.Id, b.Id)));

    private readonly List<Transaction> _ledger = new();
    private readonly List<RaceRecord> _results = new();
    private readonly HashSet<string> _deadDrivers = new();
    private DateOnly _paidUntil;
    private readonly List<Project> _projects = new();
    private readonly Dictionary<Upgrade, int> _upgradesDone = new();
    private readonly List<NewsItem> _news = new();
    private readonly Dictionary<string, Car> _rivalCars = new();
    private readonly List<string> _roster;

    /// <summary>The year whose works cars the rival teams are running (see <see cref="CatchUpYear"/>).</summary>
    private int _carsYear;

    /// <summary>The year the player's car was designed, from the team's models in the data.</summary>
    public int ModelYear { get; private set; }

    /// <summary>Everyone signed to the team, including drivers killed racing.</summary>
    public IReadOnlyList<string> Roster => _roster;

    /// <summary>Most cars a team can run at one race.</summary>
    public const int MaxDrivers = 4;

    public Team Team { get; }
    public DateOnly Today { get; private set; }
    public int Cash { get; private set; }
    public int Seed { get; }
    public RaceSettings Settings { get; }
    public IReadOnlyCollection<RaceEvent> Entries => _entries;
    public IReadOnlyList<Transaction> Ledger => _ledger;
    public IReadOnlyList<RaceRecord> Results => _results;
    public IReadOnlySet<string> DeadDrivers => _deadDrivers;

    /// <summary>The team's car as developed so far.</summary>
    public Car Car { get; private set; }

    /// <summary>Every other team's car as its works have developed it so far.</summary>
    public Car CarOf(string teamId) => teamId == Team.Id ? Car : _rivalCars.GetValueOrDefault(teamId) ?? _data.Teams[teamId].CarFor(_carsYear);

    /// <summary>The other works racing in a year: those founded by then that send drivers.</summary>
    public IEnumerable<Team> RivalsIn(int year) =>
        _data.Teams.Values.Where(t => t.Id != Team.Id && !t.Privateer && t.RosterFor(year).Count > 0).OrderBy(t => t.Id, StringComparer.Ordinal);

    /// <summary>Most private entrants at one race.</summary>
    public const int MaxPrivateers = 8;

    /// <summary>A rival's drivers in a year: its works line-up, less anyone dead, retired or signed by the player.</summary>
    public IReadOnlyList<string> RosterOf(Team team, int year) => team.Id == Team.Id
        ? _roster.Where(id => !_deadDrivers.Contains(id)).ToList()
        : team.RosterFor(year).Where(id => !_deadDrivers.Contains(id) && !_roster.Contains(id) && Racing(_data.Drivers[id], year)).ToList();

    /// <summary>Drawing-office work: the first project is being worked on, the rest wait.</summary>
    public IReadOnlyList<Project> Projects => _projects;
    public IReadOnlyList<NewsItem> News => _news;

    public Career(GameData data, string teamId, int startYear, int seed = 1930, RaceSettings? settings = null)
    {
        Seed = seed;
        Settings = settings ?? new RaceSettings();
        _data = data;
        Team = data.Teams[teamId];
        if (!Team.Playable)
            throw new ArgumentException($"Team {teamId} is not playable", nameof(teamId));
        Today = new DateOnly(startYear, 1, 1);
        _paidUntil = Today;
        Cash = Team.StartingCash;
        Car = Team.CarFor(startYear);
        ModelYear = Team.CarYearFor(startYear);
        _roster = Team.RosterFor(startYear).ToList();
        _carsYear = startYear;
    }

    /// <summary>
    /// Drivers anyone can sign this year: alive, racing this year, and not under contract with
    /// another works. Drivers the player releases join them.
    /// </summary>
    public IReadOnlyList<Driver> FreeAgents => FreeAgentsIn(Today.Year);

    private IReadOnlyList<Driver> FreeAgentsIn(int year)
    {
        var contracted = RivalsIn(year).SelectMany(t => t.RosterFor(year)).Concat(_roster).ToHashSet();
        return _data.Drivers.Values
            .Where(d => !contracted.Contains(d.Id) && !_deadDrivers.Contains(d.Id) && Racing(d, year))
            .OrderByDescending(d => d.Skills.Speed)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Drivers without a works seat race privately on their own side of the Atlantic: Americans
    /// in America, everyone else in Europe. A different few at each race when there are many.
    /// </summary>
    private IEnumerable<RaceEntry> PrivateersFor(RaceEvent ev, Circuit circuit)
    {
        var year = ev.Date.Year;
        foreach (var team in _data.Teams.Values.Where(t => t.Privateer && _data.Cities[t.BaseCityId].Region == circuit.Region).OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            var car = team.CarFor(year);
            if (Formula.Breach(car, circuit, year) is not null)
                continue;
            var entrant = team with { Car = car };
            var rng = new Random(RaceSeed(ev));
            foreach (var driver in FreeAgentsIn(year)
                         .Where(d => (Region(d) == circuit.Region))
                         .OrderBy(_ => rng.Next())
                         .Take(MaxPrivateers))
                yield return new RaceEntry(driver, entrant);
        }
    }

    private static Region Region(Driver d) => d.Nationality == "USA" ? RacingSim.Region.Usa : RacingSim.Region.Europe;

    private bool Racing(Driver d) => Racing(d, Today.Year);

    private static bool Racing(Driver d, int year) => d.FirstYear <= year && year <= d.LastYear;

    public SignResult Sign(string driverId)
    {
        var driver = FreeAgents.FirstOrDefault(d => d.Id == driverId);
        if (driver is null)
            return SignResult.NotAvailable;
        if (Drivers.Count >= MaxDrivers)
            return SignResult.TeamFull;
        var fee = Finance.SigningFee(driver);
        if (fee > Cash)
            return SignResult.NotEnoughMoney;
        Pay(MoneyKind.Wages, $"Signing fee, {driver.Name}", -fee);
        _roster.Add(driverId);
        _news.Add(new NewsItem(Today, $"{driver.Name} signs for {Team.Name} at £{Finance.DriverWage(driver)} a week."));
        return SignResult.Signed;
    }

    /// <summary>Lets a driver go; the team must keep at least one.</summary>
    public bool Release(string driverId)
    {
        if (Drivers.Count <= 1 || !_roster.Remove(driverId))
            return false;
        _news.Add(new NewsItem(Today, $"{_data.Drivers[driverId].Name} leaves {Team.Name}."));
        return true;
    }

    public int TimesDone(Upgrade upgrade) =>
        _upgradesDone.GetValueOrDefault(upgrade) + _projects.Count(p => p.Upgrade == upgrade);

    /// <summary>What the next project of this kind costs, counting ones already queued.</summary>
    public int ProjectCost(Upgrade upgrade) => DrawingOffice.Cost(upgrade, TimesDone(upgrade));

    /// <summary>Pays for a project and puts it at the back of the drawing-office queue.</summary>
    public ProjectResult StartProject(Upgrade upgrade)
    {
        if (upgrade == Upgrade.NewCar)
            return StartNewCar();
        var planned = _projects.Where(p => p.Upgrade == upgrade).Aggregate(Car, (car, p) => DrawingOffice.Apply(car, p.Upgrade));
        if (!DrawingOffice.CanImprove(planned, upgrade))
            return ProjectResult.AtLimit;
        var cost = ProjectCost(upgrade);
        if (cost > Cash)
            return ProjectResult.NotEnoughMoney;
        var kind = DrawingOffice.Kind(upgrade);
        Pay(MoneyKind.Development, kind.Name, -cost);
        _projects.Add(new Project(upgrade, cost, kind.Weeks));
        return ProjectResult.Queued;
    }

    /// <summary>
    /// The works' next model that the drawing office could build: the newest one in the data
    /// from up to a year ahead, if it is newer than the car the team has. Null when there is none
    /// or one is already being designed.
    /// </summary>
    public (int Year, Car Car)? NextModel
    {
        get
        {
            if (_projects.Any(p => p.Upgrade == Upgrade.NewCar))
                return null;
            var year = Team.CarYearFor(Today.Year + 1);
            return year > ModelYear ? (year, Team.CarFor(year)) : null;
        }
    }

    private ProjectResult StartNewCar()
    {
        if (NextModel is not { } model)
            return ProjectResult.NoNewModel;
        var cost = ProjectCost(Upgrade.NewCar);
        if (cost > Cash)
            return ProjectResult.NotEnoughMoney;
        var kind = DrawingOffice.Kind(Upgrade.NewCar);
        Pay(MoneyKind.Development, $"{kind.Name}: {model.Car.Name}", -cost);
        _projects.Add(new Project(Upgrade.NewCar, cost, kind.Weeks) { ModelYear = model.Year });
        return ProjectResult.Queued;
    }

    /// <summary>
    /// The other works develop their cars too: each week a team may finish a piece of work, more
    /// often the richer it is. Seeded by the career and the week, so a season always plays out the
    /// same. Rates are first-pass placeholders, balanced with tools/SeasonRunner.
    /// </summary>
    private void DevelopRivals(DateOnly week)
    {
        var rng = new Random(unchecked(Seed * 7919 + week.DayNumber));
        foreach (var team in RivalsIn(week.Year))
        {
            var chance = DrawingOffice.RivalWeeklyChance(team.StartingCash);
            if (rng.NextDouble() >= chance)
                continue;
            var car = CarOf(team.Id);
            var options = DrawingOffice.Tuning.Select(k => k.Id).Where(u => DrawingOffice.CanImprove(car, u)).ToList();
            if (options.Count == 0)
                continue;
            var upgrade = options[rng.Next(options.Count)];
            _rivalCars[team.Id] = DrawingOffice.Apply(car, upgrade);
            _news.Add(new NewsItem(week, $"{team.Name}: {DrawingOffice.Kind(upgrade).RivalNews}."));
        }
    }

    /// <summary>A week of drawing-office work on the first project in the queue.</summary>
    private void WorkOnProjects(DateOnly date)
    {
        if (_projects.Count == 0)
            return;
        var project = _projects[0] with { WeeksDone = _projects[0].WeeksDone + 1 };
        if (project.WeeksDone < project.Weeks)
        {
            _projects[0] = project;
            return;
        }
        _projects.RemoveAt(0);
        var kind = DrawingOffice.Kind(project.Upgrade);
        if (project.Upgrade == Upgrade.NewCar)
        {
            // A new car starts its development afresh, so tuning costs go back to their base.
            Car = Team.CarFor(project.ModelYear);
            ModelYear = project.ModelYear;
            _upgradesDone.Clear();
            _news.Add(new NewsItem(date, $"The new {Car.Name} is ready: {Car.Horsepower} hp, {Car.WeightKg} kg, {Car.EngineLitres:0.0#} litres, handling {Car.Handling}, reliability {Car.Reliability:P0}."));
            return;
        }
        _upgradesDone[project.Upgrade] = _upgradesDone.GetValueOrDefault(project.Upgrade) + 1;
        Car = DrawingOffice.Apply(Car, project.Upgrade);
        _news.Add(new NewsItem(date, $"{kind.Name} finished: {kind.Effect}. The {Car.Name} now has {Car.Horsepower} hp, weighs {Car.WeightKg} kg, handling {Car.Handling}, reliability {Car.Reliability:P0}."));
    }

    /// <summary>The team's drivers still alive.</summary>
    public IReadOnlyList<Driver> Drivers =>
        _roster.Where(id => !_deadDrivers.Contains(id)).Select(id => _data.Drivers[id]).ToList();

    public int WeeklyCosts => Finance.WeeklyCosts(Drivers);

    public int WeeklyBacking => Finance.WeeklyBacking(Team.Prestige);

    /// <summary>The next entered race still to be run, if any.</summary>
    public RaceEvent? NextRace => _entries.FirstOrDefault(e => e.Date >= Today && !HasRun(e));

    public RaceRecord? ResultOf(RaceEvent ev) => _results.FirstOrDefault(r => r.Event.Id == ev.Id);

    private bool HasRun(RaceEvent ev) => _results.Any(r => r.Event.Id == ev.Id);

    /// <summary>True once every race of the current season has been run.</summary>
    public bool SeasonOver => CurrentSeason.Events.All(HasRun);

    public bool HasNextSeason => _data.Seasons.ContainsKey(Today.Year + 1);

    /// <summary>
    /// The cars on the grid: the player's team if entered, and the other works racing that year
    /// that are based on the race's side of the Atlantic and whose cars meet the rules.
    /// </summary>
    public IReadOnlyList<RaceEntry> GridFor(RaceEvent ev)
    {
        var circuit = _data.Circuits[ev.CircuitId];
        var year = ev.Date.Year;
        var teams = RivalsIn(year)
            .Where(t => ev.AbsentTeams?.Contains(t.Id) != true)
            .Where(t => _data.Cities[t.BaseCityId].Region == circuit.Region && Formula.Breach(CarOf(t.Id), circuit, year) is null)
            .ToList();
        if (_entries.Contains(ev))
            teams.Insert(0, Team);
        return teams.SelectMany(t =>
            {
                var team = t with { Car = CarOf(t.Id) };
                return RosterOf(t, year).Select(id => new RaceEntry(_data.Drivers[id], team));
            })
            .Concat(PrivateersFor(ev, circuit))
            .ToList();
    }

    /// <summary>
    /// New year, new cars: each rival that brings out a model this year races it from now on,
    /// in place of the car it developed last year. Called before anything happens in a new year.
    /// </summary>
    private void CatchUpYear(int year)
    {
        while (_carsYear < year)
        {
            _carsYear++;
            var day = new DateOnly(_carsYear, 1, 1);
            if (_carsYear is Formula.FirstWeightYear or Formula.FirstLitreYear)
                _news.Add(new NewsItem(day, $"The {Formula.Name(_carsYear)} comes in for European races: {Formula.Rule(_carsYear)}."));
            foreach (var team in RivalsIn(_carsYear))
            {
                if (team.FirstYear == _carsYear)
                    _news.Add(new NewsItem(day, $"{team.Name} enter Grand Prix racing with the {team.CarFor(_carsYear).Name}."));
                else if (team.CarYearFor(_carsYear) == _carsYear)
                {
                    _rivalCars.Remove(team.Id);
                    _news.Add(new NewsItem(day, $"{team.Name} bring out the {team.CarFor(_carsYear).Name}."));
                }
            }
            if (NextModel is { } model)
                _news.Add(new NewsItem(day, $"The drawing office could build the {model.Car.Name} ({model.Car.Horsepower} hp, {model.Car.WeightKg} kg)."));
        }
    }

    /// <summary>
    /// Moves the calendar on to a day: pays a week's costs for every full week that passes, and
    /// runs the races the player is not in. It stops short of an entered race that is not run yet.
    /// </summary>
    public void AdvanceTo(DateOnly date)
    {
        if (date < Today)
            throw new ArgumentOutOfRangeException(nameof(date), "Time only moves forward.");
        if (NextRace is { } next && next.Date < date)
            throw new InvalidOperationException($"Run {next.Name} first.");

        foreach (var ev in _data.Seasons.Values.SelectMany(s => s.Events)
                     .Where(e => e.Date >= Today && e.Date < date && !HasRun(e))
                     .OrderBy(e => e.Date))
        {
            PayWagesUntil(ev.Date);
            CatchUpYear(ev.Date.Year);
            var race = NewRace(ev);
            race.RunToEnd();
            Record(race.Result(), entered: false, earned: 0);
        }
        PayWagesUntil(date);
        CatchUpYear(date.Year);
        Today = date;
    }

    /// <summary>Takes the team to its next race: travel is paid and the race is ready to start.</summary>
    public LiveRace PrepareRace()
    {
        var ev = NextRace ?? throw new InvalidOperationException("No race entered.");
        AdvanceTo(ev.Date);
        var travel = TravelTo(ev);
        Pay(MoneyKind.Travel, $"Travel to {_data.Circuits[ev.CircuitId].Name}", -travel.Cost);
        return NewRace(ev);
    }

    /// <summary>Pays start and prize money for a finished race and records it.</summary>
    public RaceRecord FinishRace(LiveRace race)
    {
        var ev = race.Event;
        if (ev.Id != NextRace?.Id)
            throw new InvalidOperationException($"{ev.Name} is not the next race.");
        var result = race.Result();
        var earned = 0;
        var starters = result.Classification.Count(c => c.Entry.Team.Id == Team.Id);
        if (starters > 0)
        {
            earned += Pay(MoneyKind.StartMoney, $"Start money, {ev.Name} ({starters} cars)", ev.StartMoney * starters);
        }
        for (var i = 0; i < result.Classification.Count; i++)
        {
            var car = result.Classification[i];
            var prize = Finance.Prize(ev, i + 1);
            if (car.Entry.Team.Id == Team.Id && car.Finished && prize > 0)
                earned += Pay(MoneyKind.Prize, $"{car.Entry.Driver.Name}, {Place(i + 1)} at {ev.Name}", prize);
        }
        var record = Record(result, entered: true, earned);
        Today = ev.Date.AddDays(1);
        return record;
    }

    /// <summary>Winter break: moves on to the first day of next year.</summary>
    public void StartNextSeason()
    {
        if (!HasNextSeason)
            throw new InvalidOperationException("No data for the next season yet.");
        AdvanceTo(new DateOnly(Today.Year + 1, 1, 1));
        // Drivers whose careers ended last year retire from the team.
        foreach (var id in _roster.Where(id => !Racing(_data.Drivers[id])).ToList())
        {
            if (Drivers.Count <= 1)
                break;
            _roster.Remove(id);
            _news.Add(new NewsItem(Today, $"{_data.Drivers[id].Name} retires from racing."));
        }
    }

    public SeasonSummary Summary(int year)
    {
        var mine = _results.Where(r => r.Event.Date.Year == year && r.Entered).ToList();
        int Best(RaceRecord r) => r.Result.Classification
            .Select((c, i) => (c, i))
            .Where(x => x.c.Entry.Team.Id == Team.Id && x.c.Finished)
            .Select(x => x.i + 1)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        var money = _ledger.Where(t => t.Date.Year == year).ToList();
        var lost = _results.Where(r => r.Event.Date.Year == year)
            .SelectMany(r => r.Result.Classification)
            .Where(c => c.Entry.Team.Id == Team.Id && c.Retirement?.Cause == RetirementCause.FatalCrash)
            .Select(c => c.Entry.Driver.Name)
            .ToList();
        var income = money.Where(t => t.Amount > 0).Sum(t => t.Amount);
        var costs = -money.Where(t => t.Amount < 0).Sum(t => t.Amount);
        var cashAtEnd = Cash - _ledger.Where(t => t.Date.Year > year).Sum(t => t.Amount);
        return new SeasonSummary(year, mine.Count, mine.Count(r => Best(r) == 1), mine.Count(r => Best(r) <= 3),
            cashAtEnd - income + costs, income, costs, cashAtEnd, lost);
    }

    public static string Place(int position) => position switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{position}th",
    };

    private LiveRace NewRace(RaceEvent ev) =>
        new(ev, _data.Circuits[ev.CircuitId], GridFor(ev), RaceSeed(ev),
            Settings, manualTeams: [Team.Id]);

    /// <summary>A seed per race that stays the same between runs (string.GetHashCode does not).</summary>
    private int RaceSeed(RaceEvent ev) => ev.Id.Aggregate(Seed, (hash, ch) => unchecked(hash * 31 + ch));

    private RaceRecord Record(RaceResult result, bool entered, int earned)
    {
        foreach (var car in result.Classification.Where(c => c.Retirement?.Cause == RetirementCause.FatalCrash))
            _deadDrivers.Add(car.Entry.Driver.Id);
        var record = new RaceRecord(result.Event, result, entered, earned);
        _results.Add(record);
        return record;
    }

    private void PayWagesUntil(DateOnly date)
    {
        while (_paidUntil.AddDays(7) <= date)
        {
            _paidUntil = _paidUntil.AddDays(7);
            Pay(MoneyKind.Wages, "Wages and works", -WeeklyCosts, _paidUntil);
            Pay(MoneyKind.Backing, "Works backing", WeeklyBacking, _paidUntil);
            WorkOnProjects(_paidUntil);
            DevelopRivals(_paidUntil);
        }
    }

    private int Pay(MoneyKind kind, string text, int amount, DateOnly? date = null)
    {
        Cash += amount;
        _ledger.Add(new Transaction(date ?? Today, kind, text, amount));
        return amount;
    }

    public Season CurrentSeason => _data.Seasons[Today.Year];

    /// <summary>Travel needed to reach an event from wherever the previous entry leaves the cars.</summary>
    public TravelPlan TravelTo(RaceEvent ev)
    {
        var target = _data.Circuits[ev.CircuitId];
        var previous = _entries.LastOrDefault(e => e.Date < ev.Date);
        return previous is null
            ? Travel.Plan(_data.Cities[Team.BaseCityId], target)
            : Travel.Plan(_data.Circuits[previous.CircuitId], target);
    }

    /// <summary>Earliest day the cars can leave for an event: today, or the day after the previous race.</summary>
    private DateOnly DepartureFor(RaceEvent ev)
    {
        var previous = _entries.LastOrDefault(e => e.Date < ev.Date);
        return previous is null || previous.Date < Today ? Today : previous.Date.AddDays(1);
    }

    public EntryResult Enter(string eventId)
    {
        var ev = CurrentSeason.Events.FirstOrDefault(e => e.Id == eventId);
        if (ev is null)
            return EntryResult.UnknownEvent;
        if (ev.Date < Today)
            return EntryResult.EventPassed;
        if (_entries.Contains(ev))
            return EntryResult.AlreadyEntered;
        if (Drivers.Count == 0)
            return EntryResult.NoDrivers;
        if (Formula.Breach(Car, _data.Circuits[ev.CircuitId], ev.Date.Year) is not null)
            return EntryResult.CarNotEligible;

        var arrival = DepartureFor(ev).AddDays(TravelTo(ev).Days);
        if (arrival > ev.Date)
            return EntryResult.CannotArriveInTime;

        // The next entry must still be reachable from this event's circuit.
        var next = _entries.FirstOrDefault(e => e.Date > ev.Date);
        if (next is not null)
        {
            var onward = Travel.Plan(_data.Circuits[ev.CircuitId], _data.Circuits[next.CircuitId]);
            if (ev.Date.AddDays(1 + onward.Days) > next.Date)
                return EntryResult.CannotArriveInTime;
        }

        _entries.Add(ev);
        return EntryResult.Entered;
    }

    public void AdvanceDays(int days)
    {
        if (days < 0)
            throw new ArgumentOutOfRangeException(nameof(days));
        AdvanceTo(Today.AddDays(days));
    }
}
