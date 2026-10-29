using RacingSim;

namespace RacingManager;

/// <summary>The game being played, kept across scene changes. Plain C# objects only, no Godot ones.</summary>
public static class Session
{
    /// <summary>Where a new career begins, chosen on the team screen; null for the first scenario.</summary>
    public static Scenario? Scenario { get; set; }

    public static int StartYear => Scenario?.Year ?? Data?.Scenarios[0].Year ?? 1930;

    public static GameData? Data { get; set; }
    public static Career? Career { get; set; }

    /// <summary>The settings a new career starts with, chosen on the team screen.</summary>
    public static RaceSettings Settings { get; set; } = new();
}
