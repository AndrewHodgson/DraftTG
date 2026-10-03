# Phase 7 completion report

Phase 7 statistical-card-data integration is implemented. Phase 8 scoring has not started. Arena parsing and Domain card identity remain unchanged.

## Statistics model

`LimitedCardStatistics` is an immutable provider-neutral record in RecommendationEngine. It contains Domain `CardIdentifier`, nullable sample count, play rate, GIH/OH/drawn win rates, signed drawn improvement, ALSA, and ATA. Rates remain fractions. `LimitedCardStatisticsCatalog` copies and freezes lookups by identifier and rejects duplicate keys. `LimitedStatisticsContext` contains expansion and a neutral Premier/Traditional/Quick format enum. The temporary RecommendationEngine compile anchor was removed; no ranking or recommendation API was added.

## 17Lands client

Data owns `SeventeenLandsCardRatingsClient`, `HttpClient`, `System.Text.Json`, and the internal wire DTO. The endpoint is `https://www.17lands.com/card_ratings/data`, with expansion and format parameters. Requests carry `Accept: application/json` and `DraftTG/0.7 (Limited statistics; cached for 24 hours)` as User-Agent. Each request retrieves the whole environment, never one card.

The decoded fields are name, game_count, play_rate, win_rate, opening_hand_win_rate, drawn_win_rate, drawn_improvement_win_rate, avg_seen, and avg_pick. Only name is required. Unknown fields are ignored. Validation rejects invalid structure, duplicate names, nonfinite numbers, out-of-range probabilities, negative counts, and nonpositive ALSA/ATA. Improvement is a signed rate difference within [-1, 1]. Missing metrics remain null.

HTTP, non-JSON body, and validation failures yield stale cache or unavailable statistics. The final client validates the body regardless of Content-Type, allowing correctly structured JSON with a mislabeled header while rejecting real HTML. A 429 respects Retry-After without automatic retries; other failed loads receive a one-hour in-memory cooldown. Forced refresh cannot bypass 429. Production requests have a 30-second timeout. Caller cancellation propagates normally; shutdown cancels outstanding work and disposes the owned HTTP client.

## Cache

