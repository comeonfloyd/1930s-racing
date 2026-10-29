namespace RacingSim;

/// <summary>
/// Old cars the team keeps when a new one comes. Each driver races the team's newest car
/// unless given a kept car, which races as it was last developed: a reliable old car for one
/// driver while the new one has teething troubles, as the works often did.
/// </summary>
public sealed partial class Career
{
    /// <summary>Most old cars the works keeps; when another comes, the oldest is sold.</summary>
    public const int MaxSpareCars = 2;

    private readonly List<Car> _spares = new();
    private readonly Dictionary<string, string> _carOf = new();

    /// <summary>The old cars in the works, oldest first.</summary>
    public IReadOnlyList<Car> SpareCars => _spares;

    /// <summary>The kept car a driver races, or null for the team's newest car.</summary>
    public Car? CarOfDriver(string driverId) =>
        _carOf.TryGetValue(driverId, out var name) ? _spares.FirstOrDefault(c => c.Name == name) : null;

    /// <summary>What a kept car fetches: half a customer car's price.</summary>
    public static int SpareValue(Car car) => (int)(Math.Round(OwnTeam.CustomerCarCost(car) / 2 / 50.0) * 50);

    /// <summary>Gives a driver a kept car, or the newest car with null.</summary>
    public bool AssignCar(string driverId, string? carName)
    {
        if (!_roster.Contains(driverId) || carName is not null && _spares.All(c => c.Name != carName))
            return false;
        if (carName is null)
            _carOf.Remove(driverId);
        else
            _carOf[driverId] = carName;
        return true;
    }

    /// <summary>Sells a kept car; anyone racing it goes back to the newest car.</summary>
    public bool SellSpare(string carName, DateOnly? date = null)
    {
        var car = _spares.FirstOrDefault(c => c.Name == carName);
        if (car is null)
            return false;
        _spares.Remove(car);
        foreach (var driver in _carOf.Where(kv => kv.Value == carName).Select(kv => kv.Key).ToList())
            _carOf.Remove(driver);
        Pay(MoneyKind.Development, $"Sold the old {car.Name}", SpareValue(car), date);
        return true;
    }

    /// <summary>The car being replaced goes into the works; past the limit the oldest is sold.</summary>
    private void KeepOldCar(Car old, DateOnly date)
    {
        if (_spares.Any(c => c.Name == old.Name))
            return;
        _spares.Add(old);
        if (_spares.Count > MaxSpareCars)
        {
            var oldest = _spares[0];
            SellSpare(oldest.Name, date);
            _news.Add(new NewsItem(date, $"The works has no room for the old {oldest.Name}: sold for £{SpareValue(oldest):N0}."));
        }
    }

    /// <summary>The car a driver races at an event: a kept car given to them when it meets the rules, else the team's.</summary>
    private Car CarFor(string driverId, RaceEvent ev, Car teamCar)
    {
        var circuit = _data.Circuits[ev.CircuitId];
        return !Indianapolis.Is(circuit) && CarOfDriver(driverId) is { } spare && Formula.Breach(spare, circuit, ev.Date.Year) is null
            ? Supplied(Works.Prepared(spare, Level(Facility.Workshop)))
            : teamCar;
    }
}
