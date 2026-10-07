"""Fit candidate Arena draft-slot geometry models to measured grids (measure_grid.py JSON) at several client sizes.

Every model predicts slot (row r, col c) as x = X(c), y = Y(r), w, h from the client size (W, H) and six reference
parameters (x0, px, y0, py, w, h) expressed at the reference client 1723x1009. The reference parameters are fitted by
least squares over ALL datasets jointly; residuals are reported per slot. Optional: --predict WxH,... prints the
best model's predicted grid for client sizes that have not been measured yet (pre-registered predictions).

Usage: fit_geometry.py grid1.json:W:H grid2.json:W:H ... [--predict 1920x1080,1440x900]
"""
import itertools
import json
import math
import sys

WR, HR = 1723.0, 1009.0
datasets, predict = [], []
for arg in sys.argv[1:]:
    if arg == "--predict":
        continue
    if "x" in arg and ":" not in arg and arg[0].isdigit():
        predict += [tuple(int(v) for v in s.split("x")) for s in arg.split(",")]
        continue
    path, w, h = arg.split(":")
    datasets.append((path, float(w), float(h), json.load(open(path))["cards"]))


def lstsq_1d(rows):
    """Least squares for y = a*u + b*v (two unknowns, no intercept) over rows (u, v, y)."""
    suu = sum(u * u for u, v, y in rows); svv = sum(v * v for u, v, y in rows); suv = sum(u * v for u, v, y in rows)
    suy = sum(u * y for u, v, y in rows); svy = sum(v * y for u, v, y in rows)
    det = suu * svv - suv * suv
    return ((suy * svv - svy * suv) / det, (svy * suu - suy * suv) / det) if abs(det) > 1e-12 else (suy / suu, 0.0)


def lstsq_scalar(rows):
    """Least squares for y = a*u over rows (u, y)."""
    return sum(u * y for u, y in rows) / sum(u * u for u, y in rows)


def make_model(name, sx, sy, ax):
    """sx/sy: horizontal/vertical scale functions of (W,H); ax: horizontal anchor (x origin) as a function of (W,H)."""
    return {"name": name, "sx": sx, "sy": sy, "ax": ax}


def canvas(m):
    return lambda W, H: math.exp((1 - m) * math.log(W / WR) + m * math.log(H / HR))


models = [
    make_model("A  fixed pixels (DPI only)", lambda W, H: 1.0, lambda W, H: 1.0, lambda W, H: 0.0),
    make_model("B1 uniform s=H/1009, left anchor", lambda W, H: H / HR, lambda W, H: H / HR, lambda W, H: 0.0),
    make_model("B2 uniform s=W/1723, left anchor", lambda W, H: W / WR, lambda W, H: W / WR, lambda W, H: 0.0),
    make_model("B3 uniform s=min(W/1723,H/1009), left", lambda W, H: min(W / WR, H / HR), lambda W, H: min(W / WR, H / HR), lambda W, H: 0.0),
    make_model("C/D independent sx=W/1723, sy=H/1009", lambda W, H: W / WR, lambda W, H: H / HR, lambda W, H: 0.0),
    make_model("E  uniform s=H/1009, centre-x anchor", lambda W, H: H / HR, lambda W, H: H / HR, lambda W, H: W / 2),
    make_model("E2 uniform s=W/1723, centre-x anchor", lambda W, H: W / WR, lambda W, H: W / WR, lambda W, H: W / 2),
    make_model("E3 uniform s=H/1009, right anchor", lambda W, H: H / HR, lambda W, H: H / HR, lambda W, H: W),
]


def fit(model, data):
    """Fit x0,px (in ref px, relative to the anchor) and y0,py,w,h; return params and residual table."""
    xr, yr, wr, hr = [], [], [], []
    for path, W, H, cards in data:
        sx, sy, ax = model["sx"](W, H), model["sy"](W, H), model["ax"](W, H)
        for c in cards:
            xr.append((sx, sx * c["col"], c["x"] - ax))
            yr.append((sy, sy * c["row"], c["y"]))
            wr.append((sx, c["w"]))
            hr.append((sy, c["h"]))
    x0, px = lstsq_1d(xr)
    y0, py = lstsq_1d(yr)
    w, h = lstsq_scalar(wr), lstsq_scalar(hr)
    out = []
    for path, W, H, cards in data:
        sx, sy, ax = model["sx"](W, H), model["sy"](W, H), model["ax"](W, H)
        for c in cards:
            pred = (ax + sx * (x0 + px * c["col"]), sy * (y0 + py * c["row"]), sx * w, sy * h)
            out.append((f"{int(W)}x{int(H)}", c, pred, (c["x"] - pred[0], c["y"] - pred[1], c["w"] - pred[2], c["h"] - pred[3])))
    return (x0, px, y0, py, w, h), out


