using RacingSim;

namespace RacingManager;

/// <summary>The game being played, kept across scene changes. Plain C# objects only, no Godot ones.</summary>
public static class Session
{
    public const int StartYear = 1930;

    public static GameData? Data { get; set; }
    public static Career? Career { get; set; }

    /// <summary>The settings a new career starts with, chosen on the team screen.</summary>
    public static RaceSettings Settings { get; set; } = new();
}
