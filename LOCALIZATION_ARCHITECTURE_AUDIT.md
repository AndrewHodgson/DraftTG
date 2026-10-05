# Phase 9B.9 — MTG Arena visual localization architecture audit

Date: 2026-10-04. Decision: **retain the current production architecture; no structural replacement passed the identity-to-rectangle gate.** Desktop Duplication produced one readable live Arena crop and deserves a controlled capture comparison. That is not proof of superiority or complete-draft reliability. No production migration, WGC lifecycle patch, or Phase 9D work was performed.

Player.log remains the semantic authority. Existing artwork localization and manual mapping remain available. The user's latest WGC diagnostic reports initialization failure and no frames before localization is invoked; altering recognition thresholds cannot fix that failure.

## Evidence and experiment boundaries

The user confirmed an active draft and later confirmed making another normal pick. Inspection was passive: no focus changes, game navigation, picks, internal game calls, injection, hooks, target writes, security bypass, or third-party binary inspection. Public Untapped documentation supplies feasibility context only. Its generic description discusses read-only memory inspection, while its MTGA-specific page describes log-derived state. Neither documents its object discovery or rectangle implementation. [Generic Companion description](https://help.hearthsim.net/en/articles/8377705-how-does-the-untapped-gg-companion-work), [MTGA Companion description](https://help.hearthsim.net/en/articles/5020719-how-does-the-untapped-gg-companion-work).

An isolated Windows developer project provides `snapshot`, `memory`, and `duplicate` commands. It is outside the solution and does not implement the production locator. [Tool usage and limits](tools/DraftTG.LocalizationAudit/README.md).

Local artifacts are under `artifacts/phase9b9-validation/`, which is ignored by the existing `.gitignore`. The JSON contains selected semantic facts and diagnostics, not the raw log or credentials. Memory output contains occurrence counts/addresses, not raw bytes. The image contains Arena center, including draft/pool cards, with account chrome excluded. No whole desktop image or game assembly copy was saved.

### Live observations

Times below are UTC (CDT is five hours earlier).

| Observation | Semantic evidence | UIA/native result | Identity → card rectangle |
|---|---|---|---|
| A, 11:49–11:51 | P1P10; five IDs; zero replay errors; before/after pack unchanged | One Arena pane; zero descendants; one Unity surface plus two hidden IME helpers | Not established |
| B, 11:56 | After user-confirmed pick; log now records P1P10 and P1P11 submissions, but replay resolves no current pack | Same Arena handle/geometry; zero UIA descendants again | Not established |
| B pixel probe, 11:59:22–11:59:23 | Current pack unresolved; four replay errors | Readable 2133×960 Arena-center crop from Desktop Duplication | Capture only; not semantic association or recognition |
| B memory gate, 12:00:27 | Still no current candidate pack from existing replay | Search skipped rather than reusing A's IDs | Not established |

The two consecutive pick submissions are observable, but a **successful localization/stability comparison across two current packs was not achieved**. After the picks, the audit's existing-parser replay reports two `MalformedOuterJson` errors and two `PickSubmission` conflicts at P1P3; `CurrentPack` is null. The redacted trace preserves these facts. This is a limitation of the audit's semantic observation, not evidence that memory addresses became unstable. No parser or state-engine fix was attempted in this phase.

The readable later crop shows fourteen visual cards; it must not be labeled P1P10 or P1P11 from stale state. Its top/bottom edges clip some card frame area because the diagnostic crop is not a calibrated draft region. No matching score or exact current `CardIdentifier` claim is made for that image. Deck-builder navigation was not requested from or performed for the user; live deck-builder validation remains outstanding.

Evidence files:

- [A window/runtime snapshot](artifacts/phase9b9-validation/pack-a/snapshot.json)
- [A bounded memory search](artifacts/phase9b9-validation/pack-a/memory.json)
- [A DXGI acquisition, pixels withheld because covered](artifacts/phase9b9-validation/pack-a-dxgi-acquisition/duplicate.json)
- [B window/runtime snapshot](artifacts/phase9b9-validation/pack-b/snapshot.json)
- [B redacted semantic trace](artifacts/phase9b9-validation/pack-b-redacted-trace/snapshot.json)
- [B fresh-pixel acquisition](artifacts/phase9b9-validation/pack-b-fresh-pixels/duplicate.json) and [readable crop](artifacts/phase9b9-validation/pack-b-fresh-pixels/arena-center-duplication.png)
- [B memory search skip](artifacts/phase9b9-validation/pack-b-current-id-gate/memory.json)

### Probe execution environment

Initial sandbox probes ran on `CodexSandboxDesktop-bbc813fa554cc7081117213251afcf23`, while Arena was on input desktop `Default`. Although both processes were in session 1 and input-desktop enumeration found Arena handles, window property reads failed and UIA returned an invalid-handle/element-unavailable error. Repeating the read-only probe on `Default` produced valid window properties and the actual UIA result below.

This explains the initial **probe** failure only. It does not establish the cause of the user's independently running DraftTG WGC failure. No desktop switching, security changes or game inputs were used.

## A. UI Automation/accessibility

Confirmed on the actual Arena desktop, twice:

| Property | Result |
|---|---|
| Root | `ControlType.Pane`, name `MTGA` |
| HWND/class | `0x200E24`, `UnityWndClass` |
| Framework/automation ID | `Win32`, empty ID |
| Root bounding rectangle | x=0, y=0, width=2560, height=1440 |
| Offscreen | false |
| Raw-view descendants | **0** |
| Truncation/provider error | false / none |
| Individual card names, IDs, rectangles | None exposed |

The raw view is used rather than relying on a filtered control view. UIA requires providers to expose elements; it cannot manufacture Unity-rendered card controls from pixels. [Microsoft: obtaining UI Automation elements](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/obtaining-ui-automation-elements).

**Gate: reject UIA as the current draft-card locator and stop pursuing it for this build.** A future Arena accessibility implementation could change this result; Unity version alone does not prove such a provider exists. Deck builder was not inspected, so the draft result is not represented as a deck-builder measurement.

macOS has Accessibility/AX APIs, with accessibility-client trust/permission, but their usefulness also depends on Arena exposing a tree. No Mac tree was tested. [Apple: accessibility-client trust](https://developer.apple.com/documentation/applicationservices/1459186-axisprocesstrustedwithoptions).

## B. Native HWND hierarchy

Confirmed window relationships from `GetParent` (which can represent parent or owner):

| Handle | Class | Parent/owner returned | Rectangle | Visibility |
|---|---|---|---|---|
| `0x200E24` | `UnityWndClass` | `0x0` | 0,0,2560,1440 | Visible; not minimized; DPI 96 |
| `0x1A088A` | `IME` | `0x200E24` | Zero-sized | Hidden |
| `0x3C0D66` | `MSCTFIME UI` | `0x1A088A` | Zero-sized | Hidden |

No individual card child windows were found. The helpers are input-method windows, not card tiles. HWND ordering therefore supplies no visual card ordering or card rectangles. It remains useful for the outer client rectangle, monitor, visibility and capture targeting.

**Gate: reject native-window inspection as an independent card locator.** On macOS, native window information can likewise bound an outer window; a Unity-rendered card need not have a native subview. Internal NSView inspection would not be supplied by simple cross-process window listing. Use AX if a supported provider exists; do not inject into the game to inspect its native view objects.

## C/D. Read-only process inspection and Unity runtime

### Confirmed runtime and module layout

Arena PID 27132, Steam Windows x64 build. Installed under `Steam/steamapps/common/MTGA`. Both the loaded `mono-2.0-bdwgc.dll` module and Player.log confirm **Mono**, with Unity **6000.3.14f1** (`d68c3f99a318`). Player.log records Arena version `2026.63.10.14403 / 2026.63.10.14403.1326110`. The executable/UnityPlayer file version `6000.3.14.14060607` is a Unity file version, not an Arena product-version substitute.

Relevant loaded modules include UnityPlayer, Mono, D3D11/DXGI and D3D12. Managed DLLs are present; no loaded `GameAssembly.dll` establishes an IL2CPP runtime for this observation. ASLR addresses and module sizes are in the local JSON, not compiled into the prototype.

Mono provides managed metadata; IL2CPP would require separate metadata/native-layout discovery and cannot reuse a Mono heap reader blindly. Unity documents Mono/JIT and IL2CPP/native compilation as different backends. [Unity scripting backends](https://docs.unity3d.com/6000.3/Documentation/Manual/scripting-backends-intro.html).

The tool reads PE metadata through `System.Reflection.Metadata`; it does not load the game DLLs into the CLR or invoke their methods. Selected metadata was not truncated: Core 421 authored types, SharedClientCore 80, UnityEngine.CoreModule four, and Assembly-CSharp zero matching types. The query is selective, not an exhaustive claim about all game types.

| Assembly | Observed MVID |
|---|---|
| Core.dll | `1fd4674e-f84a-42e4-8bc7-2370c6feb17a` |
| Assembly-CSharp.dll | `faaa6d19-3d52-4360-915e-62ab50061197` |
| SharedClientCore.dll | `9a1a596a-410f-4522-9a6d-c035ce801fa3` |
| UnityEngine.CoreModule.dll | `89f3c519-d1ec-48c9-9a8d-1a0ae8590602` |

SHA-256 values accompany these MVIDs in the snapshot. They identify this experiment's files; no supported-version whitelist or memory-layout implementation is claimed.

### Bounded known-ID search

The A pack's payload candidates, **not visual order**, were `86710, 86720, 86686, 86975, 86714`. Standard `OpenProcess` rights were limited to query + VM read (`0x410`), followed by `VirtualQueryEx` and `ReadProcessMemory`. No debug privilege or write/operation access was requested. These APIs expose bytes and region properties, not typed Unity objects. [Process access rights](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights), [ReadProcessMemory](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-readprocessmemory), [VirtualQueryEx](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualqueryex).

Scope: at most 2 MiB per writable runtime PE section; aligned pointer values in Mono/Unity sections select readable, committed private regions through one-hop OS queries. The sixteen most-referenced eligible regions are sampled at up to 2 MiB each around referenced addresses. These regions are **not proven Mono heap allocations**. Limits are 48 MiB requested reads, 8,192 queries and eight seconds between operations. This is not an entire-process scan or a version-specific pointer chain.

Measured result: 18,516,152 requested/read bytes, 8,192 queries, zero read failures, 450 eligible regions considered, approximately 334 ms for the memory experiment.

| Candidate | Int32LE occurrences | ASCII occurrences | UTF16LE occurrences | Proven rendered objects / rectangles |
|---|---:|---:|---:|---|
| 86710 | 0 | 0 | **1** | 0 / 0 |
| 86720 | 0 | 0 | 0 | 0 / 0 |
| 86686 | 0 | 0 | 0 | 0 / 0 |
| 86975 | 0 | 0 | 0 | 0 / 0 |
| 86714 | 0 | 0 | 0 | 0 / 0 |

All fifteen shifted-ID negative-control encoding searches yielded zero occurrences. This does **not** measure a ground-truth false-positive rate: the lone text hit may be serialized/log/catalog data, and no hit was validated as a card object. Multiplicity is raw occurrence count only. Missing IDs in a bounded sample do not prove absence from the process. An earlier region-selection control found zero of five; the final selection prioritizes Mono and deduplicates queried addresses. Neither result proves reliable discovery.

After the user's pick, the current-ID gate skipped memory inspection because replay no longer resolved an active candidate pack. Searching A's IDs again would not demonstrate current-pack stability. Live address/relationship stability across two packs remains unknown.

### What structural metadata does and does not prove

Confirmed authored type/field relationships:

- `DraftPackCardView : CDCMetaCardView : MetaCardView : MonoBehaviour`; its `CurrentCard` backing field references `ICardCollectionItem`.
- `MetaCardView` has `_cardData` and `_visualCard` of `GreClient.CardData.CardData`, plus a holder and position/scale-related fields.
- `CardData._printing` references `CardPrintingData`; `CardPrintingData.Record` is `CardPrintingRecord`, whose `GrpId` is UInt32.
- `DraftContentController` has a camera, pack collection, a last-clicked `DraftPackCardView`, and a draft-index-to-grpId dictionary.
- Deck/pool-related types include `StaticColumnMetaCardHolder` with a card-view list/parent transform, `PagesMetaCardHolder` with visible-item/RectTransform fields, and `ListMetaCardHolder_Expanding` with tiles/parent transform. This supports a conceptual future deck-builder relationship, not a live measured one.
- `UnityEngine.Object` has `m_CachedPtr`. `RectTransform` exposes rect/anchors/position/size properties, but the inspected managed metadata does not supply their native storage layout. Geometry methods include native/injected accessor names.

Hypothesis: a version-validated reader might discover a live card-view collection, follow its printing identity, validate visibility/lifetime, and resolve the associated native transform/camera. **None of those live joins, instance offsets, native geometry layouts, viewport conversions or card bounds was demonstrated.** A cached `_prevScreenPosition` is not accepted as a current rectangle; it could represent interaction state and supplies no validated width/height.

Unity documents world-space corners and camera-aware screen conversion as distinct operations. A reliable reader would need all four corners, hierarchy transforms, camera/viewport projection, clipping, pixel scaling and visibility, not just a position vector. Internal `GetWorldCorners`/`WorldToScreenPoint` calls were not made. [GetWorldCorners](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RectTransform.GetWorldCorners.html), [WorldToScreenPoint](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RectTransformUtility.WorldToScreenPoint.html).

**Gate: reject the current memory prototype as a production locator.** It found a raw candidate value, not a reliable ID → rendered object → rectangle path. Metadata makes structural research plausible, but does not meet the production authorization gate. Do not expand this into an unbounded scan or hard-code the observed address.

Any separate future structural prototype must validate Arena/Unity/runtime/architecture/module hashes, discover layouts without copied cheat signatures, verify readable bounds and object types, validate active collection membership and native lifetime, sample coherently across transitions, and fail closed on unknown versions or incoherent geometry. It must prove the complete path over consecutive picks and deck-builder scrolling before integration. If that requires invoking game internals or modifying its security/signing, reject it.

### macOS process implications

Mach task/VM read APIs offer a conceptual counterpart, but target task-port access is gated by OS policy and signing/entitlements. Apple's documented debugger entitlement describes task-port access for unsigned targets or third-party targets with `get-task-allow`; it does not establish access to the shipped Mac Arena build. No Mac runtime, architecture, entitlements or access were tested. Do not assume Windows Mono/layouts or admin rights make this portable. Re-signing Arena, disabling protections or injecting to obtain access is outside scope. [Apple debugger entitlement](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.cs.debugger).

## E. Alternative capture

The isolated prototype uses DXGI Desktop Duplication on the output containing Arena center and a D3D11 device on that output's adapter. It acquires composited desktop frames, copies only the crop to CPU and releases each acquired frame/COM resource. This is a distinct capture path, with its own failure conditions: output/session changes, access denial, rotation, multi-monitor spanning, protected content, format support and occlusion. [Desktop Duplication API](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api), [DuplicateOutput requirements](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutput1-duplicateoutput).

Measured:

1. A: duplication/frame acquisition worked, but the Arena-center visibility sample found another process covering it. No CPU pixel readback/image was performed.
2. Initial B: native acquisition returned a pointer-only update (`LastPresentTime=0`, `AccumulatedFrames=0`). The initial diagnostic incorrectly accepted this as sufficient for pixel validation and saved an all-black crop. That image is retained as a failed experiment; it is not an Arena capture success.
3. The isolated probe was corrected to release pointer-only frames and wait within the same two-second budget for a desktop image update, and reject uniform pixel crops. No WGC code changed. SDK documentation explicitly distinguishes pointer-only updates. [Frame metadata](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/ns-dxgi1_2-dxgi_outdupl_frame_info), [AcquireNextFrame](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutputduplication-acquirenextframe).
4. Final B: fresh image update, `AccumulatedFrames=1`, nonzero present QPC `801700845729`; readable 2133×960 crop, about 475 ms for acquisition/copy/save. Crop screen coordinates: x=213, y=240, width=2133, height=960. Human inspection confirmed visible card artwork/name bands. No card detector, OCR, assignment or identity matching was executed on this crop because current semantic candidates were unresolved.

**Gate: DXGI pixel acquisition passed once; material superiority did not.** There was no same-state WGC comparison or complete-draft run, and no correctly identified card rectangles. Do not integrate it as a replacement based on this result. A controlled comparison should use the same current pack, calibrated region, freshness checks and unchanged matcher, recording initialization failures, valid frame latency, false/stale frames, card-association results, occlusion, resize/monitor changes and a whole draft. That comparison is follow-up work, not a lifecycle patch in this phase.

Desktop Duplication captures the composited display, so an obscured card has no recoverable visible desktop pixels. A production fallback would need to refuse such frames and suppress self-overlay contamination. The prototype's five-point guard is only a diagnostic safeguard, not a complete visibility detector. Its crop is not a new layout calibration or production synchronization implementation.

macOS has no DXGI equivalent; use a separate ScreenCaptureKit implementation with content/window filtering and screen-recording permission. Actual Mac capture reliability remains untested. [Apple ScreenCaptureKit](https://developer.apple.com/documentation/ScreenCaptureKit?language=objc).

## F. OCR / known-pack hybrid

OCR is an evaluated fallback, not a replacement selected in this audit. The valid DXGI crop has readable name bands by human inspection, which makes a local OCR experiment reasonable; it does not prove OCR accuracy. No OCR dependency was added and no OCR accuracy/latency result is claimed.

A constrained hybrid could detect card rectangles, OCR only their name bands, normalize punctuation/language, resolve against log-derived candidates, and enforce occurrence-aware assignment plus ambiguity rejection. OCR text boxes are not whole-card rectangles. Names cannot reliably distinguish printings/art variants or duplicate occurrences; candidate IDs/printing data and geometry still matter. Require agreement with artwork when confidence is weak, and preserve manual mapping for unresolved/occluded/animated states. Do not map by payload order.

Windows `Windows.Media.Ocr` is documented as supported for desktop apps with package identity/MSIX. The current unpackaged diagnostic therefore cannot assume it is a supported drop-in. A packaged adapter or a separate local OCR engine would add deployment/model/language dependencies that need measurement. [Windows OCR namespace](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr?view=winrt-26100).

Apple Vision supplies on-device text recognition, language settings and custom vocabulary, but requires a native platform adapter and real fixture validation. Use known candidate names as vocabulary without treating forced vocabulary output as confidence. [Apple text recognition](https://developer.apple.com/documentation/vision/recognizing-text-in-images?changes=_7).

Deck builder adds duplicate cards, stack counts, compact list rows, alternate names/languages, scroll virtualization and partially visible cards. Draft candidates alone do not enumerate a deck-builder screen. OCR/artwork approaches would need independent visible-tile detection and appropriate pool/deck semantics; structural approaches would need the active visible collection rather than cached/inactive views. No deck-building logic was implemented.

## Decision matrix

These ratings combine measured findings above with explicitly unvalidated architectural estimates. “Potential” does not mean a working prototype. All visual rows retain Player.log as semantic authority.

### Reliability and localization

| Approach | Reliability | Card identity accuracy | Rectangle accuracy | Arena-update fragility | Deck builder |
|---|---|---|---|---|---|
| WGC + artwork (existing) | Works in some states; user's complete-draft failure precedes matching | Existing known-pack matcher works with correct captures; current failure produces no matches | Existing calibrated/detected geometry; needs valid frame | Moderate: capture/geometry/art changes | Potential with separate tile detection and candidate scope; untested |
| Desktop Duplication + artwork/OCR | One readable crop; covered/pointer-only cases observed; full draft unknown | Artwork can reuse current matcher; OCR unmeasured | Crop acquisition only, zero card rectangles proved | Moderate: desktop visibility/output/layout and art/name changes | Potential with tile detection; no inspection performed |
| UI Automation | Actual provider exposes only one pane | No card identities | Outer window only | Depends on Arena supplying an accessibility provider | Unknown; draft provider unusable |
| Native HWND inspection | Reliable outer window in two observations | No card identities | No card children; outer rectangle only | Low for outer targeting; cannot solve card localization | Native tile subviews unproven |
| Read-only Unity/process structures | Current prototype cannot reliably discover typed views | One raw text occurrence of five; zero proven UI identities | Zero card rectangles; native transform bridge unresolved | High until version/layout discovery and validation are proven | Metadata suggests view collections; live path unproven |
| Layered log + capture + art/name cross-check | Potential fallback diversity; capture still required | Constrained assignment may improve rejection; not measured | Visual geometry remains required | Moderate; more components to validate | Potential with occurrence/visibility model |

### Platform, cost and privacy

| Approach | Windows feasibility | macOS equivalent/complexity | Permissions | Dependencies | Performance | Privacy | Effort |
|---|---|---|---|---|---|---|---|
| WGC + artwork | Existing implementation | ScreenCaptureKit + shared matcher; medium/high native work | Windows OS capture policy; Mac screen recording | Existing WinRT/D3D + image/matcher stack | Existing async capture; reliability dominates here | Arena-region pixels only; local matching | Maintain existing; reliability work separate |
| Desktop Duplication + artwork/OCR | Acquisition proven; migration unapproved | ScreenCaptureKit, not DXGI; medium/high | Same interactive display/OS access; Mac screen recording | DXGI/D3D adapter; optional OCR deployment/models | One crop ~475 ms including save; sustained latency unmeasured | GPU surface is desktop-wide; restrict CPU/readback to Arena and reject coverage | Medium/high native hardening + full-draft validation |
| UI Automation | Probe works, game provider insufficient | AX tree + native bridge; moderate; Arena provider unknown | UIA process/desktop access; Mac Accessibility trust | Windows UIA/Apple AX | Probe <1 s here; providers can stall | Scoped tree text/rectangles; no pixels required | Low audit cost; cannot integrate observed result |
| Native HWND inspection | Proven outer geometry | Window listing/AX; low/moderate; no general remote NSView traversal | Desktop/window-query access; OS policy on Mac | Win32/native Mac window APIs | Small tree, low cost | Scope to Arena window metadata | Low; useful support layer only |
| Read-only Unity/process structures | VM reads proven; object/coordinate path unproven | Mach task rights plus runtime/architecture discovery; very high | Query + VM read; Mac signing/task-port access unknown | PE/Mono metadata + native layout reader; separate IL2CPP path if needed | Sample ~334 ms / 18.5 MB; coherence/typed discovery unmeasured | Highest exposure: process bytes may contain unrelated private state; never persist raw memory | Very high ongoing maintenance; reject current production use |
| Layered visual hybrid | Feasible design, not tested implementation | Shared candidate/assignment policy plus native capture/OCR adapters; high | Capture/OCR requirements; memory permissions only if later justified | Existing matcher plus optional OCR and backend policy | More work per frame; budget/cancel/cache needed | Local restricted crops; avoid broad OCR/log dumps | Medium/high after proven fallback advantage |

## Architecture decision and gates

1. **Primary now:** Player.log → existing known-pack artwork locator, when capture is valid. Keep current confidence/ambiguity checks and manual fallback. Do not infer visual order from payload order.
2. **Research fallback next:** controlled Desktop Duplication versus WGC capture comparison, using the unchanged matcher and production freshness contract. The single successful crop justifies testing, not migration. OCR is a secondary identity cross-check experiment if artwork failures require it.
3. **Structural locator:** no production implementation. UIA/native cards are absent; memory does not prove a live identity/rectangle relationship. Reconsider only after discoverable, version-validated read-only object and coordinate extraction succeeds over multiple picks and deck-builder states.
4. **Final fallback:** existing manual mapping. Fail closed on unavailable capture, unknown current semantic candidates, ambiguous identity or stale/incoherent geometry.

`ICardVisualLocator` already exists in `DraftTG.Application`; it was not duplicated or changed. It accepts a draft pack/occurrences/layout and returns normalized region rectangles with generation-aware safety checks. Any justified platform provider belongs behind this boundary; Windows handles/COM/types remain in a Windows adapter, not Domain/RecommendationEngine. Capture fallback must preserve the current pack/request generations and coordinate conversion contract.

For future deck-builder support, design a platform-neutral visible-card occurrence model with identity, scene, visibility, coordinate provenance, viewport and observation token. Repeated copies and virtualized rows must remain distinct. The current draft request is not automatically a general deck-builder API; no speculative replacement DTO was added now.

### Validation and remaining unknowns

- Isolated audit project build: zero warnings/errors. Existing Windows solution build: zero warnings/errors.
- **725 existing tests passed**, zero failures/skips: Domain 42, RecommendationEngine 116, Application 139, Data 138, ArenaIntegration 94, App 196. TRX files are in `artifacts/phase9b9-validation/tests/`.
- **0 new tests.** No reusable production abstraction or production behavior was added. Physical probe results matter more than tests mirroring exploratory interop.
- [Production source/test verification](artifacts/phase9b9-validation/verification.json): all **166 existing source/test files have identical hashes**, with zero added files under `src/` or `tests/`. Existing scoring, providers, parser, pack/frame synchronization, locator/manual fallback and WGC files were not edited.
- Still unknown: complete-draft DXGI reliability, same-state WGC comparison, accurate detection/matching on the new crop with current log candidates, memory object/transform discovery, cross-pick typed relationships, deck-builder behavior, and all physical Mac behavior/permissions.
- The semantic replay issue after the picks is recorded, not silently corrected or attributed to the app's live coordinator. Addressing it belongs to a separate semantic diagnostic task if it is also reproduced in production.

The audit is inconclusive about a superior replacement. It stops at research/prototypes as authorized; it does not claim successful structural localization or begin Phase 9D.
