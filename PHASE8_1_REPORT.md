# Phase 8.1 completion report

The production statistics source is now `/api/card_data`, using `expansion`, the exact `event_type`, and `time_period=ALL_TIME`. `/card_ratings/data` and the UI page `/card_data` are not production data sources. Phase 8 recommendation code, its formula/configuration, Arena parsing, all App production files, badge geometry, raw-GIH presentation and gold #1 treatment are unchanged.

## Response audit and live diagnostic

One development-only HTTPS request was made on 2026-10-03 to:

```text
https://www.17lands.com/api/card_data?expansion=WOE&event_type=QuickDraft&time_period=ALL_TIME
```

Observed response: HTTP 200, JSON body labeled `text/html; charset=utf-8`, with an object envelope containing `copyright`, `notes`, and `data`. `data` contains card records with independently populated overall and ever-drawn metrics. Dedicated `SeventeenLandsApiCardDataDto` and `SeventeenLandsApiCardDataRowDto` stay internal to Data. Unused card-image/printing fields are ignored; no images are fetched.

| WOE QuickDraft ALL_TIME observation | Count |
| --- | ---: |
| Returned provider rows | 324 |
| Rows with non-null GIH | 284 |
| Rows without GIH | 40 |

Recorded-response replay through the production client verified all GIH values and sample counts against the fetched body and retained all 324 rows, with no additional network request. The first observed card, Archon of the Wild Rose, mapped GIH 0.61753425 and sample 7,300, independently of its overall WR 0.5811066 and overall count 17,477. Live counts are observations and are not hardcoded into ordinary tests.

The response, source hashes and a replay-only driver/cache are stored under ignored `artifacts/phase8-1/`. Ordinary tests use synthetic fixtures and fake HTTP handlers and make no network requests.

## Mappings and request policy

Preserved mappings:

- `ever_drawn_win_rate` -> `GameInHandWinRate`
- `ever_drawn_game_count` -> `GameInHandGameCount`
- `avg_seen` -> ALSA; `avg_pick` -> ATA
- OH WR, drawn WR, drawn improvement and independent overall fields remain unchanged.

Missing GIH remains missing/unscored. Generic `win_rate`/`game_count` never substitute for GIH. Quick Draft requests `QuickDraft`, Premier requests `PremierDraft`, and Traditional requests `TradDraft`. Application's legacy empty-response Premier fallback was removed: only the exact requested format is fetched. There is no 50% or other-format substitute.

The client preserves one whole-environment request per expansion/event type/period, 24-hour TTL, serialized concurrent loads, a one-hour failure cooldown, cancellation, Retry-After delta/date handling and client-wide 429 cooldown. User-Agent is `DraftTG/0.8.1 (Limited statistics; whole-environment data cached for 24 hours)`. No per-card requests, polling or automatic retry loops were added.

## Automatic cache migration and sanity checks

Cache schema is **3**. Names include expansion, exact format, ALL_TIME and source/schema version, such as `WOE_QuickDraft_ALL_TIME_v3.json`. Required cache metadata includes source endpoint, period, expansion, requested/source formats, schema version and timestamp, plus the current API envelope. All identities, timestamp, rows and dataset sanity are revalidated on read.

Old legacy filenames are not read. Copied or tampered schema/source/period/format entries are rejected and cannot be used as stale fallback. A successful current load creates the new cache automatically; no manual deletion is required. Old files may remain on disk safely unused. The user's existing local legacy cache was not manually deleted or rewritten.

A named conservative floor of **20 rows** detects extremely small whole-environment responses; it is a heuristic, not an exact expected set count. Empty/fewer-than-20 responses and datasets with no valid GIH value plus positive GIH sample are suspicious. Bare arrays, malformed/missing/null data arrays, invalid metrics, duplicate names and actual HTML are rejected. Valid JSON is accepted despite the endpoint's incorrect HTML Content-Type.

A suspicious refresh keeps a valid compatible stale cache and reports the rejection reason and stale origin. Without one, statistics/recommendations are unavailable. Bad responses never overwrite valid cache data. Sanity-invalid cache payloads are rejected even while offline.

## Diagnostics and integration

The existing control-rail coverage text now reports:

```text
Expansion: WOE
Requested format: QuickDraft
Source endpoint: /api/card_data
Time period: ALL_TIME
Provider rows: 324
Data: network | fresh cache | stale cache | unavailable

Current pack:
GIH available: x / y
GIH low sample: n
GIH unavailable: z
ALSA available: x / y
Source: 17Lands QuickDraft / WOE
```

Provider rows refers to the usable dataset: retained cache rows for stale fallback, zero usable rows for a failed load. Rejection messages include suspicious observed row counts. None of these diagnostics is added to individual badges.

The existing background statistics pipeline naturally recomputes Phase 8 recommendations when the corrected data arrives. The integration regression starts with loading/unranked badges, delivers the current API response, then observes #1/#2, raw GIH and ALSA updates on the same badge instances/positions, without a restart. Phase 8 remains:

```text
b = sum(p_i*n_i) / sum(n_i)
s = (n*p + 500*b) / (n+500)
```

Occurrence identity, deterministic ties, prior-equivalent games 500 and partial/unscored behavior are unchanged. SHA-256 comparison against the pre-migration snapshot confirms all RecommendationEngine files, ArenaIntegration files and App production files are byte-for-byte unchanged.

## Validation

- `dotnet build DraftTG.sln`: **0 warnings, 0 errors**.
- `dotnet test DraftTG.sln --no-build`: **504 passed, 0 failed, 0 skipped**.
- All prior 471 test cases pass; legacy transport fixtures and the explicitly removed fallback expectations were updated for the new contract.
- **33 added cases**: 32 Data API/cache/sanity regressions and one App end-to-end asynchronous arrival regression.
- Data tests cover all exact event-type URLs, ALL_TIME, envelope deserialization, independent GIH mapping, null GIH despite populated generic WR, fresh/stale origin, legacy/malformed shapes, empty/tiny/no-GIH/HTML rejection, failure cooldown, stale preservation/no overwrite, nine incompatible metadata variants, automatic legacy migration, separate format/expansion caches, sanity-invalid cached payloads and the conservative row-floor boundary.
- Existing Phase 8 engine tests still pass; no engine code or score changed.
- Whitespace/diff review passed using a separate temporary Git index.

| Project | Passing |
| --- | ---: |
| Domain | 42 |
| ArenaIntegration | 94 |
| Data | 134 |
| RecommendationEngine | 34 |
| Application | 122 |
| App | 78 |
| Total | 504 |

Network validation was performed against the real WOE endpoint as described above. Arena and DraftTG were not running during this task, so multiple ranks in a **live Arena pack were not physically verified**. The rank update was verified with the production client and normal runtime using an offline current-shape response. No macOS physical test was performed. Phase 9 remains unimplemented.

## Files changed

- `src/DraftTG.Data/SeventeenLandsCardRatingsClient.cs`
- `src/DraftTG.Data/SeventeenLandsRatings.cs`
- `src/DraftTG.Application/LimitedStatisticsService.cs`
- `src/DraftTG.Application/LimitedStatisticsUpdate.cs`
- `tests/DraftTG.Data.Tests/SeventeenLandsCardRatingsClientTests.cs`
- `tests/DraftTG.Data.Tests/SeventeenLandsApiMigrationTests.cs` (new)
- `tests/DraftTG.Data.Tests/DraftTG.Data.Tests.csproj`
- `tests/DraftTG.Application.Tests/LimitedStatisticsTests.cs`
- `tests/DraftTG.App.Tests/CurrentApiIntegrationTests.cs` (new)
- `tests/DraftTG.App.Tests/DraftTG.App.Tests.csproj`
- `tests/Fixtures/17lands-api-card-data.json` (new, synthetic current-shape envelope)
- `ARCHITECTURE.md`
- `ROADMAP.md`
- `PHASE8_REPORT.md` (follow-up note)
- `PHASE8_1_REPORT.md` (new)

## Limits and workspace notes

The API is an unsupported third-party source whose shape/availability can change. Request volume remains conservative. The 20-row sanity floor intentionally rejects tiny datasets instead of filling badges with fabricated values. Genuine missing GIH remains unscored; 40 rows in the observed WOE dataset had no GIH. Existing exact-name identity/mapping limitations remain. No pool-aware scoring, color/archetype inference, synergy, curve, signals, deck building or Arena automation was added.

Earlier phase changes and the user's `global.json` were preserved. The pre-existing corrupt Git index remains untouched; review used a temporary index. Avalonia build logging used the same approved access as prior phases. Stop point: Phase 8.1.
