"""Make every sound in the game: game/sounds/*.wav.

Nothing is recorded or downloaded. Each sound is built here from sine waves, pulses and
filtered noise, so the game owns all of its audio outright and there is nothing to credit.

Engines are loops of exhaust pulses: an engine with n cylinders fires n times every two turns
of the crank, each pulse rings the exhaust pipe, and the uneven strength of the cylinders gives
the beat that makes a straight-eight sound different from a V16. Each engine has a loop at low
revs and one at high revs (the game cross-fades and pitches them to the car's revs) and one off
the throttle, crackling on the overrun. Loops are built so they repeat without a seam.

Needs only numpy.  Usage, from the repo root:
    python3 tools/sounds/synth.py           # all sounds
    python3 tools/sounds/synth.py crowd     # the ones whose names start with this
"""
import math, os, sys, wave

import numpy as np

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT = os.path.join(ROOT, "game", "sounds")
SR = 44100
rng = np.random.default_rng(1930)


# ------------------------------------------------------------------ helpers

def save(name, x, sr=SR, peak=0.9):
    x = np.asarray(x, dtype=np.float64)
    top = np.max(np.abs(x))
    if top > 0:
        x = x / top * peak
    data = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(data.tobytes())
    print(f"{name}.wav  {len(x) / sr:5.2f} s")


def shape(x, gain):
    """Filters a loop by a gain curve over frequency, wrapping round so the loop stays seamless."""
    spectrum = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR_NOW[0])
    return np.fft.irfft(spectrum * gain(f), len(x))


SR_NOW = [SR]


def peak(f, centre, width, height=1.0):
    return height / (1 + ((f - centre) / width) ** 2)


def lowpass(f, cut, order=2):
    return 1 / (1 + (f / cut) ** (2 * order)) ** 0.5


def highpass(f, cut, order=2):
    return (f / cut) ** order / (1 + (f / cut) ** (2 * order)) ** 0.5


def noise(n):
    return rng.standard_normal(n)


def loop_crossfade(x, fade):
    """Makes the end of x flow into its start: the last `fade` samples are blended over the first."""
    body, tail = x[:-fade].copy(), x[-fade:]
    t = np.linspace(0, 1, fade)
    body[:fade] = body[:fade] * np.sqrt(t) + tail * np.sqrt(1 - t)
    return body


def env(n, attack, decay, sr=SR):
    t = np.arange(n) / sr
    return np.minimum(1, t / max(attack, 1e-4)) * np.exp(-t / decay)


def place(out, sound, at):
    """Adds a sound into out from sample `at`, cut off at the end."""
    at = int(at)
    if at >= len(out):
        return
    end = min(len(out), at + len(sound))
    out[at:end] += sound[: end - at]


# ------------------------------------------------------------------ engines

# cylinders, how strongly each pipe resonance rings, and how rough each cylinder is.
ENGINES = {
    # Indianapolis four: few hard pulses, a bark.
    "four": dict(cyl=4, pipe=[(150, 50, 1.0), (420, 120, 0.7), (1100, 400, 0.35)], rough=0.18, rasp=2.2),
    # Small six (Masarelli 6CM, Alfa 6C): smooth, a little thin.
    "six": dict(cyl=6, pipe=[(180, 60, 1.0), (520, 160, 0.6), (1500, 500, 0.3)], rough=0.12, rasp=1.8),
    # The straight-eight of almost every Grand Prix car of the decade: a hard, even rasp.
    "eight": dict(cyl=8, pipe=[(210, 70, 1.0), (640, 200, 0.75), (1800, 600, 0.4)], rough=0.10, rasp=2.0),
    # V12s (W154, Alfa 12C): smooth and high, a scream at the top.
    "v12": dict(cyl=12, pipe=[(260, 90, 0.8), (900, 260, 0.9), (2400, 700, 0.45)], rough=0.07, rasp=1.6),
    # The Auto Unitas V16: a deep, soft thunder, two banks a little out of step.
    "v16": dict(cyl=16, pipe=[(120, 45, 1.0), (380, 120, 0.8), (1000, 350, 0.3)], rough=0.08, rasp=1.4),
}

