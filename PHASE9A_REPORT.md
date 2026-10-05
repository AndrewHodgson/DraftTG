# Phase 9A completion report

Implemented on Windows on 2026-10-03. Phase 9A adds pool-aware color commitment and an observation foundation. Phase 9B/9C scoring has not begun.

## Behavior and types

The Phase 8 engine is unchanged and separately inspectable through `LimitedStatisticsUpdate.Recommendation` and `ContextualDraftRecommendation.StatisticalRecommendation`. Its formula remains `(n*p + 500*b)/(n+500)` with the independently prepared environment baseline. Source hashes verify that its file, Data/provider code, Arena parser/state engine, native platform code and all pre-existing tests were not modified during this phase. The RecommendationEngine project still references only Domain.

New engine types:

- `ColorCommitmentConfiguration`: validated immutable scale, progress threshold, maximum adjustment and model version.
- `ColorCommitmentEvidence`: one color's evidence, raw support and effective support.
- `ColorCommitmentProfile`: five-color evidence/support, completed/unresolved counts, mean, progress, primary/secondary diagnostics, commitment strength and candidate fit.
- `ContextualCardRecommendation`: original Phase 8 occurrence/result, nullable colors, fit, adjustment, nullable context value/rank and context-pick flag.
- `ContextualDraftRecommendation`: independent Phase 8 result, pool profile, immutable ordered contextual occurrences and top index.
- `ContextualRecommendationEngine`: pure composition and deterministic contextual ranking.
- `DraftCardObservation`: original slot, identity, nullable colors and Phase 8/raw GIH/sample values.
- `DraftPackObservation`: coordinate/ordered pack identity, immutable occurrences, statistics-snapshot status and eventual selected identity/index diagnostics.
- `DraftPackObservationHistory`: immutable session observation sequence and honest coverage metadata.

`ContextualRecommendationPresentation` is the new Application formatter. Existing result/update types gain the Domain catalog, contextual result and observation history; existing snapshot adaptation also exposes resolved completed history during no-pack/completed states.

## Exact rules and configuration

For each selected history occurrence resolved through CardCatalog:

```text
monocolor: +1 to its color
k-color: +1/k to every color, totaling one
colorless: zero color evidence
meanEvidence = sum(W,U,B,R,G) / 5
rawSupport_c = clamp((evidence_c - meanEvidence) / EvidenceScale, -1, +1)
progressFactor = clamp(completedPickCount / FullCommitmentAfterPicks, 0, 1)
effectiveSupport_c = rawSupport_c * progressFactor
```

Defaults: `EvidenceScale = 4.0`, `FullCommitmentAfterPicks = 14`, `MaxColorAdjustment = 0.025`, model `pool-color-v1`. Completed picks include colorless picks. Neither the current pack nor candidate contributes. Duplicate drafted identifiers count once per actual selected occurrence; Arena's existing coordinate-idempotent state engine supplies authoritative history. Unknown metadata contributes no invented evidence and is diagnosed.

```text
colorless fit = 0
monocolor fit = effective support of that color
multicolor fit = minimum effective support among its colors
colorAdjustment = fit * MaxColorAdjustment
contextualValue = clamp(Phase8AdjustedValue + colorAdjustment, 0, 1)
```

These are transparent, configurable product heuristics for iterative tuning, not statistically optimal rules or win-rate predictions. At default settings the maximum change is ±2.5 percentage points, enough to affect close decisions while allowing large statistical gaps to justify pivots. One green pick gives green raw support 0.2, progress 1/14, effective support 0.014286 and adjustment approximately +0.0357 pp. It does not lock the draft into green. Both established colors in a two-color pool can receive positive support.

Exact regression tests verify `0.580 + 0.60*0.025 = 0.595` and `0.580 + (-0.40)*0.025 = 0.570`. Colorless scores remain exactly Phase 8. Unscored Phase 8 occurrences never receive a context score or rank from colors alone.

