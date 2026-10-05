# Phase 9B.10.2 — Quick Draft PickedCards snapshot semantics

Implemented the semantic correction. Final Windows solution build: **zero warnings, zero errors**. Final suite: **754 passed, 0 failed, 0 skipped** (739 baseline cases plus 15 focused cases). Capture implementations and acceptance criteria remain unchanged. Phase 9D was not started.

## Disproven assumption and live regression

The old parser assigned `PickedCards[i]` to historical pick `i`, provided the array length agreed with the current coordinate. Length agreement establishes how many drafted occurrences there are, not their chronological order.

The October 4 live log contained:

| Record | Relevant facts |
|---|---|
| Initial status, wire pack 0 / pick 9 | Current P1P10; nine picked cards; index 2 contains 86908, Welcome to Sweettooth |
| Outgoing pick request, wire pack 0 / pick 9 | Candy Trail, 86975, selected at P1P10 |
| Next status, wire pack 0 / pick 10 | Current P1P11; ten picked cards; Candy Trail inserted at index 2, shifting Welcome to Sweettooth |

The old interpretation created a false P1P3 contradiction. The new comparison sees one additional occurrence of 86975 across P1P10 → P1P11, assigns it to **P1P10**, and leaves any existing exact **P1P3 = Welcome to Sweettooth** record untouched.

`tests/Fixtures/quick-draft-picked-cards-live.jsonl` contains the three minimal records in their actual string-encoded `CurrentModule/Payload` and outgoing `request/PickInfo` shapes. The request correlation ID is sanitized; unrelated inventory and account data are omitted. The regression supplies the known P1P3 semantic record separately as a test precondition: the single mid-draft live snapshot itself cannot prove that earlier coordinate. Both explicit-request-plus-snapshot and snapshot-inference-only variants pass.

## Semantic model

The stateless parser emits `PickedCardsObserved(ArenaPickedCardsSnapshot)` before the current pack event. The snapshot contains optional Arena draft identity, normalized current coordinate, an immutable raw card list for diagnostics, and a semantic `ArenaCardMultiset`. It emits no historical picks by indexing that list. Completed Quick Draft status can also provide its pool before the completion event.

The multiset stores read-only per-ID occurrence counts and a deterministic occurrence list. Equality compares multiplicities, ignoring array order. A single addition requires total count +1 and no removed occurrences; `A A B → B A C A` yields C, while `A B → A A B` yields one additional A. No set difference is used for drafted cards.

`ArenaDraftStateEngine` owns the previous accepted snapshot. Exact inference requires the same existing session, a supported consecutive normal coordinate, and exactly one added occurrence. The added card is submitted through the existing canonical `ApplyPick` path at the **previous coordinate**. `ArenaQuickDraftCoordinates` handles P1P14 → P2P1 and P2P14 → P3P1 without inventing pick zero. Explicit confirmation is idempotent; a different explicit card at an already known coordinate still throws the existing genuine conflict.

Exact history remains only in `CompletedPicks`. There is no second reconstructed pick history. The snapshot baseline is pool information, not coordinate-qualified history. Reordering and repeated snapshots neither accumulate occurrences nor report a semantic change.

## Recovered pool and recommendation inputs

`ArenaDraftStateSnapshot.RecoveredPool` retains the last accepted pool and its coverage coordinate. `DraftedPool` combines that pool with exact selections not yet covered by it. This prevents an explicit selection and its following confirming status from being counted twice, even when the card is another copy of an already drafted ID. Late exact history for already covered occurrences does not add another copy. A late exact fact contradicting the older pool is retained, included in known membership, and diagnosed.

Application resolves occurrences into the new provider-neutral `DraftedCardPool`, separately from `DraftHistory`. `DraftSnapshot.DraftedPool` uses the recovered context when supplied and otherwise falls back to exact history. `ArenaDraftSnapshotResult` separately exposes `ResolvedHistory` and `ResolvedDraftedPool`, including while no current pack is available or after completion. No fake Domain `DraftPick` coordinates are created.

Phase 9A now receives this pool's identifiers and multiplicities. Its evidence weights, progress calculation, support calculation, and adjustment constants are unchanged. Phase 9C already consumes the resulting color profile and therefore receives the recovered pool through the same input. Phase 8, Phase 9B lane calculations, Phase 9C formulas, and 17Lands behavior are unchanged. Historical pack observations still require exact pick history.

The application regression starts with an unordered fourteen-card pool (seven black, seven green) and no exact history, then adds one black card. It verifies fifteen drafted occurrences, eight black and seven green evidence, unchanged Phase 8 result identity, BG archetype activation, and exactly one recovered coordinate-qualified pick.

