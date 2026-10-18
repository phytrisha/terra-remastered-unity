#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "pillow", "scipy"]
# ///
"""Texture helpers for AI-generated images.

  uv run texture_tools.py seam raw1.png [raw2.png ...]
      Tiling check: mean |edge - opposite edge| vs mean |row - next row|. "edge" much larger than "inner" means the
      image does not tile: then (and only then) use derive_pbr_maps.py --make-tileable, which adds slight ghosting.

  uv run texture_tools.py key sprite_raw.png T_Leaves_BC.png [--size 1024] [--erode 1] [--tile] [--global-despill]
      Sprites generated on a solid MAGENTA background -> RGBA. Soft alpha from a magenta score, 1 px erosion,
      despill only in a 3 px band along the silhouette (a global despill turns blue/purple subjects grey; use
      --global-despill only for see-through pinkish gaps), colour dilated into the transparent area so mips and bilinear
      filtering never bleed magenta. In Unity enable mipMapsPreserveCoverage (AssetImportRules does it for T_*_BC with alpha).
"""
import argparse
import numpy as np
from PIL import Image
from scipy import ndimage as nd


def seam(path):
    a = np.asarray(Image.open(path).convert("RGB")).astype(float)
    edge = (np.abs(a[0] - a[-1]).mean() + np.abs(a[:, 0] - a[:, -1]).mean()) / 2
    inner = (np.abs(a[0] - a[1]).mean() + np.abs(a[:, 0] - a[:, 1]).mean()) / 2
    return round(edge, 1), round(inner, 1)


def key(src, dst, size, erode=1, tile=False, global_despill=False):
    im = Image.open(src).convert("RGB").resize((size, size), Image.LANCZOS)
    a = np.asarray(im).astype(float) / 255
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mag = np.clip((np.minimum(r, b) - g) * 2.2 - 0.25, 0, 1)          # 1 = pure magenta background
    alpha = 1 - nd.uniform_filter(mag, 2, mode="wrap" if tile else "nearest")
    alpha = np.clip((alpha - 0.35) / 0.3, 0, 1)
    if global_despill: alpha = np.where(np.minimum(r, b) - g > 0.18, 0, alpha)
    solid = alpha > 0.5
    if erode: solid = nd.binary_erosion(solid, iterations=erode)
    alpha = np.where(solid, alpha, 0)
    band = solid if global_despill else solid & ~nd.binary_erosion(solid, iterations=3)
    spill = np.clip(np.minimum(r, b) - g, 0, 1)
    rgb = a.copy()
    for c in (0, 2): rgb[..., c] = np.where(band, rgb[..., c] - spill * 0.9, rgb[..., c])
    idx = nd.distance_transform_edt(~solid, return_distances=False, return_indices=True)
    rgb = rgb[idx[0], idx[1]]
    Image.fromarray((np.dstack([np.clip(rgb, 0, 1), alpha[..., None]]) * 255).astype(np.uint8), "RGBA").save(dst)
    return float(solid.mean())


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("seam"); s.add_argument("imgs", nargs="+")
    k = sub.add_parser("key"); k.add_argument("src"); k.add_argument("dst"); k.add_argument("--size", type=int, default=1024)
    k.add_argument("--erode", type=int, default=1); k.add_argument("--tile", action="store_true"); k.add_argument("--global-despill", action="store_true")
    a = ap.parse_args()
    if a.cmd == "seam":
        for p in a.imgs:
            e, i = seam(p); print(f"{p}: edge {e} inner {i} -> {'TILES' if e < i * 2 + 2 else 'SEAM'}")
    else:
        print(f"{a.dst}: coverage {key(a.src, a.dst, a.size, a.erode, a.tile, a.global_despill):.2f}")
