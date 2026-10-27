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
    IReadOnlyDictionary<string, DateOnly>? InjuredUntil = null);

public sealed record SavedRace(string EventId, bool Entered, int Earned, IReadOnlyList<SavedCar> Classification);

public sealed record SavedCar(string DriverId, string TeamId, int Laps, double TotalTime, Retirement? Retirement);

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
            r.Result.Classification.Select(c => new SavedCar(c.Entry.Driver.Id, c.Entry.Team.Id, c.LapsCompleted, c.TotalTime, c.Retirement)).ToList())).ToList(),
        _deadDrivers.ToList(),
        _projects.ToList(),
        new Dictionary<Upgrade, int>(_upgradesDone),
        _news.ToList(),
        new Dictionary<string, Car>(_rivalCars),
        _roster.ToList(),
        ModelYear,
        _carsYear,
        WaitingModelYear,
        new Dictionary<string, DateOnly>(_injuredUntil));

    public string ToJson() => JsonSerializer.Serialize(ToSave(), SaveOptions);

    public static Career FromJson(GameData data, string json) =>
        FromSave(data, JsonSerializer.Deserialize<CareerSave>(json, SaveOptions) ?? throw new InvalidDataException("Empty save"));

    public static Career FromSave(GameData data, CareerSave save)
    {
        if (save.Version > SaveVersion)
            throw new InvalidDataException($"The save is from a newer version of the game ({save.Version}).");
        var events = data.Seasons.Values.SelectMany(s => s.Events).ToDictionary(e => e.Id);
        var career = new Career(data, save.TeamId, save.Today.Year, save.Seed, new RaceSettings(save.FatalCrashes))
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
                new RaceEntry(data.Drivers[c.DriverId], data.Teams[c.TeamId]),
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
        return career;
    }
}
