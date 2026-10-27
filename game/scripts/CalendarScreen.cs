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
    private Button _driversTab = null!;
    private Button _worksTab = null!;

    private enum View { Calendar, Map, Drivers, Works, Designer }

    // Kept across scene changes, so coming back from a race shows the same view.
    private static View _view;

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
        _driversTab = new Button { Text = "Drivers", ToggleMode = true, ThemeTypeVariation = "Segment", CustomMinimumSize = new Vector2(110, 0) };
        _calendarTab.Pressed += () => { _view = View.Calendar; Refresh(); };
        _mapTab.Pressed += () => { _view = View.Map; Refresh(); };
        _worksTab = new Button { Text = "Works", ToggleMode = true, ThemeTypeVariation = "Segment", CustomMinimumSize = new Vector2(110, 0) };
        _driversTab.Pressed += () => { _view = View.Drivers; Refresh(); };
        _worksTab.Pressed += () => { _view = View.Works; Refresh(); };
        _tabs.AddChild(_calendarTab);
        _tabs.AddChild(_mapTab);
        _tabs.AddChild(_driversTab);
        _tabs.AddChild(_worksTab);
        column.AddChild(_tabs);

        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 16);
        column.AddChild(row);

        _calendarScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways };
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 8);
        _calendarScroll.AddChild(_body);
        row.AddChild(_calendarScroll);

        _map = new TravelMap { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, EnterRace = OnEnter };
        row.AddChild(_map);

        var sideScroll = new ScrollContainer { CustomMinimumSize = new Vector2(SideWidth, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways };
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
        // macOS stops drawing a window it thinks nobody can see, and the capture then waits forever.
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);
        if (DevShot.Arg("team") is { } team && Career is null)
        {
            var year = int.TryParse(DevShot.Arg("year"), out var y) ? y : Session.StartYear;
            Career = new Career(_data, team, year);
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
            _view = View.Map;
            Refresh();
            DevShot.Capture(this, "map.png", 1, () =>
            {
                _view = View.Drivers;
                Refresh();
                DevShot.Capture(this, "drivers.png", 1, () =>
                {
                    _view = View.Works;
                    Refresh();
                    DevShot.Capture(this, "works.png", 1, () =>
                    {
                        // The newest parts, so the shot shows any research they need.
                        _draft = CarDesigner.Default(Career!.Today.Year);
                        _view = View.Designer;
                        Refresh();
                        DevShot.Capture(this, "designer.png", 1, () =>
                        {
                            OpenDesigner(indy: true);
                            DevShot.Capture(this, "designer-indy.png", 1, () =>
                            {
                                _view = View.Calendar;
                                GoToRace();
                            });
                        });
                    });
                });
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
        _calendarScroll.Visible = Career is null || _view != View.Map;
        _map.Visible = Career is not null && _view == View.Map;
        _calendarTab.SetPressedNoSignal(_view == View.Calendar);
        _mapTab.SetPressedNoSignal(_view == View.Map);
        _driversTab.SetPressedNoSignal(_view == View.Drivers);
        _worksTab.SetPressedNoSignal(_view == View.Works);

        if (Career is not { } career)
        {
            _title.Text = $"The {Session.StartYear} season";
            _status.Text = "Choose a team to begin";
            _action.Visible = false;
            if (!DevShot.Active && SaveGame.Load(_data) is { } saved)
                _body.AddChild(ContinueCard(saved));
            _body.AddChild(new Label { Text = "CHOOSE YOUR TEAM", ThemeTypeVariation = "Overline" });
            var teams = new HBoxContainer();
            teams.AddThemeConstantOverride("separation", 16);
            foreach (var team in _data.Teams.Values.Where(t => t.Playable))
                teams.AddChild(TeamCard(team));
            _body.AddChild(teams);
            _side.AddChild(HowToPlay());
            return;
        }

        if (!DevShot.Active)
            SaveGame.Save(career);
        _title.Text = $"The {career.CurrentSeason.Year} season · {Formula.Name(career.CurrentSeason.Year)}";
        _status.Text = $"{career.Today.ToString("d MMMM yyyy", English)}  ·  £{career.Cash:N0}";
        _action.Visible = true;
        _action.Disabled = false;
        if (career.NextRace is { } next)
            _action.Text = next.IsEndurance ? "Go to Le Mans  ›" : $"Go to {next.Name}  ›";
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

        if (_view == View.Drivers)
            DriverMarket(career);
        else if (_view == View.Works)
            WorksView(career);
        else if (_view == View.Designer)
            DesignerView(career);
        else
            foreach (var ev in career.CurrentSeason.Events)
                _body.AddChild(EventCard(career, ev));
        _map.SetSeason(_data, career);

        if (career.SeasonOver)
            _side.AddChild(SummaryCard(career));
        _side.AddChild(MoneyCard(career));
        // The Indianapolis card goes above the drawing office while the 500 is still to come and the team has no car for it.
        var indy = career.CurrentSeason.Events.FirstOrDefault(e => Indianapolis.Is(_data.Circuits[e.CircuitId]));
        var indyFirst = indy is not null && indy.Date >= career.Today && career.RaceCarFor(indy) is null;
        if (indyFirst)
            _side.AddChild(IndyCard(career));
        _side.AddChild(DrawingOfficeCard(career));
        if (indy is not null && !indyFirst)
            _side.AddChild(IndyCard(career));
        _side.AddChild(RivalsCard(career));
        _side.AddChild(TeamCard(career));
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
            EntryResult.CarNotEligible => NotEligible(Career, eventId) ?? "The car does not meet this year's rules.",
            EntryResult.NoSportsCar => $"{Career.Team.Name} has no sports car for Le Mans this year: the Grand Prix car cannot run there.",
            EntryResult.NoIndyCar => $"The 500 runs to its own rules ({Indianapolis.Rule(Career.Today.Year)}). Buy a special or design one in the Indianapolis card.",
            EntryResult.NeedTwoDrivers => "A 24-hour race needs two drivers to share each car. Sign another driver.",
            _ => result.ToString(),
        };
        _notice.Visible = _notice.Text.Length > 0;
        Refresh();
    }

    private string? NotEligible(Career career, string eventId)
    {
        var ev = career.CurrentSeason.Events.First(e => e.Id == eventId);
        return Formula.Breach(career.Car, _data.Circuits[ev.CircuitId], ev.Date.Year) is { } why
            ? $"{why} Order a lighter chassis or a new car from the drawing office."
            : null;
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
        var startCar = team.CarFor(Session.StartYear);
        box.AddChild(new Label { Text = $"{startCar.Name} · {startCar.Horsepower} hp · {startCar.WeightKg} kg", ThemeTypeVariation = "Muted" });
        var drivers = string.Join(", ", team.RosterFor(Session.StartYear).Select(id => _data.Drivers[id].Name));
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

    private Control ContinueCard(Career saved)
    {
        var card = Card();
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 12, 16, border: new Color(UiTheme.Accent, 0.7f)));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(new ColorRect { Color = TrackView.TeamColor(saved.Team.Id), CustomMinimumSize = new Vector2(5, 40), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        titles.AddChild(new Label { Text = "SAVED GAME", ThemeTypeVariation = "Overline" });
        titles.AddChild(new Label { Text = $"{saved.Team.Name}, {saved.Today.ToString("d MMMM yyyy", English)}", ThemeTypeVariation = "Heading" });
        titles.AddChild(new Label { Text = $"£{saved.Cash:N0} · {saved.Results.Count(r => r.Entered)} races run", ThemeTypeVariation = "Muted" });
        row.AddChild(titles);
        var go = new Button { Text = "Continue  ›", ThemeTypeVariation = "Accent", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        go.Pressed += () =>
        {
            Career = saved;
            Refresh();
        };
        row.AddChild(go);
        card.AddChild(row);
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
                     "The rules change: from 1934 a car may weigh at most 750 kg, from 1938 its engine may be at most 3 litres. The drawing office can build the works' next car; one built for next season waits until 1 January, and the maker pays half when the rules rule out your car.",
                     "Sign drivers looking for a drive, or release one to save wages.",
                     "In the works, build up the drawing office, workshop and transport, hire a better engineer, mechanic or team manager, and test the car to make it more reliable. Late on, a test track of your own makes testing cheap.",
                     "On race day you run the pit wall: pace orders and pit stops.",
                     "Le Mans is a 24-hour race outside the Grand Prix rules, for the maker's sports car. Two drivers share each car and change at the stops; there is no refuelling within 24 laps of the last fill, and the race runs through the night. The most distance in 24 hours wins.",
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
        var change = new Button { Text = "Main menu", SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "Back to the team choice. The game is saved; pick Continue to come back to it." };
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
        if (career.TeethingFaults > 0)
            box.AddChild(new Label
            {
                Text = $"{career.TeethingFaults} teething trouble{(career.TeethingFaults == 1 ? "" : "s")} still to find: test days and races show them up.",
                Modulate = UiTheme.Accent, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        if (career.Team.SportsCarFor(career.Today.Year) is { } sports)
            box.AddChild(new Label { Text = $"For Le Mans: {sports.Name} · {sports.Horsepower} hp · {sports.WeightKg} kg", ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        foreach (var id in career.Roster)
        {
            var driver = _data.Drivers[id];
            var dead = career.DeadDrivers.Contains(id);
            var injured = career.InjuredUntil(id);
            box.AddChild(new Label
            {
                Text = dead ? $"✝ {driver.Name}"
                    : injured is { } until ? $"{driver.Name}  ·  injured until {until.ToString("d MMM", English)}"
                    : $"{driver.Name}  ·  speed {driver.Skills.Speed}",
                Modulate = dead ? new Color(1, 1, 1, 0.45f) : injured is not null ? new Color(1, 0.75f, 0.6f) : Colors.White,
                TooltipText = dead ? "Killed racing" : injured is not null ? "Hurt in a crash: the car runs without them until then" : "",
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
            var what = project.Upgrade == Upgrade.NewCar ? (project.Built ?? career.Team.CarFor(project.ModelYear)).Name : kind.Effect;
            if (project.Part is { } part)
                what = Research.Name(part);
            line.AddChild(new Label { Text = $"{(project.Indy ? "Indianapolis car" : kind.Name)} ({what})", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
            line.AddChild(new Label
            {
                Text = i == 0 ? $"{project.Weeks - project.WeeksDone} wk left" : "queued",
                ThemeTypeVariation = "Muted",
            });
            box.AddChild(line);
            if (i == 0)
                box.AddChild(new ProgressBar { MaxValue = 1, Step = 0, Value = project.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6) });
        }

        if (career.WaitingModelYear is { } waiting)
            box.AddChild(new Label
            {
                Text = $"The {(career.WaitingCar ?? career.Team.CarFor(waiting)).Name} is built and races from 1 January {waiting}.",
                ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });

        box.AddChild(new HSeparator());
        var design = new Button
        {
            Text = "Design your own car  ›", Disabled = career.NewCarUnderway,
            TooltipText = career.NewCarUnderway ? "A new car is already being built." : "Choose the engine and chassis of a new car.",
        };
        design.Pressed += () => OpenDesigner(indy: false);
        box.AddChild(design);
        foreach (var kind in DrawingOffice.Kinds)
        {
            var next = career.NextModel;
            if (kind.Id == Upgrade.NewCar && next is null || kind.Id == Upgrade.Research)
                continue;
            var cost = career.ProjectCost(kind.Id);
            var effect = kind.Id == Upgrade.NewCar && next is { } model
                ? $"{model.Car.Name}: {model.Car.Horsepower} hp, {model.Car.WeightKg} kg, {model.Car.EngineLitres:0.0#} L"
                : kind.Effect;
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", -2);
            text.AddChild(new Label { Text = kind.Name });
            var queued = career.Projects.Count(p => p.Upgrade == kind.Id);
            text.AddChild(new Label
            {
                Text = $"{effect} · {career.ProjectWeeks(kind.Id)} weeks" + (queued > 0 ? $" · {queued} ordered" : ""),
                ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
            line.AddChild(text);
            var button = new Button
            {
                Text = $"£{cost:N0}", CustomMinimumSize = new Vector2(84, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter,
                TooltipText = kind.Id == Upgrade.NewCar
                    ? $"Design and build the works' next car: paid now, {career.ProjectWeeks(kind.Id)} weeks of work after anything already ordered. It replaces the car you have, and its development starts afresh. A car designed for next season waits in the works until 1 January."
                      + (career.MakerPaysHalf ? " The rules rule out your car, so the maker pays half." : "")
                    : $"Order {kind.Name.ToLowerInvariant()}: paid now, {career.ProjectWeeks(kind.Id)} weeks of work after anything already ordered. Each one of a kind costs more than the last.",
                Disabled = cost > career.Cash,
            };
            button.Pressed += () =>
            {
                var result = career.StartProject(kind.Id);
                _notice.Text = result switch
                {
                    ProjectResult.NotEnoughMoney => "Not enough money for that work.",
                    ProjectResult.AtLimit => "The car is already as good as that kind of work can make it.",
                    ProjectResult.NoNewModel => "There is no newer design to build yet.",
                    _ => "",
                };
                _notice.Visible = _notice.Text.Length > 0;
                Refresh();
            };
            line.AddChild(button);
            box.AddChild(line);
        }
        return card;
    }

    /// <summary>The team's car for the Indianapolis 500, and how to get one.</summary>
    private Control IndyCard(Career career)
    {
        var card = Card();
        var box = Stack(card, 6);
        var year = career.Today.Year;
        box.AddChild(new Label { Text = "INDIANAPOLIS CAR", ThemeTypeVariation = "Overline" });
        box.AddChild(new Label
        {
            Text = $"The 500's {Indianapolis.Name(year).ToLowerInvariant()}: {Indianapolis.Rule(year)}."
                   + (year >= Indianapolis.SingleSeaterYear ? " A 3-litre Grand Prix car may run." : " The Grand Prix car cannot run."),
            ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var indyEvent = career.CurrentSeason.Events.First(e => Indianapolis.Is(_data.Circuits[e.CircuitId]));
        var racing = career.RaceCarFor(indyEvent);
        box.AddChild(new Label
        {
            Text = racing is { } car
                ? $"Races: {car.Name} · {car.Horsepower} hp · {car.WeightKg} kg · reliability {car.Reliability:P0}"
                : career.IndyCar is { } old ? $"The {old.Name} no longer meets the rules." : "No car for the 500 yet.",
            Modulate = racing is null ? new Color(1, 0.65f, 0.55f) : Colors.White, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        if (career.IndyOrder is { } order)
            box.AddChild(new Label { Text = $"The {order.Car.Name} is on its way: it arrives {order.Arrives.ToString("d MMMM", English)}.", ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart });

        var forSale = career.IndyCarForSale;
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", -2);
        text.AddChild(new Label { Text = $"Buy the {forSale.Name}" });
        text.AddChild(new Label
        {
            Text = $"{forSale.Horsepower} hp, {forSale.WeightKg} kg, {forSale.EngineLitres:0.0#} L · {Indianapolis.BuyWeeks} weeks by liner",
            ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        line.AddChild(text);
        var buy = new Button
        {
            Text = $"£{Indianapolis.BuyCost:N0}", CustomMinimumSize = new Vector2(84, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Disabled = career.IndyCarUnderway || Indianapolis.BuyCost > career.Cash,
            TooltipText = career.IndyCarUnderway ? "An Indianapolis car is already on its way."
                : $"A ready-made American car to the 500's rules, the {forSale.Name}. Paid now; it does not use the drawing office.",
        };
        buy.Pressed += () =>
        {
            Notice(career.BuyIndyCar() switch
            {
                ProjectResult.NotEnoughMoney => "Not enough money for the special.",
                ProjectResult.NewCarUnderway => "An Indianapolis car is already on its way.",
                _ => "",
            });
            Refresh();
        };
        line.AddChild(buy);
        box.AddChild(line);
        var design = new Button
        {
            Text = "Design an Indianapolis car  ›", Disabled = career.IndyCarUnderway,
            TooltipText = career.IndyCarUnderway ? "An Indianapolis car is already on its way." : "Your own car for the 500, built in the drawing office beside the Grand Prix work.",
        };
        design.Pressed += () => OpenDesigner(indy: true);
        box.AddChild(design);
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

    private Control RivalsCard(Career career)
    {
        var card = Card();
        var box = Stack(card, 4);
        box.AddChild(new Label { Text = "THE CARS", ThemeTypeVariation = "Overline" });
        foreach (var team in career.RivalsIn(career.Today.Year).Prepend(career.Team).OrderByDescending(t => career.CarOf(t.Id).Horsepower))
        {
            var car = career.CarOf(team.Id);
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(new ColorRect { Color = TrackView.TeamColor(team.Id), CustomMinimumSize = new Vector2(4, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter });
            line.AddChild(new Label { Text = team.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, Modulate = team.Id == career.Team.Id ? UiTheme.Accent : Colors.White });
            line.AddChild(new Label { Text = $"{car.Horsepower} hp · {car.WeightKg} kg · H{car.Handling} · {car.Reliability:P0}", ThemeTypeVariation = "Muted", TooltipText = "Power, weight, handling (1–20) and reliability", MouseFilter = MouseFilterEnum.Pass });
            box.AddChild(line);
        }
        return card;
    }

    // ------------------------------------------------------------------ drivers

    private void DriverMarket(Career career)
    {
        _body.AddChild(new Label { Text = $"YOUR DRIVERS · {career.Drivers.Count} OF {Career.MaxDrivers}", ThemeTypeVariation = "Overline" });
        foreach (var driver in career.Drivers)
        {
            var release = new Button { Text = "Release", SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(110, 0), Disabled = career.Drivers.Count <= 1 };
            release.TooltipText = release.Disabled ? "The team needs at least one driver" : "Let the driver go; no more wages";
            var id = driver.Id;
            // Two presses: the first asks to confirm, so a star is not let go by a stray click.
            release.Pressed += () =>
            {
                if (release.ThemeTypeVariation != "Danger")
                {
                    release.ThemeTypeVariation = "Danger";
                    release.Text = "Confirm release";
                    return;
                }
                career.Release(id);
                Refresh();
            };
            _body.AddChild(DriverCard(career, driver, release, mine: true));
        }

        _body.AddChild(new Label { Text = "DRIVERS LOOKING FOR A DRIVE", ThemeTypeVariation = "Overline" });
        foreach (var driver in career.FreeAgents)
        {
            var fee = Finance.SigningFee(driver);
            var full = career.Drivers.Count >= Career.MaxDrivers;
            var sign = new Button
            {
                Text = $"Sign · £{fee:N0}", ThemeTypeVariation = "Accent", SizeFlagsVertical = SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(110, 0), Disabled = full || fee > career.Cash,
                TooltipText = full ? $"The team already runs {Career.MaxDrivers} cars" : $"Signing fee £{fee:N0} now, then £{Finance.DriverWage(driver)} a week",
            };
            var id = driver.Id;
            sign.Pressed += () =>
            {
                var result = career.Sign(id);
                _notice.Text = result switch
                {
                    SignResult.TeamFull => $"The team already runs {Career.MaxDrivers} cars. Release a driver first.",
                    SignResult.NotEnoughMoney => "Not enough money for the signing fee.",
                    SignResult.NotAvailable => "That driver is no longer available.",
                    _ => "",
                };
                _notice.Visible = _notice.Text.Length > 0;
                Refresh();
            };
            _body.AddChild(DriverCard(career, driver, sign, mine: false));
        }
    }

    private static Control DriverCard(Career career, Driver driver, Button action, bool mine)
    {
        var card = new PanelContainer { ThemeTypeVariation = "CardTight" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        var badge = new PanelContainer { ThemeTypeVariation = "Badge", CustomMinimumSize = new Vector2(52, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var speed = new VBoxContainer();
        speed.AddThemeConstantOverride("separation", -4);
        speed.AddChild(new Label { Text = driver.Skills.Speed.ToString(), ThemeTypeVariation = "Big", HorizontalAlignment = HorizontalAlignment.Center, Modulate = mine ? UiTheme.Accent : Colors.White });
        speed.AddChild(new Label { Text = "SPEED", ThemeTypeVariation = "Overline", HorizontalAlignment = HorizontalAlignment.Center });
        badge.AddChild(speed);
        row.AddChild(badge);

        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 2);
        titles.AddChild(new Label { Text = driver.Name, ThemeTypeVariation = "Heading" });
        titles.AddChild(new Label { Text = $"{driver.Nationality} · age {career.Today.Year - driver.BirthYear} · £{Finance.DriverWage(driver)} a week", ThemeTypeVariation = "Muted" });
        var skills = new HBoxContainer();
        skills.AddThemeConstantOverride("separation", 6);
        foreach (var (name, value) in new[]
                 {
                     ("Consistency", driver.Skills.Consistency), ("Car care", driver.Skills.MechanicalSympathy),
                     ("Wet", driver.Skills.Wet), ("Bravery", driver.Skills.Bravery),
                 })
            skills.AddChild(Chip($"{name} {value}"));
        titles.AddChild(skills);
        row.AddChild(titles);
        row.AddChild(action);
        card.AddChild(row);
        return card;
    }

    private static Control MoneyCard(Career career)
    {
        var card = Card();
        var box = Stack(card, 6);
        box.AddChild(new Label { Text = "MONEY", ThemeTypeVariation = "Overline" });
        box.AddChild(new Label { Text = $"£{career.Cash:N0}", ThemeTypeVariation = "Big", Modulate = career.Cash < 0 ? UiTheme.Danger : Colors.White });
        box.AddChild(new Label { Text = $"Every week: wages and works −£{career.WeeklyCosts}, backing +£{career.WeeklyBacking}", ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        box.AddChild(new HSeparator());

        // Wages and backing come every week, so the list shows the race money and travel.
        var recent = career.Ledger.Where(t => t.Kind is not MoneyKind.Wages and not MoneyKind.Backing).TakeLast(6).Reverse().ToList();
        if (recent.Count == 0)
            box.AddChild(new Label { Text = "No race money yet.", ThemeTypeVariation = "Muted" });
        foreach (var t in recent)
        {
            var line = new HBoxContainer();
            line.AddChild(new Label
            {
                Text = t.Text, ThemeTypeVariation = "Muted", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = t.Text, MouseFilter = MouseFilterEnum.Pass,
            });
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
        box.AddChild(new Label { Text = s.Wins > 0 ? $"{Count(s.Wins, "win")} from {Count(s.Races, "race")}" : $"{Count(s.Races, "race")}, no wins", ThemeTypeVariation = "Heading" });
        box.AddChild(new Label { Text = $"On the podium in {Count(s.Podiums, "race")}", ThemeTypeVariation = "Muted" });
        box.AddChild(new Label { Text = $"Started with £{s.CashAtStart:N0}", ThemeTypeVariation = "Muted" });
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
        var card = new PanelContainer { ThemeTypeVariation = "CardTight" };
        if (isNext)
            card.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 10, 10, border: new Color(UiTheme.Accent, 0.7f)));
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
        titles.AddChild(new Label
        {
            Text = ev.IsEndurance
                ? $"{circuit.Name}, {circuit.Country} · {ev.Hours} hours · {circuit.LapKm:0.#} km lap"
                : $"{circuit.Name}, {circuit.Country} · {ev.Laps} laps · {ev.Laps * circuit.LapKm:0} km",
            ThemeTypeVariation = "Muted",
        });
        // Chips wrap onto a second line rather than widening the card past the window.
        var facts = new HFlowContainer();
        facts.AddThemeConstantOverride("h_separation", 6);
        facts.AddThemeConstantOverride("v_separation", 4);
        if (record is not null)
        {
            var w = record.Winner;
            facts.AddChild(Chip($"Won by {w.Entry.Names} ({w.Entry.Team.Name})", w.Entry.Team.Id == career.Team.Id ? UiTheme.Accent : null));
            if (record.Entered)
            {
                var best = record.Result.Classification
                    .Select((c, i) => (c, i))
                    .FirstOrDefault(x => x.c.Entry.Team.Id == career.Team.Id && x.c.Finished);
                facts.AddChild(Chip(best.c is null ? "No finishers" : $"Best: {best.c.Entry.Names} {Career.Place(best.i + 1)}"));
                facts.AddChild(Chip($"Earned £{record.Earned:N0}", UiTheme.Good));
            }
        }
        else
        {
            facts.AddChild(Chip($"Start money £{career.StartMoney(ev):N0}/car"));
            facts.AddChild(Chip($"Prizes £{ev.PrizeFund:N0}"));
            facts.AddChild(Chip($"Prestige {ev.Prestige}"));
            if (ev.IsEndurance)
            {
                facts.AddChild(Chip("24 hours", null,
                    "Sports cars only, outside the Grand Prix rules. Two drivers a car, who change at the stops; no refuelling within 24 laps of the last fill."));
                if (career.SportsCarFor(ev) is { } sports)
                    facts.AddChild(Chip(sports.Name, null, $"{sports.Horsepower} hp, {sports.WeightKg} kg, {sports.EngineLitres:0.0#} L"));
                else if (ev.Date >= career.Today)
                    facts.AddChild(Chip("No sports car", UiTheme.Danger, $"{career.Team.Name} built no sports car for this year's race."));
            }
            else if (Indianapolis.Is(circuit) && ev.Date >= career.Today && !entered)
            {
                if (career.RaceCarFor(ev) is { } indyCar)
                    facts.AddChild(Chip(indyCar.Name, null, $"{indyCar.Horsepower} hp, {indyCar.WeightKg} kg, {indyCar.EngineLitres:0.0#} L"));
                else
                    facts.AddChild(Chip("No Indianapolis car", UiTheme.Danger, $"The 500's {Indianapolis.Name(ev.Date.Year).ToLowerInvariant()}: {Indianapolis.Rule(ev.Date.Year)}."));
            }
            else if (ev.Date >= career.Today && !entered && Formula.Breach(career.Car, circuit, ev.Date.Year) is { } breach)
                facts.AddChild(Chip("Car not eligible", UiTheme.Danger, breach));
            if (career.ForecastFor(ev) is { } forecast)
                facts.AddChild(Chip($"Forecast: {forecast.ToLowerInvariant()}", null,
                    $"It rains here on about {circuit.RainChance:P0} of race days. The forecast is right about three times in four."));
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

    private static Control Chip(string text, Color? color = null, string tooltip = "")
    {
        var chip = new PanelContainer { ThemeTypeVariation = "Badge", TooltipText = tooltip };
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

    private static string Count(int n, string thing) => $"{n} {thing}{(n == 1 ? "" : "s")}";

    private static string Money(int amount) => amount < 0 ? $"−£{-amount:N0}" : $"+£{amount:N0}";
}
