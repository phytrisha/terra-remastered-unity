# Project instructions

3D asset creation in Blender (hand-built with Python, or generated with Tripo) and scene building in Unity 6 / URP,
driven from Claude Code. Read PROJECT.md for what this project is, its budgets and art direction.

## Skills (load the one that matches the task)
- `blender-assets`: modelling, UVs, materials, AI textures, review renders, export.
- `tripo-ai-models`: AI-generated models, image-to-3D, auto-rigging, ingest into the pipeline.
- `asset-pipeline`: Blender → Unity, import rules, prefabs, placement, audits, captures, lighting, performance.
- `unity-project-structure`: where anything under UnityProject/Assets goes and how it is named.

## Layout
| Path | What |
|---|---|
| `PROJECT.md` | brief, target platform, budgets, art direction, style blocks for prompts |
| `blender/lib/` | kit / export / audit / review libs, prepended to every step script |
| `blender/assets/<Name>/` | `build.py` (spec + build stages) and `<Name>.blend` per asset |
| `blender/scripts/` | generic steps: `stages.py`, `ingest_mesh.py`, `lod_regen.py`, `uv_audit.py` |
| `blender/export/<Category>/<Name>/` | generated FBX/GLB/manifest (git-ignored; Unity holds the copies) |
| `sources/` | inputs: `tripo/<Name>/` generations + `LOG.tsv`, `reference/` concept images |
| `textures/<Set>/` | raw AI images, `gen.sh` with the prompts, derived maps |
| `renders/` | review renders and captures (git-ignored) |
| `tools/` | `blender_run.sh`, `unity_run.sh`, `sync_to_unity.sh`, `tripo_make.sh`, `sheet.py`, `env.sh` |
| `unity/` | C# eval scripts run in the open editor (never inside Assets), `args/`, `reports/` |
| `UnityProject/` | the Unity project |
| `docs/` | `LESSONS.md` (read before big jobs), `CHECKLISTS.md`, `SESSION_LOG.md`, `perf.md` |

## Working rules
- **Measure, don't assume.** Every step returns numbers (`RESULT_JSON`, audit lines). Check tri counts, bounds,
  audits and renders after each step; never report success from the code you wrote alone.
- **Look at it.** Review renders from fixed views (front/back/sides/top/¾/low) after every major change, and show
  the user a render at blockout and at the end. Unity: `unity/capture.cs` with a fixed shot list.
- **Small steps.** One part (or part group) per Blender run; one concern per Unity eval script. Keep all dimensions as
  variables at the top of a build script.
- **Re-runnable everything.** Build, export, import, placement and post-process steps must give the same result when
  run twice. Rebuild terrain from its read-only source copy. When a step depends on another (placement → probes →
  lighting/occlusion rebake → perf pass), re-run the chain after any upstream rebuild.
- **No hard-coded machine paths.** Shell: `tools/env.sh` (`.env`). C#: `ProjectPaths`, `ROOT`/`ARGS` in eval scripts.
- **Unity assets move only through Unity** (`AssetDatabase`), never `mv`/`rm`/Finder. No renders or scratch files in Assets.
- **Paid APIs** (AI Hub images, Tripo): dry-run or estimate first, say the cost before batches, log every generation
  (prompts in `textures/<Set>/gen.sh`, Tripo in `sources/tripo/LOG.tsv`). Never print or commit API keys.
- **Destructive or scene-wide edits** in the user's open Blender or Unity: confirm first, save first, scope to named
  collections / root objects.
- **Parallel sessions:** each uses headless Blender on its own .blend, never the shared live session; don't touch
  another session's files, Unity scene or git state; note what others are doing in the session log.
- **Long or unattended work:** checkpoint before (git commit or copy of scene + lighting folder + terrain data), keep
  `docs/SESSION_LOG.md` (decisions made on the user's behalf, with reasons), and end with the summary format in
  docs/CHECKLISTS.md.
- **Commit** after each finished feature or asset (when the user wants commits); push only when asked.
