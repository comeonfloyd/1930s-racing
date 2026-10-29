using Godot;

namespace RacingManager;

/// <summary>
/// The game's looks, built in code. <see cref="Get"/> is the race screen's: dark translucent
/// cards over the art, rounded buttons and a warm gold accent. <see cref="Programme"/> is the
/// menus': a 1930s race programme, with cream paper cards on a dark brown ground, Playfair
/// Display headings, Oswald figures, Cormorant Garamond text, and red and gold.
/// Type variations: Card, CardTight (panels), Accent and Danger (buttons), Segment (toggle
/// buttons that sit side by side), Muted and Title (labels), Badge (a small panel). The
/// programme adds Section, OnDark and Note (labels on the dark ground), Mount (a photo's white
/// border), Ticket (the date and money box) and Tab (the screen's tabs).
/// </summary>
public static class UiTheme
{
    public static readonly Color Background = new("0e1116");
    public static readonly Color Panel = new(0.07f, 0.085f, 0.11f, 0.9f);
    public static readonly Color PanelLight = new(0.13f, 0.15f, 0.19f, 0.95f);
    public static readonly Color Line = new(1, 1, 1, 0.08f);
    public static readonly Color Text = new("eceae4");
    public static readonly Color TextMuted = new("9aa0a8");
    public static readonly Color Accent = new("e8b04a");
    public static readonly Color AccentDark = new("b9872e");
    public static readonly Color Danger = new("d9503f");
    public static readonly Color Good = new("6cc47a");

    // The programme's colours.
    public static readonly Color Ground = new("17120d");
    public static readonly Color Paper = new("efe5cf");
    public static readonly Color PaperDark = new("e0d2b2");
    public static readonly Color PhotoWhite = new("fbf6ea");
    public static readonly Color Ink = new("2a2118");
    public static readonly Color InkMuted = new("6b5b45");
    public static readonly Color Red = new("9e2b25");
    public static readonly Color Gold = new("c9a14a");
    public static readonly Color GoodInk = new("3d6b2f");
    public static readonly Color Cream = new("efe5cf");

    /// <summary>
    /// A fresh theme for a screen. Not cached in a static field: Godot objects held statically
    /// outlive the engine at quit and crash it ("!rc_owner").
    /// </summary>
    public static Theme Get() => Build();

