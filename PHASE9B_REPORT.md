# Phase 9B completion report

Implemented on Windows on 2026-10-03. Phase 9B adds conservative open-color lane inference using the existing replay-safe observations. Phase 9C/9D have not begun.

## Results and preserved stages

The recommendation pipeline now exposes three separate immutable results:

| Stage | Application property | Input/output |
| --- | --- | --- |
| Phase 8 | `LimitedStatisticsUpdate.Recommendation` | Original statistical value/rank, raw GIH/sample and environment baseline |
| Phase 9A | `LimitedStatisticsUpdate.ContextualRecommendation` | Original pool fit/adjustment, pool value/rank and commitment profile |
| Phase 9B | `LimitedStatisticsUpdate.LaneRecommendation` | Lane profile/fit/adjustment, final value/rank, with retained references to both previous stages |

Phase 8's `(n*p + 500*b)/(n+500)` and Phase 9A's evidence/progress/fit/adjustment formulas are unchanged. Hash comparison against the start of this phase confirms that `StatisticalRecommendationEngine.cs`, `ColorCommitment.cs`, `ContextualRecommendationEngine.cs`, all Data/provider code, all ArenaIntegration production code, native platform code and badge XAML are unchanged. All existing test assertions are preserved; one existing filesystem fixture gains a bounded retry for a reproduced transient Windows replacement failure. No endpoint, cache, parser, calibration or click-through behavior was modified.

RecommendationEngine continues to reference only Domain. Lane analysis has no provider DTOs, UI, filesystem, networking or OS dependency. No new provider fetch or external dataset was needed.

## Types and observation extension

New engine types:

- `LaneDetectionConfiguration`: validated immutable heuristic constants and model version `late-color-lane-v1`.
- `LaneColorEvidence`: accumulated evidence, raw support and effective support for one color.
- `LaneCardSignal`: identity, coordinate, slot, colors, ALSA, captured Phase 8 value/baseline, component weights, evidence and pack decay.
- `LaneOpennessProfile`: immutable five-color supports, meaningful signals, eligible observation count and confidence; minimum-color candidate fit.
- `LaneCardRecommendation`: retained Phase 9A occurrence, lane fit/adjustment and nullable final value/rank/top flag.
- `LaneDraftRecommendation`: retained Phase 9A/8 results, lane profile, observations, neutral format and final occurrences/top index.
- `LaneContextualRecommendationEngine`: pure composition, validation and deterministic final ranking.

`LaneRecommendationPresentation` is the new Application formatter. Existing `DraftCardObservation` gains optional ALSA; `DraftPackObservation` gains optional environment baseline. The existing observation history remains the only observation system. Current recording and delayed enrichment supply these values from the existing mapped statistics. Missing values remain absent, with no fallback manufactured for lane evidence.

## Configuration and exact formulas

| Configuration | Default |
| --- | ---: |
| LateArrivalScale | 3.0 |
| LaneStrengthScale | 0.04 |
| LaneSignalStartPick | 4 |
| LaneSignalFullPick | 8 |
| CurrentPackEvidenceWeight | 1.00 |
| PreviousPackEvidenceWeight | 0.50 |
| TwoPacksBackEvidenceWeight | 0.25 |
| LaneEvidenceScale | 2.0 |
| FullLaneConfidenceAfterObservations | 4 |
| MaxHumanLaneAdjustment | 0.020 |
| MaxBotLaneAdjustment | 0.010 |

Scales must be finite and positive. Start pick is positive, full pick >= start, and the full-confidence threshold is positive. Recency weights and adjustment caps must be finite in [0,1]; zero can disable an influence. These are transparent, tunable product heuristics, not claims of statistical optimality or predicted win rates.

```text
latenessWeight = clamp((actual one-based pick - ALSA) / LateArrivalScale, 0, 1)
strengthWeight = clamp((captured Phase8AdjustedValue - captured EnvironmentBaseline)
                       / LaneStrengthScale, 0, 1)
positionWeight = clamp((actualPick - LaneSignalStartPick + 1)
                       / (LaneSignalFullPick - LaneSignalStartPick + 1), 0, 1)
cardOpenEvidence = latenessWeight * strengthWeight * positionWeight
weightedOpenEvidence = cardOpenEvidence * packRecencyWeight
```

