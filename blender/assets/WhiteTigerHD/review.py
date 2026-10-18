# Review renders: HD (left) next to the low-poly (right) in the same pose; head close-up; flipped-face locations. No save.
import numpy as np
sc = bpy.context.scene
hd = bpy.data.objects["SM_WhiteTigerHD"]; lo = bpy.data.objects["SM_WhiteTiger"]; arm = bpy.data.objects["WhiteTiger_Rig"]
for o in sc.objects: o.hide_render = o not in (hd, lo)
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 1400, 900
w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.78, 0.82, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.8
sun = bpy.data.objects.new("RvSun", bpy.data.lights.new("RvSun", 'SUN')); sc.collection.objects.link(sun)
sun.data.energy = 3.5; sun.rotation_euler = (np.radians(50), 0, np.radians(-35))
cam = bpy.data.objects.new("RvCam", bpy.data.cameras.new("RvCam")); sc.collection.objects.link(cam); sc.camera = cam
SEP = 0.7   # lo shifted +X (tiger's left in the front view); armature modifiers keep world offsets via a parent empty
off = bpy.data.objects.new("LoOff", None); sc.collection.objects.link(off)
lo_copy = lo.copy(); sc.collection.objects.link(lo_copy); lo_copy.parent = None; lo_copy.location = (SEP, 0, 0); lo_copy.hide_render = False
lo.hide_render = True
def look(loc, tgt, lens=50):
    cam.location = loc; d = Vector(tgt) - Vector(loc); cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = lens
outs = []
frames = [("rest", "Idle", 0), ("pounce", "Pounce", int(os.environ.get("PF", "20"))), ("run", "Run", 6)]
for tag, act, f in frames:
    arm.animation_data.action = bpy.data.actions[act]; sc.frame_set(f)
    for name, loc, tgt, lens in (("q", (-2.4, -3.4, 1.5), (SEP / 2, 0, 0.55), 45), ("side", (-4.2, 0, 0.7), (SEP / 2, 0, 0.55), 45)):
        if tag != "rest" and name == "side": continue
        look(loc, tgt, lens); sc.render.filepath = os.path.join(ROOT, "renders", "WhiteTigerHD", f"rv_{tag}_{name}.png")
        bpy.ops.render.render(write_still=True); outs.append(sc.render.filepath)
arm.animation_data.action = bpy.data.actions["Idle"]; sc.frame_set(0)
lo_copy.hide_render = True
for name, loc, tgt, lens in (("head_front", (-0.25, -2.0, 1.0), (0.02, -0.9, 0.82), 85), ("head_side", (-1.3, -0.9, 0.95), (0.02, -0.9, 0.82), 85),
                             ("paws_hind", (0.9, 1.1, 0.25), (0.0, 0.35, 0.08), 60), ("throat", (-0.6, -1.5, 0.45), (0.0, -0.7, 0.62), 60), ("shoulder_R", (-1.1, -1.3, 0.75), (-0.1, -0.62, 0.62), 60),
                             ("belly", (0.9, 0.2, 0.15), (0.0, 0.3, 0.42), 50)):
    look(loc, tgt, lens); sc.render.filepath = os.path.join(ROOT, "renders", "WhiteTigerHD", f"rv_{name}.png")
    bpy.ops.render.render(write_still=True); outs.append(sc.render.filepath)
result = outs
