# veg.py: low-poly, flat-shaded vegetation and rock primitives (big faceted leaves, fronds, trunks, blobs, rocks).
# Prepend with BLENDER_LIBS="kit export audit review veg". Everything is built into bmesh in world space (Z up, metres).
# Leaves are single-sided strips: Unity renders them with Render Face = Both.


def _frame(tangent):
    """Side and normal vectors for a spine tangent; side stays horizontal so leaves don't roll."""
    up = Vector((0, 0, 1))
    side = tangent.cross(up)
    if side.length < 1e-4: side = Vector((1, 0, 0))
    side.normalize()
    nrm = side.cross(tangent).normalized()
    return side, nrm


def spine(base, heading, elev, length, droop, segs, twist=0.0, rnd=None):
    """Points along an arching curve: starts at `elev` (rad above horizontal) towards `heading` (rad around Z) and
    bends down by `droop` rad over its length."""
    pts = [Vector(base)]
    step = length / segs
    for i in range(segs):
        t = (i + 0.5) / segs
        a = elev - droop * t
        h = heading + twist * t
        d = Vector((math.cos(h) * math.cos(a), math.sin(h) * math.cos(a), math.sin(a)))
        if rnd: d += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * 0.04
        pts.append(pts[-1] + d.normalized() * step)
    return pts


def leaf_bm(bm, pts, width, fold=0.35, shape=0.8, serrate=0.0, tear=0.0, rnd=None, mat_idx=0, base_w=0.06, roll=0.0):
    """A folded leaf blade along spine pts: midrib ridge with two faceted halves. width = half width at widest.
    shape < 1 = widest near the base (ovate), > 1 = paddle. serrate 0..1 cuts sawtooth pinnae (ferns/palms).
    roll tilts the blade around its spine (rad)."""
    rnd = rnd or random.Random(0)
    n = len(pts) - 1
    mid = [bm.verts.new(p) for p in pts]
    halves = []
    for s in (-1, 1):
        edge = []
        for i, p in enumerate(pts):
            t = i / n
            if i == n:
                edge.append(None); continue
            tg = (pts[min(i + 1, n)] - pts[max(i - 1, 0)]).normalized()
            side, nrm = _frame(tg)
            if roll: side = (Matrix.Rotation(roll, 3, tg) @ side)
            w = width * (math.sin(math.pi * min(1.0, t ** shape)) ** 0.75) if i else base_w * width
            if serrate and 0 < i < n: w *= (1.0 if i % 2 else 1.0 - serrate)
            if tear and rnd.random() < tear: w *= rnd.uniform(0.55, 0.85)
            w *= rnd.uniform(0.92, 1.08)
            q = p + side * s * w - nrm * fold * w + tg * rnd.uniform(-0.15, 0.15) * width * 0.3
            edge.append(bm.verts.new(q))
        halves.append(edge)
    faces = []
    for s, edge in zip((-1, 1), halves):
        for i in range(n):
            a, b = mid[i], mid[i + 1]
            e0, e1 = edge[i], edge[i + 1]
            quad = [a, e0, e1, b] if e1 is not None else [a, e0, b]
            if s > 0: quad.reverse()
            try:
                if len(quad) == 4:
                    faces.append(bm.faces.new([quad[0], quad[1], quad[2]]))
                    faces.append(bm.faces.new([quad[0], quad[2], quad[3]]))
                else:
                    faces.append(bm.faces.new(quad))
            except ValueError:
                pass
    for f in faces: f.material_index = mat_idx
    return faces


def tube_bm(bm, pts, radii, sides=6, rnd=None, mat_idx=0, cap=True, jitter=0.0, phase=0.0):
    """Faceted tube (stems, trunks, branches) along pts with per-point radius."""
    rnd = rnd or random.Random(0)
    rings = []
    prev_side = None
    for i, p in enumerate(pts):
        tg = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        side, nrm = _frame(tg)
        if prev_side is not None:            # keep the ring orientation continuous (no twisting)
            side = (prev_side - tg * prev_side.dot(tg)).normalized(); nrm = side.cross(tg).normalized()
        prev_side = side
        ring = []
        for k in range(sides):
            a = phase + 2 * math.pi * k / sides
            r = radii[i] * (1 + rnd.uniform(-jitter, jitter))
            ring.append(bm.verts.new(p + (side * math.cos(a) + nrm * math.sin(a)) * r))
        rings.append(ring)
    faces = []
    for i in range(len(rings) - 1):
        for k in range(sides):
            a, b = rings[i][k], rings[i][(k + 1) % sides]
            c, d = rings[i + 1][(k + 1) % sides], rings[i + 1][k]
            faces.append(bm.faces.new([a, b, c])); faces.append(bm.faces.new([a, c, d]))
    if cap and radii[-1] > 0.002:
        faces.append(bm.faces.new(list(reversed(rings[-1]))) if False else bm.faces.new(rings[-1]))
    for f in faces: f.material_index = mat_idx
    return faces


