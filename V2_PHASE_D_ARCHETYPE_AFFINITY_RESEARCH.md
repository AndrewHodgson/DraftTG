# Contextual Pick Score V2 — Phase D: Set-Generic Archetype Affinity & Card Synergy Research

## 1. Executive conclusion

**Retain production A. Candidate C is best among the new families but does not qualify for future V2 integration.** Development power-matched completion MRR is 0.544553, versus 0.535207 for the frozen quality-weighted shell-frequency control: **1.7464% relative gain**, below the preregistered 2% requirement. It improves in 4/5 development sets; coverage and per-set gates pass, but aggregate prediction and cold-start gates fail. MH3/DFT remain untouched.

Production `contextual-pick-score-v1.6.2`, Q, Phase 9B L, current A, D, R/commitment, k500, FRA prior, tiers, deck optimizer, Phase 9F/localization and overlays are unchanged. All 341 protected source/evidence files match. Tested commit `a47bac227878a74990a35d10332ebd7200c68e89` plus Phase D working-tree changes. No next phase is started. [Validation](docs/evidence/v2-phase-d/validation.json), [gate](docs/evidence/v2-phase-d/promotion-gate.json).

## 2. Objective

Learn set-specific relationships with one set-generic algorithm: card-to-shell/factor, card-to-card, pool-to-factor and candidate compatibility with the existing pool. Q is intrinsic strength, L availability, A relationship/strategy fit, D projected-deck improvement. No authored set/color archetype rules or neural network. Structure is fitted without wins; game outcomes and target modal builds are joined only for labeled evaluation/descriptions.

The preregistration was committed as `bbf2693` before performance analysis. A scale clarification, `a47bac2`, prospectively fixed representation comparison to a shared binary inclusion target rather than incomparable MAEs on binary/count scales. Calibration DMU/BRO/WOE fits representation, context and family parameters; development LCI/MKM/OTJ/ONE/MOM selects the family and tests the gate. [Plan](docs/evidence/v2-phase-d/preregistration.json).

## 3. Production A audit

Current A is an empirical pair-GIH lift conditional on an active authored set/pair profile. Activation requires confidence>=.35 and lead>=.10. Pair GIH is shrunk toward overall GIH with prior300, compared against pair/overall baselines, normalized by .04 and converted to `.5+.5*affinity*activeConfidence`. Missing/unsettled pair evidence gives neutral A. Production retains this contract.

Historical replay snapshots have no qualified pair-GIH table, so current A is predominantly neutral; this is a missing-input baseline, not proof that production archetype evidence is ineffective. The experimental study learns composition relationships, not another GIH bonus. [Existing implementation](src/DraftTG.RecommendationEngine/ArchetypeRecommendationEngine.cs).

## 4. Build corpus qualification

64,638 conservative builds across 14 existing datasets; 43,852 in eight modeled Premier sets. Of these, **17,706 fit / 4,340 internal evidence-audit / 21,806 target builds**. Original approximately85k Phase B primary builds include spent sets and less reliable configurations; they are not all model training rows.

| Set | Primary available | Qualified | Fit | Audit | Target |
| --- | --- | --- | --- | --- | --- |
| AFR | 3120 | 2495 | 0 | 0 | 0 |
| BRO | 7119 | 5099 | 2088 | 481 | 2530 |
| DMU | 7256 | 5299 | 2126 | 520 | 2653 |
| KHM | 3112 | 2302 | 0 | 0 | 0 |
| LCI | 7135 | 5430 | 2165 | 531 | 2734 |
| MKM | 7430 | 5759 | 2288 | 597 | 2874 |
| NEO | 7354 | 5525 | 0 | 0 | 0 |
| OTJ | 7301 | 5722 | 2313 | 573 | 2836 |
| SNC | 7318 | 5594 | 0 | 0 | 0 |
| STX | 3198 | 2444 | 0 | 0 | 0 |
| VOW | 3149 | 2426 | 0 | 0 | 0 |
| WOE | 7305 | 5492 | 2234 | 525 | 2733 |
| MOM | 7221 | 5608 | 2287 | 542 | 2779 |
| ONE | 7359 | 5443 | 2205 | 571 | 2667 |

