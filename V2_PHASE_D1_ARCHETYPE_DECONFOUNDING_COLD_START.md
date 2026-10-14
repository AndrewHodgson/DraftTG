# Contextual Pick Score V2 — Phase D.1: Archetype Deconfounding, Uncertainty & Cold Start

## 1. Executive conclusion

**No D.1 candidate qualifies for future V2 integration. Retain production v1.6.2 and current A.** Stronger primary matching has inadequate coverage in four development sets and zero tasks in MKM. The observed four-set C MRR equals tied color-only at0.456667, with0% gain; this is **not a complete five-set primary result**. All shell utility weights and the calibrated cold envelope are0. C is safe refusal, not proof of strategic benefit.

Residual factor stability is weak, uncertainty has no useful positive-confidence test population, and the metadata prior cannot qualify. MH3/DFT remain untouched. The full A-only historical replay still evaluates all three frozen families at the existing scale. Production Q/L/A/D/R/commitment, k500/FRA prior, tiers, optimizer, Phase9F/localization/overlay and all Phase D artifacts remain unchanged. [Gate](docs/evidence/v2-phase-d1/development-gate.json), [summary](docs/evidence/v2-phase-d1/summary.json).

## 2. Phase D failure diagnosis

Phase D C improved matched development MRR1.7464% over its weaker quality-pool frequency control, below2%; only0.9529% over the stronger uniform control. Its contextual agreement gains were secondary. Raw shell/factor inclusion still mixed popularity/power with relationships, cold50–250 performance was weak, and coverage confidence did not establish correctness. OTJ regressed.

D.1 separates generic inclusion, residual shell/factor structure and residual pairs. This is a new namespace, not a relabeling of D C as qualified. The stronger D.1 task exposes a new evaluation failure: simultaneously matching colors/type/Q/mana/support/probability and demanding four distractors is usually infeasible. Thresholds were **not relaxed** to improve the result.

## 3. Preregistration

Committed as **`7c851c8`** before performance analysis, with a prospective coverage clarification **`68efed1`** after baseline-only qualification and before residual candidate fitting. Calibration DMU/BRO/WOE; development LCI/MKM/OTJ/ONE/MOM; untouched MH3/DFT; spent BLB/DSK. Reuses frozen D17,706 fit /4,340 audit /21,806 target builds. Audit hashes split utility and confidence; targets never fit models, mapping or utility.

All 399 protected prior/source/model files match. Tested commit `68efed1518eb74751b33553c10af7116be35fb20` plus new D1 working-tree files. Data attribution/atomic joins/bounded older-prefix caveats remain in [Phase B source audit](docs/evidence/v2-phase-b/source-audit.json); no acquisition/provider request. Modal observed40 builds are not proven final submitted decks. [Plan](docs/evidence/v2-phase-d1/preregistration.json), [coverage clarification](docs/evidence/v2-phase-d1/coverage-clarification.json).

## 4. Strong generic inclusion baseline

An18-feature ridge-binomial logistic model explicitly predicts inclusion from global/shell frequency, frozen Q, mana/missingness, color compatibility, creature/spell/artifact/land type and assigned-role/missingness flags. Coefficients fit only calibration structures; ridge1/10/100 is selected by independent calibration audit log loss. Selected ridge **100**. No rarity, wins, target builds/picks or future outcomes are features.

| Generic control | Primary MRR (available dev sets) | Legacy D diagnostic MRR |
| --- | --- | --- |
| color-only | 0.456667 | 0.456667 |
| uniform-frequency | 0.331293 | 0.538653 |
| quality-frequency | 0.329421 | 0.539067 |
| frequency-Q | 0.305888 | 0.532340 |
| frequency-Q-mana | 0.409866 | 0.534800 |
| generic-logistic | 0.353203 | 0.539573 |

Primary strongest control is **color-only**, tied0.456667 on the small available set population. On the original D500-task/set diagnostic, **generic-logistic** wins at 0.539573. Both control competitions were frozen **before residual candidates**. Different tasks explain the ranking; do not transport the tiny primary winner into a claim about the broader corpus. [Control freeze](docs/evidence/v2-phase-d1/baseline-freeze.json).

