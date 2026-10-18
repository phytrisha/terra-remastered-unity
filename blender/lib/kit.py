# kit.py: generic modelling helpers, prepended to every step script by tools/blender_run.sh.
#
# Conventions (they make UVs, masks and export work without per-asset fixes):
# - Real metres, Z up, front = -Y, ground at z = 0, pivot at the bottom centre.
# - Each part is built in a LOCAL frame (long/grain axis = local X), UV-mapped there in metres / tile, then moved into
#   place with bmesh.ops.transform. Objects keep identity transforms, so nothing needs transform_apply later.
# - Every asset lives in its own collection SM_<Name>, with one sub-collection per part group.
# - Face float layers `edge` (1 on bevel faces) and `var` (per-part random) feed the vertex-colour masks in export.py.
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix, Euler

ASSET = os.environ.get("ASSET", "Asset")            # set ASSET=<Name> when running a step
MATERIALS = {}                                       # key -> (name, base colour, roughness[, metallic]); steps fill it


def asset_root(name=None):
    """Root collection SM_<Name>; created and linked on first use."""
    n = "SM_" + (name or ASSET)
    root = bpy.data.collections.get(n)
    if not root:
        root = bpy.data.collections.new(n); bpy.context.scene.collection.children.link(root)
    return root


def coll(name, asset=None):
    """Part collection under the asset root."""
    c = bpy.data.collections.get(name)
    if not c:
        c = bpy.data.collections.new(name); asset_root(asset).children.link(c)
    return c


def clear_coll(name):
    """Remove a collection's objects and their now-orphaned meshes (only meshes: lights and curves are not touched)."""
    c = bpy.data.collections.get(name)
    if c:
        for o in list(c.objects):
            me = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if isinstance(me, bpy.types.Mesh) and me.users == 0:
                bpy.data.meshes.remove(me)


def mat(name, color=(0.5, 0.5, 0.5), rough=0.7, metal=0.0, emit=None, emit_strength=0.0):
    """Principled material (reused if it exists). Keep base colours inside sRGB 30-240; metal is 0 or 1."""
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = emit_strength
    return m


def M(key):
    """Material by key from MATERIALS, e.g. MATERIALS["wood"] = ("M_Wood", (0.42, 0.26, 0.12), 0.7)."""
    if key not in MATERIALS:
        return mat("M_" + key.capitalize())
    name, col, rough, *rest = MATERIALS[key]
    return mat(name, col, rough, metal=rest[0] if rest else 0.0)


def xf(loc=(0, 0, 0), rot=(0, 0, 0)):
    """Local -> world matrix from a location and Euler rotation (radians)."""
    return Matrix.Translation(Vector(loc)) @ Euler(rot).to_matrix().to_4x4()


