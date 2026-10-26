using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>The race the calendar asked to show. Set before changing to the race scene.</summary>
public static class RaceRequest
{
    public static LiveRace? Race { get; set; }

    /// <summary>The player's team: its cars get the pit wall controls.</summary>
    public static string? PlayerTeamId { get; set; }
}

/// <summary>
/// Race day: the circuit fills the screen, with cards over it for the timing tower, the race
/// clock, playback speed (real time up to 240x), the camera, and the player's pit wall with
/// fuel and tyre gauges, pace orders and a box call for each of the team's cars.
/// </summary>
public partial class RaceScreen : Control
{
    private static readonly int[] Speeds = { 1, 10, 60, 240 };
    private static readonly PaceOrder[] Paces = { PaceOrder.Nurse, PaceOrder.Hold, PaceOrder.Race, PaceOrder.Push };
    private const float SideWidth = 290;

    private LiveRace _race = null!;
    private TrackView _track = null!;
    private Control _frame = null!;
    private OptionButton _follow = null!;
    private readonly List<string?> _followChoices = new();
    private Label _lap = null!;
    private Label _clock = null!;
    private Button _pause = null!;
    private readonly List<TowerRow> _tower = new();
    private readonly List<PitCard> _pitWall = new();
    private VBoxContainer _news = null!;
    // Made once and reused: a new style box every frame floods the garbage collector with
    // Godot objects, and finalizing those during shutdown crashed the engine.
    private readonly StyleBoxFlat _rowMine = UiTheme.Box(new Color(UiTheme.Accent, 0.16f), 6, 6, vertical: 3);
    private readonly StyleBoxFlat _rowOther = UiTheme.Box(Colors.Transparent, 6, 6, vertical: 3);
    private readonly StyleBoxFlat _barGood = UiTheme.Box(UiTheme.Good, 4, 0);
    private readonly StyleBoxFlat _barWarn = UiTheme.Box(UiTheme.Accent, 4, 0);
    private readonly StyleBoxFlat _barBad = UiTheme.Box(UiTheme.Danger, 4, 0);
    private double _time;
    private int _speed = 60;
    private bool _paused;

    private sealed record TowerRow(PanelContainer Panel, Label Position, ColorRect Stripe, Label Name, Label Gap);

    private sealed record PitCard(string DriverId, Label Position, ProgressBar Fuel, Label FuelText,
        ProgressBar Tyres, Label TyreText, Button[] Pace, Button Box, Label Warning);

    public override void _Ready()
    {
        var race = RaceRequest.Race;
        if (race is null)
        {
            GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
            return;
        }
        _race = race;
        Theme = UiTheme.Get();

        var backdrop = new ColorRect { Color = UiTheme.Background, MouseFilter = MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        // The circuit fills the window and can be zoomed and dragged under the cards.
        _frame = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Stop };
        _frame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_frame);
        _frame.GuiInput += OnTrackInput;
        _track = new TrackView();
        _frame.AddChild(_track);
        // A stable seed per circuit, so the scenery is the same every visit.
        _track.SetTrack(TrackShape.For(race.Circuit.Id), race.Circuit.Id, race.Circuit.Id.Sum(ch => ch));

        AddChild(LeftColumn());
        AddChild(RightColumn());
        AddChild(CameraBar());
        AddChild(PlaybackBar());

        Resized += Layout;
        Layout();
        Refresh();

