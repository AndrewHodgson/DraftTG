# Phase 9F.4 Deck Localization Report

Main-deck runtime geometry is implemented and passively confirmed on the pinned Arena build. The user physically confirmed the initial eight main rows, order/names/quantities and grouped ×2 at 1600×900 / 96 DPI. **The remaining physical matrix is incomplete:** larger/scrollable lists, movement, second size, pick/resize transitions and DPI changes are not yet qualified. Sideboard display remains unsupported.

Production `contextual-pick-score-v1.6.2`, Q/L/A/D/R, commitment, k500, the FRA prior, tiers and deck builder remain unchanged. No V2 research, acquisition, replay or holdout evaluation was restarted; MH3/DFT remain untouched. Phase 9F.3 reports/evidence and the earlier rating audit were preserved byte-for-byte.

Runtime pack support remains the measured five-column compact list layout, 1–14 cards, windowed client height 720–1080 pixels and aspect 1.70–1.78. Counts/layouts outside that contract decline. Physical qualification is currently narrower than this runtime envelope.

## Object graph and ownership

| Runtime owner/type | Field / token | Purpose and nullable behavior |
| --- | --- | --- |
| `Wotc.Mtga.Wrapper.Draft.DraftContentController` | `_activeDeckView`, 0x04004AD8 | Active `DraftDeckView`; unavailable/changed owner declines |
| `DraftDeckView` | `_mainHolderInstance` 0x04004B27; `_sideHolderInstance` 0x04004B28 | Separate holders; actual type must be `DraftListMetaCardHolder` |
| `ListMetaCardHolder_Expanding` | `_tiles` 0x040013D4 | Grouped represented rows; membership is not proof of visibility/order |
| Same holder | `_cardParent` 0x040013D0; `_scrollbar` 0x040013D5 | Content ownership and scroll target; missing sources decline geometry |
| `ListMetaCardView_Expanding` | `<Quantity>k__BackingField` 0x040013DC; `_raycastImage` 0x040013E1 | Quantity and cached graphic canvas source |
| `MetaCardView` / `GreClient.CardData` | `_cardData` → `_printing.Record.GrpId` | Exact represented printing; row identities/quantities reread after geometry |
| `UnityEngine.UI.ScrollRect` | `m_Content` 0x0400011B; `m_ViewRect` 0x0400012D | Cached content and viewport; fallback to `m_Viewport` only if required |
| `Graphic` | `m_Canvas` 0x04000059 | Canvas ownership; cached field, no getter invocation |
| `UnityEngine.Object` | `m_CachedPtr` 0x04000A5A | Native object bridge; loaded assembly/MVID and local hashes checked |

These managed paths were statically inspected and passively observed live. Field addresses use the existing Mono metadata resolver, including inherited fields, rather than transplanted hardcoded managed offsets. Static tokens and native getter traces are hashed in [static audit evidence](docs/evidence/phase-9f4/static-audit.json).

The holder's inherited `ScrollRect` field is null live. Its scrollbar event instead identifies a unique `ScrollRect` through enabled persistent `SetVerticalNormalizedPosition` calls or `m_Calls.m_RuntimeCalls → InvokableCall<float>.Delegate.m_target`, with the vertical-scrollbar back-link. The resolved content's native transform must equal `_cardParent`. Event references, list membership and cached native bytes are rechecked after traversal.

## Visual order and geometry

`ListMetaCardHolder_Expanding.SetCards` (0x0600196E) explicitly sets sibling indexes, but the row collection is still not assumed to be visual order. Rows sort by runtime world top, then horizontal position. Equal-height ambiguity declines. Actual order comes from each tile root transform's cached rectangle and bounded TRS parent chain. The graphic raycast rectangle has zero width live and is unsuitable; it is not used as the row rectangle.

For the pinned native layout, Component+0x20 gives GameObject, GameObject+0x20 gives the component vector and its first component+8 gives the root Transform. Transform+0x28/+0x30 locate hierarchy/index; hierarchy+0x18 contains 48-byte position/quaternion/scale records and +0x20 parent indexes. Cached RectTransform x/y/w/h starts at +0xb8; +0xf1 must be clean. Cached active-in-hierarchy is GameObject+0x47 and must be initialized. Pending transform jobs, dirty rectangles, nonfinite values, cycles, rotation/reflection or torn observations decline without calling native code.

Root Canvas+0x238 walks parent canvases; mode +0x30 must be screen-space camera, scale +0x244 positive, camera ID +0x34 present. The passive warm instance-cache lookup uses the statically traced hash/probe sequence and is bounded to 256 probes. It never calls the cold resolver. Camera orthographic flag +0x513, size +0x450, full viewport +0x41c and transform must agree with the root canvas. Supplemental Unity assemblies are separately hash/MVID pinned.

With world rectangle `(x,y,w,h)` and root canvas `(cx,cy,cw,ch)`, client projection is:

