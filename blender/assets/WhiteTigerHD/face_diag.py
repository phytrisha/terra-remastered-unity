# Face diagnosis: which bones own the HD head vertices, and how much does the face stretch through the clips
# (a head should move rigidly: edge-length strain ~0). Compares HD with the low-poly head. No save.
import numpy as np
arm = bpy.data.objects["WhiteTiger_Rig"]; sc = bpy.context.scene
def head_info(ob, ymax=-0.80):
    me = ob.data; V = np.array([v.co for v in me.vertices]); sel = np.where(V[:, 1] < ymax)[0]
    names = {g.index: g.name for g in ob.vertex_groups}; own = {}; mixed = 0
    for i in sel:
        gs = sorted(((g.weight, names[g.group]) for g in me.vertices[i].groups), reverse=True)
        if not gs: continue
        own[gs[0][1]] = own.get(gs[0][1], 0) + 1
        if gs[0][0] < 0.95: mixed += 1
    E = np.array([e.vertices[:] for e in me.edges]); E = E[np.isin(E[:, 0], sel) & np.isin(E[:, 1], sel)]
    return sel, E, {"verts": int(len(sel)), "dominant": own, "mixed_lt_0.95": mixed}
def strain(ob, sel, E, act, step=3):
    arm.animation_data.action = bpy.data.actions[act]; worst = []; where = []
    sc.frame_set(0); dg = bpy.context.evaluated_depsgraph_get()
    rest = np.array([v.co for v in ob.data.vertices]); L0 = np.linalg.norm(rest[E[:, 0]] - rest[E[:, 1]], axis=1) + 1e-9
    for f in range(0, int(bpy.data.actions[act].frame_range[1]) + 1, step):
        sc.frame_set(f); dg = bpy.context.evaluated_depsgraph_get(); e = ob.evaluated_get(dg); m = e.to_mesh()
        P = np.array([v.co for v in m.vertices]); e.to_mesh_clear()
        s = np.abs(np.linalg.norm(P[E[:, 0]] - P[E[:, 1]], axis=1) / L0 - 1)
        worst.append(np.percentile(s, 99)); k = np.argmax(s); where.append((f, round(float(s[k]), 3), P[E[k, 0]].round(2).tolist()))
    w = int(np.argmax(worst))
    return {"p99_strain_max": round(float(max(worst)), 3), "p99_strain_mean": round(float(np.mean(worst)), 3), "worst_edge": where[w]}
out = {}
for name in ("SM_WhiteTigerHD", "SM_WhiteTiger"):
    ob = bpy.data.objects[name]; sel, E, inf = head_info(ob)
    inf["strain"] = {a: strain(ob, sel, E, a) for a in ("Idle", "Run", "Pounce", "Land")}
    out[name] = inf
result = out
