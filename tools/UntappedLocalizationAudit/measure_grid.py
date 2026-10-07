"""Precise Arena draft-card rectangles from an overlay-free capture (read-only image analysis).

Arena draws every pack card inside a neutral-gray "holder" of RGB (38,38,38) on a reddish table. A card edge is the
first non-holder pixel after a run of holder pixels. Every edge is the median over many scanlines. Spans far from the
dominant card size (e.g. a bonus-sheet glow that hides one holder side and merges a gap into the span) are rejected;
such cards fall back to their row/column medians and are flagged.

Usage: measure_grid.py <png> <captureX> <captureY> <clientX> <clientY> [--json out.json]
Prints client-relative rectangles (x, y, w, h) per card in reading order (rows top->bottom, columns left->right).
"""
import json
import statistics
import sys
from PIL import Image

path = sys.argv[1]
cap_x, cap_y, cli_x, cli_y = (int(v) for v in sys.argv[2:6])
out_json = sys.argv[sys.argv.index("--json") + 1] if "--json" in sys.argv else None
img = Image.open(path).convert("RGB")
W, H = img.size
px = img.load()
to_cx, to_cy = cap_x - cli_x, cap_y - cli_y          # client = capture + (cap - cli)


def holder(p):
    return max(abs(p[0] - 38), abs(p[1] - 38), abs(p[2] - 38)) <= 4


def spans(line, lo, hi, min_run=2):
    runs, start = [], None
    for i, h in enumerate(line + [False]):
        if h and start is None:
            start = i
        elif not h and start is not None:
            if i - start >= min_run:
                runs.append((start, i - 1))
            start = None
    return [(a1 + 1, b0 - 1) for (a0, a1), (b0, b1) in zip(runs, runs[1:]) if lo <= b0 - a1 - 1 <= hi]


def groups(items, key, tol):
    items = sorted(items, key=key)
    out, cur = [], []
    for it in items:
        if cur and key(it) - key(cur[-1]) > tol:
            out.append(cur)
            cur = []
        cur.append(it)
    if cur:
        out.append(cur)
    return out


# Horizontal spans everywhere, then keep those near the dominant width.
hs = []
for y in range(0, H, 2):
    line = [holder(px[x, y]) for x in range(W)]
    hs += [(y, l, r) for l, r in spans(line, int(W * 0.04), int(W * 0.25))]
dominant_w = statistics.median([r - l + 1 for _, l, r in hs]) if hs else 0
hs = [s for s in hs if abs((s[2] - s[1] + 1) - dominant_w) <= 0.06 * dominant_w]
cols = []
for g in groups(hs, key=lambda s: s[1], tol=3):
    if len(g) >= 30:
        cols.append((statistics.median([s[1] for s in g]), statistics.median([s[2] for s in g]), g))
cols.sort(key=lambda c: c[0])

# Vertical spans inside each column, keep those near the dominant height.
vs_all = []
for ci, (l, r, _) in enumerate(cols):
    for x in range(int(l) + 8, int(r) - 7, 2):
        line = [holder(px[x, y]) for y in range(H)]
        vs_all += [(ci, x, t, b) for t, b in spans(line, int(H * 0.08), int(H * 0.45))]
dominant_h = statistics.median([b - t + 1 for _, _, t, b in vs_all]) if vs_all else 0
vs_all = [v for v in vs_all if abs((v[3] - v[2] + 1) - dominant_h) <= 0.06 * dominant_h]
rows = []
for g in groups(vs_all, key=lambda v: v[2], tol=4):
    if len(g) >= 10:
        rows.append((statistics.median([v[2] for v in g]), statistics.median([v[3] for v in g])))
rows.sort()

cards = []
for ri, (rt, rb) in enumerate(rows):
    for ci, (cl, cr, g) in enumerate(cols):
        hl = [s for s in g if rt + 10 <= s[0] <= rb - 10]
        vv = [v for v in vs_all if v[0] == ci and abs(v[2] - rt) <= 4]
        if len(hl) < 10 and len(vv) < 5:
            continue                                   # no card in this cell
        flags = []
        L = statistics.median([s[1] for s in hl]) if len(hl) >= 10 else (flags.append("L=col") or cl)
        R = statistics.median([s[2] for s in hl]) if len(hl) >= 10 else (flags.append("R=col") or cr)
        T = statistics.median([v[2] for v in vv]) if len(vv) >= 5 else (flags.append("T=row") or rt)
        B = statistics.median([v[3] for v in vv]) if len(vv) >= 5 else (flags.append("B=row") or rb)
        cards.append({"row": ri, "col": ci, "x": L + to_cx, "y": T + to_cy, "w": R - L + 1, "h": B - T + 1,
                      "nH": len(hl), "nV": len(vv), "flags": flags})

print(f"capture {W}x{H}; dominant card {dominant_w:.0f}x{dominant_h:.0f}; columns {len(cols)}; rows {len(rows)}; cards {len(cards)}")
for i, c in enumerate(cards):
    c["slot"] = i
    print(f"slot {i:>2} r{c['row']}c{c['col']}: x={c['x']:7.1f} y={c['y']:7.1f} w={c['w']:6.1f} h={c['h']:6.1f}  n(H,V)=({c['nH']},{c['nV']}) {' '.join(c['flags'])}")
if out_json:
    json.dump({"image": path, "capture": [cap_x, cap_y, W, H], "client": [cli_x, cli_y], "cards": cards}, open(out_json, "w"), indent=1)
