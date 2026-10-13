# Contextual Pick Score V2 — Phase B.1: Quality Evidence Compatibility

## 1. Executive conclusion

**Do not install production V2 Q.** B.1 count-unit research and replay expansion are complete, but production evidence compatibility and generalized alternate reliability remain unqualified. Production v1.6.2, k500, FRA-only prior, L/A/D, commitment, tiers, memory, localization and overlays are unchanged.

Tested commit `46aec50260d59efea8ba1ae3c4c440e29c450f84` plus the existing dirty tree and B.1 changes. Split preregistration is `46aec50`; split SHA256 `6856df32859cd1f81e035de4f4f3ae880e5b0dbebaeda08e78ae0f0e248e0ac0`. All 91 protected Phase B/source/fixture bytes, including the 39 production baseline hashes and consumed BLB/DSK lock, still match. [Validation](docs/evidence/v2-phase-b1/validation.json).

## 2. Phase B unresolved issues

Phase B calibrated unique card/game presence, while provider counts had not been bridged. Its primary-only candidates lost the Uldaros prior. Full contextual coverage was 4,647 decisions / 112 drafts, versus 354,690 Q-only decisions. BLB/DSK were spent. B.1 uses a separate namespace and never reruns their performance or changes Phase B artifacts.

## 3. Production provider count semantics

`ever_drawn_win_rate` and `ever_drawn_game_count` pass unchanged through the production DTO, mapper and GIH fields. GIH weights each game by opening-hand instances plus later draw instances. Copies contribute repeatedly; redraw can exceed deck copies. Later draws exclude tutors and returned-to-hand events. GP weights deck copies. The environment baseline weights GIH by that sample count; production shrinks with k500.

