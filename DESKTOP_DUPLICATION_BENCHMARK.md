# Phase 9B.10 — Desktop Duplication live reliability benchmark

2026-10-04. **Implementation and focused automated checks are ready; physical acceptance is incomplete. No production capture migration is justified by these results.** Keep the existing WGC selection and manual mapping pending pack-qualified live evidence. Phase 9D was not started.

## Measured live evidence

This report preserves the original Phase 9B.10 measurements. The subsequent Quick Draft `PickedCards` semantic correction is documented in [PHASE9B_10_2_REPORT.md](PHASE9B_10_2_REPORT.md), including the resumed watcher command. Its passing automated regressions do not replace the incomplete physical acceptance reported here.

One successful **capture-only** paired control ran on the actual Windows `Default` desktop at approximately **7:59 AM CDT**. Arena PID 27132/HWND `0x200E24`, visible and not minimized, client screen rectangle 0,0,2560,1440, scale 1.0. Foreground was false; acquisition still worked. The saved calibration produced a 1344×1105 crop at screen x=339, y=223.

| Metric | WGC | Desktop Duplication |
|---|---:|---:|
| Regional frame accepted by capture coordinator | 1 | 1 |
| Capture failures in this control | 0 | 0 |
| Dimensions | 1344×1105 | 1344×1105 |
| Total branch time, including hash processing | 405.6 ms | 323.3 ms |
| Backend capture processing | 338.3 ms | 284.0 ms |
| Initialization | 323.3 ms, stage-derived, including scheduling | 242.7 ms, native session timer |
| Image acquisition | 29.1 ms, stage-derived | 10.5 ms |
| Crop/readback | 16.3 ms, stage-derived | 26.1 ms |
| Acquired native frames | Not exposed by existing WGC diagnostics | 2 |
| Pointer-only frames skipped | Cursor excluded by existing backend; no corresponding counter | **1** |
| Old presentation timestamps skipped | Not exposed as a counter | 0 |
| First usable image, CDT | 07:59:00.575 | 07:59:00.531 |
| DXGI output | N/A | `\\.\DISPLAY1` |
| Matcher / badge application | Not invoked / not displayed | Not invoked / not displayed |

Initialization timing methods differ: WGC values come from coordinator stage transitions and include owner scheduling; DD has native operation timers. Do not sum them to infer GPU timing or compare a single cold-start sample as steady-state performance. No latency distribution or long-running reliability rate was measured.

Both pixel SHA256 values and 64-bit difference hashes were recorded. The SHA256 values differ; the acquisitions were independent and Arena can render between them. This is **outcome B: both acquisition policies accepted frames, different pixels**, not proof that both contained the same current pack. This control had no authoritative candidate snapshot and therefore used pack token zero, no matcher, and no identity/current-card-count claim. Pack-qualified localization requires nonzero pack/request tokens.

[Control records](artifacts/phase9b10-validation/live-control-2/benchmark.jsonl). No screenshot was persisted. An earlier control invocation stopped on JSON serialization of an IntPtr handle before capture; diagnostics now serialize the handle as hexadecimal text. That setup error is not counted as a capture-backend failure.

### Why the draft benchmark stopped

Two attempts to start the pack-qualified watcher replayed the existing Player.log through the unchanged `DraftSessionCoordinator`, parser, state engine and snapshot adapter. Each stopped with **`ArenaDraftStateConflictException`: `PickSubmission`, P1P3**, following a `MalformedOuterJson` diagnostic. A historical ready-pack observation was canceled during initial replay; it was never captured or matched as current.

The final attempt at about **8:02 AM CDT** records the precise conflict kind/coordinate without raw log text, draft UUIDs or credentials. The reader saw ten historical pick coordinates; **these are not ten physically benchmarked consecutive picks**. The command wrote zero qualified pack-comparison records and saved zero PNGs. Process exit zero means diagnostics were written, not that the live gate passed.

[Initial watcher records](artifacts/phase9b10-validation/live-watch/benchmark.jsonl), [final redacted watcher records](artifacts/phase9b10-validation/live-watch-redacted/benchmark.jsonl).

No semantic repair, alternate parser, stale candidate reuse, screenshot-derived semantic inference or recommendation change was performed. The conflict prevents this headless watcher from establishing the authoritative current pack. It does not prove a DD/WGC capture failure or establish the behavior of a previously running UI session.

### Required live matrix

