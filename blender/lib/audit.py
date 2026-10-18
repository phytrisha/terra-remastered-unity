# audit.py: numeric checks for one asset collection. Needs kit.py. Never trust a build step's intent: measure it.
#
#   validate(root)          per object: evaluated tris, transforms, non-manifold, loose verts, n-gons, zero-area,
#                           UV layers, material slots; plus missing image files
#   audit_asset(root)       per mesh island: floating parts, long members with an unsupported end,
#                           inverted closed shells (negative signed volume), unapplied transforms, lowest point
#   clashes(root)           triangle overlaps between different objects (BVH from explicit triangle lists)
#   check_budget(root, ...) evaluated tri count and material count against the asset's budget
# Target before export: 0 floating, 0 inverted, 0 transforms, clashes reviewed, budget OK.
from mathutils.bvhtree import BVHTree


def _meshes(root_name):
    return [o for o in bpy.data.collections[root_name].all_objects if o.type == 'MESH']


def evaluated_tris(o, dg=None):
    """Triangles with modifiers applied (a Solidify or Bevel modifier can double the count)."""
    dg = dg or bpy.context.evaluated_depsgraph_get()
    eo = o.evaluated_get(dg); me = eo.to_mesh()
    n = sum(len(p.vertices) - 2 for p in me.polygons); eo.to_mesh_clear()
    return n


def validate(root_name):
    dg = bpy.context.evaluated_depsgraph_get()
    rep = {}
    for o in _meshes(root_name):
        bm = bmesh.new(); bm.from_mesh(o.data)
        rep[o.name] = dict(
            tris=evaluated_tris(o, dg),
            scale_applied=all(abs(s - 1) < 1e-4 for s in o.scale),
            rot_applied=all(abs(r) < 1e-6 for r in o.rotation_euler),
            non_manifold=sum(not e.is_manifold for e in bm.edges),
            loose_verts=sum(not v.link_edges for v in bm.verts),
            ngons=sum(len(f.verts) > 4 for f in bm.faces),
            zero_area=sum(f.calc_area() < 1e-8 for f in bm.faces),
            uv_layers=[u.name for u in o.data.uv_layers], materials=len(o.material_slots),
            zero_uv_faces=_zero_uv_faces(bm))
        bm.free()
    missing = [i.name for i in bpy.data.images if i.source == 'FILE' and not i.packed_file
               and not os.path.exists(bpy.path.abspath(i.filepath))]
    return {"objects": rep, "missing_images": missing}


def _zero_uv_faces(bm):
    """Faces whose UVs collapse to a point: they render as flat colour (typical after a bevel replaced faces)."""
    uv = bm.loops.layers.uv.active
    if not uv: return len(bm.faces)
    n = 0
    for f in bm.faces:
        us = [l[uv].uv for l in f.loops]
        if max((a - us[0]).length for a in us) < 1e-6 and f.calc_area() > 1e-4: n += 1
    return n


def check_budget(root_name, max_tris, max_materials=6):
    dg = bpy.context.evaluated_depsgraph_get()
    objs = _meshes(root_name)
    tris = sum(evaluated_tris(o, dg) for o in objs)
    mats = {s.material.name for o in objs for s in o.material_slots if s.material}
    return {"tris": tris, "max_tris": max_tris, "materials": sorted(mats),
            "ok": tris <= max_tris and len(mats) <= max_materials}