## 5. Frequency/Q deconfounding

Global/card-shell inclusion counts are disjoint fit-build marginals;18 interpretable coefficients explain their generic relationship with Q, mana and types. Assigned removal/fixing are lower bounds and unassigned indicators are explicit unknowns, not proven absences. All4,821 cached card records receive raw type-availability metadata; missing mana remains flagged.

Baseline probability is an observational inclusion estimate, not causal power. Eligible-build exposure is **not true draft offering opportunity**; actual pack exposure for game-only drafts is unavailable. Strength matching and within-shell conditioning reduce, but cannot eliminate, selection/playability bias. Frozen mature Q is not verified pick-date historical availability. Generic probability enters completion controls; it is **never copied into experimental A**.

## 6. Residual archetype representation

Shell residual is a shrunk log-odds difference between observed inclusion and generic expected inclusion. Binary presence is appropriate for inclusion likelihood; basic lands are excluded and lands are descriptive/fixing data. Sqrt counts capped3 are retained for pool evidence, not misrepresented as the likelihood target.

Factor training uses centered standardized inclusion residuals around fixed generic-plus-shrunk shell bias. Compare small-rank logistic offset factorization versus positive/negative-channel residual NMF; no neural model. The fitted signed coordinates are continuous, not probabilities of named archetypes. No hand-authored themes, labels or OTJ branches.

## 7. D1-A/B/C definitions

**A:** card-uncertainty-shrunk shell logit residual only. **B:** A plus support/stability-shrunk latent contribution. **C:** B plus .25-weighted residual pair average, multiplied by disjoint shell utility and monotonic cold weight. All use `.5+.25*tanh(raw)` for learned A fit and blend toward original A.

Calibration selects **residual-nmf, rank2** by masked-card generic-offset log loss, then pool weighting by the same calibration-only objective. All families select sqrt-capped3. This formulation choice does not imply factor utility or integration qualification. [Fit grid](docs/evidence/v2-phase-d1/model-fit.json).

## 8. Shell utility gating

For each shell, disjoint utility-audit completion compares B with the frozen strongest primary control. `lower = mean paired MRR gain − .84*SE`; require>=10 independent draft tasks; `utility = clip(lower/.02,0,1)`. Missing/weak data gets0. Also report masked-card log loss. Stable reconstruction alone cannot bypass this gate.

**Every measured shell utility is0.** This combines sparse utility-task coverage and lack of positive supported completion lift. No model artifact automatically creates confidence. Global refusal is not proof that OTJ uniquely lacks strategy. [Utility evidence](docs/evidence/v2-phase-d1/shell-utility.json).

## 9. OTJ forensic analysis

Compare all sets only after parameter freeze: shell counts/prevalence, factor stability/entropy, frequency-Q correlation, mean inclusion, pair density/magnitude, splashes, multicolor, assigned fixing, bonus-printing proxy, build diversity, role coverage and distractor coverage. OTJ has364 strict target tasks, while LCI/ONE have4 each, MOM55 and MKM0. Thus strict primary coverage concentrates in OTJ; it cannot represent five-set robustness.

The D OTJ raw co-occurrence regression is consistent with generic frequency/power and broader/splash/bonus-card populations obscuring incremental relationships, but these descriptive covariates **do not establish a causal root cause**. In D.1 the identifiable causes of refusal are unsupported shell utility, weak residual stability and missing confidence calibration. No set-name patch was added. [Forensics](docs/evidence/v2-phase-d1/otj-diagnostics.json), [component ablations](docs/evidence/v2-phase-d1/ablations.json).

## 10. Card uncertainty

For support `c`, shell n and generic probability g: `p*=(c+50g)/(n+50)`; `delta=clip(logit(p*)−logit(g),−2,2)`; `SE=sqrt(1/max(c+50g,1)+1/max(n−c+50(1−g),1))`. Card reliability is `c/(c+50)*abs(delta)/(abs(delta)+1.64SE)` times matching signs in two evidence halves. Zero support gives0; tiny denominators cannot produce unbounded log ratios.

