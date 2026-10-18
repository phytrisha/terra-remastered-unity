# White tiger: rig + idle loop + export (character, skinned).
#
#   ASSET=WhiteTiger tools/blender_run.sh blender/assets/WhiteTiger/build.py blender/assets/WhiteTiger/WhiteTiger.blend
#   STAGES=rig,weights,anim,bake,run,walk,pounce,land,export (default: all). Re-runnable: rig, weights and actions are
#   rebuilt every run. Animation stages need the IK constraints, so start partial runs at "rig"; "export" removes the
#   constraints (every action is baked to plain FK first) and writes all four takes into one FBX.
#
# Spec
#   Use: ambient creature standing on the jungle path, seen from ~4-15 m by the fixed viewer
#   Source: user-supplied low-poly tiger (Tripo-generated, sources/tiger/WhiteTiger/), ingested with
#           blender/scripts/ingest_mesh.py (HEIGHT=1.05 YAW=-90): head top 1.05 m, shoulder ~1.0 m, 2.07 m nose-tail tip
#   Budget: Character 15-50k tris -> source is 9.7k, kept as is (no decimation); 1 material M_WhiteTiger,
#           base colour 2048 (from the 4K source)
#   Pivot / facing: bottom centre at origin, head toward -Y (Unity +Z)
#   Rig: root, hips, spine1-3, chest, neck1-2, head, ear_L/R, tail1-6, 4 bones per leg (upper, lower, foot, toe);
#        IK on the legs while animating (feet stay planted), baked to FK before export (deform bones only)
#   Weights: bone heat on a voxel-remeshed proxy (the source is non-manifold with 30 islands), transferred to the
#            mesh, ears painted by distance, max 4 influences, normalised
#   Clips (30 fps, all in place: root fixed, hips move vertically only; Unity drives position from code):
#     Idle   0-150 loop: breathing, head look, tail sway with curl, one ear flick, tiny weight shift
#     Run    0-20 loop (0.67 s): rotary gallop BL > BR > FR > FL, spine flex/extend, steady head, tail streaming, ears back
#     Walk   0-36 loop (1.2 s): lateral-sequence walk BL > FL > BR > FR, duty 0.65, low prowl, body sway, lazy tail
#     Pounce 0-35 one-shot: crouch, push-off, airborne reach (forelegs up, toes spread, head up), ends in descent
#     Land   0-27 one-shot: starts on Pounce's last pose, forepaws > absorb > hind paws > settles on Idle frame 0
#   No jaw bone (bone count/rest pose fixed for the Unity prefab): "mouth open" is faked by a head tilt
import bmesh
from mathutils import Quaternion, Euler

STAGES = os.environ.get("STAGES", "rig,weights,anim,bake,run,walk,pounce,land,export").split(",")
MESH = "SM_WhiteTiger"            # ingest name; renamed to SK_ at export
RIG = "WhiteTiger_Rig"
FPS, FRAMES = 30, 150
VOXEL = 0.016                    # proxy remesh size for bone heat
X0 = 0.02                        # body centre line (x)

# bone placements, Blender coords (measured cross-sections: see measure2.py output in the session log)
B = {
    # name: (head, tail, parent, connected)
    "root":   ((0, 0, 0), (0, -0.3, 0), None, False),
    "hips":   ((X0, 0.32, 0.65), (X0, 0.52, 0.64), "root", False),
    "spine1": ((X0, 0.32, 0.65), (X0, 0.10, 0.66), "hips", False),
    "spine2": ((X0, 0.10, 0.66), (X0, -0.12, 0.67), "spine1", True),
    "spine3": ((X0, -0.12, 0.67), (X0, -0.32, 0.69), "spine2", True),
    "chest":  ((X0, -0.32, 0.69), (X0, -0.52, 0.73), "spine3", True),
    "neck1":  ((X0, -0.52, 0.73), (X0, -0.65, 0.80), "chest", True),
    "neck2":  ((X0, -0.65, 0.80), (X0, -0.76, 0.85), "neck1", True),
    "head":   ((X0, -0.76, 0.85), (X0, -1.02, 0.80), "neck2", True),
    "ear_L":  ((0.150, -0.862, 0.975), (0.162, -0.866, 1.05), "head", False),
    "ear_R":  ((-0.105, -0.865, 0.975), (-0.117, -0.868, 1.05), "head", False),
}
TAIL = [(X0, 0.56, 0.66), (0.01, 0.69, 0.44), (-0.01, 0.76, 0.30), (-0.04, 0.83, 0.22),
        (-0.09, 0.89, 0.165), (-0.17, 0.97, 0.15), (-0.25, 1.04, 0.16)]
LEGS = {   # shoulder/hip, elbow/stifle, wrist/hock, paw, toe tip
    "FL": ("chest", [(0.13, -0.47, 0.64), (0.16, -0.42, 0.40), (0.16, -0.46, 0.12), (0.175, -0.53, 0.04), (0.185, -0.61, 0.03)]),
    "FR": ("chest", [(-0.10, -0.48, 0.64), (-0.13, -0.44, 0.40), (-0.16, -0.53, 0.13), (-0.175, -0.61, 0.04), (-0.185, -0.69, 0.03)]),
    "BL": ("hips", [(0.10, 0.42, 0.62), (0.13, 0.33, 0.40), (0.125, 0.43, 0.17), (0.14, 0.33, 0.05), (0.15, 0.23, 0.03)]),
    "BR": ("hips", [(-0.07, 0.47, 0.62), (-0.09, 0.42, 0.40), (-0.10, 0.60, 0.15), (-0.11, 0.53, 0.05), (-0.11, 0.45, 0.03)]),
}
SEG = ("upper", "lower", "foot", "toe")
TAIL_SWAY, TAIL_LIFT = 4.5, 1.5                                 # deg per tail bone (6 bones accumulate)
EARS = {"ear_L": (0.155, -0.864), "ear_R": (-0.111, -0.866)}   # x, y of the ear centres
EAR_Z0, EAR_Z1 = 0.96, 1.01                                      # weight ramp head -> ear

ob = bpy.data.objects[MESH]
coll_ = ob.users_collection[0]
info = {}


def deselect():
    for o in bpy.context.scene.objects:
        if o: o.select_set(False)


def fcurves(act):
    """F-curves of an action (Blender 4.4+ slotted actions keep them in layer/strip channelbags)."""
    if hasattr(act, "fcurves"): return list(act.fcurves)
    return [fc for layer in act.layers for strip in layer.strips for cb in strip.channelbags for fc in cb.fcurves]


def bone_list():
    out = dict(B)
    for i in range(6):
        out[f"tail{i + 1}"] = (TAIL[i], TAIL[i + 1], "hips" if i == 0 else f"tail{i}", i > 0)
    for leg, (par, pts) in LEGS.items():
        for i, s in enumerate(SEG):
            out[f"{s}_{leg}"] = (pts[i], pts[i + 1], par if i == 0 else f"{SEG[i - 1]}_{leg}", i > 0)
    return out


def build_rig():
    old = bpy.data.objects.get(RIG)
    if old:
        ob.parent = None; ob.matrix_world = Matrix.Identity(4)
        bpy.data.objects.remove(old, do_unlink=True)
        bpy.context.view_layer.update()
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    for arm_data in [a for a in bpy.data.armatures if a.users == 0]:
        bpy.data.armatures.remove(arm_data)
    arm = bpy.data.objects.new(RIG, bpy.data.armatures.new(RIG))
    coll_.objects.link(arm)
    deselect()
    bpy.context.view_layer.objects.active = arm; arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    bones = bone_list()
    for n, (h, t, par, con) in bones.items():
        b = eb.new(n); b.head = h; b.tail = t
    for n, (h, t, par, con) in bones.items():
        b = eb[n]
        if par: b.parent = eb[par]; b.use_connect = con
        b.align_roll(Vector((0, 0, 1)) if abs((Vector(t) - Vector(h)).normalized().z) < 0.9 else Vector((0, -1, 0)))
    # IK targets at the paw tips (non-deform, under root) and pole targets in front of knees/elbows
    for leg, (par, pts) in LEGS.items():
        t = eb.new(f"IK_{leg}"); t.head = pts[3]; t.tail = Vector(pts[3]) + Vector((0, 0, 0.08)); t.parent = eb["root"]
        t.use_deform = False
        # clip rig (Run/Pounce/Land): wrist/hock target + a flat paw helper with the foot's exact rest matrix
        w = eb.new(f"IKW_{leg}"); w.head = pts[2]; w.tail = Vector(pts[2]) + Vector((0, 0, 0.08)); w.parent = eb["root"]; w.use_deform = False
        pw = eb.new(f"PAW_{leg}"); pw.head = pts[2]; pw.tail = pts[3]; pw.roll = eb[f"foot_{leg}"].roll; pw.parent = w; pw.use_deform = False
    eb["root"].use_deform = True
    bpy.ops.object.mode_set(mode='OBJECT')
    for leg in LEGS:
        c = arm.pose.bones[f"foot_{leg}"].constraints.new('IK')
        c.target = arm; c.subtarget = f"IK_{leg}"; c.chain_count = 3; c.use_stretch = False
    info["bones"] = len(arm.data.bones); info["deform_bones"] = sum(b.use_deform for b in arm.data.bones)
    return arm


