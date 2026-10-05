# Phase 9B.1 - Current-Pack Statistics Association Integrity

Implemented the occurrence identity fix. Automated validation passes with **605 tests**, including all 594 existing tests and 11 focused new regressions. Build: **zero warnings, zero errors**. Phase 9C has not begun.

## Root cause and evidence

The vulnerable association boundary was in Application-to-App presentation, not the provider metric decoder:

- `MainWindowViewModel.ApplyStatisticsUpdate` retained a row's name but took its identifier from the current snapshot and its three recommendation records from independent lists at the same index. Rail names also came from row indexes.
- `CardBadgeViewModel` froze its name when created, then independently replaced statistics and recommendation fields. `OverlayViewModel.RefreshPack(false)` selected those replacements by collection index without checking card identity.

Two tests deliberately move the row collection to reproduce this unguarded boundary. Before the fix, both fail with Hatching Plans expecting `60.6%` but receiving `55.6%`. The second moves rows during the presentation-change event, after the rail has been computed: the rail correctly says Hatching Plans while its named badge shows Scarecrow Guide's metrics. This reproduces the reported mathematical inconsistency. Saved pre-fix outputs are `artifacts/phase9b-1/before-regression.log` and `before-badge-regression.log`.

The tests establish a concrete cross-card presentation failure and now pass. They introduce ordering drift explicitly. The audit did not find a normal production path that deliberately reorders this collection, and the original live session was unavailable. Therefore the initiating event in that session is **not established**. If the values were only positioned over the wrong Arena images, a difference between Arena visual order and logged order or calibration placement remains a possible explanation requiring physical inspection. This report does not claim that geometry was corrected or that the original live session was replayed.

## Pipeline audit

| Boundary | Finding |
| --- | --- |
| `/api/card_data` envelope and Data DTO | `data` rows decode `ever_drawn_win_rate`, `ever_drawn_game_count` and `avg_seen` into GIH/sample/ALSA. Provider order is not card identity. Unchanged. |
| Arena/Scryfall resolver | Uses explicit Arena ID to Domain identifier mappings. Supplemental printings do not redirect an already resolved active identifier. Unchanged. |
| `LimitedStatisticsMapper` | Starts from each active identifier, finds its exact Domain card, then uses an ordinal exact name lookup. Conflicting provider rows remain unavailable. Now preserves the actual matched row name for diagnostics. |
| `LimitedCardStatisticsCatalog` | Identifier-keyed frozen lookup. Same-name active printings can legitimately share aggregate evidence without losing their distinct IDs. Unchanged. |
| Environment mapping | Deterministic printing representatives contribute only to the baseline; they never choose a current-pack printing. Unchanged. |
| Phase 8 | Creates original-order occurrences with ID and pack index. Sorts copies to assign rank, then writes ranks to those original occurrence indexes. Unchanged. |
| Phase 9A / 9B | Validate ordered IDs/indexes against their input pack, retain nested stages and sort copies for ranks. No rank-order-to-display zip exists. Unchanged. |
| Application update | Now joins each stage by full occurrence key and raw statistics by card identifier, producing immutable presentations. |
| Rows / rail | Rebuild complete rows in Arena order from those models. Rail names use top flags on the same models. Contextual formatters no longer retrieve a candidate by collection index. |
| Badges | Refresh by occurrence key and replace one whole row reference. Foreign keys, inconsistent models or names are rejected. |

The remaining index operations write complete rows to the display collection, place badges in calibrated slots, or assign rank to an already identified original engine occurrence. They do not resolve card statistics. Arena history's existing parallel coordinate/card construction comes from the same resolved pick records; it is unrelated to current-pack statistic association and is unchanged.

Names are neither stripped of punctuation nor case-folded nor split at `//`. Exact split/special names remain distinct from prefixes and case variants. The new name test covers punctuation-distinct names, split-name prefixes/case variants and two legitimate supplemental/older printings sharing an aggregate row.

## Identity and state changes

`CardOccurrenceKey(PackIndex, CardIdentifier)` identifies one occurrence **within its owning pack**. Duplicate identifiers remain separate occurrences. `CurrentPackCardPresentation` holds the Domain card/name, lookup and actual resolved names, raw GIH/sample/ALSA, Phase 8 adjusted value/rank, Phase 9A color adjustment/value/pool rank, and Phase 9B lane adjustment/final value/context rank/top flag.

The constructor checks raw and recommendation identifiers, occurrence indexes, nested-stage consistency and any known resolved name. A mismatch produces missing evidence/ranks and a rejected diagnostic. Identity-bearing rows read every metric and recommendation property from this model, so independent formatter initializer values cannot override it. Badges keep the matching immutable row as one unit and ignore foreign replacement attempts without partial mutation.

`LimitedStatisticsUpdate.PackIdentity` exposes the existing immutable `DraftPack`: position plus ordered identifiers. UI application requires both that identity and the exact current snapshot reference. Existing coordinator session generation, observation revision, cancellation and snapshot-reference checks remain unchanged. A value-equal pack from a different snapshot is still rejected. No additional session owner, polling loop or mutable pack state was introduced.