If observed inclusion equals generic expectation, residual/reliability are0, even for a strong popular card. A20-observation card has at most20/70 support budget before other gates. This is estimator shrinkage/sign consistency, **not calibrated probability of affinity correctness**.

## 11. Factor uncertainty

Loadings are matched between deterministic half-samples by maximum absolute cosine with sign/permutation alignment. Below.8, factor contribution is0; otherwise multiply by cosine and `n_shell/(n_shell+100)`. Card loadings additionally shrink `c/(c+50)` and require20 inclusions.

Only **2 of102 shells** meet the stability threshold. Most residual structures that remain after generic bias removal do not reproduce strongly enough in these samples. The two mathematically stable cases still have zero completion utility. Signed loading/activation statistics are descriptive; they do not identify human archetypes or certainty.

## 12. Pair uncertainty

Retain expected>=5 and both marginals>=20; positive observed>=5. For low-observed negatives additionally expected>=20/marginals>=30. `raw=clip(log((observed+5)/(expected+5)),−1,1)`; `SElog=sqrt(sum p_pair*(1−p_pair))/(expected+5)`; confidence is half-sign consistency × `expected/(expected+50)` × `max(0,1−1.64SElog/abs(raw))`. Effect is raw×confidence.

2/2 is exactly0; opposite resample signs suppress the estimate. Pair contribution is only.25 of the pool-weighted residual average and then receives utility/cold gating. This approximate SE is not an independent-pair significance guarantee.

## 13. Pair deconfounding

Artifacts retain observed count, generic-only expected co-occurrence and generic-plus-latent expected count separately. The conditional latent expectation uses the fixed generic bias plus shrunk shell offset and fitted residual coordinates. Pair effect measures excess beyond that shared structure.

Generic popularity is baseline, shared strategy is factor, remaining pair relationship is residual; the three are not summed as independent popularity votes. Unstable/poorly identified latent expectations remain a limitation, and C utility/cold gates refuse their scoring contribution. No pair outcome WR enters fit or A.

## 14. Cold-start blend

`A_final = current_A + w*(learned_fit-current_A)`. Pool budget `n_pool/(n_pool+8)`, card/shell support, residual reliability, factor stability, shell utility and calibrated cold envelope all constrain learned contribution. Completion forecasts use the strongest generic control plus gated residual; zero residual exactly preserves that control. Contextual A receives **only residual fit**, not generic playability.

Nested calibration50/100/250/500/1000/full selects safe alpha0/.25/.5/.75/1 only if every calibration confidence population has support and no mean loss. Lower monotone envelope/interpolation in log evidence count never exceeds a calibrated safe weight. All selected weights are0 because DMU confidence tasks are absent. This is safe deferral, not solved cold-start prediction. [Calibration](docs/evidence/v2-phase-d1/confidence-calibration.json).

## 15. Optional metadata prior

Train a weak nine-feature type/mana/assigned-role covariance prior only on calibration structures, centered within set/shell. No Q outcome bonus, rarity, set mechanics or NLP. Compare bounded alpha0/.05/.10 against frozen controls on calibration confidence and development tasks.

**Not adopted.** Missing calibration confidence coverage prevents establishing safe positive contribution. It is rejected for qualification, not declared intrinsically useless. No metadata score is inserted into any contextual candidate. [Prior evidence](docs/evidence/v2-phase-d1/metadata-prior.json).

## 16. Completion task

One hash-selected masked nonland identity per target build; remove all copies; four absent distractors with identical colors and creature/noncreature class, mana within1, frozen Q within.05, generic probability ratio.75–1.3333 and support ratio.5–2. True cards require20 fit inclusions. At most500/set, require>=300 in **every** development set.

| Set | Calibration/development | Strict primary tasks | Legacy diagnostic tasks |
| --- | --- | --- | --- |
| DMU | calibration | 7 | 500 |
| BRO | calibration | 74 | 500 |
| WOE | calibration | 57 | 500 |
| LCI | development | 4 | 500 |
| MKM | development | 0 | 500 |
| OTJ | development | 364 | 500 |
| ONE | development | 4 | 500 |
| MOM | development | 55 | 500 |

