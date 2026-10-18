# Where are the remaining HD <-> low-poly gaps and the flipped faces? (no save)
import numpy as np
from mathutils.bvhtree import BVHTree
def arr(me):
    me.calc_loop_triangles()
    v = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", v)
    t = np.empty(len(me.loop_triangles) * 3, np.int64); me.loop_triangles.foreach_get("vertices", t)
    return v.reshape(-1, 3), t.reshape(-1, 3)
L, LT = arr(bpy.data.objects["SM_WhiteTiger"].data); H, HT = arr(bpy.data.objects["SM_WhiteTigerHD"].data)
bh = BVHTree.FromPolygons(H.tolist(), HT.tolist()); bl = BVHTree.FromPolygons(L.tolist(), LT.tolist())
def regions(P, d, thr):
    sel = P[d > thr]; out = []
    while len(sel):
        c = sel[0]; m = np.linalg.norm(sel - c, axis=1) < 0.08
        out.append({"at": sel[m].mean(0).round(2).tolist(), "n": int(m.sum()), "max_mm": None}); sel = sel[~m]
    return sorted(out, key=lambda r: -r["n"])[:10]
dl = np.array([bh.find_nearest(p)[3] for p in L]); dh = np.array([bl.find_nearest(p)[3] for p in H])
result = {"hd_dims": np.ptp(H, 0).round(3).tolist(), "lo_dims": np.ptp(L, 0).round(3).tolist(),
          "lo_gaps>15mm": regions(L, dl, 0.015), "hd_gaps>8mm": regions(H, dh, 0.008)}
