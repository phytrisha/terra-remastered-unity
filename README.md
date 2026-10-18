# Terra Remastered — low-poly rainforest vista (Unity 6.6 / URP)

A fixed-position first-person view of a low-poly tropical rainforest, built after
`sources/reference/terra_rainforest.png`. The viewer stands on a jungle path and can only look around.

**Run it**: open `UnityProject/` in Unity 6000.6, open `Assets/Scenes/Rainforest.unity`, press Play, click into the
Game view to capture the mouse (Esc releases; arrows/WASD/right stick also look). A macOS player build is made by
`tools/unity_run.sh unity/build_player.cs` (→ `UnityProject/Builds/`).

**Rebuild the scene** (fully generated, deterministic, ~5 s): `tools/rebuild_scene.sh` or Tools > Terra > Build
Rainforest Scene. Everything in the scene comes from `Assets/Scripts/Editor/RainforestBuilder.cs`.

| Piece | Source |
|---|---|
| Big leaves, banana, ferns, heliconia, grass, palms, broadleaf trees, rocks, vines | procedural Blender: `blender/lib/veg.py`, `blender/assets/Vegetation/build.py` |
| Buttress-root giant tree | Weave: Nano Banana Pro concept → Hunyuan 3D V3 LowPoly → `ingest_mesh.py` (`sources/weave/`) |
| Extra plants, palms, trees, rocks | CC0: Quaternius Ultimate Nature, Kenney Nature Kit (`Assets/ThirdParty/`) |
| Terrain, lake, stream + cascades, ridges, mountains | generated meshes in `RainforestBuilder` |
| Clouds | Weave Nano Banana Pro image → `CloudCard` shader cards |
| Water / foliage / light shafts / motes | `Assets/Shaders/LowPolyWater`, `LowPolyFoliage`, `LightShaft`, `Mote` |
| Ambience + stream audio | procedural, `sources/audio/gen_audio.py` |

Checks: `tools/rebuild_scene.sh` captures the fixed shot list (`unity/args/capture.txt` → `renders/unity/`),
`unity/play_test.sh` runs Play Mode and reports console errors, perf numbers are in `docs/perf.md`.

---

# Unity Build Template — Blender + Tripo → Unity

A starting point for projects that create 3D assets and build scenes in Unity 6 (URP), with Claude Code doing most
of the work through scripts. Assets are built in Blender with Python, or generated with Tripo. They are checked
numerically and visually, exported with LODs and collision, and imported into Unity by fixed rules. This setup came
out of a larger Blender → Unity project; its lessons are in `docs/LESSONS.md`.

```
 idea / spec (PROJECT.md)
   ├─ hand-built:  blender/assets/<Name>/build.py ─┐
   └─ generated:   tools/tripo_make.sh → ingest ───┤  tools/blender_run.sh  (headless or live Blender)
                                                   ▼
          audit (numbers) → review renders (fixed views) → export (FBX LOD0-2 + UCX + GLB + manifest)
                                                   ▼
          tools/sync_to_unity.sh → AssetImportRules + unity/import_assets.cs → PF_<Name> prefab
                                                   ▼
          placement → unity/audit_scene.cs → unity/capture.cs → lighting/occlusion bake → PerfBench in a build
```

## Requirements
| Tool | Version used | Notes |
|---|---|---|
| Blender | 5.1 | headless runs need nothing else; live runs need the Blender MCP add-on |
| Unity | 6000.6 (Unity 6.6), URP 17 | project in `UnityProject/`; `com.unity.pipeline` enables `unity command` |
| Unity CLI | `~/.unity/bin/unity` | drives the open editor (`eval_file`, `job wait`) |
| Tripo CLI | 0.7+ | `npm i -g tripo-cli`, then `tripo login --region ov` |
| Python 3 + uv | | texture scripts (`numpy`, `pillow`, `scipy` via `uv run`) |
| git + git-lfs | | LFS rules are in `.gitattributes` |
| HfG AI Hub key | | for AI textures; in `.claude/settings.local.json` (see the `.example`) |