Require resolved positive integer counts, exactly40 cards, unique source mode, >=3 observed games, one observed configuration and12–20 lands. Multiple configurations decline because saved summaries retain total games but **not exact mode support**. Single-configuration support equals observed game count. HIGH means>=5 games and opening/modal agreement; MEDIUM covers other eligible builds; LOW declines. Data are reused from attributed 17Lands public bulk sources and the pinned TrevorJS transformation, plus the owned Scryfall identity catalog; source hashes and bounded-prefix caveats remain in the [Phase B source audit](docs/evidence/v2-phase-b/source-audit.json). No new acquisition/provider request occurred. Existing aggregate ties, non40 configurations, opening agreement, main colors, land counts and mode-support distributions remain in [qualification](docs/evidence/v2-phase-d/build-qualification.json). Missing opening agreement is not proven disagreement. Bounded histories are not proven complete; **zero final submitted decks are asserted**.

## 5. Build representation

Compared binary presence, normalized copies, nonland normalized copies, and square-root counts capped at3. Selected **sqrt**, calibration common-target inclusion MAE 0.107685. Basics are excluded; nonbasic lands remain in the selected card vector while land/fixing summaries are separate. Pool inference excludes lands. The nonland-only alternative was tested, not silently assumed.

| Representation | Context | Calibration inclusion MAE |
| --- | --- | --- |
| binary | pair | 0.108350 |
| binary | broad | 0.108769 |
| binary | global | 0.120210 |
| counts | pair | 0.108300 |
| counts | broad | 0.108700 |
| counts | global | 0.120210 |
| nonland | pair | 0.108300 |
| nonland | broad | 0.108700 |
| nonland | global | 0.120210 |
| sqrt | pair | 0.107685 |
| sqrt | broad | 0.108073 |
| sqrt | global | 0.120210 |

Structured creature/noncreature counts, curve0–6+, early plays, mana value, land counts, splash spell loads, assigned removal and fixing are descriptive. Untagged removal/synergy roles remain **unknown**, not zero; assigned coverage is a lower bound. No role bonus enters model fitting. [Feature coverage](docs/evidence/v2-phase-d/build-features.json).

## 6. Color-shell analysis

Compared two strongest nonland spell-color loads, broad >=15% spell-color support, and a global model. **Two-color shell wins calibration reconstruction.** Gold spell loads split by colors; basics do not determine the shell. Lesser spell colors are recorded as splashes. Shell labels are learned context keys, not labels such as UR=spells.

At inference the pool gets soft beliefs over supported shells; the candidate cannot alter this prior. Color identity alone is controlled by same-color distractors and a tied color-only baseline. Within-shell factors permit multiple strategies in one pair. Fixing lands/splash artifacts do not automatically redefine the base shell.

## 7. Frequency baselines

Color-only assigns tied scores within matched colors. Shell frequency predicts inclusion conditional on pool-inferred shell; Q-only uses frozen current-k500 raw Q. Frequency-plus-Q ranks `log(inclusion)+2*Q`. All evaluated candidates/control cards are identical per task. Pair base expectation additionally controls within-shell factor prevalence and marginal inclusion.

The main completion controls use the calibration-selected A-family pool weighting (quality). The frozen cold/full run also exports a **uniform-pool frequency control** on the same tasks. This stronger comparator is reported separately below; it does not rescue qualification. Successful-build uplift is descriptive only, never part of these scoring controls. No prevalence/frequency difference is claimed to be causal synergy.

## 8. Candidate A/B/C methodology

**A:** regularized shell inclusion profiles, no explicit latent archetypes. **B:** A plus soft within-shell factor inclusion residual. **C:** B plus regularized pair residual to the existing pool. All families share one generic implementation; the learned cards differ by set.

| Family | Calibration pool weight | Evidence prior | Calibration matched MRR |
| --- | --- | --- | --- |
| A | quality | 50 | 0.575540 |
| B | uniform | 50 | 0.579471 |
| C | uniform | 50 | 0.581365 |

