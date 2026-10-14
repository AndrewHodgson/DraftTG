# Phase 9F.3 physical validation

Only an actual user observation can check an item. Machine reads, tests and renderer diagnostics do not establish physical alignment. Record draft/pack/pick, client pixels, DPI, profile, date and ledger for each check. Stop using badges and report any mismatch.

Confirmed in this conversation: **P1P12, three cards, 1600×900 client pixels, 96 DPI**, list mode. User confirmed “All three aligned” during the repeated debug overlay. See `docs/evidence/phase-9f3/physical-confirmation.json`. Earlier 14-card validation is inherited from Phase 9F.2, not a new validation of the updated build.

## Commands

```powershell
Set-Location 'C:\Users\Graphics0\Documents\Projects\DraftTG'
dotnet build DraftTG.sln
dotnet .\src\DraftTG.App\bin\Debug\net10.0-windows10.0.19041.0\DraftTG.App.dll --capture-free-draft-overlay --localization-debug --ledger .\artifacts\phase9f3\live.jsonl
# Optional passive console snapshot, without an overlay:
dotnet .\src\DraftTG.App\bin\Debug\net10.0-windows10.0.19041.0\DraftTG.App.dll --arena-ui-localization-debug --seconds 10 --ledger .\artifacts\phase9f3\snapshot.jsonl
# After closing the overlay, summarize the same ledger:
python .\tools\phase9f3_summary.py --ledger .\artifacts\phase9f3\live.jsonl --output .\docs\evidence\phase-9f3\live-summary.json
dotnet .\tools\DraftTG.RatingAudit\bin\Debug\net10.0\DraftTG.RatingAudit.dll --set FRA --format QuickDraft --pack-ledger .\artifacts\phase9f3\live.jsonl --output .\artifacts\rating-audit
```

## Offered pack

- [ ] 14 cards on the updated exact build
- [ ] 13 cards
- [ ] 12 cards
- [ ] 10 cards
- [ ] 8 cards
- [ ] 5 cards
- [x] 3 cards: P1P12 at 1600×900 / 96 DPI; all three outlines/labels aligned
- [ ] 1 card
- [ ] Pick transition: outgoing badges disappear immediately; next pack restores only after stable reads
- [ ] Rapid pick transition: no stale badges return
- [ ] Move without resize: overlay translates, no geometry invalidation/full capture
- [ ] Resize: retire first, redisplay after fresh stable memory/window state
- [ ] Maximize/restore: same retirement rule; unsupported dimensions suppress
- [ ] 100% scaling at a second client size
- [ ] 125% scaling, if practical
- [ ] 150% scaling, if practical
- [ ] Monitor/DPI transition, if practical
- [ ] Unsupported column/grid/layout: badges suppressed; identity diagnostics may continue
- [ ] Preferred basic-land printing mismatch: existing guard suppresses rather than guessing

## Drafted deck

The current build reads both holder collections and quantities. **Deck anchors are unresolved, so no deck badges should appear**, even while scrolling. Collection index is not proven visual row order.

- [ ] Initial few cards: compare memory entries/quantities to Arena
- [ ] 10+ drafted cards: compare main/side groups and total quantity
- [ ] Duplicate quantities: compare a repeated card's grouped quantity
- [ ] Top of list: deck badges remain suppressed until anchors are qualified
- [ ] Middle scroll: normalized scrollbar changes recorded; no stale guessed anchors
- [ ] Bottom scroll
- [ ] Rapid scroll
- [ ] Resize
- [ ] Sideboard collapsed: identity collection may exist but is not asserted visible
- [ ] Sideboard expanded: geometry and expansion state currently unsupported
- [ ] Move cards between main/side if UI permits: check collection/quantity transition
- [ ] Pick updates: deck collection updates coherently

Live memory at P1P12 observed eight main groups / nine cards and two side groups / two cards, including GrpId 106520 quantity two. That is **memory evidence**, not a physical confirmation of deck order, visibility or alignment.

## Observation record

Date / tester:

Draft/session / pack / pick:

Client pixels / DPI / layout / profile:

Ledger / event / generation:

What was compared and whether every occurrence aligned:

Mismatch, suppression or unsupported reason:
