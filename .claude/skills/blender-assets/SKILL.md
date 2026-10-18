---
name: blender-assets
description: Build game-ready 3D assets in Blender for this project — real-world scale, clean topology, proper UVs, vertex-colour masks, LODs, collision and PBR texture sets (AI-generated base colour via the HfG AI Hub, derived normal/roughness/AO/ORM). Use whenever the user asks to model, texture, material, review or export a prop, building, environment piece, plant or character in Blender, or asks for textures/PBR maps or sprites.
---

# Game assets in Blender

Two ways to run Blender code (both use the same step scripts, see `tools/blender_run.sh`):
- **Headless** `tools/blender_run.sh <step.py> <file.blend>`: a private Blender process. Default for building,
  auditing and exporting, and the only mode for parallel sessions.
- **Live** `tools/blender_run.sh --live <step.py>` or the `mcp__blender__*` tools: the Blender the user has open.
  Use it to show work in progress. Scope edits to named collections, never clear the whole scene, save first.

The step script contract: `blender/lib/{kit,export,audit,review}.py` are prepended, `ASSET` and `ROOT` are set, the
script assigns a JSON-serialisable `result`, which comes back as one `RESULT_JSON` line. Parameters go in env vars.
Numbers, tables and snippets are in [reference.md](reference.md); read the relevant section before each phase.

## 1. Spec first
Write a short spec at the top of the asset's build script (see `blender/assets/Crate/build.py`). Ask the user only
when something important is ambiguous; otherwise use PROJECT.md's budgets and conventions.

| Field | Example |
|---|---|
| Use & camera distance | Prop, third-person, seen from 2–30 m |
| Real dimensions (m) | 0.6 × 0.6 × 0.6; door 0.9 × 2.1; table height 0.75 |
| Budget | LOD0 2k tris, ≤ 2 materials; LOD1 50 %, LOD2 25 % (PROJECT.md table) |
| Parts list | Panels, battens, brackets, bolts — sizes and positions |
| Materials | `MI_WoodPlanks`, `MI_SteelPainted` (shared library names = Unity names) |
| Texel density | from PROJECT.md (default 512 px/m third person) |
| Pivot / facing | bottom centre at origin, front −Y |
| Deliverables | `SM_<Name>.fbx` + LODs + UCX, `.glb`, `.json` manifest, review renders |

## 2. Build loop (part by part, verify numerically and visually)
1. **Blockout**: one named object per part, real dimensions as variables at the top. Check proportions and silhouette.
2. **Refine one part group per run.** Kit helpers (`blender/lib/kit.py`): `box`, `cylinder_bm`, `sweep` (beams/pipes
   along a polyline), `prism` (extruded outline), `make_template`/`instance` (many repeated pieces), `chisel_bm`
   (rough blocks), `peg_bm` (bolts), `soft_normals`. Build in a local frame (long axis = local X), UV in metres there,
   then transform into place: objects keep identity transforms.
3. **After every major step**: `STAGES=audit` and `STAGES=review` (fixed views: front/back/left/right/top/¾/low) and
   look at the renders, including back and sides. Fix what the audit reports. Make before/after sheets with
   `tools/sheet.py` when iterating.
4. **Materials & textures** (§3). **Masks**: `export.apply_masks` writes vertex colour `Col`
   (R custom mask via `mask_fn`, G bevel edge, B per-part random, A baked AO) for stylised shaders and variation.
5. **Export**: `export_collection` → `blender/export/<Category>/<Name>/`, then `tools/sync_to_unity.sh` (see the
   asset-pipeline skill).

External meshes (Tripo, other generators, scans): `blender/scripts/ingest_mesh.py`, then the generic
`blender/scripts/stages.py` — see the tripo-ai-models skill.

## 3. Textures with the AI Hub image API
- Key: `AIHUB_API_KEY` from `.claude/settings.local.json` (`env`). If missing, ask the user to create one at
  https://ai-hub.hfg-gmuend.de/api-keys and add it there, then restart Claude Code. Never print the key, write it into
  a project file or pass it into Blender code.
- Credits come from a weekly quota. Say how many images and which model before a batch. Run generations one at a time
  (HTTP 429 at ~8 parallel). Log every prompt in `textures/<set>/gen.sh` so a set can be regenerated.

