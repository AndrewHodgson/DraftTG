# Contextual Pick Score V2 — Phase F: Marginal Deck Value, Castability and Replacement

## 1. Executive conclusion

**Retain production `contextual-pick-score-v1.6.2` D and the current deck builder. Phase F does not qualify for integration.** The selected family is F-C; all original and corrected results are retained. The corrected input evaluation uses the same frozen candidate parameters and the actual production-compatible Q lookup. It does not refit E-C, reselect a family or rerun the production recommendation engine.

MH3/DFT remain untouched; BLB/DSK remain spent. No production Q/L/A/D/R/commitment/weights/k500/FRA/tier/builder/Phase9F/localization/overlay source was changed. No next phase is started. Full gate result follows.

## 2. Phase E failure diagnosis

Phase E improved final-build inclusion forecasts but lost marginal discrimination against the generic control and materially degraded contextual agreement. Observed-build probability answers which picked cards were played; it does not specify the displaced occurrence, utility change, castability or mana benefit. Centering low inclusion against low generic inclusion can leave an uncastable card near neutral. Phase F replaces that residual semantics with explicit before/after utility and an absolute eligibility gate.

A separate F input bug was detected: exact-full-display-name neutralization incorrectly discarded valid front-face/adventure aliases. The original run affected148 identities/15,517 offered occurrences, max Q difference0.3975555. It is preserved in the original namespace. The corrected-input run restores frozen Q and its proven neutral exception, with exactly the original selected parameters/formulas; compatibility is measured against every actual baseline occurrence. Original calibration used the faulty transport, so these are **fixed-parameter corrected-input evaluations**, not a claim of optimal recalibration on corrected data. [Original audit](docs/evidence/v2-phase-f/Q-compatibility.json), [correction registration](docs/evidence/v2-phase-f/corrected-inputs/correction-registration.json).

## 3. Preregistration

Committed prospectively as **`01443ee`**. Calibration DMU/BRO/WOE; development LCI/MKM/OTJ/ONE/MOM. Use the Phase E exact source/draft joins, source qualification, whole-draft partitions and unchanged matching. All3,708 qualified drafts/154,266 supported decisions remain registered; no acquisition or requalification occurred. Calibration uses only the three calibration-validation groups. Every family is selected independently over scale .05/.10, structure weight .02/.04, virtual baseline .45/.50/.55; ties prefer smaller structure weight, larger scale and baseline nearer .5. Family selection uses calibration pair accuracy only. Parameters freeze before development/probes.

Primary requirements: ≥2pp macro pair gain over both current D and generic, ≥4/5 improved, worst loss≤2pp; ≥500 fully experimental pairs/100 drafts each set; reconstruction ≥1pp F1/≥.5pp overlap over current/E-C, ≥4/5 improved, complete40-card integrity, bounded color/land regression; all structural/castability probes; contextual Top1 loss≤.0025 and MRR loss≤.002 overall **and each development set**; cold sensitivity loss≤.005; goldens/compatibility. [Plan](docs/evidence/v2-phase-f/preregistration.json).

## 4. Production D audit

Production remains the unchanged Phase10 projection/cut-line implementation documented in [Phase E](V2_PHASE_E_FINAL23_DECK_FIT_RESEARCH.md). It blends viable two-color projections, real/future replacement margins and bounded deficit-relative creature/early/removal need minus top-end redundancy. Owned output normally targets23 spells/17 lands. Existing future supply, provisional FRA offsets, R and smooth stage weights remain unchanged. It is a heuristic D score, not a calibrated cast probability or guaranteed final submitted deck. Phase F reads its exact saved scores and uses its real calibration function for D-only recomposition; it does not alter the application.

## 5. Marginal deck-value definition

