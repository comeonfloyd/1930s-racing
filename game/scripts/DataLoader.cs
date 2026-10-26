using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>Reads the JSON files under res://data and hands them to the sim core.</summary>
public static class DataLoader
{
    private const string Root = "res://data";

    public static GameData Load()
    {
        var seasons = DirAccess.GetFilesAt($"{Root}/seasons")
            .Where(f => f.EndsWith(".json"))
            .Order()
            .Select(f => Read($"seasons/{f}"));

        return GameData.FromJson(
            Read("circuits.json"),
            Read("cities.json"),
            Read("drivers.json"),
            Read("teams.json"),
            seasons);
    }

    private static string Read(string file) => FileAccess.GetFileAsString($"{Root}/{file}");
}
