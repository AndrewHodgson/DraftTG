# Phase 9F.2 — Live Capture-Free Draft Tracking and Experimental Overlay

2026-10-13, America/Chicago. Tested baseline: `d1d09f6b216f23b8e0ce69a77afc552e84b9a014` plus the uncommitted 9F.2 changes. The 9F.1 report/evidence are preserved.

Artifacts: [tested binary hashes](docs/evidence/phase9f2/tested-build.json), [v2 fingerprint](docs/evidence/phase9f2/supported-build-v2.json), [initial overlay ledger](docs/evidence/phase9f2/initial-overlay-smoke.jsonl), [initial overlay summary](docs/evidence/phase9f2/initial-overlay-summary.md), [final inactive-state ledger](docs/evidence/phase9f2/final-inactive-check.jsonl), [final inactive summary](docs/evidence/phase9f2/final-inactive-summary.md). Their original observations are preserved; no synthetic results are mixed into them.

**Implementation/setup is ready; the physical validation gate has NOT passed.** An initial timed live Quick Draft P1P1 smoke run read 14 ordered identities, agreed with the log multiset, generated all 14 existing scores and mapped all 14 badge models at 1600×900 / 96 DPI. The badge surface/visual trees were visible and capture initialization attempts were zero. That run preceded the final compatibility/root-reconnect refinements. The final binary attached to a restarted supported Arena process, reported an inactive draft scene, and correctly withheld badges; its active-draft recheck remains pending. No meaningful portion of a draft or second draft has been validated. Production WGC and frozen scoring remain unchanged.

## 1. Goals

Continuous read-only memory observation and an explicit experimental overlay, using actual `_allCardViews` order. Reuse existing card/catalog/stat/scoring/presentation/renderer systems. No screenshot validator, comparator reconstruction, right-deck/sideboard geometry, post-draft positioning, macOS reader or scoring changes.

## 2. Architecture and files

```text
MTGA (query/read only) -> fresh object traversal + cached runtime metadata
 -> ArenaDraftMemoryWatcher -> CaptureFreeDraftSession + existing Player.log coordinator
 -> existing ArenaDraftSnapshotAdapter / card resolver / statistics / frozen scoring
 -> existing MainWindowViewModel / OverlayViewModel
 -> CaptureFreeDraftLayout (memory index -> measured rectangle)
 -> existing CardOverlayWindow / badge renderer
```

Added:

- ArenaIntegration: `Memory/ArenaDraftMemoryWatcher.cs`.
- Application: `CaptureFreeDraftSession.cs`, `CaptureFreeDraftReconciliation.cs`, `CaptureFreeDraftLayout.cs`.
- App: `CaptureFreeDesktopSession.cs`, `CaptureFreeDiagnostics.cs`, `CaptureFreeSafety.cs`, `CaptureFreeSummaryCommand.cs`, `ArenaMemoryWatchCommand.cs`, `Platform/ArenaWindowInspector.cs`.
- Tests: `ArenaDraftMemoryWatcherTests.cs`, `CaptureFreeDraftTests.cs`, `CaptureFreeOverlayTests.cs`.

Updated existing memory snapshot/reader/runtime/object graph/profile/primitive reader, runtime factory, MainWindowViewModel/OverlayViewModel, CLI/App composition, rail controls, capture constructor guards and the existing small Mono fixture. RecommendationEngine files are unchanged.

`App` selects the experimental shell before constructing `OverlayDesktopSession`. It does not create AutomaticCardLocalizationSession, the artwork/reference pipeline, order-evidence recorder or deterministic shadow observer. The scoring/presentation stack is shared, not duplicated.

## 3. Changes from 9F.1 and provenance

`ReadOnce` retains temporary-connection disposal. `ReadCurrent` supports a serial watcher and caches only process-lifetime module bases, fingerprint, loaded image identities, class metadata/names and field offsets. Every scene, holder, course, view, printing, list instance and list content is freshly traversed.

