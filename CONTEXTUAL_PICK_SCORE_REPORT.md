# Contextual Pick Score V1

## Pre-implementation audit and design

The score answers how useful a card is to the current draft, independently of ordinal pack rank. This is DraftTG's transparent heuristic, not a reproduction of another product's formula or a predicted win rate.

Repository mismatches/limits found before implementation:

- Phase 8 already supplies adjusted GIH and the sample-weighted environment baseline, with a 500-game prior. Reuse its result directly.
- Phase 9B supplies normalized conservative/min-color support, observation confidence and format-scaled lane adjustments. It measures positive late-card evidence, not literal proof that absent colors are closed.
- Phase 9C supports empirical affinity only with an active known-set pair and exact-format pair data. WOE has a descriptive profile; FRA does not currently have one. Unsettled/missing affinity remains neutral.
- Phase 10B/C require 23 eligible spells for a full 40. Projection must also handle incomplete pools; reuse the same builder with a smaller nonland target and explicitly label an incomplete shell. Do not change the completed-deck algorithm.
- The statistics participant mapper currently includes exact history, but omits recovered-only inventory. Include the existing authoritative `DraftedPool` for projected-deck statistics, without adding/reconstructing picks.
- Structured metadata supports creatures, lands, mana value, early spells, primary-face mana demand and direct known mana production. Oracle text exists but there is no trustworthy semantic removal/synergy classifier. Add a profile extension point without scoring unverified semantic roles.
- Trophy transport/corpus types exist, but the current production source guard prevents a permitted exact-format corpus. No structural targets can be claimed from it. Preserve zero trophy-frequency scoring; use existing deck constraints for V1. Public bulk-corpus work is future work.

Concrete constants and formula agreed for implementation:

```
Q = clamp(0.5 + (Phase8AdjustedGIH - EnvironmentBaseline) / 0.16, 0, 1)
L = clamp(0.5 + 0.5 * Phase9BLaneAdjustment / MaxHumanLaneAdjustment, 0, 1)
A = clamp(0.5 + 0.5 * NormalizedPairAffinity * ActivePairConfidence, 0, 1)
```

Unavailable/disabled lane or archetype evidence is neutral 0.5. Quick Draft uses the already-scaled lane adjustment (normally half human influence), not a second bot penalty. GIH is the only quality metric: ALSA/ATA are not quality inputs. Phase 8 sample shrinkage supplies reliability; no extra sample multiplier is added.

Weights Q/L/A/D are 0.70/0.20/0.10/0.00 through four completed picks, 0.50/0.20/0.15/0.15 at pick 21, and 0.30/0.10/0.20/0.40 at pick 35 and later. Interpolate each adjacent vector with `smoothstep(t)=t²(3−2t)`. Default expected picks = 42, picks per pack = 14. Coordinate-implied progress may establish stage when chronology is missing; it never invents pool members.

For D, evaluate the pool once and each immutable pool-plus-candidate independently across viable two-color projections. Reuse unchanged eligible-pool projections and duplicate-candidate results. Compare on common overall Phase 8 quality, excluding pair affinity from deck improvement. Full decks take precedence over smaller shells. Preserve builder constraints and real copy counts. Basic lands remain generated separately; no splashes are invented.

```
D = 0                                 if candidate does not improve maindeck membership
D = clamp(0.65 + 0.25 * qualityGain + 0.10 * structuralGain, 0, 1)
                                      for a projected nonland maindeck improvement
qualityGain = clamp(CommonDeckQualityDelta / 0.01, 0, 1)
structuralGain = max(bounded creature/early/top-end/mana-shortfall improvements)
```

Structure uses the existing minimum/preferred creatures, early-play floor and high-cost cap. Taking the strongest structure improvement avoids stacking correlated bonuses. Fixing receives a contribution only when selected and reducing a quantified demand/source shortfall; unnecessary fixing is restrained. Missing projections use Phase 9A's color fit as an explicitly limited fallback, not an invented deck. Early D weight is zero.

