#!/usr/bin/env python3
"""Arena draft-pack display-order probe (diagnostic prototype, read-only).

Tests the hypothesis that MTG Arena lays out draft-pack cards in an order that is a
deterministic function of per-card sort keys Arena itself ships in its local card
database (Raw_CardDatabase_*.mtga, table Cards, columns Order_*).

This is NOT production code and is not referenced by DraftTG.sln.
* Opens the Arena SQLite database strictly read-only (URI mode=ro). Never writes game files.
* Reads Player.log as text only. Prints card IDs/pack coordinates, never account data.
* Python 3.9+ standard library only.

Commands
  fixtures   Score every hypothesis against the repo's ground-truth fixtures.
  predict    Predict the visual order of one pack under every surviving hypothesis.
  log        Find draft packs in a Player.log and predict each (flags discriminating packs).
  observe    Append a ground-truth observation (pack + confirmed visual order) to a ledger.
  ledger     Re-score all hypotheses against fixtures + ledger observations.
  simulate   Estimate how often surviving hypotheses disagree on random packs of a set.

See README.md next to this file.
"""
from __future__ import annotations

import argparse
import glob
import itertools
import json
import os
import random
import re
import sqlite3
import sys
from collections import Counter, defaultdict
from pathlib import Path

# --------------------------------------------------------------------------- database

DB_GLOBS = [
    r"C:\Program Files (x86)\Steam\steamapps\common\MTGA\MTGA_Data\Downloads\Raw\Raw_CardDatabase_*.mtga",
    r"C:\Program Files\Wizards of the Coast\MTGA\MTGA_Data\Downloads\Raw\Raw_CardDatabase_*.mtga",
    os.path.expanduser("~/Library/Application Support/com.wizards.mtga/Downloads/Raw/Raw_CardDatabase_*.mtga"),
]

COLUMNS = ["GrpId", "ExpansionCode", "CollectorNumber", "Rarity", "TitleId", "Order_MythicToCommon",
           "Order_ColorOrder", "Order_Title", "Order_LandLast", "Order_CreaturesFirst",
           "Order_CMCWithXLast", "Order_BasicLandsFirst", "Order_ManaCostDifficulty"]


def find_db(explicit: str | None) -> str:
    if explicit:
        return explicit
    hits = sorted((p for g in DB_GLOBS for p in glob.glob(g)), key=os.path.getmtime, reverse=True)
    if not hits:
        sys.exit("Raw_CardDatabase_*.mtga not found; pass --db <path>.")
    return hits[0]


class CardDb:
    def __init__(self, path: str):
        uri = "file:" + Path(path).resolve().as_posix() + "?mode=ro"
        self.db = sqlite3.connect(uri, uri=True)
        self.path = path
        self._cache: dict[int, dict] = {}

    def version(self) -> dict:
        return dict(self.db.execute("select Type, Version from Versions").fetchall())

    def card(self, grp: int) -> dict:
        if grp not in self._cache:
            row = self.db.execute(f"select {','.join(COLUMNS)} from Cards where GrpId=?", (grp,)).fetchone()
            if row is None:
                raise KeyError(f"GrpId {grp} not in Arena card database")
            c = dict(zip(COLUMNS, row))
            name = self.db.execute("select Loc from Localizations_enUS where LocId=? order by Formatted limit 1",
                                   (c["TitleId"],)).fetchone()
            c["Name"] = name[0] if name else str(grp)
            self._cache[grp] = c
        return self._cache[grp]

    def set_cards(self, code: str) -> list[dict]:
        rows = self.db.execute(
            "select GrpId from Cards where ExpansionCode=? and DraftContent=1 and IsToken=0 and IsPrimaryCard=1 "
            "and Order_MythicToCommon is not null", (code,)).fetchall()
        return [self.card(r[0]) for r in rows]


# --------------------------------------------------------------------------- hypotheses

def _collector(c: dict) -> int:
    digits = "".join(ch for ch in (c["CollectorNumber"] or "") if ch.isdigit())
    return int(digits) if digits else 10 ** 6


