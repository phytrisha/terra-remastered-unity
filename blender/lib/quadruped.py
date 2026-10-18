# quadruped.py: procedural low-poly quadrupeds (body, rig, Idle + Walk, export) from a short spec.
# Prepend with BLENDER_LIBS="kit export audit review quadruped" tools/blender_run.sh blender/assets/<Name>/build.py
#
# Spec (dict, Blender coords: metres, Z up, head toward -Y, ground z = 0):
#   bones:   {name: (head, tail, parent, connected)}; legs named upper_/lower_/foot_<FL|FR|BL|BR>, plus root, hips,
#            spine*, chest, neck*, head, optional snout / ear_L / ear_R / tail*
#   volumes: [(a, b, ra, rb)] tapered capsules (points, radii) unioned as metaballs = the body; [(c, (sx, sy, sz))]
#            ellipsoids in `blobs` for ears, cheeks, ...
#   zones:   fn(p, n) -> material key; materials: {key: (name, rgb)}
#   tris, walk (duty, cycle frames, stance length, swing height, nod, roll), idle tweaks
# Body: metaballs -> mesh -> decimate to `tris` -> flat shading (faceted like the rest of the world)
# Rig: armature from the bones (deform) + toe_<leg> at each foot's ground end, IK targets per leg while animating,
#      bone-heat weights, baked to FK on export
# From an HD mesh instead (AI-generated, textured): q_rig binds the HD mesh itself (bones fitted to it), q_lowpoly
#   decimates that weighted mesh into the faceted forest version (weights survive decimation, face colours sampled from
#   the HD texture), so both share one rig and one silhouette (the portal swaps them per pixel). Optional spec key
#   "neutral": {leg: ground y} = stance centre of the walk when the rest pose is staggered (HD models stand mid-stride).
LEG_ORDER = {"BL": 0.0, "FL": 0.25, "BR": 0.5, "FR": 0.75}      # lateral-sequence walk


def q_use(arm, act):
    """Make act the armature's action AND bind its slot (layered actions, Blender 4.4+: a new action whose slot is not
    bound plays nothing, the rig stays in rest pose)."""
    if isinstance(act, str): act = bpy.data.actions[act]
    arm.animation_data_create(); arm.animation_data.action = act
    if hasattr(arm.animation_data, "action_slot") and len(getattr(act, "slots", [])) and arm.animation_data.action_slot is None:
        arm.animation_data.action_slot = act.slots[0]
    return act


def q_smooth(t): t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)


def q_body(spec, name, coll_):
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    mb = bpy.data.metaballs.new(name + "_mb"); mb.resolution = spec.get("meta_res", 0.02); mb.render_resolution = mb.resolution
    mb.threshold = 0.6
    mo = bpy.data.objects.new(name + "_mb", mb); coll_.objects.link(mo)
    for a, b, ra, rb in spec["volumes"]:
        a, b = Vector(a), Vector(b); L = (b - a).length
        n = max(2, int(L / (0.45 * min(ra, rb))) + 1)
        for i in range(n):
            t = i / (n - 1); e = mb.elements.new(); e.type = 'BALL'
            e.co = a.lerp(b, t); e.radius = (ra + (rb - ra) * t) * 1.35; e.stiffness = 2.0
    for c, s in spec.get("blobs", []):
        e = mb.elements.new(); e.type = 'ELLIPSOID'; e.co = Vector(c); e.radius = 1.0
        e.size_x, e.size_y, e.size_z = s; e.stiffness = 2.0
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(mo.evaluated_get(dg)); me.name = name
    bpy.data.objects.remove(mo, do_unlink=True); bpy.data.metaballs.remove(mb)
    ob = bpy.data.objects.new(name, me); coll_.objects.link(ob)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bm.to_mesh(me); bm.free()
    raw = len(me.polygons)
    dec = ob.modifiers.new("Decimate", 'DECIMATE'); dec.ratio = min(1.0, spec["tris"] / max(1, sum(len(p.vertices) - 2 for p in me.polygons)))
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=dec.name)
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    me.shade_flat()
    # flat-colour zones
    keys = list(spec["materials"])
    for k in keys:
        nm, rgb = spec["materials"][k]; me.materials.append(mat(nm, rgb, rough=0.85))
    for p in me.polygons:
        p.material_index = keys.index(spec["zones"](Vector(p.center), Vector(p.normal)))
    me.calc_loop_triangles()
    return ob, {"metaball_faces": raw, "tris": len(me.loop_triangles), "dims": [round(d, 3) for d in ob.dimensions]}


