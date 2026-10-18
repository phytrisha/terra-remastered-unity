# White tiger HD: the high-detail twin of SK_WhiteTiger, on the SAME skeleton and clips, for the portal transition.
#
#   tools/blender_run.sh blender/assets/WhiteTigerHD/build.py blender/assets/WhiteTiger/WhiteTiger.blend
#   STAGES=ingest,warp,ears,weights,fur,furmesh,ao,verify,export (default: all). Opens the low-poly file (rig + baked Idle/Run/Pounce/Land
#   are the source of truth) and saves the result as WhiteTigerHD.blend, so every run starts clean; rebuild after any
#   change to the low-poly rig or clips.
#
# Spec
#   Use: the tiger seen through the portal and in the white showcase room (orbit 2-6.5 m, hotspot close-ups ~0.5 m).
#        The forest camera renders SK_WhiteTiger, the portal camera this mesh; both are skinned to the same bones, so
#        the swap at the portal rim is per pixel with no change of pose or silhouette.
#   Source: user-supplied Tripo HD conversion of the same tiger (sources/tiger/WhiteTigerHD/), 88.9k tris, PBR
#           (colour, normal, metallic, roughness, 4K). Same model and pose as the low-poly one but other proportions
#           (5 % shorter body, head rotated ~8 deg, hind-leg stance mirrored), so it is registered onto the low-poly
#           rest pose like a rig would move it: (1) the head is ONE rigid copy fitted onto the low-poly head (exact
#           source face, no stretch), (2) each leg (connected piece below LEG_Z) swings rigidly about its shoulder /
#           hip, (3) the neck bends (rotation interpolated from the neck base), (4) a coarse smooth warp (normalised
#           Gaussian convolution of same-part nearest-surface offsets, sigma 25 -> 4 cm) with the head pinned matches
#           the remaining proportions. Surface detail, UVs and texture alignment are the source's.
#   Budget: hero character close-up: 88.9k tris kept (PROJECT.md character 15-50k; one instance, only rendered in the
#           portal / showcase), 1 material M_WhiteTigerHD, BC + N + MS 4096
#   Rig: WhiteTiger_Rig unchanged (33 deform bones); body weights transferred from SM_WhiteTiger (rest pose,
#        interpolated nearest face); head 100 % head bone (ramp HEAD_T), ears only on the HD ear flaps; max 4, normalised
#   Fur (Unity shell fur, TerraRemastered/FurShell): UV1 "FurFlow" = growth direction in the UV0 tangent frame (skinned
#        with the tangents, so it bends with the animation), UV2 "FurData" = (UV0 units per metre for a constant strand
#        size, hair length mask: 0 eyes/teeth/whiskers/nose, short face/lips/paws, long cheek ruff/belly)
#   Fur mesh: SK_WhiteTigerHD_Fur, a decimated copy (FUR_MESH_TRIS) on the same bones that carries the shells (the full
#        mesh is drawn once as the skin), with its own fur data and baked AO in its vertex colour (Cycles, AO_DISTANCE:
#        creases, armpits, under the jaw, between the legs darken the fur roots)
#   Check: per-clip deviation of the deformed HD surface from the deformed low-poly surface (mm), inverted faces
import numpy as np
from mathutils.bvhtree import BVHTree
from mathutils import kdtree

STAGES = os.environ.get("STAGES", "ingest,warp,ears,weights,fur,furmesh,ao,verify,export").split(",")
SRC = "sources/tiger/WhiteTigerHD/tripo_convert_edcb4360-f1c0-469c-ad04-d3c599e77d26.fbx"
LO, RIG, HD = "SM_WhiteTiger", "WhiteTiger_Rig", "SM_WhiteTigerHD"
MAT = "M_WhiteTigerHD"
YAW = -90.0                                   # same as the low-poly ingest: head toward -Y
SIGMAS = (0.25, 0.18, 0.12, 0.08, 0.055, 0.04)   # warp smoothing radii (m), coarse -> fine. Stops at 4 cm: finer levels
                                              # imprinted the low-poly facets (shoulder/neck junction folded 4-5 cm in);
                                              # at the portal swap (4-5 m, under the rim glow) 1-3 cm is invisible
ITERS = 5                                     # iterations per radius
NORMAL_DOT = 0.35                             # correspondences need agreeing normals (keeps legs / tail apart)
# The head is never warped: it is one RIGID copy of the HD head (rotation + translation, fitted onto the low-poly head)
# and enters the body warp as a constraint, so the neck interpolates smoothly between the pinned head and the body.
HEAD_T = (-0.05, 0.02)                        # weights: distance along the head bone from its root (m) where the
                                              # transferred body weights ramp to 100 % head bone (no face stretch)
HEAD_PIN_T = (-0.12, -0.04)                   # geometry: same axis; beyond the second value the vertex is exactly the
                                              # rigid head, between the values its pull on the warp field ramps in
HEAD_FIT_T = (0.03, 0.06)                     # skull region used for the rigid fit (both meshes, in the low-poly frame)
HEAD_V = (-0.24, -0.12)                       # all head regions: height relative to the head bone axis (m, up = +):
                                              # chin -0.08, throat -0.25, paws -0.8; below the first value body, above
                                              # the second head (the axis tilts down: without this the paws and the
                                              # throat counted as "head"; a radial limit folded the throat)
LEGS = ("FL", "FR", "BL", "BR")             # leg chains of WhiteTiger_Rig (upper/lower/foot/toe_<leg>)
X0 = 0.02                                     # body centre line (x), as in the low-poly build
LEG_Z = 0.35                                  # below this the HD mesh splits into 4 legs + tail (connectivity); each leg
LEG_BLEND = 0.22                              # swings rigidly about its shoulder/hip onto the low-poly leg (the hind stance
                                              # is mirrored between the models), blending into the body over this height
NECK_V = (-0.30, -0.19)                       # neck bend: below the neck line by more than this is chest, not throat
FEET_Z = (0.05, 0.10)                         # paws (rest height, m) take only the coarse warp levels ...
FEET_MIN_SIGMA = 0.04                         # ... (>= this): the fine levels folded the HD toes onto the blocky paws
PIN_WEIGHT = 4.0                              # head constraint weight vs one surface correspondence
HD_EARS = {"ear_L": "earHD_L", "ear_R": "earHD_R"}   # the HD ears sit ~5 cm behind the low-poly ear bones: own bones
                                              # at the HD ear bases (same rest orientation, child of head) whose keys
                                              # copy the low-poly ear curves, so they flick about their own hinge
