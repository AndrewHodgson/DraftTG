# Offline UI / Overlay Reliability Pass

Completed offline on 2026-10-07. This pass implements card names, earlier accepted-pick placement retirement and Windows event-driven tracking. Production placement still uses WGC/artwork localization. Deterministic placement remains shadow-only; Phase 9E.2B and deck-builder localization were not started.

## Card names

Files: `src/DraftTG.App/OverlayViewModel.cs` and `src/DraftTG.App/CardOverlayWindow.axaml`.

`CardBadgeViewModel.DisplayName` reads the canonical name from its existing `CurrentPackCardPresentation`, the same occurrence-specific object supplying rank, GIH and ALSA. It requires a resolved, consistent identity. Unresolved/inconsistent occurrences omit the name. No OCR, independent identity lookup or additional network request was introduced. Duplicate cards retain their distinct occurrence keys and placement assignments.

The name is visible by default immediately above the original 42-DIP rating badge: one centered line, 10-DIP font, a narrow translucent background and character ellipsis. Its width is bounded by the localized card rectangle and 180 DIPs. Canonical text is retained in the model; long and multiface names, including `Hallway Heckler // Vicious Verse`, truncate only during rendering. Rating text, color/tier styling and badge dimensions remain unchanged. The label shares the parent placement visibility gate. `ShowCardNames` / `SetCardNamesVisible` provide a future settings integration point; no settings page was added.

## Stale badges

Files: `src/DraftTG.Application/DraftSessionCoordinator.cs`, `src/DraftTG.App/MainWindowViewModel.cs` and `src/DraftTG.App/OverlayViewModel.cs`.

### Trace before the change

1. The log source supplies Player.log records to `ArenaDraftLogParser`.
2. Parsed `PickSubmitted` reaches `ArenaDraftStateEngine.ApplyPick`.
3. An accepted matching coordinate records the pick and consumes the current pack. Duplicate records are unchanged; older coordinates do not consume a different displayed pack.
4. `DraftSessionCoordinator` synchronously converts the accepted state through `ArenaDraftSnapshotAdapter`, including identity/pool resolution, before yielding the update.
5. The runtime dispatches `ApplySessionUpdate` to the UI. Its no-current-pack path already clears the presentation and badges.

The old path did not deliberately wait for the next pack. Its remaining delay could include log delivery, snapshot conversion and dispatcher scheduling. Physical latency was not measured during maintenance.

### Implemented boundary

The earliest authoritative boundary is **state-engine acceptance of a `PickSubmitted` that consumes the currently displayed coordinate**. `CurrentPackPickAccepted` is emitted and awaited immediately after that acceptance, before snapshot adaptation. A known conflicting draft ID is excluded; an unknown ID may be enriched by the accepted pick. The runtime awaits the UI dispatcher, which validates the displayed draft/coordinate and raises `CurrentPackPlacementInvalidated` once.

The overlay retires the placement context, advances its visual generation, clears automatic/manual mappings and slot selections, and hides the outgoing badges while waiting for the next valid pack. This early action leaves history, pool, current occurrence rows and recommendation data intact. It neither advances the coordinate nor declares completion. The normal adapted state update continues afterward.

Changing `PlacementContext` activates the existing pack cancellation, capture invalidation and retry revision mechanisms. Late capture/recognizer results fail the existing context, pack-generation and revision checks and cannot republish the outgoing badges. Statistics updates cannot undo the waiting visibility gate. A genuine next pack creates a new context and resumes normal synchronized visual localization. Identical pick replays do not emit another notification; the UI also guards duplicate invalidation.

“Immediate” means the first UI turn after accepted semantic evidence, before snapshot conversion. It does not eliminate Player.log delivery or UI queue latency. No mouse, hover, selection or focus signal triggers pick clearing.

## Windows window tracking

Files: new `src/DraftTG.App/Platform/ArenaWindowEvents.cs`, plus `src/DraftTG.App/Platform/ArenaRegionCapture.cs`, `src/DraftTG.App/ArenaCaptureCoordinator.cs`, `src/DraftTG.App/AutomaticCardLocalizationSession.cs` and `src/DraftTG.App/OverlayDesktopSession.cs`.

### Trace before the change

The shared `GdiBitBltFrameCapture.InspectWindow` discovery path enumerates MTGA processes and their eligible top-level Unity rendering HWNDs, then reads the client rectangle, converts its origin with `ClientToScreen` and obtains DPI with `GetDpiForWindow`. Ambiguous windows remain rejected. `AutomaticCardLocalizationSession` inspected metadata every 500 ms; `SameCaptureGeometry` included desktop origin, so meaningful moves restarted the capture epoch, invalidated automatic placement and could rerun the matcher. The normalized client anchor drives `OverlayDesktopSession` position and logical viewport dimensions.

### Event observer and fallback

Windows now uses process-scoped, read-only `SetWinEventHook` registrations with `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`. Nothing is injected into Arena, and no admin rights or process-memory reader is involved. Four registrations cover:

| Events | Purpose |
|---|---|
| `EVENT_OBJECT_LOCATIONCHANGE` | Move/resize notifications |
| `EVENT_SYSTEM_FOREGROUND` | Reinspect when Arena becomes foreground |
| `EVENT_SYSTEM_MINIMIZESTART` / `EVENT_SYSTEM_MINIMIZEEND` | Minimize/restore transitions |
| `EVENT_OBJECT_CREATE` through `EVENT_OBJECT_HIDE` | Create, destroy, show and hide transitions for the tracked HWND |

Callbacks filter the exact known HWND. Object events additionally require `OBJID_WINDOW` and `CHILDID_SELF`; process filtering is part of registration. Notifications coalesce into a posted UI-dispatcher inspection, where fresh geometry/DPI is read instead of trusting event payloads. Registration and unregistration run on the installing UI/message-loop thread. This follows Microsoft's [SetWinEventHook contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook) and [UnhookWinEvent thread requirement](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-unhookwinevent).

A **2-second metadata health poll** recovers missed events, absent/recreated HWNDs, Arena restarts and failed registration. There is no 500 ms window timer. Every health/event inspection retains the existing fail-closed discovery policy. A lost HWND unregisters old hooks and invalidates its placement; a discovered replacement rebinds its process/HWND hooks and requests fresh visual localization. Create events for other HWNDs are intentionally ignored; the health poll discovers replacements that have no matching old-HWND notification.

### Geometry classification

| Change | Behavior |
|---|---|
| Origin only; same HWND/PID, exact client width/height and scaling, visible state unchanged; completed placement | Move the overlay, retain occurrence/rating objects and local rectangles, retain the pack token and capture epoch, request no WGC or matcher work |
| Client width/height change, including one pixel | Invalidate automatic/manual placement and request the existing synchronized visual locator |
| DPI/scaling change | Recalculate logical overlay dimensions and invalidate/relocalize safely |
| HWND/PID change or loss | Retire placement and rebind/recover safely |
| Foreground only | Retain visible placement; focus is diagnostic, not a correctness gate |
| Hidden/minimized | Hide the surface; restoration reinspects before further work |

Pure translation avoids resetting an unchanged overlay viewport. Display-screen notifications now inspect actual Arena geometry instead of forcing a retry. Translation tolerance applies only to completed placement: in-flight acquisition, matcher publication and shadow-evidence admission retain their existing origin-sensitive freshness checks. A move cannot authorize an old frame/result. Capture quality, confidence and ambiguity thresholds are unchanged.

The observer roots its managed delegate until native registrations are removed. Replacement/disposal unregister hooks; queued callbacks and late old-HWND notifications are ignored. Failed partial registration is cleaned up and retried by health polling. If native unregistration fails, callbacks are disabled and their root is retained safely; a later disposal can retry cleanup. Normal cleanup and this failure path are exercised through the fake native API.

## Validation

**20 new tests**: five card-name tests, five accepted-pick tests and ten window/event tests.

- `tests/DraftTG.App.Tests/OverlayReliabilityTests.cs`: 13 tests for canonical/long/multiface/duplicate/unresolved names, unchanged ratings, early hiding, duplicate/unrelated picks, late canceled results and next-pack recovery; completed translation, one-pixel resize, DPI change, lost/replaced HWND, foreground and disposal.
- `tests/DraftTG.App.Tests/ArenaWindowEventTests.cs`: five tests for native event filtering/process scope, hook replacement/late callbacks, partial registration failure, failed-unregistration callback safety, and coalescing/disposal of queued notifications.
- `tests/DraftTG.Application.Tests/DraftSessionCoordinatorTests.cs`: two tests for awaited pre-adaptation acceptance, duplicate replay, draft-ID enrichment and unrelated-coordinate history.

Two existing XAML assertions were updated to distinguish metric text/the rating border from the added label, preserving their occurrence-binding and visibility checks. No screenshot/golden tests were added.

| Validation | Result |
|---|---|
| `dotnet build DraftTG.sln` | Succeeded; **0 warnings, 0 errors** |
| `dotnet test DraftTG.sln --no-build` | **1,016 passed**, 0 failed, 0 skipped; all 996 baseline tests preserved |
| Domain / recommendation / data / Arena integration / application / app tests | 56 / 163 / 160 / 127 / 246 / 264 |
| Isolated sort-reference build and `self-test` | **10 passed**, reported separately from solution tests; 0 build warnings/errors |

The static sort-reference implementation, recommendation scoring, 17Lands calculations, identity fallback, deck behavior and deterministic placement authority were not changed. Existing working-tree changes from earlier phases were preserved.

## Physical Arena validation remaining

Arena maintenance prevented live draft validation. Offline tests establish state, cancellation, geometry and native-adapter behavior with controlled evidence, not measured live responsiveness.

- Readability, clipping and ellipsis over actual artwork at supported resolutions/DPI, including long/multiface names and duplicates.
- Accepted-pick-to-visible-hide latency during a live draft, plus empty-table transitions and next-pack recovery.
- Actual WinEvent delivery and responsiveness during drag, resize, minimize/restore, alt-tab, fullscreen and monitor/DPI transitions.
- Arena HWND recreation/restart and clean native-hook shutdown in a live session.
- Verify completed move-only placement stays visible with no new capture/matcher invocation, while geometry changes trigger fresh visual localization.

Stop condition satisfied: only the three offline reliability improvements were implemented. Phase 9E.2B and deck-builder localization remain outside this pass.
