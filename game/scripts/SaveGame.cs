using System;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// The autosave in the player's user folder (user://career.json, which Godot puts under the
/// OS's app data folder). The calendar saves every time it changes, so a save is always from
/// the calendar, never from the middle of a race.
/// </summary>
public static class SaveGame
{
    private const string Path = "user://career.json";

    public static bool Exists => FileAccess.FileExists(Path);

    public static void Save(Career career)
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PushError($"Could not save the game: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(career.ToJson());
    }

    /// <summary>The saved career, or null when there is none or it cannot be read.</summary>
    public static Career? Load(GameData data)
    {
        if (!Exists)
            return null;
        try
        {
            return Career.FromJson(data, FileAccess.GetFileAsString(Path));
        }
        catch (Exception e)
        {
            GD.PushError($"Could not load the saved game: {e.Message}");
            return null;
        }
    }
}
