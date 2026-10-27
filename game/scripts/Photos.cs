using System.Collections.Generic;
using Godot;

namespace RacingManager;

/// <summary>
/// The pictures on the season screen: period photos of drivers, staff, cars and circuits
/// (game/art/photos, picked from Wikimedia Commons by tools/photos; credits in credits.json),
/// and the drawing office's blueprints of each part (game/art/parts, drawn by tools/part-art).
/// Pictures are read from the files once per screen and kept here.
/// </summary>
public partial class CalendarScreen
{
    // Per screen, not static: Godot objects held statically crash the engine at quit.
    private readonly Dictionary<string, Texture2D?> _pictures = new();

    private Texture2D? Picture(string path)
    {
        if (!_pictures.TryGetValue(path, out var texture))
            _pictures[path] = texture = TrackView.LoadTexture(path);
        return texture;
    }

    /// <summary>A photo in a white print border; the first of the paths that exists, so there is always a fallback.</summary>
    private Control Photo(Vector2 size, params string[] paths)
    {
        var mount = new PanelContainer { ThemeTypeVariation = "Mount", SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        Texture2D? texture = null;
        foreach (var path in paths)
            if ((texture = Picture(path)) is not null)
                break;
        mount.AddChild(new TextureRect
        {
            Texture = texture, CustomMinimumSize = size, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore,
        });
        return mount;
    }

    private Control DriverPhoto(string driverId, Vector2 size) =>
        Photo(size, $"res://art/photos/drivers/{driverId}.jpg", "res://art/photos/unknown-driver.jpg");

    private Control StaffPhoto(string? staffId, Vector2 size) =>
        Photo(size, $"res://art/photos/staff/{staffId}.jpg", "res://art/photos/unknown-staff.jpg");

    /// <summary>A circuit's photo, or failing that a corner of its painted map.</summary>
    private Control CircuitPhoto(string circuitId, Vector2 size) =>
        Photo(size, $"res://art/photos/circuits/{circuitId}.jpg", $"res://art/tracks/{circuitId}.jpg");

    /// <summary>A car's photo by its name (altered brand and all), or the team's maker in general.</summary>
    private Control CarPhoto(string carName, string teamId, Vector2 size) =>
        Photo(size, $"res://art/photos/cars/{Slug(carName)}.jpg", $"res://art/photos/cars/{teamId}.jpg", "res://art/photos/unknown-car.jpg");

    /// <summary>The blueprint of one part, e.g. ("engine", "Straight8").</summary>
    private Texture2D? Blueprint(string group, string choice) => Picture($"res://art/parts/{group}-{choice.ToLowerInvariant()}.png");

    private Control CreditsCard()
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(new Label { Text = "PICTURES", ThemeTypeVariation = "Overline" });
        box.AddChild(new Label
        {
            Text = "Period photographs from Wikimedia Commons, free to use under their licences. Blueprints and circuit paintings are drawn by the game.",
            ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var show = new Button { Text = _view == View.Credits ? "‹ Back to the teams" : "Picture credits" };
        show.Pressed += () =>
        {
            _view = _view == View.Credits ? View.Calendar : View.Credits;
            Refresh();
        };
        box.AddChild(show);
        card.AddChild(box);
        return card;
    }

    /// <summary>Who took each photo and under which licence, from game/data/photo-credits.json (written by tools/photos/pick.py).</summary>
    private void CreditsView()
    {
        _body.AddChild(new Label { Text = "PICTURE CREDITS", ThemeTypeVariation = "Section" });
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        card.AddChild(box);
        const string path = "res://data/photo-credits.json";
        var credits = FileAccess.FileExists(path) ? Json.ParseString(FileAccess.GetFileAsString(path)).AsGodotArray() : new Godot.Collections.Array();
        if (credits.Count == 0)
            box.AddChild(new Label { Text = "No photographs yet.", ThemeTypeVariation = "Muted" });
        foreach (var item in credits)
        {
            var c = item.AsGodotDictionary();
            box.AddChild(new Label { Text = c["title"].AsString(), ThemeTypeVariation = "Strong", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            box.AddChild(new Label
            {
                Text = $"{c["author"].AsString()} · {c["licence"].AsString()} · {c["source"].AsString()}",
                ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }
        box.AddChild(new HSeparator());
        box.AddChild(new Label
        {
            Text = "Fonts: Playfair Display (Claus Eggers Sørensen), Oswald (Vernon Adams), Cormorant Garamond (Christian Thalmann), Architects Daughter (Kimberly Geswein), all under the SIL Open Font License. Circuit outlines © OpenStreetMap contributors (ODbL).",
            ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _body.AddChild(card);
    }

    private static string Slug(string name)
    {
        var chars = new System.Text.StringBuilder();
        foreach (var c in name.ToLowerInvariant())
            chars.Append(char.IsLetterOrDigit(c) ? c : '-');
        return System.Text.RegularExpressions.Regex.Replace(chars.ToString(), "-+", "-").Trim('-');
    }
}