def blob_bm(bm, center, size, subdiv=1, noise=0.18, rnd=None, mat_idx=0, flatten_bottom=None):
    """Faceted ico blob (canopy clumps, bushes, rocks). size = (sx, sy, sz) radii."""
    rnd = rnd or random.Random(0)
    tmp = bmesh.new()
    bmesh.ops.create_icosphere(tmp, subdivisions=subdiv, radius=1.0)
    for v in tmp.verts:
        v.co *= 1 + rnd.uniform(-noise, noise)
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        if flatten_bottom is not None and v.co.z < -flatten_bottom * size[2]:
            v.co.z = -flatten_bottom * size[2] + (v.co.z + flatten_bottom * size[2]) * 0.15
        v.co += Vector(center)
    me = bpy.data.meshes.new("_tmp"); tmp.to_mesh(me); tmp.free()
    n0 = len(bm.faces)
    bm.from_mesh(me); bpy.data.meshes.remove(me)
    bm.faces.ensure_lookup_table()
    new = [bm.faces[i] for i in range(n0, len(bm.faces))]
    for f in new: f.material_index = mat_idx
    return new


def emit(name, bm, cname, mat_keys, uv_tiles=None):
    """Finish a bmesh into an object with the MATERIALS keys as slots (box-mapped UVs so validate() stays clean).
    uv_tiles: {material index: metres per UV unit} for slots that need a coarser UV scale (e.g. crowns: the Unity
    lightmapper samples their transmission texture through UV0, and the gap pattern must be metres wide)."""
    box_uv(bm, tile=1.0)
    for idx, tile in (uv_tiles or {}).items():
        box_uv(bm, tile=tile, faces=[f for f in bm.faces if f.material_index == idx])
    return finish(name, bm, cname, [M(k) for k in mat_keys])


# ---------------- plant generators: each returns nothing, builds objects in collection cname ----------------

def plant_bigleaf(cname, seed, n_leaves=7, leaf_len=(1.0, 1.5), leaf_w=0.27, stem_len=(0.6, 1.2), droop=1.9,
                  mats=("leaf", "stem"), fold=0.32, shape=0.75, tear=0.0, prefix="BigLeaf"):
    """Elephant-ear / alocasia clump: leaves on petioles radiating from the centre."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(n_leaves):
        h = 2 * math.pi * i / n_leaves + rnd.uniform(-0.35, 0.35)
        el = rnd.uniform(1.0, 1.35)
        sl = rnd.uniform(*stem_len)
        st = spine((rnd.uniform(-0.05, 0.05), rnd.uniform(-0.05, 0.05), 0), h, el, sl, 0.4, 3, rnd=rnd)
        tube_bm(bm, st, [0.025, 0.02, 0.016, 0.012], sides=4, rnd=rnd, mat_idx=1, cap=False)
        ll = rnd.uniform(*leaf_len)
        lf = spine(st[-1], h + rnd.uniform(-0.2, 0.2), el * 0.7, ll, droop * rnd.uniform(0.75, 1.15), 7, rnd=rnd)
        leaf_bm(bm, lf, leaf_w * ll, fold=fold, shape=shape, tear=tear, rnd=rnd, mat_idx=0, roll=rnd.uniform(-0.25, 0.25))
    emit(f"{prefix}_{seed}", bm, cname, mats)


def plant_banana(cname, seed, height=2.2, n_leaves=6, leaf_len=2.0, mats=("leaf", "stem")):
    """Banana / heliconia-like: pseudostem with long paddle leaves (torn edges)."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    st = spine((0, 0, 0), rnd.uniform(0, 6.28), 1.48, height, 0.08, 4, rnd=rnd)
    tube_bm(bm, st, [0.14, 0.12, 0.1, 0.08, 0.06], sides=7, rnd=rnd, mat_idx=1, jitter=0.08)
    top = st[-1]
    for i in range(n_leaves):
        h = 2 * math.pi * i / n_leaves + rnd.uniform(-0.3, 0.3)
        el = rnd.uniform(0.6, 1.2)
        pet = spine(top - Vector((0, 0, rnd.uniform(0, 0.4))), h, el + 0.2, 0.35, 0.1, 2, rnd=rnd)
        tube_bm(bm, pet, [0.03, 0.025, 0.02], sides=4, rnd=rnd, mat_idx=1, cap=False)
        ll = leaf_len * rnd.uniform(0.75, 1.1)
        lf = spine(pet[-1], h, el, ll, rnd.uniform(1.3, 2.0), 8, rnd=rnd)
        leaf_bm(bm, lf, 0.2 * ll, fold=0.18, shape=1.1, tear=0.35, rnd=rnd, mat_idx=0, roll=rnd.uniform(-0.3, 0.3))
    emit(f"Banana_{seed}", bm, cname, mats)