        if (DevShot.Active)
        {
            _speed = 240;
            DevShot.Capture(this, "race.png", 5, () =>
            {
                var mine = _followChoices.IndexOf(_race.Cars().First(c => c.Entry.Team.Id == RaceRequest.PlayerTeamId).Entry.Driver.Id);
                _follow.Selected = Math.Max(0, mine);
                _track.Zoom = 5;
                _speed = 10;
                DevShot.Capture(this, "race-zoom.png", 2);
            });
        }
    }

    // ------------------------------------------------------------------ layout

    private Control LeftColumn()
    {
        var column = Column();

        var header = Card();
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = _race.Event.Date.Year + " · " + _race.Circuit.Country.ToUpperInvariant(), ThemeTypeVariation = "Overline" });
        box.AddChild(new Label { Text = _race.Event.Name, ThemeTypeVariation = "Heading" });
        box.AddChild(new Label { Text = $"{_race.Circuit.Name} · {_race.Circuit.LapKm:0.#} km lap", ThemeTypeVariation = "Muted" });
        var lapRow = new HBoxContainer();
        _lap = new Label { ThemeTypeVariation = "Big", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _clock = new Label { ThemeTypeVariation = "Muted", VerticalAlignment = VerticalAlignment.Center };
        lapRow.AddChild(_lap);
        lapRow.AddChild(_clock);
        box.AddChild(lapRow);
        header.AddChild(box);
        column.AddChild(header);

        var tower = Card("CardTight");
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 2);
        rows.AddChild(Margin(new Label { Text = "TIMING", ThemeTypeVariation = "Overline" }, 6, 4));
        foreach (var _ in _race.Cars())
        {
            var panel = new PanelContainer();
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            var position = new Label { CustomMinimumSize = new Vector2(26, 0), HorizontalAlignment = HorizontalAlignment.Right };
            var stripe = new ColorRect { CustomMinimumSize = new Vector2(4, 18), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            var name = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
            var gap = new Label { ThemeTypeVariation = "Muted", HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(72, 0) };
            line.AddChild(position);
            line.AddChild(stripe);
            line.AddChild(name);
            line.AddChild(gap);
            panel.AddChild(line);
            rows.AddChild(panel);
            _tower.Add(new TowerRow(panel, position, stripe, name, gap));
        }
        tower.AddChild(rows);
        column.AddChild(tower);
        return Scrolling(column, left: true);
    }

    private Control RightColumn()
    {
        var column = Column();

        var exit = new Button { Text = "Exit race", SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
        exit.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        column.AddChild(exit);

        column.AddChild(Margin(new Label { Text = "PIT WALL", ThemeTypeVariation = "Overline" }, 4, 0));
        var team = RaceRequest.PlayerTeamId;
        foreach (var entry in _race.Cars().Where(c => c.Entry.Team.Id == team).Select(c => c.Entry))
            column.AddChild(PitWallCard(entry));

        var news = Card();
        _news = new VBoxContainer();
        _news.AddThemeConstantOverride("separation", 6);
        var newsBox = new VBoxContainer();
        newsBox.AddChild(new Label { Text = "RACE NEWS", ThemeTypeVariation = "Overline" });
        newsBox.AddChild(_news);
        news.AddChild(newsBox);
        column.AddChild(news);
        return Scrolling(column, left: false);
    }

    private Control PitWallCard(RaceEntry entry)
    {
        var id = entry.Driver.Id;
        var card = Card("CardTight");
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        top.AddChild(new ColorRect { Color = TrackView.TeamColor(entry.Team.Id), CustomMinimumSize = new Vector2(4, 22), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        top.AddChild(new Label { Text = entry.Driver.Name, ThemeTypeVariation = "Heading", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
        var badge = new PanelContainer { ThemeTypeVariation = "Badge", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var position = new Label { ThemeTypeVariation = "Heading" };
        badge.AddChild(position);
        top.AddChild(badge);
        box.AddChild(top);

        var (fuelRow, fuel, fuelText) = Gauge("Fuel");
        var (tyreRow, tyres, tyreText) = Gauge("Tyres");
        box.AddChild(fuelRow);
        box.AddChild(tyreRow);

        var paceRow = new HBoxContainer();
        paceRow.AddThemeConstantOverride("separation", 4);
        var paceButtons = Paces.Select(p =>
        {
            var b = new Button
            {
                Text = p.ToString(), ToggleMode = true, ThemeTypeVariation = "Segment",
                SizeFlagsHorizontal = SizeFlags.ExpandFill, ButtonPressed = p == PaceOrder.Race,
                TooltipText = PaceTip(p),
            };
            b.Pressed += () => _race.SetPace(id, p);
            paceRow.AddChild(b);
            return b;
        }).ToArray();
        OneAtATime(paceButtons);
        box.AddChild(paceRow);

        var boxCall = new Button { ThemeTypeVariation = "Danger", CustomMinimumSize = new Vector2(0, 30), TooltipText = "Stop for fuel and rear tyres next time past the pits" };
        boxCall.Pressed += () =>
        {
            var car = _race.Cars().First(c => c.Entry.Driver.Id == id);
            _race.CallPitStop(id, !car.PitCalled);
            Refresh();
        };
        box.AddChild(boxCall);
        var warning = new Label { ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(warning);

        card.AddChild(box);
        _pitWall.Add(new PitCard(id, position, fuel, fuelText, tyres, tyreText, paceButtons, boxCall, warning));
        return card;
    }

    private static (HBoxContainer, ProgressBar, Label) Gauge(string name)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new Label { Text = name, ThemeTypeVariation = "Muted", CustomMinimumSize = new Vector2(44, 0) });
        var bar = new ProgressBar
        {
            MaxValue = 1, Step = 0, ShowPercentage = false, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(0, 8),
        };
        row.AddChild(bar);
        var text = new Label { CustomMinimumSize = new Vector2(64, 0), HorizontalAlignment = HorizontalAlignment.Right };
        row.AddChild(text);
        return (row, bar, text);
    }

    private Control CameraBar()
    {
        var card = Card("CardTight");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var zoomOut = new Button { Text = "−", TooltipText = "Zoom out (mouse wheel)", CustomMinimumSize = new Vector2(36, 0) };
        zoomOut.Pressed += () => _track.Zoom /= 1.5f;
        var zoomIn = new Button { Text = "+", TooltipText = "Zoom in (mouse wheel)", CustomMinimumSize = new Vector2(36, 0) };
        zoomIn.Pressed += () => _track.Zoom *= 1.5f;
        var whole = new Button { Text = "Whole track" };
        whole.Pressed += () =>
        {
            SetFollow(0);
            _track.Zoom = 1;
        };

        _follow = new OptionButton { TooltipText = "Camera follows this car" };
        _followChoices.Add(null);
        _follow.AddItem("Free camera");
        _followChoices.Add("");
        _follow.AddItem("Follow the leader");
        var team = RaceRequest.PlayerTeamId;
        foreach (var car in _race.Cars().OrderBy(c => c.Entry.Team.Id == team ? 0 : 1))
        {
            _followChoices.Add(car.Entry.Driver.Id);
            _follow.AddItem($"Follow {car.Entry.Driver.Name}");
        }
        _follow.ItemSelected += i =>
        {
            if (_followChoices[(int)i] is not null && _track.Zoom < 3)
                _track.Zoom = 4;
            Refresh();
        };

        row.AddChild(zoomOut);
        row.AddChild(zoomIn);
        row.AddChild(whole);
        row.AddChild(_follow);
        card.AddChild(row);
        card.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.KeepSize, 16);
        card.GrowHorizontal = GrowDirection.Both;
        return card;
    }

    private Control PlaybackBar()
    {
        var card = Card("CardTight");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);

        _pause = new Button { Text = "Pause", ToggleMode = true, ThemeTypeVariation = "Segment", CustomMinimumSize = new Vector2(76, 0) };
        _pause.Toggled += on => { _paused = on; Refresh(); };
        row.AddChild(_pause);
        row.AddChild(new VSeparator());

        var speedButtons = new List<Button>();
        foreach (var s in Speeds)
        {
            var b = new Button
            {
                Text = s == 1 ? "Real time" : $"{s}×", ToggleMode = true, ThemeTypeVariation = "Segment",
                ButtonPressed = s == _speed, CustomMinimumSize = new Vector2(s == 1 ? 96 : 56, 0),
                TooltipText = s == 1 ? "Watch at true race speed" : $"{s} times real speed",
            };
            b.Pressed += () =>
            {
                _speed = s;
                _pause.ButtonPressed = false;
            };
            row.AddChild(b);
            speedButtons.Add(b);
        }
        OneAtATime(speedButtons);
        row.AddChild(new VSeparator());

        var finish = new Button { Text = "Skip to finish", TooltipText = "Run the rest of the race at once" };
        finish.Pressed += () =>
        {
            _race.RunToEnd();
            _time = _race.FinishTime;
            Refresh();
        };
        row.AddChild(finish);
        card.AddChild(row);
        card.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.KeepSize, 16);
        card.GrowHorizontal = GrowDirection.Both;
        card.GrowVertical = GrowDirection.Begin;
        return card;
    }

    /// <summary>
    /// Toggle buttons where exactly one is down. Done by hand rather than with a ButtonGroup:
    /// a ButtonGroup made from C# is one more Godot object to outlive the engine at quit.
    /// </summary>
    private static void OneAtATime(IReadOnlyList<Button> buttons)
    {
        foreach (var button in buttons)
            button.Toggled += on =>
            {
                if (!on)
                {
                    button.SetPressedNoSignal(true);
                    return;
                }
                foreach (var other in buttons.Where(o => o != button))
                    other.SetPressedNoSignal(false);
            };
    }

    private static VBoxContainer Column()
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(SideWidth, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10);
        return column;
    }

    /// <summary>A side column that scrolls when the window is too short for it.</summary>
    private static ScrollContainer Scrolling(Control column, bool left)
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        scroll.SetAnchorsPreset(left ? LayoutPreset.LeftWide : LayoutPreset.RightWide);
        scroll.OffsetTop = 16;
        scroll.OffsetBottom = -16;
        scroll.OffsetLeft = left ? 16 : -16 - SideWidth;
        scroll.OffsetRight = left ? 16 + SideWidth : -16;
        scroll.AddChild(column);
        return scroll;
    }

    private static PanelContainer Card(string variation = "Card") =>
        new() { ThemeTypeVariation = variation, MouseFilter = MouseFilterEnum.Stop };

    private static MarginContainer Margin(Control child, int horizontal, int vertical)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", horizontal);
        m.AddThemeConstantOverride("margin_right", horizontal);
        m.AddThemeConstantOverride("margin_top", vertical);
        m.AddThemeConstantOverride("margin_bottom", vertical);
        m.AddChild(child);
        return m;
    }

    private static string PaceTip(PaceOrder pace) => pace switch
    {
        PaceOrder.Nurse => "Save the car: slower, much kinder on tyres and engine",
        PaceOrder.Hold => "Keep the place: a little slower, a little safer",
        PaceOrder.Push => "Flat out: faster, but wears tyres and engine and risks a crash",
        _ => "Normal racing pace",
    };

    private void Layout()
    {
        // Fit the whole circuit between the side columns; the picture still runs under them.
        var size = GetViewportRect().Size;
        var middle = new Rect2(SideWidth + 32, 70, size.X - 2 * (SideWidth + 32), size.Y - 140);
        if (middle.Size.X < 200)
            middle = new Rect2(0, 0, size.X, size.Y);
        _track.Area = middle;
        _track.Zoom = _track.Zoom;
    }

    // ------------------------------------------------------------------ camera input

    private void SetFollow(int choice)
    {
        _follow.Selected = choice;
        _track.FollowDriverId = null;
    }

    private void OnTrackInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } wheel:
                _track.ZoomAt(wheel.Position, 1.15f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } wheel:
                _track.ZoomAt(wheel.Position, 1 / 1.15f);
                break;
            case InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } drag:
                SetFollow(0);
                _track.Pan(drag.Relative);
                break;
        }
    }

    // ------------------------------------------------------------------ the race clock

    private bool Over => _race.IsOver && _time >= _race.FinishTime;

    public override void _Process(double delta)
    {
        if (_race is null || _paused || Over)
            return;
        _time += delta * _speed;
        _race.AdvanceTo(_time);
        if (_race.IsOver)
            _time = Math.Min(_time, _race.FinishTime);
        Refresh();
    }

    private void Refresh()
    {
        var cars = _race.Cars();
        var follow = _followChoices.Count == 0 ? null : _followChoices[_follow.Selected];
        _track.FollowDriverId = follow == "" ? cars[0].Entry.Driver.Id : follow;
        _track.SetCars(cars);

        var laps = _race.Event.Laps;
        var leaderLap = Math.Min(laps, (int)Math.Floor(cars[0].Distance) + 1);
        _lap.Text = Over ? "Finished" : $"Lap {leaderLap}/{laps}";
        _clock.Text = Clock(_time) + (_paused ? " · paused" : "");

        var team = RaceRequest.PlayerTeamId;
        var averageLap = cars[0].Distance > 0.1 ? _time / cars[0].Distance : 60;
        for (var i = 0; i < _tower.Count && i < cars.Count; i++)
        {
            var c = cars[i];
            var row = _tower[i];
            row.Position.Text = (i + 1).ToString();
            row.Stripe.Color = TrackView.TeamColor(c.Entry.Team.Id);
            row.Name.Text = Short(c.Entry.Driver.Name);
            row.Panel.TooltipText = c.Entry.Driver.Name;
            row.Gap.Text = c.Retirement?.Cause switch
            {
                RetirementCause.MechanicalFailure => "Out · engine",
                RetirementCause.Crash => "Out · crash",
                RetirementCause.FatalCrash => "Killed",
                RetirementCause.OutOfFuel => "Out · fuel",
                _ when c.InPits => "In pits",
                _ when i == 0 => c.Finished ? "Winner" : "Leader",
                _ => Gap(cars[0], c, averageLap),
            };
            var mine = c.Entry.Team.Id == team;
            SetStyle(row.Panel, "panel", mine ? _rowMine : _rowOther);
            row.Name.Modulate = c.Retirement is null ? Colors.White : new Color(1, 1, 1, 0.45f);
        }

        foreach (var card in _pitWall)
        {
            var position = cars.ToList().FindIndex(c => c.Entry.Driver.Id == card.DriverId);
            var car = cars[position];
            var fuelLaps = car.Fuel * LiveRace.TankRangeKm / _race.Circuit.LapKm;
            card.Position.Text = $"P{position + 1}";
            card.Fuel.Value = car.Fuel;
            card.FuelText.Text = $"{fuelLaps:0} laps";
            card.Tyres.Value = 1 - car.TyreWear;
            card.TyreText.Text = $"{100 - car.TyreWear * 100:0}%";
            SetStyle(card.Fuel, "fill", fuelLaps < 2 ? _barBad : _barGood);
            SetStyle(card.Tyres, "fill", car.TyreWear > 0.8 ? _barBad : car.TyreWear > 0.6 ? _barWarn : _barGood);

            card.Box.Text = car.PitCalled ? "Boxing this lap · cancel" : "Box this lap";
            card.Box.Disabled = !car.Running;
            foreach (var b in card.Pace)
                b.Disabled = !car.Running;

            card.Warning.Text = !car.Running ? (car.Finished ? "Finished." : "Out of the race.")
                : car.InPits ? "In the pits: fuel and tyres."
                : fuelLaps < 2 ? "Low fuel: box now or the car runs dry."
                : car.TyreWear > 0.8 ? "Rear tyres nearly gone."
                : "";
            card.Warning.Modulate = fuelLaps < 2 || car.TyreWear > 0.8 ? new Color(1, 0.6f, 0.5f) : Colors.White;
            card.Warning.Visible = card.Warning.Text.Length > 0;
        }

        var news = _race.Messages.Where(m => m.Time <= _time).OrderBy(m => m.Time).TakeLast(4).Reverse().ToList();
        while (_news.GetChildCount() < Math.Max(1, news.Count))
            _news.AddChild(new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(SideWidth - 40, 0) });
        for (var i = 0; i < _news.GetChildCount(); i++)
        {
            var label = (Label)_news.GetChild(i);
            label.Visible = i < Math.Max(1, news.Count);
            label.Text = i < news.Count ? $"Lap {news[i].Lap} · {news[i].Text}" : "Cars on the grid.";
        }
    }

    private static void SetStyle(Control control, string name, StyleBox style)
    {
        if (control.GetThemeStylebox(name) != style)
            control.AddThemeStyleboxOverride(name, style);
    }

    /// <summary>Gap to the leader: whole laps when lapped, otherwise roughly in seconds.</summary>
    private static string Gap(LiveCar leader, LiveCar car, double averageLap)
    {
        var laps = leader.Distance - car.Distance;
        return laps >= 1 ? $"+{Math.Floor(laps)} lap{(laps >= 2 ? "s" : "")}" : $"+{laps * averageLap:0.0}s";
    }

    /// <summary>"Giuseppe Campari" becomes "G. Campari", so the tower stays narrow.</summary>
    private static string Short(string name)
    {
        var space = name.IndexOf(' ');
        return space > 0 ? $"{name[0]}. {name[(space + 1)..]}" : name;
    }

    private static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
    }
}
