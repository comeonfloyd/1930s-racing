namespace RacingSim.Tests;

public class IndyTests
{
    /// <summary>Buys an American special and waits for it, so the team can enter the 500.</summary>
    public static void BuySpecial(Career career)
    {
        Assert.Equal(ProjectResult.Queued, career.BuyIndyCar());
        career.AdvanceDays(7 * Indianapolis.BuyWeeks);
        Assert.NotNull(career.IndyCar);
    }

    [Fact]
    public void TheGrandPrixCarCannotRunTheJunkFormula500()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1932, seed: 2);
        Assert.Equal(EntryResult.NoIndyCar, career.Enter("1932-indianapolis"));
    }

    [Fact]
    public void ABoughtSpecialArrivesAfterItsWeeksAndRacesAtIndianapolis()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1932, seed: 2);
        var cash = career.Cash;
        var forSale = career.IndyCarForSale;
        Assert.Equal(ProjectResult.Queued, career.BuyIndyCar());
        Assert.Equal(cash - Indianapolis.BuyCost, career.Cash);
        Assert.Equal(ProjectResult.NewCarUnderway, career.BuyIndyCar());
        Assert.Null(career.IndyCar);
        Assert.Equal(EntryResult.NoIndyCar, career.Enter("1932-indianapolis"));

        career.AdvanceDays(7 * Indianapolis.BuyWeeks);
        Assert.Equal(forSale, career.IndyCar);
        Assert.Equal(EntryResult.Entered, career.Enter("1932-indianapolis"));
        var ev = career.NextRace!;
        var mine = career.GridFor(ev).Where(e => e.Team.Id == "alfa-rossa").ToList();
        Assert.NotEmpty(mine);
        Assert.All(mine, e => Assert.EndsWith("Special", e.Team.Car.Name));
        // The Grand Prix car stays the team's car for Europe.
        Assert.Equal("Alfa Rossa P3", career.Car.Name);

        var loaded = Career.FromJson(TestData.Load(), career.ToJson());
        Assert.Equal(career.IndyCar, loaded.IndyCar);
        Assert.Equal(career.IndyDesign, loaded.IndyDesign);
    }

    [Fact]
    public void TheJunkFormulaBansSuperchargersAndBigEngines()
    {
        var design = Indianapolis.Default(1935);
        Assert.Null(Indianapolis.Breach(design, "X", 1935));
        Assert.NotNull(Indianapolis.Breach(design with { Supercharging = Supercharging.Single }, "X", 1935));
        Assert.NotNull(Indianapolis.Breach(design with { Litres = 6.5 }, "X", 1935));
        // From 1938 the international rules: 3 litres supercharged, 4.5 without.
        Assert.NotNull(Indianapolis.Breach(design, "X", 1938));
        Assert.Null(Indianapolis.Breach(design with { Litres = 4.5 }, "X", 1938));
        Assert.Null(Indianapolis.Breach(design with { Litres = 3.0, Supercharging = Supercharging.Single }, "X", 1938));
        Assert.NotNull(Indianapolis.Breach(design with { Litres = 3.5, Supercharging = Supercharging.Single }, "X", 1938));
    }

    [Fact]
    public void AnOwnIndianapolisDesignIsBuiltBesideTheGrandPrixCar()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1934, seed: 2);
        var blown = Indianapolis.Default(1934) with { Supercharging = Supercharging.Single };
        Assert.Equal(ProjectResult.BreaksRules, career.StartDesign(blown, indy: true));

        var design = Indianapolis.Default(1934);
        var preview = career.Preview(design, indy: true);
        Assert.Null(preview.Breach);
        // Two seats for the riding mechanic weigh more than the same car as a single-seater.
        Assert.True(preview.Car.WeightKg > CarDesigner.Build(design, "Alfa Rossa", 1934).WeightKg);
        Assert.Equal(ProjectResult.NotResearched, career.StartDesign(design, indy: true));
        CarDesignTests.ResearchFor(career, design);
        preview = career.Preview(design, indy: true);
        Assert.Equal(ProjectResult.Queued, career.StartDesign(design, indy: true));
        Assert.Equal(ProjectResult.NewCarUnderway, career.BuyIndyCar());
        // A new Grand Prix car may still be designed alongside.
        Assert.False(career.NewCarUnderway);

        career.AdvanceDays(7 * (preview.Weeks + CarDesignTests.MostOverrunWeeks));
        CarDesignTests.AsEstimated(preview, career.IndyCar);
        Assert.NotEqual(preview.Car, career.Car);
    }

    [Fact]
    public void From1938TheGrandPrixCarMayRunTheFiveHundred()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1938, seed: 2);
        Assert.True(career.Car.EngineLitres <= 3.0);
        Assert.Equal(EntryResult.Entered, career.Enter("1938-indianapolis"));
        Assert.Equal(career.Car, career.RaceCarFor(career.NextRace!));
    }
}