The position ramp is inclusive to implement the request's weak P1P4 signal: default picks 1–3 give 0, pick 4 gives 0.2, pick 6 gives 0.6 and pick 8+ gives 1. An appearance at/before ALSA does not produce positive or negative evidence. A card at/below the environment baseline produces zero evidence. ALSA measures availability, never strength; quality uses adjusted Phase 8 GIH, not raw GIH or a contextual value.

Missing or invalid Phase 8 value/baseline, absent/nonfinite/nonpositive ALSA, and unknown colors contribute nothing. Colorless cards contribute no lane evidence. Each colored occurrence distributes its weighted evidence equally over its k Domain colors, totaling its evidence. The current pack may contribute before a selection; there is no circular input from either contextual stage.

Pack recency uses the current pack number minus the observation's pack number: weight 1.00 for current, 0.50 for previous, 0.25 for two back. During Pack 3, Pack 1 evidence is retained at 0.25. Future-pack observations and later picks within the current pack are excluded. There is no direct penalty for missing cards/colors or one weak pack.

```text
meanLaneEvidence = (W + U + B + R + G) / 5
rawLaneSupport_c = clamp((laneEvidence_c - meanLaneEvidence) / LaneEvidenceScale, -1, +1)
observationConfidence = clamp(eligibleObservationCount / FullLaneConfidenceAfterObservations, 0, 1)
effectiveLaneSupport_c = rawLaneSupport_c * observationConfidence
```

An eligible observation means a distinct recorded pack coordinate with at least one positive colored signal after recency weighting. Multiple occurrences in one pack contribute evidence but only one confidence observation. Zero-weight/early/weak/missing-data packs do not inflate confidence. Default confidence at 0/1/2/4/>4 meaningful packs is 0/0.25/0.50/1/1. Older meaningful packs retain their count while their evidence decays.

The exact relative-support regression is green evidence 2.0, other colors 0: mean 0.4, raw green +0.8, raw other colors -0.2, before confidence. With two meaningful packs, effective green is +0.4 and other colors -0.1. One fully meaningful green card in one pack gives only +0.1 effective green support, producing a +0.1 pp Quick Draft or +0.2 pp human adjustment. Even many cards in one isolated pack cannot exceed confidence 0.25.

## Human/bot behavior and final ranking

Premier and Traditional use the human cap ±0.020. Quick Draft uses the bot cap ±0.010. Application uses the requested provider-neutral format already resolved from Arena mode. Unknown/unsupported format gets zero lane adjustment while evidence remains inspectable. Quick Draft reflects bot passing behavior; its validation cannot establish human-seat inference.

```text
LaneFit(colorless) = 0
LaneFit(monocolor) = effective support of that color
LaneFit(multicolor) = minimum effective support among its required colors
laneAdjustment = LaneFit * formatMaximumLaneAdjustment
finalContextualValue = clamp(Phase9AContextualValue + laneAdjustment, 0, 1)
```

Unknown candidate colors are neutral. Raw GIH, Phase 8 value and Phase 9A color adjustment/value are retained. A Phase 8 unscored card stays unscored through the final stage and receives no rank or top flag. Lane fit alone cannot create a strength value. Colorless final values equal their Phase 9A values.

Sort scorable occurrences by final Phase 9B value descending, Phase 9A value descending, Phase 8 value descending, GIH sample descending, then original pack index ascending. The returned cards and underlying Arena pack stay in original order. Duplicate copies remain independent occurrences. Fewer than two scored cards receives no final rank or Context Pick.

## Replay, partial history and application/UI

The existing coordinator owns observations and session resets. An exact replay updates one coordinate/ordered-identity entry and does not duplicate evidence or confidence. A selected card keeps the fact that it reached the observed pick; selection completes the existing observation without removing or adding its signal. In-memory observations reset with the existing session-generation lifecycle. No new Arena truth store exists.

