# Contextual Pick Score V2 — Phase C: Set-Generic Lane / Openness Modeling

## 1. Executive conclusion

**No generic L candidate is qualified for V2 integration. Do not replace production L.** Candidate A is best among the three research families, but development next-3 quality-availability Brier is 0.081258, versus 0.077968 for the actual Phase 9B formula with disjoint derived inputs: **4.22% worse**. It improves contextual agreement and descriptive recovery versus that formula, yet still loses the neutral-L frozen B.1 baseline on contextual Top1/MRR. A contested-color counterexample also exposes a confidence defect.

Production `contextual-pick-score-v1.6.2`, k500, FRA sparse prior, Q/A/D, commitment, tiers, memory, WGC/localization and overlays remain unchanged. All 303 protected files match. MH3/DFT remain untouched; BLB/DSK remain spent. Tested commit `781c4e353ac728355d0633d9768a402d769f8796` plus Phase C working-tree changes on 2026-10-14. [Validation](docs/evidence/v2-phase-c/validation.json).

## 2. Phase C goal

Infer relative color availability from cards observed through the current pick. Quality asks how good a card is; openness asks how unexpectedly late it appears. High quality alone cannot produce a late-surprise signal. The model API consumes `PickTimeState`; future offers, selected-current-card labels, final builds and records are joined only after every prediction.

Preregistered as `25578d5`, using the existing B.1 whole-set split and selected target drafts. A prospective sample-confidence clarification was committed as `781c4e3` before temporal/development analysis. No set-specific archetype rules or black-box model were introduced. [Plan](docs/evidence/v2-phase-c/preregistration.json).

## 3. Current production L audit

`late-color-lane-v1` is already a generic heuristic. For each positively supported colored occurrence: `clip((pick-ALSA)/3,0,1) * clip((k500-adjusted GIH-baseline)/.04,0,1) * clip((pick-3)/5,0,1)`. It splits gold evidence by color count, uses pack weights 1/.5/.25, confidence `min(eligiblePositivePositions/4,1)`, centers evidence over five colors, clips relative support by scale2, and uses the minimum constituent support for gold-card fit. It has no within-pack age decay or explicit negative/absence evidence. P1P1–P1P3 provide zero positive signal.

Premier/Traditional adjustment limit is .020, Quick .010. Production score L divides by the human maximum, so Quick contributes half the Premier lane range. This study replays Premier only; VOW Quick is position-audit evidence, not Quick L integration proof. No production configuration changes.

B.1 snapshots have ALSA/ATA null, so their original L is neutral. We compare that frozen baseline separately from `current-derived`: the **actual** `LaneOpennessProfile.Analyze` and `StatisticalRecommendationEngine` receive global seen means from disjoint evidence drafts, while only their L output is substituted into frozen Q/A/D. Derived averages are explicitly not official provider ALSA. [Implementation](src/DraftTG.RecommendationEngine/LaneOpennessProfile.cs).

## 4. Derived position feature quality

Recomputed **14 calibration/development set-format datasets**, with B.1 target drafts excluded. The saved full Phase B histograms were not used as model inputs because they contain target/self-future positions. Source zero-based coordinates become 1-based **pack-local** positions; global source index remains separate. Offered multiplicity defines Seen; actual picked occurrences define Taken.

Calibration seen-position forecast errors (independent fit/audit halves inside evidence drafts):

| Estimator | Mean card/pack absolute error (picks) |
| --- | --- |
| mean | 2.212780 |
| median | 2.166240 |
| 10% trimmed mean | 2.187770 |

Global empirical CDF CRPS 0.106988 beats pack-specific CDF 0.111829. Median/trimmed mean can improve point MAE; this does not prove superior lane prediction. A/B retain interpretable mean-based families, while C evaluates the full CDF. Taken forecasts are separately audited against held taken positions, with missing training groups explicit. No claim mean is universally best. Same-set mean-position percentiles are recomputed from evidence drafts and checked across independent fit/audit halves; they are descriptors, not lane inputs. [Percentile stability](docs/evidence/v2-phase-c/position-percentile-audit.json).

