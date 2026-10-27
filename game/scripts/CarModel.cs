using System;
using System.Collections.Generic;
using Godot;

namespace RacingManager;

/// <summary>
/// The 3D race car, built from code so nobody has to model it: a cigar-shaped single-seater
/// of the early thirties with a blunt radiator, a pointed tail, wire wheels standing clear of
/// the body and the driver's head above the cockpit. It points along +X and sits on y = 0;
/// one unit is about 1.4 metres, so the car is 2.6 units long.
/// The shared meshes are made once per view (Godot objects in static C# fields crash at quit).
/// </summary>
public sealed class CarModel
{
    public const float Length = 2.6f;

    private readonly ArrayMesh _body = Body();
    private readonly CylinderMesh _wheel = new() { TopRadius = 0.27f, BottomRadius = 0.27f, Height = 0.13f, RadialSegments = 14, Rings = 1 };
    private readonly CylinderMesh _hub = new() { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.14f, RadialSegments = 10, Rings = 1 };
    private readonly CylinderMesh _rod = new() { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 1, RadialSegments = 6, Rings = 1 };
    private readonly SphereMesh _head = new() { Radius = 0.12f, Height = 0.24f, RadialSegments = 10, Rings = 6 };
    private readonly SphereMesh _cockpit = new() { Radius = 0.22f, Height = 0.12f, RadialSegments = 12, Rings = 4 };
    private readonly BoxMesh _grille = new() { Size = new Vector3(0.04f, 0.32f, 0.34f) };
    private readonly CylinderMesh _roundel = new() { TopRadius = 0.13f, BottomRadius = 0.13f, Height = 0.02f, RadialSegments = 14, Rings = 1 };
    private readonly SphereMesh _wing = new() { Radius = 0.22f, Height = 0.44f, RadialSegments = 12, Rings = 6, IsHemisphere = true };
    private readonly BoxMesh _screen = new() { Size = new Vector3(0.03f, 0.2f, 0.62f) };
    private readonly SphereMesh _lamp = new() { Radius = 0.08f, Height = 0.16f, RadialSegments = 8, Rings = 4 };