- Windows: `%LOCALAPPDATA%\DraftTG\limited-data\17lands\`
- macOS: `~/Library/Application Support/DraftTG/limited-data/17lands/`
- Deterministic allowlisted names, such as `HOB_QuickDraft.json`.
- 24-hour TTL; fresh disk or memory data causes no request.
- Context, requested/source format, fetch timestamp, and every row are validated before reuse.
- Successful empty datasets are cached.
- Replacement uses a temporary file and atomic move; unsuccessful refresh never replaces good data.
- Offline/failed refresh retains valid stale data and its original timestamp, with a diagnostic.
- Without valid data, names, history, and draft tracking remain usable.

## Expansion and format resolution

Application recognizes QuickDraft, PremierDraft, TraditionalDraft, and TradDraft event names with a 2–8 character uppercase set code and optional eight-digit date suffix. `QuickDraft_HOB_20260915` resolves to HOB.

If the event name cannot resolve, fallback requires a nonempty current pack, all identities resolved, and unanimous normalized set codes. Mixed or incomplete evidence yields unavailable statistics. Pick-Two and unknown modes remain unsupported for statistics.

Quick requests QuickDraft; Premier requests PremierDraft; Traditional requests TradDraft. Only a successful empty exact-format dataset permits a PremierDraft fallback. Result metadata records requested and actual contexts independently of freshness, so fallback and stale-cache status remain visible together.

## Card mapping

Application matches provider names exactly and case-sensitively against Scryfall-derived cards. Identities directly present in the current pack/history are safe matches, including bonus-sheet cards. Otherwise an active-set printing is preferred, then a unique exact-name printing. Multiple plausible candidates produce an ambiguity diagnostic; no arbitrary first printing is chosen. Missing rows and different-case/partial names do not fabricate statistics.

The same loaded environment is remapped locally as packs change. Scryfall remains the sole source of Domain card identity.

## Overlay and concurrency

Rows retain name, rarity, colors, order, and duplicates. They add GIH at one decimal, ALSA at two decimals, sample count, and a presentation-only `Low sample` indicator below 500 games. Missing metrics use `—`; loading uses `…`. Actual source format is displayed, including an explicit Premier Draft fallback label and stale-cache indicator.

A separate Application coordinator loads statistics in a background worker, while the established Arena coordinator continues consuming log updates. The bounded statistics stream updates presentation independently. Environment changes cancel or disregard old work. Snapshot checks keep queued results from replacing another pack. Tests exercise asynchronous arrival through the actual ViewModel runtime lifecycle as well as isolated formatting.

No sorting by GIH, scores, ranks, grades, recommendation highlights, or suggested picks were added.

## Files changed

The repository was entirely untracked at the start of this task. This inventory describes changes made during this session, rather than treating every untracked file as a new Phase 7 file.

Created:

- `src/DraftTG.RecommendationEngine/LimitedCardStatistics.cs`
- `src/DraftTG.Data/SeventeenLandsRatings.cs`
- `src/DraftTG.Data/SeventeenLandsCardRatingsClient.cs`
- `src/DraftTG.Application/DraftExpansionResolver.cs`
- `src/DraftTG.Application/LimitedStatisticsMapper.cs`
- `src/DraftTG.Application/LimitedStatisticsService.cs`
- `src/DraftTG.Application/LimitedStatisticsCoordinator.cs`
- `src/DraftTG.Application/LimitedStatisticsUpdate.cs`
- `tests/DraftTG.RecommendationEngine.Tests/DraftTG.RecommendationEngine.Tests.csproj`
- `tests/DraftTG.RecommendationEngine.Tests/LimitedStatisticsTests.cs`
- `tests/DraftTG.Data.Tests/SeventeenLandsCardRatingsClientTests.cs`
- `tests/DraftTG.Application.Tests/LimitedStatisticsTests.cs`
- `PHASE7_REPORT.md`

Modified:

- `src/DraftTG.Application/DraftTG.Application.csproj`
- `src/DraftTG.Application/DraftTGRuntime.cs`
- `src/DraftTG.App/MainWindowViewModel.cs`
- `src/DraftTG.App/MainWindow.axaml`
- `tests/DraftTG.App.Tests/MainWindowViewModelTests.cs`
- `DraftTG.sln`
- `ARCHITECTURE.md`
- `ROADMAP.md`

Removed:

- `src/DraftTG.RecommendationEngine/RecommendationEngineModule.cs` — obsolete compile anchor.

## Tests and validation

| Project | Before | Final passed |
|---|---:|---:|
| Domain | 42 | 42 |
| Data | 55 | 90 |
| Arena Integration | 94 | 94 |
| Application | 51 | 78 |
| Recommendation Engine | 0 | 6 |
| App/UI | 17 | 31 |
| **Total** | **259** | **341** |

Final commands:

```text
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
```

Build succeeded: **0 warnings, 0 errors**. Tests: **341 passed, 0 failed, 0 skipped**. All 259 previous tests remain intact and pass; 82 cases were added. Automated tests use fake HTTP handlers and never access live 17Lands.

Source checks confirmed that the wire DTO appears only inside Data, and no 17Lands networking was added to Domain, Arena Integration, RecommendationEngine, or App UI. Application references RecommendationEngine; Data still references only Domain. The engine contains provider-neutral statistical values and lookups only. Ordering, async updates, cache reuse, and failure isolation have explicit behavioral coverage.

## Live 17Lands check

Performed on 2026-09-26 using the implemented client and existing local Scryfall cache:

| Item | Result |
|---|---|
| Expansion | HOB |
| Requested format | QuickDraft |
| Actual statistical source | Unavailable |
| HTTP result | 200; `text/html; charset=utf-8` |
| Accepted rows | 0 in the initial check; Content-Type rejected before body inspection |
| Fetch/cache status | Unavailable; no valid ratings cache to use |
| Premier fallback | Not attempted: no successful empty dataset was established |
| Live mapped sample cards | None; no trustworthy provider values available |

The initial client rejected the text/html header before reading the response body. Thus this check does **not** establish whether the body was HTML or mislabeled JSON. The final client parses and validates the body regardless of Content-Type; fake responses verify both mislabeled JSON acceptance and real HTML rejection. The final behavior has not been rechecked live.

The initial standalone validation harness inadvertently made **three** requests while testing repeated loads. Requests were stopped; the client then gained a one-hour failed-load cooldown with a fake-HTTP regression test. No further live endpoint calls were made. This exceeded the intended single-request validation budget and is reported explicitly.

Mirkwood Mediator, Vow to Erebor, Ordinary Bear, Dwarven Mattock, and Mountain were among the names checked, but no live statistics can be claimed for them. The ten-card sanitized HOB fixture separately passes parser → Scryfall identity → fake ratings → exact-name mapping checks and confirms unchanged pack order.

The local Scryfall cache contained **118,404 cards**. Existing Player.log replay produced an active Quick Draft with **11 resolved current-pack cards**, demonstrating identity/tracking availability despite statistics failure. Arena itself was not running. An offline desktop launch created the overlay window, but macOS screen capture could not capture it; visual layout and physical over-Arena validation remain unverified. Runtime tests confirm unavailable statistics preserve names/history and closing cancels pending statistics normally.

## Risks and follow-up

The ratings endpoint is undocumented and may change or return unexpected content. Exact-format datasets may be missing; sparse samples and exact provider/Scryfall name differences remain visible limitations. Cooldowns are in memory, and concurrent separate app processes do not share a locking protocol.

[17Lands attribution](https://www.17lands.com/) and the [official public-dataset source](https://www.17lands.com/public_datasets) are documented in ARCHITECTURE.md, including CC BY 4.0 unless otherwise noted. A future public-dataset adapter can produce the same neutral catalog; Phase 7 does not download those datasets.

A future live session should confirm genuine provider values and visual layout using body validation regardless of the provider’s Content-Type header. Phase 8 must separately design scoring and sample-confidence handling. It is not implemented here.
