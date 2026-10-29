using System.Text.Json;
using System.Text.Json.Serialization;

namespace RacingSim;

/// <summary>
/// A career as plain data for the save file. World data (drivers, teams, circuits, calendars)
/// is not saved, only ids, so a save stays small and still loads after the data files are edited.
/// Lap-by-lap times are not kept for past races: only laps completed and total time.
/// </summary>
public sealed record CareerSave(
    int Version,
    string TeamId,
    int Seed,
    bool FatalCrashes,
    DateOnly Today,
    DateOnly PaidUntil,
    int Cash,
    Car Car,
    IReadOnlyList<string> Entries,
    IReadOnlyList<Transaction> Ledger,
    IReadOnlyList<SavedRace> Results,
    IReadOnlyList<string> DeadDrivers,
    IReadOnlyList<Project> Projects,
    IReadOnlyDictionary<Upgrade, int> UpgradesDone,
    IReadOnlyList<NewsItem> News,
    IReadOnlyDictionary<string, Car>? RivalCars = null,
    IReadOnlyList<string>? Roster = null,
    int? ModelYear = null,
    int? CarsYear = null,
    int? WaitingModelYear = null,
    IReadOnlyDictionary<string, DateOnly>? InjuredUntil = null,
    IReadOnlyDictionary<Facility, int>? Facilities = null,
    IReadOnlyDictionary<StaffRole, string>? Staff = null,
    FacilityJob? Building = null,
    DateOnly? LastTest = null,
    Car? DesignBase = null,
    Car? WaitingCar = null,
    Car? IndyCar = null,
    CarDesign? IndyDesign = null,
    IndyOrder? IndyOrder = null,
    int TeethingFaults = 0,
    int WaitingFaults = 0,
    IReadOnlyList<Part>? Researched = null,
    RaceSettings? Settings = null,
    IReadOnlyList<SponsorDeal>? Deals = null,
    IReadOnlyDictionary<string, double>? StartDeals = null,
    string? ScenarioId = null,
    Team? OwnTeam = null,
    IReadOnlyList<Car>? SpareCars = null,
    IReadOnlyDictionary<string, string>? CarOfDriver = null);

public sealed record SavedRace(string EventId, bool Entered, int Earned, IReadOnlyList<SavedCar> Classification);

public sealed record SavedCar(string DriverId, string TeamId, int Laps, double TotalTime, Retirement? Retirement, string? CoDriverId = null);

public sealed partial class Career
{
    public const int SaveVersion = 1;

    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public CareerSave ToSave() => new(
        SaveVersion, Team.Id, Seed, Settings.FatalCrashes, Today, _paidUntil, Cash, Car,
        _entries.Select(e => e.Id).ToList(),
        _ledger.ToList(),
        _results.Select(r => new SavedRace(r.Event.Id, r.Entered, r.Earned,
            r.Result.Classification.Select(c => new SavedCar(c.Entry.Driver.Id, c.Entry.Team.Id, c.LapsCompleted, c.TotalTime, c.Retirement, c.Entry.CoDriver?.Id)).ToList())).ToList(),
        _deadDrivers.ToList(),
        _projects.ToList(),
        new Dictionary<Upgrade, int>(_upgradesDone),
        _news.ToList(),
        new Dictionary<string, Car>(_rivalCars),
        _roster.ToList(),
        ModelYear,
        _carsYear,
        WaitingModelYear,
        new Dictionary<string, DateOnly>(_injuredUntil),
        new Dictionary<Facility, int>(_facilities),
        new Dictionary<StaffRole, string>(_staff),
        Building,
        _lastTest,
        DesignBase,
        WaitingCar,
        IndyCar,
        IndyDesign,
        IndyOrder,
        TeethingFaults,
        _waitingFaults,
        _researched.ToList(),
        Settings,
        _deals.Values.ToList(),
        new Dictionary<string, double>(_startDeals),
        ScenarioId,
        IsOwnTeam ? Team : null,
        _spares.ToList(),
        new Dictionary<string, string>(_carOf));

    public string ToJson() => JsonSerializer.Serialize(ToSave(), SaveOptions);

