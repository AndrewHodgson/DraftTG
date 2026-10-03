# DraftTG Architecture

DraftTG is a shared Windows and macOS desktop application written in C# 14, targeting .NET 10 LTS and Avalonia 12. The solution keeps reusable product behavior independent of the desktop shell and uses operating-system-specific code only for genuine platform differences.

Supported deployment targets are Windows 10 22H2 or newer, Windows 11, and macOS 14 or newer.

## Projects

- **DraftTG.Domain** — Immutable, provider-neutral card, catalog, color, draft-position, pick, pack, history, and snapshot values. It has no Avalonia, JSON, filesystem, networking, persistence, Scryfall, Arena, or operating-system dependency.
- **DraftTG.Data** — Scryfall identity decoding and bulk caching, plus isolated 17Lands ratings HTTP/JSON and whole-environment caching. It depends only on Domain and .NET platform libraries.
- **DraftTG.ArenaIntegration** — Arena log-location resolution, asynchronous raw-file monitoring, Arena-specific identifiers, stateless draft-record parsing, and reconstructed Arena draft-session state. Its completed behavior does not currently require a Domain project reference.
- **DraftTG.Application** — UI-free cross-module orchestration. It resolves Arena card identifiers through imported Data metadata, adapts supported Arena state snapshots into provider-neutral Domain snapshots, and coordinates the sequential runtime pipeline. It depends on Domain, Data, Arena Integration, and Recommendation Engine.
- **DraftTG.RecommendationEngine** — Immutable provider-neutral Limited statistics, context/format values, and fast identity-keyed catalogs. It depends only on Domain. Phase 7 contains no scoring or recommendations.
- **DraftTG.App** — The Avalonia desktop overlay and presentation layer. It references Application, owns the UI lifecycle and UI-thread dispatch, and contains no log parsing, filesystem access, card-data decoding, or Arena-ID mapping.
- **DraftTG.Domain.Tests**, **DraftTG.Data.Tests**, **DraftTG.ArenaIntegration.Tests**, **DraftTG.Application.Tests**, **DraftTG.RecommendationEngine.Tests**, and **DraftTG.App.Tests** — Focused behavioral tests separated by architectural boundary.

## Dependency Direction

Dependencies point inward:

```text
DraftTG.App ────────────────> DraftTG.Application
                                  ├──> DraftTG.Data ───────> DraftTG.Domain
                                  ├──> DraftTG.ArenaIntegration
                                  ├──> DraftTG.RecommendationEngine ──> DraftTG.Domain
                                  └────────────────────────> DraftTG.Domain

DraftTG.RecommendationEngine ──────────────────────────────> DraftTG.Domain
```

Arena Integration and Data do not depend on each other. Arena identifiers and wire-format values must not flow into Domain, and Domain must never depend on an infrastructure, Application, or UI project. Cross-layer communication uses immutable values and focused contracts without cycles or a dependency-injection container.

## Card Catalog Boundary

`CardCatalog` copies its inputs, preserves deterministic order, rejects duplicate identifiers, and provides identifier and exact, case-sensitive name lookup. Duplicate names remain valid and retain input order.

`DraftTG.Data` owns focused Scryfall DTOs and maps external IDs, color codes, rarities, set codes, collector numbers, and multi-faced color fallbacks into Domain values. Top-level colors take precedence; otherwise face colors are unioned; otherwise the card is colorless. Scryfall `id` becomes `CardIdentifier` only at this import boundary.

Optional positive Scryfall `arena_id` values are imported alongside the catalog as auxiliary Data metadata. Missing or null values do not exclude cards from the catalog, and invalid non-positive values remain focused import errors. A uniquely associated integer is stored in the immutable direct mapping. When different Scryfall printing IDs share an Arena ID, the direct mapping is removed and every distinct `CardIdentifier` is retained in deterministic first-seen order in an immutable ambiguity mapping. All affected printings remain in `CardCatalog`; DraftTG never selects an arbitrary candidate. Neither mapping is stored on Domain `Card`, and Data does not reference Arena types.

The legacy JSON-array decoder and production JSONL loader share one per-record normalizer and one incremental catalog builder. Duplicate Domain identifiers and invalid provider values therefore fail consistently across fixture and streamed imports, while legitimate Arena-ID collisions produce the same explicit ambiguity metadata in both paths.

## Production Card Data

Production card identity data follows this flow:

```text
Scryfall /bulk-data metadata
        ↓
default_cards JSONL.gz
        ↓
DraftTG.Data
        ↓
local validated cache
        ↓
ScryfallCardCatalogData
        ↓
DraftTG.Application
        ↓
ArenaCardResolver
        ↓
ArenaDraftSnapshotAdapter
```

`ScryfallBulkDataClient` performs one HTTPS metadata request to `https://api.scryfall.com/bulk-data`, selects exactly the `default_cards` entry, and uses its current `jsonl_download_uri`. `default_cards` is used because DraftTG requires printing-level Scryfall identity and Arena IDs rather than Oracle-level identity. Requests carry an explicit DraftTG User-Agent, and the metadata request accepts JSON. The bulk response is streamed directly to an application-controlled temporary filename; remote filenames never determine local paths.

