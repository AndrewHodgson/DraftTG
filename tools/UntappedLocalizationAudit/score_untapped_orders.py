"""Offline, read-only: treat Untapped's stored per-pick DraftPack order (drafts.json) as a candidate display order
and score it against the Phase 9E hypothesis family. Never writes DraftTG's ledger. Prints GrpIds and counts only."""
import importlib.util, json, sys
from pathlib import Path
repo = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("probe", repo / "tools/ArenaSortOrderProbe/arena_sort_order_probe.py")
probe = importlib.util.module_from_spec(spec); spec.loader.exec_module(probe)
db = probe.CardDb(probe.find_db(None))
rules = probe.hypotheses()
fixtures = probe.fixture_observations(repo)
fixture_survivors, _ = probe.score(db, fixtures, rules)
drafts = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
alive = set(rules)            # all 400, Untapped evidence only
alive_fx = set(fixture_survivors)  # fixtures first, then Untapped evidence
rows = []
for draft_key, draft in drafts.items():
    for p in draft["picks"]:
        pack = [int(x) for x in p["DraftPack"]]
        if len(pack) < 2: continue
        try:
            preds = {name: probe.predict(db, pack, rules[name]) for name in rules}
        except KeyError as e:
            print("missing card", e); continue
        fit = {n for n, pr in preds.items() if pr == pack}
        before = len({tuple(preds[n]) for n in alive_fx}) if alive_fx else 0
        alive &= fit; alive_fx &= fit
        rows.append((draft["startTime"], p["PackNumber"], p["PickNumber"], len(pack), len(fit), before, len(alive), len(alive_fx)))
print(f"DB {db.version()} | rules {len(rules)} | fixture survivors {len(fixture_survivors)}")
print(" draft-start     coord  n  rulesFit  predictedOrdersAmongSurvivors  alive(400-only)  alive(fixtures+untapped)")
for r in rows: print(f" {r[0]}  P{r[1]}P{r[2]:<3} {r[3]:>2}  {r[4]:>8}  {r[5]:>8}  {r[6]:>8}  {r[7]:>8}")
print("\npacks fitted by >=1 rule:", sum(1 for r in rows if r[4] > 0), "/", len(rows))
print("survivors from all 400 using only Untapped orders:", sorted(alive)[:30], "..." if len(alive) > 30 else "")
print("survivors (fixtures + Untapped orders):", len(alive_fx), sorted(alive_fx)[:40])
