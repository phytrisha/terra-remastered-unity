# Tileable gap mask for the canopy crowns' baked shadows (Unity lightmapper reads it as _TransparencyLM through the
# crown UV0, 6 m per UV unit): white = light passes, black = opaque. Soft-edged blobs ~2-3.5 m, ~80 % open.
#   python3 textures/Canopy/gen_transmission.py  -> UnityProject/Assets/Art/Textures/Foliage/T_Canopy_LMTransmission.png
import numpy as np, os
from PIL import Image
N, rng = 256, np.random.default_rng(7)
f = np.zeros((N, N), complex)
for kx in range(-4, 5):
    for ky in range(-4, 5):
        k = (kx * kx + ky * ky) ** 0.5
        if 1.0 <= k <= 2.6:                      # 1.0-2.6 cycles per 6 m tile -> blobs of ~2.3-6 m (bold pattern)
            f[ky % N, kx % N] = rng.normal() + 1j * rng.normal()
n = np.real(np.fft.ifft2(f)); n = (n - n.mean()) / n.std()
thr = np.quantile(n, 0.20)                       # 80 % open per crown: 2-4 crowns overlap over the path (ray test)
t = np.clip((n - thr) / 0.45 + 0.5, 0, 1)
t = t * t * (3 - 2 * t)                          # soft edge
out = os.path.join(os.path.dirname(__file__), "..", "..", "UnityProject", "Assets", "Art", "Textures", "Foliage", "T_Canopy_LMTransmission.png")
Image.fromarray((t * 255).astype(np.uint8)).convert("RGB").save(os.path.normpath(out))
print("open fraction", (t > 0.5).mean().round(3), "->", os.path.normpath(out))
