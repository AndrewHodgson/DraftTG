# Phase 9E.2A.1 — Offline hardening and verified evidence integration

Date: 2026-10-07 (America/Chicago). Arena unavailable for maintenance; no live validation was performed.

**Outcome:** strict fixed-slot pixel verification can now contribute Phase 9E.1 order evidence, including discriminating packs with exactly one successful candidate. Deterministic placement remains shadow-only. Production badges still receive only WGC/artwork localization or existing manual placement. Phase 9E.2B has not begun.

## Independent review and reconciliation

Reviewed the architecture, roadmap, independent localization review, Phase 9E.1 report, Untapped localization audit and Phase 9E.2A report against the source. The working tree already contained Claude's uncommitted 9E.2A implementation; those changes were preserved and hardened in place.

Claude's central implementation was correct: the measured formula, client/crop coordinate conversions, envelope, occurrence keys, unanimous-only locator, strict score/margin checks and placement isolation agree with the reports. Independent replay reproduces the reported 14/14 and 13/13 results. It was suitable as a diagnostic prototype, but needed additional safeguards before admitting its output as evidence.

Discrepancies were reported before editing:

| Finding | Resolution |
|---|---|
| The frame hook read `_window`/`_anchor` after matching; these could describe a different capture geometry. | Bind the observation to the capture coordinator's geometry before reference preparation, then validate frame HWND, screen crop origin, dimensions and generation tokens. |
| Queued verification had no final current-pack/current-capture check. | Perform admission on the UI thread with a final native, read-only window inspection and pack/capture/request/revision checks. |
| Shutdown could skip the closure that disposed copied frames/references. | Queue cleanup separately and always run it in `finally`, including cancellation and rejected enqueue. |
| The old report said the state engine retained a picked pack until the next pack arrived. | Incorrect: `ArenaDraftStateEngine.ApplyPick` already clears a matching current pack. The full downstream hide path is traced below. No behavior changed. |

Additional hardening: publish the evidence evaluation and its observations as one atomic snapshot; exclude this same coordinate/multiset from pre-pack evaluation on retries or ledger replay; prevent stale shadow status from replacing a newer pack's status; prune retained diagnostic keys; contain native freshness-check and evidence-callback exceptions; validate occurrence count before indexing; reject overlapping fixed regions and malformed full-verification results.

The review's 20 functional classes and the later audit's 48 classes use different card universes. Raw survivors remain 120 for the two WOE observations; class counts depend on the universe used and should not be compared as if the universes were identical. The four saved Arena-only captures are replay input, not production evidence. Untapped binaries and private logs were not consumed.

### Boundary, coordinates, duplicates and thread safety

- Application owns the pure geometry, sort model, evidence codec/service and occurrence assignments. App owns Skia pixels, live freshness checks, UI admission and background persistence. Domain and RecommendationEngine are untouched.
- Formula rectangles are physical Arena **client** pixels. `VisualCaptureMapping` uses the same rounded crop origin/size as capture. Screen origin is added only to validate the actual frame crop, never to formula rectangles. The overlay's existing DIP conversion remains unchanged.
- Resize, movement, DPI change, HWND/process replacement, calibration change, retry revision, a newer capture request and pack change invalidate fast evidence. Production's existing 500 ms tracking and rematching behavior remain unchanged.
- Every pack index appears exactly once in the verified slot assignment. Identical copies are assigned in ascending pack-index order and each physical slot must independently pass. Same artwork is not treated as a competing identity against an identical copy. Different Arena IDs collapsed onto one Domain identity are rejected as unresolved for evidence.
- Evidence/model snapshots are immutable at verification time. Worker work is serialized; the final admission reads live UI state on the UI thread. Persistence is queued after admission, so an accepted observation may be flushed later without reinterpreting it against a later pack. No stale result can become an admitted observation.
- The shadow observer never calls `ApplyAutomaticLocalization`, confirms manual placement, hides badges or modifies the recognized result. New native inspections are read-only and cannot restart capture. **Deterministic output cannot alter badge placement.**

## Exact fast-evidence contract

`FixedSlotOrderEvidence` tests only distinct orders computed from the surviving rules before observing this pack. It uses the same single synchronized frame and exact-printing references as the full matcher, copied before ownership expires.

