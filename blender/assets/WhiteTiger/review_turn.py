# Top + 3/4 renders of a root-motion walk, camera following the root: ACTION=WalkTurnL FRAMES=0,9,18,27
arm = bpy.data.objects["WhiteTiger_Rig"]; arm.animation_data.action = bpy.data.actions[os.environ["ACTION"]]
sc = bpy.context.scene; files = []
solo("SM_WhiteTiger", "Lookdev"); cam = lookdev_setup(res=(700, 520))
for f in [int(x) for x in os.environ["FRAMES"].split(",")]:
    sc.frame_set(f); r = arm.matrix_world @ arm.pose.bones["root"].head
    for v, off in (("top", Vector((0, 0, 6))), ("q", Vector((-3.2, -3.6, 2.4)))):
        cam.data.type = 'ORTHO' if v == "top" else 'PERSP'; cam.data.ortho_scale = 3.6; cam.data.lens = 55
        cam.location = r + off; tgt = r + Vector((0, 0, 0.5 if v != "top" else 0))
        cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(ROOT, "renders", "WhiteTiger", f"turn_{os.environ['ACTION']}_{v}_f{f:02d}.png")
        bpy.ops.render.render(write_still=True); files.append(sc.render.filepath)
result = len(files)