LOW_RPM, HIGH_RPM, OFF_RPM = 2400, 5250, 3500  # each gives a whole number of samples per cycle


def engine_loop(spec, rpm, cycles, load):
    """Exhaust pulses for `cycles` turns of the four-stroke cycle, filtered round a loop."""
    per_cycle = SR * 120 // rpm
    n = per_cycle * cycles
    cyl = spec["cyl"]
    pulses = np.zeros(n)
    # Each cylinder a little different, the same every cycle: that is the engine's beat.
    strength = 1 + spec["rough"] * rng.standard_normal(cyl)
    offset = 0.012 * rng.standard_normal(cyl)
    pulse_len = int(SR * 0.004)
    t = np.arange(pulse_len) / SR
    pulse = (t / 0.0005) * np.exp(1 - t / 0.0005)
    burst_len = int(SR * 0.006)
    for c in range(cycles):
        for k in range(cyl):
            at = (c + (k + offset[k]) / cyl) * per_cycle
            amp = strength[k] * (1 + 0.08 * rng.standard_normal())
            if load < 1:
                # Off the throttle the mixture misfires and the pulses come weak and uneven.
                amp *= load * (0.4 + 1.2 * rng.random() ** 2)
                if rng.random() < 0.035:
                    amp *= 5  # a pop in the pipe
            i = int(at) % n
            idx = (i + np.arange(pulse_len)) % n
            pulses[idx] += amp * pulse
            idx = (i + np.arange(burst_len)) % n
            pulses[idx] += amp * 0.35 * noise(burst_len) * np.exp(-np.arange(burst_len) / (SR * 0.0015))

    def pipe(f):
        g = sum(peak(f, c * rpm / 4000, w, h) for c, w, h in spec["pipe"])
        g = g + 0.25 * lowpass(f, 3000)
        g = g * highpass(f, 35)
        if load < 1:
            g = g * lowpass(f, 1600)
        return g

    x = shape(pulses, pipe)
    x /= np.std(x) + 1e-9
    x = np.tanh(x * spec["rasp"] / 2)  # the open pipe overdriven: rasp
    # Valve gear and gears: a fine clatter at the firing rate.
    firing = cyl * rpm / 120
    tt = np.arange(n) / SR
    clatter = shape(noise(n), lambda f: peak(f, 4200, 1800)) * (0.6 + 0.4 * np.cos(2 * np.pi * firing * tt))
    x += 0.06 * clatter / (np.std(clatter) + 1e-9)
    # Heard through the air, the very top of the rasp is gone.
    return shape(x, lambda f: lowpass(f, 4500, 1))


def supercharger(rpm=4000, seconds=1.0):
    """Roots blower whine: a high tone with its harmonics and rushing air, seamless."""
    n = int(SR * seconds)
    f = np.fft.rfftfreq(n, 1 / SR)
    spectrum = np.zeros(len(f), dtype=complex)
    base = 1150 * rpm / 4000
    for h, a in [(1, 1.0), (2, 0.45), (3, 0.2), (4, 0.08)]:
        b = int(round(base * h * n / SR))
        # A narrow cluster of bins, not a single one: the whine wavers.
        for j in range(-3, 4):
            spectrum[b + j] = a * math.exp(-abs(j) * 0.8) * n * np.exp(2j * np.pi * rng.random())
    x = np.fft.irfft(spectrum, n)
    air = shape(noise(n), lambda f: peak(f, base, 250) + 0.3 * peak(f, base * 2, 500))
    x = x / np.std(x) + 0.5 * air / np.std(air)
    return x


def engines():
    for name, spec in ENGINES.items():
        save(f"engine-{name}-low", engine_loop(spec, LOW_RPM, 20, 1))
        save(f"engine-{name}-high", engine_loop(spec, HIGH_RPM, 44, 1))
        save(f"engine-{name}-off", engine_loop(spec, OFF_RPM, 29, 0.35))
    save("supercharger", supercharger())


