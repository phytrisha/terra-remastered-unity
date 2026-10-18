---
name: tripo-ai-models
description: Generate 3D models (props, characters, rigged and animated characters) from text or images with the Tripo CLI and bring them into the Blender -> Unity pipeline. Use when the user wants an AI-generated model, image-to-3D, multiview-to-3D, auto-rigging, Tripo, or a quick placeholder/blockout mesh, or when a hand-modelled asset would take much longer than its importance justifies.
---

# Tripo AI models

The `tripo` CLI (installed globally, `tripo --version`) turns a prompt or images into a textured model.
Full agent docs ship with it: `tripo docs` (skill map), `tripo docs --topic commands/generate|process|make`,
`tripo docs --topic examples/game-asset`. Read them instead of guessing parameters.

## Rules
- **Credits cost money.** Before a real run: dry-run (`DRY=1`, zero credits), tell the user model, steps and the
  cost notes, and get an OK for batches. `tripo balance` shows the balance. Every run is logged in `sources/tripo/LOG.tsv`.
- **Auth**: `tripo doctor`. No key → ask which site the account is on (`ov` international, `cn` China), then run
  `tripo login --region <ov|cn>` in the background and relay the URL + code it prints; never ask for a key in chat.
- **Branch on exit codes**: 3 auth, 4 no credits (user runs `tripo topup`), 5 content policy (rephrase), 6 task failed
  (refunded; on timeout use `tripo task watch <id>`, never redo), 7 network (check `tripo balance` before re-running),
  9 rate limit (back off).
- `tripo make` blocks until done (minutes). Don't add a timeout under 15 min; run long jobs in the background.
- Judge quality from `preview.png` before spending more credits on refine/rig; `tripo redo @last` re-rolls the seed.
- Don't pick legacy model versions; the CLI chooses `tripo-v3.1` (detailed) or `tripo-p1` (low poly, ≤ 20k faces).

## Pipeline
```bash
# 1. generate (writes sources/tripo/<Name>/..., logs credits)
tools/tripo_make.sh Barrel "a weathered oak barrel with iron hoops, game asset, neutral lighting" --for game-pc
tools/tripo_make.sh Lamp sources/reference/lamp.png --for game-mobile -p face_limit=3000
# 2. ingest into Blender: normalise scale/pivot/facing, weld seams, decimate to budget, rename, unpack textures
ASSET=Barrel SRC=sources/tripo/Barrel HEIGHT=0.9 TRIS=3000 tools/blender_run.sh blender/scripts/ingest_mesh.py
# 3. same checks and export as hand-made assets
ASSET=Barrel CATEGORY=Props BUDGET=3000 tools/blender_run.sh blender/scripts/stages.py blender/assets/Barrel/Barrel.blend
# 4. into Unity
tools/sync_to_unity.sh Props Barrel
```
- **Prompts**: describe one object, its material and condition, "game asset", "neutral lighting". Give real size in
  the spec, not the prompt: ingest scales to `HEIGHT`. Images: clean concept on a plain background, 2–4 views
  (`tripo make front.png back.png`) for better backsides.
- **Scenarios** (`--for`): `game-pc` (v3.1, detailed PBR), `game-mobile` (P1 low poly, face_limit 15k, 2K textures),
  `anim` (rig-check → rig → retarget idle+walk), `ar-web`, `film`. Budget: `-p face_limit=N`.
- **Textures**: the generated base colour often contains baked lighting and soft detail. For hero assets re-texture
  with project materials in Blender (assign library materials `MI_*` to zones, or bake), and keep the generated maps
  for background props. Rename the material with `MATERIAL=MI_<Library>` when one library material fits.
- **LODs**: Blender makes them on export. Alternative from one high-poly in Tripo: `--then decimate:5000`.
- **Facing**: ingest assumes the front is glTF +Z (Blender −Y). Check the front review render; fix with `YAW=180`.

## Characters
```bash
tools/tripo_make.sh Guard "a city guard in leather armour, T-pose, standing straight, arms out" --for anim
# or step by step, Unity Humanoid naming:
tripo anim check @last --json                     # riggable? which rig_type
tripo anim rig @last --spec mixamo --out-format fbx --json
tripo anim retarget @last --animation preset:idle --animation preset:walk --out-format fbx --json
```
- Ask for a T-pose (arms out, legs apart, nothing fused to the body) — fused hands/cloaks and fingers are the most
  common generation flaw and break skinning.
- Rigged FBX with `--spec mixamo` maps to Unity Humanoid automatically. Copy it as `SK_<Name>.fbx` into
  `blender/export/Characters/<Name>/` (ingest via Blender only if the mesh needs cleanup; keep the armature), then
  `tools/sync_to_unity.sh Characters <Name>`. AssetImportRules sets Humanoid, loops `Idle*/Walk*/Run*`.
- Check in Unity: facing (+Z), feet on the ground, no knee/elbow inversion in retargeted clips, hands not clipping hips.
- Animation presets: `preset:idle walk run jump climb dive slash shoot hurt fall turn` (body-specific variants for
  quadruped etc.).

## When not to use it
Modular kit pieces (walls, floors, trims) that must snap to a grid, assets that share trim sheets, and anything with
exact dimensions or mechanisms: model those with the kit. Generated meshes are best for organic props, statues,
characters, creatures and quick placeholders.
