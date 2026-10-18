# Example asset: a reinforced wooden crate. Copy this folder to start a new asset.
#
#   ASSET=Crate tools/blender_run.sh blender/assets/Crate/build.py blender/assets/Crate/Crate.blend
#   ASSET=Crate STAGES=build,audit,review,export tools/blender_run.sh blender/assets/Crate/build.py ...
#
# Spec (fill this in first, see PROJECT.md for budgets):
#   Real size 0.6 x 0.6 x 0.6 m, pivot bottom centre, front -Y.  Budget LOD0 <= 2k tris, <= 2 materials.
#   Parts: 6 plank panels, 12 edge battens, 8 corner brackets, 16 bolts.
STAGES = os.environ.get("STAGES", "build,audit,review,export").split(",")

# ---- all dimensions as variables ----
W = D = H = 0.6
PLANK_T = 0.02          # panel thickness
BATTEN = 0.05           # edge batten width
BATTEN_T = 0.025
BRACKET = 0.09          # corner bracket leg length
BEVEL = 0.004

MATERIALS.update({
    "wood": ("MI_WoodPlanks", (0.45, 0.31, 0.18), 0.7),
    "metal": ("MI_SteelPainted", (0.18, 0.2, 0.2), 0.45, 1.0),
})

root = "SM_" + ASSET
result = {}

if "build" in STAGES:
    for c in ("Panels", "Battens", "Hardware"): clear_coll(c)
    # panels: one box per side, built in their local frame (grain = local X)
    box("Crate_Bottom", (W, D, PLANK_T), xf((0, 0, PLANK_T / 2)), "Panels", "wood", BEVEL)
    box("Crate_Top", (W, D, PLANK_T), xf((0, 0, H - PLANK_T / 2)), "Panels", "wood", BEVEL)
    for i, (x, y, rz) in enumerate(((0, -D / 2 + PLANK_T / 2, 0), (0, D / 2 - PLANK_T / 2, 0),
                                     (-W / 2 + PLANK_T / 2, 0, math.pi / 2), (W / 2 - PLANK_T / 2, 0, math.pi / 2))):
        box(f"Crate_Side{i}", (W - 2 * PLANK_T * (rz > 0), PLANK_T, H - 2 * PLANK_T), xf((x, y, H / 2), (0, 0, rz)),
            "Panels", "wood", BEVEL)
    # battens along the 12 edges, slightly proud of the panels (offset outward, never coplanar)
    o = BATTEN_T / 2 - 0.004
    bm = bmesh.new()
    for sx in (-1, 1):
        for sy in (-1, 1):
            box_bm(bm, (H, BATTEN, BATTEN), xf((sx * (W / 2 + o), sy * (D / 2 + o), H / 2), (0, math.pi / 2, 0)), BEVEL, seed=sx * 3 + sy)
    for z in (BATTEN / 2, H - BATTEN / 2):
        for s in (-1, 1):
            box_bm(bm, (W, BATTEN_T, BATTEN), xf((0, s * (D / 2 + o), z)), BEVEL, seed=int(z * 100) + s)
            box_bm(bm, (D, BATTEN_T, BATTEN), xf((s * (W / 2 + o), 0, z), (0, 0, math.pi / 2)), BEVEL, seed=int(z * 100) + 7 * s)
    finish("Crate_Battens", bm, "Battens", M("wood"))
    # corner brackets + bolts
    bm = bmesh.new()
    for sx in (-1, 1):
        for sy in (-1, 1):
            for z in (BRACKET / 2, H - BRACKET / 2):
                for rz, (dx, dy) in ((0, (0, sy)), (math.pi / 2, (sx, 0))):
                    p = Vector((sx * (W / 2 - BRACKET / 2 + 0.012), sy * (D / 2 + o + 0.012), z)) if dy else \
                        Vector((sx * (W / 2 + o + 0.012), sy * (D / 2 - BRACKET / 2 + 0.012), z))
                    box_bm(bm, (BRACKET, 0.004, BRACKET), xf(p, (0, 0, rz)), 0.0015, seed=1)
                    peg_bm(bm, p + Vector((dx, dy, 0)) * 0.002, (dx, dy, 0), r=0.008, h=0.006)
    finish("Crate_Brackets", bm, "Hardware", M("metal"))
    result["built"] = sorted(o.name for o in asset_root().all_objects)

if "audit" in STAGES:
    result["validate"] = {k: v for k, v in validate(root)["objects"].items()
                          if v["zero_area"] or v["loose_verts"] or v["zero_uv_faces"] or not v["scale_applied"]}
    result["audit"] = audit_asset(root)
    result["budget"] = check_budget(root, max_tris=2000, max_materials=2)

if "review" in STAGES:
    result["renders"] = review_views(root, tag=os.environ.get("TAG", "review"))

if "export" in STAGES:
    result["export"] = export_collection(root, os.path.join(ROOT, "blender", "export", "Props", ASSET),
                                         lods=((0.5, ()), (0.25, ())), ao=True,
                                         anchors={"top_center": (0, 0, H)})

bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath or os.path.join(ROOT, "blender", "assets", ASSET, ASSET + ".blend"))
