using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// The circuit in 3D, as a newsreel camera on a tower would see it: the painted ground from
/// res://art/tracks/{circuit}.jpg laid flat, the trees, houses, boats and grandstands painted on
/// it stood up as models (positions from res://data/scenery/{circuit}.json, written by
/// tools/track-art/paint.py), and the cars as <see cref="CarModel"/>s. The camera looks down
/// on the whole circuit at zoom 1 and comes down low behind the followed car as it zooms in.
/// One world unit is about 1.4 metres; the drawing's height is <see cref="K"/> units.
/// </summary>
public partial class TrackView3D : Control, IRaceView
{
    /// <summary>The drawing's height in world units (its width is 1.4 times that).</summary>
    private const float K = 200;
    private const float Aspect = 1.4f;
    private const float Fov = 40;
    public const float MaxZoom = 30;

    private SubViewport _viewport = null!;
    private Node3D _world = null!;
    private Camera3D _camera = null!;
    private DirectionalLight3D _sun = null!;
    private WorldEnvironment _environment = null!;
    private ProceduralSkyMaterial _sky = null!;
    private Node2D _overlay = null!;
    private CarModel? _models;
    private TrackShape? _track;
    private Node3D? _scenery;
    private readonly List<(Node3D Node, StandardMaterial3D Paint, Color Colour)> _carNodes = new();
    private readonly List<MeshInstance3D[]> _dust = new();
    private StandardMaterial3D[] _dustMaterials = Array.Empty<StandardMaterial3D>();
    private QuadMesh? _puff;
    private readonly Dictionary<string, float> _headings = new();
    private readonly List<SpotLight3D> _beams = new();
    private readonly List<(Label Label, Vector3 At)> _cornerLabels = new();
    private readonly List<Label> _carLabels = new();
    private Vector3[] _carLabelSpots = Array.Empty<Vector3>();
    // Where each car is, which way it points and its colour, for the dots drawn when cars are too small to see.
    private (Vector3 At, Vector3 Forward, Color Colour)[] _carMarks = Array.Empty<(Vector3, Vector3, Color)>();
    private Label? _creditLabel;
    private IReadOnlyList<LiveCar> _cars = Array.Empty<LiveCar>();

    private float _zoom = 1;
    private Vector3 _target = new(K * Aspect / 2, 0, K / 2);
    private float _yaw;
    private float _turned;
    private float _night;

    public Rect2 Area { get; set; }

    /// <summary>Two-seat sports cars with wings over the wheels (Le Mans), not single-seaters.</summary>
    public bool SportsCars { get; set; }
    public float Rain { get; set; }
    public string? FollowDriverId { get; set; }

    public float Night
    {
        get => _night;
        set
        {
            _night = value;
            Light();
        }
    }

    public float Zoom
    {
        get => _zoom;
        set => _zoom = Mathf.Clamp(value, 1, MaxZoom);
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(container);
        _viewport = new SubViewport
        {
            Msaa3D = Viewport.Msaa.Msaa4X,
            PositionalShadowAtlasSize = 1024,
            OwnWorld3D = true,
        };
        container.AddChild(_viewport);
        _world = new Node3D();
        _viewport.AddChild(_world);

        _camera = new Camera3D { Fov = Fov, Near = 0.5f, Far = 3000, Current = true };
        _world.AddChild(_camera);

        // Low morning sun from the upper left, as in the paintings, so the shadows agree.
        _sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = 300,
            LightColor = new Color(1f, 0.95f, 0.84f),
        };
        _world.AddChild(_sun);
        _sun.LookAtFromPosition(Vector3.Zero, new Vector3(1.0f, -1.1f, 0.8f), Vector3.Up);

