# Phase 9E.2A — Deterministic draft slot locator (shadow validation)

Status: **implemented, shadow mode only.**

The existing WGC + artwork matcher (`AutomaticCardLocalizationSession` → `CardTemplateRecognizer` → `PackVisualAssignment`) remains the only source of badge placement. The deterministic locator is computed and compared, and it never places, hides or moves a badge.

Not changed: matcher thresholds, recommendations (Phases 8 – 10), deck building, card identity fallback, 17Lands behaviour, Phase 9E.1 evidence rules. No process-memory access was added, and no final Arena sort rule was chosen.

Inputs: [UNTAPPED_DRAFTSMITH_LOCALIZATION_AUDIT.md](UNTAPPED_DRAFTSMITH_LOCALIZATION_AUDIT.md) §20 (slot geometry) and [PHASE9E_1_REPORT.md](PHASE9E_1_REPORT.md) (order model).

## Pipeline

```text
pack arrival (PlacementContext changes)
  Player.log occurrences ──► Arena DB sort keys ──► ArenaDisplayOrderModel.Predict(survivors BEFORE this pack)
                                                         │ stored per pack generation
safe automatic placement applied (AutomaticLocalizationApplied — the existing visual path, unchanged)
  unanimous? ──no──► Unavailable (OrderDiscriminating / OrderUnavailable)
     yes
  ArenaDraftSlotGeometry inside validated envelope? ──no──► Unavailable (GeometryUnavailable)
     yes
  zip predicted order → slot rectangles (client px) ──► DeterministicSlotShadowComparer vs visual result
                                                    ──► fixed-slot verifier result (computed on a copy of the matcher's frame)
                                                    ──► one JSONL record + one rail line   (no placement effect)
```

## 1. Geometry: `ArenaDraftSlotGeometry` (DraftTG.Application, pure)

There are no Avalonia, Windows, capture, Scryfall or Player.log dependencies. Coordinates are `ArenaClientRectangle` (double): physical pixels relative to the client origin of Arena's rendering HWND. That is exactly what `ArenaWindowGeometry` reports, via `GetClientRect` + `ClientToScreen`. Nothing is rounded before the platform boundary.

```text
s = clientHeight / 1009.0          column = i % 5      row = i / 5      (row-major; short last row LEFT-aligned)
x = clientWidth / 2 + s · (−634.5 + 183.25 · column)
y = s · (175.1 + 250.0 · row)
w = s · 158.5      h = s · 216.8
```

API:
- `ArenaDraftSlotGeometry.TryCreateLayout(clientWidth, clientHeight, cardCount, ArenaDraftSlotContext)` returns either `Available(ArenaDraftSlotLayout)` or `Unavailable(ArenaDraftSlotRejection, reason)`.
- `ArenaDraftSlotGeometry.Slot(i, w, h)` is the raw formula, for tests and tools.

**Validated envelope (fails closed outside it):**

| Condition | Accepted | Evidence |
|---|---|---|
| Card count | 13–14 | P1P2 (13) and P1P1 (14) |
| Client height | 720–1080 px | 720, 1009, 1080 measured |
| Client aspect | 1.70–1.78 | 1.708 (1723×1009) and 1.778 (16:9) measured; the stated "≈1.71" lower bound is widened to 1.70 so the measured 1.708 case is included |
| View | draft pack grid | the caller states the scene |
| Window mode | captioned window | `ArenaWindowModeProbe` reads `WS_CAPTION`; borderless or exclusive fullscreen → `UnsupportedWindowMode` |

Rejections: `InvalidClientRectangle`, `UnsupportedCardCount`, `UnsupportedHeight`, `UnsupportedAspectRatio`, `UnsupportedScene`, `UnsupportedWindowMode`.

Not validated, and therefore unavailable: 15-card packs, ≤12 cards (especially ≤5), fullscreen, narrower aspect ratios, ultrawide, unusual UI scaling, other layout breakpoints.