def skin(arm):
    # clean old weights
    ob.vertex_groups.clear()
    for m in list(ob.modifiers): ob.modifiers.remove(m)
    ob.parent = None
    # proxy: closed voxel shell, so bone heat finds a solution
    proxy = ob.copy(); proxy.data = ob.data.copy(); proxy.name = "Tiger_Proxy"; coll_.objects.link(proxy)
    r = proxy.modifiers.new("Remesh", 'REMESH'); r.mode = 'VOXEL'; r.voxel_size = VOXEL
    with bpy.context.temp_override(object=proxy, active_object=proxy, selected_objects=[proxy]):
        bpy.ops.object.modifier_apply(modifier=r.name)
    info["proxy_verts"] = len(proxy.data.vertices)
    deselect()
    proxy.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    pg = {g.index: g.name for g in proxy.vertex_groups}
    unweighted = sum(1 for v in proxy.data.vertices if not any(g.weight > 1e-3 for g in v.groups))
    info["proxy_unweighted"] = unweighted
    # transfer to the real mesh
    for n in pg.values(): ob.vertex_groups.new(name=n)
    dt = ob.modifiers.new("DT", 'DATA_TRANSFER'); dt.object = proxy
    dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
    dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=dt.name)
    bpy.data.objects.remove(proxy, do_unlink=True)
    # ears: thin flaps the voxel proxy may have lost; ramp from head to ear bone by height near each ear
    vg = ob.vertex_groups
    for ear, (ex, ey) in EARS.items():
        g = vg.get(ear) or vg.new(name=ear)
        for v in ob.data.vertices:
            p = ob.matrix_world @ v.co
            if p.z < EAR_Z0 or abs(p.x - ex) > 0.06 or abs(p.y - ey) > 0.06: continue
            w = min(1.0, (p.z - EAR_Z0) / (EAR_Z1 - EAR_Z0))
            for gg in list(v.groups):
                vg[gg.group].add([v.index], gg.weight * (1 - w), 'REPLACE')
            g.add([v.index], w, 'REPLACE')
    # non-deform groups out, max 4 influences, normalised
    for n in [b.name for b in arm.data.bones if not b.use_deform]:
        if vg.get(n): vg.remove(vg[n])
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
        bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    zero = sum(1 for v in ob.data.vertices if not any(g.weight > 1e-3 for g in v.groups))
    info["mesh_unweighted"] = zero
    ob.parent = arm
    am = ob.modifiers.new("Armature", 'ARMATURE'); am.object = arm
    # dominant bone per vertex: coverage check (which bones actually own vertices)
    cnt = {}
    for v in ob.data.vertices:
        if v.groups:
            g = max(v.groups, key=lambda x: x.weight); cnt[vg[g.group].name] = cnt.get(vg[g.group].name, 0) + 1
    info["verts_per_bone"] = dict(sorted(cnt.items()))


def animate(arm):
    sc = bpy.context.scene
    sc.render.fps = FPS; sc.frame_start = 0; sc.frame_end = FRAMES
    act = bpy.data.actions.new("Idle_src"); arm.animation_data_create(); arm.animation_data.action = act
    pb = arm.pose.bones
    for p in pb: p.rotation_mode = 'XYZ'
    T = FRAMES
    def S(f, period, ph=0.0): return math.sin(2 * math.pi * (f / period + ph))
    def pulse(f, c, w): return math.exp(-((f - c) / w) ** 2)
    D = math.radians
    for f in range(0, FRAMES + 1, 2):
        b = S(f, T / 2)                                    # breathing: 2 breaths per loop (2.5 s each)
        # no bone scale: children (front legs) would inherit it in Unity and the paws would slide
        pb["spine1"].rotation_euler = (D(-0.5) * b, 0, 0)
        pb["spine2"].rotation_euler = (D(1.2) * b, 0, 0)
        pb["spine3"].rotation_euler = (D(-1.0) * b, 0, 0)
        pb["chest"].rotation_euler = (D(0.6) * b, 0, 0)
        pb["hips"].location = (0.006 * S(f, T), 0, 0.002 * b)   # tiny weight shift (local)
        look = S(f, T, 0.1)
        pb["neck1"].rotation_euler = (D(1.0) * b, 0, D(3.0) * look)
        pb["neck2"].rotation_euler = (D(0.5) * S(f, T, 0.35), D(1.5) * S(f, T, 0.6), D(3.0) * look)
        pb["head"].rotation_euler = (D(2.0) * S(f, T, 0.3), D(3.0) * S(f, T, 0.55), D(5.0) * S(f, T, 0.15))
        flick = pulse(f, 64, 2.2) - 0.6 * pulse(f, 69, 2.0)   # one quick ear flick (+ settle)
        pb["ear_L"].rotation_euler = (D(-25) * flick, D(10) * flick, 0)
        pb["ear_R"].rotation_euler = (D(1.5) * S(f, T / 3), 0, 0)
        for i in range(6):                                 # tail: travelling wave, stronger and curling to the tip
            k = (i + 1) / 6
            pb[f"tail{i + 1}"].rotation_euler = (D(TAIL_LIFT) * k * (0.6 + 0.4 * S(f, T / 2, -0.08 * i)),   # pitch: lift only
                                                  D(2.0) * k * S(f, T, -0.05 * i),
                                                  D(TAIL_SWAY) * (0.5 + 0.5 * k) * S(f, T, -0.09 * i) + D(2.0) * k * k * (0.5 + 0.5 * S(f, T / 2, 0.2 - 0.1 * i)))
        for p in pb:
            if p.name.startswith(("IK_", "upper", "lower", "foot", "toe", "root")): continue
            p.keyframe_insert("rotation_euler", frame=f)
            if p.name == "hips": p.keyframe_insert("location", frame=f)
    fcs = fcurves(act)
    for fc in fcs:
        for k in fc.keyframe_points: k.interpolation = 'BEZIER'
        fc.modifiers.new('CYCLES')
    info["idle_src_curves"] = len(fcs)


def bake(arm):
    """IK + keys -> plain FK keys on the deform bones (visual keying), constraints removed."""
    deselect()
    arm.select_set(True); bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    for p in arm.pose.bones:
        tgt = p if hasattr(p, "select") else p.bone          # Blender 5 moved selection to the pose bone
        tgt.select = p.bone.use_deform
    for leg in LEGS: arm.pose.bones[f"foot_{leg}"].constraints["IK"].influence = 1.0   # Idle: original 3-bone IK
    bake_action(arm, "Idle_src", "Idle", 0, FRAMES)
    # foot slide check: paw tip world positions over the clip
    sc = bpy.context.scene; drift = {}
    rest = {}
    for f in range(0, FRAMES + 1, 5):
        sc.frame_set(f)
        for leg in LEGS:
            p = arm.matrix_world @ arm.pose.bones[f"toe_{leg}"].head
            rest.setdefault(leg, p.copy()); drift[leg] = max(drift.get(leg, 0), (p - rest[leg]).length)
    sc.frame_set(0)
    tip = []
    for f in range(0, FRAMES + 1, 3):
        sc.frame_set(f); tip.append(arm.matrix_world @ arm.pose.bones["tail6"].tail)
    info["tail_tip_z_min_max"] = [round(min(p.z for p in tip), 3), round(max(p.z for p in tip), 3)]
    info["tail_tip_x_range"] = [round(min(p.x for p in tip), 3), round(max(p.x for p in tip), 3)]
    sc.frame_set(0)
    info["paw_drift_mm"] = {k: round(v * 1000, 2) for k, v in drift.items()}
    f0 = {}; f1 = {}
    for fr, d in ((0, f0), (FRAMES, f1)):
        sc.frame_set(fr)
        for p in arm.pose.bones: d[p.name] = p.matrix.copy()
    info["loop_mismatch"] = round(max(max(abs(a - b) for ra, rb in zip(f0[n], f1[n]) for a, b in zip(ra, rb)) for n in f0), 5)
    sc.frame_set(0)


