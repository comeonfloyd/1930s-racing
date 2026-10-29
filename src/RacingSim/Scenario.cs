namespace RacingSim;

/// <summary>A team a scenario lets the player run, with its budget for that start (null for the team's own).</summary>
public sealed record ScenarioTeam(string TeamId, int? Cash = null);

/// <summary>Where a new career can begin, from scenarios.json: a year and the teams on offer.</summary>
public sealed record Scenario(string Id, string Name, int Year, IReadOnlyList<ScenarioTeam> Teams, string Text = "")
{
    /// <summary>The full career, used when the data has no scenarios: the playable teams in 1930.</summary>
    public static Scenario Default(GameData data) =>
        new("1930", "The full decade", 1930, data.Teams.Values.Where(t => t.Playable).Select(t => new ScenarioTeam(t.Id)).ToList());
}
