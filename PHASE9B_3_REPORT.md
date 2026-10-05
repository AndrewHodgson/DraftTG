# Phase 9B.3 - Arena Visual Card Placement Order

Implemented a conservative, explicit per-pack visual map. Badges no longer use log occurrence index as visual placement. Unconfirmed badges are hidden with a rail diagnostic. **619 tests pass**, including the existing 612 and seven focused new regressions; build has **zero warnings/errors**. Physical Windows acceptance remains pending. No Phase 9C work exists.

## Root cause

`CardBadgeViewModel.Index` represents the logical/log occurrence. `OverlayViewModel.PlaceAll` previously used `_layout.Slots[badge.Index]`, assuming that index was also the visible card position. The statistics and recommendation objects were already correct; the geometry lookup put them over different images.

The confirmed nine-card screen is not simply the reverse of the log:

| Visual slot (1-based) | Card | Log occurrence index (0-based) |
| --- | --- | ---: |
| 1 | Hatching Plans | 8 |
| 2 | Stonesplitter Bolt | 7 |
| 3 | Rat Out | 6 |
| 4 | Grabby Giant // That's Mine | 3 |
| 5 | Merry Bards | 4 |
| 6 | Redcap Thief | 2 |
| 7 | Return from the Wilds | 1 |
| 8 | Gingerbrute | 5 |
| 9 | Scarecrow Guide | 0 |

The UI shows the canonical split name for the card whose Arena face is Grabby Giant. Identity, exact statistics lookup and existing missing-statistics behavior for that split name remain unchanged.

## Raw Arena data and sort investigation

Read-only inspection found **one** available `BotDraftDraftStatus` pack observation, P1P6. The actual payload fields are:

`Result`, `EventName`, `DraftStatus`, `PackNumber`, `PickNumber`, `NumCardsToPick`, `DraftPack`, `PackStyles`, `PickedCards`, `PickedStyles`.

`DraftPack` is the serialized card-ID array; `PackStyles` and `PickedStyles` are empty in this payload. There is no display index, original-pack position, visual-order array or card-position field in the observed data. The parser's `ParseBotDraftStatus` copies `DraftPack` to `ArenaDraftPackPresentation.CardIdentifiers` in source order. Its result holds draft identifier, coordinate and identifiers only. Human-pick `GrpIds` represents selected cards, not screen slots. No parser field was changed or discarded by this fix.

Compared the confirmed screen with Arena IDs, colors, mana value, rarity, collector numbers, canonical names, log order and the local Scryfall stream. The local stream order also differs from the screen and is not a visual-placement contract. Original physical booster order is not separately supplied.

| Candidate | Fits this observed pack? |
| --- | --- |
| Serialized log order | No |
| Reversed log order | No |
| Arena ID ascending or descending | No |
| Mana value ascending | No |
| Canonical name ascending | No |
| Collector number ascending across all cards | No |
| Rarity, color, name | Yes |
| Rarity, color, collector number | Yes |

The two matching candidates are ambiguous, and neither has evidence from multiple real packs. Rarity or color alone does not determine order within a group. Therefore **no automatic Arena sorting rule was implemented**. Audit output: `artifacts/phase9b-3/order-audit.json`. The fixture records the user-confirmed order as evidence, not as a production hardcoded card-name exception.

## Mapping strategy and UI

`VisualCardPlacement` owns a read-only `(PackIndex, CardIdentifier) -> visual slot` map and its immutable `DraftPack`. A complete mapping must include every current occurrence exactly once, with no duplicate/foreign key and a size within the existing 14-slot limit. Duplicate identifiers remain independent keys. Caller ordering supplies explicit visual evidence; it never supplies statistics identity.

In the rail's **status panel**, open **Match card positions**, select the visible card for every numbered slot, then choose **Confirm card positions**. Count slots left to right, then top to bottom. If needed, use the existing overlay editor to match columns and geometry first. Same-name occurrences get copy labels in the selector; the key still identifies the exact logical copy.

The App guards assignments with a `VisualPlacementContext` reference/generation and the current pack identity. Stale UI callbacks cannot apply a previous map to the next pack, even if its value happens to compare equal. Selection edits invalidate placement immediately; incomplete/duplicate/foreign assignments keep all badges hidden. New packs, pack shrink and session clearing discard the map. Statistics updates preserve a valid map, so changing rank/gold does not move a card.

Mapping is intentionally session-local and per pack. Surviving-card identity alone does not establish its new visual position, so mappings are not automatically shifted or reused after a pick. This is the conservative solution authorized in the request, rather than an unverified sort/reverse heuristic.

## `PlaceAll`, rendering and calibration

`PlaceAll` now looks up the badge's full occurrence key in the confirmed map, then indexes geometry by the resulting **visual slot**. `CardBadgeViewModel.Index` remains a log index for identity/debug compatibility; the independent nullable `VisualSlotIndex` determines placement.

Without a valid map, `ClearPlacement` removes the visual index/coordinates. The shipped passive Border binds visibility to **`IsPlaced`**, so unknown positions do not render metric badges. The transparent host surface may still be present, but no card metrics are shown. The rail exposes an unconfirmed-position warning. Guides show the mapped card name at its visual slot, or only the slot number before confirmation, never a guessed log-order card name.