## 2. Coordinate systems and conversions

| System | Used by | Conversion |
|---|---|---|
| Arena client px | `ArenaClientRectangle`, deterministic slots | origin = client origin (`ArenaWindowGeometry.X/Y` on screen) |
| Capture-crop normalized | `CardVisualMatch.Rectangle`, `CardVisualLocalizationResult.Rectangles` | `VisualCaptureMapping.FromAnchor(anchor, W, H)` reproduces `ArenaDraftCrop.Calculate`: cropX = round(anchor.X·W), and so on. Then `client = crop.XY + r·crop.Size`. `ToCrop` inverts this and returns null outside the crop |
| Screen px | WGC/overlay placement | client + `ArenaWindowGeometry.X/Y` (not needed by the shadow path) |
| Overlay-local | badges | DraftTG's overlay window covers the calibrated draft region, not the whole client. Converting into it would subtract the region origin. Not needed in shadow mode |

The comparer checks that the visual result's capture size equals the mapped crop size and that the deterministic result's client size equals the mapping's (`Stale` otherwise).

## 3. Order + geometry composition: `DeterministicDraftCardLocator`

`Locate(DeterministicSlotRequest)` takes:
- the pack and its pack generation;
- the Arena log order (GrpIds by pack index);
- the `ArenaDisplayOrderPrediction`;
- the client size and context.

| Prediction | Result |
|---|---|
| `Unanimous` (one predicted order) **and** geometry available | `Available`: assignments in predicted order |
| `Discriminating` (≥ 2 orders, any rule split) | `OrderDiscriminating`. There is **no "most likely rule"**: a 99-to-1 split is rejected |
| `Unavailable` (no DB / missing keys / refuted family) | `OrderUnavailable` |
| Unanimous but outside the envelope | `GeometryUnavailable` |
| Generation ≤ 0, log/pack size mismatch, prediction not a permutation of the pack | `InvalidRequest` |

No rule is chosen or hard-coded. The prediction is `ArenaDisplayOrderModel.Predict` over the survivors of DraftTG's own Phase 9E.1 evidence, snapshotted when the pack arrives. Untapped's private history is not used anywhere in production code.

**Duplicates.** Output is keyed by `CardOccurrenceKey(PackIndex, CardIdentifier)`. Copies of one GrpId take their predicted slots in ascending pack-index order: `Card X copy 1 → slot 0`, `copy 2 → slot 4` (tested). Because copies are visually identical, the comparer counts a copy as agreeing when the slot containing it predicts the same card identity.

**Generation safety.** `DeterministicSlotResult.IsSafeFor(pack, packGeneration, clientWidth, clientHeight)` requires:
- same `DraftPack` (coordinate and multiset);
- same live pack generation;
- same client size;
- full count.

A P1P1 result is never accepted for P1P2, and a result calculated before a resize never applies after it. In the observer, the prediction is stored per pack generation and recomputed against the client size current at comparison time. One record is written per (pack generation, client size).

## 4. Shadow mode: `DeterministicSlotShadowObserver` (App)

Composition (`OverlayDesktopSession`) mirrors the 9E.1 `OrderEvidenceRecorder`: an observer on `AutomaticLocalizationApplied`, with a serialized background queue and exception isolation.

- **Prediction at pack arrival.** It snapshots `OrderEvidenceRecorder.Evaluation` when `PlacementContext` changes, so the 9E.1 recorder (which learns from this pack *after* its placement) can never let a pack be predicted after training on itself.
- **Comparison after placement.** After the visual path applies a safe result, the observer compares; it never calls `ApplyAutomaticLocalization`.
- **Frame hook.** `AutomaticCardLocalizationSession.RecognitionFrameObserver` is a new, optional, exception-contained hook. It is invoked after `Recognize` returns, with the same frame, and the observer **copies** the frame and references and returns. The result object is untouched. The only added cost on the matcher thread is the copy, a few milliseconds.
- **Read-only accessors added:** `CaptureAnchor` (session) and `Evaluation` (recorder).
- **Test.** `DeterministicShadowFailsClosedOutsideEnvelopeAndNeverChangesBadges` asserts that badge slots and coordinates are identical before and after the shadow observer runs.

