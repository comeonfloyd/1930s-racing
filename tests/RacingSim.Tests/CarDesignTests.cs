namespace RacingSim.Tests;

public class CarDesignTests
{
    [Fact]
    public void RealDesignsComeOutCloseToTheRealCars()
    {
        var data = TestData.Load();
        // The engine's own output is checked: past the tyre limit some of it goes in wheelspin.
        void Close(Car real, CarDesign design, int year)
        {
            var built = CarDesigner.Build(design, "X", year);
            Assert.InRange(CarDesigner.EngineHorsepower(design, year), real.Horsepower * 0.85, real.Horsepower * 1.15);
            Assert.InRange(built.WeightKg, real.WeightKg - 60, real.WeightKg + 60);
            Assert.InRange(built.Handling, real.Handling - 3, real.Handling + 3);
        }
        Close(data.Teams["alfa-rossa"].CarFor(1932),
            new(EngineLayout.Straight8, 2.65, Supercharging.Single, Frame.Ladder, Suspension.BeamAxles, Brakes.Mechanical, Body.OpenWheel), 1932);
        Close(data.Teams["mercator-benz"].CarFor(1934),
            new(EngineLayout.Straight8, 3.4, Supercharging.Single, Frame.BoxedLadder, Suspension.IndependentFront, Brakes.Hydraulic, Body.OpenWheel), 1934);
        Close(data.Teams["mercator-benz"].CarFor(1937),
            new(EngineLayout.Straight8, 5.66, Supercharging.Single, Frame.Tubular, Suspension.IndependentFront, Brakes.Hydraulic, Body.OpenWheel), 1937);
        Close(data.Teams["mercator-benz"].CarFor(1938),
            new(EngineLayout.V12, 3.0, Supercharging.TwoStage, Frame.Tubular, Suspension.IndependentFront, Brakes.Hydraulic, Body.OpenWheel), 1938);
    }

    [Fact]
    public void MoreEngineMeansMorePowerAndWeight()
    {
        var small = CarDesigner.Default(1934);
        var big = small with { Litres = 4.5 };
        var a = CarDesigner.Build(small, "X", 1934);
        var b = CarDesigner.Build(big, "X", 1934);
        Assert.True(b.Horsepower > a.Horsepower);
        Assert.True(b.WeightKg > a.WeightKg);
        Assert.True(b.Reliability < a.Reliability);
        Assert.True(CarDesigner.Cost(big) > CarDesigner.Cost(small));
        // A blower adds power; the same engine is better every year.
        Assert.True(CarDesigner.Build(small with { Supercharging = Supercharging.None }, "X", 1934).Horsepower < a.Horsepower);
        Assert.True(CarDesigner.Build(small, "X", 1936).Horsepower > a.Horsepower);
    }

    [Fact]
    public void PowerBeyondTheTyresMostlyGoesInWheelspin()
    {
        var monster = new CarDesign(EngineLayout.Straight8, 7.0, Supercharging.Single, Frame.Ladder, Suspension.BeamAxles, Brakes.Mechanical, Body.OpenWheel);
        var car = CarDesigner.Build(monster, "X", 1930);
        Assert.True(CarDesigner.EngineHorsepower(monster, 1930) > car.Horsepower);
        Assert.True(car.Horsepower < CarDesigner.TyreLimit(1930) * 1.5);
        Assert.True(car.Reliability < CarDesigner.Build(CarDesigner.Default(1930), "X", 1930).Reliability);
    }

    [Fact]
    public void PartsCannotBeUsedBeforeTheyExist()
    {
        var modern = CarDesigner.Default(1938);
        Assert.NotEmpty(CarDesigner.TooEarly(modern with { Supercharging = Supercharging.TwoStage }, 1936));
        Assert.Empty(CarDesigner.TooEarly(CarDesigner.Default(1930), 1930));
        var career = new Career(TestData.Load(), "alfa-rossa", 1930, seed: 5);
        Assert.Equal(ProjectResult.PartsNotInvented, career.StartDesign(modern));
    }