Thresholds/four distractors remain unchanged after sparse coverage appeared. Existing D500-task/set matches are separate nongating diagnostics, never parameter selection or a replacement primary success claim. Missing metrics remain unavailable.

## 17. Strong baseline results

Available development primary C MRR0.456667 equals tied color-only0.456667;0% gain,0 improving/0 regressing available sets. MKM is unavailable; four sets under300 mean the complete primary gate **FAILS regardless of gain**. Ungated A/B available macro MRR0.352478 lose the primary tied control.

On the broader preserved D tasks, generic-logistic MRR0.539573 exceeds uniform-frequency0.538653 and quality-frequency0.539067. C refusal stays at color-only0.456667 on that diagnostic; it does not reproduce D C strategic/composition gains. Different matched populations/control definitions must remain separate. [Completion](docs/evidence/v2-phase-d1/completion.json).

## 18. Uncertainty calibration

| Bin | Tasks | Mean confidence | Completion lift | Residual Brier |
| --- | --- | --- | --- | --- |
| 0 | 427 | 0.000000 | 0.000000 | 0.250000 |
| 1 | 0 | unavailable | unavailable | unavailable |
| 2 | 0 | unavailable | unavailable | unavailable |
| 3 | 0 | unavailable | unavailable | unavailable |
| 4 | 0 | unavailable | unavailable | unavailable |

Weighted lift calibration error 0.000000, downward violations 0, positive-confidence tasks **0**. Empty positive bins and zero learned contribution make zero error **uninformative**. Required>=100 positive tasks is unmet, so uncertainty calibration FAILS. No probability-of-correctness claim. Post-scoring inclusion association is additionally stratified by confidence; zero-bin labels do not validate higher-confidence behavior.

## 19. Pool-to-archetype inference

Current PoolBeforePick alone determines soft shell belief and signed residual factor coordinates. Positive pool observations update a ridge-regularized logistic latent vector; absence of a not-yet-drafted card is not treated as negative pick-time evidence. Candidate identities are scored against the existing state and cannot teach it.

Uniform, sqrt-capped3 and modest frozen-Q pool weighting were compared on calibration masked log loss; sqrt wins. No D optimizer/commitment/R feature is used. Hybrid beliefs remain soft, splash spell loads preserve base color context, and empty/missing evidence retains original A. Only mathematical mechanics are demonstrated here; useful high-confidence/off-archetype separation remains unqualified.

## 20. Contextual replay

54,283 historical decisions /1,280 drafts; **50,285** complete comparable decisions. Reuses the unmodified B.1/C/D actual production runner and A-only helper; Q/L/D/R/weights/priors/optimizer projections remain copied. Baseline aggregates reproduce Phase D exactly.

| Model | Top1 | Top3 | MRR | Normalized rank |
| --- | --- | --- | --- | --- |
| current A | 37.5002% | 72.0811% | 0.577158 | 0.297833 |
| D1-A | 37.5042% | 72.0871% | 0.577214 | 0.297692 |
| D1-B | 37.5042% | 72.0871% | 0.577214 | 0.297692 |
| D1-C | 37.5002% | 72.0811% | 0.577158 | 0.297833 |

Selected C changed 0 orderings, improved 0, regressed 0. With confidence0, exact current-A replay equality is expected. A/B remain unqualified diagnostic ablations. Raw helper score labels retain D version because its source is frozen; new predictions/evidence hashes and explicit D1 [execution scope](docs/evidence/v2-phase-d1/execution-scope.json) identify this study. No D replay artifact was overwritten.

## 21. Build-inclusion association

33,631 complete replay decisions have qualified target modal-build labels. Compare current A, preserved D-C and D1-A/B/C without rerunning D.

| Model | Included occurrences/rank | Not-included occurrences/rank |
| --- | --- | --- |
| current | 44693/0.316917 | 229139/0.535710 |
| Phase-D-C | 44693/0.291884 | 229139/0.540593 |
| A | 44693/0.316779 | 229139/0.535737 |
| B | 44693/0.316779 | 229139/0.535737 |
| C | 44693/0.316917 | 229139/0.535710 |

