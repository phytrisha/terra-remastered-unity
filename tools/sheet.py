#!/usr/bin/env python3
"""Contact sheet: tools/sheet.py out.png [--cols 4] [--width 480] img1.png img2.png ...
Labels each tile with its file name. Use it to compare review rounds (before / after) side by side."""
import argparse
from PIL import Image, ImageDraw

ap = argparse.ArgumentParser(); ap.add_argument("out"); ap.add_argument("imgs", nargs="+")
ap.add_argument("--cols", type=int, default=4); ap.add_argument("--width", type=int, default=480)
a = ap.parse_args()
ims = [Image.open(p).convert("RGB") for p in a.imgs]
w = a.width; h = max(int(im.height * w / im.width) for im in ims)
rows = (len(ims) + a.cols - 1) // a.cols
sheet = Image.new("RGB", (a.cols * w, rows * (h + 18)), (24, 24, 24))
d = ImageDraw.Draw(sheet)
for i, (im, p) in enumerate(zip(ims, a.imgs)):
    x, y = (i % a.cols) * w, (i // a.cols) * (h + 18)
    sheet.paste(im.resize((w, int(im.height * w / im.width))), (x, y + 18))
    d.text((x + 4, y + 3), p.split("/")[-1], fill=(220, 220, 220))
sheet.save(a.out); print(a.out)
