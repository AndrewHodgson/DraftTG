# Phase 9B.7 — Windows Graphics Capture

Implemented WGC as the default Windows backend. **Live P1P7 capture and unchanged
matcher pass 8/8. Full overlay acceptance and three consecutive picks remain
pending.** Phase 9C has not begun.

## GDI audit and failure

The original implementation is retained as `GdiBitBltFrameCapture` in
`src/DraftTG.App/Platform/ArenaRegionCapture.cs`.

| Detail | Audited behavior |
| --- | --- |
| Source | `GetDC(arenaHWND)`, Arena client DC, not the desktop DC |
| Destination | New compatible memory DC and new top-down 32-bit DIB section on every request |
| Copy | `BitBlt(memory, 0, 0, crop.Width, crop.Height, clientDC, crop.X, crop.Y, 0x00CC0020)` |
| Flags | `SRCCOPY`, without `CAPTUREBLT` or `PrintWindow` |
| Coordinates | Source x/y are normalized-region-derived client pixels. `ClientToScreen` origin is used for output/occlusion geometry, not added to source coordinates |
| Conversion | New managed byte array and new opaque BGRA Skia bitmap per request |
| Reuse | No reused destination bitmap, managed image, saved PNG, or pixel cache |
| Clearing | DIB is not explicitly cleared. A successful full-region BitBlt is relied upon to replace all requested pixels |
| Ownership | Encoding uses the newly acquired owned image before disposal. Debug save never substitutes a previous bitmap. Native bitmap/DC cleanup is in `finally` |
| Validation | HWND/process/client geometry/DPI and covering-window checks before/after copy; failure has no published image |

No stale-destination, coordinate, ownership, or accidental-old-PNG implementation
bug was found. A new destination and recent completion timestamp do not prove
the source DC reflects Unity's current GPU output. The supplied P1P7 PNG has
SHA256 `0524FEC7B15BC8699A81400F2A3677DB3DE33D05E86CB1956344672A4C2ACCA1`,
identical to the earlier nine-card pixels: nine old candidates match 9/9, while
eight current candidates match 0/8. New WGC pixels below show the actual eight
cards and match 8/8 without matcher changes. This supports replacing the source
acquisition method; it does not identify a particular Unity/DWM driver defect.

## Architecture and resource lifetime

`IWindowFrameCapture` / `IArenaRegionCapture` keep native details out of the
localization/application models. `ArenaRegionFrame` carries owned pixels, HWND,
dimensions, screen origin/DPI, UTC completion timestamp, window epoch,
acquisition request counter, requested pack generation, backend, duration,
pixel hash, and optional WGC presentation time. The shared coordinator stamps
request/pack tokens; the existing synchronizer computes/validates image evidence.

`WindowsCaptureFactory` defaults to `PreferredArenaFrameCapture` with
`WindowsGraphicsCaptureFrameCapture` primary. HWND discovery and validation
reuse the existing Arena locator. The production localization session, Retry,
and explicit debug save all use the same composed backend and coordinator.

Each request runs on a serialized asynchronous worker:

1. Validate Arena HWND/process/visibility/client geometry and calibrated crop.
2. Initialize the worker's MTA apartment, hardware D3D11 BGRA device and projected
   `IDirect3DDevice`.
3. Create `GraphicsCaptureItem` for that HWND through
   `IGraphicsCaptureItemInterop.CreateForWindow`; no picker or desktop item.
4. Map client coordinates to the WGC window surface using verified DWM extended
   frame bounds or window bounds. Require dimensions to match exactly. No guessed
   resize/aspect conversion; non-client offsets are included explicitly.
5. Create a free-threaded two-buffer `Direct3D11CaptureFramePool` and
   `GraphicsCaptureSession`; start capture and wait asynchronously for an event.
6. Reject a presentation QPC timestamp older than the request's start, changed
   content size, or changed HWND/client/DPI/surface geometry.
7. Copy only the calibrated crop from the window GPU texture to a staging
   texture, map CPU readback, and copy row-pitch-aware BGRA pixels to a new owned
   Skia bitmap. The whole window is transiently present on the GPU because WGC
   creates a window item; no whole-window CPU image or desktop image is saved.
8. Close session/pool, unsubscribe events, close the projected device, release
   native textures/device/context/ABI references, and balance apartment setup in
   `finally`, including partial initialization, cancellation and failures.

