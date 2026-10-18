# Head height / pitch over the Run cycle (is it locked on the target?) and paw trajectories (arcs).
arm = bpy.data.objects["WhiteTiger_Rig"]; arm.animation_data.action = bpy.data.actions["Run"]; sc = bpy.context.scene
hz, hp, paws = [], [], {l: [] for l in ("FL", "BL")}
for f in range(18):
    sc.frame_set(f); h = arm.pose.bones["head"]
    hz.append(round((arm.matrix_world @ h.head).z, 3))
    d = (h.tail - h.head).normalized(); hp.append(round(math.degrees(math.asin(d.z)), 1))
    for l in paws: p = arm.matrix_world @ arm.pose.bones[f"foot_{l}"].tail; paws[l].append((round(p.y, 2), round(p.z, 2)))
result = {"head_z": hz, "head_pitch_deg": hp, "paw_path_y_z": paws}