```text
clientX = (x-cx)/cw * clientWidth
clientY = (cy+ch-y-h)/ch * clientHeight
clientW = w/cw * clientWidth
clientH = h/ch * clientHeight
```

The camera must cover the full client and canvas aspect must agree with client aspect. Client pixels are divided by the HWND DPI scale only for Avalonia presentation. No empirical row pitch, screenshot, OCR or template fallback is used.

## Scroll and culling

```text
range = max(0, contentHeight - viewportHeight)
pixelOffsetFromNormalized = (1 - normalizedValue) * range
pixelOffsetFromGeometry = max(0, viewportTop - contentTop)
```

The two offsets must agree within one client pixel. Short lists have zero range. Top/middle/bottom orientation is implemented consistently with the audited ScrollRect semantics and native positions; physical scrolling still needs confirmation. The public [Unity ScrollRect source](https://raw.githubusercontent.com/Unity-Technologies/uGUI/main/com.unity.ugui/Runtime/UGUI/UI/Core/ScrollRect.cs) supports the normalized-position semantics, not the exact Arena offsets.

Each row exports full rectangle, viewport intersection, visible/culled/clipped flags, source path, collection index, visual index, exact printing, logical identity, quantity and generation. Inactive or fully offscreen rows are culled; partial rows use the intersection. Main and side occurrences remain separate even for duplicate identities.

## Retirement and presentation

Pack and deck use separate existing `CaptureFreeUiStability` gates. Scroll/row/window changes retire the deck immediately and require repeated fresh reads. Movement changes the overlay origin without changing client geometry or requesting identity relocalization. Resize/DPI/window visibility invalidate both geometry gates; accepted picks clear debug/ordinary deck markers on the UI thread. Previous-generation deferred render diagnostics are rejected. Ordinary deck markers also require a current reconciled pack and complete pool/deck occurrence agreement.

Debug mode shows row outlines/names, ×quantity, printing, row rectangle, pixel scroll and generation. The ordinary overlay adds only an intrinsic `Q 0.xx` marker, using the existing quality normalization and narrow prior. It is not a contextual pick score or a deck-builder recommendation. Its physical gate is deliberately narrower: non-scrollable main lists at 1600×900 / 96 DPI only. Other sizes/DPI and scrollable lists remain debug-only. A visible intersection must be large enough to contain the compact marker.

Unsupported deck geometry suppresses deck markers and never invokes capture or disables a separately valid pack. Sideboard positions are diagnostic; expansion/visibility is not inferred solely from the side holder. Unknown, animated, moving, null or torn holder states decline.

Only meaningful state changes are written. Stable repeated placement calls are deduplicated; batched performance samples are separately recorded. Historical log replay pick events are distinguished from retirement of an actually placed live pack.

## Evidence, performance and status

[Physical checklist](PHASE_9F4_PHYSICAL_VALIDATION.md) contains the exact user confirmations and pending matrix. The current live Forest preferred printing passes; the Swamp metadata proof succeeds, without claiming a new physical Swamp observation. The pack matrix currently confirms 14/3/1 cards at the primary size; 10/5 cards and second size remain pending.

Build: `01443eeb5ab5cfb889991fcc45f72efb96e634dc` plus the existing working tree and these changes. The normal solution build passes with zero warnings/errors; 1,312 C# tests and all 40 frozen scoring goldens pass. Seven targeted new tests protect world order/projection, scroll/culling, narrow printing proof, occurrence conservation/ambiguity, stat grouping/risk and independent UI/physical gates. Earlier obsolete diagnostic assertions were updated without weakening null-geometry suppression.

[Validation](docs/evidence/phase-9f4/validation.json) records source/assembly/input hashes. [Live summary](docs/evidence/phase-9f4/live-summary.json) records measured read/attach, identity traversal, geometry traversal, projection/model timing and event counts. In the final reviewed ledger prefix, attach was 780.658 ms; non-attach pack reads had median 186.003 ms / P95 207.112 ms (299 samples, including declines). Detailed state-change samples had identity traversal median 7.833 ms, geometry median 110.502 ms, projection median 0.0133 ms and debug model median 0.1142 ms. Geometry is the dominant cost. These are actual passive samples, not a guarantee of responsiveness or a measured compositor time. Scroll response timing is unavailable until a live scroll occurs.

OpenProcess rights remain **0x410** (query/read only). No target write/allocation/thread/injection/managed call/debugger, capture initialization or pixel processing was added. Unknown builds fail closed. Static analysis read local PE bytes only.

The implementation and stat reviews are ready, but **Phase 9F.4 physical qualification remains incomplete**. Finish the listed user-driven matrix before calling pack localization broadly qualified or enabling ordinary deck markers for scrollable/second-size/DPI states. No production scoring or architecture promotion, and no next phase, is authorized by this report.
