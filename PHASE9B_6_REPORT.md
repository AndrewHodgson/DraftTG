# Phase 9B.6 — Pack/capture synchronization

Implemented synchronization defenses for automatic localization, Retry, and debug save. **Windows consecutive-pick acceptance remains pending.** Phase 9C is untouched.

## Root cause and evidence

The previous audit found a newly written 1325 × 1131 PNG identical to the old nine-card fixture while the latest draft payload contained eight different cards. The visible-nine candidate set matched 9/9; the latest eight-card candidate set matched 0/8 on those same pixels. See `artifacts/latest-live-audit/REPORT.md` for provenance and per-slot evidence.

The source audit identifies the freshness-policy gap precisely:

1. Arena log updates the draft snapshot and current pack presentations.
2. Overlay resets placement, creates a new `VisualPlacementContext`, and cancels the previous localization revision.
3. Automatic localization captures a request and calls the regional capture coordinator.
4. Coordinator's old generation identified window geometry/restarts, not the semantic pack or each individual acquisition.
5. BitBlt copies the window DC into a **new bitmap** and assigns a completion timestamp.
6. Geometry, dimensions and capture generation validate successfully even if that DC still contains old rendered pixels.
7. The matcher receives current candidates and those stale visual pixels. Publication revision checks cannot help: the request really is for the new pack.

There was **no cached `LastFrame` image reuse in either production path**. Debug save already requested a new BitBlt acquisition. Repeating the same pixels survived because copy completion was treated as proof of current visual content. The precise reason Arena/window-DC pixels remained old (visual timing, rendering surface freshness, or another live-state condition) cannot be established from the saved pair alone.

Automatic localization and debug save shared the vulnerable acquisition policy. Their exact acquisition paths are source-confirmed. The actual automatic 0/8 frame was not separately saved, so the audit does not prove it used the identical PNG bytes from the later debug action. The controlled offline 0/8 reproduction establishes the failure mechanism without inventing missing live evidence.

## Pack and capture identities

The existing immutable visual context is reused as the semantic pack generation: a monotonically increasing token, immutable `DraftPack` (pack/pick and ordered occurrence identifiers), and new `ChangedAt` timestamp. Statistics refreshes and same-pack Retry do not change this token. A presentation reset clears badges and cancels old work. Runtime scope is separately logged; the game draft session ID is not exposed by this presentation seam and is explicitly logged as unavailable rather than fabricated.

`PackFrameContext` binds that generation, change timestamp, layout, and a read-only array of candidate presentations. The matcher never independently fetches a later candidate list after frame acquisition. `CardVisualLocalizationRequest/Result` carry the pack token. Standalone/offline requests use token zero; production results are stamped by orchestration. `OverlayViewModel.ApplyAutomaticLocalization` requires the current token through `IsSafeFor`, in addition to its existing reference-context, pack identity, occurrence, confidence and rectangle checks.

Capture has two separate counters:

- `Generation`: rendering HWND/geometry/restart epoch, preserving the existing cancellation seam.
- `CaptureRequestGeneration`: incremented on every acquisition, including same-pack Retry and bounded recapture.

Every accepted regional frame carries both counters, requested pack generation, capture timestamp and decoded-pixel SHA256. An acquisition rejects timestamps before its request, mismatching supplied request/pack metadata, old capture epochs, changed geometry and wrong dimensions. Production backend frames receive request metadata only after coordinator validation; the backend still performs one asynchronous regional BitBlt per request.

## Shared synchronization policy

`PackFrameSynchronizer` serves automatic placement, manual Retry and explicit debug save. It stores only evidence hashes and confirmed artwork rectangles; it does not retain a bitmap that consumers could reuse.

- A pack transition immediately cancels its previous generation, invalidates current-frame metadata/result state, and retains prior evidence solely for stale-content detection.
- Initial visual settling waits until pack change + **250 ms**, using an asynchronous cancellable delay. A subsequent transition cancels that delay before acquisition.
- A new candidate-count state must change the previous crop's pixels. Byte-identical decoded pixels after a count change are rejected before the matcher is called.
- The independent count defense uses **previously confirmed actual card rectangles**, rather than treating candidate-driven geometry proposals as detection. If more than the new expected count of those artwork regions remain byte-identical, it reports, for example, `expected 8 cards, detected 9 unchanged confirmed card rectangles`. This also catches a changed cursor/background pixel outside the card artwork.
- A rejected stale frame is disposed. Reacquisition waits **150 ms**, then **250 ms**, for at most **three** fresh capture attempts. Three rejected attempts produce `Failed after 3 synchronized capture attempts`; this failure is not scheduled for another automatic retry cycle.
- Same-pack Retry always acquires again, increments the capture-request counter, and preserves the semantic pack token. Identical same-pack frames and identical same-count transitions are not rejected merely because their image hashes match.
- Generation/current-context checks run after settle, acquisition, evidence calculation and recognition, and immediately before result application/debug-pair publication. Late old-pack frames and results cannot publish placement for a new pack.

Environment settings (milliseconds, accepted range 0–2000):

| Setting | Default |
|---|---:|
| `DRAFTTG_PACK_VISUAL_SETTLE_MS` | 250 |
| `DRAFTTG_CAPTURE_RETRY_FIRST_MS` | 150 |
| `DRAFTTG_CAPTURE_RETRY_SECOND_MS` | 250 |

The existing per-acquisition timeout remains two seconds by default (`DRAFTTG_FRESH_FRAME_TIMEOUT_SECONDS`). Normal settling plus stale-content retry spacing totals at most 650 ms, excluding acquisition work/timeouts. The pre-existing bounded retry of partial matcher results remains separate; it also acquires a new synchronized frame each time.

