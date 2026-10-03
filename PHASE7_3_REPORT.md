# Phase 7.3 — Overlay calibration and passive-window hotfix

Implemented with one outstanding backend limitation: Avalonia 12.1.3 does not implement macOS `BeginResizeDrag`. Calibration now exposes AppKit's native resizable outer frame; interior custom-handle dragging remains unsupported by that backend. Physical acceptance of the fallback and Arena activation behavior remains pending.

## Changes

- Eight edge/corner targets call `Window.BeginResizeDrag` with NorthWest, North, NorthEast, East, SouthEast, South, SouthWest, and West. Corners are 20 DIPs; edge strips are 16 DIPs, above noninteractive guides. Primary-button checks remain in `WindowDrag`. The inset top instruction strip calls `BeginMoveDrag`.
- Explicit `CanResize=true`, `SizeToContent=Manual`, minimum 400 × 220 DIPs. No geometry bindings reset native resize changes and no manual pointer-delta resizing was introduced.
- Save captures live Position, ClientSize, and RenderScaling. `OverlayWindowGeometry` converts between physical position/client DIPs and the existing normalized settings model. Reopen restores saved geometry; cancel discards unsaved changes and restores the previous calibration (or hides an uncalibrated overlay). Decoration changes occur before geometry restoration.
- Passive mode explicitly requests mouse-ignore plus no activation; calibration requests input and activation permission. The session reapplies/verifies this policy after showing the native window and hides the layer on policy failure.
- macOS uses a compatible subclass of the badge's existing native window. Passive mode vetoes key/main eligibility and enables `ignoresMouseEvents`; calibration calls the original eligibility implementations. The subclass is installed once and retained across mode changes, preserving AppKit KVO observers. The policy is removed and the retained NSWindow released during shutdown. The rail's class and input/focus policy remain untouched.
- Windows retains layered/transparent/no-activate extended styles, restoring original affected bits during calibration. No Windows physical validation is claimed.
- Badges remain nonhit-testable and nonfocusable, without tooltips, popup windows, or pointer handlers. CardOverlayWindow remains topmost, transparent, and ShowActivated=false.

## macOS resize constraint

The installed [Avalonia 12.1.3 managed backend](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Native/WindowImplBase.cs) has an empty `BeginResizeDrag`. Its [native appearance code](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/native/Avalonia.Native/src/OSX/WindowImpl.mm) removes the resizable bit for borderless (`None`) windows. The adapter therefore temporarily selects `BorderOnly` in calibration, which enables AppKit's native resizable frame while keeping full-size content. Passive mode restores `None`.

Use the outer window frame on macOS. The eight custom targets invoke the requested API, but their interior area cannot initiate a drag through the current macOS implementation. This is not claimed as a complete custom-handle fix. An upstream/backend implementation is required to satisfy that behavior without manual resizing.

## Verification

- `dotnet build DraftTG.sln`: zero warnings, zero errors.
- `dotnet test DraftTG.sln --no-build`: 402 passed, zero failed/skipped. All previous 383 cases retained; 19 added. Totals: Domain 42, Data 92, ArenaIntegration 94, Application 114, RecommendationEngine 6, App 54.
- Shared tests cover the actual XAML's eight mappings/hit targets, manual window sizing, passive badge properties, fake native mode contracts, resized/moved geometry persistence across display origins/scales, reopening, unsaved geometry isolation, and invalid geometry rejection. They do not manufacture physical pointer drags.
- A local macOS native smoke harness exercised passive → calibration → passive → calibration → passive, including native show/hide and reapplication after Show. Every passive state reported ignores=true/key=false/main=false. Every calibration state reported ignores=false/key=true/main=true/resizable=true. The rail remained key/main eligible with its original native class. Transparency stayed Transparent, position stayed (180,180), and client size stayed 700 × 350. Disposal restored original key/main behavior and both windows closed without a native exception.
- An earlier smoke run caught a KVO observer exception when swapping subclasses per mode; the final implementation installs the subclass once. Both unshown and shown-window sequences subsequently passed.
- No physical Arena click-through, foreground-application/keyboard-focus retention, drag of any corner, or live Save/reopen acceptance was performed. Native getter checks do not establish those behaviors. Windows was not physically tested.

## Remaining physical acceptance

1. Restart the updated app. With Arena active, click a card underneath a badge; confirm Arena receives the click and keeps application/keyboard focus.
2. Open calibration. Move using the top strip; resize via each outer native corner (and edges), then Save and reopen. Confirm the same geometry. Move/resize again and Cancel; confirm saved geometry returns.
3. After calibration and after using a rail flyout, return to Arena and repeat the badge click/focus check.
4. Validate equivalent interaction on Windows.

No Arena parsing, 17Lands logic, recommendation scoring, ranking, inferred colors/archetypes, images, OCR, window detection, new settings system, or permissions were added. Phase 8 was not started.
