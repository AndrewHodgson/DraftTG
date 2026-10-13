# Contextual Pick Score v1.6.1 — sparse primary-format quality prior

**Status: FROZEN**, as accepted in the v1.6.2 request. Select `ContextualPickScoreVersion.V1_6_1` for exact reproduction.
The current v1.6.2 default adds [commitment-aware relevance](CONTEXTUAL_PICK_SCORE_V1_6_2_COMMITMENT_RELEVANCE.md), retaining this prior's formula, eligibility and provenance.

## Pre-change quality pipeline audit

Recorded before production implementation, 2026-10-13. Base commit `d1d09f6`, with the existing uncommitted 9F.2 and diagnostic investigation changes.

The production `SeventeenLandsCardRatingsClient` loads one exact expansion/event/ALL_TIME response. `LimitedStatisticsMapper` resolves the already-known printing to an exact canonical name, or the existing safe structured front-face alias; this alias changes the row join, not the format or quality calculation. `StatisticalRecommendationEngine` uses whole-environment valid GIH rows to compute the sample-weighted baseline B. A valid primary rate g and positive sample n produce `(n*g + 500*B)/(n+500)` and evidence weight `n/(n+500)`. `ContextualPickScoreEngine` normalizes this adjusted value through `SoftLimit(0.5+(adjusted-B)/0.16, 0.35)` to Q. The unchanged stage/playability rule produces R, then Q_eff; existing Q/L/A/D weights and calibration produce the final 0–50 score.

Existing fallbacks: no usable primary GIH, including a missing row, null rate or invalid/nonpositive sample, means neutral Q=0.5 when B exists. No B means no score. ALSA alone cannot supply Q. The 500-game UI low-sample marker does not reject valid rates. No alternate-format quality, rarity bonus or Oracle-based intrinsic-quality inference exists in v1.6. The v1.6 specification and frozen real-pack/P1P8 fixtures must remain reproducible.

The real FRA P1P1 fixture has Uldaros QuickDraft GIH null, n=371, ALSA null, and Q=0.5 / 25 EST / rank 6. Its separate PremierDraft sample is GIH 0.69034167 over 3717 games. Futurescribe's usable primary measurement produces Q=0.6021321854690532 / exact 30.00447708798361 / rank 1. This task addresses unavailable quality, without changing contextual weights or comparing numeric scores to Untapped.

## Calibration plan and eligibility constraints

Only the cached FRA QuickDraft and PremierDraft ALL_TIME snapshots are available. Both are best-of-one Limited datasets; bot versus human drafting and different player populations can change card and environment performance. Compare card deviations from their own format baselines before transferring quality. QuickDraft → PremierDraft prior is the candidate direction. Reverse-direction results must be evaluated independently; TraditionalDraft has no cached calibration dataset and best-of-three sideboarding is a further difference. No cross-set joins, missing-primary-row broadening, or bonus-sheet extrapolation is authorized by this calibration.

Evaluate (A) sample shrinkage of alternate absolute GIH toward primary B; (B) alternate-environment centering, sample shrinkage and cross-format attenuation; (C) hierarchical pooling. B uses few identifiable parameters; C needs richer per-game/population/time data than these two aggregate snapshots. Parameters will be selected from development-only folds using a conservative one-standard-error rule, and assessed once on a separate card-name-hashed holdout. Whole-environment baselines remain known context; card target rates are withheld during fitting. Report sample coverage, errors, overestimation, adverse examples and limitations. Do not select parameters to force Uldaros's rank.

## Implemented eligibility policy

Model implemented in this report: **`contextual-pick-score-v1.6.1`**. Tier scheme remains **`pick-score-tiers-v2`**. The old model is explicitly selectable through `ContextualPickScoreVersion.V1_6`; its quality path ignores alternate evidence. Original v1.6 fixture JSON and score expectations were not rewritten. Frozen audit commands explicitly select that version.

| Requested format / expansion | Quality source candidate | Status |
|---|---|---|
| FRA QuickDraft | FRA PremierDraft | Enabled for a resolved existing primary row without usable GIH |
| FRA PremierDraft | QuickDraft | Not enabled; reverse direction has only an exploratory check, no separately selected policy |
| TraditionalDraft | None | No cached calibration data; do not assume best-of-three sideboarding/population equivalence |
| Any other expansion | None | No set-specific transfer calibration; retain existing neutral fallback |

Only ordinary printings whose exact resolved `Card.SetCode` matches the requested expansion can borrow quality. Same-name reprints from another set and bonus/special-sheet printings fail closed. Healthy primary measurements retain precedence even with small positive samples; alternate evidence never blends into them. A missing, conflicting or ambiguous primary row remains neutral, even if an alternate row exists. This deliberately limits the change to the observed sparse-existing-row problem. Primary and alternate responses must be exact expansion/format, whole-environment `/api/card_data`, ALL_TIME, without color-pair filters. Both row joins reuse the existing exact-name / safe structured front-face resolver, preserving the exact printing UUID.

## Empirical relationship and parameter selection

The [calibration artifact](docs/evidence/stat-provenance/v1.6.1-calibration.json) records source hashes/fetch times, every matched observation, the parameter grid, development folds, independent card holdout, individual errors and buckets. Reproduce with the standard-library-only offline script in `tools/DraftTG.PickScoreAudit/calibrate_sparse_quality.py`. No Untapped inputs, rarity features, rating parity target or Uldaros-specific target was used.

- QuickDraft cache: fetched 2026-10-12; 186 valid GIH rows. PremierDraft cache: fetched 2026-10-05; 270 valid GIH rows. There are **186 exact provider-name overlaps** with valid GIH in both snapshots.
- GIH Pearson correlation: **0.894456891990273**. This is descriptive card-level association, not causal proof or independent-player evidence.
- Unweighted mean Quick minus Premier GIH: **+0.615106 percentage points**; delta standard deviation **1.454871 pp**. Delta percentiles 5/25/50/75/95: **−1.713649 / −0.522188 / +0.795488 / +1.621157 / +2.996998 pp**.
- Whole-environment game-weighted baselines: Quick **0.5704692518601339**, Premier **0.5636523015032496**. Quick runs about **0.681695 pp** higher by this aggregate. Transfer card deviations from their own environment instead of treating raw Premier rates as Quick rates.
- Stable SHA-256 card-name partition: **141 development cards, 45 held-out test cards**. The development cards form five fixed evaluation folds. Each masked primary card is also removed from its primary environment baseline; its rate cannot leak through that aggregate. Cards are masked individually, not all 45 simultaneously.
- Candidate cross-format reliability: 0.1–1.0 in steps of 0.1. Shrinkage-games candidates: 500 / 1000 / 2000 / 4000 / 8000 / 16000. The lower bound reuses the existing Phase 8 500-game shrinkage prior; samples below that were not used to invent a new low-sample calibration.
- Development minimum: reliability 1.0, k=500, fold-mean MAE **0.012143353970415487**, standard error **0.00040724501776717747**. Apply the predeclared one-standard-error rule, then choose the candidate with the smallest mean effective reliability within that error band. This selects **reliability 0.9, k=500**, development MAE **0.012509094382700762**. Its mean effective reliability is **0.8599680089541243**. Test cards were not used to select these parameters.

Conceptual alternatives: **A**, shrinking an uncentered alternate rate toward primary B with the same selected parameters, gives held-out MAE **1.143707 pp**; **B**, centering on the alternate baseline then shrinking and attenuating, gives **1.108452 pp**, while removing a systematic environment-level difference. **C**, a richer hierarchical model of player population, event, time and card interactions, is not identifiable from two aggregate snapshots and was not implemented. B is the smallest supported extension to the existing Q input.

An exploratory reverse-direction check with the same, non-retuned parameters has inferred-GIH MAE **1.344782 pp** versus neutral **2.564994 pp**. That is not sufficient evidence to enable reverse borrowing: it has not had independent reverse-direction parameter selection and its alternate Quick sample distribution differs substantially. TraditionalDraft has no evidence here.

## Exact prior formula and reuse of the existing quality path