```
weighted = wQ*Q + wL*L + wA*A + wD*D
ContextualValue = clamp(0.5 + 1.4 * (weighted - 0.5), 0, 1)
Score0To50 = round(50 * ContextualValue, midpoint away from zero)
```

The centered gain 1.4 calibrates a neutral early card to 25 and an early Q=1 bomb to about 50 despite neutral contextual components. Every numeric contribution is derived from this exact model, with clamping/rounding reported separately. Rank is assigned separately using unrounded contextual value and deterministic tie breaks.

Missing card statistics may use Q=0.5 only with a genuine environment baseline, marked EstimatedMissingStatistics. Without that baseline the user-facing score is unavailable. Raw GIH remains missing. Model version: `contextual-pick-score-v1`. All constants belong to RecommendationEngine configuration, with UI colors confined to App.

Score appearance bands: 0–9 graphite; 10–19 steel; 20–29 silver; 30–39 gold; 40–44 richer gold; 45–50 premium bronze/amber. Missing scores get a neutral treatment. Printed rarity never contributes to score or animation tier.

## Implemented model and interpretation

Implemented on 2026-10-07. The constants above were reported before code changes. They are centralized in `ContextualPickScoreConfiguration`; they are heuristic calibration parameters, not fitted win probabilities. The score is absolute relative to the current environment and draft, not a percentile or a transformation of pack rank. No distribution is forced: a mediocre pack can have no score above 30 and a strong pack can contain multiple premium scores.

Approximate interpretation: 45–50 exceptional; 40–44 excellent; 35–39 strong; 30–34 desirable; 25–29 playable; 20–24 marginal; 15–19 weak in context; 10–14 unlikely to make the deck; 0–9 irrelevant to the current build. V1 is an explainable first calibration and needs wider completed-draft evidence before these labels can be treated as universal empirical boundaries.

Draft progress is `clamp(max(knownPoolCount, (pack−1)*14 + pick−1)/42, 0, 1)`. This preserves a late stage for partially recovered history without reconstructing unseen cards. Between anchors, `t` is the fraction of completed picks within that interval, clamped to [0,1], then smoothstep is applied to every weight. Weights always sum to one; D is exactly zero through P1P5. The production coordinator passes the authoritative pool completeness/unresolved metadata from snapshot adaptation. Direct callers without it receive an explicitly inferred Complete/Partial status.

Q uses the existing Phase 8 output and its existing 500-game shrinkage prior. A raw 65% with 40 games stays near neutral; 65% with 8,000 games can be premium. There is no second shrinkage or reliability multiplier. L divides the existing Phase 9B candidate adjustment by its human maximum (default 0.020); the existing Quick Draft maximum 0.010 therefore yields half the deviation from neutral. Multicolor conservatism, recency, ALSA lateness and observed-pack confidence remain Phase 9B's calculations. ATA is retained as data and not newly scored. A uses Phase 9C's normalized relative lift and active-pair confidence once; exact-format/set/pair guards and unsettled neutrality remain intact.

Phase 9A commitment informs color fit and projection fallback. With a projection, a late off-color card normally receives D=0 because it cannot enter the viable two-color maindeck. There is no separate additive color bonus stacked on pair confidence. Raw GIH, lane, archetype and trophy values/ranks remain available separately in diagnostics.

## Production 17Lands metrics audit

The transport DTO in `SeventeenLandsCardRatingsClient.cs` models these exact `/api/card_data` JSON names:

| JSON field | Current use |
|---|---|
| `name` | Exact source-name attachment to an already resolved printing |
| `ever_drawn_win_rate`, `ever_drawn_game_count` | GIH and its sample count; the sole primary Q signal through Phase 8 |
| `avg_seen` | ALSA display and existing Phase 9B lane evidence |
| `avg_pick` | ATA retained in the boundary/domain statistics; no new score adjustment |
| `play_rate` | Retained in neutral statistics, not newly scored |
| `opening_hand_win_rate` | Retained; no new score adjustment |
| `drawn_win_rate` | Retained; no new score adjustment |
| `drawn_improvement_win_rate` | Retained; no new score adjustment |
| `game_count`, `win_rate` | Overall provider measures retained/validated in DTO and cache; never substituted for GIH |
| `opening_hand_game_count`, `drawn_game_count` | Validated transport/cache counts; not exposed by the neutral rating model |

