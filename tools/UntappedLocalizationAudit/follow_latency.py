"""Overlay-follow latency from a session-events.log: for every overlay rectangle update, how long ago did Arena's
client first reach that position (+-2 px)? Also: how long does Arena's client stay un-tracked (max gap)."""
import re, statistics, sys
pat = re.compile(r"t=\s*([\d.]+) fg=(\S+) .*?client=\((-?\d+),(-?\d+)\) (\d+)x(\d+) ov=\((-?\d+),(-?\d+)\) (\d+)x(\d+)")
rows = []
for line in open(sys.argv[1], encoding="utf-8"):
    m = pat.search(line)
    if m: rows.append((float(m[1]), m[2], int(m[3]), int(m[4]), int(m[5]), int(m[6]), int(m[7]), int(m[8]), int(m[9]), int(m[10])))
first_seen = {}   # client position -> first time
lags, prev_ov = [], None
for t, fg, cx, cy, cw, ch, ox, oy, ow, oh in rows:
    first_seen.setdefault((cx, cy), t)
    if (ox, oy) != prev_ov and prev_ov is not None:
        cands = [ts for (px, py), ts in first_seen.items() if abs(px - ox) <= 2 and abs(py - oy) <= 2 and ts <= t]
        if cands: lags.append(t - max(cands))
    prev_ov = (ox, oy)
# periods where the overlay is not aligned with the client (|dx|or|dy|>2): duration of each misaligned episode
episodes, start = [], None
for t, fg, cx, cy, cw, ch, ox, oy, ow, oh in rows:
    bad = abs(cx - ox) > 2 or abs(cy - oy) > 2 or abs(cw - ow) > 3 or abs(ch - oh) > 3
    if bad and start is None: start = t
    if not bad and start is not None: episodes.append(t - start); start = None
print(f"overlay position updates matched to an earlier Arena client position: {len(lags)}")
if lags:
    q = statistics.quantiles(lags, n=20)
    print(f"lag ms: median {statistics.median(lags):.1f}  p10 {q[1]:.1f}  p90 {q[17]:.1f}  max {max(lags):.1f}")
print(f"misaligned episodes: {len(episodes)}  median {statistics.median(episodes):.1f} ms  p90 {statistics.quantiles(episodes, n=10)[8]:.1f} ms  max {max(episodes):.1f} ms")