def plant_fern(cname, seed, n=9, frond_len=(0.7, 1.2), mats=("leaf",), prefix="Fern"):
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(n):
        h = 2 * math.pi * i / n + rnd.uniform(-0.3, 0.3)
        fl = rnd.uniform(*frond_len)
        sp = spine((0, 0, 0.02), h, rnd.uniform(0.9, 1.3), fl, rnd.uniform(1.6, 2.3), 10, rnd=rnd)
        leaf_bm(bm, sp, 0.16 * fl, fold=0.25, shape=0.9, serrate=0.75, rnd=rnd, mat_idx=0, roll=rnd.uniform(-0.3, 0.3))
    emit(f"{prefix}_{seed}", bm, cname, mats)


def plant_grass(cname, seed, n=14, h=(0.25, 0.55), mats=("grass",)):
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(n):
        hh = rnd.uniform(*h)
        b = (rnd.uniform(-0.12, 0.12), rnd.uniform(-0.12, 0.12), 0)
        sp = spine(b, rnd.uniform(0, 6.28), rnd.uniform(1.0, 1.4), hh, rnd.uniform(0.4, 1.2), 3, rnd=rnd)
        leaf_bm(bm, sp, 0.035, fold=0.3, shape=0.4, rnd=rnd, mat_idx=0, base_w=0.6)
    emit(f"Grass_{seed}", bm, cname, mats)