For legal pool P and candidate c, compute B=Build(P), A=Build(P+c). Compare selected **multisets**, not identity presence. Report candidate-copy selection, added/removed copies, pair/land changes, cast-adjusted replacement, retained-card mana gain, structure, future-slot comparison, confidence and bounded D. The frozen E-C probability may attenuate confidence by at most15%; it never changes the sign of raw improvement or absolute eligibility. Final-pool reconstruction/paired discrimination are explicit hindsight tasks; completed pools and observed builds never enter historical pick-time workers.

## 6. Replacement modeling

Removed[i]=max(0,B[i]−A[i]); Added[i]=max(0,A[i]−B[i]). CandidateSelected iff its after selected quantity exceeds its before quantity; land selection is separate. ReplacementValue=ΣAdded·Q_i·CastAfter_i−ΣRemoved·Q_i·CastBefore_i−V_future·(ΣAdded−ΣRemoved). This records multi-card/pair changes, not an invented scalar displaced identity. RetainedManaDelta=Σmin(A_i,B_i)·Q_i·(CastAfter_i−CastBefore_i). Together these form the complete cast-adjusted utility difference, apart from the explicit virtual slot term. No Q is added again as an independent positive bonus.

V_future=clip(base+.05*tanh((remaining*.8/max(1,openSlots)−1)/3),.35,.65), remaining=max(0,42−completedPicks−1); no remaining picks uses emergency .35 only for genuine open slots. Owned removals retain exact identities/quantities. The inherited42-pick budget remains provisional for39/45-pick source drafts.

## 7. Castability model

For N=40, S known sources, n seen cards, k required sources: H(S,n,k)=Σ[j≥k] C(S,j)C(40−S,n−j)/C(40,n). Draws on play=7+turn−1; turn is primary MV clipped2–8. Report on-curve tails and by-turn+2 tails. Adequacy=min(H(totalLands,n,MV), each required color/colorless tail). CastScore=clip(adequacy/.8,0,1).

This **minimum of marginal tails is an adequacy proxy, not exact joint casting probability**. Shared dual-land allocation, tapped tempo, mulligans, extra draws, prototype/alternate casting, activated restrictions and conditional mana remain unqualified. Plain generic/WUBRG/C costs are supported; hybrid/Phyrexian/X/special costs decline to production D. Primary-face scope is explicit. No silent generic-cost equivalence is used.

## 8. Mana-base model

Enumerate15–19 lands for sufficiently large eligible shells; early projections use17. Known direct drafted sources cap4 nonbasic occurrences. Main-color basics follow selected pip demand, with minimum6 per used main color when possible and largest-remainder allocation; shortages relax the minimum visibly. At most one splash basic. Record sources[W,U,B,R,G,C], selected drafted sources, basic quantities, effective fixing, land count and unmet source adequacy. Colorless sources receive credit only from known direct lands when relevant. Indirect/fetch/faced land sources are not silently granted.

Structured produced_mana lists potential source colors, not proof of unrestricted simultaneous production. Conditional restrictions remain a limitation. [Source/shortfall audit](docs/evidence/v2-phase-f/corrected-inputs/mana-source-audit.json).

## 9. Fixing value

A fixer has positive F-C value only if selected and RetainedManaDelta>0. Its replacement term is zero; it is never compared with a spell cut line. FixingDelta equals that retained cast-adjusted value gain. Abstract fixing tags do not earn a bonus. F-A/B have no land-benefit estimator and explicitly retain production D for land candidates.

The fixing-need probe fails in F-C: minimum marginal tails can be capped by total-land probability before the color-source channel becomes the minimum. Extra colored sources then receive zero incremental value although joint colored availability can improve. This is a model limitation, not a passed fixing mechanism. No post-probe change to the formula was made.

## 10. Structural needs

