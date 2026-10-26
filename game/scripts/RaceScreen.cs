using System;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>The race the calendar asked to show. Set before changing to the race scene.</summary>
public static class RaceRequest
{
    public static RaceResult? Result { get; set; }
}

/// <summary>Plays a simulated race on the drawn circuit with a timing tower and speed controls.</summary>
public partial class RaceScreen : Control
{
    private static readonly int[] Speeds = { 10, 60, 240 };

    private RaceReplay _replay = null!;
    private TrackView _track = null!;
    private Label _header = null!;
    private Label _tower = null!;
    private double _time;
    private int _speed = 60;
    private bool _paused;

    public override void _Ready()
    {
        var result = RaceRequest.Result;
        if (result is null)
        {
            GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
            return;
        }
        _replay = new RaceReplay(result);

        _track = new TrackView();
        AddChild(_track);
        // A stable seed per circuit, so the scenery is the same every visit.
        _track.SetTrack(TrackShape.For(result.Circuit.Id), result.Circuit.Id.Sum(ch => ch));

        var panel = new VBoxContainer { Position = new Vector2(16, 16), CustomMinimumSize = new Vector2(300, 0) };
        panel.AddThemeConstantOverride("separation", 8);
        AddChild(panel);

        panel.AddChild(new Label { Text = $"{result.Event.Name}\n{result.Circuit.Name}", ThemeTypeVariation = "HeaderMedium" });
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
        skip.Pressed += () => _time = _replay.Duration;
        controls.AddChild(skip);
        panel.AddChild(controls);

        _tower = new Label();
        panel.AddChild(_tower);

        var back = new Button { Text = "Back to calendar" };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        panel.AddChild(back);

        Resized += Layout;
        Layout();
        Refresh();
    }

    private void Layout()
    {
        var size = GetViewportRect().Size;
        const float panelWidth = 340;
        _track.Area = new Rect2(panelWidth, 16, size.X - panelWidth - 16, size.Y - 32);
        _track.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_replay is null || _paused || _time >= _replay.Duration)
            return;
        _time = Math.Min(_replay.Duration, _time + delta * _speed);
        Refresh();
    }

    private void Refresh()
    {
        var cars = _replay.At(_time);
        _track.SetCars(cars);

        var laps = _replay.Result.Event.Laps;
        var leaderLap = Math.Min(laps, (int)Math.Floor(cars[0].Distance) + 1);
        var finished = _time >= _replay.Duration;
        _header.Text = finished
            ? $"Chequered flag · {Clock(_time)}"
            : $"Lap {leaderLap} of {laps} · {Clock(_time)} · {(_paused ? "paused" : $"{_speed}x")}";

        var winner = _replay.Result.Classification[0];
        var averageLap = winner.LapsCompleted == 0 ? 60 : winner.TotalTime / winner.LapsCompleted;
        _tower.Text = string.Join("\n", cars.Select((c, i) =>
        {
            var gap = i == 0 ? "" : Gap(cars[0], c, averageLap);
            var status = c.Retirement?.Cause switch
            {
                RetirementCause.MechanicalFailure => "out, mechanical",
                RetirementCause.Crash => "out, crashed",
                RetirementCause.FatalCrash => "killed",
                _ => c.Finished ? "finished" + (i == 0 ? "" : " " + gap) : gap,
            };
            return $"{i + 1,2}. {c.Entry.Driver.Name} ({c.Entry.Team.Name})  {status}";
        }));
    }

    /// <summary>Gap to the leader: whole laps when lapped, otherwise roughly in seconds.</summary>
    private static string Gap(CarPosition leader, CarPosition car, double averageLap)
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