FUR_FLOW_BODY = (0.0, 1.0, -0.35)             # growth direction: tailward and a little down (torso, neck, head)
FUR_FLOW_LEG = (0.0, 0.25, -1.0)              # legs: down
FUR_THIN = 0.004                              # inward ray hit closer than this (m) = thin strand (whiskers): no fur
FUR_SAT = 0.30                                # base-colour saturation above this on the head = eyes / nose / lips: no fur
FUR_EYE_R = 0.020                             # no-fur sphere around each detected eye (m)
WET = {"eye": (0.016, 0.92), "nose": (0.024, 0.55)}   # radius (m) around the detected eyes / nose tip, smoothness
FUR_LEN = {"face": 0.45, "lips": 0.0, "ears": 0.6, "ruff": 1.45, "paw": 0.45, "pad": 0.05, "belly": 1.25, "tail": 0.85}
EAR_U = (-0.005, 0.035)                       # ear weight ramp: height along the ear bone (m) ...
EAR_R = (0.045, 0.065)                        # ... and radial falloff from the ear bone axis (m)
TEX = 4096
FPS = 30                                      # = SK_WhiteTiger's takes
FUR_MESH = "SM_WhiteTigerHD_Fur"; FUR_MESH_TRIS = 22000
AO_DISTANCE = 0.12; AO_SAMPLES = 64
OUT_BLEND = os.path.join(ROOT, "blender", "assets", "WhiteTigerHD", "WhiteTigerHD.blend")
OUT = os.path.join(ROOT, "blender", "export", "Characters", "WhiteTigerHD")
CLIPS = {"Idle": 150, "Run": None, "Pounce": None, "Land": None}

info = {}
FEATURES = {}                                  # eyes / nose tip found by the fur stage, used for the wet smoothness
lo = bpy.data.objects[LO]
arm = bpy.data.objects[RIG]
coll_ = lo.users_collection[0]


def deselect():
    for o in bpy.context.scene.objects: o.select_set(False)


def mesh_arrays(me):
    me.calc_loop_triangles()
    v = np.empty(len(me.vertices) * 3, np.float64); me.vertices.foreach_get("co", v)
    t = np.empty(len(me.loop_triangles) * 3, np.int64); me.loop_triangles.foreach_get("vertices", t)
    return v.reshape(-1, 3), t.reshape(-1, 3)


def vert_normals(V, T):
    fn = np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]])
    n = np.zeros_like(V)
    for k in range(3): np.add.at(n, T[:, k], fn)
    return n / (np.linalg.norm(n, axis=1, keepdims=True) + 1e-12)


def stats(d):
    return {"mean_mm": round(float(d.mean()) * 1000, 1), "p95_mm": round(float(np.percentile(d, 95)) * 1000, 1),
            "max_mm": round(float(d.max()) * 1000, 1)}


# ------------------------------------------------------------------ ingest
def ingest():
    old = bpy.data.objects.get(HD)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, SRC), use_custom_normals=False)
    new = [o for o in bpy.data.objects if o not in before]
    ob = next(o for o in new if o.type == 'MESH')
    for o in new:
        if o is not ob: bpy.data.objects.remove(o, do_unlink=True)
    for c in list(ob.users_collection): c.objects.unlink(ob)
    coll_.objects.link(ob)
    ob.name = ob.data.name = HD
    # bake the import transform + yaw into the vertices
    yaw = Matrix.Rotation(np.radians(YAW), 4, 'Z')
    ob.data.transform(yaw @ ob.matrix_world); ob.matrix_world = Matrix.Identity(4)
    # weld UV-seam splits (Tripo splits shells at seams) so normals and weights stay continuous
    bm = bmesh.new(); bm.from_mesh(ob.data)
    n0 = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    info["welded"] = n0 - len(bm.verts)
    ngons = [f for f in bm.faces if len(f.verts) > 4]                # tangents (fur flow) need tris/quads
    bmesh.ops.triangulate(bm, faces=ngons); info["ngons_triangulated"] = len(ngons)
    bm.to_mesh(ob.data); bm.free()
    ob.data.shade_smooth()
    # bbox normalisation onto the low-poly rest mesh: height scale, bottom + centre
    L, _ = mesh_arrays(lo.data); H, _ = mesh_arrays(ob.data)
    s = np.ptp(L[:, 2]) / np.ptp(H[:, 2])
    cl = (L.min(0) + L.max(0)) / 2; ch = (H.min(0) + H.max(0)) / 2; cl[2] = L[:, 2].min(); ch[2] = H[:, 2].min()
    H = (H - ch) * s + cl
    ob.data.vertices.foreach_set("co", H.ravel()); ob.data.update()
    # material: one M_WhiteTigerHD with the source's maps (images stay in the file until export writes PNGs)
    src_mat = ob.material_slots[0].material
    imgs = {n.image.name: n.image for n in src_mat.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image}
    info["source_images"] = {k: list(v.size) for k, v in imgs.items()}
    info["source_links"] = sorted({f"{l.from_node.type}:{getattr(l.from_node, 'image', None) and l.from_node.image.name}->{l.to_node.type}.{l.to_socket.name}"
                                   for l in src_mat.node_tree.links})
    src_mat.name = MAT
    info["hd_tris"] = len(ob.data.polygons) and len(mesh_arrays(ob.data)[1])
    info["hd_verts"] = len(ob.data.vertices)
    return ob


def bone_pts(name):
    b = arm.data.bones[name]
    return np.array(arm.matrix_world @ b.head_local), np.array(arm.matrix_world @ b.tail_local)


def smooth(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)


def rot_about(P, c, R, w, t=None):
    """Rotate points P about pivot c by the fraction w (per point) of rotation R (axis-angle slerp), plus w * t."""
    ang = np.arccos(np.clip((np.trace(R) - 1) / 2, -1, 1))
    if ang < 1e-8: out = P.copy()
    else:
        k = np.array([R[2, 1] - R[1, 2], R[0, 2] - R[2, 0], R[1, 0] - R[0, 1]]) / (2 * np.sin(ang))
        v = P - c; phi = (w * ang)[:, None]
        out = c + v * np.cos(phi) + np.cross(k, v) * np.sin(phi) + np.outer(v @ k, k) * (1 - np.cos(phi))
    return out if t is None else out + w[:, None] * t


def kabsch(X, Y, center=True):
    mx, my = (X.mean(0), Y.mean(0)) if center else (np.zeros(3), np.zeros(3))
    U, _, Vt = np.linalg.svd((X - mx).T @ (Y - my))
    return Vt.T @ np.diag([1, 1, np.sign(np.linalg.det(Vt.T @ U.T))]) @ U.T, mx, my


def head_s(P, ramp=HEAD_T):
    """0 = body (warped, transferred weights) .. 1 = rigid head, by distance along the head bone."""
    h, t = bone_pts("head"); ax = (t - h) / np.linalg.norm(t - h)
    up = np.cross(ax, (1.0, 0.0, 0.0)); up /= np.linalg.norm(up)
    if up[2] < 0: up = -up
    return smooth((P - h) @ ax, *ramp) * smooth((P - h) @ up, *HEAD_V)


def ear_w(P, name):
    h, t = bone_pts(name); ax = (t - h) / np.linalg.norm(t - h)
    u = (P - h) @ ax; r = np.linalg.norm((P - h) - np.outer(u, ax), axis=1)
    return smooth(u, *EAR_U) * (1 - smooth(r, *EAR_R))