Use global potential differences, with weight w: creature=−w*(max(0,14−C)^2/14+max(0,C−18)^2/6); early=−w*max(0,4−E)^2/4; top=−w*max(0,H−3)^2/4; removal=−w*max(0,3−R)^2/3. Broad curve support adds −.2w*(max(0,6−MV3)^2/6+max(0,4−MV4)^2/4). StructureDelta=PotentialAfter−PotentialBefore. These give diminishing/negative marginal returns rather than linear role rewards. Early positive support requires reliable creature/broad-main-deck metadata; unknown narrow noncreatures receive no positive applied structural bonus. Children of the structural audit are diagnostic potential differences; the applied StructuralDelta is clipped when narrow eligibility is unknown.

## 11. Role saturation

Reuse the existing precision-first role classifier and positive role labels. Untagged removal is unknown, not proven absent. Broad main-deck eligibility is conservatively available for creature bodies; unknown noncreatures get no optional positive need. Controlled removal fixtures explicitly declare qualified broad-removal evidence; they do not create a historical sideboard classifier. Ramp/card-advantage counts are diagnostics, not fitted linear bonuses. Role/cost/narrow missingness remains visible in metadata and declines. [Qualified role occurrences](docs/evidence/v2-phase-f/corrected-inputs/mana-source-audit.json).

## 12. Builder F

Deterministic greedy marginal cast-adjusted utility plus diminishing potential; one local replacement pass over top4 bench/bottom4 starter options. Consider the three pool-supported main pairs, preserve the previous pair when within .02 total utility, and report soft plausibility with temperature .25. IDs only break equivalent occurrence ties; no set-specific/card-ID model is trained. Conservative splash: one outside color/one pip, Q≥.65, ≥2 direct drafted sources, ≤2 splash spell copies, ≤1 splash basic, and no base-card adequacy loss>.04.

**Terminal integrity failure:** virtual open slots remained valued when comparing terminal incomplete plans to available complete plans. Lower-land partial plans could win the objective. Invalid/declined predictions are scored as empty reconstruction failures, not partial high-overlap success. The frozen design was not patched after development. This should be corrected in a separately preregistered follow-up before any integration.

## 13. F-A/B/C definitions

| Family | Scale | Structure weight | Virtual baseline | Original calibration accuracy |
| --- | --- | --- | --- | --- |
| A | 0.1 | 0.04 | 0.5 | 0.559988 |
| B | 0.1 | 0.04 | 0.5 | 0.555827 |
| C | 0.05 | 0.02 | 0.5 | 0.577716 |

F-A raw value is replacement only with absolute selected/castability gating. F-B adds applied structure delta, without learned probability. F-C adds retained mana/fixing delta and frozen E-C confidence support. All use the same transparent Builder F definition, with family-specific calibration parameters. Selection remains F-C under the original calibration; corrected inputs do not reselect a winner. [Selection](docs/evidence/v2-phase-f/selection.json).

## 14. D mapping

D=.10+G*(.40+.40*confidence*tanh(raw/scale)), clipped[0,1]. G is candidate selected-copy cast adequacy; zero eligibility gives .10 regardless of probability. For lands G=1 only when selected with positive retained mana benefit. Unsupported inputs retain the exact production D. At zero margin and G=1, D=.5. Positive/negative raw margins move above/below neutral subject to confidence. Low G lowers D absolutely. It is neither final-build probability nor a predicted win rate.

## 15. Final-23 supporting probability

For F-C only, confidence is multiplied by .85+.15*p_EC; missing support uses .5. The original E-C model/feature definitions remain byte-for-byte frozen. Actual pick-time support is reused from Phase E; paired late support is only frozen inference, not a refit. Its measured mean D contribution in the standalone sample is 0.001624, max absolute 0.019914. The theoretical full0→1 support change is at most .06 D, and no change can unlock G=0 or reverse raw sign. [Benchmark](docs/evidence/v2-phase-f/corrected-inputs/performance.json).

## 16. Pairwise task

