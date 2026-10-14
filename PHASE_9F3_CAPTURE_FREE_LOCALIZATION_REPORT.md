# Phase 9F.3 — Capture-Free Arena Localization

## 1. Executive status

Capture-free shrinking-pack placement and localization diagnostics are implemented. **Localization is not fully complete:** exact deck viewport-to-client transforms remain unresolved, so deck badges fail closed. The existing offered-pack printing disagreement guard remains unchanged and can suppress packs containing preferred basic-land artwork.

The user physically confirmed **three-card P1P12 placement at 1600×900 / 96 DPI** on the updated Arena build. No other new geometry is called physically validated. The existing Phase 9F.2 14-card result is inherited evidence, not a fresh current-build test. Production `contextual-pick-score-v1.6.2`, all scoring components, k500/FRA prior, tiers and deck builder are unchanged. No V2 research was run; MH3/DFT remain untouched.

## 2. Current pack-memory path

`WrapperController.Instance → SceneLoader.CurrentNavContent → DraftContentController._draftPackHolder._allCardViews[i]._visualCard._printing.Record.GrpId`. Scene active/loading/animation, full occurrence/list/quantity, pod multiset and re-read checks remain intact. Player.log supplies history/pool/format; two coherent memory reads establish the current pack.

The installed Arena updated three game assemblies. The first read correctly returned UnsupportedBuild with zero traversal bytes. Current metadata was independently inspected using the existing static audit tool. A second exact profile was added; the earlier profile remains supported. Executable, UnityPlayer, Mono and framework hashes are unchanged. No native offset was changed or guessed. Fingerprints appear in the snapshots under `docs/evidence/phase-9f3/`.

Opening-pack investigation found pod GrpId **106530** vs visual **11809**. Arena's local database identifies both as Swamp, FRA #285 vs USG #340. That explains a preferred-printing presentation difference but does not satisfy the existing exact-ID contract. The pack was suppressed; there was no name-based reconciliation or partial pack success. P1P12 later passed all existing checks with three exact IDs.

## 3. Pack geometry model

Reuse `ArenaDraftSlotGeometry`, without widening its Phase 9E shadow envelope. For client width W, height H, scale s=H/1009, compact visual index i:

`x = W/2 + s*(-634.5 + 183.25*(i%5))`

`y = s*(175.1 + 250*floor(i/5))`

`width = 158.5*s; height = 216.8*s`.

Pitch is 183.25×250 reference units; horizontal gap 24.75, vertical gap 33.2. Client-relative normalized rectangles feed the existing badge renderer. Overlay origin is ClientToScreen(client origin), and physical dimensions are divided by known scaling for Avalonia DIPs. Badge anchoring remains the existing renderer's placement policy.

Current Core.dll `DraftPackHolder.LayoutCards` token 0x0600754D uses **current list index modulo/divided by ColumnCount** and fixed UpperLeft + Offset. Its count-dependent column branch executes only when RowCount is nonzero. Live list layout is Columns=5, Rows=0, Scale=43, Offset=(196,-268). These exact live parameters are required for the new path; differing parameters decline. Remaining cards compact into the first N slots, with no centering or count-dependent shift. This follows audited IL and live memory, not card-count guessing. Static audit traces remain under ignored `artifacts/phase9f3-*-il.txt`.

## 4. Supported pack sizes

| Scope | Implemented | Physically validated |
| --- | --- | --- |
| 13–14 list cards | Existing measured slots, current state/envelope guards | Earlier 14-card Phase 9F.2 result; updated opening pack declined |
| 1–12 list cards | Compact prefix of existing slots, exact stable live layout required | **3 cards**, P1P12, 1600×900, 96 DPI |
| 15+ cards | Suppressed | None |

Passing a synthetic test or seeing mapped renderer models does not validate a new physical geometry.

## 5. Layout modes

`_activeDeckView._isColumnView` remains the memory source. List is supported within the stated envelope. Column, unknown modes, nonzero RowCount, different spacing/scale and unqualified layouts suppress badges. No count-based mode inference and no capture fallback were added.

## 6. DPI/window handling

Windowed client H=720–1080 pixels, aspect W/H=1.70–1.78. Native GetClientRect/ClientToScreen/GetDpiForWindow remain the coordinate source. Live 9F.3 tests used 1600×900, DPI=96. 125%/150%, second size, monitor transition and maximize/restore remain unvalidated. Transform conversion is implemented; physical correctness at those states is not asserted.

## 7. Resize invalidation

Width/height, scaling, HWND, visibility/minimize or window-style changes immediately retire placement/debug bounds and increment UI geometry generation. Queued reads predating invalidation are rejected. Redisplay requires repeated fresh UI/window state spanning at least 150ms plus the existing current-pack generation/context guards. Movement changes overlay origin without invalidating geometry or asking for capture/full localization. Layout/deck/scroll changes also alter the combined UI safety generation. Existing accepted-pick retirement remains intact.

## 8. Deck-memory path

`DraftContentController._activeDeckView._mainHolderInstance / _sideHolderInstance → DraftListMetaCardHolder (ListMetaCardHolder_Expanding base) → _tiles[i] → ListMetaCardView_Expanding._cardData._printing.Record.GrpId`.

