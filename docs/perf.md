# Performance record

Player-build numbers only (`-perfbench`, see the asset-pipeline skill). Same machine, same views, same resolution.
Before a perf pass, back up the values you are about to change (LOD thresholds, shadow settings) in this file.

| Date | Build / commit | Views | Mode | CPU frame ms | GPU ms | Notes |
|---|---|---|---|---|---|---|
| 2026-10-16 | after 360° scatter (3.4M tris, 8.5k renderers) | front,left,right,back,up | base | 7.44 (mean) | 5.41 (mean) | Apple M4, 1920x1080 windowed, Ultra, MSAA 4x, shadows 4096 x4 cascades 140 m; worst view front 9.75 ms CPU (103 fps), right 7.52 ms GPU |
| 2026-10-16 | + audio, motes, CC0 remap, ridges (final) | front,left,right,back,up | base | 7.82 (mean) | 6.57 (mean) | worst: front 10.39 ms CPU (96 fps), back 9.57 ms GPU; unity/reports/perfbench_2.txt |
| 2026-10-16 | + baked GI, SSAO, volumetric fog (full res), floor detail, tiger | same | base | 14.17 | 11.05 | worst front 17.40 ms frame (57 fps): over budget; unity/reports/perfbench_3.txt |
| 2026-10-16 | fog half res, SSAO 4 samples, floor static-batched (760) | same | base | 11.83 | 8.97 | worst front 14.82 ms frame (67 fps); fog 1.0 / SSAO 2.1 / floor 1.0 / shadows 3.0 ms CPU frame; perfbench_5.txt |
| 2026-10-17 | 92e0557 (NOT reproducible, see below) (LowPolyLit, soft shadows, HD fur, tapir/chital) | front,left,right,back,up + white room, eye close-up | base | 3.93 (wall 4.0–5.0 forest, 3.2 room / close-up) | 12.54* | worst: back 4.49 ms frame (223 fps); noshadow -0.4 ms, nossao -0.5, fog -0 ; unity/reports/perfbench_6.txt; screenshots renders/perf/ |
| 2026-10-17 | session 8 final (HD tapir/chital + portals, locomotion, life, gusts) | front,left,right,back,up | base | 11.52 (wall) | 21.17* | worst front 14.30 ms (70 fps); noshadow -2.7 ms; Wildlife+Life off: no gain (11.74); white rooms tiger 3.1 / tapir 3.0 ms, close-ups 4.5 / 4.0 ms; perfbench_7 / _9 |
| 2026-10-18 | night shift 9 final (fur, overlap pass: 421 pieces removed, epiphytes, edges, streaks, cloud shadows) | front,left,right,back,up | base | 8.72 (wall) | 24.22* | worst front 13.02 ms (77 fps); white room tiger 5.5 / close-up 7.8 ms, tapir room 3.9 / close-up 8.1 ms (shell fur +4 ms in close-ups); perfbench_10 |
| 2026-10-16 | run 1 | same | noshadow | 5.12 | 2.92 | shadows cost ~2.3 ms CPU / ~2.5 ms GPU |

\* Metal's `gpuFrameTime` (newest frame that has one) reads 14–17 ms while frames arrive every 4–5 ms: on this
machine it spans overlapping frames, so it is not a per-frame cost. Use `wall` (median unscaled delta time) and
`cpu_frame` as the frame time; compare GPU only between modes of one run. Earlier rows read the GPU time of the newest
frame only (often 0 on Metal).
The 4-5 ms rows of the session-8 baseline (perfbench_6) could not be reproduced: the final build, with everything
added in that session switched off (off:Wildlife+off:Life), measures 11.7 ms, the same as perfbench_5 (11.8). The
baseline run was probably taken while the player window was not composited normally; trust perfbench_5 / _7 / _9.

## What costs the frame (switch-off measurements)
| Part | ms | How measured |
|---|---|---|
| Main-light shadows | 2.3 CPU / 2.5 GPU (mean) | perfbench base vs noshadow, run 1 (unity/reports/perfbench_1.txt) |
| Volumetric fog | 3.1 full res -> 1.0 half res (frame ms, mean) | base vs feature:VolumetricFogFeature, perfbench_3 / _5 |
| SSAO (downsampled) | 2.6 at 8 samples -> 2.1 at 4 | base vs nossao, perfbench_3 / _5 |
| Floor detail (2.3k renderers) | 1.8 -> 1.0 with static batching | base vs hide:Floor, perfbench_3 / _5 |

## Settings in use
| Setting | Value | Why |
|---|---|---|
| URP shadow distance / cascades / res | 140 m / 4 / 4096, soft | crisp shadows on the faceted foreground; well inside budget |
| URP shadow normal bias / depth bias | 0 / 1.5 | normal bias opened light cracks between flat-shaded faces |
| Static batching | off for everything | broke the big terrain mesh in Play Mode; plants move (shader wind) |