`ScryfallJsonlGzipCardDataLoader` opens the downloaded file as `FileStream → GZipStream → StreamReader`, reads one JSON object per line, and retains only normalized Domain cards plus unique and ambiguous Arena-ID mappings. It never materializes the compressed file, decompressed dataset, JSONL lines, or raw Scryfall object graph as one collection. The final normalized `CardCatalog` and mappings remain in memory.

Scryfall `default_cards` is printing-oriented: each Scryfall printing `id` becomes one Domain `CardIdentifier`, while one Arena group ID may legitimately appear on multiple printings. That collision is provider evidence of ambiguity, not a reason to reject the full dataset or guess a printing.

The cache lives under `%LOCALAPPDATA%\DraftTG\card-data` on Windows and `~/Library/Application Support/DraftTG/card-data` on macOS. It contains the fixed files `scryfall-default-cards.jsonl.gz` and `metadata.json`; metadata records the dataset type, Scryfall `updated_at`, last successful check, and cache format version. OS path selection is localized behind `IApplicationDataPathProvider`, and tests may inject a custom root.

The default refresh-check interval is seven days and can be overridden or forced programmatically. A due check downloads bulk content only when the remote dataset timestamp is newer. Replacement data is downloaded to a unique temporary file, fully decompressed and normalized, and only then moved over the cache; metadata is also staged before replacement. Failed downloads or validation do not overwrite a valid cache. Cancellation propagates normally and performs best-effort temporary-file cleanup.

A validated fresh cache requires no network. When a due metadata check, download, or replacement validation fails, the provider returns the existing data with `CacheAfterRefreshFailure` and a small diagnostic. A first run with neither a valid cache nor a successful download fails explicitly rather than fabricating a catalog. Live draft card resolution remains entirely local and never performs per-card Scryfall requests.

`DraftTGCardDataBootstrapper` is the UI-independent card-data seam. It requests `ScryfallCardDataLoadResult` from Data and returns the `CardCatalog`, `ArenaCardResolver`, `ArenaDraftSnapshotAdapter`, dataset timestamp, source status, and refresh diagnostic needed for runtime composition. `DraftTGRuntimeFactory` is the production Application composition root: it performs that bootstrap asynchronously and then creates the default platform log source, parser, state engine, snapshot adapter, and coordinator. Its immutable `DraftTGRuntime` exposes the draft coordinator, statistics coordinator, catalog, and presentation-safe card-data status; HTTP and filesystem implementation details remain private. Neither type creates an Avalonia object. 17Lands is a separate supplemental statistical-data source and is not part of card identity bootstrap. The factory constructs its client and coordinator but makes no ratings request until an active draft environment is known.

## Arena Log Locations

Operating-system path resolution is localized behind `IArenaLogLocationProvider`:

- macOS: `~/Library/Logs/Wizards Of The Coast/MTGA/Player.log`
- Windows: `%USERPROFILE%\AppData\LocalLow\Wizards Of The Coast\MTGA\Player.log`

`ArenaLogLocationProviderFactory` contains the only runtime OS selection. Explicit custom paths remain supported for testing and future user selection.

## Arena Log Source

`FileArenaLogSource` implements `IArenaLogSource` as an asynchronous `IAsyncEnumerable<ArenaLogSourceEvent>`. A thread-pool producer reads from byte zero, follows appended bytes, preserves line order, buffers incomplete lines and split UTF-8 sequences, uses deterministic replacement for malformed UTF-8, waits for initially absent files, and responds to cancellation. It emits raw `Line` and `SourceReset` events only; it does not interpret Arena records.

Truncation and delete/recreate boundaries are detected on both platforms. Replacement detection uses a shared content-generation probe in addition to file length, avoiding a Unix inode assumption that would not port to Windows. A replacement whose observed prefix is byte-identical and whose length never falls below the prior offset may not be distinguishable until later content diverges. If production fixtures expose that case, a narrow Windows file-ID adapter and macOS file-ID adapter can replace the probe without changing the source contract.

Meaningful Arena draft records require Detailed Logs to be enabled in MTG Arena. DraftTG does not change or infer that client setting.

## Arena Draft Parser

`ArenaDraftLogParser` is a synchronous, deterministic, stateless translation from one raw source event to zero or more Arena draft facts. It filters for supported record families before JSON decoding, handles known object or string-encoded `request` and `Payload` shapes, preserves Arena card ordering and duplicates, and returns no events for irrelevant lines. Malformed records that clearly match a supported family throw `ArenaDraftLogParseException` with a focused error category.

`ArenaCardIdentifier` remains an Arena group ID and is not a Domain `CardIdentifier`. Human `Draft.Notify`, `CardsInPack`, and human-pick coordinates are treated as one-based. Supported BotDraft `PackNumber` and `PickNumber` values are normalized from zero-based wire values to one-based `ArenaDraftCoordinate` values. These assumptions are fixture-backed but may require updates when Arena changes its logs.

The parser recognizes Premier, Traditional, Quick, Pick-Two, and unknown draft event names while ignoring Sealed joins. Multi-card picks are preserved, but Pick-Two recommendation rules are not implemented. The parser performs no sequencing, deduplication, active-session tracking, history reconstruction, catalog lookup, `DraftHistory` creation, or `DraftSnapshot` creation. Arena-ID resolution remains outside the parser.