def field():
    """Far-off cars all round the circuit: a rumble that swells and fades, seamless over 8 s."""
    n = SR * 8
    tt = np.arange(n) / SR
    x = shape(noise(n), lambda f: peak(f, 160, 70) + 0.7 * peak(f, 330, 120) + 0.3 * peak(f, 700, 300))
    x /= np.std(x)
    swell = 0.7 + 0.2 * np.sin(2 * np.pi * tt / 8) + 0.1 * np.sin(2 * np.pi * 3 * tt / 8 + 1)
    # A few distant engines rising and falling through their gears.
    whine = np.zeros(n)
    for k in range(6):
        f0 = 180 + 90 * rng.random()
        sweep = f0 * (1 + 0.25 * np.sin(2 * np.pi * (k + 1) * tt / 8 + rng.random() * 6))
        phase = 2 * np.pi * np.cumsum(sweep) / SR
        whine += (0.3 + 0.7 * rng.random()) * (np.sin(phase) + 0.4 * np.sin(2 * phase))
    whine = shape(whine, lambda f: lowpass(f, 900))
    save("field", x * swell + 0.35 * whine / np.std(whine))


# ------------------------------------------------------------------ the crowd and the circuit

def voices(n, sr, count, shout=0.0, rate=3.5):
    """Many people talking at once: buzzing vocal tones through vowel formants, in syllables."""
    tt = np.arange(n) / sr
    out = np.zeros(n)
    vowels = [(730, 1090), (530, 1840), (270, 2290), (570, 840), (440, 1020), (660, 1720)]
    for _ in range(count):
        f0 = (95 + 140 * rng.random()) * (1 + 0.6 * shout)
        f0t = f0 * (1 + 0.05 * np.sin(2 * np.pi * (0.5 + rng.random()) * tt + rng.random() * 6))
        phase = 2 * np.pi * np.cumsum(f0t) / sr
        f1, f2 = vowels[rng.integers(len(vowels))]
        tone = np.zeros(n)
        for h in range(1, int(3000 / f0)):
            fh = h * f0
            a = peak(fh, f1, 90) + 0.6 * peak(fh, f2, 140)
            tone += a / h ** 0.5 * np.sin(h * phase)
        syl = np.zeros(n)
        t = 0.0
        while t < n / sr:
            dur = 0.08 + 0.18 * rng.random()
            i0, i1 = int(t * sr), int((t + dur) * sr)
            seg = np.sin(np.linspace(0, np.pi, max(1, min(i1, n) - i0))) ** 2
            syl[i0:i0 + len(seg)] = seg * (0.5 + rng.random())
            t += dur + rng.exponential(1 / rate)
        out += tone * syl
    return out


def crowd():
    sr = SR_NOW[0] = 22050
    n = sr * 9
    x = voices(n + sr, sr, 70)
    hum = shape(noise(n + sr), lambda f: peak(f, 400, 300) + 0.4 * peak(f, 1200, 600))
    x = x / np.std(x) + 0.6 * hum / np.std(hum)
    x = shape(x, lambda f: highpass(f, 150) * lowpass(f, 3500))
    save("crowd", loop_crossfade(x, sr), sr)

    # A cheer: voices rise to shouts, hands clap, it dies away.
    n = int(sr * 4.5)
    tt = np.arange(n) / sr
    x = voices(n, sr, 90, shout=0.5, rate=6) / 40
    claps = np.zeros(n)
    clap = noise(int(sr * 0.02)) * np.exp(-np.arange(int(sr * 0.02)) / (sr * 0.004))
    for _ in range(2500):
        place(claps, clap * (0.5 + rng.random()), rng.random() * n)
    claps = shape(claps, lambda f: peak(f, 1800, 1200))
    x = x / np.std(x) + 0.8 * claps / np.std(claps)
    swell = np.minimum(1, tt / 0.5) * np.exp(-np.maximum(0, tt - 1.6) / 1.1)
    save("cheer", shape(x * swell, lambda f: highpass(f, 180) * lowpass(f, 4500)), sr)
    SR_NOW[0] = SR