# ---------- UVs (metres / tile, so texel density is the same on every part) ----------
def box_uv(bm, tile=0.5, seed=0, faces=None):
    """Per-face planar projection in local coords; U runs along local X whenever the face contains X."""
    uv = bm.loops.layers.uv.verify()
    rnd = random.Random(seed); off = (rnd.random() * 4, rnd.random() * 4)
    for f in (faces if faces is not None else bm.faces):
        ax = max(range(3), key=lambda i: abs(f.normal[i]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]
        for l in f.loops:
            l[uv].uv = (l.vert.co[a] / tile + off[0], l.vert.co[b] / tile + off[1])


def box_uv_wrap(bm, tile=0.5, seed=0, faces=None):
    """Box UVs unrolled around local X (the grain axis): U = x, V = distance around the cross-section, scaled to a whole
    number of repeats so the single wrap seam is invisible on tileable textures. End faces (facing +-X) are planar.
    Per-face projection jumps at every long edge and bevel and shows as seams; this does not."""
    uv = bm.loops.layers.uv.verify()
    rnd = random.Random(seed); off = (rnd.random() * 4, rnd.random() * 4)
    faces = list(bm.faces if faces is None else faces)
    vs = {v for f in faces for v in f.verts}
    if not vs: return
    ys = [v.co.y for v in vs]; zs = [v.co.z for v in vs]
    cy, cz = (max(ys) + min(ys)) / 2, (max(zs) + min(zs)) / 2
    hy, hz = max((max(ys) - min(ys)) / 2, 1e-4), max((max(zs) - min(zs)) / 2, 1e-4)
    per = 4 * hy + 4 * hz
    vk = max(1, round(per / tile)) / per
    def S(y, z):
        y -= cy; z -= cz
        if hz - abs(z) <= hy - abs(y):
            return (y + hy) if z < 0 else (2 * hy + 2 * hz + (hy - y))
        return (2 * hy + (z + hz)) if y > 0 else (4 * hy + 2 * hz + (hz - z))
    for f in faces:
        if abs(f.normal.x) > 0.7:
            for l in f.loops: l[uv].uv = (l.vert.co.y / tile + off[0], l.vert.co.z / tile + off[1])
            continue
        ss = [S(l.vert.co.y, l.vert.co.z) for l in f.loops]
        if max(ss) - min(ss) > per / 2: ss = [s + per if s < per / 2 else s for s in ss]
        for l, s in zip(f.loops, ss): l[uv].uv = (l.vert.co.x / tile + off[0], s * vk + off[1])


# ---------- face tags ----------
def layers(bm):
    """Create the face layers up front: adding a layer later invalidates BMFace references you are holding."""
    le = bm.faces.layers.float.get("edge") or bm.faces.layers.float.new("edge")
    lv = bm.faces.layers.float.get("var") or bm.faces.layers.float.new("var")
    bm.loops.layers.uv.verify()
    return le, lv


def tag(bm, faces, edge_faces=(), var=None):
    le, lv = layers(bm)
    v = random.random() if var is None else var
    es = set(edge_faces)
    for f in faces:
        f[le] = 1.0 if f in es else 0.0
        f[lv] = v


def finish(name, bm, cname, mats, matrix=None):
    """Write bm into object `name` (replacing an old one) inside part collection `cname`."""
    if matrix is not None:
        bmesh.ops.transform(bm, matrix=matrix, verts=bm.verts)
    old = bpy.data.objects.get(name)
    if old:
        me_old = old.data; bpy.data.objects.remove(old, do_unlink=True)
        if me_old and me_old.users == 0: bpy.data.meshes.remove(me_old)
    me = bpy.data.meshes.new(name)
    bm.normal_update(); bm.to_mesh(me); bm.free()
    for uvl in me.uv_layers:                              # bmesh's uv.verify() names the layer "Float2"; engines expect UVMap
        uvl.name = "UVMap"; break
    for m_ in (mats if isinstance(mats, (list, tuple)) else [mats]):
        me.materials.append(m_)
    ob = bpy.data.objects.new(name, me)
    coll(cname).objects.link(ob)
    return ob


# ---------- primitives ----------
def box_bm(bm, size, matrix, bevel=0.0, segs=1, tile=0.5, seed=0, taper=None):
    """Bevelled box (taper=(sx, sy) scales the top). UVs are assigned AFTER the bevel, over every face the box owns:
    bmesh.ops.bevel replaces the original faces, and faces it missed would render as flat colour (all-zero UVs)."""
    layers(bm)
    before = set(bm.faces)
    verts = bmesh.ops.create_cube(bm, size=1.0)["verts"]
    bmesh.ops.scale(bm, vec=Vector(size), verts=verts)
    if taper:
        for v in verts:
            if v.co.z > 0: v.co.x *= taper[0]; v.co.y *= taper[1]
    faces = list({f for v in verts for f in v.link_faces})
    if bevel > 0:
        r = bmesh.ops.bevel(bm, geom=list({e for f in faces for e in f.edges}) + verts, offset=bevel, segments=segs,
                            profile=0.5, affect='EDGES', clamp_overlap=True)
        faces = [f for f in bm.faces if f not in before]
        verts = list({v for f in faces for v in f.verts})
        tag(bm, faces, r["faces"])
    else:
        tag(bm, faces)
    bm.normal_update()
    box_uv_wrap(bm, tile, seed, faces)
    bmesh.ops.transform(bm, matrix=matrix, verts=verts)
    return verts


def box(name, size, matrix, cname, key, bevel=0.02, segs=1, tile=0.5, taper=None):
    bm = bmesh.new(); box_bm(bm, size, matrix, bevel, segs, tile, hash(name) & 0xffff, taper)
    return finish(name, bm, cname, M(key))


def cylinder_bm(bm, radius, depth, matrix, segs=16, bevel=0.0, tile=0.5, cap_inset=0.0):
    """Cylinder along local Z (bottom at z=0): side U = arc length (whole repeats around), V = height.
    Caps get their own disc UVs; give them their own material slot (end grain, lids) via the returned cap faces.
    cap_inset sinks the caps (e.g. 0.025 for a barrel lid)."""
    layers(bm)
    before = set(bm.faces)
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius, depth=depth,
                          matrix=Matrix.Translation((0, 0, depth / 2)))
    faces = [f for f in bm.faces if f not in before]
    caps = [f for f in faces if abs(f.normal.z) > 0.9]
    if cap_inset:
        for f in caps:
            for v in f.verts: v.co.z += -cap_inset if v.co.z > depth / 2 else cap_inset
    edge = []
    if bevel > 0:
        r = bmesh.ops.bevel(bm, geom=list({e for f in caps for e in f.edges}), offset=bevel, segments=2, profile=0.5,
                            affect='EDGES', clamp_overlap=True)
        faces = [f for f in bm.faces if f not in before]; edge = r["faces"]
        caps = [f for f in faces if abs(f.normal.z) > 0.9 and f not in edge]
    tag(bm, faces, edge)
    uv = bm.loops.layers.uv.verify()
    per = 2 * math.pi * radius; k = max(1, round(per / tile)) / per
    for f in faces:
        if f in caps:
            for l in f.loops: l[uv].uv = (l.vert.co.x / tile + 0.5, l.vert.co.y / tile + 0.5)
            continue
        us = [(math.atan2(l.vert.co.y, l.vert.co.x) % (2 * math.pi)) * radius for l in f.loops]
        if max(us) - min(us) > per / 2: us = [u + per if u < per / 2 else u for u in us]
        for l, u in zip(f.loops, us): l[uv].uv = (u * k, l.vert.co.z / tile)
    bm.normal_update()
    bmesh.ops.transform(bm, matrix=matrix, verts=list({v for f in faces for v in f.verts}))
    return faces, caps


