using UnityEngine;

/// Per-joint procedural layer over a quadruped's walk / idle clips (runs after the Animator, before the head look-at):
///   paws:  a planted paw is LOCKED where it touched down for the whole stance (no sliding at any turn rate, speed
///          change or blend); a swing travels from the lift-off spot to the clip's landing spot, at the clip's height
///          above the terrain; planted paws tilt to the ground normal. Standing, a paw left behind by the stop steps
///          back under the body (one leg at a time)
///   legs:  two-bone IK (upper + lower) per leg onto its paw; the knee / elbow keeps the clip's bend plane (the last
///          well-defined one when the leg is nearly straight), so it can never flip
///   body:  height from the four paws' terrain, pitch / roll from front vs hind / left vs right, smoothed; the head is
///          partly counter-pitched so the gaze stays level
///   tail:  per-joint lag (stiff at the base, loose at the tip): it trails behind turns
/// Stance / swing timing is the walk clip's gait table (touchdown / lift-off as cycle fractions, see build.py).
[DefaultExecutionOrder(-20)]
public class QuadrupedLocomotion : MonoBehaviour
{
    [System.Serializable]
    public class Leg
    {
        public string name;                                 // FL / FR / BL / BR
        public Transform upper, lower, foot, toe;           // shoulder / hip, elbow / knee, wrist / hock, paw
        public float touchdown, liftoff;                    // walk-cycle fractions (lift-off may exceed 1)
        [System.NonSerialized] public bool planted, init;
        [System.NonSerialized] public Vector3 plant, lift, target, natural, axis, wristOffset;
        [System.NonSerialized] public Quaternion footRot;
        [System.NonSerialized] public float swingU, swingU0, swingRate, groundY, liftGround, restH, liftH, liftStart, plantLift, tiltW;
        [System.NonSerialized] public bool skip, timed;    // timed: a standing step, finished on its own clock
    }

    public Leg[] legs;
    public Transform hips, head;
    public Transform[] tail;
    public Collider ground;                                 // the terrain collider (plants and props are ignored)
    public string walkState = "Walk";
    [Header("Body")]
    [Range(0, 1)] public float pitchFollow = 0.85f, rollFollow = 0.3f, headLevel = 0.6f, lowBias = 0.35f;
    public float bodySmoothing = 6f;                        // 1/s
    [Header("Tail")]
    public float tailStiffness = 14f;                       // 1/s at the base, x0.8 per joint toward the tip
    [Header("Steps")]
    public float stopSwingTime = 0.28f;                     // a paw caught mid-swing by the stop lands within this (s)
    public float restepDistance = 0.09f;                    // standing: a paw this far (m) from the clip's spot steps
    public float stretchDistance = 0.2f;                    // walking: a planted paw this far (m) behind the clip's spot steps
    [Range(0, 1)] public float peelScale = 0.35f;           // planted: share of the clip's heel-peel lift (the paw rolls, the pad stays down)

    public bool Walking { get; private set; }               // the gait drives the paws (QA: PlayMotionSampler)
    public event System.Action<Vector3> Touchdown;          // a paw locks onto the ground (world point): footstep sounds

    Animator anim; AnimalWander wander;
    float pitch, roll, bodyY, bodyLength = 1f, bodyWidth = 0.3f, standTime; bool bodyInit, wasWalking;
    Quaternion[] tailRot; int walkHash;

    void Awake()
    {
        anim = GetComponentInChildren<Animator>(); wander = GetComponent<AnimalWander>();
        tailRot = new Quaternion[tail != null ? tail.Length : 0]; walkHash = Animator.StringToHash(walkState);
    }

    void OnEnable() { bodyInit = false; if (legs != null) foreach (var l in legs) l.init = false; if (wander) wander.groundedExternally = true; }
    void OnDisable() { if (wander) wander.groundedExternally = false; }

