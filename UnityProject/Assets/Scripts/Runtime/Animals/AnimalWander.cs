using System.Collections.Generic;
using UnityEngine;

/// Ambient life for an animal in the low-poly world: idles, then walks to a point near a route (world-space polyline,
/// e.g. the jungle path) and stops again; now and then it looks at the viewer. Shared by every animal (the tiger,
/// tapir, chital): the owner reads LookTarget / LookWeight for its head look-at and disables this component when its
/// own behaviour takes over (charge, portal).
///   steering: always forward along an arc (yaw rate limited by speed / turn radius; a walking cat never pivots on the
///             spot), smooth start and braking
///   motion:   in-place Walk, playback = ground speed / clip speed; with ARC CLIPS (arcClips: Walk / WalkTurnL /
///             WalkTurnR blended by "Turn", authored along arcs of constant speed and turn rate) the animal is moved with
///             exactly those rates, so the body turns and curves and its feet stay planted (the tiger)
///   modes:    random points near the route; patrol (near end <-> far end); loop (closed route followed by pursuit,
///             with stop stations where it halts and watches the viewer: the tiger pacing in front of him)
///   head:     leads into turns; looks at the viewer when idle (often) or walking (now and then) and he is in front
public class AnimalWander : MonoBehaviour
{
    [Header("Route")]
    public Vector3[] route;                         // world polyline the animal stays near (y ignored, ground raycast)
    public float lateral = 0.5f;                    // max sideways offset from the route (m)
    public float minHop = 2.5f;                     // next target at least this far away (m)
    public bool patrol;                             // near end <-> far end instead of random points
    public bool loop;                               // closed route, followed continuously
    public float[] stops = { };                     // loop: stop stations (route fraction 0..1)
    public float lookAhead = 1.3f;                  // loop: pursuit point this far ahead on the route (m at scale 1; shorter = tighter tracking)
    public Vector2 nearIdle = new(5f, 9f);          // patrol near end / loop stop stations: idle time (s)

    [Header("Motion")]
    public float walkSpeed = 0.62f;                 // m/s on the ground
    public float walkNoSlideSpeed = 0.55f;          // Walk clip ground speed at 1x and scale 1 (export manifest)
    public float turnRadius = 0.9f;                 // m at scale 1 (tighter turns are not possible at walking speed)
    public float accel = 0.6f;                      // m/s^2 (start / stop)
    public float brakeDistance = 1.2f;              // m: slows down into the stop
    public Vector3 openSide;                        // world direction with room: big turns (> 100 deg) bulge this way
    public Vector2 idleTime = new(3f, 7f);
    public string idleState = "Idle", walkState = "Walk";
    [Header("Arc clips")]
    public bool arcClips;                           // Walk is a blend of arc walks (WalkTurnL / Walk / WalkTurnR)
    public string turnParam = "Turn";               // blend parameter: -1 WalkTurnL .. 0 Walk .. +1 WalkTurnR
    public float clipTurnRate = 27.5f;              // deg/s of the turn clips at 1x (export manifest)
    public float turnBoost = 1f;                    // yaw rate up to this x the clips' (only with paws locked by QuadrupedLocomotion:
                                                    // the planted paws hold while the body turns tighter than the clip's arc)
    public string speedParam = "WalkSpeed";         // Walk state speed multiplier (when the controller has it; else anim.speed)
    public Collider ground;                         // the terrain (optional): plants and props are not ground
    [System.NonSerialized] public bool groundedExternally;   // QuadrupedLocomotion sets the height from the paws

    [Header("Attention")]
    public Transform viewer;                        // usually the main camera
    [Range(0, 1)] public float lookChanceIdle = 0.7f, lookChanceWalk = 0.3f;
    public float lookFov = 100f;                    // only looks when the viewer is within this angle of its facing

    public float LookWeight { get; private set; }   // 0..1 head look-at weight toward LookTarget
    public Vector3 LookTarget { get; private set; }
    public bool Walking => walking;
    public float Speed => speed;
    public Vector3 Target => target;
    public float TurnBlend => turn;
    public float ExtraYawRate { get; private set; } // deg/s turned beyond the clips' arc (turnBoost): QuadrupedLocomotion leads the paws

    public float personalSpace = 2f;                // animals sharing a route keep out of each other's way
    public bool Watching => watchTimer > 0f;

    /// The viewer is looking at this animal: brake to a stop (no pivot, normal braking) and watch him back for at least
    /// `seconds`; walking resumes after it runs out. Call every frame while the gaze lasts.
    public void Watch(float seconds) { watchTimer = Mathf.Max(watchTimer, seconds); }
    float watchTimer;
    static readonly List<AnimalWander> all = new();

