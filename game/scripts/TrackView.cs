using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>Draws the circuit and the cars on it, fitted into <see cref="Area"/>.</summary>
public partial class TrackView : Node2D
{
    private static readonly Color Grass = new(0.36f, 0.47f, 0.27f);
    private static readonly Color Verge = new(0.72f, 0.66f, 0.52f);
    private static readonly Color Tarmac = new(0.24f, 0.24f, 0.25f);
    private static readonly Color Tree = new(0.2f, 0.32f, 0.18f);
    private static readonly Color Stand = new(0.55f, 0.36f, 0.24f);
    private static readonly Color Water = new(0.27f, 0.43f, 0.59f);
    private static readonly Color DisusedTrack = new(0.47f, 0.47f, 0.43f);

    private TrackShape? _track;
    private IReadOnlyList<LiveCar> _cars = Array.Empty<LiveCar>();
    private Vector2[] _trees = Array.Empty<Vector2>();

    public Rect2 Area { get; set; }

    public void SetTrack(TrackShape track, int seed)
    {
        _track = track;
        // Trees scattered away from the road; seeded so the scenery stays put.
        var rng = new Random(seed);
        _trees = Enumerable.Range(0, 400)
            .Select(_ => new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()))
            .Where(p => track.Points.Min(q => q.DistanceTo(p)) > 0.05f)
            .Where(p => !track.Water.Any(w => w.HasPoint(p)))
            .Take(160)
            .ToArray();
        QueueRedraw();
    }

    public void SetCars(IReadOnlyList<LiveCar> cars)
    {
        _cars = cars;
        QueueRedraw();
    }

    public static Color TeamColor(string teamId) => teamId switch
    {
        "alfa-rossa" => new Color(0.78f, 0.1f, 0.1f),
        "bugetti" => new Color(0.16f, 0.32f, 0.72f),
        "masarelli" => new Color(0.95f, 0.55f, 0.1f),
        "mercator-benz" => new Color(0.9f, 0.9f, 0.9f),
        _ => new Color(0.6f, 0.6f, 0.6f),
    };

    /// <summary>Track drawings are 1.4 times wider than tall; fit that box, centred, into the area.</summary>
    private const float Aspect = 1.4f;

    private Rect2 Box
    {
        get
        {
            var width = Mathf.Min(Area.Size.X, Area.Size.Y * Aspect);
            var size = new Vector2(width, width / Aspect);
            return new Rect2(Area.Position + (Area.Size - size) / 2, size);
        }
    }

    private Vector2 Map(Vector2 p) => Box.Position + p * Box.Size;

    public override void _Draw()
    {
        if (_track is null)
            return;

        DrawRect(Area, Grass);
        var scale = Box.Size.Y;

        foreach (var w in _track.Water)
            DrawRect(new Rect2(Map(w.Position), w.Size * Box.Size), Water);
        foreach (var t in _trees)
            DrawCircle(Map(t), scale * 0.012f, Tree);
        foreach (var s in _track.Grandstands)
            DrawRect(new Rect2(Map(s.Position), s.Size * Box.Size), Stand);

        foreach (var u in _track.Unused)
            DrawPolyline(u.Select(Map).ToArray(), DisusedTrack, scale * 0.025f, true);

        var road = _track.Points.Select(Map).Append(Map(_track.Points[0])).ToArray();
        DrawPolyline(road, Verge, scale * 0.045f, true);
        DrawPolyline(road, Tarmac, scale * 0.032f, true);

        // Start and finish line across the road.
        var start = Map(_track.At(0));
        var ahead = (Map(_track.At(0.002)) - start).Normalized();
        var across = new Vector2(-ahead.Y, ahead.X) * scale * 0.02f;
        DrawLine(start - across, start + across, Colors.White, 3);

        var font = ThemeDB.FallbackFont;
        foreach (var (name, at) in _track.Corners)
        {
            const int size = 13;
            var width = font.GetStringSize(name, HorizontalAlignment.Left, -1, size).X;
            DrawString(font, Map(at) + new Vector2(-width / 2, size / 3f), name, HorizontalAlignment.Left, -1, size, new Color(1, 1, 1, 0.85f));
        }

        if (_track.Credit.Length > 0)
        {
            const int size = 11;
            var width = font.GetStringSize(_track.Credit, HorizontalAlignment.Left, -1, size).X;
            DrawString(font, Area.End - new Vector2(width + 8, 8), _track.Credit, HorizontalAlignment.Left, -1, size, new Color(1, 1, 1, 0.6f));
        }

        // Leader drawn last, on top.
        foreach (var (car, index) in _cars.Select((c, i) => (c, i)).Reverse())
        {
            var p = Map(_track.At(car.Distance));
            if (car.InPits)
            {
                // Standing in the pits beside the start line, on the grandstand side.
                var forward = (Map(_track.At(0.002)) - Map(_track.At(0))).Normalized();
                p += new Vector2(forward.Y, -forward.X) * scale * 0.04f;
            }
            var color = TeamColor(car.Entry.Team.Id);
            if (car.Retirement is not null)
                color = color.Darkened(0.6f);
            DrawCircle(p, scale * 0.016f, Colors.Black);
            DrawCircle(p, scale * 0.013f, color);
            var label = (index + 1).ToString();
            DrawString(font, p + new Vector2(-scale * 0.03f, -scale * 0.022f), label, HorizontalAlignment.Center, scale * 0.06f, 14, Colors.White);
        }
    }
}
