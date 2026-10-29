namespace RacingSim.Tests;

public class SponsorTests
{
    [Fact]
    public void OffersFollowStandingAndHomeMakersPayMore()
    {
        var data = TestData.Load();
        var alfa = new Career(data, "alfa-rossa", 1932, seed: 3);
        var offers = alfa.SponsorOffers();
        Assert.Contains(offers, o => o.SponsorId == "shellux");
        // No German state tyres before 1934.
        Assert.DoesNotContain(offers, o => o.SponsorId == "kontinental");
        Assert.Contains(new Career(data, "alfa-rossa", 1934, seed: 3).SponsorOffers(), o => o.SponsorId == "kontinental");

        // Across careers, an Italian maker pays an Italian team more than a French one.
        double Mean(string team) => Enumerable.Range(1, 20)
            .Average(seed => new Career(data, team, 1932, seed: seed).SponsorOffers().Single(o => o.SponsorId == "pirella").WeeklyFee
                             / (double)data.Teams[team].Prestige);
        Assert.True(Mean("alfa-rossa") > 1.1 * Mean("bugetti"));
    }

    [Fact]
    public void ASignedSponsorPaysWeeklyAndChangesTheCarUntilTheYearEnds()
    {
        var data = TestData.Load();
        var career = new Career(data, "alfa-rossa", 1932, seed: 3);
        var offer = career.SponsorOffers().Single(o => o.SponsorId == "benzolin");
        Assert.True(career.SignSponsor("benzolin"));
        // One deal per kind.
        Assert.DoesNotContain(career.SponsorOffers(), o => data.Sponsors[o.SponsorId].Kind == SupplyKind.Fuel);
        Assert.False(career.SignSponsor("shellux"));

        var supplied = career.Supplied(career.Car);
        Assert.True(supplied.Horsepower > career.Car.Horsepower);
        Assert.True(supplied.Thirst > career.Car.Thirst);

        career.AdvanceDays(14);
        Assert.Equal(2 * offer.WeeklyFee, career.Ledger.Where(t => t.Kind == MoneyKind.Sponsor).Sum(t => t.Amount));

        var loaded = Career.FromJson(data, career.ToJson());
        Assert.Equal(career.Deals[SupplyKind.Fuel], loaded.Deals[SupplyKind.Fuel]);

        career.StartNextSeason();
        Assert.Empty(career.Deals);
        Assert.Contains(career.SponsorOffers(), o => o.SponsorId == "benzolin");
    }

    [Fact]
    public void ARaceWinPaysTheBonusAndRaisesStanding()
    {
        var data = TestData.Load();
        var career = new Career(data, "alfa-rossa", 1932, seed: 3);
        foreach (var offer in career.SponsorOffers().GroupBy(o => data.Sponsors[o.SponsorId].Kind).Select(g => g.First()))
            career.SignSponsor(offer.SponsorId);
        var standing = career.Standing;
        var wins = 0;
        foreach (var ev in career.CurrentSeason.Events.Where(e => e.Date >= career.Today))
        {
            if (career.Enter(ev.Id) != EntryResult.Entered)
                continue;
            var race = career.PrepareRace();
            race.RunToEnd();
            var record = career.FinishRace(race);
            if (record.Winner.Entry.Team.Id == "alfa-rossa")
                wins++;
        }
        Assert.True(wins > 0);
        var bonuses = career.Ledger.Where(t => t.Kind == MoneyKind.Sponsor && t.Text.Contains("bonus")).ToList();
        Assert.Contains(bonuses, t => t.Text.Contains("1st"));
        Assert.True(career.Standing > standing);
    }

    [Fact]
    public void OrganisersAgreeRefuseOrTakeOffenceOnce()
    {
        var data = TestData.Load();
        var outcomes = new List<NegotiationResult>();
        for (var seed = 1; seed <= 30; seed++)
        {
            var career = new Career(data, "alfa-rossa", 1932, seed: seed);
            var ev = career.CurrentSeason.Events.First(e => e.StartMoney > 0);
            Assert.Equal(NegotiationResult.NotEntered, career.Negotiate(ev.Id, Ask.More));
            Assert.Equal(EntryResult.Entered, career.Enter(ev.Id));
            var offer = career.StartMoney(ev);
            var result = career.Negotiate(ev.Id, Ask.Lots);
            outcomes.Add(result);
            Assert.Equal(NegotiationResult.AlreadyAgreed, career.Negotiate(ev.Id, Ask.More));
            if (result == NegotiationResult.Agreed)
                Assert.True(career.StartMoney(ev) > 1.4 * offer);
            else
                Assert.True(career.StartMoney(ev) < offer);
            Assert.NotEqual(NegotiationResult.Refused, result);
            Assert.Equal(career.StartMoney(ev), Career.FromJson(data, career.ToJson()).StartMoney(ev));
        }
        Assert.Contains(NegotiationResult.Agreed, outcomes);
        Assert.Contains(NegotiationResult.Offended, outcomes);
    }

    [Fact]
    public void ABigAskIsRiskierAndAStarHelps()
    {
        Assert.True(Sponsorship.AgreeChance(Ask.Lots, 70, 10, false, 8) < Sponsorship.AgreeChance(Ask.More, 70, 10, false, 8));
        Assert.True(Sponsorship.AgreeChance(Ask.More, 70, 10, true, 8) > Sponsorship.AgreeChance(Ask.More, 70, 10, false, 8));
        Assert.True(Sponsorship.AgreeChance(Ask.More, 70, 10, false, 10) < Sponsorship.AgreeChance(Ask.More, 70, 10, false, 5));
    }
}