The envelope also retains `copyright` and `notes`. The current code has no dedicated GP/GNS neutral metric or sample fields. This audit does not reinterpret generic provider `win_rate` as a new GP/GNS feature. Adding all available rates together would double-count correlated evidence, so V1 intentionally makes no secondary quality adjustment. Exact QuickDraft, PremierDraft and TradDraft selection remains unchanged; an absent exact source is not replaced by Premier data.

## Immutable marginal deck projection

`CandidateDeckImpactEvaluator` builds a before projection and an independent immutable pool-plus-one-copy projection for every distinct candidate identifier. It returns an impact for every pack occurrence and shares duplicate impacts. It never removes, reserves or changes real pool copies. The existing Phase 10 optimizer still handles creature and early-play floors, the high-cost cap, deterministic ties, neutral priors and generated basics.

At each projection, examine all ten existing color pairs. Prefer a full 23-spell shell whenever any pair supports one. If none does, use the largest available eligible spell count, proportionally scale minimum/preferred creatures and early plays downward and the high-cost allowance upward by rounding, and label the result an incomplete shell. Its 17 generated lands support the existing builder mechanism; the result is not advertised as a finished 40. The completed-deck builder's defaults and production behavior have not changed.

Choose the strongest viable projection using common overall Phase 8 average spell quality, then existing pair-pool fit, fewer constraint relaxations, combined color evidence and canonical pair order. Pair-specific GIH is excluded from this common-quality comparison. The existing Phase 10C comparison helper is reused; only its visibility changed from private to internal. This is a counterfactual projected-best selection, while Phase 10C's recommended completed-deck baseline behavior remains intact.

The impact records inclusion, a replaced identifier, quality delta, creature/early/high-cost count deltas, known mana-source shortfall delta, before/after pair, constraint changes, incomplete-shell state, structural gain and reasons. A membership improvement means the new projected deck contains more copies of that identifier than the before deck; it does not impose a Constructed four-copy limit. A projected sidegrade may make the deck without improving average GIH, in which case it receives the membership term but no quality gain.

Creature gain is 1 when a positive count delta helps a before deck below minimum 14, 0.5 below preferred 16, and otherwise zero. Early-play gain is 1 when a positive delta helps a before deck below four early spells. High-cost gain is 1 when a negative delta reduces a before deck above six MV5+ spells. These are deficit reductions, not bonuses for every creature or cheap spell. The structure term takes the maximum, capped at 1, rather than adding correlated improvements. Quality gain is an average-deck GIH increase divided by 0.01 (one percentage point), clamped to [0,1]. A weak card filling a need receives a bounded benefit; Q still penalizes its weak intrinsic performance.

For selected lands: `D=0.50 + 0.40*clamp(manaShortfallReduction,0,1)` when a known source shortfall improves; otherwise `D=0.10`. Unselected lands get zero. Shortfall compares the ceiling of each color's 17-land proportional pip demand with the builder's known colored sources. This is a conservative source diagnostic, not a probability of casting on curve. V1 supports no speculative splash, indirect fixing, alternate-face mana credit or abstract fixing bonus. The builder still allocates basics without nonbasic source credit; that limits how many fixing candidates demonstrate an improvement.

For unavailable candidate metadata or no eligible shell, D uses `clamp(0.5 + 0.5*Phase9AColorFit,0,1)` with an explicit fallback reason. No optimizer work is done at zero early D weight or without a genuine environment baseline.

## Roles and successful-deck evidence

**Available now:** structured Creature, Land, Early Play (known nonland MV≤2), primary-face pip demand, and direct known mana production where there are no ambiguous faces. Projection structure reuses the existing deck composition analysis and constraints. `LimitedCardRoleProfile` keeps reliable roles separate from curated roles; `ILimitedCardRoleProfiles` permits exact set/card profiles. No Oracle substring classifier or large manual database was added.