    private static Theme Build()
    {
        var t = new Theme { DefaultFontSize = 15 };

        // Panels
        t.SetStylebox("panel", "PanelContainer", Box(Panel, 12, 16, border: Line));
        t.SetTypeVariation("Card", "PanelContainer");
        t.SetStylebox("panel", "Card", Box(Panel, 12, 16, border: Line));
        t.SetTypeVariation("CardTight", "PanelContainer");
        t.SetStylebox("panel", "CardTight", Box(Panel, 10, 8, border: Line));
        t.SetTypeVariation("Badge", "PanelContainer");
        t.SetStylebox("panel", "Badge", Box(PanelLight, 6, 4));
        t.SetStylebox("panel", "TooltipPanel", Box(PanelLight, 6, 8, border: Line));

        // Labels
        t.SetColor("font_color", "Label", Text);
        t.SetTypeVariation("Muted", "Label");
        t.SetColor("font_color", "Muted", TextMuted);
        t.SetFontSize("font_size", "Muted", 13);
        t.SetTypeVariation("Title", "Label");
        t.SetFontSize("font_size", "Title", 30);
        t.SetColor("font_color", "Title", Text);
        t.SetTypeVariation("Heading", "Label");
        t.SetFontSize("font_size", "Heading", 19);
        t.SetColor("font_color", "Heading", Text);
        t.SetTypeVariation("Overline", "Label");
        t.SetFontSize("font_size", "Overline", 12);
        t.SetColor("font_color", "Overline", Accent);
        t.SetTypeVariation("Big", "Label");
        t.SetFontSize("font_size", "Big", 24);
        t.SetColor("font_color", "Big", Text);

        // Buttons: flat, rounded, a gold edge on focus, and a clear pressed state for toggles.
        StyleButton(t, "Button", PanelLight, new Color(0.19f, 0.21f, 0.26f), new Color(0.1f, 0.11f, 0.14f), Text);
        t.SetTypeVariation("Accent", "Button");
        StyleButton(t, "Accent", Accent, Accent.Lightened(0.15f), AccentDark, new Color("1a1408"));
        t.SetTypeVariation("Danger", "Button");
        StyleButton(t, "Danger", Danger, Danger.Lightened(0.15f), Danger.Darkened(0.2f), Colors.White);
        t.SetTypeVariation("Segment", "Button");
        StyleButton(t, "Segment", new Color(0.1f, 0.115f, 0.15f), new Color(0.17f, 0.19f, 0.24f), Accent, Text, radius: 6, pad: 8);
        t.SetColor("font_pressed_color", "Segment", new Color("1a1408"));

        t.SetStylebox("normal", "OptionButton", Box(PanelLight, 8, 10));
        t.SetStylebox("hover", "OptionButton", Box(new Color(0.19f, 0.21f, 0.26f), 8, 10));
        t.SetStylebox("pressed", "OptionButton", Box(new Color(0.1f, 0.11f, 0.14f), 8, 10));
        t.SetStylebox("focus", "OptionButton", Box(Colors.Transparent, 8, 10, border: Accent));
        t.SetColor("font_color", "OptionButton", Text);
        t.SetStylebox("panel", "PopupMenu", Box(PanelLight, 8, 6, border: Line));
        t.SetStylebox("hover", "PopupMenu", Box(new Color(Accent, 0.25f), 6, 4));
        t.SetColor("font_color", "PopupMenu", Text);
        t.SetColor("font_hover_color", "PopupMenu", Text);

        // Bars for fuel and tyres.
        t.SetStylebox("background", "ProgressBar", Box(new Color(1, 1, 1, 0.08f), 4, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Good, 4, 0));
        t.SetColor("font_color", "ProgressBar", Colors.Transparent);
        t.SetFontSize("font_size", "ProgressBar", 1);

        // Scrollbars: thin and quiet.
        t.SetStylebox("scroll", "VScrollBar", Box(new Color(1, 1, 1, 0.06f), 3, 0));
        t.SetStylebox("grabber", "VScrollBar", Box(new Color(1, 1, 1, 0.3f), 3, 0));
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(new Color(1, 1, 1, 0.3f), 3, 0));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(Accent, 3, 0));

        t.SetStylebox("separator", "HSeparator", Line1());
        t.SetConstant("separation", "HSeparator", 10);
        return t;
    }

    private static void StyleButton(Theme t, string type, Color normal, Color hover, Color pressed, Color text, int radius = 8, int pad = 14,
        Color? border = null, Font? font = null)
    {
        t.SetStylebox("normal", type, Box(normal, radius, pad, vertical: 7, border: border));
        t.SetStylebox("hover", type, Box(hover, radius, pad, vertical: 7, border: border));
        t.SetStylebox("pressed", type, Box(pressed, radius, pad, vertical: 7, border: border));
        t.SetStylebox("hover_pressed", type, Box(pressed.Lightened(0.1f), radius, pad, vertical: 7, border: border));
        t.SetStylebox("disabled", type, Box(new Color(normal, normal.A * 0.35f), radius, pad, vertical: 7, border: border is { } b ? new Color(b, b.A * 0.35f) : null));
        t.SetStylebox("focus", type, Box(Colors.Transparent, radius, pad, vertical: 7, border: new Color(border is null ? Accent : Red, 0.8f)));
        if (font is not null)
            t.SetFont("font", type, font);
        t.SetColor("font_color", type, text);
        t.SetColor("font_hover_color", type, text);
        t.SetColor("font_pressed_color", type, text);
        t.SetColor("font_hover_pressed_color", type, text);
        t.SetColor("font_focus_color", type, text);
        t.SetColor("font_disabled_color", type, new Color(text, 0.4f));
    }

    /// <summary>A rounded, filled box with even padding and an optional 1 px border.</summary>
    public static StyleBoxFlat Box(Color fill, int radius, int pad, int vertical = -1, Color? border = null, int width = 1)
    {
        var box = new StyleBoxFlat { BgColor = fill, AntiAliasing = true };
        box.SetCornerRadiusAll(radius);
        box.ContentMarginLeft = box.ContentMarginRight = pad;
        box.ContentMarginTop = box.ContentMarginBottom = vertical < 0 ? pad : vertical;
        if (border is { } b)
        {
            box.BorderColor = b;
            box.SetBorderWidthAll(width);
        }
        return box;
    }

    /// <summary>Colours a label's text; returns it, so it can sit inside an AddChild call.</summary>
    public static Label Tint(this Label label, Color? color)
    {
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        return label;
    }

    /// <summary>
    /// The menus' theme. Fonts are read straight from the files (like the art) and built fresh
    /// for each screen: never keep a Font in a static field, it crashes Godot at quit.
    /// </summary>
    public static Theme Programme()
    {
        var text = Variation(LoadFont("CormorantGaramond.ttf"), 600);
        var textBold = Variation(LoadFont("CormorantGaramond.ttf"), 700);
        var display = Variation(LoadFont("PlayfairDisplay.ttf"), 700);
        var italic = Variation(LoadFont("PlayfairDisplay.ttf"), 400);
        var caps = Variation(LoadFont("PlayfairDisplaySC-Bold.ttf"), null, spacing: 2);
        var capsWide = Variation(LoadFont("PlayfairDisplaySC-Bold.ttf"), null, spacing: 3);
        var figures = Variation(LoadFont("Oswald.ttf"), 500, spacing: 1);

        var t = new Theme { DefaultFontSize = 17, DefaultFont = text };
        Color shadow = new(0, 0, 0, 0.45f);

        // Paper cards, square cut like a printed programme, with a soft drop shadow.
        foreach (var (type, pad) in new[] { ("PanelContainer", 16), ("Card", 16), ("CardTight", 10) })
        {
            if (type != "PanelContainer")
                t.SetTypeVariation(type, "PanelContainer");
            t.SetStylebox("panel", type, PaperBox(Paper, pad, shadow));
        }
        t.SetTypeVariation("Badge", "PanelContainer");
        t.SetStylebox("panel", "Badge", Box(PaperDark, 0, 6, vertical: 2));
        t.SetTypeVariation("Mount", "PanelContainer");
        t.SetStylebox("panel", "Mount", PaperBox(PhotoWhite, 4, new Color(0, 0, 0, 0.35f)));
        t.SetTypeVariation("Ticket", "PanelContainer");
        t.SetStylebox("panel", "Ticket", Box(Colors.Transparent, 0, 14, vertical: 8, border: new Color(Gold, 0.5f)));
        t.SetStylebox("panel", "TooltipPanel", Box(Paper, 0, 8, border: new Color(Ink, 0.3f)));
        t.SetColor("font_color", "TooltipLabel", Ink);
        t.SetFont("font", "TooltipLabel", text);

        // Labels: ink on paper by default; Section, OnDark and Note for the dark ground.
        t.SetColor("font_color", "Label", Ink);
        SetLabel("Muted", InkMuted, 16, null);
        SetLabel("Heading", Ink, 20, display);
        SetLabel("Big", Ink, 28, figures);
        SetLabel("Overline", Red, 12, capsWide);
        SetLabel("Title", Cream, 34, display);
        SetLabel("Subtitle", Gold, 34, italic);
        SetLabel("Section", Gold, 12, capsWide);
        SetLabel("OnDark", Cream, 17, null);
        SetLabel("Note", new Color(Cream, 0.7f), 16, null);
        SetLabel("Figure", Cream, 17, figures);
        SetLabel("Strong", Ink, 17, textBold);

        // Buttons: ink-ruled on the paper, red for the main action, small capitals throughout.
        StyleButton(t, "Button", Colors.Transparent, new Color(Ink, 0.08f), new Color(Ink, 0.16f), Ink, radius: 0, pad: 12, border: new Color(Ink, 0.7f), font: caps);
        t.SetTypeVariation("Accent", "Button");
        StyleButton(t, "Accent", Red, Red.Lightened(0.12f), Red.Darkened(0.2f), PhotoWhite, radius: 0, pad: 14, border: Gold, font: caps);
        t.SetTypeVariation("Danger", "Button");
        StyleButton(t, "Danger", Ink, Ink.Lightened(0.15f), Ink.Darkened(0.2f), PhotoWhite, radius: 0, pad: 12, font: caps);
        // Tabs: lettering on the dark ground, underlined in red when chosen.
        t.SetTypeVariation("Tab", "Button");
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            var box = Box(Colors.Transparent, 0, 0, vertical: 4);
            box.ContentMarginRight = 26;
            if (state is "pressed" or "hover_pressed")
            {
                box.BorderColor = Red;
                box.BorderWidthBottom = 2;
                box.ExpandMarginRight = -26;
            }
            t.SetStylebox(state, "Tab", box);
        }
        t.SetFont("font", "Tab", capsWide);
        t.SetFontSize("font_size", "Tab", 15);
        t.SetColor("font_color", "Tab", new Color("a89a80"));
        t.SetColor("font_hover_color", "Tab", Cream);
        t.SetColor("font_pressed_color", "Tab", Cream);
        t.SetColor("font_hover_pressed_color", "Tab", Cream);
        t.SetColor("font_focus_color", "Tab", new Color("a89a80"));
        // The "Segment" toggles on paper (e.g. the race view's map switch is not here, only menus).
        t.SetTypeVariation("Segment", "Button");
        StyleButton(t, "Segment", Colors.Transparent, new Color(Ink, 0.08f), Ink, Ink, radius: 0, pad: 8, border: new Color(Ink, 0.5f), font: caps);
        t.SetColor("font_pressed_color", "Segment", Paper);
        // A part on the drawing board: a plain frame that turns red when chosen.
        t.SetTypeVariation("Part", "Button");
        StyleButton(t, "Part", Paper, Paper.Darkened(0.04f), Paper, Ink, radius: 0, pad: 4, border: new Color(Ink, 0.2f), font: caps);
        t.SetStylebox("pressed", "Part", Box(Paper, 0, 4, vertical: 4, border: Red, width: 2));
        t.SetStylebox("hover_pressed", "Part", Box(Paper, 0, 4, vertical: 4, border: Red, width: 2));
        t.SetStylebox("disabled", "Part", Box(PaperDark, 0, 4, vertical: 4, border: new Color(Ink, 0.1f)));
        t.SetFontSize("font_size", "Part", 12);

        foreach (var type in new[] { "Button", "Accent", "Danger", "Segment" })
            t.SetFontSize("font_size", type, 14);

        t.SetStylebox("normal", "OptionButton", Box(new Color(Ink, 0.05f), 0, 10, border: new Color(Ink, 0.5f)));
        t.SetStylebox("hover", "OptionButton", Box(new Color(Ink, 0.1f), 0, 10, border: new Color(Ink, 0.6f)));
        t.SetStylebox("pressed", "OptionButton", Box(new Color(Ink, 0.15f), 0, 10, border: new Color(Ink, 0.6f)));
        t.SetStylebox("focus", "OptionButton", Box(Colors.Transparent, 0, 10, border: Red));
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
        {
            t.SetColor(c, "OptionButton", Ink);
            t.SetColor(c, "CheckButton", Ink);
        }
        // Text fields look like the drop-downs: ink on paper, a red rule when typing.
        t.SetStylebox("normal", "LineEdit", Box(new Color(Ink, 0.05f), 0, 10, border: new Color(Ink, 0.5f)));
        t.SetStylebox("focus", "LineEdit", Box(Colors.Transparent, 0, 10, border: Red));
        t.SetColor("font_color", "LineEdit", Ink);
        t.SetColor("font_placeholder_color", "LineEdit", new Color(Ink, 0.45f));
        t.SetColor("caret_color", "LineEdit", Red);
        t.SetColor("selection_color", "LineEdit", new Color(Red, 0.25f));
        t.SetStylebox("panel", "PopupMenu", Box(Paper, 0, 6, border: new Color(Ink, 0.4f)));
        t.SetStylebox("hover", "PopupMenu", Box(new Color(Red, 0.2f), 0, 4));
        t.SetColor("font_color", "PopupMenu", Ink);
        t.SetColor("font_hover_color", "PopupMenu", Ink);
        t.SetColor("font_disabled_color", "PopupMenu", new Color(Ink, 0.4f));

        t.SetStylebox("background", "ProgressBar", Box(new Color(Ink, 0.12f), 0, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Red, 0, 0));
        t.SetColor("font_color", "ProgressBar", Colors.Transparent);
        t.SetFontSize("font_size", "ProgressBar", 1);

        t.SetStylebox("slider", "HSlider", Box(new Color(Ink, 0.25f), 0, 0, vertical: 2));
        t.SetStylebox("grabber_area", "HSlider", Box(Red, 0, 0, vertical: 2));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(Red, 0, 0, vertical: 2));

        t.SetStylebox("scroll", "VScrollBar", Box(new Color(Gold, 0.08f), 0, 0));
        t.SetStylebox("grabber", "VScrollBar", Box(new Color(Gold, 0.35f), 0, 0));
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(new Color(Gold, 0.5f), 0, 0));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(Gold, 0, 0));

        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = new Color(Ink, 0.25f), Thickness = 1 });
        t.SetConstant("separation", "HSeparator", 10);
        return t;

        void SetLabel(string type, Color color, int size, Font? font)
        {
            t.SetTypeVariation(type, "Label");
            t.SetColor("font_color", type, color);
            t.SetFontSize("font_size", type, size);
            if (font is not null)
                t.SetFont("font", type, font);
        }

    }

    private static StyleBoxFlat PaperBox(Color fill, int pad, Color shadow)
    {
        var box = Box(fill, 0, pad);
        box.ShadowColor = shadow;
        box.ShadowSize = 4;
        box.ShadowOffset = new Vector2(0, 2);
        return box;
    }

    private static Font LoadFont(string file)
    {
        var font = new FontFile { Data = FileAccess.GetFileAsBytes($"res://art/fonts/{file}") };
        font.Antialiasing = TextServer.FontAntialiasing.Gray;
        return font;
    }

    /// <summary>A weight of a variable font, with optional letter spacing.</summary>
    private static Font Variation(Font font, int? weight, int spacing = 0)
    {
        var v = new FontVariation { BaseFont = font, SpacingGlyph = spacing };
        if (weight is { } w)
            v.VariationOpentype = new Godot.Collections.Dictionary { { TextServerManager.GetPrimaryInterface().NameToTag("wght"), w } };
        return v;
    }

    private static StyleBoxLine Line1() => new() { Color = Line, Thickness = 1 };
}
