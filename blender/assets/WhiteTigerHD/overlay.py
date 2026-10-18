# Review: low-poly (red) vs HD (cyan) silhouettes, x-ray workbench, ortho side/top/front (no save).
# HD placement: env HD_OBJ (an object already in the file) or the raw source with the bbox normalisation of align_probe.
import numpy as np
TAG = os.environ.get("TAG", "overlay")
lo = bpy.data.objects["SM_WhiteTiger"]
hd = bpy.data.objects.get(os.environ.get("HD_OBJ", ""))
if hd is None:
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, "sources/tiger/WhiteTigerHD/tripo_convert_edcb4360-f1c0-469c-ad04-d3c599e77d26.fbx"), use_custom_normals=False)
    hd = [o for o in bpy.data.objects if o not in before and o.type == 'MESH'][0]
    LO = np.array([lo.matrix_world @ v.co for v in lo.data.vertices])
    H = np.array([hd.matrix_world @ v.co for v in hd.data.vertices]); H = H[:, [1, 0, 2]] * [1, -1, 1]   # yaw -90
    s0 = np.ptp(LO[:, 2]) / np.ptp(H[:, 2])
    c_lo = (LO.min(0) + LO.max(0)) / 2; c_h = (H.min(0) + H.max(0)) / 2; c_lo[2] = LO[:, 2].min(); c_h[2] = H[:, 2].min()
    hd.matrix_world = Matrix.Identity(4); hd.data.vertices.foreach_set("co", ((H - c_h) * s0 + c_lo).ravel()); hd.data.update()
for o in bpy.context.scene.objects: o.hide_render = o not in (lo, hd)
lo.color = (1, 0.15, 0.1, 1); hd.color = (0.1, 0.8, 1, 1)
sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading; sh.light = 'FLAT'; sh.color_type = 'OBJECT'; sh.show_xray = True; sh.xray_alpha = 0.5
sc.render.resolution_x, sc.render.resolution_y = 1000, 700; sc.render.film_transparent = False
sc.world = sc.world or bpy.data.worlds.new("W"); sh.background_type = 'VIEWPORT'; sh.background_color = (1, 1, 1)
cam = bpy.data.objects.new("OvCam", bpy.data.cameras.new("OvCam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
outs = []
for name, loc, rot, scale in (("side", (3, 0, 0.55), (np.pi / 2, 0, np.pi / 2), 2.4), ("top", (0, 0, 4), (0, 0, 0), 2.4),
                              ("front", (0, -4, 0.55), (np.pi / 2, 0, 0), 1.3)):
    cam.location = loc; cam.rotation_euler = rot; cam.data.ortho_scale = scale
    sc.render.filepath = os.path.join(ROOT, "renders", "WhiteTigerHD", f"{TAG}_{name}.png")
    bpy.ops.render.render(write_still=True); outs.append(sc.render.filepath)
result = outs