Reuse the exact Phase E included-versus-wholly-excluded nonland matched identities: same pool-only color compatibility/creature status, Q caliper .10, MV caliper2, deterministic nearest matching and quantity handling. Every candidate uses a leave-one-copy-out completed pool solely in this explicit final-pool task. Current D is the preserved actual late counterfactual cache; optimizer/generic/E-A/E-C results remain frozen. Positive partial-copy identities are identity discrimination; reconstruction metrics retain interchangeable-copy quantities. Unsupported F candidates use the actual baseline D and are counted as declines, not fully experimental proof.

## 17. Pairwise results

| Development set | Pairs / drafts | Current D | Generic control | E-A | E-C | F-A | F-B | F-C |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| LCI | 2792 / 239 | 0.597779 | 0.665831 | 0.621418 | 0.604943 | 0.597958 | 0.596884 | 0.613360 |
| MKM | 2392 / 245 | 0.618938 | 0.685619 | 0.615803 | 0.631689 | 0.584239 | 0.585493 | 0.596154 |
| OTJ | 3112 / 262 | 0.589814 | 0.674486 | 0.604274 | 0.611825 | 0.571337 | 0.570373 | 0.593188 |
| ONE | 2253 / 207 | 0.571904 | 0.624945 | 0.594541 | 0.612073 | 0.560364 | 0.561696 | 0.571682 |
| MOM | 2965 / 219 | 0.556324 | 0.660034 | 0.598314 | 0.569983 | 0.543002 | 0.546712 | 0.565261 |

Primary means weight pairs within each set and then average sets equally. The original matching bytes and coverage remain unchanged. Fully experimental pair counts/declines are also reported. Five hundred deterministic paired whole-draft resamples provide candidate and candidate-minus-control intervals; those use equal draft weights and are distinguished from pair-weighted primary metrics. Shared cards/negative reuse stay inside draft clusters. No naive independent-pair statistical significance is claimed. [Detailed results](docs/evidence/v2-phase-f/corrected-inputs/pairwise.json).

## 18. Castability evaluation

Controlled same-cost single/double/triple pip cases verify source monotonicity, absent-source zero, total-source limits and unsupported symbols. Splash probes compare a committed two-color pool with absent/present fixing and reject outside double pips. The exact hypergeometric arithmetic tests pass. This validates the implementation of the declared marginal-tail proxy; it does **not** qualify exact joint probabilities, conditional lands, tapped timing or all real alternate-cost casting behavior. [Cases](docs/evidence/v2-phase-f/probes.json).

## 19. Structural probes

| Probe | F-A | F-B | F-C |
| --- | --- | --- | --- |
| fifth | False | True | False |
| early | False | True | True |
| creature | True | True | True |
| removal | True | True | True |
| fixing | False | False | False |
| offcolor | True | True | True |
| splash | True | True | True |
| upgrades | False | True | False |
| nonstarter | True | True | True |
| narrow | True | True | True |
| unsupported | True | True | True |

Selected F-C does not pass every mandatory probe. Its fifth-five-drop score can rise slightly despite a worse structural delta because land/castability/confidence changes offset it. Fixing need remains unqualified for the proxy reason above. The huge-upgrade contrast has larger raw replacement but lower final D than the modest contrast; the two pools differ in pair/state confidence, so it also exposes a confidence-confounded probe design. F-B passes more structure contrasts but has no qualified fixing estimator. These are registered failures/limitations, not retuned successes. Generic fixtures are not historical win evidence. [Frozen post-selection cases](docs/evidence/v2-phase-f/selected-regressions.json).

## 20. Builder reconstruction

