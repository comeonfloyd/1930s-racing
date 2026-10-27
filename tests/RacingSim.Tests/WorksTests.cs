namespace RacingSim.Tests;

public class WorksTests
{
    private static Career NewCareer(string team = "alfa-rossa", int year = 1930) => new(TestData.Load(), team, year, seed: 5);

    [Fact]
    public void ATeamStartsWithItsRealStaffAndALevelOneWorks()
    {
        var alfa = NewCareer();
        Assert.Equal("Vittorio Jano", alfa.StaffIn(StaffRole.Engineer)?.Name);
        Assert.Equal("Luigi Bazzi", alfa.StaffIn(StaffRole.Mechanic)?.Name);
        Assert.Equal("Enzo Ferrari", alfa.StaffIn(StaffRole.Manager)?.Name);
        Assert.All(new[] { Facility.DrawingOffice, Facility.Workshop, Facility.Transport }, f => Assert.Equal(1, alfa.Level(f)));
        Assert.False(alfa.HasTestTrack);
        // Rival works' people are not for hire; free ones and the team's own later people are.
        Assert.DoesNotContain(alfa.StaffForHire, s => s.Id == "guerino-bertocchi");
        Assert.Contains(alfa.StaffForHire, s => s.Id == "giulio-ramponi");
    }

    [Fact]
    public void BuildingTakesMoneyAndWeeksThenTakesEffect()
    {
        var career = NewCareer();
        var weeks = career.ProjectWeeks(Upgrade.NewCar);
        var cash = career.Cash;
        Assert.Equal(BuildResult.Started, career.Build(Facility.DrawingOffice));
        Assert.Equal(cash - Works.BuildCost(Facility.DrawingOffice, 2), career.Cash);
        Assert.Equal(BuildResult.AlreadyBuilding, career.Build(Facility.Workshop));
        Assert.Equal(BuildResult.NeedsWorkshop, NewCareer().Build(Facility.TestTrack));

        career.AdvanceDays(7 * (Works.BuildWeeks(Facility.DrawingOffice, 2) - 1));
        Assert.Equal(1, career.Level(Facility.DrawingOffice));
        career.AdvanceDays(7);
        Assert.Equal(2, career.Level(Facility.DrawingOffice));
        Assert.Null(career.Building);
        Assert.True(career.ProjectWeeks(Upgrade.NewCar) < weeks);
        Assert.Contains(career.News, n => n.Text.StartsWith("The drawing office is built up to level 2"));
    }

    [Fact]
    public void FacilitiesAndStaffChangeTheMoney()
    {
        // A better transport department makes travel cheaper.
        var plain = NewCareer();
        var hauled = NewCareer();
        hauled.Build(Facility.Transport);
        hauled.AdvanceDays(7 * Works.BuildWeeks(Facility.Transport, 2));
        plain.AdvanceDays(7 * Works.BuildWeeks(Facility.Transport, 2));
        var far = plain.CurrentSeason.Events.Where(e => e.Date > plain.Today).MaxBy(e => plain.TravelTo(e).Cost)!;
        Assert.True(hauled.TravelTo(far).Cost < plain.TravelTo(far).Cost);

        // A better team manager brings more start money; a better engineer cheaper projects.
        Assert.True(Works.StartMoneyFactor(20) > Works.StartMoneyFactor(10));
        Assert.True(Works.EngineerCostFactor(20) < Works.EngineerCostFactor(10));
        // Staff wages and upkeep are part of the weekly bill.
        Assert.True(plain.WeeklyCosts > Finance.WeeklyCosts(plain.Drivers));
    }

    [Fact]
    public void HiringReplacesWhoeverHeldThePost()
    {
        var career = NewCareer("bugetti");
        var cash = career.Cash;
        var ramponi = TestData.Load().Staff["giulio-ramponi"];
        Assert.Equal(HireResult.Hired, career.Hire(ramponi.Id));
        Assert.Equal(ramponi, career.StaffIn(StaffRole.Mechanic));
        Assert.Equal(cash - Works.HiringFee(ramponi), career.Cash);
        Assert.Equal(HireResult.NotAvailable, career.Hire(ramponi.Id));
        Assert.Contains(career.StaffForHire, s => s.Id == "robert-aumaitre");
    }

    [Fact]
    public void ABetterWorkshopMeansFewerBreakdowns()
    {
        var car = TestData.Load().Teams["alfa-rossa"].Car;
        Assert.True(Works.Prepared(car, 3).Reliability > Works.Prepared(car, 2).Reliability);
        Assert.Equal(car.Reliability, Works.Prepared(car, 1).Reliability, 6);
    }

    [Fact]
    public void AGoodPitCrewStopsFaster()
    {
        var data = TestData.Load();
        var career = NewCareer(year: 1933);
        var ev = career.CurrentSeason.Events.Single(e => e.IsEndurance);
        career.Enter(ev.Id);
        var grid = career.GridFor(ev);
        double PitSeconds(int crew)
        {
            var crews = grid.Select(e => e.Team.Id).Distinct().ToDictionary(t => t, _ => crew);
            var race = new LiveRace(ev, data.Circuits[ev.CircuitId], grid, 11, pitCrews: crews);
            race.RunToEnd();
            return race.Messages.Where(m => m.Text.Contains(" pits for ")).Select(m => double.Parse(m.Text.Split(", ")[^1].Split(' ')[0])).Average();
        }
        Assert.True(PitSeconds(20) < PitSeconds(5));
    }

    [Fact]
    public void TestDaysMakeTheCarMoreReliableOnceAWeek()
    {
        var career = NewCareer();
        var before = career.Car.Reliability;
        var cash = career.Cash;
        var result = career.TestDay();
        Assert.Contains(result, new[] { TestResult.Tested, TestResult.DriverHurt });
        Assert.True(career.Car.Reliability > before);
        Assert.Equal(cash - Works.HiredTestCost, career.Cash);
        Assert.Equal(TestResult.AlreadyTestedThisWeek, career.TestDay());
        career.AdvanceDays(7);
        Assert.NotEqual(TestResult.AlreadyTestedThisWeek, career.TestDay());
    }

    [Fact]
    public void TheWorksIsSaved()
    {
        var data = TestData.Load();
        var career = NewCareer();
        career.Build(Facility.Workshop);
        career.AdvanceDays(14);
        career.Hire("giulio-ramponi");
        career.TestDay();
        var loaded = Career.FromJson(data, career.ToJson());
        Assert.Equal(career.Building, loaded.Building);
        Assert.Equal("Giulio Ramponi", loaded.StaffIn(StaffRole.Mechanic)?.Name);
        Assert.Equal(career.Car.Reliability, loaded.Car.Reliability);
        Assert.True(loaded.TestedThisWeek);
        loaded.AdvanceDays(7 * 6);
        Assert.Equal(2, loaded.Level(Facility.Workshop));
    }
}
