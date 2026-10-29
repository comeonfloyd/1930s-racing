using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// A closed circuit outline in normalised 0..1 coordinates, smoothed from a few control
/// points. Maps a lap fraction to a point on the track, and, once it knows the circuit, a car's
/// progress through its lap to where it really is: ahead of the even pace on the straights,
/// behind it in the bends (see <see cref="SpeedProfile"/>).
/// </summary>
public sealed class TrackShape
{
    private readonly Vector2[] _points;
    private readonly float[] _distances;

    public IReadOnlyList<Vector2> Points => _points;
    public IReadOnlyList<Rect2> Grandstands { get; }
    public IReadOnlyList<Rect2> Water { get; }

    /// <summary>Track that exists but this race does not use, such as the rest of a banked bowl.</summary>
    public IReadOnlyList<Vector2[]> Unused { get; }

    /// <summary>Corner names and where to print them.</summary>
    public IReadOnlyList<(string Name, Vector2 At)> Corners { get; }

    /// <summary>Where the outline came from, shown under the drawing (map data licences ask for it).</summary>
    public string Credit { get; private init; } = "";

    private TrackShape(
        IReadOnlyList<Vector2> controlPoints,
        IReadOnlyList<Rect2> grandstands,
        IReadOnlyList<Rect2>? water = null,
        IReadOnlyList<Vector2[]>? unused = null,
        IReadOnlyList<(string, Vector2)>? corners = null)
    {
        _points = Smooth(controlPoints, 16);
        _distances = new float[_points.Length + 1];
        for (var i = 1; i <= _points.Length; i++)
            _distances[i] = _distances[i - 1] + (float)TrackLine.Length(Pair(_points[i - 1]), Pair(_points[i % _points.Length]));
        Grandstands = grandstands;
        Water = water ?? Array.Empty<Rect2>();
        Unused = unused ?? Array.Empty<Vector2[]>();
        Corners = corners ?? Array.Empty<(string, Vector2)>();
    }

    /// <summary>Loads res://data/tracks/{circuitId}.json, or a plain oval when there is no file yet.</summary>
    public static TrackShape For(string circuitId)
    {
        var path = $"res://data/tracks/{circuitId}.json";
        if (!FileAccess.FileExists(path))
            return Oval();

        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
        var root = doc.RootElement;
        static Vector2 Point(JsonElement p) => new(p[0].GetSingle(), p[1].GetSingle());
        static Rect2 Box(JsonElement r) => new(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle());
        List<T> Optional<T>(string name, Func<JsonElement, T> read) =>
            root.TryGetProperty(name, out var e) ? e.EnumerateArray().Select(read).ToList() : new List<T>();

        return new TrackShape(
            root.GetProperty("points").EnumerateArray().Select(Point).ToList(),
            Optional("grandstands", Box),
            Optional("water", Box),
            Optional("unused", u => u.EnumerateArray().Select(Point).ToArray()),
            Optional("corners", c => (c[0].GetString() ?? "", new Vector2(c[1].GetSingle(), c[2].GetSingle()))))
        {
            Credit = root.TryGetProperty("credit", out var credit) ? credit.GetString() ?? "" : "",
        };
    }

    private static TrackShape Oval()
    {
        var points = Enumerable.Range(0, 12)
            .Select(i => i * Mathf.Tau / 12)
            .Select(a => new Vector2(0.5f + 0.38f * Mathf.Cos(a), 0.5f + 0.3f * Mathf.Sin(a)))
            .ToList();
        return new TrackShape(points, new[] { new Rect2(0.4f, 0.84f, 0.2f, 0.03f) });
    }

    /// <summary>Point at a lap fraction (0 = start line), in normalised coordinates.</summary>
    public Vector2 At(double lapFraction)
    {
        var total = _distances[^1];
        var d = (float)(lapFraction - Math.Floor(lapFraction)) * total;
        var i = Array.FindIndex(_distances, x => x > d) - 1;
        if (i < 0)
            i = _points.Length - 1;
        var segment = _distances[i + 1] - _distances[i];
        var t = segment <= 0 ? 0 : (d - _distances[i]) / segment;
        return _points[i].Lerp(_points[(i + 1) % _points.Length], t);
    }

    private static (double X, double Y) Pair(Vector2 p) => (p.X, p.Y);

    private Circuit? _circuit;
    private TrackLine? _line;
    private readonly Dictionary<(string DriverId, bool Standing), SpeedProfile> _profiles = new();

    /// <summary>The circuit this outline is drawn for: its lap length scales the drawing to real metres.</summary>
    public void SetCircuit(Circuit circuit)
    {
        _circuit = circuit;
        _line = new TrackLine(_points.Select(Pair).ToList(), circuit.LapKm);
        _profiles.Clear();
    }

    private SpeedProfile? Profile(LiveCar car)
    {
        if (_line is null || _circuit is null)
            return null;
        var key = (car.Entry.Driver.Id, car.StandingStart);
        if (!_profiles.TryGetValue(key, out var profile))
        {
            // Fitted once to the car and driver's usual lap; the lap's own time is kept below in any case.
            var usual = _circuit.LapKm / RaceSim.AverageSpeedKmh(car.Entry, _circuit) * 3600;
            _profiles[key] = profile = SpeedProfile.Matching(_line, car.Entry.Team.Car, _circuit.Type, usual, car.StandingStart);
        }
        return profile;
    }

    /// <summary>
    /// Where a car is, in laps as drawn. The race times each lap and moves the car round it at an
    /// even pace; here that time is shared out along the lap as the car would really cover it,
    /// so it still crosses the line on the same tick.
    /// </summary>
    public double Placed(LiveCar car)
    {
        var lap = Math.Floor(car.Distance);
        var part = car.Distance - lap;
        return part <= 0 || Profile(car) is not { } profile ? car.Distance : lap + profile.DistanceAt(part);
    }

    /// <summary>The car's speed now in km/h, or 0 when standing or the circuit is not known.</summary>
    public double SpeedKmh(LiveCar car)
    {
        if (!car.Running || car.InPits || car.LapSeconds <= 0 || Profile(car) is not { } profile)
            return 0;
        var part = Placed(car) % 1;
        return profile.SpeedKmhAt(part) * profile.LapSeconds / car.LapSeconds;
    }

    /// <summary>Closed Catmull-Rom spline through the control points.</summary>
    private static Vector2[] Smooth(IReadOnlyList<Vector2> p, int steps)
    {
        var n = p.Count;
        var result = new List<Vector2>(n * steps);
        for (var i = 0; i < n; i++)
        {
            Vector2 p0 = p[(i - 1 + n) % n], p1 = p[i], p2 = p[(i + 1) % n], p3 = p[(i + 2) % n];
            for (var s = 0; s < steps; s++)
            {
                var t = s / (float)steps;
                result.Add(0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t));
            }
        }
        return result.ToArray();
    }
}
