# Procedural low-poly rainforest vegetation set (one .blend, one SM_<Name> collection + export per asset).
#
#   BLENDER_LIBS="kit export audit review veg" tools/blender_run.sh blender/assets/Vegetation/build.py
#   STAGES=build,review ONLY=Palm_A,Fern_A ...        subset / stages
#
# Spec: flat-shaded facets (hard_angle 1 deg), flat colours per material (no textures), no collision (fixed viewer),
# no LODs (single fixed camera, all within ~150 m; far trees are cheap anyway). Pivot bottom centre.
# Budgets: ground plants <= 1.5k tris, palms/trees <= 3k, rocks <= 600.
STAGES = os.environ.get("STAGES", "build,audit,review,export").split(",")
ONLY = [s for s in os.environ.get("ONLY", "").split(",") if s]

# linear colours (Unity converts with .gamma); sRGB in comments
MATERIALS.update({
    "leaf":       ("MI_Leaf",       (0.076, 0.305, 0.019), 0.6),   # 78,150,38
    "stem":       ("MI_LeafStem",   (0.102, 0.223, 0.021), 0.6),   # 90,130,40
    "grass":      ("MI_Grass",      (0.155, 0.402, 0.026), 0.7),   # 110,170,45
    "canopy":     ("MI_Canopy",     (0.061, 0.262, 0.026), 0.8),   # 70,140,45
    "bark":       ("MI_Bark",       (0.141, 0.080, 0.045), 0.9),   # 105,80,60
    "palm_trunk": ("MI_PalmTrunk",  (0.188, 0.127, 0.070), 0.9),   # 120,100,75
    "flower":     ("MI_Heliconia",  (0.716, 0.021, 0.010), 0.5),   # 220,40,25
    "flower_tip": ("MI_HeliconiaTip", (0.787, 0.468, 0.020), 0.5), # 230,180,40
    "rock":       ("MI_Rock",       (0.305, 0.115, 0.045), 0.85),  # 150,95,60
    "vine":       ("MI_Vine",       (0.061, 0.091, 0.017), 0.8),   # 70,85,35
    "orchid":     ("MI_Orchid",     (0.871, 0.578, 0.791), 0.5),   # 240,200,230 pale pink
    "moss":       ("MI_Moss",       (0.111, 0.235, 0.020), 0.95),  # 94,133,38 (same as GroundDetail)
    "leaf_light": ("MI_LeafLight",  (0.246, 0.578, 0.032), 0.6),   # 136,200,50
})

# name -> (generator, kwargs, tri budget)
ASSETS = {
    "BigLeaf_A": (plant_bigleaf, dict(seed=11, n_leaves=7), 1500),
    "BigLeaf_B": (plant_bigleaf, dict(seed=23, n_leaves=9, leaf_len=(1.1, 1.7), stem_len=(0.6, 1.1)), 1500),
    "BigLeaf_Hero": (plant_bigleaf, dict(seed=37, n_leaves=8, leaf_len=(1.5, 2.2), leaf_w=0.3, stem_len=(0.5, 1.2), droop=1.4), 1500),
    "Banana_A": (plant_banana, dict(seed=5), 1500),
    "Banana_B": (plant_banana, dict(seed=8, height=3.0, n_leaves=7, leaf_len=2.4), 1500),
    "Fern_A": (plant_fern, dict(seed=3), 1500),
    "Fern_B": (plant_fern, dict(seed=9, n=12, frond_len=(0.9, 1.5)), 1500),
    "Heliconia_A": (plant_heliconia, dict(seed=4), 1500),
    "Heliconia_B": (plant_heliconia, dict(seed=17), 1500),
    "Grass_A": (plant_grass, dict(seed=2), 600),
    "Palm_A": (palm, dict(seed=1, height=12.0), 3000),
    "Palm_B": (palm, dict(seed=6, height=15.0, lean=0.18, n_fronds=11), 3000),
    "Palm_C": (palm, dict(seed=12, height=9.0, lean=0.35, n_fronds=9, frond_len=3.6), 3000),
    "Tree_A": (broadleaf_tree, dict(seed=21), 3000),
    "Tree_B": (broadleaf_tree, dict(seed=31, height=14.0, crown=5.5, n_clumps=9), 3000),
    "Rock_A": (rock, dict(seed=1, size=(1.2, 1.0, 0.8)), 600),
    "Rock_B": (rock, dict(seed=2, size=(0.7, 0.6, 0.45)), 600),
    "Rock_C": (rock, dict(seed=3, size=(2.0, 1.5, 1.6), subdiv=2, noise=0.25), 600),
    "Vine_A": (vine, dict(seed=1, length=6.0), 300),
    "Vine_B": (vine, dict(seed=2, length=9.0), 300),
    # epiphytes / lianas for the giant trunks (night shift 9)
    "Bromeliad_A": (bromeliad, dict(seed=41), 900),
    "Bromeliad_B": (bromeliad, dict(seed=43, n=18, leaf_len=(0.35, 0.55)), 900),
    "Orchid_A": (orchid, dict(seed=51), 900),
    "Moss_A": (moss_cushion, dict(seed=61), 600),
    "Moss_B": (moss_cushion, dict(seed=62, n=9, size=0.55), 600),
    "Liana_A": (liana, dict(seed=71), 900),
    "Liana_B": (liana, dict(seed=72, height=12.0, reach=3.0, thick=0.06), 900),
    "Reeds_A": (reeds, dict(seed=81), 900),
    "Reeds_B": (reeds, dict(seed=82, n=22, h=(0.9, 1.6)), 900),
    "LilyPads_A": (lily_pads, dict(seed=91), 600),
    "LilyPads_B": (lily_pads, dict(seed=92, n=11, spread=1.3), 600),
}
names = [n for n in ASSETS if not ONLY or n in ONLY]
result = {}

