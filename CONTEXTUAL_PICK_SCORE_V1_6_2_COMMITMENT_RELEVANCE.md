# Contextual Pick Score v1.6.2 — commitment-aware quality relevance

## Audit and frozen inputs (before production changes)

The exact real FRA QuickDraft P1P8 pack/pool/history is preserved in `tests/Fixtures/phase9e2b-p1p8-rg-failure.json`. The separate v1.6.1 comparison in `tests/Fixtures/phase9f2-live-pack-v1.6.1.json` includes the sparse prior and exact per-card score breakdowns. These files and the v1.6.1 prior constants/policy remain frozen. All investigation/replay in this task is offline; no live Arena collection or localization changes are needed.

The seven drafted cards are No Admittance; Woodwork Prodigy // Soul Tether; Konstrari Charm; Greenhouse Propagator; Keeper of the Quiet Hour; Pia, Determined Rebuilder; and Aerid Konstrari. Existing Phase 9A fractional colored evidence is R=3.5, G=2.5, W/U/B=0; one card is colorless. The existing projection has a single viable RG shell with seven spells, five creatures, two early plays and 1.5 weighted removal. The offered pack has seven cards, not fourteen.

The pre-change [v1.6.1 freeze](docs/evidence/stat-provenance/v1.6.2-p1p8-before.json) records the exact UUIDs, selected-pick chronology, original Phase 9A profile (commitment strength 0.2875), projected pairs/membership, Q/Q_eff, playability, contributions, weights and exact scores before production changes. The separate [versioned v1.6.2 artifact](tests/Fixtures/phase9e2b-p1p8-v1.6.2.json) records both models, source-fixture hashes, all seven offered cards, Uldaros non-regression, controlled scenarios and continuity results. Original fixture JSON was not rewritten.

Current v1.6.1 relevance: let picks=42*progress; t=clamp((picks−10)/(35−10),0,1); ramp=t²(3−2t). `R = 1−ramp*(1−playability)*0.8`. Effective quality is `Q_eff = 0.5 + R*(Q−0.5)` above neutral, while Q<=0.5 remains unchanged. At seven completed picks, ramp=0, so **R=1 for every card**, including Unsummon with playability zero. Stage weights are Q=0.6835131284347649, L=0.2, A=0.10412171789130878, D=0.01236515367392632. Unsummon Q=0.5786468825336437 gives exact 28.70495083517041, compared with Arcane Amphisbaena Q=0.5057055995069826 and exact 25.501120969070218. The off-plan D floor cannot offset the unreduced Q upside at this stage.

Reuse audit: `ColorCommitmentProfile` already supplies fractional color evidence and known pick count. `CandidateDeckImpactEvaluator` already projects viable/near-viable pairs, assigns tapered quality-based playability weights, and computes candidate eligibility including drafted fixing, one-pip splash credit, and candidate-created alternate builds. Its D/deck-fit weights use a different temperature and must not be mistaken for playability confidence. The new score consumes pre-candidate projection confidence and existing pool evidence, leaving pair selection, playability, D, lane, archetype and sparse-quality calculations unchanged. A fixed-input simulation grid and transition audit justify the new relevance parameters; they are product calibration, not a learned draft-win model.

## Commitment definition

`ProjectedColorCommitment` is a quality-relevance view of existing signals; it does not replace Phase 9A or change the color/archetype/deck-plan systems. Before any offered card is added, reuse the existing optimizer's pool projections. Their ordering is unchanged: fully viable pairs first, then near-viable pairs, with quality and existing pool evidence tie-breaks. Reuse the **existing playability weights**, including 1-percentage-point quality softmax and near-viability taper. No second probability model or additional optimizer pass is created.

For the leading pre-candidate projection:

```text
support = sum of existing Phase 9A colored evidence in its two colors
maturity_anchor = Phase9A.FullCommitmentAfterPicks / 2 = 7
maturity = S(clamp(support / maturity_anchor, 0, 1))
projection_confidence = clamp(anchor_playability_weight − largest_other_weight, 0, 1)
known = max(0, known_pool_count − unresolved_Phase9A_identifiers)
expected = max(coordinate_completed_picks, known_pool_count + unresolved_inventory_count)
inventory_coverage = expected > 0 ? clamp(known / expected, 0, 1) : 0
C = maturity × projection_confidence × inventory_coverage
S(t) = t²(3−2t), with t clamped to [0,1]
```

