# Ears in motion: Idle frame with the largest ear rotation vs rest, plus Run/Pounce, camera aimed at the head. No save.
import numpy as np
sc = bpy.context.scene; arm = bpy.data.objects["WhiteTiger_Rig"]; hd = bpy.data.objects["SM_WhiteTigerHD"]; lo = bpy.data.objects["SM_WhiteTiger"]
for o in sc.objects: o.hide_render = o is not hd
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 900, 900
w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.78, 0.82, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.8
sun = bpy.data.objects.new("RvSun", bpy.data.lights.new("RvSun", 'SUN')); sc.collection.objects.link(sun)
sun.data.energy = 3.5; sun.rotation_euler = (np.radians(50), 0, np.radians(-35))
cam = bpy.data.objects.new("RvCam", bpy.data.cameras.new("RvCam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 60
arm.animation_data.action = bpy.data.actions["Idle"]
best, bf = 0, 0
for f in range(0, 151, 3):
    sc.frame_set(f); a = arm.pose.bones["ear_L"].matrix_basis.to_quaternion().angle + arm.pose.bones["ear_R"].matrix_basis.to_quaternion().angle
    if a > best: best, bf = a, f
outs = []
for tag, act, f in (("rest", "Idle", 0), (f"flick{bf}", "Idle", bf), ("run6", "Run", 6), ("run12", "Run", 12), ("pounce20", "Pounce", 20)):
    arm.animation_data.action = bpy.data.actions[act]; sc.frame_set(f)
    hm = arm.matrix_world @ arm.pose.bones["head"].matrix; hp = hm.translation + hm.to_3x3() @ Vector((0, 0.12, 0.02))
    eye = hp + Vector((-0.75, -0.75, 0.35)); cam.location = eye; cam.rotation_euler = (hp - eye).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(ROOT, "renders", "WhiteTigerHD", f"ear_{tag}.png"); bpy.ops.render.render(write_still=True); outs.append(sc.render.filepath)
result = {"flick_frame": bf, "flick_angle_deg": round(float(np.degrees(best)), 1), "renders": [os.path.basename(o) for o in outs]}
