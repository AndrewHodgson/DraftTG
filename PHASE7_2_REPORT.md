# Phase 7.2 — Gameplay Overlay UX

The central panel has been replaced by a 52-DIP interactive control rail and a separate transparent card-badge window. Both share the existing runtime. No scoring, ranking, suggested-pick highlighting, Arena parsing changes, OCR, screen capture, or Arena window detection was added.

## Using the overlay

- Drag the rail using its dotted top grip.
- Click the status/pick label for status and diagnostics. Routine warnings appear as a small `!`.
- Click `%` to hide/show statistics without stopping draft tracking.
- Click `≡` for drafted history.
- Click `⌖`, then **Set / edit position**. Drag the blue top edge and resize using the corner handles. Select a column profile that matches Arena and align the numbered slots.
- Choose **Save position · resume clicks** to hide guides and restore native click-through. **Cancel edit** restores the previous calibration.
- Close DraftTG using the rail’s `×`. Closing the secondary badge window also initiates shared shutdown.

Normal badges show GIH WR, ALSA, and a subtle low-sample `*`. They have no full card-name label or numbered rank. Loading uses `…`; missing statistics use `—`. Names and slot numbers appear only in calibration. Pack order and duplicate copies are preserved. A new pack replaces the badge list atomically; a statistics update changes text on existing badge objects without moving them. Between-pick states clear the badges.

## Layout and calibration

Fourteen normalized slots are stored as data, separate from Domain/Arena parsing. Available row-major profiles have 4–7 columns. Seven columns/two rows is a starting point, not a validated universal Arena layout. The user must calibrate for their Arena arrangement and may need to recalibrate if Arena reflows cards. Stored slot rectangles can also be adjusted in the small JSON file without rewriting rendering code.

Calibration is stored in DraftTG’s application-data root as `overlay-calibration.json`, containing a schema version, display bounds/render scale, normalized region, and all slot rectangles. Writes use a temporary file and atomic replacement. First-run/missing, invalid, off-screen, undersized, or changed-display calibration leaves badges hidden and displays **Set position** on the rail. Runtime loading and draft tracking continue independently. The MVP does not persist rail position or follow Arena’s window.

## Native input and lifecycle

Only the badge window receives native click-through changes. The rail and flyout remain interactive. Win32 calls are isolated behind `IClickThroughWindowController`; the Windows adapter sets layered/transparent/no-activate styles and restores the original affected bits during editing. The macOS adapter obtains a retained NSWindow, sets and verifies `ignoresMouseEvents`, and releases the handle. These operations request no Accessibility or capture permissions.

Native configuration or window-transparency failure hides the badge window and exposes a compact rail diagnostic rather than leaving an input-blocking surface over Arena. Both windows are owned by `OverlayDesktopSession`, which cancels and awaits runtime/calibration work before closing the windows and flyout. Parameterless XAML constructors create no runtime.

## Validation

Final required commands:

```text
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
```

| Project | Tests passed |
|---|---:|
| Domain | 42 |
| Data | 92 |
| Arena Integration | 94 |
| Application | 105 |
| Recommendation Engine | 6 |
| App/UI | 44 |
| **Total** | **383** |

All previous 350 tests remain and pass. The additional 33 cases cover normalized/scaled geometry, pack counts and transitions, stable statistics updates, missing/low-sample values, settings persistence and cancellation, corrupt/stale-display calibration rejection, flyout state, compact diagnostics, drag initiation, and native mode changes using fake adapters. Tests perform no live provider calls or native pointer automation.

The final build has **0 warnings and 0 errors**; tests have **0 failures and 0 skips**.

## macOS launch and native smoke check

The actual desktop app was launched in the available macOS graphical session and remained running without reported startup errors. A separate, unshown NSWindow smoke check exercised the production native adapter:

| Mode | Native setter/getter verification |
|---|---|
| Gameplay / ignore mouse events | Passed |
| Calibration / accept mouse events | Passed |
| Return to gameplay / ignore mouse events | Passed |

The retained handle reported `NSWindow` and the smoke-check window closed normally. No screen capture, global input hooks, synthetic pointer events, or Arena-window enumeration was used.

**Physical interaction remains unverified:** rail dragging with a real pointer, visual badge alignment over Arena, clicking Arena cards through badges, Confirm Pick, and end-to-end user-driven window shutdown. The user was invited to try these in the launched app. The native property smoke check is not proof of real Arena interaction. Windows was not physically tested.

## Files

Created:

- `src/DraftTG.Application/DraftCardLayout.cs`
- `src/DraftTG.Application/OverlayCalibrationService.cs`
- `src/DraftTG.Data/OverlaySettingsFile.cs`
- `src/DraftTG.App/OverlayViewModel.cs`
- `src/DraftTG.App/OverlayDesktopSession.cs`
- `src/DraftTG.App/WindowDrag.cs`
- `src/DraftTG.App/Platform/ClickThroughWindowController.cs`
- `src/DraftTG.App/ControlRailWindow.axaml` and `.axaml.cs`
- `src/DraftTG.App/CardOverlayWindow.axaml` and `.axaml.cs`
- `src/DraftTG.App/RailPanelView.axaml` and `.axaml.cs`
- `tests/DraftTG.Application.Tests/OverlayCalibrationTests.cs`
- `tests/DraftTG.Data.Tests/OverlaySettingsFileTests.cs`
- `tests/DraftTG.App.Tests/OverlayPresentationTests.cs`
- `PHASE7_2_REPORT.md`

Modified:

- `src/DraftTG.App/App.axaml.cs`
- `src/DraftTG.App/MainWindowViewModel.cs`
- `tests/DraftTG.App.Tests/MainWindowViewModelTests.cs` (partial-class test organization only)
- `ARCHITECTURE.md`
- `ROADMAP.md`

Removed:

- `src/DraftTG.App/MainWindow.axaml`
- `src/DraftTG.App/MainWindow.axaml.cs`

No packages were added. .NET 10, Avalonia 12, Windows/macOS boundaries, and the provider-independent statistics engine are preserved. Phase 8 remains unimplemented.
