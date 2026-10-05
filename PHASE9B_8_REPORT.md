# Phase 9B.8 — WGC long-running draft capture reliability

Implemented and automated/native-fixture verified on Windows, 2026-10-04. Actual Arena low-card acceptance remains pending. Phase 9D is unstarted.

## Findings and the historical live failure

The exact native cause of the reported late-draft failure was **not reproduced**. Its summary has no failing operation, exception or HRESULT. The newest matching files in `%TEMP%/DraftTG` are still the successful GDI P1P7 eight-card pair saved at `2026-10-03T22:16:11Z`; they do not document this WGC failure. No current four-card failure pair was available.

Three concrete problems were found and addressed:

1. **Cancellation-source lifetime.** The coordinator disposed its linked cancellation source when `WaitAsync` timed out, although the native task could still use `token.WaitHandle` during delayed initialization or cleanup. A regression exercises that delayed access. The source now lives until the underlying task completes. Late frames are disposed.
2. **Native resource churn.** The first instrumented native probe captured 100 frames but accumulated handles with a fresh D3D device/item every request. Device reuse greatly reduced growth. A longer 1,000-frame run exposed remaining growth associated with repeated capture-item creation. Reusing the item for unchanged, validated window geometry removed that trend in the fixture. These measured resource problems do not prove the historical failure's cause.
3. **Diagnostic loss and races.** Pack/window resets erased the prior error and successful-frame history. Old and new acquisitions in one geometry generation could also update the same diagnostics. History is now retained; request-generation guards reject old callbacks. Teardown preserves the primary error rather than replacing its operation.

`source initialized: no` is a marker for the current acquisition, not proof that WGC has never worked. Reset previously cleared that marker and the current frame to `NEVER`, even after successes. State, last successful frame across resets, and last failure now distinguish those cases.

## Lifecycle, concurrency and recovery

Before: each request used a thread-pool worker, initialized its apartment, created a device/item/pool/session, subscribed to frame arrival, captured and tore down. Caller timeout could dispose the token source before native cleanup completed. Geometry-generation guards did not separate concurrent acquisitions within that generation.

After:

```text
Auto / Retry / debug save / geometry reset
  -> shared coordinator: cancel/replace and fence old request callbacks
  -> ownership gate: wait through prior native cleanup
  -> dedicated MTA worker
  -> validate HWND; reuse healthy device/item for unchanged geometry
  -> fresh frame pool/session; StartCapture
  -> poll for a post-request QPC frame; regional readback
  -> dispose frame/session/pool; release ownership
  -> deliver frame or focused failure
```

The backend owns one stable MTA and at most one retained D3D device/capture item. No session or pool remains running between requests. Creation, use and native teardown run on the owning worker. A second request cannot initialize before the first finishes cleanup, even after caller timeout. Canceling a queued request cannot dispose the active owner's resources.

Native initialization/runtime faults close partial sessions/pools, clear the item and dispose the device. Invalid HWND validation clears retained resources too. Changed window geometry requires a new item. Every request validates HWND before initialization and geometry during acquisition. Shutdown drains owned work, clears retained resources and uninitializes the apartment without blocking UI shutdown on a native request.

`GraphicsCaptureItem` has no `Close`/`IClosable`; its projection owns/releases its COM reference. There are no frame-arrived/item-closed subscriptions to detach. Polling `TryGetNextFrame` uses a cancellable 10 ms wait. Microsoft's [screen capture guidance](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture) supports polling and returning frame buffers by disposal. Projection references follow [C#/WinRT ownership](https://github.com/microsoft/CsWinRT/blob/master/docs/interop.md); projection caches are not forcibly released.

The regional readback bridge is unchanged: map/unmap are paired and staging texture, texture/interface references and surface ABI references are released in `finally`. Frames are disposed on every path. A captured bitmap is disposed if teardown or delivery fails. All teardown releases are attempted; secondary cleanup errors are traced without hiding the primary error.

States are `Idle`, `Queued`, `Initializing`, `Capturing`, `FrameReady`, `Disposing`, and `Faulted`. The backend snapshot describes the current owner; the coordinator can show a new request queued behind it. A fault cannot poison the next request. Reset/retry is available; fresh requests recreate failed retained resources and always create fresh sessions/pools.

