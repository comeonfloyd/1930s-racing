namespace RacingSim;

/// <summary>
/// A circuit's racing line as the cars see it: the drawn outline from the track file, scaled to
/// the real lap length and cut into short steps, with how sharply the road turns at each.
/// </summary>
public sealed class TrackLine
{
    /// <summary>The drawing box is this much wider than tall; track files store x as a share of the width.</summary>
    public const double Aspect = 1.4;

    /// <summary>Length of one step along the line.</summary>
    public const double StepMetres = 2;

    /// <summary>
    /// The heading change that sets a step's curvature is measured over this stretch of road.
    /// The tracings put a point every 30 to 120 metres, so a shorter stretch would only measure
    /// the kinks between them.
    /// </summary>
    public const double CurveWindowMetres = 40;

    /// <summary>The tightest bend the line can have: a hairpin on a mountain road.</summary>
    public const double MinRadiusMetres = 9;

    /// <summary>Curvature (1 / radius, in 1/m, always positive) at each step.</summary>
    public IReadOnlyList<double> Curvature { get; }

    public double LapMetres { get; }

    /// <param name="points">The smoothed outline in the track file's 0..1 drawing coordinates.</param>
    /// <param name="lapKm">The real lap length; the drawing is to scale, so one factor fits it.</param>
    public TrackLine(IReadOnlyList<(double X, double Y)> points, double lapKm)
    {
        var n = points.Count;
        var drawn = new double[n + 1];
        for (var i = 1; i <= n; i++)
            drawn[i] = drawn[i - 1] + Length(points[i - 1], points[i % n]);
        LapMetres = lapKm * 1000;
        var scale = LapMetres / drawn[^1];

        // Headings of the outline's segments, unwrapped so they can be subtracted.
        var heading = new double[n];
        for (var i = 0; i < n; i++)
        {
            var (a, b) = (points[i], points[(i + 1) % n]);
            heading[i] = Math.Atan2(b.Y - a.Y, (b.X - a.X) * Aspect);
            if (i > 0)
                heading[i] = heading[i - 1] + Wrap(heading[i] - heading[i - 1]);
        }
        // Back round to the first segment: a closed loop has turned once round, one way or the other.
        var full = Math.Sign(heading[n - 1] + Wrap(heading[0] - heading[n - 1]) - heading[0]) * 2 * Math.PI;

        double HeadingAt(double metres)
        {
            var laps = Math.Floor(metres / LapMetres);
            var d = (metres - laps * LapMetres) / scale;
            var i = Math.Clamp(Array.BinarySearch(drawn, d) is var k && k >= 0 ? k : ~k - 1, 0, n - 1);
            return heading[i] + laps * full;
        }

        var steps = Math.Max(16, (int)Math.Round(LapMetres / StepMetres));
        var step = LapMetres / steps;
        var curvature = new double[steps];
        for (var i = 0; i < steps; i++)
        {
            var at = i * step;
            var change = HeadingAt(at + CurveWindowMetres / 2 + LapMetres) - HeadingAt(at - CurveWindowMetres / 2 + LapMetres);
            curvature[i] = Math.Min(1 / MinRadiusMetres, Math.Abs(change) / CurveWindowMetres);
        }
        // The heading jumps from segment to segment, so average once more over the same stretch.
        var half = (int)(CurveWindowMetres / step / 2);
        var smooth = new double[steps];
        for (var i = 0; i < steps; i++)
        {
            double sum = 0;
            for (var j = -half; j <= half; j++)
                sum += curvature[(i + j + steps) % steps];
            smooth[i] = sum / (2 * half + 1);
        }
        Curvature = smooth;
    }