```bash
python3 .claude/skills/blender-assets/scripts/aihub_image.py --prompt "<prompt>" \
  --out textures/<Set>/<material>_raw.png --model gpt-image-2.5-flare-2026-09-08 --quality high [--ref style.png]
uv run .claude/skills/blender-assets/scripts/texture_tools.py seam textures/<Set>/<material>_raw.png
uv run .claude/skills/blender-assets/scripts/derive_pbr_maps.py textures/<Set>/<material>_raw.png \
  --out-dir textures/<Set> --name T_<Material> [--make-tileable only if seam says SEAM] --delight \
  --normal-strength 4 --roughness-base 0.65 --roughness-range 0.2 [--metallic-map 1.0] --orm
```
Models (check costs at `GET https://ai-hub.hfg-gmuend.de/api/tools/manifest`): `gpt-image-2.5-flare-2026-09-08`
(~8, default), `gpt-image-2.5-sunburst-2026-09-08` (~8, most detail), `flux-3-image` (~75), `gemini-nano-banana-2.1`
(~53), `gemini-3-pro-image` (~209, hero only, ask first). Use `--ref` with an existing project texture to keep a set
consistent.

**Prompt template** (tiling and view words first, a material, never a scene):
```
Seamless tileable <material> texture, <specifics + scale cue: "about 7 stones across the image">,
top-down orthographic view, perfectly flat surface filling the whole frame, flat even diffuse lighting,
no shadows, no highlights, no reflections, no perspective, no vignette, uniform albedo, PBR base color map
```
- One prompt per material, not per asset. Avoid big unique features, extreme colours, lighting moods.
- When the structure is modelled (planks, tiles, rivets), ask for "one continuous surface, no seams, no planks" or the
  texture duplicates it. Never put a tiled texture on modelled tiles.
- Stylised look: add the project's style block from PROJECT.md and skip `--delight` (it flattens painted values).
- **Sprites** (leaves, decals, grass cards): generate on a solid magenta background, then
  `texture_tools.py key raw.png T_<Name>_BC.png`.
- Derived normals are strong: start at Normal Map strength ~0.5 + bump ~0.08 in Blender; in Unity set per-material
  normal scales so the look matches (keep the values in a table file).
- Metallic is never derived from brightness: 0 or 1 per material.

Unique hero assets: tileable material per zone → masks (edge wear from the G channel / AO, grime in cavities) →
bake to the asset's own UVs (reference.md §2/§4).

## 4. Wiring maps in Blender
Base colour sRGB; everything else Non-Color. Normal via Normal Map node (OpenGL for Blender/Unity/glTF; DirectX only
for Unreal, flip once). ORM: Separate Color G→Roughness, B→Metallic. Height via Bump. Drive a set from one
Mapping node. Full function in reference.md §4.

## 5. Lessons that cost time before
- `bmesh.ops.bevel` replaces faces: assign UVs after bevelling over every face the part owns (kit does it). Spot-check
  untextured-looking parts: `validate()` reports `zero_uv_faces`.
- Never put a surface coplanar with or behind another (glass in a wall): offset 2–4 cm. `clashes()` finds overlaps.
- Instancing with a basis-vector matrix: keep it right-handed (det > 0) or instances turn inside out. `audit_asset`
  reports inverted closed shells by signed volume.
- Bevel only real corners of swept beams (> 35°): joins between segments turn into stripes otherwise.
- Supports and braces must end under what they carry: check end planes, not just heights (`member_keys` in audit).
- Many variants: one parametric generator with every clash rule expressed in its parameters, audited per variant.
- Vertex AO only on dense parts; large flat faces and overlapping shingles bake near-black (`ao_skip`).
- Unity calculates flat normals on meshes with per-corner vertex colours: author normals here (`soft_normals`) and
  import them.
- Don't lightmap thousands of overlapping small pieces: light them with probes + baked vertex AO.
- Emissive under AgX: near-black base colour, saturated emission, strength ~1; higher washes to white.
- Plants: wind from world height above the object origin; identical wind settings on bark and leaves; never static-batch
  plants (batching removes the origin the wind needs); export with `vegetation=True` (keeps custom normals).
- Placement: ground props at the lowest point of their footprint, tilt fences along the slope, check against
  colliders, paths and neighbours.
- Checking a measurement script: test it on a case with a known answer first (unit scale, tiny edges, cm vs m).

## 6. Done checklist
See docs/CHECKLISTS.md "Asset done". Minimum: spec met, audit clean, budget OK, review renders looked at (all sides),
textures present with correct colour spaces, export manifest written, renders shown to the user.
