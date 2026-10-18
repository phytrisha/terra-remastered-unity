# export.py: masks, LODs, collision and engine export for one asset collection. Needs kit.py.
#
# export_collection("SM_Crate", out_dir) writes, into out_dir:
#   SM_Crate.fbx         LOD0 + UCX_SM_Crate_NN convex hulls (Unity: convex MeshColliders; Unreal: UCX collision)
#   SM_Crate_LOD1.fbx    decimated LODs (one file per LOD; Unity keys mesh sub-assets by name, so names stay stable)
#   SM_Crate_LOD2.fbx
#   SM_Crate.glb         optional quick-look / web / Godot copy (vertex colours off, see below)
#   SM_Crate.json        manifest: tri counts per LOD, materials, bounds, anchors
# Mesh channels: UV0 "UVMap" (tiling, metres / tile), UV1 "UVLightmap" (non-overlapping, for baked GI),
# vertex colour "Col" (linear): R = custom mask (dirt, moss, snow...), G = bevel edge, B = per-part random, A = baked AO.
import json


FBX_SETTINGS = dict(
    use_selection=True, object_types={'MESH'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
    axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
    use_tspace=True, colors_type='LINEAR', prioritize_active_color=True, add_leaf_bones=False, bake_anim=False,
    path_mode='STRIP', embed_textures=False)
# Plants and other assets with wind/authored normals: identity root transform, keep custom normals.
FBX_SETTINGS_VEGETATION = dict(FBX_SETTINGS, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                               mesh_smooth_type='OFF')


def apply_masks(root_name, mask_fn=None):
    """Write the Col attribute on every mesh of the asset from the kit's face layers.
    mask_fn(obj, world_pos, world_normal, edge, var) -> R value (0-1); default: up-facing (dust/moss/snow catchers)."""
    root = bpy.data.collections[root_name]
    objs = [o for c in ([root] + list(root.children_recursive)) for o in c.objects if o.type == 'MESH']
    for o in objs:
        me = o.data
        ea = me.attributes.get("edge"); va = me.attributes.get("var")
        rv = random.Random(hash(o.name)).random()
        if "Col" in me.color_attributes: me.color_attributes.remove(me.color_attributes["Col"])
        col = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
        vals = [0.0] * (len(me.loops) * 4)
        mw = o.matrix_world; nm = mw.to_3x3()
        for poly in me.polygons:
            e = ea.data[poly.index].value if ea else 0.0
            v = va.data[poly.index].value if va else rv
            n = (nm @ poly.normal).normalized()
            for li in poly.loop_indices:
                p = mw @ me.vertices[me.loops[li].vertex_index].co
                r = mask_fn(o, p, n, e, v) if mask_fn else max(0.0, n.z) ** 2
                vals[li * 4: li * 4 + 4] = (r, e, v, 1.0)
        col.data.foreach_set("color", vals)
        me.color_attributes.active_color = col
        me.color_attributes.render_color_index = me.color_attributes.find("Col")
    return len(objs)


def _bake_vertex_ao(ob, root, skip_materials=(), distance=0.6, samples=24):
    """Cycles AO into Col.A (remapped 0.4-1). Only meaningful on dense parts: big flat faces and overlapping shingles
    bake near-black, so list their materials in skip_materials. The source parts are hidden from render while baking,
    or they occlude the joined copy completely."""
    sc = bpy.context.scene; me = ob.data
    if sc.world is None: sc.world = bpy.data.worlds.new("World")       # factory-empty scenes have none
    tmp = me.color_attributes.new("AO_tmp", 'BYTE_COLOR', 'CORNER'); me.color_attributes.active_color = tmp
    root.hide_render = True; ob.hide_render = False
    old_engine = sc.render.engine
    sc.render.engine = 'CYCLES'; sc.cycles.samples = samples; sc.world.light_settings.distance = distance
    for o in sc.objects: o.select_set(False)
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    try:
        bpy.ops.object.bake(type='AO', target='VERTEX_COLORS')
    finally:
        sc.render.engine = old_engine; root.hide_render = False
    a = [0.0] * (len(me.loops) * 4); c = [0.0] * (len(me.loops) * 4)
    me.color_attributes["AO_tmp"].data.foreach_get("color", a); me.color_attributes["Col"].data.foreach_get("color", c)
    skip = {i for i, s in enumerate(ob.material_slots) if s.material and any(k in s.material.name for k in skip_materials)}
    for p in me.polygons:
        if p.material_index in skip: continue
        for i in p.loop_indices: c[i * 4 + 3] *= 0.4 + 0.6 * a[i * 4]
    me.color_attributes["Col"].data.foreach_set("color", c)
    me.color_attributes.remove(me.color_attributes["AO_tmp"])
    me.color_attributes.active_color = me.color_attributes["Col"]


def _hull(name, pts, coll_):
    bm = bmesh.new(); vs = [bm.verts.new(p) for p in pts]
    r = bmesh.ops.convex_hull(bm, input=vs)
    bmesh.ops.delete(bm, geom=list({g for g in r["geom_interior"] + r["geom_unused"] if isinstance(g, bmesh.types.BMVert)}), context='VERTS')
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); coll_.objects.link(ob)
    return ob


