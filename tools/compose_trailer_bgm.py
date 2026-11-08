"""
Original background music for the Professor Quack trailer (no samples, no third-party audio).
A playful "quirky corporate" theme: pizzicato strings (Karplus-Strong), glockenspiel melody, bouncy bass,
light percussion, and a triumphant string swell for the board-meeting finale. 115 BPM, C major, 76 s.

Sections follow the trailer's cuts (seconds): title 0-4, TV story 4-12, egg cloud + dials 12-33,
bands / landscape / gradient descent 33-54, whiteboard 54-59, board meeting 59-71, congratulations 71-76.

Run with any Python that has numpy (e.g. Blender's bundled Python):
  python3 tools/compose_trailer_bgm.py out.wav
"""
import sys, wave
import numpy as np

SR = 44100
BPM = 115.0
BEAT = 60.0 / BPM
BAR = 4 * BEAT
LENGTH = 76.0
N = int(SR * LENGTH)
L = np.zeros(N); R = np.zeros(N)
rng = np.random.default_rng(7)


def hz(m): return 440.0 * 2 ** ((m - 69) / 12.0)


def add(sig, t0, gain=1.0, pan=0.0):
    i = int(t0 * SR)
    if i >= N or i + len(sig) <= 0: return
    s = sig[: max(0, N - i)]
    lg, rg = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
    L[i:i + len(s)] += s * gain * lg * 1.414
    R[i:i + len(s)] += s * gain * rg * 1.414


def pluck(m, dur, bright=0.5):
    """Karplus-Strong pizzicato: a noise burst through a damped delay line, computed block by block."""
    f = hz(m); d = max(2, int(round(SR / f)))
    n = int(SR * dur)
    y = np.zeros(n + d + 1)
    burst = rng.uniform(-1, 1, d)
    burst = np.convolve(burst, np.ones(3) / 3, mode="same") * (0.6 + bright * 0.4)
    y[:d] = burst
    decay = 0.996 - 0.004 * (m - 48) / 36
    i = d
    while i < n + d:
        j = min(i + d, n + d)
        y[i:j] = decay * 0.5 * (y[i - d:j - d] + y[i - d - 1:j - d - 1] if i - d - 1 >= 0 else y[i - d:j - d])
        i = j
    out = y[d:d + n]
    env = np.minimum(1, np.arange(n) / (0.002 * SR)) * np.exp(-np.arange(n) / SR * 5.5)
    return out * env


def glock(m, dur=1.2):
    t = np.arange(int(SR * dur)) / SR
    f = hz(m)
    parts = [(1, 1.0, 3.0), (2.76, 0.35, 7.0), (5.40, 0.15, 12.0), (8.93, 0.06, 18.0)]
    s = sum(a * np.sin(2 * np.pi * f * k * t) * np.exp(-t * dk) for k, a, dk in parts)
    return s * np.minimum(1, t / 0.001)


def bass(m, dur):
    t = np.arange(int(SR * dur)) / SR
    f = hz(m)
    s = np.sin(2 * np.pi * f * t) + 0.35 * np.sin(4 * np.pi * f * t) + 0.12 * np.sin(6 * np.pi * f * t)
    return s * np.minimum(1, t / 0.004) * np.exp(-t * 4.5)


def kick():
    t = np.arange(int(SR * 0.25)) / SR
    f = 45 + 80 * np.exp(-t * 30)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 14)


def shaker():
    n = int(SR * 0.06)
    x = rng.uniform(-1, 1, n); x = np.diff(np.concatenate([[0], x]))
    return x * np.exp(-np.arange(n) / SR * 70)


def pad(ms, dur, attack=1.5):
    """Warm string pad: detuned band-limited saws, slow attack."""
    t = np.arange(int(SR * dur)) / SR
    s = np.zeros_like(t)
    for m in ms:
        for det in (-0.07, 0.0, 0.08):
            f = hz(m + det)
            for k in range(1, 10):
                s += np.sin(2 * np.pi * f * k * t + k) / k * (0.85 ** k)
    s /= len(ms) * 3
    env = np.minimum(1, t / attack) * np.minimum(1, (dur - t) / 0.4)
    return s * env


