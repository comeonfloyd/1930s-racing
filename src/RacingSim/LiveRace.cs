namespace RacingSim;

/// <summary>How hard a driver is told to go. The pit board shows it once a lap.</summary>
public enum PaceOrder
{
    /// <summary>Hold back to save the car: slower, much kinder on tyres and engine.</summary>
    Nurse,
    /// <summary>Keep the place: a little slower than racing, a little safer.</summary>
    Hold,
    Race,
    /// <summary>Flat out: faster, but wears tyres and engine and raises the crash risk.</summary>
    Push,
}

/// <summary>Something worth a line in the race commentary.</summary>
public sealed record RaceMessage(double Time, int Lap, string DriverId, string Text);

/// <summary>Where one car is at a moment of the race, with the state the pit wall can see.</summary>
/// <param name="Distance">Laps covered so far, e.g. 3.25 is a quarter of the way round lap 4.</param>
/// <param name="FinishedAt">Race time in seconds when the car took the flag, or 0 before then.</param>
public sealed record LiveCar(
    RaceEntry Entry, double Distance, bool Running, Retirement? Retirement, bool Finished,
    bool InPits, double Fuel, double TyreWear, PaceOrder Pace, bool PitCalled, int PitStops, double FinishedAt = 0);

/// <summary>
/// A race that runs as time goes on, so the player can give orders while it is on: call a car in
/// for fuel and tyres, or change its pace. Each car works out a lap when it starts that lap, from
/// its fuel load, tyre wear and orders at that moment, so an order takes effect next time the car
/// passes the pits.
///
/// Each car rolls its own seeded dice, so the race comes out the same whatever steps time moves in,
/// and the same orders always give the same race. All tuning numbers are first-pass placeholders.
/// </summary>
public sealed class LiveRace
{
    /// <summary>Distance a full tank lasts at racing pace.</summary>
    public const double TankRangeKm = 280;

    private readonly CarState[] _cars;
    private readonly List<RaceMessage> _messages = new();
    private readonly RaceSettings _settings;
    private readonly HashSet<string> _manualTeams;

    public RaceEvent Event { get; }
    public Circuit Circuit { get; }
    public RaceWeather Weather { get; }

    /// <summary>Rain starts and stops told so far, as race times.</summary>
    private readonly HashSet<double> _weatherTold = new();

    /// <summary>About how long a race runs, for timing showers: laps at 130 km/h.</summary>
    public static double ExpectedSeconds(RaceEvent ev, Circuit circuit) => ev.Laps * circuit.LapKm / 130 * 3600;
    public double Time { get; private set; }
    public IReadOnlyList<RaceMessage> Messages => _messages;

    /// <param name="manualTeams">Teams whose pit calls the player makes. Every other car follows the AI strategy.</param>
    public LiveRace(RaceEvent ev, Circuit circuit, IReadOnlyList<RaceEntry> grid, int seed,
        RaceSettings? settings = null, IEnumerable<string>? manualTeams = null)
    {
        Event = ev;
        Circuit = circuit;
        _settings = settings ?? new RaceSettings();
        _manualTeams = new HashSet<string>(manualTeams ?? []);
        Weather = RaceWeather.Roll(circuit, seed, ExpectedSeconds(ev, circuit));
        var dayForm = new Random(Seeds.Mix(seed, -1));
        _cars = grid.Select((entry, i) => new CarState(entry, new Random(Seeds.Mix(seed, i)),
            // Each car's form on the day: up to about 2% either way, so the fastest car does not always win.
            1 + Gaussian(dayForm) * 0.01)).ToArray();
        foreach (var car in _cars)
            StartLap(car, 0);
    }

    public bool IsOver => _cars.All(c => c.Done);

    /// <summary>Race time in seconds when the last running car takes the flag, once the race is over.</summary>
    public double FinishTime => _cars.Select(c => c.Retirement is null ? c.LapEnd : c.StopTime).DefaultIfEmpty(0).Max();

    public void SetPace(string driverId, PaceOrder pace) => Car(driverId).Pace = pace;

    /// <summary>Calls the car in at the end of its current lap; calling again cancels.</summary>
    public void CallPitStop(string driverId, bool call = true) => Car(driverId).PitCalled = call;

    /// <summary>Lets the AI pit wall make a manual team's pit calls from now on.</summary>
    public void HandOverPitWall(string teamId) => _manualTeams.Remove(teamId);

