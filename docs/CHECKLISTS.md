# Checklists

## Asset done
- [ ] Spec at the top of `build.py` (or in the ingest command): size, budget, materials, pivot
- [ ] Dimensions match the spec; transforms applied; pivot bottom centre; front −Y
- [ ] `audit`: 0 floating, 0 inverted, 0 unapplied transforms, `loose_end` reviewed; `clashes()` reviewed
- [ ] `validate`: 0 zero-area faces, 0 loose verts, 0 `zero_uv_faces`, UVMap present, no missing images
- [ ] Budget OK (evaluated tris, material count)
- [ ] Review renders looked at from all sides; screenshot shown to the user
- [ ] Textures: correct colour spaces, base colour without lighting (30–240 sRGB), metallic 0/1, normal convention OpenGL
- [ ] Exported: FBX LOD0–2 + UCX + GLB + manifest; `uv_audit.py` bad_pct reasonable (< ~10 % on large materials)
- [ ] In Unity: `import_assets.cs` says "materials mapped", prefab has LODs and colliders; LOD1/2 upright and same bounds
- [ ] Committed

## Scene change done
- [ ] `audit_scene.cs`: 0 FAIL (floating, missing refs, blocked walk routes); overlaps reviewed
- [ ] Re-ran dependent steps: probes → lighting bake → occlusion bake → perf post-pass (LOD/shadow rules)
- [ ] Captures from the fixed shot list, compared with the previous round (`tools/sheet.py`)
- [ ] Play Mode: 0 console errors, walked the main routes
- [ ] Perf: benchmark views in a player build, compared with docs/perf.md (no regression > 10 %)
- [ ] Committed (scene + lighting data + occlusion data together)

## Before an unattended / long run
- [ ] Git commit or full copy (without Library/Temp/Logs/UserSettings); verify file count / scene checksum
- [ ] Restore steps written at the top of `docs/SESSION_LOG.md` (which files belong together: scene + lighting folder + terrain data)
- [ ] Credits budget agreed for AI Hub / Tripo
- [ ] Parallel sessions: own .blend files, headless Blender, no shared Unity scene

## End-of-session summary (same layout every time)
1. **Built** — assets/features with numbers (tris, ms, counts)
2. **Reused** — what came from existing kit/assets
3. **Decisions made on your behalf** — decision | why
4. **Where to look first** — scene, shot names, renders
5. **Known flaws and limits** — honest list, including checks that were skipped
6. **How to revert** — commit hash / checkpoint path