Compatibility profile: `unity-mono-x64-69dd7ca1-arena-2026-10-13-v2`. Native Mono offsets remain those independently corroborated in [9F.1](PHASE9F_1_CAPTURE_FREE_ARENA_DRAFT_ADAPTER.md). The v2 build gate additionally pins `Wizards.Arena.Models.dll`, before human pick-info traversal:

- SHA256 `7C955BD48BF8C136686D145244B9889AF258A48F338A3E2A77ED0CED1FD9A725`.
- MVID `6180f80c-8973-4ed3-b3df-93c49402ff53`.
- Executable/player/Mono/Core/SharedClientCore/Assembly-CSharp/mscorlib identities are unchanged from 9F.1; UnityPlayer is `6000.3.14.14060607`, x64.
- All five loaded managed-image MVIDs must match the fingerprinted files. Unknown fingerprints stop before Mono/object reads.

The existing static PEReader/MetadataReader audit independently established these new managed paths from the local assemblies:

| Value | Declaration/IL provenance |
| --- | --- |
| Draft pod | `DraftContentController._limitedEvent` -> `LimitedPlayerEvent.<DraftPod>k__BackingField` (`0x040046BD`) |
| Bot draft/event | BotDraftPod backing strings `0x04004B3B` / `0x04004B3A`; empty draft ID remains unavailable |
| Bot coordinates | `_currentPack` / `_currentPick` (`0x04004B41` / `0x04004B42`); visual-data IL adds one to both |
| Bot offered multiset | `_currentPackCards` (`0x04004B43`), List<Int32> |
| Human coordinates/cards | `_currentPickInfo` (`0x04004B5B`) -> pinned PickInfo SelfPack/SelfPick/PackCards (`0x04000B21`, `0x04000B22`, `0x04000B1C`) |
| Human coordinate basis | visual-data IL passes SelfPack/SelfPick directly; opening-state reset explicitly checks 1/1 |
| Durable course identity | inherited `BasicPlayerEvent.<CourseData>k__BackingField` (`0x04004615`) -> `CourseData.Id` (`0x0400468D`) |

These are provenance tokens, not hardcoded game offsets. Course Id is an inline 16-byte sequential Guid, independently corroborated in pinned mscorlib declarations. Int32-array element classification uses `MonoClass.element_class` offset 0, corroborated by the installed getter at RVA `0xC1130` and public Mono layout. No other native offsets or proprietary formulas are added.

Window qualification queries [GetWindowLongPtrW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowlongptrw), GWL_STYLE (-16), requiring the complete [WS_CAPTION](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-styles) mask `0x00C00000`. Border-only styles cannot qualify. Existing independent Arena measurements supply geometry.

## 4. Memory watcher

100 ms completion-based polling; serial reads, no backlog/overlap. A changed coherent read immediately retires current availability. A second identical coherent state publishes the pack. Unchanged polls emit no state/scoring update; they contribute performance/outcome metrics.

Transitions: NotRunning, ProcessAppeared, ProcessReplaced, UnsupportedBuild, DraftInactive, InTransition, PackAppeared, PackChanged, ViewModeChanged, Recovered. Invalid success contracts are rejected defensively. Failed reads have no current pack; LastStablePack remains separately diagnostic and never draws old badges.

## 5. Snapshot consistency

Preserve baseline fresh root/scene/holder/list/view/visual/printing/GrpId checks. Add stable root-domain, limited-event, pod, course/GUID, coordinates/event and pod-list/pick-info checks before/after. Logical quantities must equal visual count; pod offered multiset must match all visual occurrences, including duplicates. List pointers/counts/versions/entries and all IDs are rechecked. Watcher acceptance additionally requires two matching complete reads.

Partial order never publishes. Root-domain replacement discards the read and resets the connection. No remote setup/invocation is used. Passive checks are not atomic and cannot rule out change-and-revert races; physical transition testing is still required.

## 6. Generation identity