No projections or unavailable anchor quality produces C=0. Close alternative projections reduce confidence; an alternate with more plausibility than the anchor also yields zero separation. The source weights are heuristic projection plausibility, not calibrated win/pair-choice probabilities. Recovered partial inventories reduce commitment through explicit coverage. Colorless picks alone do not establish a color lock. Pick number supplies coverage/stage context, not commitment by itself.

The maturity anchor is half the existing Phase 9A full-evidence anchor. This permits concentrated investment to matter within pack one; competing viable pairs still prevent locking merely because a few picks have elapsed. At the real P1P8, support=6, maturity=S(6/7)=**0.9446064139941691**, sole RG projection weights 1/0, coverage=1, hence **C=0.9446064139941691**. Original Phase 9A strength remains **0.2875**; its stronger stage shrinkage is unchanged for the other recommendation stages. The new C is explicitly projection-aware and should not be confused with that original field.

## Playability reuse and early projections

P is the existing candidate's tapered, plausibility-weighted eligibility across its **after-pick** viable projected pairs. Candidate-created viable alternatives therefore still count. This is color-plan/splash eligibility, not current maindeck membership: a weak colorless card may have P=1 yet D at its floor. The leading plan used for C is measured **before** the candidate, so a card cannot manufacture the commitment used to score itself.

Existing rules remain intact: an eligible/colorless spell earns full eligibility in that pair; an off-pair card with exactly one outside color and at most one pip earns up to 0.6 splash eligibility from drafted fixing, reaching that credit at three sources. Double/triple outside pips receive no splash credit in this existing model. Lands retain P=1, with fixing value handled separately by unchanged D. Missing candidate metadata retains the existing unavailable/playability fallback, avoiding invented impossibility.

v1.6.2 also runs the existing evaluator for a nonempty early pool when D has zero weight. Without this, the new reduction would appear abruptly at the first positive D weight. These early projections are used for playability/commitment; the old zero-weight D fallback value is retained, and no D coefficients or optimizer choices are altered. P1P1's empty pool still skips optimization. Explicit v1.6/v1.6.1 keep their old early optimization behavior.

## New relevance formula

The old stage relevance remains reproducible:

```text
stage_ramp = S((completed_picks − 10) / 25)
R_base = 1 − stage_ramp × (1−P) × 0.8
```

v1.6.2 adds:

```text
tail = S((Q − 0.65) / 0.30)
floor(Q) = 0.05 + (0.20−0.05) × tail
R_commitment = 1 − C × (1−P) × (1−floor(Q))
R_context = min(R_base, R_commitment)
Q_eff = Q,                              when Q <= 0.5
Q_eff = 0.5 + R_context × (Q−0.5),       when Q > 0.5
```

Q, its shrinkage/normalization and its statistical/estimated provenance are unchanged. The existing weights and final soft-limit calibration consume Q_eff. C=0 preserves the existing stage relevance; P=1 preserves full relevance. Intermediate P attenuates continuously. Fully committed zero-playability ordinary quality retains 5% of upside, while Q>=0.95 retains the existing 20% premium tail. Strong raw quality remains capable of beating modest playable alternatives; no rarity or card-name input exists. Poor quality is never lifted toward neutral. The minimum retains the original late-stage relevance bound.

## Parameter justification and grid

The [calibration grid](docs/evidence/stat-provenance/v1.6.2-grid-calibration.json) covers commitment 0/0.25/0.5/0.75/1, playability 0/0.25/0.5/0.75/1, and poor/average/good/very good/premium quality **0.30/0.50/0.60/0.80/0.95**, for **125 combinations**. It evaluates 30 candidate floor pairs: ordinary 0.025/0.05/0.075/0.10/0.15/0.20 and premium 0.10/0.15/0.20/0.30/0.40.

