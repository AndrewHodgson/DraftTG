# Phase 9B.5 — Windows Arena capture runtime

The Windows runtime fix and diagnostics are implemented. **Live acceptance is pending.** All 652 automated tests pass and the solution builds with zero warnings/errors. No real captured Arena pixels, live automatic placement, privilege comparison or consecutive picks were verified in this session.

## Original runtime trace and root cause

The shipped path was:

`RailPanelView` Retry button → `OverlayDesktopSession.OnAction` → `AutomaticCardLocalizationSession.RetryManually` → invalidate placement / schedule Retry → metadata timer → `FindWindow` → compare the entire geometry record → foreground gate → load references → synchronous Windows regional capture → existing artwork matcher → guarded placement publication.

The exact source-confirmed stall was in `AutomaticCardLocalizationSession.Tick`: `if (window is null || !window.IsForeground) { ...; return; }`. Clicking the interactive rail gives the rail focus. A found, visible Arena window then fails this condition before reference loading or the capture API is called. Retry only changed the pending flag and message; it did not directly acquire a frame. Since foreground status participated in record equality, focus changes also incremented the attempt revision and restarted the settling delay. The Windows backend independently required foreground status and compared complete records after copying, rejecting otherwise unchanged frames on focus changes.

This explains the reported Waiting/Retry requested pattern when the rail remains active. It is a reproducible code defect and the primary fix. It does **not** establish that Arena's actual GPU-rendered pixels are compatible with the GDI backend; that is a separate live check.

A secondary lifecycle defect was possible: localization startup occurred after awaited calibration loading and its revision guard. A superseded load could return before starting the localization timer. Startup now begins before that I/O and reports its missing prerequisites.

## Exact capture technology

The implementation remains Win32 GDI:

`GetDC(Arena HWND)` → `CreateCompatibleDC` → `CreateDIBSection` (top-down BGRA32) → `SelectObject` → `BitBlt(..., SRCCOPY)` → copy the bounded regional bitmap into `SKBitmap`.

There is no Windows.Graphics.Capture item, D3D graphics device, frame pool, FrameArrived callback, Desktop Duplication or PrintWindow in this backend. A frame is delivered by the asynchronous capture task returning the bitmap. Diagnostics explicitly name this backend rather than suggesting an absent callback/session might be stuck.

Microsoft describes BitBlt as a rectangular device-context copy and documents its failure return; it does not document a foreground prerequisite. Removing the application gate is appropriate for a visible, unobstructed region. This is an inference about the application policy, not a guarantee about Arena rendering. [BitBlt documentation](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt).

## Updated production flow

`Retry automatic placement` → explicit HWND rediscovery/restart → current geometry/region prerequisites → coordinator fresh-frame request → asynchronous GDI regional acquisition → frame generation validation → crop dimensions validation → reference preparation → **unchanged** CardTemplateRecognizer → guarded result publication.

- Retry resets the attempt revision, cancels prior work, rediscovers HWND and restarts capture generation. It starts acquisition immediately when prerequisites exist. Retry during active work starts after the previous attempt observes cancellation; it does not need a focus transition.
- Missing process/window/calibration/current pack, hidden/minimized window and editing calibration have specific prerequisite stages. Capture failure produces Manual fallback with its reason.
- Capture completes before reference loading. A slow/missing reference cannot hide whether a regional frame arrived.
- The timer observes metadata every 500 ms; it is not a continuous image-capture loop. Automatic attempts retain the existing three-attempt limit and two-second retry spacing.
- Manual confirmation and manual edits remain available and survive capture failures/explicit Retry. Existing publication guards prevent asynchronous matching from overwriting manual work.

## HWND and integrity audit

The old locator trusted the first MTGA process's MainWindowHandle. That was not verified to be the rendering window.

The new locator uses EnumWindows and MTGA PID association, reports HWND/PID/title/class, accepts top-level unowned UnityWndClass candidates, prefers visible non-minimized usable client geometry and refuses ambiguous multiple eligible rendering windows. Hidden/minimized candidates remain diagnostic observations instead of silently becoming a missing process. Helper/owned/class-mismatched windows are reported when no rendering candidate exists. HWND/process ownership, visibility and client geometry are checked again around capture. Retry does not indefinitely retain an old handle. [EnumWindows documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows).

Actual game-window selection is **not physically verified**. The Unity class constraint is conservative; a different future window class, multiple Arena instances or a recreated window can produce a focused discovery diagnostic rather than guessing.

The locator reads TokenIntegrityLevel for Arena and DraftTG using limited process/query-token access. Diagnostics show Medium/High/etc. or an explicit unknown/native-error reason. Different/unavailable levels are informational; they do not gate acquisition or require Administrator. Actual live integrity levels and their effect remain **unverified**. The source-confirmed failure happens at the focus gate before a native copy, so there is no evidence that privilege caused this reported stall. [GetTokenInformation documentation](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation).

## Timeout, generations and coordinates