# ------------------------------------------------------------------ warp
class Grid:
    """Normalised Gaussian convolution of scattered vectors on a regular grid (splat -> blur -> divide -> sample)."""

    def __init__(self, lo_, hi_, sigma):
        self.cell = sigma / 2.0
        self.o = lo_ - 3 * sigma
        self.n = np.ceil((hi_ + 3 * sigma - self.o) / self.cell).astype(int) + 2
        r = int(np.ceil(3 * sigma / self.cell))
        x = np.arange(-r, r + 1) * self.cell
        self.k = np.exp(-0.5 * (x / sigma) ** 2); self.k /= self.k.sum()

    def _corners(self, P):
        g = (P - self.o) / self.cell
        i0 = np.clip(np.floor(g).astype(int), 0, self.n - 2); f = g - i0
        for dx in (0, 1):
            for dy in (0, 1):
                for dz in (0, 1):
                    w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                    yield (i0[:, 0] + dx, i0[:, 1] + dy, i0[:, 2] + dz), w

    def _blur(self, a):
        r = len(self.k) // 2
        for ax in range(3):
            pad = [(0, 0)] * a.ndim; pad[ax] = (r, r)
            p = np.pad(a, pad); out = np.zeros_like(a)
            for i, w in enumerate(self.k):
                sl = [slice(None)] * a.ndim; sl[ax] = slice(i, i + a.shape[ax]); out += w * p[tuple(sl)]
            a = out
        return a

    def field(self, P, D, W, Q):
        acc = np.zeros(tuple(self.n) + (4,), np.float32)
        for idx, w in self._corners(P):
            np.add.at(acc, idx, np.c_[D * (W * w)[:, None], W * w].astype(np.float32))
        acc = self._blur(acc)
        out = np.zeros((len(Q), 4))
        for idx, w in self._corners(Q): out += acc[idx] * w[:, None]
        return out[:, :3] / np.maximum(out[:, 3:], 1e-6)


