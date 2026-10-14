# Contextual Pick Score V2 — Phase E: Final-23 Probability and Deck-Fit Research

## 1. Executive conclusion

**Retain production `contextual-pick-score-v1.6.2` D and the existing deck builder. No Phase E candidate qualifies for integration.** Phase E qualified 3,708 exact pick/pool/build drafts and 154,266 supported decisions. Calibration selected E-C, with FULL structural/marginal features; development gates are reported individually below. Healthy pair coverage is achieved, but predictive improvements alone do not prove a reliable replacement deck-fit component.

MH3/DFT remain untouched; BLB/DSK remain spent. No production Q/L/A/D/R/weights, FRA prior, k500, tiers, Phase 9F, memory/localization or overlay source was altered. No Phase F work is started.

## 2. Preregistration

Prospective plan committed as `013173b`; source-schema correction committed as `0048c80` before candidate analysis. The original plan is preserved. MKM has three 13-pick source packs, so complete histories may contain 39, 42 or 45 picks; opening pack sizes and contiguous coordinates must agree. This corrects a schema assumption, not a performance threshold.

Calibration: **DMU/BRO/WOE**, evidence drafts split by SHA into training/validation. Development: **LCI/MKM/OTJ/ONE/MOM**, target draft group only. The inherited evidence_group split groups all picks/build labels from a draft together. Matching freezes before candidate performance; candidates/ridge/features freeze on calibration validation, without development re-selection. [Plan](docs/evidence/v2-phase-e/preregistration.json), [registration](docs/evidence/v2-phase-e/registration.json), [schema correction](docs/evidence/v2-phase-e/schema-clarification.json).

## 3. Production D audit

The actual `CandidateDeckImpactEvaluator` creates before/after immutable Phase 10 projections, assesses candidate playability in viable pairs, and compares spell selection value to a real/expected-future cut line. For a playable spell, membership M=logistic(margin/scale), scale .02 early to .01 late; replacement U uses .04 for a positive margin and the membership scale below zero. F=.10+.90*(M+U)/2. D=clamp(F+.30*confidence*gate*S,0,1), S=max(creature need, early need, removal need)−top-end redundancy. Needs use projected counts scaled to 23 spells; structural confidence reaches 1 at 18 spells. Gate=logistic(margin/.02), or 1 for a composition-forced selection. Creature need targets preferred16/minimum14; early target4; weighted removal target3; top-end redundancy begins above5 MV>=5 cards over span3. Structural correlations are combined by maximum, not summed.

Lands earn .55+.35*clamp(measured source-shortfall improvement,0,1) only when selected and reducing shortfall; otherwise floor .10. Missing metadata/eligible shells use the existing explicit unavailable fallback. Off-pair playability permits only one outside color, at most one outside pip, with up to .6 credit at three drafted sources; the baseline builder itself does not add splashes. Pair plausibility uses average-quality softmax (.0025 deck-fit, .01 playability) and a two-spell viability taper.

Future supply is remaining picks*.8; open slots and starters below baseline−.03 compete for the virtual final marginal card. FRA-derived coverage/filler offsets remain explicitly provisional for other sets. Weights smoothly transition (.70,.20,.10,0) → (.50,.20,.15,.15) → (.30,.10,.20,.40) at completed-pick anchors4/21/35, assuming42 picks/14 per pack. Early D is unweighted. D is a heuristic marginal projected fit, **not** calibrated final-build probability, a causal win estimate, or proof of reconstructing the observed deck. [Sources](src/DraftTG.RecommendationEngine/CandidateDeckImpactEvaluator.cs), [configuration](src/DraftTG.RecommendationEngine/ContextualPickScoreModels.cs).

## 4. Corpus qualification

| Set | Pick drafts | Complete sequences | Observed builds | Exact joins | Qualified | Supported decisions | Target drafts |
| --- | --- | --- | --- | --- | --- | --- | --- |
| DMU | 700 | 690 | 7256 | 618 | 446 | 18732 | 232 |
| BRO | 700 | 686 | 7119 | 597 | 420 | 17640 | 217 |
| WOE | 700 | 688 | 7305 | 630 | 445 | 18690 | 218 |
| LCI | 700 | 695 | 7135 | 624 | 494 | 20748 | 240 |
| MKM | 700 | 689 | 7430 | 641 | 490 | 19110 | 245 |
| OTJ | 700 | 691 | 7301 | 632 | 512 | 21504 | 266 |
| ONE | 700 | 685 | 7359 | 618 | 419 | 17598 | 208 |
| MOM | 700 | 688 | 7221 | 636 | 482 | 20244 | 219 |