The drafted-card UI displays recovered-only occurrences as **Position unknown**, preserving duplicate copies. Diagnostics show exact-history coverage, such as `1/3 drafted card occurrences`. The benchmark adds exact-history, drafted-pool, and unknown-history occurrence counts to its existing per-pack records and logs focused pool diagnostic kinds. These are semantic telemetry additions; capture/recognition/placement criteria are unchanged.

## Gaps, inconsistent snapshots, and replay

A first mid-draft snapshot retains the pool and creates no exact history. A jump adding several occurrences updates that pool but assigns none of those cards to invented coordinates. Existing exact picks survive the gap, unknown-history occurrence counts expose incomplete coverage, and inference resumes on subsequent consecutive snapshots.

Count disagreement, removed occurrences, backward progression without a recognized reset, and disagreement with known completed selections retain prior pool/history and surface focused diagnostics. An unexplained shrink is not treated as a new selection or silently interpreted as a fresh draft. Existing new-draft ID/event rules and source reset clear the baseline. With no reliable distinguishing identity, ambiguous same-event session changes remain conservatively diagnosed rather than guessed.

Completion checks pool consistency without using array order to rewrite history. Byte-zero replay resets state and reconstructs the same final result. A first or final pool snapshot alone cannot fill missing historical coordinates. Missing coordinates also cannot identify selections for unseen historical pack observations; pool-based color/archetype context remains available.

## Focused validation

Fifteen new executed cases across eleven test methods:

- Live Candy Trail insertion, with and without an explicit request.
- Three arbitrary reorderings with no semantic change.
- Two duplicate-occurrence deltas.
- Both normal pack boundaries.
- Missed-update gap and resumed exact inference.
- Mid-draft pool, explicit duplicate copy, late exact history, and inconsistency diagnostics.
- Inferred/explicit exact replay and genuine different-card conflict.
- Incoherent count, removed occurrences, backward progression, and exact-history mismatch.
- Source replay, completion, and new-session reset.
- Application pool-to-color/archetype context and completion resolution.
- UI unknown positions and partial-history coverage.

Five existing parser/coordinator expectations that encoded index-based history were deliberately updated. An existing UI test name was clarified to describe exact supplied history.

| Assembly | Passed |
|---|---:|
| Domain | 42 |
| RecommendationEngine | 116 |
| Application | 140 |
| Data | 138 |
| ArenaIntegration | 107 |
| App | 211 |
| **Total** | **754** |

Validation commands were `dotnet build DraftTG.sln` and `dotnet test DraftTG.sln --no-build`, with build/TRX logging added. Avalonia's external build log required running the build outside the filesystem sandbox; the final requested build succeeded with zero warnings/errors. Evidence is in `artifacts/phase9b10-2-validation/`, including `final-build.log`, `verified-tests/`, `test-summary.json`, and `source-verification.json`.

Hash comparison verified **48 protected source files unchanged**: Windows capture backends/factory, synchronization/coordinator, reference preparation, matcher, visual placement, data/provider files, statistical scoring, lane engines, and archetype engine. Only semantic telemetry changed in the benchmark command. No live benchmark or game input was performed after implementation.

## Resume the live benchmark

Start the rebuilt production app, or restart an older running instance once to load this fix. Keep Arena in the active draft and retain the saved calibration. From the repository root, a first PowerShell window can run:

```powershell
Set-Location 'C:\Users\theyc\OneDrive\Documents\DraftTG\DraftTG'
dotnet run --project src/DraftTG.App --no-build
```

In a second normal PowerShell window on Arena's Windows desktop:

```powershell
Set-Location 'C:\Users\theyc\OneDrive\Documents\DraftTG\DraftTG'
$benchmarkOutput = Join-Path 'artifacts\phase9b10-2-live' (Get-Date -Format 'yyyyMMdd-HHmmss')
dotnet src/DraftTG.App/bin/Debug/net10.0-windows10.0.19041.0/DraftTG.App.dll --benchmark-arena-capture --output $benchmarkOutput --duration-seconds 1200 --save-failures
```

Make picks manually, allowing the pack to settle and the command to print its observation before the next pick. Target at least ten consecutive picks, including low-card counts where reached. `benchmark.jsonl` records both capture branches, generations/freshness/hashes, latencies, DD pointer skips, matcher results, and history coverage. Inspect production badges separately and record correct placement/manual fallback for each coordinate: the existing headless comparison explicitly reports physical placement as unobserved and cannot prove production UI placement by itself.

WGC remains production primary. Desktop Duplication still uses its existing coverage guard; the previous Untapped.gg overlay rejection is unchanged and must be recorded if it recurs. Genuine semantic conflicts still stop the watcher. Reordered legitimate pool snapshots now pass. A new live run is still required to establish ten-pick reliability and all five automatic-placement stages.