Automatic placement and debug save share the coordinator/backend. Replacement cancels the old request deterministically and waits for cleanup. Its cancellation source remains alive until the underlying task finishes. Request and geometry fences prevent old stage callbacks or frames from changing the replacement request.

Retry rediscovers HWND and compares old/new geometry. A changed HWND invalidates the old generation. Immediate native HWND validation failure reports the previous handle and rediscovery result; a different rediscovered handle is used by a fresh request rather than by an old-geometry request.

Capture receives geometry, region and generation, **no card count**. A session regression obtains frames and invokes an injected recognizer for packs from 14 down to 1. This verifies orchestration, not physical low-card recognition. Confirmed/manual placement remains preserved, and unconfirmed automatic badges remain hidden after faults. Explicit Retry can restore automatic placement.

## Paths to `source initialized: no` and diagnostics

| Condition | Specific diagnostic |
| --- | --- |
| Initial state, pack/window change or reset | Idle/reset stage; retained previous success/fault |
| Platform implementation unavailable | Platform unavailable; manual positions available |
| Arena process/HWND absent, hidden or minimized | Discovery result and explicit waiting reason |
| Missing/editing/out-of-client calibration | Calibration wait or invalid crop/calibration error |
| No current pack | Waiting for current draft pack |
| Prior work still unwinding | Waiting for cleanup; backend ownership/state |
| Confirmed placement/manual edits pause auto capture | Explicit paused reason |
| Bounded attempts exhausted or retry delay active | Explicit exhaustion/delay reason; Retry available |
| Acquisition waiting for native ownership | Queued and preceding-request cleanup stage |
| Worker apartment initialization/dispatch fails | Exact operation/type/HRESULT/message; zero owners |
| Support/item/size/device/pool/session/configuration fails | Distinct API operation/type/HRESULT/message; partial cleanup and recoverable fault |
| Cancellation before configuration completes | Canceled/stale request; cleanup before replacement ownership |
| Timeout before configuration completes | Timeout with last operation; token valid through cleanup |

`Session StartCapture`, frame waiting and texture readback are distinct runtime stages after the source-initialized marker. Without an intervening reset, that marker is `yes` when one of these later operations fails. Recognition requires a valid fresh frame first.

The rail/text diagnostic adds coordinator/backend state, request owner count, retained device/item counts, previous/current HWND, retained last successful timestamp, and last failure's operation, exception type, HRESULT, message and request. Initialization sub-stages, StartCapture, frame waiting, texture readback and disposal are distinguishable. No full stack traces appear in normal UI. Request-local frame fields still reset on transition so old frames cannot appear current.

## Actual native stress evidence

The explicit command creates/closes its own ordinary capturable window, sends no input to Arena, runs no recognition, downloads no references, and discards frames. Diagnostics/resources are saved locally:

```powershell
dotnet src/DraftTG.App/bin/Debug/net10.0-windows10.0.19041.0/DraftTG.App.dll --stress-wgc --fixture --cycles 100 --output artifacts/wgc-stress
```

Each cycle changes coordinator/request/pack generations, validates the window, obtains/disposes a frame and reuses the backend. The command accepts 1–1,000 cycles. It stops after five consecutive focused failures. Failed captures or resource guards make it fail.

| Probe | Frames | Collected handle samples | Guard |
| --- | ---: | ---: | --- |
| First instrumented per-request device/item | 100/100 | cycle 10: 507; cycle 100: 701 | Failed |
| Device reuse, fresh item per request | 1,000/1,000 | cycle 10: 592; cycle 1,000: 797 | Failed |
| Control: same harness/token/fixture, WGC disabled | 1,000/1,000 | cycle 10: 324; cycle 1,000: 332 | Passed |
| Device and validated item reuse | 1,000/1,000 | cycle 10: 593; cycle 100–700: 600; cycle 1,000: 597 | Passed |
| Final Windows binary, 100-cycle run | 100/100 | cycle 10: 593; cycle 100: 595 | Passed |

Removing event subscriptions or using the stable MTA alone did not remove the original growth. Device/item reuse produced the resource improvement. The extended item-reuse run's private bytes were 70,049,792 at cycle 10 and 72,167,424 at cycle 1,000. Final 100-cycle values were 69,091,328 and 69,603,328. Native frames were valid 755 × 529 regional images; every post-request owner count was zero. Final diagnostics showed `FrameReady`, one retained device/item, source initialized `yes`, session running `no`.

