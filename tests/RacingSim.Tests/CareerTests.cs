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

public class SeasonTests
{
    private static Career NewCareer(string team = "alfa-rossa") => new(TestData.Load(), team, 1930, seed: 7);

    [Fact]
    public void WeeklyCostsArePaidAsTimePasses()
    {
        var career = NewCareer();
        career.AdvanceTo(new DateOnly(1930, 1, 29));

        Assert.Equal(4, career.Ledger.Count(t => t.Kind == MoneyKind.Wages));
        Assert.Equal(12000 - 4 * Finance.WeeklyCosts(3) + 4 * Finance.WeeklyBacking(70), career.Cash);
    }

    [Fact]
    public void RacesTheTeamSkipsAreStillRun()
    {
        var career = NewCareer();
        career.AdvanceTo(new DateOnly(1930, 5, 1));

        var monaco = Assert.Single(career.Results);
        Assert.Equal("1930-monaco", monaco.Event.Id);
        Assert.False(monaco.Entered);
        Assert.DoesNotContain(monaco.Result.Classification, c => c.Entry.Team.Id == "alfa-rossa");
    }

    [Fact]
    public void AnEnteredRacePaysTravelStartAndPrizeMoney()
    {
        var career = NewCareer();
        career.Enter("1930-monza");
        var race = career.PrepareRace();
        Assert.Equal(new DateOnly(1930, 9, 7), career.Today);
        Assert.Contains(career.Ledger, t => t.Kind == MoneyKind.Travel && t.Amount < 0);

        race.RunToEnd();
        var cashBefore = career.Cash;
        var record = career.FinishRace(race);

        Assert.True(record.Entered);
        var prizes = record.Result.Classification
            .Select((c, i) => (c, i))
            .Where(x => x.c.Entry.Team.Id == "alfa-rossa" && x.c.Finished)
            .Sum(x => Finance.Prize(record.Event, x.i + 1));
        Assert.Equal(300 * 3 + prizes, record.Earned);
        Assert.Equal(cashBefore + record.Earned, career.Cash);
        Assert.Null(career.NextRace);
    }

    [Fact]
    public void TimeCannotSkipPastAnEnteredRace()
    {
        var career = NewCareer();
        career.Enter("1930-monaco");

        Assert.Throws<InvalidOperationException>(() => career.AdvanceTo(new DateOnly(1930, 5, 1)));
    }

    [Fact]
    public void AWholeSeasonCanBePlayedAndTheNextOneStarted()
    {
        var career = NewCareer("bugetti");
        foreach (var id in new[] { "1930-monaco", "1930-eifel", "1930-belgium", "1930-monza" })
            Assert.Equal(EntryResult.Entered, career.Enter(id));
        while (career.NextRace is not null)
        {
            var race = career.PrepareRace();
            race.RunToEnd();
            career.FinishRace(race);
        }
        career.AdvanceTo(new DateOnly(1930, 12, 31));

        Assert.True(career.SeasonOver);
        var summary = career.Summary(1930);
        Assert.Equal(4, summary.Races);
        Assert.Equal(career.Cash, 11000 + summary.Income - summary.Costs);

        career.StartNextSeason();
        Assert.Equal(1931, career.CurrentSeason.Year);
        Assert.False(career.SeasonOver);
    }

    [Fact]
    public void TheSameSeedPlaysTheSameSeason()
    {
        static string Winners()
        {
            var career = new Career(TestData.Load(), "alfa-rossa", 1930, seed: 3);
            career.AdvanceTo(new DateOnly(1930, 12, 31));
            return string.Join(",", career.Results.Select(r => r.Winner.Entry.Driver.Id));
        }

        Assert.Equal(Winners(), Winners());
    }

    [Fact]
    public void DriversKilledInARaceDoNotStartAgain()
    {
        var data = TestData.Load();
        // Run seasons until someone is killed, then check they are gone from the next grid.
        for (var seed = 0; seed < 200; seed++)
        {
            var career = new Career(data, "alfa-rossa", 1930, seed);
            career.AdvanceTo(new DateOnly(1930, 12, 31));
            var dead = career.DeadDrivers.FirstOrDefault();
            if (dead is null)
                continue;
            var nextSeason = data.Seasons[1931].Events[0];
            Assert.DoesNotContain(career.GridFor(nextSeason), e => e.Driver.Id == dead);
            return;
        }
        Assert.Fail("No fatal crash in 200 seasons; the test needs another seed range.");
    }
}
