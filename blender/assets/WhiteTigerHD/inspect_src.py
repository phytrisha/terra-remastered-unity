# Compare the HD tiger source with the low-poly source: objects, tris, bounds, materials, armature (read-only).
srcs = {"lo": "sources/tiger/WhiteTiger/white+tiger+3d+model.fbx",
        "hd": "sources/tiger/WhiteTigerHD/tripo_convert_edcb4360-f1c0-469c-ad04-d3c599e77d26.fbx"}
out = {}
for k, p in srcs.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, p), use_custom_normals=False)
    objs = []
    for o in bpy.context.scene.objects:
        d = {"name": o.name, "type": o.type, "parent": o.parent.name if o.parent else None,
             "rot": [round(a, 3) for a in o.rotation_euler], "scale": [round(a, 4) for a in o.scale]}
        if o.type == 'MESH':
            o.data.calc_loop_triangles()
            ws = [o.matrix_world @ v.co for v in o.data.vertices]
            mn = [round(min(v[i] for v in ws), 3) for i in range(3)]; mx = [round(max(v[i] for v in ws), 3) for i in range(3)]
            d.update(tris=len(o.data.loop_triangles), verts=len(o.data.vertices), min=mn, max=mx,
                     mats=[s.material.name for s in o.material_slots if s.material], vgroups=len(o.vertex_groups),
                     uv=[u.name for u in o.data.uv_layers], shapekeys=bool(o.data.shape_keys))
        if o.type == 'ARMATURE': d["bones"] = len(o.data.bones)
        objs.append(d)
    out[k] = {"objects": objs, "actions": [a.name for a in bpy.data.actions],
              "images": [(i.name, list(i.size)) for i in bpy.data.images]}
result = out
