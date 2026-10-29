using System;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// The Sponsors tab of the season screen: the team's standing, its contracts for the year with
/// fuel, oil, tyre and plug makers, and the offers for the kinds it has not signed yet.
/// </summary>
public partial class CalendarScreen
{
    private void SponsorsView(Career career)
    {
        _body.AddChild(new Label
        {
            Text = $"Standing {career.Standing}: the team's prestige ({career.Team.Prestige}) and its wins and podiums of the past year. "
                   + "Better results bring better offers. Contracts run to 31 December.",
            ThemeTypeVariation = "Note", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var offers = career.SponsorOffers();
        foreach (var kind in Enum.GetValues<SupplyKind>())
        {
            _body.AddChild(new Label { Text = KindName(kind).ToUpperInvariant(), ThemeTypeVariation = "Section" });
            if (career.Deals.TryGetValue(kind, out var deal))
            {
                _body.AddChild(SponsorCard(career, deal, Tag("Signed", UiTheme.GoodInk)));
                continue;
            }
            var ofKind = offers.Where(o => _data.Sponsors[o.SponsorId].Kind == kind).ToList();
            if (ofKind.Count == 0)
                _body.AddChild(new Label { Text = "No offers this year.", ThemeTypeVariation = "Note" });
            foreach (var offer in ofKind)
            {
                var sign = new Button
                {
                    Text = "Sign", ThemeTypeVariation = "Accent", SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(100, 0),
                    TooltipText = $"Race on {_data.Sponsors[offer.SponsorId].Name} {KindName(kind).ToLowerInvariant()} for the rest of {offer.Year}. The other {KindName(kind).ToLowerInvariant()} offers lapse.",
                };
                var id = offer.SponsorId;
                sign.Pressed += () =>
                {
                    Notice(career.SignSponsor(id) ? "" : "That offer is no longer open.");
                    Refresh();
                };
                _body.AddChild(SponsorCard(career, offer, sign));
            }
        }
    }

    private Control SponsorCard(Career career, SponsorDeal deal, Control action)
    {
        var sponsor = _data.Sponsors[deal.SponsorId];
        var card = new PanelContainer { ThemeTypeVariation = "CardTight" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(Badge($"£{deal.WeeklyFee}", "A WEEK", career.Deals.Values.Contains(deal)));
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 2);
        titles.AddChild(new Label { Text = sponsor.Name, ThemeTypeVariation = "Heading" });
        titles.AddChild(new Label
        {
            Text = $"{sponsor.Country} · £{deal.WinBonus} a win, £{deal.PodiumBonus} a podium, per car · {Capital(sponsor.EffectText)}",
            ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        titles.AddChild(new Label { Text = sponsor.Text, ThemeTypeVariation = "Muted", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        row.AddChild(titles);
        row.AddChild(action);
        card.AddChild(row);
        return card;
    }

    private static string Pct(double share) => $"{share * 100:0}%";

    private static string KindName(SupplyKind kind) => kind switch
    {
        SupplyKind.Fuel => "Fuel",
        SupplyKind.Oil => "Oil",
        SupplyKind.Tyres => "Tyres",
        _ => "Plugs",
    };

    /// <summary>Buttons to ask an entered race's organisers for more start money, or what they agreed.</summary>
    private Control? StartMoneyDeal(Career career, RaceEvent ev)
    {
        if (ev.StartMoney == 0 || career.ResultOf(ev) is not null || !career.Entries.Contains(ev))
            return null;
        if (career.Bargained(ev))
        {
            var factor = career.StartDeal(ev);
            return Chip(factor > 1 ? $"Bargained up {Pct(factor - 1)}" : factor < 1 ? "Organisers offended" : "Organisers held firm",
                factor > 1 ? UiTheme.GoodInk : factor < 1 ? UiTheme.Red : null);
        }
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 6);
        foreach (var ask in Enum.GetValues<Ask>())
        {
            var chance = career.AgreeChance(ev, ask);
            var raise = Sponsorship.Raise(ask) - 1;
            var button = new Button
            {
                Text = ask == Ask.More ? $"Ask for {Pct(raise)} more" : $"Demand {Pct(raise)} more",
                TooltipText = $"Ask the organisers for £{(int)Math.Round(career.StartMoney(ev) * Sponsorship.Raise(ask) / 10) * 10} a car. About {Pct(chance)} likely to agree; you can ask once."
                              + (ask == Ask.Lots ? $" Refused, they take offence and pay {Pct(1 - Sponsorship.OffendedFactor)} less." : " Refused, the offer stands.")
                              + " Your standing, a star driver and a good team manager help; the great races have entries to spare.",
            };
            button.Pressed += () =>
            {
                career.Negotiate(ev.Id, ask);
                Refresh();
            };
            line.AddChild(button);
        }
        return line;
    }
}
