#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "pillow"]
# ///
"""
Derive a PBR map set from a single AI-generated color texture.

Image generators only produce a color image. This script turns it into:
  <name>_basecolor.png   (sRGB)      optional de-lighting + albedo range clamp
  <name>_height.png      (Non-Color) high-passed luminance, 16-bit
  <name>_normal.png      (Non-Color) OpenGL / Y+ (Blender) by default, --directx flips green
  <name>_roughness.png   (Non-Color)
  <name>_ao.png          (Non-Color) cavity-style ambient occlusion
  <name>_metallic.png    (Non-Color) only when --metallic-map is given
  <name>_orm.png         (Non-Color) only with --orm: R=AO, G=Roughness, B=Metallic

All filtering wraps around the borders, so tileable inputs stay tileable.

Usage:
  uv run derive_pbr_maps.py textures/oak_raw.png --out-dir textures --name oak \
      [--make-tileable] [--delight] [--normal-strength 4] [--roughness-base 0.6] \
      [--roughness-range 0.25] [--invert-height] [--metallic-map 0.0] [--orm] [--directx]
"""

import argparse
import os

import numpy as np
from PIL import Image


def load_rgb(path):
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0


def save(arr, path, bits=8):
    arr = np.clip(arr, 0.0, 1.0)
    if bits == 16 and arr.ndim == 2:
        Image.fromarray((arr * 65535.0 + 0.5).astype(np.uint16)).save(path)
    else:
        Image.fromarray((arr * 255.0 + 0.5).astype(np.uint8)).save(path)
    print("wrote", path)


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(np.maximum(c, 0), 1 / 2.4) - 0.055)


def blur_wrap(arr, sigma):
    """Gaussian blur that wraps around the edges (FFT convolution is inherently periodic)."""
    if sigma <= 0:
        return arr
    h, w = arr.shape[:2]
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.rfftfreq(w)[None, :]
    kernel = np.exp(-2.0 * (np.pi * sigma) ** 2 * (fy ** 2 + fx ** 2))
    if arr.ndim == 2:
        return np.fft.irfft2(np.fft.rfft2(arr) * kernel, s=(h, w)).astype(np.float32)
    return np.stack([blur_wrap(arr[..., i], sigma) for i in range(arr.shape[2])], axis=-1)


def normalize(a):
    lo, hi = np.percentile(a, 1), np.percentile(a, 99)
    return np.clip((a - lo) / max(hi - lo, 1e-6), 0, 1)


def make_tileable(rgb):
    """Cross-fade the image with a half-offset copy so the borders wrap seamlessly."""
    h, w = rgb.shape[:2]
    shifted = np.roll(rgb, (h // 2, w // 2), axis=(0, 1))
    y = 1.0 - np.abs(np.linspace(-1, 1, h))
    x = 1.0 - np.abs(np.linspace(-1, 1, w))
    weight = np.clip(np.minimum.outer(y, x) * 3.0, 0, 1)[..., None]
    return rgb * weight + shifted * (1.0 - weight)


def delight(rgb, radius_frac=0.08):
    """Remove large-scale lighting gradients by dividing out low-frequency luminance."""
    lin = srgb_to_linear(rgb)
    lum = lin @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    low = blur_wrap(lum, max(rgb.shape[:2]) * radius_frac)
    flat = lin * (lum.mean() / np.maximum(low, 1e-4))[..., None]
    return linear_to_srgb(np.clip(flat, 0, 1))


def clamp_albedo(rgb, lo=30 / 255, hi=240 / 255):
    """Keep base color inside the PBR-plausible sRGB range (no pure black/white)."""
    return lo + rgb * (hi - lo)


def height_from(rgb, detail_radius, invert):
    lum = srgb_to_linear(rgb) @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    high = lum - blur_wrap(lum, detail_radius)  # keep surface detail, drop shading
    h = normalize(blur_wrap(high, 0.6))
    return 1.0 - h if invert else h


def normal_from(height, strength, directx):
    # Sobel on a wrapped height field.
    def sh(dy, dx):
        return np.roll(height, (dy, dx), axis=(0, 1))
    gx = (sh(-1, -1) + 2 * sh(0, -1) + sh(1, -1)) - (sh(-1, 1) + 2 * sh(0, 1) + sh(1, 1))
    gy = (sh(-1, -1) + 2 * sh(-1, 0) + sh(-1, 1)) - (sh(1, -1) + 2 * sh(1, 0) + sh(1, 1))
    nx, ny, nz = -gx * strength, gy * strength, np.ones_like(height)
    if directx:
        ny = -ny
    n = np.stack([nx, ny, nz], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def ao_from(height, radius):
    cavity = blur_wrap(height, radius) - height
    return np.clip(1.0 - normalize(np.maximum(cavity, 0)) * 0.6, 0, 1)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src")
    ap.add_argument("--out-dir", default=".")
    ap.add_argument("--name", default=None)
    ap.add_argument("--make-tileable", action="store_true")
    ap.add_argument("--delight", action="store_true", help="Flatten baked lighting in the source.")
    ap.add_argument("--no-clamp", action="store_true", help="Skip the 30-240 sRGB albedo clamp.")
    ap.add_argument("--normal-strength", type=float, default=4.0)
    ap.add_argument("--detail-radius", type=float, default=None, help="High-pass radius px (default 3%% of size).")
    ap.add_argument("--invert-height", action="store_true", help="Use when dark = raised (e.g. bright mortar).")
    ap.add_argument("--roughness-base", type=float, default=0.6)
    ap.add_argument("--roughness-range", type=float, default=0.25, help="+/- variation driven by cavities.")
    ap.add_argument("--metallic-map", type=float, default=None, help="Write a flat metallic map with this value.")
    ap.add_argument("--orm", action="store_true", help="Also write a packed AO/Roughness/Metallic map.")
    ap.add_argument("--directx", action="store_true", help="DirectX (Y-) normal map instead of OpenGL.")
    args = ap.parse_args()

    name = args.name or os.path.splitext(os.path.basename(args.src))[0]
    os.makedirs(args.out_dir, exist_ok=True)
    p = lambda suffix: os.path.join(args.out_dir, "{}_{}.png".format(name, suffix))

    rgb = load_rgb(args.src)
    if args.make_tileable:
        rgb = make_tileable(rgb)
    if args.delight:
        rgb = delight(rgb)
    base = rgb if args.no_clamp else clamp_albedo(rgb)
    save(base, p("basecolor"))

    radius = args.detail_radius or max(rgb.shape[:2]) * 0.03
    height = height_from(rgb, radius, args.invert_height)
    save(height, p("height"), bits=16)
    save(normal_from(height, args.normal_strength, args.directx), p("normal"))

    ao = ao_from(height, radius * 0.5)
    save(ao, p("ao"))

    # Cavities (low height) collect dirt and read rougher; raised areas get worn smoother.
    rough = args.roughness_base + (0.5 - height) * 2.0 * args.roughness_range
    rough = np.clip(rough, 0.04, 1.0)
    save(rough, p("roughness"))

    metal = None
    if args.metallic_map is not None:
        metal = np.full_like(height, args.metallic_map)
        save(metal, p("metallic"))
    if args.orm:
        m = metal if metal is not None else np.zeros_like(height)
        save(np.stack([ao, rough, m], axis=-1), p("orm"))


if __name__ == "__main__":
    main()
