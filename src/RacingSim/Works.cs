using System.Text.Json.Serialization;

namespace RacingSim;

/// <summary>The buildings of a team's works. The test track is built once; the rest have three levels.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Facility
{
    DrawingOffice,
    Workshop,
    Transport,
    TestTrack,
}

/// <summary>The three posts a team fills: one person each.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StaffRole
{
    Engineer,
    Mechanic,
    Manager,
}

/// <summary>
/// One of the engineers, mechanics and managers of the era, rated 1 to 20 like the drivers.
/// <paramref name="TeamId"/> is the works they started the decade with, or null for one free to hire.
/// </summary>
public sealed record StaffMember(
    string Id,
    string Name,
    StaffRole Role,
    int Skill,
    string Nationality,
    string? TeamId = null,
    int FirstYear = 1930,
    int LastYear = 1939);

/// <summary>A facility being built up to a level.</summary>
public sealed record FacilityJob(Facility Facility, int Level, int Weeks)
{
    public int WeeksDone { get; init; }
    public double Progress => Weeks == 0 ? 1 : WeeksDone / (double)Weeks;
}

public enum BuildResult
{
    Started,
    AtTopLevel,
    AlreadyBuilding,
    NotEnoughMoney,
    NeedsWorkshop,
}

public enum HireResult
{
    Hired,
    NotAvailable,
    NotEnoughMoney,
}

public enum TestResult
{
    Tested,
    DriverHurt,
    AlreadyTestedThisWeek,
    NotEnoughMoney,
    NoDriver,
}

/// <summary>
/// The rules of the works: what facilities and staff cost and what they do. All numbers are
/// first-pass placeholders, balanced with tools/CareerRunner.
/// </summary>
public static class Works
{
    public static int MaxLevel(Facility facility) => facility == Facility.TestTrack ? 1 : 3;

    /// <summary>Every team starts with a level 1 works and no test track.</summary>
    public static int StartLevel(Facility facility) => facility == Facility.TestTrack ? 0 : 1;

    public static string Name(Facility facility) => facility switch
    {
        Facility.DrawingOffice => "Drawing office",
        Facility.Workshop => "Workshop",
        Facility.Transport => "Transport",
        _ => "Test track",
    };

    public static string Effect(Facility facility, int level) => facility switch
    {
        Facility.DrawingOffice => level <= 1 ? "car projects at the usual pace" : $"car projects {100 - ProjectWeeksFactor(level) * 100:0}% quicker",
        Facility.Workshop => level <= 1 ? "cars prepared as usual" : $"{100 - FailureFactor(level) * 100:0}% fewer breakdowns in races",
        Facility.Transport => level <= 1 ? "hired trucks and rail" : $"travel {100 - TravelCostFactor(level) * 100:0}% cheaper" + (level >= 3 ? ", long trips a day shorter" : ""),
        _ => level <= 0 ? "test days on a hired circuit" : "cheap test days at home",
    };

    /// <summary>What it costs to build a facility up to a level.</summary>
    public static int BuildCost(Facility facility, int level) => facility switch
    {
        Facility.TestTrack => 10000,
        _ => level == 2 ? 2500 : 6000,
    };

    public static int BuildWeeks(Facility facility, int level) => facility switch
    {
        Facility.TestTrack => 16,
        _ => level == 2 ? 6 : 10,
    };

    /// <summary>Weekly running cost above the basic works.</summary>
    public static int Upkeep(Facility facility, int level) => facility == Facility.TestTrack
        ? (level > 0 ? 25 : 0)
        : 10 * (level - 1);

    public static double ProjectWeeksFactor(int drawingOffice) => drawingOffice switch { >= 3 => 0.65, 2 => 0.8, _ => 1 };

    /// <summary>A bigger drawing office finishes projects sooner, but none in under two weeks.</summary>
    public static int ProjectWeeks(int weeks, int drawingOffice) =>
        Math.Max(Math.Min(weeks, 2), (int)Math.Ceiling(weeks * ProjectWeeksFactor(drawingOffice)));

    public static double FailureFactor(int workshop) => workshop switch { >= 3 => 0.7, 2 => 0.85, _ => 1 };

    /// <summary>A car prepared in a better workshop: the same share of its breakdowns, fewer of them.</summary>
    public static Car Prepared(Car car, int workshop) =>
        car with { Reliability = 1 - (1 - car.Reliability) * FailureFactor(workshop) };

    public static double TravelCostFactor(int transport) => transport switch { >= 3 => 0.7, 2 => 0.85, _ => 1 };

    /// <summary>The team's own trucks and rail bookings make trips cheaper, and long ones shorter at the top level.</summary>
    public static TravelPlan Haul(TravelPlan plan, int transport) => plan with
    {
        Cost = (int)Math.Round(plan.Cost * TravelCostFactor(transport)),
        Days = transport >= 3 && plan.Days >= 3 ? plan.Days - 1 : plan.Days,
    };

