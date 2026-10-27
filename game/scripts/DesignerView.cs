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
    // The design being drawn up; kept while the player looks at other tabs.
    private static CarDesign? _draft;

    private void DesignerView(Career career)
    {
        var preview = career.Preview(_draft ??= CarDesigner.Default(career.Today.Year));
        _draft = Fit(_draft, preview.Year);
        preview = career.Preview(_draft);

        var top = new HBoxContainer();
        top.AddChild(new Label { Text = "DESIGN A NEW CAR", ThemeTypeVariation = "Overline", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var back = new Button { Text = "‹  Back to the calendar" };
        back.Pressed += () => { _view = View.Calendar; Refresh(); };
        top.AddChild(back);
        _body.AddChild(top);
        _body.AddChild(new Label
        {
            Text = $"Your own car, built with the engineering of {preview.Year}. Parts not yet invented that year are greyed out. "
                   + "The works' next model stays on offer in the drawing office as the cheaper choice.",
            ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 8);
        card.AddChild(grid);
        var year = preview.Year;
        Choice(grid, "Engine", Enum.GetValues<EngineLayout>(), _draft.Layout, CarDesigner.Name, CarDesigner.FirstYear, year, v => _draft = _draft with { Layout = v });
        Litres(grid, _draft.Litres);
        Choice(grid, "Supercharging", Enum.GetValues<Supercharging>(), _draft.Supercharging, CarDesigner.Name, CarDesigner.FirstYear, year, v => _draft = _draft with { Supercharging = v });
        Choice(grid, "Frame", Enum.GetValues<Frame>(), _draft.Frame, CarDesigner.Name, CarDesigner.FirstYear, year, v => _draft = _draft with { Frame = v });
        Choice(grid, "Suspension", Enum.GetValues<Suspension>(), _draft.Suspension, CarDesigner.Name, CarDesigner.FirstYear, year, v => _draft = _draft with { Suspension = v });
        Choice(grid, "Brakes", Enum.GetValues<Brakes>(), _draft.Brakes, CarDesigner.Name, CarDesigner.FirstYear, year, v => _draft = _draft with { Brakes = v });
        Choice(grid, "Body", Enum.GetValues<Body>(), _draft.Body, CarDesigner.Name, CarDesigner.FirstYear, year, v => _draft = _draft with { Body = v });
        _body.AddChild(card);

        _body.AddChild(Comparison(career.Car, preview.Car));
        var engineHp = CarDesigner.EngineHorsepower(_draft, preview.Year);
        if (engineHp > preview.Car.Horsepower)
            _body.AddChild(new Label
            {
                Text = $"The engine makes {engineHp} hp, but the tyres of {preview.Year} put down only about {CarDesigner.TyreLimit(preview.Year):0} hp: "
                       + $"the rest goes in wheelspin, so it counts as {preview.Car.Horsepower} hp, and the strain costs handling and reliability.",
                Modulate = UiTheme.Accent, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });

        var order = new HBoxContainer();
        order.AddThemeConstantOverride("separation", 12);
        var when = preview.Year > career.Today.Year ? $", racing from 1 January {preview.Year}" : "";
        var queued = career.Projects.Count > 0 ? " after the work already ordered" : "";
        order.AddChild(new Label
        {
            Text = $"£{preview.Cost:N0} · {preview.Weeks} weeks{queued}{when}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var why = career.NewCarUnderway ? "A new car is already being built."
            : preview.TooEarly.Count > 0 ? $"Not invented yet: {string.Join(", ", preview.TooEarly)}."
            : preview.Cost > career.Cash ? "Not enough money."
            : "";
        var go = new Button
        {
            Text = "Order this car  ›", ThemeTypeVariation = "Accent", CustomMinimumSize = new Vector2(160, 0),
            Disabled = why.Length > 0, TooltipText = why.Length > 0 ? why : "Paid now. It replaces the car you have when it is built, and its development starts afresh.",
        };
        go.Pressed += () =>
        {
            var result = career.StartDesign(_draft!);
            if (result == ProjectResult.Queued)
            {
                _draft = null;
                _view = View.Calendar;
            }
            Notice(result switch
            {
                ProjectResult.NotEnoughMoney => "Not enough money for that design.",
                ProjectResult.PartsNotInvented => "That design uses parts not invented yet.",
                ProjectResult.NewCarUnderway => "A new car is already being built.",
                _ => "",
            });
            Refresh();
        };
        order.AddChild(go);
        _body.AddChild(order);
        if (preview.Breach is { } breach)
            _body.AddChild(new Label { Text = $"{breach} It could not race in Europe.", Modulate = new Color(1, 0.65f, 0.55f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        else if (Formula.Breach(preview.Car, preview.Year + 1) is { } next && career.HasNextSeason)
            _body.AddChild(new Label { Text = $"Next year: {next}", Modulate = UiTheme.Accent, AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    /// <summary>A design brought up to the parts of a year: anything not yet invented falls back to the plainer choice.</summary>
    private static CarDesign Fit(CarDesign d, int year)
    {
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
        grid.AddChild(new Label { Text = label, ThemeTypeVariation = "Muted", VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(120, 0) });
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

    private void Litres(GridContainer grid, double litres)
    {
        grid.AddChild(new Label { Text = "Engine size", ThemeTypeVariation = "Muted", VerticalAlignment = VerticalAlignment.Center });
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);
        var value = new Label { Text = $"{litres:0.00} litres", CustomMinimumSize = new Vector2(90, 0) };
        var slider = new HSlider
        {
            MinValue = CarDesigner.MinLitres, MaxValue = CarDesigner.MaxLitres, Step = CarDesigner.LitreStep, Value = litres,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        slider.ValueChanged += v => value.Text = $"{v:0.00} litres";
        // Rebuild only when the drag ends, so the screen does not flicker under the mouse.
        slider.DragEnded += changed =>
        {
            if (!changed)
                return;
            _draft = _draft! with { Litres = slider.Value };
            Refresh();
        };
        row.AddChild(slider);
        row.AddChild(value);
        grid.AddChild(row);
    }

    /// <summary>The car you have against the one on the drawing board, figure by figure.</summary>
    private static Control Comparison(Car now, Car design)
    {
        var card = new PanelContainer { ThemeTypeVariation = "CardTight" };
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddChild(new Label { Text = "", CustomMinimumSize = new Vector2(120, 0) });
        grid.AddChild(new Label { Text = now.Name, ThemeTypeVariation = "Muted" });
        grid.AddChild(new Label { Text = design.Name, ThemeTypeVariation = "Heading" });
        void Row(string what, string a, string b, int better)
        {
            grid.AddChild(new Label { Text = what, ThemeTypeVariation = "Muted" });
            grid.AddChild(new Label { Text = a, ThemeTypeVariation = "Muted" });
            grid.AddChild(new Label { Text = b, Modulate = better > 0 ? UiTheme.Good : better < 0 ? new Color(1, 0.65f, 0.55f) : Colors.White });
        }
        Row("Power", $"{now.Horsepower} hp", $"{design.Horsepower} hp", design.Horsepower.CompareTo(now.Horsepower));
        Row("Weight", $"{now.WeightKg} kg", $"{design.WeightKg} kg", now.WeightKg.CompareTo(design.WeightKg));
        Row("Handling", now.Handling.ToString(), design.Handling.ToString(), design.Handling.CompareTo(now.Handling));
        Row("Reliability", $"{now.Reliability:P0}", $"{design.Reliability:P0}", design.Reliability.CompareTo(now.Reliability));
        Row("Engine", $"{now.EngineLitres:0.0#} L", $"{design.EngineLitres:0.0#} L", 0);
        card.AddChild(grid);
        return card;
    }
}
