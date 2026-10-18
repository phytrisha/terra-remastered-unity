using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// The portal encounter on an animal's root (the white tiger, and the tapir / chital behind the viewer; one HUD and one
/// white room shared by all of them, data per species in AnimalFacts).
///   Forest   : wanders along the path (AnimalWander: idle, walk, looks at the viewer now and then); click it while
///              aiming at it (reticle) to start. One encounter (the first) owns the reticle raycast for all of them
///   Entry    : Pounce (predator: Charge + Pounce below) or Dive (prey: Alert, then the portal opens around the alerted
///              animal and the camera glides through it into the showcase framing; the white room is moved under the
///              animal so its floor meets the paws)
///   Charge   : runs at the viewer (Run clip in place, position from code)
///   Pounce   : leaps at the viewer in one continuous flight. A second camera renders the abstract white room from the
///              viewer's exact pose; the HUD shows it through a circular portal centred on the tiger that grows until it
///              fills the screen, then the main camera switches to the white room (identical image: no cut, no flash).
///              The forest camera draws the low-poly tiger, the portal camera its HD twin (same bones, same pose), so the
///              tiger turns HD exactly where the portal rim passes over it
///   Arrive   : touches down in the white room (Land clip, crossfaded from the end of Pounce) and settles into Idle
///   Showcase : orbit camera around the tiger, hotspot markers + tags; click one to fly the camera there (Focus) and open
///              the info panel; Esc / right click / BACK returns, RETURN TO JUNGLE reloads the scene
/// Scene objects are wired by RainforestBuilder.Encounter (editor) so the scene can be regenerated at any time.
public class TigerEncounter : MonoBehaviour
{
    enum State { Forest, Charge, Pounce, Alert, Dive, Arrive, Showcase, Focus }
    public enum Entry { Pounce, Dive }

    [Header("Species")]
    public string speciesId = "tiger";                         // AnimalFacts.Get
    public Entry entry = Entry.Pounce;
    public string headBone = "head", neckBone = "neck2";
    public float centerHeight = 0.6f;                          // orbit / focus centre above the root (m at scale 1)
    public float alertTime = 0.8f, diveTime = 1.9f, diveReveal = 1.0f;   // Dive: alert hold, camera glide, portal growth (s)

    [Header("Scene")]
    public Camera cam;
    public FixedLookCamera look;
    public Transform forestRoot;                 // children are switched off in the white room (except keepActive)
    public string[] keepActive = { "Wildlife", "Sun", "WhiteRoom", "Global Volume" };
    public GameObject whiteRoom;
    public Volume whiteVolume;
    public Light sun;
    public TigerHud hud;
    public int whiteLayer = 8, tigerLayer = 9, hdLayer = 10, fxLayer = 11;
    public TigerPortalFx fx;                                   // transition effects (optional)
    public AnimalWander wander;                                // forest behaviour (optional; off once the charge starts)
    public SkinnedMeshRenderer lowRenderer, hdRenderer;       // hdRenderer shares lowRenderer's bones (optional)
    public float whiteFloorY = -0.11f;
    public Material markerMaterial;
    public Collider clickCollider;

    [Header("Motion")]
    public float runSpeed = 5.6f, pounceAt = 4.6f;          // run 7 -> 4.6 m (about one stride), leap 4.6 -> 2.4 m
    public float runNoSlideSpeed = 3.0f;                      // Run clip ground speed at 1x and scale 1 (export manifest)
    public float pounceClipSpeed = 1.25f;
    public float takeoffFrame = 12f, landContactFrame = 5f;   // Pounce leaves the ground / Land forepaws touch (30 fps)
    public float arcHeight = 0.75f, landDistance = 2.4f;
    public float showcaseDist = 3.0f, showcasePitch = 14f;  // final framing the leap camera glides into
    public float revealStart = 0.12f, revealTime = 0.72f;      // seconds after the pounce starts
    public Color whiteHorizon = new(0.965f, 0.97f, 0.975f);

    State state = State.Forest;
    AnimalFacts.Species species;
    static readonly List<TigerEncounter> all = new();
    static TigerEncounter active;                              // the encounter in progress (null while all are in the forest)
    TigerEncounter Leader => all.Count > 0 ? all[0] : this;
    TigerEncounter gazeTarget; float gazeTime;
    Animator anim;
    Transform head, neck;
    Vector3 headFwdLocal;
    float lookWeight, stateTime;
    bool swapped, landing, gliding, arcGlide;
    float glideStart, glideEnd; Vector3 camStartPos, startLook, finalCenter, finalPos;
    Vector3 pounceStart, camFloor, landPos;
    float takeoffT, contactT, pounceLen;
    bool fxRunning; float fxClock, portalR; Vector2 portalC;
    UnityEngine.Rendering.Universal.DepthOfField whiteDof;
    [Header("White room look")]
    public Color whiteSunColor = new(1f, 0.95f, 0.88f);
    public float whiteSunIntensity = 1.7f;
    public Color furRimColor = new(0.6f, 0.6f, 0.6f);        // camera backlight on the fur silhouette (white room)
    static readonly int FurRimDirId = Shader.PropertyToID("_FurRimDir"), FurRimColorId = Shader.PropertyToID("_FurRimColor");

