"""Isolate overlay-drawn elements as connected components of (Arena-only frame) vs (frame with overlay) difference.
Read-only image analysis. Usage: overlay_components.py <without.png> <with.png> <clientOffsetX> <clientOffsetY> [minArea]"""
import json, sys
from PIL import Image, ImageChops, ImageFilter

a = Image.open(sys.argv[1]).convert("RGB"); b = Image.open(sys.argv[2]).convert("RGB")
offx, offy = int(sys.argv[3]), int(sys.argv[4]); min_area = int(sys.argv[5]) if len(sys.argv) > 5 else 60
diff = ImageChops.difference(a, b).convert("L").point(lambda v: 255 if v > 24 else 0)
mask = diff.filter(ImageFilter.MaxFilter(5))          # close small gaps inside one element
W, H = mask.size; m = mask.load(); raw = diff.load()
seen = bytearray(W * H); comps = []
for y in range(H):
    for x in range(W):
        if m[x, y] and not seen[y * W + x]:
            stack = [(x, y)]; seen[y * W + x] = 1
            x0 = x1 = x; y0 = y1 = y; n = 0
            while stack:
                cx, cy = stack.pop()
                if raw[cx, cy]: n += 1
                x0, x1, y0, y1 = min(x0, cx), max(x1, cx), min(y0, cy), max(y1, cy)
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < W and 0 <= ny < H and m[nx, ny] and not seen[ny * W + nx]:
                        seen[ny * W + nx] = 1; stack.append((nx, ny))
            if n >= min_area:
                # the 5px max-filter grows boxes by 2px per side; undo it
                comps.append({"x": x0 + 2 - offx, "y": y0 + 2 - offy, "w": x1 - x0 - 3, "h": y1 - y0 - 3, "px": n})
comps.sort(key=lambda c: (c["y"] // 40, c["x"]))
for c in comps:
    print(f'client ({c["x"]:>4},{c["y"]:>4}) {c["w"]:>4}x{c["h"]:<4} center=({c["x"] + (c["w"] - 1) / 2:7.1f},{c["y"] + (c["h"] - 1) / 2:7.1f}) changedPx={c["px"]}')
print("JSON", json.dumps(comps))
