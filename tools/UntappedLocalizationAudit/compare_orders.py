"""Line up Player.log order, Untapped Scry order and Arena-DB predicted order for one pack (read-only)."""
import importlib.util, json, re, sys
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("probe", repo / "tools/ArenaSortOrderProbe/arena_sort_order_probe.py")
probe = importlib.util.module_from_spec(spec); spec.loader.exec_module(probe)

log_text = Path(sys.argv[1]).read_text(encoding="utf-8", errors="replace")
scry = [int(x) for x in sys.argv[2].split(",")]
db = probe.CardDb(probe.find_db(None))
packs = probe.packs_in_log(log_text)
pack = packs[-1]
log_order = pack["pack"]
rules = probe.hypotheses()
obs = probe.fixture_observations(repo)
survivors, _ = probe.score(db, obs, rules)
preds = probe.canonical({name: probe.predict(db, log_order, rules[name]) for name in survivors})
print("DB version:", db.version())
print("coordinate:", pack.get("label") or pack.get("coord"), "cards:", len(log_order), "surviving rules:", len(survivors), "distinct predicted orders:", len(preds))
predicted = list(next(iter(preds)))
cols = ["GrpId", "ExpansionCode", "CollectorNumber", "Rarity", "Order_MythicToCommon", "Order_ColorOrder", "Order_CMCWithXLast", "Order_CreaturesFirst", "Order_LandLast", "Order_Title"]
def row(g):
    c = db.card(g)
    return {k: c.get(k) for k in cols} | {"name": c.get("name") or c.get("Name") or ""}
print(f"\n{'#':>2} | {'Player.log order':<34} | {'Untapped Scry order':<34} | {'Arena-DB predicted (visual?)':<34}")
for i in range(len(log_order)):
    def nm(g): r = row(g); return f"{g} {str(r['name'])[:20]} ({r['Order_MythicToCommon']})"
    print(f"{i:>2} | {nm(log_order[i]):<34} | {nm(scry[i]):<34} | {nm(predicted[i]):<34}")
print("\nScry == predicted:", scry == predicted, "| Scry == log:", scry == log_order, "| same multiset:", sorted(scry) == sorted(log_order))
print("\nPer-card keys (predicted order):")
for g in predicted: print(json.dumps(row(g)))