def q_rig(spec, name, ob, coll_):
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    arm = bpy.data.objects.new(name, bpy.data.armatures.new(name)); coll_.objects.link(arm)
    for o in bpy.context.scene.objects: o.select_set(False)
    bpy.context.view_layer.objects.active = arm; arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); eb = arm.data.edit_bones
    bones = spec["bones"]
    for n, (h, t, par, con) in bones.items():
        b = eb.new(n); b.head = h; b.tail = t
    for n, (h, t, par, con) in bones.items():
        b = eb[n]
        if par: b.parent = eb[par]; b.use_connect = con
        d = (Vector(t) - Vector(h)).normalized()
        b.align_roll(Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((0, -1, 0)))
    legs = [l for l in LEG_ORDER if f"lower_{l}" in bones]
    for l in legs:                                      # toe: the paw / hoof contact (QuadrupedLocomotion plants it, tilts it to the ground)
        g = Vector(bones[f"foot_{l}"][1])
        t = eb.new(f"toe_{l}"); t.head = g; t.tail = g + Vector((0, -0.05, 0)); t.parent = eb[f"foot_{l}"]; t.use_connect = True
        t.align_roll(Vector((0, 0, 1)))
    for l in legs:                                      # IK target at the wrist / hock, under root (non-deform)
        w = Vector(bones[f"foot_{l}"][0])
        t = eb.new(f"IK_{l}"); t.head = w; t.tail = w + Vector((0, 0, 0.06)); t.parent = eb["root"]; t.use_deform = False
        # ground point under the wrist / hock that moves with the IK target: the foot (cannon) tracks it, so it stays
        # near vertical instead of turning with the lower leg (the chital's hind foot flipped upside down in the swing)
        g = Vector(bones[f"foot_{l}"][1])
        gr = eb.new(f"GR_{l}"); gr.head = g; gr.tail = g + Vector((0, 0, 0.05)); gr.parent = t; gr.use_deform = False
    bpy.ops.object.mode_set(mode='OBJECT')
    for l in legs:
        c = arm.pose.bones[f"lower_{l}"].constraints.new('IK'); c.target = arm; c.subtarget = f"IK_{l}"; c.chain_count = 2
        d = arm.pose.bones[f"foot_{l}"].constraints.new('DAMPED_TRACK'); d.target = arm; d.subtarget = f"GR_{l}"; d.track_axis = 'TRACK_Y'
    for pb in arm.pose.bones: pb.rotation_mode = 'XYZ'
    # bone-heat weights on the (closed, metaball-born) mesh
    for o in bpy.context.scene.objects: o.select_set(False)
    ob.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    for n in [b.name for b in arm.data.bones if not b.use_deform]:
        if ob.vertex_groups.get(n): ob.vertex_groups.remove(ob.vertex_groups[n])
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
        bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    unweighted = sum(1 for v in ob.data.vertices if not any(g.weight > 1e-3 for g in v.groups))
    return arm, {"bones": len(arm.data.bones), "deform": sum(b.use_deform for b in arm.data.bones), "unweighted": unweighted}


def q_key(arm, f, pose, ik=None, hips_dz=0.0):
    """pose: {bone: (x, y, z) degrees}; ik: {leg: (dy, dz)} target offsets from rest; hips_dz: vertical bob (m)."""
    for pb in arm.pose.bones:
        if pb.name.startswith(("IK_", "GR_")): continue
        r = pose.get(pb.name, (0, 0, 0))
        pb.rotation_euler = Euler([math.radians(a) for a in r], 'XYZ'); pb.keyframe_insert("rotation_euler", frame=f)
    h = arm.pose.bones.get("hips")
    if h: h.location = (0, 0, 0); h.location = h.bone.matrix_local.inverted().to_3x3() @ Vector((0, 0, hips_dz)); h.keyframe_insert("location", frame=f)
    for l, (dy, dz) in (ik or {}).items():
        pb = arm.pose.bones[f"IK_{l}"]; pb.location = pb.bone.matrix_local.inverted().to_3x3() @ Vector((0, dy, dz))
        pb.keyframe_insert("location", frame=f)


def q_action(arm, name):
    a = bpy.data.actions.get(name)
    if a: bpy.data.actions.remove(a)
    arm.animation_data_create(); a = bpy.data.actions.new(name); arm.animation_data.action = a
    for pb in arm.pose.bones: pb.location = (0, 0, 0); pb.rotation_euler = (0, 0, 0)
    return a