    /// <summary>Race time in seconds when the winner took the flag, or null before then.</summary>
    public double? FlagTime { get; private set; }

    /// <summary>
    /// Runs the race on to a race time in seconds. Time never goes back. Laps are counted in the
    /// order they end, so once the winner takes the flag every other car is flagged off the next
    /// time it crosses the line, lapped or not.
    /// </summary>
    public void AdvanceTo(double time)
    {
        Time = Math.Max(Time, time);
        while (_cars.Where(c => !c.Done && c.Retirement is null && c.LapEnd <= Time).MinBy(c => c.LapEnd) is { } car)
        {
            car.LapTimes.Add(car.LapEnd - car.LapStart);
            if (car.LapTimes.Count == Event.Laps)
                FlagTime ??= car.LapEnd;
            if (FlagTime is not null)
                car.Done = true;
            else
                StartLap(car, car.LapEnd);
        }
        foreach (var car in _cars)
            if (car.Retirement is not null && car.StopTime <= Time)
                car.Done = true;
        TellWeather();
    }

    /// <summary>Adds a race message when rain starts or stops, on the leader's lap at that moment.</summary>
    private void TellWeather()
    {
        foreach (var shower in Weather.Showers)
        {
            if (shower.Start <= Time && _weatherTold.Add(shower.Start))
                _messages.Add(new RaceMessage(shower.Start, LeaderLapAt(shower.Start), "",
                    shower.Start == 0 ? "The race starts in the rain." : "Rain begins to fall: the road is wet."));
            if (shower.End <= Time && _weatherTold.Add(shower.End))
                _messages.Add(new RaceMessage(shower.End, LeaderLapAt(shower.End), "", "The rain stops and the road dries."));
        }
    }

    private int LeaderLapAt(double time) => Math.Min(Event.Laps, _cars.Max(c => CumulativeSum(c.LapTimes).Count(t => t <= time)) + 1);

    public void RunToEnd() => AdvanceTo(double.MaxValue / 4);

    /// <summary>Every car now, leader first.</summary>
    public IReadOnlyList<LiveCar> Cars()
    {
        var list = new List<LiveCar>();
        foreach (var car in _cars)
        {
            var finished = car.Retirement is null && car.Done;
            var stopped = car.Retirement is not null && Time >= car.StopTime;
            var driving = car.LapStart + car.PitTime;
            var inPits = !car.Done && !stopped && Time < driving;
            // A finished car has its laps: the winner all of them, a lapped car fewer.
            double distance = car.LapTimes.Count;
            if (stopped)
                distance += car.StopFraction;
            else if (car.Retirement is not null)
                distance += Math.Clamp((Time - driving) / (car.StopTime - driving), 0, 1) * car.StopFraction;
            else if (!finished && !inPits)
                distance += Math.Clamp((Time - driving) / (car.LapEnd - driving), 0, 0.999);
            list.Add(new LiveCar(car.Entry, distance, !finished && !stopped, stopped ? car.Retirement : null, finished,
                inPits, car.Fuel, car.Tyres, car.Pace, car.PitCalled, car.PitStops, finished ? car.LapEnd : 0));
        }
        // Finished cars by finishing time, then everyone else by distance covered.
        return list
            .Select((c, i) => (c, finishedAt: c.Finished ? _cars[i].LapEnd : double.MaxValue))
            .OrderByDescending(x => x.c.Distance)
            .ThenBy(x => x.finishedAt)
            .Select(x => x.c)
            .ToList();
    }

    /// <summary>The classification and lap chart, once the race is over.</summary>
    public RaceResult Result()
    {
        if (!IsOver)
            throw new InvalidOperationException("The race is still running.");
        var order = Enumerable.Range(0, _cars.Length)
            .OrderByDescending(i => _cars[i].LapTimes.Count)
            .ThenBy(i => _cars[i].LapTimes.Sum())
            .ThenBy(i => i)
            .ToList();
        var classification = order
            .Select(i => new CarResult(_cars[i].Entry, _cars[i].LapTimes.ToList(), _cars[i].Retirement))
            .ToList();

        var positions = new List<IReadOnlyList<string>>();
        var reached = _cars.Select(c => CumulativeSum(c.LapTimes)).ToArray();
        for (var lap = 1; lap <= Event.Laps; lap++)
        {
            var l = lap;
            positions.Add(Enumerable.Range(0, _cars.Length)
                .OrderByDescending(i => Math.Min(_cars[i].LapTimes.Count, l))
                .ThenBy(i => reached[i].Length == 0 ? 0 : reached[i][Math.Min(reached[i].Length, l) - 1])
                .ThenBy(i => i)
                .Select(i => _cars[i].Entry.Driver.Id)
                .ToList());
        }
        return new RaceResult(Event, Circuit, classification) { Positions = positions };
    }

