# Head close-ups of the UNWARPED HD source (bbox-normalised like build.py ingest) for a before/after check. No save.
import numpy as np
sc = bpy.context.scene
for o in list(sc.objects): bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, "sources/tiger/WhiteTigerHD/tripo_convert_edcb4360-f1c0-469c-ad04-d3c599e77d26.fbx"), use_custom_normals=False)
ob = next(o for o in sc.objects if o.type == 'MESH')
ob.data.transform(Matrix.Rotation(np.radians(-90), 4, 'Z') @ ob.matrix_world); ob.matrix_world = Matrix.Identity(4)
v = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get("co", v); H = v.reshape(-1, 3)
s = 1.05 / np.ptp(H[:, 2]); ch = (H.min(0) + H.max(0)) / 2; ch[2] = H[:, 2].min()
H = (H - ch) * s; H[:, 1] += float(os.environ.get("DY", "0.05"))     # raw head sits ~5 cm back of the warped one
ob.data.vertices.foreach_set("co", H.ravel()); ob.data.update(); ob.data.shade_smooth()
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 1400, 900
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.78, 0.82, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.8
sun = bpy.data.objects.new("RvSun", bpy.data.lights.new("RvSun", 'SUN')); sc.collection.objects.link(sun)
sun.data.energy = 3.5; sun.rotation_euler = (np.radians(50), 0, np.radians(-35))
cam = bpy.data.objects.new("RvCam", bpy.data.cameras.new("RvCam")); sc.collection.objects.link(cam); sc.camera = cam
outs = []
for name, loc, tgt, lens in (("head_front", (-0.25, -2.0, 1.0), (0.02, -0.9, 0.82), 85), ("head_side", (-1.3, -0.9, 0.95), (0.02, -0.9, 0.82), 85),
                             ("paws_hind", (0.9, 1.1, 0.25), (0.0, 0.35, 0.08), 60), ("throat", (-0.6, -1.5, 0.45), (0.0, -0.7, 0.62), 60)):
    cam.location = loc; cam.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = lens
    sc.render.filepath = os.path.join(ROOT, "renders", "WhiteTigerHD", f"raw_{name}.png"); bpy.ops.render.render(write_still=True); outs.append(sc.render.filepath)
result = outs