A new pack rebuilds all occurrence keys, including shrinking Quick Draft packs. Removing A from A/B/C/D/E produces B/C/D/E with new occurrence indexes but their own identifier-keyed metrics. Presentation collection movement does not change badge identity or geometric slot. Collapsed diagnostics include every occurrence's key, Domain name, lookup/resolved name, raw evidence, all stage values/adjustments/ranks and identity status. Passive badges and native policies are unchanged.

## Exact files changed for this phase

- `src/DraftTG.Application/CurrentPackCardPresentation.cs` - new key and immutable occurrence presentation with identity checks and diagnostics.
- `src/DraftTG.Application/LimitedStatisticsUpdate.cs` - owning pack identity and keyed stage joins.
- `src/DraftTG.Application/LimitedStatisticsMapper.cs` - frozen resolved-name provenance for live participant mapping.
- `src/DraftTG.Application/LimitedStatisticsService.cs` - carries that provenance into load results.
- `src/DraftTG.Application/ContextualRecommendationPresentation.cs` - selects the pool top candidate by flag.
- `src/DraftTG.Application/LaneRecommendationPresentation.cs` - selects the final top candidate by flag.
- `src/DraftTG.App/MainWindowViewModel.cs` - identity-bearing rows, complete row construction, matching rail names, explicit stale-pack check and occurrence diagnostics.
- `src/DraftTG.App/OverlayViewModel.cs` - badge key guards, whole-row replacement and keyed refresh.
- `tests/DraftTG.App.Tests/StatisticsAssociationIntegrityTests.cs` - 11 new tests.
- `ARCHITECTURE.md`, `ROADMAP.md`, `PHASE9B_1_REPORT.md` - invariant, phase status, audit and validation report.

A SHA-256 comparison against the pre-task file inventory confirms no RecommendationEngine, Data, ArenaIntegration or Domain source changes and no modifications to existing tests. The user's `global.json` and earlier phase work were preserved. GIH/sample/ALSA mappings, endpoint, ALL_TIME, cache TTL, format selection, formulas, adjustment caps and tie breaking are unchanged.

## Focused regressions

1. Reordered named rows cannot swap Hatching Plans/Scarecrow Guide statistics.
2. Event-time row movement cannot produce a correct rail with swapped named badges.
3. Current provider JSON envelope through the real HTTP client/decoder, mapper/catalog, all three stages, occurrence model, rows, rail and badges. The two confirmed rows are deliberately reversed. Eighteen synthetic environment rows satisfy whole-dataset validation; the supplied approximately 57.8% environment baseline is explicit test input, not a live baseline claim.
4. A/B/C distinct `51.0% / 3.10`, `62.0% / 7.80`, `55.0% / 5.40` values keep Arena order while ranks differ.
5. Controlled asynchronous Pack A calculation/delivery completes after different Pack B is applied and cannot overwrite its rows, rail or badges.
6. Five-to-four Quick Draft shrink retains every surviving metric and correct rank, removes A and rejects the previous result.
7. Separate Phase 8 Beta and final Alpha picks remain consistent after row movement; badges use final context ranks while retaining raw GIH.
8. Duplicate Hatching Plans copies share raw evidence but keep different keys and ranks.
9. Foreign occurrence, mismatched resolved name or rank identity cannot partially mutate a badge; independent row initializer values cannot override occurrence evidence.
10. Exact name mapping preserves punctuation/split names and legitimate same-name printings.
11. Same-position reordered packs and value-equal new snapshots both reject an old callback.

The end-to-end fixture verifies Hatching Plans `0.60593147`, `n=46464`, `ALSA=5.2198577`, displayed **60.6% / 5.22**, and Scarecrow Guide `0.55585009`, `n=21880`, `ALSA=6.9474995`, displayed **55.6% / 6.95**. All three stage IDs and occurrence indexes match. Hatching Plans is Phase 8 rank 1 under the explicit 57.8% baseline; Stats Pick and final Context Pick match their current top occurrences.

## Validation and remaining physical work

Commands: `dotnet build DraftTG.sln` and `dotnet test DraftTG.sln --no-build`.

| Suite | Passing |
| --- | ---: |
| Domain | 42 |
| RecommendationEngine | 102 |
| Data | 134 |
| ArenaIntegration | 94 |
| Application | 135 |
| App | 98 |
| **Total** | **605** |

Zero failures, zero skipped tests, zero build warnings/errors. Final command logs are under `artifacts/phase9b-1/`.

**Windows physical validation: not performed.** No MTGA or DraftTG process was running at the checks; no active Arena draft was available. **macOS physical validation: not performed**, because this is a Windows environment. Automated cross-platform project compilation is not physical validation.

The remaining check is a real WOE Quick Draft: compare both named cards and at least three additional cards with the direct current API response, inspect occurrence diagnostics against visible Arena images/tooltips, make a pick, and confirm surviving values, rail names and gold rank 1 across the next update. Verify actual visual/log order and calibration placement as well as software identity. Repeat physical presentation checks on macOS when available. No Arena input automation was added.

Stopped after Phase 9B.1.