def ik_constraints(arm):
    return [c for leg in LEGS for c in leg_constraints(arm, leg)]


def setup_clip_rig(arm):
    """After Idle is baked: IK on upper+lower to the wrist/hock target (hinge about the bone X axis) and the paw
    (foot bone) copying the flat PAW helper's rotation, so planted paws stay flat on the floor."""
    for leg in LEGS:
        pb = arm.pose.bones
        for b in (f"foot_{leg}", f"lower_{leg}"):
            for c in list(pb[b].constraints): pb[b].constraints.remove(c)
        ik = pb[f"lower_{leg}"].constraints.new('IK'); ik.name = "IK"
        ik.target = arm; ik.subtarget = f"IKW_{leg}"; ik.chain_count = 2; ik.use_stretch = False
        cr = pb[f"foot_{leg}"].constraints.new('COPY_ROTATION'); cr.name = "PAW"
        cr.target = arm; cr.subtarget = f"PAW_{leg}"
        for b in (f"upper_{leg}", f"lower_{leg}"):
            pb[b].lock_ik_y = True; pb[b].lock_ik_z = True


def leg_constraints(arm, leg):
    pb = arm.pose.bones
    return [c for c in list(pb[f"lower_{leg}"].constraints) + list(pb[f"foot_{leg}"].constraints) if c.name in ("IK", "PAW")]


def bone_offset(pb, v):
    """Bone-local location for a world/armature-space offset v (bones whose parent is the unposed root)."""
    return pb.bone.matrix_local.to_3x3().inverted() @ Vector(v)


def bake_action(arm, src_name, out_name, f0, f1):
    """Visual-key the deform bones of action src_name (IK influences as keyed/set) into a plain FK action out_name."""
    old = bpy.data.actions.get(out_name)
    if old: bpy.data.actions.remove(old)
    arm.animation_data.action = bpy.data.actions[src_name]
    deselect()
    arm.select_set(True); bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    for p in arm.pose.bones:
        tgt = p if hasattr(p, "select") else p.bone          # Blender 5 moved selection to the pose bone
        tgt.select = p.bone.use_deform
    bpy.ops.nla.bake(frame_start=f0, frame_end=f1, step=1, only_selected=True, visual_keying=True,
                     clear_constraints=False, use_current_action=False, bake_types={'POSE'})
    bpy.ops.object.mode_set(mode='OBJECT')
    act = arm.animation_data.action; act.name = out_name; act.use_fake_user = True
    bpy.data.actions.remove(bpy.data.actions[src_name])
    return act


# ---------------------------------------------------------------- Run / Pounce / Land
DEG = math.radians
DEFORM_SKIP = ("root",)


def world_rot(pb, pitch=0.0, roll=0.0, yaw=0.0):
    """Local pose rotation that turns the bone by (pitch about world X, roll about Y, yaw about Z) degrees in its rest
    frame. Signs (Blender, head toward -Y): +pitch swings a downward limb backward, tips a forward bone (spine, neck,
    head) nose-down, lifts a back-pointing tail upward and tilts an ear forward."""
    R = pb.bone.matrix_local.to_quaternion()
    W = Quaternion((0, 0, 1), DEG(yaw)) @ Quaternion((0, 1, 0), DEG(roll)) @ Quaternion((1, 0, 0), DEG(pitch))
    return R.inverted() @ W @ R


def hips_offset(pb, dz):
    """Hips location (bone-local) for a world vertical offset dz (the hips' parent root has identity rest)."""
    return pb.bone.matrix_local.to_3x3().inverted() @ Vector((0, 0, dz))


def pose_to_channels(arm, pose, dz, prev, locs=None):
    """pose: {bone: (pitch, roll, yaw)}; every deform bone gets a channel (missing = rest). Returns {bone: (euler, loc)}."""
    out = {}
    for pb in arm.pose.bones:
        if not pb.bone.use_deform or pb.name in DEFORM_SKIP: continue
        q = world_rot(pb, *pose.get(pb.name, (0, 0, 0)))
        e = q.to_euler('XYZ', prev[pb.name][0]) if prev and pb.name in prev else q.to_euler('XYZ')
        loc = hips_offset(pb, dz) if pb.name == "hips" else Vector((0, 0, 0))
        if locs and pb.name in locs: loc = loc + bone_offset(pb, locs[pb.name])   # e.g. scapula slide on upper_F*
        out[pb.name] = (e, loc)
    return out


def apply_channels(arm, ch):
    """ch: {bone: (euler, loc)}; helper bones (IKW_*, PAW_*) may be included like deform bones."""
    for n, (e, loc) in ch.items():
        pb = arm.pose.bones[n]; pb.rotation_euler = e; pb.location = loc; pb.scale = (1, 1, 1)


def write_action(arm, name, frames, influences=None):
    """frames: {f: channels}; influences: {leg: {f: value}} keyed on the IK constraints (src actions for baking)."""
    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data.action = act
    for f, ch in sorted(frames.items()):
        apply_channels(arm, ch)
        for n in ch:
            pb = arm.pose.bones[n]
            pb.keyframe_insert("rotation_euler", frame=f); pb.keyframe_insert("location", frame=f); pb.keyframe_insert("scale", frame=f)
    for leg, keys in (influences or {}).items():
        for c in leg_constraints(arm, leg):
            for f, v in keys.items(): c.influence = v; c.keyframe_insert("influence", frame=f)
    for fc in fcurves(act):
        for k in fc.keyframe_points: k.interpolation = 'LINEAR'
    return act


def eval_pose(arm, ch):
    """Pose the rig from channels without animation (action detached) and return world points per leg."""
    arm.animation_data.action = None
    apply_channels(arm, ch); bpy.context.view_layer.update()
    pts = {}
    for leg in LEGS:
        pb = arm.pose.bones
        pts[leg] = [arm.matrix_world @ pb[f"foot_{leg}"].tail, arm.matrix_world @ pb[f"toe_{leg}"].tail]
    return pts


def rest_points(arm):
    return {leg: [Vector(LEGS[leg][1][3]), Vector(LEGS[leg][1][4])] for leg in LEGS}


def smooth01(t): t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)


# Run: felid rotary gallop, in place (root fixed, hips move vertically only). Footfall BL > BR > FR > FL with a short
# extended suspension after the hind push-off and a longer gathered one after the forelegs leave. The trunk arch does
# most of the work (the legs of this model only reach ~0.5 m). Planted paws are the IK targets (wrist/hock + flat paw
# helper) moving back at exactly RUN_SPEED, so at 1.0x playback the paws do not slide at that ground speed.
RUN_FRAMES = 18                                     # 0.6 s at 30 fps
RUN_SPEED = 3.0                                     # m/s at 1.0x: planted paws move back at this speed (stance travel =
                                                    # speed x duty x cycle: 0.45 m front, 0.49 m hind = what the legs reach)
# Rotary gait diagram (touchdown, lift-off as cycle fractions): partners 0.15 (hind) / 0.20 (fore) apart = 2.7 / 3.6
# frames, duty 0.25-0.27; overlap of BR and FR instead of an extended suspension, gathered suspension 0.88-1.0.
#            touchdown, liftoff, stance centre y of the wrist/hock (m, absolute), swing height (m), x spread (m, outward)
# the hind pair passes each other while BL is mid-swing and BR has just lifted: BL swings higher, so they pass apart
RUN_LEGS = {"BL": (0.00, 0.27, 0.52, 0.24, 0.03), "BR": (0.15, 0.42, 0.55, 0.13, 0.03),
            "FR": (0.43, 0.68, -0.50, 0.26, 0.025), "FL": (0.63, 0.88, -0.47, 0.33, 0.025)}
RUN_GATHER, RUN_EXTEND = 0.93, 0.43                 # phase of the gathered / extended suspension
# pelvis tucks hard (-), the spine segments arch (+): arch 30 deg/unit (45 deg gathered-extended), chest +13 deg, so
# the spine chord (and the head height) stays nearly level
RUN_ARCH = {"hips": -17.0, "spine1": 9.0, "spine2": 9.0, "spine3": 7.0, "chest": 5.0}   # deg per unit flexion
RUN_ARCH_LAG = 0.025                                # phase lag per segment, rear to front (no simultaneous extremes)


def flexion(p):
    """Trunk flexion: +1 fully gathered (arched), -0.5 extended (the gather dominates in heavy cats); eased."""
    return 0.25 + 0.75 * math.cos(2 * math.pi * (p - RUN_GATHER))