def q_bake(arm, src, out, f0, f1):
    """Visual bake of an IK-driven action to plain FK (deform bones), constraints kept for the next action."""
    q_use(arm, src)
    for o in bpy.context.scene.objects: o.select_set(False)
    arm.select_set(True); bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    for pb in arm.pose.bones:                           # bone selection moved to the pose bone in newer Blender
        sel = not pb.name.startswith(("IK_", "GR_"))
        if hasattr(pb, "select"): pb.select = sel
        else: pb.bone.select = sel
    bpy.ops.nla.bake(frame_start=f0, frame_end=f1, only_selected=True, visual_keying=True, clear_constraints=False,
                     use_current_action=False, bake_types={'POSE'})
    bpy.ops.object.mode_set(mode='OBJECT')
    act = arm.animation_data.action
    old = bpy.data.actions.get(out)                     # a previous run's take (fake user): replace it, or the new bake
    if old and old != act: bpy.data.actions.remove(old)  # would become "<out>.001" and the stale one be exported
    act.name = out; act.use_fake_user = True             # kept in the .blend for stage reruns
    for fc in (act.fcurves if hasattr(act, "fcurves") else [fc for l in act.layers for s in l.strips for cb in s.channelbags for fc in cb.fcurves]):
        fc.modifiers.new('CYCLES')
    return act


def q_walk(spec, arm, fps=30):
    w = spec["walk"]; N = w["frames"]; duty = w["duty"]; L = w["stance"]; H = w["swing"]
    T = N / fps; speed = L / (duty * T)
    legs = [l for l in LEG_ORDER if f"IK_{l}" in arm.pose.bones]
    off = q_neutral_offsets(spec, legs)
    q_action(arm, "Walk_src")
    for f in range(N + 1):
        p = (f % N) / N; ik = {}; load = {}
        for l in legs:
            ph = (p - LEG_ORDER[l]) % 1.0
            if ph < duty:                                  # stance: straight back at ground speed
                s = ph / duty; ik[l] = (-L / 2 + L * s + off[l], 0.0); load[l] = math.sin(math.pi * s)
            else:                                          # swing: eased forward, lifted
                s = (ph - duty) / (1 - duty); e = q_smooth(s)
                ik[l] = (L / 2 - L * e + off[l], H * math.sin(math.pi * s) ** 0.9); load[l] = 0.0
        hind = load.get("BL", 0) - load.get("BR", 0); fore = load.get("FL", 0) - load.get("FR", 0)
        yaw = math.sin(2 * math.pi * (p - 0.12)); nod = math.cos(4 * math.pi * (p - 0.3))
        pose = {"hips": (0, w.get("roll", 2.0) * hind, 2.5 * yaw), "chest": (0, w.get("roll", 2.0) * fore, -1.0 * yaw)}
        for b in spec["bones"]:
            if b.startswith("spine"): pose[b] = (0, 0, -1.0 * yaw)
            if b.startswith("neck"): pose[b] = (w.get("nod", 2.0) * nod * 0.5, 0, 1.0 * yaw)
            if b.startswith("tail"): k = int(b[4:] or 1); pose[b] = (0, 0, (4 + 3 * k) * math.sin(2 * math.pi * (p - 0.12 - 0.07 * k)))
        pose["head"] = (-w.get("nod", 2.0) * nod * 0.5, 0, 0.6 * yaw)
        if "snout" in spec["bones"]: pose["snout"] = (3 * math.sin(2 * math.pi * (p - 0.4)), 0, 2 * yaw)
        q_key(arm, f, pose, ik, -0.006 + 0.006 * math.cos(4 * math.pi * (p - 0.08)))
    act = q_bake(arm, "Walk_src", "Walk", 0, N)
    return act, {"frames": N, "no_slide_speed_mps": round(speed, 3)}


def q_neutral_offsets(spec, legs):
    """IK dy per leg that moves its stance centre from the rest foot (foot_* tail) to spec["neutral"][leg]."""
    neu = spec.get("neutral", {})
    return {l: (neu[l] - spec["bones"][f"foot_{l}"][1][1]) if l in neu else 0.0 for l in legs}