def warp(ob):
    L, LT = mesh_arrays(lo.data); LN = vert_normals(L, LT)
    bvh_lo = BVHTree.FromPolygons(L.tolist(), LT.tolist())
    # dense samples on the low-poly surface (vertices + face points) for the backward pull
    rng = np.random.default_rng(3)
    a = rng.random((len(LT) * 2, 2)); flip = a.sum(1) > 1; a[flip] = 1 - a[flip]
    ft = np.repeat(np.arange(len(LT)), 2)
    S = L[LT[ft, 0]] + a[:, :1] * (L[LT[ft, 1]] - L[LT[ft, 0]]) + a[:, 1:] * (L[LT[ft, 2]] - L[LT[ft, 0]])
    fnl = np.cross(L[LT[:, 1]] - L[LT[:, 0]], L[LT[:, 2]] - L[LT[:, 0]]); fnl /= np.linalg.norm(fnl, axis=1, keepdims=True) + 1e-12
    S = np.r_[L, S]; SN = np.r_[LN, fnl[ft]]

    V0, T = mesh_arrays(ob.data)                 # ingest result: a similarity copy of the source
    N0 = vert_normals(V0, T)
    fn0 = np.cross(V0[T[:, 1]] - V0[T[:, 0]], V0[T[:, 2]] - V0[T[:, 0]])

    # ---- 1. rigid head fit (R, t; scale stays the ingest's): trimmed bidirectional ICP on the skull, the region
    #         re-selected on the current fit so both meshes use the same anatomical part
    sk_lo = head_s(S, HEAD_FIT_T) > 0.99
    R_h, t_h = np.eye(3), np.zeros(3)
    c_lo = S[sk_lo].mean(0)
    for it in range(40):
        Hc = V0 @ R_h.T + t_h
        hv = np.where(head_s(Hc, HEAD_FIT_T) > 0.99)[0]
        if it == 0: t_h = t_h + (c_lo - Hc[hv].mean(0)); continue          # start: skull centroids together
        Hn = N0[hv] @ R_h.T
        hits = [bvh_lo.find_nearest(p) for p in Hc[hv]]
        q = np.array([h[0] for h in hits]); dq = np.linalg.norm(q - Hc[hv], axis=1)
        okf = (np.einsum('ij,ij->i', Hn, np.array([h[1] for h in hits])) > NORMAL_DOT) & (dq < np.percentile(dq, 80))
        kd = kdtree.KDTree(len(hv))
        for i, p in enumerate(Hc[hv]): kd.insert(p, i)
        kd.balance()
        nb = np.array([kd.find(p)[1] for p in S[sk_lo]]); db = np.linalg.norm(S[sk_lo] - Hc[hv][nb], axis=1)
        okb = (np.einsum('ij,ij->i', Hn[nb], SN[sk_lo]) > NORMAL_DOT) & (db < np.percentile(db, 80))
        X = np.r_[Hc[hv][okf], Hc[hv][nb[okb]]]; Y = np.r_[q[okf], S[sk_lo][okb]]
        mx, my = X.mean(0), Y.mean(0); U, _, Vt = np.linalg.svd((X - mx).T @ (Y - my))
        R = Vt.T @ np.diag([1, 1, np.sign(np.linalg.det(Vt.T @ U.T))]) @ U.T
        R_h = R @ R_h; t_h = R @ (t_h - mx) + my
    Hrig = V0 @ R_h.T + t_h
    d = np.array([bvh_lo.find_nearest(p)[3] for p in Hrig[hv]])
    info["head_fit"] = {"skull_verts": int(len(hv)), "rot_deg": round(float(np.degrees(np.arccos(min(1, (np.trace(R_h) - 1) / 2)))), 2),
                        "t_mm": (t_h * 1000).round(1).tolist(), "skull_to_lo": stats(d)}
    pin = head_s(Hrig, HEAD_PIN_T)               # 1 = exactly the rigid head, ramps to 0 on the upper neck

    # ---- 2. legs: connected pieces below LEG_Z, matched to the low-poly legs, each swung rigidly about its pivot
    lab_l = np.zeros(len(L), int)                # low-poly part labels from its weights: 0 body, 1..4 legs
    gi = {g.index: g.name for g in lo.vertex_groups}
    for v in lo.data.vertices:
        if not v.groups: continue
        n = gi[max(v.groups, key=lambda g: g.weight).group]
        for k, leg in enumerate(LEGS):
            if n.endswith("_" + leg) and n.split("_")[0] in ("upper", "lower", "foot", "toe"): lab_l[v.index] = k + 1
    lab_tri = lab_l[LT[:, 0]]; lab_S = np.r_[lab_l, lab_tri[ft]]
    below = V0[:, 2] < LEG_Z; par = np.arange(len(V0))
    def find(a):
        while par[a] != a: par[a] = par[par[a]]; a = par[a]
        return a
    for t in T[below[T].all(1)]:
        for a_, b_ in ((t[0], t[1]), (t[1], t[2])):
            ra, rb = find(a_), find(b_)
            if ra != rb: par[ra] = rb
    bi = np.where(below)[0]; roots = np.array([find(i) for i in bi])
    m_leg = np.zeros(len(V0)); lab_h = np.zeros(len(V0), int); legs_info = {}
    Vl = V0.copy()
    pieces = {}
    for r in np.unique(roots):
        idx = bi[roots == r]
        if len(idx) < 500: continue
        cen = V0[idx].mean(0)
        if cen[1] > 0.7: continue                                       # tail tip
        pieces[("F" if cen[1] < 0 else "B") + ("L" if cen[0] > X0 else "R")] = idx
    # membership above the pieces: each vertex belongs to its NEAREST leg top only (overlapping ramps folded the groin),
    # fading with height and with horizontal distance from that leg's top ring
    up_ = np.where((V0[:, 2] >= LEG_Z) & (V0[:, 2] < LEG_Z + LEG_BLEND))[0]
    dist_to = {}
    for leg, idx in pieces.items():
        top = V0[idx][V0[idx][:, 2] > LEG_Z - 0.03]
        kd = kdtree.KDTree(len(top))
        for i, p in enumerate(top): kd.insert((p[0], p[1], 0.0), i)
        kd.balance()
        dist_to[leg] = np.array([kd.find((V0[i, 0], V0[i, 1], 0.0))[2] for i in up_])
    order = list(pieces); nearest = np.argmin(np.array([dist_to[l] for l in order]), axis=0)
    for li, leg in enumerate(order):
        idx = pieces[leg]; k = LEGS.index(leg) + 1
        tgt = L[(lab_l == k) & (L[:, 2] < LEG_Z)]
        piv = np.array(arm.matrix_world @ arm.data.bones[f"upper_{leg}"].head_local)
        m = np.zeros(len(V0)); m[idx] = 1
        mine = nearest == li
        m[up_[mine]] = (1 - smooth(V0[up_[mine], 2], LEG_Z, LEG_Z + LEG_BLEND)) * (1 - smooth(dist_to[leg][mine], 0.03, 0.10))
        # pure rotation about the pivot (a translation would shear the thigh in the blend): ICP, bidirectional
        R = np.eye(3); kdt = kdtree.KDTree(len(tgt))
        for i, p in enumerate(tgt): kdt.insert(p, i)
        kdt.balance()
        for it in range(30):
            X = rot_about(V0[idx], piv, R, np.ones(len(idx)))
            fw = np.array([kdt.find(p)[1] for p in X])
            kdx = kdtree.KDTree(len(X))
            for i, p in enumerate(X): kdx.insert(p, i)
            kdx.balance()
            bw = np.array([kdx.find(p)[1] for p in tgt])
            Rd, _, _ = kabsch(np.r_[X, X[bw]] - piv, np.r_[tgt[fw], tgt] - piv, center=False)
            R = Rd @ R
        # keep the swing, drop the twist about the leg's own axis (unconstrained by a round leg: it rolled the stripes)
        a_ = V0[idx][V0[idx][:, 2] < 0.08].mean(0) - piv; a_ /= np.linalg.norm(a_); b_ = R @ a_
        ax_ = np.cross(a_, b_); sn = np.linalg.norm(ax_); cs = a_ @ b_
        if sn > 1e-9:
            ax_ /= sn; K = np.array([[0, -ax_[2], ax_[1]], [ax_[2], 0, -ax_[0]], [-ax_[1], ax_[0], 0]])
            R = np.eye(3) + sn * K + (1 - cs) * K @ K
        else: R = np.eye(3)
        X = rot_about(V0[idx], piv, R, np.ones(len(idx)))
        d = np.array([kdt.find(p)[2] for p in X])
        legs_info[leg] = {"verts": int(len(idx)), "swing_deg": round(float(np.degrees(np.arccos(np.clip((np.trace(R) - 1) / 2, -1, 1)))), 1),
                          "paw_shift_mm": ((X[X[:, 2] < 0.08].mean(0) - V0[idx][V0[idx][:, 2] < 0.08].mean(0)) * 1000).round(0).tolist(),
                          "fit_mean_mm": round(float(d.mean()) * 1000, 1)}
        sel = m > 0
        Vl[sel] = rot_about(V0[sel], piv, R, m[sel])
        m_leg = np.maximum(m_leg, m); lab_h[m > 0.5] = k
    info["legs"] = legs_info

    # ---- 3. neck: bend from the neck base to the rigid head (rotation interpolated along the neck, no stretch)
    c0 = np.array(arm.matrix_world @ arm.data.bones["neck1"].head_local)
    h0 = np.array(arm.matrix_world @ arm.data.bones["head"].head_local)
    tN = R_h @ c0 + t_h - c0                                          # rigid head map = rotation about c0 + tN
    dN = h0 - c0; upN = np.cross(dN, (1.0, 0.0, 0.0)); upN /= np.linalg.norm(upN)
    if upN[2] < 0: upN = -upN
    wN = smooth((V0 - c0) @ dN / (dN @ dN), 0.0, 1.0) * smooth((V0 - c0) @ upN, *NECK_V)
    wN = np.maximum(wN, pin)
    V = rot_about(Vl, c0, R_h, wN, tN)
    V[pin >= 0.999] = Hrig[pin >= 0.999]
    Vb = V0                                                            # rest heights for the feet rule

    # ---- 4. smooth warp of everything else, the head as a constraint, legs only matched to their own leg
    def correspond(V, maxd):
        N = vert_normals(V, T)
        free = pin < 0.5
        trans = (m_leg > 0.1) & (m_leg < 0.9)                         # leg tops: any part may match
        hits = [bvh_lo.find_nearest(p) for p in V]
        q = np.array([h[0] for h in hits]); qn = np.array([h[1] for h in hits]); qi = np.array([h[2] for h in hits])
        dv = q - V; dist = np.linalg.norm(dv, axis=1)
        ok = (np.einsum('ij,ij->i', N, qn) > NORMAL_DOT) & (dist < maxd) & free & ((lab_tri[qi] == lab_h) | trans)
        kd = kdtree.KDTree(len(V))
        for i, p in enumerate(V): kd.insert(p, i)
        kd.balance()
        nb = np.array([kd.find(p)[1] for p in S])
        db = S - V[nb]; distb = np.linalg.norm(db, axis=1)
        okb = (np.einsum('ij,ij->i', N[nb], SN) > NORMAL_DOT) & (distb < maxd) & free[nb] & ~sk_lo & ((lab_S == lab_h[nb]) | trans[nb])
        pv = np.where(pin > 0.01)[0]
        P = np.r_[V[ok], V[nb[okb]], V[pv]]; D = np.r_[dv[ok], db[okb], Hrig[pv] - V[pv]]
        W = np.r_[np.full(ok.sum(), 1.0), np.full(okb.sum(), len(V) / max(1, len(S))), PIN_WEIGHT * pin[pv]]
        return P, D, W, dist[free], distb[free[nb]], ok.sum() / max(1, free.sum())

    for sg in SIGMAS:
        for it in range(ITERS):
            P, D, W, df, db, okf = correspond(V, max(0.08, 4 * sg))
            g = Grid(np.minimum(V.min(0), L.min(0)), np.maximum(V.max(0), L.max(0)), sg)
            step = g.field(P, D, W, V)
            if sg < FEET_MIN_SIGMA: step *= smooth(Vb[:, 2], *FEET_Z)[:, None]
            V = V + step
            V[pin >= 0.999] = Hrig[pin >= 0.999]
        info.setdefault("warp", []).append({"sigma_cm": round(sg * 100, 1), "hd_to_lo_mean_mm": round(float(df.mean()) * 1000, 1),
                                            "lo_to_hd_mean_mm": round(float(db.mean()) * 1000, 1), "fwd_ok": round(float(okf), 3)})
    _, _, _, df, db, _ = correspond(V, 1.0)
    info["after_warp"] = {"body_hd_to_lo": stats(df), "body_lo_to_hd": stats(db)}
    fn1 = np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]])
    info["flipped_faces"] = int((np.einsum('ij,ij->i', fn0, fn1) < 0).sum())
    info["displacement"] = stats(np.linalg.norm(V - V0, axis=1))
    info["rigid_head_verts"] = int((pin >= 0.999).sum())
    ob.data.vertices.foreach_set("co", V.ravel()); ob.data.update()


