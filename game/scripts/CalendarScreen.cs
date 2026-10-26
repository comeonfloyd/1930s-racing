using System;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// First playable screen: pick a team, then choose which 1930 races to enter.
/// Built in code for now; it moves into a designed scene once the look is settled.
/// </summary>
public partial class CalendarScreen : Control
{
    private const int StartYear = 1930;

    private GameData _data = null!;

    // Kept across scene changes, so coming back from a race keeps the team and entries.
    private static Career? _career;
    private VBoxContainer _eventList = null!;
    private Label _status = null!;

    public override void _Ready()
    {
        try
        {
            _data = DataLoader.Load();
        }
        catch (Exception e)
        {
            // Show data problems on screen instead of leaving an empty window.
            GD.PushError(e.ToString());
            AddChild(new Label { Text = $"Could not load game data:\n{e.Message}", Position = new Vector2(24, 24) });
            return;
        }

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 24);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);

        column.AddChild(new Label { Text = $"{StartYear} season", ThemeTypeVariation = "HeaderLarge" });

        var teamRow = new HBoxContainer();
        teamRow.AddChild(new Label { Text = "Run a team:" });
        foreach (var team in _data.Teams.Values.Where(t => t.Playable))
        {
            var button = new Button { Text = team.Name };
            button.Pressed += () => StartCareer(team.Id);
            teamRow.AddChild(button);
        }
        column.AddChild(teamRow);

        _status = new Label { Text = "Pick a team to start." };
        column.AddChild(_status);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _eventList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_eventList);
        column.AddChild(scroll);

        if (_career is not null)
        {
            _status.Text = $"{_career.Team.Name} · cash £{_career.Cash:N0}";
            Refresh();
        }
    }

    private void StartCareer(string teamId)
    {
        _career = new Career(_data, teamId, StartYear);
        _status.Text = $"{_career.Team.Name} · cash £{_career.Cash:N0}";
        Refresh();
    }

    private void Refresh()
    {
        foreach (var child in _eventList.GetChildren())
            child.QueueFree();
        if (_career is null)
            return;

        foreach (var ev in _career.CurrentSeason.Events)
        {
            var circuit = _data.Circuits[ev.CircuitId];
            var entered = _career.Entries.Contains(ev);
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = ev.Date.ToString("d MMM"), CustomMinimumSize = new Vector2(80, 0) });
            row.AddChild(new Label
            {
                Text = $"{ev.Name} · {circuit.Name}, {circuit.Country}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });

            if (entered)
            {
                row.AddChild(new Label { Text = "Entered" });
                var watch = new Button { Text = "Go to race" };
                watch.Pressed += () => WatchRace(ev);
                row.AddChild(watch);
            }
            else
            {
                var travel = _career.TravelTo(ev);
                row.AddChild(new Label { Text = $"{travel.Days} days, £{travel.Cost:N0}" + (travel.CrossesAtlantic ? " by liner" : "") });
                var enter = new Button { Text = "Enter" };
                enter.Pressed += () => OnEnter(ev.Id);
                row.AddChild(enter);
            }
            _eventList.AddChild(row);
        }
    }

    private void WatchRace(RaceEvent ev)
    {
        RaceRequest.Race = new LiveRace(ev, _data.Circuits[ev.CircuitId], RaceSim.FullGrid(_data), Random.Shared.Next(),
            manualTeams: new[] { _career!.Team.Id });
        RaceRequest.PlayerTeamId = _career.Team.Id;
        GetTree().ChangeSceneToFile("res://scenes/Race.tscn");
    }

    private void OnEnter(string eventId)
    {
        var result = _career!.Enter(eventId);
        _status.Text = result switch
        {
            EntryResult.Entered => $"{_career.Team.Name} · entered {_career.Entries.Count} race{(_career.Entries.Count == 1 ? "" : "s")}",
            EntryResult.CannotArriveInTime => "The cars cannot get there in time from the previous race.",
            EntryResult.EventPassed => "That race has already been run.",
            _ => result.ToString(),
        };
        Refresh();
    }
}