def q_alert(spec, arm, fps=30, frames=60):
    """Alert (not looped): head and neck rise toward attention in ~0.5 s, ears forward, tail lifts (deer flag), then
    holds with breathing. Ends on a pose that blends back into Idle."""
    q_action(arm, "Alert_src")
    for f in range(frames + 1):
        t = f / fps; up = q_smooth(t / 0.5); breath = math.sin(2 * math.pi * t * 0.9)
        pose = {"chest": (-2 * up + 0.6 * breath, 0, 0)}
        for b in spec["bones"]:
            if b.startswith("spine"): pose[b] = (-1.5 * up + 0.4 * breath, 0, 0)
            if b.startswith("neck"): pose[b] = (-spec.get("alert_neck", 14) * up, 0, 0)
            if b.startswith("tail"): pose[b] = (-spec.get("alert_tail", 25) * up, 0, 0)
        pose["head"] = (spec.get("alert_head", 8) * up, 0, 0)
        if "ear_L" in spec["bones"]: pose["ear_L"] = (-12 * up, 0, -10 * up)
        if "ear_R" in spec["bones"]: pose["ear_R"] = (-12 * up, 0, 10 * up)
        if "snout" in spec["bones"]: pose["snout"] = (-10 * up + 3 * math.sin(2 * math.pi * t * 1.7), 0, 0)
        q_key(arm, f, pose, {l: (0.0, 0.0) for l in LEG_ORDER if f"IK_{l}" in arm.pose.bones}, -0.004 * breath)
    act = q_bake(arm, "Alert_src", "Alert", 0, frames)
    for fc in (act.fcurves if hasattr(act, "fcurves") else [fc for l in act.layers for s in l.strips for cb in s.channelbags for fc in cb.fcurves]):
        for m in list(fc.modifiers): fc.modifiers.remove(m)            # one-shot: no cycle
    return act, {"frames": frames}


def q_idle(spec, arm, fps=30, frames=150):
    q_action(arm, "Idle_src")
    for f in range(frames + 1):
        t = f / frames; tau = 2 * math.pi * t
        breath = math.sin(tau * 3)
        look = 14 * math.sin(tau) * q_smooth(abs(math.sin(tau))) ; lookp = 5 * math.sin(tau * 2 + 0.7)
        pose = {"chest": (0.8 * breath, 0, 0)}
        for b in spec["bones"]:
            if b.startswith("spine"): pose[b] = (0.5 * breath, 0, 0)
            if b.startswith("neck"): pose[b] = (lookp * 0.4, 0, look * 0.35)
            if b.startswith("tail"): k = int(b[4:] or 1); pose[b] = (0, 0, (3 + 2 * k) * math.sin(tau * 2 - 0.4 * k))
        pose["head"] = (lookp * 0.6, 0, look * 0.65)
        flick = math.exp(-((t - 0.42) / 0.012) ** 2) * 25
        if "ear_L" in spec["bones"]: pose["ear_L"] = (flick, 0, 6 * math.sin(tau))
        if "ear_R" in spec["bones"]: pose["ear_R"] = (0, 0, -6 * math.sin(tau + 1))
        if "snout" in spec["bones"]: pose["snout"] = (8 * math.sin(tau * 4) * q_smooth(math.sin(tau * 0.5)), 0, 6 * math.sin(tau * 3))
        q_key(arm, f, pose, {l: (0.0, 0.0) for l in LEG_ORDER if f"IK_{l}" in arm.pose.bones}, -0.003 * breath)
    act = q_bake(arm, "Idle_src", "Idle", 0, frames)
    return act, {"frames": frames}