Generic probability, Q, shell, stage/pick/pack, commitment, confidence and utility strata are in [association evidence](docs/evidence/v2-phase-d1/build-association.json). Labels join only after inference. Unchanged C association is fallback, not a learned benefit; observed modes are not final-deck truth.

## 22. Learning curves

| Requested builds | C observed MRR | Frozen primary control | Positive cold confidence |
| --- | --- | --- | --- |
| 50 | 0.456667 | 0.456667 | 0 |
| 100 | 0.456667 | 0.456667 | 0 |
| 250 | 0.456667 | 0.456667 | 0 |
| 500 | 0.456667 | 0.456667 | 0 |
| 1000 | 0.456667 | 0.456667 | 0 |
| full | 0.456667 | 0.456667 | 0 |

All observed C learning points equal fallback. MKM remains missing, not a zero/error-free observation; per-shell support buckets and achieved full sizes are preserved. Therefore early safety is shown on observed tasks, but complete cold coverage and required positive1000/full benefit fail. This is build-evidence cold simulation with mature Q metadata, not a chronological brand-new-set backtest. [Curves](docs/evidence/v2-phase-d1/cold.json).

## 23. Failure probes

Passed prescribed arithmetic/safety checks: explained-popularity residual0, low-support shrinkage,2/2 pair0, sign-conflict suppression, false-factor/no-utility fallback, unstable factor suppression, bounded fit/confidence, candidate-independent state, soft hybrids, splash shell preservation and exact empty/missing fallback.

**Not demonstrated:** useful high-confidence calibration, active true multi-archetype preference, or off-archetype same-color separation for the selected C. All learned C contribution is suppressed. Most factors are unstable; no positive utility or metadata prior can qualify. Do not equate architectural capacity and safe refusal with successful strategy discrimination. [Probes](docs/evidence/v2-phase-d1/failure-probes.json).

## 24. Set-by-set results

| Set | Primary C MRR | Primary strongest control | D-C old-task MRR | D1-C legacy diagnostic MRR |
| --- | --- | --- | --- | --- |
| DMU | 0.456667 | 0.456667 | 0.594700 | 0.456667 |
| BRO | 0.456667 | 0.456667 | 0.607067 | 0.456667 |
| WOE | 0.456667 | 0.456667 | 0.560733 | 0.456667 |
| LCI | 0.456667 | 0.456667 | 0.540500 | 0.456667 |
| MKM | unavailable | unavailable | 0.523233 | 0.456667 |
| OTJ | 0.456667 | 0.456667 | 0.527533 | 0.456667 |
| ONE | 0.456667 | 0.456667 | 0.584867 | 0.456667 |
| MOM | 0.456667 | 0.456667 | 0.546633 | 0.456667 |

D-C and D1 legacy rows share original D tasks but new C uses the frozen D1 primary fallback, tied color-only. OTJ D-C0.527533 becomes refused0.456667 on this nongating ranking task, while current-A contextual ranks remain exact. Thus D1 reduces risky contribution but **does not prove OTJ predictive improvement**. Every set uses identical rules; zero active evidence is global, not OTJ-specific detection.

## 25. Goldens

All **40** existing production goldens pass, including versioned1.6/1.6.1/1.6.2, Uldaros sparse quality and Unsummon/Amphisbaena commitment. Missing FRA learned evidence leaves current A/Q/L/R and exact scores unchanged. No golden tuned parameters. Existing D C# tests already protect actual A-only recomposition, reference-identical frozen components and fallback; they were reused rather than duplicated.

1297 C# tests and10 grouped D1 research tests pass, no failures/skips. Build0 warnings/errors. [Validation](docs/evidence/v2-phase-d1/validation.json).

## 26. Holdout status/result

**MH3/DFT not consumed. No holdout results.** Primary, per-set, coverage, cold and uncertainty gates fail; `final` checks frozen selection and refuses without downloading/reading performance or creating a release lock. BLB/DSK remain spent. There is no passing final release or integration qualification. [Status](docs/evidence/v2-phase-d1/holdout-status.json).

## 27. Performance