Human P1P1 may use the supported nested `CardsInPack` fallback because `Draft.Notify` can be absent. This alternate record is not guaranteed to arrive early enough for a pre-pick recommendation in every Arena client version.

### 2026 Quick Draft framing and recovery

MTG Arena 2026.63 writes a Quick Draft status exchange as three independent physical log lines:

```text
==> BotDraftDraftStatus { request ... }
<== BotDraftDraftStatus(...)
{"CurrentModule":"BotDraft","Payload":"{...}"}
```

The incoming `<== BotDraftDraftStatus(...)` line is a transport framing marker, not a malformed JSON record, and emits no event or diagnostic. The following `CurrentModule = BotDraft` envelope is independently recognized as the relevant record; its string-encoded `Payload` is decoded through the parser's existing narrow nested-object support. The parser remains stateless and does not assemble physical lines.

BotDraft pack and pick coordinates remain zero-based on the wire and are normalized to one-based `ArenaDraftCoordinate` values. For a supported single-card Quick Draft (`NumCardsToPick == 1`), a `PickedCards` array may reconstruct a resumed or mid-draft history when its count exactly equals `(PackNumber * 14) + PickNumber` and the coordinate fits the three-pack, fourteen-pick Domain model. Candidate IDs map in order to P1P1 through P3P14; recovered `PickSubmitted` events are emitted before the current `PackPresented` event. An incoherent count or multi-card status does not synthesize history, but it also does not discard an otherwise valid actionable pack. Repeated status bodies rely on the state engine's existing structural idempotence rather than parser-side deduplication.

## Arena Draft State Engine

`ArenaDraftStateEngine` is the single synchronous mutable owner of reconstructed Arena draft-session state. It consumes one `ArenaDraftLogEvent` at a time and exposes immutable `ArenaDraftStateSnapshot` values containing session status, Arena identity and mode, the current actionable pack, and completed multi-card pick records. Its owner must serialize `Apply` calls; the engine creates no tasks, threads, channels, or locks. UI code must consume snapshots and must not own reconstruction rules.

The engine accepts explicit starts and can infer an active session from a pack or pick when DraftTG attaches mid-draft. A later non-null draft identifier is adopted when the session has none. Conflicting non-null draft identifiers begin a fresh inferred session instead of merging histories. When identifiers are unavailable, a different explicit raw event name or mode is the conservative secondary new-session signal; otherwise ambiguous events remain in the current session.

Exact starts, packs, picks, and completions are idempotent. Same-coordinate pack or pick facts with different ordered card IDs throw `ArenaDraftStateConflictException`. Picks are retained in normalized pack/pick order even when they arrive out of order or contain gaps. A matching pick consumes the current pack, and late pack replay cannot resurrect an already-picked coordinate. Completion preserves identity and picks while clearing the actionable pack. A newly observed same-session pick may enrich completed history without reopening the session, while post-completion packs remain non-actionable; a later conflicting draft ID starts a fresh session.

State remains entirely Arena-native: packs and picks contain `ArenaCardIdentifier`, including ordered multi-card Pick-Two selections. The engine deliberately does not create Domain `DraftSnapshot` values and performs no Arena-to-`CardIdentifier` or catalog resolution. Application performs those conversions without changing the engine.

## Application Translation Boundary

`DraftTG.Application` owns translations that require both provider-specific infrastructure and provider-neutral Domain types but belong in neither. Card identity follows two distinct paths:

```text
Scryfall printing id
    ↓
CardIdentifier

Arena grpId
    ↓
ArenaCardIdentifier
    ↓
ArenaCardResolver
    ↓
Resolved | Missing | Ambiguous
    ↓ when Resolved
CardIdentifier
    ↓
CardCatalog
```

`ArenaCardResolver` copies the immutable unique and ambiguous mappings and performs synchronous, exact, in-memory lookup. `Resolved` contains exactly one Domain identifier, `Missing` contains none, and `Ambiguous` contains every candidate in deterministic order without a guessed identifier. `TryResolve` remains a convenience API and returns true only for `Resolved`. No per-card request, fallback lookup, name matching, set-based guessing, or other networking occurs during resolution. Future 17Lands context may inform a separately designed policy, but it is not used to resolve this ambiguity.

`ArenaDraftSnapshotAdapter` is stateless and converts an active, actionable Arena snapshot into the existing Domain recommendation boundary:

```text
ArenaDraftStateSnapshot
        ↓
ArenaDraftSnapshotAdapter
        ↓
DraftSnapshot
```

Premier and Quick map to `BestOfOne`; Traditional maps to `BestOfThree`. Every current-pack and historical coordinate is validated through Domain `PackNumber` and `PickNumber`, current-pack order and duplicates are preserved, coordinate gaps remain gaps, and every single-card historical pick is retained in its normalized order. Conversion is all-or-nothing: missing and ambiguous Arena identifiers are returned as separate deterministic, deduplicated diagnostic lists rather than producing a partial snapshot. If both occur, `AmbiguousArenaCard` is the primary availability because it represents known one-to-many identity, while both lists remain available to consumers.