    /// <summary>Weekly wage of a member of staff: a pound a week per point of skill.</summary>
    public static int Wage(StaffMember member) => member.Skill;

    /// <summary>Paid once to hire someone: six weeks of their wage.</summary>
    public static int HiringFee(StaffMember member) => 6 * Wage(member);

    /// <summary>The skill a post counts as when it is empty.</summary>
    public const int EmptyPostSkill = 6;

    /// <summary>A good chief engineer gets more from the money: skill 10 pays the list price, 20 a quarter less.</summary>
    public static double EngineerCostFactor(int skill) => 1.25 - 0.025 * skill;

    /// <summary>A good chief mechanic's crew works faster: skill 10 as now, 20 a fifth quicker.</summary>
    public static double PitTimeFactor(int skill) => 1.2 - 0.02 * skill;

    /// <summary>Chance a stop is fumbled: one in ten for skill 10, none for 20.</summary>
    public static double FumbleChance(int skill) => Math.Clamp(0.2 - 0.01 * skill, 0, 0.3);

    /// <summary>A good team manager talks organisers up: skill 10 gets the usual start money, 20 a fifth more.</summary>
    public static double StartMoneyFactor(int skill) => 0.8 + 0.02 * skill;

    /// <summary>A test day on a hired circuit, or on the team's own track.</summary>
    public const int HiredTestCost = 350, OwnTestCost = 75;

    /// <summary>A test day finds a tenth of the car's remaining weaknesses: much on a new car, little on a sorted one.</summary>
    public const double TestGainShare = 0.1;

    /// <summary>Chance the test driver crashes and is hurt.</summary>
    public const double TestCrashChance = 0.04;
}

public sealed partial class Career
{
    private readonly Dictionary<Facility, int> _facilities = Enum.GetValues<Facility>().ToDictionary(f => f, Works.StartLevel);
    private readonly Dictionary<StaffRole, string> _staff = new();
    private DateOnly? _lastTest;

    /// <summary>The facility being built, if any: one job at a time.</summary>
    public FacilityJob? Building { get; private set; }

    public int Level(Facility facility) => _facilities[facility];

    public bool HasTestTrack => Level(Facility.TestTrack) > 0;

    /// <summary>The person in a post, or null when it is empty.</summary>
    public StaffMember? StaffIn(StaffRole role) => _staff.TryGetValue(role, out var id) ? _data.Staff[id] : null;

    private int SkillOf(StaffRole role) => StaffIn(role)?.Skill ?? Works.EmptyPostSkill;

    /// <summary>People free to hire this year: not with a rival works and not already with the team.</summary>
    public IReadOnlyList<StaffMember> StaffForHire => _data.Staff.Values
        .Where(s => s.FirstYear <= Today.Year && Today.Year <= s.LastYear)
        .Where(s => (s.TeamId is null || s.TeamId == Team.Id) && !_staff.ContainsValue(s.Id))
        .OrderBy(s => s.Role).ThenByDescending(s => s.Skill).ThenBy(s => s.Id, StringComparer.Ordinal)
        .ToList();

    /// <summary>The works' own people in their real posts when the career starts.</summary>
    private void TakeOverStaff(int year)
    {
        foreach (var group in _data.Staff.Values
                     .Where(s => s.TeamId == Team.Id && s.FirstYear <= year && year <= s.LastYear)
                     .GroupBy(s => s.Role))
            _staff[group.Key] = group.OrderByDescending(s => s.Skill).First().Id;
    }

    /// <summary>Staff whose careers ended last year leave the team; their posts stand empty until filled.</summary>
    private void StaffRetire()
    {
        foreach (var (role, id) in _staff.ToList())
        {
            var member = _data.Staff[id];
            if (member.LastYear >= Today.Year)
                continue;
            _staff.Remove(role);
            _news.Add(new NewsItem(Today, $"{member.Name} leaves {Team.Name}. Hire a new {RoleName(role).ToLowerInvariant()}."));
        }
    }

    public static string RoleName(StaffRole role) => role switch
    {
        StaffRole.Engineer => "Chief engineer",
        StaffRole.Mechanic => "Chief mechanic",
        _ => "Team manager",
    };

    /// <summary>Hires someone into their post; whoever held it leaves.</summary>
    public HireResult Hire(string staffId)
    {
        var member = StaffForHire.FirstOrDefault(s => s.Id == staffId);
        if (member is null)
            return HireResult.NotAvailable;
        var fee = Works.HiringFee(member);
        if (fee > Cash)
            return HireResult.NotEnoughMoney;
        Pay(MoneyKind.Wages, $"Hiring fee, {member.Name}", -fee);
        if (StaffIn(member.Role) is { } previous)
            _news.Add(new NewsItem(Today, $"{previous.Name} leaves {Team.Name}."));
        _staff[member.Role] = member.Id;
        _news.Add(new NewsItem(Today, $"{member.Name} joins {Team.Name} as {RoleName(member.Role).ToLowerInvariant()} at £{Works.Wage(member)} a week."));
        return HireResult.Hired;
    }