def _n(v):  # NULL sort keys sort last; they occur for some non-draft/token rows.
    return (1, 0) if v is None else (0, v)


KEYS = {
    "color": lambda c: _n(c["Order_ColorOrder"]),
    "landLast": lambda c: _n(c["Order_LandLast"]),
    "creaturesFirst": lambda c: _n(c["Order_CreaturesFirst"]),
    "cmc": lambda c: _n(c["Order_CMCWithXLast"]),
    "basicLandsFirst": lambda c: _n(c["Order_BasicLandsFirst"]),
    "title": lambda c: _n(c["Order_Title"]),
    "collector": _collector,
    "grpId": lambda c: c["GrpId"],
}


def hypotheses(max_secondary: int = 3) -> dict[str, callable]:
    """Every observed pack groups by rarity first, so rarity is the fixed primary key.
    Then any ordered choice of up to `max_secondary` other Arena sort keys, GrpId tiebreak."""
    out = {}
    names = list(KEYS)
    for n in range(1, max_secondary + 1):
        for combo in itertools.permutations(names, n):
            label = "rarity>" + ">".join(combo)
            def f(c, combo=combo):
                return (_n(c["Order_MythicToCommon"]),) + tuple(KEYS[k](c) for k in combo) + (c["GrpId"],)
            out[label] = f
    return out


def predict(db: CardDb, pack: list[int], rule) -> list[int]:
    return sorted(pack, key=lambda g: rule(db.card(g)))  # stable: duplicate GrpIds stay together


def consistent(db: CardDb, pack: list[int], visual: list[int], rule) -> bool:
    return predict(db, pack, rule) == list(visual)


def canonical(predictions: dict[str, list[int]]) -> dict[tuple, list[str]]:
    groups = defaultdict(list)
    for name, order in predictions.items():
        groups[tuple(order)].append(name)
    return groups


# --------------------------------------------------------------------------- observations

def fixture_observations(repo: Path) -> list[dict]:
    obs = []
    p6 = repo / "tests/Fixtures/arena-woe-p1p6-visual-placement.json"
    if p6.exists():
        j = json.loads(p6.read_text(encoding="utf-8"))
        obs.append({"source": p6.name, "label": "WOE P1P6",
                    "pack": [int(x) for x in j["LogPack"]["DraftPack"]], "visual": j["VisualArenaIds"]})
    for d in ("arena-artwork-live", "arena-wgc-p1p7"):
        m = repo / "tests/Fixtures" / d / "manifest.json"
        if not m.exists():
            continue
        j = json.loads(m.read_text(encoding="utf-8-sig"))
        by_id = {c["Id"]: c["ArenaId"] for c in j["Cards"]}
        visual = [by_id[i] for i in j["ExpectedVisualOrder"]]
        label = f"P{j['PackNumber']}P{j['PickNumber']} ({d})"
        obs.append({"source": f"{d}/manifest.json", "label": label,
                    "pack": [c["ArenaId"] for c in j["Cards"]], "visual": visual})
    # The P1P6 JSON and arena-artwork-live manifest describe the same physical pack; de-duplicate.
    seen, unique = set(), []
    for o in obs:
        k = tuple(sorted(o["pack"]))
        if k not in seen:
            seen.add(k)
            unique.append(o)
    return unique


def ledger_observations(path: Path | None) -> list[dict]:
    if not path or not path.exists():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def score(db: CardDb, observations: list[dict], rules: dict) -> tuple[list[str], dict]:
    survivors, refuted_by = [], {}
    for name, rule in rules.items():
        bad = [o["label"] for o in observations if not consistent(db, o["pack"], o["visual"], rule)]
        if bad:
            refuted_by[name] = bad
        else:
            survivors.append(name)
    return survivors, refuted_by


# --------------------------------------------------------------------------- log scanning

