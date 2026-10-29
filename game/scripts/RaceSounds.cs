using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RacingSim;

namespace RacingManager;

/// <summary>
/// The sound of a race, heard from where the camera is. The nearest cars each get an engine
/// voice: a low-revs and a high-revs loop cross-faded and pitched to the revs, an overrun loop
/// for braking, and the supercharger's whine. Revs come from the car's real speed at that point
/// of the lap (<see cref="TrackShape.SpeedKmh"/>) through a four-speed gearbox, so a car changes
/// up out of a bend and blips down into the next. Each voice is panned to where the car is in
/// the picture, fades and dulls with distance in metres, and drops in pitch as it passes. The
/// rest of the field is a distant rumble; the crowd, the pits, rain, crashes and the night are
/// effects round it.
/// </summary>
public partial class RaceSounds : Node
{
    private const int VoiceCount = 8;
    private const float LowRpm = 2400, HighRpm = 5250, OffRpm = 3500, WhineRpm = 4000;
    private static readonly double[] Gears = { 0.36, 0.55, 0.77, 1.0 };

    private sealed class Voice
    {
        public int Bus;
        public AudioEffectPanner Panner = null!;
        public AudioEffectLowPassFilter Air = null!;
        public AudioStreamPlayer Low = null!, High = null!, Off = null!, Whine = null!;
        public string? DriverId;
        public string? Wanted;
        public float Gain;
    }

    private sealed class CarState
    {
        public int Gear;
        public double Rpm = 900;
        public double Throttle = 1;
        public double ShiftCut;
        public double Metres = -1;
        public double Closing;
        public bool InPits;
        public bool Out;
    }

    private readonly LiveRace _race;
    private readonly TrackShape _shape;
    private readonly List<Voice> _voices = new();
    private readonly Dictionary<string, CarState> _states = new();
    private AudioStreamPlayer _field = null!, _crowd = null!, _rain = null!, _night = null!;
    private double _lastTime = -1;
    private bool _started, _finished;

    public RaceSounds(LiveRace race, TrackShape shape)
    {
        _race = race;
        _shape = shape;
    }

    public override void _Ready()
    {
        for (var i = 0; i < VoiceCount; i++)
            _voices.Add(NewVoice(i));
        _field = Loop("field", Sounds.Channel.Engines);
        _crowd = Loop("crowd", Sounds.Channel.Effects);
        _rain = Loop("rain", Sounds.Channel.Effects);
        _night = Loop("night", Sounds.Channel.Effects);
    }

    public override void _ExitTree()
    {
        // Silence the voice buses for the next race; the buses themselves are kept.
        foreach (var v in _voices)
            AudioServer.SetBusVolumeDb(v.Bus, -80);
    }

    private AudioStreamPlayer Loop(string name, Sounds.Channel channel, string? bus = null)
    {
        var player = new AudioStreamPlayer { Stream = Sounds.Stream(name, loop: true), Bus = bus ?? Sounds.Bus(channel), VolumeDb = -80 };
        AddChild(player);
        return player;
    }