def q_lowpoly(hd, name, tris, classify, materials, coll_, samples=7, smooth=0):
    """Faceted forest twin of a weighted HD mesh: decimated copy (vertex groups and UVs survive the collapse), flat
    shading, one flat material per face from the HD base colour sampled over the face. classify(samples) -> material key
    (samples: list of rgb Vectors over the face, so small details like spots can win on a minority of samples)."""
    import numpy as np
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    me = hd.data.copy(); me.name = name
    ob = bpy.data.objects.new(name, me); coll_.objects.link(ob)
    ob.parent = hd.parent; ob.matrix_world = hd.matrix_world.copy()
    for m in hd.modifiers:
        if m.type == 'ARMATURE': a = ob.modifiers.new("Armature", 'ARMATURE'); a.object = m.object
    dec = ob.modifiers.new("Decimate", 'DECIMATE'); dec.ratio = min(1.0, tris / max(1, len(me.polygons)))
    dec.use_collapse_triangulate = True
    bpy.ops.object.select_all(action='DESELECT')
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_move_to_index(modifier=dec.name, index=0)
        bpy.ops.object.modifier_apply(modifier=dec.name)
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    me.shade_flat()
    # base colour of the HD material
    img = next(n.image for n in hd.data.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.colorspace_settings.name == 'sRGB')
    W, H = img.size; px = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H, W, 4)
    uv = me.uv_layers.active.data
    me.materials.clear()
    keys = list(materials)
    for k in keys: nm, rgb = materials[k]; me.materials.append(mat(nm, rgb, rough=0.85))
    counts = {k: 0 for k in keys}
    for p in me.polygons:
        cs = [uv[i].uv for i in p.loop_indices]; c = sum(cs, Vector((0, 0))) / len(cs)
        pts = [c] + [c.lerp(q, 0.6) for q in cs] + [c.lerp((cs[i] + cs[(i + 1) % len(cs)]) / 2, 0.6) for i in range(len(cs))]
        cols = []
        for q in pts[:samples]:
            x = min(W - 1, max(0, int((q.x % 1.0) * W))); y = min(H - 1, max(0, int((q.y % 1.0) * H)))
            cols.append(Vector(px[y, x, :3].tolist()))
        p.material_index = keys.index(classify(cols))
    # optional majority pass: a face that disagrees with 2+ of its 3 neighbours takes their colour (removes the sliver
    # spikes decimation leaves along colour borders; off where single-facet details matter, e.g. spots)
    for _ in range(smooth):
        bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
        cur = [f.material_index for f in bm.faces]; new = list(cur)
        for f in bm.faces:
            nb = [g.index for e in f.edges for g in e.link_faces if g is not f]
            votes = {}
            for g in nb: votes[cur[g]] = votes.get(cur[g], 0) + 1
            best = max(votes, key=votes.get) if votes else cur[f.index]
            if best != cur[f.index] and votes[best] >= 2: new[f.index] = best
        for f in bm.faces: f.material_index = new[f.index]
        bm.to_mesh(me); bm.free()
    for p in me.polygons: counts[keys[p.material_index]] += 1
    me.calc_loop_triangles()
    return ob, {"tris": len(me.loop_triangles), "faces_per_material": counts}


def q_export(spec, name, ob, arm, out_dir, clips, extra=()):
    os.makedirs(out_dir, exist_ok=True)
    for a in [a for a in bpy.data.actions if a.name not in clips]: bpy.data.actions.remove(a)
    for pb in arm.pose.bones:
        for c in list(pb.constraints): pb.constraints.remove(c)
    q_use(arm, "Idle")
    for o in bpy.context.scene.objects: o.select_set(False)
    ob.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
    for e in extra: e.select_set(True)                  # e.g. the HD fur mesh in the same file, on the same rig
    s = dict(FBX_SETTINGS, object_types={'ARMATURE', 'MESH'}, use_armature_deform_only=True, add_leaf_bones=False,
             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_step=1.0,
             bake_anim_simplify_factor=0.0, bake_anim_force_startend_keying=True, primary_bone_axis='Y', secondary_bone_axis='X')
    bpy.context.scene.name = "Scene"
    fn = os.path.join(out_dir, f"{name}.fbx")
    bpy.ops.export_scene.fbx(filepath=fn, **s)
    ob.data.calc_loop_triangles()
    man = {"name": name, "tris": len(ob.data.loop_triangles), "materials": [m.name for m in ob.data.materials],
           "dimensions_xyz": [round(d, 3) for d in ob.dimensions], "bones_deform": [b.name for b in arm.data.bones if b.use_deform],
           "clips": clips, "facing": "Blender -Y = Unity +Z"}
    with open(os.path.join(out_dir, f"{name}.json"), "w") as fh: json.dump(man, fh, indent=1)
    return fn, man