| Current visible card count | Pack-qualified WGC/DD comparison | Matcher result | Physical badge placement | Latency for this state |
|---:|---|---|---|---|
| 14+ | Not tested | Not tested | Not observed | Unknown |
| 10 | Not tested | Not tested | Not observed | Unknown |
| 8 | Not tested | Not tested | Not observed | Unknown |
| 6 | Not tested | Not tested | Not observed | Unknown |
| 5 | Not tested | Not tested | Not observed | Unknown |
| 4 | Not tested | Not tested | Not observed | Unknown |
| 3 | Not tested | Not tested | Not observed | Unknown |
| 2 | Not tested | Not tested | Not observed | Unknown |

Current-pack comparisons: **0**. Physically benchmarked consecutive picks: **0**. Localization successes/failures: **unmeasured**. Actual manual fallbacks required: **unmeasured**. No matcher-threshold adjustment or inference from synthetic tests fills these gaps.

Deck-builder capture, secondary-monitor capture, monitor movement, non-100% physical DPI, spanning-window rejection on hardware and a complete draft remain untested. The native backend's secondary-monitor mapping and spanning rejection were checked with data-only tests; these are not physical monitor validations.

## Implementation

`DesktopDuplicationFrameCapture` implements the existing App-internal `IArenaRegionCapture`/`IWindowFrameCapture` boundary. DXGI, D3D11 handles and Windows DPI calls remain in App platform code behind `WINDOWS`; no Windows types were added to Application, Domain or RecommendationEngine. The production `WindowsCaptureFactory` and backend-selection settings are unchanged.

Files:

- [DesktopDuplicationFrameCapture.cs](src/DraftTG.App/Platform/DesktopDuplicationFrameCapture.cs): serialized request owner, independent retained native session, scoped physical-pixel inspection, cancellation and metrics.
- [DesktopDuplicationNativeSession.cs](src/DraftTG.App/Platform/DesktopDuplicationNativeSession.cs): standard D3D11/DXGI interop, output enumeration, acquired-frame ownership, bounded waits and regional staging copy.
- [DesktopDuplicationGeometry.cs](src/DraftTG.App/Platform/DesktopDuplicationGeometry.cs): output selection, physical-pixel crop mapping and pixel-update qualification.
- [PhysicalPixelContext.cs](src/DraftTG.App/Platform/PhysicalPixelContext.cs): per-monitor DPI contexts scoped to this process/thread.
- [CaptureBackendComparison.cs](src/DraftTG.App/CaptureBackendComparison.cs): developer shadow comparison with independent coordinators/synchronizers and structured records.
- [DesktopDuplicationBenchmarkCommand.cs](src/DraftTG.App/DesktopDuplicationBenchmarkCommand.cs): opt-in, headless, event-driven benchmark using existing semantic components and cached catalog data.

Existing `Program.cs` adds one developer command dispatch. `ArenaRegionCapture.cs` exposes its existing conservative regional occlusion check through an internal overload; its capture behavior is unchanged. No production UI/badge integration or automatic dual-backend migration was added.

### Monitor and coordinate handling

Enumerate attached DXGI outputs across adapters and select the **one output containing the entire Arena client rectangle**, not just the crop or whichever output was enumerated first. Create the D3D11 device on that output's adapter. Negative screen origins are supported. Spanning, ambiguous, offscreen and rotated outputs fail explicitly for this phase; HDR/non-BGRA formats are also rejected. Topology/window geometry changes invalidate the DD session rather than reusing the wrong output.

All Win32/DXGI geometry is physical screen pixels. The benchmark process establishes per-monitor DPI awareness; DD inspection/capture also scopes its thread to per-monitor v2 and restores the prior context. No Arena window/DPI state is changed. [Microsoft: thread DPI context](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreaddpiawarenesscontext).

Conversion is deterministic:

```text
saved Windows screen-pixel calibration -> normalized Arena-client region
client crop pixels = round(normalized region * client pixel dimensions)
output texture crop = client screen origin - output screen origin + client crop
badge/window DIPs, when a UI consumes the frame = physical pixels / frame.Scaling
```

WGC captures an outer window surface and already has its own client-to-surface mapping. DD maps directly to output coordinates. The benchmark shares the normalized client region and checks delivered dimensions; it does not assume identical WGC/DXGI surface origins. Data-only tests include a negative-origin secondary output at scale 1.5, without rescaling physical pixels a second time.

The monitor-wide resource remains on GPU. The first GPU-to-CPU copy is **only the calibrated region inside validated Arena client bounds**, at most eight million pixels. The complete desktop and full client are not copied to CPU as intermediates. A shared z-order/visibility guard runs before and after readback, refusing covering windows and potential self-overlay contamination. These are conservative checks, not a security boundary against every compositor effect.

### Frames, resources and freshness