| Set | Current F1 | E-C F1 | F F1 | F overlap | F land MAE | F color agreement | Valid40 / N |
| --- | --- | --- | --- | --- | --- | --- | --- |
| LCI | 0.768192 | 0.764497 | 0.736416 | 0.781458 | 2.116667 | 0.879167 | 231 / 240 |
| MKM | 0.691621 | 0.784980 | 0.586867 | 0.621837 | 5.375510 | 0.734694 | 183 / 245 |
| OTJ | 0.734347 | 0.795007 | 0.661202 | 0.689004 | 4.353383 | 0.800752 | 220 / 266 |
| ONE | 0.760632 | 0.767203 | 0.743877 | 0.785697 | 2.038462 | 0.884615 | 200 / 208 |
| MOM | 0.676674 | 0.718638 | 0.652968 | 0.715639 | 2.885845 | 0.867580 | 200 / 219 |

Metrics include nonland precision/recall/F1, copy overlap, multiset Jaccard, land MAE, main/splash agreement, curve L1, creature error and source deficit. Ordinary generated basics normalize by color; nonbasic and spell copies retain logical identities. Any incomplete40-card prediction scores empty/zero reconstruction success. The full declined-draft list is retained. [Results](docs/evidence/v2-phase-f/corrected-inputs/reconstruction.json).

## 21. Stage behavior

Maturity uses actual pool state and source completed-pick index, not new pack multipliers. Early open slots compare against the provisional future marginal utility; full shells compare exact removals. Stage tables/correlations come from the same full D-only replay. The inherited42-pick horizon is not fully validated for39/45-pick sources. Land enumeration starts at21 eligible spells, creating a discrete transition; near-pair hysteresis reduces some pair changes but does not guarantee smooth whole-deck composition.

## 22. Confidence

confidence=min(1,ownedStarters/targetSpells)*picks/(picks+8)*(.5+.5*bestPairPlausibility); F-C additionally applies bounded E-C support. Empty early state has low confidence; exact castability eligibility remains an absolute gate. Low state confidence shrinks both improvements and harms toward neutral, which can counteract desired monotonic probe ordering across different pool states. Confidence is an inspectable heuristic, not a validated probability of model correctness.

## 23. Q redundancy

Raw Q contributes to candidate/displaced cast-adjusted utility differences, not an independent additive D bonus. F-A versus F-B separates replacement from structure; F-C adds mana and bounded probability. Stage-wise corr(D,Q), corr(D,absolute playability), corr(D,E-C probability) are exported. An additional standalone additive-raw-Q arm was not preregistered or fitted; no post-development bonus coefficient is invented. Correlation diagnostics do not establish causal incremental power. The original148-identity Q transport bug is retained separately from the corrected evaluation. [Correlations](docs/evidence/v2-phase-f/corrected-inputs/correlations.json).

## 24. Contextual replay

| Model | N | Top1 | Top3 | MRR | Normalized rank |
| --- | --- | --- | --- | --- | --- |
| current | 50285 | 0.375002 | 0.720811 | 0.577158 | 0.297833 |
| A | 50285 | 0.335627 | 0.676583 | 0.541906 | 0.339039 |
| B | 50285 | 0.337377 | 0.677598 | 0.543243 | 0.337926 |
| C | 50285 | 0.335269 | 0.673740 | 0.540794 | 0.340998 |

50,285 complete comparable decisions / 409,625 candidate occurrences. The actual production v1.6.2 baseline is reused, with only D recomposed through its real calibration function. Q/effective Q/L/A/R/commitment/weights/prior/ties remain exact; baseline max error 0. Experimental Builder F never feeds back into production R or its optimizer outputs. Changes: `{"A/changed": 26397, "A/improved": 5095, "A/regressed": 9945, "B/changed": 26249, "B/improved": 5109, "B/regressed": 9735, "C/changed": 26830, "C/improved": 5314, "C/regressed": 10240}`.

The original compatibility-failing D run is preserved; the corrected run is separately named. No production-engine replay, E-C fit or parameter selection was repeated. The corrected Q lookup max difference is 0. [Corrected replay](docs/evidence/v2-phase-f/corrected-inputs/contextual-replay.json).

## 25. Set-by-set gates

