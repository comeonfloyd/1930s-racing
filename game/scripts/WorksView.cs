using System;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// The Works tab of the season screen: the team's facilities and what building each up does,
/// its three senior staff and the people free to hire, and test days.
/// </summary>
public partial class CalendarScreen
{
    private void WorksView(Career career)
    {
        _body.AddChild(new Label { Text = "STAFF", ThemeTypeVariation = "Section" });
        foreach (var role in Enum.GetValues<StaffRole>())
        {
            var member = career.StaffIn(role);
            var label = new Label { Text = "In post", ThemeTypeVariation = "Muted", CustomMinimumSize = new Vector2(110, 0), HorizontalAlignment = HorizontalAlignment.Center, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            _body.AddChild(StaffCard(career, role, member, label, mine: true));
        }

        _body.AddChild(new Label { Text = "FACILITIES", ThemeTypeVariation = "Section" });
        foreach (var facility in Enum.GetValues<Facility>())
            _body.AddChild(FacilityCard(career, facility));

        _body.AddChild(new Label { Text = "TESTING", ThemeTypeVariation = "Section" });
        _body.AddChild(TestingCard(career));

        _body.AddChild(new Label { Text = "FOR HIRE", ThemeTypeVariation = "Section" });
        var free = career.StaffForHire;
        if (free.Count == 0)
            _body.AddChild(new Label { Text = "Nobody is looking for work this year.", ThemeTypeVariation = "Note" });
        foreach (var member in free)
        {
            var fee = Works.HiringFee(member);
            var current = career.StaffIn(member.Role);
            var hire = new Button
            {
                Text = $"Hire · £{fee:N0}", ThemeTypeVariation = "Accent", SizeFlagsVertical = SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(110, 0), Disabled = fee > career.Cash,
                TooltipText = $"Hiring fee £{fee:N0} now, then £{Works.Wage(member)} a week."
                              + (current is null ? "" : $" {current.Name} leaves the team."),
            };
            var id = member.Id;
            hire.Pressed += () =>
            {
                Notice(career.Hire(id) switch
                {
                    HireResult.NotEnoughMoney => "Not enough money for the hiring fee.",
                    HireResult.NotAvailable => "They are no longer looking for work.",
                    _ => "",
                });
                Refresh();
            };
            _body.AddChild(StaffCard(career, member.Role, member, hire, mine: false));
        }
    }

    private void Notice(string text)
    {
        _notice.Text = text;
        _notice.Visible = text.Length > 0;
    }

    private Control FacilityCard(Career career, Facility facility)
    {
        var level = career.Level(facility);
        var max = Works.MaxLevel(facility);
        var card = new PanelContainer { ThemeTypeVariation = "CardTight" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(Badge(facility == Facility.TestTrack ? (level > 0 ? "✓" : "–") : level.ToString(),
            facility == Facility.TestTrack ? (level > 0 ? "BUILT" : "NONE") : $"OF {max}", level > Works.StartLevel(facility)));

        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 2);
        titles.AddChild(new Label { Text = Works.Name(facility), ThemeTypeVariation = "Heading" });
        var upkeep = Works.Upkeep(facility, level);
        titles.AddChild(new Label
        {
            Text = Capital(Works.Effect(facility, level)) + (upkeep > 0 ? $" · £{upkeep} a week" : ""),
            ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var job = career.Building;
        if (job?.Facility == facility)
        {
            titles.AddChild(new Label { Text = $"Building: {job.Weeks - job.WeeksDone} weeks left", ThemeTypeVariation = "Muted" });
            titles.AddChild(new ProgressBar { MaxValue = 1, Step = 0, Value = job.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6) });
        }
        else if (level < max)
            titles.AddChild(new Label
            {
                Text = $"Next: {Works.Effect(facility, level + 1)}, £{Works.Upkeep(facility, level + 1)} a week · {Works.BuildWeeks(facility, level + 1)} weeks to build",
                ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        row.AddChild(titles);

        if (level < max && job?.Facility != facility)
        {
            var cost = Works.BuildCost(facility, level + 1);
            var needsWorkshop = facility == Facility.TestTrack && career.Level(Facility.Workshop) < 3;
            var build = new Button
            {
                Text = $"Build · £{cost:N0}", SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(124, 0),
                Disabled = cost > career.Cash || job is not null || needsWorkshop,
                TooltipText = needsWorkshop ? "Build the workshop up to level 3 first."
                    : job is not null ? $"The builders are busy with the {Works.Name(job.Facility).ToLowerInvariant()}."
                    : $"Paid now; ready in {Works.BuildWeeks(facility, level + 1)} weeks.",
            };
            build.Pressed += () =>
            {
                Notice(career.Build(facility) switch
                {
                    BuildResult.NotEnoughMoney => "Not enough money to build that.",
                    BuildResult.AlreadyBuilding => "One building job at a time.",
                    BuildResult.NeedsWorkshop => "Build the workshop up to level 3 first.",
                    _ => "",
                });
                Refresh();
            };
            row.AddChild(build);
        }
        card.AddChild(row);
        return card;
    }

    private Control TestingCard(Career career)
    {
        var card = new PanelContainer { ThemeTypeVariation = "CardTight" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(Badge($"{career.Car.Reliability * 100:0}", "RELIAB.", false));
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 2);
        titles.AddChild(new Label { Text = career.HasTestTrack ? "Test day at the team's track" : "Test day on a hired circuit", ThemeTypeVariation = "Heading" });
        titles.AddChild(new Label
        {
            Text = $"A driver runs the {career.Car.Name} until something breaks, and the works fixes it. A new car gains the most. Once a week; now and then the driver crashes and is hurt."
                   + (career.TeethingFaults > 0 ? $" {career.TeethingFaults} teething trouble{(career.TeethingFaults == 1 ? "" : "s")} still to find." : ""),
            ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        row.AddChild(titles);
        var test = new Button
        {
            Text = career.TestedThisWeek ? "Tested this week" : $"Test · £{career.TestCost:N0}",
            SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(124, 0),
            Disabled = career.TestedThisWeek || career.TestCost > career.Cash,
        };
        test.Pressed += () =>
        {
            Notice(career.TestDay() switch
            {
                TestResult.NotEnoughMoney => "Not enough money for a test day.",
                TestResult.NoDriver => "No fit driver to test the car.",
                TestResult.AlreadyTestedThisWeek => "The car has been tested this week already.",
                TestResult.DriverHurt => career.News[^1].Text,
                _ => "",
            });
            Refresh();
        };
        row.AddChild(test);
        card.AddChild(row);
        return card;
    }

    private Control StaffCard(Career career, StaffRole role, StaffMember? member, Control action, bool mine)
    {
        var card = new PanelContainer { ThemeTypeVariation = "CardTight" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(StaffPhoto(member?.Id, new Vector2(64, 80)));
        row.AddChild(Badge(member?.Skill.ToString() ?? "–", "SKILL", mine && member is not null));
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 2);
        titles.AddChild(new Label { Text = member?.Name ?? "Nobody in post", ThemeTypeVariation = "Heading" });
        titles.AddChild(new Label
        {
            Text = $"{Career.RoleName(role)}" + (member is null ? "" : $" · {member.Nationality} · £{Works.Wage(member)} a week"),
            ThemeTypeVariation = "Muted",
        });
        var skill = member?.Skill ?? Works.EmptyPostSkill;
        titles.AddChild(new Label { Text = RoleEffect(role, skill), ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        row.AddChild(titles);
        row.AddChild(action);
        card.AddChild(row);
        return card;
    }

    /// <summary>What a person of this skill does in the post, against an average one.</summary>
    private static string RoleEffect(StaffRole role, int skill)
    {
        string Change(double factor, string more, string less) =>
            Math.Abs(factor - 1) < 0.005 ? "average" : factor > 1 ? $"{(factor - 1) * 100:0}% {more}" : $"{(1 - factor) * 100:0}% {less}";
        return role switch
        {
            StaffRole.Engineer => $"Car projects: {Change(Works.EngineerCostFactor(skill), "dearer", "cheaper")}",
            StaffRole.Mechanic => $"Pit stops: {Change(Works.PitTimeFactor(skill), "slower", "quicker")}, {Works.FumbleChance(skill) * 100:0}% fumbled",
            _ => $"Start money: {Change(Works.StartMoneyFactor(skill), "more", "less")}",
        };
    }

    private static PanelContainer Badge(string value, string caption, bool accent)
    {
        var badge = new PanelContainer { ThemeTypeVariation = "Badge", CustomMinimumSize = new Vector2(64, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", -4);
        stack.AddChild(new Label { Text = value, ThemeTypeVariation = "Big", HorizontalAlignment = HorizontalAlignment.Center }.Tint(accent ? UiTheme.Red : null));
        stack.AddChild(new Label { Text = caption, ThemeTypeVariation = "Overline", HorizontalAlignment = HorizontalAlignment.Center });
        badge.AddChild(stack);
        return badge;
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
