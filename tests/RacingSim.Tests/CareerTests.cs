namespace RacingSim.Tests;

public class CareerTests
{
    [Fact]
    public void StartsWithTheTeamsCash()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1930);

        Assert.Equal(new DateOnly(1930, 1, 1), career.Today);
        Assert.Equal(12000, career.Cash);
    }

    [Fact]
    public void AiOnlyTeamsAreNotPlayable()
    {
        Assert.Throws<ArgumentException>(() => new Career(TestData.Load(), "masarelli", 1930));
    }

    [Fact]
    public void CanEnterEuropeanRacesInSequence()
    {
        var career = new Career(TestData.Load(), "bugetti", 1930);

        Assert.Equal(EntryResult.Entered, career.Enter("1930-monaco"));
        Assert.Equal(EntryResult.Entered, career.Enter("1930-eifel"));
        Assert.Equal(EntryResult.AlreadyEntered, career.Enter("1930-monaco"));
        Assert.Equal(new[] { "1930-monaco", "1930-eifel" }, career.Entries.Select(e => e.Id));
    }

    [Fact]
    public void CannotRaceMonzaAndIndianapolisSixDaysApart()
    {
        // 1931: Italian Grand Prix on 24 May, Indianapolis 500 on 30 May. The Atlantic crossing alone takes a week.
        var career = new Career(TestData.Load(), "alfa-rossa", 1931);

        Assert.Equal(EntryResult.Entered, career.Enter("1931-indianapolis"));
        Assert.Equal(EntryResult.CannotArriveInTime, career.Enter("1931-italy"));
    }

    [Fact]
    public void CanReturnFromIndianapolisForTheEifelrennen()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1930);

        Assert.Equal(EntryResult.Entered, career.Enter("1930-indianapolis"));
        Assert.Equal(EntryResult.Entered, career.Enter("1930-eifel"));
    }

    [Fact]
    public void PastEventsCannotBeEntered()
    {
        var career = new Career(TestData.Load(), "alfa-rossa", 1930);
        career.AdvanceDays(120);

        Assert.Equal(EntryResult.EventPassed, career.Enter("1930-monaco"));
    }
}