    private CarState Car(string driverId) => _cars.First(c => c.Entry.Driver.Id == driverId);

    private void StartLap(CarState car, double start)
    {
        var lap = car.LapTimes.Count + 1;
        var rng = car.Rng;
        car.LapStart = start;
        car.PitTime = 0;

        // Into the pits at the line, if called, or if the AI pit wall wants it.
        var lapsLeft = Event.Laps - car.LapTimes.Count;
        var manual = _manualTeams.Contains(car.Entry.Team.Id);
        if (lap > 1 && (car.PitCalled || car.TyreBurst || (!manual && WantsStop(car, lapsLeft))))
            PitStop(car, lapsLeft, start, lap);

        var fuelForLap = FuelPerLap(car);
        if (car.Fuel < fuelForLap)
        {
            // Ran dry out on the circuit.
            Retire(car, start, lap, RetirementCause.OutOfFuel, 0.5 + rng.NextDouble() * 0.4, "runs out of fuel");
            return;
        }

        var (speed, wear, strain, risk) = PaceEffect(car.Pace);
        var wet = Weather.IsWetAt(start);
        if (wet)
        {
            risk *= RaceWeather.WetCrashFactor(car.Entry.Driver);
            wear *= RaceWeather.WetTyreWear;
        }
        if (rng.NextDouble() < FailureChancePerLap(car.Entry, Circuit) * strain)
        {
            Retire(car, start, lap, RetirementCause.MechanicalFailure, 0.5, "retires with a mechanical failure");
            return;
        }
        var grip = 1 + 2 * car.Tyres * car.Tyres;
        if (rng.NextDouble() < CrashChancePerLap(car.Entry.Driver, Circuit) * risk * grip)
        {
            var fatal = _settings.FatalCrashes && rng.NextDouble() < Circuit.Danger * 0.15;
            // A driver who survives may still be hurt: more often on a dangerous circuit.
            var injuryWeeks = !fatal && rng.NextDouble() < 0.3 + Circuit.Danger * 0.3 ? 2 + rng.Next(9) : 0;
            Retire(car, start, lap, fatal ? RetirementCause.FatalCrash : RetirementCause.Crash, 0.5,
                fatal ? "crashes fatally" : injuryWeeks > 0 ? "crashes out and is injured" : "crashes out", injuryWeeks);
            return;
        }

        var ideal = Circuit.LapKm / RaceSim.AverageSpeedKmh(car.Entry, Circuit) * 3600;
        // Less consistent drivers spread their laps wider.
        var spread = (21 - car.Entry.Driver.Skills.Consistency) * 0.005;
        var time = ideal * (1 + Math.Abs(Gaussian(rng)) * spread) * car.Form / speed;
        time *= 1 + 0.01 * car.Fuel;                  // a full tank is heavy
        time *= 1 + 0.05 * car.Tyres * car.Tyres;     // worn rear tyres lose grip
        if (car.TyreBurst)
            time *= 1.4;                              // limping back on a burst tyre
        if (wet)
            time *= RaceWeather.WetLapFactor(car.Entry.Driver);

        car.Fuel -= fuelForLap;
        car.Tyres += TyreWearPerLap(car) * wear;
        car.TyreBurst = false;
        if (car.Tyres >= 1)
        {
            car.TyreBurst = true;
            car.Tyres = 1;
            Say(car, start, lap, "has a tyre failure");
        }
        car.LapEnd = start + car.PitTime + time;
    }

    private void PitStop(CarState car, int lapsLeft, double start, int lap)
    {
        var rng = car.Rng;
        // Fuel for the rest of the race, or a full tank.
        var needed = Math.Min(1, lapsLeft * FuelPerLap(car) * 1.05);
        var fuelAdded = Math.Max(0, needed - car.Fuel);
        var seconds = 20 + 30 * fuelAdded + 15;  // stop and go, fuel churns, rear wheels
        var fumble = rng.NextDouble() < 0.1;
        if (fumble)
            seconds += 10 + rng.NextDouble() * 20;
        car.Fuel += fuelAdded;
        car.Tyres = 0;
        car.TyreBurst = false;
        car.PitCalled = false;
        car.PitStops++;
        car.PitTime = seconds;
        Say(car, start, lap, $"pits for fuel and tyres, {seconds:0} s{(fumble ? " after a fumbled wheel change" : "")}");
    }

