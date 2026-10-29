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

/// <summary>How much fuel goes in at a stop.</summary>
public enum FuelPlan
{
    /// <summary>Enough to reach the next planned stop, or the flag when none is planned: the lightest car.</summary>
    NextStop,
    /// <summary>Fill the tank: a heavier car and a longer stop, but room to stay out.</summary>
    Full,
    /// <summary>No fuel: a quick stop for tyres only.</summary>
    None,
}

/// <summary>What the crew does when the car stops: how much fuel goes in, and whether the rear wheels are changed.</summary>
public sealed record PitPlan(FuelPlan Fuel = FuelPlan.NextStop, bool Tyres = true);

/// <summary>Something worth a line in the race commentary.</summary>
public sealed record RaceMessage(double Time, int Lap, string DriverId, string Text);

/// <summary>Where one car is at a moment of the race, with the state the pit wall can see.</summary>
/// <param name="Distance">Laps covered so far, e.g. 3.25 is a quarter of the way round lap 4.</param>
/// <param name="FinishedAt">Race time in seconds when the car took the flag, or 0 before then.</param>
/// <param name="AtWheel">The driver in the car now; for a shared car, either of the two.</param>
/// <param name="StintHours">How long the driver at the wheel has been in the car.</param>
/// <param name="SwapAtStop">For a shared car: the drivers change at the next stop.</param>
/// <param name="FuelAllowed">False while the endurance rules forbid refuelling at the next stop.</param>
/// <param name="LapsDone">Laps completed so far.</param>
/// <param name="LastLap">The last completed lap in seconds, pit stop included, or 0 before the first.</param>
/// <param name="BestLap">The quickest completed lap in seconds, or 0 before the first.</param>
/// <param name="BestLapNumber">Which lap the quickest one was.</param>
/// <param name="Grid">Starting place, from 1.</param>
/// <param name="PitSeconds">Seconds stood in the pits so far.</param>
/// <param name="Plan">What the crew does at the next stop.</param>
/// <param name="PlannedStops">The laps still to come at whose end the car is called in.</param>
/// <param name="StandingStart">The lap now running started from rest: the first lap, or out of the pits.</param>
/// <param name="LapSeconds">How long the lap now running takes on the move, pit stop left out.</param>
public sealed record LiveCar(
    RaceEntry Entry, double Distance, bool Running, Retirement? Retirement, bool Finished,
    bool InPits, double Fuel, double TyreWear, PaceOrder Pace, bool PitCalled, int PitStops, double FinishedAt = 0,
    Driver? AtWheel = null, double StintHours = 0, bool SwapAtStop = true, bool FuelAllowed = true,
    int LapsDone = 0, double LastLap = 0, double BestLap = 0, int BestLapNumber = 0, int Grid = 0,
    double PitSeconds = 0, PitPlan? Plan = null, IReadOnlyList<int>? PlannedStops = null,
    bool StandingStart = false, double LapSeconds = 0);

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
            1 + Gaussian(dayForm) * 0.002) { Grid = i + 1 }).ToArray();
        foreach (var car in _cars)
            StartLap(car, 0);
    }

    public bool IsOver => _cars.All(c => c.Done);

    /// <summary>Race time in seconds when the last running car takes the flag, once the race is over.</summary>
    public double FinishTime => _cars.Select(c => c.Retirement is null ? c.LapEnd : c.StopTime).DefaultIfEmpty(0).Max();

    public void SetPace(string driverId, PaceOrder pace) => Car(driverId).Pace = pace;

    /// <summary>Calls the car in at the end of its current lap; calling again cancels.</summary>
    public void CallPitStop(string driverId, bool call = true) => Car(driverId).PitCalled = call;

    /// <summary>What the crew does at this car's stops from now on.</summary>
    public void SetPitPlan(string driverId, PitPlan plan) => Car(driverId).Plan = plan;

    /// <summary>
    /// Plans the car's stops ahead: it comes in at the end of each of these laps. Laps already
    /// run are dropped; an empty list leaves the stops to the player's calls (and the driver's
    /// own sense of an empty tank).
    /// </summary>
    public void SetPlannedStops(string driverId, IEnumerable<int> laps)
    {
        var car = Car(driverId);
        car.PlannedStops.Clear();
        car.PlannedStops.UnionWith(laps.Where(l => l > car.LapTimes.Count && l < Event.Laps));
    }

    /// <summary>
    /// Laps that split what is left of the race into equal stints, for a plan of so many stops
    /// from now on. Endurance races run on the clock, so they have no lap plan.
    /// </summary>
    public IReadOnlyList<int> EvenStops(string driverId, int stops)
    {
        var car = Car(driverId);
        var done = car.LapTimes.Count;
        var left = Event.Laps - done;
        if (Event.IsEndurance || stops <= 0 || left < 2)
            return [];
        // A stop comes at the end of a lap: the earliest the end of the lap running now, the latest the lap before the last.
        return Enumerable.Range(1, stops)
            .Select(k => Math.Clamp(done + (int)Math.Round(left * k / (double)(stops + 1)), done + 1, Event.Laps - 1))
            .Distinct().ToList();
    }

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
            car.Crossings.Add(car.LapEnd);
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
            var laps = car.LapTimes;
            var best = laps.Count == 0 ? 0 : laps.Min();
            list.Add(new LiveCar(car.Entry, distance, !finished && !stopped, stopped ? car.Retirement : null, finished,
                inPits, car.Fuel, car.Tyres, car.Pace, car.PitCalled, car.PitStops, finished ? car.LapEnd : 0,
                car.AtWheel, Math.Max(0, Time - car.StintStart) / 3600, car.SwapAtStop, FuelAllowed(car),
                laps.Count, laps.Count == 0 ? 0 : laps[^1], best, laps.IndexOf(best) + 1, car.Grid,
                // A stop at the start of the lap now running counts once the car is standing in the pits.
                car.PitSecondsDone - car.PitTime + Math.Clamp(Time - car.LapStart, 0, car.PitTime),
                car.Plan, car.PlannedStops.Order().ToList(),
                car.LapTimes.Count == 0 || car.PitTime > 0, Math.Max(0, car.LapEnd - driving)));
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
        var planned = car.PlannedStops.Remove(lap - 1);
        if (lap > 1 && (car.PitCalled || car.TyreBurst || stops || planned))
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
        if (!manual && !Event.IsEndurance)
            car.Pace = RaceCraft(car, start);
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
        var spread = (21 - driver.Skills.Consistency) * 0.001;
        var time = ideal * (1 + Math.Abs(Gaussian(rng)) * spread) * car.Form / speed;
        time *= 1 + 0.02 * car.Fuel;                  // a full tank is heavy: a light load is worth planning for
        time *= 1 + 0.05 * car.Tyres * car.Tyres;     // worn rear tyres lose grip
        if (car.TyreBurst)
            time *= 1.4;                              // limping back on a burst tyre
        if (wet)
            time *= RaceWeather.WetLapFactor(driver);
        if (night)
            time *= NightLapFactor;                   // headlamps of the day light little of the road
        time *= 1 + tired;
        if (Event.IsEndurance)
            time *= EnduranceLapFactor;               // driven to last the day, not to win a lap

        car.Fuel -= fuelForLap;
        car.Tyres += TyreWearPerLap(car) * wear;
        car.TyreBurst = false;
        if (car.Tyres >= 1)
        {
            car.TyreBurst = true;
            car.Tyres = 1;
            Say(car, start, lap, "has a tyre failure");
        }
        car.LapEnd = Overtaking(car, start, start + car.PitTime + time);
    }

    /// <summary>Within this many seconds at the line, a car is close enough to fight the car ahead for the place.</summary>
    public const double FightingSeconds = 6;

    /// <summary>
    /// Wheel to wheel: a car that would catch and pass the car ahead on the same lap has to
    /// get by. A clearly faster car, a long straight and a brave driver make it likelier;
    /// otherwise it is held up and crosses the line just behind. Close behind on a fast
    /// circuit, a car also gains a little in the slipstream. This is what kept the leaders of
    /// a 1930s Grand Prix together, as at Tripoli in 1933 and 1934, won by a fifth of a second.
    /// Returns when the car finishes the lap.
    /// </summary>
    private double Overtaking(CarState car, double start, double lapEnd)
    {
        if (car.PitTime > 0 || car.Crossings.Count == 0)
            return lapEnd;
        var lap = car.Crossings.Count;
        var ahead = _cars
            .Where(c => c != car && c.Retirement is null && !c.Done && c.Crossings.Count == lap && c.Crossings[^1] <= start && c.PitTime == 0)
            .MaxBy(c => c.Crossings[^1]);
        if (ahead is null || start - ahead.Crossings[^1] > FightingSeconds)
            return lapEnd;
        var gap = start - ahead.Crossings[^1];
        var straights = Circuit.StraightShare;
        // The tow: a few tenths a lap on a fast circuit, from right behind.
        if (gap < 2)
            lapEnd -= (lapEnd - start) * 0.004 * straights;
        if (lapEnd >= ahead.LapEnd)
            return lapEnd;
        var advantage = (ahead.LapEnd - ahead.LapStart) / (lapEnd - start) - 1;
        var bravery = (car.AtWheel.Skills.Bravery - 10) / 100.0;
        var chance = Math.Clamp(0.1 + 0.4 * straights + 12 * advantage + bravery, 0.05, 0.95);
        if (car.Rng.NextDouble() < chance)
            return lapEnd;
        // Held up: over the line just behind, to try again next lap.
        return ahead.LapEnd + 0.2 + car.Rng.NextDouble();
    }

    /// <summary>A lead this big lets a driver ease off and spare the car.</summary>
    public const double ComfortableLeadSeconds = 45;

    /// <summary>Closer than this to the car ahead, a driver presses to pass.</summary>
    public const double BattleSeconds = 4;

    /// <summary>
    /// How an AI driver paces himself, as the drivers of the day did: one with a comfortable gap
    /// to the car behind eases off to spare the car, one right on the tail of the car ahead
    /// presses (at more risk to tyres, engine and himself, unless the tyres are already worn),
    /// and the rest race. It is what kept the leaders of a 1930s Grand Prix seconds apart while
    /// the field behind them spread out by laps. Decided as the car crosses the line.
    /// </summary>
    private PaceOrder RaceCraft(CarState car, double time)
    {
        var lap = car.Crossings.Count;
        if (lap == 0)
            return PaceOrder.Race;
        // The car ahead on the road in the race: the last to cross this line on the same lap before us.
        var ahead = _cars.Where(c => c != car && c.Retirement is null && c.Crossings.Count >= lap && c.Crossings[lap - 1] <= time)
            .Select(c => time - c.Crossings[lap - 1]).DefaultIfEmpty(double.MaxValue).Min();
        // The car behind: the first of those still on the lap we have just finished to come round.
        var behind = _cars.Where(c => c != car && c.Retirement is null && c.Crossings.Count == lap - 1 && c.LapEnd > time)
            .Select(c => c.LapEnd - time).DefaultIfEmpty(double.MaxValue).Min();
        if (ahead < BattleSeconds && car.Tyres < 0.6)
            return PaceOrder.Push;
        if (behind > ComfortableLeadSeconds)
            return PaceOrder.Hold;
        return PaceOrder.Race;
    }

    /// <summary>Engine strain, crash risk and tyre wear at endurance pace, against a Grand Prix.</summary>
    public const double EnduranceStrain = 0.25, EnduranceRisk = 0.4, EnduranceWear = 0.6;

    /// <summary>Laps at endurance pace are this much slower than a Grand Prix driver's in the same car.</summary>
    public const double EnduranceLapFactor = 1.05;

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
        // Fuel as the plan says, when the rules allow it: by default for the rest of the race,
        // or to the next planned stop, with a little to spare. At Le Mans the tank is filled:
        // the next chance to refuel may be 24 laps away.
        var plan = car.Plan;
        var nextStop = car.PlannedStops.Where(l => l >= lap).DefaultIfEmpty(0).Min();
        var lapsToRun = nextStop > 0 ? Math.Min(lapsLeft, nextStop - lap + 1) : lapsLeft;
        var needed = plan.Fuel switch
        {
            FuelPlan.None => 0,
            FuelPlan.Full => 1,
            _ when Event.IsEndurance => 1,
            _ => Math.Min(1, lapsToRun * FuelPerLap(car) * 1.05),
        };
        var fuelAdded = FuelAllowed(car) ? Math.Max(0, needed - car.Fuel) : 0;
        // A burst tyre is changed whatever the plan says.
        var tyres = plan.Tyres || car.TyreBurst;
        double seconds;
        if (Event.IsEndurance)
            // At Le Mans the driver does the work himself, alone: slow churns, all four wheels.
            seconds = 40 + 120 * fuelAdded + (tyres ? 60 : 0);
        else
            seconds = 20 + 30 * fuelAdded + (tyres ? 15 : 0);  // stop and go, fuel churns, rear wheels
        var work = (fuelAdded > 0, tyres) switch
        {
            (true, true) => "fuel and tyres",
            (true, false) => "fuel only",
            (false, true) => Event.IsEndurance && plan.Fuel != FuelPlan.None && !FuelAllowed(car) ? "tyres only, too soon to refuel" : "tyres only",
            _ => "a quick look over the car",
        };
        var crew = _pitCrews.GetValueOrDefault(car.Entry.Team.Id, AverageCrew);
        seconds *= Works.PitTimeFactor(crew);
        var fumble = rng.NextDouble() < Works.FumbleChance(crew);
        if (fumble)
            seconds += 10 + rng.NextDouble() * 20;
        if (fuelAdded > 0)
            car.LapsSinceFuel = 0;
        car.Fuel += fuelAdded;
        if (tyres)
            car.Tyres = 0;
        car.TyreBurst = false;
        car.PitCalled = false;
        car.PitStops++;
        var swap = car.Entry.CoDriver is not null && car.SwapAtStop;
        if (swap)
            seconds += 20;
        car.PitTime = seconds;
        car.PitSecondsDone += seconds;
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
    public static double TyreWear(Car car) => (0.8 + car.Horsepower / 400.0) * (0.85 + car.WeightKg / 5000.0) * car.Wear;

    /// <summary>(speed, tyre wear, engine strain, crash risk) multipliers for a pace order.</summary>
    private static (double Speed, double Wear, double Strain, double Risk) PaceEffect(PaceOrder pace) => pace switch
    {
        PaceOrder.Push => (1.015, 1.4, 1.6, 1.5),
        PaceOrder.Hold => (0.99, 0.85, 0.8, 0.8),
        PaceOrder.Nurse => (0.97, 0.7, 0.5, 0.6),
        _ => (1, 1, 1, 1),
    };

    /// <summary>
    /// The distance a car's reliability figure is the chance of lasting. Set so that, as in the
    /// races of the day, only about half the starters of a Grand Prix are running at the end
    /// (checked against real results by tools/RealismRunner).
    /// </summary>
    public const double ReliabilityKm = 250;

    internal static double FailureChancePerLap(RaceEntry entry, Circuit circuit)
    {
        // Reliability is the chance to last ReliabilityKm; spread that risk over the laps.
        var perKm = -Math.Log(entry.Team.Car.Reliability) / ReliabilityKm;
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
        /// <summary>Race time at which the car crossed the line at the end of each lap.</summary>
        public List<double> Crossings { get; } = new();
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
        /// <summary>Seconds of every stop made, the one at the start of the current lap included.</summary>
        public double PitSecondsDone;
        public PitPlan Plan = new();
        /// <summary>Laps at whose end the player wants the car in.</summary>
        public SortedSet<int> PlannedStops { get; } = new();
        public int Grid;
        public Retirement? Retirement;
        public bool Done;
        /// <summary>Who is driving: the first driver until a swap.</summary>
        public Driver AtWheel = entry.Driver;
        public double StintStart;
        public bool SwapAtStop = true;
        public int LapsSinceFuel;
    }
}