The immutable availability result also distinguishes Idle, Completed, missing current pack, unsupported coordinates, and unsupported draft modes. Pick-Two and unknown modes are not converted. Any historical pick containing other than exactly one card is unsupported because the current Domain `DraftPick` cannot represent it without discarding information.

## Runtime Draft Session Coordination

`DraftSessionCoordinator` is the single application-level owner connecting the completed runtime stages:

```text
Player.log
    ↓
IArenaLogSource
    ↓
ArenaDraftLogParser
    ↓
ArenaDraftStateEngine
    ↓
ArenaDraftSnapshotAdapter
    ↓
DraftSessionCoordinator
    ↓
MainWindowViewModel / future recommendation pipeline
```

The source owns filesystem location, reading, and generation-reset detection. The parser owns syntax interpretation and may produce zero, one, or multiple semantic events from one source event. The state engine remains the sole mutable owner of reconstructed Arena session state. The snapshot adapter owns Arena-to-Domain translation. The coordinator owns sequential consumption and application of those stages and exposes immutable `DraftSessionUpdate` values; the Avalonia ViewModel owns presentation only.

One coordinator permits one active async enumeration. It does not emit synthetic initial Idle state. Each later independent run silently resets the state engine before the source replays `Player.log` from byte zero, preventing old in-memory facts from being combined with replayed input. Parsed events are applied serially in parser order, and an update is emitted only when `ArenaDraftStateEngine.Changed` is true. The coordinator does not maintain a second deduplication store.

Irrelevant lines produce no update. A malformed relevant record becomes a compact parse diagnostic carrying the current immutable state and snapshot availability, and subsequent source events continue normally. Fatal source enumeration errors terminate the run unchanged. Requested cancellation ends the run as normal lifecycle behavior and is not converted into a diagnostic. Source resets pass through the parser and state engine without restarting the coordinator.

The coordinator has no Avalonia or UI-thread dependency, starts no worker task of its own, and does not parallelize parsing or state mutation. Consumers are responsible for marshalling emitted updates to their presentation thread. It contains no filesystem implementation, raw-log UI contract, catalog acquisition, networking, persistence, or recommendation processing. Card-data bootstrap completes separately before a resolver-backed adapter is supplied to the coordinator.

## Gameplay Overlay (Phase 7.2)

```text
DraftTG UI — one OverlayDesktopSession / MainWindowViewModel runtime owner
    ├── ControlRailWindow — interactive, draggable, always on top
    │       └── one temporary flyout: status / history / calibration
    └── CardOverlayWindow — transparent, always on top, native mouse passthrough
```

`MainWindowViewModel` retains the tested Scryfall/Arena/statistics presentation and runtime lifecycle. `OverlayViewModel` consumes its completed pack-presentation events, publishes an atomic ordered badge list, and updates statistics on existing badge instances. The two windows share this state; neither creates another draft coordinator. The old large `MainWindow` panel was removed.

The 52-DIP rail starts near the working-area left edge and remains interactive. Its explicit pointer-press grip calls Avalonia `BeginMoveDrag`, rather than relying on title-bar role hints. Buttons open status/diagnostics, toggle statistics, open drafted history, open calibration, or close DraftTG. Flyouts are temporary, dismissible, and replace one another. Routine warnings are a small `!`; details appear only inside the flyout. Fatal runtime/startup errors open status once. There is no general settings system.

The badge window has no opaque root panel, normal chrome, or taskbar entry. Normal mode draws only small rounded, translucent dark badges with GIH WR, ALSA, and a subtle low-sample asterisk. Names and numbered slot guides are shown only during calibration. Missing values use `—`, loading uses `…`. Pack order and duplicate copies are preserved; between-pick and unsupported/unavailable pack states remove all badges. Statistics visibility does not stop monitoring. No ranking or suggested-pick styling exists.

### Normalized layout and manual calibration

Application owns `DraftCardLayout`, ordered `CardSlot` values, `OverlayScreen`, and `OverlayCalibration`; Domain, ArenaIntegration, and RecommendationEngine know nothing about screen geometry. Slots use normalized X/Y/Width/Height within a calibrated region. The renderer scales these to window client DIPs, keeping compact badges at each slot's lower center. Layout lists are copied and validated as 14 ordered, bounded slots. Slots identify pack positions, never recommendations.

The initial profile is a **manual starting point** of seven columns and two rows for up to 14 cards. Calibration offers four-, five-, six-, and seven-column row-major profiles, and the settings document stores all 14 normalized rectangles so coordinates can be adjusted without renderer changes. Profiles retain fixed slot positions as packs shrink. Arena may reflow/center cards differently for a particular aspect ratio, card count, or client setting: no profile is claimed to fit every arrangement. The user must choose/calibrate a matching profile and recalibrate when Arena's arrangement changes. Automatic per-pack screen inference is not implemented.

