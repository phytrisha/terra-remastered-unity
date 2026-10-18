---
name: asset-pipeline
description: The Blender -> Unity pipeline of this project — export, sync into Assets, import rules, prefabs with LODs and colliders, scene placement, audits, review captures, lighting/occlusion rebakes and performance measurement. Use whenever an asset goes from Blender (or Tripo) into Unity, when placing/arranging assets in a scene, when running or writing unity/*.cs eval scripts, when baking lighting, or when measuring/optimising frame time.
---

# Blender -> Unity pipeline

```
blender/assets/<Name>/build.py ──(tools/blender_run.sh)──> blender/export/<Category>/<Name>/SM_<Name>{,_LOD1,_LOD2}.fbx + .glb + .json
sources/tripo/<Name>/ ──ingest_mesh.py──> blender/assets/<Name>/<Name>.blend ──stages.py──> blender/export/...
blender/export/... ──tools/sync_to_unity.sh──> UnityProject/Assets/Art/Environment/<Category>/<Name>/ ──unity/import_assets.cs──> PF_<Name>.prefab
scene placement (your step script / eval) ──unity/audit_scene.cs──> FAIL/WARN list ──unity/capture.cs──> renders/unity/*.png
```

## Running Unity code
- `tools/unity_run.sh unity/<script>.cs [args...]` runs a C# eval script in the OPEN editor (needs the
  `com.unity.pipeline` package, already in the manifest). Always detached + `job wait`: plain `eval` has a ~5 s limit.
- Eval scripts: top-level statements, fully qualified `UnityEngine.`/`UnityEditor.` names, end with `return "<text>";`.
  Args arrive in `unity/args/<script>.txt`. They live in `unity/`, never in `Assets/` (they would compile into the game).
- Reusable logic belongs in `Assets/Scripts/Editor/AssetPipeline.cs` / `AssetImportRules.cs` / `ProjectPaths.cs`,
  so the menu, the CLI and other scripts share it. Eval scripts stay thin.
- Compile-check a script before running it on a real scene: `COMPILE_ONLY=1 tools/unity_run.sh unity/x.cs`.
- Never hard-code folders: `ProjectPaths.Model/Prefab/Mat/Tex/AssetFolder`. Select assets by name or explicit list,
  never "everything in folder X".
- Turn on Player Settings > Run In Background: without it Play Mode freezes when the editor is not focused and
  CLI-driven play tests hang.

## Export -> import contract
| Blender side (export.py) | Unity side (AssetImportRules / AssetPipeline) |
|---|---|
| metres, `FBX_SCALE_NONE`, forward −Z, up Y, triangulated, tangents | `useFileScale`, scale 1, root rotation kept (never reset it in prefab code) |
| custom split normals (`soft_normals` 60° + weighted) | `importNormals = Import`, `importTangents = Import` |
| UV1 `UVLightmap` (non-overlapping) | `generateSecondaryUV = false` |
| material names = library names (`MI_*` shared, `M_<Asset>` unique) | remap by name onto `Art/Materials/**` (`RemapMaterials`, every LOD file); missing → URP Lit created by `EnsureMaterials` with the Blender colour (linear → `.gamma`, Unity reads FBX colours as sRGB) and `T_<Mat>_*` maps |
| `SM_X.fbx` + `UCX_SM_X_NN` hulls, `SM_X_LOD1/2.fbx` | `PF_X`: LODGroup, UCX → convex MeshCollider on LOD0, static flags, probe-lit |
| vertex colour `Col` (R mask, G edge, B random, A AO), linear | read by custom shaders; plain URP Lit ignores it |
| `SM_X.json` manifest (tris, materials, dims, anchors) | placement scripts read anchors (lights, doors, sockets); Blender (x, y, z) → Unity local (−x, z, −y) |

Pitfalls seen before:
- **Axis trap**: single-mesh FBX carry a (−90°, 0, 0) / ×100 root. Code that sets identity rotation on the model root
  lays LODs on their backs and stands props on end. Check by comparing LOD1/2 bounds with LOD0 (± 0.1 m).
- If you ever let Unity generate lightmap UVs on such files, set `secondaryUVMinObjectScale = 100`, or the UVs
  collapse (black lightmaps). Changing normal import settings regenerates them: rebake afterwards.
- Unity keys mesh sub-assets by name: keep object/mesh names stable across re-exports (lod_regen.py does).
- Copy updated files over the old ones (keeps `.meta` GUIDs). Moves/renames/deletes only through Unity
  (`AssetDatabase.MoveAsset/DeleteAsset`), never with `mv`/`rm`/Finder.

## Prefabs and placement
- LOD thresholds per category live in `AssetPipeline.LodThresholds` (buildings .3/.12/.025, props .25/.08/.02,
  nature .14/.04/.012 with cross-fade). Renderers under 1.5 m cast no shadows.
- Lighting choice per asset: probe-lit by default (LOD changes need no rebake); `lightmapped` in the import args for
  walk-in or hero pieces (LOD0 only). Never lightmap thousands of overlapping small parts.
- **Placeholder pattern**: the root holds logic, colliders and anchors; only a child `Visual` is swapped when the art
  arrives.
- **Plan in Python, apply in Unity**: write placements to a text file (name, position, rotation, scale) from a
  planner with footprint checks, then apply it with an eval script. Dump the scene (`terrain heights + object bounds`)
  for the planner when editing an existing layout.
- Ground objects at the lowest footprint point, tilt runs (fences, rails) along the slope, keep small furniture off
  paths and doorways, reserve space for props before scattering rocks/plants. Fixed objects never move when resolving
  overlaps; small ones shift a little and every move is logged.
- Placement dependencies: after placing or moving static geometry → `audit_scene.cs` → walk test → probes →
  **rebake lighting AND occlusion culling** (stale occlusion data culled visible terrain as a white void).
- Interiors: enough UCX hulls that doors and aisles stay open; ramps for stairs; ray-test from inside that no view
  sees the void.

## Terrain (if the project has one)
- Keep a read-only copy of the TerrainData in `Art/Environment/Terrain/Source/`; every terrain script rebuilds from it
  and writes only changed cells, so passes never stack. Log every pad/path edit (level, cells, area).
- Pads: level at the footprint average, 1.2 m margin, 2.2 m front apron, 5.5 m smoothstep falloff; protected zones are
  never edited; one owning pad per cell. Cut deeper than ~1 m → retaining wall or move the building.
- Paths only lower the terrain. Re-ground objects in edited areas afterwards.
- URP Terrain Lit draws 4 layers per pass: every extra 4 layers cost a full pass. Keep ≤ 8 layers per tile.
- Create TerrainData before setting splatmaps; import textures before creating TerrainLayers; append new layers so old
  weights stay. New TerrainData defaults to coverage detail mode (grass barely shows): match instance-count mode.
- Multiple tiles: never `Terrain.activeTerrain`; sample the tile under each point.
- Terrain detail meshes: identity root transform (`vegetation=True` export) and exactly one material.

## Review and audit loop
1. `unity/audit_scene.cs` (args: root names, `walk x z x z ...` routes) → 0 FAIL.
2. `unity/capture.cs` with a fixed shot list per area (args file under version control in `unity/args/`), then
   `tools/sheet.py` for before/after sheets. Same shots every round.
3. Play Mode with 0 console errors. Particles only animate in Play Mode.
4. Score each round when doing art direction (style, palette, scale, density, silhouette, lighting, composition 1–5)
   and write what changed and why in the session log.

## Performance
- **Measure in a player build**, not the editor: `PerfBench.cs` (`-perfbench`, views = children of `PerfViews`).
  Turn on Player Settings > Frame Timing Stats. Modes interleave per view; compare within one run only.
- Find costs by switching parts off (`noshadow`, `nossao`, `noterrain`, `nodetails`, `hide:<group>`, `feature:<Type>`).
- Changes that helped before: shadow distance 70 m with 2–3 cascades; no shadows on small renderers; LOD2 shadow
  proxies for big buildings; STP upscaling at render scale 0.75 (enable STP in the URP asset at build time — it is
  stripped otherwise); camera opaque texture off when unused; LOD bias 1–1.5; baked occlusion culling; far LODs to
  fixed budgets (`lod_regen.py`); grass/detail instance count (biggest single lever).
- Didn't help: depth priming, 1024 shadow maps, GPU occlusion culling, Unity Mesh LOD on already-LODed FBX.
  Don't toggle GPU Resident Drawer between measurements (forces a rebuild).
- Write the post-pass frame times to docs/perf.md; new content is checked against them. Back up values before a pass.
- Shared URP asset settings are project-wide: note every change in the session log.
- Lightmaps: max 2048 per atlas (a 4096 EXR was 152 MB, over GitHub's file limit); lower the lightmap scale on big
  terrain tiles (1 → 0.25 halved bake time).

## Lighting / post defaults that worked (URP)
Forward+, mixed sun with Shadowmask, baked point lights, light probes around (not inside) objects, box-projected
reflection probes; HDR grading + Neutral or ACES tonemapping; SSAO from DepthNormals, intensity ~1, radius ~0.2,
downsampled (depth-only SSAO made crawling blotches on skin). Prefer more light over grading filters for mood.