General behavioral criterion, independent of the real card names: with C=0.95/P=0, ordinary Q=0.60 should lose to playable Q=0.51 when other components are equal. At C=1/P=0, an extreme Q=0.95 should retain enough upside to beat playable Q=0.55. Retain the frozen 0.20 late relevance floor as the premium endpoint, then choose the **largest ordinary floor** satisfying the first criterion. This selects **0.05**, giving ordinary Q_eff=0.50975 and premium Q_eff=0.59. 0.075 and higher fail the ordinary criterion; 0.025 is unnecessarily harsher. The 0.65 start is the existing strong-quality explanation boundary; 0.95 is the near-extreme endpoint. Smoothstep interpolates between them without a threshold jump.

These parameters are transparent product calibration supported by the grid, real regression, controlled pool transitions and the existing deterministic draft sampler. They are not learned outcome-optimal parameters, confidence intervals, or evidence of increased draft win rate. No counterfactual trophy corpus labels are available here that would establish an optimal policy. The P1P8 outcome was checked after these general criteria, without a fixed score/margin target.

## All offered cards: quality audit

The pre-candidate plan is RG for every occurrence; original R_base=1 and old Q_eff=Q for all seven. C=0.9446064139941691 for all seven. Weights are unchanged: **Q 0.6835131284347649 / L 0.2 / A 0.10412171789130878 / D 0.01236515367392632**. A=0.5 throughout.

| Card | Color relationship | P | Intrinsic Q (both) | New R | New Q_eff | L | D |
|---|---|---:|---:|---:|---:|---:|---:|
| Blessed Ghoul | WB, outside RG | 0 | 0.474950164682197 | 0.10262390670553934 | 0.474950164682197 | 0.48931065487401465 | 0.1 |
| Cryotheory Adept | U, outside RG | 0 | 0.4425567387042253 | 0.10262390670553934 | 0.4425567387042253 | 0.5205887691061061 | 0.1 |
| Arcane Amphisbaena | G, playable in RG | 1 | 0.5057055995069826 | 1 | 0.5057055995069826 | 0.5011925995257194 | 0.7442750808127223 |
| Hallway Heckler // Vicious Verse | R, playable in RG | 1 | 0.33553167342092627 | 1 | 0.33553167342092627 | 0.4924666066138767 | 0.5094272204765126 |
| Unsummon | U, outside RG | 0 | 0.5786468825336437 | 0.10262390670553934 | 0.5080710503358141 | 0.5205887691061061 | 0.1 |
| Way of the Necromancer | B, outside RG | 0 | 0.04254748381377049 | 0.10262390670553934 | 0.04254748381377049 | 0.49644136988028315 | 0.1 |
| Dedicated Commons | Colorless; eligible, not a quality upgrade | 1 | 0.2987386185071129 | 1 | 0.2987386185071129 | 0.5 | 0.1 |

Way's Q is the frozen v1.6.1 sparse prior, not the older v1.6 neutral estimate. Its quality is not changed again here. Reduced R on below-neutral cards has no effect on their Q_eff or score.

Contributions are points relative to neutral 25; A contributes zero for every card, and the final soft-limit contribution is zero for this pack. Unchanged L/D contributions make their interaction explicit:

| Card | Old quality points | New quality points | Lane points (both) | Deck points (both) |
|---|---:|---:|---:|---:|
| Blessed Ghoul | −1.1985323913393047 | same | −0.1496508317637949 | −0.346224302869937 |
| Cryotheory Adept | −2.748425626503943 | same | +0.28824276748548505 | −0.346224302869937 |
| Arcane Amphisbaena | +0.2729896518029457 | same | +0.016696393360071227 | +0.21143492390720572 |
| Hallway Heckler // Vicious Verse | −7.869138229994529 | same | −0.10546750740572619 | +0.008159832093704394 |
| Unsummon | +3.7629323705548594 | **+0.38616682053507395** | +0.28824276748548505 | −0.346224302869937 |
| Way of the Necromancer | −21.887236031416325 | same | −0.049820821676035965 | −0.346224302869937 |
| Dedicated Commons | −9.629535754811416 | same | 0 | −0.346224302869937 |

Previously Unsummon's +3.7629 quality points dominated its −0.3462 D contribution; zero playability could not change the quality because the old ramp was still zero. The new relevance reduces only that quality upside. Amphisbaena's exact Q_eff and all other components remain unchanged. Its small measured deck benefit is enough to win after relevance becomes appropriate; no D increase is needed.

## Real P1P8 ranking before/after