First run displays `Set position` on the rail and `Set overlay position` in the control tooltip/panel. Badges stay hidden until a valid calibration is saved; runtime loading and tracking continue. In edit mode, native click-through is disabled, numbered slot guides and the overall boundary appear, and the top edge uses `BeginMoveDrag`. Eight edge/corner targets use `BeginResizeDrag` with the corresponding `WindowEdge`; corners are 20 DIPs and edges are 16 DIPs thick, above the guides in hit-test order. `CanResize=true` and `SizeToContent=Manual` allow native geometry changes. No manual drag-delta calculation is used. On macOS, see the installed Avalonia limitation below. Save captures the actual window Position and ClientSize through `OverlayWindowGeometry`, using the screen coordinate scale, validates the whole region on a single screen, and restores passive mode. Reopening restores this saved geometry; presentation updates do not reset window bounds. Cancel restores the previous geometry, or hides the uncalibrated layer. Closing the flyout leaves edit mode available until Save or Cancel is chosen.

`OverlayCalibrationService` serializes only a version, desktop display bounds/coordinate scale, normalized region, and slot rectangles. `OverlaySettingsFile` in Data owns atomic local JSON reads/writes:

- Windows: `%LOCALAPPDATA%\DraftTG\overlay-calibration.json`
- macOS: `~/Library/Application Support/DraftTG/overlay-calibration.json`

Missing settings are normal first run. Corrupt, nonfinite, out-of-bounds, mismatched display/scale, or undersized geometry is rejected without stopping the runtime. A usable region is at least 400 × 220 DIPs and fully contained in one known display. Display-change notifications invalidate unsafe calibration and keep the rail reachable. File errors become a compact warning; cancellation preserves the previous document. The format is deliberately small, with no settings database or monitor-following system.

### Calibration exit hotfix (Phase 7.4)

`IsCalibrating` remains the authoritative editing state. The single `CalibrationLayer` visibility binding gates the outer border, slot rectangles/labels, handles, instructions, and tint. `Guides` is also empty outside calibration. Badges occupy a separate subtree and remain visible for the active pack when statistics are enabled. The root/window background stays fully transparent.

`OverlayCalibrationEditor` extracts the existing UI flow from the desktop session for tests; it uses the same geometry model, file format, and settings service. Successful Save validates and persists the live geometry, updates the saved calibration, then shares Cancel's exit path: hide the card surface, clear calibration state, close the flyout, apply passive native policy (including removal of macOS calibration chrome), restore saved geometry, and refresh badge visibility. A short transition guard prevents property notifications from showing a partially transitioned window. Native policy failure leaves cards hidden with a diagnostic, while guides and the flyout are already closed. Invalid geometry or failed persistence remains editable with an explanation and does not overwrite the in-memory saved calibration.

The calibration rail icon enters editing directly and opens the flyout. Reopening after Save/Cancel restores saved geometry. Cancel restores the previous layout and region without writing settings; first-run Cancel returns to the uncalibrated state. Closing a flyout alone does not save/cancel editing. Display invalidation supersedes an in-flight save.

The macOS save regression was a coordinate-space mismatch: this Retina desktop reports `Screen.Scaling=1` and bounds 1728 × 1117, but `Window.RenderScaling=2`. Multiplying client geometry by render scale incorrectly doubled its normalized size, causing validation to return before exit. Capture and restore now both use `Screen.Scaling`; Windows scaled desktop coordinates retain their corresponding conversion. Restore removes subpixel floating-point normalization noise before Avalonia rounds layout sizes, avoiding a one-pixel growth after Save. No native adapter behavior was changed in Phase 7.4.

### Native input and window lifecycle

`IClickThroughWindowController` is bound only to `CardOverlayWindow`. One factory selects the supported OS adapter. `OverlayInteractionMode` explicitly pairs mouse-ignore with activation permission: passive requests `(true, false)`; calibration requests `(false, true)`. `OverlayInteractionController` exposes a testable edit/play switch and reports failure; the host hides the card window if passthrough or transparency cannot be established, preserving the interactive rail as an escape hatch.