def q_pipeline_hd(spec, asset, stages, classify, look_at=(0, -0.1, 0.6)):
    """Animal from an ingested HD mesh (blender/assets/<asset>HD/<asset>HD.blend, collection SM_<asset>HD):
    stages hd (append it as SK_<asset>HD), rig (fitted bones, bone heat on the HD mesh), low (faceted SK_<asset>),
    idle, walk, alert, export (both FBX + textures, same rig and takes), review. Saves blender/assets/<asset>/<asset>.blend."""
    import shutil
    name, hd_name, rig = f"SK_{asset}", f"SK_{asset}HD", f"{asset}_Rig"
    out = os.path.join(ROOT, "blender", "export", "Characters", asset)
    for n in ("Cube", "Light", "Camera"):
        if bpy.data.objects.get(n): bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    c_ = coll(f"{asset}_Body", asset)
    info = {}
    hd = bpy.data.objects.get(hd_name); ob = bpy.data.objects.get(name); arm = bpy.data.objects.get(rig)
    if "hd" in stages:
        for o in [o for o in (hd, ob, arm) if o]: bpy.data.objects.remove(o, do_unlink=True)
        src = os.path.join(ROOT, "blender", "assets", f"{asset}HD", f"{asset}HD.blend")
        with bpy.data.libraries.load(src, link=False) as (df, dt): dt.objects = [f"SM_{asset}HD"]
        hd = dt.objects[0]; hd.name = hd_name; hd.data.name = hd_name; c_.objects.link(hd)
        for m in list(hd.modifiers): hd.modifiers.remove(m)
        hd.vertex_groups.clear()
        bpy.ops.object.select_all(action='DESELECT')
        with bpy.context.temp_override(object=hd, active_object=hd, selected_objects=[hd]):
            bpy.ops.object.shade_smooth()
        hd.data.calc_loop_triangles()
        info["hd"] = {"tris": len(hd.data.loop_triangles), "dims": [round(d, 3) for d in hd.dimensions]}
    if "rig" in stages: arm, info["rig"] = q_rig(spec, rig, hd, c_)
    if "low" in stages: ob, info["low"] = q_lowpoly(hd, name, spec["tris"], classify, spec["materials"], c_, smooth=spec.get("zone_smooth", 0))
    fur = bpy.data.objects.get(hd_name + "_Fur")
    if "fur" in stages and spec.get("fur_len") is not None: fur, info["fur"] = q_furmesh(hd, spec, c_)
    if "idle" in stages: _, info["idle"] = q_idle(spec, arm)
    if "walk" in stages: _, info["walk"] = q_walk(spec, arm)
    if "alert" in stages: _, info["alert"] = q_alert(spec, arm)
    clips = ["Idle", "Walk", "Alert"]
    if "export" in stages:
        detail = {k: info.get(k.lower()) for k in clips}
        for o in (ob, hd):
            fn, man = q_export(spec, o.name, o, arm, out, clips, extra=[fur] if (o is hd and fur) else [])
            man["clips_detail"] = detail
            if o is hd:   # textures next to the FBX (Unity: AssetImportRules.Textures4K, assigned by the scene builder)
                tdir = os.path.join(out, "Textures"); os.makedirs(tdir, exist_ok=True); man["textures"] = []
                for n in hd.data.materials[0].node_tree.nodes:
                    if n.type != 'TEX_IMAGE' or not n.image: continue
                    tag = "BC" if n.image.colorspace_settings.name == 'sRGB' else "N"
                    dst = os.path.join(tdir, f"T_{asset}HD_{tag}.png"); shutil.copyfile(bpy.path.abspath(n.image.filepath), dst)
                    man["textures"].append(f"Textures/T_{asset}HD_{tag}.png")
            with open(os.path.join(out, f"{o.name}.json"), "w") as fh: json.dump(man, fh, indent=1)
        info["export"] = out
    if "review" in stages:
        sc = bpy.context.scene; cam = lookdev_setup(res=(800, 560)); outs = []
        for target in (ob, hd):
            for o in sc.objects: o.hide_render = o.type == 'MESH' and o is not target and not o.name.startswith("Lookdev")
            for tag, act, f, pos in (("side", "Idle", 0, (-5.5, -0.1, 0.8)), ("front3q", "Idle", 0, (-3.2, -4.0, 1.6)), ("rear3q", "Idle", 0, (2.8, 3.6, 1.5)),
                                     ("walk08", "Walk", 8, (-6, -0.1, 0.6)), ("walk16", "Walk", 16, (-6, -0.1, 0.6)), ("alert40", "Alert", 40, (-3.2, -4.0, 1.6))):
                q_use(arm, act); sc.frame_set(f)
                cam.data.type = 'PERSP'; cam.data.lens = 50; cam.location = pos
                cam.rotation_euler = (Vector(look_at) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
                sc.render.filepath = os.path.join(ROOT, "renders", asset, f"rv_{'hd' if target is hd else 'lo'}_{tag}.png")
                bpy.ops.render.render(write_still=True); outs.append(sc.render.filepath)
        info["review"] = len(outs)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "blender", "assets", asset, f"{asset}.blend"))
    return info


