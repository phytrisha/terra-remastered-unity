using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

/// Animals besides the tiger (blender/lib/quadruped.py, faceted twins of AI-generated HD models): each gets an Animator
/// with its Idle / Walk / Alert takes and AnimalWander on its own stretch of the jungle path behind the viewer. With an
/// HD model (SK_<Name>HD on the same rig) it also gets the portal encounter (TigerEncounter, Dive entry) sharing the
/// tiger's white room and HUD; without one, AnimalHeadLook only.
public static partial class RainforestBuilder
{
    // name, route (path or stream bank) + stretch (z from-to), scale, walk speed on the ground (m/s), Walk clip no-slide
    // speed (export manifest), colour per material. Both live on the path BEHIND the viewer (z < 0, the "back" view, 3.5-10.5 m),
    // so turning around finds them; the tiger keeps the front. Routes must stay visible from the viewer: beyond z ~30
    // the path drops behind the crest toward the lake (the chital was invisible there)
    static readonly (string name, string route, Vector2 z, float scale, float walk, float noSlide, (string mat, Color col)[] mats)[] Animals =
    {
        ("Tapir", "path", new Vector2(-10.5f, -4.5f), 1.0f, 0.39f, 0.404f, new[] { ("M_TapirBlack", new Color(0.13f, 0.13f, 0.14f)), ("M_TapirWhite", new Color(0.86f, 0.85f, 0.82f)) }),
        ("Chital", "path+", new Vector2(-8.5f, -3.5f), 1.0f, 0.47f, 0.484f, new[] { ("M_ChitalCoat", new Color(0.68f, 0.40f, 0.20f)), ("M_ChitalWhite", new Color(0.92f, 0.89f, 0.84f)), ("M_ChitalDark", new Color(0.22f, 0.16f, 0.12f)) }),
    };

    const float BankOffset = 2.3f;                            // stream route: this far from the centreline, on the path side

    static float StreamBankXAt(float z)
    {
        for (int i = 1; i < StreamPts.Length; i++)
        {
            float za = StreamPts[i - 1].y, zb = StreamPts[i].y;
            if ((z - za) * (z - zb) <= 0 && za != zb) return Mathf.Lerp(StreamPts[i - 1].x, StreamPts[i].x, (z - za) / (zb - za)) - BankOffset;
        }
        return StreamPts[^1].x - BankOffset;
    }

    static AnimalWander AddWanderRoute(GameObject animal, Transform viewer, Vector2 zRange, System.Func<float, float> xAt, float lateral, List<string> log)
    {
        var route = new List<Vector3>();
        for (float z = zRange.x; z <= zRange.y + 1e-3f; z += 0.5f) { float x = xAt(z); OnTerrain(x, z, out var hit); route.Add(new Vector3(x, hit.point.y, z)); }
        var w = animal.GetComponent<AnimalWander>() ?? animal.AddComponent<AnimalWander>();
        w.route = route.ToArray(); w.viewer = viewer; w.lateral = lateral;
        log.Add($"{animal.name}: wander route {route.Count} pts (custom), z {zRange.x}-{zRange.y}");
        return w;
    }