The Windows adapter isolates all User32 calls. It enables `WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, and `WS_EX_NOACTIVATE`, preserves/restores the original affected style bits for editing, and requests a nonactivating frame refresh. This uses the documented [layered-window mouse passthrough behavior](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features); Windows has not been physically validated in this environment.

Mouse pass-through and application activation are separate concerns. The passive badge window remains transparent, topmost, and `ShowActivated=false`; its visual-only badge subtree cannot receive input or focus and contains no tooltips/popups. The rail is a normal interactive window and can activate DraftTG.

The macOS adapter retains Avalonia's NSWindow until session teardown and installs a compatible subclass on that instance only. It sets and verifies AppKit [`ignoresMouseEvents`](https://developer.apple.com/documentation/appkit/nswindow/ignoresmouseevents) and vetoes [`canBecomeKeyWindow`](https://developer.apple.com/documentation/appkit/nswindow/canbecomekey) / [`canBecomeMainWindow`](https://developer.apple.com/documentation/appkit/nswindow/canbecomemain) in passive mode. Calibration delegates focus eligibility to Avalonia's original implementations. The subclass stays installed across mode changes to preserve AppKit KVO observers; callbacks are rooted for the Objective-C class lifetime. Disposal clears the per-window policy and releases the retained handle before closing the hidden window. It does not convert an NSWindow into an incompatible NSPanel or swizzle the class shared by the rail. No global focus restoration is attempted.

**macOS resize limitation:** the installed Avalonia 12.1.3 [`BeginResizeDrag` implementation is empty](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Native/WindowImplBase.cs). Its [native appearance code](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/native/Avalonia.Native/src/OSX/WindowImpl.mm) also strips the resizable style for `WindowDecorations.None`. Calibration temporarily uses `BorderOnly`, exposing AppKit's native resizable frame, and restores `None` on exit. All eight custom targets still call the required Avalonia API; their interior press cannot initiate resizing on this backend. Use the outer native frame on macOS. Native style/geometry checks passed; physical drag acceptance remains pending. Fully functional interior handle dragging requires an upstream/backend fix; manual pointer-delta resizing is deliberately absent.

All Objective-C calls remain in the platform adapter. Native handle access follows [Avalonia's interop boundary](https://docs.avaloniaui.net/docs/app-development/native-interop). No Accessibility permission, screen recording, global input hooks, OCR, screenshots, or Arena window enumeration is required or implemented.

`OverlayDesktopSession` is the single desktop lifetime owner. Closing the rail or card overlay cancels the runtime and pending calibration work, awaits cleanup, closes the flyout and both windows, and exits through Avalonia's main-window lifetime. Badge hiding, history viewing, and calibration never start/stop a second runtime. The card window does not activate during normal play; editing explicitly allows interaction. Native ownership and retain/release remain in the adapter.

From the repository root:

```text
dotnet run --project src/DraftTG.App
```

Detailed Logs must be enabled in Arena. Automatic Arena-window following, persisted rail placement, card images, application packaging/signing, and Phase 8 scoring remain deferred.

## Concurrency Ownership

- Avalonia presentation state and native window operations belong to the UI thread. The rail and badge window share one session; calibration file operations are asynchronous and canceled/awaited at shutdown.
- `DraftSessionCoordinator` performs ordered asynchronous orchestration on its caller's context without assuming or dispatching to the UI thread. Only one run may mutate its state engine at a time.
- Arena log monitoring runs asynchronously on a thread-pool producer and publishes raw events through an async stream.
- Domain values are immutable and contain no concurrency or platform concerns.
- Reconstructed Arena draft-session mutation is owned by one `ArenaDraftStateEngine`; its application owner must serialize calls.
- `LimitedStatisticsCoordinator` owns context changes, in-flight cancellation, and a bounded latest-update stream. Its separate worker performs statistics I/O; Arena consumption never awaits that worker.
- Recommendation calculations will run away from the UI thread and publish completed immutable results back to the application layer.

## Testing

Domain tests import only Domain. Data tests use small synthetic Scryfall-shaped JSON, in-memory gzip JSONL, temporary cache roots, and fake HTTP handlers; they never contact Scryfall. They cover mapping and ambiguity invariants, streaming import, OS paths, refresh decisions, offline fallback, atomic replacement safety, and cancellation cleanup. Arena Integration tests use isolated temporary files, small synthetic log records, and direct typed-event state transitions; they never access the user's Arena installation. State tests cover lifecycle, inferred identity, structural replay idempotence, conflicts, ordering, completion, and snapshot immutability. Application tests cover card-data bootstrap, resolved/missing/ambiguous identifier outcomes, all-or-nothing Domain snapshot conversion, and coordinator ordering, diagnostics, cancellation, restart, source failure, and single-run behavior without Avalonia. App tests exercise the ViewModel as a plain C# object with deterministic fakes, covering presentation states, pack/history fidelity, ambiguity diagnostics, failures, and lifecycle cancellation without Avalonia Headless, real files, or network access. Windows and macOS path construction is tested independently of the host OS. File monitoring has been exercised on macOS in this environment; physical Windows and real Arena log validation remain required before release.


## Phase 7 — Limited Statistical Card Data

Scryfall remains the source of card identity and printing metadata. 17Lands supplies Limited performance statistics only. The Recommendation Engine never depends on Data, Arena, provider strings, JSON, HTTP, or cache paths.

```text
Arena draft context
        ↓
Application expansion + format resolution
        ↓
Data: 17Lands whole-environment cache/client
        ↓
validated name-keyed provider rating values
        ↓
Application exact-name / draft-participant mapping
        ↓
LimitedCardStatisticsCatalog
        ↓