    [Fact]
    public void AnOwnDesignIsPaidBuiltAndRaced()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1930, seed: 5);
        var design = CarDesigner.Default(1930);
        var preview = career.Preview(design);
        Assert.Equal(1930, preview.Year);
        var cash = career.Cash;
        Assert.Equal(ProjectResult.Queued, career.StartDesign(design));
        Assert.Equal(cash - preview.Cost, career.Cash);
        Assert.Equal(ProjectResult.NewCarUnderway, career.StartDesign(design));
        Assert.Null(career.NextModel);

        career.AdvanceDays(7 * (preview.Weeks + MostOverrunWeeks));
        AsEstimated(preview, career.Car);
        Assert.Equal(career.Car, career.DesignBase);
        Assert.True(career.TeethingFaults >= 1);
        // Development goes on from the design.
        Assert.Equal(ProjectResult.Queued, career.StartProject(Upgrade.Engine));
        var loaded = Career.FromJson(TestData.Load(), career.ToJson());
        Assert.Equal(career.DesignBase, loaded.DesignBase);
        Assert.Equal(career.TeethingFaults, loaded.TeethingFaults);
    }

    /// <summary>The longest an own design or research can overrun.</summary>
    public const int MostOverrunWeeks = 4;

    /// <summary>Studies every part a design needs and waits until the drawing office is done.</summary>
    public static void ResearchFor(Career career, CarDesign design)
    {
        var weeks = 0;
        foreach (var part in career.Unresearched(design).Where(p => !career.ResearchQueued(p)))
        {
            Assert.Equal(ProjectResult.Queued, career.StartResearch(part));
            weeks += career.ResearchWeeks(part) + MostOverrunWeeks;
        }
        career.AdvanceDays(7 * weeks);
        Assert.Empty(career.Unresearched(design));
    }

    [Fact]
    public void NewPartsMustBeResearchedBeforeADesignMayUseThem()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1934, seed: 6);
        var design = CarDesigner.Default(1934);
        // Parts that raced before the career began are known; this year's are not.
        Assert.True(career.Knows(Part.BoxedLadder));
        Assert.Contains(Part.Tubular, career.Unresearched(design));
        Assert.Equal(ProjectResult.NotResearched, career.StartDesign(design));
        Assert.Equal(ProjectResult.PartsNotInvented, career.StartResearch(Part.V12));

        var cash = career.Cash;
        var cost = career.ResearchCost(Part.Tubular);
        Assert.Equal(ProjectResult.Queued, career.StartResearch(Part.Tubular));
        Assert.Equal(cash - cost, career.Cash);
        Assert.Equal(ProjectResult.AtLimit, career.StartResearch(Part.Tubular));
        ResearchFor(career, design);
        Assert.Equal(ProjectResult.Queued, career.StartDesign(design));
        var loaded = Career.FromJson(TestData.Load(), career.ToJson());
        Assert.True(loaded.Knows(Part.Tubular));
        // A fresh design can start from the parts the drawing office knows.
        var fresh = new Career(TestData.Load(), "alfa-rossa", 1934, seed: 6);
        var known = fresh.KnownParts(design with { Layout = EngineLayout.V16 });
        Assert.Empty(fresh.Unresearched(known));
        Assert.Equal(Frame.BoxedLadder, known.Frame);
        // Research gets cheaper once others have shown how it is done.
        Assert.True(Research.Cost(Part.Tubular, 1936) < Research.Cost(Part.Tubular, 1934));
    }

    [Fact]
    public void OwnDesignsSometimesOverrun()
    {
        var overran = 0;
        for (var seed = 1; seed <= 60; seed++)
        {
            var career = new Career(TestData.Load(), "alfa-rossa", 1930, seed: seed);
            var preview = career.Preview(CarDesigner.Default(1930));
            career.StartDesign(CarDesigner.Default(1930));
            career.AdvanceDays(7 * preview.Weeks);
            if (career.DesignBase is null)
            {
                overran++;
                Assert.True(career.Projects[0].Overran);
                career.AdvanceDays(7 * MostOverrunWeeks);
                Assert.NotNull(career.DesignBase);
            }
        }
        // Jano is a fine engineer: about one design in eight overruns.
        Assert.InRange(overran, 2, 20);
    }

    /// <summary>The built car is the estimate give or take the spread.</summary>
    public static void AsEstimated(DesignPreview preview, Car? built)
    {
        Assert.NotNull(built);
        Assert.Equal(preview.Car.Name, built.Name);
        var (low, high) = Prototype.Range(preview.Car.Horsepower, preview.Spread);
        Assert.InRange(built.Horsepower, low, high);
        (low, high) = Prototype.Range(preview.Car.WeightKg, preview.Spread, 0.5, higherIsBetter: false);
        Assert.InRange(built.WeightKg, low, high);
        Assert.True(built.Reliability <= preview.Car.Reliability * (1 + preview.Spread / 2));
    }

    [Fact]
    public void ABetterEngineerNarrowsTheEstimateAndFindsFewerFaults()
    {
        Assert.True(Prototype.Spread(20) < Prototype.Spread(6));
        var design = CarDesigner.Default(1934) with { Layout = EngineLayout.V8 };
        Assert.True(Prototype.ExpectedFaults(design, 1934, 18) < Prototype.ExpectedFaults(design, 1934, 6));
        // Parts new to racing bring more troubles than proven ones.
        Assert.True(Prototype.ExpectedFaults(design, 1934, 10) > Prototype.ExpectedFaults(CarDesigner.Default(1930), 1930, 10));
    }

    [Fact]
    public void BuiltCarsComeOutWorseThanEstimatedOnAverage()
    {
        var estimate = CarDesigner.Build(CarDesigner.Default(1934), "X", 1934);
        var rng = new Random(4);
        var cars = Enumerable.Range(0, 400).Select(_ => Prototype.Build(estimate, CarDesigner.Default(1934), 1934, 10, rng).Car).ToList();
        Assert.True(cars.Average(c => c.Horsepower) < estimate.Horsepower);
        Assert.Contains(cars, c => c.Horsepower > estimate.Horsepower);
        Assert.True(cars.Average(c => c.Reliability) < estimate.Reliability - Prototype.FaultReliability);
    }

    [Fact]
    public void TestDaysFindTeethingTroubles()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1930, seed: 5);
        var design = CarDesigner.Default(1930);
        var preview = career.Preview(design);
        career.StartDesign(design);
        career.AdvanceDays(7 * (preview.Weeks + MostOverrunWeeks));
        var faults = career.TeethingFaults;
        var reliability = career.Car.Reliability;
        for (var week = 0; week < 12 && career.TeethingFaults > 0; week++)
        {
            career.TestDay();
            career.AdvanceDays(7);
        }
        Assert.True(career.TeethingFaults < faults);
        Assert.True(career.Car.Reliability >= reliability + Prototype.FaultReliability * (faults - career.TeethingFaults));
    }

    [Fact]
    public void ADesignFinishedAfterNewYearIsBuiltForNextSeasonAndWaits()
    {
        var data = TestData.Load();
        var career = new Career(data, "alfa-rossa", 1933, seed: 5);
        career.AdvanceTo(new DateOnly(1933, 11, 1));
        var design = CarDesigner.Default(1933);
        var preview = career.Preview(design);
        Assert.Equal(1934, preview.Year);
        Assert.Empty(preview.TooEarly);
        Assert.Null(preview.Breach);
        Assert.Equal(ProjectResult.Queued, career.StartDesign(design));
        var old = career.Car;
        career.AdvanceTo(new DateOnly(1933, 12, 31));
        Assert.Equal(old, career.Car);
        career.StartNextSeason();
        career.AdvanceDays(7 * (preview.Weeks + MostOverrunWeeks));
        AsEstimated(preview, career.Car);
        Assert.True(career.TeethingFaults >= 1);
        Assert.Equal(1934, career.ModelYear);
    }

    [Fact]
    public void BigAndSuperchargedEnginesDrinkMoreFuel()
    {
        var plain = CarDesigner.Default(1934);
        Assert.Equal(1.0, CarDesigner.Thirst(plain with { Litres = 3.0 }), 2);
        Assert.True(CarDesigner.Thirst(plain with { Litres = 5.0 }) > 1.3);
        Assert.True(CarDesigner.Thirst(plain with { Supercharging = Supercharging.None }) < CarDesigner.Thirst(plain));
        Assert.True(CarDesigner.Thirst(CarDesigner.Default(1938) with { Supercharging = Supercharging.TwoStage })
                    > CarDesigner.Thirst(CarDesigner.Default(1938)));
        Assert.Equal(CarDesigner.Thirst(plain), CarDesigner.Build(plain, "X", 1934).Thirst);
    }

    [Fact]
    public void APlainFrameTwistsUnderABigEngine()
    {
        var big = CarDesigner.Default(1936) with { Litres = 5.0, Frame = Frame.Ladder };
        Assert.True(CarDesigner.FrameFlex(big, 1936) > 0);
        Assert.Equal(0, CarDesigner.FrameFlex(big with { Frame = Frame.Tubular }, 1936));
        Assert.True(CarDesigner.Build(big, "X", 1936).Handling < CarDesigner.Build(big with { Frame = Frame.Tubular }, "X", 1936).Handling);
    }

    [Fact]
    public void AThirstyCarMakesMoreStops()
    {
        var data = TestData.Load();
        var ev = data.Seasons[1930].Events.Single(e => e.Id == "1930-monza");
        int Stops(double thirst)
        {
            var grid = RaceSim.FullGrid(data).Select(e => e with { Team = e.Team with { Car = e.Team.Car with { Thirst = thirst, Reliability = 0.999 } } }).ToList();
            var race = new LiveRace(ev, data.Circuits[ev.CircuitId], grid, 4, new RaceSettings(false));
            race.RunToEnd();
            return race.Cars().Sum(c => c.PitStops);
        }
        Assert.True(Stops(1.5) > Stops(1.0));
    }
}
