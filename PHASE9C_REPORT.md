# Phase 9C — Set archetype and color-pair awareness

Implemented a fourth recommendation stage after Stats, Pool and Lane. All 687 existing tests remain passing; 24 focused additions bring the total to 711. Windows and portable builds have zero warnings/errors. Live Phase 9C recommendation validation remains outstanding because the Windows inspection runtime failed to initialize. Phase 9D remains unstarted.

## Files and types

New production files:

| File | Responsibility |
| --- | --- |
| `src/DraftTG.Domain/ArchetypeProfiles.cs` | Provider-neutral pair/definition/profile values and catalog contract |
| `src/DraftTG.Data/SetArchetypeProfileCatalog.cs` | Validated embedded profile loading and exact-set lookup |
| `src/DraftTG.Data/Archetypes/WOE.json` | Ten descriptive WOE archetypes, with source and story metadata |
| `src/DraftTG.RecommendationEngine/ArchetypeRecommendationEngine.cs` | Configuration, confidence, empirical affinity and final ranks |
| `src/DraftTG.Application/ArchetypeStatistics.cs` | Pair load identity and UI-free data status |
| `src/DraftTG.Application/ArchetypeRecommendationPresentation.cs` | Final rail summary and detailed archetype diagnostics |

Changed production files:

| File | Change |
| --- | --- |
| `src/DraftTG.Data/DraftTG.Data.csproj` | Embed `Archetypes/*.json` |
| `src/DraftTG.Data/SeventeenLandsRatings.cs` | Separate pair-client contract and optional result pair identity |
| `src/DraftTG.Data/SeventeenLandsCardRatingsClient.cs` | Verified color query and isolated pair cache/load path |
| `src/DraftTG.Application/LimitedStatisticsService.cs` | Exact pair response validation, existing identity mapping, full pair baseline |
| `src/DraftTG.Application/LimitedStatisticsCoordinator.cs` | Lazy bounded asynchronous pair loads and guarded recomputation |
| `src/DraftTG.Application/LimitedStatisticsUpdate.cs` | Fourth immutable result and occurrence-key join |
| `src/DraftTG.Application/CurrentPackCardPresentation.cs` | Fourth-stage identity validation and final rank/value |
| `src/DraftTG.Application/LaneRecommendationPresentation.cs` | Share existing pool/lane explanatory context without another pick label |
| `src/DraftTG.App/MainWindowViewModel.cs` | Final pick, archetype status and diagnostics; retain prior-stage properties |
| `src/DraftTG.App/RailPanelView.axaml` | Bind final recommendation and archetype metadata |

Four new test files are listed below. `ARCHITECTURE.md`, `ROADMAP.md` and this report document the implementation. No new package dependency was added. The existing production client automatically supplies the optional pair-client seam.

New public concepts include `ArchetypeColorPair`, `ArchetypeDefinition`, `SetArchetypeProfile`, `ISetArchetypeProfileCatalog`, `SetArchetypeProfileCatalog`, `ArchetypeConfiguration`, `ArchetypePairEvidence`, `ArchetypeContextProfile`, `PairGihStatistics`, `ArchetypePairStatistics`, `ArchetypeMath`, `ArchetypeCardAffinity`, `ArchetypeCardRecommendation`, `ArchetypeRecommendationResult`, `ArchetypeRecommendationEngine`, `ISeventeenLandsPairRatingsClient`, `ArchetypeDataStatus` and `ArchetypeRecommendationPresentation`.

## WOE profile

The primary source is Mark Rosewater's official Wizards article [Wilds of Eldraine Story, Part 2](https://magic.wizards.com/en/news/making-magic/wilds-story-part-2). Strategy names/descriptions are concise curated metadata; they contain no numeric card ratings.

| Display pair | Strategy | Story |
| --- | --- | --- |
| WU | Tap Tempo | Snow Queen |
| UB | Faeries Control | Sleeping Beauty |
| BR | Rats Aggro | Pied Piper |
| RG | Big-Creature Beatdown | Little Red Riding Hood |
| GW | Enchantments and Roles | Beauty and the Beast |
| WB | Sacrifice and Bargain | Snow White |
| UR | Spells and Prowess | Sorcerer's Apprentice |
| BG | Food Midrange | Hansel and Gretel |
| RW | Celebration Aggro | Cinderella |
| GU | Ramp and Adventures | Jack and the Beanstalk |