Diagnostics distinguish waiting for visual update, fresh capture attempt, stale rejection, recapture, localization, matched/ambiguous outcome, and bounded synchronization failure. Manual confirmation remains available and stale automatic badges remain hidden.

### Content-check limits

Request identity and timestamps provide rigorous request ownership, not a GPU presentation timestamp. Image evidence rejects the demonstrated repeated-prior-pack failure when prior evidence is available. The count check is intentionally conservative: it requires unchanged, previously confirmed artwork regions. It is not a universal card detector, does not infer occupancy from a guessed grid, and cannot prove visual identity for a first capture without prior evidence. Same-count transitions, changed stale pixels, and unknown geometry can still require the unchanged artwork matcher's confidence gates and live investigation. These are limits to document, not reasons to lower thresholds or claim universal synchronization accuracy.

## Debug artifacts

The save command captures the current immutable context and uses the shared settle/freshness/recapture policy, without invoking the matcher. It never saves prior pixels from a cached bitmap.

Default names now follow:

`arena-draft-P1P7-gen17-cap42-20261003-212820-123Z-<unique suffix>.png`

The matching `.txt` contains pack/pick, expected count, semantic token, runtime scope/session-ID availability, capture epoch/request counter, requested/frame pack token, frame timestamp, pack-change timestamp, after-transition check, candidate names/identifiers, decoded-pixel hash, exact **PNG SHA256**, acquisition diagnostics and localization status. Debug-only acquisition reports localization not run; it does not misattribute an earlier match result to this different frame.

Image encoding and temporary pair writes run off the UI thread. The current-generation check precedes final publication on the session continuation, with no await between that check and the two final renames. Canceled/failed writes clean staged files. Capture failures write a uniquely named failure log and **no PNG**; they never overwrite an old image under a new pack name. Retention is capped at twenty generated pairs, restricted to this command's top-level generated names; generic legacy files are not deleted. The normal developer-only annotated-image output remains unchanged.

## Tests and validation

**14 new focused synchronization tests**, using the production synchronizer/coordinator and existing nine-card live fixture:

| Regression | What it establishes |
|---|---|
| Nine live pixels with new eight candidates | Stale pixels rejected; new eight-card synthetic frame reacquired; matcher reached only after acceptance |
| Nine confirmed art regions with other pixels changed | Independent expected-eight/detected-nine rejection before assignment |
| Three repeated stale frames | Bounded failure and disposal of every rejected frame |
| Wrong pack token | Rejected before matching and reacquired |
| Frame predates transition | Rejected even if its token claims the current pack |
| Transition while frame is pending | Late frame disposed; newer generation can capture |
| Transition during settle | Delay canceled before any capture |
| Same-pack Retry | New request/frame; unchanged semantic token |
| Debug save after transition | Prior pixels rejected; fresh current PNG/log pair saved; no matcher invocation |
| Debug recapture exhausted | No old PNG saved for the new pack; explicit failure log |
| Transition during matcher work | Old result discarded; new-pack result can apply |
| Result-token application guard | Same pack/coordinates with wrong token rejected |
| Coordinator timestamp check | Cached frame predating acquisition rejected and disposed |
| Debug generation change during encoding | Pair not published; temporary files cleaned |

Existing direct matcher-to-overlay tests now explicitly supply their current presentation generation, as production orchestration does. Existing debug-command tests assert the new unique pair names and failure-only logs. No broad matcher tests were added.

Validation:

- `dotnet build DraftTG.sln`: zero warnings, zero errors. Avalonia's build telemetry log required the normal build command to run outside the filesystem sandbox; the first sandboxed attempt failed on that log path, not source compilation.
- `dotnet test DraftTG.sln --no-build`: **672 passed**, zero failed/skipped (the original 658 plus 14 synchronization tests).
- Existing exact-frame artwork regressions still verify 9/9. No synchronized *live* eight-card frame has been obtained in this implementation session.
- Matcher source SHA256 unchanged: `AF3050C2750FDE4A8866424910137390E6CDFEBFE48A662D5090C9BB2F0A3F26`.
- Reference-cache source SHA256 unchanged: `B68345C06759F6D8768E765F49472426D046348347C0333606DB6CC7A9A7A790`.

Windows physical acceptance was attempted through the computer-use skill. Initializing `@oai/sky` failed before app selection: `failed to start Node runtime: The system cannot find the path specified. (os error 3)`. Reset succeeded; initialization after reset failed identically. No game inputs were sent. **Zero consecutive live picks were validated.** Whether a synchronized current live frame still fails matching remains unknown.

## Remaining Windows acceptance

Run the rebuilt app and validate a current pack, then at least three consecutive picks. For each transition verify a new semantic token, a fresh capture-request counter/time, localization for that token, and a uniquely named saved PNG that visibly shows that same current pack. Confirm rejected old pixels cause bounded recapture rather than `matched 0/x`. If matching fails on a verified current image, preserve its unique PNG/TXT for a separate matcher investigation. If newly acquired pixels remain persistently stale, use an independent screenshot to establish the rendering-surface issue; this phase does not redesign capture or certify GDI presentation freshness.

Recommendations, statistics association, Arena parsing, card identity, reference selection, matcher threshold 0.94, ambiguity margin 0.07, fine alignment, metric and assignment are unchanged. No OCR, process-memory access, Unity/accessibility inspection, alternate capture backend or Phase 9C implementation was added.
