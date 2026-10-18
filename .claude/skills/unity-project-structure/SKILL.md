---
name: unity-project-structure
description: Folder structure, naming and placement rules for UnityProject/Assets (first-party content directly in Assets/). Use whenever you add, import, move, rename or delete anything under UnityProject/Assets, bring an export into Unity, or write an editor/eval/pipeline script that creates or loads assets, so every addition lands in the same layout and no script hard-codes folders.
---

# Unity project structure

All first-party content lives directly in `Assets/` in five top-level folders (`Art`, `Shaders`, `Scripts`, `Scenes`,
`Settings`). Group assets by **what they are**, never by the phase or milestone that produced them. The rules are encoded
in `Assets/Scripts/Editor/ProjectPaths.cs`, the single source of truth: change it in the same commit as this file.

## Layout
```
Assets/
  Art/
    Environment/
      Buildings/<Name>/   SM_<Name>.fbx, SM_<Name>_LOD1/2.fbx, PF_<Name>.prefab, anything unique to it
      Props/<Name>/       same pattern
      Nature/<Name>/      plants, rocks, terrain detail meshes
      Terrain/            TerrainData; Layers/ (TL_*), Textures/, Source/ (read-only originals scripts rebuild from)
    Characters/<Name>/    SK_<Name>.fbx, PF_<Name>.prefab, AC_<Name>.controller, M_<Name>.mat, Textures/
    Materials/            shared library: Surfaces/, Foliage/, Cloth/, Emissive/
    Textures/             texture sets behind the library: Surfaces/, Foliage/, Utility/ (noise, cookies, ramps)
    VFX/                  Materials/, Textures/
    UI/<Screen>/          UXML + USS of one HUD / screen (fonts are third party: ThirdParty/Fonts/)
  Shaders/                *.shader / *.shadergraph with their *.hlsl includes next to them
  Scripts/
    Runtime/              MonoBehaviours; Runtime/Dev/ for benchmark/debug tools; Runtime/<System>/ per gameplay system
    Rendering/            URP renderer features and passes
    Editor/               ProjectPaths, AssetImportRules, AssetPipeline, other editor tools; Editor/Tests/ EditMode tests
  Scenes/                 <Scene>.unity (+ Unity's lighting folder next to it); Sandbox/ for experiments and previews
  Settings/               Rendering/ (URP assets), PostProcessing/ (volume profiles), Lighting/, Input/, UI/ (PanelSettings, theme), Data/ (ScriptableObjects)
  README.md
```
Don't add top-level folders. Third-party content goes to `Assets/ThirdParty/<Vendor>/` or `Packages/`; vendor packs
that insist on their own top-level folder stay where they are, and our assets stay out of them. Never edit vendor
assets: override at runtime or copy into our folders.

## Where does a new thing go?
| Adding... | Goes to |
|---|---|
| Building / prop / plant | `Art/Environment/{Buildings,Props,Nature}/<Name>/` (`tools/sync_to_unity.sh` does it) |
| Character | `Art/Characters/<Name>/` |
| Material used by more than one asset | `Art/Materials/<Family>/` (`ProjectPaths.MaterialFolder` picks it) |
| Material/texture unique to one asset | that asset's folder |
| Tileable texture set | `Art/Textures/<Family>/`; terrain textures in `Art/Environment/Terrain/Textures/` |
| Shader | `Shaders/` |
| Runtime / rendering / editor code | `Scripts/Runtime/`, `Scripts/Rendering/`, `Scripts/Editor/` |
| Data asset (ScriptableObject) | `Settings/Data/<Kind>/` |
| Screenshot, render, report, scratch | **Not in Assets**: `renders/`, `unity/reports/`, the scratchpad |
| Pipeline / eval script | **Not in Assets** (it would compile into the game): `unity/` |

New categories: add a folder at the right level (`Art/Environment/Vehicles/<Name>/`, `Art/Audio/{Music,SFX,Ambience}/`,
`Art/UI/`) and a constant (+ `Category` value if assets of that kind are synced) in `ProjectPaths`.

## Naming
| Prefix | Asset | Example |
|---|---|---|
| `SM_` | static mesh FBX; LODs `_LOD1`, `_LOD2` | `SM_Crate.fbx` |
| `SK_` | skinned mesh FBX | `SK_Guard.fbx` |
| `PF_` | prefab | `PF_Crate.prefab` |
| `MI_` | shared library material | `MI_WoodPlanks.mat` |
| `M_` | one-off / asset-specific material | `M_Crate.mat` |
| `T_` | texture `T_<Set>_<Map>`: `BC` base colour, `N` normal, `ORM`/`MS`, `AO`, `E` emissive, `H` height, `Mask` | `T_WoodPlanks_N.png` |
| `TL_` | terrain layer | `TL_Gravel.terrainlayer` |
| `AC_` / `AM_` | animator controller / avatar mask | `AC_Guard.controller` |
| `UCX_` | convex collision hull inside an FBX | `UCX_SM_Crate_00` |
Folder names are PascalCase singular asset names equal to the asset's base name. The Blender material name is the
Unity material name: renaming one means re-exporting.

## Moving, renaming, deleting
- Only through Unity (`AssetDatabase.MoveAsset/DeleteAsset` in an eval script, or the Project window). Never
  `mv`, `rm` or Finder inside `Assets/`: that breaks `.meta` GUIDs and every reference.
- Keep an old→new map TSV for bulk moves, commit moves as one change so git records renames.
- After a move: `AssetPipeline.SceneProblems()` must be empty; run `unity/unused_assets.cs` before and after: the
  "used" count must not drop. Assets only scripts load (Terrain/Source) are excluded from the unused report on purpose.
