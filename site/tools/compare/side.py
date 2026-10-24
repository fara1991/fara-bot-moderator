# 使い方: python3 side.py <key> [outdir]   old と new を横に並べた画像を作る
import sys, pathlib
from PIL import Image
key = sys.argv[1]
root = pathlib.Path.cwd()
a = Image.open(root / "shots-old" / f"{key}.png").convert("RGB")
b = Image.open(root / "shots-new" / f"{key}.png").convert("RGB")
w = a.width + b.width + 8
h = max(a.height, b.height)
out = Image.new("RGB", (w, h), (255, 0, 255))
out.paste(a, (0, 0)); out.paste(b, (a.width + 8, 0))
dst = root / "side"; dst.mkdir(exist_ok=True)
out.save(dst / f"{key}.png")
print(dst / f"{key}.png")
