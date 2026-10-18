# Re-import the exported FBX in a clean scene: takes, frame ranges, bone count, rest pose vs the build spec.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, "blender/export/Characters/WhiteTiger/SK_WhiteTiger.fbx"))
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
me = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
ns = {}
exec(open(os.path.join(ROOT, "blender/assets/WhiteTiger/build.py")).read().split("ob = bpy.data.objects[MESH]")[0].split("import bmesh", 1)[1].replace("from mathutils import Quaternion, Euler", ""), globals(), ns)
spec = dict(ns["B"])
for i in range(6): spec[f"tail{i + 1}"] = (ns["TAIL"][i], ns["TAIL"][i + 1])
for leg, (par, pts) in ns["LEGS"].items():
    for i, sname in enumerate(("upper", "lower", "foot", "toe")): spec[f"{sname}_{leg}"] = (pts[i], pts[i + 1])
dev = 0.0
for b in arm.data.bones:
    if b.name in spec:
        h = arm.matrix_world @ b.head_local
        dev = max(dev, (h - Vector(spec[b.name][0])).length)
takes = {a.name: [round(x) for x in a.frame_range] for a in bpy.data.actions}
result = {"objects": [(o.name, o.type) for o in bpy.context.scene.objects], "bones": len(arm.data.bones),
          "bone_names_missing_from_spec": [b.name for b in arm.data.bones if b.name not in spec],
          "rest_head_max_dev_m": round(dev, 5), "takes": takes, "fps": bpy.context.scene.render.fps,
          "mesh_groups": len(me.vertex_groups), "tris": sum(len(p.vertices) - 2 for p in me.data.polygons)}
