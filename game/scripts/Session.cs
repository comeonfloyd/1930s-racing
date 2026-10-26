using RacingSim;

namespace RacingManager;

/// <summary>The game being played, kept across scene changes. Plain C# objects only, no Godot ones.</summary>
public static class Session
{
    public const int StartYear = 1930;

    public static GameData? Data { get; set; }
    public static Career? Career { get; set; }
}