All selected evidence-prior50; A uses frozen-Q-weighted pool membership, B/C uniform. Representation/contexts fit first, then family parameters on disjoint calibration evidence-audit completion; development selects C. Cold priors50/250/500 are confidence budgets, not measured posterior certainty. No post-development parameter changes.

## 9. Archetype discovery

Deterministic nonnegative matrix factorization, ranks1/2/4, 100 multiplicative updates, no outcome inputs. Fit vocabulary includes all nonbasic cards for marginals; factor vocabulary requires>=20 fit builds. A separate internal fit/audit split chooses the smallest rank with>=1% reconstruction gain over rank1, matched-factor cosine>=.8 across a hash-independent half-sample, and every factor>=25 effective builds. Otherwise retain rank1.

| Set | Shells | Total factors | Factors per shell |
| --- | --- | --- | --- |
| DMU | 10 | 20 | 1–4 |
| BRO | 10 | 19 | 1–2 |
| WOE | 10 | 20 | 2–2 |
| LCI | 10 | 15 | 1–2 |
| MKM | 10 | 20 | 2–2 |
| OTJ | 10 | 17 | 1–2 |
| ONE | 10 | 19 | 1–2 |
| MOM | 10 | 19 | 1–4 |

Exact shell-to-factor counts are also in the machine-readable summary and full fit evidence. Factors are automatically numbered; no name is required for scoring. Top loading-to-shell ratios, prevalence, curve and creature traits are published in [descriptions](docs/evidence/v2-phase-d/archetype-diagnostics.json). These are stable statistical deck families, not proof of human archetype names.

## 10. Cluster/factor stability

Soft memberships sum to1 and support mixed themes. NMF factors are matched across resamples by maximum permutation-averaged cosine; factor numbering alone has no meaning. The .8 threshold guards retained multi-factor models. Complete rank1/2/4 reconstruction, support and resample tables are in [fit evidence](docs/evidence/v2-phase-d/model-fit.json).

Nested learning curves separately compare fitted inclusion loadings with the full model. Different-rank comparisons are unavailable rather than assigned a synthetic stability score. Half-sample stability is a heuristic, not a bootstrap confidence interval; full factor stability does not guarantee causal synergy.

## 11. Card-card affinity

132,223 retained residual relationships: 78,485 positive / 53,738 negative. For each: source set/format/shell, observed count, latent expected count, effect, confidence and source-model SHA256 are preserved. Maximum absolute regularized effect 0.220246. Full pairs remain ignored; portable top examples and complete-artifact hash are in [pair evidence](docs/evidence/v2-phase-d/pair-affinity.json).

Expected co-occurrence is the sum of per-build latent inclusion products. Effect is `clip(log((observed+5)/(expected+5)),-1,1)*expected/(expected+50)`. Require expected>=5 and both marginals>=20; positive observed>=5. Low-observed negative relationships additionally require expected>=20 and marginals>=30. A2/2 coincidence returns exactly zero. Confidence is regularization strength, not a calibrated probability of synergy.

## 12. Frequency/power controls

The primary task matches exact colors, mana value within1, raw Q within.10 and fit frequency ratio between.5 and2. All four distractors are absent from the target build. This prevents simple off-color/bomb-vs-poor-card tests, but coarse Q and frequency matches cannot remove all observational selection bias.

Pair expectations condition on latent shell composition; marginal power/frequency is not counted as an independent synergy vote. Low-frequency cards cannot create factors, and regularized pair effects are bounded. The primary matched-task gain is modest and fails the gate; no statistical significance or causal Limited-win benefit is asserted.

## 13. Pool-to-archetype inference

Only `PoolBeforePick`, candidate identity, disjoint models and optionally frozen Q weighting enter inference. Pool shell belief uses prevalence plus weighted log inclusion ratios, softmaxed; factor belief uses prevalence plus weighted factor-to-shell inclusion ratios. Candidate cards are scored against this fixed belief; they never teach it.

