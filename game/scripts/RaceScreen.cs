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
/// Race day: the circuit fills the screen, with cards over it for the timing tower (team, last
/// lap and gap for every car), race statistics, the race clock, playback speed (real time up to
/// 240x, or 1200x for a 24-hour race), the camera, and the player's pit wall: for each of the
/// team's cars fuel and tyre gauges, pace orders, a box call, and a pit plan of fuel load, tyre
/// change and stops planned ahead. In a 24-hour race the clock shows the time of day, night falls
/// on the track, and the pit wall shows who is driving and whether the drivers change at the next stop.
/// </summary>
public partial class RaceScreen : Control
{
    private static readonly int[] GrandPrixSpeeds = { 1, 10, 60, 240 };
    private static readonly int[] EnduranceSpeeds = { 1, 60, 240, 1200 };
    private static readonly PaceOrder[] Paces = { PaceOrder.Nurse, PaceOrder.Hold, PaceOrder.Race, PaceOrder.Push };
    private static readonly int[] StopChoices = { 0, 1, 2, 3 };
    private static readonly FuelPlan[] FuelChoices = { FuelPlan.NextStop, FuelPlan.Full, FuelPlan.None };
    private const float LeftWidth = 350;
    private const float RightWidth = 280;
    /// <summary>The race's fastest lap: purple, as on a modern timing screen. A driver's own best is green.</summary>
    private static readonly Color FastestLap = new("c58cf2");

    private LiveRace _race = null!;
    private IRaceView _track = null!;
    private TrackView _map = null!;
    private TrackView3D _scene = null!;
    private Button _viewToggle = null!;
    private Control _frame = null!;
    private OptionButton _follow = null!;
    private readonly List<string?> _followChoices = new();
    private Label _lap = null!;
    private Label _clock = null!;
    private Button _pause = null!;
    private readonly List<TowerRow> _tower = new();
    private readonly List<PitCard> _pitWall = new();
    private VBoxContainer _news = null!;
    private VBoxContainer _stats = null!;
    private Control _statsScreen = null!;
    private Button _statsButton = null!;
    private readonly List<Label[]> _statRows = new();
    private static readonly (string Head, int Width, bool Right)[] StatColumns =
    {
        ("POS", 34, true), ("DRIVER", 0, false), ("TEAM", 120, false), ("CAR", 150, false), ("GRID", 40, true), ("+/−", 40, true),
        ("LAPS", 40, true), ("LAST LAP", 70, true), ("BEST LAP", 92, true), ("STOPS", 46, true), ("IN PITS", 56, true), ("STATUS", 96, true),
    };
    // Made once and reused: a new style box every frame floods the garbage collector with
    // Godot objects, and finalizing those during shutdown crashed the engine.
    private readonly StyleBoxFlat _rowMine = UiTheme.Box(new Color(UiTheme.Accent, 0.16f), 6, 6, vertical: 3);
    private readonly StyleBoxFlat _rowOther = UiTheme.Box(Colors.Transparent, 6, 6, vertical: 3);
    // Thinner rows for a full grid, so twenty cars or more fit the tower.
    private readonly StyleBoxFlat _rowMineTight = UiTheme.Box(new Color(UiTheme.Accent, 0.16f), 6, 6, vertical: 0);
    private readonly StyleBoxFlat _rowOtherTight = UiTheme.Box(Colors.Transparent, 6, 6, vertical: 0);
    private readonly StyleBoxFlat _barGood = UiTheme.Box(UiTheme.Good, 4, 0);
    private readonly StyleBoxFlat _barWarn = UiTheme.Box(UiTheme.Accent, 4, 0);
    private readonly StyleBoxFlat _barBad = UiTheme.Box(UiTheme.Danger, 4, 0);
    private double _time;
    private int _speed = 60;
    private bool _paused;
    private bool _resultShown;
    private readonly List<Button> _speedButtons = new();
    private int[] Speeds => _race.Event.IsEndurance ? EnduranceSpeeds : GrandPrixSpeeds;

    private sealed record TowerRow(PanelContainer Panel, Label Position, ColorRect Stripe, Label Name, Label Team, Label Last, Label Gap);

    private sealed record PitCard(string DriverId, Label Position, Label Laps, ProgressBar Fuel, Label FuelText,
        ProgressBar Tyres, Label TyreText, Button[] Pace, Button Box, OptionButton? Stops, OptionButton? Fuelling,
        CheckBox TyreChange, Label Plan, Button? Earlier, Button? Later, Label Warning, Label? AtWheel, CheckButton? Swap);

    public override void _Ready()
    {
        var race = RaceRequest.Race;
        if (race is null)
        {
            GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
            return;
        }
        _race = race;
        if (race.Event.IsEndurance)
            _speed = 240;
        Theme = UiTheme.Get();

        var backdrop = new ColorRect { Color = UiTheme.Background, MouseFilter = MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        // The circuit fills the window and can be zoomed and dragged under the cards.
        _frame = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Stop };
        _frame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_frame);
        _frame.GuiInput += OnTrackInput;
        // Two views of the same circuit: the 3D scene, and the flat map from above.
        _map = new TrackView { Visible = false };
        _frame.AddChild(_map);
        _scene = new TrackView3D { SportsCars = race.Event.IsEndurance };
        _frame.AddChild(_scene);
        _track = _scene;
        var shape = TrackShape.For(race.Circuit.Id);
        // A stable seed per circuit, so the scenery is the same every visit.
        foreach (var view in new IRaceView[] { _map, _scene })
            view.SetTrack(shape, race.Circuit.Id, race.Circuit.Id.Sum(ch => ch));
        if (DevShot.Arg("view") == "map")
            ShowView(_map);