        _sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.42f, 0.56f, 0.72f),
            SkyHorizonColor = new Color(0.78f, 0.76f, 0.68f),
            GroundBottomColor = new Color(0.22f, 0.24f, 0.16f),
            GroundHorizonColor = new Color(0.66f, 0.66f, 0.56f),
        };
        _environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = _sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                FogEnabled = true,
                FogLightColor = new Color(0.72f, 0.72f, 0.66f),
                FogDensity = 0.0012f,
                FogSkyAffect = 0.3f,
            },
        };
        _world.AddChild(_environment);

        // Rain streaks are drawn flat over the picture, as in the map view.
        _overlay = new Node2D();
        _overlay.Draw += () =>
        {
            DrawCarMarks();
            TrackView.DrawRain(_overlay, GetViewportRect(), Rain);
        };
        AddChild(_overlay);
        Light();
    }

    public void SetTrack(TrackShape track, string circuitId, int seed)
    {
        _track = track;
        _models ??= new CarModel();
        _scenery?.QueueFree();
        _scenery = new Node3D();
        _world.AddChild(_scenery);
        foreach (var (node, _, _) in _carNodes)
            node.QueueFree();
        _carNodes.Clear();

        var ground = TrackView.LoadTexture($"res://art/tracks/{circuitId}.jpg");
        var groundMaterial = new StandardMaterial3D
        {
            AlbedoColor = ground is null ? new Color(0.36f, 0.47f, 0.27f) : Colors.White,
            AlbedoTexture = ground,
            Roughness = 1,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        Add(_scenery, new PlaneMesh { Size = new Vector2(K * Aspect, K) }, groundMaterial, new Vector3(K * Aspect / 2, 0, K / 2));
        // The country beyond the edge of the painting, in the colour of its edge, fading into the haze.
        Add(_scenery, new PlaneMesh { Size = new Vector2(6000, 6000) }, new StandardMaterial3D { AlbedoColor = EdgeColour(circuitId), Roughness = 1 },
            new Vector3(K * Aspect / 2, -0.05f, K / 2));
        if (ground is null)
            AddPlainRoad(track);

        LoadScenery(circuitId);
        AddGrandstands(track);

        foreach (var (label, _) in _cornerLabels)
            label.QueueFree();
        _cornerLabels.Clear();
        foreach (var (name, at) in track.Corners)
            _cornerLabels.Add((TextLabel(name, 13, new Color(1, 0.97f, 0.88f, 0.95f)), World(at) + Vector3.Up * 3));
        _creditLabel?.QueueFree();
        _creditLabel = track.Credit.Length > 0 ? TextLabel(track.Credit, 11, new Color(1, 1, 1, 0.7f)) : null;
    }

    public void SetCars(IReadOnlyList<LiveCar> cars)
    {
        _cars = cars;
        if (_track is null || _models is null)
            return;
        while (_carNodes.Count < cars.Count)
        {
            var colour = TrackView.TeamColor(cars[_carNodes.Count].Entry.Team.Id);
            var (node, paint) = _models.Build(colour, SportsCars);
            _world.AddChild(node);
            _carNodes.Add((node, paint, colour));
            _dust.Add(DustPuffs());
        }
        while (_carLabels.Count < cars.Count)
            _carLabels.Add(TextLabel((_carLabels.Count + 1).ToString(), 14, Colors.White));
        if (_carLabelSpots.Length != cars.Count)
        {
            _carLabelSpots = new Vector3[cars.Count];
            _carMarks = new (Vector3, Vector3, Color)[cars.Count];
        }
    }

    /// <summary>Zooms by a factor, keeping the ground under the screen point where it is.</summary>
    public void ZoomAt(Vector2 screenPoint, float factor)
    {
        var before = Ground(screenPoint);
        Zoom = _zoom * factor;
        PlaceCamera(1);
        var after = Ground(screenPoint);
        if (before is { } b && after is { } a && FollowDriverId is null)
            _target += b - a;
        ClampTarget();
    }

    public void Pan(Vector2 pixels)
    {
        FollowDriverId = null;
        // Drag the ground: move the target against the drag, along the camera's own axes.
        var perPixel = Distance * 2 * Mathf.Tan(Mathf.DegToRad(Fov / 2)) / Math.Max(1, Size.Y);
        var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
        var back = new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw));
        _target -= (right * pixels.X + back * pixels.Y / Mathf.Sin(Pitch)) * perPixel;
        ClampTarget();
    }

    public void Turn(Vector2 pixels)
    {
        _turned -= pixels.X * 0.008f;
    }

    // ------------------------------------------------------------------ camera

    /// <summary>How far in we are, 0 (whole circuit) to 1 (nose to tail with the car).</summary>
    private float Closeness => Mathf.Log(_zoom) / Mathf.Log(MaxZoom);

    /// <summary>The camera looks steeply down on the whole circuit and comes down low up close.</summary>
    private float Pitch => Mathf.DegToRad(Mathf.Lerp(64f, 16f, Mathf.Pow(Closeness, 0.8f)));

    /// <summary>Distance from the camera to the point it looks at: at zoom 1 the circuit fills the area.</summary>
    private float Distance
    {
        get
        {
            var view = GetViewportRect().Size;
            if (Area.Size.X <= 0 || view.Y <= 0)
                return 400;
            var tan = Mathf.Tan(Mathf.DegToRad(Fov / 2));
            var wide = K * Aspect / (2 * tan * view.X / view.Y * Area.Size.X / view.X);
            var tall = K * Mathf.Sin(Pitch) / (2 * tan * Area.Size.Y / view.Y);
            return Mathf.Max(wide, tall) * 1.02f / _zoom;
        }
    }

    private void PlaceCamera(float ease)
    {
        var yaw = _yaw;
        var followed = FollowDriverId is null ? null : _cars.FirstOrDefault(c => c.Entry.Driver.Id == FollowDriverId);
        if (followed is not null && _track is not null)
        {
            _target = _target.Lerp(CarSpot(followed), Math.Min(1, ease * 2));
            // Close in, the camera swings round behind the car like a chase camera.
            if (_headings.TryGetValue(followed.Entry.Driver.Id, out var heading))
            {
                var behind = heading - Mathf.Pi / 2;
                var weight = Mathf.SmoothStep(0.25f, 0.6f, Closeness);
                yaw = Mathf.LerpAngle(_turned, behind + _turned, weight);
            }
        }
        else
            yaw = _turned;
        _yaw = Mathf.LerpAngle(_yaw, yaw, Math.Min(1, ease));
        var pitch = Pitch;
        var offset = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(_yaw) * Mathf.Cos(pitch)) * Distance;
        _camera.Position = _target + offset;
        _camera.LookAt(_target, Vector3.Up);
        // Clip planes follow the camera out: with a fixed near plane the depth buffer runs out of
        // precision when the whole circuit is fitted into a narrow area, and the ground, road and
        // water flicker through each other in stripes.
        var distance = Distance;
        _camera.Near = Mathf.Clamp(distance * 0.02f, 0.5f, 50);
        _camera.Far = Mathf.Max(3000, distance * 6);
        _sun.DirectionalShadowMaxDistance = Mathf.Clamp(Distance * 2.2f, 40, 900);
        // Haze in the distance, but a clear view of whatever the camera looks at.
        _environment.Environment.FogDensity = (0.06f + 0.12f * Rain) / Distance;
    }

    private void ClampTarget()
    {
        _target = new Vector3(Mathf.Clamp(_target.X, 0, K * Aspect), 0, Mathf.Clamp(_target.Z, 0, K));
    }

    private Vector3? Ground(Vector2 screenPoint)
    {
        var from = _camera.ProjectRayOrigin(screenPoint);
        var dir = _camera.ProjectRayNormal(screenPoint);
        if (dir.Y >= -0.01f)
            return null;
        return from + dir * (-from.Y / dir.Y);
    }

    // ------------------------------------------------------------------ every frame

    public override void _Process(double delta)
    {
        if (_track is null || !Visible)
            return;
        var dt = (float)delta;
        PlaceCars(dt);
        PlaceCamera(1 - Mathf.Exp(-dt * 6));
        PlaceLabels();
        _overlay.QueueRedraw();
    }

    private Vector3 World(Vector2 p) => new(p.X * K * Aspect, 0, p.Y * K);

    private Vector3 CarSpot(LiveCar car) => World(_track!.At(car.Distance));

    private void PlaceCars(float dt)
    {
        // Cars are always true to scale with the circuit; far out, a coloured dot marks each one.
        const float scale = 1;
        var roadWidth = 0.016f * K;
        _beams.ForEach(b => b.Visible = false);
        var beam = 0;
        for (var i = 0; i < _cars.Count && i < _carNodes.Count; i++)
        {
            var car = _cars[i];
            var (node, paint, colour) = _carNodes[i];
            var p = CarSpot(car);
            var ahead = World(_track!.At(car.Distance + 0.0015)) - p;
            var heading = ahead.LengthSquared() > 0 ? Mathf.Atan2(-ahead.Z, ahead.X) : 0;
            var id = car.Entry.Driver.Id;
            _headings[id] = _headings.TryGetValue(id, out var was) ? Mathf.LerpAngle(was, heading, Math.Min(1, dt * 8)) : heading;
            var forward = new Vector3(Mathf.Cos(_headings[id]), 0, -Mathf.Sin(_headings[id]));
            var left = new Vector3(forward.Z, 0, -forward.X);
            if (car.InPits)
                p += left * roadWidth * 1.6f;
            else if (car.Running)
                p += left * roadWidth * 0.22f * (i % 3 - 1);
            node.Position = p;
            node.Rotation = new Vector3(0, _headings[id], 0);
            node.Scale = Vector3.One * scale;
            var shade = car.Retirement is null ? colour : colour.Darkened(0.55f);
            paint.AlbedoColor = shade.Darkened(0.3f * Night);

            // At night the leading cars near the camera light the road ahead.
            if (Night > 0.2f && car.Running && !car.InPits && beam < 4 && p.DistanceTo(_target) < Distance * 1.5f)
            {
                if (beam == _beams.Count)
                {
                    var light = new SpotLight3D { SpotRange = 30, SpotAngle = 28, LightColor = new Color(1f, 0.92f, 0.7f), ShadowEnabled = false };
                    _world.AddChild(light);
                    _beams.Add(light);
                }
                var b = _beams[beam++];
                b.Visible = true;
                b.LightEnergy = 6 * Night;
                b.SpotRange = 30 * scale;
                b.Position = p + forward * 1.2f * scale + Vector3.Up * 0.6f * scale;
                b.LookAt(b.Position + forward * 10 + Vector3.Down * 1.6f, Vector3.Up);
            }
            // Dust thrown up behind a running car on a dry road, unseen in the dark.
            var dusty = car.Running && !car.InPits && Night < 0.5f && Rain < 0.3f;
            for (var k = 0; k < _dust[i].Length; k++)
            {
                var puff = _dust[i][k];
                puff.Visible = dusty;
                if (!dusty)
                    continue;
                puff.Position = p + (-forward * (1.5f + 0.9f * k) + Vector3.Up * (0.35f + 0.15f * k)) * scale;
                puff.Scale = Vector3.One * (0.8f + 0.5f * k) * scale;
            }
            _carLabelSpots[i] = p + Vector3.Up * 1.4f * scale;
            _carMarks[i] = (p, forward, shade.Darkened(0.3f * Night));
        }
        if (_models is not null)
            _models.LampGlow = Night;
    }

    /// <summary>
    /// A dot in the team colour over each car that is too small on screen to make out, so the
    /// race can be followed from the whole-track view without blowing the cars up.
    /// </summary>
    private void DrawCarMarks()
    {
        // Leader drawn last, on top.
        for (var i = Math.Min(_cars.Count, _carMarks.Length) - 1; i >= 0; i--)
        {
            var (at, forward, colour) = _carMarks[i];
            if (_camera.IsPositionBehind(at))
                continue;
            var spot = _camera.UnprojectPosition(at);
            var length = spot.DistanceTo(_camera.UnprojectPosition(at + forward * CarModel.Length));
            if (length >= 14)
                continue;
            // Fades out as the car grows big enough to be seen for itself.
            var fade = Mathf.Clamp((14 - length) / 6, 0, 1);
            _overlay.DrawCircle(spot, 5.5f, new Color(0, 0, 0, 0.75f * fade));
            _overlay.DrawCircle(spot, 4f, colour with { A = fade });
        }
    }

    private MeshInstance3D[] DustPuffs()
    {
        if (_puff is null)
        {
            _puff = new QuadMesh { Size = Vector2.One };
            var soft = new GradientTexture2D
            {
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
                Gradient = new Gradient { Colors = new[] { Colors.White, new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 1f } },
                Width = 64,
                Height = 64,
            };
            _dustMaterials = Enumerable.Range(0, 3).Select(k => new StandardMaterial3D
            {
                AlbedoColor = new Color(0.84f, 0.78f, 0.64f, 0.42f / (k + 1)),
                AlbedoTexture = soft,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            }).ToArray();
        }
        return _dustMaterials.Select(m =>
        {
            var puff = new MeshInstance3D { Mesh = _puff, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _world.AddChild(puff);
            return puff;
        }).ToArray();
    }

    private void PlaceLabels()
    {
        foreach (var (label, at) in _cornerLabels)
        {
            var spot = _camera.UnprojectPosition(at);
            label.Visible = !_camera.IsPositionBehind(at) && Area.Grow(Area.Size.X).HasPoint(spot) && GetViewportRect().HasPoint(spot);
            label.Position = spot - label.Size / 2;
        }
        if (_creditLabel is not null)
            _creditLabel.Position = Area.End - _creditLabel.Size - new Vector2(8, 4);
        // Leader first; a number that would touch a better-placed car's number is hidden.
        var placed = new List<Rect2>();
        for (var i = 0; i < _carLabels.Count; i++)
        {
            var label = _carLabels[i];
            var show = i < _cars.Count && i < _carLabelSpots.Length && !_camera.IsPositionBehind(_carLabelSpots[i]);
            var rect = show ? new Rect2(_camera.UnprojectPosition(_carLabelSpots[i]) - new Vector2(label.Size.X / 2, label.Size.Y), label.Size) : default;
            show = show && GetViewportRect().Grow(-4).Encloses(rect) && !placed.Any(r => r.Grow(1).Intersects(rect));
            label.Visible = show;
            if (!show)
                continue;
            label.Position = rect.Position;
            placed.Add(rect);
        }
    }

    private void Light()
    {
        if (_sun is null)
            return;
        // By night a pale moon: dim enough for the headlamps to tell, light enough to see the cars.
        var dim = 1 - 0.7f * _night;
        var wet = Rain;
        _sun.LightEnergy = (0.85f - 0.4f * wet) * dim * dim;
        _sun.LightColor = new Color(1f, 0.95f, 0.84f).Lerp(new Color(0.6f, 0.7f, 1f), _night);
        var env = _environment.Environment;
        env.AmbientLightColor = new Color(0.62f, 0.66f, 0.72f).Lerp(new Color(0.2f, 0.26f, 0.42f), _night);
        env.AmbientLightEnergy = (0.42f + 0.15f * wet) * Mathf.Lerp(1, 0.55f, _night);
        env.FogLightColor = new Color(0.72f, 0.72f, 0.66f).Lerp(new Color(0.5f, 0.54f, 0.6f), wet).Darkened(0.85f * _night);
        _sky.SkyTopColor = new Color(0.42f, 0.56f, 0.72f).Lerp(new Color(0.5f, 0.54f, 0.6f), wet).Darkened(0.88f * _night);
        _sky.SkyHorizonColor = new Color(0.78f, 0.76f, 0.68f).Lerp(new Color(0.62f, 0.64f, 0.66f), wet).Darkened(0.85f * _night);
    }

    private float _lastRain = -1;

    public override void _PhysicsProcess(double delta)
    {
        if (Mathf.Abs(Rain - _lastRain) > 0.005f)
        {
            _lastRain = Rain;
            Light();
        }
    }

    // ------------------------------------------------------------------ scenery

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Material material, Vector3 at)
    {
        var part = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at };
        parent.AddChild(part);
        return part;
    }

    /// <summary>A circuit without a painting: a grey ribbon of road on the grass.</summary>
    private void AddPlainRoad(TrackShape track)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.TriangleStrip);
        st.SetNormal(Vector3.Up);
        var half = 0.016f * K / 2;
        var n = track.Points.Count;
        for (var i = 0; i <= n; i++)
        {
            var a = World(track.Points[i % n]);
            var b = World(track.Points[(i + 1) % n]);
            var side = (b - a).Cross(Vector3.Up).Normalized() * half;
            st.AddVertex(a - side + Vector3.Up * 0.03f);
            st.AddVertex(a + side + Vector3.Up * 0.03f);
        }
        Add(_scenery!, st.Commit(), new StandardMaterial3D { AlbedoColor = new Color(0.24f, 0.24f, 0.25f), CullMode = BaseMaterial3D.CullModeEnum.Disabled }, Vector3.Zero);
    }

    /// <summary>The average colour round the border of the painting.</summary>
    private static Color EdgeColour(string circuitId)
    {
        var path = $"res://art/tracks/{circuitId}.jpg";
        if (!FileAccess.FileExists(path))
            return new Color(0.3f, 0.4f, 0.24f);
        var image = new Image();
        image.LoadJpgFromBuffer(FileAccess.GetFileAsBytes(path));
        image.Resize(64, 46);
        var sum = new Color(0, 0, 0, 0);
        var n = 0;
        for (var x = 0; x < 64; x++)
            for (var y = 0; y < 46; y++)
                if (x < 2 || y < 2 || x > 61 || y > 43)
                {
                    sum += image.GetPixel(x, y);
                    n++;
                }
        image.Dispose();
        return new Color(sum.R / n, sum.G / n, sum.B / n);
    }

    /// <summary>Share of trees that are tall and narrow, for a little variety in the woods.</summary>
    private const double Poplars = 0.12;

    private void LoadScenery(string circuitId)
    {
        var path = $"res://data/scenery/{circuitId}.json";
        if (!FileAccess.FileExists(path))
            return;
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
        float[][] Rows(string name) => doc.RootElement.TryGetProperty(name, out var e)
            ? e.EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetSingle()).ToArray()).ToArray()
            : Array.Empty<float[]>();

        // Trees: a round crown on a short trunk; the painted crown stays underneath as its shade.
        var trees = Rows("trees");
        var crown = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 8, Rings = 5 };
        var trunk = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.18f, Height = 1, RadialSegments = 5, Rings = 1 };
        var rng = new Random(circuitId.Sum(ch => ch));
        var crowns = new List<(Transform3D, Color)>();
        var poplars = new List<(Transform3D, Color)>();
        var trunks = new List<(Transform3D, Color)>();
        var dark = new Color(42 / 255f, 64 / 255f, 34 / 255f);
        var light = new Color(88 / 255f, 118 / 255f, 60 / 255f);
        foreach (var t in trees)
        {
            var at = World(new Vector2(t[0], t[1]));
            var r = t[2] * K * 1.25f;
            var tall = 1.1f + (float)rng.NextDouble() * 0.5f;
            var height = r * (1.2f + (float)rng.NextDouble() * 0.6f);
            var basis = Basis.Identity.Rotated(Vector3.Up, (float)rng.NextDouble() * Mathf.Tau);
            var leaf = dark.Lerp(light, t[3] * 0.6f).Lightened(0.15f);
            // Here and there a tall, narrow poplar or cypress.
            if (rng.NextDouble() < Poplars)
            {
                var spire = r * (3.2f + (float)rng.NextDouble());
                poplars.Add((new Transform3D(basis.Scaled(new Vector3(r * 0.55f, spire, r * 0.55f)), at + Vector3.Up * (r * 0.4f + spire / 2)), leaf.Darkened(0.12f)));
                continue;
            }
            crowns.Add((new Transform3D(basis.Scaled(new Vector3(r, r * tall, r)), at + Vector3.Up * (height + r * tall * 0.8f)), leaf));
            trunks.Add((new Transform3D(Basis.Identity.Scaled(new Vector3(r, height + r * 0.5f, r)), at + Vector3.Up * (height + r * 0.5f) / 2),
                new Color(0.3f, 0.24f, 0.18f)));
        }
        AddMany(crown, crowns, 0.95f);
        AddMany(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 1, Height = 1, RadialSegments = 7, Rings = 1 }, poplars, 0.95f);
        AddMany(trunk, trunks, 1);

        // Houses: pale walls under the roofs painted in the picture; the ridge along the long side.
        var walls = new List<(Transform3D, Color)>();
        var roofs = new List<(Transform3D, Color)>();
        foreach (var h in Rows("houses"))
        {
            var corner = World(new Vector2(h[0], h[1]));
            var size = new Vector3(h[2] * K * Aspect, 0, h[3] * K);
            var centre = corner + size / 2;
            var shortSide = Mathf.Min(size.X, size.Z);
            var wallHeight = shortSide * (0.6f + (float)rng.NextDouble() * 0.5f);
            walls.Add((new Transform3D(Basis.Identity.Scaled(new Vector3(size.X, wallHeight, size.Z)), centre + Vector3.Up * wallHeight / 2),
                new Color(0.86f, 0.82f, 0.72f).Darkened((float)rng.NextDouble() * 0.15f)));
            // PrismMesh: the ridge runs along Z; turn it when the house is longer across.
            var roofBasis = size.X > size.Z
                ? new Basis(Vector3.Up, Mathf.Pi / 2).Scaled(new Vector3(size.Z, shortSide * 0.5f, size.X))
                : Basis.Identity.Scaled(new Vector3(size.X, shortSide * 0.5f, size.Z));
            roofs.Add((new Transform3D(roofBasis, centre + Vector3.Up * (wallHeight + shortSide * 0.25f)),
                new Color(h[4] / 255f, h[5] / 255f, h[6] / 255f)));
        }
        AddMany(new BoxMesh(), walls, 0.9f);
        AddMany(new PrismMesh(), roofs, 0.8f);

        // Boats: white hulls with a varnished cabin.
        var hulls = new List<(Transform3D, Color)>();
        foreach (var b in Rows("boats"))
        {
            var length = b[2] * K * Aspect;
            var beam = b[3] * K;
            var at = World(new Vector2(b[0], b[1])) + new Vector3(length / 2, 0.2f, 0);
            hulls.Add((new Transform3D(Basis.Identity.Scaled(new Vector3(length, 0.6f, beam)), at), new Color(0.93f, 0.91f, 0.86f)));
            hulls.Add((new Transform3D(Basis.Identity.Scaled(new Vector3(length * 0.3f, 0.6f, beam * 0.5f)), at + new Vector3(-length * 0.1f, 0.5f, 0)), new Color(0.6f, 0.44f, 0.3f)));
        }
        AddMany(new BoxMesh(), hulls, 0.6f);

        // Straw bales on the outside of the corners.
        var bales = new List<(Transform3D, Color)>();
        foreach (var b in Rows("bales"))
            bales.Add((new Transform3D(new Basis(Vector3.Up, -b[2]).Scaled(new Vector3(0.9f, 0.45f, 0.55f)), World(new Vector2(b[0], b[1])) + Vector3.Up * 0.22f),
                new Color(0.84f, 0.73f, 0.42f)));
        AddMany(new BoxMesh(), bales, 1);
    }

    /// <summary>Wooden grandstands: raked seating under a roof on posts.</summary>
    private void AddGrandstands(TrackShape track)
    {
        var parts = new List<(Transform3D, Color)>();
        var wood = new Color(0.47f, 0.34f, 0.23f);
        var roof = new Color(0.38f, 0.27f, 0.19f);
        foreach (var s in track.Grandstands)
        {
            var corner = World(s.Position);
            var size = new Vector3(s.Size.X * K * Aspect, 0, s.Size.Y * K);
            var centre = corner + size / 2;
            parts.Add((new Transform3D(Basis.Identity.Scaled(new Vector3(size.X, 2.4f, size.Z)), centre + Vector3.Up * 1.2f), wood));
            parts.Add((new Transform3D(Basis.Identity.Scaled(new Vector3(size.X * 1.04f, 0.25f, size.Z * 1.1f)), centre + Vector3.Up * 4.2f), roof));
        }
        AddMany(new BoxMesh(), parts, 0.9f);
    }

    private void AddMany(Mesh mesh, List<(Transform3D Transform, Color Colour)> items, float roughness)
    {
        if (items.Count == 0)
            return;
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = items.Count };
        for (var i = 0; i < items.Count; i++)
        {
            multi.SetInstanceTransform(i, items[i].Transform);
            multi.SetInstanceColor(i, items[i].Colour);
        }
        _scenery!.AddChild(new MultiMeshInstance3D
        {
            Multimesh = multi,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = roughness },
        });
    }

    private Label TextLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(label);
        return label;
    }
}
