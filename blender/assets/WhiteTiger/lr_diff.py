# Left/right difference per frame for each leg pair in Run: paw (wrist/hock target end) distance in the side plane
# (y, z) and the upper-leg angle difference. Small values = the pair moves together (bound-like).
arm = bpy.data.objects["WhiteTiger_Rig"]; arm.animation_data.action = bpy.data.actions["Run"]; sc = bpy.context.scene
rows = {"hind": [], "fore": []}
for f in range(18):
    sc.frame_set(f); pb = arm.pose.bones
    for pair, (l, r) in (("hind", ("BL", "BR")), ("fore", ("FL", "FR"))):
        pl = arm.matrix_world @ pb[f"foot_{l}"].tail; pr = arm.matrix_world @ pb[f"foot_{r}"].tail
        dl = (pb[f"upper_{l}"].tail - pb[f"upper_{l}"].head).normalized(); dr = (pb[f"upper_{r}"].tail - pb[f"upper_{r}"].head).normalized()
        ang = math.degrees(math.atan2(dl.y, -dl.z) - math.atan2(dr.y, -dr.z))
        rows[pair].append((round(math.hypot(pl.y - pr.y, pl.z - pr.z), 2), round(ang)))
result = {k: {"min_paw_dist_m": min(d for d, a in v), "min_abs_upper_deg": min(abs(a) for d, a in v), "per_frame": v} for k, v in rows.items()}
