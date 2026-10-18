ob = bpy.data.objects["SM_WhiteTiger"]
vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
H = max(v.z for v in vs); ymin = min(v.y for v in vs); ymax = max(v.y for v in vs)
prof = []
y = ymin
while y < ymax:
    b = [v for v in vs if y <= v.y < y + 0.05]
    if b:
        prof.append([round(y + 0.025, 3), round(min(v.z for v in b), 3), round(max(v.z for v in b), 3), round(min(v.x for v in b), 3), round(max(v.x for v in b), 3), len(b)])
    y += 0.05
legs = {}
for zc in (0.03, 0.15, 0.3, 0.45):
    b = [v for v in vs if abs(v.z - zc) < 0.025]
    # clusters by y sign around body middle and x sign
    out = {}
    for v in b:
        k = ("F" if v.y < -0.1 else "B" if v.y > 0.2 else "M") + ("L" if v.x > 0 else "R")
        out.setdefault(k, []).append(v)
    legs[zc] = {k: [round(sum(p.x for p in l) / len(l), 3), round(sum(p.y for p in l) / len(l), 3), round(min(p.y for p in l), 3), round(max(p.y for p in l), 3), len(l)] for k, l in out.items()}
result = {"H": H, "y": [ymin, ymax], "profile y,zmin,zmax,xmin,xmax,n": prof, "legs x,yavg,ymin,ymax,n": legs}