An acquired frame counts as a desktop pixel update only if `LastPresentTime > 0` and `AccumulatedFrames > 0`. Pointer-only frames are counted, released and skipped; the next frame is awaited. Old desktop presentations before the current request QPC are also skipped. Protected masked content and uniform crops are refused. SDK frame metadata explicitly distinguishes pointer updates from desktop image updates. [DXGI frame metadata](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/ns-dxgi1_2-dxgi_outdupl_frame_info).

`AcquireNextFrame` waits at most 100 ms per OS call and checks cancellation between calls. The native request has a five-second ceiling; the existing coordinator's default two-second timeout normally cancels it earlier. Every successfully acquired frame is released; resource/texture references are released on success, skip and failure. One session/device/output and a reusable regional staging texture are retained while geometry/topology remain valid, then disposed on owner shutdown or fault. WGC has its own unrelated device/item/worker and failure state. [AcquireNextFrame](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutputduplication-acquirenextframe), [DuplicateOutput adapter/access requirements](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutput1-duplicateoutput).

Native metrics expose acquired frames, pointer-only skips, prior-presentation skips, first usable timestamp, output name, initialization/acquisition/crop times, stage and failure. Comparison records retain per-attempt DD metrics so synchronized retries do not hide skipped frames.

Pack-qualified requests use the existing `ArenaCaptureCoordinator` and `PackFrameSynchronizer`: configured visual settle delay, distinct capture/request generations, immutable pack generation, cancellation on transition, timestamp/geometry/dimension validation, and previous-pack hashes/confirmed art-region checks. Suspicious unchanged images on card-count changes are retried up to three times; they are never sent to recognition when freshness rejects them. SHA256 and an additional diagnostic difference hash are computed in memory. The difference hash is telemetry, not an acceptance threshold.

The unchanged `CardTemplateRecognizer` performs fine alignment, 0.94 acceptance, 0.07 ambiguity margin and known-pack one-to-one assignment. Shared reference-cache access is serialized during recognition to avoid evicting a bitmap while the other backend uses it. No matching source or thresholds changed.

The command captures only on semantic changes after a one-second initial replay debounce, plus the existing settle policy. It does not add a continuous high-FPS frame loop. FileArenaLogSource's existing polling is semantic monitoring, not capture polling. Capture-only is one explicit diagnostic request per backend.

### Comparison and fallback semantics

WGC and DD run concurrently for the same immutable request/window snapshot, with separate coordinators, synchronization evidence, generation counters and native owners. A failure in one does not restart the other. The pair is not an atomic same-compositor-frame snapshot.

Records classify A (both accepted, identical SHA), B (both accepted, different SHA), C (WGC failed, DD accepted), D (WGC accepted, DD failed), or E (both failed). “Accepted” means the implemented acquisition/freshness policy accepted it; physical current-card identity/count must still be verified. Actual current-pack matching is recorded separately. A stale pair is rejected after pack activation even if an earlier branch completed.

Shadow selection prefers WGC when accepted, otherwise DD, otherwise marks manual capture fallback required. **This selection is diagnostic and is not connected to production badge application.** The command records `SafePlacement` and detector/match counts when available, but explicitly marks badges as not displayed. Detector rectangle count is not independent human ground truth. Actual visible count/placement needs operator observation and a subsequent gated UI validation; a headless run cannot prove it.

If later measured evidence justifies a dual architecture, fallback must be automatic through the existing capture/localization boundary, preserve all generations/freshness checks, and hide uncertain badges when both fail. There is no normal-drafting capture API selector added here. Existing `Match card positions` remains untouched.

## Developer commands and privacy

Run from the repository root in a normal interactive Windows session on Arena's desktop, after saving the existing draft-region calibration:

```powershell
dotnet build DraftTG.sln

# One control; no semantic identity or matcher claim, no images saved.
dotnet src/DraftTG.App/bin/Debug/net10.0-windows10.0.19041.0/DraftTG.App.dll --benchmark-arena-capture --output artifacts/capture-control --capture-only

# Event-driven semantic watcher, once the existing monitor yields a valid live pack.
dotnet src/DraftTG.App/bin/Debug/net10.0-windows10.0.19041.0/DraftTG.App.dll --benchmark-arena-capture --output artifacts/draft-benchmark --duration-seconds 1200 --save-failures
```

The current Player.log conflict must be resolved in the existing semantic workflow before the second command can complete a pack-qualified benchmark; repeating it against the same conflicting replay is not a remedy. Fixing parsing/session semantics is outside this phase. A previously valid screenshot/fixture or historical pack must not be substituted as current.

Duration accepts 2–3600 seconds. `--capture-only` runs one pair and exits; otherwise the watcher ends on timeout/cancellation or an existing semantic-monitor failure. The command remains one process across observed packs and does not restart DraftTG to recover a capture. It runs headless and never focuses/navigates/picks in Arena. Operators make their own normal picks and verify visible count/order; UI badge-placement validation is separate from this shadow command.