def plant_heliconia(cname, seed, mats=("leaf", "stem", "flower", "flower_tip")):
    """Heliconia: a few upright long leaves + 2-3 stems with zig-zag red bracts."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(5):
        h = rnd.uniform(0, 6.28)
        st = spine((rnd.uniform(-.1, .1), rnd.uniform(-.1, .1), 0), h, 1.35, rnd.uniform(0.4, 0.8), 0.2, 2, rnd=rnd)
        tube_bm(bm, st, [0.02, 0.016, 0.012], sides=4, rnd=rnd, mat_idx=1, cap=False)
        lf = spine(st[-1], h, 1.15, rnd.uniform(0.8, 1.2), 1.2, 6, rnd=rnd)
        leaf_bm(bm, lf, 0.18, fold=0.2, shape=1.0, tear=0.2, rnd=rnd, mat_idx=0)
    for j in range(rnd.choice((2, 3))):
        h = rnd.uniform(0, 6.28)
        sh = rnd.uniform(0.9, 1.4)
        st = spine((rnd.uniform(-.1, .1), rnd.uniform(-.1, .1), 0), h, 1.4, sh, 0.15, 4, rnd=rnd)
        tube_bm(bm, st, [0.018, 0.016, 0.014, 0.012, 0.01], sides=4, rnd=rnd, mat_idx=1)
        top = st[-1]; d = (st[-1] - st[-2]).normalized()
        nb = rnd.choice((5, 6, 7))
        for k in range(nb):
            p = top + d * (k * 0.07)
            side = 1 if k % 2 else -1
            hh = h + side * 1.57
            b = spine(p, hh, 0.9, 0.17 - 0.012 * k, -0.4, 3, rnd=rnd)
            leaf_bm(bm, b, 0.38, fold=0.9, shape=0.6, rnd=rnd, mat_idx=2 if k < nb - 1 else 3)
    emit(f"Heliconia_{seed}", bm, cname, mats)


def palm(cname, seed, height=13.0, lean=0.25, n_fronds=12, frond_len=5.0, mats=("leaf", "palm_trunk", "stem")):
    rnd = random.Random(seed)
    bm = bmesh.new()
    segs = 12
    head = rnd.uniform(0, 6.28)
    pts = []
    for i in range(segs + 1):
        t = i / segs
        off = lean * height * (t ** 1.8)
        pts.append(Vector((math.cos(head) * off, math.sin(head) * off, height * t)))
    radii = [0.32 if i == 0 else (0.2 - 0.05 * i / segs) * (1.08 if i % 2 else 1.0) for i in range(segs + 1)]
    radii[0] = 0.34; radii[1] = 0.24
    tube_bm(bm, pts, radii, sides=7, rnd=rnd, mat_idx=1, jitter=0.03)
    top = pts[-1]
    blob_bm(bm, top + Vector((0, 0, 0.1)), (0.28, 0.28, 0.35), subdiv=1, noise=0.1, rnd=rnd, mat_idx=2)
    for i in range(n_fronds):
        h = 2 * math.pi * i / n_fronds + rnd.uniform(-0.25, 0.25)
        el = rnd.uniform(0.15, 0.9)
        fl = frond_len * rnd.uniform(0.8, 1.1)
        sp = spine(top + Vector((0, 0, 0.15)), h, el, fl, rnd.uniform(1.6, 2.4), 18, rnd=rnd)
        tube_bm(bm, sp[:7], [0.04, 0.035, 0.03, 0.025, 0.02, 0.015, 0.01], sides=3, rnd=rnd, mat_idx=2, cap=False)
        leaf_bm(bm, sp, 0.55, fold=0.5, shape=0.6, serrate=0.7, rnd=rnd, mat_idx=0, base_w=0.1, roll=rnd.uniform(-0.2, 0.2))
    emit(f"Palm_{seed}", bm, cname, mats)


def broadleaf_tree(cname, seed, height=11.0, crown=4.5, n_clumps=7, mats=("canopy", "bark")):
    """Mid-ground rainforest tree: slim trunk with 3-4 branches and faceted canopy clumps (the round crowns by the lake)."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    trunk = spine((0, 0, 0), rnd.uniform(0, 6.28), 1.5, height * 0.65, 0.05, 5, rnd=rnd)
    tube_bm(bm, trunk, [0.45, 0.3, 0.26, 0.22, 0.2, 0.17], sides=7, rnd=rnd, mat_idx=1, jitter=0.06)
    top = trunk[-1]
    tips = []
    for b in range(4):
        h = 2 * math.pi * b / 4 + rnd.uniform(-0.4, 0.4)
        br = spine(top - Vector((0, 0, rnd.uniform(0, 1.5))), h, rnd.uniform(0.6, 1.0), crown * 0.6, 0.2, 3, rnd=rnd)
        tube_bm(bm, br, [0.16, 0.11, 0.08, 0.05], sides=5, rnd=rnd, mat_idx=1)
        tips.append(br[-1])
    tips.append(top + Vector((0, 0, crown * 0.4)))
    for i in range(n_clumps):
        c = rnd.choice(tips) + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.3, 0.8))) * crown * 0.3
        s = crown * rnd.uniform(0.32, 0.5)
        blob_bm(bm, c, (s, s, s * 0.7), subdiv=1, noise=0.22, rnd=rnd, mat_idx=0)
    emit(f"Tree_{seed}", bm, cname, mats, uv_tiles={0: 6.0})   # crown UVs: 6 m per unit (baked shadow gap pattern)


def rock(cname, seed, size=(1.2, 1.0, 0.8), subdiv=2, noise=0.2, mats=("rock",), prefix="Rock"):
    rnd = random.Random(seed)
    bm = bmesh.new()
    blob_bm(bm, (0, 0, size[2] * 0.55), size, subdiv=subdiv, noise=noise, rnd=rnd, mat_idx=0, flatten_bottom=0.55)
    # low-poly read: collapse the smallest edges so facets are big
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(6), verts=bm.verts, edges=bm.edges)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    emit(f"{prefix}_{seed}", bm, cname, mats)