        AddChild(LeftColumn());
        AddChild(RightColumn());
        AddChild(CameraBar());
        AddChild(PlaybackBar());
        AddChild(_statsScreen = StatsScreen());

        Resized += Layout;
        Layout();
        Refresh();

        if (DevShot.Active)
        {
            SetSpeed(240);
            // DevShot --night: start the shots at midnight in a 24-hour race.
            if (_race.Event.IsEndurance && OS.GetCmdlineUserArgs().Contains("--night"))
            {
                _time = 8 * 3600;
                _race.AdvanceTo(_time);
                _track.Night = Darkness(_time);
                Refresh();
            }
            // Show a pit plan on the first card: two stops, just enough fuel for each stint.
            if (_pitWall.FirstOrDefault() is { Stops: { } stops } first)
            {
                stops.Selected = 2;
                _race.SetPlannedStops(first.DriverId, _race.EvenStops(first.DriverId, 2));
            }
            DevShot.Capture(this, "race.png", 5, () =>
            {
                var mine = _followChoices.IndexOf(_race.Cars().First(c => c.Entry.Team.Id == RaceRequest.PlayerTeamId).Entry.Driver.Id);
                _follow.Selected = Math.Max(0, mine);
                _track.Zoom = float.TryParse(DevShot.Arg("zoom"), System.Globalization.CultureInfo.InvariantCulture, out var zoom) ? zoom : 5;
                SetSpeed(10);
                DevShot.Capture(this, "race-zoom.png", 2, () =>
                {
                    _statsButton.ButtonPressed = true;
                    DevShot.Capture(this, "race-stats.png", 1, () =>
                    {
                        _statsButton.ButtonPressed = false;
                        _race.RunToEnd();
                        _time = _race.FinishTime;
                        Refresh();
                        DevShot.Capture(this, "race-result.png", 1, () =>
                        {
                            Session.Career?.FinishRace(_race);
                            GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
                        });
                    });
                });
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
        // A 1930s grid can have twenty cars or more: smaller rows so the tower still fits.
        var compact = _race.Cars().Count > 14;
        var size = _race.Cars().Count > 18 ? 11 : compact ? 12 : 13;
        rows.AddThemeConstantOverride("separation", compact ? 0 : 2);
        // Column heads over the rows, lined up with them.
        var heads = TowerLine(new Label { Text = "TIMING", ThemeTypeVariation = "Overline" }, "TEAM", "LAST LAP", "GAP");
        rows.AddChild(Margin(heads.Line, 6, 4));
        foreach (var _ in _race.Cars())
        {
            var panel = new PanelContainer();
            var name = new Label { ClipText = true };
            var (line, team, last, gap) = TowerLine(name, "", "", "");
            var position = new Label { CustomMinimumSize = new Vector2(22, 0), HorizontalAlignment = HorizontalAlignment.Right };
            var stripe = new ColorRect { CustomMinimumSize = new Vector2(4, compact ? 10 : 16), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            line.AddChild(position);
            line.MoveChild(position, 0);
            line.AddChild(stripe);
            line.MoveChild(stripe, 1);
            foreach (var label in new[] { position, name, team, last, gap })
                label.AddThemeFontSizeOverride("font_size", label == team ? size - 1 : size);
            if (_race.Event.IsEndurance)
                name.AddThemeFontSizeOverride("font_size", Math.Min(size, 12));  // two surnames a row
            panel.AddChild(line);
            rows.AddChild(panel);
            _tower.Add(new TowerRow(panel, position, stripe, name, team, last, gap));
        }
        tower.AddChild(rows);
        column.AddChild(tower);

        return Scrolling(column, left: true);
    }

    /// <summary>
    /// One line of the timing tower: the name (and whatever goes before it) widest, then team,
    /// last lap and gap in fixed columns, so the heads line up with the rows.
    /// </summary>
    private static (HBoxContainer Line, Label Team, Label Last, Label Gap) TowerLine(Label name, string team, string last, string gap)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 6);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.CustomMinimumSize = new Vector2(90, 0);
        var head = team.Length > 0;
        var teamLabel = new Label { Text = team, ThemeTypeVariation = head ? "Overline" : "Muted", ClipText = true, CustomMinimumSize = new Vector2(76, 0) };
        var lastLabel = new Label { Text = last, ThemeTypeVariation = head ? "Overline" : "", HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(58, 0) };
        var gapLabel = new Label { Text = gap, ThemeTypeVariation = head ? "Overline" : "Muted", HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(66, 0) };
        if (head)
            foreach (var label in new[] { teamLabel, lastLabel, gapLabel })
                label.AddThemeFontSizeOverride("font_size", 10);
        line.AddChild(name);
        line.AddChild(teamLabel);
        line.AddChild(lastLabel);
        line.AddChild(gapLabel);
        return (line, teamLabel, lastLabel, gapLabel);
    }

    private Control RightColumn()
    {
        var column = Column(RightWidth);

        column.AddChild(Margin(new Label { Text = "PIT WALL", ThemeTypeVariation = "Overline" }, 4, 0));
        var team = RaceRequest.PlayerTeamId;
        foreach (var entry in _race.Cars().Where(c => c.Entry.Team.Id == team).Select(c => c.Entry))
            column.AddChild(PitWallCard(entry));

        // The news under the pit wall, where the smaller cards leave room.
        var news = Card("CardTight");
        _news = new VBoxContainer();
        _news.AddThemeConstantOverride("separation", 4);
        var newsBox = new VBoxContainer();
        newsBox.AddChild(Margin(new Label { Text = "RACE NEWS", ThemeTypeVariation = "Overline" }, 6, 2));
        newsBox.AddChild(Margin(_news, 6, 2));
        news.AddChild(newsBox);
        column.AddChild(news);

        return Scrolling(column, left: false);
    }

    /// <summary>
    /// A compact card for one of the player's cars: name and place, fuel and tyre gauges side
    /// by side, pace orders with the box call, and the pit plan: how many stops, how much fuel
    /// and whether the tyres are changed, with the next planned stop moved a lap either way.
    /// </summary>
    private Control PitWallCard(RaceEntry entry)
    {
        var id = entry.Driver.Id;
        var endurance = _race.Event.IsEndurance;
        var card = Card("CardTight");
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 6);
        top.AddChild(new ColorRect { Color = TrackView.TeamColor(entry.Team.Id), CustomMinimumSize = new Vector2(4, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        top.AddChild(Small(new Label { Text = entry.CoDriver is null ? entry.Driver.Name : entry.Names, TooltipText = FullNames(entry), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true }, 15));
        var badge = new PanelContainer { ThemeTypeVariation = "Badge", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var position = Small(new Label(), 14);
        badge.AddChild(position);
        top.AddChild(badge);
        box.AddChild(top);
        var laps = Small(new Label { ThemeTypeVariation = "Muted", ClipText = true }, 11);
        box.AddChild(laps);

        // A shared car: who is driving now, and whether the other takes over at the next stop.
        Label? atWheel = null;
        CheckButton? swap = null;
        if (entry.CoDriver is not null)
        {
            atWheel = Small(new Label { ThemeTypeVariation = "Muted", ClipText = true }, 11);
            box.AddChild(atWheel);
            swap = Small(new CheckButton
            {
                Text = "Change drivers at the next stop", ButtonPressed = true,
                TooltipText = "Off: the same driver goes back out. A long stint tires him: slower laps and more mistakes after a few hours.",
            }, 12);
            swap.Toggled += on => _race.SetSwapAtStop(id, on);
            box.AddChild(swap);
        }

        var gauges = new HBoxContainer();
        gauges.AddThemeConstantOverride("separation", 10);
        var (fuelRow, fuel, fuelText) = Gauge("Fuel");
        var (tyreRow, tyres, tyreText) = Gauge("Tyres");
        gauges.AddChild(fuelRow);
        gauges.AddChild(tyreRow);
        box.AddChild(gauges);

        var paceRow = new HBoxContainer();
        paceRow.AddThemeConstantOverride("separation", 3);
        var paceButtons = Paces.Select(p =>
        {
            var b = Small(new Button
            {
                Text = p.ToString(), ToggleMode = true, ThemeTypeVariation = "Segment",
                SizeFlagsHorizontal = SizeFlags.ExpandFill, ButtonPressed = p == PaceOrder.Race,
                TooltipText = PaceTip(p), CustomMinimumSize = new Vector2(0, 24),
            }, 12);
            b.Pressed += () => _race.SetPace(id, p);
            paceRow.AddChild(b);
            return b;
        }).ToArray();
        OneAtATime(paceButtons);
        var boxCall = Small(new Button
        {
            ThemeTypeVariation = "Danger", CustomMinimumSize = new Vector2(62, 24),
            TooltipText = endurance
                ? $"Stop next time past the pits: tyres, and fuel when {LiveRace.MinLapsBetweenRefuels} laps have passed since the last fill"
                : "Stop next time past the pits, for the fuel and tyres the pit plan says",
        }, 12);
        boxCall.Pressed += () =>
        {
            var car = _race.Cars().First(c => c.Entry.Driver.Id == id);
            _race.CallPitStop(id, !car.PitCalled);
            Refresh();
        };
        top.AddChild(boxCall);
        box.AddChild(paceRow);

        // The pit plan. A 24-hour race runs on the clock, with the tank filled whenever the rules
        // allow, so there it is only the tyres.
        var planRow = new HBoxContainer();
        planRow.AddThemeConstantOverride("separation", 4);
        OptionButton? stops = null, fuelling = null;
        if (!endurance)
        {
            stops = Small(new OptionButton { TooltipText = "Plan the stops ahead: the race left is split into equal stints. Move the next stop with the arrows below.", SizeFlagsHorizontal = SizeFlags.ExpandFill }, 11);
            foreach (var n in StopChoices)
                stops.AddItem(n == 0 ? "My calls" : n == 1 ? "1 stop" : $"{n} stops");
            stops.ItemSelected += i =>
            {
                _race.SetPlannedStops(id, _race.EvenStops(id, StopChoices[(int)i]));
                Refresh();
            };
            planRow.AddChild(stops);
            fuelling = Small(new OptionButton { TooltipText = "Fuel at each stop. Light load: just enough for the next planned stop or the flag, so the car is lighter and quicker, with nothing to spare. A full tank weighs on lap times.", SizeFlagsHorizontal = SizeFlags.ExpandFill }, 11);
            foreach (var f in FuelChoices)
                fuelling.AddItem(f switch { FuelPlan.Full => "Full tank", FuelPlan.None => "No fuel", _ => "Light load" });
            planRow.AddChild(fuelling);
        }
        var tyreChange = Small(new CheckBox { Text = "Tyres", ButtonPressed = true, TooltipText = "Change the rear wheels at a stop (15 s). Off: a quicker stop on the old tyres." }, 11);
        void SetPlan() => _race.SetPitPlan(id, new PitPlan(fuelling is null ? FuelPlan.NextStop : FuelChoices[fuelling.Selected], tyreChange.ButtonPressed));
        if (fuelling is not null)
            fuelling.ItemSelected += _ => SetPlan();
        tyreChange.Toggled += _ => SetPlan();
        planRow.AddChild(tyreChange);
        box.AddChild(planRow);

        var nextRow = new HBoxContainer();
        nextRow.AddThemeConstantOverride("separation", 4);
        var plan = Small(new Label { ThemeTypeVariation = "Muted", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Pass, TooltipText = "The car comes in at the end of these laps" }, 12);
        nextRow.AddChild(plan);
        Button? earlier = null, later = null;
        if (!endurance)
        {
            earlier = Small(new Button { Text = "◂", TooltipText = "Bring the next stop a lap earlier", CustomMinimumSize = new Vector2(26, 22) }, 12);
            later = Small(new Button { Text = "▸", TooltipText = "Put the next stop a lap later", CustomMinimumSize = new Vector2(26, 22) }, 12);
            earlier.Pressed += () => MoveNextStop(id, -1);
            later.Pressed += () => MoveNextStop(id, +1);
            nextRow.AddChild(earlier);
            nextRow.AddChild(later);
        }

        box.AddChild(nextRow);

        var warning = Small(new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }, 12);
        box.AddChild(warning);

        card.AddChild(box);
        _pitWall.Add(new PitCard(id, position, laps, fuel, fuelText, tyres, tyreText, paceButtons, boxCall, stops, fuelling,
            tyreChange, plan, earlier, later, warning, atWheel, swap));
        return card;
    }

    /// <summary>Moves the car's next planned stop a lap, keeping it after the lap running now and before the last.</summary>
    private void MoveNextStop(string driverId, int by)
    {
        var car = _race.Cars().First(c => c.Entry.Driver.Id == driverId);
        var planned = car.PlannedStops?.ToList() ?? [];
        if (planned.Count == 0)
            return;
        var moved = Math.Clamp(planned[0] + by, car.LapsDone + 1, _race.Event.Laps - 1);
        if (planned.Contains(moved))
            return;
        planned[0] = moved;
        _race.SetPlannedStops(driverId, planned);
        Refresh();
    }

    private static T Small<T>(T control, int size) where T : Control
    {
        control.AddThemeFontSizeOverride("font_size", size);
        return control;
    }

    private static (HBoxContainer, ProgressBar, Label) Gauge(string name)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 5);
        row.AddChild(Small(new Label { Text = name, ThemeTypeVariation = "Muted" }, 11));
        var bar = new ProgressBar
        {
            MaxValue = 1, Step = 0, ShowPercentage = false, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(30, 6),
        };
        row.AddChild(bar);
        var text = Small(new Label { CustomMinimumSize = new Vector2(44, 0), HorizontalAlignment = HorizontalAlignment.Right }, 11);
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
            _follow.AddItem($"Follow {car.Entry.Names}");
        }
        _follow.ItemSelected += i =>
        {
            if (_followChoices[(int)i] is not null && _track.Zoom < 3)
                _track.Zoom = 4;
            Refresh();
        };

        _viewToggle = new Button { Text = _track == _scene ? "Map" : "3D view", TooltipText = "Switch between the 3D view and the flat map" };
        _viewToggle.Pressed += () => ShowView(_track == _scene ? _map : _scene);

        row.AddChild(zoomOut);
        row.AddChild(zoomIn);
        row.AddChild(whole);
        row.AddChild(_follow);
        row.AddChild(_viewToggle);
        _statsButton = new Button { Text = "Statistics", ToggleMode = true, ThemeTypeVariation = "Segment", TooltipText = "Every car's laps, stops and places, while the race runs on" };
        _statsButton.Toggled += on =>
        {
            _statsScreen.Visible = on;
            Refresh();
        };
        row.AddChild(_statsButton);
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

        foreach (var s in Speeds)
        {
            var b = new Button
            {
                Text = s == 1 ? "Real time" : $"{s}×", ToggleMode = true, ThemeTypeVariation = "Segment",
                ButtonPressed = s == _speed, CustomMinimumSize = new Vector2(s == 1 ? 96 : 56, 0),
                TooltipText = s == 1 ? "Watch at true race speed" : $"{s} times real speed" + (s == 1200 ? ": an hour of racing in three seconds" : ""),
            };
            b.Pressed += () =>
            {
                _speed = s;
                _pause.ButtonPressed = false;
            };
            row.AddChild(b);
            _speedButtons.Add(b);
        }
        OneAtATime(_speedButtons);
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

    /// <summary>Changes the playback speed from code, keeping the speed buttons in step.</summary>
    private void SetSpeed(int speed)
    {
        _speed = speed;
        for (var i = 0; i < Speeds.Length; i++)
            _speedButtons[i].SetPressedNoSignal(Speeds[i] == speed);
    }

    private static VBoxContainer Column(float width = LeftWidth)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
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
        scroll.OffsetLeft = left ? 16 : -16 - RightWidth;
        scroll.OffsetRight = left ? 16 + LeftWidth : -16;
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

    /// <summary>Swaps between the 3D view and the map, keeping the camera choice and the weather.</summary>
    private void ShowView(IRaceView view)
    {
        if (view == _track)
            return;
        view.Area = _track.Area;
        view.Rain = _track.Rain;
        view.Night = _track.Night;
        view.Zoom = _track.Zoom;
        _track.Visible = false;
        view.Visible = true;
        _track = view;
        if (_viewToggle is not null)
            _viewToggle.Text = view == _scene ? "Map" : "3D view";
        if (_race is not null && _follow is not null)
            Refresh();
    }

    private void Layout()
    {
        // Fit the whole circuit between the side columns; the picture still runs under them.
        var size = GetViewportRect().Size;
        var middle = new Rect2(LeftWidth + 32, 70, size.X - LeftWidth - RightWidth - 64, size.Y - 140);
        if (middle.Size.X < 200)
            middle = new Rect2(0, 0, size.X, size.Y);
        foreach (var view in new IRaceView[] { _map, _scene })
        {
            view.Area = middle;
            view.Zoom = view.Zoom;
        }
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
            case InputEventMouseMotion { ButtonMask: MouseButtonMask.Right } turn:
                _track.Turn(turn.Relative);
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
        var wet = _race.Weather.IsWetAt(_time) || DevShot.Arg("rain") is not null;
        if (_race.Event.IsEndurance)
        {
            // No set number of laps: the hours left, and the time of day at the circuit.
            var left = Math.Max(0, _race.Event.Hours * 3600 - _time);
            _lap.Text = Over ? "Finished" : $"{Math.Ceiling(left / 3600):0}h left";
            var hour = LiveRace.HourOfDay(_time);
            _clock.Text = $"Lap {(int)Math.Floor(cars[0].Distance) + 1} · {(int)hour:00}:{(int)(hour % 1 * 60):00}"
                + (_race.IsNightAt(_time) ? " · night" : "") + (wet ? " · rain" : "") + (_paused ? " · paused" : "");
            _track.Night = Mathf.MoveToward(_track.Night, Darkness(_time), 0.02f);
        }
        else
        {
            _lap.Text = Over ? "Finished" : $"Lap {leaderLap}/{laps}";
            _clock.Text = Clock(_time) + (wet ? " · rain" : "") + (_paused ? " · paused" : "");
        }
        // Rain eases in and out over a few seconds of play rather than switching at once.
        _track.Rain = Mathf.MoveToward(_track.Rain, wet ? 1 : 0, 0.02f);

        var team = RaceRequest.PlayerTeamId;
        var averageLap = cars[0].Distance > 0.1 ? _time / cars[0].Distance : 60;
        var fastest = cars.Where(c => c.BestLap > 0).MinBy(c => c.BestLap);
        for (var i = 0; i < _tower.Count && i < cars.Count; i++)
        {
            var c = cars[i];
            var row = _tower[i];
            row.Position.Text = (i + 1).ToString();
            row.Stripe.Color = TrackView.TeamColor(c.Entry.Team.Id);
            row.Name.Text = c.Entry.CoDriver is null ? Short(c.Entry.Driver.Name) : c.Entry.Names;
            row.Team.Text = TeamName(c.Entry);
            row.Last.Text = c.LastLap > 0 ? LapTime(c.LastLap) : "–";
            // Purple for the race's fastest lap, green for the driver's own best.
            row.Last.Modulate = c.LastLap <= 0 || c.Retirement is not null ? new Color(1, 1, 1, 0.45f)
                : c == fastest && c.BestLapNumber == c.LapsDone ? FastestLap
                : c.BestLapNumber == c.LapsDone ? UiTheme.Good : Colors.White;
            row.Panel.TooltipText = $"{FullNames(c.Entry)} · {c.Entry.Team.Name}, {c.Entry.Team.Car.Name}"
                + $"\nStarted {Ordinal(c.Grid)} · best lap {(c.BestLap > 0 ? LapTime(c.BestLap) + $" (lap {c.BestLapNumber})" : "–")}"
                + $" · {c.PitStops} stop{(c.PitStops == 1 ? "" : "s")}";
            row.Gap.Text = c.Retirement?.Cause switch
            {
                RetirementCause.MechanicalFailure => "Out · engine",
                RetirementCause.Crash => "Out · crash",
                RetirementCause.FatalCrash => "Killed",
                RetirementCause.OutOfFuel => "Out · fuel",
                _ when c.InPits => "In pits",
                _ when i == 0 => c.Finished ? "Winner" : "Leader",
                _ when c.Finished && cars[0].Finished && c.Distance >= cars[0].Distance => $"+{c.FinishedAt - cars[0].FinishedAt:0.0}s",
                _ => Gap(cars[0], c, averageLap),
            };
            var mine = c.Entry.Team.Id == team;
            SetStyle(row.Panel, "panel", _tower.Count > 14 ? (mine ? _rowMineTight : _rowOtherTight) : mine ? _rowMine : _rowOther);
            row.Name.Modulate = c.Retirement is null ? Colors.White : new Color(1, 1, 1, 0.45f);
        }

        foreach (var card in _pitWall)
        {
            var position = cars.ToList().FindIndex(c => c.Entry.Driver.Id == card.DriverId);
            var car = cars[position];
            var fuelLaps = car.Fuel * (_race.Event.IsEndurance ? LiveRace.EnduranceTankRangeKm : LiveRace.TankRangeKm) / _race.Circuit.LapKm;
            card.Position.Text = $"P{position + 1}";
            card.Laps.Text = car.LapsDone == 0 ? "No lap times yet"
                : $"Last {LapTime(car.LastLap)} · best {LapTime(car.BestLap)} · {car.PitStops} stop{(car.PitStops == 1 ? "" : "s")}{(car.PitSeconds > 0 ? $", {car.PitSeconds:0} s" : "")}";
            card.Fuel.Value = car.Fuel;
            // After the flag the tank is all but empty by design: no need to show it.
            card.FuelText.Text = car.Running ? $"{fuelLaps:0} laps" : "–";
            card.Tyres.Value = 1 - car.TyreWear;
            card.TyreText.Text = $"{100 - car.TyreWear * 100:0}%";
            SetStyle(card.Fuel, "fill", fuelLaps < 2 && car.Running ? _barBad : _barGood);
            SetStyle(card.Tyres, "fill", car.TyreWear > 0.8 ? _barBad : car.TyreWear > 0.6 ? _barWarn : _barGood);

            card.Box.Text = car.PitCalled ? "Cancel" : "Box";
            card.Box.Disabled = !car.Running;
            foreach (var b in card.Pace)
                b.Disabled = !car.Running;
            foreach (var control in new BaseButton?[] { card.Stops, card.Fuelling, card.TyreChange })
                if (control is not null)
                    control.Disabled = !car.Running;

            var planned = car.PlannedStops ?? [];
            card.Plan.Text = !car.Running ? ""
                : car.PitCalled ? $"Boxing at the end of lap {car.LapsDone + 1}"
                : planned.Count > 0 ? $"In after lap{(planned.Count > 1 ? "s" : "")} {string.Join(", ", planned)}"
                : "";
            // The plan line only when there is a plan to show, to keep the card small.
            card.Plan.GetParent<Control>().Visible = card.Plan.Text.Length > 0;
            if (card.Earlier is not null && card.Later is not null)
            {
                card.Earlier.Disabled = !car.Running || planned.Count == 0 || planned[0] <= car.LapsDone + 1;
                card.Later.Disabled = !car.Running || planned.Count == 0 || planned[0] >= _race.Event.Laps - 1 || planned.Contains(planned[0] + 1);
            }

            if (card.AtWheel is not null && car.AtWheel is { } driver)
                card.AtWheel.Text = car.Running
                    ? $"At the wheel: {driver.Name}, {car.StintHours:0.0} h{(car.StintHours > 3 ? " · tiring" : "")}"
                    : $"Last at the wheel: {driver.Name}";
            if (card.Swap is not null)
            {
                card.Swap.SetPressedNoSignal(car.SwapAtStop);
                card.Swap.Disabled = !car.Running;
            }

            // A plan that leaves the car short: fuel to the next stop is fine, but no stop and no fuel is not.
            var lapsToGo = _race.Event.Laps - car.LapsDone;
            var reach = planned.Count > 0 ? planned[0] - car.LapsDone : lapsToGo;
            card.Warning.Text = !car.Running ? (car.Finished ? "Finished." : "Out of the race.")
                : car.InPits ? (_race.Event.IsEndurance ? "In the pits: the driver works on the car alone." : "In the pits.")
                : fuelLaps < 2 && !car.FuelAllowed ? "Low fuel, and too soon to refuel: nurse it round."
                : fuelLaps < 2 ? "Low fuel: box now or the car runs dry."
                : car.TyreWear > 0.8 ? "Rear tyres nearly gone."
                : !_race.Event.IsEndurance && fuelLaps < reach - 0.5 && !car.PitCalled ? $"Fuel for {fuelLaps:0} of {reach} laps: plan a stop."
                : _race.Event.IsEndurance && !car.FuelAllowed ? $"No fuel at a stop yet: {LiveRace.MinLapsBetweenRefuels} laps between fills."
                : "";
            card.Warning.Modulate = car.Running && (fuelLaps < 2 || car.TyreWear > 0.8) ? new Color(1, 0.6f, 0.5f) : UiTheme.TextMuted;
            card.Warning.Visible = card.Warning.Text.Length > 0;
        }

        RefreshStats(cars, fastest);

        if (Over && !_resultShown)
        {
            _resultShown = true;
            AddChild(ResultCard());
        }

        var told = _race.Messages.Where(m => m.Time <= _time).OrderBy(m => m.Time).ToList();
        var news = told.Where(m => m.DriverId != "").TakeLast(_tower.Count > 14 ? 1 : 3).Reverse().ToList();
        // The latest word on the weather stays at the top, so it is not pushed out by retirements.
        if (told.LastOrDefault(m => m.DriverId == "") is { } weather)
            news.Insert(0, weather);
        while (_news.GetChildCount() < Math.Max(1, news.Count))
            _news.AddChild(new Label { ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(RightWidth - 40, 0) });
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

    /// <summary>The finishing order and the team's money, over a dimmed screen, with a way back to the season.</summary>
    private Control ResultCard()
    {
        var result = _race.Result();
        var team = RaceRequest.PlayerTeamId;
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        shade.AddChild(center);
        var card = Card();
        // Wider for a 24-hour race, so two drivers' names fit on a line.
        card.CustomMinimumSize = new Vector2(_race.Event.IsEndurance ? 640 : 520, 0);
        center.AddChild(card);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        card.AddChild(box);

        box.AddChild(new Label { Text = "RESULT", ThemeTypeVariation = "Overline" });
        box.AddChild(new Label { Text = _race.Event.Name, ThemeTypeVariation = "Title" });
        var winner = result.Classification[0];
        box.AddChild(new Label
        {
            Text = _race.Event.IsEndurance
                ? $"{winner.Entry.Names} win for {winner.Entry.Team.Name}: {winner.LapsCompleted} laps, {winner.LapsCompleted * _race.Circuit.LapKm:N0} km in {_race.Event.Hours} hours"
                : $"{winner.Entry.Driver.Name} wins for {winner.Entry.Team.Name} in {Clock(winner.TotalTime)}",
            ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        box.AddChild(new HSeparator());
        var header = new HBoxContainer();
        header.AddChild(new Label { Text = "", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        header.AddChild(new Label { Text = "YOUR PRIZES", ThemeTypeVariation = "Overline", HorizontalAlignment = HorizontalAlignment.Right });
        box.AddChild(header);

        // The finishing order scrolls when a full grid does not fit the window.
        var rowHeight = 28;
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            // A visible scrollbar, so the retirements below the fold are not missed.
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways,
            CustomMinimumSize = new Vector2(0, Math.Min(result.Classification.Count * rowHeight, GetViewportRect().Size.Y - 300)),
        };
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 6);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(list);
        box.AddChild(scroll);
        if (result.Classification.Count * rowHeight > scroll.CustomMinimumSize.Y)
            box.AddChild(new Label { Text = $"Scroll for the rest of the {result.Classification.Count} cars.", ThemeTypeVariation = "Muted", HorizontalAlignment = HorizontalAlignment.Center });

        for (var i = 0; i < result.Classification.Count; i++)
        {
            var c = result.Classification[i];
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 10);
            var mine = c.Entry.Team.Id == team;
            line.AddChild(new Label { Text = c.Finished ? (i + 1).ToString() : "–", CustomMinimumSize = new Vector2(24, 0), HorizontalAlignment = HorizontalAlignment.Right });
            line.AddChild(new ColorRect { Color = TrackView.TeamColor(c.Entry.Team.Id), CustomMinimumSize = new Vector2(4, 18), SizeFlagsVertical = SizeFlags.ShrinkCenter });
            line.AddChild(new Label { Text = c.Entry.Names, TooltipText = FullNames(c.Entry), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, Modulate = mine ? UiTheme.Accent : Colors.White });
            line.AddChild(new Label { Text = c.Entry.Team.Name, ThemeTypeVariation = "Muted", CustomMinimumSize = new Vector2(120, 0) });
            var status = c.Retirement?.Cause switch
            {
                RetirementCause.MechanicalFailure => $"out, lap {c.Retirement.Lap}",
                RetirementCause.Crash => $"crashed, lap {c.Retirement.Lap}",
                RetirementCause.FatalCrash => "killed",
                RetirementCause.OutOfFuel => $"no fuel, lap {c.Retirement.Lap}",
                _ when i == 0 && _race.Event.IsEndurance => $"{c.LapsCompleted} laps",
                _ when i == 0 => Clock(c.TotalTime),
                _ when c.LapsCompleted < winner.LapsCompleted => $"+{winner.LapsCompleted - c.LapsCompleted} lap{(winner.LapsCompleted - c.LapsCompleted > 1 ? "s" : "")}",
                _ => $"+{c.TotalTime - winner.TotalTime:0.0}s",
            };
            var prize = c.Finished && mine ? Finance.Prize(_race.Event, i + 1) : 0;
            line.AddChild(new Label { Text = status, ThemeTypeVariation = "Muted", CustomMinimumSize = new Vector2(96, 0), HorizontalAlignment = HorizontalAlignment.Right });
            line.AddChild(new Label { Text = prize > 0 ? $"£{prize:N0}" : "", Modulate = UiTheme.Good, CustomMinimumSize = new Vector2(64, 0), HorizontalAlignment = HorizontalAlignment.Right });
            list.AddChild(line);
        }

        box.AddChild(new HSeparator());
        var starters = result.Classification.Count(c => c.Entry.Team.Id == team);
        var prizes = result.Classification.Select((c, i) => c.Finished && c.Entry.Team.Id == team ? Finance.Prize(_race.Event, i + 1) : 0).Sum();
        var start = (Session.Career?.StartMoney(_race.Event) ?? _race.Event.StartMoney) * starters;
        var bottom = new HBoxContainer();
        bottom.AddChild(new Label
        {
            Text = $"Start money £{start:N0} + prizes £{prizes:N0} = £{start + prizes:N0}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center,
        });
        var next = new Button { Text = "Back to the season  ›", ThemeTypeVariation = "Accent" };
        next.Pressed += () =>
        {
            next.Disabled = true;
            Session.Career?.FinishRace(_race);
            GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        };
        bottom.AddChild(next);
        box.AddChild(bottom);
        return shade;
    }

    /// <summary>Gap to the leader: whole laps when lapped, otherwise roughly in seconds.</summary>
    private static string Gap(LiveCar leader, LiveCar car, double averageLap)
    {
        var laps = leader.Distance - car.Distance;
        return laps >= 1 ? $"+{Math.Floor(laps)} lap{(laps >= 2 ? "s" : "")}" : $"+{laps * averageLap:0.0}s";
    }

    /// <summary>
    /// The statistics screen, over the circuit while the race runs on: the fastest lap, the
    /// leader's pace, the player's cars and the toll so far, then a table of every car with its
    /// team, car, places gained from the grid, laps, last and best lap, stops and time in the pits.
    /// </summary>
    private Control StatsScreen()
    {
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.75f), MouseFilter = MouseFilterEnum.Stop, Visible = false };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        foreach (var (side, size) in new[] { ("left", 40), ("right", 40), ("top", 90), ("bottom", 90) })
            margin.AddThemeConstantOverride("margin_" + side, size);
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.MouseFilter = MouseFilterEnum.Ignore;
        shade.AddChild(margin);
        var card = Card();
        margin.AddChild(card);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        card.AddChild(box);

        var top = new HBoxContainer();
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddChild(new Label { Text = "RACE STATISTICS", ThemeTypeVariation = "Overline" });
        titles.AddChild(new Label { Text = _race.Event.Name, ThemeTypeVariation = "Heading" });
        top.AddChild(titles);
        var close = new Button { Text = "Back to the race", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        close.Pressed += () => _statsButton.ButtonPressed = false;
        top.AddChild(close);
        box.AddChild(top);

        _stats = new VBoxContainer();
        _stats.AddThemeConstantOverride("separation", 2);
        box.AddChild(_stats);
        box.AddChild(new HSeparator());

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        var table = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        table.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(table);
        table.AddChild(StatLine(head: true).Line);
        foreach (var _ in _race.Cars())
        {
            var (line, labels) = StatLine(head: false);
            table.AddChild(line);
            _statRows.Add(labels);
        }
        box.AddChild(scroll);
        return shade;
    }

    /// <summary>One line of the statistics table: the head, or a car's row of empty labels to fill.</summary>
    private static (HBoxContainer Line, Label[] Labels) StatLine(bool head)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 10);
        var labels = StatColumns.Select(c =>
        {
            var label = Small(new Label
            {
                Text = head ? c.Head : "", ThemeTypeVariation = head ? "Overline" : "", ClipText = true,
                HorizontalAlignment = c.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(c.Width, 0),
                SizeFlagsHorizontal = c.Width == 0 ? SizeFlags.ExpandFill : SizeFlags.Fill,
            }, head ? 10 : 13);
            line.AddChild(label);
            return label;
        }).ToArray();
        return (line, labels);
    }