# ------------------------------------------------------------------ shell-fur data (Unity FurShell / TigerFur contract)
# UV1 "FurFlow": growth direction in the UV0 tangent frame; UV2 "FurData": (UV0 units per metre, length mask 0..~1.3).
# Same layout as WhiteTigerHD/build.py's fur stage, with a generic, rig-driven flow and mask for the quadruped rigs.
def q_fur(ob, spec):
    import numpy as np
    me = ob.data; uv0 = me.uv_layers[0].name
    for n in ("FurFlow", "FurData"):
        if me.uv_layers.get(n): me.uv_layers.remove(me.uv_layers[n])
    nv = len(me.vertices)
    V = np.empty(nv * 3); me.vertices.foreach_get("co", V); V = V.reshape(-1, 3)
    N = np.empty(nv * 3); me.vertices.foreach_get("normal", N); N = N.reshape(-1, 3)
    me.calc_loop_triangles(); T = np.array([t.vertices[:] for t in me.loop_triangles])
    arm = next(m.object for m in ob.modifiers if m.type == 'ARMATURE')
    def bone_dir(n):
        b = arm.data.bones[n]; d = b.tail_local - b.head_local; return np.array(d.normalized())
    body = np.array((0.0, 1.0, -0.3)); body /= np.linalg.norm(body)              # from the head toward the tail, a little down
    leg = np.array((0.0, 0.15, -1.0)); leg /= np.linalg.norm(leg)
    names = {g.index: g.name for g in ob.vertex_groups}
    F = np.zeros((nv, 3)); W = {k: np.zeros(nv) for k in ("leg", "tail", "head", "ear", "snout")}
    for v in me.vertices:
        for g in v.groups:
            n = names[g.group]
            if n.startswith(("upper_", "lower_", "foot_", "toe_")): d = leg; W["leg"][v.index] += g.weight
            elif n.startswith("tail"): d = bone_dir(n); W["tail"][v.index] += g.weight
            elif n.startswith("ear"): d = bone_dir(n); W["ear"][v.index] += g.weight
            else:
                d = body
                if n == "head": W["head"][v.index] += g.weight
                if n == "snout": W["snout"][v.index] += g.weight
            F[v.index] += g.weight * d
    F -= N * np.einsum('ij,ij->i', F, N)[:, None]
    F /= np.maximum(np.linalg.norm(F, axis=1, keepdims=True), 1e-6)
    # length mask by region, from spec["fur_len"] (relative): face, snout, legs, ears, tail, hooves
    fl = spec.get("fur_len", {})
    L = np.ones(nv)
    L = np.where(W["leg"] > 0.5, fl.get("legs", 0.6), L)
    L = np.where(W["head"] + W["snout"] > 0.5, fl.get("face", 0.45), L)
    L = np.where(W["snout"] > 0.5, fl.get("snout", 0.2), L)
    L = np.where(W["ear"] > 0.5, fl.get("ears", 0.6), L)
    L = np.where(W["tail"] > 0.5, fl.get("tail", 1.2), L)
    L = np.where(V[:, 2] < spec.get("hoof_h", 0.06), 0.0, L)                     # hooves: bare
    # bare skin on the head from the texture: very dark patches (eyes, nose, lips)
    m = ob.material_slots[0].material
    img = next(n.image for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.colorspace_settings.name == 'sRGB')
    w_, h_ = img.size; px = np.empty(w_ * h_ * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(h_, w_, 4)[..., :3]
    uvl = np.empty(len(me.loops) * 2); me.uv_layers[uv0].data.foreach_get("uv", uvl); uvl = uvl.reshape(-1, 2)
    lvi = np.empty(len(me.loops), np.int64); me.loops.foreach_get("vertex_index", lvi)
    col = px[np.clip((uvl[:, 1] % 1) * h_, 0, h_ - 1).astype(int), np.clip((uvl[:, 0] % 1) * w_, 0, w_ - 1).astype(int)]
    vc = np.zeros((nv, 3)); cn = np.zeros(nv); np.add.at(vc, lvi, col); np.add.at(cn, lvi, 1); vc /= np.maximum(cn, 1)[:, None]
    lum = vc.mean(1)
    bare = (W["head"] + W["snout"] > 0.5) & (lum < spec.get("bare_lum", 0.06))
    L[bare] = 0.0
    # soften borders (neighbour average)
    E = np.r_[T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]]
    deg = np.bincount(E.ravel(), minlength=nv).astype(float)
    for _ in range(4):
        acc = np.zeros(nv); np.add.at(acc, E[:, 0], L[E[:, 1]]); np.add.at(acc, E[:, 1], L[E[:, 0]])
        L = 0.5 * L + 0.5 * acc / np.maximum(deg, 1)
    # strand density: UV0 units per metre
    lt = np.array([t.loops[:] for t in me.loop_triangles])
    a3 = np.linalg.norm(np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]]), axis=1) / 2
    a2 = np.abs(np.cross(uvl[lt[:, 1]] - uvl[lt[:, 0]], uvl[lt[:, 2]] - uvl[lt[:, 0]])) / 2
    dens = np.sqrt(a2 / np.maximum(a3, 1e-12))
    acc = np.zeros(nv); w = np.zeros(nv)
    for k in range(3): np.add.at(acc, T[:, k], dens * a3); np.add.at(w, T[:, k], a3)
    D = acc / np.maximum(w, 1e-12); D[D <= 0] = np.median(D[D > 0])
    me.calc_tangents(uvmap=uv0)
    nl = len(me.loops)
    tan = np.empty(nl * 3); me.loops.foreach_get("tangent", tan); tan = tan.reshape(-1, 3)
    bit = np.empty(nl * 3); me.loops.foreach_get("bitangent", bit); bit = bit.reshape(-1, 3)
    flw = me.uv_layers.new(name="FurFlow"); fd = me.uv_layers.new(name="FurData")
    fx = np.einsum('ij,ij->i', F[lvi], tan); fy = np.einsum('ij,ij->i', F[lvi], bit)
    flw.data.foreach_set("uv", np.c_[fx, fy].ravel()); fd.data.foreach_set("uv", np.c_[D[lvi], L[lvi]].ravel())
    me.uv_layers.active = me.uv_layers[uv0]; me.uv_layers[uv0].active_render = True
    me.free_tangents()
    return {"bare_head_verts": int(bare.sum()), "no_fur_verts": int((L < 0.02).sum()), "len_mean": round(float(L.mean()), 2),
            "uv_per_m_median": round(float(np.median(D)), 3)}


