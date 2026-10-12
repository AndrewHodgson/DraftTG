# Phase 9E.2B — Live deterministic localization validation

Promotion gate: **NOT PASSED**. Production authority: **WGC full matcher**.

Build ID tested (assembly MVID): `f91b5307-b7c6-4734-9bbb-85234488e2ce`. Build/commit metadata: 1.0.0+449eab11ae1b9f6d29953fc8e3cb4ed845db57f2.
Only this build's opt-in synchronized WGC records are included. Offline, legacy, synthetic/unspecified captures and other builds do not count.

Total live observation attempts: 0; eligible attempts: 0; fully verified promotion-counting distinct packs: 0.
Accepted pack generation events: 0; accepted-event card counts (including packs with no recognized frame): none observed
Fully verified attempts: 0; qualifying identified real drafts: 0; qualifying client sizes: 0.
Comparator/order mismatches: 0; geometry mismatches: 0; verifier failures on eligible attempts: 0.
False deterministic proposals against complete WGC evidence: 0; actual production deterministic placements: 0 (shadow only).
Fallback events: 0; unclean/missing fallback diagnostics: 0; localization/shadow failures requiring investigation: 0.
Unsupported observations: 0; rejected non-WGC/unsynchronized observation rows: 0; excluded ledger rows: 0; malformed rows: 0.

Draft scopes: none observed

Runtime sessions: none observed

Client sizes / DPI: none observed

Card-count coverage: none observed

Fallback reasons: none observed

Stale-frame admission rejections: 0; asynchronous stale-result rejection events: 0; canceled localization attempts: 0 (cancellation alone is not proof of stale rejection).
Move without resize: 0 events; completed placement retained without full relocalization: 0.
Resize/identity/visibility/DPI invalidation: 0 events; size changes: 0; DPI changes: 0.
Accepted-pick badge retirement: 0 events; cleared context and zero placed badges: 0. Semantic-event/UI timestamps are recorded; Player.log emission latency is not measured.
CaptureMilliseconds: not measured
FullMatcherMilliseconds: not measured
VerificationMilliseconds: not measured
AdmissionAgeMilliseconds: not measured

Unsupported/unvalidated: <=12 and 15 cards; non-windowed clients; height outside 720–1080; aspect outside 1.70–1.78; unusual UI scaling. No deterministic geometry is extrapolated. DPI/display transitions require live verification.

Recommendation: retain WGC authority. Gather >=30 qualifying distinct packs across >=2 identified real drafts and >=2 client sizes; investigate every mismatch/failure and missing fallback diagnostic. Zero observations means no live validation, not proof of zero faults. Physical move, resize, pick retirement and stale-result behavior must be reviewed before proposing promotion.

Implementation and validation notes (2026-10-12, America/Chicago):