# ------------------------------------------------------------------ ears
def channel_containers(act):
    if hasattr(act, "fcurves"): return [act.fcurves]
    return [cb.fcurves for layer in act.layers for strip in layer.strips for cb in strip.channelbags]


def hd_ears(ob):
    """earHD_L/R: bones at the HD ear bases (offset = HD flap centroid - low-poly flap centroid), animated by copies of
    the low-poly ear curves in every action."""
    H, _ = mesh_arrays(ob.data); L, _ = mesh_arrays(lo.data)
    def flap(V, sg):
        sel = V[(V[:, 1] < -0.7) & (sg * V[:, 0] > 0.04)]; top = sel[:, 2].max()
        return sel[sel[:, 2] > top - 0.07].mean(0)
    deselect(); bpy.context.view_layer.objects.active = arm; arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); eb = arm.data.edit_bones
    offs = {}
    for src, dst in HD_EARS.items():
        off = flap(H, 1 if src.endswith("L") else -1) - flap(L, 1 if src.endswith("L") else -1)
        if dst in eb: eb.remove(eb[dst])
        b = eb.new(dst); b.head = eb[src].head + Vector(off); b.tail = eb[src].tail + Vector(off); b.roll = eb[src].roll
        b.parent = eb["head"]; b.use_deform = True; offs[dst] = (off * 1000).round(1).tolist()
    bpy.ops.object.mode_set(mode='OBJECT')
    copied = 0
    for act in bpy.data.actions:
        for fcs in channel_containers(act):
            for fc in list(fcs):
                for src, dst in HD_EARS.items():
                    if not fc.data_path.startswith(f'pose.bones["{src}"]'): continue
                    path = fc.data_path.replace(f'["{src}"]', f'["{dst}"]')
                    old = fcs.find(path, index=fc.array_index)
                    if old: fcs.remove(old)
                    nf = fcs.new(path, index=fc.array_index)
                    nf.keyframe_points.add(len(fc.keyframe_points))
                    co = np.empty(len(fc.keyframe_points) * 2, np.float32); fc.keyframe_points.foreach_get("co", co)
                    nf.keyframe_points.foreach_set("co", co)
                    for k, kk in zip(fc.keyframe_points, nf.keyframe_points):
                        kk.interpolation = k.interpolation; kk.handle_left = k.handle_left; kk.handle_right = k.handle_right
                    nf.update(); copied += 1
    info["hd_ears"] = {"offset_mm": offs, "curves_copied": copied}


# ------------------------------------------------------------------ weights
def weights(ob):
    ob.vertex_groups.clear()
    for m in list(ob.modifiers): ob.modifiers.remove(m)
    ob.parent = None; ob.matrix_world = Matrix.Identity(4)
    arm.data.pose_position = 'REST'; bpy.context.view_layer.update()
    for g in lo.vertex_groups: ob.vertex_groups.new(name=g.name)
    for dst in HD_EARS.values(): ob.vertex_groups.new(name=dst)
    # source must be the rest shape: the low-poly's armature modifier is off while transferring
    lo_mods = [(m, m.show_viewport) for m in lo.modifiers]
    for m, _ in lo_mods: m.show_viewport = False
    dt = ob.modifiers.new("DT", 'DATA_TRANSFER'); dt.object = lo
    dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
    dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=dt.name)
    for m, v in lo_mods: m.show_viewport = v
    # head: the transferred weights leaked the low-poly ear groups over the HD forehead/cheeks (the ears sit elsewhere)
    # and mixed neck into the face. Rebuild them: body weights (ear groups folded into head) -> 100 % head along the
    # head ramp, ears only on the HD's own ear flaps
    V, _ = mesh_arrays(ob.data)
    hsv = head_s(V); eL = ear_w(V, HD_EARS["ear_L"]) * hsv; eR = ear_w(V, HD_EARS["ear_R"]) * hsv
    names = [g.name for g in ob.vertex_groups]; W = np.zeros((len(V), len(names)))
    for v in ob.data.vertices:
        for g in v.groups: W[v.index, g.group] = g.weight
    ih, il, ir = names.index("head"), names.index("ear_L"), names.index("ear_R")
    W[:, ih] += W[:, il] + W[:, ir]; W[:, il] = W[:, ir] = 0
    W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
    jl, jr = names.index(HD_EARS["ear_L"]), names.index(HD_EARS["ear_R"])
    rigid = np.zeros_like(W); rigid[:, ih] = 1 - eL - eR; rigid[:, jl] = eL; rigid[:, jr] = eR
    W = (1 - hsv)[:, None] * W + hsv[:, None] * rigid
    for j, n in enumerate(names):
        g = ob.vertex_groups[n]; g.remove(range(len(V)))
        nz = np.where(W[:, j] > 1e-4)[0]
        for i in nz: g.add([int(i)], float(W[i, j]), 'REPLACE')
    info["head_rigid_verts"] = int((hsv > 0.999).sum()); info["ear_verts"] = {"L": int((eL > 0.5).sum()), "R": int((eR > 0.5).sum())}
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
        bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    info["unweighted"] = sum(1 for v in ob.data.vertices if not any(g.weight > 1e-3 for g in v.groups))
    info["vgroups"] = len(ob.vertex_groups)
    ob.parent = arm
    am = ob.modifiers.new("Armature", 'ARMATURE'); am.object = arm
    arm.data.pose_position = 'POSE'