Admission requires all of the following:

1. Same immutable current pack, coordinate and positive pack generation; same ordered Arena occurrence list and current presentation occurrence keys.
2. Supported windowed draft geometry: 13 or 14 cards, height 720–1080, aspect 1.70–1.78. Predicted regions must lie wholly inside the calibrated capture crop.
3. Matching capture generation, capture-request generation, frame pack generation, HWND, crop screen origin, crop size, anchor and localization revision. Capture timestamp must be at or after the pack transition; the frame has already passed `PackFrameSynchronizer`'s freshness/hash/settle checks.
4. Final native client origin, dimensions, process, HWND and DPI must still equal the captured geometry; the window must be visible and not minimized.
5. No manual edits, confirmed manual map or active calibration.
6. Every distinct candidate is tested, up to **8**. More than 8 declines the entire fast attempt; no prefix or majority class is selected.
7. Exactly **one** candidate has all slots accepted, all expected scores finite and at least **0.94**, and all ambiguity margins at least **0.07**. Competition includes other current-pack identities at that slot and the expected identity at other differently assigned slots.
8. Slot and occurrence indices are complete permutations of the pack; no partial, reused, overlapping or unresolved duplicate assignment is accepted. A 12/13 result is rejected regardless of aggregate score.

`FixedSlotVerificationResult.FullyVerifies(count)` rechecks count, index coverage and numeric thresholds instead of trusting acceptance flags alone. All candidates use the bounded local artwork registration from the existing recognizer. No threshold was lowered; the full `Recognize` search/assignment implementation is unchanged.

The fast gate returns a pixel-confirmed **visual order**, not a claim that a rule is authoritative. It never supplies a badge placement result. Zero winners, two winners, partial verification, excessive candidate count, missing references, unsupported geometry, stale state and manual involvement record nothing through this path.

### Circular-learning prevention

The sequence is:

```text
ledger before this pack (excluding any replay of this same pack state)
  -> immutable surviving-rule snapshot at pack arrival
  -> distinct predicted orders
  -> one synchronized frame, strict independent pixel verification
  -> current-state admission of exactly one fully verified order
  -> append observation with frozen pre-verification prediction/provenance
  -> reevaluate rules for subsequent packs
```

Both the copied order snapshot and the service's same-pack exclusion have regression coverage. A retry after an observation was already written cannot train its own prediction. The service's diagnostic `PredictionBefore` for fast evidence is the frozen prediction supplied by verification, even if full-matcher evidence reached the persistence queue first. The ledger deduplicates the same draft scope/coordinate/card multiset across both sources.

## Evidence provenance and developer summary

The full-matcher path remains subscribed to `AutomaticLocalizationApplied` and uses the existing Phase 9E.1 recording gate. The two sources are:

- FullVisualMatcher: existing `DraftTG/9E.1 automatic-artwork-matcher` records.
- DeterministicSlotFullVerification: `DraftTG/9E.2A.1 deterministic-slot-full-verification` records, carrying minimum ambiguity margin and pre-verification candidate-order count.

Schema-1 full-matcher rows remain readable. Fast rows require recognized provenance, valid dimensions, full score/margin metadata and bounded candidate count. Unknown sources, duplicate sort-key rows, malformed lists/JSON and incomplete fast metadata are skipped safely during replay.

The existing command now reports observations, complete drafts, cards observed, both source counts, raw surviving rules, functional classes, contradictions, discriminating observations, fast candidate-order resolutions and all Gate 1 coverage. No new UI was created.

```powershell
dotnet src/DraftTG.App/bin/Debug/net10.0-windows10.0.19041.0/DraftTG.App.dll --order-evidence-summary
```

Production summary checked during this phase: **0 observations, 0 cards, 0 complete drafts, Full matcher 0, Fast verifier 0, 400 surviving rules, no class universe, 0 contradictions, 0 discriminating observations, 0 candidate resolutions, Gate 1 not satisfied**. Saved screenshots were not appended to that ledger.

## Offline replay

Replayed the two repository WOE fixtures and all four requested Arena-only audit captures. Image mode now reads only the local catalog and reference thumbnails; fixture mode uses its saved reference files. Neither mode opens Arena, reads Untapped data, accesses the network or writes order evidence. Reports contain numeric results and no images.

