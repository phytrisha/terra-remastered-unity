# Malayan tapir: HD (Weave: Nano Banana Pro side view -> Hunyuan 3D V3, ingested as TapirHD) + its faceted forest
# twin, one rig, Idle / Walk / Alert, export.
#
#   ASSET=TapirHD SRC=sources/weave/TapirHD/TapirHD.glb HEIGHT=1.127 TRIS=60000 JOIN=1 YAW=-90 tools/blender_run.sh blender/scripts/ingest_mesh.py
#   BLENDER_LIBS="kit export audit review quadruped" tools/blender_run.sh blender/assets/Tapir/build.py blender/assets/Tapir/Tapir.blend
#   STAGES=hd,rig,low,fur,idle,walk,alert,export,review (default: all). Re-runnable: everything is rebuilt each run.
#
# Spec
#   Use: ambient animal on the path behind the viewer (AnimalWander), seen from ~7-25 m as the faceted SK_Tapir; the
#        portal shows SK_TapirHD (same bones, same pose) and the white-room showcase uses it for close-ups
#   Real dimensions: ~2.0 m nose to tail, back 1.05-1.12 m (rump highest), short sturdy legs, drooping proboscis
#   Budget: HD 60k tris + 4K base colour / normal (one hero instance, like the HD tiger); forest twin 3k tris, 2 flat
#        colours (black, white saddle) sampled from the HD texture
#   Pivot / facing: bottom centre at origin, head toward -Y (Unity +Z)
#   Rig: bones fitted to the HD mesh (elbows 10-13 cm behind the shoulder-wrist line, stifles well ahead: legs at
#        85-92 % of full length at rest; nearly straight legs could not reach the ends of the stride and kicked back), (measured hoof / leg columns / back line / head / ears / tail), rest = the HD
#        model's staggered stance (right legs 16-22 cm back); the walk is centred on the pairs' mean ("neutral")
#   Clips (30 fps, in place): Idle 0-150 loop, Walk 0-32 loop (no-slide speed in the manifest), Alert 0-60 one-shot
STAGES = os.environ.get("STAGES", "hd,rig,low,fur,idle,walk,alert,export,review").split(",")

# shoulder / hip, elbow / stifle, wrist / hock, ground (Blender metres)
LEGS = {
    "FL": [(0.15, -0.10, 0.76), (0.14, 0.08, 0.48), (0.13, 0.00, 0.20), (0.12, -0.04, 0.0)],
    "FR": [(-0.15, -0.10, 0.76), (-0.14, 0.0, 0.48), (-0.13, -0.13, 0.20), (-0.13, -0.19, 0.0)],
    "BL": [(0.12, 0.74, 0.84), (0.12, 0.60, 0.58), (0.12, 0.83, 0.27), (0.12, 0.80, 0.0)],
    "BR": [(-0.12, 0.74, 0.84), (-0.12, 0.52, 0.58), (-0.12, 0.67, 0.27), (-0.13, 0.58, 0.0)],
}
BONES = {
    "root": ((0, 0, 0), (0, -0.3, 0), None, False),
    "hips": ((0, 0.62, 0.88), (0, 0.90, 0.82), "root", False),
    "spine1": ((0, 0.62, 0.88), (0, 0.18, 0.86), "hips", False),
    "spine2": ((0, 0.18, 0.86), (0, -0.22, 0.85), "spine1", True),
    "chest": ((0, -0.22, 0.85), (0, -0.48, 0.86), "spine2", True),
    "neck": ((0, -0.48, 0.86), (0, -0.64, 0.88), "chest", True),
    "head": ((0, -0.64, 0.88), (0, -0.86, 0.74), "neck", True),
    "snout": ((0, -0.86, 0.74), (0, -0.99, 0.60), "head", True),
    "ear_L": ((0.09, -0.56, 1.00), (0.18, -0.55, 1.10), "head", False),
    "ear_R": ((-0.09, -0.56, 1.00), (-0.18, -0.55, 1.10), "head", False),
    "tail1": ((0, 0.92, 0.88), (0, 0.99, 0.72), "hips", False),
}
for leg, pts in LEGS.items():
    par = "chest" if leg[0] == "F" else "hips"
    for i, seg in enumerate(("upper", "lower", "foot")):
        BONES[f"{seg}_{leg}"] = (pts[i], pts[i + 1], par if i == 0 else f"{('upper', 'lower')[i - 1]}_{leg}", i > 0)


def classify(samples):
    rgb = sum(samples, Vector((0, 0, 0))) / len(samples)
    return "white" if (rgb.x + rgb.y + rgb.z) / 3 > 0.38 else "black"


SPEC = {"bones": BONES, "tris": 3000, "zone_smooth": 2,
        "fur_len": {"face": 0.6, "snout": 0.3, "legs": 0.8, "ears": 0.7, "tail": 0.8}, "hoof_h": 0.05, "bare_lum": 0.0,   # sparse bristles all over (near-black coat: no dark-patch detection)
        "materials": {"black": ("M_TapirBlack", (0.045, 0.045, 0.05)), "white": ("M_TapirWhite", (0.70, 0.69, 0.66))},
        "neutral": {"FL": -0.115, "FR": -0.115, "BL": 0.69, "BR": 0.69},
        "walk": {"frames": 32, "duty": 0.65, "stance": 0.28, "swing": 0.07, "nod": 3.0, "roll": 2.0},
        "alert_neck": 9, "alert_head": 6, "alert_tail": 4}

result = q_pipeline_hd(SPEC, "Tapir", STAGES, classify, look_at=(0, -0.1, 0.6))
