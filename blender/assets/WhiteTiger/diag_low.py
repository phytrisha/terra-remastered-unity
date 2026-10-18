# Which bones own the lowest vertices at given action frames (floor penetration check).
arm = bpy.data.objects["WhiteTiger_Rig"]; ob = bpy.data.objects["SM_WhiteTiger"]
out = {}
for spec in os.environ["CHECK"].split(";"):
    a, f = spec.split(":"); f = int(f)
    arm.animation_data.action = bpy.data.actions[a]; bpy.context.scene.frame_set(f)
    dg = bpy.context.evaluated_depsgraph_get(); e = ob.evaluated_get(dg); me = e.to_mesh()
    zs = sorted(((e.matrix_world @ v.co).z, v.index) for v in me.vertices)[:40]
    e.to_mesh_clear()
    own = {}
    for z, i in zs:
        if z > -0.005: break
        v = ob.data.vertices[i]; g = max(v.groups, key=lambda x: x.weight); n = ob.vertex_groups[g.group].name
        own[n] = min(own.get(n, 9), round(z, 3))
    pb = arm.pose.bones
    out[spec] = {"low": own, "paw_z": {l: round((arm.matrix_world @ pb[f"foot_{l}"].tail).z, 3) for l in ("FL", "FR", "BL", "BR")},
                 "toe_tip_z": {l: round((arm.matrix_world @ pb[f"toe_{l}"].tail).z, 3) for l in ("FL", "FR", "BL", "BR")}}
result = out