def rain():
    sr = SR_NOW[0] = 22050
    n = sr * 7
    x = shape(noise(n + sr), lambda f: highpass(f, 900) * lowpass(f, 7000))
    drops = np.zeros(n + sr)
    for _ in range(6000):
        d = int(sr * (0.002 + 0.006 * rng.random()))
        f = 1500 + 3500 * rng.random()
        t = np.arange(d) / sr
        place(drops, np.sin(2 * np.pi * f * t) * np.exp(-t / 0.0015) * rng.random(), rng.random() * (n + sr))
    x = 0.6 * x / np.std(x) + drops / np.std(drops)
    save("rain", loop_crossfade(x, sr), sr)
    SR_NOW[0] = SR


def night():
    """Crickets in the fields round the Sarthe at night."""
    sr = SR_NOW[0] = 22050
    n = sr * 6
    out = np.zeros(n + sr)
    for _ in range(9):
        f = 3800 + 1200 * rng.random()
        chirp_len = int(sr * 0.012)
        t = np.arange(chirp_len) / sr
        chirp = np.sin(2 * np.pi * f * t) * np.sin(np.pi * t / t[-1]) ** 2
        period = 0.45 + 0.4 * rng.random()
        start = rng.random() * period
        while start < (n + sr) / sr:
            for k in range(3):
                place(out, chirp * (0.3 + 0.7 * rng.random()), (start + k * 0.03) * sr)
            start += period * (0.9 + 0.2 * rng.random())
    air = shape(noise(n + sr), lambda f: lowpass(f, 500))
    save("night", loop_crossfade(out / np.std(out) + 0.15 * air / np.std(air), sr), sr)
    SR_NOW[0] = SR


def metal(freqs, decay, n, sr=SR):
    t = np.arange(n) / sr
    return sum(a * np.sin(2 * np.pi * f * t + rng.random() * 6) * np.exp(-t / (decay * d)) for f, a, d in freqs)


def pits():
    # The copper hammer on a knock-off hub nut: a thump and a ring, three blows a wheel.
    n = int(SR * 3.2)
    out = np.zeros(n)
    ring = [(780, 1.0, 1.0), (1930, 0.6, 0.6), (3150, 0.4, 0.4), (4720, 0.25, 0.25)]
    for wheel, at in enumerate([0.15, 1.75]):
        for k in range(3):
            blow_n = int(SR * 0.5)
            thump = noise(blow_n) * np.exp(-np.arange(blow_n) / (SR * 0.004))
            blow = 0.8 * thump + metal(ring, 0.07 * (1 + 0.2 * rng.random()), blow_n)
            place(out, blow * (1 - 0.15 * k), (at + k * 0.32 + 0.03 * rng.random()) * SR)
        # The wheel off and on: a scrape and a clunk.
        scrape_n = int(SR * 0.4)
        scrape = shape(noise(scrape_n), lambda f: peak(f, 900, 500)) * np.sin(np.linspace(0, np.pi, scrape_n))
        place(out, 0.3 * scrape / np.std(scrape), (at + 1.05) * SR)
    save("pit-wheel", shape(out, lambda f: highpass(f, 60)))

    # Fuel poured from churns through a funnel: a gurgling rush.
    n = int(SR * 3.0)
    tt = np.arange(n) / SR
    rush = shape(noise(n), lambda f: peak(f, 500, 300) + 0.5 * peak(f, 1400, 600))
    gurgle = 0.6 + 0.4 * np.sin(2 * np.pi * np.cumsum(7 + 4 * np.sin(2 * np.pi * 0.7 * tt)) / SR)
    bubbles = np.zeros(n)
    for _ in range(140):
        d = int(SR * 0.03)
        t = np.arange(d) / SR
        f = 300 + 500 * rng.random()
        place(bubbles, np.sin(2 * np.pi * f * (1 + 8 * t) * t) * np.exp(-t / 0.01), rng.random() * n)
    x = rush / np.std(rush) * gurgle + 0.6 * bubbles / np.std(bubbles)
    fade = np.minimum(1, tt / 0.15) * np.minimum(1, (tt[-1] - tt) / 0.4)
    save("pit-fuel", x * fade)


