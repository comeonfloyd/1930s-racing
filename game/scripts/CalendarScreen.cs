using System;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// First playable screen: pick a team, then choose which 1930 races to enter.
/// Cards over a darkened circuit painting, in the game's <see cref="UiTheme"/>.
/// </summary>
public partial class CalendarScreen : Control
{
    private const int StartYear = 1930;

    private GameData _data = null!;

    // Kept across scene changes, so coming back from a race keeps the team and entries.
    private static Career? _career;
    private VBoxContainer _body = null!;
    private Label _status = null!;
    private PanelContainer _statusChip = null!;

    public override void _Ready()
    {
        Theme = UiTheme.Get();
        AddChild(Backdrop());

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
        foreach (var (side, value) in new[] { ("left", 56), ("right", 56), ("top", 36), ("bottom", 28) })
            margin.AddThemeConstantOverride($"margin_{side}", value);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 18);
        margin.AddChild(column);

        var header = new HBoxContainer();
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        titles.AddChild(new Label { Text = "GRAND PRIX MANAGER · 1930–1939", ThemeTypeVariation = "Overline" });
        titles.AddChild(new Label { Text = $"The {StartYear} season", ThemeTypeVariation = "Title" });
        header.AddChild(titles);
        _statusChip = new PanelContainer { ThemeTypeVariation = "Card", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _status = new Label();
        _statusChip.AddChild(_status);
        header.AddChild(_statusChip);
        column.AddChild(header);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_body);
        column.AddChild(scroll);

