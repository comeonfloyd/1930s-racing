namespace RacingSim;

public enum EntryResult
{
    Entered,
    AlreadyEntered,
    EventPassed,
    CannotArriveInTime,
    UnknownEvent,
}

/// <summary>
/// The player's running career: which team they run, today's date, and which events they
/// have entered. Entries are planned in date order from the team's base, so the cars can
/// only be where the previous entry left them.
/// </summary>
public sealed class Career
{
    private readonly GameData _data;
    private readonly SortedSet<RaceEvent> _entries = new(Comparer<RaceEvent>.Create((a, b) =>
        a.Date != b.Date ? a.Date.CompareTo(b.Date) : string.CompareOrdinal(a.Id, b.Id)));

    public Team Team { get; }
    public DateOnly Today { get; private set; }
    public int Cash { get; private set; }
    public IReadOnlyCollection<RaceEvent> Entries => _entries;

    public Career(GameData data, string teamId, int startYear)
    {
        _data = data;
        Team = data.Teams[teamId];
        if (!Team.Playable)
            throw new ArgumentException($"Team {teamId} is not playable", nameof(teamId));
        Today = new DateOnly(startYear, 1, 1);
        Cash = Team.StartingCash;
    }

    public Season CurrentSeason => _data.Seasons[Today.Year];

    /// <summary>Travel needed to reach an event from wherever the previous entry leaves the cars.</summary>
    public TravelPlan TravelTo(RaceEvent ev)
    {
        var target = _data.Circuits[ev.CircuitId];
        var previous = _entries.LastOrDefault(e => e.Date < ev.Date);
        return previous is null
            ? Travel.Plan(_data.Cities[Team.BaseCityId], target)
            : Travel.Plan(_data.Circuits[previous.CircuitId], target);
    }

    /// <summary>Earliest day the cars can leave for an event: today, or the day after the previous race.</summary>
    private DateOnly DepartureFor(RaceEvent ev)
    {
        var previous = _entries.LastOrDefault(e => e.Date < ev.Date);
        return previous is null || previous.Date < Today ? Today : previous.Date.AddDays(1);
    }

    public EntryResult Enter(string eventId)
    {
        var ev = CurrentSeason.Events.FirstOrDefault(e => e.Id == eventId);
        if (ev is null)
            return EntryResult.UnknownEvent;
        if (ev.Date < Today)
            return EntryResult.EventPassed;
        if (_entries.Contains(ev))
            return EntryResult.AlreadyEntered;

        var arrival = DepartureFor(ev).AddDays(TravelTo(ev).Days);
        if (arrival > ev.Date)
            return EntryResult.CannotArriveInTime;

        // The next entry must still be reachable from this event's circuit.
        var next = _entries.FirstOrDefault(e => e.Date > ev.Date);
        if (next is not null)
        {
            var onward = Travel.Plan(_data.Circuits[ev.CircuitId], _data.Circuits[next.CircuitId]);
            if (ev.Date.AddDays(1 + onward.Days) > next.Date)
                return EntryResult.CannotArriveInTime;
        }

        _entries.Add(ev);
        return EntryResult.Entered;
    }

    public void AdvanceDays(int days)
    {
        if (days < 0)
            throw new ArgumentOutOfRangeException(nameof(days));
        Today = Today.AddDays(days);
    }
}