def crash():
    n = int(SR * 2.2)
    tt = np.arange(n) / SR
    hit = noise(n) * np.exp(-tt / 0.08)
    crunch = metal([(310, 1, 1), (720, 0.8, 0.7), (1240, 0.6, 0.5), (2210, 0.5, 0.3), (3480, 0.3, 0.2)], 0.25, n)
    debris = np.zeros(n)
    for _ in range(40):
        bit = metal([(2000 + 4000 * rng.random(), 1, 1)], 0.03, int(SR * 0.15))
        place(debris, bit * rng.random() * 0.4, (0.1 + 1.6 * rng.random() ** 1.5) * SR)
    thud = np.sin(2 * np.pi * 55 * tt) * np.exp(-tt / 0.15)
    save("crash", shape(np.tanh(1.5 * (1.2 * hit + 0.7 * crunch + debris + 1.2 * thud)), lambda f: lowpass(f, 5000)))

    # An engine letting go: a bang, a clatter of loose metal running down, steam.
    n = int(SR * 2.5)
    tt = np.arange(n) / SR
    bang = shape(noise(n), lambda f: lowpass(f, 1500)) * np.exp(-tt / 0.05)
    clatter = np.zeros(n)
    t, gap = 0.08, 0.025
    while t < 1.8:
        place(clatter, metal([(900 + 900 * rng.random(), 1, 1), (2500, 0.5, 0.5)], 0.02, int(SR * 0.08)) * math.exp(-t / 0.9), t * SR)
        t += gap
        gap *= 1.08
    steam = shape(noise(n), lambda f: highpass(f, 2500)) * np.minimum(1, tt / 0.3) * np.exp(-tt / 1.0)
    x = 1.5 * bang / np.std(bang) + clatter / (np.std(clatter) + 1e-9) + 0.25 * steam / np.std(steam)
    save("engine-blow", shape(np.tanh(x), lambda f: lowpass(f, 6000)))


# ------------------------------------------------------------------ menus

def ui():
    # A heavy switch: a click down, a click back.
    n = int(SR * 0.09)
    out = np.zeros(n)
    for at, a, f in [(0.0, 1.0, 2400), (0.045, 0.5, 3100)]:
        k = int(SR * 0.012)
        t = np.arange(k) / SR
        place(out, a * (noise(k) * 0.5 + np.sin(2 * np.pi * f * t)) * np.exp(-t / 0.0025), at * SR)
    save("click", shape(out, lambda f: highpass(f, 400)), peak=0.7)

    # A softer tick for switches and boxes.
    k = int(SR * 0.03)
    t = np.arange(k) / SR
    save("tick", (np.sin(2 * np.pi * 1700 * t) + 0.3 * noise(k)) * np.exp(-t / 0.004), peak=0.5)

    # A page of the programme turned.
    n = int(SR * 0.45)
    tt = np.arange(n) / SR
    rustle = shape(noise(n), lambda f: peak(f, 3000, 2500) + 0.3 * peak(f, 800, 400))
    flutter = (0.5 + 0.5 * np.abs(np.sin(2 * np.pi * 9 * tt + 3 * np.sin(2 * np.pi * 2 * tt)))) ** 2
    save("page", rustle * flutter * np.sin(np.pi * tt / tt[-1]) ** 1.5, peak=0.55)


# ------------------------------------------------------------------ music

NOTES = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4, "F": 5, "F#": 6, "Gb": 6,
         "G": 7, "G#": 8, "Ab": 8, "A": 9, "A#": 10, "Bb": 10, "B": 11}


def hz(note):
    name, octave = note[:-1], int(note[-1])
    return 440 * 2 ** ((NOTES[name] + 12 * (octave + 1) - 69) / 12)


def piano(f, dur, sr):
    n = int(sr * (dur + 0.6))
    t = np.arange(n) / sr
    x = sum((0.9 ** h) / h * np.sin(2 * np.pi * f * h * (1 + 0.0004 * h * h) * t) * np.exp(-t * (1.5 + 0.9 * h))
            for h in range(1, 9))
    return x * np.minimum(1, t / 0.004) * np.where(t < dur, 1, np.exp(-(t - dur) / 0.08))