Per-set/format, pack, sample count, rarity, color and available Q strata are in [seen audit](docs/evidence/v2-phase-c/position-audit.json) and [taken audit](docs/evidence/v2-phase-c/taken-position-audit.json). The neutral-or-lower Q bucket includes neutral estimates for unavailable evidence; older-set/Quick Q is unavailable and cannot support measured-quality inference. Rarity is diagnostic metadata, never a lane bonus; logical identities do not recover every original printing rarity.

## 5. Position confidence

Forecast pooling and signal uncertainty are separate. Calibration CDF CRPS selected zero pooling toward a broad set distribution, preserving card-specific CDFs. Explicit user requirements still require sample-sensitive lane confidence. A prospective clarification therefore fits positive penalties 20/100/400 jointly with temporal policies; every selected family uses:

`c_card = n_independent_evidence_drafts_seen / (n_independent_evidence_drafts_seen + 20)`

Twenty independent seen drafts yield .5, 10,000 would yield .998; 10,000 sightings from repeated cards are not 10,000 independent drafts. Seen/Taken counts, variance and histograms remain visible. Rolling confidence is `N_effective_observed_support/(N_effective_observed_support+4)`, with recency and pack retention applied. This is an evidence-budget heuristic, **not a calibrated posterior that a color is open**. The contested-color probe reveals why that distinction matters. [Clarification](docs/evidence/v2-phase-c/confidence-clarification.json).

## 6. Card-level surprise

A uses positive raw lateness `clip((p-mu_seen)/3,0,1)`. B uses signed standardized lateness `clip((p-mu_seen)/max(1,SD_seen),-1,1)`. C uses signed empirical mid-CDF rank `2*(F_seen(p)-.5*mass_seen(p))-1`. Each is multiplied by card confidence, bounded quality eligibility and a pick ramp. All selected explicit negative weights are zero.

A card with high Q at its expected or earlier position contributes no positive A signal; a weak late card has zero eligibility. C is the percentile of an observed sighting, **not physical-pack survival probability**: original pack IDs, copy identities, initial collation and wheel provenance are unavailable. Missing card expectation contributes no evidence; a missing qualified positional dataset declines the entire experimental substitution and retains existing L.

## 7. Quality gating

`g = clip(2*(frozen_current_raw_Q-.5),0,1)`. This emphasizes above-baseline cards without changing Q. It never uses experimental k250, future target-game outcomes, rarity or final records. It uses intrinsic Q rather than committed-deck relevance, avoiding turning lane inference into a confirmation of already chosen colors.

The k500 evidence snapshots and all Q provenance/sparse-prior contracts remain frozen. These are mature-format replay snapshots, not proven historical publication-time statistics; no chronological availability or live-draft improvement claim is made.

## 8. Negative evidence

Signed B/C variants tested explicit negative coefficients 0 and .25. Calibration chose **0** for every family. We do not turn unseen cards into invented disappearance observations. Lack of fresh qualified evidence causes aging; relative centering can lower unsupported colors. Early negative observations may also consume the per-observation normalization budget even when their explicit accumulator coefficient is zero.

This conservative choice has a demonstrated failure: earlier-than-expected qualified sightings can increase coverage-style confidence while old positive evidence remains. Blue support rises in the controlled probe despite contested-color evidence. All candidates remain unqualified; absence inference and supporting-evidence confidence need repair. [Probes](docs/evidence/v2-phase-c/behavior-probes.json).

## 9. Color aggregation

Per observation, positive/negative color vectors sum confidence × quality eligibility × positional surprise × occurrence quantity × pick ramp. Divide by `max(1,totalPositive+abs(totalNegative))` to bound each observation. Aging banks accumulate vectors; retained evidence is centered over W/U/B/R/G, then `support_c = tanh((evidence_c-meanEvidence)/1.5) * rollingConfidence`, in [-1,1].

