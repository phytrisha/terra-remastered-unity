# Procedural low-poly forest-floor detail for the rainforest path (one .blend, one SM_<Name> collection per asset).
#
#   BLENDER_LIBS="kit export audit review veg" tools/blender_run.sh blender/assets/GroundDetail/build.py \
#       blender/assets/GroundDetail/GroundDetail.blend
#   STAGES=build,review ONLY=Litter_A ...        subset / stages
#
# Spec: same style as Vegetation (flat-shaded facets, flat colours, no textures, no collision, no LODs: fixed viewer,
# all within ~40 m). Pivot bottom centre, ground at z = 0. Seen from 1.65 m eye height at 0.5-30 m.
# Budgets: litter/twigs/cover <= 400 tris, mushrooms <= 400, roots <= 600, logs <= 900.
#   Litter_A/B/C   fallen leaf patches ~1-1.6 m (dry / dead / yellow-green leaves lying flat)
#   Twig_A/B       fallen branches 1.2-2 m with side twigs
#   Log_A/B        mossy fallen logs 3-4.5 m, broken ends, bracket fungus
#   Mushroom_A/B   clusters of 3-6 small mushrooms (orange caps / pale caps)
#   Roots_A/B      surface roots snaking out 3-5 m, half sunk (for the buttress-tree bases)
#   Cover_A/B      low sprout / seedling patches ~0.8 m (break up the flat ground facets)
STAGES = os.environ.get("STAGES", "build,audit,review,export").split(",")
ONLY = [s for s in os.environ.get("ONLY", "").split(",") if s]

# linear colours (Unity converts with .gamma); sRGB in comments
MATERIALS.update({
    "leaf":       ("MI_Leaf",        (0.076, 0.305, 0.019), 0.6),   # 78,150,38   (existing library)
    "leaf_light": ("MI_LeafLight",   (0.246, 0.578, 0.032), 0.6),   # 136,200,50  (existing)
    "stem":       ("MI_LeafStem",    (0.102, 0.223, 0.021), 0.6),   # 90,130,40   (existing)
    "bark":       ("MI_Bark",        (0.141, 0.080, 0.045), 0.9),   # 105,80,60   (existing)
    "dry":        ("MI_LeafDry",     (0.305, 0.129, 0.030), 0.85),  # 150,100,50  new
    "dead":       ("MI_LeafDead",    (0.147, 0.065, 0.024), 0.9),   # 107,73,43   new
    "litter_y":   ("MI_LeafLitterYellow", (0.402, 0.300, 0.040), 0.8),  # 170,150,56 new
    "moss":       ("MI_Moss",        (0.111, 0.235, 0.020), 0.95),  # 94,133,38   new
    "cap":        ("MI_MushroomCap", (0.708, 0.198, 0.040), 0.6),   # 219,123,56  new
    "cap_pale":   ("MI_MushroomPale", (0.745, 0.638, 0.456), 0.6),  # 225,210,180 new
    "fungus_stem": ("MI_MushroomStem", (0.807, 0.745, 0.590), 0.7), # 232,224,203 new
})


def _up_faces(bm):
    """Leaves lying on the ground must face up (single-sided URP Lit)."""
    bm.normal_update()
    for f in bm.faces:
        if f.normal.z < 0: f.normal_flip()


def litter(cname, seed, n=16, radius=0.7, mats=("dry", "dead", "litter_y")):
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(n):
        r = radius * math.sqrt(rnd.random()); a = rnd.uniform(0, 2 * math.pi)
        base = Vector((math.cos(a) * r, math.sin(a) * r, rnd.uniform(0.012, 0.03)))
        ln = rnd.uniform(0.13, 0.26)
        pts = spine(base, rnd.uniform(0, 2 * math.pi), rnd.uniform(-0.05, 0.12), ln, rnd.uniform(-0.1, 0.25), 3, rnd=rnd)
        for p in pts: p.z = max(p.z, 0.008)
        leaf_bm(bm, pts, ln * rnd.uniform(0.2, 0.32), fold=0.25, shape=0.85, rnd=rnd,
                mat_idx=rnd.choices((0, 1, 2), weights=(5, 3, 2))[0], roll=rnd.uniform(-0.25, 0.25))
    _up_faces(bm)
    emit(f"Litter_{seed}", bm, cname, mats)