With at least two scorable occurrences, sort by context value descending, Phase 8 adjusted value descending, GIH sample descending, then original pack index ascending. Keep the returned occurrences and underlying pack in Arena order. Duplicate slots remain independent. With fewer than two scorable cards there is no Context Pick or rank.

## Application, observations and UI

The existing background statistics coordinator recomputes context from the current snapshot and pool. Session generation, snapshot identity and an observation revision reject stale worker results. New packs immediately clear old ranks/picks. Loading/unavailable and no-active-pack states cannot retain an old contextual recommendation. The unchanged Phase 8 result remains reproducible from the same pack and mapped statistics.

Observation history records only packs actually supplied to observation, keyed by coordinate plus ordered identity. Replay does not add a duplicate. Drafted history completes the matching coordinate's eventual selected card, including the gap after a pick and draft completion. The Application adapter resolves existing authoritative Arena picks; the parser and state engine are unchanged. A unique matching occurrence gets its index. When duplicate copies are indistinguishable, selected card identity and matching slots are retained with a null selected index rather than an invented choice.

Delayed statistics enrich already observed packs locally from the loaded environment, without extra network requests or adding earlier packs. Statistics-snapshot status distinguishes an evaluated unscored pack from one awaiting data. A missing later resolved history preserves already known selection/coverage. Observation values are not an input to any Phase 9A score.

Coverage includes first observed coordinate, starts-at-P1P1 flag, actual observation count, known completed picks and completed picks whose packs were observed; the fraction is null when no completed picks are known. Starting from only a P2P4 snapshot produces one observation and zero observed coverage of earlier picks. Packs actually available through source replay can be captured, but unseen packs are never invented from selected history. Coverage measures available recorded evidence, not physical user viewing. Observations are in memory, reset with the session, and do not create another mutable Arena session truth store.

Badge rank and single gold border now follow Context Pick. Raw GIH and its low-sample marker remain displayed; ALSA remains informational. Stats Pick stays separately available and gets a concise rail line when it differs. The rail shows Context Pick/name, stats/context rank, effective colors, signed adjustment in pp, progress and scored coverage. Collapsed diagnostics provide model constants, all five evidence/raw/effective supports, every occurrence's baseline/fit/adjustment/context/ranks, and observed-history coverage. No context value is labeled GIH WR or Best Pick. Badge geometry, slot objects during statistics updates, passive interaction and calibration remain unchanged.

## Files changed

| Area | File | Change |
| --- | --- | --- |
| Engine | `src/DraftTG.RecommendationEngine/ColorCommitment.cs` | New configuration, evidence and profile |
| Engine | `src/DraftTG.RecommendationEngine/ContextualRecommendationEngine.cs` | New contextual types and composition |
| Engine | `src/DraftTG.RecommendationEngine/DraftPackObservationHistory.cs` | New immutable observation foundation |
| Application | `src/DraftTG.Application/ContextualRecommendationPresentation.cs` | New concise and detailed formatting |
| Application | `src/DraftTG.Application/LimitedStatisticsService.cs` | Attach Domain catalog and enrich observed statistics |
| Application | `src/DraftTG.Application/LimitedStatisticsUpdate.cs` | Preserve Phase 8 and expose context/observations |
| Application | `src/DraftTG.Application/LimitedStatisticsCoordinator.cs` | Session observations, replay completion and stale revision checks |
| Application | `src/DraftTG.Application/ArenaDraftSnapshotAdapter.cs` | Resolve existing completed history without requiring an active pack |
| Application | `src/DraftTG.Application/ArenaDraftSnapshotResult.cs` | Expose resolved Domain history |
| UI | `src/DraftTG.App/MainWindowViewModel.cs` | Contextual occurrence presentation and rail status |
| UI | `src/DraftTG.App/OverlayViewModel.cs` | Context rank and sole Context Pick gold treatment |
| UI | `src/DraftTG.App/RailPanelView.axaml` | Concise context/Stats Pick and collapsed diagnostics |
| Tests | `tests/DraftTG.RecommendationEngine.Tests/ColorCommitmentTests.cs` | Evidence, progress, fit and configuration |
| Tests | `tests/DraftTG.RecommendationEngine.Tests/ContextualRecommendationTests.cs` | Exact scoring, flexibility, pivots, missing values, ties and order |
| Tests | `tests/DraftTG.RecommendationEngine.Tests/DraftPackObservationTests.cs` | Idempotence, enrichment, completion, ambiguity and partial coverage |
| Tests | `tests/DraftTG.Application.Tests/ColorContextOrchestrationTests.cs` | Composition, workers, picks, reset, delayed data and dependency boundary |
| Tests | `tests/DraftTG.App.Tests/ContextualRecommendationPresentationTests.cs` | Gold/ranks/raw GIH/ALSA, rail, stale clearing, geometry and interaction policy |
| Documentation | `ARCHITECTURE.md` | Exact Phase 9A design, rules and limitations |
| Documentation | `ROADMAP.md` | Mark 9A implemented; 9B/9C remain not started |
| Documentation | `PHASE9A_REPORT.md` | This completion report |

