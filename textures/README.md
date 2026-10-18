# Texture sets

One folder per set (`textures/<Set>/`), e.g. a project's surface library or one asset's unique maps:
- `gen.sh` — every AI Hub prompt used for the set, as a re-runnable script (one `g name "prompt" [ref]` line per image).
  Run generations one at a time (the hub returns HTTP 429 on bursts) and skip files that already exist.
- `<material>_raw.png` — generator output, never edited.
- `T_<Material>_{basecolor,normal,roughness,ao,height,orm}.png` — derived with `derive_pbr_maps.py`.
  Rename/pack to the Unity set (`T_<Material>_BC`, `_N`, `_ORM` or `_MS`, `_AO`, `_H`) when copying into
  `UnityProject/Assets/Art/Textures/<Family>/`.

Example `gen.sh`:
```bash
#!/bin/bash
cd "$(dirname "$0")"
GEN="python3 ../../.claude/skills/blender-assets/scripts/aihub_image.py --model gpt-image-2.5-flare-2026-09-08 --quality high"
STY="top-down orthographic view, perfectly flat surface filling the whole frame, flat even diffuse lighting, no shadows, no highlights, no perspective, no vignette, uniform albedo, PBR base color map"
g() { [ -f "$1_raw.png" ] && return; $GEN --prompt "$2, $STY" --out "$1_raw.png" ${3:+--ref "$3"} >> gen.log 2>&1 || echo "FAIL $1" >> gen.log; }
g wood_planks "Seamless tileable weathered pine plank texture, planks 15 cm wide running vertically, about 6 planks across the image"
g steel_painted "Seamless tileable painted steel texture, matte dark green paint with small chips showing grey metal, one continuous surface"
```
