namespace RacingSim.Tests;

public class SaveTests
{
    private static void PlayNextRace(Career career)
    {
        var race = career.PrepareRace();
        race.RunToEnd();
        career.FinishRace(race);
    }

    [Fact]
    public void ALoadedCareerCarriesOnExactlyAsTheOriginal()
    {
        var data = TestData.Load();
        var original = new Career(data, "alfa-rossa", 1930, seed: 11);
        original.StartProject(Upgrade.Engine);
        original.StartProject(Upgrade.Handling);
        foreach (var id in new[] { "1930-monaco", "1930-eifel", "1930-monza" })
            original.Enter(id);
        PlayNextRace(original);

        var loaded = Career.FromJson(data, original.ToJson());
        Assert.Equal(original.Today, loaded.Today);
        Assert.Equal(original.Cash, loaded.Cash);
        Assert.Equal(original.Car, loaded.Car);
        Assert.Equal(original.Projects, loaded.Projects);
        Assert.Equal(original.Results.Select(r => r.Winner.Entry.Driver.Id), loaded.Results.Select(r => r.Winner.Entry.Driver.Id));

        foreach (var career in new[] { original, loaded })
        {
            while (career.NextRace is not null)
                PlayNextRace(career);
            career.AdvanceTo(new DateOnly(1930, 12, 31));
        }

        Assert.Equal(original.Cash, loaded.Cash);
        Assert.Equal(original.Car, loaded.Car);
        Assert.Equal(original.Ledger, loaded.Ledger);
        Assert.Equal(original.Summary(1930), loaded.Summary(1930), new SummaryComparer());
    }

    [Fact]
    public void SavesFromANewerGameAreRefused()
    {
        var data = TestData.Load();
        var save = new Career(data, "bugetti", 1930).ToSave() with { Version = Career.SaveVersion + 1 };

        Assert.Throws<InvalidDataException>(() => Career.FromSave(data, save));
    }

    private sealed class SummaryComparer : IEqualityComparer<SeasonSummary>
    {
        public bool Equals(SeasonSummary? a, SeasonSummary? b) =>
            a is not null && b is not null && a with { DriversLost = [] } == b with { DriversLost = [] } && a.DriversLost.SequenceEqual(b.DriversLost);

        public int GetHashCode(SeasonSummary s) => s.Year;
    }
}
