"""Measure Arena draft-card rectangles in a composited capture (read-only image analysis).
Seeds: column spans from a column-mean profile over each row band; edges refined per card with median scanlines.
Usage: measure_cards.py <png> <captureX> <captureY> <clientX> <clientY> <rowBands as y0-y1,y0-y1,...>"""
import json, statistics, sys
from PIL import Image

img = Image.open(sys.argv[1]).convert("RGB"); px = img.load(); W, H = img.size
ox, oy, cx, cy = (int(v) for v in sys.argv[2:6])
bands = [tuple(int(v) for v in b.split("-")) for b in sys.argv[6].split(",")]
T = 70
lum = lambda p: (p[0] * 299 + p[1] * 587 + p[2] * 114) / 1000

def spans(values, base, minlen):
    out, start = [], None
    for i, v in enumerate(values + [0]):
        if v > T and start is None: start = i
        elif v <= T and start is not None:
            if i - start >= minlen: out.append((start + base, i - 1 + base))
            start = None
    return out

def edge_scan(fixed, lo, hi, vertical, from_low):
    """Walk from outside (gap) towards the card; return first coordinate where a 3-px run is above T."""
    rng = range(lo, hi) if from_low else range(hi, lo, -1)
    for c in rng:
        vals = [lum(px[fixed, c + k]) if vertical else lum(px[c + k, fixed]) for k in ((0, 1, 2) if from_low else (0, -1, -2))]
        if min(vals) > T: return c
    return None

cards = []
for (y0, y1) in bands:
    colmeans = [sum(lum(px[x, y]) for y in range(y0, y1, 2)) / len(range(y0, y1, 2)) for x in range(0, W)]
    for (x0, x1) in spans(colmeans, 0, 100):
        mx, my = (x0 + x1) // 2, (y0 + y1) // 2
        lefts = [edge_scan(y, x0 - 25, mx, False, True) for y in range(my - 60, my + 61, 15)]
        rights = [edge_scan(y, mx, x1 + 25, False, False) for y in range(my - 60, my + 61, 15)]
        tops = [edge_scan(x, y0 - 30, my, True, True) for x in range(mx - 50, mx + 51, 12)]
        bottoms = [edge_scan(x, my, min(H - 3, y1 + 30), True, False) for x in range(mx - 50, mx + 51, 12)]
        med = lambda v: int(statistics.median([a for a in v if a is not None]))
        L, R, Tp, B = med(lefts), med(rights), med(tops), med(bottoms)
        cards.append({"x": L, "y": Tp, "w": R - L + 1, "h": B - Tp + 1})
for i, c in enumerate(cards):
    c["slot"] = i; c["screen"] = [c["x"] + ox, c["y"] + oy]; c["client"] = [c["x"] + ox - cx, c["y"] + oy - cy]
    c["clientCenter"] = [c["client"][0] + (c["w"] - 1) / 2, c["client"][1] + (c["h"] - 1) / 2]
    print(f'slot {i:>2}: capture ({c["x"]},{c["y"]}) {c["w"]}x{c["h"]}  screen {tuple(c["screen"])}  client {tuple(c["client"])}  center {tuple(c["clientCenter"])}')
print("JSON", json.dumps(cards))