Let Bp be the current primary whole-environment baseline, Ba the alternate whole-environment baseline, ga the alternate raw GIH, and na its positive sample count:

```text
sample_weight = na / (na + 500)
cross_format_reliability = 0.9
effective_reliability = 0.9 * sample_weight
prior_quality_value = clamp(Bp + effective_reliability * (ga - Ba), 0, 1)
Q = existing SoftLimit(0.5 + (prior_quality_value - Bp)/0.16, knee=0.35)
```

Q then flows through **unchanged** quality relevance R, Q_eff, Q/L/A/D weights, soft calibration and display rounding. There is no second recommendation pipeline and no extra 500-game primary shrink applied to an already constructed prior. The alternate value is a quality estimate, not an asserted QuickDraft win rate. Finite-input checks and the clamp fail safely at the rate boundaries.

Tiny alternate samples approach neutral; n=1 retains less than 0.2% of alternate deviation after attenuation. Large samples retain at most 90% of the alternate environment-relative deviation. Missing/null/invalid alternate GIH, zero/nonpositive samples, unsupported scope or no primary baseline do not produce an alternate prior. A valid environment with no admissible prior retains Q=0.5; absent primary baseline produces MISSING/no score. The primary positive-sample path, including 500-game shrinkage, is unchanged. Direct primary data always wins, irrespective of alternate sample size; alternate evidence is always labeled estimated and is never assigned a primary statistical data weight.

`LimitedStatisticsService` loads at most one additional whole-environment dataset per loaded FRA Quick environment through the existing cache/client and coordinator. It computes the alternate baseline once through the existing mapper and `StatisticalRecommendationEngine`. Immutable quality-only evidence travels alongside the primary result. It is never inserted into primary `Catalog`, `EnvironmentCatalog`, lane observations, pair statistics, pool quality, deck optimizer or commitment inputs. The existing generation/revision checks apply to the completed statistics update. Provider unavailability does not discard the successful primary result. Cold alternate I/O can extend the initial statistics load; it has the existing HTTP timeout/cache/backoff behavior and does not poll per card or per pack. `StaleCache` evidence is permitted under the existing provider-cache policy and explicitly records its source and fetch time; no claim of fresh data is made.

## Provenance and ALSA

Every computed score has one explicit provenance value:

| Code value | Meaning |
|---|---|
| `DirectPrimary` | Usable primary GIH from an exact-name row |
| `FrontFacePrimary` | Usable primary GIH via the existing safe front-face statistical alias |
| `AlternateFormatPrior` | Quality-only estimate from an eligible alternate row |
| `NeutralEstimate` | No admissible measured/prior quality; Q=0.5 |
| `Missing` | No valid primary environment baseline; no score |

Alternate provenance records source expansion/format/name, raw GIH, game count, source/fetch time/time period, primary and alternate baselines, k, sample weight and removed fraction, cross-format/effective reliability, prior quality value and resulting Q. Diagnostics and explanations distinguish “alternate PremierDraft prior” from “neutral format prior.” These reliability factors are model attenuation, not statistical confidence intervals.

The normal badge retains **EST** for an alternate prior. It still shows the original primary GIH and ALSA, independently of quality estimation. Uldaros remains **GIH — / ALSA —**, with n=371 as the primary sample where shown. Premier ALSA is never attached to its Quick statistics. Measured cards retain measured status. Tier boundaries and normal badge layout are unchanged.

## Real Uldaros before/after

The [new frozen comparison](tests/Fixtures/phase9f2-live-pack-v1.6.1.json) reuses the [original 14-card fixture](tests/Fixtures/phase9f2-live-pack-stat-provenance.json), production cache parser, complete historical Scryfall catalog, same primary response, production statistics service and same contextual stages. It contains the complete alternate raw cache, per-card primary inputs, both score breakdowns, exact ranks, diagnostics and the P1P8 comparison. All replay HTTP is rejected; network requests were zero. This is offline regression evidence, not new live localization or promotion evidence.

Uldaros:

- v1.6: primary rate null; Q=0.5; exact **24.999999999999996**; display **25 EST**, rank **6**.
- v1.6.1: alternate PremierDraft ga=**0.69034167**, na=**3717**, Ba=**0.5636523015032496**.
- Sample weight **0.8814322978420678**; removed fraction **0.11856770215793222**; cross-format reliability **0.9**; effective reliability **0.793289068057861**.
- Bp=**0.5704692518601339**; prior quality value **0.6709705429277599**; Q=**0.9765137572943683**; R=1, Q_eff=Q. L=A=D=0.5, weights 0.70/0.20/0.10/0.
- Unchanged calibration yields exact **48.21998625015792**, display **48 EST**, rank **1**. Quality contributes +23.349174107424048 points over neutral; the existing final soft limit removes 0.12918785726611648 points. Ranking #1 was not an acceptance requirement or a fitting target; it follows from the independent Premier sample and the empirically selected transfer policy.

Futurescribe remains direct QuickDraft: g=0.5907767, n=2060, adjusted=0.5868104015351824, Q=**0.6021321854690532**, exact **30.00447708798361**, display **30**. Its rank becomes 2 because Uldaros's estimated quality changes; its own score calculation and primary fields are identical.

## Complete native-order pack comparison

Exact values for both versions and all components are in the fixture. Displayed EST is retained for every estimated-quality card.

| GrpId | Card | v1.6 display / rank | v1.6.1 display / rank | v1.6.1 exact | Quality provenance |
|---:|---|---|---|---:|---|
| 106401 | Uldaros Theorix | 25 EST / 6 | 48 EST / 1 | 48.21998625015792 | Alternate Premier prior |
| 106292 | Rise of the Deathbringer | 25 EST / 7 | 25 EST / 7 | 25.024082088419718 | Alternate Premier prior |
| 106451 | Tomik, Orzhov Lawmage | 16 / 12 | 16 / 11 | 15.747426634811736 | Direct primary |
| 106486 | Yargle, Glutton of Urborg | 25 EST / 8 | 25 EST / 8 | 24.999999999999996 | Neutral estimate |
| 106498 | Tomik, Izzet Sparkmage | 12 / 14 | 12 / 13 | 12.198334652282957 | Direct primary |
| 106347 | Restore with Empathy | 27 / 4 | 27 / 5 | 27.450675221485056 | Direct primary |
| 106364 | Desperate Futurescribe | 30 / 1 | 30 / 2 | 30.00447708798361 | Direct primary |
| 106240 | Memory Trap | 26 / 5 | 26 / 6 | 25.86786803670924 | Direct primary |
| 106289 | Rampart Hunter | 25 EST / 9 | 6 EST / 14 | 6.264761697264612 | Alternate Premier prior |
| 106327 | Tether Technician | 16 / 13 | 16 / 12 | 15.597178853926094 | Direct primary |
| 106369 | Fatehold Chronologist // Peer Review | 28 / 3 | 28 / 4 | 28.05536850112788 | Front-face primary |
| 106376 | Konstrari Improviser // Soul Tether | 24 / 10 | 24 / 9 | 23.720637984636383 | Front-face primary |
| 106418 | Medic's Kitesail | 20 / 11 | 20 / 10 | 19.927283192773697 | Direct primary |
| 106423 | Formidable Commons | 28 / 2 | 28 / 3 | 28.29547999881179 | Direct primary |

All **ten measured cards' exact scores are unchanged**. Rise is nearly neutral because its Premier GIH 0.56374753 is near the Premier baseline. Rampart's Premier GIH 0.4835055 over 3001 games supports a substantial below-baseline estimate; it is no longer treated as intrinsically equal to an unknown average card. Yargle's Premier rate is also null (n=297), so it retains neutral Q=0.5. No primary GIH or ALSA values were manufactured for any of them.

## Broader masked-primary validation

For each usable Quick/Premier pair, hide Quick quality and its environment contribution, construct the prior from Premier, and compare against the observed Quick GIH. Q error compares prior Q with the existing primary shrinkage/normalization at that masked primary baseline. These are predictions of noisy observed card rates, not per-game calibrated win probabilities or draft outcomes.

