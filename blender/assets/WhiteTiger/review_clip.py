# Side-view (and optional extra) renders of one action at given frames: ACTION=Run FRAMES=0,5,10 VIEWS=left
arm = bpy.data.objects["WhiteTiger_Rig"]; arm.animation_data.action = bpy.data.actions[os.environ["ACTION"]]
sc = bpy.context.scene; files = []
solo("SM_WhiteTiger", "Lookdev"); cam = lookdev_setup(res=(700, 450))
for f in [int(x) for x in os.environ["FRAMES"].split(",")]:
    sc.frame_set(f)
    for v in os.environ.get("VIEWS", "left").split(","):
        cam.data.type = "ORTHO"; cam.data.ortho_scale = float(os.environ.get("ORTHO", "3.4"))
        pos = {"left": (-8, -0.1, 0.65), "front": (0, -8, 0.65), "threequarter": (-5, -5.5, 2.2)}[v]
        cam.location = pos; tgt = Vector((0, -0.1, float(os.environ.get("TZ", "0.6"))))
        if v == "threequarter": cam.data.type = 'PERSP'; cam.data.lens = 70
        cam.rotation_euler = (tgt - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(ROOT, "renders", "WhiteTiger", f"clip_{os.environ['ACTION']}_{v}_f{f:02d}.png")
        bpy.ops.render.render(write_still=True); files.append(sc.render.filepath)
result = files