The profile validates an exact uppercase alphanumeric set code, two distinct WUBRG colors, unique normalized pairs, nonempty name/description and an HTTPS source. Story metadata is optional for future sets. Unknown fields, including invented numeric ratings, reject. The catalog discovers embedded profile files; another set does not require an engine conditional. Unknown sets return no profile, make no pair request, and retain Lane values with zero archetype adjustment.

## Exact API audit

Four manual JSON probes were made on 2026-10-03. No HTML card-data page was scraped and no live-network test was added. Responses are retained in the ignored local `artifacts/phase9c-api-audit` directory.

All probes used `expansion=WOE&event_type=QuickDraft&time_period=ALL_TIME`:

| Final query component | HTTP result | Rows | Evidence |
| --- | --- | ---: | --- |
| `deck_color=BG` | 200 | 324 | All 324 GIH rates/counts match the pre-existing unfiltered overall cache |
| `colors=BG` | 200 | 324 | GIH samples/rates change to the filtered environment |
| `colors=GU` | 200 | 0 | Display pair order is not accepted for this pair |
| `colors=UG` | 200 | 324 | WUBRG order returns the intended blue/green dataset |

Observed examples, without rounding the rate decimals:

| Card | Unfiltered / `deck_color=BG`: sample, GIH | `colors=BG`: sample, GIH |
| --- | --- | --- |
| Archon of the Wild Rose | 7,300; 0.61753425 | 37; unavailable |
| Candy Grapple | 83,626; 0.61211824 | 22,877; 0.60987018 |
| Hamlet Glutton | 102,503; 0.59090953 | 37,542; 0.6044963 |

For `colors=UG`, Candy Grapple has sample 1,262 and GIH 0.6473851, while Hamlet Glutton has sample 13,381 and GIH 0.59711531. The `deck_color` query appears to be ignored: every returned GIH row matches the earlier overall cache. The implementation therefore uses **`colors` with WUBRG-normalized values**, never `deck_color`.