This is relative openness: equally supported colors are neutral, not all declared closed. One occurrence cannot provide unbounded evidence. Supporting positions are proxies; repeated sightings are not proven independent physical packs/cards. The coverage-confidence defect prevents treating the accumulator as a Bayesian certainty estimate.

## 10. Multicolor handling

Gold evidence splits evenly and receives an additional .5 discount. Offline per-card L uses the minimum constituent support: `.5+.5*min(colorSupport)`. This prevents a UR card from receiving a strong lane bonus from U alone. Colorless cards and lands contribute no generic color evidence and get neutral experimental L. No archetype/synergy inference or fixing-land lane bonus was added.

## 11. Recency

All selected families use exponential aging `2^(-source_global_pick_gap/16)`. Global source indices preserve actual historical pack lengths rather than inventing 42-pick chronology. Every stored pack bank ages each observation, including gaps. The pick ramp is `clip((packLocalPick-2)/6,0,1)`, so P1P1/P1P2 are neutral and P1P3 is weak.

Temporal fit compared half-lives 4/8/16 on calibration only. The long selected memory helps measured future availability but also contributes to the contested-color failure. Stability/responsiveness tables and reversal labels remain descriptive, not guarantees of real-table inference.

## 12. Pack-direction behavior

P1/P3 are one passing direction; P2 is opposite. Banks are separate by pack. The current bank has weight1; older same-direction banks weight .5; opposite-direction retention tested 0/.25/.5 and selected .5, in addition to global aging. Thus P2 reduces old neighbor certainty, while P3 can reuse a decayed P1 belief. The fitted data did not establish a smaller opposite-direction retention than the fixed same-direction .5. No evidence is carried unchanged across boundaries.

## 13. Candidate A/B/C formulas

| Family | Surprise | Confidence / aging / retention | Explicit negative coefficient |
| --- | --- | --- | --- |
| A | positive raw lateness | n/(n+20); half-life16; old-pack .5 | 0 |
| B | standardized signed lateness | n/(n+20); half-life16; old-pack .5 | 0 |
| C | signed empirical mid-CDF | n/(n+20); half-life16; old-pack .5 | 0 |

Common quality gate, ramp, observation cap, gold policy, centered tanh support, missing-data decline and bounded L conversion apply. With fitted negative coefficients0, B/C accumulate their positive parts. Parameters are shared across sets, not hand-authored by color/archetype. Family parameters were fitted on calibration; family selection uses development primary Brier, not human picks or wins. [Calibration grid](docs/evidence/v2-phase-c/temporal-calibration-corrected.json).

## 14. Offline lane metrics

| Model | Calibration next3 Brier | Development next3 Brier | Next3 picked-quality Brier (all) | Build top-color in top2 (all) |
| --- | --- | --- | --- | --- |
| A | 0.082007 | 0.081258 | 0.131752 | 47.6066% |
| B | 0.082031 | 0.081276 | 0.131725 | 48.0209% |
| C | 0.082305 | 0.081761 | 0.132012 | 48.5406% |
| current-derived | 0.080227 | 0.077968 | 0.129350 | 47.7350% |
| uniform | 0.083652 | 0.084142 | 0.133547 | 39.9240% |
| current-pack-quality-mass | 0.149162 | 0.138148 | 0.190118 | 44.0247% |

Primary: same-pack next3 quality-card color-composition Brier, equally averaged over set, pack and early/mid/late stage. Secondary horizons1/3/5 evaluate future available cards and actual future quality picks separately. Targets normalize bounded quality/color mass; empty or unresolved/noncontiguous horizons decline. No prediction reads those labels. A current-pack quality-composition baseline tests persistence without lane inference.