| Metric | Neutral fallback | Prior | Scope |
|---|---:|---:|---|
| Inferred GIH MAE | 2.568773 pp | **1.108452 pp** | 45 held-out cards, **56.85% reduction** |
| Inferred GIH RMSE | 3.167474 pp | **1.382727 pp** | Held out |
| Q MAE | 0.113766906 | **0.063947181** | Held out, **43.79% reduction** |
| Rank correlation | Undefined: all neutral estimates tie | **0.877470356** | Held out |
| Above/below baseline direction | Undefined: neutral has no direction | **82.22%** | Held out |
| Overestimate actual GIH by >=5 pp | 2 / 45 | **0 / 45** | Predefined catastrophic threshold |
| Absolute error worsened by >=2 pp | — | **0 / 45** | Predefined material-worsening threshold |

Sample-size buckets below use **all 186 overlap cards** for descriptive coverage, not another independent holdout:

| Alternate GIH sample | Cards | Neutral MAE | Prior MAE |
|---|---:|---:|---:|
| <500 | 0 | Unvalidated | Unvalidated |
| 500–2849 | 0 | Unvalidated | Unvalidated |
| 2850–4999 | 8 | 4.710790 pp | 2.121698 pp |
| 5000–9999 | 57 | 3.772881 pp | 1.460027 pp |
| 10000–19999 | 86 | 2.101707 pp | 1.092454 pp |
| >=20000 | 35 | 1.715237 pp | 0.889022 pp |

The artifact also reports error by primary sample (500–999, 1000–1999, 2000–4999 and >=5000), with improvement in every populated bucket. The empirical sample floor is **2850 alternate games**; tiny-sample behavior is protected mathematically by targeted tests, not claimed as empirically validated. Uldaros's 3717 and Rampart's 3001 lie within the observed overlap range.

**Adverse cases:** improvement is not universal. 12/45 held-out and 46/186 descriptive-all cards have any greater absolute error than neutral. No card worsens by >=2 pp, but Theorix Charm worsens by **1.0871 pp** (predicted 58.1412%, observed 56.0545%) and Konstrari Improviser by **1.0052 pp** (55.0572% versus 56.5565%) in the heldout. Across all pairs, Hapatra, the Desert Frost worsens by **1.4987 pp**, Divining Duelist **1.3258 pp**, and Compel Brutality **1.0724 pp**. These are masked-primary failures; their actual usable primary measurements still win in production and are never replaced by these predictions. The tested policy improves the aggregate and avoids observed catastrophic overestimation; it is not perfect for each card.

## Failure and fallback rules

Use primary adjusted GIH whenever available. Only version v1.6.1 plus a supported scope plus a resolved existing primary row can enter prior creation. Reject a different set/printing identifier, unsupported format direction, unknown time period, filtered environment, invalid baseline/rate/sample, unavailable source, missing or unsafe name join. No primary baseline means no score. Other prior declines mean neutral Q=0.5; a sparse primary response is never replaced wholesale by another format. Alternate cache source/time and optional load diagnostics are retained independently of the primary statistics status. Repeated scoring with fixed inputs is deterministic.

## Tests and replay validation

