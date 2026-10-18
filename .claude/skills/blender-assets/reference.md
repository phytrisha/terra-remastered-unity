# AAA Asset Reference

Numbers are community consensus (Polycount, Substance PBR Guide, engine docs) — no studio publishes an
official standard. Scale budgets to platform and camera distance. bpy names were checked against Blender 5.1.

## 1. Prompting / planning an asset

- Research systems for LLM-driven Blender modeling (L3GO, LL3M, SceneCraft, ShapeCraft) converge on one loop:
  **plan → split into parts → code → execute → render → critique the image → refine**. Single-pass whole-object
  code is low quality and compounds spatial errors.
- Keep a part list with dimensions and positions; build one part per call with named objects.
- Put every dimension, bevel width and segment count in variables at the top of the script.
- Use fixed review views (front, side, front-left ¾, top) every iteration.
- Query the scene numerically (dimensions, tris, materials) — never assume your code did what you intended.
- Modifiers change counts: the Blender Lab MCP demo missed a Solidify modifier that doubled the polycount.
  Always evaluate with `obj.evaluated_get(depsgraph)`.
- Be explicit about scope ("only objects in collection X") and confirm before destructive or scene-wide edits.

## 2. Modeling standards

**Units & transforms**
```python
s = bpy.context.scene
s.unit_settings.system = 'METRIC'; s.unit_settings.scale_length = 1.0
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
```
Unapplied scale breaks bevels, modifiers, texel density, physics and export.

**Topology**
- Author mostly quads; triangles are fine in the final game mesh. N-gons only on flat planar areas.
- Spend polygons on the silhouette; remove edges that add nothing to silhouette, shading or UVs.
- Characters: loops around eyes, mouth and joints, 2–3 loops at elbows and knees, no poles in deforming areas.
- Clean: merge by distance (0.0001 m), no interior faces, zero-area faces or loose verts.

**Hard-surface shading (mid-poly, no bake needed)**
```python
bev = obj.modifiers.new("Bevel", 'BEVEL')
bev.width = 0.003; bev.segments = 3; bev.limit_method = 'ANGLE'
bev.angle_limit = math.radians(30); bev.harden_normals = True
wn = obj.modifiers.new("WeightedNormal", 'WEIGHTED_NORMAL'); wn.keep_sharp = True; wn.mode = 'FACE_AREA'
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(30))   # Auto Smooth was removed in 4.1
```
Bevel width: 2–5 mm for props.

**Triangle budgets (current-gen PC/console)**

| Asset | Tris |
|---|---|
| Small / background prop | 100–2k |
| Medium prop (crate, chair) | 1k–8k |
| Hero prop / first-person weapon | 10k–50k |
| Third-person weapon | 2k–6.5k |
| Hero character (incl. hair, clothes) | 50k–100k+ (older/budget: 15k–50k) |
| Mobile / VR | divide by 4–10 |

**LODs**: each about 50% of the previous (100 / 50 / 25 / 12.5%). Decimate (COLLAPSE ratio), then fix
artifacts; keep the silhouette, UV seams and materials. Name them `SM_Name_LOD0`, `_LOD1`, ...

**Pivot**: floor props at bottom center, at world origin; doors at the hinge; weapons at the grip; modular
pieces at a grid-snapped corner, sized on a 0.5 / 1 / 2 / 4 m grid. Blender is Z up, −Y forward.
```python
bpy.context.scene.cursor.location = (0, 0, 0); bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
```

**Naming** (Unreal style): `SM_` static mesh, `SK_` skeletal, `M_` / `MI_` material,
`T_Name_BC / _N / _ORM / _R / _M / _E / _H` textures, `UCX_SM_Name_01` collision. No spaces; object and mesh data names match.

**High → low bake (Cycles)**
```python
scene.render.engine = 'CYCLES'
scene.render.bake.margin = 16
# select high-poly, then low-poly (active). In the low-poly material, add an Image Texture node with the
# target image (Non-Color for normal/roughness) and make it the active node.
bpy.ops.object.bake(type='NORMAL', use_selected_to_active=True, cage_extrusion=0.01,
                    margin=16, normal_space='TANGENT')
```
- Triangulate the low-poly before baking and export that triangulation, so tangent spaces match (MikkTSpace).
- Put hard edges only at UV seams, or the bake shows gradients.
- Extrusion around 1–2% of object size, or use a cage.
- Bake the base color from a procedural or mixed material with `type='DIFFUSE'` and `pass_filter={'COLOR'}`.
  Also bake `ROUGHNESS` and `NORMAL`.

