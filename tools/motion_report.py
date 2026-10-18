#!/usr/bin/env python3
"""Animation QA report from a PlayMotionSampler JSON (unity/motion_test.sh).
  slip:  horizontal travel of each paw while it is planted (contact = paw within CONTACT m of its lowest height in a
         sliding window AND nearly still vertically) -> per contact: total horizontal drift (mm); report p50 / p95 / max
  ground: paw height above the terrain while planted (mm): negative = through the ground, large = floating
  locked: with QuadrupedLocomotion, paw drift over each stance by its own planted flags (the height-based contacts
         above also catch the paw peeling off and settling)
  pops:  frame-to-frame jerk of bones in the root's frame (3rd difference of position, m/frame^3) -> spikes = glitches
Usage: motion_report.py motion.json [--paw-rest 0.05]"""
import json, math, sys, statistics as st

path = sys.argv[1]
paw_rest = float(sys.argv[sys.argv.index("--paw-rest") + 1]) if "--paw-rest" in sys.argv else 0.06
d = json.load(open(path)); F = d["frames"]; fps = d["fps"]
paws = list(F[0]["paws"].keys())

def hdist(a, b): return math.hypot(a[0] - b[0], a[2] - b[2])

out = {"frames": len(F), "seconds": round(len(F) / fps, 1),
       "states": {s: sum(1 for f in F if f["state"] == s) for s in set(f["state"] for f in F)}}
slips, ground_err, walking_slips = [], [], []
for pw in paws:
    P = [f["paws"][pw]["p"] for f in F]; G = [f["paws"][pw]["g"] for f in F]
    # planted: horizontal speed < 0.25 m/s of root speed OR vertical speed tiny and near its local minimum height
    contact = []
    for i in range(len(F)):
        lo = min(P[j][1] - G[j] for j in range(max(0, i - 8), min(len(F), i + 9)))
        vz = abs(P[min(i + 1, len(F) - 1)][1] - P[max(i - 1, 0)][1]) * fps / 2
        contact.append((P[i][1] - G[i]) - lo < 0.012 and vz < 0.05)
    i = 0
    while i < len(F):
        if not contact[i]: i += 1; continue
        j = i
        while j + 1 < len(F) and contact[j + 1]: j += 1
        if j - i >= 4:                                    # >= 5 frames planted
            a, b = i + 1, j - 1                           # trim the touchdown / lift-off frames
            drift = max(hdist(P[k], P[a]) for k in range(a, b + 1))
            slips.append(drift)
            if all(F[k]["state"] == "Walk" for k in range(a, b + 1)): walking_slips.append(drift)
            ground_err += [(P[k][1] - G[k] - paw_rest) for k in range(a, b + 1)]
        i = j + 1
mm = lambda v: round(v * 1000, 1)
if slips:
    s = sorted(slips)
    out["slip_mm"] = {"contacts": len(s), "p50": mm(s[len(s) // 2]), "p95": mm(s[int(len(s) * 0.95) - 1 if len(s) > 1 else 0]), "max": mm(s[-1])}
if walking_slips:
    s = sorted(walking_slips); out["slip_walking_mm"] = {"contacts": len(s), "p50": mm(s[len(s) // 2]), "max": mm(s[-1])}
if ground_err:
    g = sorted(ground_err)
    out["planted_height_above_ground_mm"] = {"p5": mm(g[int(len(g) * 0.05)]), "p50": mm(g[len(g) // 2]), "p95": mm(g[int(len(g) * 0.95)])}
# pops: bones in the root's frame, jerk spikes
def local(f, p):
    yaw = math.radians(f["yaw"]); dx, dy, dz = p[0] - f["pos"][0], p[1] - f["pos"][1], p[2] - f["pos"][2]
    return (dx * math.cos(yaw) - dz * math.sin(yaw), dy, dx * math.sin(yaw) + dz * math.cos(yaw))
nb = len(F[0]["b"]); jerks = []
for bi in range(nb):
    L = [local(f, f["b"][bi]) for f in F]
    for k in range(3, len(L)):
        j = math.sqrt(sum((L[k][c] - 3 * L[k - 1][c] + 3 * L[k - 2][c] - L[k - 3][c]) ** 2 for c in range(3)))
        jerks.append((j, k, d["bones"][bi]))
jerks.sort(reverse=True)
med = st.median(j for j, _, _ in jerks)
out["pops"] = {"jerk_median_mm": mm(med), "top": [(mm(j), k, n, F[k]["state"]) for j, k, n in jerks[:8]],
               "frames_over_20x_median": len({k for j, k, _ in jerks if j > 20 * med})}
# root height smoothness (vertical jerk of the root)
R = [f["pos"][1] for f in F]
rj = [abs(R[k] - 3 * R[k - 1] + 3 * R[k - 2] - R[k - 3]) for k in range(3, len(R))]
out["root_vertical_jerk_mm"] = {"p95": mm(sorted(rj)[int(len(rj) * 0.95)]), "max": mm(max(rj))}
# QuadrupedLocomotion's own stance flags (when the sampler recorded them): paw drift from touchdown to lift-off
if any("legs" in f for f in F):
    drift = []
    for leg in F[next(i for i, f in enumerate(F) if "legs" in f)]["legs"]:
        start = None
        for i, f in enumerate(F):
            L = f.get("legs", {}).get(leg)
            if L is None: start = None; continue
            if L["pl"] and start is None: start = i
            elif not L["pl"] and start is not None:
                drift.append(hdist(F[start]["paws"]["toe_" + leg]["p"], F[i - 1]["paws"]["toe_" + leg]["p"])); start = None
    if drift:
        s = sorted(drift)
        out["locked_stance_drift_mm"] = {"stances": len(s), "p50": mm(s[len(s) // 2]), "p95": mm(s[int(len(s) * 0.95) - 1]), "max": mm(s[-1])}
print(json.dumps(out, indent=1))