| Card | v1.6.1 exact / display / rank | v1.6.2 exact / display / rank |
|---|---|---|
| Arcane Amphisbaena | 25.501120969070218 / 26 / 2 | **25.501120969070218 / 26 / 1** |
| Unsummon | 28.70495083517041 / 29 / 1 | **25.328185285150624 / 25 / 2** |
| Blessed Ghoul | 23.305592474026962 / 23 / 3 | unchanged |
| Cryotheory Adept | 22.193592838111602 / 22 / 4 | unchanged |
| Hallway Heckler // Vicious Verse | 17.03355409469345 / 17 / 5 | unchanged |
| Dedicated Commons | 15.024239942318646 / 15 / 6 | unchanged |
| Way of the Necromancer | 2.716718844037702 / 3 EST / 7 | unchanged |

The acceptance criterion passes. The margin emerges from the general formula and existing contributions; no card-specific rule or target score was coded. The original v1.6 scores, including Way's old neutral estimate, are separately retained by explicit version selection.

## Early/open behavior with the same quality and offered pack

Controlled counterfactuals reuse all seven real offered identities, the same primary/alternate statistics and intrinsic Q, and prefixes of the exact selected pool. Their lane observation history is deliberately empty to isolate the state transition; these are not claims about earlier live offered packs. Unsummon remains Q=0.5786468825336437 throughout:

| Completed picks / hypothetical position | Exact Unsummon score | Rank | Relevance |
|---|---:|---:|---:|
| 0 / P1P1 | 28.853697244148545 | 1 | 1 |
| 1 / P1P2 | 28.853697244148545 | 1 | 1 |
| 2 / P1P3 | 28.826532208444856 | 1 | 0.9929509159690894 |
| 3 / P1P4 | 28.240964683930216 | 1 | see artifact |
| 4 / P1P5 | 27.283335680857935 | 1 | see artifact |
| 5 / P1P6 | 27.24113541648709 | 1 | see artifact |
| 6 / P1P7 | 26.566150102968134 | 1 | see artifact |
| 7 / P1P8 | 25.12552427768755 | 2 | 0.10262390670553934 |

The controlled P1P8 differs from the exact forensic score because it has no previous offered-pack lane evidence; the exact replay above retains that evidence. Largest adjacent drop in this prefix sequence is about **1.4406 points**, arising from a new selected card and changing projected playability, not a hard pick-number switch. The separate same-quality gold-pool sequence from 0–14 picks has maximum adjacent drop **1.8804 points**. The mixed five-color seven-card pool has C≈0.2628 and the ordinary blue card still ranks first. Open state and projection ambiguity continue to encourage speculation.

## Premium pivot behavior

Controlled synthetic inputs through the production engines: blue GIH 0.70 over 10000 games, baseline 0.56, yields Q=0.9940200448264412. Its opponent is a modest green card at GIH 0.561. No rarity difference is used.

| RG gold-pool picks | Commitment | Premium blue relevance | Display / rank |
|---:|---:|---:|---|
| 3 | 0.39358600583090375 | 0.7900874210601597 | 44 / 1 |
| 5 | 0.8017492711370262 | 0.358600583090379 | 34 / 1 |
| 7 | 1 | 0.2 | 29 / 1 |
| 14 | 1 | 0.2 | 26 / 2 |

A premium card can win even with full measured commitment and P=0 at pack-one stage. At higher stage/investment it can decline naturally because unchanged D/stage weights matter more. Its quality upside retains a nonzero tail; pivoting is not prohibited. This does not promise that any off-plan card must beat an already excellent on-plan card or a finished deck upgrade.

## Fixing, colorless and multicolor results

Same strong seven-gold-card RG pool, same primary quality, with 0/1/3 drafted blue/red fixing lands. These are controlled metadata-consistent fixtures, not live cards or live validations. All use the unchanged projection eligibility rules:

| Candidate | P with 0 / 1 / 3 fixing sources | R with 0 / 1 / 3 | Display with 0 / 1 / 3 |
|---|---|---|---|
| Single-pip blue bomb | 0 / 0.2 / 0.6 | 0.2 / 0.36 / 0.68 | 29 / 32 / 38 |
| Partly matching RU gold, one U pip | 0 / 0.2 / 0.6 | 0.2 / 0.36 / 0.68 | 29 / 32 / 38 |
| Double-pip blue | 0 / 0 / 0 | 0.2 / 0.2 / 0.2 | 29 / 29 / 28 |
| Triple-pip blue | 0 / 0 / 0 | 0.2 / 0.2 / 0.2 | 29 / 29 / 28 |
| Colorless strong spell | 1 / 1 / 1 | 1 / 1 / 1 | 47 / 47 / 47 |
| Offered fixing land | 1 / 1 / 1 | 1 / 1 / 1 | 25 / 24 / 24 |

The source land additions also advance the stage; those small D/weight changes explain display differences where P/R stay fixed. Tests compare versions on identical inputs and verify unchanged Q, P, L, A and D. Lands receive no new relevance penalty; their existing fixing value still depends on actual measured source-shortfall improvement, not their label. The phase-10 projection remains a two-color builder with limited splash eligibility credit; arbitrary splash construction is not introduced.

## Continuity audit

Production formula audit: 10001 samples per axis, one input varied at a time, **40004 samples total**. Commitment, P and Q vary by 0.0001; stage progress varies by 0.0001 (0.0042 equivalent picks). Fixed off-plan D=0.1 and neutral L/A isolate relevance and the unchanged stage weights.

| Axis | Largest adjacent exact-score change | Expected monotonicity | Result |
|---|---:|---|---|
| Commitment | 0.00045453623042135405 | Nonincreasing | Pass |
| Playability | 0.00045453623042135405 | Nondecreasing | Pass |
| Quality | 0.004784591899049673 | Nondecreasing | Pass |
| Stage | 0.0034274765118667005 | Nonincreasing in this off-plan control | Pass |

No discontinuous score jumps were found. Pure-grid monotonicity and bounded adjacent changes are also protected by tests. With fixed other components, exact rank crossings occur when continuous scores cross; display rounding and tiers remain their existing discrete presentation rules. Existing intentional piecewise rules remain: Q<=0.5 is never lifted, the min between stage and commitment bounds can change slope, and metadata/projection feasibility may change when discrete cards enter an inventory. The new smoothstep endpoints do not introduce score jumps.

This does not establish universal monotonic scores when several actual draft inputs change together. A new pick can change eligibility/constraints/alternative plans, or new recovery information can change inventory coverage. In particular, advancing a coordinate with a fixed partial pool reduces evidence coverage and can restore speculative relevance; that represents increased uncertainty, not a claim of stronger commitment. The fixed-C stage audit isolates the formula instead of conflating those effects.

## Broader deterministic replay

Reused the existing Phase 7 `fraprobe` harness, copied into `artifacts/v162-fraprobe` with only an explicit version selector. Ran `dist 20` across nine stages and `selfdraft 3 0` under v1.6.1 and v1.6.2, using existing cached FRA Premier statistics and deterministic packs. Both completed successfully with zero network requests.

- v1.6.1 decoded output is identical to the previously frozen v1.6.1 replay, preserving old behavior through the playability-weight extraction.
- Fixed-input distribution comparison: **1740 card evaluations**, **257 exact-score reductions**, **0 increases**, maximum reduction **14.559 points** at the harness's 0.001-point output precision. Some rankings/other diagnostic fields change even when a card's exact score does not, because alternatives lose relevance.
- Self-draft trajectories change as expected when recommendations select different picks; these are simulations, not independent win-rate validation. Do not interpret downstream changed pools/D as a D recalibration.
- Summary/hashes: `artifacts/v162-fraprobe/comparison.json`. Numerical corpus output is retained for review.

## v1.6.1 sparse prior and Uldaros non-regression

All 14 opening-pack exact scores/ranks and all prior records are unchanged between explicit v1.6.1 and v1.6.2. Uldaros still uses **AlternateFormatPrior**, Q=**0.9765137572943683**, exact **48.21998625015792**, display **48 EST**, rank **1**. Futurescribe stays direct-primary, Q=**0.6021321854690532**, exact **30.00447708798361**. GIH/ALSA remain their original primary fields, including Uldaros's nulls. P1P1 remains open and skips projection work.