Total **3,708 qualified drafts / 154,266 supported picked labels**. Qualification includes all registered evidence/target drafts; development metrics use target drafts only. Complete-sequence count is a coordinate-length census; actual qualification additionally requires every pre-pick inventory to equal preceding picks. Source identity coverage and every exclusion category are retained per set in [qualification](docs/evidence/v2-phase-e/qualification.json). Historical pick15 remains in completed pools, but outside supported scoring/feature coordinates.

Data attribution: existing [17Lands public bulk datasets](https://www.17lands.com/public_datasets), pinned TrevorJS transformation and owned card metadata as qualified in [Phase B](V2_PHASE_B_SET_NORMALIZED_QUALITY_RESEARCH.md) and Phase B.1. No new aggregate/API/acquisition request was made. Exact replay catalog/statistics/split SHA256 match the completed D.1 inputs; [input hashes](docs/evidence/v2-phase-e/input-hashes.json).

## 5. Pool/build join integrity

Joins use exact set/format/provider/draft IDs in pinned canonical datasets, not timestamps, player-name similarity or card overlap. Existing exact game/build qualification is inherited; source hashes and URI provenance accompany each evaluation draft. Ordered actual picks reconstruct quantities; every source PoolBeforePick is checked exactly. Offered and picked identities must resolve; repeated quantity rows decline. Nonbasic observed copies must be a subset of the completed pool. Ordinary Plains/Island/Swamp/Mountain/Forest are the explicit Arena supplied-basic exception; snow basics/nonbasics are not silently generated. No impossible pool was repaired. The evaluation draft retains ordered picks, completed pool, observed build, support, provenance, and outcome labels in separate fields.

## 6. ObservedPrimaryBuild caveats

A unique modal observed 40-card game-deck configuration is the label, not a proven final submitted/pre-sideboard deck. At least3 games and a single configuration are required because support for the modal configuration among multiple configurations is unavailable in the inherited aggregate. Tied/ambiguous joins decline. Land range12–20 is a sanity qualification envelope, not a requirement to play17 lands. Unobserved/abandoned drafts and public-user selection limit generalization. No game wins enter fitting. The source cannot identify which drafted occurrence of identical copies was retained.

## 7. Current deck-builder reconstruction accuracy

| Development set | Current F1 | Highest-Q F1 | Curve-17 F1 | Builder B F1 | Builder C F1 | Current overlap | C overlap |
| --- | --- | --- | --- | --- | --- | --- | --- |
| LCI | 0.768192 | 0.756123 | 0.756637 | 0.756883 | 0.764497 | 0.820625 | 0.791771 |
| MKM | 0.691621 | 0.776700 | 0.779513 | 0.779614 | 0.784980 | 0.736531 | 0.806633 |
| OTJ | 0.734347 | 0.786810 | 0.790055 | 0.790055 | 0.795007 | 0.767105 | 0.808647 |
| ONE | 0.760632 | 0.759211 | 0.761689 | 0.761689 | 0.767203 | 0.815986 | 0.788341 |
| MOM | 0.676674 | 0.703647 | 0.707808 | 0.707808 | 0.718638 | 0.751256 | 0.766324 |

The current builder is executed directly, with exact frozen statistics, against each completed pool. It selects23 nonlands/17 lands, conservative two-color plan, creature/early floors and MV5+ cap; drafted nonbasics cap4 and basic allocation minimum6 per used color where feasible. Pair-specific statistics and successful-deck corpus are unavailable, as in prior frozen replays. If it cannot supply23 eligible spells it declines; empty unavailable predictions remain visible rather than being replaced with an invented deck. [Reconstruction](docs/evidence/v2-phase-e/reconstruction.json).

## 8. Current cut-line accuracy

| Audit spell-copy rank | Copies | Observed inclusion |
| --- | --- | --- |
| 1-10 | 18450 | 0.855597 |
| 11-20 | 18450 | 0.703402 |
| 21-23 | 5535 | 0.598735 |
| 24-26 | 5535 | 0.365742 |
| 27+ | 25852 | 0.342104 |

Actual production starter membership first, then frozen Q within membership group; ties are source-ID order. This is an audit ordering, not a claimed optimizer objective rank. Duplicate copies receive fractional m/n labels rather than arbitrary kept/cut ordering. The current-builder precision/recall/F1 is also the starter/nonstarter confusion summary. A rank-23 boundary is a product target, not a ground-truth universally optimal spell count. Land/creature/curve/mana/pair limitations and discrete composition constraints can disrupt monotonicity. [Cut-line audit](docs/evidence/v2-phase-e/cutline.json).

Quantity confusion counts and observed land histogram: [final-pool diagnostics](docs/evidence/v2-phase-e/final-pool-diagnostics.json). Current builder availability: `{"Ready": 1765, "InsufficientEligibleCards": 80}`. Observed target builds by land count: `{"17": 1327, "16": 456, "18": 24, "15": 35, "14": 3}`. Thus17 is a common observed choice, not universal ground truth.

## 9. Final-build probability features

Generic p0=logistic(β·[intercept, centered frozen Q, pool-projected playability, source stage, colorless, multicolor]). E-B adds legal current structural state: types, mana/pips and unknown flags, current duplicate count, pool size, projected spells/creatures, early/top coverage and deficits, reliable role channels with missingness. E-C adds candidate enters projection, candidate-minus-cutline Q, creature/early/top/quality deltas and color-pair transition; FULL also includes all assigned role channels and unknown flags. Before/after deterministic projections use only PoolBeforePick plus the candidate. This is information available immediately after choosing the candidate; neither completed pool nor observed deck is an argument of `feature`. Source stage measures completed picks before selection; before/after deltas describe the just-added card.

Selected hybrid: **FULL, ridge 1**, 51 features. No set/card ID, player identity, final record, build, future picks or learned archetype enters the generic model. [Exact coefficients and feature names](docs/evidence/v2-phase-e/selection.json), [probability calibration](docs/evidence/v2-phase-e/probability-calibration.json).

## 10. Structural feature coverage

Structured colors/type/MV, the actual `DeckManaDemand` parser and actual `LimitedCardRoleClassifier` are reused. Positive removal/fixing/card-advantage/ramp/trick/finisher/defensive/aggressive labels are evidence channels; an unassigned label is explicit unknown, not a proven absence. Interaction and defensive roles are not inferred from outcomes. Detailed histograms and missingness are reported in [structural coverage](docs/evidence/v2-phase-e/structural-coverage.json). Nonland curve counts preserve copies, primary-face mana only; alternative faces/indirect fixing/colorless-specific costs remain unmodeled.

## 11. Q redundancy analysis

| Feature model | Cal validation log loss | Dev macro log loss | Dev macro Brier |
| --- | --- | --- | --- |
| generic | 0.594934 | 0.580527 | 0.198414 |
| NO-Q-strict | 0.585977 | 0.587393 | 0.200112 |
| NO-Q | 0.577037 | 0.576472 | 0.195721 |
| Q-CONTEXT | 0.570323 | 0.563589 | 0.190512 |
| FULL | 0.563115 | 0.557096 | 0.187955 |
| no-roles | 0.568132 | 0.560367 | 0.189127 |
| basic-roles | 0.568132 | 0.560367 | 0.189127 |
| no-curve | 0.563560 | 0.558212 | 0.188527 |
| detailed-curve | 0.563031 | 0.557741 | 0.188229 |
| no-mana | 0.563528 | 0.556740 | 0.187791 |
| E-B | 0.570036 | 0.567250 | 0.192013 |

These are fractional-occurrence probability losses, not card GIH errors. NO-Q-strict drops direct raw/cut-line/numeric quality-delta features; Q-CONTEXT permits marginal cut-line/quality differences; FULL includes raw Q. Existing Q still carries intrinsic quality in contextual scoring. E-B/E-C D use centered probability residual relative to p0, preventing a raw final-build probability from simply becoming another Q vote. Correlations between D and frozen Q remain visible per set in [redundancy](docs/evidence/v2-phase-e/Q-redundancy.json); residualization does not prove independence or causal deck improvement. Basic roles/no roles and broad/detail curve variants are separately reported; no development result changes selection.

Diagnostic correction: the original NO-Q feature list inadvertently included numeric qualityDelta. It remains preserved under NO-Q; **NO-Q-strict** excludes Q, cutDelta and qualityDelta. Even strict coefficient-level NO-Q uses a frozen-Q-conditioned optimizer projection, so it is not a fully quality-free builder. This diagnostic was fitted on calibration only and did not change E-C selection. [Correction](docs/evidence/v2-phase-e/no-q-audit.json).

## 12. E-A definition

No learned inclusion model: D_A=clamp(.10+.45*playable*enters+.20*tanh(4*(Q−cutQ))+.15*playable*(creatureHole*isCreature+earlyHole*isMV<=2−topSaturation*isMV>=5),0,1). Candidate and cutline use frozen normalized Q, not raw win-rate percentage points. The deterministic experimental structural projection enumerates two-color pairs, selects eligible occurrences greedily, and uses deficit-relative creature/early/removal bonuses and top-end penalties. The displaced identity, before/after chosen quantities, curve/creature and Q deltas remain auditable. This is an improved offline heuristic projection, not the production optimizer substituted into the app.

## 13. E-B definition

pB=logistic(βB·generic+structural state). D_B=clamp(.5+.5*(pB−p0),0,1). Calibration uses grouped calibration-training picks; L2 ridge1/10/100 is selected by calibration-validation set-macro log loss. The final-build target is m/n exchangeable copy probability, where n drafted copies and m observed copies are known only as labels after legal features are computed. Identity presence, build quantity, drafted quantity and pick copy ordinal are retained separately. Exact later-copy retention is **unidentifiable**; fractional labeling does not assert which pick was played.

## 14. E-C definition

pC is the calibrated logistic hybrid using the selected generic/structural/marginal feature family. D_C=clamp(.5+.5*(pC−p0),0,1). Coefficients determine incremental contributions; optimizer/probability/structural signals are not independently summed as votes. An intercept/slope logit calibration with penalties1/10 toward identity is fitted only on calibration validation, after the original family/ridge choice; that choice remains unchanged. Both raw and calibrated model files are preserved. Residual D has a known weakness: a low uncastable probability can remain near neutral if p0 is also low. The off-color probe assesses this rather than silently adding a post-development gate.

## 15. Builder A/B/C

A is the actual production builder. Registered simple baselines are highest-Q eligible spells plus basics and two-color curve heuristic at17 lands. B uses the structural projection with flexible15–19 lands. Land count=clip(round(17+scale*(projected average MV−3)),15,19), scale selected among .5/1/1.5 solely by calibration-validation F1. Spell greedy value=Q+.12*creature*max(0,14−creatures)/14+.10*early*max(0,4−early)/4−.08*high*max(0,high−4)/3+.04*removalWeight*max(0,3−removal)/3. C blends this value equally with calibrated final-build probability; no coefficients fitted on development. Both use known drafted sources and proportional primary-face pip allocation. No perfect mana solver, unsupported splash inference, or manual set rule is added. [Builder selection](docs/evidence/v2-phase-e/builder-selection.json).

## 16. Final-pool reconstruction results

| Development set | Current F1 | Highest-Q F1 | Curve-17 F1 | Builder B F1 | Builder C F1 | Current overlap | C overlap |
| --- | --- | --- | --- | --- | --- | --- | --- |
| LCI | 0.768192 | 0.756123 | 0.756637 | 0.756883 | 0.764497 | 0.820625 | 0.791771 |
| MKM | 0.691621 | 0.776700 | 0.779513 | 0.779614 | 0.784980 | 0.736531 | 0.806633 |
| OTJ | 0.734347 | 0.786810 | 0.790055 | 0.790055 | 0.795007 | 0.767105 | 0.808647 |
| ONE | 0.760632 | 0.759211 | 0.761689 | 0.761689 | 0.767203 | 0.815986 | 0.788341 |
| MOM | 0.676674 | 0.703647 | 0.707808 | 0.707808 | 0.718638 | 0.751256 | 0.766324 |

Nonland precision/recall/F1, copy overlap, multiset Jaccard, generated-basic color overlap, land MAE, main/splash color agreement, creature error, curve L1 and known removal-count differences are exported. Ordinary-basic printings are normalized by color; nonbasic spell copies keep exact research logical identity. Top23 precision/recall here are selected-spell copy metrics: flexible21–25-spell builds are not mislabeled as universally23. Deck integrity is explicitly gated; no partial deck is counted as a valid40-card success. Final-pool information is permitted only in this hindsight reconstruction task.

## 17. Picked-card inclusion results

| Development set | N | Generic Brier | C Brier | Generic log loss | C log loss | Generic ECE | C ECE |
| --- | --- | --- | --- | --- | --- | --- | --- |
| LCI | 10069 | 0.200819 | 0.191221 | 0.587024 | 0.566624 | 0.016374 | 0.015563 |
| MKM | 9555 | 0.186760 | 0.175076 | 0.553761 | 0.526241 | 0.021647 | 0.023672 |
| OTJ | 10761 | 0.187280 | 0.173410 | 0.555779 | 0.523589 | 0.034151 | 0.027496 |
| ONE | 8736 | 0.204189 | 0.195353 | 0.593865 | 0.574562 | 0.025319 | 0.024536 |
| MOM | 9184 | 0.213022 | 0.204716 | 0.612206 | 0.594465 | 0.042493 | 0.036230 |

Fractional-label expected Brier is y*(1−p)^2+(1−y)*p^2, with y=m/n; log loss uses the same exchangeable expectation. Fractional labels are not incorrectly squared as if they were certain labels. AUROC and average precision use only identifiable binary y∈{0,1}, with denominator retained. Ten fixed probability bins report calibration; set-macro and stage results are distinct. This predicts observed build choice, not necessarily the best deck.

## 18. Pairwise deck-fit task

For each completed pool, match a build-included nonland identity against a wholly build-excluded drafted identity, with equal pool-only color compatibility and creature status, Q caliper .10, MV caliper2. Choose nearest distance |Qdiff|/.10+|MVdiff|/2+|drafted-copy difference|/3, deterministic SHA tie break. One nearest excluded identity per included identity; unique pair per draft/identity. A negative may be reused across positives; pairs are not independent observations. This avoids requiring four simultaneous distractors. Positive partial-copy identities retain their uncertainty: the task is identity discrimination, while reconstruction/inclusion metrics carry copy semantics.

Candidates see a leave-one-copy-out completed pool plus the candidate only for this final-pool discrimination task; a separate actual production replay scores current D at an explicit late counterfactual. Its stage has no remaining picks. It is not a claimed historical legal pick-time observation. [Task freeze](docs/evidence/v2-phase-e/pair-task-freeze.json).

## 19. Pairwise results

| Development set | Pairs / drafts | Current D | Current optimizer | Generic control | E-A | E-B | E-C |
| --- | --- | --- | --- | --- | --- | --- | --- |
| LCI | 2792 / 239 | 0.597779 | 0.521669 | 0.665831 | 0.621418 | 0.604226 | 0.604943 |
| MKM | 2392 / 245 | 0.618938 | 0.515050 | 0.685619 | 0.615803 | 0.588211 | 0.631689 |
| OTJ | 3112 / 262 | 0.589814 | 0.518798 | 0.674486 | 0.604274 | 0.588689 | 0.611825 |
| ONE | 2253 / 207 | 0.571904 | 0.513094 | 0.624945 | 0.594541 | 0.583222 | 0.612073 |
| MOM | 2965 / 219 | 0.556324 | 0.502867 | 0.660034 | 0.598314 | 0.558179 | 0.569983 |

Accuracy uses .5 credit for ties. Margins and pair log loss are available; pair log loss applies logistic(4*score difference) equally to bounded D/comparator values and is diagnostic, not fitted probability calibration. Pair accuracy is the primary marginal discrimination metric; human agreement is secondary. Matching distances and draft counts are independent of candidate scores. Calibration target results are diagnostics, not the validation split used for fitting/selection. [Results](docs/evidence/v2-phase-e/pairwise.json).

## 20. Stage behavior

Inclusion probability and calibration are stratified by source pack, with current duplicate count, source pick index and structural state as generic inputs. No new pack-specific multiplier was fitted. Existing contextual D weights stay fixed and smoothly mature. Controlled pool transitions expose unavoidable discrete spell-selection/color-pair changes; residual logistic probability is smooth conditional on features, but deterministic membership and displaced-card identity can jump. [Stage results](docs/evidence/v2-phase-e/stage.json), [transition magnitudes](docs/evidence/v2-phase-e/failure-probes.json).

## 21. Curve/role/mana ablations

| Feature model | Cal validation log loss | Dev macro log loss | Dev macro Brier |
| --- | --- | --- | --- |
| generic | 0.594934 | 0.580527 | 0.198414 |
| NO-Q-strict | 0.585977 | 0.587393 | 0.200112 |
| NO-Q | 0.577037 | 0.576472 | 0.195721 |
| Q-CONTEXT | 0.570323 | 0.563589 | 0.190512 |
| FULL | 0.563115 | 0.557096 | 0.187955 |
| no-roles | 0.568132 | 0.560367 | 0.189127 |
| basic-roles | 0.568132 | 0.560367 | 0.189127 |
| no-curve | 0.563560 | 0.558212 | 0.188527 |
| detailed-curve | 0.563031 | 0.557741 | 0.188229 |
| no-mana | 0.563528 | 0.556740 | 0.187791 |
| E-B | 0.570036 | 0.567250 | 0.192013 |

All available roles are precision-first positive channels with explicit unknown flags; basic roles means generic type/early/creature channels, without assigned interaction/fixing tags. No roles omits the optional tag channels. Broad curve is early/mid/top occupancy; detailed curve includes eight MV buckets. Missing mana is not assumed free. Ablations are fixed before development and used diagnostically after the calibration selection; no post-development switch to the best-looking ablation.

## 22. Contextual D-only replay

| Family | Comparable decisions | Top1 | Top3 | MRR | Normalized rank |
| --- | --- | --- | --- | --- | --- |
| current | 50285 | 0.375002 | 0.720811 | 0.577158 | 0.297833 |
| A | 50285 | 0.364204 | 0.705976 | 0.566683 | 0.313829 |
| B | 50285 | 0.296510 | 0.642398 | 0.510337 | 0.377672 |
| C | 50285 | 0.305976 | 0.655742 | 0.519907 | 0.366192 |

The actual production adapter ran once for 54,283 source decisions; 50,285 complete comparable decisions are recomposed through the actual production calibration function. Q/effective Q/R/L/A/weights/commitment/current optimizer outputs and tie breaks remain frozen. Experimental D uses its own clearly labeled offline structural projection but does not alter production projections or feed them back into R. Baseline recomposition max error: 0.

Changes: `{"A/changed": 22098, "A/improved": 4601, "A/regressed": 5405, "B/changed": 32517, "B/improved": 5828, "B/regressed": 15569, "C/changed": 32154, "C/improved": 5826, "C/regressed": 14676}`. Only the actual picked card is joined to observed build inclusion after scoring; unpicked offered cards are not treated as build negatives. Actual-pick conditioned probabilities and record-stratified labels are in [association](docs/evidence/v2-phase-e/actual-pick-association.json). Source display order remains unavailable, source occurrence ordering preserved.

## 23. Set-by-set development gates

| Gate | Pass |
| --- | --- |
| pair | False |
| reconstruction | False |
| inclusion | True |
| coverage | True |
| contextual | False |
| cold | True |
| probes | False |
| goldens | True |
| frozenQCompatibility | False |

Exact thresholds were registered: primary pair gain≥2pp over current D, optimizer and generic control, positive4/5 and worst loss≤2pp; F1 gain≥1pp over current/strongest simple builder, positive4/5, all deck integrity, no severe color/land regression; inclusion log-loss gain≥2%/Brier≥1%, both improve4/5 and ECE loss≤1pp; every set≥500 pairs/100 pair drafts and≥100 target drafts/1000 supported picks; contextual Top1/MRR loss≤.005; calibration LOSO no macro loss; required probes/goldens pass. No gate was relaxed. [Gate and exact per-set deltas](docs/evidence/v2-phase-e/development-gate.json).

The input-contract audit adds a conservative blocker: 5 offered occurrences of one DMU identity differ between inherited research Q lookup and actual production neutral Q. The original source spells Sol'kanar while the catalog uses Sol'Kanar; research identity normalization resolves it, whereas the frozen production mapper uses exact ordinal names. Production Q remains0.5, while the research feature lookup is0.5488579112. No refit or production-name repair followed. This discrepancy is not offered as an explanation for the large contextual regression. [Compatibility audit](docs/evidence/v2-phase-e/frozen-Q-compatibility.json), [execution corrections](docs/evidence/v2-phase-e/execution-corrections.json).

## 24. Cold-start cross-set results

| Held calibration set | Generic log loss | Hybrid log loss | Generic Brier | Hybrid Brier |
| --- | --- | --- | --- | --- |
| DMU | 0.581985 | 0.557913 | 0.198517 | 0.188131 |
| BRO | 0.634189 | 0.620292 | 0.221598 | 0.214970 |
| WOE | 0.581422 | 0.544806 | 0.198406 | 0.183006 |

Cold gate: **True**. Development models have zero development-set build-training rows and no card/set IDs. Q remains the frozen exact-format evidence, so this is zero-build transfer conditional on the available Q/metadata, not zero-statistics first-day success. LOSO coefficients refit only on the other calibration sets; the frozen ridge choice came from the registered calibration process, not a fully nested independent holdout. No same-set adaptation is installed.

## 25. Outcome-stratified description

Outcomes are attached after scoring/model selection only. Terminal pick records are separated from unknown/nonterminal source records; lower/average/5/6/7-win slices describe predicted inclusion and separation for actual picked cards. Game-support wins do not become features, quality weights or learned deck-fit targets. Sparse high-record strata are not evidence of causal win improvement. [Conditional results](docs/evidence/v2-phase-e/actual-pick-association.json).

## 26. Failure probes

Bounded transitions: **True**; main structural contrast checks: **False**; all required strategic behaviors qualified: **False**. Fifth-five-drop, missing-two-drop, creature saturation, off-color bomb, cutline/huge upgrades and discrete transitions are measured using real metadata and controlled pool states. Fixing/splash unlocking and narrow sideboard-only behavior remain unqualified where the experimental projection/tag scope cannot support the mechanism. This is a failure/limitation, not a passed probe. Existing production fixes/need behavior remains authoritative. Selection was frozen before probes; no parameters were altered in response. [Probe details](docs/evidence/v2-phase-e/failure-probes.json), [selected regression cases](docs/evidence/v2-phase-e/selected-regressions.json).

## 27. Goldens and validation

**40 existing goldens passed**, including v1.6/v1.6.1/v1.6.2, Uldaros sparse-quality, Unsummon/Arcane Amphisbaena, deck fit and cut-line fixtures. Production Uldaros remains exact score48.219986250; Unsummon25.328185285 below Arcane Amphisbaena25.501120969. Goldens were not calibration targets. 1298 C# tests and 8 grouped Phase E Python tests passed. Build:0 warnings/errors. Targeted additions protect actual D-only recomposition, no-future feature boundary, exact joins/copy labels/subsets, missingness, curve, deterministic matching, selected model/probe regressions and holdout refusal. Tested commit `0048c80ff9bf433766b8874a4138e4bb87501d99` plus dirty Phase E/previous completed D sources; fingerprint `8508859cbb51ff370a8f0a0e7b45ae4f0120d35680f466030fdbb65cc9c550b1`. [Validation](docs/evidence/v2-phase-e/validation.json).

## 28. Holdout status

**MH3/DFT were not consumed.** No holdout acquisition/performance read or release lock was created. One or more frozen development gates fail, so a release is prohibited. BLB/DSK remain spent and are not used as tuning or final holdouts. The optional final command reports/refuses release on a failed gate. Passing a future research gate would still not authorize changing production automatically. [Status](docs/evidence/v2-phase-e/holdout-status.json).

## 29. Performance

Actual replay: 1072.357s, peak 271.30MiB. Experimental feature/probability preparation: 937.139s for 409,625 candidate occurrences; mean 2.1857ms, p95 5.5191ms including deterministic projection/cache overhead. All calibrated model families: 18,606 bytes. Actual builder timing is separately recorded in [builder timing](docs/evidence/v2-phase-e/current-builder-timing.json). Frozen-Q lookup versus actual raw Q max difference: 0.0489. These offline concurrent-process timings are not a live UI responsiveness claim; no Arena session occurred.

Separate linear-only inference benchmark: mean 0.06058ms / p95 0.06501ms for all three families, excluding projections/I/O; benchmark-process peak 488.96MiB includes loaded feature rows/metadata. This is not deployed model residency. [Benchmark](docs/evidence/v2-phase-e/linear-inference-benchmark.json).

## 30. Limitations and reproduction

Observed modal deck choice is not proven optimal/final/submitted; only one-configuration supported builds qualify. Pool/source/schema/metadata exclusions are visible. Exchangeable labels identify quantity rates, not individual retained copies. Pair positives are identity-level; reused negatives and shared drafts require clustered uncertainty, no significance claim is made. Frozen mature Q is not a chronological evidence backtest. Production assumes42picks; historical pick15 cannot be scored. Greedy structural projections, primary-face costs, partial roles and no splash solver cannot establish all proposed deck-fit mechanisms. Flexible-land partial predictions fail integrity. No set identity feature does not remove observational selection bias.

Commands below reproduce stages on a **fresh separate research namespace with the same frozen source hashes**; completed-study guards refuse refits/replays in this released workspace. Do not reset locks or mutate existing evidence. Normal commands are read-only `summary`, `report`, `verify`.

```powershell
Set-Location C:\Users\Graphics0\Documents\Projects\DraftTG
dotnet build DraftTG.sln
dotnet build tools/DraftTG.DeckFitResearch/DraftTG.DeckFitResearch.csproj -o data/historical/evaluation/phase-e/runner4
# Initial study only, after registration; no historical acquisition:
python tools/DraftTG.HistoricalEvaluation/phase_e.py qualify
python tools/DraftTG.HistoricalEvaluation/phase_e.py features
python tools/DraftTG.HistoricalEvaluation/phase_e.py pair-task
python tools/DraftTG.HistoricalEvaluation/phase_e.py fit
python tools/DraftTG.HistoricalEvaluation/phase_e.py calibrate
python tools/DraftTG.HistoricalEvaluation/phase_e.py builder-audit
python tools/DraftTG.HistoricalEvaluation/phase_e.py pair-current
python tools/DraftTG.HistoricalEvaluation/phase_e.py builders
python tools/DraftTG.HistoricalEvaluation/phase_e.py compare
python tools/DraftTG.HistoricalEvaluation/phase_e.py replay
python tools/DraftTG.HistoricalEvaluation/phase_e.py prepare-replay
python tools/DraftTG.HistoricalEvaluation/phase_e.py contextual
python tools/DraftTG.HistoricalEvaluation/phase_e.py cold
python tools/DraftTG.HistoricalEvaluation/phase_e.py probes
python tools/DraftTG.HistoricalEvaluation/test_phase_e.py
dotnet test DraftTG.sln --no-build --logger trx --results-directory data/historical/evaluation/phase-e/tests
dotnet tools/DraftTG.HistoricalEvaluation/bin/Debug/net10.0/DraftTG.HistoricalEvaluation.Cli.dll golden --root . --output data/historical/evaluation/phase-e/golden
python tools/DraftTG.HistoricalEvaluation/phase_e.py validate
# Reports/refuses optional holdout release when gates fail; never promotes production:
python tools/DraftTG.HistoricalEvaluation/phase_e.py final
# Normal read-only use after this completed run:
python tools/DraftTG.HistoricalEvaluation/phase_e.py verify
python tools/DraftTG.HistoricalEvaluation/phase_e.py summary
python tools/DraftTG.HistoricalEvaluation/phase_e.py report
```

Python NumPy uses the existing task-local2.3.4 dependency; raw pools/labels/feature rows/model traces stay ignored under data/historical/phase-e paths. Compact evidence retains hashes, thresholds, selections, coefficients and released metrics.

## 31. Recommendation for Phase F

**Retain existing production D and deck builder.** Phase E does not qualify experimental integration. If separately authorized, Phase F should define an integration contract that preserves successful existing components, separates residual probability from absolute castability/marginal replacement, and establishes fixing/splash/sideboard behavior with reliable metadata. First resolve failed reconstruction/primary/probe gates on a newly preregistered evaluation; do not combine unqualified Q/L/A/D candidates or spend MH3/DFT to rescue a model. No Phase F is started here.
