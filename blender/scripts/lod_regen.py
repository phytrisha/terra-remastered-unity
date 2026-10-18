# Regenerate the far LODs (SM_<Name>_LOD1.fbx, SM_<Name>_LOD2.fbx) from an exported LOD0 FBX, to fixed budgets.
# Use it when far LODs are the frame's triangle cost (many instances in view): plain 50 % / 20 % collapse keeps every
# small part and material, so LOD2 stays heavy with many submeshes. Unity's own Mesh LOD generation can't collapse meshes
# whose vertices are split per corner (vertex colours, hard edges), so do it here.
#   LOD1: drop DROP1 materials, about 30 % of LOD0, at most 20k tris (landmarks > 200k tris: 25 %, at most 60k)
#   LOD2: a clean far shell: drop DROP2 materials and every loose part smaller than MIN_PART, then collapse to about 4 %,
#         1.5-3.5k tris (landmarks 2 %, at most 12k)
# Usage: DROP2="Leaves,Bolts" $BLENDER -b --factory-startup --python blender/scripts/lod_regen.py -- <out_dir> <SM_Name.fbx> [...]
# Writes <out_dir>/SM_<Name>_LOD1.fbx and _LOD2.fbx with the FBX settings of export.py, reusing the object/mesh names of
# existing LOD files (Unity keys mesh sub-assets by name), and prints one RESULT line per asset.
# Don't use it on very large single walls (100k+ tris): fixed budgets smear their UVs; keep the Blender LODs there.
import bpy, bmesh, math, os, sys, json

def _env_list(k, default=""):
    return tuple(x for x in os.environ.get(k, default).split(",") if x)

DROP1 = _env_list("DROP1")                         # material-name keywords removed from LOD1 (e.g. "Petals,Decal")
DROP2 = _env_list("DROP2")                         # ... and from LOD2 (small detail materials: foliage, trims, bolts)
MIN_PART = float(os.environ.get("MIN_PART", "0.9"))   # LOD2 drops loose parts whose bbox diagonal is below this (m)
# Per-asset LOD2 part size, e.g. PART_OVERRIDE="SM_Bridge=0,SM_Tower=0.45". 0 = keep every part and only collapse
# (to 15 %): use it for assets built from pieces with no backing surface, where removing parts opens holes.
PART_OVERRIDE = {k: float(v) for k, v in (x.split("=") for x in _env_list("PART_OVERRIDE"))}
FBX_IMPORT = dict(use_custom_normals=True, colors_type='LINEAR', axis_forward='-Z', axis_up='Y', use_image_search=False)
FBX_EXPORT = dict(use_selection=True, object_types={'MESH'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
                  axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
                  use_tspace=True, colors_type='LINEAR', prioritize_active_color=True, add_leaf_bones=False,
                  bake_anim=False, path_mode='STRIP', embed_textures=False)


def budget(t0, level, base=""):
    landmark = t0 > 200000
    if level == 2 and PART_OVERRIDE.get(base, MIN_PART) == 0: return min(t0 * 0.15, 40000)
    if level == 1: return min(t0 * (0.25 if landmark else 0.3), 60000 if landmark else 20000)
    return min(max(t0 * (0.02 if landmark else 0.04), 1500), 12000 if landmark else 3500)


def tris(me):
    me.calc_loop_triangles(); return len(me.loop_triangles)


def soft_normals(ob, angle=60.0):
    # same as kit.soft_normals: hard above 60 deg, weighted normals (face area + corner angle)
    me = ob.data; bm = bmesh.new(); bm.from_mesh(me); lim = math.radians(angle)
    for f in bm.faces: f.smooth = True
    for e in bm.edges: e.smooth = len(e.link_faces) == 2 and e.calc_face_angle(0.0) < lim
    bm.to_mesh(me); bm.free()
    md = ob.modifiers.new("SoftNormals", 'WEIGHTED_NORMAL'); md.mode = 'FACE_AREA_WITH_ANGLE'; md.weight = 100; md.keep_sharp = True
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=md.name)


def old_names(path):
    # object and mesh-data names inside an existing LOD FBX: Unity keys the mesh sub-asset (and every scene/prefab
    # reference to it) by name, so the new file must reuse them exactly. Read straight from the binary FBX
    # ("<name>\x00\x01Model" / "\x00\x01Geometry"): importing some old LOD files crashes Blender 5.1.
    if not os.path.exists(path): return None
    import re
    data = open(path, "rb").read()
    def first(kind):
        m = re.search(rb"([\x20-\x7e]+)\x00\x01" + kind, data)
        return m.group(1).decode() if m else None
    models = [m.decode() for m in re.findall(rb"([\x20-\x7e]+)\x00\x01Model", data)]
    model = next((n for n in models if "_LOD" in n), first(b"Model"))
    geom = first(b"Geometry")
    return (model, geom) if model and geom else None