The sparse prior's 0.9 reliability, 500-game shrinkage, centering, FRA-only Quick→Premier eligibility, raw inputs and provenance are not modified. That source file only gains the v1.6.2 **version enum member**; its prior implementation is untouched. Score composition allows the same frozen prior in both v1.6.1 and v1.6.2. The v1.6.1 replay CLI and frozen tests explicitly choose their version; v1.6 remains selectable and its fixture expectations remain intact. Tier scheme remains `pick-score-tiers-v2`.

## Explanations

Normal badge layout/labels are unchanged. Expanded diagnostics expose base R, context R, C, pre-candidate plan, top/alternative plausibility, colored support, maturity, inventory coverage, actual P, raw Q and Q_eff. An ordinary off-plan example explains: “Card quality is less useful outside your current RG plan: commitment 94%, projected playability 0%, quality relevance 10%.” Premium quality's retained tail is also disclosed. These are model interpretations, not statistical-confidence guarantees. Intrinsic quality and contextual relevance remain separate.

## Build, tests and changed files

- `dotnet build DraftTG.sln`: **passed, 0 warnings, 0 errors**.
- `dotnet test DraftTG.sln --no-build`: **1277 passed, 0 failed, 0 skipped** (Domain 56, Data 160, ArenaIntegration 146, Application 285, RecommendationEngine 335, App 295). Final verification TRX: `artifacts/v162-verified-tests` (the earlier full run in `artifacts/v162-final-tests` also passed).
- Five added cases: four focused commitment/relevance tests, plus a v1.6.2 case in the existing real P1P8 versioned regression. Existing sparse-real-pack test adds direct v1.6.1/v1.6.2 equality checks. Coverage includes open/mixed/moderate/strong commitment, all P ranges, premium tail, partial inventory, same real off-color quality at open positions, exact P1P8 ranking, fixing/gold/colorless/pips, monotonicity, determinism and Uldaros parity.
- The legacy “zero early optimizer calls” expectation is kept for explicit v1.6.1; the same test separately checks v1.6.2's early projection behavior and preserved speculative-bomb flexibility.
- Production changes: `ProjectedColorCommitment.cs`, read-only before-projection exposure/refactoring in `CandidateDeckImpactEvaluator.cs`, version/relevance fields and composition in `ContextualPickScoreModels.cs` / `ContextualPickScoreEngine.cs`, version enum addition, and debug explanations in `PickScorePresentation.cs`.
- Audit/test additions: `CommitmentRelevanceAudit.cs`, `calibrate_commitment_relevance.py`, shared controlled scenario inputs, targeted tests/project fixture references, versioned audit fixture and grid. Existing audit tools/version documentation are updated; original fixtures are not changed.

No Phase 9F localization, memory integration, capture geometry, WGC authority, lane/archetype algorithms, sparse-prior policy, D coefficients or tier boundaries were changed. No live result was fabricated or counted. Base repository commit remains `d1d09f6` with existing uncommitted earlier-phase work and these additions.

## Limitations and freeze recommendation

The normalized commitment and premium tail are behavioral heuristics, not outcome-trained probabilities. The real acceptance evidence is one FRA P1P8 regression; broader coverage is controlled/simulated. Projection quality, viability, inventory recovery and the existing splash-credit limitations constrain this view. Extra projection work is performed for nonempty early pools in v1.6.2; old versions retain the zero-D optimization shortcut. No claim is made that every possible pool/metadata transition is globally smooth or that this improves live draft win rates.

**Recommend freezing v1.6.2's defined behavior and constants** with these versioned regression/continuity artifacts. The general mechanism resolves the real failure while preserving open speculation, finite premium upside, fixing treatment, intrinsic quality and the frozen prior. Keep future model changes versioned and use additional real drafts to evaluate behavior rather than silently retuning these constants. Stop here; no v1.6.3 or Phase 9F.3 work is started.

## Reproduce the audit

```powershell
dotnet run --project .\tools\DraftTG.PickScoreAudit -- --commitment-relevance-audit .\tests\Fixtures .\artifacts\v162-replay.json
python .\tools\DraftTG.PickScoreAudit\calibrate_commitment_relevance.py .\artifacts\v162-grid.json
```

The audit uses committed frozen inputs and production engines, with no Arena or provider access. Explicit `ContextualPickScoreVersion.V1_6` / `V1_6_1` reproduces older scoring; the current default is `contextual-pick-score-v1.6.2`.