    /// <summary>Pays for the next level of a facility and starts building it.</summary>
    public BuildResult Build(Facility facility)
    {
        var level = Level(facility) + 1;
        if (level > Works.MaxLevel(facility))
            return BuildResult.AtTopLevel;
        if (Building is not null)
            return BuildResult.AlreadyBuilding;
        if (facility == Facility.TestTrack && Level(Facility.Workshop) < 3)
            return BuildResult.NeedsWorkshop;
        var cost = Works.BuildCost(facility, level);
        if (cost > Cash)
            return BuildResult.NotEnoughMoney;
        Pay(MoneyKind.Development, $"{Works.Name(facility)}{(facility == Facility.TestTrack ? "" : $", level {level}")}", -cost);
        Building = new FacilityJob(facility, level, Works.BuildWeeks(facility, level));
        return BuildResult.Started;
    }

    /// <summary>A week of building work.</summary>
    private void WorkOnBuilding(DateOnly date)
    {
        if (Building is not { } job)
            return;
        job = job with { WeeksDone = job.WeeksDone + 1 };
        if (job.WeeksDone < job.Weeks)
        {
            Building = job;
            return;
        }
        Building = null;
        _facilities[job.Facility] = job.Level;
        _news.Add(new NewsItem(date, job.Facility == Facility.TestTrack
            ? "The team's own test track is finished: test days now cost little."
            : $"The {Works.Name(job.Facility).ToLowerInvariant()} is built up to level {job.Level}: {Works.Effect(job.Facility, job.Level)}."));
    }

    public int TestCost => HasTestTrack ? Works.OwnTestCost : Works.HiredTestCost;

    public bool TestedThisWeek => _lastTest is { } last && Today.DayNumber - last.DayNumber < 7;

    /// <summary>
    /// A day of testing: one of the team's fit drivers runs the car until something breaks,
    /// and the works fixes it, so the car gets more reliable. Once a week. Now and then the
    /// driver crashes and is hurt.
    /// </summary>
    public TestResult TestDay()
    {
        if (TestedThisWeek)
            return TestResult.AlreadyTestedThisWeek;
        var drivers = StartersOf(Team, Today);
        if (drivers.Count == 0)
            return TestResult.NoDriver;
        if (TestCost > Cash)
            return TestResult.NotEnoughMoney;
        Pay(MoneyKind.Development, HasTestTrack ? "Test day at the team's track" : "Test day on a hired circuit", -TestCost);
        _lastTest = Today;
        var rng = new Random(Seeds.Mix(Seed, Today.DayNumber * 7 + 3));
        var driver = _data.Drivers[drivers[rng.Next(drivers.Count)]];
        var gain = Math.Max(0, (DrawingOffice.MaxReliability - Car.Reliability) * Works.TestGainShare);
        Car = Car with { Reliability = Math.Round(Car.Reliability + gain, 4) };
        FindFault(rng, Prototype.TestFindChance, Today, $"{driver.Name}'s test day");
        if (rng.NextDouble() < Works.TestCrashChance)
        {
            var weeks = 2 + rng.Next(4);
            _injuredUntil[driver.Id] = Today.AddDays(7 * weeks);
            _news.Add(new NewsItem(Today, $"{driver.Name} crashes testing and is hurt: out for {weeks} weeks."));
            return TestResult.DriverHurt;
        }
        _news.Add(new NewsItem(Today, gain > 0.001
            ? $"{driver.Name} tests the {Car.Name}: the works fixes what broke. Reliability now {Car.Reliability:P0}."
            : $"{driver.Name} tests the {Car.Name}, which runs without fault: there is nothing left to learn."));
        return TestResult.Tested;
    }

    /// <summary>Weekly running cost of the facilities and the staff.</summary>
    private int WorksCosts =>
        _facilities.Sum(f => Works.Upkeep(f.Key, f.Value)) + _staff.Values.Sum(id => Works.Wage(_data.Staff[id]));

    /// <summary>Each team's chief mechanic's skill, for pit stops: the player's own, or a works' real one.</summary>
    private IReadOnlyDictionary<string, int> PitCrews(int year)
    {
        var crews = _data.Staff.Values
            .Where(s => s.Role == StaffRole.Mechanic && s.TeamId is not null && s.TeamId != Team.Id && s.FirstYear <= year && year <= s.LastYear && !_staff.ContainsValue(s.Id))
            .GroupBy(s => s.TeamId ?? "")
            .ToDictionary(g => g.Key, g => g.Max(s => s.Skill));
        crews[Team.Id] = SkillOf(StaffRole.Mechanic);
        return crews;
    }
}
