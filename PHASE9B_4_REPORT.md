# Phase 9B.4 — Automatic Arena card localization

Windows automatic localization is implemented using local known-pack artwork matching. The manual matcher remains fallback and partial automatic results prefill known positions. **Live acceptance is incomplete:** the Windows inspection runtime cannot start, and macOS capture is not implemented. Synthetic tests do not establish real Arena accuracy or that ordinary drafting no longer needs manual mapping.

## Research and decision record

Only public documentation/source was examined; no proprietary code, binaries, process memory, injection or game modifications were used.

| Category | Finding / decision |
| --- | --- |
| A. Arena logs/UI metadata | Actual current payload supplies identity and pack/pick numbers, not visual order/positions. Keep logs as identity source. |
| B. Deterministic display rules | Both rarity/color/name and rarity/color/collector fit the single prior visual observation. Public assistant output sorting is not an authoritative Arena rendering contract. Do not derive a placement rule. |
| C. Capture + vision | Supported OS window capture supplies missing pixels. Implement bounded Windows client-region capture with local CPU analysis. |
| D. OCR | Microsoft documents native OCR as requiring package identity/MSIX; DraftTG is unpackaged. Vision provides local recognition/custom words but needs a Mac bridge. Tesseract needs binaries/language data. None was benchmarked on real Arena pixels. Defer these dependencies until actual captures show they are necessary. |
| E. Artwork/templates | Local bulk metadata includes small front-image URLs for the known cards. Implement normalized correlation using small, bounded cached references. |
| F. Known-pack hybrid | Combine geometry hypotheses/refinement, artwork evidence, exact assignment and confidence/margin checks. Recognition searches only current occurrences. This is geometry/art matching, not implemented OCR/art consensus. |
| G. Accessibility | Custom controls require accessibility providers. No public source establishes Arena per-card accessible metadata; physical inspection was blocked, so its absence is not proven. |
| H. Third-party overlays | Overwolf Native is Windows-only. Untapped publicly demonstrates per-card ratings and Windows/Mac downloads, but its localization internals could not be confirmed. |

**Chosen:** known-pack template correlation with geometric refinement and constrained assignment. It offers local processing, bounded assets, an existing Skia runtime, a portable analysis core, no OCR model distribution, inspectable confidence and safe fallback. This is a provisional implementation choice pending real captures, not proof of superiority to OCR.

Rejected here: log sorting (insufficient evidence), cloud OCR (privacy), Windows OCR as sole provider (supported deployment/MSIX and platform parity), heavy Tesseract/OpenCV distributions (cost before demonstrated need), and hash-only forced assignment (no independent rejection of unrelated pixels). Feature matching, border/edge detection and OCR consensus were assessed qualitatively; they remain candidates if actual images expose unsupported geometry/frame styles.

Public findings and sources consulted:

