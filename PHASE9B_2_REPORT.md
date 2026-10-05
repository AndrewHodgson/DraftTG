# Phase 9B.2 - Live Badge Binding Integrity

**Binding refactor implemented; the reported physical placement swap remains unresolved.** Build passes with zero warnings/errors. All **612 tests pass**: the existing 605 plus seven focused regressions. No Phase 9C work was added.

## Actual passive path and root-cause evidence

`OverlayDesktopSession` constructs one `OverlayViewModel` and passes that same instance to both the rail and `CardOverlayWindow`. The passive window's XAML binds `ItemsSource` to **`Badges`**. Its item template is typed as `CardBadgeViewModel`; Canvas placement binds the item's X/Y.

Before this phase, `OverlayViewModel.RefreshPack` created/refreshed badge wrappers from the separate `MainWindowViewModel.CurrentPackCards` collection. Phase 9B.1 had already keyed those refreshes and made row metrics derive from occurrences. The source audit found no second passive badge window, rank-sorted badge collection or remaining `statistics[i]`/`recommendations[i]` join in that active path. It would be inaccurate to claim that an old rank-sorted statistics producer was discovered.

There **is** a remaining positional placement assumption: `CardBadgeViewModel` sets `Index` from `PackIndex`, and `OverlayViewModel.PlaceAll` uses `_layout.Slots[badge.Index]`. This assigns a log-order occurrence to a visible geometric slot without verifying the card image at that slot.

Read-only inspection of the current local P1P6 `BotDraftDraftStatus` response and Scryfall/17Lands caches found:

| Log index | Arena ID | Domain card | Raw GIH | ALSA |
| --- | ---: | --- | ---: | ---: |
| 0 | 86982 | Scarecrow Guide | 0.55585009 | 6.9474995 |
| 1 | 86889 | Return from the Wilds | 0.55529606 | 5.4564476 |
| 2 | 86849 | Redcap Thief | 0.5872322 | 5.3453346 |
| 3 | 86831 | Grabby Giant // That's Mine | unavailable | unavailable |
| 4 | 86840 | Merry Bards | 0.57558255 | 6.3894335 |
| 5 | 86978 | Gingerbrute | 0.57460169 | 4.7623357 |
| 6 | 86799 | Rat Out | 0.59775495 | 5.311911 |
| 7 | 86853 | Stonesplitter Bolt | 0.57034551 | 2.4395601 |
| 8 | 87058 | Hatching Plans | 0.60593147 | 5.2198577 |

The request's visual-order example places Hatching Plans first, Stonesplitter Bolt second and Scarecrow Guide last. These are the opposite endpoints of the actual log list. Correctly bound Scarecrow data at geometric slot 0 and Hatching data at geometric slot 8 would produce exactly the reported apparent swap if those visible endpoints are confirmed. This is evidence for a log-order-versus-screen-order placement problem; it does not prove that every pack or Arena sort setting should be reversed.

Saved evidence: `artifacts/phase9b-2/live-log-evidence.json`. Only relevant card identities/metrics were saved, not the full user log. The split-name missing statistic above is an existing exact-name mapping behavior; provider/name mapping was not changed in this presentation phase.

**The binding changes below do not change this geometric assumption.** Without the actual visible order or working Windows inspection, a blanket reversal or rarity sort would be an unverified fix. The user was asked for the current left-to-right/top-to-bottom order. Live visual placement remains an outstanding requirement, not a completed check.

## Changes made

- `MainWindowViewModel.CurrentPackPresentations` is a complete immutable collection of the exact occurrence objects used for rail names. It is set after pack construction/statistics association and cleared with the pack.
- `OverlayViewModel.RefreshPack` reads only this snapshot. Its former production dependency on `CurrentPackCards` is removed. It reconciles existing geometric wrappers by `(PackIndex, CardIdentifier)` and publishes the complete read-only `Badges` list in snapshot order. No recommendation-rank sorting occurs.
- Each badge exposes one `Presentation` reference. `CardOverlayWindow.axaml` reads rank, GIH and ALSA directly through that reference. Gold/rank color and identity properties derive from it too. Replacing the reference notifies the nested compiled bindings as a unit.
- `CurrentPackCardPresentation` adds presentation-only formatted GIH/ALSA/context-rank properties. No raw metric mapping, scoring, caps or ranking changes were made.
- `BadgeBindingDiagnosticsText` captures **final bound objects**, after collection construction and placement: display index, pack index, identifier/name, raw GIH, ALSA, context rank, top flag, X/Y. It refreshes on layout/viewport changes, appears in the collapsed rail diagnostics, and is written to Trace.
- Full-suite testing exposed a waiting-state callback race in the existing guard: `_currentSnapshot` could change after the initial check and before occurrence lookup. The guard now captures one snapshot and checks it before publishing. A focused reentrant transition regression protects this case. The previously failing `ClosingOverlayCancelsPendingStatisticsNormally` test passes without changing that existing test.

