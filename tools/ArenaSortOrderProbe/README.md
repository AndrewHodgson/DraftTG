# Arena sort-order probe (diagnostic prototype)

Isolated, read-only research tool for [CLAUDE_LOCALIZATION_REVIEW.md](../../CLAUDE_LOCALIZATION_REVIEW.md). It is **not** part of `DraftTG.sln` and is not production code.

It tests one hypothesis: *MTG Arena's draft-pack display order is a deterministic function of per-card sort keys that Arena ships in its own local card database* (`Raw_CardDatabase_*.mtga`, SQLite table `Cards`, columns `Order_MythicToCommon`, `Order_ColorOrder`, `Order_Title`, `Order_LandLast`, `Order_CreaturesFirst`, `Order_CMCWithXLast`, `Order_BasicLandsFirst`, plus `CollectorNumber`/`GrpId`).

If the hypothesis holds, Player.log identity + this database gives every card's **visual slot index** with zero pixels. Capture is then only needed to find slot rectangles and to verify.

## Safety

- Opens the Arena database with SQLite URI `mode=ro`. Never writes or copies game files.
- Reads Player.log as text and prints only card IDs/names and pack coordinates.
- No process access, no injection, no capture, no network.
- Python 3.9+ standard library only.

## Usage

Run from the repository root. The database is auto-detected under the Steam or default Windows install, or `~/Library/Application Support/com.wizards.mtga/Downloads/Raw/` on macOS; otherwise pass `--db`.

```powershell
# 1. Score all 400 hypotheses (rarity first, then up to three other keys) against the repo's ground-truth fixtures
python tools/ArenaSortOrderProbe/arena_sort_order_probe.py fixtures

# 2. During or after a draft: predict every pack found in Player.log, flag packs where surviving hypotheses disagree
python tools/ArenaSortOrderProbe/arena_sort_order_probe.py log --log "$env:USERPROFILE\AppData\LocalLow\Wizards Of The Coast\MTGA\Player.log"

# 3. For a DISCRIMINATING pack, look at Arena, then record the true left-to-right, top-to-bottom order
python tools/ArenaSortOrderProbe/arena_sort_order_probe.py --ledger artifacts/sort-order-ledger.jsonl observe --label "FRA P1P3" --pack "1,2,3,..." --visual "3,1,2,..."

# 4. Re-score with fixtures + ledger
python tools/ArenaSortOrderProbe/arena_sort_order_probe.py --ledger artifacts/sort-order-ledger.jsonl ledger

# 5. How often do the surviving hypotheses disagree on random packs of a set?
python tools/ArenaSortOrderProbe/arena_sort_order_probe.py simulate --set WOE --bonus WOT
```

Keep the ledger under the ignored `artifacts/` directory. It contains card IDs only.

## Interpreting results

- **Surviving hypotheses** are sort rules consistent with every observation. Many survive today because two packs cannot separate them.
- A pack is **UNANIMOUS** when every surviving rule predicts the same order. Those predictions are as safe as the hypothesis family itself.
- A pack is **DISCRIMINATING** when rules disagree. Confirming its real order eliminates rules. Roughly half of simulated packs are discriminating, so one complete draft should collapse the family.
- If every hypothesis is eventually refuted, Arena's order is not a simple function of these keys, and the deterministic-order architecture must be abandoned in favor of the visual locator.

Results as of 2026-10-05 (Arena data `2026.63.0.270`) are in the review document, section 13.

Phase 9E.1 ports this hypothesis family unchanged to `DraftTG.Application.ArenaDisplayOrderModel`. DraftTG now records confirmed orders automatically in `<app data>/localization/order-evidence.jsonl` and summarizes them with `dotnet run --project src/DraftTG.App -- --order-evidence-summary`. That ledger uses its own self-contained schema. This probe remains an offline research tool. See [PHASE9E_1_REPORT.md](../../PHASE9E_1_REPORT.md).