Application presentation → Overlay / future Recommendation Engine
```

### Statistical values and provider boundary

`LimitedCardStatistics` is an immutable record keyed by Domain `CardIdentifier`. Nullable metrics preserve absent data: sample/game count, play rate, Game-in-Hand win rate, Opening-Hand win rate, drawn win rate, drawn improvement, average last seen (ALSA), and average taken (ATA). Rates remain fractions (0.5874 internally displays as 58.7%). Drawn improvement is a signed rate difference and may be negative. `LimitedCardStatisticsCatalog` copies and freezes identity lookups, rejecting duplicate keys. `LimitedStatisticsContext` records expansion and the provider-neutral `PremierDraft`, `TraditionalDraft`, or `QuickDraft` format. No ranking API exists.

`SeventeenLandsCardRatingsClient` isolates the community-accessible, undocumented `https://www.17lands.com/card_ratings/data` endpoint. It uses `HttpClient` and `System.Text.Json` with `expansion` and `format` query parameters, `Accept: application/json`, and `User-Agent: DraftTG/0.7 (Limited statistics; cached for 24 hours)`. It requests the complete environment once, never individual cards. Its internal `SeventeenLandsCardRatingDto` decodes only `name`, `game_count`, `play_rate`, `win_rate`, `opening_hand_win_rate`, `drawn_win_rate`, `drawn_improvement_win_rate`, `avg_seen`, and `avg_pick`; unknown properties are ignored. Only the exact name is required. The wire DTO never leaves Data: Application receives immutable, validated `SeventeenLandsRating` boundary values with names rather than invented Domain IDs.

Provider `win_rate` maps directly to GIH WR for this phase. Probability values must be finite within [0, 1], signed improvement within [-1, 1], counts nonnegative, and ALSA/ATA finite and positive. Null metrics remain null. Invalid rows, duplicate names, malformed arrays/JSON, and non-JSON bodies reject the response without replacing a usable cache. The body is validated as JSON even when the server incorrectly labels it `text/html`; real HTML is rejected by JSON decoding. HTTP errors and timeouts are nonfatal. Cancellation propagates normally. There are no automatic retries or polling: 429 establishes a client-wide cooldown using either form of `Retry-After` (one hour if absent, at least one minute if expired). Other failures have a one-hour per-environment in-memory cooldown; forced refresh can bypass that cooldown but never 429. The runtime uses a 30-second HTTP timeout.

### Cache and offline behavior

Cache roots:

- Windows: `%LOCALAPPDATA%\DraftTG\limited-data\17lands\`
- macOS: `~/Library/Application Support/DraftTG/limited-data/17lands/`

Filenames are deterministic, such as `HOB_QuickDraft.json`. Expansion components are restricted to 2–8 ASCII letters/digits and normalized uppercase; formats are validated enum values. Each JSON document includes expansion, required requested/source formats, fetch timestamp, and provider rows. All metadata and rows are validated on read, including rejection of future timestamps. Successful empty datasets are also cached. Writes stage a unique local temporary file and atomically replace the destination after serialization. Failed refreshes never overwrite good data.

The default TTL is **24 hours**. A fresh disk or memory cache avoids HTTP entirely. An expired but valid cache remains available offline when refresh fails, marked `StaleCache` with its original timestamp and a diagnostic. With no usable data, statistics are unavailable while Arena tracking, card names, and history continue. Concurrent client loads are serialized and recheck cache, preventing duplicate requests in one runtime. This is a single-application cache, not a cross-process locking system. Failure/429 cooldowns are in memory and reset when the app restarts.

### Expansion, format, and matching

Recognized event names have the exact form `QuickDraft_SET`, `PremierDraft_SET`, `TraditionalDraft_SET`, or `TradDraft_SET`, optionally followed by `_YYYYMMDD` (eight digits). SET is 2–8 uppercase ASCII letters/digits. For example, `QuickDraft_HOB_20260915` resolves to HOB. If the event name is unrecognized, fallback requires a nonempty current pack whose **every** identity resolves and whose set codes unanimously agree after uppercase normalization. Mixed-set or incomplete evidence cannot infer an expansion. A known environment is retained across the temporary gap between packs within the same session. Unknown expansion or unsupported mode causes a nonfatal unavailable state.

Quick maps to `QuickDraft`; Premier to `PremierDraft`; Traditional to provider `TradDraft`. Exact format is always requested first. Only a successful empty exact dataset permits one fallback to Premier; transport or validation failure does not imply that the format lacks data. Premier never falls back further. `LimitedStatisticsLoadResult` preserves requested context, actual source context, catalog, Live/Cache/StaleCache/Unavailable status, timestamp, diagnostic, and an explicit `IsFallback` flag. Format fallback and stale-cache status can coexist without losing either fact.

Phase 7.1 live mapping starts from the exact identities already resolved by Arena/Scryfall:

```text
DraftSnapshot current-pack and history CardIdentifier
        ↓
CardCatalog.Find(identifier): exact Domain Card
        ↓
exact, case-sensitive Card.Name
        ↓
17Lands row for the selected expansion and format
        ↓