    // portal / per-camera environment
    Camera portalCam; RenderTexture portalRT;
    public Shader portalCompositeShader;
    GameObject portalCanvas; Material portalMat;
    static readonly int CenterId = Shader.PropertyToID("_Center"), RadiusId = Shader.PropertyToID("_Radius"), GlowId = Shader.PropertyToID("_Glow"),
        FlashId = Shader.PropertyToID("_Flash");
    readonly List<AudioSource> jungleAudio = new(); readonly List<float> jungleVol = new();
    bool forestFog; SphericalHarmonicsL2 forestAmbient, whiteAmbient; float forestSunI, forestSunShadow; Color forestSunColor;
    int mainMask;

    // showcase
    class Hotspot { public TigerFacts.Point pt; public Transform bone; public Vector3 local; public Transform marker; public Material mat; }
    readonly List<Hotspot> spots = new();
    readonly List<Vector3> screenPos = new();
    int hover = -1, selected = -1;
    float orbitYaw, orbitPitch = 8f, orbitDist = 3.6f, targetDist = -1, targetPitch;
    Vector3 camFromPos, camToPos, lookFrom, lookTo; float flyTime = -1, flyDur = 1.2f; bool flyingToOverview;
    bool dragging; Vector2 dragLast;

    static readonly int OpenId = Shader.PropertyToID("_Open"), HoverId = Shader.PropertyToID("_Hover"), FadeId = Shader.PropertyToID("_Fade"), PhaseId = Shader.PropertyToID("_Phase");

    void Awake()
    {
        all.Add(this);
        species = AnimalFacts.Get(speciesId);
        anim = GetComponentInChildren<Animator>();
        var smr = lowRenderer ? lowRenderer : GetComponentInChildren<SkinnedMeshRenderer>();
        var bones = new Dictionary<string, (Transform t, Matrix4x4 rest)>();
        for (int i = 0; i < smr.bones.Length; i++)
            bones[smr.bones[i].name] = (smr.bones[i], transform.worldToLocalMatrix * smr.transform.localToWorldMatrix * smr.sharedMesh.bindposes[i].inverse);
        head = bones[headBone].t; neck = bones[neckBone].t;
        // snout direction in the head bone's local space (rest pose: +Z, slightly down)
        headFwdLocal = bones[headBone].rest.inverse.MultiplyVector(new Vector3(0, -0.15f, 1f)).normalized;
        foreach (var pt in species.points)
        {
            if (!bones.TryGetValue(pt.bone, out var b)) continue;
            spots.Add(new Hotspot { pt = pt, bone = b.t, local = b.rest.inverse.MultiplyPoint3x4(pt.restPos) });
        }
        if (whiteRoom) whiteRoom.SetActive(false);
        // environment values of both worlds, applied per camera while both are on screen
        forestFog = RenderSettings.fog; forestAmbient = RenderSettings.ambientProbe;
        // white room ambient: soft sky from above, bright bounce from the white floor, a little darker at the horizon
        whiteAmbient = new SphericalHarmonicsL2(); whiteAmbient.AddAmbientLight(new Color(0.62f, 0.64f, 0.67f));
        whiteAmbient.AddDirectionalLight(Vector3.up, new Color(0.30f, 0.32f, 0.36f), 1f);
        whiteAmbient.AddDirectionalLight(Vector3.down, new Color(0.26f, 0.25f, 0.24f), 1f);
        if (whiteVolume) whiteVolume.profile.TryGet(out whiteDof);           // runtime copy of the profile
        if (sun) { forestSunI = sun.intensity; forestSunShadow = sun.shadowStrength; forestSunColor = sun.color; }
        mainMask = cam.cullingMask & ~(1 << whiteLayer) & ~(1 << hdLayer);
        cam.cullingMask = mainMask;
        foreach (var a in forestRoot.GetComponentsInChildren<AudioSource>())
            if (!fx || !a.transform.IsChildOf(fx.transform)) { jungleAudio.Add(a); jungleVol.Add(a.volume); }   // the whoosh is not jungle
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        if (hud)
        {
            hud.ReturnClicked += () => { if (active == this) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); };
            hud.BackClicked += BackToOverview; hud.PointClicked += i => { if (state == State.Showcase || state == State.Focus) Focus(i); };
        }
    }

    // ---------------------------------------------------------------- input
    struct In { public Vector2 mouse, delta; public bool click, rclick, release, held, esc; public float scroll; }
    static In ReadInput()
    {
        var i = new In();
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m != null)
        {
            i.mouse = m.position.ReadValue(); i.delta = m.delta.ReadValue();
            i.click = m.leftButton.wasPressedThisFrame; i.release = m.leftButton.wasReleasedThisFrame; i.held = m.leftButton.isPressed;
            i.rclick = m.rightButton.wasPressedThisFrame; i.scroll = m.scroll.ReadValue().y / 120f;
        }
        if (Keyboard.current != null) i.esc = Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.backspaceKey.wasPressedThisFrame;