**Foundation only:** hard/conditional removal, combat trick, card advantage, ramp, synergy enabler/payoff, finisher, defensive and aggressive roles can be represented by curated profiles. V1 does not score semantic role quotas or claim that arbitrary spell keywords identify removal. Curated role support is an explanation/profile extension point, not an unvalidated removal bonus.

**Future dataset work:** successful 6+ win/trophy structural medians and distributions are not available reliably from the current guarded provider/local cache. The Phase 9D corpus/provenance/denominator infrastructure remains, but trophy frequency contributes zero to Pick Score and no successful-deck targets were learned here. Existing conservative 14/16 creature, four early-play and six MV5+ defaults are used. A future validated exact-format corpus can inform deck-configuration targets and curated profiles after metadata coverage and selection bias are established. No universal removal quota or trophy-frequency bonus is introduced.

## Explanations, rank and availability

The immutable engine result retains Q/L/A/D, weights, stage, color fit, Phase 8 data weight, deck impact, reliable/curated roles, availability, model version, reasons and actual contributions. Numeric contributions are `50*1.4*weight*(component−0.5)` around neutral 25; adding them gives the real unrounded value before final clamp and rounding. They are not fabricated integer bonuses. Operational optimizer counts/timing are kept outside the score, so wall-clock time never influences determinism.

The new score rank orders unrounded contextual value, Q, Phase 8 data weight and original pack index. It stays separate from the displayed integer and from legacy Phase 8/9 ordinal ranks. There is no top-pick claim when fewer than two candidates can be scored. A metadata/statistics-only candidate with a genuine baseline can have an explicit neutral-quality estimate; the status panel and per-occurrence explanation identify the estimate and raw GIH remains unavailable. Without a valid environment baseline, the badge shows a dash. Loading shows an ellipsis. Identity mismatch fails the entire occurrence presentation closed.

Understandable reasons include strong intrinsic performance, open color evidence, poor active-pair performance, missing early play, creature-floor help, replacing a card, substantial top-end, outside established colors and partial inventory. All candidates are inspectable in the existing rail's recommendation diagnostics. The status summary names the current score winner and shows its score, pack ordinal, version and brief reasons. Previous stage details, including disabled trophy reasons, remain in expanded diagnostics.

## Badge integration and exact tier mapping

The existing 86×42-DIP box, border, placement and name/ellipsis line remain. Its contents are now three compact lines: dominant 15-point Pick Score, 10-point `GIH ...`, and 9-point `ALSA ...`, with explicit 16/11/10 line heights. Rank is retained in diagnostics. The existing top-pick border uses the score winner, while animation reads only `Presentation.PickScore.Score0To50`. No animation styling code reads CardRarity.

Animation color is based on DraftTG card-quality tier, not Magic printed rarity. UI colors stay in App, downstream of recommendation logic.

| Score / UI tier | Primary accent | Secondary accent | Glow | Sparkle | Highlight / silver opacity | Sparkle multiplier |
|---|---|---|---|---|---|---|
| 0–9 Graphite | `#555D69` | `#727B88` | `#8792A3` | `#A3AFBF` | .11 / .14 | .65 |
| 10–19 Steel | `#6D798B` | `#8797A9` | `#A4B7CE` | `#CAD8EA` | .14 / .18 | .78 |
| 20–29 Silver | `#A3B0C1` | `#8797A9` | `#B9C6D8` | `#D6E2F2` | .18 / .18 | .88 |
| 30–39 Gold | `#BFA56D` | `#A78B68` | `#C5A568` | `#E4D3AC` | .20 / .19 | .93 |
| 40–44 Rich gold | `#E2BE64` | `#B58A60` | `#D8A24F` | `#F3DCA2` | .24 / .20 | 1.00 |
| 45–50 Premium bronze/amber | `#E1AE70` | `#BD8656` | `#DC9D57` | `#FFE0AC` | .26 / .20 | 1.00 |
| Unavailable | `#6D798B` | `#8797A9` | `#B9C6D8` | `#D6E2F2` | .14 / .18 | .88 |