For the four audited captures, pre-pack evidence is only WOE P1P6/P1P7 (17 independently observed cards, 120 surviving rules). WOE P1P6 is predicted with no earlier observations; WOE P1P7 uses only P1P6, excluding its own ground truth from prediction.

| Fixture | Distinct candidates before pack | Full matcher | Fast passing slots by candidate | Fully verified candidates | Minimum winning score / margin | Result |
|---|---:|---:|---|---:|---|---|
| WOE P1P6, 9 cards | 11 | 9/9 | not run | 0 | full matcher 0.979759 / 0.433010 | Full matcher produces independent order; fast geometry unavailable. |
| WOE P1P7, 8 cards | 1 | 8/8 | not run | 0 | full matcher 0.968524 / 0.484929 | Full matcher produces independent order; fast geometry unavailable. |
| P1P1 1723×1009, 14 cards | 1 | 14/14 | 14/14 | 1 | 0.9737 / 0.4651 | One order passes the full pixel contract. |
| P1P2 1723×1009, 13 cards | 2 (110 / 10 rules) | 12/13 | 13/13; 10/13 | 1 | 0.9785 / 0.3326 | Only the correct order is eligible by pixels. Wrong minimum score 0.3086, margin −0.6891. |
| P1P2 1280×720, 13 cards | 2 (110 / 10 rules) | 12/13 | 13/13; 10/13 | 1 | 0.9733 / 0.3268 | Only the correct order is eligible by pixels. Wrong minimum score 0.2639, margin −0.7320. |
| P1P2 1920×1080, 13 cards | 2 (110 / 10 rules) | 11/13 | 13/13; 10/13 | 1 | 0.9715 / 0.3353 | Only the correct order is eligible by pixels. Wrong minimum score 0.2659, margin −0.7307. |
| P1P1 with empty production ledger | 17 | 14/14 | not run | 0 | — | All fast verification declined: 17 exceeds 8; normal full matcher remains available. |

Every per-card score, ambiguity margin, GrpId and pass/fail result is retained in the small, non-personal [numeric replay fixture](tests/Fixtures/phase9e2a1-replay-metadata.json). `FastCandidates[].Slots[]` holds both correct and wrong orders; `FullMatcherSlots[]` holds the WOE fixtures. Values are rounded to six decimals for reporting; runtime checks use full precision. Full reports are also kept locally under ignored `artifacts/phase9e2a1-replay/` and `artifacts/phase9e2a1-final-replay/`.

The known P1P2 split is resolved by verification, never by its 110-to-10 vote. These replays demonstrate eligibility under the pixel contract, not live generation/transition safety or permission to persist saved images as live evidence.

