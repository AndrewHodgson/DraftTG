"""Untapped badge rectangles vs Arena slot rectangles (read-only image analysis).

Overlay elements = connected components of |overlay frame - arena-only frame| (same window position, captured
~0.7 s apart). Each component near a measured card is classified as the header strip (wide, near the card top) or the
shield (near the card bottom), and expressed relative to its slot, both in client px and in units of the slot scale
s = card height / 216.5 (the 1723x1009 reference card height).

Usage: badges_vs_slots.py <arenaonly.png> <overlay.png> <grid.json> [--json out.json]
"""
import json
import sys
from PIL import Image, ImageChops, ImageFilter

arena_png, overlay_png, grid_json = sys.argv[1:4]
out_json = sys.argv[sys.argv.index("--json") + 1] if "--json" in sys.argv else None
grid = json.load(open(grid_json))
cap_x, cap_y, W, H = grid["capture"]
cli_x, cli_y = grid["client"]
to_cx, to_cy = cap_x - cli_x, cap_y - cli_y
a = Image.open(arena_png).convert("RGB")
b = Image.open(overlay_png).convert("RGB")
diff = ImageChops.difference(a, b).convert("L").point(lambda v: 255 if v > 30 else 0)
mask = diff.filter(ImageFilter.MaxFilter(3))
m = mask.load()
raw = diff.load()
seen = bytearray(W * H)
comps = []
for y in range(H):
    for x in range(W):
        if m[x, y] and not seen[y * W + x]:
            stack = [(x, y)]
            seen[y * W + x] = 1
            x0 = x1 = x
            y0 = y1 = y
            n = 0
            while stack:
                cx, cy = stack.pop()
                n += 1 if raw[cx, cy] else 0
                x0, x1, y0, y1 = min(x0, cx), max(x1, cx), min(y0, cy), max(y1, cy)
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < W and 0 <= ny < H and m[nx, ny] and not seen[ny * W + nx]:
                        seen[ny * W + nx] = 1
                        stack.append((nx, ny))
            if n >= 25:
                # MaxFilter(3) grows the box by 1 px per side.
                comps.append({"x": x0 + 1 + to_cx, "y": y0 + 1 + to_cy, "w": x1 - x0 - 1, "h": y1 - y0 - 1, "n": n})

ref_h = 216.5
rows = []
for card in grid["cards"]:
    s = card["h"] / ref_h
    cx_mid = card["x"] + card["w"] / 2
    bottom = card["y"] + card["h"]
    strips = [c for c in comps if c["w"] > 0.6 * card["w"] and c["h"] < 0.25 * card["h"]
              and card["x"] - 4 <= c["x"] <= card["x"] + 0.2 * card["w"] and card["y"] <= c["y"] <= card["y"] + 0.25 * card["h"]]
    shields = [c for c in comps if c["w"] < 0.5 * card["w"] and abs(c["x"] + c["w"] / 2 - cx_mid) < 0.15 * card["w"]
               and bottom - 0.3 * card["h"] <= c["y"] + c["h"] <= bottom + 0.25 * card["h"]]
    strip = max(strips, key=lambda c: c["n"]) if strips else None
    shield = max(shields, key=lambda c: c["n"]) if shields else None
    row = {"slot": card["slot"], "row": card["row"], "col": card["col"], "s": s}
    if strip:
        row["strip"] = strip
        row["strip_dx"], row["strip_dy"] = strip["x"] - card["x"], strip["y"] - card["y"]
        row["strip_dw"] = strip["w"] - card["w"]
    if shield:
        row["shield"] = shield
        row["shield_dcx"] = shield["x"] + shield["w"] / 2 - cx_mid
        row["shield_dbottom"] = shield["y"] + shield["h"] - bottom
    rows.append(row)
    fmt = lambda v: f"{v:+6.1f}" if v is not None else "   -  "
    print(f"slot {card['slot']:>2} r{card['row']}c{card['col']} s={s:.4f} | strip "
          + (f"({strip['x']:.0f},{strip['y']:.0f}) {strip['w']:.0f}x{strip['h']:.0f} dx={fmt(row['strip_dx'])} dy={fmt(row['strip_dy'])} "
             f"[/s: dx={row['strip_dx'] / s:+.1f} dy={row['strip_dy'] / s:+.1f} w={strip['w'] / s:.1f}]" if strip else "not found")
          + " | shield " + (f"{shield['w']:.0f}x{shield['h']:.0f} dcx={fmt(row['shield_dcx'])} dbottom={fmt(row['shield_dbottom'])} "
                            f"[/s: dcx={row['shield_dcx'] / s:+.1f} db={row['shield_dbottom'] / s:+.1f}]" if shield else "not found"))
if out_json:
    json.dump({"components": comps, "slots": rows}, open(out_json, "w"), indent=1)