    Vector3 lookRaw, lookVel; bool hasSpeedParam;
    Animator anim; bool walking, atNear, towardFar = true, stopping; Vector3 target; float timer, lookTimer, wantLook, speed, yawRate, turn;
    int uturn, lastStop = -1;
    float Scale => transform.lossyScale.x;
    int TurnId => Animator.StringToHash(turnParam);

    void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        if (anim) foreach (var p in anim.parameters) hasSpeedParam |= p.name == speedParam;
        LookTarget = lookRaw = transform.position + transform.forward * 3f;
    }

    // playback of the Walk state only: a global anim.speed also sped up / froze the Idle being faded in
    void SetPlayback(float s) { if (hasSpeedParam) { anim.SetFloat(speedParam, s); anim.speed = 1f; } else anim.speed = s; }
    void OnEnable() { walking = false; speed = 0; timer = Random.Range(idleTime.x, idleTime.y) * 0.5f; if (!all.Contains(this)) all.Add(this); }
    void OnDisable() { ExtraYawRate = 0; all.Remove(this); if (anim && arcClips) anim.SetFloat(TurnId, 0f); }

    void Update()
    {
        if (route == null || route.Length < 2 || !anim) return;
        float dt = Time.deltaTime;
        if (watchTimer > 0f) { watchTimer -= dt; if (!walking) timer = Mathf.Max(timer, 0.6f); }   // stays until the gaze ends
        if (walking) Walk(dt);
        else if ((timer -= dt) <= 0f) StartWalk();
        Attention(dt);
    }

    // ------------------------------------------------------------ attention / head
    void Attention(float dt)
    {
        if ((lookTimer -= dt) <= 0f)
        {
            float chance = walking ? lookChanceWalk : (atNear ? 0.95f : lookChanceIdle);
            wantLook = Random.value < chance ? (walking ? 0.55f : 0.9f) : 0f;
            lookTimer = walking ? Random.Range(1.2f, 2.5f) : Random.Range(2.5f, 5f);
        }
        if (watchTimer > 0f && viewer) { lookRaw = viewer.position; LookWeight = Mathf.MoveTowards(LookWeight, 1f, dt * 1.5f); SmoothLook(dt); return; }
        bool viewerOk = wantLook > 0 && ViewerInFront();
        float leadDeg = walking ? Mathf.Clamp(yawRate * 0.8f, -40f, 40f) : 0f;          // look ~0.8 s ahead into the turn
        if (viewerOk) { lookRaw = viewer.position; LookWeight = Mathf.MoveTowards(LookWeight, wantLook, dt * 0.8f); }
        else
        {
            var ahead = Quaternion.AngleAxis(leadDeg, Vector3.up) * transform.forward;
            lookRaw = transform.position + ahead * 3f * Scale + Vector3.up * 0.7f * Scale;
            LookWeight = Mathf.MoveTowards(LookWeight, Mathf.Abs(leadDeg) > 5f ? 0.5f : 0f, dt * 0.8f);
        }
        SmoothLook(dt);
    }

    // the head follows the raw target with a lag (switching between viewer and "ahead" snapped the head)
    void SmoothLook(float dt) { LookTarget = Vector3.SmoothDamp(LookTarget, lookRaw, ref lookVel, 0.3f, Mathf.Infinity, dt); }

    bool ViewerInFront()
    {
        if (!viewer) return false;
        var d = viewer.position - transform.position; d.y = 0;
        return d.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, d) < lookFov;
    }

    // ------------------------------------------------------------ targets
    void StartWalk()
    {
        if (loop) { walking = true; stopping = false; atNear = false; uturn = 0; anim.CrossFadeInFixedTime(walkState, 0.4f); return; }
        for (int tries = 0; tries < 16; tries++)
        {
            float u;
            if (patrol)
            {
                bool mid = Random.value < 0.25f && tries < 8;
                u = mid ? Random.Range(0.35f, 0.65f) : towardFar ? Random.Range(0.85f, 1f) : Random.Range(0f, 0.08f);
            }
            else u = Random.value;
            var p = PointOnRoute(u) + Vector3.Cross(Vector3.up, RouteDir(u)).normalized * Random.Range(-lateral, lateral);
            var flat = p - transform.position; flat.y = 0;
            if (flat.magnitude < minHop || Crowded(p)) continue;
            // a target inside the turning circle on either side cannot be reached at walking speed
            var local = transform.InverseTransformDirection(flat);
            float r = turnRadius * Scale;
            if (new Vector2(local.x - r, local.z).magnitude < r * 1.05f || new Vector2(local.x + r, local.z).magnitude < r * 1.05f) continue;
            target = p; walking = true; atNear = false; uturn = 0;
            anim.CrossFadeInFixedTime(walkState, 0.4f);
            return;
        }
        if (patrol) towardFar = !towardFar;
        timer = 1f;
    }

    void Arrive()
    {
        walking = false; stopping = false; speed = 0; yawRate = 0; turn = 0; ExtraYawRate = 0;
        anim.CrossFadeInFixedTime(idleState, 0.5f); if (!hasSpeedParam) anim.speed = 1f;
        if (arcClips) anim.SetFloat(TurnId, 0f);
        float u = RouteU(transform.position);
        atNear = (patrol && u < 0.2f) || loop;
        if (patrol && (u < 0.2f || u > 0.8f)) towardFar = u < 0.5f;                 // turn around at the ends
        timer = atNear ? Random.Range(nearIdle.x, nearIdle.y) : Random.Range(idleTime.x, idleTime.y);
        lookTimer = 0.4f;
    }

    // ------------------------------------------------------------ steering
    void Walk(float dt)
    {
        if (loop) LoopTarget();
        var to = target - transform.position; to.y = 0;
        float dist = to.magnitude;
        bool final = !loop || stopping;                                                // loop: only stop stations end a walk
        if (final && (dist < 0.2f || (speed < 0.05f && dist < 0.5f))) { Arrive(); return; }
        if (BlockedAhead()) { Arrive(); return; }
        float err = Vector3.SignedAngle(transform.forward, to, Vector3.up);
        // U-turn: swing out into the open side. The direction is decided once and held until the turn is mostly done
        // (re-deciding every frame locked the heading where openSide is straight ahead: the tiger walked off east)
        if (uturn == 0 && Mathf.Abs(err) > 100f && openSide.sqrMagnitude > 0.01f)
        {
            float d = Vector3.Dot(transform.right, openSide);
            uturn = Mathf.Abs(d) > 0.2f ? (d > 0 ? 1 : -1) : (err > 0 ? 1 : -1);    // + = turn right (clockwise from above)
        }
        if (uturn != 0)
        {
            if (Mathf.Abs(err) < 60f) uturn = 0;
            else if (Mathf.Sign(err) != uturn) err = uturn * (360f - Mathf.Abs(err));
        }
        // speed: accelerate, brake into a stop, slow down in tight turns (never below a crawl: no pivoting)
        float brake = final ? Mathf.Clamp01(dist / (brakeDistance * Scale) + 0.15f) : 1f;
        float want = watchTimer > 0f ? 0f : walkSpeed * brake * Mathf.Lerp(1f, 0.55f, Mathf.Abs(err) / 180f);
        speed = Mathf.MoveTowards(speed, want, accel * dt);
        if (watchTimer > 0f && speed < 0.06f) { Arrive(); timer = Mathf.Max(timer, watchTimer); return; }   // stopped to watch
        float clipSpeed = walkNoSlideSpeed * Scale;
        if (arcClips)
        {
            // move exactly as the clips were authored: forward at clip speed x playback, yaw = blend x clip turn rate x playback
            float play = Mathf.Max(0.15f, speed / clipSpeed);
            SetPlayback(play);
            float maxYaw = clipTurnRate * play, boost = groundedExternally ? Mathf.Max(1f, turnBoost) : 1f;
            float wantYaw = Mathf.Clamp(err * 1.8f, -maxYaw * boost, maxYaw * boost);
            turn = Mathf.MoveTowards(turn, wantYaw / Mathf.Max(maxYaw, 1e-3f), 2.2f * dt);  // blend changes ease in
            anim.SetFloat(TurnId, Mathf.Clamp(turn, -1f, 1f));
            yawRate = turn * maxYaw; ExtraYawRate = (turn - Mathf.Clamp(turn, -1f, 1f)) * maxYaw;
            transform.rotation = Quaternion.AngleAxis(yawRate * dt, Vector3.up) * transform.rotation;
            var q = transform.position + transform.forward * clipSpeed * play * dt;
            if (!groundedExternally) q.y = Mathf.Lerp(q.y, GroundAt(q), 0.25f);
            transform.position = q;
            return;
        }
        float maxYawC = Mathf.Max(speed, 0.12f) / (turnRadius * Scale) * Mathf.Rad2Deg;
        float wantYawC = Mathf.Clamp(err * 1.8f, -maxYawC, maxYawC);
        yawRate = Mathf.MoveTowards(yawRate, wantYawC, 120f * dt);                    // no snap in angular velocity
        transform.rotation = Quaternion.AngleAxis(yawRate * dt, Vector3.up) * transform.rotation;
        var p = transform.position + transform.forward * speed * dt;
        p.y = Mathf.Lerp(p.y, GroundAt(p), 0.25f);
        transform.position = p;
        anim.speed = Mathf.Max(0.08f, speed / clipSpeed);
    }

    // loop: pursue a point ahead on the closed route; brake into stop stations
    void LoopTarget()
    {
        float total = RouteLength(); if (total < 0.1f) return;
        float s = RouteU(transform.position) * total;
        if (stopping) return;                                                          // target = the station
        for (int k = 0; k < stops.Length; k++)
        {
            float ahead = Mathf.Repeat(stops[k] * total - s, total);
            if (k != lastStop && ahead < brakeDistance * Scale * 1.6f && ahead > 0.05f) { stopping = true; lastStop = k; target = PointOnRoute(stops[k]); return; }
        }
        if (lastStop >= 0 && Mathf.Repeat(s - stops[lastStop] * total, total) > 2f * Scale && Mathf.Repeat(s - stops[lastStop] * total, total) < total * 0.5f)
            lastStop = stops.Length > 1 ? lastStop : -1;                              // a single station is re-armed once passed
        target = PointOnRoute(Mathf.Repeat((s + lookAhead * Scale) / total, 1f));
    }

    bool Crowded(Vector3 p)
    {
        foreach (var o in all)
        {
            if (o == this) continue;
            if (Flat(o.transform.position - p) < personalSpace || (o.walking && Flat(o.target - p) < personalSpace)) return true;
        }
        return false;
    }

    bool BlockedAhead()
    {
        foreach (var o in all)
        {
            if (o == this) continue;
            var d = o.transform.position - transform.position; d.y = 0;
            if (d.magnitude < 1.3f * Mathf.Max(1f, Scale) && Vector3.Dot(d.normalized, transform.forward) > 0.3f) return true;
        }
        return false;
    }

    static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

    // ------------------------------------------------------------ route (closed when loop: last point joins the first)
    IEnumerable<(Vector3 a, Vector3 b)> Segments()
    {
        for (int i = 1; i < route.Length; i++) yield return (route[i - 1], route[i]);
        if (loop) yield return (route[^1], route[0]);
    }

    float RouteLength() { float t = 0; foreach (var (a, b) in Segments()) t += Vector3.Distance(a, b); return t; }

    Vector3 PointOnRoute(float u)
    {
        float d = u * RouteLength();
        foreach (var (a, b) in Segments())
        {
            float seg = Vector3.Distance(a, b);
            if (d <= seg) return Vector3.Lerp(a, b, d / Mathf.Max(seg, 1e-4f));
            d -= seg;
        }
        return loop ? route[0] : route[^1];
    }

    float RouteU(Vector3 p)
    {
        float best = float.MaxValue, bu = 0, acc = 0, total = RouteLength();
        foreach (var (a, b) in Segments())
        {
            float seg = Vector3.Distance(a, b);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, b - a) / Mathf.Max(seg * seg, 1e-6f));
            float d = Flat(Vector3.Lerp(a, b, t) - p);
            if (d < best) { best = d; bu = (acc + t * seg) / total; }
            acc += seg;
        }
        return bu;
    }

    Vector3 RouteDir(float u) => (PointOnRoute(Mathf.Min(1, u + 0.02f)) - PointOnRoute(Mathf.Max(0, u - 0.02f))).normalized;

    float GroundAt(Vector3 p)
    {
        if (ground && ground.Raycast(new Ray(p + Vector3.up * 3f, Vector3.down), out var gh, 10f)) return gh.point.y;
        float y = float.NaN;                                                           // the terrain first (plants are not ground)
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider is TerrainCollider) return h.point.y;
            if (float.IsNaN(y) && !h.collider.transform.IsChildOf(transform)) y = h.point.y;
        }
        return float.IsNaN(y) ? p.y : y;
    }

    void OnDrawGizmosSelected()
    {
        if (route == null) return;
        Gizmos.color = Color.yellow;
        foreach (var (a, b) in Segments()) Gizmos.DrawLine(a, b);
        if (walking) { Gizmos.color = Color.cyan; Gizmos.DrawSphere(target, 0.15f); }
    }
}
