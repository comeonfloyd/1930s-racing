namespace RacingSim;

/// <summary>
/// The Grand Prix rules of each year, for European races (the Indianapolis 500 ran to its own
/// rules). Until 1933 any car could race; from 1934 to 1937 a car could weigh at most 750 kg;
/// from 1938 its engine could be at most 3 litres. Simplified from the real rules, which also
/// set body widths and an unsupercharged 4.5-litre class.
/// </summary>
public static class Formula
{
    public const int FirstWeightYear = 1934;
    public const int MaxWeightKg = 750;
    public const int FirstLitreYear = 1938;
    public const double MaxLitres = 3.0;

    public static string Name(int year) => year switch
    {
        < FirstWeightYear => "Formula libre",
        < FirstLitreYear => "750 kg formula",
        _ => "3-litre formula",
    };

    /// <summary>The rule in a line, for the season screen.</summary>
    public static string Rule(int year) => year switch
    {
        < FirstWeightYear => "any car may race",
        < FirstLitreYear => $"cars may weigh at most {MaxWeightKg} kg",
        _ => $"engines of at most {MaxLitres:0.0} litres",
    };

    public static bool Applies(Circuit circuit) => circuit.Region == Region.Europe;

    /// <summary>Why a car may not race at a circuit in a year, or null if it may.</summary>
    public static string? Breach(Car car, Circuit circuit, int year) => Applies(circuit) ? Breach(car, year) : null;

    /// <summary>Why a car may not race in Europe in a year, or null if it may.</summary>
    public static string? Breach(Car car, int year)
    {
        if (year >= FirstLitreYear && car.EngineLitres > MaxLitres)
            return $"The {car.Name} has a {car.EngineLitres:0.0#}-litre engine; the {Name(year)} allows {MaxLitres:0.0}.";
        if (year is >= FirstWeightYear and < FirstLitreYear && car.WeightKg > MaxWeightKg)
            return $"The {car.Name} weighs {car.WeightKg} kg; the {Name(year)} allows {MaxWeightKg} kg.";
        return null;
    }
}