    /// <summary>An engine voice on its own bus, panned and filtered, feeding the Engines bus.</summary>
    private Voice NewVoice(int i)
    {
        var name = $"Engine voice {i + 1}";
        var bus = AudioServer.GetBusIndex(name);
        if (bus < 0)
        {
            AudioServer.AddBus();
            bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, name);
            AudioServer.SetBusSend(bus, Sounds.Bus(Sounds.Channel.Engines));
            AudioServer.AddBusEffect(bus, new AudioEffectPanner());
            AudioServer.AddBusEffect(bus, new AudioEffectLowPassFilter { CutoffHz = 16000, Resonance = 0.5f });
        }
        var voice = new Voice
        {
            Bus = bus,
            Panner = (AudioEffectPanner)AudioServer.GetBusEffect(bus, 0),
            Air = (AudioEffectLowPassFilter)AudioServer.GetBusEffect(bus, 1),
        };
        AudioServer.SetBusVolumeDb(bus, 0);
        voice.Low = Player(name);
        voice.High = Player(name);
        voice.Off = Player(name);
        voice.Whine = Player(name);
        return voice;
    }

    private AudioStreamPlayer Player(string bus)
    {
        var player = new AudioStreamPlayer { Bus = bus, VolumeDb = -80 };
        AddChild(player);
        return player;
    }

    // ------------------------------------------------------------------ every frame

    /// <summary>Brings the sound up to the race at <paramref name="time"/>, played at <paramref name="speed"/> times real time.</summary>
    public void Update(IReadOnlyList<LiveCar> cars, IRaceView view, double time, int speed, bool paused, float rain, float night, double delta)
    {
        var dt = (float)Math.Max(delta, 1e-4);
        var simDelta = _lastTime < 0 ? 0 : time - _lastTime;
        _lastTime = time;
        var (ear, height) = view.Ear;
        var scale = _shape.MetresPerHeight > 0 ? _shape.MetresPerHeight : 3000;
        double Metres(Vector2 p) =>
            scale * Math.Sqrt(Math.Pow((p.X - ear.X) * TrackLine.Aspect, 2) + Math.Pow(p.Y - ear.Y, 2) + height * height);

        // Sped up, the engines step back and the revs smooth out: the race heard in a hurry.
        var hurry = Math.Max(1, speed);
        var engineLevel = paused ? 0 : 0.28 * Math.Clamp(1 - 0.27 * Math.Log10(hurry), 0.25, 1);
        var smooth = 1 - Math.Exp(-dt / (0.05 * Math.Sqrt(hurry)));

        var heard = new List<(LiveCar Car, Vector2 At, double Metres, double Loudness)>();
        foreach (var car in cars)
        {
            var id = car.Entry.Driver.Id;
            if (!_states.TryGetValue(id, out var state))
                _states[id] = state = new CarState { InPits = car.InPits };
            var at = _shape.At(_shape.Placed(car));
            var metres = Metres(at);
            if (state.Metres >= 0 && simDelta > 0)
                state.Closing += (Math.Clamp((metres - state.Metres) / simDelta, -150, 150) - state.Closing) * Math.Min(1, smooth * 2);
            state.Metres = metres;
            Events(car, state, at, metres, time > 0 && !paused && simDelta < 120);
            if (!car.Running || car.InPits)
                continue;
            Revs(car, state, speed, simDelta, smooth);
            heard.Add((car, at, metres, Engine.For(car.Entry.Team.Car).Loudness / Math.Max(metres, 10)));
        }

        // The loudest cars get voices; a car keeps its voice while it is still nearly loud enough.
        var ranked = heard.OrderByDescending(h => h.Loudness).ToList();
        var keep = ranked.Take(VoiceCount + 2).Select(h => h.Car.Entry.Driver.Id).ToHashSet();
        foreach (var v in _voices)
            v.Wanted = v.DriverId is not null && keep.Contains(v.DriverId) ? v.DriverId : null;
        foreach (var h in ranked.Take(VoiceCount))
        {
            var id = h.Car.Entry.Driver.Id;
            if (_voices.Any(v => v.Wanted == id))
                continue;
            if (_voices.FirstOrDefault(v => v.Wanted is null) is { } free)
                free.Wanted = id;
        }

        var field = 0.0;
        var voiced = new HashSet<string>();
        foreach (var v in _voices)
        {
            // A voice handed to another car fades out first, then picks the new car up.
            if (v.DriverId != v.Wanted)
            {
                v.Gain = Mathf.MoveToward(v.Gain, 0, dt * 8);
                if (v.Gain <= 0.001f)
                    Assign(v, v.Wanted, cars);
            }
            var h = heard.FirstOrDefault(x => x.Car.Entry.Driver.Id == v.DriverId);
            if (h.Car is null)
            {
                v.Gain = Mathf.MoveToward(v.Gain, 0, dt * 8);
                Mix(v, null, null, 0, 0, 1, 0);
                continue;
            }
            voiced.Add(v.DriverId!);
            if (v.DriverId == v.Wanted)
                v.Gain = Mathf.MoveToward(v.Gain, 1, dt * 6);
            var state = _states[v.DriverId!];
            var engine = Engine.For(h.Car.Entry.Team.Car);
            // Doppler: the note rises as a car comes on and drops as it goes by.
            var doppler = Math.Clamp(343 / (343 + state.Closing), 0.75, 1.3);
            var distance = Math.Min(1, 12 / Math.Max(h.Metres, 1));
            Mix(v, engine, state, engine.Loudness * distance * engineLevel * v.Gain, doppler, view.PanOf(h.At), h.Metres);
        }
        foreach (var h in heard.Where(h => !voiced.Contains(h.Car.Entry.Driver.Id)))
            field += Math.Pow(Engine.For(h.Car.Entry.Team.Car).Loudness * Math.Min(1, 40 / Math.Max(h.Metres, 1)), 2);
        var fieldLevel = Math.Min(1, 0.45 * Math.Sqrt(field)) * engineLevel;
        Fade(_field, fieldLevel, dt);
        if (heard.Count > 0)
            _field.PitchScale = (float)Math.Clamp(0.8 + 0.4 * heard.Average(h => _shape.SpeedKmh(h.Car)) / 200, 0.7, 1.3);

        // The crowd is loudest close to the stands, and never quite silent.
        var stands = _shape.Grandstands.Sum(s => Math.Min(1, 80 / Metres(s.GetCenter())));
        Fade(_crowd, Math.Min(0.45, 0.06 + 0.35 * stands), dt);
        Fade(_rain, 0.5 * rain, dt);
        Fade(_night, 0.35 * night * (1 - fieldLevel), dt);

        if (!_started && time > 0 && !paused)
        {
            _started = true;
            Sounds.Play("cheer", Sounds.Channel.Effects, -6);
        }
        if (!_finished && cars.Count > 0 && cars[0].Finished)
        {
            _finished = true;
            Sounds.Play("cheer", Sounds.Channel.Effects, -2, 1.05f);
        }
    }

    private static void Fade(AudioStreamPlayer player, double level, float dt)
    {
        if (player.Stream is null)
            return;
        var target = (float)Mathf.LinearToDb(Math.Max(level, 1e-4));
        player.VolumeDb = Mathf.MoveToward(player.VolumeDb, target, dt * 40);
        var audible = player.VolumeDb > -60;
        if (audible && !player.Playing)
            player.Play();
        else if (!audible && player.Playing)
            player.Stop();
    }

    /// <summary>The pit stops, crashes and broken engines heard round the circuit.</summary>
    private void Events(LiveCar car, CarState state, Vector2 at, double metres, bool live)
    {
        var near = Math.Min(1, 25 / Math.Max(metres, 1));
        var db = (float)Mathf.LinearToDb(Math.Max(near, 1e-4));
        if (car.InPits && !state.InPits && live && near > 0.02)
        {
            Sounds.Play("pit-wheel", Sounds.Channel.Effects, db);
            Sounds.Play("pit-fuel", Sounds.Channel.Effects, db - 6);
        }
        state.InPits = car.InPits;
        if (car.Retirement is { } out_ && !state.Out)
        {
            state.Out = true;
            var sound = out_.Cause switch
            {
                RetirementCause.MechanicalFailure => "engine-blow",
                RetirementCause.Crash or RetirementCause.FatalCrash => "crash",
                _ => null,
            };
            if (live && near > 0.01 && sound is not null)
                Sounds.Play(sound, Sounds.Channel.Effects, db + 3);
        }
    }

    /// <summary>The revs and throttle of a car now, from its speed and a four-speed gearbox.</summary>
    private void Revs(LiveCar car, CarState state, int speed, double simDelta, double smooth)
    {
        var engine = Engine.For(car.Entry.Team.Car);
        var kmh = _shape.SpeedKmh(car);
        var top = Math.Max(120, _shape.TopSpeedKmh(car));
        var redline = engine.Redline;
        double target;
        var braking = _shape.SpeedKmh(car, 15) < kmh - 1.5;
        if (speed >= 60)
        {
            // Too fast for gear changes to mean anything: the note just follows the speed.
            target = redline * (0.5 + 0.42 * kmh / top);
        }
        else
        {
            double In(int gear) => redline * 0.96 * kmh / (top * Gears[gear]);
            if (state.Gear < Gears.Length - 1 && In(state.Gear) > 0.95 * redline)
            {
                state.Gear++;
                state.ShiftCut = 0.12;
            }
            else if (state.Gear > 0 && In(state.Gear) < 0.5 * redline)
                state.Gear--;
            target = Math.Max(900, In(state.Gear));
            // Away from rest the clutch slips and the engine is held high.
            if (state.Gear == 0 && !braking)
                target = Math.Max(target, 0.6 * redline);
        }
        state.ShiftCut = Math.Max(0, state.ShiftCut - simDelta);
        var throttle = braking || state.ShiftCut > 0 ? 0 : 1;
        // A change up is quick; the revs fall at once and climb again.
        var rate = target < state.Rpm && state.ShiftCut > 0 ? Math.Min(1, smooth * 3) : smooth;
        state.Rpm += (target - state.Rpm) * rate;
        state.Throttle += (throttle - state.Throttle) * Math.Min(1, smooth * 1.5);
    }

    private void Assign(Voice v, string? driverId, IReadOnlyList<LiveCar> cars)
    {
        v.DriverId = driverId;
        var car = cars.FirstOrDefault(c => c.Entry.Driver.Id == driverId);
        foreach (var p in new[] { v.Low, v.High, v.Off, v.Whine })
            p.Stop();
        if (car is null)
            return;
        var engine = Engine.For(car.Entry.Team.Car);
        v.Low.Stream = Sounds.Stream($"engine-{engine.Kind}-low", loop: true);
        v.High.Stream = Sounds.Stream($"engine-{engine.Kind}-high", loop: true);
        v.Off.Stream = Sounds.Stream($"engine-{engine.Kind}-off", loop: true);
        v.Whine.Stream = engine.Supercharged ? Sounds.Stream("supercharger", loop: true) : null;
        // Each loop from a different point, so two cars of one make do not sound as one.
        var start = (float)GD.RandRange(0.0, 0.9);
        foreach (var p in new[] { v.Low, v.High, v.Off, v.Whine })
            if (p.Stream is not null)
                p.Play(start);
    }

    private static void Mix(Voice v, Engine? engine, CarState? state, double gain, double doppler, float pan, double metres)
    {
        if (engine is null || state is null || gain < 1e-4)
        {
            foreach (var p in new[] { v.Low, v.High, v.Off, v.Whine })
                p.VolumeDb = -80;
            return;
        }
        var rpm = state.Rpm;
        var t = Math.Clamp((rpm - LowRpm) / (HighRpm - LowRpm), 0, 1);
        var on = state.Throttle;
        Set(v.Low, gain * on * Math.Cos(t * Math.PI / 2), rpm / LowRpm * doppler);
        Set(v.High, gain * on * Math.Sin(t * Math.PI / 2), rpm / HighRpm * doppler);
        Set(v.Off, gain * (1 - on) * 0.55, rpm / OffRpm * doppler);
        Set(v.Whine, gain * 0.28 * (0.4 + 0.6 * on) * Math.Min(1, rpm / engine.Redline), rpm / WhineRpm * doppler);
        v.Panner.Pan = pan * 0.85f;
        // Air takes the top off a far engine.
        v.Air.CutoffHz = (float)Math.Clamp(16000 / (1 + metres / 150), 1200, 16000);
    }

    private static void Set(AudioStreamPlayer player, double gain, double pitch)
    {
        player.VolumeDb = (float)Mathf.LinearToDb(Math.Max(gain, 1e-5));
        player.PitchScale = (float)Math.Clamp(pitch, 0.25, 3.5);
    }

    // ------------------------------------------------------------------ engines by car

    /// <summary>What a car's engine sounds like: which loops, a blower or not, how high it revs, how loud.</summary>
    public sealed record Engine(string Kind, bool Supercharged, double Redline, double Loudness)
    {
        private static readonly Dictionary<string, Engine> Known = new();

        public static Engine For(Car car)
        {
            if (Known.TryGetValue(car.Name, out var known))
                return known;
            var name = car.Name;
            bool Has(params string[] parts) => parts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));
            // Designs from the drawing office are named for their cylinders: "Alfa Rossa 12C-37".
            var designed = System.Text.RegularExpressions.Regex.Match(name, @" (\d+)C-\d\d");
            var cylinders = designed.Success ? int.Parse(designed.Groups[1].Value)
                : Has("Auto Unitas Typ D") ? 12
                : Has("Auto Unitas") || Has("Alfa Rossa 316") ? 16
                : Has("W154", "12C-36") ? 12
                : Has("6CM", "6C 1750", "SSK") ? 6
                : Has("Millard Special", "Indianapolis special") ? 4
                : 8;
            var kind = cylinders switch { <= 4 => "four", <= 6 => "six", <= 8 => "eight", <= 12 => "v12", _ => "v16" };
            // Nearly every racing car of the decade was supercharged; the Indianapolis junk-formula cars and the 57G were not.
            var blown = !Has("Millard Special", "Indianapolis special", "57G", "Duesenhoff Special");
            var redline = kind switch { "four" => 5800, "six" => 5600, "eight" => 5800, "v12" => 7200, _ => 5000 };
            var loudness = Math.Clamp(Math.Pow(car.Horsepower / 250.0, 0.35), 0.6, 1.3);
            return Known[car.Name] = new Engine(kind, blown, redline, loudness);
        }
    }
}
