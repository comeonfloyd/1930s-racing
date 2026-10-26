using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// The travel map: the painted chart of the eastern USA and western Europe
/// (tools/world-map/paint.py) with a pin for every race of the season, the team's base, and the
/// route the cars take through the races entered. Clicking a race's name enters it.
/// Text is Label nodes, not drawn with a font from C# (see TrackView).
/// </summary>
public partial class TravelMap : Control
{
    private sealed record Panel(Region Region, double LonMin, double LonMax, double LatMin, double LatMax, Rect2 Box);

    private static readonly Color Ink = new("5c4228");
    private static readonly Color RouteDone = new(0.36f, 0.26f, 0.16f, 0.55f);

    private Texture2D? _map;
    private Vector2 _mapSize = new(2400, 1200);
    private List<Panel> _panels = new();
    private Career? _career;
    private GameData? _data;
    private readonly List<(Label Label, Vector2 At, bool Below)> _labels = new();

    /// <summary>Called with an event id when the player clicks a race they can still enter.</summary>
    public Action<string>? EnterRace { get; set; }

    public override void _Ready()
    {
        ClipContents = true;
        _map = TrackView.LoadTexture("res://art/map.jpg");
        var json = FileAccess.GetFileAsString("res://data/map.json");
        if (json.Length > 0)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            _mapSize = new Vector2(root.GetProperty("width").GetSingle(), root.GetProperty("height").GetSingle());
            _panels = root.GetProperty("panels").EnumerateArray().Select(p => new Panel(
                Enum.Parse<Region>(p.GetProperty("region").GetString()!),
                p.GetProperty("lonMin").GetDouble(), p.GetProperty("lonMax").GetDouble(),
                p.GetProperty("latMin").GetDouble(), p.GetProperty("latMax").GetDouble(),
                new Rect2(p.GetProperty("x").GetSingle(), p.GetProperty("y").GetSingle(), p.GetProperty("w").GetSingle(), p.GetProperty("h").GetSingle())))
                .ToList();
        }
        Resized += QueueRedraw;
    }

    public void SetSeason(GameData data, Career career)
    {
        _data = data;
        _career = career;
        foreach (var (label, _, _) in _labels)
            label.QueueFree();
        _labels.Clear();

        var bases = data.Cities[career.Team.BaseCityId];
        AddLabel($"{bases.Name} (works)", Project(bases.Region, bases.Latitude, bases.Longitude), Ink, null, below: true);
        foreach (var ev in career.CurrentSeason.Events)
        {
            var circuit = data.Circuits[ev.CircuitId];
            var record = career.ResultOf(ev);
            var entered = career.Entries.Contains(ev);
            var text = $"{ShortName(ev)} · {ev.Date.ToString("d MMM", CultureInfo.InvariantCulture)}";
            if (record is not null)
                text += $"\nWon by {record.Winner.Entry.Driver.Name}";
            else if (!entered && ev.Date >= career.Today)
                text += "\nClick to enter";
            var color = record is not null ? new Color(Ink, 0.55f) : entered ? new Color("8e1b12") : Ink;
            var id = ev.Id;
            AddLabel(text, Project(circuit.Region, circuit.Latitude, circuit.Longitude), color,
                record is null && !entered && ev.Date >= career.Today ? () => EnterRace?.Invoke(id) : null);
        }
        QueueRedraw();
    }

    private static string ShortName(RaceEvent ev) => ev.Name.Replace(" Grand Prix", " GP");

    // ------------------------------------------------------------------ drawing

    /// <summary>Where the map picture sits in this control: as large as fits, centred.</summary>
    private Rect2 MapRect
    {
        get
        {
            var scale = Mathf.Min(Size.X / _mapSize.X, Size.Y / _mapSize.Y);
            var size = _mapSize * scale;
            return new Rect2((Size - size) / 2, size);
        }
    }

    /// <summary>A place on the map in map-picture pixels, or null outside every panel.</summary>
    private Vector2? Project(Region region, double lat, double lon)
    {
        var panel = _panels.FirstOrDefault(p => p.Region == region);
        if (panel is null)
            return null;
        var x = panel.Box.Position.X + (lon - panel.LonMin) / (panel.LonMax - panel.LonMin) * panel.Box.Size.X;
        var y = panel.Box.Position.Y + (panel.LatMax - lat) / (panel.LatMax - panel.LatMin) * panel.Box.Size.Y;
        return new Vector2((float)x, (float)y);
    }

    private Vector2 ToScreen(Vector2 mapPixel) => MapRect.Position + mapPixel / _mapSize * MapRect.Size;

    public override void _Draw()
    {
        var rect = MapRect;
        if (_map is not null)
            DrawTextureRect(_map, rect, false);
        else
            DrawRect(rect, new Color("e2d0aa"));
        if (_career is null || _data is null)
            return;

        // The route: the works, then every race entered, in date order.
        var stops = new List<(Region Region, Vector2 At, bool Done)>();
        var home = _data.Cities[_career.Team.BaseCityId];
        if (Project(home.Region, home.Latitude, home.Longitude) is { } h)
            stops.Add((home.Region, h, true));
        foreach (var ev in _career.Entries)
        {
            var c = _data.Circuits[ev.CircuitId];
            if (Project(c.Region, c.Latitude, c.Longitude) is { } at)
                stops.Add((c.Region, at, _career.ResultOf(ev) is not null));
        }
        for (var i = 1; i < stops.Count; i++)
        {
            var (fromRegion, from, _) = stops[i - 1];
            var (toRegion, to, done) = stops[i];
            var color = done ? RouteDone : new Color("8e1b12");
            if (fromRegion == toRegion)
                Dashed(ToScreen(from), ToScreen(to), color, done);
            else
            {
                // Across the Atlantic: to the port side of one panel, then from the other.
                var gapY = (from.Y + to.Y) / 2;
                var leave = new Vector2(fromRegion == Region.Europe ? EdgeX(Region.Europe, left: true) : EdgeX(Region.Usa, left: false), from.Y);
                var arrive = new Vector2(toRegion == Region.Europe ? EdgeX(Region.Europe, left: true) : EdgeX(Region.Usa, left: false), to.Y);
                Dashed(ToScreen(from), ToScreen(leave), color, done);
                Dashed(ToScreen(leave), ToScreen(new Vector2((leave.X + arrive.X) / 2, gapY)), color, done);
                Dashed(ToScreen(new Vector2((leave.X + arrive.X) / 2, gapY)), ToScreen(arrive), color, done);
                Dashed(ToScreen(arrive), ToScreen(to), color, done);
            }
        }

        // Pins: the works as a square, races as round pins.
        if (Project(home.Region, home.Latitude, home.Longitude) is { } homeAt)
        {
            var p = ToScreen(homeAt);
            DrawRect(new Rect2(p - new Vector2(6, 6), new Vector2(12, 12)), TrackView.TeamColor(_career.Team.Id));
            DrawRect(new Rect2(p - new Vector2(6, 6), new Vector2(12, 12)), Ink, false, 2);
        }
        var next = _career.NextRace;
        foreach (var ev in _career.CurrentSeason.Events)
        {
            var c = _data.Circuits[ev.CircuitId];
            if (Project(c.Region, c.Latitude, c.Longitude) is not { } at)
                continue;
            var p = ToScreen(at);
            var entered = _career.Entries.Contains(ev);
            var run = _career.ResultOf(ev) is not null;
            if (ev.Id == next?.Id)
                DrawCircle(p, 13, new Color(UiTheme.Accent, 0.6f));
            DrawCircle(p, 8, Ink);
            DrawCircle(p, 6, run ? new Color("b8a888") : entered ? new Color("c0281c") : new Color("f2e6c8"));
        }
    }

    private float EdgeX(Region region, bool left)
    {
        var panel = _panels.First(p => p.Region == region);
        return left ? panel.Box.Position.X : panel.Box.End.X;
    }

    private void Dashed(Vector2 from, Vector2 to, Color color, bool solid)
    {
        if (solid)
            DrawLine(from, to, color, 2, true);
        else
            DrawDashedLine(from, to, color, 3, 9, true, true);
    }

    // ------------------------------------------------------------------ labels

    private void AddLabel(string text, Vector2? at, Color color, Action? onClick, bool below = false)
    {
        if (at is null)
            return;
        var label = new Label { Text = text, MouseFilter = onClick is null ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_outline_color", new Color("efe2c2"));
        label.AddThemeConstantOverride("outline_size", 5);
        if (onClick is not null)
        {
            label.MouseDefaultCursorShape = CursorShape.PointingHand;
            label.TooltipText = "Enter this race";
            label.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    onClick();
            };
        }
        AddChild(label);
        _labels.Add((label, at.Value, below));
    }

    public override void _Process(double delta) => PlaceLabels();

    /// <summary>
    /// Labels sit to the right of their pin, or to the left when another pin is close on the
    /// right (Spa and the Nürburgring), and the works label sits below, clear of nearby races.
    /// </summary>
    private void PlaceLabels()
    {
        var pins = _labels.Select(l => ToScreen(l.At)).ToList();
        for (var i = 0; i < _labels.Count; i++)
        {
            var (label, _, below) = _labels[i];
            var p = pins[i];
            if (below)
            {
                label.Position = p + new Vector2(-label.Size.X - 8, 6);
                continue;
            }
            var crowded = pins.Where((q, j) => j != i && !_labels[j].Below)
                .Any(q => q.X > p.X && q.X - p.X < 160 && Math.Abs(q.Y - p.Y) < 40);
            label.Position = crowded ? p + new Vector2(-label.Size.X - 12, -10) : p + new Vector2(12, -10);
        }
    }
}
