# Phase 9D — Successful / Trophy Deck Evidence

Implemented 2026-10-04. **Evidence pipeline implemented; trophy scoring disabled.** All previous recommendation values and ranks remain reproducible. WGC production capture is unchanged.

## Decision and measured source audit

The exact JSON requests were discovered in the public page's referenced `Routes.2d00f83e0ae5712e0c41.bundle.js`, then checked with a single bounded trophy query and a few individual schema probes. No rendered-page scraping, guessed endpoint, aggressive pagination or bulk download was used.

| Source item | Measured finding |
|---|---|
| Trophy source | `POST https://www.17lands.com/api/trophies/` |
| Body | `expansion`, `event_type`, `deck_colors`, `ranks`, `card_names`; array filters use empty arrays for all ranks/cards |
| Exact format tokens | `QuickDraft`, `PremierDraft`, `TradDraft` |
| Query audited | WOE / QuickDraft / BG / all ranks / no card filter |
| Summary schema | Envelope `copyright`, `notes`, `data`; rows include `aggregate_id`, `deck_index`, wins/losses, start/end rank, colors, time, has_draft |
| Build source | `GET https://www.17lands.com/api/deck/draft/?draft_id=<aggregate_id>&deck_index=<index>` |
| Build schema | Envelope; data has event_info, card dictionary, Maindeck/Sideboard repeated-ID groups, main_colors/splash_colors |
| Trophy semantics | Reaches event_info.trophy_win_count in matches; retain best_of_n. Audited QuickDraft metadata: maximum 7, best-of-one |
| Sample behavior | UI says up to 100 most recent matching decks. No limit/page/cursor is supplied by the UI request. Audit returned 60; this does not prove a separate hard server cap of 60 |
| Pair behavior | BG filter also returned BGu/BGr/BGw variants. 34 of the 60 were strict BG. Initial adapter excludes splashes |
| Multiple builds | 10 returned summaries had nonzero deck_index; a probed index-2 event had three deck routes. Summary nominates one build; no claim that it is final or most frequently played |
| Deduplication | 60 distinct aggregate_id values in this query; adapter counts a stable event once |
| Recency | Summary timestamps 2026-09-29 19:18:50 through 2026-10-04 11:34:22 UTC; retrieved at approximately 2026-10-04 14:13:21 UTC |
| Rate limits | No 429 observed during the audit and no numeric quota exposed. Implementation handles 429/Retry-After; no deliberate rate-limit probing |
| Support status | Internal, unversioned website JSON API; no supported external contract established |
| Current use notice | API envelope explicitly restricts outside use, including private use, to public datasets |

