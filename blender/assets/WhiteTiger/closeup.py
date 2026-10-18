# Close-up renders: head (ear flick) and hindquarters/tail at given frames.
sc = bpy.context.scene
solo("SM_WhiteTiger", "Lookdev"); cam = lookdev_setup(res=(800, 600))
cam.data.type = 'PERSP'; cam.data.lens = 50
out = os.path.join(ROOT, "renders", "WhiteTiger"); files = []
shots = {"head": ((0.9, -1.6, 1.15), (0.02, -0.85, 0.9)), "tail": ((1.2, 1.8, 0.9), (0.0, 0.7, 0.4)), "legs": ((1.6, -0.2, 0.5), (0.02, -0.1, 0.35))}
for f in [int(x) for x in os.environ.get("FRAMES", "0,64").split(",")]:
    sc.frame_set(f)
    for n, (p, t) in shots.items():
        cam.location = p; cam.rotation_euler = (Vector(t) - Vector(p)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(out, f"close_{n}_f{f}.png"); bpy.ops.render.render(write_still=True); files.append(sc.render.filepath)
result = files
