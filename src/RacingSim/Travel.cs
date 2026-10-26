namespace RacingSim;

public sealed record TravelPlan(double DistanceKm, int Days, int Cost, bool CrossesAtlantic);

/// <summary>
/// Moves a team's cars and crew between places. Inside a continent the team goes by rail
/// and truck; between Europe and the USA it adds an ocean liner crossing.
/// </summary>
public static class Travel
{
    public const double RailKmPerDay = 500;
    public const int LoadingDays = 1;
    public const int AtlanticCrossingDays = 7;
    public const int AtlanticCrossingCost = 400;
    public const double CostPerKm = 0.15;

    /// <summary>Port used for every Atlantic crossing: Le Havre and New York.</summary>
    private static readonly (double Lat, double Lon) EuropePort = (49.49, 0.11);
    private static readonly (double Lat, double Lon) UsaPort = (40.71, -74.01);

    public static TravelPlan Plan(Region fromRegion, double fromLat, double fromLon, Region toRegion, double toLat, double toLon)
    {
        if (fromRegion == toRegion)
        {
            var km = DistanceKm(fromLat, fromLon, toLat, toLon);
            return new TravelPlan(km, LandDays(km), LandCost(km), false);
        }

        var (portA, portB) = fromRegion == Region.Europe ? (EuropePort, UsaPort) : (UsaPort, EuropePort);
        var toPort = DistanceKm(fromLat, fromLon, portA.Lat, portA.Lon);
        var fromPort = DistanceKm(portB.Lat, portB.Lon, toLat, toLon);
        var land = toPort + fromPort;
        return new TravelPlan(
            land + DistanceKm(portA.Lat, portA.Lon, portB.Lat, portB.Lon),
            LandDays(toPort) + AtlanticCrossingDays + LandDays(fromPort),
            LandCost(land) + AtlanticCrossingCost,
            true);
    }

    public static TravelPlan Plan(City from, Circuit to) =>
        Plan(from.Region, from.Latitude, from.Longitude, to.Region, to.Latitude, to.Longitude);

    public static TravelPlan Plan(Circuit from, Circuit to) =>
        Plan(from.Region, from.Latitude, from.Longitude, to.Region, to.Latitude, to.Longitude);

    private static int LandDays(double km) => LoadingDays + (int)Math.Ceiling(km / RailKmPerDay);

    private static int LandCost(double km) => (int)Math.Round(km * CostPerKm);

    /// <summary>Great-circle distance by the haversine formula.</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371;
        double rad(double deg) => deg * Math.PI / 180;
        var dLat = rad(lat2 - lat1);
        var dLon = rad(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(rad(lat1)) * Math.Cos(rad(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * earthRadiusKm * Math.Asin(Math.Sqrt(a));
    }
}
