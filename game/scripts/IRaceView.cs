using System.Collections.Generic;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>What the race screen asks of a view of the circuit: the flat map or the 3D view.</summary>
public interface IRaceView
{
    /// <summary>The part of the window the whole circuit should fit in at zoom 1.</summary>
    Rect2 Area { get; set; }
    float Zoom { get; set; }
    float Rain { get; set; }
    float Night { get; set; }
    string? FollowDriverId { get; set; }
    bool Visible { get; set; }

    void SetTrack(TrackShape track, string circuitId, int seed);
    void SetCars(IReadOnlyList<LiveCar> cars);
    void ZoomAt(Vector2 screenPoint, float factor);
    void Pan(Vector2 pixels);

    /// <summary>
    /// Where the race is heard from: a point on the drawing (0 to 1 each way), and how high
    /// above it, in drawing heights.
    /// </summary>
    (Vector2 At, float Height) Ear { get; }

    /// <summary>How far left (-1) or right (1) of the middle of the picture a point on the drawing is.</summary>
    float PanOf(Vector2 point);

    /// <summary>Turns the view round (the 3D view only): a drag of this many pixels.</summary>
    void Turn(Vector2 pixels) { }
}