The UI source and definition are from [17Lands Trophy Decks](https://www.17lands.com/trophy_decks) and its [referenced public bundle](https://www.17lands.com/static/Routes.2d00f83e0ae5712e0c41.bundle.js). API status/notice comes from the actual responses retained locally for the audit. [17Lands usage guidelines](https://www.17lands.com/usage_guidelines) also discourage automated scraping and give no stability guarantee. The adapter fails closed on the observed restricted-use notice rather than treating public accessibility as permission for runtime corpus use. It stops before build requests/cache writes and reports the reason in the rail. No source restriction is silently bypassed by another hostname.

Current supported-format loss sanity checks follow the audited [Arena Quick Draft structure](https://magic.wizards.com/en/mtgarena/draft) and [Traditional Draft structure](https://magic.wizards.com/en/news/mtg-arena/mtg-arena-state-game-streets-new-capenna-2022-04-21): fewer than three losses for Bo1 trophies and no match losses for a current three-match Traditional trophy. Maximum wins are read from event metadata, not globally hardcoded to seven. Format, set, event ID, record, pair and build colors must agree before a sample is accepted.

## Public dataset findings and WOE QuickDraft

The current [public dataset catalog](https://www.17lands.com/public_datasets) was checked through its backing Prismic `public-data` document. WOE PremierDraft and TradDraft have draft/game/replay files dated 2023-10-17; WOE Sealed and TradSealed also have files. **WOE QuickDraft has no listed public dataset.** The only QuickDraft row in the current catalog is VOW draft-only data dated 2021-12-02. It does not establish WOE QuickDraft availability. Public files are typically compressed CSV/JSON under CC BY 4.0. No large archives were downloaded or made startup dependencies.

Therefore: WOE QuickDraft trophy summaries were technically retrievable (60), but **zero permitted complete production corpus samples were loaded**. Premier/Traditional data is not substituted. Even usable exact-format trophy data would remain diagnostic because of the independent denominator mismatch below.

## Pool/deck semantics and identity

An individual build supplies repeated-ID maindeck and sideboard lists and a name dictionary. Copies are preserved. The audited build contained 40 maindeck cards and 18 sideboard cards. Its separate `/data/pool?draft_id=...` response contained 42 drafted cards; the extra 16 build-pool cards were added Swamp/Forest basics. Thus maindeck + sideboard is recorded as `MaindeckAndSideboard` available-pool basis, not mislabeled as the drafted pool. Complete deck counts remain accessible for later work.

One provider-nominated deck_index is used per aggregate_id. Identical duplicate summary rows are deduplicated; conflicting duplicates are rejected. Builds are not counted as separate trophy events. There is no extra request for every build or game merely to infer most-played/final selection; that policy remains unverified and is exposed as provenance.

Production composition resolves provider card IDs with the existing Arena/Scryfall resolver. A validated front-face name can map to the canonical multiface card name. Unknown/ambiguous IDs retain exact provider names, which cannot attach to arbitrary current-pack cards. Each occurrence joins by canonical card identity/name, never array order. Duplicate current-pack cards keep independent occurrences and share the same evidence.

## Statistical decision

[17Lands metric definitions](https://www.17lands.com/metrics_definitions) specify that Play Rate is weighted by **games and card copies**. Phase 9D's requested pool-to-maindeck conversion counts **events containing at least one copy**. These denominators and observation units differ, including across Bo3 sideboarding. A subtraction cannot be called a comparable trophy lift.

**Scoring is disabled, with adjustment exactly 0.** Final value, rank, tie order and Context Pick exactly preserve Phase 9C. No prior/lift formula, score-enabling switch, raw prevalence bonus, copy-count bonus or second GIH reward was introduced.

Diagnostics calculate:

- Pool exposures: known-pool events with at least one copy.
- Maindeck conversion: known-pool events main-decking the card / pool exposures.
- Copy utilization: maindeck copies in known-pool events / pool copies; diagnostic only.
- Deck presence: maindeck events / all eligible corpus events; diagnostic only.
- Average copies when played: maindeck copies / maindeck events; diagnostic only.
- Corpus confidence: clamp(eligible corpus / FullCorpusConfidenceDecks, 0, 1).
- Exposure confidence: clamp(pool exposures / FullCardExposureDecks, 0, 1).
- Combined diagnostic confidence: active 9C confidence * corpus confidence * exposure confidence.

Missing pools are unknown and never treated as zero. Their maindecks contribute to prevalence/copy diagnostics but not to a conversion numerator. Off-pair/third-color and unknown metadata are ineligible; colorless requires observed maindeck use in the active-pair corpus. An unsettled archetype does not select or combine trophy corpora. Small samples remain evidence only.

The requested future comparable-baseline formula is documented, **not executed**: shrunk rate `(n*r + 10*q)/(n+10)`, lift minus q, affinity clamp(lift/.20,-1,1), combined confidence multiplier, maximum +/- .010, then clamp final to [0,1]. It needs a separate semantic audit before implementation. There is no live +/-1pp adjustment to tune in Phase 9D.

## Configuration and cache

| Active configuration | Default |
|---|---:|
| MinimumTrophyCorpusDecks | 20 |
| MinimumCardTrophyExposure | 5 |
| FullCorpusConfidenceDecks | 50 |
| FullCardExposureDecks | 15 |
| MaxCorporaPerDraft | 3 (0..10) |
| MaximumDecks | 20 (1..100) |
| Cache TTL | 24 hours |
| Failure cooldown | 1 hour |
| Request spacing | 1 second |
| Immediate automatic retries | 0 |
| Cache schema | 1 |
| Query mode | recent-strict-pair |
| Rank/card filter | all ranks / none |
| HTTP timeout | Existing runtime 30 seconds per request |

A usable source would require one summary request and at most 20 nominated-build requests per corpus, with no pagination. Current restricted responses require only the summary request and never proceed to build fetching. The coordinator remembers availability/unavailability per exact key for a draft; no per-pick request occurs. Cache is distinct under `limited-data/17lands/trophy`; filename/content validates expansion, exact format, normalized pair, rank, query mode, limit, schema and endpoints. Writes use unique temporary files and atomic replacement. TTL, future-timestamp, shape and identity validation reject malformed caches. Stale valid cache can survive transport/schema failure, while an explicit new use restriction prevents stale fallback. Retry-After applies across pair requests.

## Implementation and files

Added provider-neutral Domain types: SuccessfulDeckKey/Format/Source/PoolBasis, SuccessfulEventCriteria, SuccessfulDeckSample, SuccessfulDeckCorpus, SuccessfulDeckProvenance, SuccessfulDeckLoadResult and ISuccessfulDeckProvider. Added recommendation types: TrophyEvidenceCatalog, TrophyCardEvidence, TrophyRecommendationConfiguration, TrophyCardRecommendation/Result and TrophyRecommendationEngine. Data contains the transport DTOs and bounded cached adapter. Application owns lazy orchestration, status and rail presentation; UI has no provider JSON.

New production files:

- `src/DraftTG.Domain/SuccessfulDeckCorpus.cs`
- `src/DraftTG.RecommendationEngine/TrophyRecommendationEngine.cs`
- `src/DraftTG.Data/SeventeenLandsTrophyClient.cs`
- `src/DraftTG.Application/TrophyRecommendationPresentation.cs`

Modified production files:

- `src/DraftTG.Application/LimitedStatisticsService.cs`
- `src/DraftTG.Application/LimitedStatisticsCoordinator.cs`
- `src/DraftTG.Application/LimitedStatisticsUpdate.cs`
- `src/DraftTG.Application/CurrentPackCardPresentation.cs`
- `src/DraftTG.Application/DraftTGRuntime.cs`
- `src/DraftTG.App/MainWindowViewModel.cs`

Five focused new test files: SuccessfulDeckCorpusTests (Domain), TrophyRecommendationTests (RecommendationEngine), TrophyDataTests (Data), TrophyOrchestrationTests (Application), TrophyPresentationTests (App). Documentation: ARCHITECTURE.md, ROADMAP.md and this report. Existing test files are unchanged.

Active archetypes load asynchronously without blocking Phase 9C. New drafts/environment transitions cancel and fence workers. Pair switches immediately stop using the previous key. Late old-pair/draft responses cannot attach; current snapshot/revision is recomputed on completion. Loading/unavailable results keep prior recommendations. The rail exposes Stats/Pool/Lane/Archetype/Final ranks and evidence or its availability/disabled reason. Badges retain raw overall GIH, ALSA, geometry, occurrence order and confirmed placement.

## Validation

**23 focused new tests; 777 total passing; no failed/skipped tests.** All 754 existing tests are retained without modification.

| Assembly | Passing | Added |
|---|---:|---:|
| Domain | 44 | 2 |
| RecommendationEngine | 121 | 5 |
| Application | 145 | 5 |
| Data | 147 | 9 |
| ArenaIntegration | 107 | 0 |
| App | 213 | 2 |
| Total | 777 | 23 |

Coverage includes immutable copies/criteria/corpus identity, synthetic exact conversion/copy aggregates, unknown pools, duplicate occurrences, format/pair/set gates, colorless/off-pair eligibility, sample confidence, the observed restricted notice, verified POST queries, metadata record checks, canonical ID naming, bounds/dedup/splashes, cache identity/atomic write/stale fallback/corruption/429, lazy loading/no-per-pick calls, archetype changes, old-draft responses, request budget and UI metrics/geometry/status/identity. Scoring-formula/reranking tests are intentionally inapplicable because scoring is disabled.

Commands:

```text
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
dotnet build DraftTG.sln -p:DraftTGPortableBuild=true
dotnet test DraftTG.sln --no-build -p:DraftTGPortableBuild=true
```

Final Windows and portable builds: **0 warnings, 0 errors**. Windows/portable full suites: **777/777**. One intermediate Windows build encountered an Avalonia file lock; an immediate retry succeeded, with no process termination or source workaround. Build logs, full TRX results, test summaries and source verification are in `artifacts/phase9d-validation`. Source hashes verify 35 explicitly protected files unchanged and show only the six existing production files above changed; all other existing source/test/tool files remain unchanged. In particular the original Phase 8/9A/9B/9C engines, statistics client/mapper, Arena parser, capture backends, artwork matcher and placement are untouched.

**Windows physical active-archetype corpus validation was not performed:** the current WOE QuickDraft source refuses permitted external use, and there is no exact public dataset fallback. Synthetic UI/async tests are not represented as live Arena evidence. Native macOS execution is also unvalidated; portable compilation/tests ran on Windows. Another draft cannot resolve the source/denominator problems. Once usable exact-format data is available, physical one-archetype corpus/status validation remains necessary before claiming live evidence loading. Existing user-confirmed capture acceptance is preserved, not retested or redesigned.

## Bias, limits and future foundation

Successful events/players, cards opened/drafted, recent tracked samples and construction decisions all bias this corpus. A nominated build is not proven to be the most-played/final deck. Added basics are not drafted availability. No corpus is labeled ALL_TIME. Current restrictions and exact-format availability prevent a real WOE QuickDraft evidence sample from being shipped. The evidence layer supplements GIH and cannot replace it.

Full readonly sample structures retain the foundation for later deck composition/copy distributions and suggestions. No deck builder, add/remove marks, mana base, curve/quota rules, co-occurrence scoring, Oracle-text synergy, splash inference, Phase 10 or capture redesign is included. Work stops after Phase 9D.