- The [MTGA_Draft_17Lands fork inspected](https://github.com/carloscrespog/MTGA_draft_17lands) documents Refresh-triggered missing-P1P1 screenshots sent with candidate names to a Google Cloud Function/Google Vision, reverting to logs after a pick. It verifies screenshot OCR, but does not meet local-only processing. The [archived original](https://github.com/bstaple1/MTGA_Draft_17Lands), [17Lands log client](https://github.com/rconroy293/mtga-log-client), and [log-based assistant source](https://github.com/wolfeatyou/mtga/blob/main/draft_live.py) illustrate log-based tooling, not physical-slot contracts.
- [Untapped's own Companion page](https://mtga.untapped.gg/companion) demonstrates overlay behavior without implementation details. No private implementation was inferred.
- [Microsoft OCR remarks](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr?view=winrt-26100), [GDI capture](https://learn.microsoft.com/en-us/windows/win32/gdi/capturing-an-image), [BitBlt](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt), [GetTopWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-gettopwindow), [GetWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindow), and [UI Automation providers](https://learn.microsoft.com/windows/win32/winauto/uiauto-providersoverview) informed the Windows boundary.
- [Apple ScreenCaptureKit sample](https://developer.apple.com/documentation/screencapturekit/capturing-screen-content-in-macos), [single-frame manager](https://developer.apple.com/documentation/screencapturekit/scscreenshotmanager), and [Vision recognition](https://developer.apple.com/documentation/vision/recognizing-text-in-images) inform the Mac plan.
- [Tesseract installation](https://tesseract-ocr.github.io/tessdoc/Installation.html), a [local MTG scanner using Vision/Tesseract](https://github.com/McDandle/local-mtg-scanner), and [OpenCV's normalized template matching](https://docs.opencv.org/4.12.0/de/da9/tutorial_template_matching.html) informed alternatives. The collection scanner is not an Arena draft utility; the broader claim that other MTGA utilities use native Vision OCR was not independently confirmed. No implementation code was copied.
- [Scryfall's own API types](https://github.com/scryfall/api-types/blob/main/src/objects/Card/CardFields.ts) and [rate-limit guidance](https://scryfall.com/docs/faqs/i-m-having-trouble-accessing-the-scryfall-api-or-i-m-blocked-17) informed image access. General API/image documentation returned HTTP 403 in browsing; exact URLs were verified in local bulk records. [Overwolf framework documentation](https://dev.overwolf.com/ow-native/getting-started/onboarding-resources/framework-overview/) establishes Native's platform restriction.

## Current logs, assets and layout

The sanitized audit found **one unique current payload**: QuickDraft_WOE_20260929, PackNumber 0 / PickNumber 5, nine cards. Fields: Result, EventName, DraftStatus, PackNumber, PickNumber, NumCardsToPick, DraftPack, PackStyles, PickedCards, PickedStyles. Style arrays are empty. There is no visual order, screen position, layout index, stable visual-slot identifier or rendered post-pick sequence. Parsing preserves serialization order.

Log order starts with Scarecrow Guide and ends with Hatching Plans; the confirmed screen order is Hatching Plans, Stonesplitter Bolt, Rat Out, Grabby Giant, Merry Bards, Redcap Thief, Return from the Wilds, Gingerbrute, Scarecrow Guide. No automatic sort rule was inferred from this one pack.

The local compressed Scryfall file is about 79 MB. Normalized Domain cards discard image URLs, but bulk records retain them. All nine regression cards have small front-image URLs: eight normal layouts and Grabby Giant's adventure layout. There was no local image cache. URLs are indexed separately from existing bulk metadata; no metadata API request or unrestricted recognition search is needed.

Existing calibration allocates 14 normalized slots in user-selected 4–7 column profiles. Recognition adds compact/max-row, left/center-aligned geometry hypotheses and refines translation, scale and portrait aspect using pixels. A hypothesis alone never authorizes identity. Tests bound proposals for 14→1 cards; they do not establish actual Arena behavior at all those sizes. A live survey of spacing, row count, aspect ratio, fullscreen/windowed modes, resolutions, DPI and Mac layouts could not be performed. Unsupported geometry must fall back.

## Architecture and normal behavior

Application adds `ICardVisualLocator`, occurrence-oriented request/result records, confidence states, result validation and `PackVisualAssignment`. Exact bitmask dynamic programming maximizes the whole score matrix for up to 14 occurrences, O(n·2^n). Duplicates consume independent keys. Indistinguishable physical copies get deterministic logical assignment; their original log-copy origin cannot be recovered from pixels.

App composes capture/cache/recognition/presentation. `Platform/WindowsArenaRegionCapture` finds MTGA's client/DPI, requires a foreground/non-minimized window, checks covering windows before and after copying, and captures only the calibrated region through its client DC using SRCCOPY without CAPTUREBLT. There is no full-desktop capture, whole-client intermediate, cloud recognition, game input or persisted normal screenshot. Black/unsupported DirectX capture should fail recognition; actual GDI suitability for Arena remains unvalidated.

The saved calibration becomes a normalized Arena-client anchor for the running session. Moves/resizes/DPI/monitor changes use physical desktop origins and logical overlay dimensions; size/DPI changes invalidate placement. Screen changes can preserve a live anchor; otherwise existing calibration invalidation remains. Restarting after incompatible screen changes may require recalibration.

New packs/context changes cancel old work and wait 700 ms for UI settling. A 500 ms timer checks window metadata, not pixels. Capture runs only on pack/window changes or explicit retries, with at most three attempts and two-second retry spacing. Indexing, network I/O and recognition run off the UI thread. Context reference/generation, revision and geometry checks reject stale results. Statistics/rank updates preserve placement. Manual edits block asynchronous overwrites.

Reference downloads use local HTTPS `cards.scryfall.io/small/front/` URLs, at most 14 distinct current candidates, sequential 150 ms spacing, explicit User-Agent/Accept headers and no redirects. Reads are capped at 256 KiB and decoded dimensions bounded. RAM retains only current-pack images; disk retains at most 128 thumbnails (~32 MiB maximum). Cached images are reused; no per-frame or repeated cached-pick requests. Missing images remain unresolved. Initial URL indexing/download time is separate from matching benchmark time.

Area averaging creates 20×14 RGB artwork descriptors. Normalized correlation must reach 0.94, agree across artwork halves, beat other identities/slots by 0.07 and avoid overlap. Flat images are rejected. Solve the complete one-to-one assignment before publishing accepted matches; require at least half the pack to pass before using partial geometry. Result validation rejects foreign/duplicate keys, reused slots, invalid rectangles, overlapping accepted rectangles and nonfinite/out-of-range scores. **Similarity values are not calibrated probabilities.** Real false-positive/negative rates remain unmeasured.

`PlaceAll` resolves each immutable badge occurrence through accepted rectangles or explicit manual maps, never log index/rank. Full success bypasses confirmation. Partial success shows accepted badges and prefills known choices; only unresolved choices need selection. Same artwork under different identifiers remains ambiguous; genuine duplicate identifiers are supported. Fallback geometry still needs to agree with visible cards/calibration.

Status shows Automatic or matched/expected counts. The collapsed manual matcher stays available for override. Placement diagnostics include method, capture dimensions, rectangles, candidates, matches, duration and per-slot similarity/alternatives/overlap. Ordinary logs contain no pixels.

## Permissions, privacy and debug

Windows GDI uses existing desktop-session access; this implementation needs no administrator elevation or Screen Recording prompt. Arena must be foreground and unobstructed; move the rail outside the draft region. Exclusive fullscreen, remote sessions, protected content and covering windows/tooltips may force fallback. There is a residual OS timing race if another window moves during a copy; checks surround the operation and frames normally stay only in memory.

Debug saving is disabled by default. Set `DRAFTTG_LOCALIZATION_DEBUG_DIRECTORY` to an explicit development directory before starting DraftTG to save an annotated draft-region `localization-latest.png` with rectangles, slot/name and similarity. One file is overwritten, not archived or uploaded. Clear the variable to disable saving; remove the file when finished. Status → Card placement diagnostics → Retry automatic placement starts another attempt. Test images are generated in memory; no screenshot archive was checked in.

## Concrete macOS plan

Mac currently reports Manual fallback. Shared matching/assignment/cache/interfaces remain portable, but no native capture provider or physical validation exists.

Bundle a signed Swift/Objective-C ScreenCaptureKit bridge. Enumerate shareable windows, select MTGA, create `SCContentFilter(desktopIndependentWindow:)`, set a source rectangle in window-relative points, and capture one image with `SCScreenshotManager.captureImage`. Return bounded BGRA bytes plus window origin and point/backing-pixel scale through `IArenaRegionCapture`; implement window discovery in points, preserving negative monitor origins/Retina scaling and the same cancellation/settle policy. Handle denied/revoked Screen Recording permission without prompting in retries. Add usage text/signing/bundling; the user grants permission in Privacy & Security. If actual artwork recognition needs another signal, use local Vision with current names as custom words and the same constrained assignment. Validate several real Mac picks before claiming parity.

## Tests and benchmark

18 focused tests cover assignment/cancellation, pixel-derived order, unrelated/flat rejection, ambiguity, duplicate occurrences, missing references, compact/scaled geometry, automatic publication/evidence preservation, partial completion, stale/foreign/invalid confidence, relative coordinates/DPI, offline cache reuse/source restrictions, explicit debug output, bounded 14→1 hypotheses, next-pick invalidation and P1P6 identity/metrics.

The P1P6 test uses the real-shaped payload/catalog/statistics with **synthetic artwork**, without manual mapping: Hatching Plans → slot 1, 60.6%, ALSA 5.22, #1; Scarecrow Guide → slot 9, 55.6%, ALSA 6.95, #8. It is not recognition of an Arena screenshot.

| Synthetic 1000×600 image | NCC accepted/correct | NCC time | dHash forced correct | dHash time |
| --- | ---: | ---: | ---: | ---: |
| Clean | 9/9 | 891.6 ms | 9/9 | 2.7 ms |
| Dimmed | 9/9 | 596.0 ms | 9/9 | 0.4 ms |
| Unrelated flat | 0 accepted | 315.7 ms | 2/9 forced | 0.3 ms |

dHash received correct geometry and no rejection gate; NCC searched geometric alternatives. These different workloads are not an apples-to-apples speed comparison or evidence about OCR quality. OCR was not benchmarked without configured supported deployment/real captures. The numbers justify bounded prototype work; production acceptance still needs real images.

Required commands: `dotnet build DraftTG.sln`; `dotnet test DraftTG.sln --no-build`. **637 passing tests (619 existing + 18 new), zero warnings/errors, zero failures/skips.** Counts: Domain 42, RecommendationEngine 102, Data 134, ArenaIntegration 94, Application 135, App 130. Logs, benchmark, sanitized log/image audit and source hashes are in ignored `artifacts/phase9b-4/`.

## Files and dependencies

New: `src/DraftTG.Application/CardVisualLocalization.cs`; App's `AutomaticCardLocalizationSession.cs`, `CardTemplateRecognition.cs`, `ScryfallVisualReferenceCache.cs`, `LocalizationDebugCapture.cs`, `Platform/ArenaRegionCapture.cs`; `tests/DraftTG.App.Tests/AutomaticLocalizationTests.cs`; this report.

Updated: App's `OverlayViewModel.cs`, `OverlayDesktopSession.cs`, `RailPanelView.axaml`, `DraftTG.App.csproj`; `ARCHITECTURE.md`, `ROADMAP.md`.

SkiaSharp 3.119.4 is now explicit; Avalonia already resolved that same transitive version. No OCR engine/model, OpenCV distribution or Windows-only target framework was added. SHA-256 comparison preserves prior tests, RecommendationEngine, Data/17Lands formulas/cache, Arena/parser, Domain and `global.json`. Phase 9C remains unstarted.

## Live results and remaining acceptance

MTGA process 42472 was running. Supported Windows computer-use `node_repl`/`@oai/sky` failed before initialization with `The system cannot find the path specified. (os error 3)`, including retry after reset. No physical capture, five-card badge comparison, pick advancement or consecutive-pick validation occurred. No game input was automated. No Mac environment is available.

Remaining: run the built app against an unobstructed calibrated Arena region; verify actual GDI pixels; compare at least five automatically placed badges including the named regression cards if still present; advance several picks manually and record match counts/errors; exercise moves/resizes/DPI/monitor changes; assess special/adventure frames, alternate art, animations, hover enlargement and duplicate printings. Then implement and validate Mac capture.

**Manual mapping is bypassed when this matcher is confident; whether ordinary real drafting consistently achieves that is unknown.** Missing assets, ambiguous artwork, unsupported layouts/frames, occlusion and capture failure activate fallback. Phase 9B.4 is not fully accepted until Windows physical validation succeeds. Stop here; no Phase 9C work.