def sweep_bm(bm, pts, plane_n, width, thick, tile=0.5, seed=0, bevel=0.0, widths=None):
    """Rectangular beam swept along a polyline; width in the plane with normal plane_n. U = arc length (grain follows
    the curve). Bevel only real corners (> 35 deg): bevelling the flat joins between segments makes stripes."""
    layers(bm)
    B = Vector(plane_n).normalized(); pts = [Vector(p) for p in pts]
    rings, s, svals = [], 0.0, []
    for i, p in enumerate(pts):
        if i > 0: s += (p - pts[i - 1]).length
        svals.append(s)
        T = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        N = B.cross(T).normalized()
        w = (widths[i] if widths else width) / 2; t = thick / 2
        rings.append([bm.verts.new(p + N * a + B * b) for a, b in ((w, t), (-w, t), (-w, -t), (w, -t))])
    uv = bm.loops.layers.uv.verify()
    off = random.Random(seed).random() * 4
    side = [width, thick, width, thick]
    vs_ = max(1, round(2 * (width + thick) / tile)) / (2 * (width + thick)) * tile
    faces = []
    for i in range(len(rings) - 1):
        vacc = 0.0
        for j in range(4):
            k = (j + 1) % 4
            f = bm.faces.new((rings[i][j], rings[i][k], rings[i + 1][k], rings[i + 1][j])); faces.append(f)
            for l, (a, b) in zip(f.loops, [(svals[i], vacc), (svals[i], vacc + side[j]), (svals[i + 1], vacc + side[j]), (svals[i + 1], vacc)]):
                l[uv].uv = (a / tile + off, b * vs_ / tile)
            vacc += side[j]
    for ring, rev in ((rings[0], True), (rings[-1], False)):
        f = bm.faces.new(list(reversed(ring)) if rev else ring); faces.append(f)
        for l in f.loops: l[uv].uv = (l.vert.co.x / tile, l.vert.co.z / tile)
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    if bevel > 0:
        before = set(bm.faces) - set(faces); bm.normal_update()
        edges = [e for e in {e for f in faces for e in f.edges}
                 if len(e.link_faces) != 2 or e.calc_face_angle(0.0) > math.radians(35)]
        r = bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=1, profile=0.5, affect='EDGES', clamp_overlap=True)
        faces = [f for f in bm.faces if f not in before]
        tag(bm, faces, r["faces"])
    else:
        tag(bm, faces)
    return faces