Build alignment uses top-2 nonland spell-color loads from unique modal observed 40-card builds with >=3 games. Completed-pool labels require an actually complete terminal source inventory. Ties in predicted top color receive fractional credit. Calibration-by-pick reports prediction versus observed mass bins, not certainty of a binary open lane. [All metrics](docs/evidence/v2-phase-c/lane-metrics.json).

## 15. Contextual replay results

| Model | Comparable | Top1 | Top3 | MRR | Normalized rank |
| --- | --- | --- | --- | --- | --- |
| complete-baseline | 50285 | 37.5002% | 72.0811% | 0.577158 | 0.297833 |
| lane-current-derived | 50285 | 37.1125% | 71.6695% | 0.573810 | 0.300718 |
| lane-A | 50285 | 37.3929% | 71.9897% | 0.576355 | 0.298842 |
| lane-B | 50285 | 37.4028% | 71.9976% | 0.576426 | 0.298710 |
| lane-C | 50285 | 37.4147% | 72.0434% | 0.576589 | 0.298539 |

**54,283 decisions / 1,280 drafts; 50,285 complete comparable full-context decisions.** Partial pools, unresolved offers/labels, singleton ranks and unsupported pick15 do not enter comparative metrics. Their earlier valid observed cards remain explicit history; no truncated-pack ranking is counted.

Candidate A versus current-derived L changes 16,761 rankings, improves actual-pick rank in 2,522, regresses in 1,769. It still loses neutral frozen L overall. Human selections are revealed preference, not truth. Q/A/D, weights, R/commitment, optimizer results and sparse priors are copied unchanged; only L and its recomposed score/rank/explanation change. Actual B.1 baseline aggregate metrics reproduce exactly.

Runtime 640.114s; 84.80 decisions/s; peak 270.04 MiB. Engine/adapter/setup timings and optimizer calls are exported. Existing canonical importer/store and opt-in immutable evidence cache are reused. No second scoring engine or optimizer approximation. The reused CLI aggregate retains its legacy outer Q-report version; explicit Phase C L-only identity and format scope are in [execution scope](docs/evidence/v2-phase-c/execution-scope.json). [Replay](docs/evidence/v2-phase-c/contextual-replay.json).

## 16. Set-level results

| Set | A next3 Brier | B | C | Current-derived | A human Top1 | Current human Top1 |
| --- | --- | --- | --- | --- | --- | --- |
| DMU | 0.087607 | 0.087614 | 0.087947 | 0.084779 | 35.0292% | 34.8509% |
| BRO | 0.072705 | 0.072783 | 0.073039 | 0.071572 | 33.7773% | 33.6277% |
| WOE | 0.085709 | 0.085697 | 0.085929 | 0.084331 | 39.7201% | 39.4949% |
| LCI | 0.083459 | 0.083429 | 0.083569 | 0.083511 | 39.4434% | 39.3685% |
| MKM | 0.081677 | 0.081665 | 0.082026 | 0.078617 | 41.6434% | 41.1713% |
| OTJ | 0.084895 | 0.084849 | 0.085300 | 0.081749 | 36.4263% | 36.0897% |
| ONE | 0.080439 | 0.080621 | 0.081779 | 0.072287 | 39.5462% | 38.5042% |
| MOM | 0.075819 | 0.075817 | 0.076130 | 0.073676 | 34.2948% | 34.4307% |

DMU multicolor/domain, BRO artifacts, WOE adventures/bonus sheet, LCI discovery, MKM investigate/List, OTJ bonus sheets/outlaws, ONE toxic/oil and MOM battles/bonus sheet are tested. ONE is a particularly large predictive regression. LCI slightly improves the primary metric, insufficient for the required 4/5 development-set gate. Derived feature availability is set-specific data; formulas have no authored archetype rules. Cold-start zero-data sets and Quick contextual behavior remain unvalidated. Build-color, pack/pick, commitment, lane-confidence and outcome strata are exported separately. Records never train the model.