    float GroundY(Vector3 p, out Vector3 n)
    {
        n = Vector3.up;
        if (ground && ground.Raycast(new Ray(new Vector3(p.x, p.y + 4f, p.z), Vector3.down), out var hit, 12f)) { n = hit.normal; return hit.point.y; }
        return transform.position.y;
    }
    float GroundY(Vector3 p) => GroundY(p, out _);
    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    void LateUpdate()
    {
        if (!anim || legs == null || legs.Length == 0) return;
        // edits stack up unless the Animator rewrites the pose every frame (culled Animators skip that)
        if (anim.cullingMode != AnimatorCullingMode.AlwaysAnimate) { Debug.LogWarning($"{name}: QuadrupedLocomotion needs AnimatorCullingMode.AlwaysAnimate", this); anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        // ---- gait phase: the Walk state (current, or the one being faded in) drives stance / swing
        var cur = anim.GetCurrentAnimatorStateInfo(0); bool trans = anim.IsInTransition(0);
        var nxt = trans ? anim.GetNextAnimatorStateInfo(0) : default;
        bool walkCur = cur.shortNameHash == walkHash, walkNext = trans && nxt.shortNameHash == walkHash;
        float tt = trans ? anim.GetAnimatorTransitionInfo(0).normalizedTime : 0f;
        float walkW = walkCur ? (trans ? 1f - tt : 1f) : walkNext ? tt : 0f;
        float phase = Mathf.Repeat(walkCur ? cur.normalizedTime : walkNext ? nxt.normalizedTime : 0f, 1f);
        bool walking = walkW > 0.5f; Walking = walking;
        float cycle = walkCur ? cur.length : walkNext ? nxt.length : 1f;                // s per walk cycle at the current playback
        float extraYaw = wander && wander.enabled ? wander.ExtraYawRate : 0f;
        standTime = walking ? 0f : standTime + dt;

        // ---- the clip's pose (before any correction): paw spot, paw height, wrist offset, paw rotation, bend plane
        var rootRot = transform.rotation; var invRoot = Quaternion.Inverse(rootRot); float rootY = transform.position.y;
        foreach (var l in legs)
        {
            l.natural = l.toe.position;
            l.wristOffset = invRoot * (l.foot.position - l.toe.position);
            l.footRot = invRoot * l.foot.rotation;
            var a = l.upper.position; var b = l.lower.position; var c = l.foot.position;
            var n = Vector3.Cross(b - a, c - b);
            // hinge axis (in the upper bone's frame): follows the clip's bend plane, firmly when the clip's leg is clearly
            // bent, not at all when it is nearly straight (its plane is noise there: the elbow wobbled 4 cm sideways)
            float bend = Vector3.Angle(b - a, c - b);
            if (!l.init) l.axis = l.upper.InverseTransformDirection(bend > 2f ? n.normalized : transform.right);
            else if (bend > 3f)
            {
                float trust = Mathf.Clamp01((bend - 3f) / 15f);
                l.axis = Vector3.Slerp(l.axis, l.upper.InverseTransformDirection(n.normalized), trust * (1f - Mathf.Exp(-20f * dt))).normalized;
            }
            if (!l.init)
            {
                l.restH = Mathf.Clamp(l.natural.y - rootY, 0f, 0.06f);
                l.groundY = GroundY(l.natural); l.plant = Flat(l.natural); l.planted = true; l.init = true;
            }
            l.liftH = Mathf.Max(0f, l.natural.y - rootY - l.restH);                 // the clip's own lift (heel peel, swing)
        }
        if (!bodyInit) BodySize();

        // ---- per leg: stance (locked) / swing (lift-off spot -> the clip's spot, over the terrain)
        bool walkStart = walking && !wasWalking; wasWalking = walking;
        bool anySwing = false; foreach (var l in legs) anySwing |= !l.planted;
        foreach (var l in legs)
        {
            float duty = l.liftoff - l.touchdown, ph = Mathf.Repeat(phase - l.touchdown, 1f);
            bool stance = !walking || ph < duty;
            float phaseU = Mathf.Clamp01((ph - duty) / Mathf.Max(1f - duty, 1e-3f));
            if (stance) l.skip = false;                                                // skip: landed early, waits for its next swing
            // planted: the clip's heel peel (a share of it), eased (crossfades and skipped swings changed it in one frame)
            float wantPlantLift = walking && stance && !l.skip ? l.liftH * peelScale : 0f;
            if (l.planted)
            {
                bool restep = walkW <= 0f && !anySwing && standTime > 0.3f && (Flat(l.natural) - l.plant).magnitude > restepDistance;
                // the walk starting with this leg late in its swing: it stays down and waits for its next one (catching
                // the clip's swing at 60 % and rushing the rest popped)
                if (walkStart && !stance && phaseU > 0.35f) l.skip = true;
                // left too far behind (a skipped swing, the body turning tighter than the clip): a quick step of its own
                // onto the clip's spot (during the clip's stance that spot then moves with the body: locked again)
                bool stretched = walking && stance && (Flat(l.natural) - l.plant).magnitude > stretchDistance;   // (in the clip's
                                                                              // swing its spot races ahead: no measure there)
                if ((walking && !stance && !l.skip) || restep || stretched)
                {
                    l.planted = false; l.lift = l.plant; l.liftGround = l.groundY; l.liftStart = l.plantLift; anySwing = true;
                    l.timed = !walking || stance || l.skip; l.swingU0 = l.timed ? 0f : phaseU; l.swingU = l.swingU0;
                    l.swingRate = (l.timed && walking ? 0.6f : 1f) / stopSwingTime; l.skip = false;
                }
            }
            if (!l.planted)
            {
                float prev = l.swingU;
                if (walking && !stance && !l.timed) { l.swingU = phaseU; if (l.swingU > prev) l.swingRate = (l.swingU - prev) / dt; }
                else
                {   // stopped (or the clip is already down): finish the swing, speeding up smoothly from its current pace
                    l.swingRate = Mathf.MoveTowards(l.swingRate, 1f / stopSwingTime, 8f * dt);
                    l.swingU = Mathf.Min(1f, l.swingU + dt * l.swingRate);
                }
                float u = Mathf.Clamp01((l.swingU - l.swingU0) / Mathf.Max(1f - l.swingU0, 1e-3f));
                float e = u * u * u * (10f + u * (6f * u - 15f));                       // minimum jerk: no velocity OR acceleration
                                                                                               // step at lift-off / touchdown
                // turning tighter than the clip: land ahead by half the extra yaw of the coming stance, so the paw ends the
                // stance as far behind as it started ahead (instead of falling a whole stance behind: stutter steps)
                var land = Flat(l.natural);
                if (walking && extraYaw != 0f)
                {
                    var c = Flat(transform.position);
                    land = c + Quaternion.AngleAxis(extraYaw * duty * cycle * 0.5f, Vector3.up) * (land - c);
                }
                var xz = Vector3.Lerp(l.lift, land, e);
                // the clip's peel (scaled down while planted) grows to its full swing height over the first third
                float sw = u / 0.35f; l.liftH = Mathf.Lerp(l.liftStart, l.liftH, sw >= 1f ? 1f : sw * sw * (3f - 2f * sw));
                if (l.timed) l.liftH = l.liftStart * (1f - e) + (walking ? 0.08f : 0.06f) * Mathf.Sin(Mathf.PI * u);   // own arc (the clip's
                                                                                               // lift belongs to another moment)
                // the swing arcs from the lift-off ground to the ground at the clip's spot (following the terrain under
                // the paw frame by frame jerked it up and down banks); it only rises over a bump in between
                float gPath = Mathf.Lerp(l.liftGround, GroundY(land), e);
                l.groundY = gPath;
                // smooth max of the arc and the ground under the paw (a hard max kinked where a bump starts to lift it);
                // equal to the arc at lift-off and touchdown, where both are the same ground
                float gUnder = GroundY(xz), gd = gPath - gUnder;
                float gSwing = 0.5f * (gPath + gUnder + Mathf.Sqrt(gd * gd + 0.0009f)) - 0.015f;
                l.target = new Vector3(xz.x, gSwing + l.restH + l.liftH, xz.z);
                l.plantLift = l.liftH;
                if (l.swingU >= 1f)
                {   // touchdown: lock
                    l.planted = true; l.plant = xz; l.groundY = GroundY(xz);
                    Touchdown?.Invoke(new Vector3(xz.x, l.groundY, xz.z));
                    if (l.timed && walking && !stance) l.skip = true;                    // landed in the clip's swing: wait
                    l.timed = false;
                }
            }
            else
            {
                l.plantLift = Mathf.Lerp(l.plantLift, wantPlantLift, 1f - Mathf.Exp(-14f * dt));
                l.target = new Vector3(l.plant.x, l.groundY + l.restH + l.plantLift, l.plant.z);
            }
        }

        // ---- body: height, pitch and roll from the paws' terrain (smoothed)
        float gF = 0, gH = 0, gL = 0, gR = 0; int nF = 0, nH = 0, nL = 0, nR = 0;
        foreach (var l in legs)
        {
            if (l.name[0] == 'F') { gF += l.groundY; nF++; } else { gH += l.groundY; nH++; }
            if (l.name[^1] == 'L') { gL += l.groundY; nL++; } else { gR += l.groundY; nR++; }
        }
        gF /= Mathf.Max(1, nF); gH /= Mathf.Max(1, nH); gL /= Mathf.Max(1, nL); gR /= Mathf.Max(1, nR);
        float gMin = float.MaxValue; foreach (var l in legs) gMin = Mathf.Min(gMin, l.groundY);
        float wantY = Mathf.Lerp(0.5f * (gF + gH), gMin, lowBias);                      // uphill legs fold, downhill ones reach
        float wantPitch = Mathf.Atan2(gF - gH, bodyLength) * Mathf.Rad2Deg * pitchFollow;
        float wantRoll = Mathf.Atan2(gL - gR, bodyWidth) * Mathf.Rad2Deg * rollFollow;
        if (!bodyInit) { bodyY = wantY; pitch = wantPitch; roll = wantRoll; bodyInit = true; }
        float k = 1f - Mathf.Exp(-bodySmoothing * dt);
        bodyY = Mathf.Lerp(bodyY, wantY, k); pitch = Mathf.Lerp(pitch, wantPitch, k); roll = Mathf.Lerp(roll, wantRoll, k);
        var p0 = transform.position; transform.position = new Vector3(p0.x, bodyY, p0.z);
        if (hips)
        {
            var pivot = hips.position + transform.forward * (bodyLength * 0.5f);
            hips.RotateAround(pivot, transform.right, -pitch);                          // + pitch = front higher = nose up
            hips.RotateAround(pivot, transform.forward, -roll);                         // + roll = left higher
            if (head) head.rotation = Quaternion.AngleAxis(pitch * headLevel, transform.right) * head.rotation;
        }

        // ---- legs: IK of the wrist onto target + the clip's wrist offset, paw exactly on its target, then the pad
        //      tilts to the ground (eased in / out over the stance: switching it at lift-off popped the leg 9 cm)
        foreach (var l in legs)
        {
            SolveTwoBone(l, l.target + rootRot * l.wristOffset);
            l.foot.rotation = rootRot * l.footRot;
            var w = l.foot.position; var paw = l.toe.position;
            if ((paw - w).sqrMagnitude > 1e-8f)                                       // limited: a leg short of its target must not flail the paw
                l.foot.rotation = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(paw - w, l.target - w), 20f) * l.foot.rotation;
            l.tiltW = Mathf.MoveTowards(l.tiltW, l.planted ? 1f : 0f, dt * 6f);
            if (l.tiltW > 0f)
            {
                GroundY(l.target, out var gn);
                var up = transform.up;
                l.toe.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(up, gn), l.tiltW) * l.toe.rotation;
            }
        }