    private readonly StandardMaterial3D _tyre = Matte(new Color(0.07f, 0.065f, 0.06f), 0.9f);
    private readonly StandardMaterial3D _wire = Shiny(new Color(0.72f, 0.7f, 0.66f), 0.6f);
    private readonly StandardMaterial3D _leather = Matte(new Color(0.36f, 0.22f, 0.13f), 0.7f);
    private readonly StandardMaterial3D _dark = Matte(new Color(0.09f, 0.08f, 0.07f), 0.8f);
    private readonly StandardMaterial3D _chrome = Shiny(new Color(0.8f, 0.78f, 0.74f), 0.25f);
    private readonly StandardMaterial3D _white = Matte(new Color(0.94f, 0.92f, 0.86f), 0.6f);
    private readonly StandardMaterial3D _glass = new()
    {
        AlbedoColor = new Color(0.8f, 0.88f, 0.92f, 0.35f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.1f,
    };
    private readonly StandardMaterial3D _lampGlass = new()
    {
        AlbedoColor = new Color(1f, 0.95f, 0.75f),
        EmissionEnabled = true,
        Emission = new Color(1f, 0.9f, 0.6f),
        EmissionEnergyMultiplier = 0,
    };

    /// <summary>Headlamps glow from 0 (off) to 1 (full beam, at night).</summary>
    public float LampGlow
    {
        set => _lampGlass.EmissionEnergyMultiplier = 4 * value;
    }

    /// <summary>
    /// A car painted in this colour; a sports car (for Le Mans) gets wings over the wheels, a
    /// wider cockpit for two and a windscreen. The returned material is the paint, for darkening later.
    /// </summary>
    public (Node3D Car, StandardMaterial3D Paint) Build(Color colour, bool sports = false)
    {
        var car = new Node3D();
        var paint = Shiny(colour, 0.38f);
        Add(car, _body, paint, Vector3.Zero);

        // Wheels stand clear of the body on both sides, joined by axles.
        foreach (var x in new[] { 0.98f, -0.86f })
        {
            foreach (var side in new[] { -1, 1 })
            {
                var at = new Vector3(x, 0.27f, side * 0.5f);
                Add(car, _wheel, _tyre, at, new Vector3(Mathf.Pi / 2, 0, 0));
                Add(car, _hub, _wire, at, new Vector3(Mathf.Pi / 2, 0, 0));
            }
            Add(car, _rod, _dark, new Vector3(x, 0.27f, 0), new Vector3(Mathf.Pi / 2, 0, 0));
        }

        // Radiator, cockpit opening, the driver's leather helmet, the exhaust down the side.
        Add(car, _grille, _chrome, new Vector3(1.24f, 0.4f, 0));
        Add(car, _cockpit, _dark, new Vector3(-0.2f, 0.62f, 0), scale: new Vector3(1.5f, 1, 1));
        Add(car, _head, _leather, new Vector3(-0.3f, 0.78f, 0));
        var exhaust = Add(car, _rod, _chrome, new Vector3(-0.1f, 0.36f, 0.34f), new Vector3(0, 0, Mathf.Pi / 2), new Vector3(1.4f, 1.6f, 1.4f));
        exhaust.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        // White number roundels on the tail.
        Add(car, _roundel, _white, new Vector3(-0.85f, 0.42f, 0.25f), new Vector3(Mathf.Pi / 2 - 0.3f, 0, 0));
        Add(car, _roundel, _white, new Vector3(-0.85f, 0.42f, -0.25f), new Vector3(-Mathf.Pi / 2 + 0.3f, 0, 0));
        if (sports)
        {
            foreach (var x in new[] { 0.98f, -0.86f })
                foreach (var side in new[] { -1, 1 })
                    Add(car, _wing, paint, new Vector3(x, 0.5f, side * 0.5f), scale: new Vector3(1.6f, 0.5f, 0.75f));
            Add(car, _cockpit, _dark, new Vector3(-0.2f, 0.62f, 0), scale: new Vector3(1.3f, 1, 1.9f));
            Add(car, _screen, _glass, new Vector3(0.12f, 0.78f, 0));
        }
        // Headlamps for the 24-hour race; dark by day.
        Add(car, _lamp, _lampGlass, new Vector3(1.18f, 0.5f, 0.3f));
        Add(car, _lamp, _lampGlass, new Vector3(1.18f, 0.5f, -0.3f));
        return (car, paint);
    }

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Material material, Vector3 at, Vector3? rotation = null, Vector3? scale = null)
    {
        var part = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at, Rotation = rotation ?? Vector3.Zero, Scale = scale ?? Vector3.One };
        parent.AddChild(part);
        return part;
    }

    private static StandardMaterial3D Matte(Color colour, float roughness) => new() { AlbedoColor = colour, Roughness = roughness };

    private static StandardMaterial3D Shiny(Color colour, float roughness) => new() { AlbedoColor = colour, Roughness = roughness, Metallic = 0.15f, MetallicSpecular = 0.7f };

    /// <summary>
    /// The body as a lathe: stations from the pointed tail to the radiator, each an ellipse a
    /// little taller than wide, flattened underneath.
    /// </summary>
    private static ArrayMesh Body()
    {
        // (x along the car, half-width, half-height, centre height)
        var stations = new (float X, float W, float H, float Y)[]
        {
            (-1.32f, 0.01f, 0.01f, 0.5f),
            (-1.2f, 0.09f, 0.11f, 0.48f),
            (-0.95f, 0.2f, 0.22f, 0.45f),
            (-0.6f, 0.28f, 0.28f, 0.43f),
            (-0.25f, 0.31f, 0.29f, 0.42f),
            (0.2f, 0.3f, 0.27f, 0.42f),
            (0.7f, 0.25f, 0.24f, 0.42f),
            (1.1f, 0.21f, 0.21f, 0.42f),
            (1.24f, 0.19f, 0.19f, 0.42f),
            (1.25f, 0.0f, 0.0f, 0.42f),
        };
        const int around = 16;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var ring = new List<Vector3[]>();
        foreach (var (x, w, h, y) in stations)
        {
            var points = new Vector3[around];
            for (var j = 0; j < around; j++)
            {
                var a = j * Mathf.Tau / around;
                var up = Mathf.Sin(a);
                points[j] = new Vector3(x, y + h * up * (up < 0 ? 0.7f : 1), w * Mathf.Cos(a));
            }
            ring.Add(points);
        }
        for (var i = 0; i + 1 < ring.Count; i++)
        {
            for (var j = 0; j < around; j++)
            {
                var k = (j + 1) % around;
                Vector3 a = ring[i][j], b = ring[i][k], c = ring[i + 1][j], d = ring[i + 1][k];
                // Wound so the outside faces outwards (Godot culls clockwise-from-outside back faces).
                st.AddVertex(a); st.AddVertex(c); st.AddVertex(b);
                st.AddVertex(b); st.AddVertex(c); st.AddVertex(d);
            }
        }
        st.Index();
        st.GenerateNormals();
        return st.Commit();
    }
}