The harness collects managed projections/finalizers every ten cycles and briefly allows asynchronous native release before sampling. **Only the developer harness forces GC; production does not.** Its unchanged guard allows up to 64 handles and 64 MiB private-byte growth between collected samples after warmup, with zero owners after each request. That guard rejected the growing device-only 1,000-cycle run. The final extended trace levels off. Finite fixture success is not proof of indefinite production reliability. Retained result strings account for increasing managed bytes in long probes.

Evidence in ignored `artifacts/phase9b8-validation/` includes `native-stress/wgc-stress.json`, `native-stress-extended/wgc-stress.json`, `noop-resource-probe.json`, `native-stress-item-reuse/wgc-stress.json`, and `native-stress-final/wgc-stress.json`. No Arena screenshots were captured by these probes. The control probe source stays in ignored artifacts.

## Focused tests and builds

Fourteen additions in `tests/DraftTG.App.Tests/WgcLifecycleTests.cs` cover:

1. 100 scoped requests dispose resources/frames and pass harness checks.
2. Partial initialization cleanup, exact operation/HRESULT and recovery.
3. Worker initialization failure before scope creation, diagnostics and retry.
4. Cleanup failure disposes returned pixels and releases ownership.
5. Secondary cleanup failure preserves the original acquisition error.
6. Concurrent requests wait through preceding disposal.
7. Queued cancellation preserves the owner and next request.
8. Timed-out workers retain a usable token wait handle until cleanup ends.
9. Old callbacks cannot overwrite same-generation replacement diagnostics; late pixels are disposed.
10. Pack/geometry resets retain last fault/success.
11. Retry rediscovers HWND and reports both handles.
12. Frame/localization orchestration across all fourteen card counts.
13. Debug save replaces automatic capture after cleanup and saves a fresh pair.
14. Retry after fault restores placement while preserving manual safety.

Injectable unit scopes do not prove native COM behavior; the actual fixture probes exercise native ownership/reuse separately.

| Suite | Passed |
| --- | ---: |
| Domain | 42 |
| RecommendationEngine | 116 |
| Application | 139 |
| Data | 138 |
| ArenaIntegration | 94 |
| App | 196 |
| **Total** | **725** |

All original 711 tests plus 14 additions pass on Windows and portable targets. Both builds have zero warnings/errors:

```powershell
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
dotnet build DraftTG.sln -p:DraftTGPortableBuild=true
dotnet test DraftTG.sln --no-build -p:DraftTGPortableBuild=true
```

The normal Windows build was restored after portable validation. Forty protected baseline file hashes have zero changes, including recommendation/domain/provider/parser sources, WOE profile, matcher and synchronization. Recommendation formulas, archetype confidence/lead, pair-data behavior, references, .94 threshold, .07 margin, fine alignment and geometry remain unchanged. The readback bridge is unchanged.

## Files changed

Added: `Platform/WgcCaptureLifecycle.cs`, `Platform/WgcCaptureWorker.cs`, `Platform/WgcStressFixture.cs`, `WgcStressCommand.cs`, and `WgcLifecycleTests.cs`.

Updated in App: `ArenaCaptureCoordinator.cs`, `AutomaticCardLocalizationSession.cs` (waiting diagnostics only), `Platform/WindowsGraphicsCaptureFrameCapture.cs`, `Platform/PreferredArenaFrameCapture.cs`, and `Program.cs` (stress command). Architecture/roadmap/report documentation records the behavior.

## Remaining physical Arena acceptance

| Cards | WGC/fresh dimensions/localization/badges observed in this phase |
| --- | --- |
| 5 | Pending |
| 4 | Pending; no current four-card PNG saved |
| 3 | Pending |
| 2 | Pending |
| 1 | Pending |

The computer-use runtime failed to initialize with `os error 3`, before desktop inspection. The developer-owned fixture could run directly but cannot establish Arena badge placement. No Arena actions or draft picks were made. User-confirmed completion of Phase 9B.7 and Phase 9C is retained; it does not substitute for this new acceptance check.

Validate the rebuilt app at each low-card state: initialized WGC, fresh nonzero regional frame, invoked localization, and automatic badges if recognition succeeds. Save a synchronized four-card debug pair. If capture succeeds and recognition fails, diagnose that pair separately. **No separate low-card matcher issue was discovered**, and recognition remains unchanged. Stop after Phase 9B.8.
