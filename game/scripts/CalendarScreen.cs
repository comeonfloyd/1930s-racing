using System;
using System.Globalization;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// The season screen: pick a team, enter races, then play the season one race weekend at a
/// time. Shows each race's result once it is run, the team's money, and a summary at the end
/// of the season. Cards over a darkened circuit painting, in the game's <see cref="UiTheme"/>.
/// </summary>
public partial class CalendarScreen : Control
{
    private const float SideWidth = 340;
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    private GameData _data = null!;
    private VBoxContainer _body = null!;
    private VBoxContainer _side = null!;
    private Label _title = null!;
    private Label _status = null!;
    private Button _action = null!;
    private Label _notice = null!;
    private ScrollContainer _calendarScroll = null!;
    private TravelMap _map = null!;
    private HBoxContainer _tabs = null!;
    private Button _calendarTab = null!;
    private Button _mapTab = null!;

    // Kept across scene changes, so coming back from a race shows the same view.
    private static bool _showMap;

    private static Career? Career
    {
        get => Session.Career;
        set => Session.Career = value;
    }

    public override void _Ready()
    {
        Theme = UiTheme.Get();
        AddChild(Backdrop());

        try
        {
            _data = Session.Data ??= DataLoader.Load();
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
        foreach (var (side, value) in new[] { ("left", 48), ("right", 48), ("top", 32), ("bottom", 24) })
            margin.AddThemeConstantOverride($"margin_{side}", value);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        margin.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        titles.AddChild(new Label { Text = "GRAND PRIX MANAGER · 1930–1939", ThemeTypeVariation = "Overline" });
        _title = new Label { ThemeTypeVariation = "Title" };
        titles.AddChild(_title);
        header.AddChild(titles);
        var chip = new PanelContainer { ThemeTypeVariation = "Card", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _status = new Label();
        chip.AddChild(_status);
        header.AddChild(chip);
        _action = new Button { ThemeTypeVariation = "Accent", SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(0, 44) };
        _action.Pressed += OnAction;
        header.AddChild(_action);
        column.AddChild(header);

        _notice = new Label { Modulate = new Color(1, 0.7f, 0.6f), Visible = false };
        column.AddChild(_notice);

        _tabs = new HBoxContainer();
        _tabs.AddThemeConstantOverride("separation", 4);
        _calendarTab = new Button { Text = "Calendar", ToggleMode = true, ThemeTypeVariation = "Segment", CustomMinimumSize = new Vector2(110, 0) };
        _mapTab = new Button { Text = "Travel map", ToggleMode = true, ThemeTypeVariation = "Segment", CustomMinimumSize = new Vector2(110, 0) };
        _calendarTab.Pressed += () => { _showMap = false; Refresh(); };
        _mapTab.Pressed += () => { _showMap = true; Refresh(); };
        _tabs.AddChild(_calendarTab);
        _tabs.AddChild(_mapTab);
        column.AddChild(_tabs);

        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 16);
        column.AddChild(row);

        _calendarScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 10);
        _calendarScroll.AddChild(_body);
        row.AddChild(_calendarScroll);

        _map = new TravelMap { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, EnterRace = OnEnter };
        row.AddChild(_map);

        var sideScroll = new ScrollContainer { CustomMinimumSize = new Vector2(SideWidth, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _side.AddThemeConstantOverride("separation", 12);
        sideScroll.AddChild(_side);
        row.AddChild(sideScroll);

        Refresh();
        if (DevShot.Active)
            RunDevShot();
    }

    private void RunDevShot()
    {
        if (DevShot.Arg("team") is { } team && Career is null)
        {
            Career = new Career(_data, team, Session.StartYear);
            Career.StartProject(Upgrade.Engine);
            if (DevShot.Arg("race") is { } race)
                Career.Enter(race);
            Refresh();
        }
        // Before the race: calendar.png. Back from it, with results in: season.png.
        var file = Career?.Results.Any(r => r.Entered) == true ? "season.png" : "calendar.png";
        DevShot.Capture(this, file, 1.5, () =>
        {
            if (Career?.NextRace is null)
            {
                Shutdown.Quit(GetTree());
                return;
            }
            _showMap = true;
            Refresh();
            DevShot.Capture(this, "map.png", 1, () =>
            {
                _showMap = false;
                GoToRace();
            });
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
        var shade = new ColorRect { Color = new Color(UiTheme.Background, 0.84f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddChild(shade);
        return root;
    }

    // ------------------------------------------------------------------ screen

    private void Refresh()
    {
        foreach (var child in _body.GetChildren().Concat(_side.GetChildren()))
            child.QueueFree();

        _tabs.Visible = Career is not null;
        _calendarScroll.Visible = Career is null || !_showMap;
        _map.Visible = Career is not null && _showMap;
        _calendarTab.SetPressedNoSignal(!_showMap);
        _mapTab.SetPressedNoSignal(_showMap);

        if (Career is not { } career)
        {
            _title.Text = $"The {Session.StartYear} season";
            _status.Text = "Choose a team to begin";
            _action.Visible = false;
            _body.AddChild(new Label { Text = "CHOOSE YOUR TEAM", ThemeTypeVariation = "Overline" });
            var teams = new HBoxContainer();
            teams.AddThemeConstantOverride("separation", 16);
            foreach (var team in _data.Teams.Values.Where(t => t.Playable))
                teams.AddChild(TeamCard(team));
            _body.AddChild(teams);
            _side.AddChild(HowToPlay());
            return;
        }

        _title.Text = $"The {career.CurrentSeason.Year} season";
        _status.Text = $"{career.Today.ToString("d MMMM yyyy", English)}  ·  £{career.Cash:N0}";
        _action.Visible = true;
        _action.Disabled = false;
        if (career.NextRace is { } next)
            _action.Text = $"Go to {next.Name}  ›";
        else if (!career.SeasonOver)
            _action.Text = "Play out the season  ›";
        else if (career.HasNextSeason)
            _action.Text = $"Start the {career.Today.Year + 1} season  ›";
        else
        {
            _action.Text = "More seasons coming";
            _action.Disabled = true;
        }
        _action.TooltipText = career.NextRace is null && !career.SeasonOver
            ? "No more races entered: run the rest of the season without your team"
            : "";

        foreach (var ev in career.CurrentSeason.Events)
            _body.AddChild(EventCard(career, ev));
        _map.SetSeason(_data, career);

        if (career.SeasonOver)
            _side.AddChild(SummaryCard(career));
        _side.AddChild(TeamCard(career));
        _side.AddChild(DrawingOfficeCard(career));
        _side.AddChild(MoneyCard(career));
        if (career.News.Count > 0)
            _side.AddChild(NewsCard(career));
    }

    private void OnAction()
    {
        if (Career is not { } career)
            return;
        _notice.Visible = false;
        if (career.NextRace is not null)
            GoToRace();
        else if (!career.SeasonOver)
            career.AdvanceTo(new DateOnly(career.Today.Year, 12, 31));
        else if (career.HasNextSeason)
            career.StartNextSeason();
        Refresh();
    }

    private void GoToRace()
    {
        RaceRequest.Race = Career!.PrepareRace();
        RaceRequest.PlayerTeamId = Career.Team.Id;
        GetTree().ChangeSceneToFile("res://scenes/Race.tscn");
    }

    private void OnEnter(string eventId)
    {
        var result = Career!.Enter(eventId);
        _notice.Text = result switch
        {
            EntryResult.Entered => "",
            EntryResult.CannotArriveInTime => "The cars cannot get there in time from the race before, or on to the race after.",
            EntryResult.EventPassed => "That race has already been run.",
            EntryResult.NoDrivers => "You have no drivers left to enter.",
            _ => result.ToString(),
        };
        _notice.Visible = _notice.Text.Length > 0;
        Refresh();
    }

    // ------------------------------------------------------------------ cards

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
            Career = new Career(_data, team.Id, Session.StartYear, seed: (int)(Time.GetUnixTimeFromSystem() % 100000));
            Refresh();
        };
        box.AddChild(run);
        card.AddChild(box);
        return card;
    }

    private static Control HowToPlay()
    {
        var card = Card();
        var box = Stack(card, 6);
        box.AddChild(new Label { Text = "HOW A SEASON WORKS", ThemeTypeVariation = "Overline" });
        foreach (var line in new[]
                 {
                     "Enter the races worth the trip. Each pays start money for every car, and prizes for the first four.",
                     "Travel costs money and days: the cars must get from one race to the next in time.",
                     "Wages are paid every week. Your maker's backing comes in every week too.",
                     "The drawing office improves the car: more power, less weight, better handling, fewer failures.",
                     "On race day you run the pit wall: pace orders and pit stops.",
                 })
            box.AddChild(new Label { Text = line, ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        return card;
    }

    private Control TeamCard(Career career)
    {
        var card = Card();
        var box = Stack(card, 6);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        top.AddChild(new ColorRect { Color = TrackView.TeamColor(career.Team.Id), CustomMinimumSize = new Vector2(5, 36), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        titles.AddChild(new Label { Text = "YOUR TEAM", ThemeTypeVariation = "Overline" });
        titles.AddChild(new Label { Text = career.Team.Name, ThemeTypeVariation = "Heading" });
        top.AddChild(titles);
        var change = new Button { Text = "New game", SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "Pick a team again and start over in 1930" };
        change.Pressed += () =>
        {
            Career = null;
            _notice.Visible = false;
            Refresh();
        };
        top.AddChild(change);
        box.AddChild(top);
        var car = career.Car;
        box.AddChild(new Label { Text = $"{car.Name} · {car.Horsepower} hp · {car.WeightKg} kg · handling {car.Handling} · reliability {car.Reliability:P0}", ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        foreach (var id in career.Team.DriverIds)
        {
            var driver = _data.Drivers[id];
            var dead = career.DeadDrivers.Contains(id);
            box.AddChild(new Label
            {
                Text = dead ? $"✝ {driver.Name}" : $"{driver.Name}  ·  speed {driver.Skills.Speed}",
                Modulate = dead ? new Color(1, 1, 1, 0.45f) : Colors.White,
                TooltipText = dead ? "Killed racing" : "",
                MouseFilter = MouseFilterEnum.Pass,
            });
        }
        return card;
    }

    private Control DrawingOfficeCard(Career career)
    {
        var card = Card();
        var box = Stack(card, 6);
        box.AddChild(new Label { Text = "DRAWING OFFICE", ThemeTypeVariation = "Overline" });

        if (career.Projects.Count == 0)
            box.AddChild(new Label { Text = "Nothing on the drawing boards. Order work to improve the car.", ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        for (var i = 0; i < career.Projects.Count; i++)
        {
            var project = career.Projects[i];
            var kind = DrawingOffice.Kind(project.Upgrade);
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(new Label { Text = $"{kind.Name} ({kind.Effect})", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
            line.AddChild(new Label
            {
                Text = i == 0 ? $"{project.Weeks - project.WeeksDone} wk left" : "queued",
                ThemeTypeVariation = "Muted",
            });
            box.AddChild(line);
            if (i == 0)
                box.AddChild(new ProgressBar { MaxValue = 1, Step = 0, Value = project.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6) });
        }

        box.AddChild(new HSeparator());
        foreach (var kind in DrawingOffice.Kinds)
        {
            var cost = career.ProjectCost(kind.Id);
            var button = new Button
            {
                Text = $"{kind.Name}  {kind.Effect}",
                Alignment = HorizontalAlignment.Left,
                TooltipText = $"£{cost:N0}, {kind.Weeks} weeks of work after anything already queued",
                Disabled = cost > career.Cash,
            };
            var price = new Label { Text = $"£{cost:N0} · {kind.Weeks} wk", ThemeTypeVariation = "Muted", MouseFilter = MouseFilterEnum.Ignore };
            price.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterRight, LayoutPresetMode.KeepSize, 12);
            price.GrowHorizontal = GrowDirection.Begin;
            button.AddChild(price);
            button.Pressed += () =>
            {
                var result = career.StartProject(kind.Id);
                _notice.Text = result switch
                {
                    ProjectResult.NotEnoughMoney => "Not enough money for that work.",
                    ProjectResult.AtLimit => "The car is already as good as that kind of work can make it.",
                    _ => "",
                };
                _notice.Visible = _notice.Text.Length > 0;
                Refresh();
            };
            box.AddChild(button);
        }
        return card;
    }

    private static Control NewsCard(Career career)
    {
        var card = Card();
        var box = Stack(card, 6);
        box.AddChild(new Label { Text = "TEAM NEWS", ThemeTypeVariation = "Overline" });
        foreach (var item in career.News.TakeLast(3).Reverse())
            box.AddChild(new Label
            {
                Text = $"{item.Date.ToString("d MMM", English)} · {item.Text}",
                ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        return card;
    }

    private static Control MoneyCard(Career career)
    {
        var card = Card();
        var box = Stack(card, 6);
        box.AddChild(new Label { Text = "MONEY", ThemeTypeVariation = "Overline" });
        box.AddChild(new Label { Text = $"£{career.Cash:N0}", ThemeTypeVariation = "Big", Modulate = career.Cash < 0 ? UiTheme.Danger : Colors.White });
        box.AddChild(new Label { Text = $"Every week: wages −£{career.WeeklyCosts}, backing +£{career.WeeklyBacking}", ThemeTypeVariation = "Muted" });
        box.AddChild(new HSeparator());

        // Wages and backing come every week, so the list shows the race money and travel.
        var recent = career.Ledger.Where(t => t.Kind is not MoneyKind.Wages and not MoneyKind.Backing).TakeLast(6).Reverse().ToList();
        if (recent.Count == 0)
            box.AddChild(new Label { Text = "No race money yet.", ThemeTypeVariation = "Muted" });
        foreach (var t in recent)
        {
            var line = new HBoxContainer();
            line.AddChild(new Label { Text = t.Text, ThemeTypeVariation = "Muted", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TooltipText = t.Text, MouseFilter = MouseFilterEnum.Pass });
            line.AddChild(new Label { Text = Money(t.Amount), Modulate = t.Amount < 0 ? new Color(1, 0.65f, 0.55f) : UiTheme.Good });
            box.AddChild(line);
        }
        return card;
    }

    private static Control SummaryCard(Career career)
    {
        var year = career.Results.Count > 0 ? career.Results[^1].Event.Date.Year : career.Today.Year;
        var s = career.Summary(year);
        var card = Card();
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 12, 16, border: new Color(UiTheme.Accent, 0.6f)));
        var box = Stack(card, 6);
        box.AddChild(new Label { Text = $"{year} SEASON OVER", ThemeTypeVariation = "Overline" });
        box.AddChild(new Label { Text = s.Wins > 0 ? $"{s.Wins} win{(s.Wins > 1 ? "s" : "")} from {s.Races} races" : $"{s.Races} races, no wins", ThemeTypeVariation = "Heading" });
        box.AddChild(new Label { Text = $"Podiums in {s.Podiums} races", ThemeTypeVariation = "Muted" });
        box.AddChild(new Label { Text = $"Income £{s.Income:N0} · costs £{s.Costs:N0}", ThemeTypeVariation = "Muted" });
        box.AddChild(new Label { Text = $"Cash at the end: £{s.CashAtEnd:N0}" });
        if (s.DriversLost.Count > 0)
            box.AddChild(new Label { Text = $"Lost: {string.Join(", ", s.DriversLost)}", Modulate = new Color(1, 0.65f, 0.55f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        return card;
    }

    private Control EventCard(Career career, RaceEvent ev)
    {
        var circuit = _data.Circuits[ev.CircuitId];
        var entered = career.Entries.Contains(ev);
        var record = career.ResultOf(ev);
        var isNext = career.NextRace?.Id == ev.Id;
        var card = Card();
        if (isNext)
            card.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 12, 16, border: new Color(UiTheme.Accent, 0.7f)));
        else if (record is not null)
            card.Modulate = new Color(1, 1, 1, 0.82f);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);

        // Date as a calendar tile.
        var tile = new PanelContainer { ThemeTypeVariation = "Badge", CustomMinimumSize = new Vector2(58, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var date = new VBoxContainer();
        date.AddThemeConstantOverride("separation", -4);
        date.AddChild(new Label { Text = ev.Date.Day.ToString(), ThemeTypeVariation = "Big", HorizontalAlignment = HorizontalAlignment.Center });
        date.AddChild(new Label { Text = ev.Date.ToString("MMM", English).ToUpperInvariant(), ThemeTypeVariation = "Overline", HorizontalAlignment = HorizontalAlignment.Center });
        tile.AddChild(date);
        row.AddChild(tile);

        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 2);
        titles.AddChild(new Label { Text = ev.Name, ThemeTypeVariation = "Heading" });
        titles.AddChild(new Label { Text = $"{circuit.Name}, {circuit.Country} · {ev.Laps} laps · {ev.Laps * circuit.LapKm:0} km", ThemeTypeVariation = "Muted" });
        var facts = new HBoxContainer();
        facts.AddThemeConstantOverride("separation", 6);
        if (record is not null)
        {
            var w = record.Winner;
            facts.AddChild(Chip($"Won by {w.Entry.Driver.Name} ({w.Entry.Team.Name})", w.Entry.Team.Id == career.Team.Id ? UiTheme.Accent : null));
            if (record.Entered)
            {
                var best = record.Result.Classification
                    .Select((c, i) => (c, i))
                    .FirstOrDefault(x => x.c.Entry.Team.Id == career.Team.Id && x.c.Finished);
                facts.AddChild(Chip(best.c is null ? "No finishers" : $"Best: {best.c.Entry.Driver.Name} {Career.Place(best.i + 1)}"));
                facts.AddChild(Chip($"Earned £{record.Earned:N0}", UiTheme.Good));
            }
        }
        else
        {
            facts.AddChild(Chip($"Start money £{ev.StartMoney:N0}/car"));
            facts.AddChild(Chip($"Prizes £{ev.PrizeFund:N0}"));
            facts.AddChild(Chip($"Prestige {ev.Prestige}"));
            if (ev.Date >= career.Today)
            {
                var travel = career.TravelTo(ev);
                facts.AddChild(Chip($"{travel.Days} days, £{travel.Cost:N0}" + (travel.CrossesAtlantic ? " by liner" : " travel")));
            }
        }
        titles.AddChild(facts);
        row.AddChild(titles);

        if (record is not null)
            row.AddChild(Tag(record.Entered ? "Raced" : "Not entered", record.Entered ? UiTheme.Text : UiTheme.TextMuted));
        else if (isNext)
            row.AddChild(Tag("Next race", UiTheme.Accent));
        else if (entered)
            row.AddChild(Tag("Entered", UiTheme.Good));
        else if (ev.Date >= career.Today)
        {
            var enter = new Button { Text = "Enter", SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(100, 0) };
            enter.Pressed += () => OnEnter(ev.Id);
            row.AddChild(enter);
        }
        card.AddChild(row);
        return card;
    }

    // ------------------------------------------------------------------ small pieces

    private static PanelContainer Card() => new() { ThemeTypeVariation = "Card" };

    private static VBoxContainer Stack(Control parent, int separation)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        parent.AddChild(box);
        return box;
    }

    private static Control Chip(string text, Color? color = null)
    {
        var chip = new PanelContainer { ThemeTypeVariation = "Badge" };
        var label = new Label { Text = text, ThemeTypeVariation = "Muted" };
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        chip.AddChild(label);
        return chip;
    }

    private static Control Tag(string text, Color color)
    {
        var tag = new PanelContainer { ThemeTypeVariation = "Badge", SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(100, 0) };
        tag.AddChild(new Label { Text = text, Modulate = color, HorizontalAlignment = HorizontalAlignment.Center });
        return tag;
    }

    private static string Money(int amount) => amount < 0 ? $"−£{-amount:N0}" : $"+£{amount:N0}";
}