def hull_from_objects(*names):
    """Point list for one convex hull from the world-space vertices of these objects."""
    pts = []
    for n in names:
        o = bpy.data.objects.get(n)
        if o: pts += [o.matrix_world @ v.co for v in o.data.vertices]
    return pts


def export_collection(root_name, out_dir, hulls=None, lods=((0.5, ()), (0.2, ())), ao=True, ao_skip=(),
                      mask_fn=None, glb=True, vegetation=False, anchors=None, hard_angle=32, smooth_keys=()):
    """Join, clean, mask, LOD, hull and export one SM_ collection. Returns the manifest dict.
    hulls: list of point lists (see hull_from_objects); None = one hull around the whole asset. [] = no collision.
    lods: ((ratio, drop_material_keywords), ...). Typical budgets: LOD1 25-50 %, LOD2 2-20 % (see asset-pipeline skill).
    anchors: {"name": (x, y, z)} in Blender coords (lights, doors, sockets) written to the manifest for Unity placement."""
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    ec = bpy.data.collections.get("Export_Tmp") or bpy.data.collections.new("Export_Tmp")
    if ec.name not in sc.collection.children: sc.collection.children.link(ec)
    for o in list(ec.objects): bpy.data.objects.remove(o, do_unlink=True)
    solo(root_name, "Export_Tmp")
    root = bpy.data.collections[root_name]
    src = [o for c in ([root] + list(root.children_recursive)) for o in c.objects if o.type == 'MESH']
    if not src: raise RuntimeError(f"{root_name}: no meshes")
    if not any("Col" in o.data.color_attributes for o in src): apply_masks(root_name, mask_fn)
    copies = []
    for o in src:
        me = o.data.copy()
        bm = bmesh.new(); bm.from_mesh(me)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0004)
        bmesh.ops.dissolve_degenerate(bm, dist=0.0002, edges=bm.edges)
        soft = any(k in o.name for k in smooth_keys)
        for f in bm.faces: f.smooth = True
        for e in bm.edges:
            e.smooth = soft or not (e.is_boundary or (len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(hard_angle)))
        bm.to_mesh(me); bm.free()
        for u in me.uv_layers: u.name = "UVMap"; break
        ob = bpy.data.objects.new(o.name + "_x", me); ob.matrix_world = o.matrix_world.copy(); ec.objects.link(ob); copies.append(ob)
    for o in sc.objects: o.select_set(False)
    for o in copies: o.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    if len(copies) > 1: bpy.ops.object.join()
    lod0 = bpy.context.view_layer.objects.active
    lod0.name = lod0.data.name = root_name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if not vegetation: soft_normals(lod0)
    me = lod0.data
    if ao and not vegetation: _bake_vertex_ao(lod0, root, ao_skip)
    # lightmap UVs: non-overlapping, never mirrored. Authored here so Unity's generateSecondaryUV stays off.
    if "UVLightmap" not in me.uv_layers: me.uv_layers.new(name="UVLightmap")
    me.uv_layers.active = me.uv_layers["UVLightmap"]
    for o in sc.objects: o.select_set(False)
    lod0.select_set(True); bpy.context.view_layer.objects.active = lod0
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.002, area_weight=0.0, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    me.uv_layers.active = me.uv_layers["UVMap"]; me.uv_layers["UVMap"].active_render = True
    objs = [lod0]
    for i, (ratio, drop) in enumerate(lods or ()):
        m2 = me.copy(); ob = bpy.data.objects.new(f"{root_name}_LOD{i + 1}", m2); m2.name = ob.name; ec.objects.link(ob)
        if drop:
            bm = bmesh.new(); bm.from_mesh(m2)
            idx = {j for j, s in enumerate(lod0.material_slots) if any(d in s.material.name for d in drop)}
            bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in idx], context='FACES'); bm.to_mesh(m2); bm.free()
            used = {p.material_index for p in m2.polygons}
            for j in reversed(range(len(m2.materials))):
                if j not in used: m2.materials.pop(index=j)          # no empty submeshes in Unity
        md = ob.modifiers.new("Decimate", 'DECIMATE'); md.ratio = ratio; md.use_collapse_triangulate = True
        with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
            bpy.ops.object.modifier_apply(modifier=md.name)
        if not vegetation: soft_normals(ob)
        objs.append(ob)
    if hulls is None:
        hulls = [[lod0.matrix_world @ v.co for v in me.vertices]]
    ucx = [_hull(f"UCX_{root_name}_{i:02d}", pts, ec) for i, pts in enumerate(hulls)]
    fbx = FBX_SETTINGS_VEGETATION if vegetation else FBX_SETTINGS
    manifest = {"name": root_name, "lods": {}, "materials": [s.material.name for s in lod0.material_slots if s.material],
                "dimensions": [round(d, 3) for d in lod0.dimensions], "hulls": len(ucx), "anchors": anchors or {}}
    for i, ob in enumerate(objs):
        for o in sc.objects: o.select_set(False)
        for o in [ob] + (ucx if i == 0 else []): o.select_set(True)
        bpy.context.view_layer.objects.active = ob
        fn = f"{out_dir}/{root_name}.fbx" if i == 0 else f"{out_dir}/{root_name}_LOD{i}.fbx"
        bpy.ops.export_scene.fbx(filepath=fn, **fbx)
        ob.data.calc_loop_triangles(); manifest["lods"][f"LOD{i}"] = len(ob.data.loop_triangles)
    if glb:
        for o in sc.objects: o.select_set(False)
        lod0.select_set(True); bpy.context.view_layer.objects.active = lod0
        tri = lod0.modifiers.new("Tri", 'TRIANGULATE'); tri.keep_custom_normals = True   # glTF tangents need triangles
        # vertex colours off: viewers multiply COLOR_0 into base colour
        bpy.ops.export_scene.gltf(filepath=f"{out_dir}/{root_name}.glb", export_format='GLB', use_selection=True,
                                  export_apply=True, export_yup=True, export_tangents=True,
                                  export_vertex_color='NONE', export_materials='EXPORT')
        lod0.modifiers.remove(tri)
    for o in list(ec.objects):
        m_ = o.data; bpy.data.objects.remove(o, do_unlink=True)
        if m_.users == 0: bpy.data.meshes.remove(m_)
    solo(root_name)
    with open(f"{out_dir}/{root_name}.json", "w") as fh: json.dump(manifest, fh, indent=1, default=list)
    return manifest
