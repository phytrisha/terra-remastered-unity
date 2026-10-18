ob = bpy.data.objects["SM_WhiteTiger"]
vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
out = {}
win = {"FL": (1, -0.75, -0.25), "FR": (-1, -0.75, -0.25), "BL": (1, 0.15, 0.68), "BR": (-1, 0.15, 0.68)}
for k, (sx, y0, y1) in win.items():
    rows = []
    for zc in [0.02, 0.06, 0.1, 0.15, 0.2, 0.25, 0.3, 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.65]:
        b = [v for v in vs if abs(v.z - zc) < 0.02 and y0 < v.y < y1 and v.x * sx > 0.02]
        if b: rows.append([zc, round(sum(v.x for v in b) / len(b), 3), round(sum(v.y for v in b) / len(b), 3), round(min(v.y for v in b), 3), round(max(v.y for v in b), 3), len(b)])
    out[k] = rows
# tail centre line
tail = []
for yc in [0.55, 0.6, 0.65, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95, 1.0, 1.03]:
    b = [v for v in vs if abs(v.y - yc) < 0.025 and (yc < 0.68 and v.z > 0.55 or yc >= 0.68)]
    if b: tail.append([yc, round(sum(v.x for v in b) / len(b), 3), round(sum(v.z for v in b) / len(b), 3), round(max(v.z for v in b) - min(v.z for v in b), 3)])
# head: centre per y
head = []
for yc in [-1.0, -0.95, -0.9, -0.85, -0.8, -0.75, -0.7, -0.65, -0.6]:
    b = [v for v in vs if abs(v.y - yc) < 0.025 and v.z > 0.55]
    if b: head.append([yc, round(min(v.z for v in b), 3), round(max(v.z for v in b), 3)])
# ears: high verts on the head
ears = sorted([(round(v.x, 3), round(v.y, 3), round(v.z, 3)) for v in vs if v.z > 1.02], key=lambda p: -p[2])[:12]
result = {"legs": out, "tail y,x,z,thick": tail, "head y,zmin,zmax": head, "ears": ears}
