using System.Text.Json;

namespace RacingSim;

/// <summary>All static world data, loaded from the JSON files in game/data.</summary>
public sealed class GameData
{
    public IReadOnlyDictionary<string, Circuit> Circuits { get; }
    public IReadOnlyDictionary<string, City> Cities { get; }
    public IReadOnlyDictionary<string, Driver> Drivers { get; }
    public IReadOnlyDictionary<string, Team> Teams { get; }
    public IReadOnlyDictionary<int, Season> Seasons { get; }

    /// <summary>The Le Mans field of each year the race was held.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<LeMansEntry>> LeMans { get; }

    /// <summary>Engineers, mechanics and team managers.</summary>
    public IReadOnlyDictionary<string, StaffMember> Staff { get; }

    /// <summary>World news: historical events and the random pool.</summary>
    public IReadOnlyList<WorldEvent> World { get; }

    /// <summary>Fuel, oil, tyre and plug makers who sponsor teams.</summary>
    public IReadOnlyDictionary<string, Sponsor> Sponsors { get; }

    /// <summary>Where a new career can begin, in order; never empty.</summary>
    public IReadOnlyList<Scenario> Scenarios { get; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public GameData(
        IEnumerable<Circuit> circuits,
        IEnumerable<City> cities,
        IEnumerable<Driver> drivers,
        IEnumerable<Team> teams,
        IEnumerable<Season> seasons,
        LeMansData? leMans = null,
        IEnumerable<StaffMember>? staff = null,
        WorldData? world = null,
        IEnumerable<Sponsor>? sponsors = null,
        IEnumerable<Scenario>? scenarios = null)
    {
        Circuits = circuits.ToDictionary(c => c.Id);
        Cities = cities.ToDictionary(c => c.Id);
        Drivers = drivers.ToDictionary(d => d.Id);
        Teams = teams.Concat(leMans?.Teams ?? []).ToDictionary(t => t.Id);
        Seasons = seasons.ToDictionary(s => s.Year);
        LeMans = (leMans?.Entries ?? new Dictionary<string, IReadOnlyList<LeMansEntry>>())
            .ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
        Staff = (staff ?? []).ToDictionary(s => s.Id);
        World = world?.Events ?? [];
        Sponsors = (sponsors ?? []).ToDictionary(s => s.Id);
        Scenarios = scenarios?.ToList() is { Count: > 0 } list ? list : [Scenario.Default(this)];
        Validate();
    }

    private GameData(GameData data, Team extra)
    {
        Circuits = data.Circuits;
        Cities = data.Cities;
        Drivers = data.Drivers;
        Teams = data.Teams.Values.Where(t => t.Id != extra.Id).Append(extra).ToDictionary(t => t.Id);
        Seasons = data.Seasons;
        LeMans = data.LeMans;
        Staff = data.Staff;
        World = data.World;
        Sponsors = data.Sponsors;
        Scenarios = data.Scenarios;
    }

    /// <summary>The same data with one more team: the player's own.</summary>
    public GameData WithTeam(Team team) => new(this, team);

    /// <summary>
    /// Builds the data set from raw JSON text. The caller reads the files, so the same
    /// code works from Godot (res:// paths) and from tests (disk paths).
    /// </summary>
    public static GameData FromJson(string circuits, string cities, string drivers, string teams, IEnumerable<string> seasons,
        string? leMans = null, string? staff = null, string? world = null, string? sponsors = null, string? scenarios = null) =>
        new(
            Parse<List<Circuit>>(circuits),
            Parse<List<City>>(cities),
            Parse<List<Driver>>(drivers),
            Parse<List<Team>>(teams),
            seasons.Select(Parse<Season>),
            leMans is null ? null : Parse<LeMansData>(leMans),
            staff is null ? null : Parse<List<StaffMember>>(staff),
            world is null ? null : Parse<WorldData>(world),
            sponsors is null ? null : Parse<List<Sponsor>>(sponsors),
            scenarios is null ? null : Parse<List<Scenario>>(scenarios));

    /// <summary>Loads every file from a data directory on disk.</summary>
    public static GameData FromDirectory(string dir) =>
        FromJson(
            File.ReadAllText(Path.Combine(dir, "circuits.json")),
            File.ReadAllText(Path.Combine(dir, "cities.json")),
            File.ReadAllText(Path.Combine(dir, "drivers.json")),
            File.ReadAllText(Path.Combine(dir, "teams.json")),
            Directory.GetFiles(Path.Combine(dir, "seasons"), "*.json").Order().Select(File.ReadAllText),
            File.Exists(Path.Combine(dir, "lemans.json")) ? File.ReadAllText(Path.Combine(dir, "lemans.json")) : null,
            File.Exists(Path.Combine(dir, "staff.json")) ? File.ReadAllText(Path.Combine(dir, "staff.json")) : null,
            File.Exists(Path.Combine(dir, "world.json")) ? File.ReadAllText(Path.Combine(dir, "world.json")) : null,
            File.Exists(Path.Combine(dir, "sponsors.json")) ? File.ReadAllText(Path.Combine(dir, "sponsors.json")) : null,
            File.Exists(Path.Combine(dir, "scenarios.json")) ? File.ReadAllText(Path.Combine(dir, "scenarios.json")) : null);

    private static T Parse<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException($"Empty {typeof(T).Name} data");

    private void Validate()
    {
        foreach (var team in Teams.Values)
        {
            if (!Cities.ContainsKey(team.BaseCityId))
                throw new InvalidDataException($"Team {team.Id} has unknown base city {team.BaseCityId}");
            foreach (var driverId in team.DriverIds.Concat(team.Rosters?.Values.SelectMany(r => r) ?? []))
                if (!Drivers.ContainsKey(driverId))
                    throw new InvalidDataException($"Team {team.Id} has unknown driver {driverId}");
            foreach (var year in (team.Rosters?.Keys ?? []).Concat(team.Cars?.Keys ?? []))
                if (!int.TryParse(year, out _))
                    throw new InvalidDataException($"Team {team.Id} has a roster or car for \"{year}\", which is not a year");
        }

        // A driver drives for one works at a time.
        foreach (var year in Seasons.Keys)
        {
            var taken = new Dictionary<string, string>();
            foreach (var team in Teams.Values)
            foreach (var driverId in team.RosterFor(year))
                if (!taken.TryAdd(driverId, team.Id))
                    throw new InvalidDataException($"{driverId} drives for both {taken[driverId]} and {team.Id} in {year}");
        }

        foreach (var (year, field) in LeMans)
        foreach (var entry in field)
        {
            if (!Teams.ContainsKey(entry.TeamId))
                throw new InvalidDataException($"Le Mans {year} lists unknown team {entry.TeamId}");
            foreach (var driverId in entry.DriverIds)
                if (!Drivers.ContainsKey(driverId))
                    throw new InvalidDataException($"Le Mans {year} lists unknown driver {driverId}");
        }

        foreach (var member in Staff.Values)
            if (member.TeamId is { } teamId && !Teams.ContainsKey(teamId))
                throw new InvalidDataException($"Staff member {member.Id} works for unknown team {teamId}");

        foreach (var ev in World)
            if (ev.Factor <= 0 || ev.Weeks <= 0 || ev.Chance is < 0 or > 1)
                throw new InvalidDataException($"World event {ev.Id} needs a positive factor and weeks, and a chance from 0 to 1");

        foreach (var sponsor in Sponsors.Values)
            if (sponsor.Fee <= 0 || sponsor.Power <= 0 || sponsor.Thirst <= 0 || sponsor.Wear <= 0)
                throw new InvalidDataException($"Sponsor {sponsor.Id} needs a positive fee, power, thirst and wear");

        foreach (var scenario in Scenarios)
        {
            if (!Seasons.ContainsKey(scenario.Year))
                throw new InvalidDataException($"Scenario {scenario.Id} starts in {scenario.Year}, which has no season");
            foreach (var entry in scenario.Teams)
                if (!Teams.TryGetValue(entry.TeamId, out var team) || team.Privateer || team.FirstYear > scenario.Year)
                    throw new InvalidDataException($"Scenario {scenario.Id} offers {entry.TeamId}, which is not a works racing in {scenario.Year}");
        }

        foreach (var season in Seasons.Values)
        foreach (var ev in season.Events)
        {
            if (!Circuits.TryGetValue(ev.CircuitId, out var circuit))
                throw new InvalidDataException($"Event {ev.Id} has unknown circuit {ev.CircuitId}");
            if (ev.Date.Year != season.Year)
                throw new InvalidDataException($"Event {ev.Id} is dated outside season {season.Year}");
            if (season.Year < circuit.FirstYear || season.Year > circuit.LastYear)
                throw new InvalidDataException($"Event {ev.Id} uses {circuit.Id} outside its active years");
            foreach (var teamId in ev.AbsentTeams ?? [])
                if (!Teams.ContainsKey(teamId))
                    throw new InvalidDataException($"Event {ev.Id} lists unknown team {teamId} as absent");
        }
    }
}