    /// <summary>Closed Catmull-Rom spline through the control points, the same curve the race view draws.</summary>
    public static (double X, double Y)[] Smooth(IReadOnlyList<(double X, double Y)> p, int steps)
    {
        var n = p.Count;
        var result = new List<(double, double)>(n * steps);
        for (var i = 0; i < n; i++)
        {
            var (p0, p1, p2, p3) = (p[(i - 1 + n) % n], p[i], p[(i + 1) % n], p[(i + 2) % n]);
            for (var s = 0; s < steps; s++)
            {
                var t = s / (double)steps;
                double Axis(double a, double b, double c, double d) =>
                    0.5 * (2 * b + (c - a) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (3 * b - a - 3 * c + d) * t * t * t);
                result.Add((Axis(p0.X, p1.X, p2.X, p3.X), Axis(p0.Y, p1.Y, p2.Y, p3.Y)));
            }
        }
        return result.ToArray();
    }

    /// <summary>Distance between two drawing points, in drawing heights.</summary>
    public static double Length((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt(Math.Pow((b.X - a.X) * Aspect, 2) + Math.Pow(b.Y - a.Y, 2));

    private static double Wrap(double angle) => Math.IEEERemainder(angle, 2 * Math.PI);
}

/// <summary>
/// How fast a car goes at each point of a lap: as fast as the bend ahead allows, braking hard
/// into it, slowest through it and accelerating out on the power it has, until drag holds it
/// at top speed on a long straight. The race view uses it to move the cars; the race itself is
/// timed by <see cref="LiveRace"/>, so the profile only shares out each lap's time along the
/// lap and never changes it.
///
/// The figures are of the period: drum brakes stopping at about 0.65 g, narrow cross-ply tyres
/// holding about 0.8 g in a bend and putting down about half a g before the wheels spin, and a
/// tall open-wheeled car with its driver sitting up in the wind. With them a 215 hp Alfa P3 tops
/// out near 230 km/h and a 560 hp Mercedes of 1937 above 300.
/// </summary>
public sealed class SpeedProfile
{
    private const double G = 9.81;
    private const double AirDensity = 1.2;
    private const double Drivetrain = 0.85;
    private const double RollingResistance = 0.015;
    private const double BrakingG = 0.65;
    private const double TractionG = 0.5;

    /// <summary>Weight on board besides the car: the driver and a part-full tank.</summary>
    private const double CrewAndFuelKg = 140;

    /// <summary>Speed at each step, m/s; the last entry is the speed back at the line.</summary>
    private readonly double[] _speeds;

    /// <summary>Seconds from the line to each step; the last entry is the whole lap.</summary>
    private readonly double[] _times;

    private readonly double _step;

    public double LapSeconds => _times[^1];
    public double GripScale { get; }
    public double TopSpeedKmh => _speeds.Max() * 3.6;
    public double SlowestKmh => _speeds.Skip(1).Min() * 3.6;

    /// <param name="standingStart">The lap starts from rest: the first lap, or out of the pits.</param>
    /// <param name="gripScale">Grip in the bends against the period figure; see <see cref="Matching"/>.</param>
    public SpeedProfile(TrackLine line, Car car, CircuitType type, bool standingStart = false, double gripScale = 1)
    {
        GripScale = gripScale;
        var curvature = line.Curvature;
        var n = curvature.Count;
        _step = line.LapMetres / n;
        var mass = car.WeightKg + CrewAndFuelKg;
        var power = car.Horsepower * 745.7 * Drivetrain;
        var drag = 0.5 * AirDensity * DragArea(car) / gripScale;
        // Grip in a bend: the better-handling cars of the day held a little more.
        var flat = (0.8 + 0.01 * (car.Handling - 13)) * gripScale;
        var grip = flat * Banking(type, Math.Min(flat, 1.2));

        var corner = new double[n];
        for (var i = 0; i < n; i++)
            corner[i] = curvature[i] <= 0 ? double.MaxValue : Math.Sqrt(grip * G / curvature[i]);

        double Accelerate(double v)
        {
            var push = Math.Min(TractionG * G, power / (mass * Math.Max(v, 1)));
            return Math.Max(0, push - drag * v * v / mass - RollingResistance * G);
        }
        double Brake(double v) => BrakingG * G + drag * v * v / mass + RollingResistance * G;

        // A flying lap: go round twice, so the speed at the line is what the last bend leaves.
        var flying = new double[n + 1];
        var v = 0.0;
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < n; i++)
            {
                flying[i] = v = Math.Min(v, corner[i]);
                v = Math.Sqrt(v * v + 2 * Accelerate(v) * _step);
            }
        flying[n] = flying[0];
        var after = flying[0];
        for (var pass = 0; pass < 2; pass++)
            for (var i = n - 1; i >= 0; i--)
            {
                var limit = Math.Sqrt(after * after + 2 * Brake(after) * _step);
                flying[i] = after = Math.Min(flying[i], limit);
            }
        flying[n] = flying[0];

        _speeds = flying;
        if (standingStart)
        {
            // From rest, flat out until the car meets the speeds of a flying lap.
            _speeds = (double[])flying.Clone();
            v = 0;
            for (var i = 0; i <= n && v < _speeds[i]; i++)
            {
                _speeds[i] = v;
                v = Math.Sqrt(v * v + 2 * Accelerate(v) * _step);
            }
        }

        _times = new double[n + 1];
        for (var i = 1; i <= n; i++)
            _times[i] = _times[i - 1] + 2 * _step / Math.Max(0.5, _speeds[i - 1] + _speeds[i]);
    }

    /// <summary>
    /// The profile for a car whose flying lap takes the given time in the race. The drawn
    /// outlines round off the tightest bends and leave out the hills, so on most circuits the
    /// period figures alone lap quicker than the cars really did. The difference is taken out of
    /// the bends, where the drawing is least true, and top speeds on the straights stay what
    /// the engine gives. When even that cannot reach the time (rain, a car nursed home) the
    /// view just plays the lap slower.
    /// </summary>
    public static SpeedProfile Matching(TrackLine line, Car car, CircuitType type, double lapSeconds, bool standingStart = false)
    {
        double low = 0.3, high = 1.4;
        for (var i = 0; i < 18; i++)
        {
            var mid = (low + high) / 2;
            if (new SpeedProfile(line, car, type, false, mid).LapSeconds > lapSeconds)
                low = mid;
            else
                high = mid;
        }
        return new SpeedProfile(line, car, type, standingStart, (low + high) / 2);
    }

    /// <summary>Frontal area times drag coefficient, m²: a sports car's wings and screen catch more air.</summary>
    private static double DragArea(Car car) => car.WeightKg > 1100 ? 1.2 : 0.9;

    /// <summary>
    /// How much more a banked bend holds than a flat one: Indianapolis is banked about 9 degrees,
    /// Brooklands' great bankings near 30 at the top.
    /// </summary>
    private static double Banking(CircuitType type, double grip)
    {
        var degrees = type switch
        {
            CircuitType.BrickOval => 9.0,
            CircuitType.BankedOval => 25.0,
            _ => 0.0,
        };
        var a = degrees * Math.PI / 180;
        return (Math.Sin(a) + grip * Math.Cos(a)) / (Math.Cos(a) - grip * Math.Sin(a)) / grip;
    }

    /// <summary>
    /// How far round the lap (0 to 1) the car is after this share of the lap's time: early in
    /// the lap where it is fast, it covers more road in the same time.
    /// </summary>
    public double DistanceAt(double timeFraction)
    {
        if (timeFraction <= 0)
            return 0;
        if (timeFraction >= 1)
            return 1;
        var t = timeFraction * _times[^1];
        var i = Array.BinarySearch(_times, t);
        if (i >= 0)
            return (double)i / (_times.Length - 1);
        i = ~i - 1;
        // Within a step the speed changes evenly, so the time does not; a straight share is close enough over 2 m.
        var part = (t - _times[i]) / (_times[i + 1] - _times[i]);
        return (i + part) / (_times.Length - 1);
    }

    /// <summary>Speed in km/h at a point of the lap (0 to 1), if the lap took <see cref="LapSeconds"/>.</summary>
    public double SpeedKmhAt(double distanceFraction)
    {
        var x = Math.Clamp(distanceFraction, 0, 1) * (_speeds.Length - 1);
        var i = Math.Min((int)x, _speeds.Length - 2);
        return (_speeds[i] + (_speeds[i + 1] - _speeds[i]) * (x - i)) * 3.6;
    }
}
