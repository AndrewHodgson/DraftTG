# DraftTG Architecture

DraftTG is a shared Windows and macOS desktop application written in C# 14, targeting .NET 10 LTS and Avalonia 12. The solution keeps reusable product behavior independent of the desktop shell and uses operating-system-specific code only for genuine platform differences.

Supported deployment targets are Windows 10 22H2 or newer, Windows 11, and macOS 14 or newer.

The phase sections below retain their original validation history. Phase 9B.7 was subsequently confirmed physically complete by the user; the current recommendation pipeline and Phase 9C validation status are described in the final section.

## Projects

- **DraftTG.Domain** — Immutable, provider-neutral card, catalog, color, draft-position, pick, pack, history, and snapshot values. It has no Avalonia, JSON, filesystem, networking, persistence, Scryfall, Arena, or operating-system dependency.
- **DraftTG.Data** — Scryfall identity decoding and bulk caching, isolated 17Lands overall/pair ratings HTTP/JSON and caching, and validated embedded descriptive archetype profiles. It depends only on Domain and .NET platform libraries.
- **DraftTG.ArenaIntegration** — Arena log-location resolution, asynchronous raw-file monitoring, Arena-specific identifiers, stateless draft-record parsing, and reconstructed Arena draft-session state. Its completed behavior does not currently require a Domain project reference.
- **DraftTG.Application** — UI-free cross-module orchestration. It resolves Arena card identifiers through imported Data metadata, adapts supported Arena state snapshots into provider-neutral Domain snapshots, and coordinates the sequential runtime pipeline. It depends on Domain, Data, Arena Integration, and Recommendation Engine.
- **DraftTG.RecommendationEngine** — Immutable provider-neutral Limited statistics, Phase 8 statistical recommendations, Phase 9A pool-color context, Phase 9B lane inference, Phase 9C empirical archetype affinity, Phase 9D successful-deck evidence, and observed-pack history. It depends only on Domain; it contains no provider DTOs, UI, networking, filesystem or OS access.
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

BotDraft pack and pick coordinates remain zero-based on the wire and are normalized to one-based `ArenaDraftCoordinate` values. For a supported single-card Quick Draft (`NumCardsToPick == 1`), the parser emits `PickedCardsObserved` before `PackPresented`. `PickedCards` is an unordered multiset, including duplicate copies; array positions provide no chronology. Its raw immutable list remains available on the event for diagnostics, while semantic pool equality ignores ordering. A completion status may also emit a pool snapshot before `DraftCompleted`. Multi-card status does not enable this single-card recovery path.

The state engine owns snapshot comparison. Only same-session snapshots advancing exactly one normal coordinate and adding exactly one occurrence infer a canonical pick at the **previous** coordinate. `ArenaQuickDraftCoordinates` handles P1P14 → P2P1 and P2P14 → P3P1. Inferred and explicit submissions share the same exact replay/conflict path. A first mid-draft snapshot or a multi-pick gap preserves pool membership without inventing historical positions; consecutive inference can resume after a gap. Incoherent counts, removed occurrences, backward coordinates, or contradiction with known selections produce a focused `PickedCardsDiagnostic` and retain the prior pool/history. Existing source reset and new-session identity rules clear the baseline. Reordering and repeated snapshots do not accumulate cards or picks.

`ArenaDraftStateSnapshot.RecoveredPool` retains the last accepted snapshot baseline. `DraftedPool` combines its occurrences with exact picks not yet covered by that snapshot, so an explicit pick followed by its confirming status is counted once, including duplicate copies. Exact coordinate-qualified history remains solely in `CompletedPicks`. Application resolves the pool into provider-neutral `DraftedCardPool`, separately from `DraftHistory`; `DraftSnapshot.DraftedPool` falls back to exact history for sources without recovered pools. Phase 9A consumes pool membership/multiplicity with the existing formula, and Phase 9C receives the same resulting color evidence. Phase 9B pack observations still use only exact history. UI marks recovered-only occurrences as `Position unknown` and reports exact-history coverage. Missing coordinates cannot supply historical pack-observation selections and are never synthesized to fill that gap.

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

The badge window has no opaque root panel, normal chrome, or taskbar entry. Normal mode draws only small rounded, translucent dark badges with GIH WR, ALSA, and a subtle low-sample asterisk. Names and numbered slot guides are shown only during calibration. Missing values use `—`, loading uses `…`. Pack order and duplicate copies are preserved; between-pick and unsupported/unavailable pack states remove all badges. Statistics visibility does not stop monitoring. Phase 8 adds occurrence ranks alongside raw GIH and a restrained gold border/rank color for the Stats Pick, without changing the badge dimensions or native interaction policy.

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

Detailed Logs must be enabled in Arena. Automatic Arena-window following, persisted rail placement, card images, application packaging/signing, and Phase 9C/9D archetype/trophy analysis remain deferred.

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
Recommendation Engine → Application update → Overlay
```

### Statistical values and provider boundary

`LimitedCardStatistics` is an immutable record keyed by Domain `CardIdentifier`. Nullable metrics preserve absent data: sample/game count, play rate, Game-in-Hand win rate, Opening-Hand win rate, drawn win rate, drawn improvement, average last seen (ALSA), and average taken (ATA). Rates remain fractions (0.5874 internally displays as 58.7%). Drawn improvement is a signed rate difference and may be negative. `LimitedCardStatisticsCatalog` copies and freezes identity lookups, rejecting duplicate keys. `LimitedStatisticsContext` records expansion and the provider-neutral `PremierDraft`, `TraditionalDraft`, or `QuickDraft` format. Phase 8 adds the pure statistical ranking API described below.

`SeventeenLandsCardRatingsClient` uses the current `https://www.17lands.com/api/card_data` endpoint. The legacy `/card_ratings/data` endpoint is no longer a production source; `/card_data` is a UI page, not the data API. Requests use `HttpClient` and `System.Text.Json` with `expansion`, `event_type`, and `time_period=ALL_TIME`, `Accept: application/json`, and `User-Agent: DraftTG/0.8.1 (Limited statistics; whole-environment data cached for 24 hours)`. It requests the whole environment once, never individual cards. The inspected response is an object with `copyright`, `notes`, and a required `data` array; `SeventeenLandsApiCardDataDto` models this envelope inside Data. Its internal `SeventeenLandsApiCardDataRowDto` decodes `name`, `game_count`, `play_rate`, `win_rate`, `ever_drawn_win_rate`, `ever_drawn_game_count`, `opening_hand_win_rate`, `opening_hand_game_count`, `drawn_win_rate`, `drawn_game_count`, `drawn_improvement_win_rate`, `avg_seen`, and `avg_pick` independently; unknown properties are ignored. Only the exact name is required. The wire DTO never leaves Data: Application receives immutable, validated `SeventeenLandsRating` boundary values with names rather than invented Domain IDs.

Phase 7.5 corrects GIH WR to use `ever_drawn_win_rate` and its explicit `GameInHandGameCount` sample to use `ever_drawn_game_count`. Overall `win_rate`/`game_count` remain separately parsed and retained inside Data and its cache; they never substitute for missing GIH. Phase 8.1 cache schema 3 additionally rejects all legacy-endpoint data, including schema 2. Expansion, exact event type, ALL_TIME period, source endpoint, and schema/source version are part of cache identity. Incompatible caches are never used even after a failed refresh. Probability values must be finite within [0, 1], signed improvement within [-1, 1], counts nonnegative, and ALSA/ATA finite and positive. Null metrics remain null. Invalid rows, duplicate names, malformed arrays/JSON, and non-JSON bodies reject the response without replacing a usable cache. The body is validated as JSON even when the server incorrectly labels it `text/html`; real HTML is rejected by JSON decoding. HTTP errors and timeouts are nonfatal. Cancellation propagates normally. There are no automatic retries or polling: 429 establishes a client-wide cooldown using either form of `Retry-After` (one hour if absent, at least one minute if expired). Other failures have a one-hour per-environment in-memory cooldown; forced refresh can bypass that cooldown but never 429. The runtime uses a 30-second HTTP timeout.

### Cache and offline behavior

Cache roots:

- Windows: `%LOCALAPPDATA%\DraftTG\limited-data\17lands\`
- macOS: `~/Library/Application Support/DraftTG/limited-data/17lands/`

Filenames are deterministic, such as `HOB_QuickDraft_ALL_TIME_v3.json`; old legacy filenames are not read and require no manual deletion. Expansion components are restricted to 2–8 ASCII letters/digits and normalized uppercase; formats are validated enum values. Each JSON document includes required schema version, source endpoint, time period, expansion, requested/source formats, fetch timestamp, and the current API envelope. All metadata and rows are validated on read, including rejection of future timestamps. Empty or suspiciously small/no-GIH datasets are rejected, never cached, and cannot overwrite a usable cache. Writes stage a unique local temporary file and atomically replace the destination after serialization. Failed refreshes never overwrite good data.

The default TTL is **24 hours**. A fresh disk or memory cache avoids HTTP entirely. An expired but valid cache remains available offline when refresh fails, marked `StaleCache` with its original timestamp and a diagnostic. With no usable data, statistics are unavailable while Arena tracking, card names, and history continue. Concurrent client loads are serialized and recheck cache, preventing duplicate requests in one runtime. This is a single-application cache, not a cross-process locking system. Failure/429 cooldowns are in memory and reset when the app restarts.

### Expansion, format, and matching

Recognized event names have the exact form `QuickDraft_SET`, `PremierDraft_SET`, `TraditionalDraft_SET`, or `TradDraft_SET`, optionally followed by `_YYYYMMDD` (eight digits). SET is 2–8 uppercase ASCII letters/digits. For example, `QuickDraft_HOB_20260915` resolves to HOB. If the event name is unrecognized, fallback requires a nonempty current pack whose **every** identity resolves and whose set codes unanimously agree after uppercase normalization. Mixed-set or incomplete evidence cannot infer an expansion. A known environment is retained across the temporary gap between packs within the same session. Unknown expansion or unsupported mode causes a nonfatal unavailable state.

Quick maps to `event_type=QuickDraft`; Premier to `event_type=PremierDraft`; Traditional to `event_type=TradDraft`. The exact requested event type is the only format fetched. Phase 8.1 removes the old empty-dataset Premier fallback; no sparse or empty Quick/Traditional response causes a different format request. `LimitedStatisticsLoadResult` preserves requested/actual context, catalog, source status, timestamp, diagnostic, and dataset diagnostic text. The generic `IsFallback` presentation field remains for compatibility but production no longer creates format-fallback results. Stale cache is allowed only for the same endpoint, expansion, event type, period and schema.

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

Each row retains name, rarity, colors, and Arena ordering/duplicates. Secondary text shows GIH to one decimal, ALSA to two decimals, and `n=4,820` sample count when known. GIH sample counts below 500 display `Low sample`; this is presentation only and changes no raw values. Loading uses `…`; unavailable/missing values use `—`. The source label identifies the exact requested format and stale-cache status; production no longer uses an automatic format substitute. The control rail status panel also reports current-pack GIH availability, low GIH samples, unavailable GIH, ALSA availability, and source format/expansion. Counts include duplicate pack slots and exclude drafted history. Low-sample counts can overlap unavailable GIH when a known sample is below 500; an unknown sample never implies low sample. Coverage is cleared while loading or between packs. Diagnostics remain separate from Arena errors. Phase 7 presentation preserves raw statistics; Phase 8 separately adds adjusted-value occurrence ranks and a Stats Pick. Pack order remains unchanged; no grades or tiers are assigned.

### Attribution, stability, and future work

Limited statistics are sourced from [17Lands](https://www.17lands.com/). Its [official public datasets](https://www.17lands.com/public_datasets) are published under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) unless otherwise noted; this attribution does not assume a stability guarantee for the community-accessible ratings endpoint. Phase 7 does not download or recompute those large datasets. A future public-dataset adapter can produce the same provider-neutral catalog without changing Domain identity or scoring inputs.

Endpoint stability, exact-format availability, sparse samples, and provider/Scryfall name differences remain limitations. Name normalization or fuzzy matching is deliberately absent. Phase 8 uses the explicit sample-shrinkage model below. Phase 7 supplies raw statistics; Phase 9A adds the separate pool-color layer documented below.

### Phase 7 validation

Automated ratings tests use fake HTTP handlers, temporary cache roots, and injected clocks; no automated test calls 17Lands. Coverage includes nullable metrics and units, immutable catalogs, query/header correctness, malformed responses, offline/stale cache, empty-cache reuse, TTL, 429, failure cooldowns, cancellation, printing ambiguity, supplemental cards, format fallback, sanitized HOB pipeline order, asynchronous arrival, old-environment suppression, shutdown, and UI formatting.

On 2026-09-26, local cached Scryfall data contained 118,404 cards and local Player.log replay produced an active, resolved HOB Quick Draft pack with 11 cards. The live HOB QuickDraft endpoint returned HTTP 200 with `text/html; charset=utf-8`. The initial client rejected that header before reading the body, so the live payload itself was not validated. The final client instead validates the body as JSON regardless of Content-Type, with a fake-response regression test; it has not been rechecked live. No valid rows or actual statistical source were available, and no Premier fallback was attempted because no successful empty dataset was established. The initial validation harness inadvertently made three requests while checking repeated loads; live requests were stopped and a failure-cooldown regression test was added. No provider values or live name-to-statistics matches can be claimed from that check. The sanitized HOB fixture verifies matching separately. MTG Arena was not running during this validation, so a physical in-game overlay check remains outstanding. An offline desktop launch created the overlay window, but macOS screen capture could not capture it; visual layout was not verified. The final solution build has zero warnings/errors and all 341 tests pass (including the previous 259); see PHASE7_REPORT.md for the per-project breakdown and file inventory.


### Phase 7.1 live regression

The user subsequently physically validated HOB Quick Draft statistics on macOS at P1P4, including Stony-Voiced Goblins, Old Thrush, Long Lake Nuisance, and Mirkwood Nurturer. Quick Draft source, GIH, ALSA, and sample counts worked; the excessive global printing-ambiguity banner motivated Phase 7.1. This supersedes the earlier Phase 7 live-validation limitation above.

Phase 7.1 fixtures use synthetic provider values for those names and multiple global printings, including a supplemental active printing and unrelated ambiguous cards. They verify safe active mappings, exact Quick Draft source, retained pack order, missing/conflicting evidence, history mapping, and concise presentation without network calls. No Arena parser, scoring, ranking, or overlay layout changes are part of this cleanup.


### Phase 7.2 verification

Layout, calibration persistence/rejection, atomic badge replacement, 14/12/1-card packs, between-pick clearing, in-place statistics changes, missing/low-sample values, flyout selection, rail warnings, explicit drag initiation, and fake native-controller mode transitions are covered without native-window automation. All previous 350 tests are retained. Physical Arena interaction and Windows testing are reported separately in PHASE7_2_REPORT.md; unit tests do not establish mouse passthrough to a real game window.


## Phase 8 - Basic Statistical Recommendation Engine

`StatisticalRecommendationEngine` depends only on Domain and provider-neutral statistics. It performs no I/O and has no UI, provider, Arena, filesystem, networking or operating-system dependencies. Its input is `DraftPack`, not `DraftSnapshot` or drafted history. It never selects or clicks an Arena card.

### Exact model

For the active environment `LimitedCardStatisticsCatalog`, valid entries have finite GIH WR `p_i` in [0, 1] and a positive GIH sample `n_i`:

```text
b = sum(p_i * n_i) / sum(n_i)
s = (n * p + k * b) / (n + k)
data weight = n / (n + k)
k = PriorEquivalentGames = 500
```

The baseline is a GIH-sample-weighted environment mean, not 50% or an unweighted mean. `StatisticalRecommendationConfiguration` exposes `PriorEquivalentGames` and `ModelVersion` (`gih-shrinkage-v1`). The prior must be a positive integer. 500 is a transparent Phase 8 product/model configuration choice, not a claim of statistical optimality. Counts are converted to double before addition/multiplication where needed; nonfinite/out-of-range rates and nonpositive/unknown samples are excluded. With int-bounded counts and collection sizes, the weighted sums cannot overflow double.

Only raw GIH and GIH sample contribute to the score. ALSA, ATA, play rate, OH WR, drawn WR, drawn improvement, colors, rarity, names, drafted history, curve, synergy and signals are not strength inputs. Raw GIH remains the badge percentage; adjusted values are internal ranking values. The low-sample asterisk independently retains the existing `n < 500` threshold.

### Environment preparation and occurrence identity

Application prepares a separate immutable environment statistics catalog once for each loaded response, before pack mapping. It retains one statistical entry per nonconflicting exact source name known to the Domain catalog. To avoid double weighting different printings, it chooses a deterministic representative identity (prefer actual source expansion, then ordinal identifier) solely for baseline aggregation. This representative does not resolve pack identity; Arena/Scryfall-resolved current-pack identifiers remain authoritative. Unknown source names and conflicting source evidence are excluded rather than assigned invented identities. This is a mapping-coverage limitation of the baseline. Pack copies and drafted history cannot alter the environment baseline.

The live `LimitedStatisticsLoadResult` carries both participant statistics and `EnvironmentCatalog`. A missing environment defaults to an empty catalog and cannot silently use participant/history statistics for a baseline. The pure engine can also accept one full active catalog directly.

`CardRecommendation` records original `PackIndex`, `CardIdentifier`, raw GIH/sample, nullable adjusted value and data weight, nullable statistical rank, top-candidate flag and scoring availability. `DraftRecommendation` retains an immutable list in Arena order, availability, top pack index, scored/total counts and coverage fraction, environment baseline/card count, and configuration metadata. Duplicate identifiers remain separate occurrences. Ranking uses adjusted value descending, then sample descending, then original index ascending; names play no role.

Without a valid baseline: `NoEnvironmentBaseline`, no scores/ranks/pick. With fewer than two scored occurrences: `InsufficientComparableStatistics`, no comparative ranks or top pick; a sole valid card may retain its genuine adjusted value. With two or more: `ReadyCompleteCoverage` if all pack occurrences are scored, otherwise `ReadyPartialCoverage`. Unscored occurrences have null scores and ranks and are never assigned a fake bottom score/rank.

### Async integration and presentation

The existing flow now continues from Limited statistics into RecommendationEngine and an Application update. `LimitedStatisticsCoordinator.Observe` queues background mapping/scoring, including cached environments; it does not perform recommendation calculation on the UI/Arena-monitoring thread. Workers capture session generation and exact snapshot and drop results after pack or context changes. The ViewModel also rejects mismatched snapshots. New packs immediately clear previous ranks; loading statistics show no recommendation. Statistics responses produce new recommendation values; panel/visibility/calibration changes do not rerun the algorithm. Between packs and unsupported/completed states clear the recommendation UI.

Badges remain 42 pixels high with the existing width/placement/calibration mapping and click-through behavior. A compact rank sits beside raw GIH, with ALSA on the second line. The #1 Stats Pick uses a restrained gold border and rank color. The rail status panel reports Stats Pick/name, `Rank model: GIH + sample shrinkage`, scored/total coverage, baseline, and explicit partial or unavailable status. No label claims a contextual Best Pick. Numeric adjusted values/data weights remain available through the immutable model for diagnostics, without expanding the normal overlay.

Phase 8 continues to rank cards by baseline statistical performance only. Phase 9A composes a separate pool-color adjustment and Phase 9B composes lane inference with these unchanged results. Archetypes, curve, synergy, text analysis, deck construction, automation and OCR remain deferred.

Validation on Windows: solution build has zero warnings/errors and all tests pass (see PHASE8_REPORT.md). Live Arena badge alignment/focus/click-through has not been physically retested for Phase 8; no Arena/DraftTG process was running. macOS physical testing was unavailable. Existing Windows/macOS calibration and injected native-policy tests remain passing, and the new engine adds no platform-specific code.


## Phase 8.1 - Current card-data API migration

Production request:

```text
https://www.17lands.com/api/card_data?expansion=WOE&event_type=QuickDraft&time_period=ALL_TIME
```

The dedicated Data envelope requires `data` to be an array; bare legacy arrays, missing/null/incorrect data shapes and HTML bodies fail decoding. The live endpoint labels valid JSON `text/html`, so the body is validated independently of Content-Type. Provider-specific names and DTOs remain in Data. GIH still uses only `ever_drawn_win_rate` / `ever_drawn_game_count`; generic overall rate/count stay separate in the DTO/cache. ALSA, ATA and other displayed metrics retain their Phase 7.5 mappings. No generic WR, other event type or 50% fills missing GIH.

Whole-environment sanity validation uses a named conservative floor of 20 rows (a heuristic, not a required exact card count), and requires at least one non-null valid GIH value with a positive GIH sample. Empty, fewer-than-20 and no-scorable-GIH datasets are suspicious. These checks apply both to fetched payloads and cache reads. A rejected refresh retains a compatible, valid stale cache and reports why; without one, statistics and recommendations are unavailable. Failed responses do not overwrite good cache data. Existing 24-hour TTL, one-hour failure cooldown, cancellation, concurrent request sharing and client-wide Retry-After handling remain unchanged. Forced refresh is explicit; there is no polling or per-card traffic.

The status rail extends its existing pack coverage with expansion, requested provider event type, source endpoint, ALL_TIME period, usable provider row count, and network/fresh-cache/stale-cache/unavailable origin. Diagnostics are not added to badges. A failed load has zero usable provider rows; the rejection reason states suspicious observed row counts. A stale fallback reports the row count of the retained usable dataset.

The Phase 8 engine and badge UI are byte-for-byte unchanged. Scoring remains `s = (n*p + 500*b)/(n+500)` with the sample-weighted environment baseline. The existing background pipeline recomputes ranks when the corrected dataset arrives and updates badge values in place without geometry changes or restart.

One development-only WOE QuickDraft ALL_TIME fetch on 2026-10-03 returned 324 rows, 284 with GIH and 40 without it. Recorded-response replay through the production decoder verified every GIH/sample mapping and retained all rows, without another network request. Counts are diagnostic observations, not test expectations. Ordinary tests use synthetic current-shape envelopes and no live network. See PHASE8_1_REPORT.md for validation and limitations. Phase 9A is documented below.

## Phase 9A - Pool-aware color commitment

The unchanged `StatisticalRecommendationEngine` remains the independently reproducible baseline: `s = (n*p + 500*b)/(n+500)`. `ContextualRecommendationEngine` consumes its result, the current `DraftSnapshot`, and the Domain `CardCatalog`. It checks the ordered pack identity, retains each original `CardRecommendation`, and produces an immutable `ContextualDraftRecommendation` in the same pack order. The engine still references only Domain. Observations are never a Phase 9A scoring input.

`ColorCommitmentConfiguration` exposes `EvidenceScale = 4.0`, `FullCommitmentAfterPicks = 14`, `MaxColorAdjustment = 0.025`, and model version `pool-color-v1`. The scale must be finite and positive; the pick threshold must be positive; the maximum adjustment must be finite in [0,1]. Zero adjustment can disable the color influence. These are transparent Phase 9A product heuristics for iterative tuning, not statistically optimal estimates or predicted win rates. Their limited influence favors established colors in close decisions while allowing a much stronger off-color card to remain recommended.

For every selected occurrence in `DraftSnapshot.History`, resolve its Domain card and distribute evidence equally across its `ColorSet`: monocolor +1; a k-color card +1/k to each color; colorless +0. Duplicate selected identifiers count independently. The current pack and candidate do not contribute. Arena's existing state engine remains the single owner of coordinate deduplication; the recommendation layer does not deduplicate by card ID. Missing catalog identities contribute no invented evidence and are counted explicitly as unresolved.

```text
meanEvidence = (W + U + B + R + G) / 5
rawSupport_c = clamp((evidence_c - meanEvidence) / EvidenceScale, -1, +1)
progressFactor = clamp(completedPickCount / FullCommitmentAfterPicks, 0, 1)
effectiveSupport_c = rawSupport_c * progressFactor