def twig(cname, seed, length=1.5, radius=0.035, mats=("bark",)):
    rnd = random.Random(seed)
    bm = bmesh.new()
    h = rnd.uniform(0, math.pi)
    d = Vector((math.cos(h), math.sin(h), 0))
    pts = [d * (-length / 2) + Vector((0, 0, radius))]
    for i in range(5):
        pts.append(pts[-1] + (d + Vector((rnd.uniform(-.25, .25), rnd.uniform(-.25, .25), 0))).normalized() * length / 5)
        pts[-1].z = radius * (1 - 0.1 * i)
    tube_bm(bm, pts, [radius * (1 - 0.13 * i) for i in range(6)], sides=4, rnd=rnd)
    for i in (1, 2, 3, 4):
        if rnd.random() < 0.3: continue
        s = rnd.choice((-1, 1)); side = Vector((-d.y, d.x, 0)) * s
        b = [pts[i]]
        for k in range(2):
            b.append(b[-1] + (side + d * 0.8).normalized() * length * rnd.uniform(0.1, 0.16) + Vector((0, 0, 0.03 * (k == 0))))
        tube_bm(bm, b, [radius * 0.55, radius * 0.4, radius * 0.25], sides=3, rnd=rnd)
    emit(f"Twig_{seed}", bm, cname, mats)


def log(cname, seed, length=3.6, radius=0.26, mats=("bark", "moss", "cap_pale")):
    rnd = random.Random(seed)
    bm = bmesh.new()
    pts = []
    for i in range(6):
        t = i / 5
        pts.append(Vector(((t - 0.5) * length, rnd.uniform(-0.06, 0.06), radius * 0.82 + math.sin(t * 3) * 0.03)))
    radii = [radius * (1.08 - 0.25 * (i / 5)) for i in range(6)]
    tube_bm(bm, pts, radii, sides=7, rnd=rnd, jitter=0.08)
    # broken stub branch
    b0 = pts[3]; tube_bm(bm, [b0, b0 + Vector((0.15, 0.3, 0.35))], [radius * 0.35, radius * 0.25], sides=5, rnd=rnd)
    # moss on top (flattened blobs) and bracket fungus on the side
    for i in range(4):
        c = pts[1 + i] + Vector((0, rnd.uniform(-.05, .05), radii[1 + i] * 0.78))
        blob_bm(bm, c, (length * 0.13, radius * 0.75, radius * 0.22), subdiv=1, noise=0.25, rnd=rnd, mat_idx=1)
    for i in range(rnd.randint(2, 4)):
        p = pts[rnd.randint(1, 4)] + Vector((rnd.uniform(-.3, .3), -radius * 0.95, rnd.uniform(-0.02, 0.12)))
        blob_bm(bm, p, (0.09, 0.06, 0.025), subdiv=1, noise=0.2, rnd=rnd, mat_idx=2)
    emit(f"Log_{seed}", bm, cname, mats)


def mushrooms(cname, seed, n=5, scale=1.0, mats=("fungus_stem", "cap")):
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(n):
        a = rnd.uniform(0, 2 * math.pi); r = rnd.uniform(0, 0.16) * scale
        base = Vector((math.cos(a) * r, math.sin(a) * r, 0))
        h = rnd.uniform(0.05, 0.16) * scale
        top = base + Vector((rnd.uniform(-.02, .02), rnd.uniform(-.02, .02), h))
        tube_bm(bm, [base - Vector((0, 0, 0.01)), top], [0.012 * scale, 0.009 * scale], sides=5, rnd=rnd)
        cr = rnd.uniform(0.03, 0.065) * scale
        blob_bm(bm, top + Vector((0, 0, cr * 0.2)), (cr, cr, cr * 0.55), subdiv=1, noise=0.1, rnd=rnd, mat_idx=1,
                flatten_bottom=0.1)
    emit(f"Mushroom_{seed}", bm, cname, mats)


