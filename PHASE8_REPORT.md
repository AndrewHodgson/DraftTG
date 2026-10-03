# Phase 8 completion report

Phase 8 is implemented: advisory baseline statistical ranks for each current-pack occurrence, a Stats Pick, compact badge ranks, and explicit coverage/baseline status. Phase 9 has not started. Phase 7.5 GIH mappings, the provider endpoint/cache schema, Arena parsing, pack order, calibration geometry and native click-through policy were preserved.

## Model and types

New provider-neutral types in `DraftTG.RecommendationEngine`:

- `StatisticalRecommendationEngine`
- `StatisticalRecommendationConfiguration`
- `RecommendationAvailability`
- `DraftRecommendation`
- `CardRecommendation`

The engine depends only on Domain. It performs no networking, file access, operating-system calls or UI work and receives no Arena, Scryfall or statistics-provider DTOs. Models retain immutable occurrence results in original pack order, raw GIH/sample, nullable adjusted values and data weights, ranks, top index, availability, scored/total coverage, environment baseline/card count, and model/configuration metadata.

Exact formulas:

```text
b = sum(p_i * n_i) / sum(n_i)
s = (n * p + k * b) / (n + k)
data weight = n / (n + k)
k = PriorEquivalentGames = 500
```

`p` is raw GIH WR, `n` is its GIH sample, `b` is the active environment GIH-sample-weighted mean, and `k` is the configurable prior-equivalent games. 500 is a Phase 8 product/model choice, not a statistical optimality claim. Model version: `gih-shrinkage-v1`. Invalid prior values are rejected. Finite rates in [0, 1] and positive samples are required; missing/zero/negative samples and NaN/infinite/out-of-range rates do not contribute to baseline or scoring. Double arithmetic avoids integer overflow, including maximum-int sample/prior regression cases.

ALSA/ATA, play rate, OH WR, drawn WR and improvement do not affect scoring. No color commitment, archetype, curve, synergy, signals, fixing, card-text analysis or drafted-pool inputs are used. The engine takes a `DraftPack`, never draft history. Phase 9 will add contextual draft/pool-aware evaluation.

## Baseline integration and coverage

Application prepares an environment catalog from the whole loaded response once, independently of the pack/history. It contributes one entry per nonconflicting exact source name with known Domain identity; a deterministic representative printing is used only for baseline aggregation (prefer source expansion, then ordinal identifier). Actual pack identities continue to come from the existing Arena/Scryfall resolver. Duplicate pack slots or multiple printings cannot overweight the environment; drafted history cannot change it. A missing environment catalog is empty and never silently replaced by pack/history statistics.

Cards lacking a valid GIH/sample are unscored: adjusted value and rank are null, not fake zero/baseline/bottom values. Availability rules:

- No valid environment baseline: `NoEnvironmentBaseline`; no score/rank/pick.
- Fewer than two scorable occurrences: `InsufficientComparableStatistics`; no comparative rank or top pick. A sole valid occurrence retains its genuine adjusted value.
- Two or more, all pack occurrences scored: `ReadyCompleteCoverage`.
- Two or more, some unscored: `ReadyPartialCoverage`.

Coverage exposes scored count, total pack count and fraction. Empty packs have coverage zero. Duplicate copies are independently addressable by `PackIndex` and get separate ranks. Tie order is adjusted value descending, GIH sample descending, then original pack index ascending. Card names do not break ties, and the Domain pack never changes order.

## Orchestration and UI

Mapping and recommendation calculation run on a background worker, including when the environment is already cached. Arena monitoring and initial pack presentation do not wait for scoring. Generation and snapshot checks drop obsolete calculations; ViewModel snapshot checks reject stale queued UI updates. Statistics/context/pack changes produce new recommendation output. UI visibility/panel/calibration changes do not recalculate scores.

Badges retain existing width/42-pixel height and placement. Rank appears beside raw GIH (which is still the percentage shown), ALSA stays on the second line, and the #1 Stats Pick receives a subtle gold border and rank color. Unscored cards and insufficient comparisons show no rank. Statistics updates preserve badge instances and positions. No new tooltip, popup, large card panel, animation, Arena input or auto-pick was added.