        // ---- tail: per-joint lag
        float s = tailStiffness;
        for (int i = 0; i < tailRot.Length; i++)
        {
            if (!tail[i]) continue;
            var want = tail[i].rotation;
            if (tailRot[i].w == 0f && tailRot[i].x == 0f && tailRot[i].y == 0f && tailRot[i].z == 0f) tailRot[i] = want;
            tailRot[i] = Quaternion.Slerp(tailRot[i], want, 1f - Mathf.Exp(-s * dt));
            tail[i].rotation = tailRot[i];
            s *= 0.8f;
        }
    }

    // shoulder-hip and left-right paw spacing from the start pose
    void BodySize()
    {
        Vector3 f = Vector3.zero, h = Vector3.zero, lft = Vector3.zero, rgt = Vector3.zero;
        foreach (var l in legs)
        {
            if (l.name[0] == 'F') f += l.natural; else h += l.natural;
            if (l.name[^1] == 'L') lft += l.natural; else rgt += l.natural;
        }
        bodyLength = Mathf.Max(0.3f, Flat(f - h).magnitude * 0.5f);
        bodyWidth = Mathf.Max(0.1f, Flat(lft - rgt).magnitude * 0.5f);
    }

    /// Two-bone IK: the wrist (foot bone origin) onto target. The bend angle about the clip's hinge axis is set by the
    /// law of cosines (0 = straight, always the clip's bend direction), then the chain swings onto the target.
    const float SoftStart = 0.92f;
    static void SolveTwoBone(Leg l, Vector3 target)
    {
        var a = l.upper.position; var b = l.lower.position; var c = l.foot.position;
        float la = (b - a).magnitude, lb = (c - b).magnitude;
        if (la < 1e-5f || lb < 1e-5f) return;
        // soft IK: past softStart of the full length the reach eases toward it exponentially (a hard clamp snapped the
        // elbow straight in one frame: 100-200 mm pops when a paw reached down a bank)
        float L = la + lb, ds = L * SoftStart, d = (target - a).magnitude;
        if (d > ds) d = ds + (L - ds) * (1f - Mathf.Exp(-(d - ds) / (L - ds)));
        d = Mathf.Clamp(d, Mathf.Abs(la - lb) + 1e-3f, L * 0.9995f);
        var axis = l.upper.TransformDirection(l.axis);
        float bendCur = Vector3.SignedAngle(b - a, c - b, axis);
        float bendDes = 180f - Mathf.Acos(Mathf.Clamp((la * la + lb * lb - d * d) / (2f * la * lb), -1f, 1f)) * Mathf.Rad2Deg;
        l.lower.rotation = Quaternion.AngleAxis(bendDes - bendCur, axis) * l.lower.rotation;
        l.upper.rotation = Quaternion.FromToRotation(l.foot.position - a, target - a) * l.upper.rotation;
    }
}