PID plus process start time defines the process lifetime. A hashed state key includes process, checked session/coordinates, scene/pod/holder/list identity/version, mode and ordered IDs. Equal-count pack changes are detected. Known draft ID or course GUID plus event/coordinates defines a semantic pack key, deduplicating restart/re-entry observations of the same pack. Unknown identity uses a conservative hashed fallback and cannot satisfy two identified drafts.

Watcher generation retires localization. The reconciled feed has a separate generation/latest-update reference. Delayed feed/statistics/render work is rejected; placement belongs to the current VisualPlacementContext. Persisted ledgers contain hashes, not remote pointers.

## 7. Player.log reconciliation

Memory owns coherent offered membership/order and mode. Logs own durable accepted picks, recovered pool, event/format and chronology. After coordinate/session/event and multiset checks, the existing adapter receives log history with current offered occurrences replaced in exact memory order. No comparator is called and timestamps need not be frame-synchronous.

Diagnostics distinguish agreement, multiset/session disagreement, memory ahead, log ahead, absent context/coordinates and transition. Ambiguity withholds badges. Accepted log coordinates cannot reuse their outgoing memory pack. The existing pre-identity-resolution pick event invalidates the ready token and awaits UI retirement, including Quick Draft presentation scopes enriched by a course ID.

## 8. Layout mapper

Memory occurrence i maps directly to measured rectangle i. Supported `drafttg-measured-list-13-14-v1`: list mode, captioned/windowed client, 13–14 cards, client-pixel height 720–1080, aspect 1.70–1.78.

These are placement limits. Memory identity accepts sane coherent counts 1–32. <=12 and 15-card packs continue tracking/name output but have no badges. No geometry extrapolation or visual verifier is used.

## 9. Experimental launch mode / exact commands

From the repository root, close other DraftTG overlays, then:

```powershell
dotnet build .\DraftTG.sln
dotnet .\src\DraftTG.App\bin\Debug\net10.0-windows10.0.19041.0\DraftTG.App.dll --capture-free-draft-overlay --verbose --ledger .\artifacts\phase9f2-live.jsonl
```

This command includes named order, reconciliation, badge diagnostics and evidence. Optional console-only watcher:

```powershell
dotnet .\src\DraftTG.App\bin\Debug\net10.0-windows10.0.19041.0\DraftTG.App.dll --arena-memory-draft-watch --verbose --ledger .\artifacts\phase9f2-console.jsonl
```

`--arena-memory-draft-once` remains; text output adds resolved names/set/collector and explicit unresolved reasons. `--json` remains raw snapshot diagnostics. Watch/overlay support `--seconds <1..86400>` for timed checks; omit it for drafting. Close the rail or use watcher Ctrl+C to flush/dispose.

Startup explicitly prints capture-free ENABLED, screen capture DISABLED, WGC DISABLED and GDI fallback DISABLED. Coherent updates print CONNECTED, session/course, P/P, view/count, generation, stable-read status and reconciliation. Verbose output lists every index/GrpId/name/set/collector. Manual calibration/capture actions are hidden or rejected.

## 10. Badge lifecycle / human review

Reuse occurrence-specific badge models, existing stat/score joins, EST rules and XAML renderer. Dedicated memory placement avoids fake visual-confidence claims. Record catalog/stat/score/loading/model/slot stages and visual-tree visibility/bounds. Unsupported/ambiguous/unavailable state retires placements; delayed work is generation checked.

Expand the rail's Card placement diagnostics. Confirm only after visually checking every card/order and supported badge position. Placement confirmation requires the complete badge set to be visible; unsupported geometry records order only. The mismatch button records a negative review and withholds that pack until a new pack or explicit re-confirmation. Any recorded mismatch prevents a clean gate.

Visual-tree evidence proves model/tree placement/visibility, not compositor pixels or correct alignment. Human review is required; no screenshots are used.

## 11. Window / resize / DPI