def clarinet(f, dur, sr):
    n = int(sr * (dur + 0.12))
    t = np.arange(n) / sr
    vib = 1 + 0.006 * np.sin(2 * np.pi * 5.2 * t) * np.minimum(1, t / 0.3)
    phase = 2 * np.pi * f * np.cumsum(vib) / sr
    x = sum(a * np.sin(h * phase) for h, a in [(1, 1), (3, 0.55), (5, 0.3), (7, 0.15), (2, 0.08), (9, 0.06)])
    a = np.minimum(1, t / 0.035) * np.where(t < dur, 1, np.exp(-(t - dur) / 0.04))
    return x * a


def bass(f, dur, sr):
    n = int(sr * (dur + 0.2))
    t = np.arange(n) / sr
    x = np.sin(2 * np.pi * f * t) + 0.5 * np.sin(4 * np.pi * f * t) + 0.2 * np.sin(6 * np.pi * f * t)
    return x * np.minimum(1, t / 0.01) * np.exp(-t / 0.35)


CHORDS = {
    "F": ["F3", "A3", "C4"], "F6": ["F3", "A3", "D4"], "D7": ["F#3", "A3", "C4"], "G7": ["F3", "B3", "D4"],
    "C7": ["E3", "G3", "Bb3"], "Gm": ["G3", "Bb3", "D4"], "A7": ["E3", "G3", "C#4"], "Dm": ["F3", "A3", "D4"],
    "Bb": ["F3", "Bb3", "D4"], "Bbm": ["F3", "Bb3", "Db4"],
}
ROOTS = {"F": ("F2", "C2"), "F6": ("F2", "C2"), "D7": ("D2", "A1"), "G7": ("G1", "D2"), "C7": ("C2", "G1"),
         "Gm": ("G1", "D2"), "A7": ("A1", "E2"), "Dm": ("D2", "A1"), "Bb": ("Bb1", "F2"), "Bbm": ("Bb1", "F2")}

# A foxtrot in F, AABA, two chords a bar at most. (chord per half bar)
A_HARMONY = ["F", "F6", "D7", "D7", "G7", "G7", "C7", "C7", "F", "D7", "Gm", "C7", "F", "Bb", "F", "C7"]
A2_HARMONY = ["F", "F6", "D7", "D7", "G7", "G7", "C7", "C7", "F", "D7", "Gm", "C7", "F", "Bbm", "F", "F"]
B_HARMONY = ["A7", "A7", "Dm", "Dm", "G7", "G7", "C7", "C7", "A7", "A7", "Dm", "Dm", "G7", "G7", "C7", "C7"]
# Melody: (note or None for a rest, beats). Each section is 8 bars of 4 beats.
A_TUNE = [("C5", 1.5), ("A4", 0.5), ("F4", 1), ("A4", 1), ("C5", 2), ("D5", 1), ("C5", 1),
          ("A4", 1.5), ("F#4", 0.5), ("A4", 1), ("C5", 1), ("D5", 3), (None, 1),
          ("B4", 1.5), ("A4", 0.5), ("G4", 1), ("B4", 1), ("D5", 2), ("C5", 1), ("B4", 1),
          ("Bb4", 1.5), ("G4", 0.5), ("E4", 1), ("G4", 1), ("C5", 3), (None, 1)]
A_END = [("A4", 1), ("C5", 1), ("F5", 1.5), ("E5", 0.5), ("D5", 1), ("C5", 1), ("A4", 1), ("F#4", 1),
         ("G4", 1), ("Bb4", 1), ("D5", 1), ("C5", 1), ("Bb4", 1), ("A4", 1), ("G4", 2)]
A2_END = [("A4", 1), ("C5", 1), ("F5", 1.5), ("E5", 0.5), ("D5", 1), ("C5", 1), ("A4", 1), ("F#4", 1),
          ("G4", 1), ("C5", 1), ("E4", 1), ("G4", 1), ("F4", 3), (None, 1)]