def vine(cname, seed, length=6.0, mats=("vine",)):
    rnd = random.Random(seed)
    bm = bmesh.new()
    pts = [Vector((0, 0, 0))]
    for i in range(10):
        pts.append(pts[-1] + Vector((rnd.uniform(-.08, .08), rnd.uniform(-.08, .08), -length / 10)))
    tube_bm(bm, pts, [0.03 - 0.002 * i for i in range(11)], sides=3, rnd=rnd, mat_idx=0)
    for i in range(2, 10, 2):
        lf = spine(pts[i], rnd.uniform(0, 6.28), -0.3, 0.18, 0.5, 2, rnd=rnd)
        leaf_bm(bm, lf, 0.07, fold=0.2, rnd=rnd, mat_idx=0)
    emit(f"Vine_{seed}", bm, cname, mats)


# ---------------- epiphytes and lianas for the giant trunks (placed on bark by RainforestBuilder.Epiphytes) ----------------

def bromeliad(cname, seed, n=14, leaf_len=(0.28, 0.42), mats=("leaf", "flower"), prefix="Bromeliad"):
    """Tank bromeliad: a rosette of stiff, channelled strap leaves arching out from a cup; the inner leaves blush red."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(n):
        h = 2 * math.pi * i / n * 2.39 + rnd.uniform(-0.15, 0.15)            # golden-angle spiral
        inner = i > n * 0.6
        el = rnd.uniform(1.15, 1.35) if inner else rnd.uniform(0.75, 1.05)
        ll = rnd.uniform(*leaf_len) * (0.7 if inner else 1.0)
        lf = spine((0, 0, 0.02), h, el, ll, 1.1 if not inner else 0.6, 4, rnd=rnd)
        leaf_bm(bm, lf, 0.055 * (ll / 0.35), fold=0.55, shape=0.35, rnd=rnd, mat_idx=1 if inner else 0, base_w=0.03)
    emit(f"{prefix}_{seed}", bm, cname, mats)


def orchid(cname, seed, mats=("leaf", "orchid", "stem"), prefix="Orchid"):
    """Epiphytic orchid: 3-4 thick strap leaves and an arching spray of 5-7 pale flowers."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(rnd.choice((3, 4))):
        lf = spine((0, 0, 0), rnd.uniform(0, 6.28), rnd.uniform(0.2, 0.6), rnd.uniform(0.18, 0.26), 0.7, 3, rnd=rnd)
        leaf_bm(bm, lf, 0.05, fold=0.3, shape=0.9, rnd=rnd, mat_idx=0)
    h = rnd.uniform(0, 6.28)
    st = spine((0, 0, 0.01), h, 1.0, 0.42, 1.9, 6, rnd=rnd)
    tube_bm(bm, st, [0.006] * len(st), sides=3, rnd=rnd, mat_idx=2, cap=False)
    for k in range(2, len(st)):
        c = st[k]
        for p in range(5):                                                   # five-petal star, tilted outward
            a = 2 * math.pi * p / 5 + rnd.uniform(-0.2, 0.2)
            tip = c + Vector((math.cos(a) * 0.035, math.sin(a) * 0.035, rnd.uniform(-0.008, 0.008)))
            leaf_bm(bm, [c, c.lerp(tip, 0.5), tip], 0.022, fold=0.1, shape=0.9, rnd=rnd, mat_idx=1, base_w=0.004)
    emit(f"{prefix}_{seed}", bm, cname, mats)


