# Historical Limited evaluator

## Phase B cross-set quality research

The supported Windows entry point is `python .\tools\DraftTG.HistoricalEvaluation\phase_b.py summary`.
See [the Phase B report](../../V2_PHASE_B_SET_NORMALIZED_QUALITY_RESEARCH.md) for the exact pipeline and results.
`report` rebuilds the Markdown and machine summary from released aggregates; it does not reevaluate models.

Phase B adds pinned public Parquet acquisition (optional task-local PyArrow 26.0.0), bounded original wide-CSV
fallback for lost older game keys, explicit logical Oracle/gameplay consensus identities, canonical Phase A
decision export, position features, set baselines, sparse quality experiments and offline Q-only ablation.
The C# `evaluate --research-q FILE` option reuses real production scores and preserves L/A/D, weights,
commitment, already-computed R, optimizer results and evidence tie breaks. It is never a production V2 model.

The split/plan was committed at `d2cb723` before analysis. A/k250 was selected using calibration/development;
BLB/DSK were released once and are now spent holdouts. `train` refuses an existing selection and `final`
refuses a consumed release; do not reset those files. The source/formula/parameter freeze is verified before
release. The original analytical selection remains preserved alongside the documented execution-only log
parsing correction. Windows telemetry is configured by the supported launcher.

Production remains v1.6.2. Missing legitimate paired Quick/Premier game data and the Uldaros missing-prior
regression block candidate adoption. Neither production Q nor Phase C was implemented.

```powershell
python .\tools\DraftTG.HistoricalEvaluation\phase_b.py report
python .\tools\DraftTG.HistoricalEvaluation\phase_b.py summary
python .\tools\DraftTG.HistoricalEvaluation\test_phase_b.py
```

Phase A provides import and evaluation infrastructure. It does not define V2 scoring. The exact current baseline and existing fixtures are frozen in `docs/evidence/v2-phase-a/baseline-freeze.json`.

Build with `dotnet build DraftTG.sln`; run `dotnet run --project tools/DraftTG.HistoricalEvaluation -- help` for all commands. No UI, Arena, account, or network access is used by the C# evaluator. `Microsoft.Data.Sqlite` is already a project dependency.

## Local data

`data/historical/` is ignored. Raw public CSV/gzip files go under `raw/`, exact-printing Scryfall JSON and normalized stat snapshots under `cache/`, disk-backed normalized records under `normalized/`, and row-level evaluation outputs under `evaluation/`. Commit small configuration/manifests, schemas and aggregate reports only. Do not add row-level files to git.

The importer accepts public 17Lands wide draft CSV, gzip **CSV streams**, and provider-neutral `historical-decision-v1` JSONL/gzip. Older gzip **tar archives must first be extracted**; the bounded public sampling helper handles the observed single-member VOW container. Pack/pick CSV coordinates are zero-based; canonical coordinates are one-based with a zero-based global index. Configure picks per pack from observed source rows, never from the current Arena layout. Pilot values: MKM 13, OTJ/VOW/WOE 14, LCI 15. Frozen Domain accepts pick coordinates 1–14; pick 15 is stored and excluded from replay. Stage calibration remains the frozen 42/14 behavior.

```powershell
dotnet run --project .\tools\DraftTG.HistoricalEvaluation -- import --source .\data\historical\raw\WOE.PremierDraft.draft_data.sample.csv --options .\docs\historical\datasets\WOE-PremierDraft-prefix-v1.json --catalog .\data\historical\cache\catalog.json --splits .\docs\historical\splits\pilot-v1.json --db .\data\historical\normalized\pilot-v1.sqlite
dotnet run --project .\tools\DraftTG.HistoricalEvaluation -- validate --db .\data\historical\normalized\pilot-v1.sqlite --splits .\docs\historical\splits\pilot-v1.json --output .\data\historical\normalized\validation.json
dotnet run --project .\tools\DraftTG.HistoricalEvaluation -- compare --db .\data\historical\normalized\pilot-v1.sqlite --catalog .\data\historical\cache\catalog.json --stats .\data\historical\cache\stats.json --splits .\docs\historical\splits\pilot-v1.json --bonus .\docs\historical\bonus-sheets.json --model-a contextual-pick-score-v1.6.1 --model-b contextual-pick-score-v1.6.2 --output .\data\historical\evaluation\comparison
dotnet run --project .\tools\DraftTG.HistoricalEvaluation -- evaluate --db .\data\historical\normalized\pilot-v1.sqlite --catalog .\data\historical\cache\catalog.json --stats .\data\historical\cache\stats.json --splits .\docs\historical\splits\pilot-v1.json --bonus .\docs\historical\bonus-sheets.json --model contextual-pick-score-v1.6.2 --split Holdout --sets OTJ --output .\data\historical\evaluation\holdout
dotnet run --project .\tools\DraftTG.HistoricalEvaluation -- golden --root . --output .\artifacts\v2-phase-a\golden
dotnet run --project .\tools\DraftTG.HistoricalEvaluation -- report --source .\data\historical\evaluation\comparison\summary.json --output .\data\historical\evaluation\comparison
```