No obsolete row-based passive construction/update path remains in production. Internal legacy badge formatter construction/update methods and the App's row projection remain for existing tests/callers; the passive refresh and XAML do not consume them. There is one bound passive collection, not a second statistics/rank collection.

## Exact changed files

- `src/DraftTG.App/MainWindowViewModel.cs`
- `src/DraftTG.App/OverlayViewModel.cs`
- `src/DraftTG.App/CardOverlayWindow.axaml`
- `src/DraftTG.App/RailPanelView.axaml`
- `src/DraftTG.Application/CurrentPackCardPresentation.cs`
- `tests/DraftTG.App.Tests/LiveBadgeBindingTests.cs` (new)
- `ARCHITECTURE.md`
- `ROADMAP.md`
- `PHASE9B_2_REPORT.md` (new)

SHA-256 comparison against the pre-task inventory confirms RecommendationEngine, Data, ArenaIntegration and Domain sources are unchanged. All pre-existing tests are unchanged. Earlier phase work and `global.json` were preserved.

## Seven focused tests

The tests resolve the `ItemsSource` from the embedded **shipped CardOverlayWindow XAML**, then inspect that property on the same `OverlayViewModel` type constructed by `OverlayDesktopSession`. They do not instantiate native windows or claim physical rendering validation.

1. Hatching Plans `60.6% / ALSA 5.22 / #1` and Scarecrow Guide `55.6% / ALSA 6.95 / #8`, with exact raw values and reference equality to the rail's occurrence snapshot.
2. A/B/C/D collection order with contextual order D/B/A/C: displayed ranks #3/#2/#4/#1 and unique GIH/ALSA retained.
3. A/B/C/D to A/C/D shrink retains each card's evidence, updates occurrence indexes/ranks and rejects the old result.
4. Clearing the obsolete row projection during presentation events cannot affect the passive snapshot. Nested XAML bindings receive a `Presentation` notification and wrappers retain their geometric identities.
5. Diagnostics observed at final collection publication describe the exact bound objects, including display index and raw fields.
6. Waiting-state statistics notification cannot adopt a pack that becomes active reentrantly.
7. The shipped XAML's three metric bindings all traverse `Presentation`, with no independent row/recommendation lookup.

## Validation

Ran `dotnet build DraftTG.sln` and `dotnet test DraftTG.sln --no-build`.

| Suite | Passed |
| --- | ---: |
| Domain | 42 |
| RecommendationEngine | 102 |
| Data | 134 |
| ArenaIntegration | 94 |
| Application | 135 |
| App | 105 |
| **Total** | **612** |

Zero warnings, zero errors, zero test failures or skips in final validation. Logs and scope audit are under `artifacts/phase9b-2/`.

## Physical status and remaining work

Arena was running (`MTGA` process), and its live P1P6 log was read. DraftTG was not running at the initial process check. Windows inspection via the computer-use skill's required `node_repl` / `@oai/sky` entry point failed to start with **"The system cannot find the path specified. (os error 3)"**. Initialization retry and kernel reset/retry failed the same way. No screenshot or window observation was obtained, no live card was picked, and no physical verification is claimed. macOS physical verification was not performed in this Windows environment.

To finish the reported placement fix, confirm the current Arena visual order and its relationship to the log using a screenshot/working inspection tool, then supply verified display placement without changing occurrence identity or ranking. Validate both named cards, at least three others, rail versus gold #1, and a subsequent pick. The final badge diagnostics make it possible to distinguish wrong model evidence from a correct model over the wrong image. A view-model-only pass cannot resolve that distinction.

Stopped within Phase 9B.2. Live placement acceptance remains unresolved; Phase 9C has not begun.