    public static Career FromJson(GameData data, string json) =>
        FromSave(data, JsonSerializer.Deserialize<CareerSave>(json, SaveOptions) ?? throw new InvalidDataException("Empty save"));

    public static Career FromSave(GameData data, CareerSave save)
    {
        if (save.Version > SaveVersion)
            throw new InvalidDataException($"The save is from a newer version of the game ({save.Version}).");
        if (save.OwnTeam is { } own)
            data = data.WithTeam(own);
        var events = data.Seasons.Values.SelectMany(s => s.Events).ToDictionary(e => e.Id);
        var career = new Career(data, save.TeamId, save.Today.Year, save.Seed, save.Settings ?? new RaceSettings(save.FatalCrashes), save.ScenarioId)
        {
            Today = save.Today,
            _paidUntil = save.PaidUntil,
            Cash = save.Cash,
            Car = save.Car,
        };
        foreach (var id in save.Entries)
            career._entries.Add(events[id]);
        career._ledger.AddRange(save.Ledger);
        foreach (var race in save.Results)
        {
            var ev = events[race.EventId];
            var classification = race.Classification.Select(c => new CarResult(
                new RaceEntry(data.Drivers[c.DriverId], data.Teams[c.TeamId], c.CoDriverId is null ? null : data.Drivers[c.CoDriverId]),
                Enumerable.Repeat(c.Laps == 0 ? 0 : c.TotalTime / c.Laps, c.Laps).ToList(),
                c.Retirement)).ToList();
            career._results.Add(new RaceRecord(ev, new RaceResult(ev, data.Circuits[ev.CircuitId], classification), race.Entered, race.Earned));
        }
        career._deadDrivers.UnionWith(save.DeadDrivers);
        career._projects.AddRange(save.Projects);
        foreach (var (upgrade, count) in save.UpgradesDone)
            career._upgradesDone[upgrade] = count;
        career._news.AddRange(save.News);
        foreach (var (teamId, car) in save.RivalCars ?? new Dictionary<string, Car>())
            career._rivalCars[teamId] = car;
        if (save.Roster is { } roster)
        {
            career._roster.Clear();
            career._roster.AddRange(roster);
        }
        career.ModelYear = save.ModelYear ?? career.Team.CarYearFor(save.Today.Year);
        career._carsYear = save.CarsYear ?? save.Today.Year;
        career.WaitingModelYear = save.WaitingModelYear;
        foreach (var (id, until) in save.InjuredUntil ?? new Dictionary<string, DateOnly>())
            career._injuredUntil[id] = until;
        // Saves from before the works was in the game keep the level 1 works and the real staff.
        foreach (var (facility, level) in save.Facilities ?? new Dictionary<Facility, int>())
            career._facilities[facility] = level;
        if (save.Staff is { } staff)
        {
            career._staff.Clear();
            foreach (var (role, id) in staff.Where(s => data.Staff.ContainsKey(s.Value)))
                career._staff[role] = id;
        }
        career.Building = save.Building;
        career._lastTest = save.LastTest;
        career.DesignBase = save.DesignBase;
        career.WaitingCar = save.WaitingCar;
        career.IndyCar = save.IndyCar;
        career.IndyDesign = save.IndyDesign;
        career.IndyOrder = save.IndyOrder;
        career.TeethingFaults = save.TeethingFaults;
        career._waitingFaults = save.WaitingFaults;
        // Saves from before research keep the parts known in the year they were made.
        if (save.Researched is { } researched)
        {
            career._researched.Clear();
            career._researched.UnionWith(researched);
        }
        foreach (var deal in (save.Deals ?? []).Where(d => data.Sponsors.ContainsKey(d.SponsorId)))
            career._deals[data.Sponsors[deal.SponsorId].Kind] = deal;
        career._spares.AddRange(save.SpareCars ?? []);
        foreach (var (driver, car) in save.CarOfDriver ?? new Dictionary<string, string>())
            career._carOf[driver] = car;
        foreach (var (id, factor) in save.StartDeals ?? new Dictionary<string, double>())
            career._startDeals[id] = factor;
        return career;
    }
}
