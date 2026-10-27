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
/// <param name="AtWheel">The driver in the car now; for a shared car, either of the two.</param>
/// <param name="StintHours">How long the driver at the wheel has been in the car.</param>
/// <param name="SwapAtStop">For a shared car: the drivers change at the next stop.</param>
/// <param name="FuelAllowed">False while the endurance rules forbid refuelling at the next stop.</param>
public sealed record LiveCar(
    RaceEntry Entry, double Distance, bool Running, Retirement? Retirement, bool Finished,
    bool InPits, double Fuel, double TyreWear, PaceOrder Pace, bool PitCalled, int PitStops, double FinishedAt = 0,
    Driver? AtWheel = null, double StintHours = 0, bool SwapAtStop = true, bool FuelAllowed = true);

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

    /// <summary>A sports car's bigger tank, which the refuelling rule below needs.</summary>
    public const double EnduranceTankRangeKm = 400;

    /// <summary>The Le Mans rule of the 1930s: no fresh fuel, oil or water until this many laps after the last.</summary>
    public const int MinLapsBetweenRefuels = 24;

    /// <summary>An endurance race starts at four in the afternoon.</summary>
    public const double EnduranceStartHour = 16;

    /// <summary>Darkness in mid-June, as hours of the clock: from half past nine to half past four.</summary>
    public const double DuskHour = 21.5, DawnHour = 4.5;

    private readonly CarState[] _cars;
    private readonly List<RaceMessage> _messages = new();
    private readonly RaceSettings _settings;
    private readonly HashSet<string> _manualTeams;

    /// <summary>Each team's chief mechanic's skill; a team not listed has an average crew.</summary>
    private readonly IReadOnlyDictionary<string, int> _pitCrews;
    private const int AverageCrew = 10;

    public RaceEvent Event { get; }
    public Circuit Circuit { get; }
    public RaceWeather Weather { get; }

    /// <summary>Rain starts and stops told so far, as race times.</summary>
    private readonly HashSet<double> _weatherTold = new();

    /// <summary>About how long a race runs, for timing showers: laps at 130 km/h.</summary>
    public static double ExpectedSeconds(RaceEvent ev, Circuit circuit) =>
        ev.IsEndurance ? ev.Hours * 3600 : ev.Laps * circuit.LapKm / 130 * 3600;

    /// <summary>For an endurance race, when the time runs out; the race ends as cars next cross the line.</summary>
    public double? TimeLimit => Event.IsEndurance ? Event.Hours * 3600 : null;

    /// <summary>The hour of the day (0 to 24) at a race time, for an endurance race.</summary>
    public static double HourOfDay(double time) => (EnduranceStartHour + time / 3600) % 24;

    /// <summary>True in the dark hours of an endurance race. Other races are run by day.</summary>
    public bool IsNightAt(double time)
    {
        if (!Event.IsEndurance)
            return false;
        var hour = HourOfDay(time);
        return hour >= DuskHour || hour < DawnHour;
    }

    private double TankKm => Event.IsEndurance ? EnduranceTankRangeKm : TankRangeKm;
    public double Time { get; private set; }
    public IReadOnlyList<RaceMessage> Messages => _messages;

    /// <param name="manualTeams">Teams whose pit calls the player makes. Every other car follows the AI strategy.</param>
    public LiveRace(RaceEvent ev, Circuit circuit, IReadOnlyList<RaceEntry> grid, int seed,
        RaceSettings? settings = null, IEnumerable<string>? manualTeams = null, IReadOnlyDictionary<string, int>? pitCrews = null)
    {
        Event = ev;
        Circuit = circuit;
        _settings = settings ?? new RaceSettings();
        _manualTeams = new HashSet<string>(manualTeams ?? []);
        _pitCrews = pitCrews ?? new Dictionary<string, int>();
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

    /// <summary>For a shared car: whether the drivers change at the next stop.</summary>
    public void SetSwapAtStop(string driverId, bool swap) => Car(driverId).SwapAtStop = swap;

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
            car.LapsSinceFuel++;
            if (car.LapTimes.Count == Event.Laps)
                FlagTime ??= car.LapEnd;
            // An endurance race ends on the clock: each car is flagged as it next crosses the line.
            if (TimeLimit is { } limit && car.LapEnd >= limit)
                FlagTime ??= limit;
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
                inPits, car.Fuel, car.Tyres, car.Pace, car.PitCalled, car.PitStops, finished ? car.LapEnd : 0,
                car.AtWheel, Math.Max(0, Time - car.StintStart) / 3600, car.SwapAtStop, FuelAllowed(car)));
        }
        // Finished cars by finishing time, then everyone else by distance covered.
        return list
            .Select((c, i) => (c, finishedAt: c.Finished ? _cars[i].LapEnd : double.MaxValue))
            .OrderBy(x => Unclassified(x.c.Retirement) ? 1 : 0)
            .ThenByDescending(x => x.c.Distance)
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
            .OrderBy(i => Unclassified(_cars[i].Retirement) ? 1 : 0)
            .ThenByDescending(i => _cars[i].LapTimes.Count)
            .ThenBy(i => _cars[i].LapTimes.Sum())
            .ThenBy(i => i)
            .ToList();
        var classification = order
            .Select(i => new CarResult(_cars[i].Entry, _cars[i].LapTimes.ToList(), _cars[i].Retirement))
            .ToList();

        var positions = new List<IReadOnlyList<string>>();
        var reached = _cars.Select(c => CumulativeSum(c.LapTimes)).ToArray();
        var mostLaps = _cars.Max(c => c.LapTimes.Count);
        for (var lap = 1; lap <= mostLaps; lap++)
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

    /// <summary>
    /// In a timed race only cars running at the end are classified, so a retired car ranks
    /// behind every finisher however far it got. In a race over a distance the laps decide.
    /// </summary>
    private bool Unclassified(Retirement? retirement) => Event.IsEndurance && retirement is not null;

    private CarState Car(string driverId) => _cars.First(c => c.Entry.Driver.Id == driverId);

    private void StartLap(CarState car, double start)
    {
        var lap = car.LapTimes.Count + 1;
        var rng = car.Rng;
        car.LapStart = start;
        car.PitTime = 0;

        // Into the pits at the line, if called, or if the AI pit wall wants it. A driver left out
        // by the player's pit wall still comes in himself before the tank runs dry.
        var lapsLeft = LapsLeft(car, start);
        var manual = _manualTeams.Contains(car.Entry.Team.Id);
        var stops = manual ? FuelShort(car, lapsLeft) : WantsStop(car, lapsLeft, start);
        if (lap > 1 && (car.PitCalled || car.TyreBurst || stops))
            PitStop(car, lapsLeft, start, lap);

        var fuelForLap = FuelPerLap(car);
        if (car.Fuel < fuelForLap)
        {
            // Ran dry out on the circuit.
            Retire(car, start, lap, RetirementCause.OutOfFuel, 0.5 + rng.NextDouble() * 0.4, "runs out of fuel");
            return;
        }

        var driver = car.AtWheel;
        var entry = car.Entry with { Driver = driver };
        var (speed, wear, strain, risk) = PaceEffect(car.Pace);
        var wet = Weather.IsWetAt(start);
        if (wet)
        {
            risk *= RaceWeather.WetCrashFactor(driver);
            wear *= RaceWeather.WetTyreWear;
        }
        var night = IsNightAt(start);
        if (night)
            risk *= NightCrashFactor;
        var tired = Fatigue(car, start);
        risk *= 1 + tired * 6;
        if (Event.IsEndurance)
        {
            // A 24-hour race is driven to last: sports cars built for it, drivers sparing them.
            strain *= EnduranceStrain;
            risk *= EnduranceRisk;
            wear *= EnduranceWear;
        }
        if (rng.NextDouble() < FailureChancePerLap(entry, Circuit) * strain * _settings.FailureRate && !PastTheClock(car, start))
        {
            Retire(car, start, lap, RetirementCause.MechanicalFailure, 0.5, "retires with a mechanical failure");
            return;
        }
        var grip = 1 + 2 * car.Tyres * car.Tyres;
        if (rng.NextDouble() < CrashChancePerLap(driver, Circuit) * risk * grip && !PastTheClock(car, start))
        {
            var fatal = _settings.FatalCrashes && rng.NextDouble() < Circuit.Danger * 0.15;
            // A driver who survives may still be hurt: more often on a dangerous circuit.
            var injuryWeeks = !fatal && rng.NextDouble() < 0.3 + Circuit.Danger * 0.3 ? 2 + rng.Next(9) : 0;
            Retire(car, start, lap, fatal ? RetirementCause.FatalCrash : RetirementCause.Crash, 0.5,
                fatal ? "crashes fatally" : injuryWeeks > 0 ? "crashes out and is injured" : "crashes out", injuryWeeks);
            return;
        }

        var ideal = Circuit.LapKm / RaceSim.AverageSpeedKmh(entry, Circuit) * 3600;
        // Less consistent drivers spread their laps wider.
        var spread = (21 - driver.Skills.Consistency) * 0.005;
        var time = ideal * (1 + Math.Abs(Gaussian(rng)) * spread) * car.Form / speed;
        time *= 1 + 0.01 * car.Fuel;                  // a full tank is heavy
        time *= 1 + 0.05 * car.Tyres * car.Tyres;     // worn rear tyres lose grip
        if (car.TyreBurst)
            time *= 1.4;                              // limping back on a burst tyre
        if (wet)
            time *= RaceWeather.WetLapFactor(driver);
        if (night)
            time *= NightLapFactor;                   // headlamps of the day light little of the road
        time *= 1 + tired;

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

    /// <summary>Engine strain, crash risk and tyre wear at endurance pace, against a Grand Prix.</summary>
    public const double EnduranceStrain = 0.5, EnduranceRisk = 0.4, EnduranceWear = 0.6;

    /// <summary>Laps at night are this much slower, and crashes this much more likely.</summary>
    public const double NightLapFactor = 1.05, NightCrashFactor = 1.6;

    /// <summary>
    /// How much a long stint slows the driver, as a share of lap time: nothing for the first two
    /// hours, then quickly more (4% after four hours, 9% after five). Only endurance races are
    /// long enough for it to matter.
    /// </summary>
    private double Fatigue(CarState car, double time)
    {
        if (!Event.IsEndurance)
            return 0;
        var hours = Math.Max(0, (time - car.StintStart) / 3600 - 2);
        return 0.01 * hours * hours;
    }

    /// <summary>Laps still to run: to the lap count, or for an endurance race, until the clock runs out.</summary>
    private int LapsLeft(CarState car, double time)
    {
        if (TimeLimit is not { } limit)
            return Event.Laps - car.LapTimes.Count;
        // The quickest recent lap, so a lap slowed by a pit stop does not make the end seem nearer.
        var lapTime = car.LapTimes.Count > 0 ? car.LapTimes.TakeLast(5).Min() : Circuit.LapKm / RaceSim.AverageSpeedKmh(car.Entry, Circuit) * 3600;
        return Math.Max(0, (int)Math.Ceiling((limit - time) / lapTime));
    }

    /// <summary>The endurance rules let a car take on fuel only every <see cref="MinLapsBetweenRefuels"/> laps.</summary>
    private bool FuelAllowed(CarState car) => !Event.IsEndurance || car.LapsSinceFuel >= MinLapsBetweenRefuels;

    private void PitStop(CarState car, int lapsLeft, double start, int lap)
    {
        var rng = car.Rng;
        // Fuel for the rest of the race, or a full tank, when the rules allow it.
        // At Le Mans the tank is filled: the next chance to refuel may be 24 laps away.
        var needed = Event.IsEndurance ? 1 : Math.Min(1, lapsLeft * FuelPerLap(car) * 1.05);
        var fuelAdded = FuelAllowed(car) ? Math.Max(0, needed - car.Fuel) : 0;
        double seconds;
        string work;
        if (Event.IsEndurance)
        {
            // At Le Mans the driver does the work himself, alone: slow churns, all four wheels.
            seconds = 40 + 120 * fuelAdded + 60;
            work = fuelAdded > 0 ? "fuel and tyres" : "tyres only, too soon to refuel";
        }
        else
        {
            seconds = 20 + 30 * fuelAdded + 15;  // stop and go, fuel churns, rear wheels
            work = "fuel and tyres";
        }
        var crew = _pitCrews.GetValueOrDefault(car.Entry.Team.Id, AverageCrew);
        seconds *= Works.PitTimeFactor(crew);
        var fumble = rng.NextDouble() < Works.FumbleChance(crew);
        if (fumble)
            seconds += 10 + rng.NextDouble() * 20;
        if (fuelAdded > 0)
            car.LapsSinceFuel = 0;
        car.Fuel += fuelAdded;
        car.Tyres = 0;
        car.TyreBurst = false;
        car.PitCalled = false;
        car.PitStops++;
        var swap = car.Entry.CoDriver is not null && car.SwapAtStop;
        if (swap)
            seconds += 20;
        car.PitTime = seconds;
        Say(car, start, lap, $"pits for {work}, {seconds:0} s{(fumble ? " after a fumbled wheel change" : "")}");
        if (swap)
        {
            var other = car.AtWheel == car.Entry.Driver ? car.Entry.CoDriver! : car.Entry.Driver;
            car.AtWheel = other;
            car.StintStart = start + seconds;
            Say(car, start, lap, "takes over the car");
        }
    }

    /// <summary>
    /// The AI pit wall: stop when the next lap would run the tank dry (and the rules allow fuel)
    /// or the tyres are nearly gone; in an endurance race, also to rest a driver after about
    /// three hours at the wheel.
    /// </summary>
    private bool FuelShort(CarState car, int lapsLeft) =>
        lapsLeft > 0 && car.Fuel < FuelPerLap(car) * 1.5 && car.Fuel < lapsLeft * FuelPerLap(car) && FuelAllowed(car);

    private bool WantsStop(CarState car, int lapsLeft, double time)
    {
        if (lapsLeft <= 0)
            return false;
        var fuelShort = FuelShort(car, lapsLeft);
        var tyresGone = car.Tyres > 0.85 && lapsLeft * TyreWearPerLap(car) > 1 - car.Tyres;
        var weary = car.Entry.CoDriver is not null && (time - car.StintStart) / 3600 > 3 && lapsLeft > 3;
        return fuelShort || tyresGone || weary;
    }

    /// <summary>
    /// True when a breakdown or crash on this lap would come after the clock ran out: the car
    /// was still running at the end of a timed race, so it is classified as running.
    /// </summary>
    private bool PastTheClock(CarState car, double start) =>
        TimeLimit is { } limit && start + car.PitTime + Circuit.LapKm / RaceSim.AverageSpeedKmh(car.Entry, Circuit) * 3600 * 0.5 >= limit;

    private void Retire(CarState car, double start, int lap, RetirementCause cause, double fraction, string text, int injuryWeeks = 0)
    {
        var ideal = Circuit.LapKm / RaceSim.AverageSpeedKmh(car.Entry, Circuit) * 3600;
        car.Retirement = new Retirement(lap, cause, injuryWeeks, car.Entry.CoDriver is null ? null : car.AtWheel.Id);
        car.StopFraction = fraction;
        car.StopTime = start + car.PitTime + ideal * fraction;
        Say(car, car.StopTime, lap, text);
    }

    private void Say(CarState car, double time, int lap, string text) =>
        _messages.Add(new RaceMessage(time, lap, car.Entry.Driver.Id, $"{car.AtWheel.Name} {text}"));

    private double FuelPerLap(CarState car) =>
        Circuit.LapKm / TankKm * car.Entry.Team.Car.Thirst * (car.Pace == PaceOrder.Push ? 1.1 : car.Pace == PaceOrder.Nurse ? 0.9 : 1);

    /// <summary>Rear tyres last about 250 km at racing pace in a 150 hp car; twisty circuits wear them faster.</summary>
    private double TyreWearPerLap(CarState car) =>
        Circuit.LapKm * TyreWear(car.Entry.Team.Car) / 300 * (1.15 - 0.3 * Circuit.StraightShare);

    /// <summary>How hard a car is on its tyres: more power and more weight wear them faster.</summary>
    public static double TyreWear(Car car) => (0.8 + car.Horsepower / 400.0) * (0.85 + car.WeightKg / 5000.0);

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
        /// <summary>Who is driving: the first driver until a swap.</summary>
        public Driver AtWheel = entry.Driver;
        public double StintStart;
        public bool SwapAtStop = true;
        public int LapsSinceFuel;
    }
}