PACK_PATTERNS = [
    re.compile(r'"DraftPack"\s*:\s*\[([0-9",\s]*)\]'),
    re.compile(r'\\?"DraftPack\\?"\s*:\s*\[([0-9\\",\s]*)\]'),
    re.compile(r'"PackCards"\s*:\s*"([0-9,\s]*)"'),
    re.compile(r'\\?"PackCards\\?"\s*:\s*\\?"([0-9,\s]*)\\?"'),
]
PACK_NO = re.compile(r'\\?"(PackNumber|SelfPack)\\?"\s*:\s*(\d+)')
PICK_NO = re.compile(r'\\?"(PickNumber|SelfPick)\\?"\s*:\s*(\d+)')


def coordinate(line: str) -> str:
    """BotDraft PackNumber/PickNumber are zero-based; Draft.Notify SelfPack/SelfPick are one-based."""
    pk, pc = PACK_NO.search(line), PICK_NO.search(line)
    if not (pk and pc):
        return "P?P?"
    pack = int(pk.group(2)) + (1 if pk.group(1) == "PackNumber" else 0)
    pick = int(pc.group(2)) + (1 if pc.group(1) == "PickNumber" else 0)
    return f"P{pack}P{pick}"


def packs_in_log(text: str) -> list[dict]:
    out, seen = [], set()
    for line in text.splitlines():
        for pat in PACK_PATTERNS:
            for m in pat.finditer(line):
                ids = [int(x) for x in re.findall(r"\d+", m.group(1))]
                if not (1 <= len(ids) <= 15):
                    continue
                coord = coordinate(line)
                key = (coord, tuple(ids))
                if key not in seen:
                    seen.add(key)
                    out.append({"coord": coord, "pack": ids})
    return out


# --------------------------------------------------------------------------- commands

def cmd_fixtures(a, db):
    rules = hypotheses(a.max_secondary)
    obs = fixture_observations(Path(a.repo)) + ledger_observations(Path(a.ledger) if a.ledger else None)
    print(f"Arena DB: {db.path}  versions={db.version()}")
    print(f"Observations: {len(obs)}  hypotheses tested: {len(rules)}")
    for o in obs:
        names = [db.card(g)["Name"] for g in o["visual"]]
        print(f"  {o['label']}: {len(o['pack'])} cards, log order == visual? {o['pack'] == o['visual']}")
        print("    visual: " + " | ".join(names))
    survivors, refuted = score(db, obs, rules)
    print(f"\nSurviving hypotheses: {len(survivors)} / {len(rules)}")
    for s in survivors[: a.show]:
        print("  " + s)
    if len(survivors) > a.show:
        print(f"  ... {len(survivors) - a.show} more")
    for name in ("rarity>color>title", "rarity>color>collector", "rarity>collector", "rarity>color>cmc>title"):
        if name in rules:
            print(f"  [{'FITS' if name in survivors else 'REFUTED by ' + ','.join(refuted[name])}] {name}")
    return survivors


def cmd_predict(a, db):
    rules = hypotheses(a.max_secondary)
    obs = fixture_observations(Path(a.repo)) + ledger_observations(Path(a.ledger) if a.ledger else None)
    survivors, _ = score(db, obs, rules)
    pack = [int(x) for x in re.findall(r"\d+", a.pack)]
    report_pack(db, pack, {n: rules[n] for n in survivors}, "pack")


def report_pack(db, pack, rules, label):
    groups = canonical({n: predict(db, pack, r) for n, r in rules.items()})
    status = "UNANIMOUS" if len(groups) == 1 else f"DISCRIMINATING ({len(groups)} distinct predicted orders)"
    print(f"\n{label}: {len(pack)} cards -> {status}")
    for order, names in sorted(groups.items(), key=lambda kv: -len(kv[1])):
        print(f"  {len(names):3d} hypotheses -> " + " | ".join(db.card(g)["Name"] for g in order))
    return len(groups) == 1


def cmd_log(a, db):
    rules = hypotheses(a.max_secondary)
    obs = fixture_observations(Path(a.repo)) + ledger_observations(Path(a.ledger) if a.ledger else None)
    survivors, _ = score(db, obs, rules)
    text = Path(a.log).read_text(encoding="utf-8", errors="replace")
    packs = packs_in_log(text)
    print(f"{len(packs)} draft pack observations found in {a.log}")
    unanimous = sum(report_pack(db, p["pack"], {n: rules[n] for n in survivors}, p["coord"]) for p in packs)
    print(f"\nUnanimous predictions: {unanimous}/{len(packs)}. Confirm DISCRIMINATING packs visually and record them with 'observe'.")