| Frozen gate | Pass |
| --- | --- |
| pair | False |
| coverage | True |
| structuralProbes | False |
| castability | True |
| reconstruction | False |
| contextual | False |
| cold | True |
| goldens | True |
| compatibility | True |

All gates are conjunctive; no secondary gain rescues a failed primary/compatibility/probe/integrity requirement. Per-set primary gains and contextual thresholds are machine-readable. The corrected lookup satisfies raw-Q transport; initial faulty calibration is still disclosed and was not redone. [Development gate](docs/evidence/v2-phase-f/development-gate.json).

## 26. Cold-start results

| Held calibration set | Fixed full accuracy | Fixed leave-out choice accuracy | Gain |
| --- | --- | --- | --- |
| DMU | 0.546820 | 0.542403 | -0.004417 |
| BRO | 0.592841 | 0.592841 | 0.000000 |
| WOE | 0.593487 | 0.582983 | -0.010504 |

Macro loss 0.004974; marginal sensitivity gate True. Leave-out parameter choices from the original study are held fixed and reevaluated on corrected inputs; no new selection is performed. Development sets have zero same-set build training and no set-ID feature. Frozen E-C was trained on all3 calibration sets, so calibration leave-out is **marginal-parameter sensitivity**, not fully independent E-C training. Actual development zero-build transfer is evaluated separately through the five-set metrics.

## 27. Goldens and tests

All **40 existing production goldens pass**, including v1.6/v1.6.1/v1.6.2, sparse Uldaros, Unsummon/Amphisbaena and deck-fit/cut-line cases. 1298 C# tests and 9 grouped Phase F Python tests pass; build0 warnings/errors. Existing D-only C# preservation coverage is reused rather than duplicated. New tests protect multiset displacement, virtual slots, physical source arithmetic, hypergeometric monotonicity, eligibility/splash, diminishing potential, narrow/missing-cost fallback, confidence-only probability, source freezes, matching and explicit incomplete-deck failure. Frozen probe outputs include failures; reproduction-test success is not a strategic-probe pass. Commit `01443eeb5ab5cfb889991fcc45f72efb96e634dc` plus dirty research sources; fingerprint `38ddba2f375f067cefe2db88b0190b83b08c5e6299fb41d3d6eeb71bebf4ff3b`. [Validation](docs/evidence/v2-phase-f/validation.json).

## 28. Holdout status

**MH3/DFT were not consumed.** No download/performance read or release lock was created. BLB/DSK remain spent. Failed development gates prohibit final release; passing a future gate would still not authorize automatic production integration. [Status](docs/evidence/v2-phase-f/holdout-status.json).

## 29. Performance

| Operation | Mean ms | P95 ms | N |
| --- | --- | --- | --- |
| candidate | 16.522302 | 31.880900 | 120 |
| beforeAfterBuilder | 12.816718 | 24.472780 | 154 |
| castability | 0.039001 | 0.070995 | 5462 |
| mana | 0.039289 | 0.079295 | 5462 |

Standalone benchmark peak 47.79MiB; frozen E probability artifact 18,606bytes; kernel source 15,152bytes. Corrected prediction preparation took 626.067s with4 bounded workers; every result key is checked against source pack identity/order. This is offline feasibility data, not a live Arena responsiveness demonstration. Timing includes cache effects and concurrent load; the castability and mana summaries are per internal call, not total deck reconstruction time.

## 30. Limitations and reproduction

ObservedPrimaryBuild is a uniquely modal observed game deck, not proven submitted/optimal/final. Exchangeable copies cannot identify which physical pick was retained. Public-user/deck selection, incomplete histories and mature aggregate evidence constrain interpretation; no causal win improvement, chronological backtest or exact collector printing proof is claimed. Source costs/roles are incomplete. Conditional/indirect/tapped mana, shared dual-source joint casting, prototype/adventure alternate costs and MDFC back-land decisions remain unqualified. Terminal virtual-slot ranking is a concrete integrity failure. Confidence varies across controlled pools. Original calibration used an invalid Q transport; corrected inputs preserve its fixed choices, not an optimally recalibrated model.