Only observed packs contribute. Mid-draft startup exposes the first actual observed coordinate and existing partial coverage; no earlier packs are invented from selected history. Meaningful lane observation count/confidence is distinct from total recorded-pack and completed-pick coverage. Delayed statistics locally enrich earlier observed packs' ALSA/baseline from the existing environment. Snapshot/generation/revision checks continue to suppress stale results. New/no-active packs clear final ranks and rail state.

Badge contextual rank and the sole gold treatment now use the final Phase 9B result. Raw GIH, its low-sample marker, ALSA, geometry, calibration and native passive mode are unchanged. The existing rail shows final Context Pick/name; Stats/Pool/Context ranks; pool colors/progress; signed pool/lane adjustments; at most two positive lane signals; Quick/bot or Premier/Traditional/human source; meaningful count/confidence; partial-history start and statistics coverage. A different Stats Pick remains a separate line. The original Phase 9A summary remains separately available in the ViewModel.

Collapsed diagnostics contain all previous-stage information plus all five lane evidence/raw/effective values, confidence, constants, and each meaningful signal's identity/coordinate/slot/colors/ALSA/statistical value/baseline/lateness/strength/position/evidence/recency. Final per-card diagnostics retain all stages and their ranks. Nothing adds lane text to normal passive badges or claims a color is definitely open.

## Files changed

| Area | File | Change |
| --- | --- | --- |
| Engine | `src/DraftTG.RecommendationEngine/LaneDetectionConfiguration.cs` | New tunable configuration and mode/position/recency policies |
| Engine | `src/DraftTG.RecommendationEngine/LaneOpennessProfile.cs` | New signal/evidence/profile types and analysis |
| Engine | `src/DraftTG.RecommendationEngine/LaneContextualRecommendationEngine.cs` | New final occurrence/result types and composition/ranking |
| Engine | `src/DraftTG.RecommendationEngine/DraftPackObservationHistory.cs` | Extend existing observations with ALSA and baseline |
| Application | `src/DraftTG.Application/LimitedStatisticsService.cs` | Supply ALSA during existing local observation enrichment |
| Application | `src/DraftTG.Application/LimitedStatisticsUpdate.cs` | Capture complete lane inputs and expose final result |
| Application | `src/DraftTG.Application/LaneRecommendationPresentation.cs` | New concise and detailed lane formatting |
| UI | `src/DraftTG.App/MainWindowViewModel.cs` | Present final result/rail while retaining earlier stages |
| UI | `src/DraftTG.App/OverlayViewModel.cs` | Final rank/gold with unchanged badge layout |
| UI | `src/DraftTG.App/RailPanelView.axaml` | Bind final summary and retain collapsed diagnostics |
| Tests | `tests/DraftTG.RecommendationEngine.Tests/LaneDetectionTests.cs` | 17 focused model/ranking/observation tests |
| Tests | `tests/DraftTG.Application.Tests/LaneOrchestrationTests.cs` | 2 focused coordinator/enrichment tests |
| Tests | `tests/DraftTG.App.Tests/LanePresentationTests.cs` | 2 focused final presentation/stale-clearing tests |
| Tests | `tests/DraftTG.ArenaIntegration.Tests/FileArenaLogSourceTests.cs` | Bounded replacement retry in existing fixture; original assertions preserved |
| Documentation | `ARCHITECTURE.md` | Exact formulas, design and limits |
| Documentation | `ROADMAP.md` | Mark Phase 9B implemented; keep 9C/9D deferred |
| Documentation | `PHASE9B_REPORT.md` | This report |

Ignored artifacts under `artifacts/phase9b/` record before hashes, preservation verification, build and final test logs. Prior phase work and the user's global.json edit remain preserved.

## Automated validation

Executed:

```text
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
```

