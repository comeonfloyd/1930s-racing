using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// Draws the circuit and the cars on it inside <see cref="Area"/>, with zoom and a camera that
/// can follow one car. The ground is the painted picture in res://art/tracks/{circuit}.jpg
/// (tools/track-art/paint.py); a circuit without one is drawn from plain shapes.
/// </summary>
public partial class TrackView : Node2D
{
    private static readonly Color Grass = new(0.36f, 0.47f, 0.27f);
    private static readonly Color Verge = new(0.72f, 0.66f, 0.52f);
    private static readonly Color Tarmac = new(0.24f, 0.24f, 0.25f);
    private static readonly Color Tree = new(0.2f, 0.32f, 0.18f);
    private static readonly Color Stand = new(0.55f, 0.36f, 0.24f);
    private static readonly Color Water = new(0.27f, 0.43f, 0.59f);
    private static readonly Color DisusedTrack = new(0.47f, 0.47f, 0.43f);

    /// <summary>Road width in the painted pictures, as a share of the picture height (paint.py ROAD).</summary>
    private const float PaintedRoad = 0.016f;

    public const float MaxZoom = 12;

    private TrackShape? _track;
    private Texture2D? _ground;
    // Per view, not static: Godot objects held in static C# fields crash the engine on quit.
    private Texture2D? _carBody;
    private Texture2D? _carDetail;
    private IReadOnlyList<LiveCar> _cars = Array.Empty<LiveCar>();
    private Vector2[] _trees = Array.Empty<Vector2>();
    private float _zoom = 1;
    private Vector2 _focus = new(0.5f, 0.5f);

    public Rect2 Area { get; set; }