FreshFrameTimeout defaults to 2 seconds. Set `DRAFTTG_FRESH_FRAME_TIMEOUT_SECONDS` before starting DraftTG to a value from 0.1 through 30; invalid/unset values use 2 seconds. The acquisition wait does not block the UI thread. Timeout reports `Capture started but no frame was received within 2.0 s`, keeps last-frame state visible and prevents localization.

Each frame carries a capture generation. HWND/PID changes, meaningful client origin/size changes, DPI changes and explicit restarts increment it. Focus, title, integrity and one-pixel observation jitter do not. Old-generation deliveries are rejected/disposed. Post-delivery geometry is checked and stale placement revisions are rejected by the session/presentation boundary.

Task.Run moves GDI copying/conversion off the UI thread. Timeout/cancellation cannot forcibly interrupt a native call already inside Windows. Its resources unwind in finally; late frames are disposed. A semaphore limits the backend to one native acquisition at a time, including after timeout, so repeated retries cannot accumulate simultaneous GDI buffers. A permanently hung native call would cause subsequent requests to time out while waiting for that gate; process restart remains a recovery path.

ClientToScreen supplies the client origin in desktop pixels; GetClientRect supplies its client extent; GetDpiForWindow supplies scaling. Calibration's physical screen-normalized rectangle becomes an Arena-client normalized anchor. BitBlt source coordinates and bitmap dimensions are client/region pixels. Overlay position uses desktop pixels and overlay dimensions divide by scaling for Avalonia DIPs. Movement/resize follows the window-relative anchor; meaningful size/DPI changes invalidate old placement. [GetClientRect documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclientrect).

This backend intentionally acquires only the requested region. An invalid region must be rejected **before** copying, with `no regional frame was requested`; it does not copy the entire client merely to diagnose an invalid crop. A returned frame whose dimensions cannot produce the expected crop is separately reported as `Fresh frame received, but draft-region crop dimensions are outside the delivered capture bounds`. Last-frame time/dimensions remain present in that case, while localization remains uninvoked.

## Stage diagnostics and explicit debug capture

Status → Card placement diagnostics exposes:

- Stage and exact backend; process found, PID, HWND found/value, title/class.
- Visible/minimized/foreground (foreground is explicitly not required).
- Client origin/extent, DPI scale and both integrity levels.
- Capture initialized/running, generation and configured timeout.
- Last frame timestamp or NEVER, frame generation/dimensions and crop dimensions.
- Localization invoked/result (`matched x/y`).
- Exception type, HRESULT, Win32 native code when available and concise reason.
- Saved image and diagnostic-log paths.

Discovery/initialization/acquisition/conversion/crop/localization failures retain their reason and stage in this expanded panel. The passive badge content is unchanged. The first failed native API names itself in the exception; giant stack traces are not placed in the rail.

`Save current Arena capture for debugging` explicitly requests one fresh draft-region capture and bypasses artwork-reference loading and localization. On success it overwrites:

`%TEMP%\DraftTG\arena-draft-capture.png`

It also overwrites the full stage diagnostic in:

`%TEMP%\DraftTG\arena-draft-capture.txt`

The text log is saved even when capture fails. No image is claimed saved on a failed attempt; an existing PNG from an earlier success is not a new capture. Its path is reported only when the current attempt saves successfully. The text file supplies one complete log for the next failure investigation. These writes occur only on this explicit action. The prior `DRAFTTG_LOCALIZATION_DEBUG_DIRECTORY` option remains off unless explicitly configured. No screenshot upload or persistent capture logging is enabled by default.

Captures remain limited to an unobstructed Arena region. The backend rejects overlap by the rail/other windows and checks geometry/occlusion before and after copying. OS window movement can still race those checks. No full-desktop capture, game input, memory inspection or injection was added.

## Fallback decision

No evidence yet establishes that BitBlt itself is fundamentally unreliable for this Arena session: the original runtime stopped before calling it. Therefore no replacement capture framework was added blindly.

Windows.Graphics.Capture is the reasonable next fallback to benchmark if a real saved GDI crop is blank/stale despite valid dimensions. Microsoft's implementation uses a capture item, Direct3D frame pool and frame-arrival handling; it would require additional Windows interop/resources and live support checks. It can implement the same IWindowFrameCapture seam. It was **not benchmarked against Arena**, because physical inspection is blocked. [Windows screen capture documentation](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture).

PrintWindow was also reviewed: Microsoft documents that it is synchronous and delegates rendering to the owning application through print messages. That does not prove it will reproduce Arena's rendering, so it was not added as an unverified fallback. [PrintWindow documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow).

## Focused verification

15 new tests in `tests/DraftTG.App.Tests/ArenaCaptureRuntimeTests.cs` cover:

1. Actual rail Retry path acquires and invokes localization while Arena is not foreground.
2. Retry during an active acquisition rediscovers a replaced HWND and starts another capture.
3. Focus/one-pixel geometry jitter accepts the current generation.
4. Meaningful size/DPI changes cancel the old generation and accept a fresh one.
5. A delivered older-generation frame is rejected/disposed.
6. No-frame timeout is bounded and a late frame is disposed.
7. Missing process, missing HWND, minimized and hidden windows have distinct reasons.
8. Invalid crop fails before requesting regional pixels.
9. Received-frame/crop-dimension failure is distinct from missing frame; revalidation exceptions also dispose the received bitmap.
10. Initialization native failure retains stage/type/HRESULT/native code.
11. Bitmap-conversion failure retains source-initialization and failure stage.
12. Capture failure leaves manual selection/confirmation operational.
13. A successful synthetic crop runs the existing artwork matcher and publishes automatic placement.
14. Explicit debug capture saves one region PNG/log without invoking matching; a subsequent failed capture still writes the error log.
15. Explicit Retry preserves confirmed manual mapping when capture fails.

`dotnet build DraftTG.sln --no-restore`: **0 warnings, 0 errors**.

`dotnet test DraftTG.sln --no-build`: **652 passed, 0 failed, 0 skipped**.

| Project | Passing tests |
| --- | ---: |
| Domain | 42 |
| ArenaIntegration | 94 |
| Application | 135 |
| Data | 134 |
| RecommendationEngine | 102 |
| App | 145 |
| Total | 652 |

Final per-project TRX files are in `artifacts/phase9b-5/final-verification`; counters are in `artifacts/phase9b-5/test-summary.json`. These are fake-capture/synthetic checks, not GPU-stack simulations or physical acceptance evidence.

## Files changed and protected scope

New source/test files:

- `src/DraftTG.App/ArenaCaptureCoordinator.cs` — stages, generation, timeout and late-frame ownership.
- `tests/DraftTG.App.Tests/ArenaCaptureRuntimeTests.cs` — 15 focused regressions.

Existing source/UI files changed:

- `src/DraftTG.App/Platform/ArenaRegionCapture.cs` — rendering-window discovery, integrity diagnostics, asynchronous regional GDI capture, native error preservation.
- `src/DraftTG.App/AutomaticCardLocalizationSession.cs` — active Retry, frame-first pipeline, diagnostics, debug action and cancellation.
- `src/DraftTG.App/OverlayDesktopSession.cs` — diagnostic action dispatch and unconditional localization startup.
- `src/DraftTG.App/OverlayViewModel.cs` — separate capture-runtime diagnostic text.
- `src/DraftTG.App/RailPanelView.axaml` — expanded diagnostic binding and one-shot save button.

Documentation: this report, ARCHITECTURE.md and ROADMAP.md. Validation artifacts are under `artifacts/phase9b-5`.

Before/after SHA256 comparison of existing source/tests/global.json records only those five existing App files changed. The matcher, Application/Domain interfaces, all recommendation models, 17Lands/Scryfall providers, Arena parser, statistics mapping, passive CardOverlayWindow XAML, previous tests/fixtures and global.json are byte-identical. Existing uncommitted earlier-phase work was preserved. No Git index repair, commit or unrelated cleanup was attempted.

## Physical Windows validation result and remaining work

**Blocked, not accepted.** Read-only process inspection found MTGA running (PID 42472, Steam executable) before implementation. That proves process existence only, not a live draft screen or correct HWND/pixels.

The Windows computer-use runtime failed before initialization/window enumeration: `failed to start ... node ... BEFOREstartNode ... The system cannot find the path specified. (os error 3)`. Reset succeeded, but the subsequent initialization failed in the same way. It could not enumerate the displayed windows, inspect the draft, operate DraftTG's controls or observe a capture. No real draft-region image or matcher confidence output was obtained. No picks were made. The requested two-consecutive-pick validation is **not performed**.

To obtain the next decisive evidence, run the rebuilt app with Arena's draft visible and the rail clear of the calibrated region. Open Card placement diagnostics and click Retry. Then use Save current Arena capture for debugging. The resulting `.txt` log identifies the current process/HWND/integrity/acquisition/crop/localization stage; the PNG, if saved, proves the actual pixels being delivered. A single diagnostic log is sufficient to locate a remaining pre-matcher failure. If automatic placement succeeds, validate visible identities/known GIH-ALSA-rank values and two consecutive picks before accepting this phase. If a valid crop yields poor matching, retain the existing matcher and report its actual confidence/rectangle output first.

Remaining live risks include incorrect/future Unity class selection, multiple Arena windows, integrity-query access failure, occlusion, minimized/hidden windows, invalid calibration, DPI/monitor changes, GDI blank/stale DirectX content, native-call stalls, slow reference preparation and poor real artwork confidence. None was measured live in this session. Manual fallback remains available.

macOS remains deferred. A future backend must implement discovery, asynchronous bounded regional acquisition, generation/timestamp metadata and diagnostic failures behind the same seam, with its own permission handling. No Phase 9C work was started. Stop at Phase 9B.5 pending physical acceptance.