        Refresh();
        if (DevShot.Active)
            RunDevShot();
    }

    private void RunDevShot()
    {
        if (DevShot.Arg("team") is { } team)
        {
            _career = new Career(_data, team, StartYear);
            Refresh();
        }
        DevShot.Capture(this, "calendar.png", 1.5, () =>
        {
            var ev = _career?.CurrentSeason.Events.FirstOrDefault(e => e.Id == DevShot.Arg("race"));
            if (ev is null)
            {
                GetTree().Quit();
                return;
            }
            _career!.Enter(ev.Id);
            WatchRace(ev);
        });
    }

    private static Control Backdrop()
    {
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        var colour = new ColorRect { Color = UiTheme.Background, MouseFilter = MouseFilterEnum.Ignore };
        colour.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddChild(colour);
        if (TrackView.LoadTexture("res://art/tracks/monza.jpg") is { } art)
        {
            var picture = new TextureRect
            {
                Texture = art, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore,
            };
            picture.SetAnchorsPreset(LayoutPreset.FullRect);
            root.AddChild(picture);
        }
        var shade = new ColorRect { Color = new Color(UiTheme.Background, 0.82f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddChild(shade);
        return root;
    }

    private void Refresh()
    {
        foreach (var child in _body.GetChildren())
            child.QueueFree();

        if (_career is null)
        {
            _status.Text = "Choose a team to begin";
            _body.AddChild(new Label { Text = "CHOOSE YOUR TEAM", ThemeTypeVariation = "Overline" });
            var teams = new HBoxContainer();
            teams.AddThemeConstantOverride("separation", 16);
            foreach (var team in _data.Teams.Values.Where(t => t.Playable))
                teams.AddChild(TeamCard(team));
            _body.AddChild(teams);
            return;
        }

        _status.Text = $"{_career.Team.Name}  ·  £{_career.Cash:N0}  ·  {_career.Entries.Count} entered";
        _body.AddChild(TeamBar(_career.Team));
        _body.AddChild(new Label { Text = "CALENDAR", ThemeTypeVariation = "Overline" });
        foreach (var ev in _career.CurrentSeason.Events)
            _body.AddChild(EventCard(ev));
    }

    private Control TeamCard(Team team)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card", CustomMinimumSize = new Vector2(320, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(new ColorRect { Color = TrackView.TeamColor(team.Id), CustomMinimumSize = new Vector2(0, 4) });
        box.AddChild(new Label { Text = team.Nationality.ToUpperInvariant(), ThemeTypeVariation = "Overline" });
        box.AddChild(new Label { Text = team.Name, ThemeTypeVariation = "Big" });
        box.AddChild(new Label { Text = $"{team.Car.Name} · {team.Car.Horsepower} hp · {team.Car.WeightKg} kg", ThemeTypeVariation = "Muted" });
        var drivers = string.Join(", ", team.DriverIds.Select(id => _data.Drivers[id].Name));
        box.AddChild(new Label { Text = drivers, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        box.AddChild(new Label { Text = $"Budget £{team.StartingCash:N0} · prestige {team.Prestige}", ThemeTypeVariation = "Muted" });
        var run = new Button { Text = "Run this team", ThemeTypeVariation = "Accent" };
        run.Pressed += () =>
        {
            _career = new Career(_data, team.Id, StartYear);
            Refresh();
        };
        box.AddChild(run);
        card.AddChild(box);
        return card;
    }

    private Control TeamBar(Team team)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(new ColorRect { Color = TrackView.TeamColor(team.Id), CustomMinimumSize = new Vector2(5, 40), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        titles.AddChild(new Label { Text = "YOUR TEAM", ThemeTypeVariation = "Overline" });
        titles.AddChild(new Label { Text = team.Name, ThemeTypeVariation = "Heading" });
        titles.AddChild(new Label { Text = $"{team.Car.Name} · {team.Car.Horsepower} hp · " + string.Join(", ", team.DriverIds.Select(id => _data.Drivers[id].Name)), ThemeTypeVariation = "Muted" });
        row.AddChild(titles);
        var change = new Button { Text = "Change team", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        change.Pressed += () =>
        {
            _career = null;
            Refresh();
        };
        row.AddChild(change);
        card.AddChild(row);
        return card;
    }

    private Control EventCard(RaceEvent ev)
    {
        var circuit = _data.Circuits[ev.CircuitId];
        var entered = _career!.Entries.Contains(ev);
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);

        // Date as a calendar tile.
        var tile = new PanelContainer { ThemeTypeVariation = "Badge", CustomMinimumSize = new Vector2(58, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var date = new VBoxContainer();
        date.AddThemeConstantOverride("separation", -4);
        date.AddChild(new Label { Text = ev.Date.Day.ToString(), ThemeTypeVariation = "Big", HorizontalAlignment = HorizontalAlignment.Center });
        date.AddChild(new Label { Text = ev.Date.ToString("MMM").ToUpperInvariant(), ThemeTypeVariation = "Overline", HorizontalAlignment = HorizontalAlignment.Center });
        tile.AddChild(date);
        row.AddChild(tile);

        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 2);
        titles.AddChild(new Label { Text = ev.Name, ThemeTypeVariation = "Heading" });
        titles.AddChild(new Label { Text = $"{circuit.Name}, {circuit.Country} · {ev.Laps} laps · {ev.Laps * circuit.LapKm:0} km", ThemeTypeVariation = "Muted" });
        var facts = new HBoxContainer();
        facts.AddThemeConstantOverride("separation", 6);
        facts.AddChild(Chip($"Start money £{ev.StartMoney:N0}"));
        facts.AddChild(Chip($"Prizes £{ev.PrizeFund:N0}"));
        facts.AddChild(Chip($"Prestige {ev.Prestige}"));
        if (!entered)
        {
            var travel = _career.TravelTo(ev);
            facts.AddChild(Chip($"{travel.Days} days, £{travel.Cost:N0}" + (travel.CrossesAtlantic ? " by liner" : " travel")));
        }
        titles.AddChild(facts);
        row.AddChild(titles);

        if (entered)
        {
            var tag = new PanelContainer { ThemeTypeVariation = "Badge", SizeFlagsVertical = SizeFlags.ShrinkCenter };
            tag.AddChild(new Label { Text = "Entered", Modulate = UiTheme.Good });
            row.AddChild(tag);
            var go = new Button { Text = "Go to race", ThemeTypeVariation = "Accent", SizeFlagsVertical = SizeFlags.ShrinkCenter };
            go.Pressed += () => WatchRace(ev);
            row.AddChild(go);
        }
        else
        {
            var enter = new Button { Text = "Enter", SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(100, 0) };
            enter.Pressed += () => OnEnter(ev.Id);
            row.AddChild(enter);
        }
        card.AddChild(row);
        return card;
    }

    private static Control Chip(string text)
    {
        var chip = new PanelContainer { ThemeTypeVariation = "Badge" };
        chip.AddChild(new Label { Text = text, ThemeTypeVariation = "Muted" });
        return chip;
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
        Refresh();
        if (result != EntryResult.Entered)
        {
            _status.Text = result switch
            {
                EntryResult.CannotArriveInTime => "The cars cannot get there in time from the previous race.",
                EntryResult.EventPassed => "That race has already been run.",
                _ => result.ToString(),
            };
        }
    }
}
