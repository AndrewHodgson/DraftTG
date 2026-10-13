# Contextual Pick Score V2 — Phase B: Set-Normalized Quality Research

## 1. Executive conclusion

**Do not adopt a production V2 Q model.** The research run and single final holdout release are finished, but full Phase B qualification is not achieved. The corpus scale target is met; legitimate multi-set Quick/Premier quality evidence is unavailable. Every primary-only candidate loses the existing Uldaros alternate prior, and selected candidate A reduces contextual holdout human agreement.

Candidate A (simple shrunk delta, k=250) improves disjoint-game holdout card MAE from 2.4565% to 2.4038%: 0.0527 percentage points, 2.1467% relative. This modest predictive gain is not evidence of better drafting or causal card power. No statistical significance or causal improvement is claimed.

Production `contextual-pick-score-v1.6.2`, its FRA-only prior, L/A/D, commitment, `pick-score-tiers-v2`, memory, localization and overlay behavior remain unchanged. The 39 baseline source/fixture hashes match the Phase A freeze.

Tested build: commit `d2cb723216cd39b6be8c36b1e0e8a8634a49e2a3` plus the existing dirty working tree and Phase B changes; source fingerprint `1874648d366137d0bc15a54b83b687b77ee43ed3df6f061bbf51711924ba98d0`. Score assembly SHA256 `05ba6a963f86e7c5b951099f89b01b42fa983257eb2e769412123feaf303f076`. [Build/test evidence](docs/evidence/v2-phase-b/validation.json).

## 2. Expanded corpus

14 sets, 15 set/format datasets, PremierDraft, QuickDraft; 2021–2024 release environments. 9,800 pick-bearing drafts; 96,597 distinct set/format/draft groups across picks and games (overlap deduplicated), including 93,730 game-bearing drafts. 411,054 imported decisions, 354,690 conservative eligible decisions, and all 354,690 eligible decisions evaluated for Q-only revealed-preference ranking. 542,718 qualified atomic/uniquely joined games. Actual frozen contextual replay is a separately identified subset of 4,647 decisions / 112 drafts, 4,007 ranked decisions.

| Set | Format | Partition | Pick drafts | Imported | Eligible | Qualified games |
| --- | --- | --- | --- | --- | --- | --- |
| AFR | PremierDraft | Calibration | 700 | 28511 | 26428 | 20000 |
| BLB | PremierDraft | Holdout | 700 | 27114 | 25030 | 46705 |
| BRO | PremierDraft | Calibration | 700 | 31145 | 29075 | 46850 |
| DMU | PremierDraft | Calibration | 700 | 29157 | 27076 | 41528 |
| DSK | PremierDraft | Holdout | 700 | 29191 | 27109 | 46820 |
| KHM | PremierDraft | Calibration | 0 | 0 | 0 | 20000 |
| LCI | PremierDraft | Development | 700 | 31369 | 29279 | 46988 |
| MKM | PremierDraft | Development | 700 | 27040 | 24964 | 46639 |
| NEO | PremierDraft | Calibration | 700 | 31281 | 29199 | 47397 |
| OTJ | PremierDraft | Development | 700 | 29194 | 27113 | 46260 |
| SNC | PremierDraft | Calibration | 700 | 28765 | 26719 | 46646 |
| STX | PremierDraft | Calibration | 700 | 30539 | 28463 | 20000 |
| VOW | PremierDraft | Calibration | 700 | 29326 | 27233 | 20000 |
| VOW | QuickDraft | Calibration | 700 | 29348 | 0 | 0 |
| WOE | PremierDraft | Calibration | 700 | 29074 | 27002 | 46885 |

Selection was fixed by calendar/mechanical diversity, not agreement with v1.6.2: snow/foretell, lessons/spells, dungeons, graveyard/vampires, artifacts/enchantments, three-color, domain, adventures, discovery, investigate, outlaws, typal and enchantment environments. Fast/slow/power labels were not fitted outcomes or selection filters. KHM has games but no Parquet picks; it remains registered, with zero pick rows. VOW Quick has picks but no permitted game table. No Traditional-as-Quick substitution.

Modern draft samples use the lowest outcome-independent SHA256 whole-draft IDs (700/dataset) over every source row group; modern games use 8,000 such drafts/dataset. Source incomplete/abandoned drafts stay represented; eligible picks weight observable decisions, not each drafter equally. The four 2021 game sources use original 20,000-row prefixes because Parquet lost distinguishing game keys. Those prefixes are not random, not full-format maturity, and can bias calibration. Public 17Lands users are a selected population. “Full” later means the entire qualified evidence sample, never the entire released dump.

