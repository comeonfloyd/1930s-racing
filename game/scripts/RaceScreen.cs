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
/// Runs a race on the drawn circuit with a timing tower, speed controls and the player's pit
/// wall: pace orders and pit stop calls for each of the team's cars.
/// </summary>
public partial class RaceScreen : Control
{
    private static readonly int[] Speeds = { 10, 60, 240 };
    private static readonly PaceOrder[] Paces = { PaceOrder.Nurse, PaceOrder.Hold, PaceOrder.Race, PaceOrder.Push };

    private LiveRace _race = null!;
    private TrackView _track = null!;
    private Control _frame = null!;
    private ScrollContainer _side = null!;
    private OptionButton _follow = null!;
    private readonly List<string?> _followChoices = new();
    private Label _header = null!;
    private Label _tower = null!;
    private Label _news = null!;
    private readonly List<PitWallRow> _pitWall = new();
    private double _time;
    private int _speed = 60;
    private bool _paused;

    /// <summary>The controls for one of the player's cars.</summary>
    private sealed record PitWallRow(string DriverId, Label Status, OptionButton Pace, Button Pit);

    public override void _Ready()
    {
        var race = RaceRequest.Race;
        if (race is null)
        {
            GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
            return;
        }
        _race = race;

        // The circuit sits in a clipping frame so it can be zoomed and panned under the panel.
        _frame = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Stop };
        AddChild(_frame);
        _frame.GuiInput += OnTrackInput;
        _track = new TrackView();
        _frame.AddChild(_track);
        // A stable seed per circuit, so the scenery is the same every visit.
        _track.SetTrack(TrackShape.For(race.Circuit.Id), race.Circuit.Id, race.Circuit.Id.Sum(ch => ch));