Pool multiplicity uses the selected sqrt cap3 transform. The compatibility value combines shell-relative inclusion, latent residual and (C only) weighted average pair residual to existing nonland pool cards. No target final build, future picks/offers, win record, R/commitment or optimizer result enters inference. P1P1 is exact fallback; a one-card pool has at most1/9 pool-confidence budget, so early belief grows smoothly. [Prediction provenance](docs/evidence/v2-phase-d/prediction-provenance.json).

## 14. Experimental A formula

Let `g_i` be global regularized inclusion, `p_si` shell inclusion, `p_ki` factor inclusion, `w_s` pool shell belief and `u_sk` pool factor belief.

```text
A raw = sum_s w_s * log(p_si/g_i)
B raw = A raw + sum_s w_s * log(sum_k u_sk*p_ki / p_si)
C raw = B raw + sum_s w_s * weighted_mean_pool(pairResidual_s(i,j))
fit = .5 + .25*tanh(raw)
confidence = poolSupport/(poolSupport+8) * sum_s w_s*n_s/(n_s+50)
experimental A = current A + confidence*(fit-current A)
```

Shell/factor log beliefs divide the weighted log-evidence sum by4 and include prevalence priors. Inclusion shrinkage uses50 builds toward set/shell marginals. Fit lies [.25,.75], confidence[0,1]. Missing model/card, unsupported shell, land candidate or empty pool retains original A. Offline recomposition uses the actual frozen calibration and weights; Q/L/D/R/commitment, priors and deck projections are copied. No production configuration installs this formula.

## 15. Build-completion metrics

Mask all copies of one hash-selected nonland card from a disjoint target modal build, then rank it against four distractors. At most one task per target draft and500/set; all sets reach500. Full remaining build is the completion pool; this is **not a historical pick-time task**. Tied rankings get expected reciprocal rank and fractional TopK credit.

| Partition/model | Top1 | Top3 | MRR | Normalized rank |
| --- | --- | --- | --- | --- |
| calibration target/A | 36.0667% | 77.6000% | 0.587900 | 0.342000 |
| calibration target/B | 36.0667% | 77.6667% | 0.588622 | 0.340833 |
| calibration target/C | 35.8667% | 77.6667% | 0.587500 | 0.341167 |
| calibration target/frequency | 29.8000% | 82.4000% | 0.560700 | 0.338000 |
| calibration target/Q | 6.6667% | 65.1333% | 0.383167 | 0.518500 |
| calibration target/color-only | 20.0000% | 60.0000% | 0.456667 | 0.500000 |
| development/A | 29.3600% | 72.2000% | 0.539320 | 0.392800 |
| development/B | 30.0800% | 72.3200% | 0.544200 | 0.389000 |
| development/C | 30.1600% | 72.1600% | 0.544553 | 0.389000 |
| development/frequency | 25.5200% | 81.2800% | 0.535207 | 0.356100 |
| development/Q | 7.0000% | 66.7200% | 0.392473 | 0.499300 |
| development/color-only | 20.0000% | 60.0000% | 0.456667 | 0.500000 |

These calibration-target results are separate from internal evidence-audit numbers used to fit parameters. Same-color relaxed completion is secondary and cannot pass the gate. [Completion evidence](docs/evidence/v2-phase-d/build-completion.json).

## 16. Power-matched distractor metrics

Primary development C MRR 0.544553, versus frozen quality-weighted frequency 0.535207: 1.7464% relative, below2%. It wins4/5 sets, losing OTJ, with500 tasks in each. Thus primary **FAIL**, per-set and coverage **PASS**.

The separately frozen uniform-pool full-model frequency control reaches 0.539413 on the same tasks; C improves only **0.9529%** against it. This stronger control makes rejection more conservative. It is a comparator audit, not another family selection or retuning. Gains over the tied color-only baseline do not by themselves establish strategy fit beyond frequency. [Immutable selection](docs/evidence/v2-phase-d/selection.json).

## 17. Pick-time/modal-build association

33,631 complete contextual decisions have qualified disjoint target-build labels. Offered-card normalized ranks are grouped by eventual modal inclusion, actual picked versus other offers, set/pack/pick/stage, pool color fit, Q bucket, commitment and archetype confidence. Lower rank means greater preference.