def summary(res):
    errs = [abs(e) for *_, r in res for e in r]
    return max(errs), math.sqrt(sum(e * e for e in errs) / len(errs))


print(f"datasets: {[(p.split('/')[-1], int(W), int(H), len(c)) for p, W, H, c in datasets]}\n")
print(f"{'model':42s} {'max|res| px':>11s} {'RMS px':>7s}   fitted reference params (x0 rel. anchor, px, y0, py, w, h)")
results = {}
for model in models:
    params, res = fit(model, datasets)
    mx, rms = summary(res)
    results[model["name"]] = (params, res)
    print(f"{model['name']:42s} {mx:11.2f} {rms:7.2f}   " + ", ".join(f"{v:.2f}" for v in params))

# G: Unity-style CanvasScaler (match width<->height in log space) with a free horizontal anchor fraction.
best = None
for m, a in itertools.product([i / 100 for i in range(0, 101)], [i / 100 for i in range(0, 101)]):
    model = make_model(f"G  canvas m={m:.2f}, x-anchor={a:.2f}·W", canvas(m), canvas(m), lambda W, H, a=a: a * W)
    params, res = fit(model, datasets)
    mx, rms = summary(res)
    if best is None or rms < best[0]:
        best = (rms, mx, model["name"], params, res)
print(f"{best[2]:42s} {best[1]:11.2f} {best[0]:7.2f}   " + ", ".join(f"{v:.2f}" for v in best[3]))

sizes = sorted({f"{int(W)}x{int(H)}" for _, W, H, _ in datasets})
print(f"\nPer-size max|res| / RMS (px) for each model (fitted jointly on all sizes):")
print(f"{'model':42s} " + "  ".join(f"{s:>15s}" for s in sizes))
for mname, (params_, res_) in results.items():
    cells = []
    for s in sizes:
        errs = [abs(e) for size, c, p, r in res_ if size == s for e in r]
        cells.append(f"{max(errs):6.1f} / {math.sqrt(sum(e * e for e in errs) / len(errs)):5.2f}")
    print(f"{mname:42s} " + "  ".join(f"{c:>15s}" for c in cells))

name = "E  uniform s=H/1009, centre-x anchor"
params, res = results[name]
print(f"\nPer-slot residuals for '{name}' (measured - predicted, client px):")
for size, c, pred, r in res:
    print(f"  {size:>9s} r{c['row']}c{c['col']}: meas ({c['x']:6.1f},{c['y']:6.1f},{c['w']:5.1f},{c['h']:5.1f})  pred ({pred[0]:6.1f},{pred[1]:6.1f},{pred[2]:5.1f},{pred[3]:5.1f})  "
          f"res ({r[0]:+5.1f},{r[1]:+5.1f},{r[2]:+5.1f},{r[3]:+5.1f})")
x0, px, y0, py, w, h = params
print(f"\nBest-fit formula (client px):  s = H/{HR:.0f}")
print(f"  x(c) = W/2 + s*({x0:.2f} + {px:.3f}*c)   y(r) = s*({y0:.2f} + {py:.3f}*r)   w = s*{w:.2f}   h = s*{h:.2f}")
print(f"  normalized by H: x(c) = W/2 + H*({x0 / HR:+.5f} + {px / HR:.5f}*c), y(r) = H*({y0 / HR:.5f} + {py / HR:.5f}*r), w = {w / HR:.5f}*H, h = {h / HR:.5f}*H")
for W, H in predict:
    s = H / HR
    cols = [W / 2 + s * (x0 + px * c) for c in range(5)]
    rows_ = [s * (y0 + py * r) for r in range(3)]
    print(f"  PREDICTION {W}x{H}: col x = {[round(v, 1) for v in cols]}, row y = {[round(v, 1) for v in rows_]}, card {s * w:.1f}x{s * h:.1f}, "
          f"grid left edge {cols[0]:.1f} px ({cols[0] / W:.3f}W)")