        // The side panel scrolls when a small window can't fit the tower and the pit wall.
        _side = new ScrollContainer { Position = new Vector2(16, 16), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(_side);
        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        panel.AddThemeConstantOverride("separation", 8);
        _side.AddChild(panel);

        panel.AddChild(new Label { Text = $"{race.Event.Name}\n{race.Circuit.Name}", ThemeTypeVariation = "HeaderMedium" });
        _header = new Label();
        panel.AddChild(_header);

        var controls = new HBoxContainer();
        foreach (var s in Speeds)
        {
            var b = new Button { Text = $"{s}x" };
            b.Pressed += () => { _speed = s; _paused = false; };
            controls.AddChild(b);
        }
        var pause = new Button { Text = "Pause" };
        pause.Pressed += () => _paused = !_paused;
        controls.AddChild(pause);
        var skip = new Button { Text = "Skip to end" };
        skip.Pressed += () =>
        {
            _race.RunToEnd();
            _time = _race.FinishTime;
            Refresh();
        };
        controls.AddChild(skip);
        panel.AddChild(controls);
        panel.AddChild(CameraControls());

        AddPitWall(panel);

        _tower = new Label();
        panel.AddChild(_tower);
        _news = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0) };
        panel.AddChild(_news);

        var back = new Button { Text = "Back to calendar" };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        panel.AddChild(back);

        Resized += Layout;
        Layout();
        Refresh();
    }

    private void AddPitWall(VBoxContainer panel)
    {
        var team = RaceRequest.PlayerTeamId;
        var mine = _race.Cars().Where(c => c.Entry.Team.Id == team).Select(c => c.Entry).ToList();
        if (mine.Count == 0)
            return;

        panel.AddChild(new Label { Text = $"Pit wall · {mine[0].Team.Name}", ThemeTypeVariation = "HeaderSmall" });
        foreach (var entry in mine)
        {
            var id = entry.Driver.Id;
            var box = new VBoxContainer();
            var status = new Label();
            box.AddChild(status);

            var row = new HBoxContainer();
            var pace = new OptionButton { TooltipText = "Pace order, shown on the pit board next time the car passes" };
            foreach (var p in Paces)
                pace.AddItem(p.ToString());
            pace.Selected = Array.IndexOf(Paces, PaceOrder.Race);
            pace.ItemSelected += i => _race.SetPace(id, Paces[i]);
            row.AddChild(pace);

            var pit = new Button { Text = "Call in", TooltipText = "Stop for fuel and rear tyres at the end of this lap" };
            pit.Pressed += () =>
            {
                var car = _race.Cars().First(c => c.Entry.Driver.Id == id);
                _race.CallPitStop(id, !car.PitCalled);
                Refresh();
            };
            row.AddChild(pit);
            box.AddChild(row);
            panel.AddChild(box);
            _pitWall.Add(new PitWallRow(id, status, pace, pit));
        }
    }

    private HBoxContainer CameraControls()
    {
        var row = new HBoxContainer();
        var zoomOut = new Button { Text = "−", TooltipText = "Zoom out (or use the mouse wheel)" };
        zoomOut.Pressed += () => _track.Zoom /= 1.5f;
        var zoomIn = new Button { Text = "+", TooltipText = "Zoom in (or use the mouse wheel)" };
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
        return row;
    }

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

    private void Layout()
    {
        var size = GetViewportRect().Size;
        const float panelWidth = 400;
        _side.Size = new Vector2(panelWidth - 24, size.Y - 32);
        _frame.Position = new Vector2(panelWidth, 16);
        _frame.Size = new Vector2(size.X - panelWidth - 16, size.Y - 32);
        _track.Area = new Rect2(Vector2.Zero, _frame.Size);
        _track.Zoom = _track.Zoom;
    }

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
        _header.Text = Over
            ? $"Chequered flag · {Clock(_time)}"
            : $"Lap {leaderLap} of {laps} · {Clock(_time)} · {(_paused ? "paused" : $"{_speed}x")}";

        var averageLap = cars[0].Distance > 0.1 ? _time / cars[0].Distance : 60;
        _tower.Text = string.Join("\n", cars.Select((c, i) =>
        {
            var gap = i == 0 ? "" : Gap(cars[0], c, averageLap);
            var status = c.Retirement?.Cause switch
            {
                RetirementCause.MechanicalFailure => "out, mechanical",
                RetirementCause.Crash => "out, crashed",
                RetirementCause.FatalCrash => "killed",
                RetirementCause.OutOfFuel => "out, no fuel",
                _ when c.InPits => "in the pits",
                _ => c.Finished ? "finished" + (i == 0 ? "" : " " + gap) : gap,
            };
            return $"{i + 1,2}. {c.Entry.Driver.Name} ({c.Entry.Team.Name})  {status}";
        }));

        foreach (var row in _pitWall)
        {
            var position = cars.ToList().FindIndex(c => c.Entry.Driver.Id == row.DriverId);
            var car = cars[position];
            var fuelLaps = car.Fuel * LiveRace.TankRangeKm / _race.Circuit.LapKm;
            var warning = !car.Running ? "" : fuelLaps < 2 ? "  · LOW FUEL" : car.TyreWear > 0.8 ? "  · TYRES GOING" : "";
            row.Status.Text = $"P{position + 1} {car.Entry.Driver.Name} · fuel {fuelLaps:0} laps · tyres {100 - car.TyreWear * 100:0}%{warning}";
            row.Status.Modulate = warning.Length > 0 ? new Color(1, 0.6f, 0.4f) : Colors.White;
            row.Pit.Text = car.PitCalled ? "Cancel call" : "Call in";
            row.Pit.Disabled = !car.Running;
            row.Pace.Disabled = !car.Running;
        }

        var news = _race.Messages.Where(m => m.Time <= _time).OrderBy(m => m.Time).TakeLast(3).Reverse();
        _news.Text = string.Join("\n", news.Select(m => $"Lap {m.Lap}: {m.Text}"));
    }

    /// <summary>Gap to the leader: whole laps when lapped, otherwise roughly in seconds.</summary>
    private static string Gap(LiveCar leader, LiveCar car, double averageLap)
    {
        var laps = leader.Distance - car.Distance;
        return laps >= 1 ? $"+{Math.Floor(laps)} lap{(laps >= 2 ? "s" : "")}" : $"+{laps * averageLap:0.0}s";
    }

    private static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
    }
}