Every tier shares base `#E6171B22` and neutral silver `#B9C6D8`. Cached smoke/glow, five sparse sparkles, the fixed text veil, rounded clipping, unchanged motion cycles and the shared 30 Hz clock are preserved. Static mode still freezes all backgrounds and stops subscriptions. The cache is bounded to seven treatments, approximately 5.68 MB of uncompressed smoke/sparkle pixels before renderer copies, allocated once as treatments are used. No timer or blur filter is allocated per badge/frame.

## Saved-state replay and calibration

The offline tool reads the actual local v3 FRA Premier `/api/card_data` cache (290 provider rows, fetched 2026-10-05 19:44:35 UTC) and cached Scryfall metadata, with matching fallback printings for environment source names. 269 provider names resolve in that local catalog; the Phase 8 weighted baseline is 56.4675%. Unresolved source names remain absent, following the production mapper. No external comparison scores, network requests or new provider downloads were used.

WOE saved artwork packs retain their real P1P6/P1P7 coordinates but lack earlier pool chronology and an exact WOE QuickDraft ratings cache. Scores/ranks therefore remain unavailable. FRA's real completed 42-card pool has 33 distinct printings and no per-pick pack chronology. The six FRA examples below evaluate hypothetical additional copies in a 14-candidate comparison pack against that saved inventory, using late-stage weights. They are **counterfactuals on a real saved pool, not claimed historical pick recommendations**. Its only viable full pair is RG; its current known-set empirical A remains neutral because FRA has no shipped archetype profile/pair dataset. Hypothetical current-pack observations can still supply ordinary Phase 9B lane evidence.

| Saved state / coordinate | Card | Old Phase 9B rank | New Pick Score / rank | Contextual explanation |
|---|---|---|---|---|
| WOE actual P1P6, prior pool absent | Scarecrow Guide | unavailable | unavailable | No exact-format environment baseline |
| WOE actual P1P6, prior pool absent | Return from the Wilds | unavailable | unavailable | No exact-format environment baseline |
| WOE actual P1P7, prior pool absent | Freeze in Place | unavailable | unavailable | No exact-format environment baseline |
| WOE actual P1P7, prior pool absent | Voracious Vermin | unavailable | unavailable | No exact-format environment baseline |
| FRA completed pool, late what-if | Ajani Unrelenting | #1 | 46 / #1 | Q=1.000, D=.857: replaces a card and improves common quality in RG |
| FRA completed pool, late what-if | Chandra, Torch of Defiance | #2 | 37 / #2 | Q=.726, D=.732: strong on-color maindeck upgrade |
| FRA completed pool, late what-if | Konstrari Charm | #5 | 30 / #4 | Q=.495, D=.692: playable projected upgrade despite weaker lane support |
| FRA completed pool, late what-if | Way of the Warlord | #7 | 28 / #7 | Q=.367, D=.670: below-baseline card that still replaces a weaker maindeck slot |
| FRA completed pool, late what-if | Overwrite the Multiverse | #3 | 21 / #11 | Q=.984, D=0: excellent intrinsic card outside RG; not selected |
| FRA completed pool, late what-if | Rank Rat | #14 | 10 / #14 | Q=.438, D=0: no projected maindeck improvement outside established colors |

These results show a useful late-context reversal: Overwrite's old #3 becomes score 21/rank #11 while weaker on-color upgrades remain playable. The model was not fitted to this draft. Synthetic calibration/scenario fixtures separately cover early bombs, confidence, needed 2-drops, excess top-end, creature deficits, positive/negative pair lift, human/bot lanes, fixing and pair changes.

Reproduce offline evidence, using the existing local caches:

```powershell
dotnet run --project tools/DraftTG.PickScoreAudit -- . artifacts/pick-score-v1/audit.json
```