def hermite(p0, p1, m0, m1, t):
    t2, t3 = t * t, t * t * t
    return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1


def run_leg(leg, p):
    """Absolute wrist/hock target (y, z offset above rest), paw pitch, toe pitch, stance weight (0 = in the air)."""
    td, lo, yc, H, _ = RUN_LEGS[leg]; front = leg[0] == "F"
    T = RUN_FRAMES / FPS; duty = lo - td; L = RUN_SPEED * duty * T
    ph = (p - td) % 1.0
    if ph < duty:                                   # stance: straight line back at ground speed, paw flat
        s = ph / duty; y = yc - L / 2 + L * s; z = 0.0; rot = 0.0; toe = 0.0
        if s > 0.65:                                # push-off: heel lifts, paw rolls onto the toes
            k = (s - 0.65) / 0.35; z = (0.035 if front else 0.065) * k * k; rot = (22 if front else -18) * k * k; toe = -12 * k
        return y, z, rot, toe, math.sin(math.pi * s)
    s = (ph - duty) / (1 - duty)                    # swing: Hermite with backward end tangents = flick back, reach, retract
    sw = (1 - duty) * T * RUN_SPEED                 # tangent scale: paw moves back at ground speed at both ends
    y = hermite(yc + L / 2, yc - L / 2, sw * 0.9, sw * 0.6, s)
    if front:                                       # tall oval: fold up early, reach high and forward, drop to contact
        z = H * math.sin(math.pi * s ** 0.8) ** 1.3
        rot = 105 * math.sin(math.pi * min(1.0, s / 0.55)) * (s < 0.55) - 28 * math.sin(math.pi * max(0.0, (s - 0.5) / 0.5))
        toe = 40 * math.sin(math.pi * min(1.0, s / 0.5)) * (s < 0.5) - 22 * math.sin(math.pi * max(0.0, (s - 0.55) / 0.45))
    else:                                           # long low oval: fold tight, swing under the belly
        z = 0.065 * (1 - s) ** 2 + H * math.sin(math.pi * s) ** 0.8       # continues the push-off lift
        rot = -18 * (1 - s) ** 2 + 70 * math.sin(math.pi * min(1.0, s / 0.6)) * (s < 0.6) - 10 * math.sin(math.pi * max(0.0, (s - 0.6) / 0.4))
        toe = -12 * (1 - s) ** 2 - 15 * math.sin(math.pi * s)
    return y, z, rot, toe, 0.0


def run_pose(p):
    pose, targets, locs, stance = {}, {}, {}, []
    support_h = support_f = 0.0
    for leg in RUN_LEGS:
        y, z, rot, toe, w = run_leg(leg, p)
        rest = Vector(LEGS[leg][1][2])
        side_x = RUN_LEGS[leg][4] * (1 if leg.endswith("L") else -1)
        targets[leg] = ((side_x, y - rest.y, z), rot)
        pose[f"toe_{leg}"] = (toe, 0, 0)
        side = 1 if leg.endswith("L") else -1
        if w > 0: stance.append(leg)
        if leg[0] == "B": support_h += side * w
        else:
            support_f += side * w
            # scapula: blade rides up as the body passes over the loaded leg, drops and slides forward on the reach
            td, lo, yc, H, _ = RUN_LEGS[leg]; ph = (p - td) % 1.0; duty = lo - td
            if ph < duty: s = ph / duty; locs[f"upper_{leg}"] = (0, 0.03 * s, 0.018 * math.sin(math.pi * s) + 0.01)
            else:
                s = (ph - duty) / (1 - duty)       # continuous with stance: (0, 0.03, 0.01) at lift-off, (0, 0, 0.01) at contact
                locs[f"upper_{leg}"] = (0, 0.03 * (1 - s) - 0.06 * math.sin(math.pi * s ** 0.8), 0.01 - 0.025 * math.sin(math.pi * s))
    # trunk arch with a rear-to-front wave; pelvis tucks under (-) while the chest pitches nose-down (+) when gathered
    chest_abs = 0.0
    for i, (b, k) in enumerate(RUN_ARCH.items()):
        f = flexion(p - RUN_ARCH_LAG * i); chest_abs += k * f
        pose[b] = (k * f, 0, 0)
    # roll toward the supporting side (pelvis follows the hind pair, chest the front pair)
    pose["hips"] = (pose["hips"][0], 3.0 * support_h, 0)
    pose["chest"] = (pose["chest"][0], 3.5 * support_f - 3.0 * support_h, 0)
    # head: level and locked on the target; lowered and forward for the charge, ears pinned
    fh = flexion(p - 0.15)
    # the neck lifts more than the chest drops (keeps the head height), the head pitches back so its orientation is net 0
    pose["neck1"] = (12 - 0.9 * chest_abs, 0, 0); pose["neck2"] = (2 - 0.4 * chest_abs, -1.5 * support_f, 0)
    pose["head"] = (-14 + 0.3 * chest_abs + 1.5 * math.sin(4 * math.pi * (p - 0.1)), -2.0 * support_f, 0)
    pose["ear_L"] = (-42, 0, 0); pose["ear_R"] = (-42, 0, 0)
    # tail: long and low with a slight S, follows the pelvis with ~2 frames lag per segment, tip whips
    base = (24, -6, -8, 4, 6, 8)
    for i in range(6):
        lag = 2.0 * (i + 1) / RUN_FRAMES
        amp = 7 + 4.0 * i                              # grows toward the tip: whip
        counter = 0.75 * -RUN_ARCH["hips"] * flexion(p - 0.03) if i == 0 else 0.0   # tail root resists the pelvis tuck
        pose[f"tail{i + 1}"] = (base[i] + counter - amp * flexion(p - lag) * 0.9, 0, (3 + 2.5 * i) * math.sin(2 * math.pi * (p - lag - 0.2)))
    # two bounces per stride: high at the hind push-off and in the gathered suspension, low at each pair's mid-stance
    front_load = sum(run_leg(l, p)[4] for l in ("FL", "FR"))
    dz = -0.055 + 0.025 * math.cos(4 * math.pi * (p - (RUN_EXTEND - 0.01))) - 0.065 * min(1.0, front_load)
    return pose, targets, stance, dz, locs


def target_channels(arm, targets, prev):
    """IKW location offsets + PAW rotation (pitch about world X) per leg."""
    ch = {}
    for leg, tg in targets.items():
        off, rot = tg[0], tg[1]; yaw = tg[2] if len(tg) > 2 else 0.0          # yaw: planted paw keeps its world heading
        w = arm.pose.bones[f"IKW_{leg}"]; pw = arm.pose.bones[f"PAW_{leg}"]
        ch[w.name] = (Euler((0, 0, 0)), bone_offset(w, off))
        q = world_rot(pw, rot, yaw=yaw)
        ch[pw.name] = (q.to_euler('XYZ', prev[pw.name][0]) if prev and pw.name in prev else q.to_euler('XYZ'), Vector((0, 0, 0)))
    return ch


def rest_targets():
    return {leg: ((0, 0, 0), 0.0) for leg in LEGS}


def build_run(arm):
    frames, prev, flight = {}, None, []
    for f in range(RUN_FRAMES):
        pose, targets, stance, dz, locs = run_pose(f / RUN_FRAMES)
        ch = pose_to_channels(arm, pose, dz, prev, locs); ch.update(target_channels(arm, targets, prev))
        frames[f] = ch; prev = ch
        if not stance: flight.append(f)
    frames[RUN_FRAMES] = frames[0]                    # exact loop
    write_action(arm, "Run_src", frames, {leg: {0: 1.0} for leg in LEGS})
    act = bake_action(arm, "Run_src", "Run", 0, RUN_FRAMES)
    for fc in fcurves(act): fc.modifiers.new('CYCLES')
    info["run"] = {"frames": [0, RUN_FRAMES], "flight_frames": flight, "no_slide_speed_mps": RUN_SPEED,
                   "contact_min_z": contact_report(arm, act, list(range(0, RUN_FRAMES)))}
    info["run"]["stance_paw_lift_mm"] = stance_lift(arm, act)
    info["run"]["contacts"], info["run"]["gait_diagram"] = gait_table(arm, act)