| Model | Included occurrences/rank | Not-included occurrences/rank |
| --- | --- | --- |
| current | 44693/0.316917 | 229139/0.535710 |
| A | 44693/0.300295 | 229139/0.538952 |
| B | 44693/0.291940 | 229139/0.540582 |
| C | 44693/0.291884 | 229139/0.540593 |

These are labels joined after scoring, not features. Modal builds may omit draft picks due to deck construction, source truncation or sideboarding. Identity duplicates use the best ranked occurrence for the additional human/build strata; canonical evaluator metrics remain separately authoritative. [Association evidence](docs/evidence/v2-phase-d/maindeck-association.json).

## 18. Contextual replay

**54,283 decisions / 1,280 drafts; 50,285 complete comparable decisions.** Reuses B.1 bulk SQLite, target samples, identity catalog, exact-set k500 snapshots, production replay adapter and immutable evidence cache. Production runs once per decision; only A is substituted, with no extra optimizer. Current aggregate metrics exactly reproduce Phase C/B.1 baseline.

| Model | Top1 | Top3 | MRR | Normalized rank |
| --- | --- | --- | --- | --- |
| current A | 37.5002% | 72.0811% | 0.577158 | 0.297833 |
| A | 38.2957% | 73.1232% | 0.584927 | 0.286637 |
| B | 38.8247% | 73.8709% | 0.590163 | 0.279022 |
| C | 38.8287% | 73.8789% | 0.590215 | 0.278961 |

Human revealed preference is secondary, not correctness. Complete pools/identities and comparative ranked labels are required; unsupported historical pick15 and singleton/unresolved rankings do not count. Set/pack/pick/commitment/confidence and Q/eventual-build strata are exported. [Replay evidence](docs/evidence/v2-phase-d/contextual-replay.json).

## 19. Set-level results

| Set | A MRR | B MRR | C MRR | Frequency MRR | C human Top1 | Current human Top1 |
| --- | --- | --- | --- | --- | --- | --- |
| DMU | 0.593667 | 0.597333 | 0.594700 | 0.559733 | 36.2929% | 34.9320% |
| BRO | 0.608733 | 0.608400 | 0.607067 | 0.579033 | 35.6480% | 33.9419% |
| WOE | 0.561300 | 0.560133 | 0.560733 | 0.543333 | 41.8275% | 39.7040% |
| LCI | 0.534867 | 0.543167 | 0.540500 | 0.537133 | 40.0718% | 39.4733% |
| MKM | 0.525467 | 0.523567 | 0.523233 | 0.481833 | 43.5140% | 41.6259% |
| OTJ | 0.521033 | 0.526300 | 0.527533 | 0.542767 | 36.8109% | 36.7949% |
| ONE | 0.580267 | 0.584933 | 0.584867 | 0.567800 | 41.7143% | 39.8319% |
| MOM | 0.534967 | 0.543033 | 0.546633 | 0.546500 | 35.5935% | 34.4307% |

DMU/BRO/WOE calibration versus LCI/MKM/OTJ/ONE/MOM development remain distinct. No set-specific authored strategy formula. OTJ primary regression persists; no change was made to repair it after development. Main discovery and replay are Premier only; Quick and new-format portability remain unvalidated.

## 20. Outcome-stratified description

Clusters were discovered using all qualified structure, not trophies. After fitting, soft factor prevalence, observed game WR and stronger-build inclusion uplift are reported. Stronger means>=5 observed games and observed WR>=.6; this is **not a trophy/final-event label**. Within-shell with/without-card observed WR is descriptive and selection-biased. Target final records are not imported into inference.

Outcomes never enter factor identities, pair residuals or candidate A. The separately ignored uplift artifact and portable counts/examples preserve denominators. Modal build association is not proof of successful construction; actual completed event records are unavailable in the primary build summaries, so no invented mean final record is reported.

## 21. Cold-start learning curve