def sweep(name, pts, plane_n, width, thick, cname, key, tile=0.5, bevel=0.02, widths=None):
    bm = bmesh.new(); sweep_bm(bm, pts, plane_n, width, thick, tile, hash(name) & 0xffff, bevel, widths)
    return finish(name, bm, cname, M(key))


def prism_bm(bm, outline, depth, matrix, tile=0.5, seed=0):
    """Extrude a 2D outline [(x, z), ...] (counter-clockwise seen from -Y) along local Y by depth."""
    layers(bm)
    front = [bm.verts.new((x, -depth / 2, z)) for x, z in outline]
    back = [bm.verts.new((x, depth / 2, z)) for x, z in outline]
    faces = [bm.faces.new(front), bm.faces.new(list(reversed(back)))]
    for i in range(len(outline)):
        k = (i + 1) % len(outline)
        faces.append(bm.faces.new((front[i], back[i], back[k], front[k])))
    bmesh.ops.recalc_face_normals(bm, faces=faces); bm.normal_update()
    box_uv(bm, tile, seed, faces)
    tag(bm, faces)
    bmesh.ops.transform(bm, matrix=matrix, verts=front + back)
    return faces


def prism(name, outline, depth, matrix, cname, key, tile=0.5):
    bm = bmesh.new(); prism_bm(bm, outline, depth, matrix, tile, hash(name) & 0xffff)
    return finish(name, bm, cname, M(key))


def arch_outline(w, h, n=12):
    """Round-top opening outline in local XZ, bottom at z=0, centred on x=0."""
    r = w / 2
    pts = [(-r, 0.0), (r, 0.0), (r, h - r)]
    pts += [(r * math.cos(math.pi * i / n), h - r + r * math.sin(math.pi * i / n)) for i in range(1, n)]
    return pts + [(-r, h - r)]


# ---------- instancing (many small repeated pieces: tiles, stones, bolts, planks) ----------
def make_template(size, bevel, segs=1, jitter=0.0, pillow=0.0, seed=0):
    """Bevelled box template: (local verts, faces[(ids, uv_axes, is_edge)], size). pillow bulges the -Y face.
    For pieces seen from one side only, delete the hidden faces from the template to save triangles, then check the
    corners where a hidden face becomes visible."""
    rnd = random.Random(seed)
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0); bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    edge_faces = set()
    if bevel > 0:
        edge_faces = set(bmesh.ops.bevel(bm, geom=list(bm.edges) + list(bm.verts), offset=bevel, segments=segs,
                                         profile=0.5, affect='EDGES', clamp_overlap=True)["faces"])
    S = Vector(size)
    for v in bm.verts:
        if jitter: v.co += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * jitter
        if pillow and v.co.y < 0:
            fx, fz = 1 - abs(v.co.x) / (S.x / 2), 1 - abs(v.co.z) / (S.z / 2)
            v.co.y -= pillow * max(0.0, min(fx, fz)) ** 0.5
    bm.normal_update()
    idx = {v: i for i, v in enumerate(bm.verts)}
    verts = [v.co.copy() for v in bm.verts]
    faces = [([idx[v] for v in f.verts], [(1, 2), (0, 2), (0, 1)][max(range(3), key=lambda i: abs(f.normal[i]))], f in edge_faces)
             for f in bm.faces]
    bm.free()
    return verts, faces, S


def instance(bm, tpl, matrix, scale=(1, 1, 1), tile=0.5, uv_off=(0, 0), var=0.0, smooth=False):
    """Stamp a template into bm. Keep `matrix` right-handed (det > 0): a mirrored basis turns every instance inside out."""
    verts, faces, _ = tpl
    le, lv = layers(bm); uv = bm.loops.layers.uv.verify()
    S = Vector(scale)
    loc = [Vector((c.x * S.x, c.y * S.y, c.z * S.z)) for c in verts]
    vs = [bm.verts.new(matrix @ q) for q in loc]
    out = []
    for ids, (a, b), is_e in faces:
        f = bm.faces.new([vs[i] for i in ids]); f.smooth = smooth
        f[le] = 1.0 if is_e else 0.0; f[lv] = var
        for l, i in zip(f.loops, ids): l[uv].uv = (loc[i][a] / tile + uv_off[0], loc[i][b] / tile + uv_off[1])
        out.append(f)
    return out