- Harness implementation is complete; physical live validation is pending. MTGA was not running, and no draft was entered or paid for. No offline screenshot, replay or synthetic unit-test record was appended as live evidence.
- Tested source: base commit `449eab11ae1b9f6d29953fc8e3cb4ed845db57f2` plus the uncommitted 9E.2B working-tree changes listed below. The assembly MVID above identifies the tested compiled application; commit metadata alone does not identify this uncommitted patch.
- The existing `DeterministicSlotShadowObserver`, `ArenaDisplayOrderModel`, `FixedSlotOrderEvidence`, recognizer, WGC frame synchronization, order recorder, geometry model, native event tracking and pick-retirement path are reused. There is no second localization pipeline or evidence database. The append-only file remains `<application data>/localization/deterministic-shadow.jsonl`.
- Schema 3 adds runtime session ID, stable hashed draft identifier when available, draft scope, pack/pick/card count, physical client origin and dimensions, aspect, effective window DPI/scale, HWND, frozen comparator orders, supported deterministic rectangles, WGC occurrence identities/rectangles/confidence, every verifier occurrence's unrounded confidence/margin/acceptance/reason, complete-pack flags, per-slot comparison details, generation/request/revision metadata, capture/prediction/verification/admission timestamps, timing and explicit decline reasons. DPI is the effective HWND DPI derived from the existing window scale, not a physical monitor measurement. No screenshots, account data or raw draft identifiers are written.
- Promotion eligibility requires opt-in live WGC provenance, freshness at UI admission, supported windowed geometry, a unanimous prediction frozen from evidence before this pack, and no manual involvement during this generation. Manual involvement stays marked even if resize later clears the manual placement. Exactly one strict complete verifier result and complete independent WGC agreement are required to count. Discriminating orders can continue teaching the existing order ledger without supplying deterministic-placement proof.
- Confidence >=0.94 and ambiguity margin >=0.07 remain unchanged; full precision is retained for admission and summary. Existing holder/artwork comparison metrics and the prior diagnostic tolerance (scatter <=3 px, common offset <=6*height/1009 px) remain visible. Unsupported card counts never receive extrapolated deterministic rectangles.
- Retries, window moves, resizes and application restarts cannot count the same draft/pack/pick twice. A verified pack seen at two sizes contributes size coverage but only one pack observation. Reports default to the current assembly MVID; `--build-id <MVID>` reads another tested build separately. At least two stable identified draft IDs are required before claiming two real drafts. Missing IDs are reported by scope/session but do not establish identified-draft coverage.
- `FallbackInvoked` denotes a declined deterministic shadow attempt while the WGC full matcher was used; `WgcFullMatcherInvoked` records that the authoritative matcher actually ran. The production matcher runs first throughout this phase, including on successful deterministic shadow attempts. Capture/matcher failures and missing fallback diagnostics remain explicit and block the conservative gate. No automatic authority switch exists.
- Native window events report actual translation separately from focus-only state changes, capture-generation retention, and resize/DPI/identity/visibility invalidation. Existing movement, resize, DPI, cancellation and immediate accepted-pick badge-retirement behavior is preserved. Accepted-pick retirement records cleared context and placed-badge count after the existing retirement handler. Cancellation is reported separately from positively rejected stale results. UI/native freshness rejections after queued verification are retained even when the old pack is no longer current.
- Added two focused gate/report test methods, and extended existing observer and window-move tests. Coverage protects exact thresholds, stale/manual/offline/discriminating/unsupported declines, incomplete or inconsistent WGC evidence, prediction identity binding, geometry tolerance, semantic-pack deduplication, build isolation, clean-fallback reporting, malformed-ledger rejection, detailed per-card serialization, and manual-history exclusion. Synthetic records are confined to tests.
- No changes were made to `contextual-pick-score-v1.6`, `pick-score-tiers-v2`, scoring code, comparator hypotheses, deterministic supported envelope, production card assignment, or production authority.

Build/test execution:

```powershell
dotnet build DraftTG.sln -p:BaseOutputPath=bin/Phase9E2B/
dotnet test DraftTG.sln --no-build -p:BaseOutputPath=bin/Phase9E2B/ -m:1 -- xUnit.ParallelizeTestCollections=false
```

Final validation: **1,231 passed, 0 failed, 0 skipped** (Domain 56, Data 160, ArenaIntegration 127, Application 273, App 286, RecommendationEngine 329). The isolated build succeeds with 0 warnings and 0 errors. The default output directory was locked by an already-running DraftTG process (PID 31548); it was left running. The dedicated `bin/Phase9E2B/` build prevents replacing that running binary.

Earlier parallel full runs passed all 1,231 tests. A later parallel rerun had one transient failure in the pre-existing `HundredScopedCapturesDisposeEachRequestAndPassStressResourceChecks` process-wide resource bound; the same test passed immediately in isolation. It samples process private bytes and handles while other test collections allocate images. No resource threshold or unrelated test was changed; the final full run disables collection parallelism to avoid contaminating that measurement.

First live session (close the existing DraftTG instance, then run from this workspace):