Shared ArenaWindowInspector performs only HWND/client queries. Reuse existing WinEvent tracking/event pump plus a two-second health timer. Move translates the overlay without requesting an identity read. Resize/DPI recomputes from the current stable memory pack, converting physical pixels to overlay DIPs; no matching/capture. Unsupported geometry, hidden/minimized windows and mismatched PID hide badges while retaining memory diagnostics. Independent regular memory polling continues.

Physical move/resize/DPI campaign coverage is still pending.

## 12. Restart / reconnect

Exit/replacement disposes the connection and clears all process addresses; the new PID/start-time is fingerprinted and bootstrapped afresh. Unknown builds never reconnect into traversal. Root-domain replacement also resets metadata. Watcher drops prior stable state on process replacement.

Tests cover exit/replacement/recovery. A fresh restarted supported process was actually attached, with inactive draft state and no badges. Continuous in-flight restart remains unvalidated.

## 13. Performance

Original one-shot cost included file hashing, image/class discovery and field lookup. Cached process-lifetime metadata removes them from warm reads without caching objects or weakening checks.

Initial console smoke: first attach 462.8 ms, subsequent reads ~17 ms, traversal ~10 ms; 35 successful polls, one semantic pack. Initial overlay smoke: first attach 1128.1 ms during application bootstrap; 129 steady reads, median 17.43 ms, p95 19.16 ms, max 23.12 ms. Steady traversal median 10.29 ms, p95 11.25 ms, max 12.67 ms. These measurements preceded final v2/root refinements and are identified as initial smoke evidence.

100 ms delay plus read time yields roughly 8–9 Hz. Cold first-active-scene discovery may appear in the steady maximum after inactive attachment. CPU/allocations were not isolated. Each subsequent run reports its own timings.

Final default-binary inactive-state check: 508 inactive reads, first attach 350.99 ms, steady median 7.74 ms / p95 8.51 ms / max 18.35 ms; zero packs or placements. These inactive timings are not active traversal measurements.

## 14. Evidence and summary

Append-only JSONL schema 1: run lifecycle, memory transitions, batched performance, placements, badge stages, manual reviews, accepted picks, stale rejections, move/resize/DPI/window/source diagnostics. Fields include build/profile, PID/lifetime, session/course, P/P, generation/key, IDs/names/mode, client/DPI, consistency/reconciliation/reasons and timings. No dumps/pixels/remote pointers.

Summary accepts native capture-blocked runs, validates complete coherent occurrences and deduplicates semantic packs. Repeated polls/re-entry do not inflate thirty packs. Synthetic fixtures do not count. Unclosed runs cannot pass. Geometry coverage can grow without inflating pack count. Supported placements require human review for the used profile/geometry.

```powershell
dotnet .\src\DraftTG.App\bin\Debug\net10.0-windows10.0.19041.0\DraftTG.App.dll --capture-free-draft-summary --ledger .\artifacts\phase9f2-live.jsonl --report .\artifacts\phase9f2-live-summary.md
```

Default ledger, if omitted: application-data `capture-free-draft/phase9f2-live.jsonl`, printed at startup. Summary includes sessions/counts/modes/sizes/DPI, poll outcomes/failures, disagreements/declines, stale rejections, process/window events, timing and `Screen capture initialized: NO` for valid observed runs. File replay does not create observations.

## 15. Validation status / tests

Initial live smoke: Quick Draft P1P1, 14 cards, list, 1600×900 / 96 DPI; all IDs resolved and all 14 scores/slots generated, including unchanged estimated-statistics fallback. Renderer diagnostics showed a visible badge surface/trees. No agent clicks/picks or capture validator. The final v2 binary's fresh-process/inactive-scene checks succeeded; active final-v2 pack/overlay recheck is pending.

Not validated: second draft/size, smaller pack, live accepted pick, pack boundary/P1→P2, column geometry, move/resize/DPI, leave/re-entry and in-flight restart. These are unvalidated, not zero failures. Gate: **NOT PASSED**.