statistics attached to the original CardIdentifier
```

Scryfall supplies exact card identity; 17Lands supplies name/environment Limited statistics. The active card is never re-resolved by globally searching for printings of its name. The same provider evidence can attach to multiple known Domain identifiers, including supplemental/bonus-sheet printings with another set code. Domain records and identifiers remain unchanged. Pack/history identifiers are deduplicated only for catalog construction; the overlay retains pack order and duplicate card copies. Mapping is repeated locally against new snapshots without HTTP.

Live mapping visits only current-pack and history cards. Unrelated global printing ambiguity produces neither entries nor overlay diagnostics. Missing exact-name rows or conflicting provider evidence leave the affected card without statistics. Equivalent duplicate boundary rows can be reconciled; conflicting rows for one exact name are never selected arbitrarily. The Data HTTP decoder continues rejecting malformed provider duplication before caching. The overlay uses dashes and, when needed, a bounded summary such as `1 current-pack card has no statistics.` History-only issues do not consume overlay space. Counts are of distinct current-pack identities, so repeated copies do not inflate the warning. No comma-separated name audits enter presentation.

A direct mapper call without a snapshot retains conservative standalone mapping (prefer an active-set candidate, otherwise require a unique printing), with a count-only ambiguity diagnostic. This standalone path is separate from live orchestration: the live service returns an empty mapped catalog while awaiting a snapshot, never triggering a global audit during a gap between packs.

### Async overlay and lifecycle

The separate `LimitedStatisticsCoordinator` reacts to draft updates; the existing sequential `DraftSessionCoordinator` contains no ratings I/O. Pack rendering precedes statistics observation. A background worker loads each environment once during a session, while pack/pick changes reuse loaded data and perform local lookups. New session/context generations cancel pending work and disregard late completions deterministically. UI-ready updates carry their snapshot identity so queued results cannot overwrite a different pack. The bounded stream keeps only the latest presentation update. Shutdown cancels workers, waits for completion, and disposes the owned HTTP client.

Each row retains name, rarity, colors, and Arena ordering/duplicates. Secondary text shows GIH to one decimal, ALSA to two decimals, and `n=4,820` sample count when known. Counts below 500 display `Low sample`; this is presentation only and changes no raw values. Loading uses `…`; unavailable/missing values use `—`. The source label identifies the actual format, including `Stats: Premier Draft (fallback)` and stale-cache status. Diagnostics remain separate from Arena errors. No scores, grades, tiers, sorting by win rate, strongest-card highlighting, or pick recommendations are produced.

### Attribution, stability, and future work

Limited statistics are sourced from [17Lands](https://www.17lands.com/). Its [official public datasets](https://www.17lands.com/public_datasets) are published under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) unless otherwise noted; this attribution does not assume a stability guarantee for the community-accessible ratings endpoint. Phase 7 does not download or recompute those large datasets. A future public-dataset adapter can produce the same provider-neutral catalog without changing Domain identity or scoring inputs.

Endpoint stability, exact-format availability, sparse samples, and provider/Scryfall name differences remain limitations. Name normalization or fuzzy matching is deliberately absent. Phase 8 must design confidence-aware scoring explicitly; Phase 7 does not infer recommendations from GIH alone.

### Phase 7 validation

Automated ratings tests use fake HTTP handlers, temporary cache roots, and injected clocks; no automated test calls 17Lands. Coverage includes nullable metrics and units, immutable catalogs, query/header correctness, malformed responses, offline/stale cache, empty-cache reuse, TTL, 429, failure cooldowns, cancellation, printing ambiguity, supplemental cards, format fallback, sanitized HOB pipeline order, asynchronous arrival, old-environment suppression, shutdown, and UI formatting.

On 2026-09-26, local cached Scryfall data contained 118,404 cards and local Player.log replay produced an active, resolved HOB Quick Draft pack with 11 cards. The live HOB QuickDraft endpoint returned HTTP 200 with `text/html; charset=utf-8`. The initial client rejected that header before reading the body, so the live payload itself was not validated. The final client instead validates the body as JSON regardless of Content-Type, with a fake-response regression test; it has not been rechecked live. No valid rows or actual statistical source were available, and no Premier fallback was attempted because no successful empty dataset was established. The initial validation harness inadvertently made three requests while checking repeated loads; live requests were stopped and a failure-cooldown regression test was added. No provider values or live name-to-statistics matches can be claimed from that check. The sanitized HOB fixture verifies matching separately. MTG Arena was not running during this validation, so a physical in-game overlay check remains outstanding. An offline desktop launch created the overlay window, but macOS screen capture could not capture it; visual layout was not verified. The final solution build has zero warnings/errors and all 341 tests pass (including the previous 259); see PHASE7_REPORT.md for the per-project breakdown and file inventory.


### Phase 7.1 live regression

The user subsequently physically validated HOB Quick Draft statistics on macOS at P1P4, including Stony-Voiced Goblins, Old Thrush, Long Lake Nuisance, and Mirkwood Nurturer. Quick Draft source, GIH, ALSA, and sample counts worked; the excessive global printing-ambiguity banner motivated Phase 7.1. This supersedes the earlier Phase 7 live-validation limitation above.

Phase 7.1 fixtures use synthetic provider values for those names and multiple global printings, including a supplemental active printing and unrelated ambiguous cards. They verify safe active mappings, exact Quick Draft source, retained pack order, missing/conflicting evidence, history mapping, and concise presentation without network calls. No Arena parser, scoring, ranking, or overlay layout changes are part of this cleanup.


### Phase 7.2 verification

Layout, calibration persistence/rejection, atomic badge replacement, 14/12/1-card packs, between-pick clearing, in-place statistics changes, missing/low-sample values, flyout selection, rail warnings, explicit drag initiation, and fake native-controller mode transitions are covered without native-window automation. All previous 350 tests are retained. Physical Arena interaction and Windows testing are reported separately in PHASE7_2_REPORT.md; unit tests do not establish mouse passthrough to a real game window.
