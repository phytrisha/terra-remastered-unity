# Procedural one-shot for the tiger's portal transition (44.1 kHz mono 16-bit, deterministic).
#   uv run --with numpy --with scipy sources/audio/gen_portal_sfx.py
# Writes UnityProject/Assets/Art/Audio/SFX/A_PortalWhoosh.wav, played at takeoff by TigerPortalFx (real time, not slowed):
#   0.00-0.45 s  rising filtered-noise whoosh (the leap)
#   0.32 s       low boom + bright crack (the swap: the rim passes the tiger ~0.3 s after takeoff in bullet time)
#   0.32-2.6 s   shimmering partials with tremolo, decaying (the HD tiger materializing)
import os, wave
import numpy as np
from scipy.signal import butter, sosfilt

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "UnityProject", "Assets", "Art", "Audio", "SFX")
DUR, SWAP = 2.8, 0.32
rng = np.random.default_rng(11)
t = np.arange(int(DUR * SR)) / SR


def bandpass(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], btype="band", fs=SR, output="sos"), x)


# whoosh: noise through a band sweeping up, in short blocks (time-varying filter), swelling then cut at the swap
noise = rng.standard_normal(len(t))
whoosh = np.zeros_like(t); blk = 1024
for i in range(0, len(t), blk):
    u = min(1.0, (i / SR) / SWAP)
    fc = 250 + 2800 * u ** 1.6
    seg = noise[max(0, i - 4096):i + blk]
    whoosh[i:i + blk] = bandpass(seg, fc * 0.6, min(fc * 1.6, SR / 2 - 100))[-len(whoosh[i:i + blk]):]
env_w = np.clip(t / SWAP, 0, 1) ** 2.2 * np.exp(-np.clip(t - SWAP, 0, None) / 0.12)
whoosh *= env_w

# swap: sub boom (pitch drop) + bright crack
ts = np.clip(t - SWAP, 0, None); on = (t >= SWAP).astype(float)
boom = np.sin(2 * np.pi * (38 * ts + 30 * (1 - np.exp(-ts / 0.08)) * 0.08)) * np.exp(-ts / 0.35) * on
crack = bandpass(noise, 3000, 9000) * np.exp(-ts / 0.03) * on

# shimmer: inharmonic bell partials with tremolo, slightly detuned pairs
shimmer = np.zeros_like(t)
for f, a in ((1318.5, 1.0), (1975.5, 0.7), (2637.0, 0.5), (3520.0, 0.35), (4186.0, 0.25)):
    for det in (-1.5, 1.5):
        shimmer += a * np.sin(2 * np.pi * (f + det) * t + rng.uniform(0, 6.28))
shimmer *= (0.6 + 0.4 * np.sin(2 * np.pi * 9 * t)) * np.exp(-ts / 0.7) * np.clip(ts / 0.05, 0, 1) * on

mix = 0.55 * whoosh / (np.abs(whoosh).max() + 1e-9) + 0.9 * boom + 0.25 * crack / (np.abs(crack).max() + 1e-9) \
      + 0.22 * shimmer / (np.abs(shimmer).max() + 1e-9)
mix *= np.clip((DUR - t) / 0.2, 0, 1)                       # no click at the end
mix = mix / np.abs(mix).max() * 0.8

os.makedirs(OUT, exist_ok=True)
fn = os.path.join(OUT, "A_PortalWhoosh.wav")
with wave.open(fn, "wb") as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((mix * 32767).astype(np.int16).tobytes())
print("wrote", fn, f"{DUR:.1f} s, peak {np.abs(mix).max():.2f}")