# ------------------------------------------------------------------ fur attributes
def fur(ob):
    me = ob.data
    uv0 = me.uv_layers[0].name
    for n in ("FurFlow", "FurData"):
        if me.uv_layers.get(n): me.uv_layers.remove(me.uv_layers[n])
    V, T = mesh_arrays(me); nv = len(V)
    N = np.empty(nv * 3); me.vertices.foreach_get("normal", N); N = N.reshape(-1, 3)
    # dominant-bone flow, blended by the skin weights
    names = {g.index: g.name for g in ob.vertex_groups}
    def bone_dir(n):
        h, t = bone_pts(n); d = t - h; return d / np.linalg.norm(d)
    cache = {}
    def flow_of(n):
        if n not in cache:
            if n.startswith("tail") or n.startswith("ear"): cache[n] = bone_dir(n)
            elif any(n.startswith(p + "_") for p in ("upper", "lower", "foot", "toe")): cache[n] = np.array(FUR_FLOW_LEG) / np.linalg.norm(FUR_FLOW_LEG)
            else: cache[n] = np.array(FUR_FLOW_BODY) / np.linalg.norm(FUR_FLOW_BODY)
        return cache[n]
    F = np.zeros((nv, 3)); legw = np.zeros(nv); tailw = np.zeros(nv)
    for v in me.vertices:
        for g in v.groups:
            n = names[g.group]; F[v.index] += g.weight * flow_of(n)
            if any(n.startswith(p + "_") for p in ("upper", "lower", "foot", "toe")): legw[v.index] += g.weight
            if n.startswith("tail"): tailw[v.index] += g.weight
    F -= N * np.einsum('ij,ij->i', F, N)[:, None]
    F /= np.maximum(np.linalg.norm(F, axis=1, keepdims=True), 1e-6)
    # islands: everything except the body (eyes, teeth, whiskers, claws) gets no fur
    par = np.arange(nv)
    def find(a):
        while par[a] != a: par[a] = par[par[a]]; a = par[a]
        return a
    for t in T:
        for a_, b_ in ((t[0], t[1]), (t[1], t[2])):
            ra, rb = find(a_), find(b_)
            if ra != rb: par[ra] = rb
    roots = np.array([find(i) for i in range(nv)]); u, cnt = np.unique(roots, return_counts=True)
    body = roots == u[np.argmax(cnt)]
    # length mask by region (rest pose)
    L = np.ones(nv)
    hs = head_s(V)
    nose = V[body & (hs > 0.5)][np.argmin(V[body & (hs > 0.5)][:, 1])]
    L = np.where(hs > 0.5, FUR_LEN["face"], L)
    cheek = (hs > 0.3) & (np.abs(V[:, 0] - X0) > 0.085) & (V[:, 1] > nose[1] + 0.12) & (V[:, 2] < nose[2] + 0.06)
    L = np.where(cheek, FUR_LEN["ruff"], L)
    for e in HD_EARS.values(): L = np.where(ear_w(V, e) > 0.3, FUR_LEN["ears"], L)
    lips = (hs > 0.5) & (V[:, 1] < nose[1] + 0.07) & (V[:, 2] < nose[2] - 0.015)
    L = np.where(lips, FUR_LEN["lips"], L)
    belly = (legw < 0.5) & (tailw < 0.5) & (hs < 0.3) & (N[:, 2] < -0.55) & (V[:, 2] > 0.3)
    L = np.where(belly, FUR_LEN["belly"], L)
    L = np.where(tailw > 0.5, FUR_LEN["tail"], L)
    L = np.where(V[:, 2] < 0.07, FUR_LEN["paw"], L)
    L = np.where((V[:, 2] < 0.035) & (N[:, 2] < -0.4), FUR_LEN["pad"], L)
    dn = np.linalg.norm(V - nose, axis=1); L *= smooth(dn, 0.022, 0.045)
    L[~body] = 0.0
    # thin strands (whiskers are fused into the body mesh): inward ray from just inside the surface
    bvh = BVHTree.FromPolygons(V.tolist(), T.tolist())
    thin = np.zeros(nv, bool)
    for i in np.where(hs > 0.3)[0]:
        hit = bvh.ray_cast(Vector(V[i] - N[i] * 1e-4), Vector(-N[i]), FUR_THIN)
        thin[i] = hit[0] is not None
    # saturated base colour on the head: amber eyes, pink nose / lips
    m = ob.material_slots[0].material
    img = next(l.from_node.image for l in m.node_tree.links if l.from_node.type == 'TEX_IMAGE' and l.to_socket.name == "Base Color")
    w_, h_ = img.size; px = np.empty(w_ * h_ * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(h_, w_, 4)[..., :3]
    uvl = np.empty(len(me.loops) * 2); me.uv_layers[uv0].data.foreach_get("uv", uvl); uvl = uvl.reshape(-1, 2)
    lvi = np.empty(len(me.loops), np.int64); me.loops.foreach_get("vertex_index", lvi)
    col = px[np.clip((uvl[:, 1] % 1) * h_, 0, h_ - 1).astype(int), np.clip((uvl[:, 0] % 1) * w_, 0, w_ - 1).astype(int)]
    vc = np.zeros((nv, 3)); cn = np.zeros(nv); np.add.at(vc, lvi, col); np.add.at(cn, lvi, 1); vc /= np.maximum(cn, 1)[:, None]
    mx, mn = vc.max(1), vc.min(1); sat = (mx - mn) / np.maximum(mx, 1e-4)
    colored = (hs > 0.5) & (sat > FUR_SAT) & (vc[:, 0] > vc[:, 2] + 0.08)          # warm: amber iris, pink nose
    # eyes: saturated clusters on the head away from the nose -> spheres
    eyes = []
    cand = np.where(colored & (dn > 0.06))[0]
    while len(cand) >= 5:                                              # ~12 amber vertices per eye
        seed = cand[0]; cl = cand[np.linalg.norm(V[cand] - V[seed], axis=1) < 0.03]
        if len(cl) >= 5: eyes.append(V[cl].mean(0))
        cand = np.setdiff1d(cand, cl)
    if not eyes and FEATURES.get("eye"): eyes = FEATURES["eye"]   # decimated fur mesh: too few amber verts, reuse the full mesh's
    for e in eyes: L *= smooth(np.linalg.norm(V - e, axis=1), FUR_EYE_R * 0.7, FUR_EYE_R)
    FEATURES["eye"] = eyes; FEATURES["nose"] = [nose]
    L[thin | colored] = 0.0
    # soften region borders: a few neighbour-average passes (body only)
    E = np.r_[T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]]
    deg = np.bincount(E.ravel(), minlength=nv).astype(float)
    for it in range(6):
        acc = np.zeros(nv); np.add.at(acc, E[:, 0], L[E[:, 1]]); np.add.at(acc, E[:, 1], L[E[:, 0]])
        L = np.where(body, 0.5 * L + 0.5 * acc / np.maximum(deg, 1), 0.0)
    # strand density: UV0 units per metre (per face, averaged to vertices)
    uvd = np.empty(len(me.loops) * 2); me.uv_layers[uv0].data.foreach_get("uv", uvd); uvd = uvd.reshape(-1, 2)
    me.calc_loop_triangles()
    lt = np.array([t.loops[:] for t in me.loop_triangles])
    a3 = np.linalg.norm(np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]]), axis=1) / 2
    a2 = np.abs(np.cross(uvd[lt[:, 1]] - uvd[lt[:, 0]], uvd[lt[:, 2]] - uvd[lt[:, 0]])) / 2
    dens_f = np.sqrt(a2 / np.maximum(a3, 1e-12))
    acc = np.zeros(nv); w = np.zeros(nv)
    for k in range(3): np.add.at(acc, T[:, k], dens_f * a3); np.add.at(w, T[:, k], a3)
    D = acc / np.maximum(w, 1e-12); D[D <= 0] = np.median(D[D > 0])
    # write per loop: flow in the UV0 tangent frame, density + length
    me.calc_tangents(uvmap=uv0)
    nl = len(me.loops)
    tan = np.empty(nl * 3); me.loops.foreach_get("tangent", tan); tan = tan.reshape(-1, 3)
    bit = np.empty(nl * 3); me.loops.foreach_get("bitangent", bit); bit = bit.reshape(-1, 3)
    lv = np.empty(nl, np.int64); me.loops.foreach_get("vertex_index", lv)
    fl = me.uv_layers.new(name="FurFlow"); fd = me.uv_layers.new(name="FurData")
    fx = np.einsum('ij,ij->i', F[lv], tan); fy = np.einsum('ij,ij->i', F[lv], bit)
    fl.data.foreach_set("uv", np.c_[fx, fy].ravel()); fd.data.foreach_set("uv", np.c_[D[lv], L[lv]].ravel())
    me.uv_layers.active = me.uv_layers[uv0]; me.uv_layers[uv0].active_render = True
    me.free_tangents()
    info["fur"] = {"thin_verts": int(thin.sum()), "colored_verts": int(colored.sum()), "eyes": [e.round(3).tolist() for e in eyes],
                   "islands": int(len(u)), "body_verts": int(body.sum()), "no_fur_verts": int((L < 0.02).sum()),
                   "len_mean": round(float(L[body].mean()), 2), "uv_per_m_median": round(float(np.median(D)), 3),
                   "uv_per_m_p5_p95": [round(float(np.percentile(D, 5)), 3), round(float(np.percentile(D, 95)), 3)],
                   "flow_tangent_len_mean": round(float(np.sqrt(fx ** 2 + fy ** 2).mean()), 3), "nose": nose.round(3).tolist()}