    static void PlaceAnimals(Transform parent, Transform viewer, List<string> log)
    {
        foreach (var a in Animals)
        {
            var fbx = ProjectPaths.Find($"SK_{a.name}.fbx");
            var prefab = ProjectPaths.Find($"PF_{a.name}.prefab");
            if (fbx == null || prefab == null) { log.Add($"WARN animal {a.name}: SK_/PF_ missing (sync it first)"); continue; }
            foreach (var (mn, col) in a.mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(ProjectPaths.Find(mn + ".mat") ?? "");
                if (m) { LowPolyLitSetup(m, col, 0.05f); EditorUtility.SetDirty(m); }
            }
            // animator controller with the FBX takes
            var acPath = $"{System.IO.Path.GetDirectoryName(fbx)}/AC_{a.name}.controller".Replace('\\', '/');
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(acPath) ?? AnimatorController.CreateAnimatorControllerAtPath(acPath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToDictionary(c => c.name);
            var sm = ac.layers[0].stateMachine;
            foreach (var n in new[] { "Idle", "Walk", "Alert" })
            {
                if (!clips.TryGetValue(n, out var clip)) continue;
                var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == n) ?? sm.AddState(n);
                st.motion = clip; st.writeDefaultValues = true;
                if (n == "Idle") sm.defaultState = st;
            }
            EditorUtility.SetDirty(ac);
            // placement: on the path, facing along it
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), parent);
            go.name = $"PF_{a.name}";
            float z0 = Mathf.Lerp(a.z.x, a.z.y, a.route == "path+" ? 0.15f : 0.6f);
            float x0 = a.route == "stream" ? StreamBankXAt(z0) : PathXAt(z0) + (a.route == "path+" ? 0.8f : WanderShiftX);
            OnTerrain(x0, z0, out var hit);
            go.transform.SetPositionAndRotation(new Vector3(x0, hit.point.y, z0), Quaternion.Euler(0, 20f, 0));
            go.transform.localScale = Vector3.one * a.scale;
            var anim = go.GetComponentInChildren<Animator>() ?? go.AddComponent<Animator>();
            anim.runtimeAnimatorController = ac; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            { r.shadowCastingMode = ShadowCastingMode.On; r.lightProbeUsage = LightProbeUsage.BlendProbes; r.updateWhenOffscreen = false; }
            // "path+" = the right half of the path (shared stretch, AnimalWander keeps them apart)
            var w = a.route == "stream" ? AddWanderRoute(go, viewer, a.z, StreamBankXAt, 0.4f, log)
                  : a.route == "path+" ? AddWanderRoute(go, viewer, a.z, z => PathXAt(z) + 0.8f, 0.3f, log) : AddWander(go, viewer, a.z, log);
            w.walkSpeed = a.walk; w.walkNoSlideSpeed = a.noSlide; w.turnRadius = 0.7f;   // tight: hooves are locked by QuadrupedLocomotion; wide arcs left the band
            ClearRouteBand(go.transform.root, w.route, w.lateral + (a.name == "Tapir" ? 0.3f : 0.22f) + 0.35f, a.name, log);   // drift + half the body width + turning arcs
            AddLocomotion(go.transform, log, a.name == "Chital" ? 0.62f : 0.65f);   // planted hooves, body on the terrain (walk duty from build.py)
            if (ProjectPaths.Find($"SK_{a.name}HD.fbx") != null) { log.Add($"animal {a.name}: at {go.transform.position}, clips {string.Join(",", clips.Keys)} (encounter later)"); continue; }
            var look = go.AddComponent<AnimalHeadLook>(); look.wander = w;
            var bones = go.GetComponentsInChildren<Transform>();
            look.head = bones.FirstOrDefault(t => t.name == "head"); look.neck = bones.FirstOrDefault(t => t.name.StartsWith("neck"));
            log.Add($"animal {a.name}: at {go.transform.position}, clips {string.Join(",", clips.Keys)}");
        }
    }

    // species id, click box (local width, height, length), orbit / focus centre height, showcase distance (m)
    static readonly Dictionary<string, (string id, Vector3 box, float center, float dist)> AnimalEncounters = new()
    {
        ["Tapir"] = ("tapir", new Vector3(0.62f, 1.12f, 2.0f), 0.65f, 2.9f),
        ["Chital"] = ("chital", new Vector3(0.45f, 1.35f, 1.65f), 0.8f, 2.4f),
    };

    // shell fur per species (FurShell): hair length (m), strands per metre, thickness, comb, root AO, colour variation
    static readonly Dictionary<string, (float len, float density, float thick, float comb, float rootAO, float colVar, float under)> AnimalFur = new()
    {
        ["Tapir"] = (0.0035f, 300f, 0.35f, 1.2f, 0.45f, 0.06f, 0.6f),     // short, sparse bristles over near-bare skin
        ["Chital"] = (0.009f, 560f, 0.45f, 2.2f, 0.5f, 0.1f, 2.0f),      // short, dense, sleek deer coat
    };

    /// Portal encounters for the animals that have an HD twin. Needs the tiger's encounter (white room, HUD, studio
    /// probe, layers): run after BuildEncounter and PlaceAnimals.
    static void AddAnimalEncounters(Transform root, List<string> log)
    {
        var tiger = root.GetComponentsInChildren<TigerEncounter>(true).FirstOrDefault(e => e.speciesId == "tiger");
        if (!tiger) { log.Add("WARN no tiger encounter: the other animals get no portal"); return; }
        foreach (var (name, d) in AnimalEncounters)
        {
            var go = root.Find($"Wildlife/PF_{name}");
            if (!go) continue;
            var lo = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var hd = AttachHdTwin(lo, $"SK_{name}HD", log);
            if (!hd) { go.gameObject.AddComponent<AnimalHeadLook>().wander = go.GetComponent<AnimalWander>(); continue; }
            hd.probeAnchor = tiger.whiteRoom.transform.Find("StudioProbe"); hd.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            SetLayer(hd.gameObject, TigerHDLayer);
            var furR = AttachHdFur(lo, hd, $"SK_{name}HD_Fur", name, log);
            if (furR) { furR.probeAnchor = hd.probeAnchor; SetLayer(furR.gameObject, TigerHDLayer); }
            var box = go.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0, d.box.y * 0.5f, 0); box.size = d.box;
            var enc = go.gameObject.AddComponent<TigerEncounter>();
            enc.speciesId = d.id; enc.entry = TigerEncounter.Entry.Dive; enc.headBone = "head"; enc.neckBone = "neck";
            enc.centerHeight = d.center; enc.showcaseDist = d.dist;
            enc.cam = tiger.cam; enc.look = tiger.look; enc.forestRoot = tiger.forestRoot; enc.whiteRoom = tiger.whiteRoom;
            enc.whiteVolume = tiger.whiteVolume; enc.sun = tiger.sun; enc.hud = tiger.hud; enc.keepActive = tiger.keepActive;
            enc.whiteLayer = tiger.whiteLayer; enc.tigerLayer = tiger.tigerLayer; enc.hdLayer = TigerHDLayer; enc.fxLayer = tiger.fxLayer;
            enc.whiteFloorY = tiger.whiteFloorY; enc.portalCompositeShader = tiger.portalCompositeShader; enc.markerMaterial = tiger.markerMaterial;
            enc.clickCollider = box; enc.lowRenderer = lo; enc.hdRenderer = hd;
            enc.wander = go.GetComponent<AnimalWander>();
            enc.fx = BuildPortalFx(go, lo, hd, log);
            log.Add($"encounter {name}: HD twin {hd.sharedMesh.triangles.Length / 3} tris, {AnimalFacts.Get(d.id).points.Length} hotspots, dive entry");
        }
    }

    /// The HD file's fur mesh (decimated copy with fur data, quadruped.py q_furmesh) on the same bones, drawn as shells
    /// by TigerFur with a FurShell material per species; the HD skin underneath is darkened at the roots like the tiger's.
    static SkinnedMeshRenderer AttachHdFur(SkinnedMeshRenderer lo, SkinnedMeshRenderer hd, string furName, string animal, List<string> log)
    {
        var path = ProjectPaths.Find(hd.name + ".fbx");
        var src = path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name == furName);
        if (!src || !AnimalFur.TryGetValue(animal, out var f)) { log.Add($"{animal}: no fur mesh ({furName})"); return null; }
        var bones = lo.bones.ToDictionary(b => b.name);
        var go = new GameObject(furName, typeof(SkinnedMeshRenderer));
        go.transform.SetParent(hd.transform.parent, false);
        go.transform.localPosition = src.transform.localPosition; go.transform.localRotation = src.transform.localRotation; go.transform.localScale = src.transform.localScale;
        var r = go.GetComponent<SkinnedMeshRenderer>();
        r.sharedMesh = src.sharedMesh; r.bones = src.bones.Select(b => bones[b.name]).ToArray(); r.rootBone = bones[src.rootBone.name]; r.localBounds = src.localBounds;
        r.quality = SkinQuality.Bone4; r.updateWhenOffscreen = false; r.shadowCastingMode = ShadowCastingMode.Off; r.lightProbeUsage = LightProbeUsage.BlendProbes;
        var fur = VfxMat($"M_{animal}Fur", "TerraRemastered/FurShell");
        fur.SetTexture("_BaseMap", hd.sharedMaterial.GetTexture("_BaseMap"));
        fur.SetFloat("_FurLength", f.len); fur.SetFloat("_Density", f.density); fur.SetFloat("_Thickness", f.thick); fur.SetFloat("_Comb", f.comb);
        fur.SetFloat("_RootAO", f.rootAO); fur.SetFloat("_ColorVar", f.colVar); fur.SetFloat("_UnderDensity", f.under);
        fur.SetFloat("_Elongate", 3f); fur.SetFloat("_TipLighten", 0f); fur.SetFloat("_Wrap", 0.3f); fur.SetFloat("_Translucency", 0.15f);
        fur.SetFloat("_Clump", 0.35f); fur.SetFloat("_ClumpSize", 2.5f); fur.SetFloat("_BakedAO", 0.8f);
        EditorUtility.SetDirty(fur);
        var tf = go.AddComponent<TigerFur>(); tf.shell = fur;
        tf.lodLayers = animal == "Tapir" ? new[] { 12, 8, 6 } : new[] { 20, 14, 8 };   // short hair needs fewer shells
        tf.lodDistance = new[] { 1.8f, 4.2f };
        hd.sharedMaterial.SetColor("_BaseColor", new Color(0.88f, 0.88f, 0.88f));       // skin under the shells: roots shadowed
        log.Add($"{animal} fur: {r.sharedMesh.triangles.Length / 3} tris x {string.Join("/", tf.lodLayers)} shells, {f.len * 1000:F1} mm, node scale {go.transform.lossyScale.x:F2}");
        return r;
    }

    /// SK_<Name>HD's skinned mesh re-bound to the faceted twin's bones (exported from the same armature: same names,
    /// same rest pose), so one Animator drives both. Material: URP Lit with the generated 4K base colour + normal map.
    static SkinnedMeshRenderer AttachHdTwin(SkinnedMeshRenderer lo, string hdName, List<string> log)
    {
        var path = ProjectPaths.Find(hdName + ".fbx");
        if (path == null) return null;
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name == hdName);
        if (!src) { log.Add($"WARN {hdName}: no skinned mesh of that name"); return null; }
        var bones = lo.bones.ToDictionary(b => b.name);
        var missing = src.bones.Where(b => !bones.ContainsKey(b.name)).Select(b => b.name).ToArray();
        if (missing.Length > 0) { log.Add($"WARN {hdName}: bones not in the low-poly rig: {string.Join(",", missing)}"); return null; }
        var go = new GameObject(hdName, typeof(SkinnedMeshRenderer));
        go.transform.SetParent(lo.transform.parent, false);
        go.transform.localPosition = src.transform.localPosition; go.transform.localRotation = src.transform.localRotation; go.transform.localScale = src.transform.localScale;
        var r = go.GetComponent<SkinnedMeshRenderer>();
        r.sharedMesh = src.sharedMesh;
        r.bones = src.bones.Select(b => bones[b.name]).ToArray(); r.rootBone = bones[src.rootBone.name]; r.localBounds = src.localBounds;
        r.quality = SkinQuality.Bone4; r.updateWhenOffscreen = false;
        r.shadowCastingMode = ShadowCastingMode.Off;                 // TigerEncounter sets casting per camera
        r.lightProbeUsage = LightProbeUsage.BlendProbes;
        var tex = hdName.Substring(3);                                // SK_TapirHD -> T_TapirHD_*
        var bc = AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectPaths.Find($"T_{tex}_BC.png") ?? "");
        var nm = AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectPaths.Find($"T_{tex}_N.png") ?? "");
        // own material asset (the FBX's embedded one is read-only: edits to it would not reach a build)
        var matPath = ProjectPaths.Mat("M_" + tex);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_" + tex }; AssetDatabase.CreateAsset(mat, matPath); }
        r.sharedMaterials = new[] { mat };
        foreach (var m in r.sharedMaterials)
        {
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            if (bc) { m.SetTexture("_BaseMap", bc); m.SetColor("_BaseColor", Color.white); }
            if (nm) { m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", 0.22f); m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
        }
        log.Add($"{hdName}: {r.bones.Length} bones shared, maps {(bc ? "BC" : "-")}/{(nm ? "N" : "-")}");
        return r;
    }
}