Reproduce initial stages only in a fresh separately identified workspace with the same inherited source hashes. Completed guards prevent tuning/resetting this released namespace. All prior-phase outputs remain read-only. No new acquisition is needed.

```powershell
Set-Location C:\Users\Graphics0\Documents\Projects\DraftTG
python tools/DraftTG.HistoricalEvaluation/phase_f.py verify
# Initial study only, after prospective registration:
python tools/DraftTG.HistoricalEvaluation/phase_f.py prepare
python tools/DraftTG.HistoricalEvaluation/phase_f.py pair-inputs
python tools/DraftTG.HistoricalEvaluation/phase_f.py validation-baseline
python tools/DraftTG.HistoricalEvaluation/phase_f.py calibrate
python tools/DraftTG.HistoricalEvaluation/phase_f.py pairs
python tools/DraftTG.HistoricalEvaluation/phase_f.py builders
python tools/DraftTG.HistoricalEvaluation/phase_f.py predict
python tools/DraftTG.HistoricalEvaluation/phase_f.py contextual
python tools/DraftTG.HistoricalEvaluation/phase_f.py cold
python tools/DraftTG.HistoricalEvaluation/phase_f.py probes
python tools/DraftTG.HistoricalEvaluation/phase_f.py benchmark
# Same frozen parameters: correction is separately registered, original run retained.
python tools/DraftTG.HistoricalEvaluation/phase_f_corrected.py prepare
python tools/DraftTG.HistoricalEvaluation/phase_f_corrected.py pairs
python tools/DraftTG.HistoricalEvaluation/phase_f_corrected.py builders
python tools/DraftTG.HistoricalEvaluation/phase_f_corrected.py predict
python tools/DraftTG.HistoricalEvaluation/phase_f_corrected.py contextual
python tools/DraftTG.HistoricalEvaluation/phase_f_corrected.py cold
python tools/DraftTG.HistoricalEvaluation/phase_f_corrected.py benchmark
python tools/DraftTG.HistoricalEvaluation/phase_f_corrected.py mana-audit
dotnet build DraftTG.sln *> data/historical/evaluation/phase-f/build.log
dotnet test DraftTG.sln --no-build --logger trx --results-directory data/historical/evaluation/phase-f/tests
dotnet tools/DraftTG.HistoricalEvaluation/bin/Debug/net10.0/DraftTG.HistoricalEvaluation.Cli.dll golden --root . --output data/historical/evaluation/phase-f/golden
python tools/DraftTG.HistoricalEvaluation/test_phase_f.py
python tools/DraftTG.HistoricalEvaluation/phase_f.py validate
# Failed gate reports/refuses holdout release and never changes production.
python tools/DraftTG.HistoricalEvaluation/phase_f.py final
# Normal read-only commands after this completed run:
python tools/DraftTG.HistoricalEvaluation/phase_f.py summary
python tools/DraftTG.HistoricalEvaluation/phase_f.py report
```

Large row/model/prediction traces remain ignored under data/historical/phase-f paths. NumPy is the existing task-local dependency; no new ML package/neural model was introduced. Data attribution is the inherited [17Lands public bulk corpus](https://www.17lands.com/public_datasets), pinned transformations and owned metadata documented in Phase B/B.1/E.

## 31. Recommendation

**Do not integrate F-A/B/C. Retain production v1.6.2 D and builder.** Next authorized research should first prevent virtual cards from outranking complete terminal40-card decks, qualify joint/conditional mana and fixing deltas, and compare upgrade probes at controlled castability/confidence. Start with an actual-production-Q compatibility preflight before any calibration. Then preregister a fresh complete-input calibration/development evaluation; do not fit weights around these failed candidates or spend MH3/DFT to rescue them. No next phase begins automatically.