B_TUNE = [("C#5", 1), ("E5", 1), ("G5", 1.5), ("E5", 0.5), ("C#5", 1), ("A4", 1), ("D5", 2),
          ("F5", 1), ("D5", 1), ("A4", 1), ("F4", 1), ("B4", 1), ("D5", 1), ("G5", 1.5), ("F5", 0.5),
          ("D5", 1), ("B4", 1), ("C5", 1), ("E5", 1), ("G5", 2), ("E5", 1), ("C5", 1), ("Bb4", 1), ("G4", 1),
          ("C#5", 1), ("E5", 1), ("A5", 2), ("G5", 1), ("E5", 1), ("D5", 1), ("C#5", 1), ("D5", 1), ("F5", 1),
          ("A5", 1.5), ("F5", 0.5), ("D5", 1), ("A4", 1), ("G4", 1), ("B4", 1), ("D5", 1), ("F5", 1),
          ("E5", 1), ("D5", 1), ("C5", 1), ("Bb4", 1), ("A4", 1), ("G4", 1), ("E4", 2)]


def music():
    sr = SR_NOW[0] = 22050
    bpm = 132
    beat = 60 / bpm
    sections = [(A_HARMONY, A_TUNE + A_END), (A_HARMONY, A_TUNE + A_END),
                (B_HARMONY, B_TUNE), (A2_HARMONY, A_TUNE + A2_END)]
    bars = 8 * len(sections)
    n = int(sr * (bars * 4 * beat + 1.0))
    band = np.zeros(n)
    tune = np.zeros(n)
    swing = 0.06 * beat
    bar0 = 0
    for harmony, melody in sections:
        for half, chord in enumerate(harmony):
            start = (bar0 * 4 + half * 2) * beat
            root, fifth = ROOTS[chord]
            # Oom-pah: the bass on the beat, root then fifth, the chord after it.
            place(band, 0.9 * bass(hz(root if half % 2 == 0 else fifth), beat * 0.9, sr), start * sr)
            for k, note in enumerate(CHORDS[chord]):
                place(band, 0.32 * piano(hz(note), beat * 0.45, sr), (start + beat + 0.004 * k) * sr)
        t = bar0 * 4 * beat
        for note, beats in melody:
            if note:
                on = t + (swing if (t / beat) % 1 > 0.25 else 0)
                place(tune, 0.5 * clarinet(hz(note), beats * beat * 0.92, sr), on * sr)
            t += beats * beat
        bar0 += 8
    # Brushes on two and four.
    brush = np.zeros(n)
    k = int(sr * 0.18)
    swish = shape(noise(k), lambda f: peak(f, 5000, 3000)) * np.exp(-np.arange(k) / (sr * 0.05))
    for b in range(bars * 4):
        place(brush, (0.35 if b % 2 else 0.12) * swish / np.std(swish), (b * beat) * sr)
    x = band / np.std(band) + 1.1 * tune / np.std(tune) + 0.35 * brush

    # Played on a gramophone: no deep bass or high treble, a slow wow, hiss and crackle.
    tt = np.arange(n) / sr
    wow = 1 + 0.0025 * np.sin(2 * np.pi * 0.55 * tt)
    read = np.cumsum(wow) - 1
    x = np.interp(read, np.arange(n), x)
    x = shape(x, lambda f: highpass(f, 220) * lowpass(f, 3800, 3) * (1 + 0.6 * peak(f, 1500, 800)))
    x = np.tanh(1.3 * x / np.max(np.abs(x))) * 0.9
    hiss = shape(noise(n), lambda f: peak(f, 3000, 2000))
    crackle = np.zeros(n)
    for _ in range(int(bars * 4 * beat * 18)):
        d = int(sr * 0.002)
        place(crackle, noise(d) * rng.random() ** 3, rng.random() * n)
    x = x + 0.02 * hiss / np.std(hiss) + 0.25 * crackle
    x[: int(sr * 0.05)] *= np.linspace(0, 1, int(sr * 0.05))
    x[-int(sr * 0.8):] *= np.linspace(1, 0, int(sr * 0.8))
    save("music-foxtrot", x, sr, peak=0.8)
    SR_NOW[0] = SR


ALL = {"engine": engines, "field": field, "crowd": crowd, "rain": rain, "night": night, "pit": pits,
       "crash": crash, "ui": ui, "music": music}

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    want = sys.argv[1] if len(sys.argv) > 1 else ""
    for name, make in ALL.items():
        if name.startswith(want) or want.startswith(name):
            make()