Reproduce all six required fixtures after building, retaining the four ignored Arena-only images:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ReplayDeterministicSlotEvidence.ps1
```

The script creates only numeric reports and a local evidence-pairs file. Its execution-policy option is process-local; it changes no machine policy. The command no longer silently verifies only the first four candidate orders.

## Append-only live shadow validation ledger

The existing separate `<app data>/localization/deterministic-shadow.jsonl` is extended to schema 2. Each future captured attempt records:

- hashed draft scope, pack/pick coordinate, count, client dimensions/aspect/scale;
- pre-pack order status, distinct candidate count, raw survivors and functional classes;
- actual envelope/geometry availability and deterministic status;
- full-matcher result, fast-verifier result and fully verified candidate-order count;
- fast-evidence eligibility, order agreement, max/RMS center error, offset and max/RMS residual scatter;
- fallback reason, synchronized-live-frame marker, capture generation and request token;
- Arena DB version/file name. No pixels, images, account data or raw draft IDs.

Deduplication includes capture generation/request so a failed or stale attempt does not suppress a later fresh retry at the same size. Recent in-memory keys are pruned. Comparisons on discriminating packs can use the uniquely pixel-confirmed order for **diagnostic comparison only**; the original model still reports `OrderDiscriminating` and production still uses full localization. Logging cannot change placement or recommendations. Persistence/freshness failures are exception-contained.

## Phase 9E.2B promotion gate — documented only

Before proposing production authority, require:

1. Gate 1 order evidence coverage remains satisfied; no hypothesis refutation or contradiction.
2. At least **30 distinct live supported pack comparisons**, across **at least 2 hashed draft scopes** and **2 client sizes**. Deduplicate repeated capture attempts for the same pack/size. Saved-image replay, synthetic tests and unsynchronized standalone diagnostic CLI captures do not count.
3. **Zero verified order mismatches**. Count only complete, safe independent comparisons; partial full-matcher results are inconclusive rather than agreements.
4. Strict full verifier success for **every** pack proposed for a deterministic path. No majority/partial acceptance.
5. Clean fallback records for discriminating, excessive-candidate, unsupported and unverified packs. A discriminating pack may teach the ledger without becoming authoritative for placement.
6. **No stale-generation acceptance/failure in the qualifying sample**; intentionally stale tests must fail closed. Investigate any live stale-frame fault before promotion.
7. Geometry tolerance: maximum per-slot residual scatter **≤3 physical client px**, RMS residual **≤3 px**, and reported common center offset magnitude **≤6·(H/1009) px**. The raw max/RMS errors remain visible. The offset allowance distinguishes the artwork registration rectangle from Arena's holder and is a proposed gate tolerance, not a new measured envelope.

This phase supplies **0 qualifying live comparisons**. Gate 1 is also unsatisfied. Offline replay supports the existing formula and verifier behavior, but satisfies no live Phase 9E.2B promotion requirement. No automatic promotion mechanism was added.

## Historical pack-size research

Inventoried repository images, fixture manifests and the ignored audit capture series. The tracked real draft screenshots are P1P6 (9 cards) and P1P7 (8 cards). The audit's settled pack datasets are 14-card P1P1 and 13-card P1P2; its many move/hover/pick-animation frames are not additional settled pack counts. Reference JPEGs are card thumbnails, not pack captures.

The two WOE crop images were visually inspected and measured with the existing holder-edge tool. Their client was documented as 2560×1440; both crops are 1325×1131. Coordinates below are **crop-relative**, because the original client-relative crop origin is not preserved in their fixture metadata:

| Real count | Holder rectangles measured | Median width / height | Columns and rows measured | Comparison with H=1440 formula |
|---:|---:|---|---|---|
| 9 | 8 of 9 | 226 / 310.5 px | x 36, 297, 558, 820, 1081; row tops 52, 409 | Width 226.21, height 309.41, column pitch 261.52, row pitch 356.79 predicted; measured pitch 261–262 and 357. Consistent relative geometry. |
| 8 | 7 of 8 | 226 / 310 px | same x values and row tops | Same relative agreement; last row contains three cards, left-aligned. |

The bonus-sheet card in the first column does not expose the same clean holder edge and was excluded by the measurement tool. No missing edge or crop origin was fabricated. Absolute client x/y anchoring cannot be independently checked from these crop-only fixtures. Neither H=1440 nor 8–9 cards enters the validated fast envelope.

No coordinate-backed, settled real captures were found for **15, 12, 11, 10, 7, 6, 5, 4, 3, 2 or 1 cards**. Those counts need live measurement. Counts **9 and 8** additionally need full-client screenshots with known client origin/size at supported heights to verify absolute anchoring and any layout breakpoint. Fullscreen, borderless, unusual DPI/UI scaling and narrower/ultrawide clients remain unvalidated.

## Future window tracking — design only

Current `AutomaticCardLocalizationSession` uses a 500 ms `DispatcherTimer`. `ArenaCaptureCoordinator.Observe` restarts on meaningful origin/size/HWND/DPI change; the session invalidates automatic placement and retries the full matcher. Pure translation therefore causes avoidable rematching. Focus and visibility are separate diagnostics.

Proposed narrow platform adapter: register out-of-context WinEvent hooks on a message-loop thread, root the managed callback, dispatch/coalesce notifications to the UI and unhook on that same thread at disposal. Rebind process-scoped hooks when Arena's rendering HWND/process changes. This follows the [SetWinEventHook API contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook).

Use `EVENT_OBJECT_LOCATIONCHANGE`, filtered to Arena's known rendering HWND/window object, and minimize start/end notifications. Observe foreground changes globally so losing focus to a different process is also noticed. Re-read client bounds/DPI rather than trusting event payloads; retain polling as a watchdog. These event types are documented in Microsoft's [WinEvent constants](https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants).

Classify each notification as translation, resize/DPI/identity change, foreground change or minimize/restore. For pure translation with the same size/DPI/HWND, translate the overlay using its client anchor and retain card identities/normalized rectangles. Separate capture-freshness epochs from placement invalidation so movement does not trigger full rematching. Resize/DPI/identity change must invalidate geometry and retain the current safe fallback until recomputation is verified. Minimize/focus changes control visibility; restoration requires reinspection. No hooks, polling changes or movement behavior changes were implemented here.

## Outgoing pick and stale badges — design only

The source trace is:

```text
Player.log outgoing BotDraftDraftPick / EventPlayerDraftMakePick
 -> ArenaDraftLogParser: PickSubmitted, normalized coordinate/card IDs
 -> ArenaDraftStateEngine.ApplyPick: validate/replay/conflict handling;
    clear CurrentPack when the accepted submission matches its coordinate
 -> DraftSessionCoordinator: changed snapshot -> ArenaDraftSnapshotAdapter.NoCurrentPack
 -> MainWindowViewModel: UI dispatcher -> ApplySessionUpdate -> ClearCurrentPack
 -> PackPresentationChanged(true) -> OverlayViewModel.RefreshPack/ResetVisualPlacement
 -> empty badges/null placement context; pending localization canceled
