using System.Text.Json.Serialization;

namespace RacingSim;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Region
{
    Europe,
    Usa,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CircuitType
{
    Street,
    RoadCourse,
    PublicRoads,
    MountainRoads,
    BankedOval,
    BrickOval,
    DirtOval,
    HillClimb,
}

/// <summary>
/// A real venue on the world map. <see cref="StraightShare"/> (0 to 1) is how much of a lap
/// rewards engine power over handling; <see cref="Danger"/> (0 to 1) scales crash risk;
/// <see cref="RainChance"/> (0 to 1) is how often it rains on race day.
/// </summary>
public sealed record Circuit(
    string Id,
    string Name,
    string Country,
    Region Region,
    CircuitType Type,
    double LapKm,
    double Latitude,
    double Longitude,
    int FirstYear,
    int LastYear,
    double StraightShare = 0.5,
    double Danger = 0.3,
    double RainChance = 0.25);

/// <summary>A city a team is based in; also used as a travel start point.</summary>
public sealed record City(string Id, string Name, string Country, Region Region, double Latitude, double Longitude);

/// <summary>
/// One race on a season's calendar. Money is in pounds sterling of the day.
/// <see cref="AbsentTeams"/> are works that did not come (the player's own team still may).
/// </summary>
public sealed record RaceEvent(
    string Id,
    string Name,
    string CircuitId,
    DateOnly Date,
    int Laps,
    int StartMoney,
    int PrizeFund,
    int Prestige,
    IReadOnlyList<string>? AbsentTeams = null,
    int Hours = 0)
{
    /// <summary>
    /// An endurance race such as Le Mans runs for <see cref="Hours"/> instead of a set number of
    /// laps, with two drivers to a car; <see cref="Laps"/> is then only an upper bound.
    /// </summary>
    public bool IsEndurance => Hours > 0;
}

public sealed record Season(int Year, IReadOnlyList<RaceEvent> Events);

/// <summary>
/// One car in the Le Mans field of a year, as it really ran: the entrant (a team, often a
/// sports-car maker that never races in Grands Prix), the car and its two drivers.
/// </summary>
public sealed record LeMansEntry(string TeamId, Car Car, IReadOnlyList<string> DriverIds);

/// <summary>The lemans.json file: the sports-car entrants and each year's field.</summary>
public sealed record LeMansData(IReadOnlyList<Team> Teams, IReadOnlyDictionary<string, IReadOnlyList<LeMansEntry>> Entries);

/// <summary>Driver skills run 1 to 20, like the staff ratings in the design plan.</summary>
public sealed record DriverSkills(int Speed, int Consistency, int MechanicalSympathy, int Wet, int Bravery);

public sealed record Driver(
    string Id,
    string Name,
    string Nationality,
    int BirthYear,
    int FirstYear,
    int LastYear,
    DriverSkills Skills,
    bool SportsCarsOnly = false);

/// <summary>
/// A team's race car. Handling runs 1 to 20; reliability is the chance (0 to 1) of
/// finishing 500 km without a mechanical failure. <see cref="EngineLitres"/> matters for the
/// 3-litre formula from 1938 (0 means unknown and counts as within it). <see cref="Thirst"/>
/// scales fuel use: 1 is a typical car of the day, more for a big or highly supercharged engine.
/// </summary>
public sealed record Car(string Name, int Horsepower, int WeightKg, int Handling, double Reliability, double EngineLitres = 0, double Thirst = 1, double Wear = 1);

/// <summary>
/// A team. <see cref="Name"/> is the in-game brand (altered from the real maker's
/// trademark); names live in data so players can edit them. <see cref="DriverIds"/> and
/// <see cref="Car"/> are the team in 1930 (or its first year); <see cref="Rosters"/> and
/// <see cref="Cars"/>, keyed by year, change them from that year on. A <see cref="Privateer"/>
/// entry is not a works: it stands for the drivers no works has signed, in bought cars.
/// </summary>
public sealed record Team(
    string Id,
    string Name,
    string Nationality,
    string BaseCityId,
    int StartingCash,
    int Prestige,
    bool Playable,
    IReadOnlyList<string> DriverIds,
    Car Car,
    int FirstYear = 1930,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Rosters = null,
    IReadOnlyDictionary<string, Car>? Cars = null,
    bool Privateer = false,
    IReadOnlyDictionary<string, Car>? SportsCars = null,
    string? Colour = null)
{
    /// <summary>The maker's sports car for endurance races in a year, or null when it has none.</summary>
    public Car? SportsCarFor(int year) => Latest(SportsCars, year);

    /// <summary>The works drivers in a year; empty before the team's first year or when it stays away.</summary>
    public IReadOnlyList<string> RosterFor(int year) =>
        year < FirstYear ? [] : Latest(Rosters, year) ?? DriverIds;

    /// <summary>The car the works builds for a year.</summary>
    public Car CarFor(int year) => Latest(Cars, year) ?? Car;

    /// <summary>The year the car for this year came out (so a new model shows as a change).</summary>
    public int CarYearFor(int year) =>
        Cars?.Keys.Select(int.Parse).Where(y => y <= year).DefaultIfEmpty(FirstYear).Max() ?? FirstYear;

    private static T? Latest<T>(IReadOnlyDictionary<string, T>? byYear, int year) where T : class =>
        byYear?.Select(kv => (Year: int.Parse(kv.Key), kv.Value))
            .Where(x => x.Year <= year)
            .OrderBy(x => x.Year)
            .Select(x => x.Value)
            .LastOrDefault();
}