## 5. Comparison result: `DeterministicSlotShadowComparer`

Slot membership is geometric: the deterministic slot whose rectangle contains each visual match's centre. It is **not** the matcher's `VisualSlot`, which indexes whichever 4–7-column proposal grid won.

| Status | Meaning |
|---|---|
| `ExactAgreement` | every occurrence in a slot predicting its identity; every centre ≤ 3 px from its slot centre |
| `GeometryAgreement` | order agrees; centres offset uniformly, per-slot scatter around the mean offset ≤ 3 px |
| `GeometryMismatch` | order agrees; scatter > 3 px |
| `OrderMismatch` | some occurrence lands in a slot predicting another card, or centres don't map one-to-one |
| `DeterministicUnavailable` / `UnsupportedEnvelope` | no candidate (order not unanimous / outside geometry envelope) |
| `VisualUnavailable` | visual result partial or not `IsSafeFor` its request |
| `Stale` | pack, generation, capture or client geometry differ |

Reported values:
- compared count and slot agreements;
- reading-order agreement, via the 9E.1 `ReadingOrder`;
- max and RMS centre delta;
- mean offset (x, y);
- max and RMS scatter;
- median width and height ratio.

**Tolerance rationale.** The diagnostic tolerance is 3 px, as specified. The matcher's rectangle is a Scryfall portrait-card model registered on the artwork: art sits at 7.5/16.5 % with an 85 × 40 % box, inside ±10 % shift and ±15 % scale bounds. It is not Arena's card holder.

On real captures this model sits a **uniform +2.9 to +4.2 px lower** (scaling with s), and its rectangle is 8–11 % larger, while per-slot scatter is ≤ 1.4 px. Requiring the raw centre within 3 px would therefore fail on rectangle *semantics*, not on geometry. So "strong agreement" applies to the per-slot scatter around the common offset (`GeometryAgreement`), and the raw offset is reported, not hidden. Matcher thresholds are unchanged.

## 6. Experimental fast verifier: `CardTemplateRecognizer.VerifyFixedSlots`

This is a new method; `Recognize` is unchanged. For each predicted rectangle it:
- scores every current candidate with the same bounded local artwork registration that `Recognize` applies after its search;
- skips proposal search and the global assignment solve entirely;
- applies the **unchanged** gate: score ≥ **0.94**, and a lead of ≥ **0.07** over other identities at that slot and over the expected card at other slots.

It is not invasive, places nothing and is shadow-only.

## 7. Results on real Arena pixels

