# Is the registered HD head (incl. the right ear) a rigid copy of the source head? No save.
import numpy as np
hd = bpy.data.objects["SM_WhiteTigerHD"]
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, "sources/tiger/WhiteTigerHD/tripo_convert_edcb4360-f1c0-469c-ad04-d3c599e77d26.fbx"), use_custom_normals=False)
src = [o for o in bpy.data.objects if o not in before and o.type == 'MESH'][0]
S = np.array([src.matrix_world @ v.co for v in src.data.vertices]); H = np.array([v.co for v in hd.data.vertices])
print_ = {}
def fit(idx):
    X = S[idx] - S[idx].mean(0); Y = H[idx] - H[idx].mean(0); U, Sg, Vt = np.linalg.svd(X.T @ Y)
    R = Vt.T @ np.diag([1, 1, np.sign(np.linalg.det(Vt.T @ U.T))]) @ U.T; s = Sg.sum() / (X ** 2).sum()
    r = np.linalg.norm(Y - s * (R @ X.T).T, axis=1); return round(float(r.mean() * 1000), 2), round(float(r.max() * 1000), 2), round(float(s), 4)
earR = np.where(np.linalg.norm(H - np.array([-0.12, -0.85, 1.01]), axis=1) < 0.05)[0]
earL = np.where(np.linalg.norm(H - np.array([0.16, -0.85, 1.01]), axis=1) < 0.05)[0]
face = np.where(np.linalg.norm(H - np.array([0.02, -0.95, 0.85]), axis=1) < 0.08)[0]
result = {"n_verts_src_hd": [len(S), len(H)], "earR": [len(earR), fit(earR)], "earL": [len(earL), fit(earL)], "face": [len(face), fit(face)],
          "earR+face": fit(np.r_[earR, face])}
head = np.where(H[:, 1] < -0.86)[0]
X = S[head] - S[head].mean(0); Y = H[head] - H[head].mean(0); U, Sg, Vt = np.linalg.svd(X.T @ Y)
R = Vt.T @ np.diag([1, 1, np.sign(np.linalg.det(Vt.T @ U.T))]) @ U.T; s = Sg.sum() / (X ** 2).sum()
r = np.linalg.norm(Y - s * (R @ X.T).T, axis=1)
bins = {}
for i in np.argsort(-r)[:400]:
    k = tuple((H[head[i]] / 0.04).round().astype(int) * 4); bins[k] = bins.get(k, 0) + 1
result["whole_head"] = {"n": len(head), "res_mm": [round(float(np.median(r)) * 1000, 2), round(float(np.percentile(r, 95)) * 1000, 1), round(float(r.max()) * 1000, 1)],
                        "worst_cells_cm": sorted(bins.items(), key=lambda kv: -kv[1])[:8]}
# raw source: is the right ear one connected piece with the head, and how far are its verts from the left/right mirror?