Ignored validation artifacts are under `artifacts/phase9a/`: before hashes, source verification, build transcript and test transcript. Prior phase work and the user's global.json edit are preserved.

## Validation

Executed the requested commands:

```text
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
```

Build: **0 warnings, 0 errors**. Tests: **573 passed, 0 failed, 0 skipped**. All pre-existing 504 tests remain unchanged and pass, with **69 new Phase 9A cases**: 51 engine, 11 Application and 7 UI cases.

| Project | Passed |
| --- | ---: |
| Domain | 42 |
| Data | 134 |
| ArenaIntegration | 94 |
| RecommendationEngine | 85 |
| Application | 133 |
| App | 85 |
| **Total** | **573** |

Tests cover empty/first/five-green pools, two-color and 1–5-color fractional evidence, duplicate picks, colorless progress, 0/1/7/14/>14 progress, configuration validation, required-color minimum fit, exact positive/negative adjustments, clamping, equal/close reordering, early flexibility, large-gap pivots, unchanged colorless scores, unscored cards, single-card suppression, deterministic ties/duplicate slots/order, independent Phase 8 reproduction, replay-safe observations, no-pack/final-pick completion, delayed statistics, unknown-history preservation, mid-draft partial coverage, new-session reset, raw UI values, separate picks, stale clearing, calibration and passive-policy preservation.

Windows physical status: **not performed**. No MTGA or DraftTG process was running during the check. Automated UI/model tests and compiled Avalonia bindings pass; no in-game badge alignment, focus retention, click-through or real early/late draft behavior is claimed.

macOS physical status: **not performed**; this is a Windows environment. The implementation uses shared managed code, and existing platform-policy tests pass, but physical macOS acceptance is still outstanding.

## Known limitations

- Every drafted colored card contributes equally, regardless of power or eventual playability. There is no pool-quality model.
- Colors use Domain ColorSet only. Multicolor minimum fit can be conservative; there is no hybrid/mana-symbol, fixing or splash analysis.
- Constants require future product tuning; contextual ranking values are not estimated win rates.
- Missing statistics stay unscored; missing color metadata is diagnosed and neutral. Existing exact-name/provider coverage limitations still apply.
- Observation coverage can be partial, is held only in memory, and cannot identify which indistinguishable duplicate slot was selected.
- No passed-card/lane/ALSA-openness signals, bot/human inference, archetypes, trophy data, synergy, curve, deck needs, text analysis, deck building, automatic Arena input or OCR were added.
- Live Windows and macOS physical overlay validation remains outstanding. No new provider fetch was needed or performed for Phase 9A.

Stopped after Phase 9A.
