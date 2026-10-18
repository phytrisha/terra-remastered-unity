#!/usr/bin/env python3
"""Recorded body sweep of the tiger -> footprint grid for RainforestBuilder.ResolveClearance: every body sphere of a
PlayMotionSampler recording (tools/tiger_points.py layout) rasterised into 5 cm cells, keeping the highest body point
(world y) per cell. Re-run after changing the loop: rebuild -> unity/motion_test.sh -> this -> rebuild.
Usage: tiger_corridor.py motion.json [out=sources/tiger/corridor.txt]"""
import json, math, sys, subprocess, os, tempfile
src = sys.argv[1]; out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "..", "sources", "tiger", "corridor.txt")
tmp = tempfile.mktemp(suffix=".txt")
subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), "tiger_points.py"), src, tmp, "--step", "1"], check=True, stdout=subprocess.DEVNULL)
C = 0.05; cells = {}
for line in open(tmp):
    f, x, y, z, r = line.split(); x, y, z, r = float(x), float(y), float(z), float(r)
    n = int(math.ceil(r / C))
    for i in range(-n, n + 1):
        for k in range(-n, n + 1):
            cx, cz = math.floor(x / C) + i, math.floor(z / C) + k
            if math.hypot((cx + 0.5) * C - x, (cz + 0.5) * C - z) > r + C * 0.71: continue
            top = y + r
            if cells.get((cx, cz), -1e9) < top: cells[(cx, cz)] = top
os.remove(tmp)
d = json.load(open(src))
with open(out, "w") as fo:
    fo.write(f"# tiger body sweep: cell {C} m, columns ix iz top_y (world); {len(d['frames'])} frames from {os.path.basename(src)}\n")
    for (cx, cz), top in sorted(cells.items()): fo.write(f"{cx} {cz} {top:.3f}\n")
print(len(cells), "cells ->", out)
