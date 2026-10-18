# Generic audit -> review -> export for any asset .blend whose meshes live in collection SM_<ASSET>
# (ingested meshes, hand-modelled files, or a build script that only does the "build" stage).
#
#   ASSET=Barrel CATEGORY=Props BUDGET=3000 tools/blender_run.sh blender/scripts/stages.py blender/assets/Barrel/Barrel.blend
#
# Env: STAGES (default audit,review,export), CATEGORY (Props|Buildings|Nature|Characters... -> export folder),
#      BUDGET (LOD0 tris), MAX_MATS (default 3), LODS ("0.5,0.25"), AO (1/0), HULLS (1 = one hull, 0 = none),
#      VEGETATION=1 (keep authored normals, FBX_SCALE_ALL + bake_space_transform), TAG (review file prefix).
STAGES = os.environ.get("STAGES", "audit,review,export").split(",")
root = "SM_" + ASSET
if root not in bpy.data.collections: raise RuntimeError(f"collection {root} not in {bpy.data.filepath}")
result = {}
if "audit" in STAGES:
    v = validate(root)
    result["validate_issues"] = {k: x for k, x in v["objects"].items()
                                 if x["zero_area"] or x["loose_verts"] or x["zero_uv_faces"] or not x["uv_layers"]
                                 or not x["scale_applied"] or not x["rot_applied"]}
    result["missing_images"] = v["missing_images"]
    result["audit"] = audit_asset(root)
    if os.environ.get("BUDGET"):
        result["budget"] = check_budget(root, int(os.environ["BUDGET"]), int(os.environ.get("MAX_MATS", "3")))
if "review" in STAGES:
    result["renders"] = review_views(root, tag=os.environ.get("TAG", "review"))
if "export" in STAGES:
    lods = tuple((float(r), ()) for r in os.environ.get("LODS", "0.5,0.25").split(",") if r)
    result["export"] = export_collection(root, os.path.join(ROOT, "blender", "export", os.environ.get("CATEGORY", "Props"), ASSET),
                                         lods=lods, ao=os.environ.get("AO", "1") == "1",
                                         hulls=None if os.environ.get("HULLS", "1") == "1" else [],
                                         vegetation=os.environ.get("VEGETATION") == "1")
    bpy.ops.wm.save_mainfile()
