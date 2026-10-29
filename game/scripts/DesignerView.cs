using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// The car designer, the Drawing office tab: a drawing board with the blueprint of each part.
/// Pick a part on the right to open its drawings on the left and choose between them; see the
/// car it makes next to the current one, and order it.
/// </summary>
public partial class CalendarScreen
{
    // The design being drawn up, one for each kind of car; kept while the player looks at other tabs.
    private static CarDesign? _draft, _indyDraft;
    private static bool _designIndy;
    // The part whose drawings are open on the board.
    private static string _designGroup = "engine";

    /// <summary>One part of the design: its choices, their names and first years, and how to pick one.</summary>
    private sealed record PartGroup(string Key, string Label, string[] Choices, string[] Names, int[] FirstYears, int Chosen, Action<int> Set);

    private static PartGroup Group<T>(string key, string label, T chosen, Func<T, string> name, Func<T, int> first, Action<T> set)
        where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return new PartGroup(key, label, values.Select(v => v.ToString()).ToArray(), values.Select(name).ToArray(),
            values.Select(first).ToArray(), Array.IndexOf(values, chosen), i => set(values[i]));
    }

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
        top.AddChild(new Label { Text = indy ? "DESIGN AN INDIANAPOLIS CAR" : "DESIGN A NEW CAR", ThemeTypeVariation = "Section", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center });
        if (indy)
        {
            var grandPrix = new Button { Text = "‹  Grand Prix car", ThemeTypeVariation = "Tab" };
            grandPrix.Pressed += () => OpenDesigner(indy: false);
            top.AddChild(grandPrix);
        }
        _body.AddChild(top);
        _body.AddChild(new Label
        {
            Text = indy
                ? $"A special for the 500, built with the engineering of {preview.Year} to its {Indianapolis.Name(preview.Year).ToLowerInvariant()}: {Indianapolis.Rule(preview.Year)}. "
                  + "It races only at Indianapolis."
                : $"Your own car, built with the engineering of {preview.Year}. Pick a part on the right to see its drawings; parts not yet invented are greyed out.",
            ThemeTypeVariation = "Note", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        var year = preview.Year;
        var d = draft;
        // The 500's junk formula bans superchargers until the AAA takes up the international rules.
        int BoostFirstYear(Supercharging s) => indy && s != Supercharging.None
            ? Math.Max(Indianapolis.SingleSeaterYear, CarDesigner.FirstYear(s)) : CarDesigner.FirstYear(s);
        PartGroup[] groups =
        [
            Group("engine", "Engine", d.Layout, CarDesigner.Name, CarDesigner.FirstYear, v => SetDraft(Draft with { Layout = v })),
            Group("supercharging", "Supercharging", d.Supercharging, CarDesigner.Name, BoostFirstYear, v => SetDraft(Draft with { Supercharging = v })),
            Group("frame", "Frame", d.Frame, CarDesigner.Name, CarDesigner.FirstYear, v => SetDraft(Draft with { Frame = v })),
            Group("suspension", "Suspension", d.Suspension, CarDesigner.Name, CarDesigner.FirstYear, v => SetDraft(Draft with { Suspension = v })),
            Group("brakes", "Brakes", d.Brakes, CarDesigner.Name, CarDesigner.FirstYear, v => SetDraft(Draft with { Brakes = v })),
            Group("body", "Body", d.Body, CarDesigner.Name, CarDesigner.FirstYear, v => SetDraft(Draft with { Body = v })),
        ];
        var open = groups.FirstOrDefault(g => g.Key == _designGroup) ?? groups[0];

        var board = new HBoxContainer();
        board.AddThemeConstantOverride("separation", 14);
        board.AddChild(DrawingBoard(open, year, open.Key == "engine" ? (d.Litres, MaxLitres(indy, year)) : null));
        var parts = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        parts.AddThemeConstantOverride("separation", 6);
        foreach (var g in groups)
            parts.AddChild(PartRow(g, g == open, g.Key == "engine" ? $" · {d.Litres:0.00} litres" : ""));
        board.AddChild(parts);
        _body.AddChild(board);
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
            ThemeTypeVariation = "Figure", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
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
            ThemeTypeVariation = "Note", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        if (CarDesigner.FrameFlex(d, preview.Year) is > 0 and var flex)
            _body.AddChild(new Label
            {
                Text = $"The {CarDesigner.Name(d.Frame).ToLowerInvariant()} twists under an engine this strong: {flex} handling lost. A stiffer frame would hold it.",
                ThemeTypeVariation = "OnDark", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            }.Tint(UiTheme.Gold));
        var engineHp = CarDesigner.EngineHorsepower(d, preview.Year);
        if (engineHp > preview.Car.Horsepower)
            _body.AddChild(new Label
            {
                Text = $"The engine makes {engineHp} hp, but the tyres of {preview.Year} put down only about {CarDesigner.TyreLimit(preview.Year):0} hp: "
                       + $"the rest goes in wheelspin, so it counts as {preview.Car.Horsepower} hp, and the strain costs handling and reliability.",
                ThemeTypeVariation = "OnDark", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            }.Tint(UiTheme.Gold));

        if (preview.Breach is { } breach)
            _body.AddChild(new Label { Text = $"{breach} It could not race {(indy ? "at Indianapolis" : "in Europe")}.", ThemeTypeVariation = "OnDark", AutowrapMode = TextServer.AutowrapMode.WordSmart }.Tint(new Color("eaa08a")));
        else if ((indy ? Indianapolis.Breach(d, preview.Car.Name, preview.Year + 1) : Formula.Breach(preview.Car, preview.Year + 1)) is { } next && career.HasNextSeason)
            _body.AddChild(new Label { Text = $"Next year: {next}", ThemeTypeVariation = "OnDark", AutowrapMode = TextServer.AutowrapMode.WordSmart }.Tint(UiTheme.Gold));
    }

    /// <summary>The new parts the design needs that the drawing office has not studied, as one row of buttons.</summary>
    private Control ResearchCard(Career career, IReadOnlyList<Part> parts)
    {
        var card = new PanelContainer { ThemeTypeVariation = "CardTight" };
        var row = new HFlowContainer();
        card.AddChild(row);
        row.AddThemeConstantOverride("h_separation", 8);
        row.AddThemeConstantOverride("v_separation", 4);
        row.AddChild(new Label { Text = "Research first:", ThemeTypeVariation = "Strong", VerticalAlignment = VerticalAlignment.Center }.Tint(UiTheme.Red));
        foreach (var part in parts)
        {
            var queued = career.ResearchQueued(part);
            var cost = career.ResearchCost(part);
            var study = new Button
            {
                Text = queued ? $"{ShortName(part)} · studying" : $"{ShortName(part)} £{cost:N0}",
                Disabled = queued || cost > career.Cash,
                TooltipText = $"{Capital(Research.Name(part))}, new to racing in {Research.FirstYear(part)}. Paid now, then {career.ResearchWeeks(part)} weeks in the drawing office; once studied, every design may use it. "
                              + "It gets cheaper each year as others show how it is done.",
            };
            study.Pressed += () =>
            {
                Notice(career.StartResearch(part) == ProjectResult.NotEnoughMoney ? "Not enough money for that research." : "");
                Refresh();
            };
            row.AddChild(study);
        }
        return card;
    }

    private static string ShortName(Part part) => part switch
    {
        Part.IndependentFront => "Independent front",
        Part.AllIndependent => "All-independent",
        Part.TwoStage => "Two-stage",
        Part.BoxedLadder => "Boxed frame",
        _ => Capital(Research.Name(part)),
    };

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

    /// <summary>
    /// The open part's drawings: the chosen one large, as a pinned-up blueprint, and every choice
    /// beneath it to pick from. For the engine, the size slider too.
    /// </summary>
    private Control DrawingBoard(PartGroup group, int year, (double Litres, double Max)? engine)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
        column.AddThemeConstantOverride("separation", 8);
        var mount = new PanelContainer { ThemeTypeVariation = "Mount" };
        mount.AddChild(new TextureRect
        {
            Texture = Blueprint(group.Key, group.Choices[group.Chosen]), CustomMinimumSize = new Vector2(472, 295),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
        });
        column.AddChild(mount);

        var card = new PanelContainer { ThemeTypeVariation = "CardTight" };
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 6);
        stack.AddChild(new Label { Text = $"{group.Label.ToUpperInvariant()} · CHOOSE ONE", ThemeTypeVariation = "Overline" });
        var options = new HBoxContainer();
        options.AddThemeConstantOverride("separation", 6);
        var width = (460 - 6 * (group.Choices.Length - 1)) / group.Choices.Length - 8;
        for (var i = 0; i < group.Choices.Length; i++)
        {
            var late = group.FirstYears[i] > year;
            var option = new Button
            {
                ThemeTypeVariation = "Part", ToggleMode = true, ButtonPressed = i == group.Chosen, Disabled = late,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = late ? $"{group.Names[i]}: not invented until {group.FirstYears[i]}" : group.Names[i],
            };
            var inner = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            inner.AddThemeConstantOverride("separation", 2);
            inner.SetAnchorsPreset(LayoutPreset.FullRect);
            inner.AddChild(new TextureRect
            {
                Texture = Thumbnail(group.Key, group.Choices[i]), CustomMinimumSize = new Vector2(width, width * 0.625f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                TextureFilter = TextureFilterEnum.LinearWithMipmaps,
                MouseFilter = MouseFilterEnum.Ignore, Modulate = late ? new Color(1, 1, 1, 0.35f) : Colors.White,
            });
            inner.AddChild(new Label
            {
                Text = late ? $"From {group.FirstYears[i]}" : Short(group.Names[i]), HorizontalAlignment = HorizontalAlignment.Center,
                ThemeTypeVariation = "Muted", ClipText = true, MouseFilter = MouseFilterEnum.Ignore,
            }.Tint(i == group.Chosen ? UiTheme.Red : late ? new Color(UiTheme.InkMuted, 0.6f) : null));
            option.AddChild(inner);
            // Buttons do not size to children: give it room for the picture and the name.
            option.CustomMinimumSize = new Vector2(width + 8, width * 0.625f + 30);
            var index = i;
            option.Pressed += () =>
            {
                group.Set(index);
                Refresh();
            };
            options.AddChild(option);
        }
        stack.AddChild(options);
        if (engine is { } e)
            stack.AddChild(Litres(e.Litres, e.Max));
        card.AddChild(stack);
        column.AddChild(card);
        return column;

        static string Short(string name) => name.Replace(" body", "").Replace(" frame", "").Replace(" brakes", "");
    }

    /// <summary>One part of the design on the right of the board: its blueprint, its name, and a click opens its drawings.</summary>
    private Control PartRow(PartGroup group, bool open, string extra)
    {
        var button = new Button { ThemeTypeVariation = "Part", ToggleMode = true, ButtonPressed = open, CustomMinimumSize = new Vector2(0, 62) };
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = row.OffsetTop = 4;
        row.OffsetRight = row.OffsetBottom = -4;
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new TextureRect
        {
            Texture = Thumbnail(group.Key, group.Choices[group.Chosen]), CustomMinimumSize = new Vector2(86, 54),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspect, MouseFilter = MouseFilterEnum.Ignore,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
        });
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        names.AddThemeConstantOverride("separation", -4);
        names.AddChild(new Label { Text = group.Label.ToUpperInvariant(), ThemeTypeVariation = "Overline", MouseFilter = MouseFilterEnum.Ignore });
        names.AddChild(new Label { Text = group.Names[group.Chosen] + extra, ThemeTypeVariation = "Heading", ClipText = true, MouseFilter = MouseFilterEnum.Ignore });
        row.AddChild(names);
        row.AddChild(new Label { Text = open ? "" : "›", ThemeTypeVariation = "Heading", VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore }.Tint(UiTheme.InkMuted));
        button.AddChild(row);
        button.Pressed += () =>
        {
            _designGroup = group.Key;
            Refresh();
        };
        return button;
    }

    private Control Litres(double litres, double max)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new Label { Text = "Engine size", ThemeTypeVariation = "Muted" });
        var value = new Label { Text = $"{litres:0.00} litres", CustomMinimumSize = new Vector2(90, 0), ThemeTypeVariation = "Strong" };
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
        return row;
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
            grid.AddChild(new Label { Text = b, ThemeTypeVariation = "Strong" }.Tint(better > 0 ? UiTheme.GoodInk : better < 0 ? UiTheme.Red : null));
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