The control rail status panel shows Stats Pick/card name, `Rank model: GIH + sample shrinkage`, scored/total coverage and weighted baseline. Partial statistics and unavailable states are explicit. New packs immediately clear old ranks; no-pack/unsupported/completed states clear recommendation presentation. The product does not claim a contextual Best Pick.

## Validation

Final commands:

```text
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
```

Build: **0 warnings, 0 errors**. Tests: **471 passed, 0 failed, 0 skipped**. All original 431 tests still pass; **40 Phase 8 cases were added**:

| Project | Added | Total passing |
| --- | ---: | ---: |
| RecommendationEngine | 28 | 34 |
| Application | 7 | 122 |
| App | 5 | 77 |
| Domain | 0 | 42 |
| Data | 0 | 102 |
| ArenaIntegration | 0 | 94 |
| Total | 40 | 471 |

Engine tests cover large/tiny samples, exact shrinkage regression (0.575), weighted baseline (0.575), very large counts, small-sample baseline convergence, missing/invalid metrics, duplicate occurrences, unchanged order, all availability states, deterministic ties, ignored secondary metrics, configurable prior and immutable results. Application tests cover whole-environment baseline/history independence, printing deduplication, conflict/unknown-name exclusion, loading-to-recommendation arrival, replacement packs, stale context results, new responses/ranks/baselines and the no-participant-baseline boundary. UI tests cover #1/#2/#3, partial/unavailable status, raw-versus-adjusted GIH, stable badge instances/geometry, recomputation on data changes, irrelevant UI changes, stale rejection, no-pack clearing, calibration and injected native-policy invariants.

Windows: build/unit tests ran on Windows; Phase 8 was **not physically tested in live Arena**. No running Arena/DraftTG process was observed. macOS: **not physically tested**. Cross-platform calibration/native-policy tests still pass; no new OS-specific code was introduced. Live aligned ranks, focus and actual badge passthrough need a physical session on each platform.

## Files changed in Phase 8

- `src/DraftTG.RecommendationEngine/StatisticalRecommendationEngine.cs` (new)
- `src/DraftTG.RecommendationEngine/LimitedCardStatistics.cs`
- `src/DraftTG.Application/RecommendationPresentation.cs` (new)
- `src/DraftTG.Application/LimitedStatisticsMapper.cs`
- `src/DraftTG.Application/LimitedStatisticsService.cs`
- `src/DraftTG.Application/LimitedStatisticsCoordinator.cs`
- `src/DraftTG.Application/LimitedStatisticsUpdate.cs`
- `src/DraftTG.App/MainWindowViewModel.cs`
- `src/DraftTG.App/OverlayViewModel.cs`
- `src/DraftTG.App/CardOverlayWindow.axaml`
- `src/DraftTG.App/RailPanelView.axaml`
- `tests/DraftTG.RecommendationEngine.Tests/StatisticalRecommendationEngineTests.cs` (new)
- `tests/DraftTG.Application.Tests/RecommendationOrchestrationTests.cs` (new)
- `tests/DraftTG.App.Tests/RecommendationPresentationTests.cs` (new)
- `ARCHITECTURE.md`
- `ROADMAP.md`
- `PHASE8_REPORT.md` (new)

## Limitations

Statistical output is advisory and not pool-aware. Sparse or missing data can leave partial coverage or no meaningful comparison. The baseline includes mapped known source names; unknown identities and conflicting evidence are excluded rather than invented. The existing exact-name mapping and successful-empty-environment format fallback remain unchanged, with actual source labeled by the statistics UI. Existing refresh/cache behavior is unchanged; there is no new provider polling. New loaded responses are rescored when propagated as Application updates.

The pre-existing corrupt Git index remains untouched; diff review used a separate temporary index. Earlier Phase 7.5 changes and the user's `global.json` edit were preserved. Avalonia build logging required the same approved filesystem access as the prior phase. No scope beyond Phase 8 was implemented.


## Phase 8.1 follow-up

The subsequent Phase 8.1 migration changes the statistics source to `/api/card_data` using exact `event_type` and `time_period=ALL_TIME`, removes the legacy empty-response format fallback, and migrates caches to schema 3. The Phase 8 engine, formula, configuration and badge UI are unchanged. This report records Phase 8 validation; current migration behavior and validation are in PHASE8_1_REPORT.md.