`evaluate --models` accepts all three frozen models on exactly the same streaming inputs. `--golden-report PATH` copies a separate golden result alongside corpus metrics; golden tests never enter human-agreement denominators. `stats` exports normalized card features/identity coverage; `import-decks` accepts observed public game CSV builds; `decks` exports the completed-deck foundation and continuous-record card presence counts. Game builds are explicitly not final decks. Missing sideboards remain missing. Draft decisions retain reliable source outcomes and a completed pool recovered from the final pre-pick pool plus final selected occurrence, where available.

`schema --output docs/historical/schemas` exports serialization contracts directly from C# types. Semantic constraints (source scope, quantities, chronology, rate ranges, conflicting records) are additionally validated in code. Canonical JSONL identities are re-resolved in declared set/sheet scope rather than trusting supplied mapping flags. The canonical type separates inputs, selected-pick labels, outcomes and analysis-only player metadata. See the generated schemas for exact fields.

## Reproduce the bounded pilot

Python scripts use the standard library. Acquisition follows the links published on the [17Lands public datasets page](https://www.17lands.com/public_datasets); data is attributed to 17Lands under its published CC BY 4.0 terms. No trophy/account endpoints are used. Acquisition is explicit and capped at 4 MiB compressed prefix per file; it does not download full multi-gigabyte corpora. The resulting first 1,000 complete rows per dataset are not random or representative.

```powershell
python .\tools\DraftTG.HistoricalEvaluation\acquire_pilot.py --sets WOE LCI MKM OTJ VOW --rows 1000
python .\tools\DraftTG.HistoricalEvaluation\acquire_pilot.py --kind game_data --sets WOE LCI MKM OTJ VOW --rows 1000
Invoke-WebRequest 'https://17lands-public.s3.amazonaws.com/analysis_data/cards/cards.csv' -OutFile .\data\historical\raw\cards.csv
python .\tools\DraftTG.HistoricalEvaluation\prepare_pilot.py
python .\tools\DraftTG.HistoricalEvaluation\run_pilot.py
```

Preparation requires the existing owned `%LOCALAPPDATA%\DraftTG\card-data\scryfall-default-cards.jsonl.gz` cache. Its hash and the public helper hash are recorded. Public Arena IDs select exact metadata printings; all matching printings are retained. Reprints, bonus sheets and multiple-art identities never become arbitrary first matches. LCI/OTJ basics and many VOW variants remain ambiguous; MKM List cards without a declared exact printing remain unresolved.

Pilot GIH/GP/OH/drawn rates are explicitly sample-derived from public game outcomes; GIH uses one game if the card is in the opener **or** drawn later. No absent GIH is borrowed across formats. Published ALSA/ATA are absent and remain null; sample seen/taken means are stored as separate descriptors, not injected into frozen lane scoring. `SetBaseline` preserves sample game win rate as research data; the replay engine uses its unchanged GIH-weighted production baseline.

## Evaluation and limits

Default regime is **MatureFormatReplay**, which allows inclusive aggregate statistics and chronological future information. It is not a chronological backtest. Chronological mode is a fail-closed extension point: it requires a strictly earlier snapshot and card availability timestamps; this pilot cannot run it. A complete temporal backtest additionally needs auditable historical metadata/time boundaries and is not claimed here.

Set assignments are explicit and hashed. One provider-scoped draft cannot cross sets/partitions. No pick-level random split is supported. OTJ is wholly assigned to holdout, though the pilot imports only a prefix of that set. Viewing these baseline metrics is not permission to select V2 parameters on holdout outcomes.

Metrics use fully ranked offered packs; incomplete identity packs and unavailable scores are excluded with coverage reasons. Estimated scores and unresolved/partial pools are reported separately. Duplicate card occurrences retain quantities; because a human source names a card rather than its indistinguishable slot, actual-pick rank is the best matching occurrence rank. Normalized rank is `(rank-1)/(occurrences-1)` (zero for singleton packs), and score gap is best score minus actual score in 0–50 points. Human agreement is revealed preference, not correctness. Final records are continuous strata, not a binary 6-win target.

Publication-time `event_match_wins/losses` are preserved as reported records. Final outcomes are populated only for terminal supported-format records (Bo1 seven wins or three losses; Traditional three total matches), or an explicit reliable canonical provider label. Completion/retirement of nonterminal source records is unknown. The frozen engine intentionally emits no choice rank for singleton packs, which are excluded from agreement denominators.

Streaming import retains one bounded CSV/JSONL record and a capped identity vocabulary, uses disk unique keys/indexes, reports progress, supports Ctrl+C, and rolls back incomplete file imports. Completed imports are cached only with matching raw/catalog/split/options hashes. Resume is at file boundaries, not arbitrary byte offsets. Replay keeps only one draft's history, bounded change examples and aggregate strata; detailed output is streamed to ignored JSONL. Keep replay and builds sequential on Windows because running assemblies are locked. Run resource-sensitive UI tests after replay finishes.

Manifest import rows/s measures parsing/normalization/storage after the SHA-256 cache preflight. The separate `pilot-timings.json` captures complete CLI process durations including metadata loading, preflight and startup; acquisition and preparation are separately measured stages.

Read `V2_PHASE_A_HISTORICAL_LIMITED_DATASET_EVALUATOR.md` for actual coverage, baseline metrics and the Phase B recommendation.
