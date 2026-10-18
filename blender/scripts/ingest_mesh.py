# Ingest an external mesh (Tripo / other AI generator / scan / marketplace) into the asset pipeline.
#
#   ASSET=Barrel SRC=sources/tripo/Barrel/model.glb HEIGHT=0.9 TRIS=3000 tools/blender_run.sh blender/scripts/ingest_mesh.py
#
# Result: blender/assets/<ASSET>/<ASSET>.blend with collection SM_<ASSET> holding one clean object, ready for the normal
# stages: ASSET=<Name> tools/blender_run.sh blender/scripts/stages.py blender/assets/<Name>/<Name>.blend
# Steps (each fixes a problem seen with generated meshes):
#   1. import GLB/GLTF/FBX/OBJ; keep the largest mesh (or JOIN=1 joins every mesh)
#   2. normalise: apply transforms, bottom at z=0, centred on XY, scaled to HEIGHT (m) if given, rotated by YAW (deg)
#      so the front faces -Y (glTF +Z front arrives as Blender -Y already)
#   3. weld UV-seam splits (generators split the shell at every UV seam: thousands of loose pieces otherwise),
#      delete loose verts, dissolve degenerates, recalc normals outward
#   4. decimate (collapse) to TRIS if the mesh is over budget, then soft normals
#   5. rename the object/mesh/materials: SM_<ASSET>, materials M_<ASSET>[_n] (MATERIAL=MI_Shared to use a library name)
#   6. unpack the embedded textures to textures/<ASSET>/ (so they travel with the asset) and report the material maps
# What it does not fix: baked-in lighting in the textures, fused parts (fingers, cloaks), wrong scale guesses. Look at
# the review renders. For characters, rig with Tripo (`--then rig` / `--spec mixamo`) before ingesting, or rig in Blender.
import glob

SRC = os.environ["SRC"]
HEIGHT = float(os.environ.get("HEIGHT", "0") or 0)
TRIS = int(os.environ.get("TRIS", "0") or 0)
YAW = float(os.environ.get("YAW", "0") or 0)
JOIN = os.environ.get("JOIN", "0") == "1"
MATERIAL = os.environ.get("MATERIAL", "")
src = SRC if os.path.isabs(SRC) else os.path.join(ROOT, SRC)
if os.path.isdir(src):                                   # a tripo output folder (may hold <name>-<id>/ subfolders):
    found = [f for e in ("glb", "gltf", "fbx", "obj") for f in glob.glob(os.path.join(src, "**", "*." + e), recursive=True)]
    if not found: raise RuntimeError("no model file under " + src)
    newest = max(found, key=os.path.getmtime)                # the newest run's folder, then GLB before FBX/OBJ in it
    src = sorted([f for f in found if os.path.dirname(f) == os.path.dirname(newest)], key=lambda f: not f.endswith((".glb", ".gltf")))[0]
ext = os.path.splitext(src)[1].lower()

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'; bpy.context.scene.unit_settings.scale_length = 1.0
if ext in (".glb", ".gltf"): bpy.ops.import_scene.gltf(filepath=src)
elif ext == ".fbx": bpy.ops.import_scene.fbx(filepath=src, use_custom_normals=False)
elif ext == ".obj": bpy.ops.wm.obj_import(filepath=src)
else: raise RuntimeError(f"unsupported source {src}")

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not meshes: raise RuntimeError("no mesh in " + src)
for o in bpy.context.scene.objects: o.select_set(False)
if JOIN and len(meshes) > 1:
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]; bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
else:
    ob = max(meshes, key=lambda o: len(o.data.polygons))
# un-parent (glTF roots, armature-less empties) keeping the world transform, then drop everything else
mw = ob.matrix_world.copy(); ob.parent = None; ob.matrix_world = mw
for o in list(bpy.context.scene.objects):
    if o is not ob and o.type != 'ARMATURE': bpy.data.objects.remove(o, do_unlink=True)
ob.select_set(True); bpy.context.view_layer.objects.active = ob
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

me = ob.data
src_tris = sum(len(p.vertices) - 2 for p in me.polygons)
bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
bmesh.ops.dissolve_degenerate(bm, dist=1e-6, edges=bm.edges)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
# normalise placement
if YAW: bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(YAW), 3, 'Z'), verts=bm.verts)
lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)]); hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
bmesh.ops.translate(bm, vec=Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)), verts=bm.verts)
if HEIGHT > 0:
    s = HEIGHT / max(hi.z - lo.z, 1e-6)
    bmesh.ops.scale(bm, vec=(s, s, s), verts=bm.verts)
bm.to_mesh(me); bm.free()

tris = sum(len(p.vertices) - 2 for p in me.polygons)
if TRIS and tris > TRIS:
    md = ob.modifiers.new("Decimate", 'DECIMATE'); md.ratio = TRIS / tris; md.use_collapse_triangulate = True
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=md.name)
soft_normals(ob)
for uvl in me.uv_layers: uvl.name = "UVMap"; break

# names + collection
name = "SM_" + ASSET
ob.name = me.name = name
for c in list(ob.users_collection): c.objects.unlink(ob)
asset_root().objects.link(ob)
for i, slot in enumerate(ob.material_slots):
    if slot.material: slot.material.name = MATERIAL or (f"M_{ASSET}" + (f"_{i}" if i else ""))

# textures: unpack next to the asset sources so Unity gets real files
tex_dir = os.path.join(ROOT, "textures", ASSET); os.makedirs(tex_dir, exist_ok=True)
maps = {}
for slot in ob.material_slots:
    if not slot.material or not slot.material.node_tree: continue
    for n in slot.material.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image:
            sock = next((l.to_socket.name for l in n.outputs[0].links), "?") if n.outputs[0].links else "?"
            fn = os.path.join(tex_dir, f"{slot.material.name}_{sock.replace(' ', '')}.png")
            n.image.filepath_raw = fn; n.image.file_format = 'PNG'; n.image.save()
            maps[fn] = dict(socket=sock, size=list(n.image.size), colorspace=n.image.colorspace_settings.name)

blend = os.path.join(ROOT, "blender", "assets", ASSET, ASSET + ".blend")
os.makedirs(os.path.dirname(blend), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=blend)
me.calc_loop_triangles()
result = {"blend": blend, "source": src, "source_tris": src_tris, "tris": len(me.loop_triangles),
          "dimensions": [round(d, 3) for d in ob.dimensions], "materials": [s.material.name for s in ob.material_slots if s.material],
          "textures": maps, "armature": any(o.type == 'ARMATURE' for o in bpy.context.scene.objects)}