#else
        i.mouse = Input.mousePosition; i.delta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 20f;
        i.click = Input.GetMouseButtonDown(0); i.release = Input.GetMouseButtonUp(0); i.held = Input.GetMouseButton(0);
        i.rclick = Input.GetMouseButtonDown(1); i.scroll = Input.mouseScrollDelta.y; i.esc = Input.GetKeyDown(KeyCode.Escape);
#endif
        return i;
    }

    void Update()
    {
        if (active && active != this) return;                  // another animal's encounter runs
        var input = ReadInput();
        stateTime += Time.deltaTime;
        switch (state)
        {
            case State.Forest: UpdateForest(input); break;
            case State.Charge: UpdateCharge(); break;
            case State.Pounce: UpdatePounce(); break;
            case State.Alert: UpdateAlert(); break;
            case State.Dive: UpdateDive(); break;
            case State.Arrive: UpdateArrive(); break;
            case State.Showcase:
            case State.Focus: UpdateShowcase(input); break;
        }
        FxFrame();
    }

    // transition effects on one continuous clock from the pounce until the camera has settled (offsets go on top of
    // the glide pose that the state update just set)
    void FxFrame()
    {
        if (!fxRunning) return;
        fxClock += Time.deltaTime;
        if (!gliding && fxClock > takeoffT) { fx.End(cam); fxRunning = false; return; }
        fx.Tick(cam, fxClock, portalR, portalC);
        fx.ApplyCamera(cam, TigerCenter, fxClock);
    }

    void Go(State s) { state = s; stateTime = 0; }

    // ---------------------------------------------------------------- forest
    void UpdateForest(In input)
    {
        lookWeight = Mathf.MoveTowards(lookWeight, wander && wander.enabled ? wander.LookWeight : 0.75f, Time.deltaTime);
        if (Leader != this) return;                              // the leader aims for every animal (one HUD reticle)
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        var aim = locked ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : input.mouse;
        TigerEncounter target = null;
        if (Physics.Raycast(cam.ScreenPointToRay(aim), out var hit, 60f, ~0, QueryTriggerInteraction.Collide))
            target = all.Find(e => e.clickCollider == hit.collider && e.state == State.Forest);
        // held gaze: after 0.8 s on an animal it stops and watches the viewer back
        gazeTime = target && target == gazeTarget ? gazeTime + Time.deltaTime : 0f; gazeTarget = target;
        bool watched = target && gazeTime > 0.8f && target.wander && target.wander.enabled;
        if (watched) target.wander.Watch(2.5f);
        if (hud) hud.SetReticle(true, target, aim, target ? (watched ? target.species.reticle.Replace("CLICK TO APPROACH", "WATCHING YOU  //  CLICK") : target.species.reticle) : null);
        if (input.click && target && (!hud || !hud.PointerOverUI(input.mouse))) target.Begin();
    }

    void Begin()
    {
        if (active) return;
        active = this;
        foreach (var e in all)                                     // one HD twin in the white room (skin + fur)
            if (e != this) foreach (var r in e.GetComponentsInChildren<Renderer>()) if (r.gameObject.layer == e.hdLayer) r.enabled = false;
        if (hud) hud.SetReticle(false, false, Vector2.zero);
        if (entry == Entry.Pounce) StartCharge(); else StartAlert();
    }

    // ---------------------------------------------------------------- dive (prey)
    void StartAlert()
    {
        if (wander) wander.enabled = false;
        var loco = GetComponent<QuadrupedLocomotion>(); if (loco) loco.enabled = false;
        Go(State.Alert);
        anim.speed = 1f;
        anim.CrossFadeInFixedTime(anim.HasState(0, Animator.StringToHash("Alert")) ? "Alert" : "Idle", 0.3f);
        camFloor = new Vector3(cam.transform.position.x, transform.position.y, cam.transform.position.z);
        foreach (var r in GetComponentsInChildren<Renderer>()) r.lightProbeUsage = LightProbeUsage.Off;   // same lighting path in both worlds
        takeoffT = alertTime;                                    // FX clock: "takeoff" = the moment the portal opens
        if (fx) { fx.Begin(cam, alertTime, transform.forward); fxRunning = true; fxClock = 0f; }    // charge sparkles while it stares
    }

    void UpdateAlert()
    {
        lookWeight = Mathf.MoveTowards(lookWeight, 0.9f, Time.deltaTime * 2.5f);   // it stares at the viewer
        if (stateTime >= alertTime) StartDive();
    }

    void StartDive()
    {
        Go(State.Dive);
        landPos = transform.position; landing = true;
        // the white room's floor at the animal's feet, centred on it (it was built around the viewer)
        if (whiteRoom)
        {
            var floor = whiteRoom.transform.Find("Floor");
            var anchor = floor ? floor.position : new Vector3(cam.transform.position.x, whiteFloorY, cam.transform.position.z);
            whiteRoom.transform.position += new Vector3(landPos.x - anchor.x, landPos.y - anchor.y, landPos.z - anchor.z);
        }
        StartPortal();
        if (look) look.enabled = false;
        camStartPos = cam.transform.position;
        startLook = camStartPos + cam.transform.forward * Vector3.Distance(camStartPos, TigerCenter);
        finalCenter = TigerCenter;
        var dir = camStartPos - finalCenter; dir.y = 0; dir.Normalize();
        // end on a 3/4 front view (on the camera's side of the animal), not on whatever side it showed the viewer
        var fwd = transform.forward; fwd.y = 0; fwd.Normalize();
        float side = Mathf.Sign(Vector3.Dot(Vector3.Cross(fwd, dir), Vector3.up)); if (side == 0f) side = 1f;
        dir = Quaternion.AngleAxis(side * 40f, Vector3.up) * fwd;
        arcGlide = true;
        orbitYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; orbitPitch = showcasePitch; orbitDist = showcaseDist * Scale;
        finalPos = finalCenter + Quaternion.Euler(-orbitPitch, orbitYaw, 0) * Vector3.forward * orbitDist;
        glideStart = Time.time; glideEnd = Time.time + diveTime; gliding = true; targetDist = -1;
    }

    void UpdateDive()
    {
        lookWeight = Mathf.MoveTowards(lookWeight, 0.6f, Time.deltaTime);
        float rv = Mathf.Clamp01((stateTime - 0.15f) / diveReveal);
        UpdatePortalReveal(rv);
        UpdateGlide();
        if (swapped && !gliding)
        {
            anim.CrossFadeInFixedTime("Idle", 0.5f);
            BuildMarkers();
            if (hud) hud.ShowShowcase(species);
            Go(State.Showcase);
        }
    }

    void StartCharge()
    {
        if (wander) wander.enabled = false;
        active = this;
        var loco = GetComponent<QuadrupedLocomotion>(); if (loco) loco.enabled = false;   // the run / pounce clips own the legs
        Go(State.Charge);
        anim.CrossFadeInFixedTime("Run", 0.18f);
        anim.speed = runSpeed / (runNoSlideSpeed * Scale);    // paws stay planted at this root speed
        if (hud) hud.SetReticle(false, false, Vector2.zero);
        camFloor = new Vector3(cam.transform.position.x, transform.position.y, cam.transform.position.z);
    }

    Vector3 FlatToCam() { var d = camFloor - transform.position; d.y = 0; return d; }

    void FaceCamera(float rate)
    {
        var d = FlatToCam(); if (d.sqrMagnitude < 1e-4f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d.normalized), 1 - Mathf.Exp(-rate * Time.deltaTime));
    }

    float GroundAt(Vector3 p)
    {
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
            if (!h.collider.transform.IsChildOf(transform)) return h.point.y;
        return p.y;
    }

    void UpdateCharge()
    {
        lookWeight = Mathf.MoveTowards(lookWeight, 0.3f, Time.deltaTime * 2);   // eyes stay on the viewer while charging
        FaceCamera(10f);
        var d = FlatToCam();
        float step = Mathf.Min(runSpeed * Mathf.Clamp01(stateTime * 3f + 0.3f) * Time.deltaTime, d.magnitude);
        var p = transform.position + d.normalized * step;
        p.y = Mathf.Lerp(p.y, GroundAt(p), 0.5f);
        transform.position = p;
        if (d.magnitude <= pounceAt) StartPounce();
    }

    void StartPounce()
    {
        Go(State.Pounce);
        anim.speed = pounceClipSpeed;
        anim.CrossFadeInFixedTime("Pounce", 0.1f);
        pounceStart = transform.position;
        takeoffT = takeoffFrame / 30f / pounceClipSpeed;
        pounceLen = 35f / 30f / pounceClipSpeed;
        contactT = pounceLen + landContactFrame / 30f;                       // Land plays at 1x
        var d = FlatToCam().normalized;
        landPos = camFloor - d * landDistance; landPos.y = whiteFloorY;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.lightProbeUsage = LightProbeUsage.Off;   // same lighting path in both worlds
        StartPortal();
        StartGlide();
        if (fx) { fx.Begin(cam, takeoffT, transform.forward); fxRunning = true; fxClock = 0f; }
    }

    // One camera move from the leap to the showcase framing: it starts just before takeoff, tracks the tiger through
    // the portal and settles on the orbit pose as the landing finishes (the orbit then continues from exactly there).
    void StartGlide()
    {
        if (look) look.enabled = false;
        camStartPos = cam.transform.position;
        startLook = camStartPos + cam.transform.forward * Vector3.Distance(camStartPos, TigerCenter);
        finalCenter = landPos + Vector3.up * 0.6f * Scale;
        var dir = camStartPos - finalCenter; dir.y = 0; dir.Normalize();
        orbitYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; orbitPitch = showcasePitch; orbitDist = showcaseDist * Scale / 1.3f;
        finalPos = finalCenter + Quaternion.Euler(-orbitPitch, orbitYaw, 0) * Vector3.forward * orbitDist;
        glideStart = Time.time + takeoffT * 0.5f; glideEnd = Time.time + pounceLen + 0.8f;
        gliding = true; targetDist = -1;
    }

    void UpdateGlide()
    {
        if (!gliding) return;
        float u = Mathf.Clamp01((Time.time - glideStart) / (glideEnd - glideStart));
        float e = u * u * u * (u * (6 * u - 15) + 10);                          // smootherstep
        float track = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.time - glideStart) / 0.35f));
        var pos = Vector3.Lerp(camStartPos, finalPos, e);
        if (arcGlide)
        {   // around the animal (spherical about its centre): the straight line could pass through the body
            var a = camStartPos - finalCenter; var b = finalPos - finalCenter;
            pos = finalCenter + Vector3.Slerp(a.normalized, b.normalized, e) * Mathf.Lerp(a.magnitude, b.magnitude, e);
        }
        var look = Vector3.Lerp(Vector3.Lerp(startLook, TigerCenter, track), finalCenter, e);
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
        if (u >= 1f) gliding = false;
    }

    void UpdatePounce()
    {
        float t = stateTime;
        // one continuous flight: small creep during the crouch, then a ballistic-looking arc to the touchdown point
        float f = Mathf.Clamp01((t - takeoffT) / (contactT - takeoffT));
        var p = Vector3.Lerp(pounceStart, landPos, t < takeoffT ? 0.05f * t / takeoffT : 0.05f + 0.95f * f);
        float baseY = Mathf.Lerp(pounceStart.y, landPos.y, f);
        p.y = baseY + (t < takeoffT ? 0 : 4f * arcHeight * f * (1 - f));
        transform.position = p;
        FaceCamera(6f);
        if (!landing && t >= pounceLen) { landing = true; anim.speed = 1f; anim.CrossFadeInFixedTime("Land", 0.04f); }

        UpdatePortalReveal(Mathf.Clamp01((t - revealStart) / revealTime));
        UpdateGlide();
        if (t >= contactT) { transform.position = landPos; Go(State.Arrive); }
    }

    // portal reveal centred on the animal, eased in so it starts small and engulfs the screen (rv 0..1)
    void UpdatePortalReveal(float rv)
    {
        float e = rv * rv * (3 - 2 * rv);
        var sc = cam.WorldToScreenPoint(TigerCenter - Vector3.up * 0.05f * Scale);
        float full = new Vector2(Screen.width, Screen.height).magnitude * 1.05f;
        portalR = Mathf.Lerp(0f, full, e * e); portalC = sc;
        if (!swapped && portalMat)
        {
            portalMat.SetVector(CenterId, new Vector4(sc.x, sc.y, 0, 0));
            portalMat.SetFloat(RadiusId, portalR);
            portalMat.SetFloat(GlowId, Mathf.Clamp01(rv * 6f) * (1f - rv * 0.5f));
            portalMat.SetFloat(FlashId, fx ? fx.Flash : 0f);
        }
        for (int i = 0; i < jungleAudio.Count; i++) if (jungleAudio[i]) jungleAudio[i].volume = jungleVol[i] * (1 - e);
        if (!swapped && rv >= 1f) SwapToWhite();
    }

    void StartPortal()
    {
        if (whiteRoom) whiteRoom.SetActive(true);                              // on its own layer: the main camera ignores it
        var go = new GameObject("PortalCamera");
        portalCam = go.AddComponent<Camera>();
        portalCam.CopyFrom(cam);
        portalCam.cullingMask = WhiteMask;
        portalCam.clearFlags = CameraClearFlags.SolidColor; portalCam.backgroundColor = whiteHorizon;
        var mainData = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        var pd = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        pd.renderPostProcessing = true; pd.volumeLayerMask = (1 << whiteLayer) | (1 << fxLayer); pd.renderShadows = true;
        if (mainData) { pd.antialiasing = mainData.antialiasing; pd.antialiasingQuality = mainData.antialiasingQuality; }
        portalRT = new RenderTexture(Screen.width, Screen.height, 24, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB) { name = "PortalRT" };
        portalCam.targetTexture = portalRT;
        portalCam.depth = cam.depth - 1;                                        // render before the main camera
        VolumetricFogFeature.ExcludedCameras.Add(portalCam);
        // composite: full-screen RawImage on an overlay canvas below the HUD; the shader masks it to the portal circle
        portalCanvas = new GameObject("PortalCanvas", typeof(Canvas));
        var cv = portalCanvas.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 5;
        var img = new GameObject("PortalImage", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        img.transform.SetParent(portalCanvas.transform, false);
        var rt = (RectTransform)img.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var raw = img.GetComponent<UnityEngine.UI.RawImage>(); raw.raycastTarget = false; raw.texture = portalRT;
        portalMat = new Material(portalCompositeShader); portalMat.SetFloat(RadiusId, 0); raw.material = portalMat;
    }

    void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
    {
        if (active ? active != this : Leader != this) return;   // one encounter sets the per-camera environment
        bool white = (portalCam && c == portalCam) || (swapped && c == cam);
        if (c != cam && c != portalCam) return;
        RenderSettings.fog = !white && forestFog;
        // fur rim light: from behind the subject, above (light travels toward the camera and down)
        Shader.SetGlobalVector(FurRimDirId, white ? (Vector4)(-c.transform.forward + Vector3.down * 0.6f).normalized : Vector4.zero);
        Shader.SetGlobalColor(FurRimColorId, white ? furRimColor : Color.black);
        if (white && whiteDof) whiteDof.focusDistance.value = FocusDistance(c);
        if (hdRenderer && lowRenderer)
        {   // each world casts the shadow of its own tiger only (no double shadow from the hidden twin)
            lowRenderer.shadowCastingMode = white ? ShadowCastingMode.Off : ShadowCastingMode.On;
            hdRenderer.shadowCastingMode = white ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
        RenderSettings.ambientProbe = white ? whiteAmbient : forestAmbient;
        if (sun)
        {
            sun.intensity = white ? whiteSunIntensity : forestSunI; sun.shadowStrength = white ? 0.6f : forestSunShadow;
            sun.color = white ? whiteSunColor : forestSunColor;
        }
    }

    void LateUpdatePortalCamera()
    {
        if (!portalCam) return;
        portalCam.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
        portalCam.fieldOfView = cam.fieldOfView;
        if (portalRT && (portalRT.width != Screen.width || portalRT.height != Screen.height))
        {
            portalCam.targetTexture = null; portalRT.Release();
            portalRT.width = Screen.width; portalRT.height = Screen.height; portalRT.Create(); portalCam.targetTexture = portalRT;
        }
    }

    // ---------------------------------------------------------------- white room
    void SwapToWhite()
    {
        // the portal covers the screen: the main camera now renders exactly what the portal camera showed
        swapped = true;
        foreach (Transform c in forestRoot)
            if (System.Array.IndexOf(keepActive, c.name) < 0) c.gameObject.SetActive(false);
        VolumetricFogFeature.ExcludedCameras.Add(cam);
        cam.cullingMask = WhiteMask;
        if (hdRenderer && lowRenderer) lowRenderer.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = whiteHorizon;
        var md = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        if (md) md.volumeLayerMask = (1 << whiteLayer) | (1 << fxLayer);
        if (portalCanvas) Destroy(portalCanvas);
        if (portalCam) { VolumetricFogFeature.ExcludedCameras.Remove(portalCam); Destroy(portalCam.gameObject); portalCam = null; }
        if (portalRT) { portalRT.Release(); Destroy(portalRT); portalRT = null; }
        var ring = whiteRoom ? whiteRoom.transform.Find("LandingRing") : null;
        if (ring) ring.position = new Vector3(landPos.x, ring.position.y, landPos.z);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    void UpdateArrive()
    {
        if (!landing) { landing = true; anim.speed = 1f; anim.CrossFadeInFixedTime("Land", 0.04f); }
        var st = anim.GetCurrentAnimatorStateInfo(0);
        if ((st.IsName("Land") && st.normalizedTime >= 0.96f) || stateTime > 1.4f)
        {
            anim.CrossFadeInFixedTime("Idle", 0.3f);
            BuildMarkers();
            if (hud) hud.ShowShowcase(species);
            Go(State.Showcase);
        }
        UpdateGlide();
    }

    void BuildMarkers()
    {
        foreach (var s in spots)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "Hotspot_" + s.pt.id;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(whiteRoom ? whiteRoom.transform : transform, false); q.layer = whiteLayer;
            var r = q.GetComponent<MeshRenderer>(); s.mat = new Material(markerMaterial); s.mat.SetFloat(PhaseId, Random.value); r.sharedMaterial = s.mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            s.marker = q.transform;
        }
    }

    // depth of field: the selected hotspot in Focus, otherwise the tiger's centre
    float FocusDistance(Camera c)
    {
        var target = state == State.Focus && selected >= 0 ? SpotWorld(spots[selected]) : TigerCenter;
        return Mathf.Max(0.3f, Vector3.Dot(target - c.transform.position, c.transform.forward));
    }

    int WhiteMask => (1 << whiteLayer) | (1 << fxLayer) | (1 << (hdRenderer ? hdLayer : tigerLayer));
    Vector3 SpotWorld(Hotspot s) => s.bone.TransformPoint(s.local);
    float Scale => transform.lossyScale.x;
    Vector3 TigerCenter => transform.position + Vector3.up * centerHeight * Scale;

    void UpdateShowcase(In input)
    {
        // marker placement and screen positions
        screenPos.Clear();
        for (int i = 0; i < spots.Count; i++)
        {
            var w = SpotWorld(spots[i]);
            var toCam = (cam.transform.position - w).normalized;
            spots[i].marker.position = w + toCam * 0.03f;
            var sp = cam.WorldToScreenPoint(w);
            screenPos.Add(sp);
        }
        // hover: nearest marker within 30 px of the pointer
        hover = -1; float best = 30f;
        bool overUI = hud && hud.PointerOverUI(input.mouse);
        if (!overUI && !dragging)
            for (int i = 0; i < screenPos.Count; i++)
            {
                if (screenPos[i].z <= 0) continue;
                float d = Vector2.Distance(input.mouse, screenPos[i]);
                if (d < best) { best = d; hover = i; }
            }
        for (int i = 0; i < spots.Count; i++)
        {
            float h = i == hover ? 1 : 0;
            spots[i].mat.SetFloat(HoverId, Mathf.MoveTowards(spots[i].mat.GetFloat(HoverId), h, Time.deltaTime * 6));
            spots[i].mat.SetFloat(FadeId, selected < 0 || selected == i ? 1f : 0.35f);
        }
        if (hud) { hud.UpdateTags(screenPos, hover, selected); if (selected >= 0) hud.UpdateLeader(screenPos[selected]); }

        // clicks: a marker focuses it; otherwise a drag orbits (overview only)
        if (input.click && !overUI)
        {
            if (hover >= 0) Focus(hover);
            else if (state == State.Showcase) { dragging = true; dragLast = input.mouse; }
        }
        if (input.release) dragging = false;
        if (dragging && input.held && state == State.Showcase && flyTime < 0)
        {
            var dm = input.mouse - dragLast; dragLast = input.mouse;
            orbitYaw += dm.x * 0.25f; orbitPitch = Mathf.Clamp(orbitPitch - dm.y * 0.2f, -5f, 55f); targetDist = -1;
        }
        if (state == State.Showcase && Mathf.Abs(input.scroll) > 0.01f && !overUI) targetDist = -1;
        if (state == State.Showcase && Mathf.Abs(input.scroll) > 0.01f && !overUI) orbitDist = Mathf.Clamp(orbitDist - input.scroll * 0.35f, 2.0f, 6.5f);
        if (state == State.Focus && (input.esc || input.rclick)) BackToOverview();

        lookWeight = Mathf.MoveTowards(lookWeight, state == State.Focus ? 0.2f : 0.5f, Time.deltaTime);
        if (gliding) UpdateGlide();
        else if (flyTime >= 0) Fly();
        else if (state == State.Showcase) OrbitCamera(true);
        else if (state == State.Focus) FollowFocus();
    }

    Vector3 OverviewPos() => TigerCenter + Quaternion.Euler(-orbitPitch, orbitYaw, 0) * Vector3.forward * orbitDist;   // +pitch = camera above

    void OrbitCamera(bool smooth)
    {
        if (targetDist > 0)
        {
            orbitDist = Mathf.Lerp(orbitDist, targetDist, 1 - Mathf.Exp(-1.5f * Time.deltaTime));
            orbitPitch = Mathf.Lerp(orbitPitch, targetPitch, 1 - Mathf.Exp(-1.5f * Time.deltaTime));
            if (Mathf.Abs(orbitDist - targetDist) < 0.01f) targetDist = -1;
        }
        var target = OverviewPos();
        float k = smooth ? 1 - Mathf.Exp(-8f * Time.deltaTime) : 1;
        cam.transform.position = Vector3.Lerp(cam.transform.position, target, k);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(TigerCenter - cam.transform.position), k);
    }

    Vector3 FocusCamPos(Hotspot s) => SpotWorld(s) + transform.rotation * s.pt.viewDir.normalized * s.pt.viewDist * Scale;

    void Focus(int i)
    {
        selected = i;
        StartFly(FocusCamPos(spots[i]), SpotWorld(spots[i]), false);
        Go(State.Focus);
        if (hud) { hud.HideInfo(); hud.ShowInfo(spots[i].pt, i, spots.Count); }
    }

    void BackToOverview()
    {
        if (state != State.Focus) return;
        selected = -1;
        if (hud) hud.HideInfo();
        StartFly(OverviewPos(), TigerCenter, true);
        Go(State.Showcase);
    }

    void StartFly(Vector3 toPos, Vector3 lookAt, bool toOverview)
    {
        camFromPos = cam.transform.position; camToPos = toPos;
        lookFrom = cam.transform.position + cam.transform.forward * Vector3.Distance(cam.transform.position, lookAt);
        lookTo = lookAt; flyTime = 0; flyingToOverview = toOverview;
        flyDur = Mathf.Clamp(Vector3.Distance(camFromPos, camToPos) * 0.35f + 0.7f, 0.8f, 1.6f);
    }

    void Fly()
    {
        flyTime += Time.deltaTime;
        float f = Mathf.Clamp01(flyTime / flyDur), e = f * f * f * (f * (6 * f - 15) + 10);   // smootherstep
        // move around the tiger (spherical about its centre) so the path never cuts through the body
        var c = TigerCenter;
        var a = camFromPos - c; var b = camToPos - c;
        var p = c + Vector3.Slerp(a.normalized, b.normalized, e) * Mathf.Lerp(a.magnitude, b.magnitude, e);
        cam.transform.position = p;
        if (selected >= 0) lookTo = SpotWorld(spots[selected]);
        cam.transform.rotation = Quaternion.LookRotation(Vector3.Lerp(lookFrom, flyingToOverview ? c : lookTo, e) - p);
        if (f >= 1) flyTime = -1;
    }

    void FollowFocus()
    {
        var s = spots[selected];
        float k = 1 - Mathf.Exp(-6f * Time.deltaTime);
        cam.transform.position = Vector3.Lerp(cam.transform.position, FocusCamPos(s), k);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(SpotWorld(s) - cam.transform.position), k);
    }

    // ---------------------------------------------------------------- head look-at (after the Animator)
    void LateUpdate()
    {
        LateUpdatePortalCamera();
        if (!head || lookWeight <= 0.001f) return;
        // forest: the wander decides (the viewer, or ahead into a turn); otherwise the camera
        var target = state == State.Forest && wander && wander.enabled ? wander.LookTarget : cam.transform.position;
        foreach (var (bone, share) in new[] { (neck, 0.4f), (head, 1f) })
        {
            var cur = head.rotation * headFwdLocal;
            var want = (target - head.position).normalized;
            var delta = Quaternion.FromToRotation(cur, Vector3.RotateTowards(cur, want, 40f * Mathf.Deg2Rad, 0));
            bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, lookWeight * share * (bone == head ? 0.6f : 1f)) * bone.rotation;
        }
    }

    // test hooks (play-mode automation from unity/encounter_test.cs)
    public string StateName => state.ToString();
    public void TestStart() { if (state == State.Forest) Begin(); }
    public string SpeciesId => speciesId;
    public void TestFocus(int i) { if (state == State.Showcase || state == State.Focus) Focus(Mathf.Clamp(i, 0, spots.Count - 1)); }
    public void TestBack() => BackToOverview();

    void OnDestroy()
    {
        all.Remove(this); if (active == this) active = null;
        if (fxRunning && fx) fx.End(cam);
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        VolumetricFogFeature.ExcludedCameras.Clear();
        if (portalRT) portalRT.Release();
    }
}