Generic fitting 2.184s; residual grid/pool selection 501.201s, measured peak 114.52 MiB. Eight inference models total **9.00 MiB**; per-set bytes below. Source-cache/formulation intermediates remain ignored. Models contain only frequencies/generic coefficients, residual loadings/pairs, uncertainties/utility/evidence hashes—no raw drafts or outcome rows.

| Set | Model MiB |
| --- | --- |
| DMU | 1.048 |
| BRO | 1.278 |
| WOE | 1.195 |
| LCI | 1.096 |
| MKM | 0.986 |
| OTJ | 1.283 |
| ONE | 0.996 |
| MOM | 1.114 |

Measured500-pick C inference mean 3.6751ms, P95 4.2473ms; amortized candidate scoring 0.0614ms across 3,770 actual identities. Replay 1261.879s /43.02 decisions/s, peak 270.42 MiB. Fast inference does not compensate for invalid/unqualified evidence.

## 28. Limitations

1. Strict matching leaves only one adequately covered development set; MKM has no tasks. Available-set MRR is not a complete primary result.
2. No validated incremental strategic contribution; C is zero-weight refusal, A/B lose strict controls.
3. Residual factor instability and unsupported utility/calibration limit confident inference.
4. Safe cold equality does not establish positive mature benefit or useful new-set affinity.
5. Generic inclusion/popularity/Q proxies do not identify causal synergy, playability or true card offering opportunity.
6. Matching selection can bias the small task population; its strongest control differs from the broad D task.
7. Reconstruction log loss selected formulation but cannot replace primary completion or held utility.
8. Role assignments are lower bounds; logical identity does not prove exact printing/bonus-sheet provenance.
9. Modal single-configuration builds are not final submissions; bounded histories/source join/player selection biases persist.
10. Mature metadata/Q do not prove chronological evidence availability; no draft-win/significance claim.
11. C1 positive-confidence inclusion reliability cannot be assessed because active predictions are absent.
12. Pair SE/resample consistency is heuristic; factor expectation instability remains.
13. No Quick, unknown-format, live or MH3/DFT performance test.
14. A-only recomposition is isolated, not a whole-engine V2 integration.
15. Initial baseline null-metric aggregation and list-vs-array log-loss implementation errors were corrected before selection: saved controls/tasks/model caches were reused, matching/coefficients/formulas were not retuned. A premature utility invocation declined before fit completion and changed no artifact.

## 29. Recommendation

**Retain current production A and v1.6.2. D.1 is not qualified; do not integrate any candidate or start another phase automatically.** A separately preregistered evaluation-design follow-up should first demonstrate adequate deconfounded task coverage—possibly pairwise/stratified tasks with explicitly planned support—before fitting confidence or claiming useful latent structure. Keep strongest controls, held utility and safe refusal. Preserve MH3/DFT. Do not silently relax/relabel this completed primary task.

Initial-study commands require identical frozen source bytes in a fresh separately identified workspace. Published selection prevents initial refits; completed evidence prevents expensive reruns. Read-only `summary`/`report` are the normal commands after release.

```powershell
Set-Location 'C:\Users\Graphics0\Documents\Projects\DraftTG'
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py verify
# Reuse task-local NumPy2.3.4 installed in Phase D; no acquisition.
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py qualify
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py baseline
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py fit
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py utility
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py calibrate
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py completion
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py select
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py cold
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py metadata-prior
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py ablate
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py otj
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py probes
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py benchmark
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py predict
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py replay
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py associate
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
python .\tools\DraftTG.HistoricalEvaluation\test_phase_d1.py
# Refuses holdout release unless every gate passes and proper release inputs exist.
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py final
# Read saved results without new analysis:
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py summary
python .\tools\DraftTG.HistoricalEvaluation\phase_d1.py report
```

Build/TRX/golden validation is exported by `validate` after the documented logs/results exist. Portable evidence lives under `docs/evidence/v2-phase-d1/`; larger models/tasks/predictions/traces remain ignored under `data/historical/normalized/phase-d1` and `data/historical/evaluation/phase-d1`. No production implementation or next phase is authorized by this research.