ColorFit(colorless) = 0
ColorFit(monocolor) = effectiveSupport of that color
ColorFit(multicolor) = minimum effectiveSupport among all its colors
colorAdjustment = ColorFit * MaxColorAdjustment
contextualValue = clamp(Phase8AdjustedValue + colorAdjustment, 0, 1)
```

Progress counts all completed picks, including colorless picks. Default factors at 0/1/7/14/>14 picks are 0, 1/14, 0.5, 1, 1. One green pick has raw green support 0.2 but effective support only 0.2/14, giving a +0.0357 percentage-point adjustment. `ColorCommitmentProfile` exposes all five immutable evidence/raw/effective entries, mean evidence, completed/unresolved counts, progress, primary/secondary evidence colors, and commitment strength (highest effective support). Primary/secondary colors are diagnostics, with enum-order ties, and do not restrict scoring to a selected color pair. Unknown candidate colors remain explicitly null with neutral fit; valid colorless candidates have a known empty ColorSet and zero adjustment.

Unscored Phase 8 cards remain unscored, with no contextual rank or top-pick flag. A color-fit diagnostic alone cannot manufacture strength. With at least two scorable occurrences, ranking is contextual value descending, Phase 8 adjusted value descending, GIH sample descending, then original pack index ascending. One scored card retains its numeric value but gets no rank/top pick. Duplicate pack copies remain independent slots. The exact regressions are `0.580 + 0.60*0.025 = 0.595` and `0.580 - 0.40*0.025 = 0.570`.

### Application and UI

`LimitedStatisticsService` attaches the existing Domain catalog to its mapped result. `LimitedStatisticsUpdate.Recommendation` still exposes Phase 8; `ContextualRecommendation` separately composes color context. Computation remains on the existing statistics worker. Session generation, exact snapshot identity, and an observation revision reject stale calculations, including repeated no-pack states. Each new pack clears previous presentation before its statistics arrive. Loading, unsupported, completed, and no-active-pack states clear contextual ranks and picks. Picks update the next snapshot's history and therefore recompute commitment.

Badge rank and its single gold treatment follow Context Pick. Raw GIH, its low-sample marker, ALSA, badge X/Y/width, layout, calibration and native passive/focus policy are unchanged. The status rail shows Context Pick/name, that card's stats/context ranks, effective color supports, progress, signed adjustment in percentage points and scored/total coverage. A different Stats Pick has a separate concise line. The collapsed diagnostics retain the detailed Phase 8 summary, all five evidence/raw/effective values, model constants, unresolved count, each occurrence's baseline/fit/adjustment/context/ranks, and observation coverage. Context values are ranking values, never labeled GIH WR. No absolute Best Pick label is introduced.

### Observation foundation, without lane inference

`DraftCardObservation`, `DraftPackObservation`, and `DraftPackObservationHistory` are immutable engine types. The statistics coordinator owns their session-scoped history. `Observe` captures only actual available current packs, keyed by Domain coordinate plus ordered card identifiers. An exact replay updates the same entry; a different coordinate or ordered pack identity is distinct. Every ordered occurrence records identity, nullable colors, Phase 8 value, raw GIH, and GIH sample. A statistics response enriches already observed packs locally without more provider requests, even if the pack advanced while loading. It never adds missing packs. `HasStatisticsSnapshot` distinguishes a scored/unscored evaluation from one that is still awaiting statistics.

The Application adapter also exposes resolved existing completed history when there is no current pack or the draft completed. This uses the existing Arena identity resolver and authoritative completed picks; neither the Arena parser nor state engine changes. Later history completes observations by coordinate. Selected card identity is retained. A unique matching occurrence yields its index; indistinguishable duplicate copies expose matching indexes with a null selected index rather than guessing which slot Arena selected.

Coverage exposes the first observed position, whether it was P1P1, actual observed pack count, known completed pick count, observed completed pick count, and a coverage fraction (unknown/null with no completed picks). Starting with only a P2P4 snapshot creates one observation, never earlier packs from drafted history. Packs actually supplied by source replay can be recorded; unseen packs are not reconstructed from picks. Coverage represents available observation evidence, not a guarantee of physical user viewing. The history is in memory and resets with the session; it is recommendation context, not another mutable Arena truth store.

No cards passed, ALSA deviations, late-card signals, missing-pack colors, bot/human inference, archetypes, synergy, curve, fixing, splashes, playable classification, card text or deck construction affect the Phase 9A result. Phase 9B adds the separate lane stage below; Phase 9C/9D remain deferred.

Build/test and physical validation status are recorded in PHASE9A_REPORT.md. Windows automated validation passes; live Windows Arena interaction and macOS physical acceptance remain unverified.

## Phase 9B - Open color lane detection

`LaneContextualRecommendationEngine` composes a third inspectable stage: unchanged Phase 8 statistical value, unchanged Phase 9A pool value, then lane-adjusted final value. `LimitedStatisticsUpdate.Recommendation`, `ContextualRecommendation`, and `LaneRecommendation` expose each result separately, including its ranks. New immutable types are `LaneDetectionConfiguration`, `LaneColorEvidence`, `LaneCardSignal`, `LaneOpennessProfile`, `LaneCardRecommendation`, and `LaneDraftRecommendation`, plus the new engine. RecommendationEngine continues to reference only Domain. No observation input comes from a contextual score; all lane quality inputs are captured Phase 8 values.

The existing `DraftCardObservation` gains nullable ALSA and `DraftPackObservation` gains nullable environment baseline. Current-pack recording and background enrichment supply these from the existing mapped whole-environment load. There is no additional provider request, dataset, history store or Arena-state owner. Replay updates the existing coordinate/ordered-identity entry. Selected cards retain their arrival evidence; completion neither removes nor duplicates it. Partial observation coverage and session reset behavior remain in the existing coordinator.

### Lane heuristics and confidence

`LaneDetectionConfiguration` defaults:

| Constant | Default |
| --- | ---: |
| LateArrivalScale | 3.0 |
| LaneStrengthScale | 0.04 |
| LaneSignalStartPick | 4 |
| LaneSignalFullPick | 8 |
| CurrentPackEvidenceWeight | 1.00 |
| PreviousPackEvidenceWeight | 0.50 |
| TwoPacksBackEvidenceWeight | 0.25 |
| LaneEvidenceScale | 2.0 |
| FullLaneConfidenceAfterObservations | 4 |
| MaxHumanLaneAdjustment | 0.020 |
| MaxBotLaneAdjustment | 0.010 |

Scales must be finite and positive. Pick thresholds are positive and full >= start; the confidence threshold is positive. Recency weights and maximum adjustments must be finite in [0,1], with zero allowing that influence to be disabled. These are transparent tunable heuristics, not statistically optimal estimates or predicted win rates.

For each actually observed colored occurrence at a coordinate no later than the current position:

```text
latenessWeight = clamp((actualPick - ALSA) / LateArrivalScale, 0, 1)
strengthWeight = clamp((Phase8AdjustedValue - observationEnvironmentBaseline) / LaneStrengthScale, 0, 1)
positionWeight = clamp((actualPick - LaneSignalStartPick + 1)
                      / (LaneSignalFullPick - LaneSignalStartPick + 1), 0, 1)
