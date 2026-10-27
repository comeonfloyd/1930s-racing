namespace RacingSim;

/// <summary>A spell of rain, in race time: from the start to the end, in seconds.</summary>
public sealed record Shower(double Start, double End);

/// <summary>
/// The weather of one race day, rolled from the race's seed so a race run in the background
/// gets the same weather as one the player watches. Rain makes laps slower and crashes more
/// likely, and good wet-weather drivers lose less than others. Numbers are first-pass
/// placeholders.
/// </summary>
public sealed record RaceWeather(IReadOnlyList<Shower> Showers)
{
    public static readonly RaceWeather Dry = new([]);

    public bool IsWetAt(double time) => Showers.Any(s => time >= s.Start && time < s.End);

    /// <summary>True when it rains at some time of the race.</summary>
    public bool AnyRain => Showers.Count > 0;

    /// <summary>
    /// Rolls the day's weather. <paramref name="raceSeconds"/> is about how long the race will
    /// last: rain all race long, or a shower somewhere in it.
    /// </summary>
    public static RaceWeather Roll(Circuit circuit, int seed, double raceSeconds)
    {
        var rng = new Random(Seeds.Mix(seed, 7349));
        if (rng.NextDouble() >= circuit.RainChance)
            return Dry;
        if (rng.NextDouble() < 0.4)
            return new([new Shower(0, double.MaxValue)]);
        var start = rng.NextDouble() * 0.8 * raceSeconds;
        var length = (0.15 + rng.NextDouble() * 0.25) * raceSeconds;
        return new([new Shower(start, start + length)]);
    }

    /// <summary>
    /// What the forecast says a few days before: right three times in four, so a dry forecast
    /// can still bring rain.
    /// </summary>
    public static string Forecast(Circuit circuit, int seed, double raceSeconds)
    {
        var rain = Roll(circuit, seed, raceSeconds).AnyRain;
        var right = new Random(Seeds.Mix(seed, 104729)).NextDouble() < 0.75;
        return rain == right ? "Rain likely" : "Dry";
    }

    /// <summary>How much slower a lap in the rain is: 6% for the best wet driver, about 17% for the worst.</summary>
    public static double WetLapFactor(Driver driver) => 1.12 - (driver.Skills.Wet - 10) * 0.006;

    /// <summary>How much more likely a crash is in the rain: two to three times, less for a good wet driver.</summary>
    public static double WetCrashFactor(Driver driver) => 2.5 - (driver.Skills.Wet - 10) * 0.05;

    /// <summary>Rear tyres wear less on a wet road.</summary>
    public const double WetTyreWear = 0.6;
}