def audit_asset(root_name, ground=0.0, tol=0.03, skip=(), member_keys=()):
    """Mesh islands = individual parts. member_keys: substrings of object names that are structural members (beams,
    struts, pipes); long thin islands of those must be held at both ends. skip: object-name substrings to ignore."""
    objs = _meshes(root_name)
    rep = {"objects": len(objs), "floating": [], "loose_end": [], "inverted": [], "transforms": [], "min_z": None}
    verts_all, tris_all, owner, islands = [], [], [], []
    for o in objs:
        if any(abs(v) > 1e-4 for v in o.rotation_euler) or any(abs(v - 1) > 1e-4 for v in o.scale):
            rep["transforms"].append(o.name)
        mw = o.matrix_world
        bm = bmesh.new(); bm.from_mesh(o.data)
        seen, vid = set(), {}
        for f in bm.faces:
            if f.index in seen: continue
            stack, comp = [f], []; seen.add(f.index)
            while stack:
                g = stack.pop(); comp.append(g)
                for e in g.edges:
                    for h in e.link_faces:
                        if h.index not in seen: seen.add(h.index); stack.append(h)
            iid = len(islands)
            co = [mw @ v.co for v in {v for g in comp for v in g.verts}]
            closed = all(e.is_manifold for g in comp for e in g.edges)
            vol = 0.0
            for g in comp:
                t = [mw @ v.co for v in g.verts]
                for k in range(1, len(t) - 1): vol += t[0].dot(t[k].cross(t[k + 1])) / 6.0
            if closed and vol < -1e-6: rep["inverted"].append(f"{o.name}#{iid}")
            for g in comp:
                idx = []
                for v in g.verts:
                    key = (o.name, v.index)
                    if key not in vid: vid[key] = len(verts_all); verts_all.append(mw @ v.co)
                    idx.append(vid[key])
                for k in range(1, len(idx) - 1): tris_all.append((idx[0], idx[k], idx[k + 1])); owner.append(iid)
            islands.append((o.name, co))
        bm.free()
    if not tris_all: return rep
    bvh = BVHTree.FromPolygons(verts_all, tris_all, all_triangles=True, epsilon=0.0)
    rep["min_z"] = round(min(v.z for v in verts_all), 4)
    isl_tris = {}
    for ti, iid in enumerate(owner): isl_tris.setdefault(iid, []).append(tris_all[ti])

    def overlaps(iid):
        tr = isl_tris.get(iid, [])
        ids = sorted({i for t in tr for i in t}); rm = {g: k for k, g in enumerate(ids)}
        b2 = BVHTree.FromPolygons([verts_all[g] for g in ids], [tuple(rm[i] for i in t) for t in tr], all_triangles=True)
        return any(owner[a] != iid for a, b in bvh.overlap(b2))

    def touches(iid, pts, r):
        return any(owner[hit[2]] != iid for p in pts for hit in bvh.find_nearest_range(p, r))

    def end_held(iid, e):
        for p in e:
            for co, nrm, fi, dist in bvh.find_nearest_range(p, 0.3):
                if owner[fi] != iid and (dist < 0.06 or (p - co).dot(nrm) < 0): return True
        return False

    for iid, (oname, co) in enumerate(islands):
        if any(k in oname for k in skip): continue
        lo = Vector([min(p[i] for p in co) for i in range(3)]); hi = Vector([max(p[i] for p in co) for i in range(3)])
        if lo.z <= ground + tol: continue
        if not (touches(iid, co[::max(1, len(co) // 40)], tol + 0.01) or overlaps(iid)):
            c = (lo + hi) / 2
            rep["floating"].append(f"{oname}#{iid} at ({c.x:.2f},{c.y:.2f},{lo.z:.2f}) size {tuple(round(v, 2) for v in (hi - lo))}")
            continue
        dims = hi - lo; L = max(dims)
        if member_keys and any(k in oname for k in member_keys) and L > 0.9 and L > 4 * sorted(dims)[1]:
            ax = max(range(3), key=lambda i: dims[i])
            for k, e in enumerate(([p for p in co if p[ax] < lo[ax] + 0.25], [p for p in co if p[ax] > hi[ax] - 0.25])):
                if e and not (min(p.z for p in e) <= ground + tol or touches(iid, e[::max(1, len(e) // 12)], 0.06) or end_held(iid, e)):
                    rep["loose_end"].append(f"{oname}#{iid} end{k} near ({e[0].x:.2f},{e[0].y:.2f},{e[0].z:.2f}) len {L:.2f}")
    rep["islands"] = len(islands)
    return rep


def clashes(root_name, min_tris=1):
    """Pairs of different objects whose triangles intersect. Built from explicit triangle lists: BVHTree.FromObject
    indices do not map back to loop_triangles. Report locations, then decide: intended embed or a real clash."""
    objs = _meshes(root_name)
    data = {}
    for o in objs:
        me = o.data; me.calc_loop_triangles(); mw = o.matrix_world
        vs = [mw @ v.co for v in me.vertices]; ts = [tuple(t.vertices) for t in me.loop_triangles]
        if ts: data[o.name] = (BVHTree.FromPolygons(vs, ts, all_triangles=True), vs, ts)
    out = []
    names = sorted(data)
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            hits = data[a][0].overlap(data[b][0])
            if len(hits) >= min_tris:
                va, ta = data[a][1], data[a][2]
                c = sum((sum((va[k] for k in ta[h[0]]), Vector()) / 3 for h in hits[:50]), Vector()) / min(len(hits), 50)
                out.append({"a": a, "b": b, "tris": len(hits), "near": [round(x, 2) for x in c]})
    return sorted(out, key=lambda r: -r["tris"])
