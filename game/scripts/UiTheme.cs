using Godot;

namespace RacingManager;

/// <summary>
/// The game's look, built in code: dark translucent cards over the art, rounded buttons with
/// hover and pressed states, and a warm gold accent. Every screen sets it on its root control.
/// Type variations: Card, CardTight (panels), Accent and Danger (buttons), Segment (toggle
/// buttons that sit side by side), Muted and Title (labels), Badge (a small panel).
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

    private static void StyleButton(Theme t, string type, Color normal, Color hover, Color pressed, Color text, int radius = 8, int pad = 14)
    {
        t.SetStylebox("normal", type, Box(normal, radius, pad, vertical: 7));
        t.SetStylebox("hover", type, Box(hover, radius, pad, vertical: 7));
        t.SetStylebox("pressed", type, Box(pressed, radius, pad, vertical: 7));
        t.SetStylebox("hover_pressed", type, Box(pressed.Lightened(0.1f), radius, pad, vertical: 7));
        t.SetStylebox("disabled", type, Box(new Color(normal, 0.35f), radius, pad, vertical: 7));
        t.SetStylebox("focus", type, Box(Colors.Transparent, radius, pad, vertical: 7, border: new Color(Accent, 0.8f)));
        t.SetColor("font_color", type, text);
        t.SetColor("font_hover_color", type, text);
        t.SetColor("font_pressed_color", type, text);
        t.SetColor("font_hover_pressed_color", type, text);
        t.SetColor("font_focus_color", type, text);
        t.SetColor("font_disabled_color", type, new Color(text, 0.4f));
    }

    /// <summary>A rounded, filled box with even padding and an optional 1 px border.</summary>
    public static StyleBoxFlat Box(Color fill, int radius, int pad, int vertical = -1, Color? border = null)
    {
        var box = new StyleBoxFlat { BgColor = fill, AntiAliasing = true };
        box.SetCornerRadiusAll(radius);
        box.ContentMarginLeft = box.ContentMarginRight = pad;
        box.ContentMarginTop = box.ContentMarginBottom = vertical < 0 ? pad : vertical;
        if (border is { } b)
        {
            box.BorderColor = b;
            box.SetBorderWidthAll(1);
        }
        return box;
    }

    private static StyleBoxLine Line1() => new() { Color = Line, Thickness = 1 };
}
