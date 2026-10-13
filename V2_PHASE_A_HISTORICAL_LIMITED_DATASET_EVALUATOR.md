# Contextual Pick Score V2 — Phase A: historical Limited dataset and cross-set evaluator

## 1. Goals and frozen baseline

Build a provider-neutral, offline data/replay/evaluation foundation. **No V2 score, weight selection, training, clustering or score bonuses were implemented.** `contextual-pick-score-v1.6.2` remains the frozen production baseline; explicit v1.6/v1.6.1/v1.6.2 replay uses existing engines.

Tested git base: `d1d09f6b216f23b8e0ce69a77afc552e84b9a014`, with preexisting uncommitted earlier-phase work and these additions. SDK 10.0.112 / runtime 10.0.12. Exact pilot build: `git=d1d09f6b216f23b8e0ce69a77afc552e84b9a014; evaluationAssemblySha256=89AACBE685E260D463849925AE3108BB9891013E15DEE9A714CF741F9C50278C; scoreAssemblySha256=778C592668472C67DF80AF2AC278ECF25DCD43CFDCFE5AA7A520316E6FCAC032; runtime=10.0.12; working-tree build (see baseline-freeze.json)`. [Baseline freeze](docs/evidence/v2-phase-a/baseline-freeze.json) records 39 recommendation-source/fixture hashes; **all 39 remain unchanged**. The current build/report records evaluator and scoring assembly hashes, catalog/stat hashes, split hash and exact model versions.

## 2. Source datasets actually used

The existing integration provides GIH/sample/ALSA/ATA and some hand/drawn fields; it does not expose a historical pick corpus or games-played count/WR as normalized rating DTO fields. Local caches contained only FRA QuickDraft/PremierDraft ALL_TIME v3 ratings plus Scryfall metadata. Existing draft models preserve quantities and known chronological picks or a recovered inventory. Those engines/types are reused. Frozen real FRA Uldaros/P1P8 regressions and existing deck-fit/cut-line tests are golden evidence, separately from the historical pilot.

