# Where are the eyes on the HD head? Saturation / hue / value of the base colour at head vertices. No save.
import numpy as np
ob = bpy.data.objects["SM_WhiteTigerHD"]; me = ob.data
V = np.array([v.co for v in me.vertices])
m = ob.material_slots[0].material
img = next(l.from_node.image for l in m.node_tree.links if l.from_node.type == 'TEX_IMAGE' and l.to_socket.name == "Base Color")
w_, h_ = img.size; px = np.empty(w_ * h_ * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(h_, w_, 4)[..., :3]
uvl = np.empty(len(me.loops) * 2); me.uv_layers[0].data.foreach_get("uv", uvl); uvl = uvl.reshape(-1, 2)
lvi = np.empty(len(me.loops), np.int64); me.loops.foreach_get("vertex_index", lvi)
col = px[np.clip((uvl[:, 1] % 1) * h_, 0, h_ - 1).astype(int), np.clip((uvl[:, 0] % 1) * w_, 0, w_ - 1).astype(int)]
vc = np.zeros((len(V), 3)); cn = np.zeros(len(V)); np.add.at(vc, lvi, col); np.add.at(cn, lvi, 1); vc /= np.maximum(cn, 1)[:, None]
mx, mn = vc.max(1), vc.min(1); sat = (mx - mn) / np.maximum(mx, 1e-4)
head = np.where(V[:, 1] < -0.85)[0]
top = head[np.argsort(-sat[head])[:40]]
# also: texture-space: find the most saturated pixels overall (amber) and map back to nearest vertex UVs
flat = px.reshape(-1, 3); fmx = flat.max(1); fsat = (fmx - flat.min(1)) / np.maximum(fmx, 1e-4)
amber = (fsat > 0.45) & (flat[:, 0] > flat[:, 2] + 0.15) & (fmx > 0.25)
ys, xs = np.divmod(np.where(amber)[0], w_)
result = {"head_verts": int(len(head)), "top_sat": [[round(float(sat[i]), 2), V[i].round(3).tolist(), vc[i].round(2).tolist()] for i in top[:12]],
          "amber_pixels": int(amber.sum()), "amber_uv_bbox": [round(xs.min() / w_, 3), round(ys.min() / h_, 3), round(xs.max() / w_, 3), round(ys.max() / h_, 3)] if amber.any() else None,
          "verts_per_head_cm2": round(len(head) / 1.0, 1)}
