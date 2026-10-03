# Phase 7.4 — Calibration exit and passive overlay

Implemented the calibration exit hotfix. No Phase 8 work was started.

## Cause

The Save handler could return during geometry validation before clearing calibration state. Native inspection on this Mac showed screen bounds of 1728 × 1117 with `Screen.Scaling=1`, while `Window.RenderScaling=2`. The previous capture multiplied the client extent by render scale, doubling the apparent region within desktop screen coordinates. A valid region such as position (180,160), size 1200 × 600 was consequently rejected as outside the display. This explains why the guides, instruction strip, interactive overlay, and flyout remained visible.

Capture now uses the screen's coordinate scale, matching restore. The same conversion supports Windows desktop pixels at scaled DPI. The existing file format is unchanged. Native smoke testing also exposed a one-pixel height growth caused by normalized floating-point error being rounded up during layout; restore now removes that numerical noise while preserving fractional logical sizes.

## UI transition

The existing flow is extracted into `OverlayCalibrationEditor` so production Save/Cancel transitions can be tested without native windows. `IsCalibrating` remains the single authoritative editing state; transition/persistence guards only prevent reentry and intermediate window refreshes.

Successful Save captures and validates current geometry, persists it, updates saved state, then:

1. Hides the card window during the transition.
2. Sets calibration mode false, clearing guides and hiding the entire calibration visual subtree.
3. Closes the calibration flyout.
4. Applies the existing passive native mode, removing calibration chrome and restoring mouse-ignore/non-activation.
5. Restores saved geometry after the native chrome change.
6. Shows only active-pack badges when statistics are enabled; the rail remains visible and interactive.

Cancel shares that exit path, discarding unsaved region/layout changes and restoring the previous calibration. First-run Cancel returns to the uncalibrated state. The calibration icon now enters editing and opens the flyout directly; reopening after Save/Cancel restores saved geometry. Clicking the icon while already editing reopens controls without resetting current edits.

The cyan boundary, slot rectangles/names, handles, instruction strip, and tint all remain inside one `IsCalibrating` visibility gate. The guide collection is empty in passive mode. Badges are outside that gate, and the root/window background remains transparent. No native adapter implementation was changed.

Invalid geometry and failed persistence retain editing with a diagnostic rather than falsely claiming a save. A native passive-mode failure closes the calibration presentation but leaves cards hidden. Display invalidation supersedes an in-flight save.

## Automated validation

- `dotnet build DraftTG.sln`: zero warnings, zero errors.
- `dotnet test DraftTG.sln --no-build`: 415 passed, zero failed/skipped. All previous 402 test cases retained; 13 added.
- Totals: Domain 42, Data 92, ArenaIntegration 94, Application 114, RecommendationEngine 6, App 67.
- New coverage: enter/save/cancel/reopen, persisted current geometry, Retina desktop versus backing-pixel scale, scaled Windows-style coordinates, integer/fractional geometry round trips, pending writes, invalid geometry, persistence failure, display invalidation, native policy failure, and actual XAML containment of all calibration-only visuals.
- The existing guide-resize test now explicitly enters calibration before inspecting guide positions, because guides are intentionally empty during passive presentation.

## macOS native-window smoke validation

A local harness used real `OverlayDesktopSession`, card/rail windows, production bindings, and routed button click events. It supplied two synthetic pack cards and in-memory settings; no live runtime, user settings, Arena process, screen capture, or physical input was used.

Observed and asserted:

| Action | Calibration layer | Guide count | Flyout | Badges | Mouse-ignore | Key/main eligible | Chrome |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Enter | Visible | 14 | Open | Visible | No | Yes | BorderOnly |
| Save | Hidden | 0 | Closed | Visible | Yes | No | None |
| Re-enter | Visible | 14 | Open | Visible | No | Yes | BorderOnly |
| Cancel | Hidden | 0 | Closed | Visible | Yes | No | None |

The saved position (180,160) and client size 1200 × 600 remained identical after Save, re-entry, and Cancel of a subsequent move/resize. Save populated the settings document. The rail stayed visible. Both `IsVisible` and `IsEffectivelyVisible` on the actual calibration layer were false after Save/Cancel.

This verifies actual native windows, bindings, and button routing, not physical mouse clicks or rendered-pixel inspection. Physical Arena click-through and foreground/keyboard focus retention were not reverified in this run. Windows was not physically tested. The user's physical Phase 7.3 testing is prior evidence, not claimed as Phase 7.4 validation.

Remaining manual check: restart the updated app over Arena, enter calibration, save, confirm only badges and rail remain, click an Arena card through a badge, and confirm Arena retains focus. Re-enter calibration and confirm the saved region returns.

Arena parsing, Scryfall, 17Lands behavior, scoring, ranking, suggestions, images, inference, OCR, and Arena window detection are unchanged.