def timpani(m):
    t = np.arange(int(SR * 1.2)) / SR
    f = hz(m) * (1 + 0.15 * np.exp(-t * 20))
    return (np.sin(2 * np.pi * np.cumsum(f) / SR) * 0.9 + rng.uniform(-1, 1, len(t)) * 0.15 * np.exp(-t * 25)) * np.exp(-t * 3)


def cymbal(dur, swell=False):
    n = int(SR * dur); t = np.arange(n) / SR
    x = rng.uniform(-1, 1, n); x = np.diff(np.concatenate([[0], x]))
    env = (t / dur) ** 2 if swell else np.exp(-t * 1.8)
    return x * env


CH = {"C": [48, 52, 55], "Am": [45, 48, 52], "F": [41, 45, 48], "G": [43, 47, 50], "Em": [40, 43, 47], "Dm": [38, 41, 45]}
ROOT = {"C": 36, "Am": 33, "F": 29, "G": 31, "Em": 28, "Dm": 26}
A = ["C", "Am", "F", "G"]
B = ["F", "G", "Em", "Am"]

# melody: (beat in bar, length in beats, midi), four bars
MEL = [
    [(0, .5, 76), (.5, .5, 79), (1, .5, 81), (1.5, .5, 79), (2, 1, 76), (3, 1, 72)],
    [(0, .5, 72), (.5, .5, 74), (1, .5, 76), (1.5, .5, 74), (2, 2, 69)],
    [(0, .5, 77), (.5, .5, 76), (1, .5, 74), (1.5, .5, 72), (2, 1, 74), (3, 1, 77)],
    [(0, 1.5, 79), (1.5, .5, 77), (2, 1, 74), (3, 1, 71)],
]
MEL_B = [
    [(0, 1, 81), (1, .5, 79), (1.5, .5, 77), (2, 1, 76), (3, 1, 77)],
    [(0, 1, 79), (1, .5, 77), (1.5, .5, 76), (2, 2, 74)],
    [(0, .5, 76), (.5, .5, 79), (1, .5, 83), (1.5, .5, 79), (2, 1, 76), (3, 1, 79)],
    [(0, 1, 81), (1, 1, 76), (2, 2, 72)],
]


def bar_time(b): return b * BAR


def oompah(b, chord, level=1.0, offbeats=True):
    t0 = bar_time(b)
    add(bass(ROOT[chord], BEAT * 0.9), t0, 0.55 * level)
    add(bass(ROOT[chord] + 7, BEAT * 0.9), t0 + 2 * BEAT, 0.45 * level)
    for k, beat in enumerate([1, 3] + ([0.5, 1.5, 2.5, 3.5] if offbeats else [])):
        for j, m in enumerate(CH[chord]):
            add(pluck(m + 12, 0.35, 0.4), t0 + beat * BEAT + j * 0.004, 0.16 * level, pan=-0.4 if k % 2 else 0.4)


def melody(b, phrase, inst="glock", octave=0, level=1.0):
    t0 = bar_time(b)
    for beat, ln, m in phrase:
        if inst == "glock": add(glock(m + octave, max(0.6, ln * BEAT * 2)), t0 + beat * BEAT, 0.22 * level, pan=0.2)
        else: add(pluck(m + octave, max(0.3, ln * BEAT * 1.4), 0.7), t0 + beat * BEAT, 0.3 * level, pan=-0.1)


def drums(b, level=1.0, kicks=True):
    t0 = bar_time(b)
    for e in range(8):
        add(shaker(), t0 + e * BEAT / 2, (0.05 if e % 2 else 0.035) * level, pan=0.3)
    if kicks:
        add(kick(), t0, 0.5 * level); add(kick(), t0 + 2 * BEAT, 0.42 * level)