def moss_cushion(cname, seed, n=6, size=0.35, mats=("moss",), prefix="Moss"):
    """A flat cushion of moss for bark and buttresses: overlapping flattened blobs, local Z = surface normal."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(n):
        r = rnd.uniform(0.3, 1.0) * size
        c = (rnd.uniform(-1, 1) * size * 0.8, rnd.uniform(-1, 1) * size * 0.8, 0.0)
        blob_bm(bm, c, (r * 0.6, r * 0.5, r * 0.12), subdiv=1, noise=0.25, rnd=rnd, mat_idx=0, flatten_bottom=0.2)
    emit(f"{prefix}_{seed}", bm, cname, mats)


def liana(cname, seed, height=9.0, reach=2.2, thick=0.05, mats=("bark", "leaf"), prefix="Liana"):
    """A woody liana: from crown height (top at local z = height) down to the ground at the origin, bowing out and
    sagging in its lower half, swinging a little sideways; tapering, a few leaves near the top. Pivot = the ground end."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    pts = []; ph = rnd.uniform(0, 6.28)
    for i in range(25):
        t = i / 24.0
        x = reach * t + 0.35 * reach * math.sin(math.pi * t)                 # bows out between ground and crown
        y = 0.4 * math.sin(2 * math.pi * t * 1.2 + ph) * math.sin(math.pi * t)
        z = height * t - 0.18 * height * math.sin(math.pi * t) ** 2 * (1 - t)   # sags (hangs) in its lower half
        pts.append(Vector((x, y, z)))
    tube_bm(bm, pts, [thick * (0.75 + 0.25 * (1 - i / 24)) for i in range(25)], sides=5, rnd=rnd, mat_idx=0, jitter=0.15)
    for i in range(16, 25, 2):
        lf = spine(pts[i], rnd.uniform(0, 6.28), -0.4, 0.22, 0.5, 2, rnd=rnd)
        leaf_bm(bm, lf, 0.09, fold=0.2, rnd=rnd, mat_idx=1)
    emit(f"{prefix}_{seed}", bm, cname, mats)


# ---------------- water edge: reeds for the stream banks, lily pads for the lake shallows ----------------

def reeds(cname, seed, n=16, h=(0.7, 1.3), mats=("grass", "stem", "bark"), prefix="Reeds"):
    """A clump of tall thin blades leaning out a little, with 2-3 brown seed heads (cattail-like)."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(n):
        hh = rnd.uniform(*h); a = rnd.uniform(0, 6.28)
        base = (rnd.uniform(-0.08, 0.08), rnd.uniform(-0.08, 0.08), 0)
        bl = spine(base, a, rnd.uniform(1.35, 1.5), hh, rnd.uniform(0.15, 0.5), 4, rnd=rnd)
        leaf_bm(bm, bl, 0.025, fold=0.1, shape=0.3, rnd=rnd, mat_idx=0, base_w=0.012)
    for j in range(rnd.choice((2, 3))):
        a = rnd.uniform(0, 6.28); st = spine((rnd.uniform(-.05, .05), rnd.uniform(-.05, .05), 0), a, 1.5, rnd.uniform(1.0, 1.4), 0.08, 3, rnd=rnd)
        tube_bm(bm, st, [0.007] * len(st), sides=3, rnd=rnd, mat_idx=1, cap=False)
        d = (st[-1] - st[-2]).normalized()
        tube_bm(bm, [st[-1] - d * 0.14, st[-1]], [0.018, 0.016], sides=5, rnd=rnd, mat_idx=2)     # brown seed head
    emit(f"{prefix}_{seed}", bm, cname, mats)


def lily_pads(cname, seed, n=7, spread=0.9, mats=("leaf_light", "orchid"), prefix="LilyPads"):
    """A raft of lily pads (faceted discs with a notch, lying flat at z = 0) and one or two pink flowers."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(n):
        r = rnd.uniform(0.12, 0.26); c = Vector((rnd.uniform(-1, 1) * spread, rnd.uniform(-1, 1) * spread, 0.004 * i))
        rot = rnd.uniform(0, 6.28); seg = 9
        ring = []
        for k in range(seg):
            a = rot + 0.35 + (2 * math.pi - 0.7) * k / (seg - 1)              # the notch: a 0.7 rad wedge left out
            ring.append(bm.verts.new(c + Vector((math.cos(a) * r, math.sin(a) * r, 0))))
        ctr = bm.verts.new(c)
        for k in range(seg - 1):
            f = bm.faces.new((ctr, ring[k], ring[k + 1])); f.material_index = 0
    for j in range(rnd.choice((1, 2))):
        c = Vector((rnd.uniform(-0.6, 0.6) * spread, rnd.uniform(-0.6, 0.6) * spread, 0.02))
        for p in range(8):
            a = 2 * math.pi * p / 8 + rnd.uniform(-0.1, 0.1)
            tip = c + Vector((math.cos(a) * 0.07, math.sin(a) * 0.07, 0.05))
            leaf_bm(bm, [c, c.lerp(tip, 0.5), tip], 0.035, fold=0.4, shape=0.8, rnd=rnd, mat_idx=1, base_w=0.006)
    emit(f"{prefix}_{seed}", bm, cname, mats)
