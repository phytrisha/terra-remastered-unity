# Procedural placeholder ambience for the rainforest (seamless loops, 44.1 kHz mono 16-bit).
#   uv run --with numpy --with scipy sources/audio/gen_audio.py
# Writes UnityProject/Assets/Art/Audio/Ambience/A_StreamLoop.wav and A_JungleLoop.wav. Deterministic (fixed seeds).
import os, wave
import numpy as np
from scipy.signal import butter, sosfilt

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "UnityProject", "Assets", "Art", "Audio", "Ambience")


def bandpass(x, lo, hi, order=4):
    return sosfilt(butter(order, [lo, hi], btype="band", fs=SR, output="sos"), x)


def lowpass(x, f, order=4):
    return sosfilt(butter(order, f, btype="low", fs=SR, output="sos"), x)


def loopify(x, fade_s=1.5):
    """Crossfade the tail into the head so the clip loops without a click."""
    n = int(fade_s * SR)
    head, tail = x[:n].copy(), x[-n:]
    t = np.linspace(0, 1, n)
    x = x[:-n].copy()
    x[:n] = head * np.sqrt(t) + tail * np.sqrt(1 - t)
    return x


def write(name, x, peak=0.8):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    os.makedirs(OUT, exist_ok=True)
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())
    print(name, f"{len(x) / SR:.1f}s")


def stream(seconds=24.0, seed=3):
    rng = np.random.default_rng(seed)
    n = int((seconds + 1.5) * SR)
    white = rng.standard_normal(n)
    body = bandpass(white, 180, 2400) * 0.6 + bandpass(white, 2400, 7000) * 0.25
    # babble: many short bubbling resonances
    t = np.arange(n) / SR
    mod = 0.65 + 0.35 * lowpass(rng.standard_normal(n), 3.0, 2) / 0.05
    mod = np.clip(mod, 0.3, 1.2)
    bub = np.zeros(n)
    for _ in range(int(seconds * 55)):
        s = rng.integers(0, n - 12000); f = rng.uniform(300, 1400); d = rng.uniform(0.01, 0.05)
        k = np.arange(int(d * SR * 4)); env = np.exp(-k / (d * SR))
        bub[s:s + len(k)] += np.sin(2 * np.pi * (f + 900 * k / len(k)) * k / SR) * env * rng.uniform(0.05, 0.2)
    return loopify(body * mod + bub)


def jungle(seconds=32.0, seed=7):
    rng = np.random.default_rng(seed)
    n = int((seconds + 1.5) * SR)
    t = np.arange(n) / SR
    wind = lowpass(rng.standard_normal(n), 500) * (0.5 + 0.5 * np.sin(2 * np.pi * t / 11.0) ** 2) * 0.5
    insects = bandpass(rng.standard_normal(n), 4200, 5200, 2) * (0.6 + 0.4 * np.sin(2 * np.pi * 7.5 * t) ** 8) * 0.25
    birds = np.zeros(n)
    for _ in range(int(seconds * 0.9)):                       # chirp phrases
        s = rng.integers(0, n - SR); f0 = rng.uniform(1800, 4200); notes = rng.integers(2, 6)
        for k in range(notes):
            d = rng.uniform(0.05, 0.14); m = int(d * SR); kk = np.arange(m)
            sweep = f0 * (1 + rng.uniform(-0.3, 0.4) * kk / m)
            env = np.sin(np.pi * kk / m) ** 2
            seg = np.sin(2 * np.pi * np.cumsum(sweep) / SR) * env * rng.uniform(0.15, 0.35)
            o = s + int(k * rng.uniform(0.09, 0.2) * SR)
            if o + m < n: birds[o:o + m] += seg
    for _ in range(int(seconds * 0.15)):                      # distant low calls
        s = rng.integers(0, n - 2 * SR); d = rng.uniform(0.25, 0.5); m = int(d * SR); kk = np.arange(m)
        f = rng.uniform(450, 750) * (1 - 0.2 * kk / m)
        birds[s:s + m] += np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * kk / m) * 0.12
    return loopify(wind + insects + birds)


if __name__ == "__main__":
    write("A_StreamLoop.wav", stream())
    write("A_JungleLoop.wav", jungle(), peak=0.6)