## 3. UVs

**Texel density**

| Use | px/m |
|---|---|
| Mobile / VR | 256–512 |
| Third person | 512–1024 |
| First person / current-gen AAA standard | 1024–2048 |
| Hero / cinematic | 2048–4096 |

Formula: `TD = texture_px × sqrt(uv_area_fraction / surface_area_m²)`. Consistency between assets matters
more than the absolute value.

**Padding**

| Texture size | Padding |
|---|---|
| 512–1K | 4–8 px |
| 2K | 8–16 px |
| 4K | 16–32 px |

bpy `margin` / `island_margin` is a UV-space fraction (16 px / 2048 ≈ 0.008). Bake margin is in pixels.

**Seams**: on hard edges (≥ 90°, which need split normals anyway) and in hidden areas (underside, back,
inner corners). Cylinders get one seam down the back plus the caps.

**Methods**
```python
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.008)  # hard-surface/background
bpy.ops.mesh.mark_seam(); bpy.ops.uv.unwrap(method='ANGLE_BASED', margin=0.008)  # hero/organic
bpy.ops.uv.average_islands_scale(); bpy.ops.uv.pack_islands(margin=0.008, rotate=True)
```
- Straighten hard-surface islands.
- Check stretch with a checker: `bpy.ops.image.new(name="UVGrid", generated_type='UV_GRID')`.
- Mirrored or overlapping UVs are fine for symmetric parts (offset them by +1 U for baking), but never in a
  lightmap UV.
- For UE/Unity baked lighting, add a second, non-overlapping UV channel.

**Strategies**
- Atlases: several props share one texture set, which means fewer draw calls.
- Trim sheets: one tileable sheet of strips that large environment meshes map onto.
- UDIMs: film and hero characters only.
- Tiling materials plus decals: for environments.

## 4. PBR

**Metal/roughness** (Blender, glTF, UE, Unity)
- Base Color holds albedo for dielectrics and the F0 color for metals.
- Metallic is 0 or 1 for raw materials; grey only at transitions such as dirt over metal.
- Dielectric F0 is 4%: `Specular IOR Level` 0.5, or `IOR` 1.5.

**Spec/gloss** (legacy) → metal/rough conversion
- roughness = 1 − gloss.
- Specular maps hold the F0 color (dielectric ≈ sRGB 56).
- Raw metals have a black diffuse. Their metallic value comes from where specular is high or colored while
  diffuse is dark.

**Albedo ranges** (sRGB 0–255)
- Dielectrics: 30–240, with 50 as the floor for most materials. Charcoal ≈ 30–50, fresh snow ≈ 240.
- Metals: 170–255. Linear base colors:

| Metal | Linear base color |
|---|---|
| Iron | (0.56, 0.57, 0.58) |
| Aluminium | (0.91, 0.92, 0.92) |
| Gold | (1.0, 0.77, 0.34) |
| Copper | (0.95, 0.64, 0.54) |
| Silver | (0.95, 0.93, 0.88) |

- Base color must contain no lighting, AO, shadows or highlights.

**Typical roughness**

| Material | Roughness |
|---|---|
| Polished metal | 0.1–0.35 |
| Plastic | 0.3–0.6 |
| Wood | 0.5–0.8 |
| Painted metal | 0.4–0.7 |
| Concrete / stone | 0.7–0.95 |
| Fabric / rope | 0.8–1.0 |

Never use exactly 0 or 1; break it up with smudges and wear.

**Normal maps**
- Blender and glTF use OpenGL (Y+). Unreal, CryEngine and 3ds Max use DirectX (Y−).
- To convert, invert green only: G' = 1 − G.
- In Blender 5.1 the Normal Map node also has `convention = 'OPENGL' | 'DIRECTX'`. Use either that or
  pixel flipping, never both.
- Symptom of the wrong convention: bumps read as dents along one axis.

**Texture resolution**
- Pick from texel density × surface size, power-of-two only.
- Typical sizes: background prop 512–1K, medium prop 1K–2K, hero prop or character 2K–4K (characters are
  often split into several 4K sets). Tiling environment materials: 1K–2K covering 1–4 m per repeat.
- Use 16-bit for baked normals and height, 8-bit for final game normals.