    /// <summary>The AI pit wall: stop when the next lap would run the tank dry or the tyres are nearly gone.</summary>
    private bool WantsStop(CarState car, int lapsLeft)
    {
        if (lapsLeft <= 0)
            return false;
        var fuelShort = car.Fuel < FuelPerLap(car) * 1.5 && car.Fuel < lapsLeft * FuelPerLap(car);
        var tyresGone = car.Tyres > 0.85 && lapsLeft * TyreWearPerLap(car) > 1 - car.Tyres;
        return fuelShort || tyresGone;
    }

    private void Retire(CarState car, double start, int lap, RetirementCause cause, double fraction, string text, int injuryWeeks = 0)
    {
        var ideal = Circuit.LapKm / RaceSim.AverageSpeedKmh(car.Entry, Circuit) * 3600;
        car.Retirement = new Retirement(lap, cause, injuryWeeks);
        car.StopFraction = fraction;
        car.StopTime = start + car.PitTime + ideal * fraction;
        Say(car, car.StopTime, lap, text);
    }

    private void Say(CarState car, double time, int lap, string text) =>
        _messages.Add(new RaceMessage(time, lap, car.Entry.Driver.Id, $"{car.Entry.Driver.Name} {text}"));

    private double FuelPerLap(CarState car) =>
        Circuit.LapKm / TankRangeKm * (car.Pace == PaceOrder.Push ? 1.1 : car.Pace == PaceOrder.Nurse ? 0.9 : 1);

    /// <summary>Rear tyres last about 250 km at racing pace in a 150 hp car; twisty circuits wear them faster.</summary>
    private double TyreWearPerLap(CarState car) =>
        Circuit.LapKm * (0.8 + car.Entry.Team.Car.Horsepower / 400.0) / 300 * (1.15 - 0.3 * Circuit.StraightShare);

    /// <summary>(speed, tyre wear, engine strain, crash risk) multipliers for a pace order.</summary>
    private static (double Speed, double Wear, double Strain, double Risk) PaceEffect(PaceOrder pace) => pace switch
    {
        PaceOrder.Push => (1.015, 1.4, 1.6, 1.5),
        PaceOrder.Hold => (0.99, 0.85, 0.8, 0.8),
        PaceOrder.Nurse => (0.97, 0.7, 0.5, 0.6),
        _ => (1, 1, 1, 1),
    };

    internal static double FailureChancePerLap(RaceEntry entry, Circuit circuit)
    {
        // Reliability is the chance to survive 500 km; spread that risk over the laps.
        var perKm = -Math.Log(entry.Team.Car.Reliability) / 500;
        var sympathy = 1.3 - entry.Driver.Skills.MechanicalSympathy / 40.0;
        return 1 - Math.Exp(-perKm * circuit.LapKm * sympathy);
    }

    internal static double CrashChancePerLap(Driver driver, Circuit circuit) =>
        circuit.Danger * 0.0015 * (1 + (driver.Skills.Bravery - 10) / 20.0) * (21 - driver.Skills.Consistency) / 10.0 * circuit.LapKm / 5;

    internal static double Gaussian(Random rng)
    {
        var u1 = 1 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    private static double[] CumulativeSum(IReadOnlyList<double> values)
    {
        var sums = new double[values.Count];
        double running = 0;
        for (var i = 0; i < values.Count; i++)
            sums[i] = running += values[i];
        return sums;
    }

    private sealed class CarState(RaceEntry entry, Random rng, double form)
    {
        public RaceEntry Entry { get; } = entry;
        public Random Rng { get; } = rng;
        public double Form { get; } = form;
        public List<double> LapTimes { get; } = new();
        public double LapStart;
        public double LapEnd;
        /// <summary>Seconds standing in the pits at the start of the current lap.</summary>
        public double PitTime;
        public double StopTime;
        /// <summary>How far round its last lap a retiring car gets.</summary>
        public double StopFraction;
        public double Fuel = 1;
        public double Tyres;
        public bool TyreBurst;
        public PaceOrder Pace = PaceOrder.Race;
        public bool PitCalled;
        public int PitStops;
        public Retirement? Retirement;
        public bool Done;
    }
}
