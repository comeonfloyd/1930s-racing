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
public partial class TrackView : Node2D, IRaceView
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
    // Text is shown with Label nodes, not drawn with a Font from C#: holding the fallback
    // font in C# and drawing with it every frame crashed Godot ("!rc_owner") and hung it at quit.
    private readonly List<(Label Label, Vector2 At)> _cornerLabels = new();
    private readonly List<Label> _carLabels = new();
    private Vector2[] _carLabelSpots = Array.Empty<Vector2>();
    private Label? _creditLabel;
    private IReadOnlyList<LiveCar> _cars = Array.Empty<LiveCar>();
    private Vector2[] _trees = Array.Empty<Vector2>();
    private float _zoom = 1;
    private Vector2 _focus = new(0.5f, 0.5f);

    public Rect2 Area { get; set; }

    /// <summary>How hard it is raining, 0 (dry) to 1: darkens the view and draws falling rain.</summary>
    public float Rain { get; set; }

    /// <summary>How dark it is, 0 (day) to 1 (night): shades the view, and cars light their headlamps.</summary>
    public float Night { get; set; }

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
        if (DevShot.Arg("no-textures") is null)
        {
            _ground = LoadTexture($"res://art/tracks/{circuitId}.jpg");
            _carBody ??= LoadTexture("res://art/car-body.png");
            _carDetail ??= LoadTexture("res://art/car-detail.png");
        }
        foreach (var (label, _) in _cornerLabels)
            label.QueueFree();
        _cornerLabels.Clear();
        foreach (var (name, at) in track.Corners)
            _cornerLabels.Add((TextLabel(name, 13, new Color(1, 0.97f, 0.88f, 0.95f)), at));
        _creditLabel?.QueueFree();
        _creditLabel = track.Credit.Length > 0 ? TextLabel(track.Credit, 11, new Color(1, 1, 1, 0.7f)) : null;
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
        while (_carLabels.Count < cars.Count)
            _carLabels.Add(TextLabel((_carLabels.Count + 1).ToString(), 14, Colors.White));
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
        // A team of the player's own carries the colours they chose.
        OwnTeam.Id when Session.Career?.Team.Colour is { } own => new Color(own),
        "alfa-rossa" => new Color(0.78f, 0.1f, 0.1f),
        "bugetti" => new Color(0.16f, 0.32f, 0.72f),
        "masarelli" => new Color(0.95f, 0.55f, 0.1f),
        "mercator-benz" => new Color(0.92f, 0.92f, 0.9f),
        "auto-unitas" => new Color(0.72f, 0.74f, 0.78f),
        "millard" => new Color(0.2f, 0.55f, 0.35f),
        "duesenhoff" => new Color(0.55f, 0.3f, 0.6f),
        "privateers" or "us-privateers" => new Color(0.45f, 0.38f, 0.3f),
        // Le Mans sports-car entrants.
        "bentlay" => new Color(0.1f, 0.3f, 0.18f),
        "lagonde" => new Color(0.32f, 0.46f, 0.3f),
        "ryley" => new Color(0.36f, 0.46f, 0.62f),
        "aston-marten" => new Color(0.56f, 0.72f, 0.62f),
        "alfa-private" => new Color(0.58f, 0.16f, 0.16f),
        "talbert" or "talbert-lago" => new Color(0.2f, 0.3f, 0.56f),
        "delahoye" => new Color(0.26f, 0.42f, 0.86f),
        "delange" => new Color(0.46f, 0.56f, 0.88f),
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

        if (Night > 0.01f)
            DrawRect(GetViewportRect(), NightShade with { A = 0.62f * Night });
        DrawCars(scale, roadWidth);
        DrawRain(this, GetViewportRect(), Rain);
    }

    private static readonly Color NightShade = new(0.02f, 0.03f, 0.09f);
    private static readonly Color Headlamp = new(1f, 0.93f, 0.65f);
    private static readonly Color RainShade = new(0.12f, 0.16f, 0.24f);
    private static readonly Color RainStreak = new(0.8f, 0.86f, 0.95f);

    /// <summary>A grey wash and slanting streaks, more of them the harder it rains.</summary>
    /// <summary>The whole window, not just the drawing's area, so no dry edge shows when zoomed in.</summary>
    public static void DrawRain(CanvasItem canvas, Rect2 frame, float rain)
    {
        if (rain <= 0.01f)
            return;
        canvas.DrawRect(frame, RainShade with { A = 0.3f * rain });
        var t = Time.GetTicksMsec() / 1000.0;
        var count = (int)(320 * rain);
        var fall = new Vector2(-6, 18);
        for (var i = 0; i < count; i++)
        {
            // Fixed scattered columns; each streak falls and wraps round the view.
            var x = Fract(Mathf.Sin(i * 12.9898f) * 43758.5453f);
            var y = Fract(Fract(Mathf.Sin(i * 78.233f) * 12345.678f) + t * (0.9f + 0.4f * x));
            var at = frame.Position + new Vector2(x, (float)y) * frame.Size;
            canvas.DrawLine(at, at + fall, RainStreak with { A = 0.35f * rain }, 1);
        }
    }

    private static float Fract(double v) => (float)(v - Math.Floor(v));

    /// <summary>Moves the text labels to where the last drawing put things.</summary>
    public override void _Process(double delta)
    {
        // Corner names stay inside the drawing, even for a corner right at its edge; a corner
        // outside the view when zoomed in has no name shown.
        foreach (var (label, at) in _cornerLabels)
        {
            var spot = Map(at);
            label.Visible = Area.HasPoint(spot);
            label.Position = (spot - label.Size / 2).Clamp(Area.Position, Area.End - label.Size);
        }
        if (_creditLabel is not null)
            _creditLabel.Position = Area.End - _creditLabel.Size - new Vector2(8, 4);
        // Leader first; a number that would touch a better-placed car's number is hidden.
        var placed = new List<Rect2>();
        for (var i = 0; i < _carLabels.Count; i++)
        {
            var label = _carLabels[i];
            var rect = i < _carLabelSpots.Length ? new Rect2(_carLabelSpots[i] - label.Size / 2, label.Size) : default;
            var shown = i < _carLabelSpots.Length && !placed.Any(r => r.Grow(1).Intersects(rect));
            label.Visible = shown;
            if (!shown)
                continue;
            label.Position = rect.Position;
            placed.Add(rect);
        }
    }

    private Label TextLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(label);
        return label;
    }

    private void DrawCars(float scale, float roadWidth)
    {
        if (_carLabelSpots.Length != _cars.Count)
            _carLabelSpots = new Vector2[_cars.Count];
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
            color = color.Darkened(0.4f * Night);

            // At night, a pale beam of headlamp light on the road ahead.
            if (Night > 0.01f && car.Running && !car.InPits)
            {
                var near = p + forward * length * 0.45f;
                var far = p + forward * length * 3.2f;
                DrawColoredPolygon(new[] { near + left * length * 0.14f, far + left * length * 0.75f, far - left * length * 0.75f, near - left * length * 0.14f },
                    Headlamp with { A = 0.22f * Night });
                DrawCircle(near, length * 0.12f, Headlamp with { A = 0.7f * Night });
            }

            // Dust thrown up behind a running car, unseen in the dark.
            if (car.Running && !car.InPits && Night < 0.5f)
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

            _carLabelSpots[index] = p + new Vector2(0, -length * 0.45f - 12);
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