# Walk: lateral-sequence walk (the big-cat default), in place. Each hind foot lands a quarter cycle before the fore foot
# of the same side; ~65 % ground contact per leg, so 2-3 paws always carry the body. Planted paws move back at
# WALK_SPEED (no slide at 1.0x and that root speed).
WALK_FRAMES = 36                                    # 1.2 s at 30 fps
WALK_SPEED = 0.45                                   # m/s at 1.0x: stance travel = 0.45 x 0.65 x 1.2 = 0.35 m (0.51 m
                                                    # lifted planted paws 22 mm, 0.43 m drifted 30-40 mm in the world:
                                                    # the stance ends were beyond what these short legs reach)
#             touchdown, liftoff, stance centre y (m, absolute: each leg's own rest wrist/hock), swing height (m)
WALK_LEGS = {"BL": (0.00, 0.65, 0.45, 0.075), "FL": (0.25, 0.90, -0.47, 0.095),
             "BR": (0.50, 1.15, 0.51, 0.075), "FR": (0.75, 1.40, -0.49, 0.095)}
# (right legs centred closer under the shoulder / hip than their rest wrists -0.53 / 0.58: the asymmetric rest pose
#  put the stride ends at 99-100 % of the leg's reach -> the IK snapped straight at touchdown)
# A walking cat sets its paws close to the midline (near single-track): planted paws sit this far (m) inside the rest
# wrist / hock x. Rest track 0.35 m front / 0.25 m hind -> 0.20 / 0.15 m (x1.3 in the scene: 0.26 / 0.20 m)
WALK_NARROW = {"F": 0.075, "B": 0.05}


def walk_home_x(leg):
    x = LEGS[leg][1][2][0]
    return x - math.copysign(WALK_NARROW[leg[0]], x)


HEEL = 0.7                                          # stance fraction where the heel starts to peel


def walk_leg(leg, p):
    """Absolute wrist/hock target (y, z offset), paw pitch, toe pitch, stance weight. Every channel is continuous in value
    AND velocity across lift-off and touchdown (the earlier curves kinked there: one-frame elbow / toe pops of 50-95 mm)."""
    td, lo, yc, H = WALK_LEGS[leg]; front = leg[0] == "F"
    T = WALK_FRAMES / FPS; duty = lo - td; L = WALK_SPEED * duty * T
    hp, rp, tp = (0.02, 16.0, -8.0) if front else (0.04, -12.0, -8.0)      # heel-peel height, paw pitch, toe pitch at lift-off
    ph = (p - td) % 1.0
    if ph < duty:                                   # stance: back at ground speed, paw flat, heel peels at the end (k^2: C1)
        s = ph / duty; y = yc - L / 2 + L * s
        k = max(0.0, (s - HEEL) / (1 - HEEL))
        return y, hp * k * k, rp * k * k, tp * k * k, math.sin(math.pi * s)
    u = (ph - duty) / (1 - duty)
    g = (1 - duty) / duty                           # d/du = d/ds * g
    dk = 2.0 / (1 - HEEL) * g                        # d(k^2)/du at lift-off
    sw = (1 - duty) * T * WALK_SPEED                 # stance velocity in swing-fraction units: tangents match both ends
    y = hermite(yc + L / 2, yc - L / 2, sw, sw, u)
    bump = lambda a: math.sin(math.pi * u ** a) ** 2          # 0 at both ends with zero slope
    z = hermite(hp, 0.0, hp * dk, 0.0, u) + H * bump(0.9)      # soft landing: zero vertical velocity at touchdown
    if front:   # wrist folds as the paw lifts, unfolds to reach
        rot = hermite(rp, 0.0, rp * dk, 0.0, u) + 66.0 * bump(0.75)
        toe = hermite(tp, 0.0, tp * dk, 0.0, u) + 22.0 * bump(0.7)
    else:
        rot = hermite(rp, 0.0, rp * dk, 0.0, u) + 38.0 * bump(0.8)
        toe = hermite(tp, 0.0, tp * dk, 0.0, u) - 10.0 * bump(1.0)
    return y, z, rot, toe, 0.0


def walk_pose(p):
    pose, targets, locs = {}, {}, {}
    sup = {}
    for leg in WALK_LEGS:
        y, z, rot, toe, w = walk_leg(leg, p)
        rest = Vector(LEGS[leg][1][2])
        targets[leg] = ((walk_home_x(leg) - rest.x, y - rest.y, z), rot)
        pose[f"toe_{leg}"] = (toe, 0, 0); sup[leg] = w
        if leg[0] == "F":                               # scapula rides up over the loaded leg, slides forward on the reach
            td, lo, yc, H = WALK_LEGS[leg]; ph = (p - td) % 1.0; duty = lo - td
            if ph < duty: s = ph / duty; locs[f"upper_{leg}"] = (0, 0.02 * s, 0.022 * math.sin(math.pi * s))
            else: s = (ph - duty) / (1 - duty); locs[f"upper_{leg}"] = (0, 0.02 * (1 - s) - 0.03 * math.sin(math.pi * s), -0.006 * math.sin(math.pi * s))
    hind = sup["BL"] - sup["BR"]; fore = sup["FL"] - sup["FR"]   # + = left loaded
    # pelvis / chest roll toward the loaded side and yaw with the stride (S-shaped spine), small and smooth
    yaw = math.sin(2 * math.pi * (p - 0.12))
    pose["hips"] = (0, 2.5 * hind, 3.0 * yaw)
    pose["spine1"] = (0, 0, -1.2 * yaw); pose["spine2"] = (0, 0, -1.2 * yaw); pose["spine3"] = (0, 0, -0.8 * yaw)
    pose["chest"] = (0, 2.5 * fore, -1.0 * yaw)
    # head carried low and steady (prowl), small nod at each fore footfall, counter-yaw keeps the gaze forward
    nod = math.cos(4 * math.pi * (p - 0.3))
    pose["neck1"] = (-6 + 1.5 * nod, 0, 1.2 * yaw); pose["neck2"] = (-2, 0, 1.0 * yaw)
    pose["head"] = (8 - 1.5 * nod, 0, 0.8 * yaw)
    pose["ear_L"] = (-6, 0, 0); pose["ear_R"] = (-6, 0, 0)
    # tail: low lazy S, swaying with the pelvis, delayed toward the tip
    base = (22, -10, -8, 6, 9, 12)
    for i in range(6):
        lag = 0.06 * (i + 1)
        pose[f"tail{i + 1}"] = (base[i] + 2 * math.sin(2 * math.pi * (p - lag)), 0, (2.5 + 2.0 * i) * math.sin(2 * math.pi * (p - 0.12 - lag)))
    # slightly crouched prowl (-4 cm): at the old -1.2 cm the stride ends were beyond the legs' reach (front 0.55 m needed
    # vs 0.53 m, hind 0.50 vs 0.49): the IK snapped to full extension and the elbows popped for a frame
    dz = -0.040 + 0.008 * math.cos(4 * math.pi * (p - 0.08))   # two dips per cycle (double support)
    return pose, targets, dz, locs


# Arc walks (Walk, WalkTurnL, WalkTurnR): authored with the root bone travelling along an arc (speed WALK_SPEED, turn
# rate omega), every planted paw fixed in the WORLD (contact point under the hip at mid-stance), swings in the root's
# frame, the spine bent into the turn with the head leading. Exported IN PLACE (root keys removed after the bake: every
# other bone is keyed relative to the moving root), and Unity moves the animal with the same constant speed / turn rate
# (AnimalWander), blended by turn amount: the body turns and curves and the planted paws stay put.
WALK_TURN_RADIUS = 0.75                             # m (model scale): omega = WALK_SPEED / radius = 36.7 deg/s (1.0 m swung the tiger 2.6 m wide in hairpins)
TURN_BEND = {"hips": -2.0, "spine1": 2.0, "spine2": 3.0, "spine3": 3.5, "chest": 3.5, "neck1": 5.0, "neck2": 4.0, "head": 5.0}


def root_path(t, omega):
    """Root position (x, y) and heading (rad, + = CCW = left turn for a -Y facing animal) at time t."""
    v = WALK_SPEED
    if abs(omega) < 1e-6: return Vector((0.0, -v * t, 0.0)), 0.0
    th = omega * t
    return Vector((v * (1 - math.cos(th)) / omega, -v * math.sin(th) / omega, 0.0)), th


def to_local(P, t, omega):
    c, th = root_path(t, omega)
    return Matrix.Rotation(-th, 3, 'Z') @ (P - c)


def plant(leg, n, omega):
    """World contact point of stance n of a leg: under its home (rest x, stance centre y) at mid-stance."""
    td, lo, yc, H = WALK_LEGS[leg]; T = WALK_FRAMES / FPS
    tm = ((td + n) + (lo + n)) / 2 * T
    c, th = root_path(tm, omega)
    return c + Matrix.Rotation(th, 3, 'Z') @ Vector((walk_home_x(leg), yc, 0.0))