The tool's HTTP handler rejects any network attempt and records zero requests. A frozen cache-age clock permits decoding the historical cache as a recorded snapshot; it does not change live cache expiry behavior. Outputs include full component/contribution/reason details and benchmark summaries. A missing required local FRA cache is an explicit tool failure; the tool never fetches replacement data.

## Performance

Measured on this Windows machine, .NET 10.0.12, 24 logical processors, three warmups and 20 samples per workload. Timings include complete Phase 8/9 recommendation composition, current-pack presentations and score calculation. Cached file loading is excluded. Simulation uses one before projection, candidate-local immutable pools, duplicate memoization and eligible-pool projection reuse. It is bounded by ten pairs times (one before plus distinct candidates), with fewer calls when eligibility is unchanged or equivalent. Equivalent pair reuse requires no drafted lands and identical eligible spells with all colored demands supported; a regression compares cached and uncached impact outputs.

| Full 14-card workload | Median | p95 | Maximum | Deck simulation median / p95 | Optimizer calls |
|---|---|---|---|---|---|
| Real saved FRA pool, 42 cards | 72.53 ms | 101.28 ms | 101.70 ms | 72.20 / 100.98 ms | 12 |
| Stress fixture: 42 varied colorless spells, all ten pairs viable | 205.82 ms | 227.37 ms | 237.22 ms | 205.62 / 227.17 ms | 15 |

Before equivalent-pair reuse, that stress workload took median 1,932 ms and 150 optimizer calls. Reuse removes redundant evaluation while preserving impacts. Scoring stays in the existing background worker outside the coordinator lock; initial pair detection skips the expensive score pass, and existing generation/revision/snapshot fences reject stale publication. No per-candidate network calls are added. Larger pools up to the existing builder limit, especially diverse constraint states, may cost more than these 42-card draft benchmarks; this is not a universal latency guarantee.

## Preview, tests and validation

The offline preview keeps 15 gameplay-size badges with all six score bands, long-name cases, one score winner and one unavailable score. These values are explicitly preview fixtures, not calculated statistics. Launch it with:

```powershell
dotnet run --project src/DraftTG.App -- --badge-preview
# Live clock, pixel-motion and static/animated performance evidence:
src/DraftTG.App/bin/Debug/net10.0-windows10.0.19041.0/DraftTG.App.exe --badge-preview --measure-output artifacts/pick-score-v1/preview/performance.json
```

The live preview was rendered and visually inspected: score dominates, GIH/ALSA remain readable, long names ellipsize above the badge, stationary text/borders stay crisp, and every effect is clipped. Captures at 0/1.5/3 seconds show distinct live pixels in both sampled treatments with 15 active shared-clock subscribers. Existing reduced/static behavior and pause/hidden subscription checks are retained.

Thirty focused tests were added: 26 engine scenarios/invariants, three Application occurrence/availability/recovered-pool tests, and one six-tier boundary test. Existing animation tests now assert score-driven palettes and rarity independence; existing phase/UI tests retain phase expectations while inspecting the new winner/display and expanded legacy diagnostics. Coverage includes all requested A–O scenarios, stable ties/duplicates, exact Phase 8 reuse, no baseline, explicit neutral estimates, no mutation, cancellation/identity rejection, finite range, monotonic Q, derived contribution sums, small early changes, supported fixing, changed best pair and cached/uncached equivalence.

Final validation:

