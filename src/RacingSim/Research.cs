using System.Text.Json.Serialization;

namespace RacingSim;

/// <summary>The parts that came into racing during the decade; the 1930 ones need no research.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Part { V8, V12, V16, TwoStage, BoxedLadder, Tubular, IndependentFront, AllIndependent, Hydraulic, Streamlined }

/// <summary>
/// A part's first year only makes it possible: the drawing office must study it before a design
/// may use it, which costs money and weeks in the queue. Each year after the part first raced,
/// others have shown how it is done and the research gets cheaper.
/// </summary>
public static class Research
{
    public static int FirstYear(Part p) => p switch
    {
        Part.V8 => CarDesigner.FirstYear(EngineLayout.V8),
        Part.V12 => CarDesigner.FirstYear(EngineLayout.V12),
        Part.V16 => CarDesigner.FirstYear(EngineLayout.V16),
        Part.TwoStage => CarDesigner.FirstYear(Supercharging.TwoStage),
        Part.BoxedLadder => CarDesigner.FirstYear(Frame.BoxedLadder),
        Part.Tubular => CarDesigner.FirstYear(Frame.Tubular),
        Part.IndependentFront => CarDesigner.FirstYear(Suspension.IndependentFront),
        Part.AllIndependent => CarDesigner.FirstYear(Suspension.AllIndependent),
        Part.Hydraulic => CarDesigner.FirstYear(Brakes.Hydraulic),
        _ => CarDesigner.FirstYear(Body.Streamlined),
    };

    public static string Name(Part p) => p switch
    {
        Part.V8 => "V8 engine",
        Part.V12 => "V12 engine",
        Part.V16 => "V16 engine",
        Part.TwoStage => "two-stage supercharging",
        Part.BoxedLadder => "boxed ladder frame",
        Part.Tubular => "tubular frame",
        Part.IndependentFront => "independent front suspension",
        Part.AllIndependent => "all-independent suspension",
        Part.Hydraulic => "hydraulic brakes",
        _ => "streamlined body",
    };

    private static int BaseCost(Part p) => p switch
    {
        Part.V8 => 1200,
        Part.V12 => 2500,
        Part.V16 => 3000,
        Part.TwoStage => 2500,
        Part.BoxedLadder => 800,
        Part.Tubular => 1500,
        Part.IndependentFront => 1500,
        Part.AllIndependent => 2200,
        Part.Hydraulic => 600,
        _ => 900,
    };

    /// <summary>What studying a part costs in a year, before the chief engineer's skill: a quarter less each year after it first raced.</summary>
    public static int Cost(Part p, int year) =>
        (int)(Math.Round(BaseCost(p) * Math.Max(0.4, 1 - 0.25 * Math.Max(0, year - FirstYear(p))) / 50) * 50);

    /// <summary>Weeks of drawing-office work, before a bigger drawing office speeds it up.</summary>
    public static int Weeks(Part p) => 4 + BaseCost(p) / 600;

    /// <summary>The new parts a design uses.</summary>
    public static IEnumerable<Part> Uses(CarDesign d)
    {
        switch (d.Layout)
        {
            case EngineLayout.V8: yield return Part.V8; break;
            case EngineLayout.V12: yield return Part.V12; break;
            case EngineLayout.V16: yield return Part.V16; break;
        }
        if (d.Supercharging == Supercharging.TwoStage)
            yield return Part.TwoStage;
        if (d.Frame == Frame.BoxedLadder)
            yield return Part.BoxedLadder;
        if (d.Frame == Frame.Tubular)
            yield return Part.Tubular;
        if (d.Suspension == Suspension.IndependentFront)
            yield return Part.IndependentFront;
        if (d.Suspension == Suspension.AllIndependent)
            yield return Part.AllIndependent;
        if (d.Brakes == Brakes.Hydraulic)
            yield return Part.Hydraulic;
        if (d.Body == Body.Streamlined)
            yield return Part.Streamlined;
    }

    /// <summary>
    /// Chance a design or a piece of research overruns when it should be finished: a prototype
    /// fails on the test bench, or a drawing does not work. Rarer with a good chief engineer.
    /// </summary>
    public static double OverrunChance(int engineerSkill) => Math.Clamp(0.3 - 0.01 * engineerSkill, 0.08, 0.3);
}

public sealed partial class Career
{
    private readonly HashSet<Part> _researched = new();

    /// <summary>True once the drawing office has studied a part (or it raced before the career began).</summary>
    public bool Knows(Part part) => _researched.Contains(part);

    /// <summary>The parts a design uses that the drawing office has not yet studied.</summary>
    public IReadOnlyList<Part> Unresearched(CarDesign design) => Research.Uses(design).Where(p => !Knows(p)).ToList();

    public int ResearchCost(Part part) =>
        (int)(Math.Round(Research.Cost(part, Today.Year) * Works.EngineerCostFactor(SkillOf(StaffRole.Engineer)) / 50) * 50);

    public int ResearchWeeks(Part part) => Works.ProjectWeeks(Research.Weeks(part), Level(Facility.DrawingOffice));

    public bool ResearchQueued(Part part) => _projects.Any(p => p.Upgrade == Upgrade.Research && p.Part == part);

    /// <summary>Pays to study a part and puts the work in the drawing-office queue.</summary>
    public ProjectResult StartResearch(Part part)
    {
        if (Research.FirstYear(part) > Today.Year)
            return ProjectResult.PartsNotInvented;
        if (Knows(part) || ResearchQueued(part))
            return ProjectResult.AtLimit;
        var cost = ResearchCost(part);
        if (cost > Cash)
            return ProjectResult.NotEnoughMoney;
        Pay(MoneyKind.Development, $"Research: {Research.Name(part)}", -cost);
        _projects.Add(new Project(Upgrade.Research, cost, ResearchWeeks(part)) { Part = part });
        return ProjectResult.Queued;
    }

    /// <summary>Parts already racing before the career began are known to the drawing office.</summary>
    private void KnowPartsBefore(int year)
    {
        foreach (var part in Enum.GetValues<Part>().Where(p => Research.FirstYear(p) < year))
            _researched.Add(part);
    }

    /// <summary>
    /// When an own design or research should be finished, it may overrun once: some weeks more,
    /// and for a car a tenth more money. Returns true when it did and the work goes on.
    /// </summary>
    private bool Overruns(Project project, DateOnly date)
    {
        if (project.Overran || (project.Upgrade != Upgrade.Research && project.Design is null))
            return false;
        var rng = new Random(Seeds.Mix(Seed, date.DayNumber * 5 + project.Cost));
        if (rng.NextDouble() >= Research.OverrunChance(SkillOf(StaffRole.Engineer)))
            return false;
        var weeks = 1 + rng.Next(4);
        string what;
        if (project.Design is not null)
        {
            var extra = (int)(Math.Round(project.Cost * 0.1 / 50) * 50);
            Pay(MoneyKind.Development, $"Overrun: {project.Built?.Name}", -extra, date);
            what = $"The prototype {project.Built?.Name} fails on the test bench: {weeks} more week{(weeks == 1 ? "" : "s")} and £{extra:N0} to put it right.";
        }
        else
            what = $"The drawings for the {Research.Name(project.Part!.Value)} do not work first time: {weeks} more week{(weeks == 1 ? "" : "s")}.";
        _projects[0] = project with { Weeks = project.Weeks + weeks, Overran = true };
        _news.Add(new NewsItem(date, what));
        return true;
    }
}