**Wiring function** (verified socket names, Blender 5.1)
```python
import bpy, os

def build_pbr_material(name, tex_dir, prefix, tiling=1.0, metallic=None, use_orm=False,
                       normal_convention='OPENGL', use_height_bump=True,
                       normal_strength=0.5, bump_strength=0.08):
    # Normal map + height bump stack. Derived maps look like sandpaper above ~0.6 / 0.1 (crate test).
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree; nodes, links = nt.nodes, nt.links
    bsdf = nodes["Principled BSDF"]
    coord = nodes.new("ShaderNodeTexCoord"); mapping = nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (tiling, tiling, tiling)
    links.new(coord.outputs["UV"], mapping.inputs["Vector"])

    def tex(suffix, colorspace, y):
        path = os.path.join(tex_dir, f"{prefix}_{suffix}.png")
        if not os.path.exists(path):
            return None
        n = nodes.new("ShaderNodeTexImage"); n.location = (-600, y)
        n.image = bpy.data.images.load(path, check_existing=True)
        n.image.colorspace_settings.name = colorspace
        links.new(mapping.outputs["Vector"], n.inputs["Vector"])
        return n

    if (bc := tex("basecolor", "sRGB", 300)):
        links.new(bc.outputs["Color"], bsdf.inputs["Base Color"])
    if use_orm and (orm := tex("orm", "Non-Color", 0)):
        sep = nodes.new("ShaderNodeSeparateColor")
        links.new(orm.outputs["Color"], sep.inputs["Color"])
        links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
        links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    else:
        if (r := tex("roughness", "Non-Color", 0)):
            links.new(r.outputs["Color"], bsdf.inputs["Roughness"])
        if (m := tex("metallic", "Non-Color", -150)):
            links.new(m.outputs["Color"], bsdf.inputs["Metallic"])
    if metallic is not None and not bsdf.inputs["Metallic"].is_linked:
        bsdf.inputs["Metallic"].default_value = metallic
    normal_out = None
    if (n := tex("normal", "Non-Color", -300)):
        nm = nodes.new("ShaderNodeNormalMap"); nm.space = 'TANGENT'; nm.convention = normal_convention
        nm.inputs["Strength"].default_value = normal_strength
        links.new(n.outputs["Color"], nm.inputs["Color"]); normal_out = nm.outputs["Normal"]
    if use_height_bump and (h := tex("height", "Non-Color", -500)):
        bump = nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = bump_strength; bump.inputs["Distance"].default_value = 0.02
        links.new(h.outputs["Color"], bump.inputs["Height"])
        if normal_out:
            links.new(normal_out, bump.inputs["Normal"])
        normal_out = bump.outputs["Normal"]
    if normal_out:
        links.new(normal_out, bsdf.inputs["Normal"])
    return mat
```
- **Cycles true displacement**: Height → Displacement node (Midlevel 0.5, Scale 0.01–0.05 m) → Material
  Output `Displacement`, with `mat.displacement_method = 'BOTH'` and enough geometry or adaptive subdivision.
- **AO for glTF**: create a node group named "glTF Material Output" with an `Occlusion` input and feed it
  the AO (or the ORM red channel). Multiplying AO into base color (Mix Multiply, factor ≤ 0.5) is only for
  stylized work or renders inside Blender.
- **Alpha**: `mat.surface_render_method = 'DITHERED'` (or `'BLENDED'`).
- **Edge wear / grime masks** for unique assets: Geometry → Pointiness (Cycles) or a baked AO/curvature map
  → ColorRamp → mix factor between two tileable materials, broken up with Noise Texture.

**Deriving maps from one AI image** (what `scripts/derive_pbr_maps.py` does)
- De-light: divide out heavily blurred luminance, then clamp to sRGB 30–240.
- Height: high-passed luminance. "Dark = low" is only a heuristic; invert when crevices are lighter.
- Normal: Sobel of height with wrap-around, OpenGL green-up.
- Roughness: base value ± a cavity-driven variation.
- AO: cavity estimate.
- Metallic: never derived from brightness; set it per material.
- All derived maps are approximations. When in doubt, use sensible per-material constants for roughness
  and metallic.

## 5. Texture prompts

- Lead with the tiling and view words: "seamless tileable", "top-down", "orthographic", "flat-lay".
- Then the material and its specifics, plus a physical scale cue.
- Then the lighting words: "flat even diffuse overcast lighting, no shadows, no highlights, no reflections,
  uniform albedo, PBR base color map".
- Avoid: scene or object words, lighting moods, macro or depth-of-field shots, large unique features, text,
  borders, and extreme saturation or brightness.
- The AI Hub models (GPT Image, FLUX, Gemini) have no native tiling switch. Fix seams afterwards: the
  script's `--make-tileable` cross-fades with a half-offset copy.