The existing trophy client rejects restricted external API data; that behavior is unchanged. This phase used anonymized bulk files linked from the [official 17Lands public datasets page](https://www.17lands.com/public_datasets), published under its stated CC BY 4.0 terms, and the already owned Scryfall JSONL/gzip cache. No account, trophy, private-player endpoint or UI scraping was used. [17Lands usage guidelines](https://www.17lands.com/usage_guidelines) distinguish the public dumps from site/API data.

Downloaded bounded 4 MiB compressed prefixes from six draft files and five game files, retaining the first 1,000 complete CSV rows each: **6,000 picks and 5,000 games**. Files are direct `17lands-public.s3.amazonaws.com/analysis_data/draft_data/` and `game_data/` links. The public `analysis_data/cards/cards.csv` helper maps Arena IDs to set/name metadata. WOE/LCI/MKM/OTJ/VOW PremierDraft and VOW QuickDraft are imported. The public index did not provide a VOW QuickDraft game file; GIH for that format remains unavailable. Acquisition scripts/metadata document transport headers, retained-source hashes, source URLs, prefix limits and sampling; the full original gzip was not downloaded or hashed.

## 3. Actual observed source schemas

Draft CSV: zero-based `pack_number`/`pick_number`; `pack_card_<name>` and `pool_<name>` integer quantities; `pick` names the selected card. Source event records and participation-rate columns are labels, not input features. Old VOW uses `user_rank` and match-lifetime buckets; newer files use `rank` and game-lifetime buckets. Game CSV: quantities in `deck_<name>`, `opening_hand_<name>` and `drawn_<name>` (including observed float-formatted integer counts); game/build metadata and `won`. Newer schemas also contain `game_time`, `match_number` and tutored-card columns. VOW lacks reliable match/time game identifiers and its `.csv.gz` transport is a gzip **tar** member, not a bare gzip CSV. The acquisition tool handles that container; normal importer gzip support is for CSV streams, so full tar archives require extraction first.

| Sample file | Columns | Actual metadata fields |
|---|---:|---|
| LCI.PremierDraft.game_data.sample.csv | 1475 | `expansion`, `event_type`, `draft_id`, `draft_time`, `game_time`, `build_index`, `match_number`, `game_number`, `rank`, `opp_rank`, `main_colors`, `splash_colors`, `on_play`, `num_mulligans`, `opp_num_mulligans`, `opp_colors`, `num_turns`, `won`, `user_n_games_bucket`, `user_game_win_rate_bucket` |
| LCI.PremierDraft.draft_data.sample.csv | 596 | `expansion`, `event_type`, `draft_id`, `draft_time`, `rank`, `event_match_wins`, `event_match_losses`, `pack_number`, `pick_number`, `pick`, `pick_maindeck_rate`, `pick_sideboard_in_rate`, `user_n_games_bucket`, `user_game_win_rate_bucket` |
| MKM.PremierDraft.game_data.sample.csv | 1650 | `expansion`, `event_type`, `draft_id`, `draft_time`, `game_time`, `build_index`, `match_number`, `game_number`, `rank`, `opp_rank`, `main_colors`, `splash_colors`, `on_play`, `num_mulligans`, `opp_num_mulligans`, `opp_colors`, `num_turns`, `won`, `user_n_games_bucket`, `user_game_win_rate_bucket` |
| MKM.PremierDraft.draft_data.sample.csv | 666 | `expansion`, `event_type`, `draft_id`, `draft_time`, `rank`, `event_match_wins`, `event_match_losses`, `pack_number`, `pick_number`, `pick`, `pick_maindeck_rate`, `pick_sideboard_in_rate`, `user_n_games_bucket`, `user_game_win_rate_bucket` |
| OTJ.PremierDraft.game_data.sample.csv | 1925 | `expansion`, `event_type`, `draft_id`, `draft_time`, `game_time`, `build_index`, `match_number`, `game_number`, `rank`, `opp_rank`, `main_colors`, `splash_colors`, `on_play`, `num_mulligans`, `opp_num_mulligans`, `opp_colors`, `num_turns`, `won`, `user_n_games_bucket`, `user_game_win_rate_bucket` |
| OTJ.PremierDraft.draft_data.sample.csv | 776 | `expansion`, `event_type`, `draft_id`, `draft_time`, `rank`, `event_match_wins`, `event_match_losses`, `pack_number`, `pick_number`, `pick`, `pick_maindeck_rate`, `pick_sideboard_in_rate`, `user_n_games_bucket`, `user_game_win_rate_bucket` |
| VOW.PremierDraft.game_data.sample.csv | 1106 | `user_win_rate_bucket`, `user_n_games_bucket`, `draft_id`, `build_index`, `draft_time`, `expansion`, `event_type`, `game_number`, `rank`, `opp_rank`, `main_colors`, `splash_colors`, `on_play`, `num_mulligans`, `opp_num_mulligans`, `opp_colors`, `num_turns`, `won` |
| VOW.PremierDraft.draft_data.sample.csv | 558 | `user_match_win_rate_bucket`, `user_n_matches_bucket`, `draft_id`, `draft_time`, `expansion`, `event_type`, `user_rank`, `event_match_wins`, `event_match_losses`, `pack_number`, `pick_number`, `pick`, `pick_maindeck_rate`, `pick_sideboard_in_rate` |
| VOW.QuickDraft.draft_data.sample.csv | 558 | `user_match_win_rate_bucket`, `user_n_matches_bucket`, `draft_id`, `draft_time`, `expansion`, `event_type`, `user_rank`, `event_match_wins`, `event_match_losses`, `pack_number`, `pick_number`, `pick`, `pick_maindeck_rate`, `pick_sideboard_in_rate` |
| WOE.PremierDraft.game_data.sample.csv | 1665 | `expansion`, `event_type`, `draft_id`, `draft_time`, `game_time`, `build_index`, `match_number`, `game_number`, `rank`, `opp_rank`, `main_colors`, `splash_colors`, `on_play`, `num_mulligans`, `opp_num_mulligans`, `opp_colors`, `num_turns`, `won`, `user_n_games_bucket`, `user_game_win_rate_bucket` |
| WOE.PremierDraft.draft_data.sample.csv | 672 | `expansion`, `event_type`, `draft_id`, `draft_time`, `rank`, `event_match_wins`, `event_match_losses`, `pack_number`, `pick_number`, `pick`, `pick_maindeck_rate`, `pick_sideboard_in_rate`, `user_n_games_bucket`, `user_game_win_rate_bucket` |

The public card helper header is exactly `id,expansion,name,rarity,color_identity,mana_value,types,is_booster`. Exact imported header arrays are retained in local manifests; [observed schema metadata](docs/evidence/v2-phase-a/observed-schemas.json) and manifest header hashes are committed. Names are actual schema observations, not assumed historical API fields.

## 4. Canonical pick schema

`HistoricalDraftDecision` separates **Inputs**, **Label**, **Outcome**, **PlayerMetadata**, and **Provenance**. `PickTimeState` contains set/format/provider-scoped draft ID, optional draft timestamp, one-based pack/pick, zero-based global index, offered quantities, pre-pick pool quantities, strictly earlier known picks and completeness. `HistoricalPickLabel` holds actual selected identity and observed maindeck/sideboard-in rates. `DraftOutcome` holds reliable final records, reported snapshot records, optional completed pool/main deck/sideboard and evidence. Lifetime skill buckets and source rank have uncertain temporal scope and live outside Inputs as analysis metadata.

Every card mapping retains source identity, canonical DraftTG `CardIdentifier` value, printing set, mapping rule/status and all ambiguous candidates. Quantities remain explicit; expansion into Domain occurrences preserves duplicate copies. Schemas are exported directly from the actual C# types under [docs/historical/schemas](docs/historical/schemas), with additional semantic validation in the importer. JSONL mappings are re-resolved rather than trusting caller-supplied mapping flags. Unknown/invalid scopes, future history and impossible quantities are rejected.

## 5. Identity normalization and coverage

Exact declared set plus normalized full name wins, followed by explicit DFC/adventure/front-face conventions. Only explicitly declared bonus sheets are searched: WOT for WOE; SPG for MKM; OTP/BIG/SPG for OTJ. Names never silently resolve across unrelated reprints. Explicit canonical IDs must also lie in the declared scope. Multiple matching printings remain ambiguous; no arbitrary first match is selected.

The metadata subset contains **1,730** exact printings selected by public helper Arena IDs from the owned Scryfall cache; every matching printing is retained. Counts across offered/pool/selected occurrences: **165,095 resolved / 173,063 total (95.3959%)**, **7,968 unresolved including ambiguity**. These are repeated occurrence-weighted coverage counts, not unique card counts. The imported pack identities are incomplete in **1,808** decisions. LCI/OTJ basics have multiple printings; many VOW styles are ambiguous; MKM List cards without an explicitly established printing remain unresolved. Unresolved identity detail/provenance remains in normalized rows and per-set issue aggregates. No geometry or card identity was guessed.

## 6. Dataset manifests and registry

Six [dataset options](docs/historical/datasets) plus the split/bonus registries define source/scope/schema/partition/pick counts explicitly. Manifests include raw file SHA-256/size, URI, actual columns, import date, min/max source draft dates, unique draft/decision counts, identity coverage, malformed/duplicate/conflict accounting, outcome/deck/pool coverage, catalog/split/options hashes, timing and approximate peak process memory. Cached import requires all relevant hashes plus normalization version to match. File names alone are never provenance.

All six pilot imports had **0 malformed rows, 0 duplicate decisions and 0 conflicting rows**. Conflicting duplicates or inconsistent event labels reject the whole file transaction; incomplete/cancelled imports roll back. Cached completed imports skip normalization. Resume is at file boundaries, not arbitrary gzip offsets. SQLite already exists in DraftTG dependencies; no analytics framework was added. Inputs, selected labels, outcomes and player metadata occupy separate storage columns. Disk indexes enforce unique provider/draft/pack/pick keys and draft/set/partition isolation.

## 7. Leakage controls

The scoring adapter accepts only `PickTimeState`, not a full decision or outcome. Selected picks become known history **only after** their own replay, and history resets by provider-scoped draft. Future picks, future packs, completed pools, decks, records, participation rates and lifetime buckets cannot enter scoring through that boundary. Explicit prior coordinates must be strictly earlier. The source pre-pick pool is retained; its count is validated against the actual historical pick index. Completed pools are reconstructed separately after import only when the last pre-pick inventory is complete, by adding the last selected occurrence. They are future labels even on earlier rows.

No model is optimized for human agreement here. Human labels/outcomes are joined after scoring. Imported dates are draft-level timestamps; source-naive timestamps are serialized assuming UTC, and are not asserted to be per-pick times.

## 8. Mature-format versus chronological evaluation

**Pilot regime: MatureFormatReplay.** Statistics are sample-derived aggregates of public game data and overlap some replay drafts, including chronological future information. They are neither all-time full-format estimates nor a chronological backtest. Current cached Oracle metadata is also not a historical as-of-pick snapshot.

Chronological mode is designed as a fail-closed extension point: it requires a strictly earlier available snapshot and per-card timestamps. This pilot has none and the command refuses that regime. A full temporal backtest additionally requires auditable source times, historical metadata boundaries and chronological stat construction; it is not claimed implemented.

## 9. Set-level split strategy

[pilot-v1.json](docs/historical/splits/pilot-v1.json): **WOE/VOW calibration; LCI/MKM development; OTJ holdout**. All formats of one set share its partition. No pick-level random split exists, and one provider-scoped draft cannot cross sets/partitions. Split hashes are checked at import and evaluation. The whole OTJ set is assigned to holdout, but only its 1,000-row prefix was imported. The entire historical OTJ corpus has not been evaluated. Holdout outcomes must not guide parameter selection; this phase selected no parameters. Future research should preregister untouched holdouts before choosing V2 behavior.

## 10. Replay engine and CLI

New `DraftTG.HistoricalEvaluation` library/tool reuses `CardCatalog`, `DraftSnapshot`, duplicate-preserving `DraftPoolSnapshot`, `LimitedStatisticsMapper`, `DraftPackObservationHistory` and the production `LimitedStatisticsUpdate` pipeline. No scoring formula was copied or rewritten. Explicit model strings select v1.6/v1.6.1/v1.6.2. Every decision output records occurrence scores/ranks/components, model version, actual-pick rank, estimated/unavailable cards, missing-input reasons, source hash and performance. Histories are streamed in source coordinate order with one draft in memory.

Commands: **import, validate, import-decks, decks, stats, evaluate, compare, golden, report, schema**. Full invocation examples and bounded pilot reproduction are in [the CLI README](tools/DraftTG.HistoricalEvaluation/README.md). Outputs include markdown, JSON and CSV; detailed row outputs stay ignored. No Avalonia UI or live runtime is required.

Frozen Domain supports pick coordinates 1–14 and scoring stage assumes 42/14. Historical MKM packs have 13 picks, OTJ/VOW/WOE 14, LCI 15. Source global indexes/completeness reflect those observations; source coordinates are passed unchanged to the frozen engine. LCI pick 15 is stored but cannot be scored. Known prior pick-15 identities remain in recovered inventory; their unsupported chronology coordinate is omitted from Domain history with diagnostics. Historical visual slot order is unavailable; deterministic source column order supplies occurrence ties. Pair stats/trophy corpora are not supplied; production missing-data behavior applies. These are limits of baseline comparability, not silent fixes to the baseline.

## 11. Metrics and coverage

Top-1/3/5 agreement, MRR, mean normalized rank `(rank−1)/(occurrences−1)` and recommended-minus-actual score gap in 0–50 points are reported with eligible denominators. A human source names a card rather than an indistinguishable duplicate slot, so actual rank uses the best matching occurrence. All occurrences retain their scores. Singleton packs have no meaningful choice; the frozen engine omits their rank and they are excluded, rather than inflating agreement.

**3,370 eligible decisions / 6,000 total (56.17%)**. Strictly excluded classes in evaluation priority order:

- Unresolved offered identity; decision excluded rather than ranking a truncated pack.: **1788**.
- Frozen Domain/score baseline supports pick coordinates 1–14; historical pick 15 is retained but not replayed.: **66**.
- Singleton pack: frozen engine intentionally emits no choice rank: **248**.
- No usable environment baseline / unavailable scores: **528**.

The source-wide 1,808 incomplete-identity count overlaps 20 pick-15 observations, so it differs from the 1,788 identity-first exclusions. **0 source pool-count incompleteness** after configuring observed pack counts. There are **2,331 decisions with unresolved pool identities**; these are explicitly partial model inventories, not fabricated complete pools. The report also counts **1,449 estimated offered-card scores** and reports quality evidence/provenance. Agreement does not imply all underlying inputs are fully measured.

Strata cover set, exact format, pack/pick, card count, unchanged input Phase9A commitment bucket, source rank/lifetime metadata, confirmed final record and unconfirmed reported record. A common unchanged Phase9A commitment view supplies strata; v1.6.2's candidate score retains its separate projected relevance diagnostics. [metrics.csv](docs/evidence/v2-phase-a/metrics.csv) contains these aggregate strata.

## 12. Outcome interpretation

Source event records exist for all 6,000 rows. **5,055 rows** have terminal supported-format records retained as final outcomes (Bo1: 7 wins or 3 losses; Traditional: three total matches). **945 rows** have only reported publication-time records; completion/retirement cannot be established, so final outcomes remain missing. Source reported counts are still retained separately. No missing games or event endings are invented.

| Confirmed final-win group | Eligible decisions | v1.6.1 Top-1 | v1.6.2 Top-1 | v1.6.2 MRR |
|---|---:|---:|---:|---:|
| 0-2 wins | 1029 | 34.9854% | 36.4431% | 0.564365 |
| 3-4 wins | 850 | 34.0000% | 34.8235% | 0.554720 |
| 5 wins | 177 | 39.5480% | 40.1130% | 0.599796 |
| 6 wins | 319 | 36.3636% | 36.3636% | 0.565095 |
| 7 wins | 494 | 42.1053% | 42.3077% | 0.611622 |
| unknown | 501 | 30.3393% | 31.3373% | 0.519646 |

This bounded sample shows higher v1.6.2 agreement in 7-win than low-win drafts, but skill, sampling, format/time and selection confound that association. A trophy does not prove each pick optimal. Continuous exact records remain in row labels and record strata; there is no binary “6+ wins is good” target and no causal performance claim.

## 13. Final-deck and participation coverage

**0 reliably reconstructed final main decks / sideboards** from these public pick/game prefixes. Pick-level observed maindeck and sideboard-in rates are available for all 6,000 decisions; eligible rows average observed maindeck rate **60.6832%**. These rates do not identify individual duplicate copies or a unique final 23. The schema preserves explicit source-declared final decks/sideboards when supplied by a reliable provider; none were invented here. Completed full-pool labels are recoverable for **5,829 rows**, including preserved unresolved identities/quantities, and remain analysis-only.

## 14. Normalized card-stat feature coverage

Sample GIH counts/rates are derived from one game if a card is in the opener **or** drawn later; GP/OH/drawn counts/rates and drafted counts are also exposed. Rates without observations remain missing. GIH is not substituted with GP WR or play rate, and no cross-format GIH is borrowed for missing VOW QuickDraft. `SetBaseline` preserves sample game win rate for research; replay uses the existing GIH-weighted production baseline.

Published ALSA/ATA are absent in these actual raw schemas and stay null. Sample average observed seen/taken positions are separate descriptors, not silently renamed ALSA/ATA or injected into baseline lane scoring. Every feature snapshot has exact set/format, source hashes, sample-period description, availability semantics and mapping coverage. No V2 weighting is chosen.

| Set/format | Feature rows | Identity resolved | Usable GIH | GP count | OH WR | Drawn WR | Published ALSA/ATA | Sample seen/taken means |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| LCI/PremierDraft | 291 | 287 | 262 | 263 | 253 | 259 | 0/0 | 276/239 |
| MKM/PremierDraft | 326 | 281 | 274 | 277 | 267 | 269 | 0/0 | 292/236 |
| OTJ/PremierDraft | 381 | 376 | 326 | 330 | 307 | 322 | 0/0 | 348/264 |
| VOW/PremierDraft | 272 | 211 | 257 | 259 | 247 | 253 | 0/0 | 249/210 |
| VOW/QuickDraft | 237 | 195 | 0 | 0 | 0 | 0 | 0/0 | 237/206 |
| WOE/PremierDraft | 329 | 324 | 268 | 273 | 253 | 263 | 0/0 | 305/238 |

Observed source pick position plus nullable published ALSA/ATA and separate sample descriptors supports later lane-feature analysis. No new lane formula was added.

## 15. Completed-deck corpus and general Limited features

**960 observed game builds** from 5,000 game rows, stored separately from decisions. **86** builds have an exact provider/draft/set/format join to confirmed final event records; **95** have completed source pool labels. Game-number resets without match/time metadata (VOW) use source-row identities; no false cross-row game deduplication is claimed. Observed game outcomes are separate from final event records. Main-deck quantities must agree within a build; inconsistent builds reject import. Basic lands are not subtracted from pools to guess a sideboard.

Provider-neutral summaries reuse `LimitedCardRoleClassifier` and `DeckManaDemand`: land/spell/creature/noncreature counts, curve buckets, early plays, MV5+ top end, fixing, precision-first weighted removal, color distribution and colored-pip demand. Roles come from structured metadata/established Oracle rules, not names. Exact splash plan selection is deferred; pip/color/source evidence is retained. **23,254 / 38,456 main-deck occurrences** have resolved structured/Oracle metadata; **15,202** remain unresolved. Counts describe the resolved subset, with coverage alongside them.

`decks.jsonl` retains quantities/build identities and continuous known outcomes. `deck-aggregates.json` provides card-presence counts and build denominators by set/format/colors/exact record; missing records stay “unknown.” Denominators are observed builds, not unique drafts. Full rows support later card-pair co-occurrence comparisons; no pair bonuses, manual archetype labels or clustering were introduced. Resolve build-selection/coverage before interpreting P(card | successful decks).

## 16. Pilot corpus and resource limits

| Set | Format | Drafts | Decisions | Actual picks/pack | Identity occurrence coverage | Confirmed outcome rows | Completed-pool label rows |
|---|---|---:|---:|---:|---:|---:|---:|
| LCI | PremierDraft | 23 | 1000 | 15 | 97.66% | 765 | 990 |
| MKM | PremierDraft | 26 | 1000 | 13 | 99.49% | 883 | 975 |
| OTJ | PremierDraft | 25 | 1000 | 14 | 96.56% | 701 | 966 |
| VOW | PremierDraft | 24 | 1000 | 14 | 88.80% | 1000 | 966 |
| VOW | QuickDraft | 24 | 1000 | 14 | 90.01% | 832 | 966 |
| WOE | PremierDraft | 25 | 1000 | 14 | 100.00% | 874 | 966 |

Total **147 drafts, 6,000 pick decisions, 5 sets, 2 imported formats**. Ranked evaluation is possible for all five PremierDraft sets; QuickDraft is imported but has no eligible scores without its exact-format GIH baseline. OTJ is a whole-set partition holdout, sampled rather than exhaustively imported. Source prefixes cover 2021–2024 draft timestamps, not the full lifetime of each format.

The optional 100,000-decision target was **not reached**. This phase deliberately bounded network reads and replay work to validate schemas/identity/leakage on direct public data without pulling multi-gigabyte dumps. At the measured three-model throughput, 100,000 decisions would require approximately **29 minutes of replay**, plus acquisition/import and roughly multiple GiB of detailed outputs. Larger representative/full-set data qualification is the next prerequisite, not a fabricated pilot count. The streaming/disk-backed architecture supports expansion beyond these prefixes; million-row memory/runtime has not been physically benchmarked.

## 17. Baseline comparison and important changes

| Model | Eligible / total | Top-1 | Top-3 | Top-5 | MRR | Normalized rank (lower better) | Mean score gap |
|---|---:|---:|---:|---:|---:|---:|---:|
| contextual-pick-score-v1.6 | 3370/6000 | 35.4599% | 69.8220% | 84.0653% | 0.558462 | 0.311809 | 2.937099 |
| contextual-pick-score-v1.6.1 | 3370/6000 | 35.4599% | 69.8220% | 84.0653% | 0.558462 | 0.311809 | 2.937099 |
| contextual-pick-score-v1.6.2 | 3370/6000 | 36.3205% | 70.2671% | 84.3323% | 0.564142 | 0.306878 | 2.890100 |

v1.6 and v1.6.1 produce identical rankings/scores on this non-FRA pilot, consistent with the restricted FRA sparse prior. v1.6.2 versus either older version: **338 ranking changes**, **86 actual-pick rank improvements**, **15 regressions**, **0 missing-data differences**. Largest absolute changed card score: **5.463215 points**. [Model comparison details](docs/evidence/v2-phase-a/model-comparisons.json) retain bounded largest score/rank improvements/regressions for review; full per-occurrence components remain local. No winner is declared from a sub-one-percentage-point aggregate difference on biased, partially observed data.

| v1.6.2 set/format | Eligible / total | Top-1 | MRR |
|---|---:|---:|---:|
| LCI/PremierDraft | 614/1000 | 33.8762% | 0.536617 |
| MKM/PremierDraft | 856/1000 | 34.2290% | 0.548452 |
| OTJ/PremierDraft | 465/1000 | 29.4624% | 0.509665 |
| VOW/PremierDraft | 506/1000 | 42.0949% | 0.626114 |
| VOW/QuickDraft | 0/1000 | unavailable | unavailable |
| WOE/PremierDraft | 929/1000 | 40.1507% | 0.590303 |

Golden suite is separate: **40 existing cases passed**, covering Uldaros's frozen FRA sparse prior, the exact P1P8 RG/Unsummon versioned regression and existing deck-fit/cut-line/freeze tests. Original fixtures/expectations are unchanged. Golden results can be copied alongside corpus reports with `--golden-report`; they never enter historical agreement metrics.

## 18. Performance, disk usage and tests

Fresh import throughput: **2046–2488 raw rows/s**, with all valid rows becoming normalized decisions at the same rate. Manifest timing starts after the SHA-256 cache preflight; it excludes separate acquisition and metadata preparation. Complete CLI process durations are separately recorded in `pilot-timings.json` (the last run is a completed-import cache replay, not a fresh-import benchmark). Completed-source hash-cache skips normalization. Three-model replay: **104.649s / 6,000 decisions = 57.334 decisions/s** (approximately 172.003 model-decision evaluations/s), **98,039 optimizer calls**, 92.440s reported engine time. Approximate peak replay process working set: **198.44 MiB**; import peak working sets are in manifests. These are local operational measurements, not a million-row scaling benchmark.

Active pilot footprint: raw **25.88 MiB**, cache **10.65 MiB**, normalized records/exports **123.39 MiB**, evaluation **343.02 MiB**, total **502.94 MiB**. Whole ignored historical area including intermediate runs: **1.399 GiB**. Automatic approval review rejected deletion of intermediate task outputs with “blocked by policy,” so they remain and are included in total disk accounting. No raw/normalized/scored row data was added to git; `.gitignore` covers the entire local historical area. Only small aggregate/hash/schema/report evidence is proposed for source control.

`dotnet build DraftTG.sln`: **passed, 0 warnings/errors**. `dotnet test DraftTG.sln --no-build`: **1,289 passed, 0 failed/skipped**, including 12 targeted historical cases. TRX: `artifacts/v2-phase-a/release-tests`. Coverage protects CSV schema/quotes/bounds, malformed rows, duplicate quantities/conflicts, exact/DFC/reprint/bonus identity, source pool reconstruction, future-label separation, future history rejection, set/draft isolation, explicit/deterministic version replay, metrics, manifest caching, cancellation rollback, observed deck separation and land-creature summary counts. One intermediate suite run hit the existing WGC resource-bound assertion while other work ran; the isolated test and subsequent full quiet suite passed. WGC code/tests were not modified.

## 19. Known limitations

Prefix sampling is strongly biased and far smaller than full historical formats. Aggregate samples are sparse and inclusive of some evaluated events. Published ALSA/ATA, reliable chronological snapshots, exact UI ordering, pair statistics and trophy corpora are absent. Printing ambiguity reduces both pack and pool/deck coverage. Singleton and unsupported pick-15 observations are stored but excluded from ranks. Baseline stage assumptions remain 42/14 even for 13/15-pick formats. Current metadata is not historical as-of metadata. Partial recovered identities and provisional cross-set supply defaults constrain interpretation. Source ranks/lifetime skill buckets are descriptive analysis metadata, not verified pick-time values. Game builds do not prove final deck choices or individual-copy participation. No throughput or correctness claim substitutes for the missing representative 100,000-pick pilot.

## 20. Data gaps to close

Obtain manageable representative/full public draft/game files, including an exact QuickDraft game/stat source; document windows/selection and source hashes. Add genuinely published aggregate ALSA/ATA where permitted, with exact schema/provenance. Resolve printing ambiguity through explicit supported identity evidence, not first matching. Establish historical stat/metadata timestamps for temporal evaluation. Improve exact pool/deck role coverage and define one build/final-deck selection policy before outcome/card-pair frequency comparisons. Maintain the golden suite separately and expand representative set/format coverage. No private source or prohibited trophy API is required to close these gaps.

## 21. Recommendation for V2 Phase B

**The evaluator is ready as an offline import/replay/measurement foundation; this pilot is not yet adequate for trustworthy V2 parameter selection or superiority claims.** Phase B should first qualify a larger representative corpus and feature/identity coverage, preregister set-level calibration/development/holdout assignments and evaluation denominators, then perform feature diagnostics and outcome-aware association analyses while preserving leakage controls. Keep mature-format and chronological claims separate, retain continuous outcomes and test every candidate against the frozen golden suite. A new model must outperform baselines across withheld sets with explained coverage, rather than merely fit average human choices.

Stop here. No V2 scoring, V2 weights, new bonuses, archetype clustering, training, Phase 9F.3, localization, memory, overlay, WGC authority, sparse-prior policy or existing scoring behavior was changed.

Machine-readable concise evidence: [pilot-summary.json](docs/evidence/v2-phase-a/pilot-summary.json). Detailed local evaluation: `data/historical/evaluation/phase-a/summary.json`, `metrics.csv`, `report.md`, `decisions.jsonl`; normalized database: `data/historical/normalized/pilot-v1.sqlite`. CLI examples and exact reproduction steps: [README](tools/DraftTG.HistoricalEvaluation/README.md).