Final result: **0 build warnings, 0 build errors; 594 tests passed, 0 failed, 0 skipped**. All existing 573 tests pass with their assertions preserved. **21 new tests** stay within the requested approximately 15–25: 10 core model tests, 4 ranking tests, 3 observation tests, 2 Application tests and 2 UI tests. Table-driven examples share focused facts rather than creating exhaustive color/format/coordinate combinations.

| Project | Passed |
| --- | ---: |
| Domain | 42 |
| Data | 134 |
| ArenaIntegration | 94 |
| RecommendationEngine | 102 |
| Application | 135 |
| App | 87 |
| **Total** | **594** |

Tests cover late versus expected/early arrival; adjusted quality versus baseline/below-baseline; early-pick suppression/ramp; colored fractions/colorless; the +0.8/-0.2 regression; meaningful-pack confidence; recency; missing/invalid inputs; configuration; future-observation exclusion; conservative multicolor fit; close reorder/large-gap pivots; bot/human/unknown caps; unscored/colorless preservation; final clamping/ties/order/duplicates; replay; retained selected-card evidence; partial coverage; current-pack signals; delayed ALSA/baseline enrichment; session reset; independently retained stages; rail diagnostics; final gold/raw values; stale clearing; unchanged badge geometry/objects/passive policy. Existing Phase 8/9A and native calibration tests are reused.

The existing Arena temporary-file replacement test intermittently failed with `UnauthorizedAccessException` both inside and outside the filesystem sandbox. The fixture replaces its temporary log immediately after receiving the first channel line; the asynchronous poll can still hold the reader until its remaining reads/probe complete. The fixture now retries only access-denied replacement, in 10 ms steps for at most one second, and still fails if replacement remains denied. The original reset/line assertions remain intact. No production source/parser or OS policy changed. Final validation passes with this narrowly scoped test-timing fix, and three additional focused reruns of that replacement test also pass.

## Physical and live validation status

| Validation | Status |
| --- | --- |
| Windows physical overlay | Not performed; no MTGA or DraftTG process was running during the process check |
| macOS physical overlay | Not performed; this is a Windows environment |
| Quick Draft live P1P5–P1P10 | Not performed; no active Arena draft was available |
| Premier/Traditional human draft live | Not performed; no human draft was available |

Automated synthetic Quick Draft flow exercises repeated signals at P1P8–P1P10 and resulting close-candidate reordering. That is an automated scenario, not live draft validation. Actual badge alignment, click-through, Arena focus, real bot passing and human passing have not been physically validated for Phase 9B. No human-validation claim is derived from bot-mode tests. The next physical check should observe several real Quick Draft picks around P1P5–P1P10, inspect isolated versus repeated signals and modest adjustments, then validate Premier/Traditional separately when available.

## Known limitations

- Lane inference is probabilistic. Random pack color composition can produce misleading or sparse evidence; relative negative support does not prove a color is closed.
- ALSA is population-level historical availability, not a guarantee of when an individual card should disappear. Strength uses Phase 8 shrinkage rather than raw GIH.
- Quick Draft signals reflect bots, while Premier/Traditional reflect human passing behavior. Different caps are heuristics, not a calibrated human/bot model.
- Pass direction changes across packs; decay reduces old influence without reconstructing seats, packs or wheel identities. Repeated actual observations remain evidence; exact replay is idempotent.
- Coverage may be partial and observations are session-local/in-memory. Missing ALSA, baseline, scores or colors gives no signal. Indistinguishable duplicate selected slots remain unknown as in Phase 9A.
- Confidence counts distinct meaningful pack coordinates, not statistically independent events. Recency decays evidence rather than the count; real draft validation is needed to tune these choices and constants.
- Phase 9B knows colors only. No set archetypes, trophy decks, deck-color rates, synergy, curve/deck composition, fixing, splashes or card-text analysis exist.
- No external AI recommendations, auto-picking, mouse/keyboard automation or OCR were added. Provider/cache/parser and native behavior remain unchanged.
- Windows/macOS physical and Quick/human live validation remain outstanding.

Stopped after Phase 9B.