- `dotnet build DraftTG.sln`: **0 warnings, 0 errors**.
- `dotnet test DraftTG.sln --no-build`: **1,057 passed**, 0 failed, 0 skipped (Domain 56; Data 160; ArenaIntegration 127; RecommendationEngine 189; Application 249; App 276).
- `dotnet run --project tools/DraftTG.PickScoreAudit ...`: completed successfully; **zero network requests**, ten saved-state examples, unchanged six numeric FRA outcomes and both 20-sample benchmark workloads.
- Final 15-badge probe: animated CPU **7.55% of one logical core / 0.314% of this 24-thread machine**, versus static **0.39% / 0.016%**; managed allocation **0.980 MB/s** animated versus **1.7 KB/s** static. These whole-process figures include the developer HUD and are not GPU utilization or a universal responsiveness guarantee. The preceding two-treatment visibility pass measured 0.379% machine CPU and 0.965 MB/s; this short run shows similar animation cost, with a larger bounded one-time texture cache.
- In the 12.01-second animated interval: 289 shared ticks, 15 subscribers, **4,335 updates and 4,335 renders**; the static interval had zero ticks/subscribers/updates/renders. Both sampled paused interiors remained pixel-identical after 1.5 seconds. Pause and hidden-window clock shutdown passed. Dispatch cadence is about 24 Hz on this machine, not measured GPU presentation FPS.
- `git diff --check`: clean. No live Arena gameplay, GPU profiling or long-duration memory test is claimed.

Local generated evidence: [audit JSON](artifacts/pick-score-v1/audit.json), [15-badge preview](artifacts/pick-score-v1/preview/badge-preview-3.0.png), [motion/static/performance JSON](artifacts/pick-score-v1/preview/performance.json). Those artifacts remain under the existing ignored output directory.

## Files and scope

- Engine: new `ContextualPickScoreModels.cs`, `ContextualPickScoreEngine.cs`, `CandidateDeckImpactEvaluator.cs`, `LimitedCardRoles.cs`; `SuggestedDeckBuilder.cs` exposes its existing comparison helper internally.
- Application: new `PickScorePresentation.cs`; updates to `CurrentPackCardPresentation.cs`, `LimitedStatisticsUpdate.cs`, `LimitedStatisticsService.cs`, `LimitedStatisticsCoordinator.cs` and `LimitedStatisticsMapper.cs` compose the model, attach by occurrence, preserve recovered inventory and publish safely.
- App: `MainWindowViewModel.cs`, `OverlayViewModel.cs`, `CardOverlayWindow.axaml`, `MysticRatingBackground.cs`, `MysticBadgePreviewWindow.cs` integrate the number, reasons and score palettes. `App.axaml.cs` contains the existing offline preview route from the preceding badge work.
- Tests: new `ContextualPickScoreTests.cs` and `ContextualPickScorePresentationTests.cs`; updated `MysticRatingBackgroundTests.cs`, `LiveBadgeBindingTests.cs`, `ContextualRecommendationPresentationTests.cs`, `ArchetypePresentationTests.cs` and `TrophyPresentationTests.cs` reflect primary-score/expanded-rank presentation while preserving phase checks.
- Offline tool: `tools/DraftTG.PickScoreAudit/Program.cs` and its project file. Generated audit/preview artifacts stay under the existing ignored `artifacts/` directory.
- Documentation: this report, `ARCHITECTURE.md`, `ROADMAP.md`, and `ANIMATED_RATING_BADGE_REPORT.md`.

No Domain UI colors, production statistics formula changes, localization/capture/Player.log changes, deterministic placement promotion or deck-builder redesign were introduced. Stop at Contextual Pick Score V1; Phase 9E.2B remains unstarted. Future work is broader real chronology replay/calibration, trustworthy semantic roles/structural datasets, supported splashes and mana optimization, and cross-pick score-delta presentation. None is required to use the shipped V1.

## Later stabilization (current model `contextual-pick-score-v1.6`, frozen V1; tiers `pick-score-tiers-v2`)

This report documents V1 as delivered. Six follow-up phases changed scoring behaviour, and one changed only the badge tiers. The frozen reference is [CONTEXTUAL_PICK_SCORE_V1_SPEC.md](CONTEXTUAL_PICK_SCORE_V1_SPEC.md).

