# Terra Remastered

> Claude reads this before every asset and uses it as the default for anything a request leaves open.

## Brief
- **What**: A single low-poly tropical rainforest vista (see `sources/reference/terra_rainforest.png`). The viewer stands
  on a dirt jungle path and can only look around (mouse look, fixed position). Must look great: the framing toward the
  lake and distant peak, big faceted foreground leaves, the stream with small cascades over boulders on the right,
  buttress-root trees with hanging vines and sun shafts on the left.
- **Target**: macOS (Apple Silicon), 1080p+, 60 fps in the editor/player.
- **Camera**: first person, fixed at eye height 1.65 m; foreground leaves at 0.5–3 m, lake ~60–150 m, hills 200–600 m,
  mountains 1–3 km.
- **Scale of the world**: ~300 m modelled detail around the viewer + low-poly backdrop hills/mountains out to 3 km.

## Art direction
- **Style**: low-poly, flat-shaded facets, flat colours per material (no textures needed), soft warm sun, light haze.
- **Palette & mood**: saturated leaf greens (light lime to deep green), warm red-brown dirt, brown-orange boulders,
  turquoise water, pale blue sky, white cumulus, red heliconia accents; late-morning sun from upper-left, god rays.
- **References**: `sources/reference/terra_rainforest.png`
- **Texture prompt style block**: none (flat colours, no textures).
- **AI 3D prompt suffix** (Nano Banana concept → Hunyuan 3D LowPoly, or Tripo P1): ", low-poly, flat-shaded faceted
  polygons, game-ready stylized, isolated, plain white background, soft even studio lighting" + the reference image.

## Budgets (defaults; per-asset specs may override)
| Class | LOD0 tris | Materials | Texel density | LODs |
|---|---|---|---|---|
| Small prop (< 0.5 m) | 300–1.5k | 1 | 512 px/m | LOD1 50 %, cull |
| Medium prop (crate, chair) | 1k–5k | 1–2 | 512 px/m | 50 % / 25 % |
| Large prop / vehicle | 5k–25k | ≤ 4 | 512 px/m | 50 % / 15 % |
| Building (exterior) | 25k–100k | ≤ 6 | 256–512 px/m | 25–30 % / 2–4 % (≤ 3.5k) |
| Landmark / walk-in | ≤ 250k | ≤ 8 | 512 px/m | 25 % / 2 % (≤ 12k) |
| Character | 15k–50k | 1–3 | 1024 px/m | 50 % / 20 % |
| Tree / plant | 2k–15k | 2 (bark, leaves) | — | 50 % + cross-fade |
Mobile/VR: divide by 4–10. Far LODs: ≤ 2–3k tris, 1 material, used beyond ~120 m.

## Frame budget (player build, the benchmark views in the main scene)
- Target: 16.6 ms GPU at 1080p on an Apple M-series laptop
- Current: see docs/perf.md

## Conventions (change only with a reason, and update the skills)
- Metres, Z up in Blender (−Y front), bottom-centre pivot; Unity Y up, +Z front.
- Names: `SM_` static, `SK_` skinned, `PF_` prefab, `MI_` shared material, `M_` unique material, `T_<Set>_<Map>`.
- Material library (Blender name = Unity name):
  | Material | Use | Notes |
  |---|---|---|
  | `MI_Leaf`, `MI_LeafLight/Dark/Yellow` | big leaves, ferns, palms (per-instance swap) | LowPolyFoliage shader |
  | `MI_LeafStem`, `MI_Grass`, `MI_Canopy`, `MI_Vine` | stems, grass tufts, tree crowns, vines | LowPolyFoliage |
  | `MI_Heliconia`, `MI_HeliconiaTip` | red bracts / yellow tips | LowPolyFoliage |
  | `MI_Bark`, `MI_PalmTrunk` | trunks (same wind as the leaves) | LowPolyFoliage, no flutter |
  | `MI_Rock` | all rocks incl. CC0 packs (forced at placement) | URP Lit |
  | `MI_Dirt`, `MI_Ground(Dark)`, `MI_HillForest`, `MI_Shore`, `MI_StreamBank`, `MI_LakeBed` | generated terrain submeshes | URP Lit |
  | `MI_Ridge`, `MI_Mountain(Far)` | backdrop ridges / mountains | URP Lit + fog |
  | `M_Lake`, `M_Stream`, `M_StreamCascade`, `M_StreamFroth` | water | LowPolyWater |

## Open decisions
- Tripo has 0 credits; AI meshes came from Weave (Hunyuan 3D V3 LowPoly). Top up Tripo if more AI assets are wanted.
- HD animals (tapir, chital) came from Weave: Nano Banana Pro photo side view -> Hunyuan 3D V3 (Normal + PBR, 60k tris
  after ingest), 226 credits for both (sources/weave/LOG.tsv). Their forest versions are decimated from the HD meshes.
- Audio is procedural placeholder (sources/audio/gen_audio.py); replace with recorded ambience if available.
