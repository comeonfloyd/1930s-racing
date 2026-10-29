using System;
using Godot;

namespace RacingManager;

/// <summary>
/// Autoload that quits cleanly. Godot objects that C# still refers to must be released while
/// the engine is fully alive; when that happens during engine shutdown Godot fails with
/// "!rc_owner" and hangs. So every quit, from the window or from code, first frees the screen
/// and runs the garbage collector, then quits on the next frame.
/// </summary>
public partial class Shutdown : Node
{
    public override void _Ready() => GetTree().AutoAcceptQuit = false;

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            Quit(GetTree());
    }

    public static void Quit(SceneTree tree) => Callable.From(() => FreeAndQuit(tree)).CallDeferred();

    private static void FreeAndQuit(SceneTree tree)
    {
        tree.CurrentScene?.Free();
        Sounds.Release();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        tree.CreateTimer(0.1).Timeout += () => tree.Quit();
    }
}