Provider changes matter: GIH was presence-only before March 29, 2021; returned-to-hand exclusion and GP copy weighting were backfilled to February 10, 2022; tutor exclusion to September 1, 2022. From November 12, 2024 OH includes the kept seven before bottoming, rather than the start-turn-one hand. Current provider invalid-game filtering cannot be fully reconstructed here. [Official metric definitions](https://www.17lands.com/metrics_definitions). [Path/cache audit](docs/evidence/v2-phase-b1/provider-semantics.json).

## 4. Historical count definitions

A: `deck>0 AND (opening>0 OR drawn>0)` contributes one card/game outcome. B: `deck>0`, weight `opening+drawn`, repeats that outcome per observed instance. This is the closest documented provider **unit**, not a proven equal cohort or current OH collection process. C: ever-drawn presence is identical to A for these fields; physical card identity/redraw event histories are unavailable. OH, drawn, GP, drawn-without-OH and never-seen denominators remain separate; tutor-only is excluded from GIH. No OH/drawn rate averaging or presence-to-copy universal multiplier is used.

Newer unique draft/build/match/game joins reuse Phase B qualification. Every game stays in one B.1 evidence/target draft group. Basics, sideboard-only and unresolved identities are excluded from Q; rate/count transformations retain actual integer weights.

## 5. Provider bridge experiment

**Direct historical/provider bridge unavailable.** The owned cache contains 2 files, both FRA. No historical set/cohort overlaps those aggregates. GIH MAE, count ratios/correlations and duplicate-sensitive bias **against provider aggregates** are unavailable. Same-name cross-set substitutions are prohibited.

Within owned rows, `#GIH=#OH+#drawn` is checked and recorded. Historical presence-versus-copy comparisons are a separate raw-data experiment, not a provider match. Only documentation/public bulk files were downloaded; zero new aggregate requests. Owned cache bytes stay ignored; raw aggregate rows are not redistributed.

## 6. Duplicate/copy effects

| Set | Copy vs presence rate MAE | Median count ratio | Qualified cards |
| --- | --- | --- | --- |
| DMU | 0.2314% | 1.0457 | 257 |
| BRO | 0.2753% | 1.0330 | 309 |
| WOE | 0.2429% | 1.0480 | 283 |
| LCI | 0.2496% | 1.0485 | 282 |
| MKM | 0.2538% | 1.0397 | 283 |
| OTJ | 0.2476% | 1.0343 | 346 |
| ONE | 0.2325% | 1.0476 | 253 |
| MOM | 0.2603% | 1.0400 | 334 |

The count ratio varies by card, sample size and copy usage; even small mean rate differences change shrinkage strength. Duplicate-heavy means at least 25% of observed deck games have multiple copies. Per-card count differences, rate differences, count/rate correlations, rarity diagnostics, average deck copies, OH/draw overlap, repeated-instance and sample-size strata are in [comparisons](docs/evidence/v2-phase-b1/count-comparisons.json). Rarity is not a score input.

## 7. Compatible shrinkage calibration

| Policy | Calibration MAE | Development MAE |
| --- | --- | --- |
| 0 | 3.7659% | 3.6804% |
| 50 | 2.9644% | 2.9350% |
| 100 | 2.7476% | 2.7411% |
| 250 | 2.6411% | 2.6826% |
| 500 | 2.7028% | 2.7789% |
| 1000 | 2.8331% | 2.9282% |
| 2000 | 2.9671% | 3.0746% |
| EB-count-adjusted | 2.8501% | 2.8317% |

**k250 still beats k500 on documented copy/draw units.** Calibration improves by 0.0617 percentage points; development by 0.0963. Fit uses DMU/BRO/WOE; checks use LCI/MKM/OTJ/ONE/MOM. Older definition eras are excluded, never mixed into this parameter fit.

Primary objective is card-macro MAE averaged equally over six levels, then equally over sets. Evidence requires at least 50 unique card/game presences; targets at least 100. Nested samples take entire SHA-ordered game clusters until requested copy counts are reached, with actual counts/overshoot/shortfalls reported. Copy outcomes are correlated within games. The existing moment EB option uses effective cluster sample size and converts to count units; its calibration median is 69.245195. No significance or causal effect is claimed. [Experiments](docs/evidence/v2-phase-b1/shrinkage-experiments.json). Current k500 remains frozen: current OH collection era and same-cohort bridge are still missing.

## 8. Same-set cross-format data sources

One existing owned FRA Premier/Quick pair, 186 mutually usable card statistics. The historical public source has VOW Quick picks but no Quick game table; new ONE/MOM availability also has no qualifying Quick games. No Traditional-as-Quick substitution, prohibited aggregate acquisition or proprietary source. Target five paired sets is not met. Attribution: [17Lands public datasets](https://www.17lands.com/public_datasets), transformed tables from [TrevorJS/17lands-corpus pinned revision](https://huggingface.co/datasets/TrevorJS/17lands-corpus/tree/766a56bbdb5ebe1ed4c0d3441df694c81ec9f774), CC-BY-4.0 per pinned README. New ONE/MOM bytes are LFS-SHA256 verified; their ETL is reused, not claimed independently proven by Phase B’s older original-CSV spot checks.

Current owned historical metadata predates FRA. Pairing therefore uses unique exact **FRA/provider-name statistical identities**, not recovered printing or Oracle IDs. Generic same-name cross-set pairing remains prohibited. Cache and metadata hashes, exclusions and fetched timestamps are recorded. [Sources](docs/evidence/v2-phase-b1/source-availability.json), [pair audit](docs/evidence/v2-phase-b1/cross-format.json).

## 9. Cross-format relationships

FRA card-rate correlation 0.894457; rank correlation 0.900275. Premier baseline 56.3652%; Quick baseline 57.0469%. Residual quantiles and sample-size strata remain descriptive. One previously viewed set cannot identify set dependence or validate a global/directional reliability. Format populations, selected players, windows and game weights differ.

## 10. Sparse-prior contract

Research `QualityEvidence` identifies set, format, card, rate/count, count units, source, time period, primary status and provenance. `QualityPriorResult` retains both sources, mode, effective rate, Q, reliability and shrinkage.

Usable primary wins. Unusable primary plus credible exact same-set/card, different authorized format, compatible documented units/period and provenance can use an explicitly labeled alternate prior:

`effectiveRate = clamp(primaryBaseline + .9 * n_alt/(n_alt+500) * (p_alt-altBaseline), 0, 1)`

No credible alternate yields Q=.5. Unknown units, wrong set/card/direction, inconsistent period and missing provenance decline. The optional bounded reference clips the delta to +/- .08. Reference .9/500 was preregistered and preserves the existing successful concept; it is **not fitted or qualified generalized reliability**. A generalized policy declines without multi-set evidence. There is no FRA/Uldaros branch in the contract. Production FRA policy is unchanged. [Contract](tools/DraftTG.HistoricalEvaluation/phase_b1_core.py).

## 11. Hold-one-format-out results

| Direction | Policy | GIH MAE | Q MAE | Sign accuracy | >=5pp overestimate |
| --- | --- | --- | --- | --- | --- |
| PremierDraft->QuickDraft | neutral | 2.6533% | 0.117566 | 58.6022% | 10.2151% |
| PremierDraft->QuickDraft | reference | 1.2111% | 0.060487 | 84.9462% | 0.0000% |
| PremierDraft->QuickDraft | bounded | 1.2111% | 0.060487 | 84.9462% | 0.0000% |
| PremierDraft->QuickDraft | generalized-unqualified | 2.6533% | 0.117566 | 58.6022% | 10.2151% |
| QuickDraft->PremierDraft | neutral | 2.4435% | 0.143490 | 60.7527% | 5.9140% |
| QuickDraft->PremierDraft | reference | 1.2073% | 0.067958 | 84.9462% | 0.0000% |
| QuickDraft->PremierDraft | bounded | 1.2073% | 0.067958 | 84.9462% | 0.0000% |
| QuickDraft->PremierDraft | generalized-unqualified | 2.4435% | 0.143490 | 60.7527% | 5.9140% |

The focal primary rate is hidden from both prediction and primary environment baseline. Q targets recompose held true primary with k500; raw-relative Q error is also exported. Neutral uses the masked baseline. Rank correlations, >=2pp tails, catastrophic percentile-rank errors and primary-sample buckets are exported. Bounded and reference coincide here; this is an observed result, not proof the bound is universally inactive. This one already observed set is a diagnostic, not independent multi-set holdout qualification.

## 12. Uldaros portability

Generic reference contract reproduces production Q=0.976513757294, exact score=48.219986250158 (48 EST), with unchanged production contextual components/recomposition. Primary Quick row has unusable GIH; alternate Premier evidence retains its separate provenance. Golden fixture names were not calibration targets. Unknown generalized reliability does not turn this reference reproduction into permission to broaden production. [Golden](docs/evidence/v2-phase-b1/golden-portability.json).

## 13. Contextual replay profiler

Frozen detailed sample: 336 decisions / 8 drafts. Before cache: 5.682s total; 4.380s engine; 4.777s adapter; 0.541s store/parse; 0.341s serialization/writer. 3,814 optimizer calls. Engine scoring dominates. Timers for pair projections versus other recommendation components are not separately exposed; no invented breakdown.

Phase B detailed four-model output was about 397 MB for 3,999 training decisions. B.1 retains actual optimizer work and every candidate but omits per-card trace serialization during bulk. Setup/catalog parsing, store I/O, adapter/engine and Q-only recomposition have separate measurements. An additional 3,999-decision cache benchmark is recorded; it overlapped bulk activity, so its timings are contention-affected diagnostics rather than isolated speed claims.

## 14. Replay optimizations

Only historical infrastructure changes: opt-in immutable provider-row/environment caches; copied snapshot and bonus containers; one parsed catalog per run. Default adapter behavior remains uncached. No optimizer approximation, candidate removal, scoring-engine edit, incremental pool approximation or thread-sharing.

Detailed isolated sample: 5.682s -> 6.238s (single run; no reliable net speedup claimed). Non-engine adapter remainder fell from 0.397s to 0.201s, while engine runtime varied. Cached compact sample: 5.289s; identical aggregates; detailed trace output omitted. Bulk preserves existing canonical import/store and real production scoring. It substitutes only experimental Q afterward, retaining already computed relevance R, L/A/D, weights, deck projections, commitment and source tie breaks. This is an isolation experiment, not a rerun V2 engine. No parallel agent work or draft-level score approximations.

## 15. Replay equivalence

**PASS.** 336 decisions / 8 drafts have identical complete detailed output before/after cache. Canonical UTF-8/LF content SHA256 `f3c108c0a59a8a8c90bf8f50427be08218bad674073bd3ffd9c3aa83f92643b2`; raw trace bytes also compare equal. All score fields, exact scores/ranks, selected deck projections, commitment, explanations, provenance, observation history and declines are included; timings alone are excluded. Targeted tests repeat sequential history across all three frozen versions and prove caller-array mutation cannot corrupt cached evidence. [Equality](docs/evidence/v2-phase-b1/replay-equivalence.json).

## 16. Expanded contextual corpus

| Set | Selected target drafts | Imported decisions | Prequalified comparative |
| --- | --- | --- | --- |
| DMU | 160 | 6646 | 6172 |
| BRO | 160 | 7159 | 6682 |
| WOE | 160 | 6694 | 6216 |
| LCI | 160 | 7160 | 6683 |
| MKM | 160 | 6196 | 5720 |
| OTJ | 160 | 6720 | 6240 |
| ONE | 160 | 6616 | 5950 |
| MOM | 160 | 7092 | 6622 |

**54,283 decisions / 1,280 drafts replayed; 52,845 complete offered-pack scores; 50,466 ranked comparative decisions; 413,381 card occurrence scores.** 50,285 comparative decisions also passed the conservative complete-pool/identity qualification before scoring, exceeding 50k. Phase B covered 4,647/112. Selection is lowest B.1 hash within target draft groups, 160 drafts/dataset after preregistered 128/160/192 count-based extension. Own target-draft games cannot enter fitted pick-time aggregates. Existing pre-pick inventory and observed-pack history are retained. [Selection/import manifests](docs/evidence/v2-phase-b1/replay-selection.json).

The aggregate production-behavior comparison includes 197 explicitly flagged partial-pool attempts; these do not count toward conservative complete-input coverage. Unsupported pick15 and unresolved offered identities decline without ranking a truncated pack. Missing inputs and exact reason counts are exported rather than silently imputed as complete.

Runtime 617.997s plus setup 0.415s; 87.84 decisions/s; peak 281.38 MiB; 686,463 optimizer calls; engine 595.504s. Import timings/store hashes and peak measurements are preserved. Aggregate output avoids large row-level diagnostic traces.

## 17. New split manifest

Committed before new-set model analysis: **`46aec50`**. Calibration retains nine registered sets, but compatible-unit fitting uses DMU/BRO/WOE only. Development LCI/MKM/OTJ plus **ONE/MOM**. New final holdouts **MH3/DFT** remain untouched: only pinned source metadata availability was checked, no outcome/card-rate files downloaded or evaluated. BLB/DSK remain SPENT and unavailable to the B.1 runner. There is no new-holdout release command. [Immutable manifest](V2_PHASE_B1_SPLIT_MANIFEST.json).

## 18. Q-only results

| Set | Target decisions | Current Top1 | Candidate Top1 | Current Top3 | Candidate Top3 |
| --- | --- | --- | --- | --- | --- |
| DMU | 13989 | 28.6868% | 29.1872% | 64.1147% | 64.8224% |
| BRO | 14707 | 25.1173% | 25.3553% | 59.0671% | 59.8015% |
| WOE | 13229 | 29.8208% | 30.0703% | 65.3186% | 65.7722% |
| LCI | 14413 | 26.3235% | 26.9687% | 61.4931% | 62.2494% |
| MKM | 12381 | 29.9572% | 30.5630% | 67.1109% | 67.7005% |
| OTJ | 14184 | 25.6275% | 25.9024% | 62.2673% | 62.5282% |
| ONE | 13012 | 29.5189% | 29.7034% | 64.8555% | 65.1552% |
| MOM | 13921 | 25.2712% | 25.4364% | 57.7617% | 58.5734% |

All 109,836 eligible target-group decisions in the eight compatible datasets are evaluated. Q-only results are separate from contextual results and never include the evidence group’s self-labels. Sparse-to-mature MAE/Q-MAE, rank correlation, sign accuracy, tails, actual sample counts and per-set metrics are in [experiments](docs/evidence/v2-phase-b1/shrinkage-experiments.json). Only current k500 and compatible k250 are compared; both use the same production delta normalization. Phase B set-normalized delta is already this family, so no redundant second candidate or reopening B/C. No legitimate alternate historical game table exists; historical missing primary remains explicit neutral, while portable priors are tested separately.

## 19. Contextual results

| Model | Eligible | Top1 | Top3 | MRR | Normalized rank |
| --- | --- | --- | --- | --- | --- |
| current | 50466 | 37.5163% | 72.0941% | 0.577288 | 0.297751 |
| candidate | 50466 | 37.4985% | 72.1416% | 0.577039 | 0.299109 |

Compared 50,466; changed orderings 25,218; actual-pick rank improved 3,142, regressed 2,859. All cards use the actual production baseline once; only Q is recomposed. Existing commitment/relevance and contextual evidence remain frozen. Human selection is revealed preference, not correctness. Per-set, pack/pick and common commitment buckets are in [replay](docs/evidence/v2-phase-b1/contextual-replay.json). No human-agreement selection or causal benefit claim.

## 20. Outcome-stratified descriptive results

| Reliable terminal Bo1 label | Compared | Current Top1 | Candidate Top1 | Rank improved | Regressed |
| --- | --- | --- | --- | --- | --- |
| 0-2 wins | 17145 | 36.6404% | 36.5879% | 1064 | 1023 |
| 3-4 wins | 10621 | 36.7385% | 36.6820% | 686 | 601 |
| 5 wins | 3981 | 38.7591% | 38.5582% | 236 | 222 |
| 6 wins | 2728 | 38.7463% | 38.7097% | 175 | 149 |
| 7 wins | 5581 | 39.2044% | 39.4374% | 356 | 293 |
| unknown | 10410 | 38.0500% | 38.0692% | 625 | 571 |

Low/average/5-win/6-win/trophy labels are joined only after scoring. Nonterminal or unsupported records remain unknown rather than invented final outcomes. No record is a pick-time input, sampling filter or shrinkage target. Strata reflect player/deck/format selection and do not demonstrate causal draft improvement.

## 21. Golden regressions

Generic Uldaros portability passes. Production Unsummon retains C=0.944606413994, R=0.102623906706, P=0, Q=0.578646882534, exact score=25.328185285151; Amphisbaena remains ahead. All production baseline hashes are unchanged. Build/test evidence: `{"commit": "46aec50260d59efea8ba1ae3c4c440e29c450f84", "dirtyTree": true, "buildPassed": true, "warnings": 0, "errors": 0, "CSharpPassed": 1293, "CSharpFailed": 0, "CSharpSkipped": 0, "PythonB1Passed": 7, "PythonExistingPassed": 10, "GoldenPassed": 40, "preservedFiles": 91, "baselineHashesPreserved": 39, "sourceFingerprint": "ae1fe82c9134b3c06fa6759749ebd878eb3d6e983a65346c8348defe0c1f64bd", "productionScoreAssemblySha256": "da89fef042040ac4e4335112824214f2309c0888e592444ec50c8a8e3a187bf6", "logs": "data\\historical\\evaluation\\phase-b1\\validation", "newHoldoutsUntouched": true, "productionChanged": false, "BuildTestSourceFingerprint": "a7a85486db8b8805d75e3b979b1e30302145d59907d3cc33ba884b22abc90dc7", "PostValidationPythonB1Passed": 7, "PostValidationChanges": "Report rendering correction and removal of unused duplicate prior diagnostics; analytical methods unchanged; final prior/equivalence tests rerun."}`. [Machine validation](docs/evidence/v2-phase-b1/validation.json).

## 22. Remaining limitations

1. One already observed paired set, fewer than five; no independent generalized cross-format reliability.
2. Provider-name identities are adequate for cache diagnostics but do not recover FRA printing/Oracle metadata.
3. No matching provider historical cohort; exact bridge metrics remain unavailable.
4. Pre-2024 OH collection differs from current kept-seven semantics; compatible units are not identical observation processes.
5. Provider whole-game invalidity filters cannot be fully reconstructed from transformed tables.
6. Whole-draft bounded public samples and selected 17Lands population; mature replay is not a chronological availability backtest.
7. Shared game outcomes across copies/cards; no independent-card confidence interval or significance claim.
8. No historical Quick game tables, Arena order, pair statistics or successful-deck corpus; production missing-input behavior remains.
9. Frozen pick15/domain/42-pick assumptions persist. No L/A/D/commitment change.
10. Profile subcomponent granularity is limited; cache reduces measured setup remainder, but no reliable net runtime gain is established.
11. Q-only recomposition freezes R and deck projections; no full candidate-driven V2 engine evaluation.
12. Two new holdouts remain unconsumed, so no final generalization approval.

The calibrator/transform/replay sources were preserved at selection; later prior diagnostics corrected golden contextual recomposition and removed the masked focal rate from the diagnostic baseline. Final prior boundary checks reject unknown units. Fitted parameters, historical count formulas, ranking outputs and new-holdout policy were not retuned. Execution/source identity evidence distinguishes final harness from the selection-stage fingerprint.

## 23. Recommendation for Phase C

**Retain production v1.6.2, k500 and the narrow FRA prior. V2 Q is not qualified for implementation.** k250 remains promising research on documented count units, and the generic reference contract removes the conceptual primary-only portability failure. Multi-set reliability, modern OH/cohort transport and future holdout evaluation remain required.

It is safe to separately scope/preregister offline Phase C lane research using the already derived descriptors while freezing production Q and preserving MH3/DFT. It is not safe to infer that V2 integration or a production lane change is approved. **Phase C is not started.**

Supported commands (from repository root):

```powershell
python .\tools\DraftTG.HistoricalEvaluation\phase_b1.py audit
python .\tools\DraftTG.HistoricalEvaluation\phase_b1.py prior
python .\tools\DraftTG.HistoricalEvaluation\phase_b1.py golden
# Initial fresh study namespace only: acquire, qualify, train, prepare-replay.
# train reads the published calibration when selection already exists.
python .\tools\DraftTG.HistoricalEvaluation\phase_b1.py profile-before
python .\tools\DraftTG.HistoricalEvaluation\phase_b1.py profile-after
python .\tools\DraftTG.HistoricalEvaluation\phase_b1.py replay
python .\tools\DraftTG.HistoricalEvaluation\phase_b1_tests.py
python .\tools\DraftTG.HistoricalEvaluation\test_phase_b.py
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
python .\tools\DraftTG.HistoricalEvaluation\phase_b1.py summary
python .\tools\DraftTG.HistoricalEvaluation\phase_b1.py report
```

Pinned public development acquisition verifies LFS SHA256; task-local PyArrow26 reuses Phase B’s installed dependency. Canonical selected rows import through the existing strict hash-cached importer/store. Raw/cache/SQLite/trace files stay ignored. All final commands refuse protected new/spent holdout sets. Reproduction on a fresh separately identified workspace is a verification run, not a newly untouched reuse of spent outcomes; no lock is deleted/reset.