def walk_target_turn(leg, t, omega):
    """Wrist/hock target in the root's frame (x, y), plus z / paw / toe / weight from walk_leg at the same phase."""
    td, lo, yc, H = WALK_LEGS[leg]; T = WALK_FRAMES / FPS
    _, z, rot, toe, w = walk_leg(leg, (t / T) % 1.0)
    for n in (-2, -1, 0, 1):
        a, b = (td + n) * T, (lo + n) * T
        if a <= t <= b: return to_local(plant(leg, n, omega), t, omega), z, rot, toe, w
        nxt = (td + n + 1) * T
        if b < t < nxt:                                 # swing: Hermite in the root frame, end velocities = stance velocities
            P0, P1 = plant(leg, n, omega), plant(leg, n + 1, omega); dt = nxt - b; e = 1e-3
            S, E = to_local(P0, b, omega), to_local(P1, nxt, omega)
            v0 = (to_local(P0, b + e, omega) - to_local(P0, b - e, omega)) / (2 * e)
            v1 = (to_local(P1, nxt + e, omega) - to_local(P1, nxt - e, omega)) / (2 * e)
            u = (t - b) / dt
            return hermite(S, E, v0 * dt, v1 * dt, u), z, rot, toe, w          # C1 with both stances
    raise RuntimeError("no stance/swing interval")


def paw_yaw(leg, t, omega):
    """Paw heading relative to the body (deg): planted = aligned with the body at mid-stance and then fixed in the world
    (counter-rotates as the body turns); in the swing it rotates back to the next stance's start value."""
    if abs(omega) < 1e-6: return 0.0
    td, lo, yc, H = WALK_LEGS[leg]; T = WALK_FRAMES / FPS
    for n in (-2, -1, 0, 1):
        a, b = (td + n) * T, (lo + n) * T; tm = (a + b) / 2; nxt = (td + n + 1) * T
        if a <= t <= b: return math.degrees(omega * (tm - t))
        if b < t < nxt:
            y0 = math.degrees(omega * (tm - b)); tm1 = (nxt + (lo + n + 1) * T) / 2; y1 = math.degrees(omega * (tm1 - nxt))
            u = (t - b) / (nxt - b); return y0 + (y1 - y0) * u * u * (3 - 2 * u)
    return 0.0


def build_walk_rm(arm, name, omega):
    rb = arm.pose.bones["root"]; rb.rotation_mode = 'XYZ'
    # turning needs a little abduction: the upper leg may also swing sideways (bone Z = forward axis); knee stays a hinge
    # (also on the straight walk: the hinge-only leg left planted paws drifting 20-40 mm)
    for leg in LEGS: arm.pose.bones[f"upper_{leg}"].lock_ik_z = False
    k = omega * WALK_TURN_RADIUS / WALK_SPEED              # -1 (right) .. +1 (left)
    frames, prev = {}, None
    for f in range(WALK_FRAMES + 1):                       # frame N continues the arc (root motion accumulates)
        t = f / FPS; p = (f % WALK_FRAMES) / WALK_FRAMES
        pose, _, dz, locs = walk_pose(p)
        for b, deg in TURN_BEND.items():                   # body curves into the turn, head leads
            x, y, zz = pose.get(b, (0, 0, 0)); pose[b] = (x, y, zz + deg * k)
        for i in range(6):
            x, y, zz = pose.get(f"tail{i + 1}", (0, 0, 0)); pose[f"tail{i + 1}"] = (x, y, zz - 3.0 * k)
        targets = {}
        for leg in WALK_LEGS:
            loc, z, rot, toe, w = walk_target_turn(leg, t, omega)
            rest = Vector(LEGS[leg][1][2])
            targets[leg] = ((loc.x - rest.x, loc.y - rest.y, z), rot, paw_yaw(leg, t, omega))
        ch = pose_to_channels(arm, pose, dz, prev, locs); ch.update(target_channels(arm, targets, prev))
        c, th = root_path(t, omega)
        q = world_rot(rb, yaw=math.degrees(th))
        ch["root"] = (q.to_euler('XYZ', prev["root"][0]) if prev and "root" in prev else q.to_euler('XYZ'), bone_offset(rb, c))
        frames[f] = ch; prev = ch
    write_action(arm, name + "_src", frames, {leg: {0: 1.0} for leg in LEGS})
    act = bake_action(arm, name + "_src", name, 0, WALK_FRAMES)
    # measured: planted paws must stay put in the WORLD (root motion included)
    mute_all(arm, True); arm.animation_data.action = act; sc = bpy.context.scene; drift = {}; lift = {}
    T = WALK_FRAMES / FPS; rest = rest_points(arm)
    for leg in WALK_LEGS:
        td, lo, yc, H = WALK_LEGS[leg]
        for n in (-1, 0):
            fs = [f for f in range(WALK_FRAMES + 1) if (td + n) * T + 0.06 <= f / FPS <= (lo + n) * T - 0.3 * (lo - td) * T]
            if len(fs) < 2: continue
            pts = []
            for f in fs: sc.frame_set(f); pts.append(arm.matrix_world @ arm.pose.bones[f"foot_{leg}"].tail)
            drift[leg] = max(drift.get(leg, 0), round(max((q_ - pts[0]).xy.length for q_ in pts) * 1000))
            lift[leg] = max(lift.get(leg, -99), round((max(q_.z for q_ in pts) - rest[leg][0].z) * 1000))
    reach = {}                                   # worst (shoulder/hip -> wrist target distance) / (upper + lower length)
    arm.animation_data.action = bpy.data.actions[name + "_src"] if bpy.data.actions.get(name + "_src") else act
    mute_all(arm, False)
    for leg in LEGS: arm.pose.bones[f"upper_{leg}"].lock_ik_z = True          # back to the hinge for Pounce / Land
    # in place for export: drop the root's travel (Unity drives it with the same formulas; extracted root motion came out
    # in the root bone's rotated frame)
    for fc in [fc for fc in fcurves(act) if fc.data_path.startswith('pose.bones["root"]')]:
        for cont in ([act.fcurves] if hasattr(act, "fcurves") else [cb.fcurves for l in act.layers for s_ in l.strips for cb in s_.channelbags]):
            if fc in list(cont): cont.remove(fc)
    rb.location = (0, 0, 0); rb.rotation_euler = (0, 0, 0)
    info.setdefault("walk_rm", {})[name] = {"omega_deg_s": round(math.degrees(omega), 1), "stance_world_drift_mm": drift, "stance_lift_mm": lift}
    return act


def build_walk(arm):
    frames, prev = {}, None
    for f in range(WALK_FRAMES):
        pose, targets, dz, locs = walk_pose(f / WALK_FRAMES)
        ch = pose_to_channels(arm, pose, dz, prev, locs); ch.update(target_channels(arm, targets, prev))
        frames[f] = ch; prev = ch
    frames[WALK_FRAMES] = frames[0]
    write_action(arm, "Walk_src", frames, {leg: {0: 1.0} for leg in LEGS})
    act = bake_action(arm, "Walk_src", "Walk", 0, WALK_FRAMES)
    for fc in fcurves(act): fc.modifiers.new('CYCLES')
    # measured: planted paws must stay on the floor (lift > ~10 mm = the leg could not reach the target)
    mute_all(arm, True); arm.animation_data.action = act; sc = bpy.context.scene; rest = rest_points(arm); lift = {}
    slide = {}
    for f in range(WALK_FRAMES):
        sc.frame_set(f); pos = {l: arm.matrix_world @ arm.pose.bones[f"foot_{l}"].tail for l in LEGS}
        sc.frame_set(f + 1); pos1 = {l: arm.matrix_world @ arm.pose.bones[f"foot_{l}"].tail for l in LEGS}
        for leg in WALK_LEGS:
            y, z, rot, toe, w = walk_leg(leg, f / WALK_FRAMES)
            if w > 0.2 and z < 0.003:
                lift[leg] = max(lift.get(leg, -1), round((pos[leg].z - rest[leg][0].z) * 1000))
                slide[leg] = max(slide.get(leg, 0), round(abs((pos1[leg].y - pos[leg].y) * FPS - WALK_SPEED) / WALK_SPEED * 100))
    mute_all(arm, False)
    info["walk"] = {"frames": [0, WALK_FRAMES], "no_slide_speed_mps": WALK_SPEED, "stance_paw_lift_mm": lift, "stance_speed_err_pct": slide}