The capture item has no `IClosable`; its SDK projection owns the COM reference.
Size/window/DPI/monitor changes abort that acquisition; subsequent bounded
requests create fresh resources. Device/Map failures likewise dispose resources
and fail safely; no old CPU image is reused. The shared default acquisition
timeout remains two seconds (comparison command uses five). No game input,
process injection, game memory access, OCR, or remote image processing was added.

Phase 9B.6 is preserved: immutable pack snapshot/token, visual settle delay,
request counter, cancellation, previous-hash/confirmed-art occupancy defense,
three stale-frame attempts, and result/revision/window guards. Identical pixels
after a card-count transition remain rejected for WGC and GDI. Manual matching
remains available. Normal frames stay in memory; saves require the existing
explicit debug action, explicitly configured debug output, or developer command.

## Requirements, dependencies, indicators

Direct HWND creation requires Windows 10 1903 / build 18362 or newer;
`CreateFreeThreaded` is available from 1809. The Windows App/App.Tests target is
`net10.0-windows10.0.19041.0` with `SupportedOSPlatformVersion=10.0.18362.0`.
This states the WGC API floor; deploy on a Windows release supported by the
installed .NET 10 runtime. Cursor capture is disabled only where build 19041+
supports that API. Runtime capture support is checked. See Microsoft's
[HWND interop requirements](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
and [free-threaded frame-pool API](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded).

No new third-party capture framework or explicit NuGet PackageReference was
added. The Windows TFM implicitly provides Microsoft.Windows.SDK.NET.Ref
10.0.19041.57, Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll. A small
App.Platform bridge uses native D3D11/DXGI/WinRT interop and verified Windows SDK
ABI declarations for surface readback. Windows code is compiled under `WINDOWS`;
other platforms retain net10.0 and manual fallback. Portable compilation was
verified on this Windows host; physical macOS capture remains future work.

The desktop HWND path does not request a capture picker, elevation, borderless
permission, or system-setting change. Windows policy/protected content can
still prevent capture. The standard capture indicator/border is left enabled;
the one-shot session ends after readback. See Microsoft's
[screen capture documentation](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture).

## Backend policy, diagnostics and developer comparison

Default: WGC, with automatic GDI fallback **disabled** because of the established
stale-source failure. `DRAFTTG_ALLOW_GDI_FALLBACK=1` opts into GDI only after a
typed WGC initialization failure. Timeout, cancellation, runtime/device failure
never silently switch to GDI. `DRAFTTG_CAPTURE_BACKEND=gdi` explicitly selects
the diagnostic fallback; `wgc` selects primary; invalid values are rejected.
Fallback diagnostics retain the reason. Both paths keep freshness/manual guards.

Rail diagnostics show actual capture backend, focused automatic-placement
availability, initialized/running state, last received timestamp/dimensions,
raw pixel hash, pack/request/window generations and capture/matcher duration.
Saved text additionally includes frame HWND, UTC capture time, WGC QPC time,
expected candidate identities and paired PNG SHA256. Debug save bypasses the
matcher and uses the same production backend.

Developer comparison, with Arena already showing the supplied manifest's pack:

```powershell
dotnet src/DraftTG.App/bin/Debug/net10.0-windows10.0.19041.0/DraftTG.App.dll --compare-arena-capture --manifest artifacts/p1p7-exact-artifact-audit/manifest.json --output artifacts/phase9b7-live-comparison --pack-generation 2
```

The command reads existing calibration, validates one Arena HWND, captures WGC
then GDI sequentially, and saves unique crop/log pairs and hashes. This is not
an atomic simultaneous snapshot; manifest/generation are developer-supplied and
must match the live state. A failed backend produces a failure log and no PNG.
GDI's privacy/covering-window check is retained. No screenshots were uploaded.

## Physical capture and matcher results

The first sandboxed developer invocation could not enumerate desktop windows.
The approved desktop-access invocation succeeded with Arena non-foreground:
PID 42472, HWND 0x140C8E, client 2560 x 1440, DPI 1.0, both integrity levels Medium.

At **2026-10-03 22:42:03 UTC / 17:42:03 CDT**, WGC initialized, received a frame,
and delivered the calibrated 1325 x 1131 crop in **432.5 ms**. Inspection shows
exactly Season of Growth, Rimefur Reindeer, Freeze in Place, Misleading Motes,
Voracious Vermin, Harried Spearguard, Skewer Slinger and Evolving Wilds in that
visual order. The old Hatching Plans / Scarecrow Guide pack is absent.

Saved evidence:

- `artifacts/phase9b7-live-comparison/wgc-P1P7-gen2-cap1-20261003-224203-097Z-c229436cd9f648b4b3fdddcbb3077153.png` and matching TXT
- PNG SHA256: `B744651BB9FFF71B2EC57FC532D6EC3A5D0C3446CE77DCF08254F3756B147EC7`
- Raw opaque BGRA SHA256: `B3E4F53C4AF8F62BBC1BDA83F174093F1A03B0C6DCE23773C44B989F158B0F81`
- Complete per-slot rectangles/best/second/assignment/rejection evidence:
  `artifacts/phase9b7-wgc-matcher/scores.txt` and `scores.json`

| Visual slot | Correct identity | Similarity | Result |
| --- | --- | ---: | --- |
| 1 | Season of Growth | 0.984399 | accepted |
| 2 | Rimefur Reindeer | 0.968524 | accepted |
| 3 | Freeze in Place | 0.977841 | accepted |
| 4 | Misleading Motes | 0.980972 | accepted |
| 5 | Voracious Vermin | 0.977048 | accepted |
| 6 | Harried Spearguard | 0.980258 | accepted |
| 7 | Skewer Slinger | 0.986069 | accepted |
| 8 | Evolving Wilds | 0.995402 | accepted |

Every best candidate and assigned identity equals the expected visible card;
all eight pass the unchanged **0.94** threshold and **0.07** margin. This
establishes that acquisition solves the supplied case without a matcher change.

A second capture using the final backend at 22:45:52 UTC also shows the current
eight cards and matches **8/8**; raw hash
`A0BC30910E304F6465C6F024D950ABFCC3184710AEA602FE6412ACA0A0EADF66`.
Animated background pixels changed while identities/scores remained stable.
See the later paired files and `artifacts/phase9b7-wgc-matcher-final`.

On both comparisons, GDI refused acquisition because HWND 0x209DA covered the
draft region. Its logs record that reason. **No simultaneous current-WGC /
stale-GDI claim is made for this run.** The pre-existing stale GDI evidence and
these current WGC captures are distinct observations.

## Checks and remaining acceptance

`dotnet build DraftTG.sln`: **zero warnings, zero errors**.
`dotnet test DraftTG.sln --no-build`: **687 passed, zero failed/skipped**.
Breakdown: Domain 42, RecommendationEngine 102, Application 135, Data 134,
ArenaIntegration 94, App 180. Original 672 tests remain passing.
`dotnet build DraftTG.sln -p:DraftTGPortableBuild=true` also passed without
warnings/errors; the final normal Windows target was rebuilt afterward.
Final separate TRX files are in `artifacts/phase9b7-tests/final`.

Fifteen focused additions: primary preference; explicit/invalid selector;
default initialization failure; opt-in initialization fallback/reason;
runtime/device/timeout no-fallback; canceled initialization; frame metadata;
wrong pack token/disposal; identical changed-count pixels for either backend;
production/debug backend sharing and text metadata; failed primary hides prior
badges/manual fallback; late primary result discarded after pack advance;
non-client/DPI crop mapping; invalid surface bounds; actual current eight-card
WGC regression. The exact privacy-safe crop, manifest and eight local references
are retained in `tests/Fixtures/arena-wgc-p1p7`.

Matcher, reference cache and Phase 9B.6 synchronizer source SHA256 values are
unchanged from the start of this phase:

- CardTemplateRecognition.cs: `AF3050C2750FDE4A8866424910137390E6CDFEBFE48A662D5090C9BB2F0A3F26`
- PackFrameSynchronization.cs: `93DA7E36C876598E763BDDC290434ADC185AA814E279000B39BB07B96FC0310B`
- ScryfallVisualReferenceCache.cs: `B68345C06759F6D8768E765F49472426D046348347C0333606DB6CC7A9A7A790`

Phase 8/9A/9B scoring, providers, statistics association and Arena parser were
not edited. Manual mapping remains intact. No Phase 9C work.

**Remaining physical acceptance:** the rebuilt rail's automatic placement and
Save action must be observed across at least three actual consecutive picks,
verifying current card counts and no prior-pack pixels. **Zero picks were made
in this turn.** Computer Use initialization failed with `failed to start Node
runtime: The system cannot find the path specified. (os error 3)`, and failed
again after resetting the runtime. This prevents UI actions/visual badge
inspection; it did not prevent the purpose-built read-only capture command.
The single-pack backend + offline matcher are physically verified, while UI
placement, transitions, device-loss and moved/resized/HDR hardware behavior
remain unverified. Keep the phase's broader acceptance pending.
