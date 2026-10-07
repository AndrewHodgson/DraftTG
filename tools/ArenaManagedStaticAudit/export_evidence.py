"""Read-only exporter of existing sanitized repo orders and local Arena DB inputs.

Writes analysis JSON only to the requested destination. Never writes game files or
the production evidence ledger; never reads Untapped storage or process memory.
Python standard library only. Replay with the isolated C# `reference` command.
"""
import argparse
import json
import re
import sqlite3
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--db", required=True)
parser.add_argument("--output", required=True)
parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
parser.add_argument("--ledger", type=Path)
args = parser.parse_args()
db = sqlite3.connect(Path(args.db).resolve().as_uri() + "?mode=ro", uri=True)
db.row_factory = sqlite3.Row

def mask(value):
    result = 0
    for color in str(value or "").split(","):
        if color and 1 <= int(color) <= 5:
            result |= 1 << (int(color) - 1)
    return result

def card(grp):
    c = db.execute("select * from Cards where GrpId=?", (grp,)).fetchone()
    if c is None:
        raise ValueError(f"Missing Arena card {grp}")
    loc = db.execute("select Loc from Localizations_enUS where LocId=? and Formatted=0", (c["TitleId"],)).fetchone()
    # Arena localization falls back to Formatted=1 when Formatted=0 is absent.
    if loc is None:
        loc = db.execute("select Loc from Localizations_enUS where LocId=? and Formatted=1", (c["TitleId"],)).fetchone()
    types = set(str(c["Types"] or "").split(","))
    mapping = {"MythicToCommon":"Order_MythicToCommon", "ColorOrder":"Order_ColorOrder", "Title":"Order_Title",
               "CmcWithXLast":"Order_CMCWithXLast", "CreaturesFirst":"Order_CreaturesFirst", "LandLast":"Order_LandLast",
               "BasicLandsFirst":"Order_BasicLandsFirst", "CollectorNumber":"CollectorNumber", "ExpansionCode":"ExpansionCode"}
    return dict(GrpId=grp, Rarity=c["Rarity"], IsLand="5" in types, IsArtifact="1" in types,
                ColorFlags=mask(c["Colors"]), ColorIdentityFlags=mask(c["ColorIdentity"]), LocalizedTitle=loc[0] if loc else "",
                HypothesisKeys={"GrpId":grp, **{name:c[column] for name,column in mapping.items()}})

metadata = json.loads((args.repo / "tests/Fixtures/phase9e2a1-replay-metadata.json").read_text(encoding="utf-8-sig"))
observations = [dict(Label=o["Label"], Log=o["Log"], Visual=o["Visual"]) for o in metadata["PriorEvidenceForAuditedCaptures"]]
script = (args.repo / "tools/ReplayDeterministicSlotEvidence.ps1").read_text(encoding="utf-8-sig")
for fixture, variable in [("p1p1-1723", "taskP1"), ("p1p2-1723", "taskP2")]:
    log = [int(x) for x in re.search(r"\$" + variable + r" = '([0-9,]+)'", script)[1].split(",")]
    capture = next(x for x in metadata["Fixtures"] if x["Fixture"] == fixture)
    passing = [x for x in capture["FastCandidates"] if x["PassingSlots"] == capture["CardCount"]]
    if len(passing) != 1:
        raise ValueError("Saved audit does not establish a unique fully verified order")
    visual = [x["GrpId"] for x in sorted(passing[0]["Slots"], key=lambda s:s["Slot"])]
    observations.append(dict(Label=fixture, Log=log, Visual=visual))
if args.ledger and args.ledger.exists():
    for number, line in enumerate(args.ledger.read_text(encoding="utf-8-sig").splitlines(), 1):
        if not line.strip():
            continue
        o = json.loads(line)
        observations.append(dict(Label=f"ledger row {number}", Log=o["LogOrder"], Visual=o["VisualOrder"]))
for o in observations:
    if sorted(o["Log"]) != sorted(o["Visual"]):
        raise ValueError("Visual order does not match incoming multiset")
    o["Cards"] = [card(grp) for grp in sorted(set(o["Log"]))]
universe = [card(r[0])["HypothesisKeys"] for r in db.execute(
    "select GrpId from Cards where ExpansionCode in ('ANA','FRA','USG','WOE','WOT') "
    "and DraftContent=1 and IsToken=0 and IsPrimaryCard=1 and Order_MythicToCommon is not null").fetchall()]
Path(args.output).write_text(json.dumps(dict(Culture="en-US", Observations=observations, UniverseKeys=universe), indent=2), encoding="utf-8")
print(f"Exported {len(observations)} observations; DB versions: " + str([tuple(r) for r in db.execute("select * from Versions")]))
db.close()