def gait_table(arm, act):
    """Measured contacts: paw within 1.5 cm of the floor and moving back at ground speed (+-35 %), per frame."""
    mute_all(arm, True); arm.animation_data.action = act; sc = bpy.context.scene; rest = rest_points(arm)
    pos = {l: [] for l in RUN_LEGS}
    for f in range(RUN_FRAMES + 1):
        sc.frame_set(f)
        for l in RUN_LEGS: pos[l].append(arm.matrix_world @ arm.pose.bones[f"foot_{l}"].tail)
    mute_all(arm, False)
    v_frame = RUN_SPEED / FPS; contacts = {}; rows = []
    for l in ("BL", "BR", "FR", "FL"):
        on = []
        for f in range(RUN_FRAMES):
            dy = (pos[l][f + 1].y - pos[l][f - 1 if f > 0 else RUN_FRAMES - 1].y) / 2
            if pos[l][f].z - rest[l][0].z < 0.015 and abs(dy - v_frame) < 0.35 * v_frame: on.append(f)
        contacts[l] = on
        rows.append(f"{l} " + "".join("#" if f in on else "." for f in range(RUN_FRAMES)))
    return contacts, rows


def stance_lift(arm, act):
    """Max height (mm) of each planted paw above the floor during its stance (> ~10 = leg could not reach)."""
    mute_all(arm, True); arm.animation_data.action = act; sc = bpy.context.scene; rest = rest_points(arm); out = {}
    for f in range(RUN_FRAMES):
        sc.frame_set(f)
        for leg in RUN_LEGS:
            y, z, rot, toe, w = run_leg(leg, f / RUN_FRAMES)
            if w > 0 and z < 0.005:
                h = (arm.matrix_world @ arm.pose.bones[f"foot_{leg}"].tail).z - rest[leg][0].z
                out[leg] = max(out.get(leg, -1), round(h * 1000))
    mute_all(arm, False)
    return out


# Pounce / Land key poses: {frame: (pose, hips_dz)}; legs not listed stay at rest
def legs(prefix, up, lo, ft, toe=0, spread=0):
    out = {}
    for leg in ([f"{prefix}L", f"{prefix}R"]):
        side = 1 if leg.endswith("L") else -1
        out[f"upper_{leg}"] = (up, -side * spread, 0); out[f"lower_{leg}"] = (lo, 0, 0)
        out[f"foot_{leg}"] = (ft, 0, 0); out[f"toe_{leg}"] = (toe, -side * spread * 1.2, 0)
    return out


def spine(a, b, c, d, neck1=0, neck2=0, head=0, tail1=0, tail=0, ears=0, hips=0):
    p = {"spine1": (a, 0, 0), "spine2": (b, 0, 0), "spine3": (c, 0, 0), "chest": (d, 0, 0), "neck1": (neck1, 0, 0),
         "neck2": (neck2, 0, 0), "head": (head, 0, 0), "ear_L": (ears, 0, 0), "ear_R": (ears, 0, 0), "hips": (hips, 0, 0)}
    for i in range(6): p[f"tail{i + 1}"] = (tail1 if i == 0 else tail, 0, 0)
    return p


POUNCE_END = 35
POUNCE_KEYS = {
    0:  ({}, 0.0),
    6:  ({**spine(4, 3, 3, 3, neck1=-6, neck2=-4, head=-6, tail1=10, tail=2, ears=-15), **legs("F", 12, 10, 5), **legs("B", 0, 0, 0)}, -0.12),
    9:  ({**spine(-4, -4, -3, -3, neck1=-10, neck2=-6, head=-10, tail1=5, ears=-25, hips=-6), **legs("F", -45, 70, 45), **legs("B", 0, 0, 0)}, -0.04),
    12: ({**spine(-3, -3, -2, -2, neck1=-8, neck2=-5, head=-14, tail1=15, tail=3, ears=-30, hips=-6),
          **legs("F", -70, 30, 12, -10, 4), **legs("B", 45, -12, 28, 10)}, 0.08),
    18: ({**spine(-3, -3, -3, -2, neck1=-8, neck2=-6, head=-20, tail1=25, tail=6, ears=-35, hips=-10),
          **legs("F", -84, -8, -22, -35, 14), **legs("B", 58, -15, 35, 20)}, 0.12),
    POUNCE_END: ({**spine(-1, -1, -1, 0, neck1=-6, neck2=-4, head=-16, tail1=18, tail=4, ears=-30, hips=1),
                  **legs("F", -75, -6, -12, -28, 10), **legs("B", 32, 8, -4, 8)}, 0.10),
}
POUNCE_IK = {"FL": {0: 1, 6: 1, 8: 0}, "FR": {0: 1, 6: 1, 8: 0}, "BL": {0: 1, 10: 1, 12: 0}, "BR": {0: 1, 10: 1, 12: 0}}
LAND_END = 27
LAND_KEYS = {
    0:  POUNCE_KEYS[POUNCE_END],
    4:  ({**spine(0, 0, 0, 1, neck1=-6, neck2=-4, head=-6, tail1=20, tail=4, ears=-25, hips=8),
          **legs("F", -28, 0, -6, 0, 4), **legs("B", 38, 22, -22, 5)}, 0.06),
    9:  ({**spine(1, 2, 4, 5, neck1=-8, neck2=-5, head=-8, tail1=14, tail=3, ears=-18, hips=6),
          **legs("F", 0, 0, 0), **legs("B", -18, 42, -40, 10)}, 0.0),
    12: ({**spine(3, 2, 2, 2, neck1=-4, neck2=-3, head=-4, tail1=8, tail=2, ears=-12, hips=0),
          **legs("F", 0, 0, 0), **legs("B", 0, 0, 0)}, -0.06),
    17: ({**spine(2, 1, 1, 2, neck1=-2, neck2=-1, head=-2, tail1=4, ears=-6), **legs("F", 0, 0, 0), **legs("B", 0, 0, 0)}, -0.08),
    22: ({**spine(0, 0, 0, 0, tail1=1), **legs("F", 0, 0, 0), **legs("B", 0, 0, 0)}, -0.01),
    LAND_END: ({}, 0.0),
}
LAND_IK = {"FL": {0: 0, 3: 0, 5: 1, 20: 1, 26: 0}, "FR": {0: 0, 3: 0, 5: 1, 20: 1, 26: 0},
           "BL": {0: 0, 10: 0, 12: 1, 20: 1, 26: 0}, "BR": {0: 0, 10: 0, 12: 1, 20: 1, 26: 0}}
LAND_TO_IDLE = (20, LAND_END)                     # blend the channels onto Idle frame 0 over these frames


def interp_keys(keys, f):
    ks = sorted(keys); f0 = max(k for k in ks if k <= f); f1 = min(k for k in ks if k >= f)
    if f0 == f1: return keys[f0]
    t = smooth01((f - f0) / (f1 - f0))
    (pa, da), (pb_, db) = keys[f0], keys[f1]
    names = set(pa) | set(pb_); z = (0, 0, 0)
    pose = {n: tuple(a + (b - a) * t for a, b in zip(pa.get(n, z), pb_.get(n, z))) for n in names}
    return pose, da + (db - da) * t


def idle_frame0(arm):
    arm.animation_data.action = bpy.data.actions["Idle"]
    bpy.context.scene.frame_set(0)
    out = {pb.name: (pb.rotation_euler.copy(), pb.location.copy()) for pb in arm.pose.bones if pb.bone.use_deform and pb.name not in DEFORM_SKIP}
    arm.animation_data.action = None
    return out


def build_oneshot(arm, name, keys, iks, end, to_idle=None):
    frames, prev = {}, None
    idle0 = idle_frame0(arm) if to_idle else None
    for f in range(end + 1):
        pose, dz = interp_keys(keys, f)
        ch = pose_to_channels(arm, pose, dz, prev)
        if to_idle and f >= to_idle[0]:
            w = smooth01((f - to_idle[0]) / (to_idle[1] - to_idle[0]))
            ch = {n: (Euler([a + (b - a) * w for a, b in zip(e, idle0[n][0])], 'XYZ'), l.lerp(idle0[n][1], w)) for n, (e, l) in ch.items()}
        ch.update(target_channels(arm, rest_targets(), prev))
        frames[f] = ch; prev = ch
    write_action(arm, name + "_src", frames, iks)
    return bake_action(arm, name + "_src", name, 0, end)


def mute_all(arm, mute):
    """Baked actions are plain FK: evaluate them with every constraint muted, as Unity will play them."""
    for pb in arm.pose.bones:
        for c in pb.constraints: c.mute = mute


