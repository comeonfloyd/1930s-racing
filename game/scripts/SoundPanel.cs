using Godot;

namespace RacingManager;

/// <summary>A "Sound" button that opens the volume sliders: everything, engines, the circuit, music, menus, and mute.</summary>
public static class SoundPanel
{
    private static readonly (string Name, Sounds.Channel Channel, string Tip)[] Sliders =
    {
        ("Engines", Sounds.Channel.Engines, "The cars on the circuit"),
        ("Circuit", Sounds.Channel.Effects, "The crowd, the pits, rain, crashes"),
        ("Music", Sounds.Channel.Music, "The gramophone in the menus"),
        ("Menus", Sounds.Channel.Interface, "Buttons and pages"),
    };

    public static Button Button()
    {
        var button = new Button { Text = Label(), TooltipText = "Sound volumes (M mutes)", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var popup = new PopupPanel();
        button.AddChild(popup);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) };
        box.AddThemeConstantOverride("separation", 6);
        popup.AddChild(box);

        var mute = new CheckButton { Text = "Sound on", ButtonPressed = !Sounds.Muted };
        mute.Toggled += on =>
        {
            Sounds.Muted = !on;
            Sounds.Save();
            button.Text = Label();
        };
        box.AddChild(mute);
        box.AddChild(Row("Volume", "Everything", Sounds.Master, v => Sounds.Master = v));
        foreach (var (name, channel, tip) in Sliders)
            box.AddChild(Row(name, tip, Sounds.Volume(channel), v => Sounds.SetVolume(channel, v)));

        button.Pressed += () =>
        {
            // M may have changed it since the panel was last open.
            mute.SetPressedNoSignal(!Sounds.Muted);
            var at = button.GetScreenPosition() + new Vector2(0, button.Size.Y + 4);
            popup.Popup(new Rect2I((Vector2I)at, new Vector2I(280, 0)));
        };
        popup.PopupHide += () => button.Text = Label();
        return button;
    }

    private static string Label() => Sounds.Muted ? "Sound off" : "Sound";

    private static Control Row(string name, string tip, float value, System.Action<float> set)
    {
        var row = new HBoxContainer { TooltipText = tip };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(70, 0) });
        var slider = new HSlider
        {
            MinValue = 0, MaxValue = 1, Step = 0.05, Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        slider.ValueChanged += v =>
        {
            set((float)v);
            Sounds.Save();
        };
        row.AddChild(slider);
        return row;
    }
}