def cmd_observe(a, db):
    pack = [int(x) for x in re.findall(r"\d+", a.pack)]
    visual = [int(x) for x in re.findall(r"\d+", a.visual)]
    if sorted(pack) != sorted(visual):
        sys.exit("visual order must be a permutation of the pack multiset")
    for g in pack:
        db.card(g)  # validates IDs exist
    rec = {"label": a.label, "pack": pack, "visual": visual, "source": "manual-observe"}
    with open(a.ledger, "a", encoding="utf-8") as f:
        f.write(json.dumps(rec) + "\n")
    print(f"Recorded {a.label} in {a.ledger}")
    cmd_fixtures(a, db)


def cmd_simulate(a, db):
    rules = hypotheses(a.max_secondary)
    obs = fixture_observations(Path(a.repo)) + ledger_observations(Path(a.ledger) if a.ledger else None)
    survivors, _ = score(db, obs, rules)
    cards = db.set_cards(a.set)
    bonus = db.set_cards(a.bonus) if a.bonus else []
    by = defaultdict(list)
    for c in cards:
        by[c["Order_MythicToCommon"]].append(c["GrpId"])
    if not by:
        sys.exit(f"No draft cards for {a.set}")
    rnd = random.Random(a.seed)
    agree, n, distinct = 0, a.packs, Counter()
    for _ in range(n):
        rare = rnd.choice(by[0]) if by.get(0) and rnd.random() < 1 / 7.4 else rnd.choice(by[1])
        pack = [rare] + [rnd.choice(by[2]) for _ in range(3)] + [rnd.choice(by[3]) for _ in range(9 if bonus else 10)]
        if bonus:
            pack.append(rnd.choice(bonus)["GrpId"])
        pack = rnd.sample(pack, rnd.randint(1, len(pack)))
        k = len(canonical({s: predict(db, pack, rules[s]) for s in survivors}))
        distinct[k] += 1
        agree += k == 1
    print(f"{a.set}{'+' + a.bonus if a.bonus else ''}: {len(survivors)} surviving hypotheses agree on "
          f"{agree / n:.1%} of {n} simulated packs (sizes 1-14/15).")
    print("  distinct predicted orders per pack: " + ", ".join(f"{k}:{v}" for k, v in sorted(distinct.items())))


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--db", help="Path to Raw_CardDatabase_*.mtga (auto-detected on Windows/macOS)")
    p.add_argument("--repo", default=str(Path(__file__).resolve().parents[2]), help="DraftTG repository root")
    p.add_argument("--ledger", help="JSONL file of additional confirmed observations")
    p.add_argument("--max-secondary", type=int, default=3)
    p.add_argument("--show", type=int, default=25)
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("fixtures")
    sp = sub.add_parser("predict"); sp.add_argument("--pack", required=True)
    sl = sub.add_parser("log"); sl.add_argument("--log", required=True)
    so = sub.add_parser("observe"); so.add_argument("--pack", required=True); so.add_argument("--visual", required=True)
    so.add_argument("--label", required=True)
    sub.add_parser("ledger")
    ss = sub.add_parser("simulate"); ss.add_argument("--set", required=True); ss.add_argument("--bonus")
    ss.add_argument("--packs", type=int, default=5000); ss.add_argument("--seed", type=int, default=1)
    a = p.parse_args(argv)
    if a.cmd == "observe" and not a.ledger:
        p.error("observe requires --ledger")
    db = CardDb(find_db(a.db))
    {"fixtures": cmd_fixtures, "ledger": cmd_fixtures, "predict": cmd_predict, "log": cmd_log,
     "observe": cmd_observe, "simulate": cmd_simulate}[a.cmd](a, db)


if __name__ == "__main__":
    main()