- **v1.1** ([phase 1](CONTEXTUAL_PICK_SCORE_PHASE1_REPORT.md)): graded deck fit replaces the binary membership step.
- **v1.2** ([phase 2](CONTEXTUAL_PICK_SCORE_PHASE2_REPORT.md)): soft limits replace the Q and final-value clamps, so rankings no longer fall through to sample size.
- **v1.3** ([phase 3](CONTEXTUAL_PICK_SCORE_PHASE3_REPORT.md)): multiface cards receive their 17Lands rows, and estimated scores are visibly marked.
  - **Root cause:** 17Lands names the drafted card by its front face ("Hallway Heckler"), while the catalog's canonical name is "Hallway Heckler // Vicious Verse".
  - **Resolver:** a separate statistical identity tries the exact name first, then a structural front-face alias (Adventure, Transform, modal DFC and Prepare layouts only). It fails closed on any ambiguity and never uses back faces or fuzzy matching.
  - **FRA impact:**
    - attached rows go from 269/290 to 290/290;
    - estimated scores go from 41 to 20 (the remaining 20 have no GIH from 17Lands);
    - the baseline moves from 56.4675% to 56.3652% because the 21 rows now enter the environment.
  - **Badge:** the score now shows a small neutral `EST` marker whenever card-level quality uses the neutral prior. EST does not change the quality tier.

- **v1.4** ([phase 4](CONTEXTUAL_PICK_SCORE_PHASE4_REPORT.md)): deck need D is rebuilt from orthogonal terms.
  - **Graded maindeck value F:** membership plus upgrade over the replaced card, with a 0.10 sideboard floor; neutral is about .55 at the cut line.
  - **Structure S:** creature, early-play and removal need minus top-end redundancy, shrunk by projected-shell confidence and realised only as far as the card is likely to be played.
  - **Removed:** the old average-quality gain, the unscaled floors and the nonland mana artifact.
  - **Roles:**
    - high-confidence Oracle removal (FRA: 18 hard, 12 conditional);
    - structured land fixing (16);
    - ramp deferred;
    - add/remove overrides supported.
  - **Successful-deck structure data:** unavailable (use-restricted source, no local corpus), so generic builder targets are used.
  - **Scores:** early unchanged; late scores shift upward (on-colour +5/+6, off-colour +2/+3).

- **Phase 5** ([report](CONTEXTUAL_PICK_SCORE_PHASE5_REPORT.md)): calibration and tier validation, with no score change.
  - **Calibration kept:** a 9 × 1,000-pack replay and semantic anchors confirm the v1.4 calibration.
  - **New badge tiers** (`pick-score-tiers-v2`): 0–14 graphite, 15–24 steel, 25–34 silver, 35–39 gold, 40–44 rich gold, 45–50 premium. They replace the V1 bands listed in this report.
  - **Central constants:** calibration and tier thresholds now live in `ContextualPickScoreCalibration` and `PickScoreTierThresholds`.
  - **Known gaps:** a late off-colour bomb scores about 24, and late "strong" scores come mostly from deck-need saturation.

- **v1.5** ([phase 6](CONTEXTUAL_PICK_SCORE_PHASE6_REPORT.md)): playability-gated quality and an expected-future cut line.
  - **Quality relevance:** intrinsic Q is kept, but its upside is scaled by R, which reflects whether any realistic projected build (including a close alternate pair or a fixed splash) can play the card. R ramps smoothly from 10 to 35 picks.
  - **Deck fit:** open slots and weak maindeck cards are measured against the expected marginal card of the final deck (an order statistic over contested slots and later supply) instead of a near-free open-slot value.
  - **Results:**
    - a late off-colour bomb goes 24 → 16;
    - open-slot mediocre Strong cards at P3P1 go 196 → 27;
    - P1P1 and P1P5 are unchanged;
    - calibration and tiers are unchanged.

- **v1.6, frozen V1** ([phase 7](CONTEXTUAL_PICK_SCORE_PHASE7_REPORT.md)): validation and freeze.
  - **Defect fixed:** a near-equal alternative pair switched on and off at a one-card count threshold (swings of up to 27 points). Near-viable pairs now enter with a taper, and each build uses its own virtual cut line.
  - **Supply constants:** now an explicit profile (FRA calibrated; other sets use a labelled provisional default).
  - **Golden fixtures:** twelve freeze the model.

Statements above about "Estimated" scores, the 269 resolved provider names and the 56.4675% FRA baseline describe V1 before these corrections.