## 17. Early-lock analysis

| Model | P1P1–3 complete cases | Locks | False locks | False rate |
| --- | --- | --- | --- | --- |
| A | 3826 | 0 | 0 | 0.0000% |
| B | 3826 | 0 | 0 | 0.0000% |
| C | 3826 | 0 | 0 | 0.0000% |
| current-derived | 3826 | 0 | 0 | 0.0000% |

A lock requires confidence>=.5 and leader-runner support gap>=.5; false means the leader is outside eventual top-2 build colors. Sensitivities .25/.5/.75 are exported. Zero at this early threshold is largely **mechanical**: the ramp suppresses early support. It is not proof of universally accurate lane confidence. Eventual-build alignment and calibration curves by pick can grow later without forcing that pattern.

## 18. Recovery/switch analysis

| Model | Reversal episodes | Initially recognized | Recovered by11 | Rate |
| --- | --- | --- | --- | --- |
| A | 1933 | 968 | 493 | 50.9298% |
| B | 1933 | 1001 | 502 | 50.1499% |
| C | 1933 | 1078 | 540 | 50.0928% |
| current-derived | 1933 | 891 | 393 | 44.1077% |

Reversal labels compare quality-availability leaders at picks4–6 versus9–11 within the same pack, with a >=.05 leader margin and different colors. Recovery is conditional on recognizing the old leader at6, then switching to the new leader by11; mean latency is measured from9. These are post-scoring labels, not counterfactual table truth. Conditional denominators differ between models and may select different episodes. Development candidate/current rates are 50.2600% / 42.2348%.

Mean support L1 movement and non-tied leader-switch rates quantify stability versus responsiveness. Controlled reversal, aging, gold/colorless/land and boundary probes pass the intended invariants. The separate contested-blue counterexample fails the desired strong-closure behavior: A blue support 0.366183 -> 0.570977. It is disclosed, not patched by post-development tuning.

## 19. Real/golden regressions

Production Uldaros Q=0.976513757294, exact score=48.219986250158; Unsummon Q=0.578646882534, C=0.944606413994, R=0.102623906706, exact=25.328185285151; Amphisbaena exact=25.501120969070 remains ahead. Missing FRA positional data declines experimental substitution, retaining existing L—not overwriting its non-neutral live lane contribution. This fallback correction affects no qualified replay dataset or fitted parameters. Fixtures never select parameters.

**Build passes, 0 warnings/errors. 1295 C# tests pass, none failed/skipped; 7 Phase C Python tests pass; 40 golden regressions pass.** One existing WGC resource-bound assertion failed in an initial solution-wide run; isolated and final full-suite reruns passed, with no capture code changes. Exact cause is not established. Targeted tests protect indexing, confidence/surprise, aging/boundary/recovery, colorless/gold, no future leakage, held-out protection, complete component isolation, stream ordering and missing-data fallback. [Goldens](docs/evidence/v2-phase-c/golden-reference.json), [validation](docs/evidence/v2-phase-c/validation.json).

## 20. Development selection

**Frozen candidate A; development gate FAIL.** Primary objective is equally weighted next3 availability Brier across development sets/stages. Candidate A improves over uniform but is 4.22% worse than the best reference, current-derived L. It beats all references in only 1/5 development sets. Contextual, early-lock and descriptive recovery gates pass; primary prediction and per-set gates fail. No human agreement or trophy-based retuning followed.

**Why secondary gains do not rescue the position-only candidate:** predicting independent future quality-card availability is the preregistered target. Human agreement measures selected preferences, while recovery is conditional on different initially recognized episodes for each model. Neither establishes better openness prediction. A is only slightly better in LCI and loses in four other development sets, especially ONE. Its gains over current-derived L may reflect a weaker, smoothed lane influence that better imitates observed choices and transitions; that is an interpretation, not a measured causal mechanism. Its contextual Top1/MRR also remain below the frozen neutral-L baseline. Thus better secondary metrics cannot override the primary and per-set failures.