Data attribution: [17Lands public datasets](https://www.17lands.com/public_datasets), transformed draft/newer-game tables from [TrevorJS/17lands-corpus](https://huggingface.co/datasets/TrevorJS/17lands-corpus/tree/766a56bbdb5ebe1ed4c0d3441df694c81ec9f774), pinned revision `766a56bbdb5ebe1ed4c0d3441df694c81ec9f774`, CC-BY-4.0 per pinned source README. Only bulk picks/pack/pool/games/game_cards and card metadata were acquired; no targets, embeddings, replay model outputs or private endpoints. All 72 pinned file bytes were SHA256-verified. Five original CSV spot checks covered 5,000 picks with zero missing rows or pack/pool/picked-name differences; this is not an exhaustive ETL proof. [Sources/hashes](docs/evidence/v2-phase-b/source-provenance.json), [source audit](docs/evidence/v2-phase-b/source-audit.json).

Raw Phase B disk: 2.740 GiB; normalized including replay stores: 626.07 MiB; all retained Phase B data/dependency/replay traces: 3.868 GiB. Individual source files are compressed Parquet; readers filter 131,072-row batches. Canonical JSONL/gzip reuses `HistoricalDraftDecision`, not a second scoring schema. Full contextual replay was bounded to eight preregistered hash-selected drafts/dataset because optimizer work and detailed JSONL scale much more heavily than Q-only ranking. Row-level data stays ignored.

Production replay: training 48.794s / 81.96 decisions/s / 334.31 MiB peak; holdout 8.893s / 72.86 decisions/s / 370.46 MiB. Holdout quality processing 15.192s / 406.22 MiB peak (excludes subsequent contextual replay). Import manifests preserve parse/store rows/s, peak and hash-cache keys; full process timings include startup/preflight. A completed AFR import was reused after the logging fix with matching raw/catalog/split/options hashes. Training analysis/export finished, but the final Win32 telemetry write failed; wall time/peak are explicitly unavailable. The supported launcher supplies the 64-bit HANDLE prototype. Selection was not rerun. Existing older Phase A intermediate outputs remain ignored; no user files were cleaned up. [Storage](docs/evidence/v2-phase-b/storage.json), [run metadata](docs/evidence/v2-phase-b/training-result.json).

Canonical replay parse/store speed: 1559.98–1883.27 rows/s, excluding SHA preflight. Recorded draft qualification stages total 161.252s (2549.13 decisions/s); this includes source long-table scanning/joins and canonical gzip output, excludes metadata loading and separately rejected old-game attempts.

## 3. Frozen split manifest

Registered and committed before candidate analysis as **`d2cb723`**. Manifest SHA256 `64B9C3F76C44A56B18F32D35B4B29E06B875F1400FE2D92A95EB176B813A80F2`; plan SHA256 `F60C70CAAA3F60A342E2203CCDAF3D0EFFD6F5261DC032C442149F2A7CA0147D`.

- Calibration: KHM, STX, AFR, NEO, SNC, DMU, BRO, VOW, WOE.
- Development: LCI, MKM, OTJ.
- Untouched final holdout: BLB, DSK.

OTJ was already viewed in Phase A and was deliberately burned into development. No pick-level set split. Within each dataset, SHA256(`DraftTG-phase-b-v1/17LandsPublic/set/format/draft_id`) chooses disjoint evidence and target draft groups; all games in a draft stay together. Sampling hashes do not use outcomes. Qualification handled holdout schema, identity, count and missingness only; candidate performance/distributions were locked until selection.

Parameters were fitted on calibration; fitted fixed/adaptive/EB/decomposition families were chosen on development using set-macro card MAE averaged equally over six levels. Selected A/k250 was frozen at `2026-10-13T19:33:32.559802+00:00`. The [original analytical selection](docs/evidence/v2-phase-b/selection-analysis-freeze.json) is preserved byte-for-byte. A later CLI progress/JSON parsing correction changed only the execution fingerprint; [correction evidence](docs/evidence/v2-phase-b/execution-correction.json) and a deep equality check confirm all formulas, parameters, missing rules and metrics unchanged. [Current selection](docs/evidence/v2-phase-b/selection.json).

The exclusive final release lock was consumed once before reading holdout outcomes and completed at `2026-10-13T19:43:42.064662+00:00`. Both BLB and DSK were evaluated together, including the contextual subset, in that release. No post-holdout retuning. A consumed or failed release cannot be retried. BLB/DSK are now spent holdouts and must not be called untouched in another tuning study.

## 4. Identity coverage

12,063,598/12,063,598 offered/pool/label occurrences resolve: **100.0000% logical score identity**, including basics. Score-relevant nonbasic coverage is also 100%. This improves Phase A’s 95.3959%, but is not 100% exact printing identification.

595 set/source-name identities use explicit research logical IDs because all declared Arena printing candidates share one Oracle ID and identical gameplay fields/faces. Every original printing candidate remains in mapping provenance; no printing is selected arbitrarily. Single printing matches stay exact. Mixed printing rarity, where present, is not a quality input; neutral decoder metadata is not proof of original printing rarity. Rebalanced A- names stay separate, tokens/non-draft objects decline, and generic cross-set name fallback is prohibited. MKM List exceptions are limited to [publisher-enumerated Arena List names](https://magic.wizards.com/en/news/announcements/whats-new-on-the-list-for-murders-at-karlov-manor), including the documented Arena swaps; consensus is still required.

| Observed identity category | Occurrences |
| --- | --- |
| DFC naming | 438,470 |
| basic lands | 128,670 |
| bonus sheet | 236,097 |
| exact/normalized name | 10,370,328 |
| explicit MKM Arena List | 5,238 |
| same-name printings | 674,347 |
| special guest | 2,972 |
| split/adventure cards | 207,476 |

No unresolved vocabulary entries remain in this sample. DFC front names, split/adventure faces, basics, art variants, explicit sheets/guests and MKM List are classified. Token, genuinely conflicting Oracle/gameplay, rebalanced-name substitution, collector discrepancy, unavailable metadata and malformed-schema declines remain supported categories; zero occurrences does not prove those failure modes impossible. Collector/printing identity cannot be recovered from a name-only source. Helper, owned Scryfall cache and policy hashes are in [identity evidence](docs/evidence/v2-phase-b/identity-summary.json); the full mapping/candidate list remains in ignored normalized storage. Legacy strict Phase A resolver behavior is unchanged; the research catalog uses explicit proven logical identities.

## 5. Exclusion waterfall

| Set | Format | Imported | Schema | Pack | Identity | Inputs | ≥2 | Eligible | Eligible % |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| AFR | PremierDraft | 28,511 | 28,511 | 28,511 | 28,511 | 28,511 | 26,428 | 26,428 | 92.6940% |
| BLB | PremierDraft | 27,114 | 27,114 | 27,114 | 27,114 | 27,114 | 25,030 | 25,030 | 92.3139% |
| BRO | PremierDraft | 31,145 | 31,145 | 31,145 | 31,145 | 29,075 | 29,075 | 29,075 | 93.3537% |
| DMU | PremierDraft | 29,157 | 29,157 | 29,157 | 29,157 | 29,157 | 27,076 | 27,076 | 92.8628% |
| DSK | PremierDraft | 29,191 | 29,191 | 29,191 | 29,191 | 29,191 | 27,109 | 27,109 | 92.8677% |
| LCI | PremierDraft | 31,369 | 31,369 | 31,369 | 31,369 | 29,279 | 29,279 | 29,279 | 93.3374% |
| MKM | PremierDraft | 27,040 | 27,040 | 27,040 | 27,040 | 27,040 | 24,964 | 24,964 | 92.3225% |
| NEO | PremierDraft | 31,281 | 31,281 | 31,281 | 31,281 | 29,199 | 29,199 | 29,199 | 93.3442% |
| OTJ | PremierDraft | 29,194 | 29,194 | 29,194 | 29,194 | 29,194 | 27,113 | 27,113 | 92.8718% |
| SNC | PremierDraft | 28,765 | 28,765 | 28,765 | 28,765 | 28,765 | 26,719 | 26,719 | 92.8872% |
| STX | PremierDraft | 30,539 | 30,539 | 30,539 | 30,539 | 28,463 | 28,463 | 28,463 | 93.2021% |
| VOW | PremierDraft | 29,326 | 29,326 | 29,326 | 29,326 | 29,326 | 27,233 | 27,233 | 92.8630% |
| VOW | QuickDraft | 29,348 | 29,348 | 29,348 | 29,348 | 0 | 0 | 0 | 0.0000% |
| WOE | PremierDraft | 29,074 | 29,074 | 29,074 | 29,074 | 29,074 | 27,002 | 27,002 | 92.8734% |

| Stage | Remaining | % of imported |
| --- | --- | --- |
| imported | 411,054 | 100.0000% |
| validSchema | 411,054 | 100.0000% |
| validOfferedPack | 411,054 | 100.0000% |
| identitiesResolved | 411,054 | 100.0000% |
| scoreInputsAvailable | 373,388 | 90.8367% |
| atLeastTwoChoices | 354,690 | 86.2879% |
| eligible | 354,690 | 86.2879% |

Schema/pack/identity exclusions are zero here. Missing exact-format game evidence excludes all 29,348 VOW Quick decisions; supported-domain pick coordinates exclude historical pick 15 in 15-pick sets; singleton packs are excluded from comparative ranking. Source pool completeness requires count==global source index; unresolved pool cards decline eligibility. Those final reasons are disjoint in the waterfall. Per-set exact reason counts are in [qualification](docs/evidence/v2-phase-b/qualification.json). “Inputs available” means an exact-format usable environment plus complete identity/inventory and supported coordinates, not that every card has measured GIH; missing individual GIH remains explicit neutral Q. Actual contextual availability is separately verified in the 4,647-decision production subset. No truncated-pack rankings or hidden outcome filtering.

## 6. Outcome/build coverage

**85,472 ObservedPrimaryBuilds**, versus 960 observed builds in the pilot. Definition: one uniquely most frequent observed main-deck configuration among exact 40-card game decks per draft. Tied modal configurations decline. Configuration identity uses every card quantity, not just build_index. This is stronger evidence of the commonly played main deck, not proof of the final submitted draft deck.

| Set | Games | Configurations | 40-card game decks | Primary builds | Modal ties | Opening-key/modal agreement |
| --- | --- | --- | --- | --- | --- | --- |
| AFR | 20000 | 4252 | 18759 | 3120 | 98 | 53 |
| BLB | 46705 | 9741 | 44194 | 7427 | 185 | 6591 |
| BRO | 46850 | 10891 | 43201 | 7119 | 312 | 5876 |
| DMU | 41528 | 10074 | 38876 | 7256 | 261 | 5594 |
| DSK | 46820 | 9951 | 43177 | 7248 | 190 | 6375 |
| KHM | 20000 | 4492 | 18949 | 3112 | 122 | 36 |
| LCI | 46988 | 10292 | 42963 | 7135 | 233 | 6198 |
| MKM | 46639 | 9978 | 44291 | 7430 | 198 | 6490 |
| NEO | 47397 | 10309 | 44591 | 7354 | 202 | 6307 |
| OTJ | 46260 | 9953 | 43165 | 7301 | 202 | 6442 |
| SNC | 46646 | 10163 | 43810 | 7318 | 236 | 6251 |
| STX | 20000 | 4362 | 18850 | 3198 | 80 | 44 |
| VOW | 20000 | 4418 | 18672 | 3149 | 105 | 52 |
| WOE | 46885 | 10285 | 43978 | 7305 | 233 | 6289 |

Individual observed game decks and sideboard counts remain available. Unique modal 40, opening-key observed deck, post-sideboard build, inferred primary build and final submitted deck are distinct. Match 1/game 1 gives an opening-key observation where available; old game numbering may reset, and missing initial games prevent a true pre-sideboard claim. **Zero proven final submitted main decks/sideboards**; no modal sideboard/full pool is invented. Source pick maindeck/sideboard rates and terminal supported Bo1 records stay analysis labels, never scoring inputs. Nonterminal records are reported records, not completed events.

Older Parquet draft/build/game joins were materially ambiguous; [qualification](docs/evidence/v2-phase-b/qualification.json) preserves rejected-key counts. Original wide CSV rows keep result and card counts atomic, with source row ordinal; they are not deduplicated by an ambiguous game number. Newer draft/build/match/game keys were unique in the sampled corpus. Observed-build association uses unique modal builds with ≥3 observed games, high observed game WR≥.6 vs lower; presence counts a card once per build, not copies. It is descriptive, selection-biased, and does not use final draft wins as a simplistic card-quality target. [Associations](docs/evidence/v2-phase-b/training-build-association.json).

## 7. Derived seen/taken-position statistics

4,146 set/format/card feature rows. Each source zero-based pick becomes a **1-based pack-local pick**; global index is a separate source coordinate. Seen means/counts include offered multiplicity; Taken means/counts include actual picked names. Mean, median, complete frequency distribution, counts, and pack-specific Seen distributions exist in ignored `*.positions.json`. Portable means/medians/counts/CDF descriptors are in [derived positions](docs/evidence/v2-phase-b/derived-positions.json).

The audit checks histogram count conservation, weighted mean recomputation, positive 1-based indexes and pack-specific totals for every row. The five original-source pack/pool checks additionally validate underlying offered counts. Set normalization uses empirical midrank percentiles over per-card means and `SeenLaterThanSetExpectation = card_seen_mean - unweighted_set_card_seen_mean`; this expectation is explicit, not a new lane formula. Taken percentile is separate. Low Seen/Taken counts remain visible, not imputed.

These are `DerivedAverageSeenAt` / `DerivedAverageTakenAt`, not official ALSA/ATA. There is no licensed overlapping published aggregate in this corpus; correlation/error against official ALSA/ATA is **unavailable**, not zero. Current aggregate API explicitly disallows outside use, so it was not harvested into research. No position signal enters Q, production L, A, D or commitment. [Position audit](docs/evidence/v2-phase-b/position-audit.json).

## 8. Available card-performance fields

Source long game-card fields: `opening_hand`, `drawn`, `tutored`, `deck`, `sideboard`; game fields: `won`, source draft/build/match/game keys, rank, and `main_colors`/`splash_colors` where present. Original wide sources use those prefixes with each source card name. Draft sources supply `pack_card`, `pool`, `pick`, `pack_number`, `pick_number`, observed pick-maindeck/sideboard rates and reported event record/lifetime buckets.

| Research signal | Exact denominator / meaning |
| --- | --- |
| Game-presence GIH | deck>0 and (opening>0 OR drawn>0), at most one card/game outcome |
| GIH count | count of those unique card/game presences, not sum of copies |
| Copy-weighted GIH audit | sum(opening+drawn) and outcome-weighted same sum; not interchangeable |
| GP presence | deck>0, one card/game; separate copy-GP audit uses deck copies |
| OH presence | opening>0; may overlap drawn presence |
| Drawn presence | drawn>0; may overlap OH presence |
| Drawn-without-OH | drawn>0 AND opening==0, disjoint from OH-presence stratum |
| Never-seen style | deck>0 AND opening==0 AND drawn==0; tutored-only is not added silently |
| Derived IWD | game-presence GIH WR minus Never-seen WR; not declared official provider IWD |
| Pick/Seen counts | source offered/picked occurrences; not game counts |
| Color/rank strata | reported main_colors and source game rank; descriptive post-draft, not intrinsic inputs |

Only nonbasic resolved deck cards enter Q baseline/research; sideboard-only, basic-land and unresolved rows are counted as exclusions. Rates/counts are separately exported for each field. OH+Drawn counts can overlap, so marginal aggregate rates alone cannot reconstruct disjoint game-presence strata. Tutoring is audited but is not silently equated with drawing. Provider GIH/Games/count definitions must be bridged before deploying a k trained on unique presences. [Training fields](docs/evidence/v2-phase-b/training-card-features.json), [holdout fields](docs/evidence/v2-phase-b/holdout-card-features.json).

## 9. Set/format baselines

| Set | Cards | Card/game GIH observations | GIH weighted | Card unweighted | Unique-game WR | Card-rate SD | P05 | P95 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| AFR | 259 | 182,475 | 55.6027% | 53.9746% | 54.9150% | 6.1377% | 43.4783% | 62.0911% |
| BRO | 329 | 449,011 | 55.7630% | 53.4648% | 55.1355% | 8.1407% | 41.9279% | 62.2143% |
| DMU | 260 | 443,163 | 57.2823% | 56.4439% | 56.7352% | 4.5677% | 50.7863% | 63.1716% |
| KHM | 280 | 218,927 | 55.1366% | 54.6680% | 54.3550% | 6.4744% | 48.0715% | 62.3053% |
| LCI | 286 | 446,244 | 56.0738% | 55.3887% | 55.3418% | 4.9818% | 46.8911% | 63.3531% |
| MKM | 321 | 434,222 | 55.4141% | 52.9547% | 55.0312% | 10.0683% | 37.5000% | 63.1526% |
| NEO | 282 | 472,267 | 56.9199% | 55.7411% | 55.5120% | 5.2729% | 48.3146% | 63.9197% |
| OTJ | 374 | 463,411 | 55.3269% | 54.2884% | 54.0380% | 6.4223% | 46.4103% | 61.4355% |
| SNC | 261 | 493,171 | 55.7077% | 54.5360% | 54.8793% | 5.8977% | 46.6667% | 61.9857% |
| STX | 335 | 204,617 | 54.8307% | 53.2015% | 53.9800% | 9.3705% | 42.1053% | 62.9119% |
| VOW | 266 | 191,591 | 54.1414% | 53.6834% | 53.7850% | 5.2219% | 43.4837% | 61.9465% |
| WOE | 319 | 433,743 | 56.2979% | 53.9998% | 55.3503% | 8.8307% | 40.8807% | 62.1187% |
| BLB | 270 | 426,048 | 56.2117% | 55.4713% | 54.9620% | 5.9686% | 48.6629% | 62.4062% |
| DSK | 280 | 441,960 | 55.8838% | 54.1937% | 54.6006% | 7.9453% | 46.8750% | 61.4110% |

Weighted GIH counts each observed card/game once, so a game can contribute several cards. Unweighted GIH averages card rates equally. Unique-game WR counts each game once. Copy-weighted GIH counts each opening/drawn copy; these are different denominators and have separate tables. Baselines and SD for predictions are computed from **evidence drafts only**, never target outcomes. Full qualified evidence+target baselines above are descriptive released-corpus distributions, not fitting inputs. Quantiles P05/P25/P50/P75/P95 and rarity distributions/counts are in [baselines](docs/evidence/v2-phase-b/training-baselines.json) and [holdout baselines](docs/evidence/v2-phase-b/holdout-baselines.json). [Unique-game/rank/color diagnostics](docs/evidence/v2-phase-b/report-diagnostics.json).

## 10. Cross-set GIH distributions

Observed GIH-weighted baselines range from 54.1414% to 57.2823%. A 58% card is therefore not the same relative strength in every environment. Unweighted card means can differ materially from occurrence-weighted baselines because rare/low-played cards receive much more influence when weighted equally.

Unweighted raw card-rate SD ranges from 4.5677% to 10.0683%; low-count extremes inflate it, notably in larger/bonus-sheet vocabularies. A percentile/CDF diagnostic was prepared and measured on calibration/development only after selection, against the common mature delta-Q reference; it preserves monotonic ranking but discards absolute effect size and needs a separate early-data distribution calibration. It was not selected as a fourth candidate or fitted on holdouts. Rarity appears only in stratification/sample diagnostics, never a quality bonus.

Win rates reflect player/deck/card selection, color strength, play decisions, draw timing and survivorship of decks people submit. Unique-game rank/main-color strata demonstrate observational dependence; unknown/lifetime buckets have uncertain temporal scope. No causal card-power claim, archetype clustering, synergy bonus or A formula follows from these diagnostics. Copy vs presence differences are retained per card and must not be washed out by a common name “GIH”.

## 11. Shrinkage experiments

Primary metric: unweighted card MAE, first averaged equally over n=50/100/250/500/1000/full, then equally over whole sets. Weighted MAE, RMSE, Brier, fixed-bin calibration/ECE, bias, rank correlation and ≥2/5pp overestimation are secondary outputs. Target card rates require ≥100 observations; evidence requires ≥50. Targets and evidence are disjoint drafts.

| Policy | Calibration MAE | Development MAE |
| --- | --- | --- |
| fixed k=0 | 3.7306% | 3.5823% |
| fixed k=50 | 3.0212% | 2.8678% |
| fixed k=100 | 2.8348% | 2.6803% |
| fixed k=250 | 2.7769% | 2.6274% |
| fixed k=500 | 2.8580% | 2.7223% |
| fixed k=1000 | 2.9863% | 2.8711% |
| fixed k=2000 | 3.1072% | 3.0090% |
| adaptive-250 | 2.8371% | 2.7093% |
| adaptive-500 | 2.9360% | 2.8136% |
| adaptive-1000 | 3.0433% | 2.9353% |
| EB-calibration-median | 2.8637% | 2.7087% |
| decomposition-250 | 2.7876% | 2.6281% |

Fixed k=250 wins calibration. Adaptive k=k0*sqrt(500/n) fits k0=250 but loses development; the simple moment EB estimate subtracts average binomial sampling variance from between-card variance, caps k to [0,2000], and freezes the calibration median at **87.214205**. EB loses the fixed policy on development. The moment estimator is sensitive to small-count card variance; it is not a causal variance estimate. No set-specific holdout k was fitted. **Retain production k500:** observed improvement on unique-presence samples does not authorize halving the prior on differently counted provider data. [All grid/level results](docs/evidence/v2-phase-b/training-experiments.json).

## 12. Multi-stat experiments

Candidate C divides GIH games into mutually exclusive OH-presence and drawn-without-OH strata, shrinks each toward the evidence baseline with k/2, and mixes using their observed frequencies. Each outcome enters once. k=250 fits calibration; development MAE 2.6281% vs GIH-only 2.6274%; holdout C 2.4163% vs A 2.4038%. **GIH alone is slightly better here.**

OH+Drawn marginal rates overlap; GP is a mixture of seen/not-seen outcomes; IWD is an algebraic difference of already shared outcomes. They were audited, but adding GIH/OH/Drawn/GP/IWD as independent weighted “votes” was rejected as redundant. We did not fit a GP/IWD black-box combination or pretend to exhaust all distinct-signal models. The tested small decomposition does not justify extra complexity. Color/rank/build associations remain descriptive; conditional archetype strength belongs in later A research, not a rarity or intrinsic-Q bonus.

## 13. Cross-format experiments

**Zero legitimate paired Quick/Premier card-quality observations; no reliability estimate or successful multi-set experiment.** Only VOW Quick draft rows exist in the permitted bulk corpus, and its game table is absent. Public Premier/Traditional aggregates in the third-party repository are not a substitute for Quick, and undocumented provider-derived targets were not used. The current aggregate API carries an explicit outside-use prohibition; no such aggregate was imported into research.

The experiment contract supports same-set, different-format name/identity pairing; tests reject cross-set and same-format pairing. Planned comparators are neutral Q=.5, v1.6.1-style .9 reliability/k500 alternate prior, and generalized global/direction/sample reliability. Correlation, baseline offsets, residuals, GIH/Q MAE, sign accuracy and ≥2/5pp overestimation are **unavailable**, not zero. No direction-specific/global/set-specific/sample reliability is identified. Production FRA Quick→Premier .9/k500 remains narrow and unchanged. [Explicit blocked experiment](docs/evidence/v2-phase-b/cross-format-experiments.json). This Phase B success criterion is not met; permitted paired data is required before a general prior proposal.

## 14. Sample-size simulation

| Requested n | Current MAE | A MAE | B Q-MAE | C MAE | Target-qualified cards | Actually reached n |
| --- | --- | --- | --- | --- | --- | --- |
| 50 | 2.8371% | 2.7926% | 0.168292 | 2.8106% | 503 | 503 |
| 100 | 2.7200% | 2.6984% | 0.161004 | 2.7248% | 503 | 502 |
| 250 | 2.4726% | 2.4240% | 0.147071 | 2.4362% | 503 | 450 |
| 500 | 2.3006% | 2.2438% | 0.138198 | 2.2500% | 503 | 332 |
| 1000 | 2.2231% | 2.1539% | 0.134571 | 2.1594% | 503 | 152 |
| full | 2.1858% | 2.1101% | 0.132517 | 2.1167% | 503 | 503 |

Deterministic, nested card/game samples are ordered by SHA256(seed/sample/set/format/card/game-key); selection does not inspect outcomes. Requested n can exceed available observations, so actual minimum/median and fully supported counts are reported, not extrapolated. “Full” is all evidence-group observations in the bounded corpus. Target rates use the other draft group; they remain noisy mature-sample estimates. This is **sample-size simulation, not chronological backtest**, not full-dump/all-time performance. No exact historical statistics availability dates are asserted. Repeated cards in one game count once; all games from one draft stay on one side. [Final sparse metrics](docs/evidence/v2-phase-b/holdout-experiments.json).

Environment baseline and spread use the full evidence group at every n; only the focal card sample is reduced. This models sparse card evidence with an established environment, not jointly uncertain first-week card rates/baselines/spreads. QMAE is an absolute error on the 0–1 Q scale, not win-rate percentage points.

## 15. Candidate Q specifications

All three are `research-q-phase-b-v1`, offline scalar Q in (0,1), neutral .5 when required usable exact-set/format evidence is missing. Evidence provenance: public qualified game-presence observations; no rarity/position/player/deck-win feature. Baseline b is evidence-group card/game-weighted GIH; counts are unique card/game presences. No generalized alternate prior; this missing policy is a demonstrated portability failure for Uldaros.

| Candidate | Inputs and formula | Early / mature behavior |
| --- | --- | --- |
| A | p,n,b; p*= (n*p+250*b)/(n+250); Q=SoftLimit(.5+(p*-b)/.16,.35) | Shrinks sparse estimates toward neutral; converges toward observed relative rate |
| B | Same p*,b plus evidence-group unweighted card-rate SD; Q=SoftLimit(.5+(p*-b)/(4*max(.005,SD)),.35) | Normalizes set spread; noisy low-count extremes can dilute/amplify quality magnitude |
| C | Per-game disjoint OH vs drawn-without-OH outcomes; shrink each with k/2=125 and observed-frequency mix, then A delta normalization | Sparse strata are noisier; missing stratum has zero mixture mass; aggregate marginal rates alone cannot supply the required decomposition |

SoftLimit is linear within .5±.35 and asymptotically approaches 0/1 outside, with the frozen quality soft-limit shape. Format handling is exact set/format only. Missing baseline declines prediction rather than inventing an environment. QMAE uses a common mature delta-Q reference; B’s rate predictions equal A but its Q magnitude differs. Machine specifications and exact selected policy are in [selection](docs/evidence/v2-phase-b/selection.json). These are not production V2 weights/models.

## 16. Calibration results

| Model | Card MAE | Common-reference QMAE |
| --- | --- | --- |
| current-k500 | 2.8580% | 0.162285 |
| A | 2.7769% | 0.157274 |
| B | 2.7769% | 0.165920 |
| C | 2.7876% | 0.157945 |

Nine registered calibration sets, including KHM games without picks. Per-set card counts, evidence/target counts, actual sparse sample sizes, metrics and exclusions are exported. Fixed/adaptive/decomposition k fits and EB calibration median use only these sets. Human revealed-preference agreement and stronger-build presence are secondary; known golden cards were not calibration targets. [Candidate experiments](docs/evidence/v2-phase-b/training-candidate-experiments.json).

## 17. Development results

| Model | Card MAE | Common-reference QMAE |
| --- | --- | --- |
| current-k500 | 2.7223% | 0.157571 |
| A | 2.6274% | 0.151420 |
| B | 2.6274% | 0.162738 |
| C | 2.6281% | 0.151539 |

LCI/MKM/OTJ choose among calibration-fitted families. A/k250 wins the preregistered primary objective; B shares identical probability predictions and loses the complexity tie, while C adds no improvement. The selected MAE is 2.6274% vs current 2.7223%. No choice was made from BLB/DSK performance. Q-only ranking on all eligible decisions and the contextual subset are separately exported; secondary agreement did not replace the primary objective.

## 18. Final untouched holdout results

| Set | Model | Six-level MAE | QMAE | ≥5pp overestimate | Sign accuracy |
| --- | --- | --- | --- | --- | --- |
| BLB | current-k500 | 2.3963% | 0.143371 | 4.5515% | 68.8755% |
| BLB | A | 2.3846% | 0.142196 | 3.8153% | 68.8755% |
| BLB | B | 2.3846% | 0.143569 | 3.8153% | 68.8755% |
| BLB | C | 2.3967% | 0.143126 | 4.2169% | 68.6747% |
| DSK | current-k500 | 2.5168% | 0.147204 | 6.6273% | 71.5223% |
| DSK | A | 2.4230% | 0.141442 | 5.3806% | 71.5223% |
| DSK | B | 2.4230% | 0.150315 | 5.3806% | 71.5223% |
| DSK | C | 2.4359% | 0.142290 | 5.6430% | 71.9160% |

Final set-macro MAE: current 2.4565%, A/B 2.4038%, C 2.4163%. All inputs, targets, missingness/exclusions and sample-level behavior are traceable by set/source/normalized hash. 550 observed card rows; 46,712 evidence games and 46,813 target games. Missing card GIH uses neutral Q for pick-ranking; prediction metrics require evidence≥50/target≥100 and do not score nonexistent measurements. Q-only ranking includes 52,139 eligible decisions; contextual replay includes 648 decisions/16 drafts, 600 eligible.

The [single-use release](docs/evidence/v2-phase-b/holdout-release.json) records selected-input hash and final result hashes. No re-selection or retuning followed. Largest contextual regressions and source coordinates are in [holdout contextual comparisons](docs/evidence/v2-phase-b/holdout-production-replay.json).

## 19. Current v1.6.2 Q comparison

| Partition | Model | Q-only N | Q-only Top1 | Contextual N | Contextual Top1 | Contextual MRR |
| --- | --- | --- | --- | --- | --- | --- |
| training | currentQ | 302551 | 26.9158% | 3407 | 38.7144% | 0.584605 |
| training | A | 302551 | 27.2338% | 3407 | 38.5090% | 0.583693 |
| training | B | 302551 | 27.2338% | 3407 | 39.5069% | 0.592305 |
| training | C | 302551 | 27.2490% | 3407 | 38.8318% | 0.585382 |
| holdout | currentQ | 52139 | 29.0876% | 600 | 43.0000% | 0.621592 |
| holdout | A | 52139 | 29.1778% | 600 | 41.1667% | 0.612748 |
| holdout | B | 52139 | 29.1778% | 600 | 42.3333% | 0.620499 |
| holdout | C | 52139 | 29.1989% | 600 | 41.5000% | 0.614261 |

The offline ablation uses the **actual production v1.6.2 result once**, substitutes only Q, and retains L/A/D, weights, source evidence tie breaks, deck optimizer result, commitment and already-computed R. Calibration/ranking reuses the production calibration function. Holding R fixed also prevents experimental Q from changing the quality-tail commitment calculation; this is an isolation experiment, not a fully rerun V2 engine.

Selected A’s contextual holdout Top1 declines from 43.0000% to 41.1667%. It changes 282 orderings, improves actual-pick rank in 29, regresses in 44. Human pick preference is not correctness; nevertheless this regression and the golden failure preclude adoption. Pure Q ranking improves modestly across the large corpus, while contextual composition can behave differently. Fixed-500 probability/Q is the offline current-Q comparator; production itself is unchanged. No unsupported claim of draft win improvement.

## 20. Real regression checks

The full suite includes **40 frozen golden tests**, all passing. Production Uldaros stays Q=0.976513757294, exact score 48.219986250 (48 EST). The research primary-only missing rule makes **A/B/C Q=.5 and score 25**: an explicit **portability FAIL**, not a reason to retune on golden targets or holdouts. Its existing FRA-only prior is preserved in production.

Production Unsummon retains C=.9446064139941691, P=0, R=.10262390670553934, Q=.5786468825336437 and exact score 25.328185285150624, behind Arcane Amphisbaena at 25.501120969070218. The Q-only sensitivity keeps the same commitment/relevance:

| Card | Candidate | Raw Q | Frozen R | Exact score | Pack rank |
| --- | --- | --- | --- | --- | --- |
| Arcane Amphisbaena | A | 0.506068736 | 1.000000000 | 25.518496 | 1 |
| Arcane Amphisbaena | B | 0.507509971 | 1.000000000 | 25.587453 | 1 |
| Arcane Amphisbaena | C | 0.500000000 | 1.000000000 | 25.228131 | 1 |
| Unsummon | A | 0.581366346 | 0.102623907 | 25.341538 | 2 |
| Unsummon | B | 0.600689648 | 0.102623907 | 25.436418 | 2 |
| Unsummon | C | 0.500000000 | 0.102623907 | 24.942018 | 3 |

A/B numbers here illustrate transporting the existing authorized fixture’s published GIH/count into the fitted normalization. Provider copy vs presence semantics are not proven equivalent, so these are sanity sensitivities, not new quality calibration evidence. C requires disjoint game strata unavailable in marginal aggregate fixtures and therefore declines to neutral. No new provider request, live session or golden-target fitting occurred. The recomposition audit exactly reproduces frozen production scores. [Golden sanity](docs/evidence/v2-phase-b/golden-sanity.json).

## 21. Failure modes

1. Multi-set Quick/Premier public game pairs unavailable: cross-format reliability and hold-one-format-out metrics remain unqualified.
2. Primary-only missing rule loses Uldaros’s narrow alternate prior; all candidates fail production portability.
3. A improves card prediction slightly but worsens contextual holdout revealed preference; improvement is not causal drafting benefit.
4. Card-game presence and copy-weighted provider counts differ; learned k250 cannot be transplanted silently.
5. 2021 original game sources are bounded prefixes; modern samples are whole-draft hash samples, not full all-time datasets. Selection/player/deck/color bias remains.
6. Target card rates are noisy; samples across cards share games. No independent-card confidence interval, exact chronology or statistical significance is claimed.
7. Low-count extremes inflate unweighted set SD; B’s normalized range changes contextual influence despite fixed component weights. Percentiles lose effect-size magnitude.
8. Printing-specific art/collector identities and potentially printing-specific rarity remain unresolved where logical consensus is used; no first-match printing claim.
9. Modal observed builds are not final submitted or proven pre-sideboard decks; incomplete game histories and modal ties decline.
10. Historical pick15/stage mismatch remains frozen; detailed source coordinates and exclusions stay visible. Missing pair/trophy stats and Arena slot order remain baseline limitations.
11. Training metadata write had a Win32 HANDLE prototype failure after analysis/selection completed; no time/memory was fabricated and no selection rerun. CLI import progress/JSON parsing was corrected before holdout, with immutable original selection and unchanged analytical parameters.

Validation: `dotnet build DraftTG.sln` passed, **0 warnings/errors**; `dotnet test DraftTG.sln --no-build` passed **1,291**, zero failed/skipped; **10** targeted Python tests passed. Two added C# tests protect real Q-only substitution and duplicate/version rejection; ten grouped Python tests cover byte-level split/production freeze, whole-set isolation, weighting/shrinkage, position indexing, same-set format pairs, neutral missing data, outcome-independent deterministic samples, disjoint decomposition, single-use holdout lock and logical identity consensus/A- scope. No synthetic case inflation. [Validation](docs/evidence/v2-phase-b/validation.json).

## 22. Recommendation for Phase C / V2 Q implementation

**Retain production v1.6.2, k500 and the narrow FRA alternate prior. No winning candidate is installed. Phase C is not started.** Keep A/k250 as the simplest promising *unique-game-presence research* policy, not an approved production definition.

Next authorized work should resolve quality-evidence compatibility: preserve the existing successful missing-primary prior contract, establish provider copy/presence count transport, obtain permitted same-set Quick/Premier evidence across multiple sets, and test the revised contract on new whole-set holdouts. BLB/DSK are already spent. Only after that review should V2 Q integration be considered. Derived position descriptors may support separately authorized Phase C lane research; they do not justify a lane formula or automatic architecture change now.

Reproduce the original study pipeline on a fresh, separately identified research workspace with the same input hashes; published selection/final-release files intentionally prevent retuning or a second “untouched” release in this workspace. Do not delete/reset the consumed lock. The normal commands here are `summary` and `report`, which only read released aggregates.

```powershell
Set-Location 'C:\Users\Graphics0\Documents\Projects\DraftTG'
python -m pip install --target .\data\historical\cache\phase-b\python pyarrow==26.0.0
dotnet build DraftTG.sln
# Initial study only; acquisition uses public bulk sources and the owned Scryfall cache.
python .\tools\DraftTG.HistoricalEvaluation\phase_b.py acquire
python .\tools\DraftTG.HistoricalEvaluation\phase_b.py qualify
python .\tools\DraftTG.HistoricalEvaluation\phase_b.py audit
python .\tools\DraftTG.HistoricalEvaluation\phase_b.py train
python .\tools\DraftTG.HistoricalEvaluation\phase_b.py replay
python .\tools\DraftTG.HistoricalEvaluation\phase_b_golden.py
# Exactly one final release, after frozen selection; refuses an existing consumed lock.
python .\tools\DraftTG.HistoricalEvaluation\phase_b.py final
python .\tools\DraftTG.HistoricalEvaluation\phase_b_diagnostics.py
python .\tools\DraftTG.HistoricalEvaluation\test_phase_b.py
dotnet test DraftTG.sln --no-build
# Safe after the published release: no model evaluation or parameter changes.
python .\tools\DraftTG.HistoricalEvaluation\phase_b.py report
python .\tools\DraftTG.HistoricalEvaluation\phase_b.py summary
```

`acquire` verifies pinned LFS SHA256 and caps data at 6 GiB; the original wide fallback reuses Phase A’s bounded public sampler. It requires the existing public card helper and owned Scryfall cache (hashes in identity evidence). `qualify` reuses cached dataset results; canonical replay imports reuse the existing store’s strict hash cache. Supported entry point `phase_b.py` configures Windows process telemetry without modifying analytical code. `evaluate --research-q FILE` on the existing C# evaluator is the labeled experimental Q-only ablation. Baselines/shrinkage/candidate metrics are produced by `train`; missing paired-data cross-format results remain explicit. Small source/feature/metric/specification outputs are under `docs/evidence/v2-phase-b/`; raw rows, full histogram/build mappings, SQLite and detailed occurrence traces remain ignored under `data/historical/`.

Data-derived metrics are attributable by set/format/card/count plus source file/normalized SHA and filtering rules. Evidence provenance distinguishes original atomic CSV from pinned Parquet transformations. A fresh verification run is not another newly untouched holdout study and must not retune using these released outcomes.
