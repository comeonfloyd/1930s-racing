using System;
using System.Linq;
using Godot;

namespace RacingManager;

/// <summary>
/// Development helper: saves screenshots of the game from the command line, so the look can be
/// checked without anyone at the keyboard. Run Godot with user arguments after "--":
///   --shot=/tmp/shots            folder for the pictures
///   --race=1930-monza            go straight to this race (with --team)
///   --team=alfa-rossa
///   --stay=1                     keep running after the last picture
///   --no-textures                draw the race without the painted art (for debugging)
/// It saves calendar.png, then race-start.png and race-zoom.png, then quits.
/// </summary>
public static class DevShot
{
    public static string? Arg(string name) =>
        OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith($"--{name}="))?.Split('=', 2)[1];

    public static bool Active => Arg("shot") is not null;

    /// <summary>Waits for the screen to settle, saves it, then runs the next step.</summary>
    public static async void Capture(Node node, string file, double delay, Action? then = null)
    {
        await node.ToSignal(node.GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
        await node.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var folder = Arg("shot")!;
        DirAccess.MakeDirRecursiveAbsolute(folder);
        // Dispose only the image: the viewport texture is shared and used again.
        using (var image = node.GetViewport().GetTexture().GetImage())
            GD.Print($"DevShot {file}: {image.SavePng($"{folder}/{file}")}");
        var tree = node.GetTree();
        if (then is null && Arg("stay") is not null)
            return;
        if (then is null)
            Shutdown.Quit(tree);
        else
            then();
    }
}
