# Where are faces whose normal flipped between the source and the registered HD mesh? No save.
import numpy as np
hd = bpy.data.objects["SM_WhiteTigerHD"]
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, "sources/tiger/WhiteTigerHD/tripo_convert_edcb4360-f1c0-469c-ad04-d3c599e77d26.fbx"), use_custom_normals=False)
src = [o for o in bpy.data.objects if o not in before and o.type == 'MESH'][0]
def polys(ob):
    me = ob.data
    N = np.array([p.normal for p in me.polygons]); C = np.array([p.center for p in me.polygons]); A = np.array([p.area for p in me.polygons])
    if ob is src: N = (np.array(ob.matrix_world.to_3x3()) @ N.T).T; N = N[:, [1, 0, 2]] * [1, -1, 1]
    return N, C, A
Ns, _, _ = polys(src); Nh, C, area = polys(hd)
fl = np.where(np.einsum('ij,ij->i', Ns, Nh) < 0)[0]; C = C[fl]; area = area[fl]
cl = []
while len(C):
    m = np.linalg.norm(C - C[0], axis=1) < 0.05
    cl.append({"at": C[m].mean(0).round(2).tolist(), "n": int(m.sum()), "area_mm2": round(float(area[m].sum()) * 1e6, 1)}); C = C[~m]; area = area[~m]
result = {"flipped": int(len(fl)), "clusters": sorted(cl, key=lambda c: -c["n"])}
