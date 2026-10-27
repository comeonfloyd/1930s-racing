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

        career.AdvanceDays(7 * preview.Weeks);
        Assert.Equal(preview.Car, career.Car);
        Assert.Equal(preview.Car, career.DesignBase);
        // Development goes on from the design.
        Assert.Equal(ProjectResult.Queued, career.StartProject(Upgrade.Engine));
        var loaded = Career.FromJson(TestData.Load(), career.ToJson());
        Assert.Equal(career.DesignBase, loaded.DesignBase);
    }

    [Fact]
    public void ADesignFinishedAfterNewYearIsBuiltForNextSeasonAndWaits()
    {
        var data = TestData.Load();
        var career = new Career(data, "alfa-rossa", 1933, seed: 5);
        career.AdvanceTo(new DateOnly(1933, 11, 1));
        var design = CarDesigner.Default(1934);
        Assert.NotEmpty(CarDesigner.TooEarly(design, 1933));
        var preview = career.Preview(design);
        Assert.Equal(1934, preview.Year);
        Assert.Empty(preview.TooEarly);
        Assert.Null(preview.Breach);
        Assert.Equal(ProjectResult.Queued, career.StartDesign(design));
        var old = career.Car;
        career.AdvanceTo(new DateOnly(1933, 12, 31));
        Assert.Equal(old, career.Car);
        career.StartNextSeason();
        career.AdvanceDays(7 * preview.Weeks);
        Assert.Equal(preview.Car, career.Car);
        Assert.Equal(1934, career.ModelYear);
    }
}