| Requested fit builds | Achieved development range | C MRR | Uniform frequency MRR |
| --- | --- | --- | --- |
| 50 | 50–50 | 0.456667 | 0.489352 |
| 100 | 100–100 | 0.453838 | 0.486918 |
| 250 | 250–250 | 0.483564 | 0.499043 |
| 500 | 500–500 | 0.528147 | 0.523340 |
| 1000 | 1000–1000 | 0.538360 | 0.532053 |
| 2500 | 2165–2313 | 0.544553 | 0.539413 |
| full | 2165–2313 | 0.544553 | 0.539413 |

Nested outcome-independent evidence hashes; same frozen family/parameters and full-model matched target tasks.2500 requests exceed available fit evidence and plateau at actual counts. At50–100, most shells lack minimum30 support: fallback is safe but completion performance loses frequency, so the cold gate fails. Around500–1000 aggregate benefit emerges; this does **not** identify a universal mature threshold or qualify deployment. Unknown cards retain current A; coverage confidence blends continuously, with no performance-proven production COLD/WARM/MATURE thresholds. [Learning curve](docs/evidence/v2-phase-d/cold-start.json).

## 22. Failure probes

Low-sample2/2 pairs produce zero; empty/missing models/cards retain A; basics are excluded; fitted values remain bounded; no outcomes are fit inputs; soft beliefs normalize. Rare factors require common-card/effective-factor support, and splash shell context is separated from land counts. Controlled determinism and no-label APIs are tested.

Observed failures: insufficient aggregate primary gain, OTJ regression, weak50–250-build completion, residual frequency/color confounding, and outcome/role coverage limitations. Good-stuff correlations are not declared mechanistic synergy. Hybrid soft memberships work mathematically; no human theme labels or universally calibrated certainty are claimed. [Probes](docs/evidence/v2-phase-d/failure-probes.json).

## 23. Goldens

All **40 existing frozen golden regressions pass**, including versioned v1.6/v1.6.1/v1.6.2 reproductions. Existing Uldaros sparse-prior and Unsummon/Amphisbaena cases remain unchanged. FRA has no qualified learned archetype model; experimental A declines, retaining the existing non-neutral L/A/Q/R and exact score. No golden selects parameters.

Eight actual matched-completion cases are frozen **after** selection with card/pool coordinates and scores, covering stronger and weaker fit versus frequency. They retain current role provenance rather than inventing payoff/enabler tags. A research test checks frozen case membership/model scores; no retroactive parameter tuning. Portable case export was corrected to use each family own frozen pool-weight choice; the original aggregate metrics and immutable selection were unaffected. [Case evidence](docs/evidence/v2-phase-d/archetype-regression-cases.json).

## 24. Holdout status

**MH3/DFT NOT CONSUMED.** Primary and cold-start development gates fail, so `final` verifies frozen input hashes and refuses without acquisition, performance reads or a release lock. There is no holdout result. BLB/DSK remain spent and excluded. No completed Phase B/B.1/C artifact was regenerated. [Holdout status](docs/evidence/v2-phase-d/holdout-status.json).

## 25. Performance

Model fitting and calibration controls: 30.121s; representation 21.660s; completion 17.984s; cold curve 19.470s. Eight versioned model artifacts total 23.47 MiB; each stores vocabulary, marginals, factors, prevalence, regularized pairs, evidence count/hash, set/format/version, **no draft rows or outcomes**.

Three-model pick-time inference mean 0.9477ms, P95 1.3786ms; estimated per-candidate 0.0316ms (three families/approximately10 cards, not isolated latency). Contextual replay 642.050s / 84.55 decisions/s; peak 272.36 MiB. Replay includes baseline optimizer; learned inference alone is far cheaper. Initial Python-fit peak memory was not instrumented and is **unavailable**, not estimated as measured. A DMU frozen-fit validation reproduces the model exactly: 0.491s, 64.66 MiB peak. A separately measured500-pick Candidate C benchmark gives 0.4370ms/pick, P95 0.6931ms; marginal scoring costs 0.0369ms per actual offered identity, amortized across3770 candidates. [Measured benchmark](docs/evidence/v2-phase-d/inference-benchmark.json). No production-performance promise follows from these offline timings.