## Start a new project
```bash
tools/new_project.sh ~/Dev/my-project "My Project"
cd ~/Dev/my-project
# 1. Open UnityProject/ in Unity 6.6, then Tools > Asset Pipeline > Setup Project (URP asset, linear, text serialization, ...)
# 2. Fill in PROJECT.md: brief, budgets, art direction, material library
# 3. Check the tools:
tripo doctor
ASSET=Crate tools/blender_run.sh blender/assets/Crate/build.py          # example asset: build, audit, review, export
tools/sync_to_unity.sh Props Crate                                       # into Unity (the editor must be open)
```
Machine paths (Blender, Unity CLI, Unity project) are in `.env` (copied from `.env.example`).

## Layout
| Path | What |
|---|---|
| `CLAUDE.md` | working rules for Claude in this project |
| `PROJECT.md` | the brief: target, budgets, art direction (fill in) |
| `.claude/skills/` | `blender-assets`, `tripo-ai-models`, `asset-pipeline`, `unity-project-structure` (+ scripts for AI textures) |
| `blender/lib/` | `kit.py` (modelling helpers), `export.py` (masks, LODs, UCX, FBX/GLB), `audit.py`, `review.py` |
| `blender/scripts/` | `stages.py` (audit/review/export any asset), `ingest_mesh.py` (Tripo/external meshes), `lod_regen.py`, `uv_audit.py` |
| `blender/assets/<Name>/` | one folder per asset: `build.py` + `.blend` (example: `Crate`) |
| `blender/export/` | generated exports (git-ignored) |
| `sources/` | Tripo generations (`tripo/<Name>/`, `LOG.tsv`) and reference images |
| `textures/` | AI texture sets: raw images, `gen.sh` with prompts, derived PBR maps |
| `tools/` | shell entry points (see below) |
| `unity/` | C# eval scripts for the open editor; `args/` inputs, `reports/` outputs |
| `UnityProject/` | the Unity project (`Assets/README.md` for its layout) |
| `docs/` | `LESSONS.md`, `CHECKLISTS.md`, `SESSION_LOG.md`, `perf.md` |
| `renders/` | review renders and Unity captures (git-ignored) |

## Tools
| Command | Does |
|---|---|
| `tools/blender_run.sh [--live] <step.py> [file.blend]` | runs a step with the libs prepended; prints `RESULT_JSON` |
| `tools/tripo_make.sh <Name> "<prompt or image>" [flags]` | dry-run, then generate into `sources/tripo/<Name>/`, log credits (`DRY=1` = plan only) |
| `tools/sync_to_unity.sh <Category> <Name> ...` | copy exports into `Assets/Art/...` and run the Unity import step |
| `tools/unity_run.sh unity/<script>.cs [args]` | run an eval script in the open editor (`COMPILE_ONLY=1` to check it compiles) |
| `tools/sheet.py out.png imgs...` | contact sheet for review rounds |
| `tools/new_project.sh <dir> [name]` | new project from this template |

## Unity side
- `Assets/Scripts/Editor/ProjectPaths.cs`: where everything lives (no hard-coded folders anywhere).
- `AssetImportRules.cs`: import settings for `SM_`/`SK_` models and `T_` textures, applied automatically.
- `AssetPipeline.cs`: prefab builder (LODGroup, UCX colliders, static flags), material creation and remap, scene problems.
- `ProjectSetup.cs`: one-time setup (URP asset, linear colour, Force Text, Run In Background, Frame Timing Stats).
- `Scripts/Runtime/Dev/PerfBench.cs`: benchmark in player builds (`-perfbench`, views under a `PerfViews` object).

## What was verified when the template was made
The Crate example was built, audited, rendered and exported with Blender 5.1. LOD regen, UV audit and mesh ingest
ran on its files. The Unity scripts compiled in Unity 6000.6.0f1. With an editor running, the full loop was exercised:
sync, import, materials and prefab, a scene audit that caught a planted floating crate, an overlap and a blocked
walk route, and a capture. Tripo was checked with dry runs only, so no credits were spent.