def small_parts(bm, skip, min_size):
    """Faces of loose parts (connected face sets) whose bounding-box diagonal is below min_size."""
    seen, out = set(), []
    for f in bm.faces:
        if f.index in seen or f.material_index in skip: continue
        part = [f]; seen.add(f.index); i = 0
        while i < len(part):
            for e in part[i].edges:
                for h in e.link_faces:
                    if h.index not in seen: seen.add(h.index); part.append(h)
            i += 1
        cs = [v.co for g in part for v in g.verts]
        lo = [min(c[k] for c in cs) for k in range(3)]; hi = [max(c[k] for c in cs) for k in range(3)]
        if sum((hi[k] - lo[k]) ** 2 for k in range(3)) ** 0.5 < min_size: out += part
    return out


def decimate(ob, ratio):
    if ratio >= 1: return
    md = ob.modifiers.new("Decimate", 'DECIMATE'); md.ratio = max(ratio, 0.005); md.use_collapse_triangulate = True
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=md.name)


def make_lod(src, name, level, t0, names=None, base=""):
    me = src.data.copy(); ob = bpy.data.objects.new(name, me)
    if names: ob.name, me.name = names
    else: me.name = name
    ob.matrix_world = src.matrix_world.copy(); bpy.context.scene.collection.objects.link(ob)
    drop = DROP1 if level == 1 else DROP2
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0004)
    idx = {i for i, m in enumerate(me.materials) if m and any(d in m.name for d in drop)}
    dead = [f for f in bm.faces if f.material_index in idx]
    if level == 2:
        bm.faces.ensure_lookup_table(); bm.faces.index_update()
        size = PART_OVERRIDE.get(base, MIN_PART)
        if size: dead += small_parts(bm, idx, size)
    dead = list(set(dead))
    if len(dead) < len(bm.faces): bmesh.ops.delete(bm, geom=dead, context='FACES')
    bm.to_mesh(me); bm.free()
    # remove now-unused material slots, so Unity gets no empty submeshes
    used = {p.material_index for p in me.polygons}
    for i in reversed(range(len(me.materials))):
        if i not in used: me.materials.pop(index=i)              # pop() shifts the face material indices itself
    t = tris(me); target = budget(t0, level, base)
    if t > target: decimate(ob, target / t)
    soft_normals(ob)
    return ob


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    out_dir, files = argv[0], argv[1:]
    os.makedirs(out_dir, exist_ok=True)
    for fbx in files:
        base = os.path.splitext(os.path.basename(fbx))[0]
        names = {level: old_names(f"{out_dir}/{base}_LOD{level}.fbx") for level in (1, 2)}
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=fbx, **FBX_IMPORT)
        base = os.path.splitext(os.path.basename(fbx))[0]
        meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
        src = next((o for o in meshes if o.name == base), None) or max((o for o in meshes if not o.name.startswith("UCX_")), key=lambda o: len(o.data.polygons))
        for o in meshes:
            if o is not src: bpy.data.objects.remove(o, do_unlink=True)
        src.name = src.data.name = "lod0_source"                          # frees the old names for the LOD copies
        t0 = tris(src.data); stats = {"name": base, "lod0": t0}
        for level in (1, 2):
            path = f"{out_dir}/{base}_LOD{level}.fbx"
            ob = make_lod(src, f"{base}_LOD{level}", level, t0, names[level], base)
            if names[level] and (ob.name, ob.data.name) != names[level]: raise RuntimeError(f"name clash {names[level]} -> {(ob.name, ob.data.name)}")
            for o in bpy.context.scene.objects: o.select_set(False)
            ob.select_set(True); bpy.context.view_layer.objects.active = ob
            bpy.ops.export_scene.fbx(filepath=path, **FBX_EXPORT)
            stats[f"lod{level}"] = tris(ob.data); stats[f"mats{level}"] = len(ob.data.materials)
            me = ob.data; bpy.data.objects.remove(ob, do_unlink=True); bpy.data.meshes.remove(me)   # LOD1 and LOD2 share the old mesh name
        print("RESULT " + json.dumps(stats), flush=True)


main()