def q_furmesh(hd, spec, coll_):
    """Decimated copy of the weighted HD mesh carrying the shells (FurShell draws every shell over the whole mesh: the
    tiger's 89k -> 22k copy cut the close-up GPU time ~40 %), with its own fur data and baked AO."""
    name = hd.name + "_Fur"
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    fo = hd.copy(); fo.data = hd.data.copy(); fo.name = fo.data.name = name; coll_.objects.link(fo)
    for m in list(fo.modifiers):
        if m.type != 'ARMATURE': fo.modifiers.remove(m)
    n0 = len(fo.data.polygons)
    dec = fo.modifiers.new("Decimate", 'DECIMATE'); dec.ratio = spec.get("fur_tris", 20000) / n0; dec.use_collapse_triangulate = True
    with bpy.context.temp_override(object=fo, active_object=fo, selected_objects=[fo]):
        bpy.ops.object.modifier_move_to_index(modifier=dec.name, index=0)
        bpy.ops.object.modifier_apply(modifier=dec.name)
        bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
        bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    info = q_fur(fo, spec)
    info["tris"] = len(fo.data.polygons)
    info["ao"] = q_bake_ao(fo, spec.get("ao_dist", 0.08))
    return fo, info


def q_bake_ao(fo, dist, samples=64):
    import numpy as np
    sc = bpy.context.scene; arm = next(m.object for m in fo.modifiers if m.type == 'ARMATURE')
    arm.data.pose_position = 'REST'; bpy.context.view_layer.update()
    eng = sc.render.engine; sc.render.engine = 'CYCLES'; sc.cycles.samples = samples
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences; prefs.compute_device_type = 'METAL'; prefs.get_devices()
        for d in prefs.devices: d.use = True
        sc.cycles.device = 'GPU'
    except Exception: sc.cycles.device = 'CPU'
    w = sc.world or bpy.data.worlds.new("World"); sc.world = w; w.light_settings.distance = dist
    me = fo.data
    for a in [a for a in me.color_attributes if a.name == "AO"]: me.color_attributes.remove(a)
    ca = me.color_attributes.new("AO", 'FLOAT_COLOR', 'POINT'); me.color_attributes.active_color = ca
    hidden = [o for o in sc.objects if o.type == 'MESH' and o is not fo and not o.hide_render]
    for o in hidden: o.hide_render = True                                    # only its own occlusion
    bpy.ops.object.select_all(action='DESELECT'); fo.select_set(True); bpy.context.view_layer.objects.active = fo
    bpy.ops.object.bake(type='AO', target='VERTEX_COLORS')
    for o in hidden: o.hide_render = False
    arm.data.pose_position = 'POSE'; sc.render.engine = eng
    vals = np.empty(len(me.vertices) * 4, np.float32); ca.data.foreach_get("color", vals); ao = vals.reshape(-1, 4)[:, 0]
    return {"mean": round(float(ao.mean()), 3), "p5": round(float(np.percentile(ao, 5)), 3)}