    /// <summary>Fills the statistics screen, while it is open.</summary>
    private void RefreshStats(IReadOnlyList<LiveCar> cars, LiveCar? fastest)
    {
        if (!_statsScreen.Visible)
            return;
        var team = RaceRequest.PlayerTeamId;
        for (var i = 0; i < _statRows.Count && i < cars.Count; i++)
        {
            var c = cars[i];
            var gained = c.Grid - (i + 1);
            var cells = new[]
            {
                (i + 1).ToString(),
                FullNames(c.Entry),
                TeamName(c.Entry),
                c.Entry.Team.Car.Name,
                c.Grid.ToString(),
                gained > 0 ? $"▲{gained}" : gained < 0 ? $"▼{-gained}" : "–",
                c.LapsDone.ToString(),
                c.LastLap > 0 ? LapTime(c.LastLap) : "–",
                c.BestLap > 0 ? $"{LapTime(c.BestLap)} ({c.BestLapNumber})" : "–",
                c.PitStops.ToString(),
                c.PitSeconds > 0 ? $"{c.PitSeconds:0} s" : "–",
                c.Retirement?.Cause switch
                {
                    RetirementCause.MechanicalFailure => $"out lap {c.Retirement.Lap} · engine",
                    RetirementCause.Crash => $"out lap {c.Retirement.Lap} · crash",
                    RetirementCause.FatalCrash => "killed",
                    RetirementCause.OutOfFuel => $"out lap {c.Retirement.Lap} · fuel",
                    _ when c.Finished => "finished",
                    _ when c.InPits => "in the pits",
                    _ => "running",
                },
            };
            var row = _statRows[i];
            var mine = c.Entry.Team.Id == team;
            for (var k = 0; k < row.Length; k++)
            {
                row[k].Text = cells[k];
                row[k].Modulate = c.Retirement is not null ? new Color(1, 1, 1, 0.45f) : mine ? UiTheme.Accent : Colors.White;
            }
            row[5].Modulate = gained > 0 ? UiTheme.Good : gained < 0 ? UiTheme.Danger : row[5].Modulate;
            if (c == fastest)
                row[8].Modulate = FastestLap;
        }

        var lines = new List<(string Text, Color Color)>();
        var lapKm = _race.Circuit.LapKm;
        lines.Add(fastest is null
            ? ("Fastest lap: none yet", Colors.White)
            : ($"Fastest lap: {Short(fastest.Entry.Driver.Name)}, {LapTime(fastest.BestLap)} on lap {fastest.BestLapNumber} · {lapKm / fastest.BestLap * 3600:0} km/h", FastestLap));
        var leader = cars[0];
        if (_time > 0 && leader.Distance > 0.05)
        {
            var leaderTime = leader.Finished ? leader.FinishedAt : _time;
            lines.Add(($"Leader's average: {leader.Distance * lapKm / leaderTime * 3600:0} km/h"
                + (leader.LastLap > 0 ? $" · last lap {LapTime(leader.LastLap)}" : ""), Colors.White));
        }
        var running = cars.Count(c => c.Running || c.Finished);
        lines.Add(($"Running {running} of {cars.Count} · pit stops {cars.Sum(c => c.PitStops)} · out {cars.Count - running}", Colors.White));

        for (var i = 0; i < cars.Count; i++)
        {
            var c = cars[i];
            if (c.Entry.Team.Id != team)
                continue;
            var gained = c.Grid - (i + 1);
            var places = !c.Running && !c.Finished ? "out"
                : gained > 0 ? $"up {gained}" : gained < 0 ? $"down {-gained}" : "no change";
            lines.Add(($"{Short(c.Entry.Driver.Name)}: from {Ordinal(c.Grid)} to {Ordinal(i + 1)} ({places}) · best {(c.BestLap > 0 ? LapTime(c.BestLap) : "–")}"
                + $" · {c.PitStops} stop{(c.PitStops == 1 ? "" : "s")}{(c.PitSeconds > 0 ? $", {c.PitSeconds:0} s" : "")}", UiTheme.Accent));
        }

        while (_stats.GetChildCount() < lines.Count)
            _stats.AddChild(Small(new Label(), 13));
        for (var i = 0; i < _stats.GetChildCount(); i++)
        {
            var label = (Label)_stats.GetChild(i);
            label.Visible = i < lines.Count;
            if (i < lines.Count)
            {
                label.Text = lines[i].Text;
                label.Modulate = lines[i].Color;
            }
        }
    }