    /// <summary>The car the camera follows, or null for a free camera.</summary>
    public string? FollowDriverId { get; set; }

    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Mathf.Clamp(value, 1, MaxZoom);
            Focus = _focus;
        }
    }

    /// <summary>The point of the drawing (0..1 each way) at the centre of the view.</summary>
    public Vector2 Focus
    {
        get => _focus;
        set
        {
            // Keep the drawing filling the view: no empty space past its edges.
            var half = Area.Size / (Box.Size * _zoom) / 2;
            _focus = new Vector2(
                half.X >= 0.5f ? 0.5f : Mathf.Clamp(value.X, half.X, 1 - half.X),
                half.Y >= 0.5f ? 0.5f : Mathf.Clamp(value.Y, half.Y, 1 - half.Y));
            QueueRedraw();
        }
    }

    public void SetTrack(TrackShape track, string circuitId, int seed)
    {
        _track = track;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        _ground = LoadTexture($"res://art/tracks/{circuitId}.jpg");
        _carBody ??= LoadTexture("res://art/car-body.png");
        _carDetail ??= LoadTexture("res://art/car-detail.png");
        // Trees scattered away from the road for circuits without a painting; seeded so the scenery stays put.
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
        var followed = cars.FirstOrDefault(c => c.Entry.Driver.Id == FollowDriverId);
        if (followed is not null && _track is not null)
            Focus = _focus.Lerp(_track.At(followed.Distance), 0.25f);
        QueueRedraw();
    }

    /// <summary>Zooms by a factor, keeping the drawing point under the screen point where it is.</summary>
    public void ZoomAt(Vector2 screenPoint, float factor)
    {
        var before = Unmap(screenPoint);
        Zoom = _zoom * factor;
        var after = Unmap(screenPoint);
        Focus = _focus + before - after;
    }

    /// <summary>Moves the view by a drag of this many pixels.</summary>
    public void Pan(Vector2 pixels)
    {
        FollowDriverId = null;
        Focus = _focus - pixels / (Box.Size * _zoom);
    }

    public static Color TeamColor(string teamId) => teamId switch
    {
        "alfa-rossa" => new Color(0.78f, 0.1f, 0.1f),
        "bugetti" => new Color(0.16f, 0.32f, 0.72f),
        "masarelli" => new Color(0.95f, 0.55f, 0.1f),
        "mercator-benz" => new Color(0.92f, 0.92f, 0.9f),
        _ => new Color(0.6f, 0.6f, 0.6f),
    };

    /// <summary>Track drawings are 1.4 times wider than tall; at zoom 1 that box fits, centred, in the area.</summary>
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

    private Vector2 Map(Vector2 p) => Area.GetCenter() + (p - _focus) * Box.Size * _zoom;

    private Vector2 Unmap(Vector2 screen) => _focus + (screen - Area.GetCenter()) / (Box.Size * _zoom);

    public override void _Draw()
    {
        if (_track is null)
            return;

        if (_ground is null)
            DrawRect(Area, Grass);
        var scale = Box.Size.Y * _zoom;
        float roadWidth;

        if (_ground is not null)
        {
            DrawTextureRect(_ground, new Rect2(Map(Vector2.Zero), Box.Size * _zoom), false);
            roadWidth = scale * PaintedRoad;
        }
        else
        {
            foreach (var w in _track.Water)
                DrawRect(new Rect2(Map(w.Position), w.Size * Box.Size * _zoom), Water);
            foreach (var t in _trees)
                DrawCircle(Map(t), scale * 0.012f, Tree);
            foreach (var s in _track.Grandstands)
                DrawRect(new Rect2(Map(s.Position), s.Size * Box.Size * _zoom), Stand);
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
            roadWidth = scale * 0.032f;
        }

        var font = ThemeDB.FallbackFont;
        foreach (var (name, at) in _track.Corners)
        {
            const int size = 13;
            var width = font.GetStringSize(name, HorizontalAlignment.Left, -1, size).X;
            var p = Map(at) + new Vector2(-width / 2, size / 3f);
            DrawString(font, p + new Vector2(1, 1), name, HorizontalAlignment.Left, -1, size, new Color(0, 0, 0, 0.6f));
            DrawString(font, p, name, HorizontalAlignment.Left, -1, size, new Color(1, 0.97f, 0.88f, 0.95f));
        }

        DrawCars(font, scale, roadWidth);

        if (_track.Credit.Length > 0)
        {
            const int size = 11;
            var width = font.GetStringSize(_track.Credit, HorizontalAlignment.Left, -1, size).X;
            DrawString(font, Area.End - new Vector2(width + 8, 8), _track.Credit, HorizontalAlignment.Left, -1, size, new Color(1, 1, 1, 0.7f));
        }
    }

    private void DrawCars(Font font, float scale, float roadWidth)
    {
        // Cars are drawn bigger than life so they read at full view; up close they reach true size.
        var length = Mathf.Clamp(scale * 0.026f, 16, 110);
        // Leader drawn last, on top.
        foreach (var (car, index) in _cars.Select((c, i) => (c, i)).Reverse())
        {
            var p = Map(_track!.At(car.Distance));
            var forward = (Map(_track.At(car.Distance + 0.0015)) - p).Normalized();
            if (forward == Vector2.Zero)
                forward = Vector2.Right;
            var left = new Vector2(forward.Y, -forward.X);
            if (car.InPits)
                p += left * roadWidth * 1.6f;          // standing in the pits beside the start line
            else if (car.Running)
                p += left * roadWidth * 0.22f * (index % 3 - 1); // spread across the road so a pack doesn't stack

            var color = TeamColor(car.Entry.Team.Id);
            if (car.Retirement is not null)
                color = color.Darkened(0.55f);

            // Dust thrown up behind a running car.
            if (car.Running && !car.InPits)
            {
                for (var k = 1; k <= 3; k++)
                    DrawCircle(p - forward * length * (0.5f + 0.35f * k), length * (0.12f + 0.06f * k), new Color(0.82f, 0.76f, 0.62f, 0.22f / k));
            }

            if (_carBody is not null && _carDetail is not null)
            {
                var s = length / _carBody.GetWidth();
                var size = _carBody.GetSize();
                DrawSetTransform(p + new Vector2(1, 1.5f) * length * 0.06f, forward.Angle(), new Vector2(s, s));
                DrawTexture(_carBody, -size / 2, new Color(0, 0, 0, 0.35f));
                DrawSetTransform(p, forward.Angle(), new Vector2(s, s));
                DrawTexture(_carBody, -size / 2, color);
                DrawTexture(_carDetail, -size / 2, car.Retirement is null ? Colors.White : new Color(0.6f, 0.6f, 0.6f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            else
            {
                DrawCircle(p, length * 0.3f, Colors.Black);
                DrawCircle(p, length * 0.25f, color);
            }

            var label = (index + 1).ToString();
            var at = p + new Vector2(-12, -length * 0.45f - 4);
            DrawString(font, at + new Vector2(1, 1), label, HorizontalAlignment.Center, 24, 14, new Color(0, 0, 0, 0.7f));
            DrawString(font, at, label, HorizontalAlignment.Center, 24, 14, Colors.White);
        }
    }

    /// <summary>Reads a picture straight from the project files, so it needs no editor import step.</summary>
    public static Texture2D? LoadTexture(string path)
    {
        if (!FileAccess.FileExists(path))
            return null;
        var bytes = FileAccess.GetFileAsBytes(path);
        var image = new Image();
        var error = path.EndsWith(".png") ? image.LoadPngFromBuffer(bytes) : image.LoadJpgFromBuffer(bytes);
        if (error != Error.Ok)
            return null;
        image.GenerateMipmaps();
        var texture = ImageTexture.CreateFromImage(image);
        image.Dispose();
        return texture;
    }
}