Use a new output directory per run. `benchmark.jsonl` is appended; timestamps, process IDs, pack generations and record kinds must be used to separate runs. No images are saved by default. `--save-failures` explicitly enables local PNGs only after fresh, pack-qualified captures whose completed matcher identifies fewer cards than expected. `--save-images` explicitly enables all accepted debug crops. These flags are developer/debug behavior, not normal capture policy. Debug disk failures are recorded without poisoning the independent capture results.

Failed recognition crops are associated with their JSON candidate IDs/names, hashes and generation tokens for subsequent fixture preparation. No recognition failure fixture arose in this run. The existing exact-printing small-reference cache supplies matcher references; provider/statistics behavior is unchanged. Screenshots are never sent to a server. Ordinary captures remain memory-only; no broad desktop image is persisted. Local JSON can contain draft card identities and should be reviewed before sharing. Existing `artifacts/` ignore rules apply.

## Automated validation

**14 new tests; 739 tests total**, preserving the existing 725. Focused fakes exercise:

- primary shadow selection, secondary recovery and secondary failure isolation;
- both failures → manual capture fallback and no safe placement;
- independent pack/request generations and old-generation bitmap disposal;
- pack change during capture and post-comparison stale-result rejection;
- identical prior-pack pixels on card-count change, three retries and rejection;
- recovery without resetting the other backend;
- pointer-only/invalid image-update metadata;
- primary/negative-origin secondary output mapping, DPI-independent pixel coordinates and spanning rejection;
- old timestamps/wrong pack or request tokens;
- debug-save failure isolation.

These tests use capture fakes and data-only geometry; they do not emulate actual DXGI output duplication or count as physical acceptance. [Tests](tests/DraftTG.App.Tests/DesktopDuplicationBenchmarkTests.cs), [final TRX results](artifacts/phase9b10-validation/final-tests/), [739-pass summary](artifacts/phase9b10-validation/test-summary.json).

The required `dotnet build DraftTG.sln` completed with zero warnings/errors; `dotnet test DraftTG.sln --no-build` passed **739/739**, zero failures/skips. The portable App target also compiled with zero warnings/errors. A portable-branch method-group compile error was found and corrected; native DXGI/DPI implementation is excluded from that target. No Mac runtime validation or implementation is claimed.

Protected recommendation, provider, Arena parser/state, artwork matcher, WGC implementation, pack/frame synchronization and production factory files retain their existing source hashes; a before/after verification is stored under `artifacts/phase9b10-validation/verification.json`. Only the developer command dispatch and visibility-helper exposure modify existing App files; new capture/comparison code is opt-in.

## Production decision and remaining validation

**Provisional retention: existing WGC architecture, with existing manual fallback.** Evidence does not distinguish options A/B/C reliably. It also does not demonstrate that DD offers no improvement; the comparison is simply incomplete. No automatic dual-backend policy was wired into normal drafting.

Before selecting DD primary or an automatic fallback, complete all of the following:

1. Obtain a valid current semantic observation from the existing Player.log pipeline, without repairing it as part of capture work or reusing historical candidates.
2. Capture at least ten consecutive normal picks in one running benchmark process, ideally a complete pack through final cards; cover 14+, 10, 8, 6, 5, 4, 3 and 2 cards. Record qualified successes/failures, retry/stale-image rejections, pointer skips, matcher counts and real manual fallback use.
3. Verify actual visible card identities/count/order independently, and validate real badge placement under the existing safety rules. The headless eligibility result cannot substitute for this observation.
4. Compare WGC/DD on the same candidate snapshots, identify currentness rather than merely API success, and collect warm initialization/acquisition/crop/localization latency over time. Include WGC-failure recovery by DD if it occurs.
5. Physically test secondary-monitor movement, DPI scaling, occlusion/self-overlay handling, output/device loss and the focused spanning-window failure.
6. Observe a deck-builder frame when the user naturally opens it; verify deck/pool/list/tiles capture only, without deck suggestions or navigation automation.

Desktop Duplication is Windows-only. A future Mac adapter would use ScreenCaptureKit or another native capture API behind the existing boundary, with its own pixel/point/Retina mapping and screen-recording permission. Native capture availability does not establish complete-draft reliability. [Apple ScreenCaptureKit](https://developer.apple.com/documentation/ScreenCaptureKit?language=objc).

The implementation is available for further controlled testing. **Phase 9B.10 has not passed its live reliability decision gate.** Stop here with production selection unchanged; do not begin Phase 9D.