Added 19 focused cases covering watcher state/dedup/generation/failure/reconnect, reconciliation and pre-resolution retirement, measured/unsupported layouts, renderer association/resize/stale context, capture guards, missing identities and summary gates. Existing Mono fixture is updated for the fifth fingerprinted image and root-change recovery. No full process mock.

Executed requested default commands `dotnet build DraftTG.sln` and `dotnet test DraftTG.sln --no-build`. Final build: **0 warnings/errors**. Tests: **1,265 passed; 0 failed/skipped**: Domain 56, Data 160, ArenaIntegration 146, Application 279, RecommendationEngine 329, App 295. TRX: `artifacts/phase9f2-default-tests`. Earlier isolated builds used `bin/Phase9F2/`; launch commands use final default Windows output.

## 16. Limitations

Sparse physical coverage. HumanDraftPod semantics are independently implemented but not physically validated. Missing log history/format/session context conservatively withholds scoring. Cosmetic/preferred-printing ID differences cause explicit multiset declines. Passive reads are not atomic. Renderer visibility is not physical correctness; incident counts mean recorded observations/guard outcomes, not proof of untested cases. A positive numerical summary still needs physical matrix and exact-build review.

## 17. Unsupported layouts

Column: `column-mode layout not validated`, no badges. Counts <=12/15+, non-windowed/borderless/fullscreen/unknown style, outside height/aspect envelope: no badges. Identity tracking continues for sane counts. No visual fallback or invented geometry.

## 18. Read-only guarantees

Exact process rights remain `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` (`0x410`), inheritance disabled. Dispose on exit/replacement/root reset/shutdown. Keep bounded region/address/string/list/field/count reads and per-snapshot time/byte limits. No writes, remote allocation/thread, injection, debugger attachment, target managed/Mono execution or elevation. Window code queries Arena and modifies only DraftTG's own overlay. Static provenance comes from local metadata/passive DLL inspection.

## 19. Capture exclusion proof

Experimental composition never constructs the production visual session. Process-wide guard precedes Avalonia composition and rejects capture factory, concrete WGC/GDI/DXGI and automatic visual localization before native capture initialization. Tests exercise those paths. Environment capture selectors cannot enable fallback in this branch.

Smoke ledgers recorded capture blocking enabled, completed runs, ScreenCaptureInitialized=false, RejectedCaptureAttempts=0. Renderer graphics remain ordinary display output. Artwork matcher, fast visual verifier and pixel fallback are absent. Production WGC code remains.

## 20. First physical workflow / promotion requirements

First recheck the current draft with the final binary. Another draft is useful after that check; finish the existing draft first. In windowed list mode, compare/confirm the opening 14/13-card order and supported placements. Pick yourself; check outgoing retirement before coherent next-pack recovery. Continue through <=12: names/order continue, badges disappear by design. Observe pack boundaries and P1→P2.

Move without resize. Test another supported client size (1280×720 and 1600×900 are useful) and an unsupported size that must hide badges. Try DPI/display, leave/re-enter and column switching when practical. Restart with the watcher running outside valuable/time-sensitive drafting when practical. Report mismatches immediately; no screenshot validation. Close/flush and run summary; retain the same ledger for the second real draft.

Require >=30 distinct coherent live packs, >=2 identified real drafts, >=2 client sizes; no incorrect order/placement, stale restored badges or partial current packs; safe observed reconnects; reviewed supported layouts and clean unsupported declines; no capture attempts/initialization or target writes/execution; clean build/tests. Numerical/recorded checks are automated; physical coverage and exact-build evidence require review.

**No default production authority change even if the gate later passes.** Separate explicit approval is required. No 9F.3 starts here.

## 21. Deferred work

Right-side main-deck and sideboard localization; post-draft deckbuilder integration; macOS; independently evidenced additional geometries; broader build profiles; production provider promotion. Scoring/calibration/tiers/v1.6.1 are outside scope.

**Recommendation:** use the explicit experimental mode for the physical campaign. Current results are smoke evidence; WGC remains default authority and scoring remains frozen.