The BG URI is [the tested card-data query](https://www.17lands.com/api/card_data?expansion=WOE&event_type=QuickDraft&time_period=ALL_TIME&colors=BG); blue/green uses [colors=UG](https://www.17lands.com/api/card_data?expansion=WOE&event_type=QuickDraft&time_period=ALL_TIME&colors=UG). Display GU/GW/RW becomes provider UG/WG/WR. Overall requests remain unfiltered. GIH/sample mapping remains `ever_drawn_win_rate` / `ever_drawn_game_count`; pair GIH never overwrites overall GIH.

Exact supported-format construction is tested for QuickDraft, PremierDraft and TradDraft. The live probes above cover QuickDraft only; live data availability for other formats was not claimed. Application rejects a response whose set, provider format or pair differs from its requested identity. An unavailable exact dataset leaves zero adjustment; no Premier-for-Quick substitution occurs.

The endpoint is unversioned and not a supported unlimited integration. Its saved responses also contain restrictive external-use notes; this implementation does not remove those notes or claim provider permission. No trophy, aggregate deck-color win-rate, public draft or full-game dataset is consumed.

## Confidence and model

All values below are explicit product choices, not optimality claims. Defaults are constructor-configurable:

| Setting | Default |
| --- | ---: |
| MinimumArchetypeConfidence | 0.35 |
| MinimumArchetypeLead | 0.10 |
| PairPriorEquivalentGames | 300 |
| ArchetypeLiftScale | 0.04 |
| MaxArchetypeAdjustment | 0.015 |
| MaxPairDatasetsPerDraft | 3 |
| ModelVersion | `pair-gih-affinity-v1` |

Confidence uses the original Phase 9A color evidence and progress, independently of Phase 9B lane evidence:

```text
coverage = (A + B) / (W + U + B + R + G)
balance = 2 * min(A, B) / (A + B)
pairPoolFit = coverage * (0.75 + 0.25 * balance)
confidence = clamp(pairPoolFit * Phase9AProgressFactor, 0, 1)
```

Each zero denominator produces zero. Candidates sort by confidence, then canonical pair code for deterministic diagnostics. An active pair needs confidence >= 0.35 and top-minus-second confidence >= 0.10. Empty pools and single-color ties remain Unsettled. Every snapshot recomputes confidence; the first active pair is not locked for the draft.

The pair baseline uses every valid GIH row in the filtered provider environment, not just the current pack. Finite rates in [0,1] and positive sample counts are required:

```text
pairBaseline = sum(pairGIH_i * pairSample_i) / sum(pairSample_i)
pairAdjusted = (pairSample * pairGIH + 300 * Phase8AdjustedValue) / (pairSample + 300)
overallRelative = Phase8AdjustedValue - OverallEnvironmentBaseline
pairRelative = pairAdjusted - pairBaseline
archetypeLift = pairRelative - overallRelative
affinity = clamp(archetypeLift / 0.04, -1, +1)
adjustment = affinity * archetypeConfidence * 0.015
finalValue = clamp(Phase9BValue + adjustment, 0, 1)
```

Regression examples protect the requested exact arithmetic: a 300-game .62 pair result shrunk toward .56 with a 300-game prior equals .59. With overall .57/baseline .55 and pair-adjusted .60/baseline .56, lift is .02, normalized affinity is .50, and confidence .60 produces .0045 (+0.45 pp).

Mono-A, mono-B and AB cards are eligible. Off-pair, colorless, 3+ color and unknown-color cards receive no adjustment. Missing/invalid GIH, unavailable pair/overall baseline, unsettled context and unknown sets remain neutral. Available raw pair metrics remain inspectable even when a score cannot be adjusted. An unscored Phase 8 card remains unscored and unranked; pair evidence cannot create a score.

A full-confidence card adjustment is bounded by +/-1.5 pp. Two cards can therefore differ by at most 3 pp of Phase 9C adjustment, refining close decisions while preserving large Lane-score gaps. Sorting is final value descending, Lane value descending, Pool value descending, Stats value descending, overall GIH sample descending, original pack index ascending. The original pack order and duplicate occurrence identities remain intact; fewer than two scored cards still yields no Context Pick.

## Loading and cache

The coordinator initially publishes the Lane score. Only an active pair triggers loading, off the monitor/UI thread. Completed pair data recalculates the latest current snapshot. New active GU stops applying BG immediately; a late BG completion can be cached but is evaluated against the current pair and cannot attach old affinity. New draft/environment generations cancel pending work and reject stale callbacks; revision and exact snapshot guards also protect publication.

The default maximum is three distinct pair-dataset load attempts per draft, with zero-to-ten allowed by configuration. The budget includes client calls served by disk cache, so it is a conservative upper bound on extra network calls. Loaded, failed or pending pair keys are reused on subsequent pack observations. A failed key is not repeatedly retried during the same draft. Exhausted budget is diagnostic and neutral.

The shared provider client retains these cache principles:

- Set, exact format/event type, normalized pair, ALL_TIME, card-data schema v3 and pair schema v1 identify a dataset.
- Example filename: `WOE_QuickDraft_ALL_TIME_v3_BG_pair_v1.json`, under the existing `limited-data/17lands` application-data directory.
- Payload validation checks `/api/card_data`, time period, requested/source format, expansion, `DeckColors`, `PairSchemaVersion`, timestamp and dataset validity. Copying BG bytes into a GU cache filename cannot authorize reuse.
- Existing overall v3 filenames remain unchanged; older overall payloads need no new pair fields.
- RAM/disk caching uses the existing 24-hour TTL and atomic temporary-file replacement. Refresh failure retains valid stale pair evidence and reports stale state.
- Pair network attempts are serialized with at least one second of spacing. Failures have the existing one-hour cooldown; global HTTP 429 Retry-After is respected. There is no automatic retry loop or per-card request.

## UI and preserved behavior

The normal rail has one final Context Pick and a separate Stats Pick only when different. It exposes Stats, Pool, Lane and Final ranks; pool/lane explanations; archetype name/description/confidence; adjustment; pair GIH/sample when available; and pair loading/cache/unavailable state. Unsettled profiles show the leading two candidates. Detailed collapsed diagnostics include all pair evidence, configuration/source, baselines, pair-adjusted values, lift, normalized affinity, signed adjustment and final values/ranks.

Passive badge XAML and geometry are unchanged: rank, raw overall GIH, ALSA and gold styling remain the same. Gold #1 now follows the fourth stage's final Context Pick. Statistics updates replace immutable occurrence presentations while retaining the badge wrappers and confirmed visual map. Identity validation includes the new stage's key and original Lane result.

Phase 8, 9A and 9B engines, card identity/statistics mapping, Arena parsing, Windows capture, references, artwork matcher, pack/frame synchronization and manual fallback are unchanged. Final SHA256 checks match the pre-change hashes of the statistical, color, pool and lane engine files, `CardTemplateRecognition.cs` and `PackFrameSynchronization.cs`. Prior results remain individually available in both the update and nested fourth-stage result. No Oracle-text heuristics, trophy analysis, deck construction or automatic Arena input was added.

## Focused tests and validation

| New file | Added tests | Coverage |
| --- | ---: | --- |
| `tests/DraftTG.RecommendationEngine.Tests/ArchetypeRecommendationTests.cs` | 14 | Empty/single-color/balanced/close pools, exact confidence/shrinkage/lift, valid weighted baseline, unknown sets, close reversal, large gaps, negative affinity, ineligible/missing/unscored cards, wrong identities and duplicate occurrences |
| `tests/DraftTG.Data.Tests/ArchetypeDataTests.cs` | 4 | Ten WOE pairs/source, invalid profiles and optional story, actual query/exact formats, independent cache identity/TTL/stale fallback/failure cooldown |
| `tests/DraftTG.Application.Tests/ArchetypeOrchestrationTests.cs` | 4 | Lazy loading, Lane while pending, recomputation/cache reuse, pair change with both cached and late old responses, request budget/new draft, exact-format rejection/no request storm |
| `tests/DraftTG.App.Tests/ArchetypePresentationTests.cs` | 2 | Active archetype/four-rank rail, final gold #1, unchanged raw metrics/wrappers/geometry/manual map, stale-pack and foreign-stage rejection |
| **Total new** | **24** | No live-network tests |

| Project | Previous | Current |
| --- | ---: | ---: |
| Domain | 42 | 42 |
| RecommendationEngine | 102 | 116 |
| Data | 134 | 138 |
| Application | 135 | 139 |
| ArenaIntegration | 94 | 94 |
| App | 180 | 182 |
| **Total** | **687** | **711** |

Validation commands:

```text
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
dotnet build DraftTG.sln -p:DraftTGPortableBuild=true
dotnet test DraftTG.sln --no-build -p:DraftTGPortableBuild=true
```

Both target configurations pass all 711 tests with zero failures/skips. Both builds report zero warnings/errors. The final normal Windows build was restored after the portable run. Local evidence is saved in ignored `artifacts/phase9c-validation/windows-build.log`, `windows-test.log`, `portable-build.log` and `portable-test.log`.

The user's Phase 9C request confirms prior Phase 9B.7 Windows physical acceptance. A new Phase 9C Windows check was attempted using the Computer Use skill; its Node runtime failed before app inspection with “The system cannot find the path specified. (os error 3).” No live early-pool transition, real affinity rerank or gold/placement observation is claimed for Phase 9C. Those remain to be checked in a rebuilt WOE live session when inspection is available.

No macOS host was available. The portable net10.0 build and full automated suite were run on Windows, verifying shared code and excluding Windows SDK-only paths. This does not prove native macOS UI, packaging or live Arena behavior. Existing macOS manual localization fallback is preserved.

## Statistical limitations

Confidence reflects drafted color evidence and progress, not a posterior probability of the optimal archetype. The confidence thresholds, prior, lift scale and maximum adjustment are tunable product choices.

Filtered GIH measures observed card performance conditional on deck colors. It is affected by player skill, selection, card availability and deck construction, and does not establish causal synergy. The pair baseline is a sample-weighted card-GIH environment, not an aggregate deck win rate; samples can share games and are not independent. Shrinkage limits sparse-sample influence but supplies no confidence interval and cannot remove selection bias.

Only WOE descriptive metadata is supplied initially. ALL_TIME combines historical play and the unversioned API can change or disappear. Exact pair data may be sparse/unavailable, stale data may lag, and the bounded request budget can leave a later pair without affinity. These conditions preserve Phase 9B recommendations rather than guessing data. Phase 9D remains unstarted.
