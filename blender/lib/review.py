# review.py: fixed-view review renders, so every iteration of an asset is comparable. Needs kit.py.
#
#   review_views(root)   front, back, left, right (ortho), top (ortho), 3/4 and low-angle perspective
#   lookdev_setup()      neutral world + sun, AgX, grey ground; called by review_views
# Images go to renders/<Asset>/<tag>_<view>.png. Make contact sheets with tools/sheet.py.
# Always look at the back and sides too, not just the hero angle: holes and clashes hide there.

def _bounds(objs):
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            p = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, p)); hi = Vector(map(max, hi, p))
    return lo, hi


def lookdev_setup(engine='BLENDER_EEVEE', res=(1200, 1000), ground=True):
    sc = bpy.context.scene
    sc.render.engine = engine
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    sc.view_settings.view_transform = 'AgX'; sc.view_settings.exposure = 0.0
    w = sc.world or bpy.data.worlds.new("World"); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    if bg: bg.inputs["Color"].default_value = (0.62, 0.66, 0.72, 1); bg.inputs["Strength"].default_value = 0.8
    lc = bpy.data.collections.get("Lookdev") or bpy.data.collections.new("Lookdev")
    if lc.name not in sc.collection.children: sc.collection.children.link(lc)
    sun = bpy.data.objects.get("Lookdev_Sun")
    if not sun:
        sun = bpy.data.objects.new("Lookdev_Sun", bpy.data.lights.new("Lookdev_Sun", 'SUN')); lc.objects.link(sun)
    sun.data.energy = 3.0; sun.data.angle = math.radians(3); sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    fill = bpy.data.objects.get("Lookdev_Fill")          # from the opposite side, so shadowed faces still show form
    if not fill:
        fill = bpy.data.objects.new("Lookdev_Fill", bpy.data.lights.new("Lookdev_Fill", 'SUN')); lc.objects.link(fill)
    fill.data.energy = 1.0; fill.data.angle = math.radians(20); fill.rotation_euler = (math.radians(65), 0, math.radians(215))
    if ground and not bpy.data.objects.get("Lookdev_Ground"):
        me = bpy.data.meshes.new("Lookdev_Ground")
        bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=60); bm.to_mesh(me); bm.free()
        me.materials.append(mat("Lookdev_Grey", (0.35, 0.35, 0.35), 0.9))
        g = bpy.data.objects.new("Lookdev_Ground", me); g.location.z = -0.001; lc.objects.link(g)
    cam = bpy.data.objects.get("Lookdev_Cam")
    if not cam:
        cam = bpy.data.objects.new("Lookdev_Cam", bpy.data.cameras.new("Lookdev_Cam")); lc.objects.link(cam)
    sc.camera = cam
    return cam


def review_views(root_name, tag="review", views=("front", "back", "left", "right", "top", "threequarter", "low"),
                 out_dir=None, res=(1200, 1000)):
    """Render the asset from fixed views. Returns the written file paths."""
    sc = bpy.context.scene
    out_dir = out_dir or os.path.join(ROOT, "renders", root_name[3:] if root_name.startswith("SM_") else root_name)
    os.makedirs(out_dir, exist_ok=True)
    solo(root_name, "Lookdev")
    cam = lookdev_setup(res=res)
    lo, hi = _bounds([o for o in bpy.data.collections[root_name].all_objects if o.type == 'MESH'])
    c = (lo + hi) / 2; size = hi - lo; ext = max(size) * 1.15
    dirs = {"front": (0, -1, 0), "back": (0, 1, 0), "left": (-1, 0, 0), "right": (1, 0, 0), "top": (0, 0, 1)}
    files = []
    for v in views:
        if v in dirs:
            cam.data.type = 'ORTHO'; cam.data.ortho_scale = ext
            cam.location = c + Vector(dirs[v]) * (ext * 4 + 10)
            cam.data.clip_end = ext * 10 + 50
        else:
            cam.data.type = 'PERSP'; cam.data.lens = 50 if v == "threequarter" else 24
            d = ext * (2.2 if v == "threequarter" else 1.6)
            cam.location = c + (Vector((0.7, -1.0, 0.55)).normalized() * d if v == "threequarter"
                                else Vector((d * 0.55, -d * 0.8, -c.z + max(0.3, size.z * 0.05))))
        target = c if v != "low" else Vector((c.x, c.y, c.z * 1.1))
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(out_dir, f"{tag}_{v}.png")
        bpy.ops.render.render(write_still=True); files.append(sc.render.filepath)
    return files