A prospective integrity correction removed one incomplete calibration future horizon. The original calibration is preserved; the corrected pass selects **identical** family/half-life/retention/confidence/negative parameters, before development analysis. Qualified predictions and replay did not change. The zero-pooling versus positive-signal-confidence clarification was preregistered before temporal metrics. [Correction](docs/evidence/v2-phase-c/calibration-correction.json), [immutable selection](docs/evidence/v2-phase-c/selection.json).

## 21. Holdout result if consumed

**NOT CONSUMED.** MH3/DFT remain untouched because the preregistered development gate fails. `final` checks frozen input hashes and records a refusal without reading/downloads or creating a consumed release lock. No holdout performance exists for this phase. BLB/DSK remain spent and unavailable to the runner; metadata/hash preservation is not a fresh performance release. [Status](docs/evidence/v2-phase-c/holdout-status.json).

## 22. Limitations

1. No convincing replacement: predictive regression, only one improving development set, worse contextual results than neutral L, and a contested-color confidence failure.
2. No MH3/DFT final generalization result; formula portability is tested across observed sets, not declared universally proven.
3. Mature Q/position snapshots are not verified publication-time evidence; no chronological/live claim.
4. Repeated sightings/shared packs and games are correlated; no physical card/pack IDs or independent-card significance claim.
5. Empirical sighting CDF is length-biased availability data, not physical-pack survival probability.
6. Coverage-style confidence can inflate certainty from observations lacking positive support. Negative/absence inference is not qualified.
7. Seen and Taken distributions reflect different drafter/player/format selection; rarity diagnostics do not identify every printing.
8. Observed modal builds are not final submitted decks; completed pools and terminal records have explicit coverage gaps.
9. Future quality picks are human choices; future availability can depend on the current actual pick and table autocorrelation.
10. Unknown card expectations decline; missing datasets retain existing L. Quick contextual behavior and cold starts remain unvalidated.
11. Frozen domain pick15, stage assumptions, missing pair/trophy stats and source tie order persist.
12. L-only recomposition freezes R/A/D and optimizer projections; it does not prove whole-engine V2 behavior.
13. Early zero-lock rates are partly built-in safeguards; conditional recovery rates use different recognized-episode denominators.
14. No new independent holdout or causal draft-win benefit is claimed.

## 23. Recommendation for Phase D

**Retain existing Phase 9B production lane behavior and all frozen components. Candidate A is a research reference, not an integration definition.** Keep the existing positive-only safeguard, early ramp and conservative gold fit as controls; the current-with-derived-input hybrid wins the lane-specific objective but itself loses contextual agreement versus neutral L, so injecting derived ALSA into production is also unqualified.

Only if separately authorized, recommend a **preregistered offline Phase D affinity/A study**, keeping current production Q/L/D/commitment frozen and MH3/DFT untouched. Carry the lane confidence/contestation defect as an explicit blocker for later V2 composition. A lane follow-up must repair supporting-evidence confidence and validate value mapping on development before any final release; do not silently tune this frozen selection. **Phase D is not started.**

The research is complete. Do not rerun acquisition, position derivation, calibration, predictions, contextual replay, candidate selection, controlled probes or holdout evaluation in this workspace. Published results can be displayed without reevaluation:

```powershell
python .\tools\DraftTG.HistoricalEvaluation\phase_c.py summary
python .\tools\DraftTG.HistoricalEvaluation\phase_c.py report
```

Task-local dependencies and raw/SQLite/prediction/observer files remain ignored under `data/historical/`; small source/calibration/metric artifacts are under `docs/evidence/v2-phase-c/`. Source hashes, source membership, fit/audit seeds, original/corrected calibration, selection hashes and build identity are retained. Zero provider aggregate requests or new holdout downloads occurred.
