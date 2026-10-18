#!/usr/bin/env python3
"""Body sample spheres of an animal from a PlayMotionSampler JSON, for unity/clearance_audit.cs: every Nth frame, each
bone head plus midpoints along the spine / neck / legs / tail, with a radius per body part (world m). The species is
read from the rig (tiger: spine3 / neck1-2; tapir: snout; else chital); radii are measured half-widths of each body.
Usage: tiger_points.py motion.json out.txt [--step 3]"""
import json, sys
src, out = sys.argv[1], sys.argv[2]
step = int(sys.argv[sys.argv.index("--step") + 1]) if "--step" in sys.argv else 3
d = json.load(open(src)); B = d["bones"]; F = d["frames"]
RADII = {
    "tiger": {"hips": 0.17, "spine1": 0.18, "spine2": 0.18, "spine3": 0.18, "chest": 0.17, "neck1": 0.13, "neck2": 0.12, "head": 0.12,
              "upper": 0.08, "lower": 0.06, "foot": 0.045, "toe": 0.035, "tail": 0.035, "ear": 0.03},
    "tapir": {"hips": 0.26, "spine1": 0.29, "spine2": 0.28, "chest": 0.26, "neck": 0.18, "head": 0.14, "snout": 0.07,
              "upper": 0.10, "lower": 0.07, "foot": 0.05, "toe": 0.045, "tail": 0.04, "ear": 0.04},
    "chital": {"hips": 0.18, "spine1": 0.19, "spine2": 0.19, "chest": 0.18, "neck": 0.10, "head": 0.09,
               "upper": 0.07, "lower": 0.04, "foot": 0.03, "toe": 0.03, "tail": 0.04, "ear": 0.04}}
species = "tiger" if "spine3" in B else "tapir" if "snout" in B else "chital"
R = RADII[species]
def rad(n): return R.get(n, R.get(n.split("_")[0], R.get(n.rstrip("0123456789"), 0)))
spine = [b for b in ["hips", "spine1", "spine2", "spine3", "chest", "neck", "neck1", "neck2", "head", "snout"] if b in B]
chains = [spine, ["hips"] + [f"tail{i}" for i in range(1, 7) if f"tail{i}" in B]]
for leg in ("FL", "FR", "BL", "BR"): chains.append([f"{s}_{leg}" for s in ("upper", "lower", "foot", "toe")])
lines = []
for fi in range(0, len(F), step):
    P = dict(zip(B, F[fi]["b"]))
    for ch in chains:
        for a, b in zip(ch, ch[1:]):
            pa, pb = P[a], P[b]; ra, rb = rad(a), rad(b)
            for t in (0.0, 0.5):
                p = [pa[k] + (pb[k] - pa[k]) * t for k in range(3)]
                lines.append(f"{fi} {p[0]:.3f} {p[1]:.3f} {p[2]:.3f} {ra + (rb - ra) * t:.3f}")
        lines.append(f"{fi} {P[ch[-1]][0]:.3f} {P[ch[-1]][1]:.3f} {P[ch[-1]][2]:.3f} {rad(ch[-1]):.3f}")
    # nose: the last bone's tail is not recorded; extend the neck -> head direction by about a head length
    h, n2 = P["head"], P[spine[spine.index("head") - 1]]; nose = [h[k] + (h[k] - n2[k]) * (2.0 if species == "tiger" else 1.6) for k in range(3)]
    lines.append(f"{fi} {nose[0]:.3f} {nose[1]:.3f} {nose[2]:.3f} {0.09 if species == 'tiger' else R['head'] * 0.6:.3f}")
open(out, "w").write("\n".join(lines))
print(len(lines), "spheres from", len(range(0, len(F), step)), "frames", species)