for name in names:
    gen, kw, budget = ASSETS[name]
    ASSET = name
    root = "SM_" + name
    cname = name + "_Parts"
    r = result.setdefault(name, {})
    if "build" in STAGES:
        clear_coll(cname); coll(cname, name)
        gen(cname, **kw)
    if "audit" in STAGES:
        r["budget"] = check_budget(root, max_tris=budget, max_materials=4)
    if "export" in STAGES:
        m = export_collection(root, os.path.join(ROOT, "blender", "export", "Nature", name), hulls=[], lods=(),
                              ao=False, glb=False, vegetation=True, hard_angle=1)
        r["tris"] = m["lods"]; r["dims"] = m["dimensions"]

if "review" in STAGES:
    # one line-up render of every asset (front-left 3/4 view), instead of 7 views x 20 assets
    sc = bpy.context.scene
    cam = lookdev_setup(res=(2400, 1000))
    sc.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.45, 0.62, 0.85, 1)
    lc = bpy.data.collections.get("Lineup") or bpy.data.collections.new("Lineup")
    if lc.name not in sc.collection.children: sc.collection.children.link(lc)
    for o in list(lc.objects): bpy.data.objects.remove(o, do_unlink=True)
    x = 0.0; maxh = 0.0
    for name in names:
        src = bpy.data.collections["SM_" + name]
        lo, hi = _bounds([o for o in src.all_objects if o.type == 'MESH'])
        w = hi.x - lo.x; maxh = max(maxh, hi.z)
        e = bpy.data.objects.new("L_" + name, None); e.instance_type = 'COLLECTION'; e.instance_collection = src
        e.location = (x - lo.x, 0, 0); lc.objects.link(e)
        x += w + 1.0
    solo("Lineup", "Lookdev")
    for n in names: bpy.data.collections["SM_" + n].hide_render = False
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(x, maxh * 2.6) * 1.04
    tilt = math.radians(72)
    cam.location = (x / 2, -200 * math.sin(tilt), maxh * 0.45 + 200 * math.cos(tilt)); cam.rotation_euler = (tilt, 0, 0)
    cam.data.clip_end = 1000
    sc.render.filepath = os.path.join(ROOT, "renders", "Vegetation", os.environ.get("TAG", "lineup") + ".png")
    os.makedirs(os.path.dirname(sc.render.filepath), exist_ok=True)
    bpy.ops.render.render(write_still=True)
    result["lineup"] = sc.render.filepath

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "blender", "assets", "Vegetation", "Vegetation.blend"))