## 26. Limitations

1. Primary development gain is below gate; safe cold fallback still loses completion controls. No qualified model is installed.
2. Composition association is not mechanistic synergy or causal drafting/win improvement; no significance claim.
3. Single observed configuration strengthens support identification but excludes real adaptive/sideboard histories and can bias discovery.
4. Modal observed builds are not final submitted decks; bounded histories and source join qualifications persist.
5. Set/format/player selection bias, shared cards/games and redundant factor/pair evidence remain.
6. Matching tolerances do not remove all Q, rarity, opportunity and inclusion-frequency confounds; rare-card coverage is limited.
7. Reconstruction and half-sample cosine do not prove meaningful human archetypes; k1 is an admissible fallback.
8. Untagged roles are unknown; no authored labels/archetypes are used.
9. Mature Q/metadata snapshots are not verified historical publication-time data.
10. Full-build completion differs from partial draft-pool inference; contextual human agreement and modal association are secondary.
11. Only Premier eight-set models; Quick, unknown sets/cards and all-time/full-dump distributions remain unvalidated.
12. Confidence is regularization/coverage, not calibrated correctness or per-card uncertainty; low-frequency profile log-ratios still need scrutiny despite bounded mapping and shrinkage; original Python-fit memory is unavailable.
13. Uniform frequency gives a stronger control than the original quality-weighted primary control; rejection remains robust.
14. Experimental A-only recomposition freezes optimizer/R/Q/L/D; it is not a rerun whole-engine V2 model.
15. MH3/DFT remain untested, not successful holdouts; no generalization claim from unseen sets.

## 27. Recommendation for next phase

**Retain production v1.6.2 and current A. Do not integrate Candidate C or start the next phase automatically.** If separately authorized, use a new preregistered affinity follow-up to resolve frequency controls, genuinely useful within-shell structure, low-frequency card uncertainty and cold coverage before any new holdout release. Preserve MH3/DFT. Pair outcomes must remain separate from discovery; metadata fallback alone does not make a learned model safe to promote.

Initial-study commands require the exact existing source hashes and a fresh separately identified research workspace; this published selection rejects refitting/retuning. Do not delete the selection or reuse an untouched-release label. No new acquisition is needed.

```powershell
Set-Location 'C:\Users\Graphics0\Documents\Projects\DraftTG'
python -m pip install --target .\data\historical\cache\phase-d\python numpy==2.3.4
dotnet build DraftTG.sln
# Metadata export reuses the existing decoded identity catalog and role classifier.
dotnet .\tools\DraftTG.QualityEvidenceResearch\bin\Debug\net10.0\DraftTG.QualityEvidenceResearch.dll --catalog .\data\historical\normalized\phase-b1-v1\catalog.json --sets DMU,BRO,WOE,LCI,MKM,OTJ,ONE,MOM --splits V2_PHASE_B1_SPLIT_MANIFEST.json --output .\data\historical\normalized\phase-d-v1\unused.json --export-affinity-metadata .\data\historical\normalized\phase-d-v1\metadata.json
# Initial study only; completed workspaces reject the first five stages.
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py qualify
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py representation
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py train
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py completion
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py select
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py diagnostics
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py pairs
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py predict
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py replay
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py cold
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py probes
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py benchmark
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py inference-benchmark
python .\tools\DraftTG.HistoricalEvaluation\test_phase_d.py
dotnet test DraftTG.sln --no-build
# After validation evidence: refuses when the development gate fails.
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py final
# Read-only released result publication:
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py summary
python .\tools\DraftTG.HistoricalEvaluation\phase_d.py report
```

No production authority/scoring change or next-phase action is authorized by a passing future research gate. Raw/build/model/prediction/trace files remain ignored under `data/historical/`; portable evidence is in `docs/evidence/v2-phase-d/`. NumPy2.3.4 and deterministic seeds are pinned. Build/test evidence: clean build, 1297 C# tests, 8 research tests,40 goldens; no failures/skips.
