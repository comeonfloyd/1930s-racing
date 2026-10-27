using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// The car designer, opened from the drawing office: pick the engine and chassis, see the car
/// it makes next to the current one, and order it.
/// </summary>
public partial class CalendarScreen
{
    // The design being drawn up, one for each kind of car; kept while the player looks at other tabs.
    private static CarDesign? _draft, _indyDraft;
    private static bool _designIndy;

    private void OpenDesigner(bool indy)
    {
        _designIndy = indy;
        _view = View.Designer;
        Refresh();
    }

    private void DesignerView(Career career)
    {
        var indy = _designIndy;
        // A fresh design starts from the parts the drawing office knows, so it can be ordered at once.
        var draft = indy ? _indyDraft ?? career.KnownParts(Indianapolis.Default(career.Today.Year))
            : _draft ?? career.KnownParts(CarDesigner.Default(career.Today.Year));
        var preview = career.Preview(draft, indy);
        draft = Fit(draft, preview.Year, indy);
        SetDraft(draft);
        preview = career.Preview(draft, indy);

        var top = new HBoxContainer();
        top.AddChild(new Label { Text = indy ? "DESIGN AN INDIANAPOLIS CAR" : "DESIGN A NEW CAR", ThemeTypeVariation = "Overline", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var back = new Button { Text = "‹  Back to the calendar" };
        back.Pressed += () => { _view = View.Calendar; Refresh(); };
        top.AddChild(back);
        _body.AddChild(top);
        _body.AddChild(new Label
        {
            Text = indy
                ? $"A special for the 500, built with the engineering of {preview.Year} to its {Indianapolis.Name(preview.Year).ToLowerInvariant()}: {Indianapolis.Rule(preview.Year)}. "
                  + "It is built in the drawing office beside your Grand Prix work and races only at Indianapolis."
                : $"Your own car, built with the engineering of {preview.Year}. Parts not yet invented that year are greyed out. "
                  + "The works' next model stays on offer in the drawing office as the cheaper choice.",
            ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        // Two choices to a row, so the comparison and the Order button fit without scrolling.
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 8);
        card.AddChild(grid);
        var year = preview.Year;
        var d = draft;
        // The 500's junk formula bans superchargers until the AAA takes up the international rules.
        int BoostFirstYear(Supercharging s) => indy && s != Supercharging.None
            ? Math.Max(Indianapolis.SingleSeaterYear, CarDesigner.FirstYear(s)) : CarDesigner.FirstYear(s);
        Choice(grid, "Engine", Enum.GetValues<EngineLayout>(), d.Layout, CarDesigner.Name, CarDesigner.FirstYear, year, v => SetDraft(Draft with { Layout = v }));
        Litres(grid, d.Litres, MaxLitres(indy, year));
        Choice(grid, "Supercharging", Enum.GetValues<Supercharging>(), d.Supercharging, CarDesigner.Name, BoostFirstYear, year, v => SetDraft(Draft with { Supercharging = v }));
        Choice(grid, "Frame", Enum.GetValues<Frame>(), d.Frame, CarDesigner.Name, CarDesigner.FirstYear, year, v => SetDraft(Draft with { Frame = v }));
        Choice(grid, "Suspension", Enum.GetValues<Suspension>(), d.Suspension, CarDesigner.Name, CarDesigner.FirstYear, year, v => SetDraft(Draft with { Suspension = v }));
        Choice(grid, "Brakes", Enum.GetValues<Brakes>(), d.Brakes, CarDesigner.Name, CarDesigner.FirstYear, year, v => SetDraft(Draft with { Brakes = v }));
        Choice(grid, "Body", Enum.GetValues<Body>(), d.Body, CarDesigner.Name, CarDesigner.FirstYear, year, v => SetDraft(Draft with { Body = v }));
        _body.AddChild(card);
        if (preview.Unresearched is { Count: > 0 } unknown)
            _body.AddChild(ResearchCard(career, unknown));

        // The price and the Order button come before the figures, so they stay in view.
        var order = new HBoxContainer();
        order.AddThemeConstantOverride("separation", 12);
        var when = preview.Year > career.Today.Year ? $", racing from 1 January {preview.Year}" : "";
        var queued = career.Projects.Count > 0 ? " after the work already ordered" : "";
        order.AddChild(new Label
        {
            Text = $"£{preview.Cost:N0} · {preview.Weeks} weeks{queued}{when}\n"
                   + $"Estimate ±{preview.Spread * 100:0}%" + (preview.Faults > 0 ? $" · {preview.Faults}–{preview.Faults + 1} teething troubles" : ""),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var why = (indy ? career.IndyCarUnderway : career.NewCarUnderway) ? "A new car is already being built."
            : preview.TooEarly.Count > 0 ? $"Not invented yet: {string.Join(", ", preview.TooEarly)}."
            : indy && preview.Breach is { } rules ? rules
            : preview.Unresearched is { Count: > 0 } study ? $"Research first: {string.Join(", ", study.Select(Research.Name))}."
            : preview.Cost > career.Cash ? "Not enough money."
            : "";
        var go = new Button
        {
            Text = "Order this car  ›", ThemeTypeVariation = "Accent", CustomMinimumSize = new Vector2(160, 0),
            Disabled = why.Length > 0,
            TooltipText = why.Length > 0 ? why
                : indy ? "Paid now. It becomes your car for the 500 when it is built; your Grand Prix car is not touched."
                : "Paid now. It replaces the car you have when it is built, and its development starts afresh.",
        };
        go.Pressed += () =>
        {
            var result = career.StartDesign(Draft, indy);
            if (result == ProjectResult.Queued)
            {
                SetDraft(null);
                _view = View.Calendar;
            }
            Notice(result switch
            {
                ProjectResult.NotEnoughMoney => "Not enough money for that design.",
                ProjectResult.PartsNotInvented => "That design uses parts not invented yet.",
                ProjectResult.NewCarUnderway => "A new car is already being built.",
                ProjectResult.BreaksRules => preview.Breach ?? "That design breaks the rules.",
                ProjectResult.NotResearched => "The drawing office must study the new parts first.",
                _ => "",
            });
            Refresh();
        };
        order.AddChild(go);
        _body.AddChild(order);
        _body.AddChild(Comparison(indy ? career.IndyCar ?? career.IndyCarForSale : career.Car, preview));
        _body.AddChild(new Label
        {
            Text = $"The drawing office's estimate: with {career.StaffIn(StaffRole.Engineer)?.Name ?? "no chief engineer"} the real car can come out "
                   + $"{preview.Spread * 100:0}% either way, more often worse than better."
                   + (preview.Faults > 0 ? $" Expect about {preview.Faults}–{preview.Faults + 1} teething troubles, each costing {Prototype.FaultReliability * 100:0}% reliability until a test day or a race shows it up." : ""),
            ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        if (CarDesigner.FrameFlex(d, preview.Year) is > 0 and var flex)
            _body.AddChild(new Label
            {
                Text = $"The {CarDesigner.Name(d.Frame).ToLowerInvariant()} twists under an engine this strong: {flex} handling lost. A stiffer frame would hold it.",
                Modulate = UiTheme.Accent, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        var engineHp = CarDesigner.EngineHorsepower(d, preview.Year);
        if (engineHp > preview.Car.Horsepower)
            _body.AddChild(new Label
            {
                Text = $"The engine makes {engineHp} hp, but the tyres of {preview.Year} put down only about {CarDesigner.TyreLimit(preview.Year):0} hp: "
                       + $"the rest goes in wheelspin, so it counts as {preview.Car.Horsepower} hp, and the strain costs handling and reliability.",
                Modulate = UiTheme.Accent, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });

        if (preview.Breach is { } breach)
            _body.AddChild(new Label { Text = $"{breach} It could not race {(indy ? "at Indianapolis" : "in Europe")}.", Modulate = new Color(1, 0.65f, 0.55f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        else if ((indy ? Indianapolis.Breach(d, preview.Car.Name, preview.Year + 1) : Formula.Breach(preview.Car, preview.Year + 1)) is { } next && career.HasNextSeason)
            _body.AddChild(new Label { Text = $"Next year: {next}", Modulate = UiTheme.Accent, AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    /// <summary>The new parts the design needs that the drawing office has not studied, as one row of buttons.</summary>
    private Control ResearchCard(Career career, IReadOnlyList<Part> parts)
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 8);
        row.AddThemeConstantOverride("v_separation", 4);
        row.AddChild(new Label { Text = "Research first:", Modulate = UiTheme.Accent, VerticalAlignment = VerticalAlignment.Center });
        foreach (var part in parts)
        {
            var queued = career.ResearchQueued(part);
            var cost = career.ResearchCost(part);
            var study = new Button
            {
                Text = queued ? $"{Capital(Research.Name(part))} · being studied" : $"{Capital(Research.Name(part))} · £{cost:N0} · {career.ResearchWeeks(part)} wk",
                Disabled = queued || cost > career.Cash,
                TooltipText = $"New to racing in {Research.FirstYear(part)}. Paid now, then weeks in the drawing office; once studied, every design may use it. "
                              + "It gets cheaper each year as others show how it is done.",
            };
            study.Pressed += () =>
            {
                Notice(career.StartResearch(part) == ProjectResult.NotEnoughMoney ? "Not enough money for that research." : "");
                Refresh();
            };
            row.AddChild(study);
        }
        return row;
    }

    private static CarDesign Draft => (_designIndy ? _indyDraft : _draft)!;

    private static void SetDraft(CarDesign? design)
    {
        if (_designIndy)
            _indyDraft = design;
        else
            _draft = design;
    }

    /// <summary>The biggest engine the slider offers: for the 500, what its rules allow at most.</summary>
    private static double MaxLitres(bool indy, int year) =>
        !indy ? CarDesigner.MaxLitres
        : year < Indianapolis.SingleSeaterYear ? Indianapolis.JunkFormulaMaxLitres : Indianapolis.MaxUnsuperchargedLitres;

    /// <summary>
    /// A design brought up to the parts of a year: anything not yet invented falls back to the
    /// plainer choice. For the 500's junk formula the supercharger comes off and the engine is cut to size.
    /// </summary>
    private static CarDesign Fit(CarDesign d, int year, bool indy = false)
    {
        if (indy)
            d = d with
            {
                Supercharging = year < Indianapolis.SingleSeaterYear ? Supercharging.None : d.Supercharging,
                Litres = Math.Min(d.Litres, MaxLitres(true, year)),
            };
        T Latest<T>(T chosen, Func<T, int> first) where T : struct, Enum =>
            first(chosen) <= year ? chosen : Enum.GetValues<T>().Where(v => first(v) <= year).Last();
        return d with
        {
            Layout = d.Layout == EngineLayout.Straight8 || CarDesigner.FirstYear(d.Layout) <= year ? d.Layout : EngineLayout.Straight8,
            Supercharging = Latest(d.Supercharging, CarDesigner.FirstYear),
            Frame = Latest(d.Frame, CarDesigner.FirstYear),
            Suspension = Latest(d.Suspension, CarDesigner.FirstYear),
            Brakes = Latest(d.Brakes, CarDesigner.FirstYear),
            Body = Latest(d.Body, CarDesigner.FirstYear),
        };
    }

    private void Choice<T>(GridContainer grid, string label, IReadOnlyList<T> values, T chosen, Func<T, string> name, Func<T, int> first, int year, Action<T> set)
        where T : struct, Enum
    {
        grid.AddChild(new Label { Text = label, ThemeTypeVariation = "Muted", VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(100, 0) });
        var option = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        for (var i = 0; i < values.Count; i++)
        {
            var late = first(values[i]) > year;
            option.AddItem(name(values[i]) + (late ? $" (from {first(values[i])})" : ""), i);
            option.SetItemDisabled(i, late);
            if (EqualityComparer<T>.Default.Equals(values[i], chosen))
                option.Selected = i;
        }
        option.ItemSelected += i =>
        {
            set(values[(int)i]);
            Refresh();
        };
        grid.AddChild(option);
    }

    private void Litres(GridContainer grid, double litres, double max)
    {
        grid.AddChild(new Label { Text = "Engine size", ThemeTypeVariation = "Muted", VerticalAlignment = VerticalAlignment.Center });
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);
        var value = new Label { Text = $"{litres:0.00} litres", CustomMinimumSize = new Vector2(90, 0) };
        var slider = new HSlider
        {
            MinValue = CarDesigner.MinLitres, MaxValue = max, Step = CarDesigner.LitreStep, Value = litres,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        slider.ValueChanged += v => value.Text = $"{v:0.00} litres";
        // Rebuild only when the drag ends, so the screen does not flicker under the mouse.
        slider.DragEnded += changed =>
        {
            if (!changed)
                return;
            SetDraft(Draft with { Litres = slider.Value });
            Refresh();
        };
        row.AddChild(slider);
        row.AddChild(value);
        grid.AddChild(row);
    }

    /// <summary>The car you have against the one on the drawing board, figure by figure, as the estimate's range.</summary>
    private static Control Comparison(Car now, DesignPreview preview)
    {
        var design = preview.Car;
        string Between((int Low, int High) r, string unit) => r.Low == r.High ? $"{r.Low}{unit}" : $"{r.Low}–{r.High}{unit}";
        var power = Between(Prototype.Range(design.Horsepower, preview.Spread), " hp");
        var weight = Between(Prototype.Range(design.WeightKg, preview.Spread, 0.5, higherIsBetter: false), " kg");
        var reliability = design.Reliability - preview.Faults * Prototype.FaultReliability;
        // Tyre wear against a typical 200 hp, 750 kg car of the day.
        var typical = LiveRace.TyreWear(new Car("", 200, 750, 0, 0));
        var wearNow = LiveRace.TyreWear(now) / typical;
        var wearDesign = LiveRace.TyreWear(design) / typical;

        // Two figures to a row, so the card stays short enough to fit under the form.
        var card = new PanelContainer { ThemeTypeVariation = "CardTight" };
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 4);
        stack.AddChild(new Label { Text = $"{design.Name} against the {now.Name}", ThemeTypeVariation = "Heading" });
        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 18);
        stack.AddChild(grid);
        for (var i = 0; i < 2; i++)
        {
            grid.AddChild(new Label { Text = "" });
            grid.AddChild(new Label { Text = "NOW", ThemeTypeVariation = "Overline" });
            grid.AddChild(new Label { Text = "NEW", ThemeTypeVariation = "Overline" });
        }
        void Cell(string what, string a, string b, int better)
        {
            grid.AddChild(new Label { Text = what, ThemeTypeVariation = "Muted" });
            grid.AddChild(new Label { Text = a, ThemeTypeVariation = "Muted" });
            grid.AddChild(new Label { Text = b, Modulate = better > 0 ? UiTheme.Good : better < 0 ? new Color(1, 0.65f, 0.55f) : Colors.White });
        }
        Cell("Power", $"{now.Horsepower} hp", power, design.Horsepower.CompareTo(now.Horsepower));
        Cell("Weight", $"{now.WeightKg} kg", weight, now.WeightKg.CompareTo(design.WeightKg));
        Cell("Handling", now.Handling.ToString(), $"about {design.Handling}", design.Handling.CompareTo(now.Handling));
        Cell("Reliability", $"{now.Reliability:P0}", preview.Faults > 0 ? $"about {reliability:P0} at first" : $"about {design.Reliability:P0}", reliability.CompareTo(now.Reliability));
        Cell("Fuel use", $"{now.Thirst:P0}", $"{design.Thirst:P0}", now.Thirst.CompareTo(design.Thirst));
        Cell("Tyre wear", $"{wearNow:P0}", $"{wearDesign:P0}", Math.Round(wearNow, 2).CompareTo(Math.Round(wearDesign, 2)));
        card.AddChild(stack);
        return card;
    }
}