- `dotnet build DraftTG.sln`: **passed, 0 warnings, 0 errors**.
- `dotnet test DraftTG.sln --no-build`: **1272 passed, 0 failed, 0 skipped** (Domain 56, Data 160, ArenaIntegration 146, Application 284, RecommendationEngine 331, App 295). TRX: `artifacts/v161-final-tests`.
- Added five focused tests across `SparsePrimaryQualityPriorTests` and `SparsePrimaryQualityTests`, plus one additional version case in the existing P1P8 regression. They cover centered shrinkage, strong/weak/average and tiny/large samples, valid-primary precedence, missing/null alternate, missing primary, cross-set/special-sheet rejection, invalid context/period/baseline/sample, safe multiface joins, provenance, unchanged primary fields/components, all real-pack scores and deterministic calculation.
- Original real-pack v1.6 regression explicitly selects v1.6. V1 golden fixtures explicitly select their frozen version. No original fixture JSON or v1.6 score constants were altered.
- The existing Phase 7 `fraprobe` harness was copied into `artifacts/v161-fraprobe`, adding only an explicit version selector. Ran `dist 20` (20 deterministic packs at each of the nine stages) and `selfdraft 3 0` under each version. PremierDraft score output is **byte-identical** between versions: 1775 decoded distribution output lines and 1742 decoded selfdraft output lines; selfdraft reports 252 evaluations and zero network requests. These decoded counts correct an earlier byte-level count of PowerShell UTF-16 output; the identical output hashes and score comparison are unchanged. This is a bounded compatibility replay, not another calibration or a live draft campaign. Summary/hashes: `artifacts/v161-fraprobe/comparison.json`.
- Offline full-catalog real-pack/P1P8 replay through the production service: zero network requests; alternate baseline computed once using the existing baseline aggregator. Diagnostic tool builds cleanly.

## Remaining P1P8 RG/Unsummon issue

Both versions were rerun with the frozen primary inputs and the same historical alternate cache. **Unsummon remains 28.70495083517041 → 29, rank 1; Arcane Amphisbaena remains 25.501120969070218 → 26, rank 2.** RG projection, Unsummon playability zero, R=1 and the small early D weight remain unchanged. This contextual-weight/relevance issue is unresolved and is not part of v1.6.1.

One incidental change is expected under the new quality-only policy: **Way of the Necromancer**, whose Quick GIH was null, has Premier GIH 0.44300518 over 1930 games. It falls from **24.603954875454026 → 25, rank 3** to **2.716718844037702 → 3 EST, rank 7**. Other measured cards retain their exact scores; their ordinal ranks shift after Way falls. This does not correct or mask the Unsummon/Amphisbaena comparison. The original P1P8 fixture remains intact; the new comparison is separately frozen.

## Known limitations and recommendation

This is one set and two asynchronous, aggregate snapshots. The card holdout does not establish generalization to other expansions, later metagames, shared-player effects, alternate samples below 2850, TraditionalDraft, bonus sheets, or individual game/draft outcomes. The dates differ by seven days; source/time are disclosed, not silently treated as synchronized. Reliability 0.9 is an empirically chosen attenuation, not a calibrated confidence probability. The catastrophic-error count is finite-sample evidence, not a guarantee. No live overlay session was started or re-collected during this task.

**Recommend freezing v1.6.1 with this exact restricted FRA QuickDraft → PremierDraft policy and constants**, its EST/provenance semantics and the new regression artifacts. Do not broaden format/set/sheet eligibility without separate calibration. Broader weighting/relevance work, including the RG/Unsummon issue, requires another explicitly scoped model version. Phase 9F.2 localization, memory reading, capture geometry, WGC authority, lane/archetype/deck-need/playability/commitment algorithms and tier boundaries were left unchanged.

## Reproduction commands

From the repository root, with the preserved original local caches:

```powershell
python .\tools\DraftTG.PickScoreAudit\calibrate_sparse_quality.py "$env:LOCALAPPDATA\DraftTG\limited-data\17lands\FRA_QuickDraft_ALL_TIME_v3.json" "$env:LOCALAPPDATA\DraftTG\limited-data\17lands\FRA_PremierDraft_ALL_TIME_v3.json" .\artifacts\v161-calibration-replay.json

dotnet run --project .\tools\DraftTG.PickScoreAudit -- --sparse-quality-replay .\tests\Fixtures\phase9f2-live-pack-stat-provenance.json .\artifacts\v161-live-pack-replay.json
```

The regression fixtures themselves are self-contained for tests; historical reproduction uses frozen rows and baselines rather than refreshing provider data. The CLI checks that the primary statistics and historical Scryfall cache still match the original investigation. Existing `--card-stat-provenance` continues to inspect the original v1.6 record, not masquerade as a v1.6.1 live score. The v1.6.1 JSON artifact contains the full updated diagnostic record for each card. Stop here; no RG/Unsummon correction or localization promotion was started.
