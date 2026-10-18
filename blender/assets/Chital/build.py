# Chital doe (spotted deer): HD (Weave: Nano Banana Pro side view -> Hunyuan 3D V3, ingested as ChitalHD) + its
# faceted forest twin, one rig, Idle / Walk / Alert, export.
#
#   ASSET=ChitalHD SRC=sources/weave/ChitalHD/ChitalHD.glb HEIGHT=1.351 TRIS=60000 JOIN=1 YAW=90 tools/blender_run.sh blender/scripts/ingest_mesh.py
#   BLENDER_LIBS="kit export audit review quadruped" tools/blender_run.sh blender/assets/Chital/build.py blender/assets/Chital/Chital.blend
#   STAGES=hd,rig,low,fur,idle,walk,alert,export,review (default: all). Re-runnable: everything is rebuilt each run.
#
# Spec
#   Use: ambient animal on the path behind the viewer (AnimalWander), faceted SK_Chital in the forest, SK_ChitalHD
#        through the portal and in the white-room showcase
#   Real dimensions: doe ~1.65 m nose to tail, back ~1.1 m, ear tips 1.35 m, slender legs, no antlers
#   Budget: HD 60k tris + 4K base colour / normal; forest twin 2.6k tris, 3 flat colours (rufous coat, white belly /
#        throat / spots, dark dorsal line / muzzle / hooves) sampled from the HD texture, spots as single facets
#   Pivot / facing: bottom centre at origin, head toward -Y (Unity +Z)
#   Rig: bones fitted to the HD mesh (elbows 11 cm back, stifles 14 cm forward: legs at
#        ~80-93 % of full length so the IK reaches the ends of the stride); rest = the model's stance (right hind leg 12 cm forward), walk centred on the
#        pairs' mean ("neutral")
#   Clips (30 fps, in place): Idle 0-150 loop, Walk 0-30 loop, Alert 0-60 one-shot (head up, ears forward, tail flag)
STAGES = os.environ.get("STAGES", "hd,rig,low,fur,idle,walk,alert,export,review").split(",")

LEGS = {
    "FL": [(0.10, -0.12, 0.86), (0.095, 0.0, 0.58), (0.09, -0.105, 0.28), (0.095, -0.15, 0.0)],
    "FR": [(-0.12, -0.12, 0.86), (-0.12, 0.0, 0.58), (-0.125, -0.105, 0.28), (-0.14, -0.15, 0.0)],
    "BL": [(0.10, 0.62, 0.90), (0.10, 0.48, 0.62), (0.12, 0.74, 0.40), (0.135, 0.72, 0.0)],
    "BR": [(-0.10, 0.62, 0.90), (-0.10, 0.46, 0.62), (-0.12, 0.68, 0.40), (-0.14, 0.60, 0.0)],
}
BONES = {
    "root": ((0, 0, 0), (0, -0.3, 0), None, False),
    "hips": ((0, 0.48, 0.95), (0, 0.72, 0.92), "root", False),
    "spine1": ((0, 0.48, 0.95), (0, 0.08, 0.95), "hips", False),
    "spine2": ((0, 0.08, 0.95), (0, -0.24, 0.94), "spine1", True),
    "chest": ((0, -0.24, 0.94), (0, -0.40, 0.99), "spine2", True),
    "neck": ((0, -0.40, 0.99), (0, -0.53, 1.18), "chest", True),
    "head": ((0, -0.53, 1.18), (0, -0.80, 1.10), "neck", True),
    "ear_L": ((0.06, -0.53, 1.27), (0.22, -0.53, 1.34), "head", False),
    "ear_R": ((-0.06, -0.53, 1.27), (-0.22, -0.53, 1.34), "head", False),
    "tail1": ((0, 0.74, 0.98), (0, 0.82, 0.86), "hips", False),
}
for leg, pts in LEGS.items():
    par = "chest" if leg[0] == "F" else "hips"
    for i, seg in enumerate(("upper", "lower", "foot")):
        BONES[f"{seg}_{leg}"] = (pts[i], pts[i + 1], par if i == 0 else f"{('upper', 'lower')[i - 1]}_{leg}", i > 0)


def classify(samples):
    lum = [(c.x + c.y + c.z) / 3 for c in samples]
    white = sum(1 for c, l in zip(samples, lum) if l > 0.5 and max(c) - min(c) < 0.3)
    if white >= 2: return "white"                                        # spots: a minority of the face is enough
    if sorted(lum)[len(lum) // 2] < 0.09: return "dark"                  # dorsal line, muzzle, hooves (median)
    return "coat"


SPEC = {"bones": BONES, "tris": 2600,
        "fur_len": {"face": 0.5, "snout": 0.3, "legs": 0.6, "ears": 0.5, "tail": 1.3}, "hoof_h": 0.06, "bare_lum": 0.06,   # dense short coat; bare nose / eyes / hooves
        "materials": {"coat": ("M_ChitalCoat", (0.42, 0.17, 0.06)), "white": ("M_ChitalWhite", (0.78, 0.74, 0.66)),
                      "dark": ("M_ChitalDark", (0.08, 0.05, 0.04))},
        "neutral": {"FL": -0.15, "FR": -0.15, "BL": 0.66, "BR": 0.66},
        "walk": {"frames": 30, "duty": 0.62, "stance": 0.30, "swing": 0.10, "nod": 4.0, "roll": 1.5},
        "alert_neck": 14, "alert_head": 8, "alert_tail": 35}

result = q_pipeline_hd(SPEC, "Chital", STAGES, classify, look_at=(0, -0.1, 0.75))