cardOpenEvidence = latenessWeight * strengthWeight * positionWeight
weightedOpenEvidence = cardOpenEvidence * packRecencyWeight
```

The position ramp is inclusive: defaults give picks 1–3 zero, pick 4 0.2, pick 6 0.6, and pick 8+ 1. This implements the requested weak initial P1P4 signal and full P1P8 signal. Cards at/before ALSA, or at/below the environment baseline, give zero positive evidence. ALSA is availability only, never strength or an additive recommendation bonus. Missing/nonfinite/nonpositive ALSA, missing/invalid Phase 8 value or baseline, and unknown colors give no signal. Colorless cards give no lane evidence. A colored card distributes its weighted evidence equally across its k colors (1/k each). The current visible pack intentionally contributes before selection.

Recency uses pack-number distance from the current pack: current 1.0, previous 0.5, two packs back 0.25. Thus Pack 3 still includes Pack 1 at a reduced weight. Future observations are excluded, including later picks in the current pack. No negative evidence is manufactured from early cards, weak packs, missing colors or individual missing cards.

```text
meanLaneEvidence = sum(W,U,B,R,G) / 5
rawLaneSupport_c = clamp((laneEvidence_c - meanLaneEvidence) / LaneEvidenceScale, -1, +1)
observationConfidence = clamp(eligibleObservationCount / FullLaneConfidenceAfterObservations, 0, 1)
effectiveLaneSupport_c = rawLaneSupport_c * observationConfidence
```

An eligible observation is a distinct pack coordinate with at least one meaningful positive, colored, recency-weighted signal. Multiple card occurrences in one pack contribute their evidence but count as one confidence observation. Exact replay contributes neither another observation nor another card signal. Selected occurrences are retained. No usable evidence gives zero supports and zero confidence. One meaningful pack caps effective support at 0.25 by default even if it contains many strong late cards. Confidence reaches 0.5 after two such packs and 1 after four; older packs retain their count while their evidence decays. Coverage does not assume unseen packs. Starting from P2P4 uses only actually recorded packs and exposes that first position.

The regression with green evidence 2.0 and other colors zero gives mean 0.4, raw green +0.8 and raw other colors -0.2, before confidence. Negative supports mean relatively less positive evidence, not certainty that a color is closed. Pool counts have no role in this lane calculation.

### Mode, fit and final rank

Premier and Traditional are human environments with maximum ±0.020; Quick Draft is a bot environment with maximum ±0.010. Application uses the requested neutral format resolved from the existing Arena mode, with exact-format provider data. Unknown/unsupported mode uses zero lane adjustment; collected evidence can still be inspected. Quick Draft passing evidence represents bot behavior and is not human-seat inference.

```text
LaneFit(colorless) = 0
LaneFit(monocolor) = effectiveLaneSupport for its color
LaneFit(multicolor) = minimum effectiveLaneSupport among its required colors
laneAdjustment = LaneFit * modeMaximumLaneAdjustment
Phase9BContextualValue = clamp(Phase9AContextualValue + laneAdjustment, 0, 1)
```

Unknown candidate colors have neutral fit. Unscored Phase 8 cards stay unscored through both contextual stages, without manufactured baseline scores or ranks. Final ranking uses Phase 9B value descending, Phase 9A value descending, Phase 8 value descending, GIH sample descending, then original index ascending. Duplicate occurrences remain independent and the Arena pack is never reordered. Fewer than two scored cards yields no final rank or Context Pick.

### Presentation, validation and limits

Normal badge rank/gold uses the final lane-adjusted Context Pick. Raw GIH, ALSA, low-sample markers, badge geometry, calibration and native click-through policy are unchanged. The existing rail shows Stats/Pool/Context ranks, pool colors/progress, signed pool/lane adjustments, up to two positive lane signals, human/bot source, meaningful observation count/confidence, partial-history start and scored coverage. A different Stats Pick remains a separate line. Collapsed diagnostics retain all prior-stage information plus five-color lane evidence/raw/effective support, configuration and per-signal identity/coordinate/colors/ALSA/Phase 8 value/baseline/weights/evidence/decay. There are no per-card badge lane explanations or definitive open/closed claims.

Lane inference remains probabilistic: packs have random color composition and ALSA reflects population-level historical behavior. Pass-direction changes justify decay, without modeling exact seats or wheels. There are no archetypes, trophy datasets, deck-color rates, synergy, deck composition, curve, fixing, splashes, card-text analysis, auto-picking, input automation or OCR. See PHASE9B_REPORT.md for focused tests and physical/live status. Phase 9C has not begun.

## Phase 9B.1 - Current-pack statistics association integrity

**Card statistics are joined by card identity, never by provider row position, recommendation rank position, or UI slot position. Arena display order and recommendation rank order are independent.**

The live lookup still starts with the Arena-resolved active `CardIdentifier`, finds that exact Domain `Card`, then matches the provider's ordinal exact name. Punctuation, case and split-card separators are not removed or folded. Legitimate same-name printings can share aggregate statistics while retaining their active printing identities. Conflicting provider evidence remains unavailable. Environment representatives are only for baseline aggregation; they never resolve a current-pack card. Mapping now retains the actual resolved provider name in a frozen dictionary for identity diagnostics; no endpoint, DTO metrics, cache policy or scoring changes were made.

`CardOccurrenceKey(PackIndex, CardIdentifier)` identifies one occurrence inside its owning immutable pack. `LimitedStatisticsUpdate.PackIdentity` exposes the existing `DraftPack`, whose equality includes draft position and the ordered identifier list. The coordinator's existing session generation, observation revision and exact snapshot checks remain in place. The UI checks both exact snapshot reference and pack identity before applying a result. Equal slot indexes, or even an equal pack value from another snapshot, cannot authorize a stale callback. Each new pack rebuilds occurrences, including after a Quick Draft pick removes a card.

`LimitedStatisticsUpdate.Occurrences` joins raw statistics by identifier and all three recommendation stages by occurrence key. One immutable `CurrentPackCardPresentation` contains the Domain card/name, lookup/resolved names, raw GIH/sample/ALSA, Phase 8 value/rank, Phase 9A adjustment/value/rank and Phase 9B adjustment/value/rank/top flag. Identity checks validate all embedded IDs, occurrence indexes, nested stages and any known provider name. Inconsistent models expose missing metrics/ranks and a rejected diagnostic, rather than attaching foreign evidence or throwing in production.

The App creates complete rows from these occurrences in Arena order. Identity-bearing rows derive every statistics/recommendation property from their occurrence; standalone formatter initialization cannot override it. Rail pick names come from the same keyed models. Contextual rail formatters select the top candidate flag, not a list element at the rank or slot number. Rank assignment inside the engines still sorts copies and writes metadata back to the original occurrence indexes, with existing input-order validation and tie breaking.

Each badge keeps one row reference and an occurrence key. Statistics-only refreshes find rows by that key, reject foreign identity/name or inconsistent models, then replace the entire row reference. Reordering a presentation collection cannot move evidence between badges. Duplicate identifiers remain separate keys/ranks because their pack indexes differ. Pack indexes determine calibrated geometric placement only; geometry, native interaction and passive badge size are unchanged. Collapsed recommendation diagnostics show each occurrence's full identity, raw evidence and all stage values/adjustments/ranks.

See PHASE9B_1_REPORT.md for the two pre-fix reproductions, focused regressions and physical validation limits. The automated reproduction deliberately introduces presentation-order drift; the initiating event in the reported live session and Arena image order/alignment remain unverified. Phase 9C has not begun.

## Phase 9B.2 - Passive badge binding snapshot

The actual desktop path is `OverlayDesktopSession` -> its one shared `OverlayViewModel` -> `CardOverlayWindow.DataContext` -> XAML `ItemsSource="{Binding Badges}"`. The App now publishes `MainWindowViewModel.CurrentPackPresentations`, an immutable ordered collection containing the exact objects used for rail pick names. Passive badge construction and refresh consume this collection directly. They never read the separate `CurrentPackCards` row projection. That row projection remains for existing formatter callers/tests and is not bound by the passive window.

Each geometric badge wrapper exposes `Presentation`. Its compiled XAML bindings read `Presentation.DisplayedContextRank`, `Presentation.DisplayedGIH` and `Presentation.DisplayedALSA`. Rank colors and gold treatment derive from that same model. A statistics update replaces the entire presentation reference, not individual fields, and notifies `Presentation` so nested compiled bindings reconnect. Existing wrappers are reconciled by occurrence key within a pack to preserve geometry; the complete read-only collection is then published in snapshot order. New packs rebuild wrappers. No rank sort drives this collection.

The snapshot guard captures the current snapshot once and retains it throughout the association. Reentrant/queued waiting-state callbacks cannot adopt a different pack midway through notification. Existing generation/revision checks and formulas remain unchanged.

After the final bound collection and geometric placement are constructed, `BadgeBindingDiagnosticsText` records display index, pack index, identifier, name, raw GIH, ALSA, context rank/top flag and coordinates from the actual XAML items. It is available in the collapsed rail diagnostics and Trace output; layout/viewport changes recapture it.

**Log order is not yet proven to equal Arena's visible card order.** The live P1P6 log puts Scarecrow Guide first and Hatching Plans last, opposite the reported visible endpoints. `CardBadgeViewModel.Index` and `OverlayViewModel.PlaceAll` still map the pack index to a calibrated geometric slot. This can put an otherwise correctly bound model over another card image. The binding refactor does not establish or correct Arena's visual-order policy. Physical inspection was blocked by the Windows inspection runtime failing to start. See PHASE9B_2_REPORT.md; live placement acceptance remains unresolved, and Phase 9C has not begun.

## Phase 9B.3 - Explicit Arena visual placement

The user-confirmed P1P6 screen establishes that log serialization and visual order differ. The observed Quick Draft payload contains `DraftPack`, pack/pick numbers, selected cards and empty style arrays, but no visual-slot field. The parser preserves the serialized identifiers; it does not infer their screen positions. Only one real pack observation is available. Rarity/color/name and rarity/color/collector-number sorting both fit that pack, while reversing the log does not, so no automatic rule is justified.

`VisualCardPlacement` is a separate immutable map from the full `CardOccurrenceKey` to a zero-based visual slot, scoped to one immutable `DraftPack`. It validates a complete bijection: every current occurrence exactly once, including duplicate IDs, with no foreign key and at most the existing 14 calibrated slots. It never changes the ordered pack, scores, ranks or raw statistics.

The status panel's **Match card positions** controls allow the user to select the card at each numbered slot, left to right then top to bottom, and **Confirm card positions**. Same-name choices receive copy labels; their underlying occurrence keys remain distinct. `VisualPlacementContext` adds a per-pack reference/generation guard, rejecting old selectors and callbacks even for value-equal pack states. Editing selections hides badges until confirmation. A new pack clears the map and requires fresh confirmation; statistics-only updates retain it. Maps are session-local and are not persisted or automatically carried to surviving cards because their new screen positions are unknown.

`PlaceAll` resolves each badge's key through the confirmed map, then uses that independent visual index to select the existing calibrated `CardSlot`. It does not index geometry by `PackIndex` or recommendation rank. Unconfirmed or invalid placement clears its visual index/coordinates and the passive item's `IsPlaced` visibility gate hides it. The transparent host surface can still exist while no metric items are visible; the rail reports the missing placement. Calibration guides use assigned visual-slot identity or show only the slot number, never a guessed log-order name.

All existing geometry, column profiles, persistence and native interaction policy remain unchanged. The five-column profile's first nine slots form 5+4; this does not establish a universal Arena layout. Other pack sizes use the user's selected calibrated profile, with new assignments on each advance. If Arena changes card sizes or row distribution, edit the existing overlay geometry/columns before confirming positions again. Diagnostics expose collection index, log occurrence index, independent visual slot, card identity, raw metrics/ranks and coordinates; unmapped slots are explicitly labeled.

See PHASE9B_3_REPORT.md for the exact P1P6 replay, all-size checks, metadata-order audit and physical validation limits. The conservative implementation prevents silent misplacement without inventing an Arena sort rule. Phase 9C has not begun.

## Phase 9B.4 - Local automatic localization

`ICardVisualLocator`, `CardVisualLocalizationRequest/Result`, `CardVisualMatch`, confidence states and `PackVisualAssignment` live in Application. The exact maximum-weight assignment uses occurrence keys and an O(n·2^n) dynamic program for 1–14 cards. Results validate current-pack ownership, unique keys/slots, finite confidence, normalized rectangles and non-overlap. Identity and recommendation ordering remain separate from physical location.

App's `AutomaticCardLocalizationSession` orchestrates capture/reference loading/recognition. Its Windows `IArenaRegionCapture` implementation lives in Platform, discovers MTGA's client/DPI, and copies only an unobstructed calibrated draft region through the window DC. Phase 9B.5 removes the original foreground prerequisite and moves GDI acquisition off the UI thread. Capture is read-only, memory-only normally, with no full-desktop image, game input, memory access or screenshot upload. Covering-window and geometry checks surround capture. GDI/DirectX compatibility is not physically validated yet; unsupported or low-confidence results retain manual fallback.

The calibrated region becomes a transient normalized Arena-client anchor, following window movement/size/DPI while converting physical desktop origins to logical overlay dimensions. A 500 ms timer checks metadata only. Phase 9B.6 binds immutable candidate snapshots to the visual pack token and change timestamp. Pack changes cancel old work and invalidate frame/result metadata. Shared synchronization waits asynchronously for a configurable 250 ms visual settle, rejects repeated prior-pack pixels after count changes, and uses unchanged previously confirmed artwork rectangles for a conservative excess-occupancy check. Stale captures retry after 150/250 ms, at most three acquisitions; exhausted synchronization does not reschedule. Each acquisition has its own request counter in addition to the window/geometry epoch. Same-pack Retry obtains new pixels without changing the semantic token. References, image evidence and analysis run off the UI thread. Explicit result tokens, context/reference and geometry/revision checks prevent late-result publication; manual edits are preserved. Partial matching retains the existing bounded retries with two-second spacing, through the same synchronized acquisition policy. Size/DPI changes invalidate placement. Existing screen calibration remains fallback when no anchor exists; the anchor is not persisted across restarts.

`ScryfallVisualReferenceCache` indexes small-image URLs from the already local bulk file. Only current-pack references are fetched from validated HTTPS small/front image URLs, sequentially with rate spacing/headers, bounded bytes/dimensions and no redirects. RAM holds current candidates; disk retention is capped at 128 thumbnails (~32 MiB). Cached picks/frames do not request assets again. This component changes neither normalized Domain cards nor the statistics provider.

`CardTemplateRecognizer` proposes calibrated and compact/max-row left/centered layouts, refines geometry against artwork, and area-averages 20×14 RGB templates. Proposals are hypotheses, not claimed Arena layout rules. Matching uses normalized correlation with independent half-art agreement, minimum similarity 0.94, competing identity/slot margin 0.07, flat-image rejection and overlap guards. Assignment is global before partial accepted matches are published; at least half the pack must pass to anchor partial geometry. Similarity is not a calibrated probability. Same artwork under distinct IDs stays ambiguous; duplicate identical IDs receive separate deterministic logical occurrence assignments.

`OverlayViewModel` retains the immutable badge statistics model. Automatic matches supply normalized rectangles by full occurrence key; manual confirmed maps remain fallback. Full recognition needs no interaction; partial recognition prefills accepted choices and exposes only unresolved positions for completion. Uncertain badges stay hidden. The manual matcher is available as a collapsed explicit override. Normal status shows matched/expected counts; collapsed diagnostics expose method, dimensions, rectangle/candidate counts, duration and similarity/margin evidence without image payloads.

Debug image saving is disabled by default. Explicit `DRAFTTG_LOCALIZATION_DEBUG_DIRECTORY` opt-in writes one overwritten `localization-latest.png` containing only the draft region, annotated with rectangles/names/slots/similarity. Clear the variable and delete that file after development. No automatic screenshot persistence or network upload occurs. Windows GDI needs desktop-session access, not a new Screen Recording prompt; Arena/region must be visible and unobstructed. Capture still has a residual OS timing race if another window moves mid-copy.

macOS currently uses manual fallback. The shared core is portable, but native capture is not implemented. The concrete plan is a bundled signed ScreenCaptureKit bridge selecting only MTGA, window-relative point source rectangles and single-frame `SCScreenshotManager` capture, returning bounded BGRA plus origin/Retina scale through the existing capture seam. Screen Recording permission and denied/revoked permission handling must be implemented/validated on macOS. Vision is an optional future local OCR signal if real captures justify it.

The Phase 9B.4 baseline had 637 passing tests, including 18 focused tests and a synthetic P1P6 automatic placement/metrics regression. Windows physical capture/consecutive picks remain blocked by the inspection runtime failing to start; Mac validation is unavailable. The synthetic benchmark does not prove live accuracy. See PHASE9B_4_REPORT.md for public sources, decision record and acceptance gaps. Phase 9C remains unstarted.

## Phase 9B.5 - Windows capture runtime

The production action path is RailPanelView -> OverlayDesktopSession.OnAction -> AutomaticCardLocalizationSession.RetryManually -> ArenaCaptureCoordinator.Restart/AcquireAsync -> selected IWindowFrameCapture backend -> existing CardTemplateRecognizer -> OverlayViewModel's existing placement guard. Phase 9B.7 selects WindowsGraphicsCaptureFrameCapture by default, with GdiBitBltFrameCapture retained as a diagnostic/opt-in initialization fallback. Startup begins the metadata timer before calibration I/O, so a superseded load cannot skip localization startup. Explicit Retry rediscovers Arena, cancels the previous revision and actively starts a fresh acquisition; a retry during active work resumes as soon as cancellation is observed.

The backend remains GDI GetDC/CreateCompatibleDC/CreateDIBSection/BitBlt(SRCCOPY), with a top-down bounded BGRA bitmap; there is no graphics-device creation, frame pool or callback subscription. IWindowFrameCapture separates discovery and asynchronous acquisition. EnumWindows associates top-level unowned UnityWndClass windows with MTGA PIDs, reports title/class/client origin/size/DPI, refuses multiple eligible windows, and validates HWND/process/visibility/geometry around each acquisition. TokenIntegrityLevel queries are diagnostic only; mismatch or unavailable token access does not demand elevation. Foreground is reported, not required. Occlusion checks still reject overlapping windows.

ArenaCaptureCoordinator tags frames with a capture generation and enforces a two-second fresh-frame timeout, configurable with DRAFTTG_FRESH_FRAME_TIMEOUT_SECONDS (0.1-30 seconds). Handle/process changes, meaningful client origin/size/DPI changes and explicit restarts increment generation. Focus/title/integrity changes and one-pixel observation jitter do not. Geometry and generation are checked before publication; older frames are disposed. GDI work runs in Task.Run and native allocations are always released in finally. A timed-out native call cannot be forcibly stopped; late results are disposed and a semaphore prevents concurrent native resource accumulation. Capture/crop complete before reference loading begins, exposing frame receipt independently of network work. The artwork matcher is unchanged.

Expanded Card placement diagnostics show stage, process/HWND/title/class, visible/minimized/foreground, physical client bounds/DPI, integrity, initialization/running state, generation, last frame time/dimensions, crop dimensions, localization invocation/result, and exception type/HRESULT/native code. Crop validation failures before requesting regional pixels explicitly say that no frame was requested; a delivered frame with invalid crop dimensions is separately diagnosed. Full exception details are absent from passive badges.

The explicit Save current Arena capture for debugging action bypasses references/matching and captures one bounded draft crop to %TEMP%/DraftTG/arena-draft-capture.png. It also overwrites arena-draft-capture.txt with the full stage diagnostics, including on capture failure. PNG/log writes are opt-in through this diagnostic action; the existing localization debug environment option remains disabled by default. Manual confirmation and edits survive capture failures and explicit retries. macOS must later implement the discovery/frame/generation seam; no Mac capture or Phase 9C work is included.

652 tests pass (15 focused additions), and the solution build has zero warnings/errors. Physical HWND selection, integrity, rendered pixels, automatic localization and two consecutive picks remain unverified: the Windows computer-use runtime fails before initialization with os error 3, including after a reset. See PHASE9B_5_REPORT.md. Phase 9B.5 is implemented but has not passed live acceptance.

## Live-frame artwork matching audit

The user subsequently supplied the actual 1325 x 1131 regional PNG and its capture diagnostic. The log confirms the Unity rendering HWND, initialized GDI acquisition, a fresh delivered frame, non-foreground Arena and Medium integrity for both applications. The image visibly contains all nine expected cards. Capture was retained unchanged. This establishes successful capture for this observed frame, not consecutive-pick or full overlay acceptance.

The original matcher reproduces 0/9 offline despite choosing every correct identity and assignment. Its descriptor already samples artwork, not the full card: a 20 x 14 RGB area average of the normalized core (x=.075, y=.165, width=.85, height=.40), centered and energy normalized, with the minimum of global/left/right correlation as its score. The coarse portrait/cell geometry does not align that core finely enough between Arena and the paper-card thumbnails. All nine scores fail the existing .94 gate; margins/assignment/reference identity are not the cause.

CardTemplateRecognizer now registers the artwork locally after selecting a coarse geometry proposal. Bounded coarse-to-fine coordinate descent adjusts origin and independent width/height, using the same descriptor/objective. Origin stays within 10% of the coarse seed dimensions; dimensions stay between 85% and 115% of the seed. Five step sizes (.04 through .0025), four iterations each and eight neighbors bound the work. Scores never decrease during refinement. The existing exact occurrence assignment is solved again and the unchanged .94 confidence, .07 identity/slot margin, overlap, variance and pack-geometry guards authorize results. The original proposal remains available in the explicit offline coarse-only mode for regression/ablation. No OCR, new image dependency, reference/printing heuristic or scoring change was introduced.

All nine exact-printing 146 x 204 small-front JPEGs already in the cache match the visible art. Their ID/URI/Arena/set/collector metadata is saved with the regression fixture. Both captured/reference cores normalize to the same 20 x 14 descriptor grid; independent local dimensions tolerate small Arena rendering/aspect differences without relying on title/text layout. The resulting scores are .980-.996 and all nine identities/rectangles pass the existing placement safety guard on this PNG. Manual selection and asynchronous publication guards remain unchanged.

Program recognizes the explicit --localize-image/--manifest/--output developer command before initializing Avalonia. OfflineArtworkCommand loads a pack manifest, PNG and local references, runs the production matcher, and writes per-slot text/JSON plus an annotated PNG. It has no capture/network path. ExpectedVisualOrder is used only by the report/test assertions; it does not reach the matching request. --coarse-only reproduces the original failure. CardRecognitionAudit records candidate matrices/rectangles, best/second/assigned scores, competing-slot score, threshold/margin and rejection reasons.

The privacy-safe exact live PNG and nine reference snapshots are under tests/Fixtures/arena-artwork-live. Six new focused tests reproduce the old failure, prove 9/9 correctness, cover resized/dimmed captures, reject missing/wrong artwork and verify offline output. All 658 tests pass with zero build warnings/errors. The rebuilt live overlay and new packs/consecutive picks still require validation; this is exact-frame offline acceptance. See ARTWORK_MATCHER_REPORT.md. Windows capture, Phase 8/9A/9B scoring, providers, statistics association and Arena parsing are unchanged.

## Phase 9B.7 - One-shot Windows Graphics Capture

The earlier nine-card GDI PNG was later returned unchanged for a current eight-card pack despite fresh destinations and valid request timestamps. The Windows composition now defaults to `WindowsGraphicsCaptureFrameCapture` behind `IWindowFrameCapture`/`IArenaRegionCapture`. `GdiBitBltFrameCapture` retains the original client-DC copy as an explicit diagnostic backend; automatic fallback is disabled unless the developer opts into initialization-only fallback. Runtime/device/timeout/cancellation failures never silently switch to GDI. Production automatic localization, Retry and debug save share the selected backend and existing coordinator/synchronizer.

Each request initializes a hardware D3D11 BGRA device, creates an HWND GraphicsCaptureItem through supported interop, and starts a free-threaded two-buffer frame pool/session on an asynchronous MTA worker. It requires a post-request presentation timestamp and unchanged content/client/window geometry. WGC window bounds are verified against DWM/window rectangles before converting the calibrated client crop; only that ROI is copied to CPU staging memory and a newly owned Skia bitmap. Session/pool/device/native textures close after one frame or any failure. Geometry/device changes fail safely and subsequent requests initialize fresh resources. The whole window exists transiently on the GPU; no desktop or whole-window CPU image is produced. Normal frames are memory-only.

The conditional Windows target uses the Microsoft Windows SDK .NET projection (net10.0-windows10.0.19041.0, API minimum build 18362) and a small App.Platform native bridge, without a third-party graphics framework. Other platforms still compile net10.0 with manual fallback. The existing matcher/reference cache and Phase 9B.6 source remain byte-for-byte unchanged. Diagnostics and saved text show actual backend/HWND, timestamp, dimensions, hashes, request/pack/window ownership and processing durations. Developer-only `--compare-arena-capture` uses saved calibration and an explicit current-pack manifest to save paired WGC/GDI observations; covering-window protection remains enforced for GDI.

Two real WGC captures of current P1P7 show the expected eight cards; the unchanged matcher correctly identifies 8/8 at .94 similarity/.07 ambiguity gates. The privacy-safe current crop is retained as a regression fixture. All 687 tests pass, including 15 focused additions; Windows and portable builds have zero warnings/errors. Physical overlay placement and three consecutive picks remain unvalidated because Computer Use cannot initialize (os error 3). GDI comparison was refused because another HWND covers the draft crop, so simultaneous current-WGC/stale-GDI is not claimed. See PHASE9B_7_REPORT.md for requirements, source audit, exact capture/scores and remaining acceptance. Phase 9C remains untouched.

The subsequent Phase 9C request confirms Phase 9B.7 physically complete on Windows, superseding that historical pending status.

## Phase 9C - Set archetype and color-pair awareness

The pipeline now preserves four immutable results: Phase 8 statistical value -> Phase 9A pool value -> Phase 9B lane value -> Phase 9C final contextual value. Each archetype card holds its original lane result, which retains the pool and statistical results. Prior formulas and occurrence order are unchanged. No new platform API or dependency is needed.

### Curated profiles and empirical evidence

Domain owns `ArchetypeColorPair`, `ArchetypeDefinition`, `SetArchetypeProfile` and the profile-catalog contract. Data's `SetArchetypeProfileCatalog` loads every embedded `Archetypes/*.json` file. Profiles validate exact uppercase set codes, two distinct Magic colors, unique normalized pairs, nonempty names/descriptions, an HTTPS source, and optional nonempty story metadata. Unknown JSON fields are rejected; there are no numeric card ratings. Another set requires another profile file, with no engine conditional. Unknown sets receive zero Phase 9C adjustment and continue using the Lane result.

The initial WOE profile describes all ten color pairs. Its source is Wizards' [Wilds of Eldraine Story, Part 2](https://magic.wizards.com/en/news/making-magic/wilds-story-part-2). These curated descriptions explain strategies; empirical 17Lands pair GIH supplies numeric affinity. The engine does not inspect descriptions, story names or Oracle text to score cards.

### Pair confidence

`ArchetypeContextProfile` uses Phase 9A's drafted-pool color evidence and progress factor. It does not add lane support again:

```text
coverage = (evidenceA + evidenceB) / totalColoredEvidence
balance = 2 * min(evidenceA, evidenceB) / (evidenceA + evidenceB)
pairPoolFit = coverage * (0.75 + 0.25 * balance)
confidence = clamp(pairPoolFit * Phase9AProgressFactor, 0, 1)
```

A zero denominator produces zero coverage/balance. Candidates sort by confidence then normalized pair code. The leading pair is active only at confidence >= 0.35 and a lead over second place >= 0.10. Both thresholds are configurable. Unsettled pools receive zero adjustment. The profile is recomputed on each snapshot; an established pair can change as later picks change the pool. This is a product heuristic, not a calibrated probability or optimality claim.

### Verified API and lazy cache

The tested JSON contract is `/api/card_data?expansion=WOE&event_type=QuickDraft&time_period=ALL_TIME&colors=BG`. The actual filter is `colors`, not `deck_color`. Pair values must use WUBRG order: UI GU queries `colors=UG`; GW queries WG; RW queries WR. The live audit returned zero rows for GU and 324 rows for UG. The proposed `deck_color=BG` returned all 324 overall GIH rows unchanged against the existing overall cache; `colors=BG` changed those samples/rates. No card-data HTML is scraped. GIH is still `ever_drawn_win_rate` with `ever_drawn_game_count`.

Application requests only an active pair and preserves the exact draft format: QuickDraft, PremierDraft, or TradDraft. Pair response identity must match the requested set, format and pair; there is no cross-format fallback. The initial update uses the Lane result, then asynchronous pair completion recomputes the latest snapshot. Session generation, revision and snapshot guards reject stale calculations. A late old-pair response can remain cached but cannot apply to a newly active pair.

The default budget is three distinct pair-dataset load attempts per draft, configurable from zero to ten. Replayed observations reuse loaded or pending data. Requests share the existing provider gate, with at least one second between pair network attempts. No per-card requests or ten-pair prefetch occurs. The existing client retains a 24-hour TTL, atomic temporary-file replacement, valid stale fallback, one-hour failure cooldown, and Retry-After handling, with no automatic retry loop.

Pair cache identity includes set, exact format, ALL_TIME, normalized pair, card-data schema v3 and pair schema v1. A BG example is `WOE_QuickDraft_ALL_TIME_v3_BG_pair_v1.json`; cache payloads additionally validate `/api/card_data`, requested/source format, `DeckColors`, and `PairSchemaVersion`. Existing overall v3 filenames and payload compatibility are preserved. Missing data, exhausted budget, invalid responses or provider failures leave the Lane score in use and report the reason.

### Affinity model and final ranking

The pair environment baseline uses every valid row in the filtered provider dataset, independent of the current pack. Only finite GIH in [0,1] and a positive GIH sample count contribute:

```text
pairBaseline = sum(pairRawGIH * pairSample) / sum(pairSample)
pairAdjusted = (pairSample * pairRawGIH + 300 * Phase8Value) / (pairSample + 300)
overallRelative = Phase8Value - OverallEnvironmentBaseline
pairRelative = pairAdjusted - pairBaseline
lift = pairRelative - overallRelative
affinity = clamp(lift / 0.04, -1, +1)
archetypeAdjustment = affinity * activePairConfidence * 0.015
finalValue = clamp(Phase9BValue + archetypeAdjustment, 0, 1)
```

`ArchetypeConfiguration` makes the 300-game prior, 0.04 lift scale and 0.015 maximum adjustment explicit and configurable. These are model choices; full confidence permits at most +/-1.5 percentage points per card. Mono-A, mono-B and AB cards are eligible. Off-pair, colorless, 3+ color or unknown-color cards are neutral. Missing/invalid pair GIH, unavailable pair/overall baselines or an unsettled/unknown archetype produce zero adjustment. Phase 8 unscored cards stay unscored, even when pair GIH exists; missing pair evidence is never synthesized.

Scorable cards sort by final value, Lane value, Pool value, Stats value, overall GIH sample, then original pack index. Ranks are written back to the original occurrence positions; duplicate IDs remain independent. Fewer than two scorable cards still yields no Context Pick.

### Presentation and validation

`CurrentPackCardPresentation` validates the fourth stage against the same full occurrence key and nested lane result. The rail shows one final Context Pick, a different Stats Pick, all four ranks, existing pool/lane context, active archetype description/confidence, pair-data state, signed adjustment and available pair GIH/sample. Unsettled profiles may show the two leading candidates. Collapsed diagnostics expose every pair's evidence and all card-level baselines, raw/adjusted pair values, lift, affinity, adjustment and final value/rank.

Passive badge XAML, raw overall GIH, ALSA, size and placement remain unchanged; rank and gold #1 consume the final occurrence result. Statistics-only changes preserve badge objects and confirmed placement. WGC, artwork recognition, synchronization and Arena parsing are untouched.

Twenty-four focused additions bring all 687 existing tests to 711 passing tests. Windows and portable net10.0 builds/tests pass with zero build warnings/errors. Windows Phase 9C physical validation could not run because the Computer Use runtime cannot start (os error 3); actual macOS execution is also unvalidated. Portable validation on Windows verifies compilation and automated behavior, not native macOS acceptance. See [PHASE9C_REPORT.md](PHASE9C_REPORT.md) for the exact audit, files, tests and limitations. Phase 9D remains unstarted.

## Phase 9B.8 — WGC capture ownership and recovery

One `ArenaCaptureCoordinator` handles automatic placement, Retry and debug save. Replacement cancels the preceding acquisition, retains its cancellation source until the underlying worker finishes, and fences old callbacks by request as well as geometry generation. Reset clears current-frame fields while retaining the last successful timestamp and precise failure operation/type/HRESULT/message. Idle/calibration/window/pack/confirmed-placement waits are explicitly diagnosed.

`WgcCaptureLifecycle` serializes initialization, acquisition and teardown through one ownership gate. Native work runs on one backend-owned MTA. A healthy D3D device and capture item are retained for unchanged validated window geometry; each request creates/closes its own session/frame pool. HWND/geometry checks and post-request QPC filtering remain. Polling avoids frame-arrived/item-closed subscriptions. Faults close partial resources and clear retained device/item state; Retry rediscovers HWND and starts a clean request. Captured bitmaps are disposed if teardown/delivery fails. Shutdown drains owned work before releasing retained resources and the apartment.

`--stress-wgc --fixture --cycles 100 --output <directory>` captures its own temporary window, discards frames and records diagnostics/resources. Device-only reuse still grew handles in an extended probe; device plus item reuse captured 1,000/1,000 frames with collected handles leveling around 600. A final 100-cycle run passed. Diagnostic GC occurs only in the harness. Fourteen focused additions bring Windows/portable suites to 725 passing tests with zero build warnings/errors. Actual Arena 5/4/3/2/1-card capture and placement remain pending. Matcher, geometry, synchronization and recommendation/provider code are unchanged. See [PHASE9B_8_REPORT.md](PHASE9B_8_REPORT.md).

## Phase 9D - Successful / trophy deck evidence

Phase 9D preserves Phase 8 -> Pool -> Lane -> Archetype -> Final as independently inspectable results. `TrophyCardRecommendation` retains its exact Phase 9C occurrence, value, rank and top-pick flag. The production adjustment is **zero**. The source audit found incompatible baseline semantics; there is no configuration switch that invents a comparable baseline. Existing recommendation formulas are unchanged.

### Corpus boundary and identity

Domain owns `SuccessfulDeckKey`, the exact QuickDraft/PremierDraft/TraditionalDraft enumeration, metadata-derived `SuccessfulEventCriteria`, `SuccessfulDeckSample`, `SuccessfulDeckCorpus`, provenance/load status and `ISuccessfulDeckProvider`. Samples retain full readonly maindeck and optional pool name/count dictionaries, normalized pair, final match record, completion time, internal event identity, representative build and pool basis. They reject non-trophy records, invalid counts, deck counts exceeding known pool counts, mixed context, duplicate events and future/incomplete corpora.

The Data adapter contains all 17Lands DTOs. Production composition can canonicalize provider card IDs through the existing Arena/Scryfall resolver, including validated front-face/canonical multiface names. Unresolved names remain exact and can only join to matching canonical names; there is no fuzzy or positional association. Current pack duplicates retain independent occurrence ranks and share the same card evidence. `CurrentPackCardPresentation` checks the new nested occurrence before exposing any metrics.

### Audited source and current availability

The [Trophy Decks page](https://www.17lands.com/trophy_decks) uses `POST /api/trophies/` with expansion, exact event_type, deck_colors, ranks and card_names arrays. Its UI describes a recent maximum-100 sample; no pagination/limit request is exposed. A summary nominates an aggregate_id/deck_index. `GET /api/deck/draft/` supplies event metadata, maindeck/sideboard repeated card IDs and a card dictionary. The adapter uses the nominated build once per event, without claiming it is final or most-played. Splash variants returned by the color filter are excluded from this initial strict-pair corpus.

A trophy reaches its event metadata's `trophy_win_count`, with wins/losses measured in matches and `best_of_n` retained. The supported format loss guards follow the audited Arena event structures: Bo1 trophies have fewer than three losses; current Traditional Draft trophies have zero match losses. Maximum wins are not universally hardcoded to seven. See [Arena's Draft Guide](https://magic.wizards.com/en/mtgarena/draft) and [Traditional Draft event structure](https://magic.wizards.com/en/news/mtg-arena/mtg-arena-state-game-streets-new-capenna-2022-04-21).

Maindeck plus sideboard represents the *available build pool*, including added basic lands; it is not mislabeled as the drafted pool. The audit observed 42 drafted cards versus 58 available build cards (40 main + 18 side), with the 16 additional cards being basic lands. The pool-basis tag preserves this distinction for later construction work.

The internal API's current response notice explicitly restricts outside use. `SeventeenLandsTrophyClient` stops at that notice before fetching builds, displaying corpus evidence or saving a cache. Restricted cache payloads are also rejected; a newly observed restriction prevents stale-cache fallback. The [public dataset catalog](https://www.17lands.com/public_datasets) has WOE PremierDraft/TradDraft data but no WOE QuickDraft data. Consequently no permitted exact-format WOE QuickDraft corpus loads in current production. The adapter handles usable structured payloads, but the audit does not claim a supported public API or a live corpus. No Premier/Quick/Traditional substitution, alternative-host bypass or bulk startup dependency is added.

### Evidence and scoring decision

`TrophyEvidenceCatalog` counts each event once. For card A, pool exposures count events with at least one A in a known pool. The conversion numerator counts those known-pool events with A in the representative maindeck. Missing pools are unknown, not zero: their maindeck exposures remain in prevalence diagnostics, but never enter the conversion/copy-utilization numerator. Conversion = known-pool maindeck events / pool events; copy utilization = known-pool maindeck copies / pool copies; prevalence = maindeck events / corpus events; average copies when played = maindeck copies / maindeck events. None of these raw diagnostics alone ranks cards.

[17Lands metric definitions](https://www.17lands.com/metrics_definitions) state that Play Rate is weighted by games and copies. Event-presence conversion uses a different observation unit and denominator. Subtracting the two would not be a valid lift. Phase 9D therefore calculates evidence/confidence diagnostics only: adjustment = 0 and final value/rank exactly equals Phase 9C. It cannot double-count GIH, choose an archetype, score an unscored card or boost an off-pair card.

Defaults: minimum corpus 20, minimum pool exposure 5, full corpus confidence 50, full exposure confidence 15, maximum distinct corpora per draft 3. Corpus confidence = clamp(decks/50, 0, 1); exposure confidence = clamp(pool events/15, 0, 1); combined confidence = active Phase 9C confidence * corpus confidence * exposure confidence for eligible evidence. Pair-contained colors qualify; colorless requires observed maindeck use; third-color and unknown cards do not. Small samples remain diagnostic.

If a future comparable baseline is established, the proposed prior/lift model requires a separate audited change: prior 10 exposures, lift scale .20, maximum +/- .010, and final value clamped to [0,1]. These numbers do not activate a formula in this implementation. A future enabled rank stage would sort by Final, Archetype, Lane, Pool, Stats, GIH sample and pack index; current ranks simply preserve the independently reproducible Phase 9C order.

### Cache and asynchronous ownership

Data caches under `limited-data/17lands/trophy`. Identity includes exact expansion/format/pair, all-ranks, recent-strict-pair query mode, requested cap and schema v1; content validates both source endpoints. Defaults are 20 nominated builds maximum (bounded 1..100), 24-hour TTL, one-hour failure cooldown, one-second request spacing, and zero immediate retries. There is one recent summary request, then at most 20 build requests for usable responses, with no pagination. A 429 honors Retry-After across pairs. Writes use a unique temporary file and atomic replacement; corrupt/mismatched/future/restricted cache data fails closed. Stale valid data can survive transport/schema failures, with diagnostics.

`LimitedStatisticsCoordinator` starts a load only with an active supported Phase 9C archetype. It memoizes success and unavailability per exact key for the draft; no per-pick requests occur. Computation and I/O stay off the monitor/UI thread. New drafts/environments cancel and generation-fence workers; pair switches immediately select the new key, so late old-pair results cannot attach. Completion recomputes the current immutable snapshot and revision, preserving current placement.

The existing rail displays source availability/loading, eligible recent sample count, pool/main event counts, conversion, pre-trophy/final ranks and the disabled-adjustment reason. Diagnostics expose copy counts, prevalence, utilization, average copies, confidence, source query/caps/time and representative/pool policy. Passive badge size, GIH, ALSA and placement are unchanged.

### Validation and limits

Twenty-three focused tests retain all 754 prior tests, bringing Windows and portable suites to 777 passing tests and zero final build warnings/errors. Source hashes establish that Phase 8/9A/9B/9C scoring, original statistics provider/mapper, Arena parser, capture backends, matcher and placement files are unchanged. Windows active-archetype corpus validation was not performed because no permitted exact-format WOE QuickDraft corpus is available. Native macOS execution is not proven by portable tests on Windows.

Trophy samples are biased toward successful events/players, available drafted cards, recent tracked events and deck construction decisions. They supplement GIH. Complete samples preserve a foundation for future deck patterns/copy distributions, but no deck builder, mana/curve/quota rules, co-occurrence or semantic synergy is implemented. See [PHASE9D_REPORT.md](PHASE9D_REPORT.md).

## Phase 10A: drafted-pool and deck-building metadata foundation

### Inventory and chronology

Domain owns immutable `DraftPoolSnapshot`, `DraftPoolEntry` and `DraftPoolCompleteness`. A snapshot contains the existing immutable `DraftedCardPool` occurrence inventory, derived identity/count entries, known/total counts, and unresolved occurrence count. Exact `DraftHistory` remains a separate set of coordinate-qualified selections. No entry requires a pick position or recommendation rank. Snapshot value equality includes counts, completeness and unresolved occurrences. Collections are copied and exposed read-only.

Application's `ArenaDraftPoolAssembler` resolves the state engine's already-authoritative `ArenaDraftStateSnapshot.DraftedPool`. That existing inventory combines the recovered multiset, missing covered exact selections, and exact selections after its coordinate; the assembler does not add history again. Thus exact A/B plus recovered A/B/C/D gives four cards. Existing recommendation snapshots and resolver failure behavior are preserved. The new `ArenaDraftSnapshotResult.DraftPool` is also available while waiting for a pack, after Completed, and when only some pool identities resolve. Unknown/ambiguous IDs remain explicitly counted without inventing Domain identities; unique count covers resolved identities only.

Completeness concerns inventory, not chronology. For currently supported one-card, three-pack/fourteen-pick formats, Complete requires reliable counts matching the current coordinate (or 42 at completion), plus a validated recovered baseline or exact coverage contiguous from the first pick. A recovered current multiset can therefore be Complete with no early coordinates. Between packs the latest exact/recovered count establishes the current expected boundary. Missing selections or identities yield Partial. Contradictory picked-card diagnostics, unsupported modes/coordinates, multi-card picks, or an unknown boundary yield Unknown. The absence of a parser warning alone does not prove completeness. Completion with an old 15-card baseline retains those cards as Partial; it cannot fabricate 42 cards. A source reset clears the pool; a genuine new draft follows existing engine reset semantics and replaces it. Domain contains no Arena provenance.

`MainWindowViewModel` updates the pool and analysis independently of actionable pack availability. Clearing the current pack leaves the pool visible after completion. Monitoring warnings can downgrade inventory reliability without deleting the known cards. There is no persisted saved-draft feature in this phase.

### Card metadata and normalization

`Card.GameplayMetadata` carries one immutable `CardGameplayMetadata` profile. Existing six-argument Card construction defaults to explicitly unknown gameplay metadata. The profile includes nullable finite nonnegative double mana value, canonical mana cost and type line, `CardTypeSet`, layout, immutable faces, optional Oracle text, immutable keywords, string power/toughness, and nullable structured mana production. Missing mana value differs from zero. `ColorsKnown` distinguishes absent color metadata from an explicitly colorless card while leaving the existing `Card.Colors` and all recommendation color behavior unchanged.

Central `CardTypeSet.Parse` interprets type tokens before the subtype separator; it supports overlapping Creature/Land/Instant/Sorcery/Artifact/Enchantment/Planeswalker/Battle flags and Other. It derives `IsCreature`, `IsLand`, `IsBasicLand`, `IsNonlandSpell` and the five normal basic-land subtypes from type metadata, without name matching. Unknown types are not silently called nonland spells. Other-only non-game card types do not enter spell curves. Multi-type cards contribute to every applicable type count.

Data is the only Scryfall schema boundary. It maps `cmc`, `mana_cost`, `type_line`, `layout`, `oracle_text`, `keywords`, `power`, `toughness`, `produced_mana` and useful `card_faces` fields. No such JSON attributes/DTOs enter Domain or RecommendationEngine. Existing identity, printing order, name multiplicity and Arena ID resolution remain intact. Present invalid mana values or malformed type data produce focused import errors; absent optional/legacy fields remain unknown.

Adventure/transform/modal double-faced profiles use permanent/front types; Adventure cards do not become solely their Adventure spell type. The original combined display type/cost strings are retained. A missing top-level cost/text/P/T can fall back to the front face. Split cards retain both types and provider-supplied combined mana value/cost. Top-level mana value is used when supplied; otherwise front-face supplied value is retained. A front Adventure/transform/modal face can inherit the known top-level value. Other missing face values/colors remain unknown: no mana-symbol evaluator or face-choice logic is introduced. Each `CardFaceMetadata` retains name/cost/value/colors/type/text/P/T and derives its types centrally, avoiding a second editable type source. Existing top-level colors or the established face-color union are preserved.

`ManaKind` represents White/Blue/Black/Red/Green/Colorless separately from the five-value `MagicColor`. Absent produced-mana data is unavailable, not proof of no production (e.g. Evolving Wilds). Recognized kinds are normalized; unusual symbols are retained in `UnrecognizedManaSymbols`. The real cache includes two Unfinity Sole Performer printings with `T`; they do not reject the catalog or become a color. Whole-card production can describe an alternate face and is not yet a chosen mana-source policy.

The existing raw gzip JSONL already contains these fields. No normalized metadata cache exists, no cache version bump/invalidation is necessary, and reparsing does not force download. The production loader successfully imports all 118,469 cached printings, including 381 WOE printings; after face fallback, all have top-level value/type/color information. A reduced public-card fixture and normalized audit preserve representative WOE and other multiface cases.

### Descriptive analysis and presentation

RecommendationEngine's `DraftPoolAnalyzer` consumes only Domain pool/catalog values and returns immutable `DraftPoolAnalysis`. It counts copies for totals, unique resolved identities, creatures, lands, nonland spells, all type memberships, W/U/B/R/G memberships, colorless and multicolor. Membership sums can exceed card count: BG adds one to both B and G; Artifact Creature adds one to both types. None of these diagnostics feeds recommendation scoring.

Nonland and creature mana curves use intervals [0,2), [2,3), [3,4), [4,5), [5,6), [6,7), [7,infinity), plus Unknown. Integer values read as 0–1/2/3/4/5/6/7+ while fractional values remain unambiguous. Lands are excluded from the spell curve; an unknown value on a known nonland spell goes in Unknown. Missing card records, identities, types and colors have explicit diagnostic counts; unknown types are not guessed into a curve and unknown colors are not counted as colorless. Diagnostic counts can overlap.

The existing status rail adds a collapsible Draft pool summary, available at completion, with a nested selectable entry diagnostic view. It exposes identity/name/count/colors/value/cost/types/flags and optional text/keywords/P/T/faces/production. Full entries are not emitted into normal logs or badges. The existing active Phase 9C archetype remains a separate diagnostic in the same rail and does not drive this analyzer. Phase 9D's canonical card/count samples can later join via the existing catalog name/identity policy; no corpus comparison is implemented.

Twenty-five focused additions retain all 777 prior tests. Windows and portable suites pass 802 tests with zero final build warnings/errors. Hash inventories verify no changes to prior scoring, statistics providers/mapping, Arena parsing/state reconstruction, capture, matcher or placement. Portable validation on Windows does not prove native macOS execution or physical panel layout. Phase 10A describes the pool; Phase 10B will construct decks from it. No deck, basic-land additions, roles, curve targets, mana optimization or add/remove highlights exist. See [PHASE10A_REPORT.md](PHASE10A_REPORT.md).

## Phase 10B: one baseline two-color deck

### Inputs, output and availability

RecommendationEngine owns the provider-neutral `DeckBuildInput`, `DeckCardStrength`, `DeckPlan`, `DeckBuildResult`, `BaselineDeck` and counted card/basic-land entries. The pure `BaselineDeckBuilder` consumes the final Domain pool/catalog, overall and optional environment statistics, set archetype profile, exact statistics context and optional matching pair statistics. It requires no current pack or pick chronology. Output collections are immutable; generated basics have no pool identity and consume no drafted inventory. Every resolved pool occurrence appears once in the main deck or sideboard, including unused drafted basics. Limited has no four-copy cap.

The default successful build is exactly 40 = 23 nonland spells + 17 lands. Complete inventory gives Ready; Partial or Unknown inventory with enough known eligible cards gives explicitly provisional output. Insufficient eligible cards or missing identities/types/colors/costs can make construction unavailable. Unknown mana value does not itself prevent selection; it remains Unknown in the curve and earns no early-play credit. Unusable statistics affect confidence rather than eligibility. The internal API can evaluate pools directly; normal UI construction starts only after a recognized Completed state.

### Final plan and strength

Recompute final color evidence and Phase 9C context from the pool, even after the current pack clears. Prefer the ActiveArchetype pair when it supplies 23 eligible spells. Otherwise rank all ten pairs by the Phase 9C fit formula `coverage * (.75 + .25 * balance)`, then combined color evidence, balance and canonical WUBRG pair order. Select the highest viable pair and expose PoolEvidenceFallback. Known spell types, known colors and a supported primary casting cost are required. Color eligibility uses Domain colors: colorless or a subset of the selected pair. No intentional third color is introduced.

DeckSelectionValue is Phase 8 adjusted overall strength, optionally plus the existing Phase 9C adjustment when the final active pair, set/format statistics context and available pair card row match. Colorless cards keep overall strength under the existing affinity eligibility policy. Phase 9A evidence chooses a plan; its per-card bonus is not applied again. Phase 9B lanes and Phase 9D trophy conversion never enter deck strength. Rarity does not enter selection.

An otherwise eligible unscored card receives the usable overall environment baseline as a NeutralBaselineFallback selection prior, explicitly not measured GIH. With no baseline its selection value is null; deterministic unscored ordering and InsufficientStatistics confidence replace an invented percentage. Objective sums and average measured selection values are diagnostics, not a predicted deck win rate.

### Bounded spell selection

Defaults: MinimumCreatures 14, PreferredCreatures 16, MinimumEarlyPlays 4, HighCostThreshold 5, MaximumHighCostCards 6. An early play has known mana value <=2; high cost has known value >=5. The floors shrink to available eligible supply. A bounded dynamic program over occurrence count, actual creature count, early count clamped at the required floor, and high-cost count selects exactly 23 occurrences. It first finds the minimum high-cost count jointly feasible with both floors, then raises the configured high-cost cap only to that minimum if necessary. Constraint relaxations are reported.

The optimizer maximizes the sum of selection values, then measured-strength count, proximity to 16 creatures, then stable CardIdentifier/copy order. A bit mask represents inventory choices and deterministic ties, not exhaustive subset enumeration. Configurations validate jointly supportable floor settings; the public builder bounds input at 200 occurrences. Cancellation is checked during optimization. Per-card reason flags use counterfactual optima from the same DP state table with individual constraints removed, plus the unconstrained optimum; they do not infer requirements merely from card type or cost.

### Nonbasics and baseline mana

Select lands separately. Defaults: at most four drafted nonbasics and six generated basics per used color. A nonbasic's known card colors must fit the pair. Prefer reliable structured production of selected colors (both before one), then Phase 8 adjusted overall strength and stable identity/copy order. Without reliable colored production, measured overall value at least the environment baseline is required. A neutral prior does not prove usefulness. Structured off-plan-only production rejects a land. Whole-card production for multiface lands may describe a back face, so it is not credited in this phase. Missing metadata does not prove fixing; Evolving Wilds is not declared a five-color source from Oracle text.

`DeckManaDemand` uses the primary face cost when present, otherwise the first cost in the combined display. It never sums both top-level and face costs. W/U/B/R/G contribute one pip; colored hybrid alternatives split one pip evenly. Generic, C, X/Y/Z and snow symbols contribute no colored demand. Colored Phyrexian/numeric hybrids conservatively retain their colored demand. Missing or unsupported costs remain explicit. Alternative faces, colorless/snow source needs and activated costs are not optimized; eligibility still uses Domain card colors rather than cost strings.

Generate `17 - selectedNonbasicCount` basics. Reserve six per color with positive selected-spell demand when feasible, reduce that reserve to `floor(basicSlots / usedColors)` otherwise, and allocate remaining slots by demand with largest-remainder rounding and WUBRG ties. Only one used color receives basics for an effectively monocolor deck. With no colored demand, the first canonical plan color supplies generic mana. Reliable nonbasic colored sources are reported diagnostically but do not subtract from basic demand. Generated counts always sum to the exact remaining land slots.

### Runtime and rail

Application's `DeckConstructionService` uses the existing statistics client's environment and optional final-pair loads/cache without modifying provider behavior or requesting trophy evidence. A synthetic pack scopes existing identity mapping and the pure Phase 8 API; it is neither an Arena pack nor fabricated draft history. `DeckConstructionCoordinator` runs completed-only work off the UI thread, deduplicates unchanged final inventories and fences publication by generation. Changed pools/new drafts cancel prior work. The view model additionally checks generation, session identity/event and pool equality. Runtime cancels deck work before disposing the shared statistics HTTP owner.

The existing rail adds a collapsible Baseline Suggested Deck: plan/confidence, 40/23/17 summary, creature count, Baseline Mana Base, fractional-safe curve, counted Creatures/Other Spells/Lands, Sideboard / Not Included and expanded decision diagnostics. Deck publication does not trigger pack presentation or move passive badges. It performs no Arena interaction, import, highlighting or automatic card changes.

Twenty-six focused additions brought initial Phase 10B Windows and portable validation to 828 passing tests. Existing scoring, provider/mapping, parser, capture and localization sources were unchanged in that phase. A cache-only real completed WOE pool yielded RG, 23 spells, 17 generated basics, 14 creatures, 19 sideboard copies, four neutral-prior selections and no constraint relaxation in approximately 15 ms. Its original audit explicitly adapted an unrecognized DeckSelect completion payload; Phase 10B.1 below resolves that activation boundary. Native macOS execution and physical UI acceptance remain unverified. See [PHASE10B_REPORT.md](PHASE10B_REPORT.md).

## Phase 10B.1: live DeckSelect completion integration

### Observed response and canonical completion

The current Windows log shows a standalone incoming `BotDraftDraftPick` correlation marker followed on the next line by a top-level JSON object with `CurrentModule: DeckSelect`, string-encoded `Payload` and unrelated `DTO_InventoryInfo`. Payload contains `Result: Success`, `EventName: QuickDraft_WOE_20260929`, `DraftStatus: Completed`, zero-based PackNumber 2/PickNumber 13, NumCardsToPick 1, an empty DraftPack and 42 string Arena IDs in PickedCards. It carries no draft ID. The outgoing final-pick request names card 87053; the preceding P3P14 response has 41 picked occurrences. The final multiset equals those 41 plus that exact selection. A subsequent scene change says Draft → DeckBuilder.

ArenaIntegration alone parses this envelope. `ParseDeckSelection` verifies the actual root module, successful result, explicit Completed status and draft event/mode. It emits the existing canonical `DraftCompleted` semantic event with optional completion origin, mode and final Quick Draft PickedCards. No JSON/DTO moves to Domain or Application. A bare DeckSelect module, Courses reference, ModulePayload, scene change, current deck, sideboard, generic CardPool, failed result or noncompleted response is not draft-completion evidence. Final inventory ingestion is limited to the observed single-selection Quick Draft shape. The standalone incoming pick response marker is skipped, like the existing status marker; it is not parsed as an outgoing pick request.

### Authoritative pool and consistency

Completion is applied atomically: status becomes Completed, the actionable pack clears, exact selections and recovered inventory remain. If the existing recovered/exact multiset already has 42 occurrences and the completion PickedCards agrees, that inventory remains authoritative; even the recovered baseline object is retained. The observed 41-card baseline plus the exact final selection therefore stays 42 without summing it with another 42-card array.

An explicit successful single-selection Quick Draft completion snapshot may recover a missing final pick or bootstrap a completed replay if it contains exactly 42 cards and includes every already known occurrence with at least its known multiplicity. It becomes a coordinate-free recovered pool; it does not invent pick chronology. Wrong totals or missing known multiplicities produce FinalPoolMismatch with both counts, expected 42 and the retained-source rule. Known inventory is retained without a merge, completeness becomes Unknown through the existing diagnostic policy, and any usable deck is provisional. A later agreeing snapshot can clear the warning. Other deck/sideboard/CardPool lists never correct drafted inventory.

Repeated equivalent completion facts are idempotent in the state engine, so the session coordinator publishes one changed completion and the existing deck coordinator builds once. Identifiable late DeckSelect responses for a different event/draft cannot complete the current session. Genuinely new session boundaries clear the pool and existing generation/session/pool fences discard old asynchronous deck results. The observed EventJoin request shape includes EntryCurrencyType, EntryCurrencyPaid and an outer request ID. A new such entry after completion resets even an anonymous session using the same event name; replaying the same entry request does not reopen it. Its correlation token is retained privately for entry deduplication and never promoted to DraftIdentifier or Domain identity. If no identifiable entry/session boundary is logged, the existing anonymous ambiguity remains.

### Automatic activation and validation

The existing flow now runs directly from raw records: source → parser → state engine Completed → DraftSessionCoordinator → preserved Domain DraftPoolSnapshot → DeckConstructionCoordinator → Baseline Suggested Deck. No completion polling, manual refresh, manually fabricated Completed snapshot or direct developer builder call is needed. Completed mismatch warnings also clear the actionable pack and update Draft complete status while retaining warning/provisional details in the rail.

Eleven focused additions retain all 828 previous tests: 839 pass on Windows and portable targets with zero build warnings/errors. The small privacy-safe real sequence and public catalog subset cover canonical completion, conservation, recovery, mismatch diagnostics, irrelevant deck/course data, malformed fields, legacy completion, idempotence, new-session reset, automatic construction and the view-model transition.

A snapshot of current Player.log replayed from byte zero through the observed WOE completion and next scene change using the actual parser, state/session coordinator, view model, deck coordinator and real local statistics caches. It produced one changed completion, zero parser warnings/conflicts, Complete pool 42, Draft complete status, an accessible Ready baseline and the same RG 40-card result (23 spells, Mountain ×9, Forest ×8, 14 creatures, 19 sideboard copies). No HTTP requests occurred. This is a production-code replay with a headless dispatcher, not a physical final-pick/rail observation. Physical live acceptance and native macOS execution remain outstanding. Scoring, providers, Phase 10B selection and Windows capture/localization sources are unchanged. See [PHASE10B_1_REPORT.md](PHASE10B_1_REPORT.md).

## Phase 10C — Multiple two-color suggested decks

`SuggestedDeckBuilder` preserves the baseline by invoking `BaselineDeckBuilder.Build` directly and retaining that result/deck as Recommended Build 1. The baseline entry point and `BuildForPair` share one extracted `BuildForPlan` core: the same occurrence optimizer, constraints, nonbasic policy, primary-face mana parser, basic allocator and inventory accounting. Alternative plans use `AlternativeColorPair`; baseline plan source and affinity eligibility remain unchanged. Phase 8/9 formulas, parsing, completion and capture/localization are untouched.

All ten canonical WUBRG pairs receive eligibility diagnostics using existing metadata/color rules and counted occurrences, including colorless cards. Every pair meeting `TargetNonlandCount` is built independently against the entire pool, even when only one suggestion is requested. Invalid optimization/composition/color/inventory candidates are excluded with a reason. Output has at most three distinct pairs; it is never padded. Each build independently selects nonbasics and allocates basics, and sharing owned cards across mutually exclusive proposals is legal.

`SuggestedDeckComparison.CommonSpellQuality` averages selected spell copies on one common overall Phase 8 basis. Measured copies use `Phase8AdjustedValue`; missing rows use the overall environment neutral prior. Pair-specific adjustments are excluded. If no common basis exists, quality is null and unscored coverage is explicit. This internal metric is not a deck win prediction or calibrated rating. Build 1 stays fixed; alternatives sort lexicographically by quality descending, final pair pool fit descending, measured copies descending, neutral copies ascending, composition relaxation count ascending, combined final color evidence descending, and canonical WUBRG pair order. Basic-allocation diagnostics do not count as composition relaxations. Trophy evidence does not participate.

Immutable provider-neutral `SuggestedDeckSet`, `SuggestedDeck`, `SuggestedDeckId`, `SuggestedDeckRank`, comparison/candidate diagnostics and counted difference types live in RecommendationEngine. Availability reuses `DeckBuildAvailability`. A canonical SHA-256 pool identity includes resolved copy counts, completeness and unresolved count. Stable deck IDs combine an opaque completed-session identity with the pair, independently of rank and selected cards. Standalone builders default the session identity to the pool hash. Application uses a completed-session generation identity that survives same-session pool corrections and changes at a real session boundary. These IDs are session state, not persisted Arena identities.

`SuggestedDeckDifference` computes added/removed spell copies, shared/changed nonland counts and separate drafted/generated land changes against Build 1. Duplicates are quantities, never unique-name set differences. Sets expose the recommended and selected build, all ten candidate diagnostics, viable evaluated count and excluded reasons. `Select` returns a new immutable set without reconstruction.

`DeckConstructionService` loads the same overall environment and optional final active-pair dataset already used by Phase 10B. It then constructs the entire suggested set locally. `MaximumSuggestedBuilds` defaults to 3, limited to 1–3. `MaximumAdditionalPairDataRequests` defaults to 2, limited to 0–2; Phase 10C performs zero additional pair requests. Alternatives use overall strength because the existing affinity rule applies only to the final active-archetype baseline. Provider/cache behavior is unchanged. Initial relevant loads finish before construction; no additional alternative-request/retry loop is introduced.

The existing completed-only coordinator publishes the baseline and set together. Worker generation, cancellation, session identity and final-pool checks reject stale completions. Identical session updates do not rebuild. No rail expansion, movement or capture event triggers construction. The view model defaults to Build 1, retains a selected ID through unrelated updates or a replacement set within the same session, and falls back when that ID disappears. On a corrected pool it hides the old proposal while retaining the pair choice for the replacement; on a new draft it clears suggestions and selection. The compact Suggested Decks selector shows one main deck and sideboard, coverage, curve, mana base and relaxations, with alternative differences available in an expander. Selection changes advisory state only and emits no pack/overlay action.

The actual completed 42-card WOE replay produces two suggestions: RG Big-Creature Beatdown and UG Ramp and Adventures. The RG card/land lists exactly match the saved Phase 10B.1 result. UG uses 10 creatures and visibly relaxes the floor from 14 to 10. All other pairs fail the 23-copy eligibility gate. The audit uses production parser/coordinators/view model with a headless dispatcher and actual local caches; physical UI interaction and native macOS execution remain unvalidated. See [PHASE10C_REPORT.md](PHASE10C_REPORT.md).

## Phase 10D audit and semantic foundation (in progress)

Phase 10D is incomplete. The October 4 log audit establishes exact historical saved WOE main/sideboard and basic-land counts, but not the current unsaved editor contents. A real deck-builder capture and a user-performed edit observation are still required before production localization/highlighting. The live log reported EventLanding and later Home during this audit. No deck-builder zone rectangles, duplicate display rules or scrolling behavior have been inferred from the draft-pack layout. See [PHASE10D_AUDIT.md](PHASE10D_AUDIT.md).

`DraftPoolSnapshot` remains the owned inventory; `SuggestedDeckSet.Selected` remains the chosen proposal; new Domain `ArenaDeckSnapshot` represents an observation with explicit scope, source, completeness, basic counts, unresolved copies and an `ArenaDeckRevision`. Scope distinguishes `SavedDeck` from `CurrentEditor`: exact saved counts alone cannot certify live editor contents. Revisions combine an opaque completed-session identity and a caller-assigned positive monotonic number. No Arena revision field was found in the audited save response, and no runtime deck revision coordinator is wired yet.

The separate ArenaIntegration `ArenaDeckLogParser` recognizes the audited scene marker and direct top-level server `CourseDeck` responses with `InternalEventName`. Positive bounded `cardId`/`quantity` main/sideboard arrays are required. Requests, historical nested course collections, generic deck JSON and draft completion `PickedCards` never become current deck membership. Malformed observations and unsupported companion/command zones fail closed. The existing draft parser and state machine remain unchanged. `ArenaSavedDeckAdapter` resolves exact printing identifiers through the existing resolver/catalog, separates stock basics by type, checks main-plus-sideboard drafted multiplicities against the completed pool, and always retains `SavedDeck` scope.

Application `ArenaDeckComparisonTarget.From` uses the selected build, including drafted nonbasics and separately generated basics. Immutable `ArenaDeckDifference` computes exact copy additions/removals, already-correct entries, current/target sizes, selected build identity, observed revision and five separate basic-land changes. It returns `CurrentDeckUnknown` with empty guidance for saved-only, incomplete, missing or wrong-session observations. Build switching and fresh observed snapshots are pure recomparisons; no optimizer, ordering or provider changes occur. These APIs are compiled and tested but are not connected to the rail or live Arena source.

The eventual visual layer must localize visible tiles or list rows within independently audited Pool/MainDeck/Sideboard zones. Identity matching must be constrained to the known pool plus basics and measured against real crops before selecting deck-specific alignment/configuration; draft thresholds remain 0.94 and 0.07. One displayed stack is one visual occurrence with an optional confidently known quantity, not multiple invented draft slots. Add outlines belong in the addable pool zone and Remove outlines in the current main deck. Ambiguous or hidden cards must remain without rectangles while trustworthy textual differences remain available. This visual abstraction, localization, highlight model and production UI are pending the audit rather than implemented.

The intended dedicated passive deck overlay will reuse existing native transparency, click-through and non-activation behavior; the rail remains the only interactive DraftTG surface. Event-driven WGC requests and apply-time fences must bind current revision, selected build, localization generation, session and compatible screen/window geometry. They must clear stale outlines on edits, build switches, navigation or a new draft. No production deck capture loop, overlay window or stale-result application pipeline exists yet; the existing draft pipeline is unaffected.

An explicit `--capture-deck-builder` developer command uses existing WGC and Arena-window-only region capture. It refuses a non-DeckBuilder latest scene and discards the frame if the scene record or window geometry changes during acquisition. It saves a cropped PNG and diagnostics only when explicitly invoked, never automates Arena input, and does not certify current editor counts. The command and semantic models compile on the portable target; native macOS deck-builder capture/localization remains unimplemented and unvalidated.
