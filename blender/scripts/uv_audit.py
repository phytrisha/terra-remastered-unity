# Usage: $BLENDER -b --factory-startup --python blender/scripts/uv_audit.py -- a.fbx [b.fbx ...]
# UV distortion audit on exported FBX: per face, the UV->3D Jacobian's singular values give stretch anisotropy
# (s_max/s_min) and texel density (UV units per metre). Faces with anisotropy > 2.5 or density off the material's median by
# more than 2.5x count as distorted. Reports the worst materials and the 3D location of distorted clusters.
import bpy, sys, json, math
from mathutils import Vector
args = sys.argv[sys.argv.index("--") + 1:]
out = {}
for path in args:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, use_custom_normals=True)
    rep = {}
    for o in bpy.context.scene.objects:
        if o.type != 'MESH' or o.name.startswith("UCX"): continue
        me = o.data; uvl = me.uv_layers.get("UVMap") or me.uv_layers[0]; mw = o.matrix_world
        me.calc_loop_triangles()
        per = {}
        for t in me.loop_triangles:
            p = [mw @ me.vertices[v].co for v in t.vertices]; q = [uvl.data[l].uv for l in t.loops]
            e1, e2 = p[1] - p[0], p[2] - p[0]; a3 = e1.cross(e2).length / 2
            if a3 < 1e-6: continue
            # local 2D frame of the triangle
            X = e1.normalized(); N = e1.cross(e2).normalized(); Y = N.cross(X)
            x1, y1 = e1.dot(X), e1.dot(Y); x2, y2 = e2.dot(X), e2.dot(Y)
            u1, v1 = q[1] - q[0]; u2, v2 = q[2] - q[0]
            det = x1 * y2 - x2 * y1
            if abs(det) < 1e-9: continue
            # J = dUV/dxy
            a = (u1 * y2 - u2 * y1) / det; b = (u2 * x1 - u1 * x2) / det; c = (v1 * y2 - v2 * y1) / det; d = (v2 * x1 - v1 * x2) / det
            s1 = math.sqrt(max(((a * a + b * b + c * c + d * d) + math.sqrt(max((a * a + b * b - c * c - d * d) ** 2 + 4 * (a * c + b * d) ** 2, 0))) / 2, 0))
            s2 = abs(a * d - b * c) / max(s1, 1e-9)
            m = me.materials[t.material_index].name if me.materials else "?"
            per.setdefault(m, []).append((s1 / max(s2, 1e-9), math.sqrt(s1 * s2), a3, sum(p, Vector()) / 3))
        for m, lst in per.items():
            dens = sorted(x[1] for x in lst); med = dens[len(dens) // 2]
            tot = sum(x[2] for x in lst); bad = [x for x in lst if x[0] > 2.5 or x[1] > med * 2.5 or x[1] < med / 2.5]
            badA = sum(x[2] for x in bad)
            clusters = {}
            for x in bad:
                k = tuple(round(c / 1.0) for c in x[3]); clusters[k] = clusters.get(k, 0) + x[2]
            top = sorted(clusters.items(), key=lambda kv: -kv[1])[:4]
            rep[m] = dict(area=round(tot, 2), bad_pct=round(100 * badA / max(tot, 1e-9), 1), med_density=round(med, 2), worst=[(list(k), round(v, 3)) for k, v in top])
    out[path.split("/")[-1]] = rep
print("RESULT_JSON " + json.dumps(out))