def chisel_bm(bm, size, matrix, rnd, taper=None, bevel=0.05, tile=0.8, cuts=2, amp=0.018, chips=3):
    """Rough-hewn block (stone, rock, rubble): subdivided, tapered, bevelled, noise-displaced, chipped corners."""
    from mathutils import noise
    layers(bm)
    before = set(bm.faces)
    vs = bmesh.ops.create_cube(bm, size=1.0)["verts"]
    bmesh.ops.scale(bm, vec=Vector(size), verts=vs)
    bmesh.ops.subdivide_edges(bm, edges=list({e for v in vs for e in v.link_edges}), cuts=cuts, use_grid_fill=True)
    faces = [f for f in bm.faces if f not in before]
    vs = list({v for f in faces for v in f.verts})
    if taper:
        for v in vs:
            t = v.co.z / size[2] + 0.5
            v.co.x *= 1 + (taper[0] - 1) * t; v.co.y *= 1 + (taper[1] - 1) * t
    r = bmesh.ops.bevel(bm, geom=list({e for f in faces for e in f.edges}), offset=bevel, segments=1, profile=0.5,
                        affect='EDGES', clamp_overlap=True)
    faces = [f for f in bm.faces if f not in before]
    vs = list({v for f in faces for v in f.verts})
    seed = Vector((rnd.random() * 50, rnd.random() * 50, rnd.random() * 50))
    corners = sorted(vs, key=lambda v: -v.co.length)[:16]
    for v in vs: v.co += v.co.normalized() * noise.noise(v.co * 3.0 + seed) * amp
    for v in rnd.sample(corners, min(chips, len(corners))): v.co *= rnd.uniform(0.9, 0.96)
    bm.normal_update()
    tag(bm, faces, r["faces"]); box_uv(bm, tile, rnd.randint(0, 999), faces)
    bmesh.ops.transform(bm, matrix=matrix, verts=vs)
    return faces


def peg_bm(bm, pos, normal, r=0.028, h=0.035, segs=8):
    """Small cylinder stuck onto a surface (bolt, rivet, peg, nail head)."""
    layers(bm)
    n = Vector(normal).normalized()
    before = set(bm.faces)
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r * 0.85, depth=h,
                          matrix=Matrix.Translation(Vector(pos) + n * h * 0.3) @ n.to_track_quat('Z', 'Y').to_matrix().to_4x4())
    faces = [f for f in bm.faces if f not in before]
    tag(bm, faces); box_uv(bm, 0.3, 0, faces)
    return faces


# ---------- normals ----------
def soft_normals(ob, angle=60.0, weighted=True):
    """Edges sharper than `angle` stay hard, the rest smooth; then Weighted Normal (face area + corner angle) so big flat
    faces stay flat and bevels carry the curvature. Written as custom split normals: import them in Unity
    (ModelImporter normals = Import). Unity's own 'Calculate' produces flat shading on meshes with per-corner vertex
    colours, so always author normals here."""
    me = ob.data
    bm = bmesh.new(); bm.from_mesh(me)
    lim = math.radians(angle)
    for f in bm.faces: f.smooth = True
    for e in bm.edges: e.smooth = len(e.link_faces) == 2 and e.calc_face_angle(0.0) < lim
    bm.to_mesh(me); bm.free()
    if not weighted: return
    md = ob.modifiers.new("SoftNormals", 'WEIGHTED_NORMAL'); md.mode = 'FACE_AREA_WITH_ANGLE'; md.weight = 100; md.keep_sharp = True
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=md.name)


def _include_tree(lc):
    lc.exclude = False
    for ch in lc.children: _include_tree(ch)


def solo(*names):
    """Show only these top-level collections (with their whole subtree) in the view layer, for renders and bakes."""
    for c in bpy.context.view_layer.layer_collection.children:
        if c.name in names: _include_tree(c)
        else: c.exclude = True