    /// <summary>The team for the tower: a private entrant shows its car's make instead.</summary>
    private string TeamName(RaceEntry entry) =>
        entry.Team.Privateer ? "Private · " + entry.Team.Car.Name.Split(' ')[0] : entry.Team.Name;

    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    /// <summary>A lap time as minutes, seconds and tenths, e.g. 4:12.3.</summary>
    private static string LapTime(double seconds)
    {
        var tenths = (int)Math.Round(seconds * 10);
        return $"{tenths / 600}:{tenths % 600 / 10:00}.{tenths % 10}";
    }

    /// <summary>Both drivers' full names, for a tooltip.</summary>
    private static string FullNames(RaceEntry entry) => string.Join(" and ", entry.Drivers.Select(d => d.Name));

    /// <summary>
    /// How dark the circuit is: dusk deepens over the hour around half past nine, and dawn
    /// comes over the hour around half past four, the hours the race counts as night.
    /// </summary>
    private float Darkness(double time)
    {
        if (!_race.Event.IsEndurance)
            return 0;
        var hour = LiveRace.HourOfDay(time);
        var dusk = Mathf.Clamp((float)(hour - (LiveRace.DuskHour - 0.5)), 0, 1);
        var dawn = Mathf.Clamp((float)(LiveRace.DawnHour + 0.5 - hour), 0, 1);
        return hour >= 12 ? dusk : dawn;
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