def roots(cname, seed, n=3, length=4.0, radius=0.14, mats=("bark", "moss")):
    """Surface roots from the origin outward (+X half-plane fan), arching above ground and sinking at the tip."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    for k in range(n):
        h = rnd.uniform(-0.7, 0.7)
        ln = length * rnd.uniform(0.6, 1.0)
        pts = []
        for i in range(7):
            t = i / 6
            hh = h + math.sin(t * 5 + k) * 0.12
            pts.append(Vector((math.cos(hh) * ln * t, math.sin(hh) * ln * t,
                               radius * (0.55 + 0.6 * math.sin(math.pi * min(1, t * 1.3))) - 0.12 * t * t)))
        rr = [radius * (1.15 - 0.85 * i / 6) for i in range(7)]
        tube_bm(bm, pts, rr, sides=5, rnd=rnd, jitter=0.1, cap=False)
        if rnd.random() < 0.7:   # moss patch on the hump
            c = pts[2] + Vector((0, 0, rr[2] * 0.8))
            blob_bm(bm, c, (0.22, 0.12, 0.04), subdiv=1, noise=0.25, rnd=rnd, mat_idx=1)
    emit(f"Roots_{seed}", bm, cname, mats)


def cover(cname, seed, n=11, radius=0.42, mats=("leaf", "leaf_light", "stem")):
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(n):
        r = radius * math.sqrt(rnd.random()); a = rnd.uniform(0, 2 * math.pi)
        base = Vector((math.cos(a) * r, math.sin(a) * r, 0))
        h = rnd.uniform(0.04, 0.14)
        tube_bm(bm, [base, base + Vector((0, 0, h))], [0.006, 0.004], sides=3, rnd=rnd, mat_idx=2)
        for j in range(rnd.randint(2, 3)):
            ln = rnd.uniform(0.08, 0.15)
            pts = spine(base + Vector((0, 0, h)), a + j * 2.2 + rnd.uniform(-.4, .4), rnd.uniform(0.2, 0.6), ln, 0.7, 2, rnd=rnd)
            leaf_bm(bm, pts, ln * 0.38, fold=0.3, shape=0.8, rnd=rnd, mat_idx=rnd.choice((0, 1)))
    emit(f"Cover_{seed}", bm, cname, mats)


# name -> (generator, kwargs, tri budget)
ASSETS = {
    "Litter_A": (litter, dict(seed=1), 400),
    "Litter_B": (litter, dict(seed=2, n=22, radius=0.85), 450),
    "Litter_C": (litter, dict(seed=3, n=12, radius=0.5, mats=("litter_y", "dry", "dead")), 400),
    "Twig_A": (twig, dict(seed=1), 400),
    "Twig_B": (twig, dict(seed=5, length=2.1, radius=0.045), 400),
    "Log_A": (log, dict(seed=1), 900),
    "Log_B": (log, dict(seed=4, length=4.6, radius=0.33), 900),
    "Mushroom_A": (mushrooms, dict(seed=2), 400),
    "Mushroom_B": (mushrooms, dict(seed=7, n=4, scale=1.4, mats=("fungus_stem", "cap_pale")), 400),
    "Roots_A": (roots, dict(seed=1), 600),
    "Roots_B": (roots, dict(seed=3, n=2, length=5.0, radius=0.18), 600),
    "Cover_A": (cover, dict(seed=1), 600),
    "Cover_B": (cover, dict(seed=4, n=14, radius=0.55), 700),
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
    # one line-up render (front 3/4 from above, like the viewer looking down at the floor)
    sc = bpy.context.scene
    cam = lookdev_setup(res=(2400, 900))
    sc.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.45, 0.62, 0.85, 1)
    lc = bpy.data.collections.get("Lineup") or bpy.data.collections.new("Lineup")
    if lc.name not in sc.collection.children: sc.collection.children.link(lc)
    for o in list(lc.objects): bpy.data.objects.remove(o, do_unlink=True)
    x = 0.0; maxh = 0.0
    for name in names:
        src = bpy.data.collections["SM_" + name]
        lo, hi = _bounds([o for o in src.all_objects if o.type == 'MESH'])
        w = hi.x - lo.x; maxh = max(maxh, hi.z, 0.6)
        e = bpy.data.objects.new("L_" + name, None); e.instance_type = 'COLLECTION'; e.instance_collection = src
        e.location = (x - lo.x, 0, 0); lc.objects.link(e)
        x += w + 0.4
    solo("Lineup", "Lookdev")
    for n in names: bpy.data.collections["SM_" + n].hide_render = False
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = x * 1.04
    tilt = math.radians(55)
    cam.location = (x / 2, -200 * math.sin(tilt), 200 * math.cos(tilt)); cam.rotation_euler = (tilt, 0, 0)
    cam.data.clip_end = 1000
    sc.render.filepath = os.path.join(ROOT, "renders", "GroundDetail", os.environ.get("TAG", "lineup") + ".png")
    os.makedirs(os.path.dirname(sc.render.filepath), exist_ok=True)
    bpy.ops.render.render(write_still=True)
    result["lineup"] = sc.render.filepath

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "blender", "assets", "GroundDetail", "GroundDetail.blend"))
