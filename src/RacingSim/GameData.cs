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
        IEnumerable<Season> seasons)
    {
        Circuits = circuits.ToDictionary(c => c.Id);
        Cities = cities.ToDictionary(c => c.Id);
        Drivers = drivers.ToDictionary(d => d.Id);
        Teams = teams.ToDictionary(t => t.Id);
        Seasons = seasons.ToDictionary(s => s.Year);
        Validate();
    }

    /// <summary>
    /// Builds the data set from raw JSON text. The caller reads the files, so the same
    /// code works from Godot (res:// paths) and from tests (disk paths).
    /// </summary>
    public static GameData FromJson(string circuits, string cities, string drivers, string teams, IEnumerable<string> seasons) =>
        new(
            Parse<List<Circuit>>(circuits),
            Parse<List<City>>(cities),
            Parse<List<Driver>>(drivers),
            Parse<List<Team>>(teams),
            seasons.Select(Parse<Season>));

    /// <summary>Loads every file from a data directory on disk.</summary>
    public static GameData FromDirectory(string dir) =>
        FromJson(
            File.ReadAllText(Path.Combine(dir, "circuits.json")),
            File.ReadAllText(Path.Combine(dir, "cities.json")),
            File.ReadAllText(Path.Combine(dir, "drivers.json")),
            File.ReadAllText(Path.Combine(dir, "teams.json")),
            Directory.GetFiles(Path.Combine(dir, "seasons"), "*.json").Order().Select(File.ReadAllText));

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
