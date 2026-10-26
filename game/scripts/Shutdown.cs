using System;
using Godot;

namespace RacingManager;

/// <summary>
/// Autoload that quits cleanly. C# wrappers of Godot objects left for the garbage collector
/// are freed only when their finalizers run; if that happens while the engine is shutting down
/// it fails with "!rc_owner" and hangs. So every quit, from the window or from code, first
/// collects and finalizes, then quits.
/// </summary>
public partial class Shutdown : Node
{
    public override void _Ready() => GetTree().AutoAcceptQuit = false;

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            Quit(GetTree());
    }

    public static void Quit(SceneTree tree)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        tree.Quit();
    }
}
