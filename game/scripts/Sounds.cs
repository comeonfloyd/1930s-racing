using System;
using System.Collections.Generic;
using Godot;

namespace RacingManager;

/// <summary>
/// Autoload that owns the game's sound: the mixer buses and their volumes (saved in
/// user://settings.cfg), the sounds themselves, loaded from game/sounds/*.wav, a click for every
/// button, and the gramophone in the menus. The sounds are made by tools/sounds/synth.py and
/// read raw, without Godot's import step, like the art. M mutes everything.
/// </summary>
public partial class Sounds : Node
{
    public enum Channel { Engines, Effects, Music, Interface }

    private const string SettingsPath = "user://settings.cfg";
    private const string MusicName = "music-foxtrot";

    private static Sounds? _instance;
    private static readonly Dictionary<(string Name, bool Loop), AudioStreamWav?> Cache = new();
    private static readonly float[] Volumes = { 1, 1, 0.5f, 0.7f };
    private static float _master = 0.8f;
    private static bool _muted;

    private AudioStreamPlayer? _music;
    private readonly List<AudioStreamPlayer> _oneShots = new();

    public static float Master
    {
        get => _master;
        set
        {
            _master = Mathf.Clamp(value, 0, 1);
            Apply();
        }
    }

    public static bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            Apply();
        }
    }

    public static float Volume(Channel channel) => Volumes[(int)channel];

    public static void SetVolume(Channel channel, float value)
    {
        Volumes[(int)channel] = Mathf.Clamp(value, 0, 1);
        Apply();
    }

    public static string Bus(Channel channel) => channel.ToString();

    public override void _Ready()
    {
        _instance = this;
        ProcessMode = ProcessModeEnum.Always;
        foreach (Channel channel in Enum.GetValues(typeof(Channel)))
            if (AudioServer.GetBusIndex(Bus(channel)) < 0)
            {
                AudioServer.AddBus();
                var index = AudioServer.BusCount - 1;
                AudioServer.SetBusName(index, Bus(channel));
                AudioServer.SetBusSend(index, "Master");
            }
        // Many engines close together must not clip.
        AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -0.5f });
        Load();

        _music = new AudioStreamPlayer { Stream = Stream(MusicName, loop: true), Bus = Bus(Channel.Music), VolumeDb = -80 };
        AddChild(_music);

        // Every button in the game clicks: tabs turn a page, switches tick.
        GetTree().NodeAdded += node =>
        {
            if (node is not BaseButton button)
                return;
            var sound = button.ThemeTypeVariation == "Tab" ? "page"
                : button is CheckBox or CheckButton || button.ToggleMode ? "tick" : "click";
            button.Pressed += () => Play(sound, Channel.Interface);
        };
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    /// <summary>Lets go of every sound, for a clean quit (see <see cref="Shutdown"/>).</summary>
    public static void Release()
    {
        if (_instance is { } sounds)
        {
            if (sounds._music is { } music)
                music.Stream = null;
            foreach (var player in sounds._oneShots)
                player.Stream = null;
        }
        Cache.Clear();
    }

    public override void _Process(double delta)
    {
        // The gramophone plays in the menus and is lifted off for a race, where the engines are the music.
        if (_music?.Stream is null)
            return;
        var racing = GetTree().CurrentScene is RaceScreen;
        var target = racing ? -60f : 0f;
        _music.VolumeDb = Mathf.MoveToward(_music.VolumeDb, target, (float)delta * (racing ? 40 : 15));
        if (!racing && !_music.Playing)
        {
            _music.VolumeDb = -30;
            _music.Play();
        }
        else if (racing && _music.Playing && _music.VolumeDb <= -59)
            _music.Stop();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.M } key && !key.CtrlPressed && !key.MetaPressed && !key.AltPressed)
        {
            Muted = !Muted;
            Save();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Plays a sound once, at this volume. Returns false when it could not be loaded.</summary>
    public static bool Play(string name, Channel channel, float volumeDb = 0, float pitch = 1)
    {
        if (_instance is not { } sounds || Stream(name) is not { } stream)
            return false;
        var player = sounds._oneShots.Find(p => !p.Playing);
        if (player is null)
        {
            if (sounds._oneShots.Count >= 16)
                return false;
            player = new AudioStreamPlayer();
            sounds.AddChild(player);
            sounds._oneShots.Add(player);
        }
        player.Stream = stream;
        player.Bus = Bus(channel);
        player.VolumeDb = volumeDb;
        player.PitchScale = pitch;
        player.Play();
        return true;
    }

    /// <summary>A sound from game/sounds, read once and kept; null when the file is missing or not a 16-bit WAV.</summary>
    public static AudioStreamWav? Stream(string name, bool loop = false)
    {
        if (Cache.TryGetValue((name, loop), out var cached))
            return cached;
        var stream = Read($"res://sounds/{name}.wav", loop);
        Cache[(name, loop)] = stream;
        return stream;
    }

    private static AudioStreamWav? Read(string path, bool loop)
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PushWarning($"Missing sound {path}");
            return null;
        }
        var bytes = FileAccess.GetFileAsBytes(path);
        int channels = 0, rate = 0, bits = 0;
        var at = 12;
        while (at + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, at, 4);
            var size = BitConverter.ToInt32(bytes, at + 4);
            var body = at + 8;
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(bytes, body + 2);
                rate = BitConverter.ToInt32(bytes, body + 4);
                bits = BitConverter.ToInt16(bytes, body + 14);
            }
            else if (id == "data" && bits == 16 && channels is 1 or 2)
            {
                size = Math.Min(size, bytes.Length - body);
                var data = new byte[size];
                Array.Copy(bytes, body, data, 0, size);
                var frames = size / (2 * channels);
                return new AudioStreamWav
                {
                    Format = AudioStreamWav.FormatEnum.Format16Bits,
                    MixRate = rate,
                    Stereo = channels == 2,
                    Data = data,
                    LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
                    LoopBegin = 0,
                    LoopEnd = loop ? frames : 0,
                };
            }
            at = body + size + (size & 1);
        }
        GD.PushWarning($"Sound {path} is not a 16-bit WAV");
        return null;
    }

    // ------------------------------------------------------------------ settings

    private static void Apply()
    {
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Math.Max(_master, 1e-4f)));
        AudioServer.SetBusMute(0, _muted || _master <= 0);
        foreach (Channel channel in Enum.GetValues(typeof(Channel)))
        {
            var index = AudioServer.GetBusIndex(Bus(channel));
            if (index < 0)
                continue;
            var v = Volumes[(int)channel];
            AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Math.Max(v, 1e-4f)));
            AudioServer.SetBusMute(index, v <= 0);
        }
    }

    private static void Load()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsPath) == Error.Ok)
        {
            _master = (float)config.GetValue("sound", "master", _master);
            _muted = (bool)config.GetValue("sound", "muted", _muted);
            foreach (Channel channel in Enum.GetValues(typeof(Channel)))
                Volumes[(int)channel] = (float)config.GetValue("sound", channel.ToString().ToLowerInvariant(), Volumes[(int)channel]);
        }
        Apply();
    }

    public static void Save()
    {
        var config = new ConfigFile();
        config.Load(SettingsPath);
        config.SetValue("sound", "master", _master);
        config.SetValue("sound", "muted", _muted);
        foreach (Channel channel in Enum.GetValues(typeof(Channel)))
            config.SetValue("sound", channel.ToString().ToLowerInvariant(), Volumes[(int)channel]);
        config.Save(SettingsPath);
    }
}