The exact current holder subclass and base fields were statically inspected and passively observed. An initial use of `_visualCard` declined on populated rows; static `MetaCardView.Card` and `VisualCard` accessors established represented `_cardData` as the correct new deck identity source. Offered-pack contracts were not changed.

## 9. Deck identity/order

At live P1P12: eight main groups totaling nine cards, two side groups totaling two cards. Group 106520 had quantity two. Every tile printing and quantity, holder/list identity/version and entry sequence is reread. Torn/invalid traversals decline as a whole. `_tiles` index is reported as collection order; **actual visual sibling/row order is unqualified**.

## 10. Main/sideboard behavior

Separate holder identities/quantities are captured. Sideboard-expanded/toggle state is currently null in live snapshots; neither holder membership nor collection presence implies visibility. Both main and side deck geometry are suppressed until the corresponding active/viewport mapping is qualified.

## 11. Deck row geometry

No guessed parent rectangle, row pitch or native transform offsets were installed. Debug rows explicitly carry Rectangle=null, Visible=null and Culled=null, with source provenance and a suppression reason. Compact score/tier presentation can use the existing renderer once anchors are qualified; this phase does not draw unlocated deck ratings.

## 12. Scrolling

Normalized `_scrollbar.m_Value` is captured through managed field metadata. UnityEngine.UI.dll is additionally pinned to SHA256 4BF50572EE01F1A62147142CC9750058EC01DFB3790B46BE9B42D207A4BD3797 for this optional traversal. Main/side values were 0/1 in the measured state. They are **not pixel offsets**. Changes enter the combined UI generation and ledger; deck badges stay suppressed. Top/middle/bottom/rapid scrolling and viewport culling still require qualification.

## 13. Quantity handling

Managed `<Quantity>k__BackingField` supplies bounded positive group quantity. Duplicate identities across holders remain distinct entries. Live quantity two was read; no OCR is used. Physical confirmation of duplicate labeling and movement between holders remains pending.

## 14. Debug tooling

`--arena-ui-localization-debug [--seconds N] [--ledger PATH]` emits compact JSON snapshots with build, window/DPI, pack, deck rows, scroll, geometry generation, stable-read pack generation, reasons and timings. IntPtr is converted to a numeric handle at the serialization boundary. Read-only ledger aggregation: `python tools/phase9f3_summary.py --ledger PATH [--output JSON]`.

`--capture-free-draft-overlay --localization-debug` draws opt-in offered-card bounds/labels with slot, GrpId, name, rectangle and generations. Ordinary UX has an empty debug collection. Unknown deck rectangles remain null in diagnostics, never drawn. Meaningful changes are deduplicated; performance samples are batched, not logged as unchanged 10Hz state rows.

## 15. Physical validation status

User opened QuickDraft, advanced themselves to P1P12 and confirmed all three outlines aligned during the repeated 45-second overlay. See [confirmation](docs/evidence/phase-9f3/physical-confirmation.json) and [checklist](PHASE_9F3_PHYSICAL_VALIDATION.md). Renderer diagnostics independently showed three mapped/rendered badges, but human confirmation is the placement evidence. No automated clicks, picks, scrolling, resizing or display-setting changes occurred.

## 16. Unsupported cases

Column/unknown layout, 15+, unsupported size/aspect/window style, mismatched preferred printing, incomplete/unreconciled pack/history, stale generations, unproven main/side native visibility/transforms, sideboard expansion and deck culling. Each is withheld, with no WGC localization inserted into the capture-free branch. The ordinary application's authority/composition is unchanged.

## 17. Performance

Measured P1P12 overlay: attach 668.55ms; 172 steady reads mean 21.38ms / p95 23.01ms. Combined UI traversal mean 7.71ms / p95 7.76ms (includes first initialization); deck traversal mean 6.85ms / p95 7.66ms. Five placement-model updates mean 10.84ms / maximum 48.78ms; this excludes actual GPU rendering. Scroll-position/row-anchor timings are unavailable because those anchors are unsupported. These are short live measurements, not broad benchmarks. See [overlay summary](docs/evidence/phase-9f3/overlay-summary.json).

## 18. Safety/read-only audit

OpenProcess rights remain **0x410**, inheritance disabled. No target writes, allocation, remote threads, injections, managed target calls or debugger attachment. Native Mono offsets are unchanged. Unknown fingerprints still fail before Mono/object traversal. Existing capture-exclusion tests pass; live runs reported no capture initialization/rejected attempts. Local static metadata inspection never executes Arena assemblies. Scoring sources, tiers and deck builder are unchanged; all 40 golden regressions pass.

## 19. Remaining localization work

Qualify exact deck viewport/client transform, visible sibling order, active/culled rows and scroll pixel offsets; qualify sideboard expansion. Design an explicitly reviewed logical-vs-preferred-printing contract for the Swamp discrepancy before changing the established pack guard. Complete the physical count/size/DPI/transition matrix. **Do not call localization complete or promote architecture from this partial physical campaign.** Build/tests and compact evidence are in `docs/evidence/phase-9f3/validation.json`. No subsequent phase was started.