# ------------------------------------------------------------------ fur mesh + AO
def furmesh(ob):
    old = bpy.data.objects.get(FUR_MESH)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    fo = ob.copy(); fo.data = ob.data.copy(); fo.name = fo.data.name = FUR_MESH; coll_.objects.link(fo)
    fo.parent = arm
    for m in list(fo.modifiers):
        if m.type != 'ARMATURE': fo.modifiers.remove(m)
    n0 = len(mesh_arrays(fo.data)[1])
    dec = fo.modifiers.new("Decimate", 'DECIMATE'); dec.ratio = FUR_MESH_TRIS / n0; dec.use_collapse_triangulate = True
    with bpy.context.temp_override(object=fo, active_object=fo, selected_objects=[fo]):
        bpy.ops.object.modifier_move_to_index(modifier=dec.name, index=0)
        bpy.ops.object.modifier_apply(modifier=dec.name)
    with bpy.context.temp_override(object=fo, active_object=fo, selected_objects=[fo]):
        bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
        bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    fur(fo)                                        # fur data recomputed on the decimated surface (its own tangents)
    info["fur_mesh"] = {"tris": len(mesh_arrays(fo.data)[1]), "from": n0, "fur": info.pop("fur")}
    return fo


def bake_ao(fo):
    """Cycles AO (rest pose) into the fur mesh's vertex colour: occlusion within AO_DISTANCE darkens the fur roots."""
    sc = bpy.context.scene
    arm.data.pose_position = 'REST'; bpy.context.view_layer.update()
    sc.render.engine = 'CYCLES'; sc.cycles.samples = AO_SAMPLES
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences; prefs.compute_device_type = 'METAL'; prefs.get_devices()
        for d in prefs.devices: d.use = True
        sc.cycles.device = 'GPU'
    except Exception: sc.cycles.device = 'CPU'
    w = sc.world or bpy.data.worlds.new("World"); sc.world = w; w.light_settings.distance = AO_DISTANCE
    me = fo.data
    for a in [a for a in me.color_attributes if a.name == "AO"]: me.color_attributes.remove(a)
    ca = me.color_attributes.new("AO", 'FLOAT_COLOR', 'POINT'); me.color_attributes.active_color = ca
    for o in [o for o in sc.objects if o.type == 'MESH' and o is not fo]: o.hide_render = True   # only its own occlusion
    deselect(); fo.select_set(True); bpy.context.view_layer.objects.active = fo
    import time; t0 = time.time()
    bpy.ops.object.bake(type='AO', target='VERTEX_COLORS')
    for o in sc.objects: o.hide_render = False
    arm.data.pose_position = 'POSE'
    vals = np.empty(len(me.vertices) * 4, np.float32); ca.data.foreach_get("color", vals); ao = vals.reshape(-1, 4)[:, 0]
    info["ao"] = {"seconds": round(time.time() - t0, 1), "device": sc.cycles.device, "mean": round(float(ao.mean()), 3),
                  "p5": round(float(np.percentile(ao, 5)), 3), "dark_lt_0.5_pct": round(float((ao < 0.5).mean()) * 100, 1)}


# ------------------------------------------------------------------ verify
def clip_end(act):
    return int(round(act.frame_range[1]))


def verify(ob):
    """Deform both meshes through every clip (every 2nd frame); distance HD vertex -> low-poly surface."""
    dg = bpy.context.evaluated_depsgraph_get()
    sc = bpy.context.scene
    out = {}
    for name in CLIPS:
        act = bpy.data.actions.get(name)
        if not act: continue
        arm.animation_data.action = act
        means, p95s, maxs = [], [], []
        for f in range(0, clip_end(act) + 1, 2):
            sc.frame_set(f); dg = bpy.context.evaluated_depsgraph_get()
            le = lo.evaluated_get(dg); lm = le.to_mesh()
            Lv, Lt = mesh_arrays(lm); Lv = (np.array(le.matrix_world) @ np.c_[Lv, np.ones(len(Lv))].T).T[:, :3]
            le.to_mesh_clear()
            bvh = BVHTree.FromPolygons(Lv.tolist(), Lt.tolist())
            he = ob.evaluated_get(dg); hm = he.to_mesh()
            Hv, _ = mesh_arrays(hm); Hv = (np.array(he.matrix_world) @ np.c_[Hv, np.ones(len(Hv))].T).T[:, :3]
            he.to_mesh_clear()
            d = np.array([bvh.find_nearest(p)[3] for p in Hv[::4]])
            means.append(d.mean()); p95s.append(np.percentile(d, 95)); maxs.append(d.max())
        out[name] = {"frames": clip_end(act), "mean_mm": round(float(np.mean(means)) * 1000, 1),
                     "p95_mm_worst_frame": round(float(np.max(p95s)) * 1000, 1), "max_mm": round(float(np.max(maxs)) * 1000, 1)}
    arm.animation_data.action = bpy.data.actions["Idle"]; sc.frame_set(0)
    info["deviation_per_clip"] = out


# ------------------------------------------------------------------ export
def save_png(img, path, size, pixels=None):
    im = bpy.data.images.new(os.path.basename(path), size, size, alpha=True, float_buffer=False)
    # colour space before the pixels: changing it afterwards clears the buffer (black PNG)
    im.colorspace_settings.name = img.colorspace_settings.name if img is not None else 'Non-Color'
    if pixels is None:
        src = img.copy(); src.scale(size, size)
        px = np.empty(size * size * 4, np.float32); src.pixels.foreach_get(px); bpy.data.images.remove(src)
    else:
        px = pixels
    im.pixels.foreach_set(px)
    im.filepath_raw = path; im.file_format = 'PNG'; im.save()
    return im