Arena was **closed** when validation ran (Player.log ends with Unity's input shutdown; no MTGA HWND), so there was no live pack.

The new developer command (`DraftTG.App --deterministic-shadow-live`) was therefore run in `--image` mode on the localization audit's **overlay-free full-resolution captures**, which have exact client geometry. It uses the same code path as live mode. Live mode (Player.log replay + WGC) is built and ready.

Order evidence used: DraftTG's own repository fixtures (WOE P1P6/P1P7, recorded before these packs; 120 surviving rules), supplied with `--evidence-pairs`.

| Capture | Cards | Order prediction | Deterministic | Full matcher | Order agreement | Geometry vs formula | Fast verifier (true order) |
|---|---|---|---|---|---|---|---|
| P1P1 1723×1009 | 14 | **Unanimous** (120 rules) | **Available** | 14/14 (922 ms) | **14/14**, reading order agrees | centre RMS 4.2 / max 5.0 px; offset (−0.1, +4.2); **scatter RMS 0.7 / max 1.4 px** → `GeometryAgreement` | **14/14** pass (444 ms), min 0.974, min lead +0.465 |
| P1P2 1723×1009 | 13 | Discriminating (110 vs 10) | **fails closed** | 12/13 | — | matched 12/12 centres inside formula slots; offset (+0.1, +4.0); scatter ≤ 1.1 px | order 1: **13/13** (412 ms); order 2: 10/13 |
| P1P2 1280×720 | 13 | Discriminating | **fails closed** | 12/13 | — | 12/12 inside; offset (+0.1, +2.9); scatter ≤ 1.3 px | order 1: **13/13** (347 ms); order 2: 10/13 |
| P1P2 1920×1080 | 13 | Discriminating | **fails closed** | 11/13 | — | 11/11 inside; offset (−0.1, +4.2); scatter ≤ 1.3 px | order 1: **13/13** (362 ms); order 2: 10/13 |
| P1P1, **production ledger (empty)** | 14 | Discriminating (**17 orders**, 400 rules) | **fails closed** | 14/14 | — | as row 1 | only order 1 passes 14/14 (others 12, 5, 1) |

Findings:
1. **Order: zero mismatches.** On the one unanimous real pack, deterministic order equals the visual order slot for slot. The P1P2 "order 1" equals the visual order the audit confirmed from screenshots.
2. **Geometry agrees.** Every matched visual centre (49 of 49) falls inside its formula slot. The formula-vs-matcher offset is uniform and small, and per-slot scatter is ≤ 1.4 px.
3. **Fails closed** on every discriminating pack, including the production configuration, where DraftTG's own ledger is empty and so all 400 rules survive.
4. **The fixed-slot verifier is stronger than the global search when geometry is known.** It passed every card of the true order, including **Syr Ginger** and **Gingerbrute**, which the full search left unresolved. It passed them with leads ≥ +0.327. It **rejected every wrong candidate order**, with leads down to −0.99, and ran in about 350–470 ms vs about 800–920 ms for full recognition, on the same thresholds.

**Live-pack coordinates for completeness** (P1P2, 1920×1080, s = 1.0704): slot 1 (280.8, 187.4, 169.7×232.1) … slot 13 (673.2, 722.6, 169.7×232.1). Every slot is listed by the command's `report.txt` under the git-ignored `artifacts/phase9e2a-live/`.

## 8. Diagnostics and logging

- **Rail:** one line under "Card placement", with a tooltip saying it is shadow-only. Examples:
  - `Deterministic slots: P1P2 available · 13 cards · order unanimous (110 rules) · geometry validated`
  - `Deterministic slots: P1P1 shadow 14/14 order agreement · centre RMS 4.2 px · max 5.0 px · geometry agreement (uniform offset)`
  - `Deterministic slots: P1P2 unavailable · order discriminating (2 predicted orders)`
  - `Deterministic slots: P1P4 unavailable · card count 12 outside validated envelope`
- **Card placement diagnostics:** the last 12 detail lines, also sent to `Trace`.
- **Log:** `<app data>/localization/deterministic-shadow.jsonl`, one line per pack/geometry generation. It holds:
  - coordinate, card count, client W/H, aspect, s;
  - order status, predicted orders, surviving rules and classes;
  - deterministic status and reason, visual status, shadow status;
  - agreements and reading order;
  - max/RMS centre, offset, max/RMS scatter;
  - verifier (accepted / count / ms);
  - Arena Data/GRP version and DB file name.

  It contains no pixels, names, paths or account data.

## 9. Why process memory is still unnecessary

Untapped reads Arena's memory for pack identity, order and hover state. For the pack grid, DraftTG gets each piece without it:

| Piece | DraftTG source | Comparison with Untapped |
|---|---|---|
| Identity | Player.log | about 145 ms *earlier* than Untapped's memory reader observed it |
| Order | Arena DB model | reproduced Untapped's (visual) order on every audited pack; production uses only DraftTG's own evidence |
| Geometry | the formula | equals Untapped's slot placement within about 1 px |

Memory would add only hover-preview decoration. That is out of scope, and it carries per-build Mono maintenance and policy risk. No `PROCESS_VM_READ`, Mono/IL2CPP traversal or injection was added.

## 10. Window tracking (audit only)

Current mechanism (`AutomaticCardLocalizationSession.Tick`):
- A **500 ms `DispatcherTimer`** calls `ArenaCaptureCoordinator.Observe()`.
- **Any** geometry change, including a pure move, restarts the coordinator (generation + 1) and calls `InvalidateAutomaticLocalization`.
- Badges then hide until a fresh synchronized capture and full re-match complete (≈1–2 s).

By comparison, Untapped follows moves with a median of 5 ms.

**Recommendation (later small phase):**
- Use `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)` scoped to Arena's process plus `EVENT_SYSTEM_FOREGROUND` for prompt tracking.
- On a **pure translation** (same client size and DPI), move the overlay and keep the placement: rectangles are client-relative.
- On a size or DPI change, re-run only geometry. Once the deterministic path is authoritative that is a pure computation, plus a verifier pass.

## 11. Stale badges after a pick (audit only)

The parser already emits `PickSubmitted` (`BotDraftDraftPick` / `EventPlayerDraftMakePick` request). The state engine records the pick but keeps `CurrentPack` until the next pack is presented, about 1.1 s later in the audit. So DraftTG shows the old pack's badges after the cards have left, the same "ghost badge" window Untapped has.

**Recommendation (later small phase):** when a `PickSubmitted` for the current coordinate arrives, hide the current badges immediately. Show the next pack's badges when its pack arrives. With the deterministic path these can be placed at final slots before the fly-in completes, as Untapped does. Not changed in 9E.2A.

## 12. Tests

12 new test methods (33 cases), table-driven where possible.

`tests/DraftTG.Application.Tests/ArenaDeterministicSlotTests.cs` (10 methods / 31 cases):
- formula reproduces 7 audit measurements (1723×1009 with 13 and 14 cards, 1280×720, 1920×1080);
- row-major grid with a left-aligned last row, repeatable, unrounded;
- envelope: 13 and 14 accepted; 12 and 15 rejected; height below and above; 16:10 and ultrawide aspect; fullscreen; non-draft scene; invalid rectangle;
- unanimous order zipped to slots;
- discriminating (including 99:1) and unavailable orders rejected;
- unanimous order still rejected outside the envelope;
- duplicate copies stay distinct occurrences;
- generation, pack and client-size binding;
- shadow comparison: exact, uniform offset, scatter, swapped order, stale;
- unavailable deterministic path reported, not compared.

`tests/DraftTG.App.Tests/DeterministicSlotShadowTests.cs` (2 methods):
- fixed-slot verifier accepts the true assignment and rejects a swap with unchanged thresholds (synthetic cards in formula slots);
- observer fails closed outside the envelope, writes one record per pack/geometry generation, and **leaves badges unchanged**.

## 13. Promotion gate for Phase 9E.2B (recommendation)

**Not justified yet.** The evidence is promising (zero order mismatches, sub-1.5 px scatter, a verifier that outperforms the search), but it rests on **one** unanimous real pack and saved captures. In production today, DraftTG's ledger is empty, so the deterministic path is unavailable for every pack.

Evidence still needed before 9E.2B may make deterministic placement authoritative:
1. **DraftTG-recorded order evidence** (Phase 9E.1 ledger, Gate 1 or an explicitly approved lower bar), so that real packs become unanimous *before learning*.
2. **≥ 30 live shadow records with `Shadow` ∈ {Exact, Geometry}Agreement** across at least two drafts and at least two client sizes, with **zero `OrderMismatch`** and per-slot scatter ≤ 3 px.
3. **Verifier agreement on every compared pack:** `VerifyFixedSlots` all-pass on the deterministic assignment. Any verifier failure must keep the visual path.
4. **Clean fallback** recorded in the log for discriminating, unsupported-envelope and partial-visual packs.
5. **Regimes still unmeasured:** ≤12-card packs (late picks, especially ≤5), 15-card packs, fullscreen. Each must be measured (`tools/UntappedLocalizationAudit` `geom` + `measure_grid.py`) before its envelope opens.

A natural 9E.2B design: deterministic placement becomes authoritative only when **unanimous order + envelope + verifier all-pass**, with the full visual locator unchanged as fallback.

*Open question for approval (not implemented):* could a verifier-confirmed order count as 9E.1 order evidence, as matcher-confirmed orders do today? It is pixel evidence at the same 0.94/0.07 gate and would accelerate Gate 1. It must not be used to *select* a rule for placement.

## 14. Housekeeping

- **`tatus`:** independently confirmed accidental. It held 24 lines of ANSI-coloured `git diff --stat` output, was committed in `5fb2b2c`, and nothing references it. It was **already removed** in commit `5816516 Remove stray tatus file` before this phase began, so nothing further is needed.
- **`artifacts/untapped-audit/` (252 MB):** git-ignored; not staged. All measurements this phase needs are extracted: the audit table is inline test data, and results are in this report and the audit report.
  - **Safe to delete** entirely.
  - To re-run the offline shadow command later, keep only the four overlay-free captures (≈8 MB): `A-baseline/arena-window-1.png`, `G-geometry/e01-arenaonly-b-1723x1009.png`, `G-geometry/e02-arenaonly-b-1280x720.png`, `G-geometry/validation/e03-arenaonly-a-1920x1080.png`.
- **`artifacts/phase9e2a-live/`** (84 KB, ignored) holds this phase's command reports. It has no pixels.

## Files

**Added**
- `src/DraftTG.Application/ArenaDeterministicDraftSlots.cs`: `ArenaClientRectangle`, `ArenaDraftSlotGeometry`, `DeterministicDraftCardLocator`, `VisualCaptureMapping`, `DeterministicSlotShadowComparer`, presentation.
- `src/DraftTG.App/DeterministicSlotShadowObserver.cs`: observer, `RecognitionFrameObservation`, `ArenaWindowModeProbe`.
- `src/DraftTG.App/DeterministicShadowLiveCommand.cs`: `--deterministic-shadow-live` (live or `--image`).
- `tests/DraftTG.Application.Tests/ArenaDeterministicSlotTests.cs`, `tests/DraftTG.App.Tests/DeterministicSlotShadowTests.cs`.
- `PHASE9E_2A_REPORT.md`.

**Modified** (additive; placement behaviour unchanged)
- `CardTemplateRecognition.cs`: `VerifyFixedSlots` plus result records. `Recognize` is untouched.
- `AutomaticCardLocalizationSession.cs`: read-only `CaptureAnchor`; optional exception-isolated `RecognitionFrameObserver`.
- `OrderEvidenceRecorder.cs`: read-only `Evaluation`.
- `OverlayViewModel.cs`: `DeterministicSlotStatus` / `Diagnostics`.
- `RailPanelView.axaml`: two text lines.
- `OverlayDesktopSession.cs`: composition and disposal.
- `Program.cs`: command.
- `ARCHITECTURE.md`, `ROADMAP.md`.

## Validation

| Check | Result |
|---|---|
| `dotnet build DraftTG.sln` | **0 warnings, 0 errors** |
| `dotnet test DraftTG.sln --no-build` | **978 passed, 0 failed**: Domain 56, RecommendationEngine 163, Application 241, Data 160, ArenaIntegration 127, App 231. All 945 pre-existing cases still pass; 33 new cases (12 methods) |
| Placement behaviour | unchanged; asserted by the observer test (badge slots and coordinates identical) |
| Matcher thresholds | unchanged (0.94 / 0.07; `Recognize` untouched) |
| Process memory | none added |
| Final sort rule | none; only unanimous predictions over DraftTG's own surviving rules |
| Unsupported geometry / discriminating order | fail closed (tests + real captures, including the empty production ledger) |