```

**Earliest safe semantic event:** an accepted `PickSubmitted` that consumes the current pack after state-engine validation. Raw substring detection, an unmatched coordinate or a malformed request is not safe. The existing path already hides badges at that event's UI publication; it does not deliberately wait for the next pack. `FileArenaLogSource`'s default 300 ms poll and log/dispatcher latency may still leave a visible delay, which cannot be measured while Arena is offline.

Future minimal proposal, only if live timing demonstrates a material delay: expose a small `CurrentPackConsumed` coordinate/generation marker on `DraftSessionUpdate`, and suppress matching-generation badges at the beginning of its UI application before pool/presentation work. Keep all identity/history reconstruction in the engine and reject delayed markers for another generation. First instrument accepted-pick-to-UI-hide timestamps to distinguish log latency from UI latency. No pick, parser, state-engine or badge behavior was changed in this phase.

## Housekeeping

- `tatus` is already absent from disk and tracked files; no deletion was needed.
- `artifacts/` ignores `artifacts/untapped-audit/` and the new local replay reports. `git check-ignore` confirms the audit PNG is ignored.
- Keep the four Arena-only audit screenshots as **ignored local fixtures**. Only approximately 26 KB of non-personal numeric replay metadata was added for review; no screenshot or image was added or staged.
- The index was empty before and after this phase. Existing uncommitted user/Claude files were preserved. No commit or staging operation was performed.

## Tests and final validation

Added **11 focused methods / 18 cases**, bringing **978 → 996** passing tests (all 945 tests preceding 9E.2A and all 33 Claude cases remain passing):

- App: six table-driven strict-admission scenarios; zero/one/two candidate winners; functional order deduplication; whole-attempt candidate cap; frozen pre-verification order; per-slot duplicate pixel verification and identity-alias rejection; malformed/overlapping/reused occurrence requests; queued stale rejection followed by fresh evidence after a partial matcher, with badge coordinates/placement unchanged.
- Application: current-pack evidence exclusion before and after ledger replay; frozen prediction before recording versus post-record rule evaluation; both source provenances and summary resolutions; safe malformed-ledger replay with existing schema-1 full-matcher compatibility.

Final required commands:

```text
dotnet build DraftTG.sln              -> 0 warnings, 0 errors
dotnet test DraftTG.sln --no-build     -> 996 passed, 0 failed, 0 skipped
```

Totals: Domain 56; RecommendationEngine 163; Data 160; ArenaIntegration 127; Application 244; App 246. The six-fixture replay script also completes successfully. No recommendation/deck changes, process-memory reader, threshold reduction, production badge-placement change, envelope expansion or deterministic authority promotion was introduced. Stop at Phase 9E.2A.1; live evidence gathering remains pending.