```powershell
Set-Location 'C:\Users\Graphics0\Documents\Projects\DraftTG'
$env:DRAFTTG_CAPTURE_BACKEND = 'wgc'
$env:DRAFTTG_ALLOW_GDI_FALLBACK = '0'
dotnet .\src\DraftTG.App\bin\Phase9E2B\Debug\net10.0-windows10.0.19041.0\DraftTG.App.dll --live-localization-validation
```

1. Open Arena in captioned windowed mode with detailed Player.log logging enabled. Save the DraftTG draft-region calibration before the draft starts. Start a real draft normally; do not manually assign/confirm card positions during evidence collection. Calibration/manual intervention while a pack is current excludes that pack generation.
2. Let the 14-card opening pack and the 13-card pack settle so both matchers finish before picking. Continue the draft naturally into <=12-card packs; those are unsupported deterministic observations and remain on the existing WGC production path. If 15 cards occur naturally, retain the failure/decline diagnostics; do not manufacture that pack size. The existing production assignment model also has a 14-card cap, so this phase makes no claim of a complete WGC placement for an unencountered 15-card pack.
3. While a completed placement is visible, move Arena without resizing. Check that the overlay follows, keeps its placements and does not rematch.
4. Resize to a second real client size inside the envelope (for example 1280x720 and 1920x1080 client pixels; use the actual client dimensions reported by DraftTG). Check that old geometry is retired and fresh WGC localization/strict shadow verification run. A resize of the same pack supplies size coverage, not another pack for the 30-pack count.
5. Make normal accepted picks; verify outgoing badges disappear immediately. If localization is still asynchronous around a pick or resize, retain its rejection/cancellation diagnostics and verify it never restores previous-generation badges.
6. If practical, move between displays with different DPI. Expect invalidation and fresh verification rather than simple translation retention. If no such transition is practical, leave DPI-transition coverage explicitly unvalidated.
7. Complete the draft naturally and allow pending diagnostics to finish before closing DraftTG. Repeat with additional real drafts. The gate needs >=30 distinct qualifying supported packs, >=2 identified drafts and >=2 verified client sizes. With only 13?14 cards supported, a conventional three-pack 14-card draft offers about six qualifying-size packs, so reaching 30 may require at least five drafts, and more if early comparator predictions decline. The single-session goal is to establish clean live evidence, not to satisfy the gate in one draft.

After the session, display the summary and refresh this live report from the existing ledger:

```powershell
dotnet .\src\DraftTG.App\bin\Phase9E2B\Debug\net10.0-windows10.0.19041.0\DraftTG.App.dll --live-localization-validation-summary --output .\PHASE9E_2B_LIVE_VALIDATION_REPORT.md
```

The one-shot `--deterministic-shadow-live` command remains diagnostic only and never counts. The summary command reads evidence and writes a report; it never captures a frame, records an observation or promotes production authority. Its output replaces this baseline report with current numeric live results. Keep using the same tested build while accumulating evidence.

Files changed:

- `src/DraftTG.App/AutomaticCardLocalizationSession.cs`
- `src/DraftTG.App/DeterministicSlotShadowObserver.cs`
- `src/DraftTG.App/OverlayDesktopSession.cs`
- `src/DraftTG.App/Program.cs`
- `src/DraftTG.App/LiveLocalizationValidation.cs` (new)
- `src/DraftTG.App/LiveLocalizationValidationSummaryCommand.cs` (new)
- `tests/DraftTG.App.Tests/FixedSlotOrderEvidenceTests.cs`
- `tests/DraftTG.App.Tests/OverlayReliabilityTests.cs`
- `tests/DraftTG.App.Tests/LiveLocalizationValidationTests.cs` (new)
- `PHASE9E_2B_LIVE_VALIDATION_REPORT.md` (new)

Concrete recommendation: retain WGC authority and begin live collection with the command above. The current gate does not pass because there are zero qualifying live packs, zero tested real drafts and zero live client sizes. Do not treat automated tests or offline captures as a substitute. Even a future PASS is a report result only; production promotion requires explicit approval in a separate architecture change.