# --- arrangement (bar numbers; one bar = 2.087 s) ---
# bars 0-1: title: glockenspiel motif alone over a soft C pad
add(pad(CH["C"], 2 * BAR, 1.0), 0, 0.10)
melody(0, MEL[0]); melody(1, MEL[3], level=0.8)
# bars 2-5: the TV story: pizzicato oom-pah, no melody yet
for i, b in enumerate(range(2, 6)): oompah(b, A[i % 4], 0.8, offbeats=(b > 3))
melody(5, [(2, .5, 79), (2.5, .5, 81), (3, 1, 83)], level=0.7)
# bars 6-15: egg cloud + dials: full groove, glockenspiel melody doubled by pizzicato
for i, b in enumerate(range(6, 16)):
    oompah(b, A[i % 4]); drums(b, 0.9 if b > 7 else 0.5, kicks=b > 7)
    melody(b, MEL[i % 4]); melody(b, MEL[i % 4], "pluck", -12, 0.5)
# bars 16-25: bands, landscape, gradient descent: B section, countermelody
for i, b in enumerate(range(16, 26)):
    ch = B[i % 4] if i < 8 else ["F", "G"][i - 8]
    oompah(b, ch); drums(b, 1.0)
    melody(b, MEL_B[i % 4]); melody(b, MEL_B[i % 4], "pluck", -12, 0.45)
add(cymbal(BAR, swell=True), bar_time(25), 0.05)
# bars 26-27: whiteboard: a breather, plucks only
oompah(26, "Am", 0.6, offbeats=False); oompah(27, "Dm", 0.6, offbeats=False)
melody(27, [(2, .5, 74), (2.5, .5, 77), (3, .5, 79), (3.5, .5, 83)], "pluck", 0, 0.6)
# bars 28-33: board meeting: strings swell, timpani, the theme an octave up, triumphant progression
FIN = ["F", "G", "Am", "F", "G", "G"]
for i, b in enumerate(range(28, 34)):
    ch = FIN[i]
    add(pad([m + 12 for m in CH[ch]], BAR + 0.3, 0.6), bar_time(b), 0.10 + 0.03 * i)
    oompah(b, ch, 0.9); drums(b, 1.0)
    add(timpani(ROOT[ch] + 12), bar_time(b), 0.25)
    melody(b, MEL[i % 4], octave=12 if i > 1 else 0, level=0.9)
add(cymbal(BAR, swell=True), bar_time(33), 0.08)
for k in range(4): add(timpani(43), bar_time(33) + 2 * BEAT + k * BEAT / 2, 0.16 + 0.05 * k)
# bar 34+: congratulations: big C chord, glockenspiel arpeggio flourish, cymbal, ring out
t_end = bar_time(34)
add(pad([60, 64, 67, 72], LENGTH - t_end, 0.08), t_end, 0.28)
add(bass(36, 3.0), t_end, 0.7); add(timpani(48), t_end, 0.4); add(kick(), t_end, 0.6)
add(cymbal(4.0), t_end, 0.12)
for k, m in enumerate([72, 76, 79, 84, 88, 91, 96]): add(glock(m, 2.5), t_end + k * 0.07, 0.2)
for j, m in enumerate([60, 64, 67, 72]): add(pluck(m, 1.5, 0.8), t_end + j * 0.01, 0.25)

# --- master: gentle compression, fade in/out, normalise ---
mix = np.stack([L, R])
mix = np.tanh(mix * 1.6) / np.tanh(1.6)
fade = np.ones(N); fi = int(0.3 * SR); fo = int(2.5 * SR)
fade[:fi] = np.linspace(0, 1, fi); fade[-fo:] = np.linspace(1, 0, fo) ** 1.5
mix *= fade
mix /= np.max(np.abs(mix)) / 0.89
pcm = (mix.T * 32767).astype("<i2")
out = sys.argv[1] if len(sys.argv) > 1 else "trailer_bgm.wav"
with wave.open(out, "wb") as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
print("wrote", out, f"{LENGTH:.1f}s")