def contact_report(arm, act, frames):
    """Lowest evaluated mesh vertex (m) at the given frames: paws in the floor if < 0."""
    mute_all(arm, True)
    arm.animation_data.action = act; sc = bpy.context.scene; out = {}
    for f in frames:
        sc.frame_set(f); dg = bpy.context.evaluated_depsgraph_get(); e = ob.evaluated_get(dg); me = e.to_mesh()
        out[f] = round(min((e.matrix_world @ v.co).z for v in me.vertices), 3); e.to_mesh_clear()
    mute_all(arm, False)
    return out


def airborne_frames(arm, act, end):
    """Frames where every paw is > 2 cm above its rest height (evaluated bones)."""
    mute_all(arm, True)
    arm.animation_data.action = act; sc = bpy.context.scene; rest = rest_points(arm); air = []; ext = []
    for f in range(end + 1):
        sc.frame_set(f)
        pb = arm.pose.bones
        hs = [min((arm.matrix_world @ pb[f"foot_{l}"].tail).z - rest[l][0].z, (arm.matrix_world @ pb[f"toe_{l}"].tail).z - rest[l][1].z) for l in LEGS]
        if min(hs) > 0.02: air.append(f)
        fr = (arm.matrix_world @ pb["toe_FL"].tail + arm.matrix_world @ pb["toe_FR"].tail) / 2
        hi = (arm.matrix_world @ pb["toe_BL"].tail + arm.matrix_world @ pb["toe_BR"].tail) / 2
        ext.append(((fr - hi).length, f))
    mute_all(arm, False)
    return air, max(ext)[1]


def build_pounce(arm):
    act = build_oneshot(arm, "Pounce", POUNCE_KEYS, POUNCE_IK, POUNCE_END)
    air, ext = airborne_frames(arm, act, POUNCE_END)
    info["pounce"] = {"frames": [0, POUNCE_END], "takeoff_frame": air[0] if air else None, "max_extension_frame": ext,
                      "contact_min_z": contact_report(arm, act, [0, 6, 9])}


def build_land(arm):
    act = build_oneshot(arm, "Land", LAND_KEYS, LAND_IK, LAND_END, to_idle=LAND_TO_IDLE)
    # continuity: Land first frame vs Pounce last, Land last vs Idle first (max pose-matrix difference)
    def mats(a, f):
        arm.animation_data.action = bpy.data.actions[a]; bpy.context.scene.frame_set(f)
        return {p.name: p.matrix.copy() for p in arm.pose.bones if p.bone.use_deform}
    mute_all(arm, True)
    def diff(m1, m2): return round(max(max(abs(x - y) for r1, r2 in zip(m1[n], m2[n]) for x, y in zip(r1, r2)) for n in m1), 4)
    info["land"] = {"frames": [0, LAND_END],
                    "start_vs_pounce_end": diff(mats("Land", 0), mats("Pounce", POUNCE_END)),
                    "end_vs_idle_start": diff(mats("Land", LAND_END), mats("Idle", 0)),
                    "contact_min_z": contact_report(arm, act, [5, 9, 12, 17, 22, LAND_END])}
    mute_all(arm, False)
    info["idle_contact_min_z"] = contact_report(arm, bpy.data.actions["Idle"], [0, 40, 64, 110])


def export(arm):
    out = os.path.join(ROOT, "blender", "export", "Characters", "WhiteTiger"); os.makedirs(os.path.join(out, "Textures"), exist_ok=True)
    # 2K base colour for Unity (T_<stem>_BC naming, see AssetPipeline.EnsureMaterials)
    m = ob.material_slots[0].material
    img = next(n.image for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image)
    small = img.copy(); small.scale(2048, 2048)
    small.filepath_raw = os.path.join(out, "Textures", "T_WhiteTiger_BC.png"); small.file_format = 'PNG'; small.save()
    bpy.data.images.remove(small)
    # every action is baked FK: drop the IK constraints so the exporter does not re-apply them
    for pb in arm.pose.bones:
        for c in list(pb.constraints): pb.constraints.remove(c)
    for a in [a for a in bpy.data.actions if a.name not in ("Idle", "Run", "Walk", "WalkTurnL", "WalkTurnR", "Pounce", "Land")]: bpy.data.actions.remove(a)
    arm.animation_data.action = bpy.data.actions["Idle"]
    ob.name = ob.data.name = "SK_WhiteTiger"
    deselect()
    ob.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
    s = dict(FBX_SETTINGS, object_types={'ARMATURE', 'MESH'}, use_armature_deform_only=True, add_leaf_bones=False,
             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_step=1.0,
             bake_anim_simplify_factor=0.0, bake_anim_force_startend_keying=True, primary_bone_axis='Y', secondary_bone_axis='X')
    bpy.context.scene.name = "Scene"                               # takes are "<rig>|<action>"
    fn = os.path.join(out, "SK_WhiteTiger.fbx")
    bpy.ops.export_scene.fbx(filepath=fn, **s)
    ob.data.calc_loop_triangles()
    dims = [round(d, 3) for d in ob.dimensions]
    man = {"name": "SK_WhiteTiger", "tris": len(ob.data.loop_triangles), "materials": [m.name], "dimensions_xyz": dims,
           "bones_deform": [b.name for b in arm.data.bones if b.use_deform], "root": "root", "hips": "hips",
           "clips": {"Idle": {"frames": [0, FRAMES], "fps": FPS, "loop": True},
                     "Run": {"frames": [0, RUN_FRAMES], "fps": FPS, "loop": True, "in_place": True,
                             "footfall": "BL, BR, FR, FL (rotary)", "flight_frames": info.get("run", {}).get("flight_frames"),
                             "no_slide_speed_mps_at_1x": RUN_SPEED, "suggested_playback_for_5.5mps": round(5.5 / RUN_SPEED, 2),
                             "note": "planted paws move back at no_slide_speed; root speed = that x playback speed"},
                     "Walk": {"frames": [0, WALK_FRAMES], "fps": FPS, "loop": True, "in_place": True, "drive": "forward at speed x playback",
                              "footfall": "BL, FL, BR, FR (lateral sequence)", "speed_mps_at_1x": WALK_SPEED},
                     "WalkTurnL": {"frames": [0, WALK_FRAMES], "fps": FPS, "loop": True, "in_place": True,
                                   "turn_deg_s_at_1x": round(math.degrees(WALK_SPEED / WALK_TURN_RADIUS), 1), "turn": "left (CCW)"},
                     "WalkTurnR": {"frames": [0, WALK_FRAMES], "fps": FPS, "loop": True, "in_place": True,
                                   "turn_deg_s_at_1x": round(math.degrees(WALK_SPEED / WALK_TURN_RADIUS), 1), "turn": "right (CW)"},
                     "Pounce": {"frames": [0, POUNCE_END], "fps": FPS, "loop": False, "in_place": True,
                                "takeoff_frame": info.get("pounce", {}).get("takeoff_frame"),
                                "max_extension_frame": info.get("pounce", {}).get("max_extension_frame"),
                                "hips_vertical_offset_only": True, "note": "flight arc + forward motion come from code"},
                     "Land": {"frames": [0, LAND_END], "fps": FPS, "loop": False, "in_place": True,
                              "starts_on": "Pounce last frame", "ends_on": "Idle frame 0"}},
           "facing": "Blender -Y = Unity +Z",
           "source": "sources/tiger/WhiteTiger (user-supplied)", "texture": "Textures/T_WhiteTiger_BC.png (2048)"}
    with open(os.path.join(out, "SK_WhiteTiger.json"), "w") as fh: json.dump(man, fh, indent=1)
    ob.name = ob.data.name = MESH                                   # keep the build re-runnable
    info["export"] = fn; info["tris"] = man["tris"]; info["dims"] = dims


arm = bpy.data.objects.get(RIG)
if "rig" in STAGES: arm = build_rig()
if "weights" in STAGES: skin(arm)
if "anim" in STAGES: animate(arm)
if "bake" in STAGES: bake(arm)
if any(k in STAGES for k in ("run", "walk", "pounce", "land")): setup_clip_rig(arm)
if "run" in STAGES: build_run(arm)
if "walk" in STAGES:
    w_ = WALK_SPEED / WALK_TURN_RADIUS
    for nm, om in (("Walk", 0.0), ("WalkTurnL", w_), ("WalkTurnR", -w_)): build_walk_rm(arm, nm, om)
if "pounce" in STAGES: build_pounce(arm)
if "land" in STAGES: build_land(arm)
if "export" in STAGES: export(arm)
bpy.ops.wm.save_mainfile()
result = info
