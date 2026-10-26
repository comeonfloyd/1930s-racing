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

/// <summary>A real venue on the world map.</summary>
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
    int LastYear);

/// <summary>A city a team is based in; also used as a travel start point.</summary>
public sealed record City(string Id, string Name, string Country, Region Region, double Latitude, double Longitude);

/// <summary>One race on a season's calendar. Money is in pounds sterling of the day.</summary>
public sealed record RaceEvent(
    string Id,
    string Name,
    string CircuitId,
    DateOnly Date,
    int Laps,
    int StartMoney,
    int PrizeFund,
    int Prestige);

public sealed record Season(int Year, IReadOnlyList<RaceEvent> Events);

/// <summary>Driver skills run 1 to 20, like the staff ratings in the design plan.</summary>
public sealed record DriverSkills(int Speed, int Consistency, int MechanicalSympathy, int Wet, int Bravery);

public sealed record Driver(
    string Id,
    string Name,
    string Nationality,
    int BirthYear,
    int FirstYear,
    int LastYear,
    DriverSkills Skills);

/// <summary>
/// A team. <see cref="Name"/> is the in-game brand (altered from the real maker's
/// trademark); names live in data so players can edit them.
/// </summary>
public sealed record Team(
    string Id,
    string Name,
    string Nationality,
    string BaseCityId,
    int StartingCash,
    int Prestige,
    bool Playable,
    IReadOnlyList<string> DriverIds);