def export(ob):
    tdir = os.path.join(OUT, "Textures"); os.makedirs(tdir, exist_ok=True)
    m = ob.material_slots[0].material
    by_socket = {}
    for l in m.node_tree.links:
        if l.from_node.type == 'TEX_IMAGE' and l.from_node.image:
            # follow through Normal Map / Separate nodes to the BSDF input
            to = l.to_node; sock = l.to_socket.name
            if to.type == 'NORMAL_MAP': sock = "Normal"
            elif to.type in ('SEPARATE_COLOR', 'SEPARATE_RGB'):
                sock = next((ll.to_socket.name for ll in m.node_tree.links if ll.from_node == to), sock)
            by_socket[sock] = l.from_node.image
    info["export_maps"] = {k: v.name for k, v in by_socket.items()}
    bc = by_socket["Base Color"]; nrm = by_socket.get("Normal")
    save_png(bc, os.path.join(tdir, "T_WhiteTigerHD_BC.png"), TEX)
    if nrm: save_png(nrm, os.path.join(tdir, "T_WhiteTigerHD_N.png"), TEX)
    # URP Lit metallic map: R = metallic, A = smoothness (1 - roughness)
    def chan(img):
        if img is None: return None
        c = img.copy(); c.scale(TEX, TEX); px = np.empty(TEX * TEX * 4, np.float32); c.pixels.foreach_get(px); bpy.data.images.remove(c)
        return px.reshape(-1, 4)[:, 0]
    met = chan(by_socket.get("Metallic")); rough = chan(by_socket.get("Roughness"))
    ms = np.zeros((TEX * TEX, 4), np.float32)
    ms[:, 0] = met if met is not None else 0.0
    ms[:, 3] = 1.0 - rough if rough is not None else 0.4
    # wet eyes and nose leather: raise smoothness on their texels (catchlights from the white-room reflection probe)
    me = ob.data; me.calc_loop_triangles()
    Vv = np.array([v.co for v in me.vertices]); uvd = np.empty(len(me.loops) * 2); me.uv_layers[0].data.foreach_get("uv", uvd); uvd = uvd.reshape(-1, 2)
    tris = np.array([t.vertices[:] for t in me.loop_triangles]); tl = np.array([t.loops[:] for t in me.loop_triangles])
    wet = {}
    for kind, (rad, sm) in WET.items():
        n = 0
        for c in FEATURES.get(kind, []):
            sel = np.where((np.linalg.norm(Vv[tris] - c, axis=2) < rad).all(1))[0]
            for k in sel:
                P = uvd[tl[k]] * TEX; x0, y0 = np.floor(P.min(0)).astype(int); x1, y1 = np.ceil(P.max(0)).astype(int)
                xs, ys = np.meshgrid(np.arange(max(x0, 0), min(x1 + 1, TEX)), np.arange(max(y0, 0), min(y1 + 1, TEX)))
                q = np.c_[xs.ravel() + 0.5, ys.ravel() + 0.5]
                a, b, c2 = P; den = (b[1] - c2[1]) * (a[0] - c2[0]) + (c2[0] - b[0]) * (a[1] - c2[1])
                if abs(den) < 1e-9: continue
                l1 = ((b[1] - c2[1]) * (q[:, 0] - c2[0]) + (c2[0] - b[0]) * (q[:, 1] - c2[1])) / den
                l2 = ((c2[1] - a[1]) * (q[:, 0] - c2[0]) + (a[0] - c2[0]) * (q[:, 1] - c2[1])) / den
                inside = (l1 >= -0.02) & (l2 >= -0.02) & (1 - l1 - l2 >= -0.02)
                idx = (q[inside, 1].astype(int)) * TEX + q[inside, 0].astype(int)
                ms[idx, 3] = np.maximum(ms[idx, 3], sm); n += int(inside.sum())
        wet[kind] = n
    info["wet_texels"] = wet
    info["metallic_mean"] = round(float(ms[:, 0].mean()), 3); info["smoothness_mean"] = round(float(ms[:, 3].mean()), 3)
    save_png(None, os.path.join(tdir, "T_WhiteTigerHD_MS.png"), TEX, ms.ravel())
    # takes: identical to SK_WhiteTiger (same rig object, same actions)
    for pb in arm.pose.bones:
        for c in list(pb.constraints): pb.constraints.remove(c)
    arm.animation_data.action = bpy.data.actions["Idle"]
    ob.name = ob.data.name = "SK_WhiteTigerHD"
    fo = bpy.data.objects.get(FUR_MESH)
    deselect(); ob.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
    if fo: fo.name = fo.data.name = "SK_WhiteTigerHD_Fur"; fo.select_set(True)
    s = dict(FBX_SETTINGS, object_types={'ARMATURE', 'MESH'}, use_armature_deform_only=True, add_leaf_bones=False,
             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_step=1.0,
             bake_anim_simplify_factor=0.0, bake_anim_force_startend_keying=True, primary_bone_axis='Y', secondary_bone_axis='X')
    bpy.context.scene.name = "Scene"
    # the source FBX import set the scene to 24 fps: every take came out 25 % longer than SK_WhiteTiger's (30 fps)
    bpy.context.scene.render.fps = FPS; bpy.context.scene.render.fps_base = 1.0
    fn = os.path.join(OUT, "SK_WhiteTigerHD.fbx")
    bpy.ops.export_scene.fbx(filepath=fn, **s)
    ob.data.calc_loop_triangles()
    man = {"name": "SK_WhiteTigerHD", "tris": len(ob.data.loop_triangles), "verts": len(ob.data.vertices), "materials": [m.name],
           "dimensions_xyz": [round(d, 3) for d in ob.dimensions], "rig": "WhiteTiger_Rig (same rest pose as SK_WhiteTiger) + earHD_L/R (children of head, keys copied from ear_L/R)",
           "bones_deform": [b.name for b in arm.data.bones if b.use_deform], "clips": "Idle, Walk, WalkTurnL, WalkTurnR, Run, Pounce, Land (SK_WhiteTiger's + earHD curves; walks with root motion on 'root')",
           "fur_mesh": info.get("fur_mesh", {}).get("tris"),
           "deviation_from_low_poly": info.get("deviation_per_clip"),
           "textures": ["Textures/T_WhiteTigerHD_BC.png", "Textures/T_WhiteTigerHD_N.png", "Textures/T_WhiteTigerHD_MS.png"],
           "source": SRC + " (user-supplied, Tripo HD conversion)", "facing": "Blender -Y = Unity +Z"}
    with open(os.path.join(OUT, "SK_WhiteTigerHD.json"), "w") as fh: json.dump(man, fh, indent=1)
    ob.name = ob.data.name = HD
    if fo: fo.name = fo.data.name = FUR_MESH
    info["export"] = fn; info["tris"] = man["tris"]


os.makedirs(OUT, exist_ok=True)
hd = ingest() if "ingest" in STAGES else bpy.data.objects[HD]
if "warp" in STAGES: warp(hd)
if "ears" in STAGES: hd_ears(hd)
if "weights" in STAGES: weights(hd)
if "fur" in STAGES: fur(hd)
fo_ = furmesh(hd) if "furmesh" in STAGES else bpy.data.objects.get(FUR_MESH)
if "ao" in STAGES and fo_: bake_ao(fo_)
if "verify" in STAGES: verify(hd)
if "export" in STAGES: export(hd)
bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
result = info