- Validate: tile the result 2×2 and check for seams and visible repetition. The histogram should sit around
  mid-grey with no clipping.
- Generate at 1K minimum and 2K for hero work. For GPT models, `--quality high` or above.

## 6. Validation, look-dev, export

**Validation script** (run via `execute_blender_code`)
```python
import bpy, bmesh, os
dg = bpy.context.evaluated_depsgraph_get()
for o in [o for o in bpy.context.scene.objects if o.type == 'MESH']:
    eo = o.evaluated_get(dg); me = eo.to_mesh()
    tris = sum(len(p.vertices) - 2 for p in me.polygons); eo.to_mesh_clear()
    bm = bmesh.new(); bm.from_mesh(o.data)
    report = dict(
        dims=tuple(round(d, 3) for d in o.dimensions), tris_evaluated=tris,
        scale_applied=all(abs(s - 1) < 1e-4 for s in o.scale),
        rot_applied=all(abs(r) < 1e-6 for r in o.rotation_euler),
        non_manifold=sum(not e.is_manifold for e in bm.edges),
        loose_verts=sum(not v.link_edges for v in bm.verts),
        ngons=sum(len(f.verts) > 4 for f in bm.faces),
        zero_area=sum(f.calc_area() < 1e-8 for f in bm.faces),
        uv_layers=len(o.data.uv_layers), materials=len(o.material_slots))
    bm.free(); print(o.name, report)
missing = [i.name for i in bpy.data.images if i.source == 'FILE' and not i.packed_file
           and not os.path.exists(bpy.path.abspath(i.filepath))]
print("missing image files:", missing)
```

**Fixes** (Edit mode)
- `bpy.ops.mesh.remove_doubles(threshold=0.0001)`
- `bpy.ops.mesh.normals_make_consistent(inside=False)`
- `bpy.ops.mesh.delete_loose()`
- `bpy.ops.mesh.select_non_manifold()`

Open edges are fine on game meshes with hidden backsides removed. Non-manifold geometry matters for booleans,
cages and 3D printing.

**Look-dev**
- Use a neutral studio or overcast HDRI (Poly Haven, CC0) and check under 2–3 HDRIs.
- View transform AgX, exposure 0. Use Standard only to inspect raw texture colors.
- Neutral grey ground, 50–85 mm camera for props.
- Debug views: wireframe-on-shaded, clay material override, checker texture, Face Orientation overlay
  (red = flipped).
- Use EEVEE for speed and Cycles for the final render.
```python
w = bpy.context.scene.world; w.use_nodes = True
env = w.node_tree.nodes.new("ShaderNodeTexEnvironment"); env.image = bpy.data.images.load(hdr_path)
w.node_tree.links.new(env.outputs["Color"], w.node_tree.nodes["Background"].inputs["Color"])
```

**glTF** (web, Godot, general)
```python
bpy.ops.export_scene.gltf(filepath=out + ".glb", export_format='GLB', use_selection=True,
    export_apply=True, export_yup=True, export_texcoords=True, export_normals=True,
    export_tangents=True, export_materials='EXPORT', export_image_format='AUTO')
```
- Only Principled BSDF with image textures exports. Bake procedural materials first.
- ORM is recognised (G = roughness, B = metallic). AO goes through the "glTF Material Output" group.

**FBX** (Unreal / Unity)
```python
bpy.ops.export_scene.fbx(filepath=out + ".fbx", use_selection=True, object_types={'MESH', 'ARMATURE', 'EMPTY'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
    bake_anim=False, path_mode='COPY', embed_textures=False)
```
- Unreal needs DirectX normals: flip green, or tick "Flip Green Channel" on import.
- Export `UCX_` collision meshes and `_LODn` meshes alongside.
- Ship textures as PNG (8-bit) and 16-bit PNG or EXR for height.

## Sources
- blender.org/lab/mcp-server · github.com/ahujasid/blender-mcp
- L3GO arxiv.org/abs/2402.09052 · LL3M arxiv.org/abs/2508.08228 · SceneCraft arxiv.org/abs/2403.01248 · ShapeCraft arxiv.org/abs/2510.17603
- Adobe Substance PBR Guide (parts 1–2) · KhronosGroup glTF KHR_materials_specular
- Blender manual: glTF add-on (glTF Material Output / ORM) · bpy ShaderNodeNormalMap
- Polycount threads on PBR value ranges, texel density and polycounts · Cocos Creator PBR docs (albedo ranges)
- strayspark.studio (DirectX vs OpenGL normals; texture resolution guide)