`DraftCardLayout`, normalized geometry, existing column choices, calibration persistence and native click-through/focus policy are unchanged. A five-column profile's first nine slots form five top/next four bottom positions. The existing profile allocates geometry for up to 14 slots; no universal two-row rule or pack-size-dependent rescaling was invented. Tests check 14 down to 1 cards within all four existing column profiles. If Arena changes size/row distribution, the user must edit the existing geometry/profile before confirming the new pack.

Diagnostics from the final bound items include name/identifier, log occurrence index, visual slot, raw GIH/ALSA, context rank/top flag and X/Y. Visual indices in diagnostics are zero-based; UI labels are one-based. The P1P6 regression verifies `Hatching Plans: log 8 -> visual 0` and `Scarecrow Guide: log 0 -> visual 8`. Unconfirmed items explicitly report `Visual slot: unmapped`.

## Exact files changed

- `src/DraftTG.Application/VisualCardPlacement.cs` (new immutable map and validation)
- `src/DraftTG.App/VisualSlotAssignmentViewModel.cs` (new per-pack context and slot selectors)
- `src/DraftTG.App/MainWindowViewModel.cs` (exposes immutable current pack identity)
- `src/DraftTG.App/OverlayViewModel.cs` (mapping lifecycle, confirmation, placement and diagnostics)
- `src/DraftTG.App/CardOverlayWindow.axaml` (per-item placement visibility gate)
- `src/DraftTG.App/RailPanelView.axaml` (card-position assignment controls)
- `src/DraftTG.App/OverlayDesktopSession.cs` (routes the confirm action)
- `tests/DraftTG.App.Tests/VisualPlacementTests.cs` (new seven focused tests)
- `tests/DraftTG.App.Tests/DraftTG.App.Tests.csproj` (copies the new fixture)
- `tests/Fixtures/arena-woe-p1p6-visual-placement.json` (new observed pack, relevant catalog/metric rows and confirmed visual IDs)
- `ARCHITECTURE.md`, `ROADMAP.md`, `PHASE9B_3_REPORT.md`

The SHA-256 scope audit confirms RecommendationEngine, provider/Data, ArenaIntegration/parser and Domain sources are unchanged. All existing test source files are unchanged; only the App test project adds its fixture-copy entry. Earlier work and `global.json` were preserved. No scoring, raw metric mapping, caps, tie breaking, endpoint, format or cache changes were made.

## Seven new tests

1. **Exact P1P6:** replay the real-shaped payload through the actual Arena parser/state engine/adapter, active-card-first mapper, all recommendation stages and the shipped bound `Badges` collection. Apply the confirmed visual assignment. Every slot keeps its own identity, GIH, ALSA and rank; Hatching Plans is **slot 1 / 60.6% / ALSA 5.22 / #1**, Scarecrow Guide **slot 9 / 55.6% / ALSA 6.95 / #8**. Check all nine items and five-plus-four geometry. The approximately 57.8% baseline is explicit fixture input, not a newly fetched live baseline claim.
2. **Unconfirmed:** no item is placed/renderable; shipped XAML has the `IsPlaced` gate and the rail explains how to assign cards.
3. **Invalid mapping:** partial, duplicated, foreign or wrong-occurrence keys fail closed, including attempts to replace a previously valid map.
4. **Rank change:** a new top recommendation retains each assigned visual index/coordinates and its own raw values; gold follows its new occurrence rank, not visual slot zero.
5. **Shrink 14 through 1:** new states discard maps and stale contexts; explicit new assignments retain all identities/values and use valid unchanged slots for each existing column profile.
6. **Actual selector flow:** selections require confirmation, edits hide badges, duplicate choices are rejected, and old selectors/forged contexts cannot alter a new valid map.
7. **Duplicates:** identical card IDs retain separate logical keys, copy labels, ranks and visual slots while sharing legitimate raw evidence.

These are mapping/presentation tests, not another scoring suite. Recommendation order and the shared immutable presentation objects remain unchanged.

## Validation and physical status

Ran `dotnet build DraftTG.sln` and `dotnet test DraftTG.sln --no-build`.

| Suite | Passing |
| --- | ---: |
| Domain | 42 |
| RecommendationEngine | 102 |
| Data | 134 |
| ArenaIntegration | 94 |
| Application | 135 |
| App | 112 |
| **Total** | **619** |

Zero warnings/errors, zero test failures/skips. Final logs and scope audit are in `artifacts/phase9b-3/`.

**Windows physical validation was not performed.** Arena was running and its local pack data was inspected, but the Windows computer-use `node_repl` / `@oai/sky` runtime failed to start: `The system cannot find the path specified. (os error 3)`. Retry after kernel reset failed too. No screenshot/window capture, visible rendered control check or subsequent physical pick was possible. The exact screen order comes from the user's confirmed evidence; automated replay is not physical validation. macOS physical validation was not performed in this Windows environment.

Remaining acceptance: run the rebuilt app, confirm the nine card slots with the existing five-column geometry, compare both named cards and three others, verify rail versus gold #1, then make a pick and assign the smaller pack. The product deliberately hides badges until that assignment is confirmed. Automatic placement remains unavailable until multiple real observations or an authoritative visual-order source establish a reliable rule. Duplicate indistinguishable copies can be assigned as logical copies; there is no claim to recover which physically identical copy Arena rendered first.

Stopped after Phase 9B.3. Phase 9C has not begun.
