# Phase 10D audit and semantic foundation

Status: **in progress, not a completion report**. Production deck-builder localization and add/remove highlighting have not been implemented. The required real visual audit and current unsaved-edit observation remain outstanding. No Arena clicks, navigation, scrolling, deck edits or submission were automated.

## Measured log behavior

The initial October 4 Player.log was 29,410,871 bytes, 64,575 lines, last modified at 11:27:47 AM CDT. Its public structural audit was preserved before Arena restarted and rotated the log. Historical line references below apply to that original log, not the replacement live file.

| Original line | Observed record | Meaning |
|---|---|---|
| 551 | Draft → DeckBuilder scene | Builder appeared after draft completion. |
| 560 | Outgoing EventSetDeckV3 request | Proposed saved deck; not proof of accepted or current unsaved contents. |
| 561 | Global timestamp, 9:05:32 AM CDT | Log timestamp, not a deck revision identifier. |
| 562–563 | Save response followed by direct CourseDeck | Exact accepted saved main/sideboard quantities. |
| 564 | DeckBuilder → EventLanding | Builder left immediately after the observed save. |

The direct CourseDeck carries InternalEventName and main/sideboard arrays of integer `cardId` and `quantity`. The audited ordinary Limited response has empty companion/command zones. Nested Courses also repeat saved decks; they do not prove current editor membership. No deck-specific revision number was observed. No incremental add/remove or full unsaved-edit snapshot was identified in this sequence.

**Inference:** this sequence is consistent with save-time reporting. **Not determined:** whether every manual unsaved edit, or any separate builder configuration, emits useful current counts. A live add/remove probe is necessary to decide that; absence in the historical sequence is not conclusive.

During this audit Arena was initially closed, then launched into EventLanding for FRA_Premier_Draft and later moved through BoosterChamber/Achievements to Home. Current course data did not expose the historical WOE event, which had an earlier claim-prize record. This does not establish whether the WOE deck remains reachable elsewhere. The user was asked to open the completed builder manually; no ready confirmation was received during the audit. Read-only log watching observed no DeckBuilder transition. The explicit capture command refused EventLanding before saving any image.

## Historical WOE counts and comparisons

The exact saved WOE deck has 40 cards: 23 drafted spells, Evolving Wilds, 8 Mountains and 8 Forests. Its sideboard has 18 drafted copies. The 24 main-deck nonbasic copies plus 18 sideboard copies conserve the completed 42-card inventory. The public fixture resolves the exact Arena stock basics 75900/75903 through the local catalog.

These are **historical saved comparisons, not verified current-editor guidance**. The existing Phase 10C target lists were used unchanged.

| Target | Add drafted copies | Remove drafted copies | Basic changes |
|---|---:|---:|---|
| Build 1 — RG | 5 | 6 | +1 Mountain |
| Build 2 — GU (canonical UG) | 11 | 12 | +8 Island, +1 Forest, −8 Mountain |

Build 1 adds Candy Trail, Eriette's Tempting Apple, Edgewall Pack, Merry Bards and Skewer Slinger, one each. It removes Evolving Wilds, Skybeast Tracker ×2, Season of Growth, Return from the Wilds and Night of the Sweets' Revenge.

Build 2 adds Obyra's Attendants, Candy Trail, Bestial Bloodline, Eriette's Tempting Apple, Sleight of Hand ×2, Into the Fae Court ×2, Merfolk Coralsmith, Compulsion and Toadstool Admirer. It removes Evolving Wilds, Charming Scoundrel, Ruby, Daring Tracker ×2, Season of Growth, Dragon Mantle, Flick a Coin, Ratcatcher Trainee, Redcap Thief, Cut In, Bellowing Bruiser and Night of the Sweets' Revenge. Full public counts are in [saved-woe-comparison.json](artifacts/phase10d-audit/saved-woe-comparison.json).

## Implemented independent foundation

- Domain `ArenaDeckSnapshot`, entries, separate basic types and `ArenaDeckRevision`: immutable aggregated counts, explicit Complete/Partial/Unknown, source and SavedDeck/CurrentEditor scope. Exact saved snapshots never qualify as trustworthy current counts.
- Separate `ArenaDeckLogParser`: audited scene records and direct server saved CourseDeck only. Requests, nested history, completion PickedCards and unrelated JSON are ignored. Unsupported or malformed observations fail closed without changing draft parsing.
- `ArenaSavedDeckAdapter`: exact identity resolution, basic separation and owned main-plus-sideboard multiset validation. Missing identities/counts remain partial. Scope always remains SavedDeck.
- `ArenaDeckComparisonTarget.From`: uses SuggestedDeckSet.Selected, retaining drafted nonbasics and separating generated basics.
- `ArenaDeckDifference`: exact count additions/removals/already-correct entries, build/revision identities and sizes. Basic changes are separate. Missing, incomplete, saved-only or mismatched-session observations return empty guidance and CurrentDeckUnknown. No live source, rail or outline is wired to these APIs yet.
- Explicit developer WGC capture command: scene/geometry checked before and after acquisition; cropped Arena-window-only image plus local diagnostics; no automatic persistence or input. This is capture preparation, not a tile-localization harness.

The revision number is caller-assigned and session-local. The foundation preserves revision identity in comparisons and rejects a different session; apply-time localization generation/revision/build fences are not yet implemented or tested. A future observation coordinator must supply monotonic revisions and clear state at source/session boundaries.

## Visual findings and implementation gate

No current real completed-draft builder image was obtained, so the following remain **unknown**: Pool/MainDeck/Sideboard zone rectangles, tile vs list-row layout, quantity badges/stacking, dimensions/sort order, scroll virtualization, filters and basic-land representation. There are no measured artwork best/second scores, expected crop alignments or confidence/ambiguity results. No screenshot regression fixture or OCR dependency was added.

The pending strategy is constrained known-pool artwork identity for visible tiles, with separate row localization if the measured main-deck layout requires it. Duplicate visual quantities must come from observed behavior. Add belongs on addable pool cards; Remove belongs on main-deck cards. Hidden and ambiguous cards cannot receive invented rectangles. Any local OCR evaluation requires evidence that art crops are insufficient. Existing draft thresholds remain 0.94 acceptance and 0.07 ambiguity.

A dedicated passive deck overlay will reuse native click-through/non-activation only after the audit. Selected build, current revision, session, localization generation and scene/geometry must fence result application. Event-driven WGC stays primary; no continuous loop or capture architecture change was made. Mandatory basic guidance belongs on the rail once current counts are trustworthy. The production rail comparison, highlights, live edit response, build-switch outline clearing and scroll refresh are all pending.

## Explicit capture command

From the repository root, after the user manually opens the builder:

```powershell
dotnet run --no-build --project src/DraftTG.App -- --capture-deck-builder --log "$env:USERPROFILE\AppData\LocalLow\Wizards Of The Coast\MTGA\Player.log" --output artifacts/phase10d-audit/captures
```

Optional `--region x,y,width,height` uses normalized Arena-window coordinates. The default `(0, 0.13, 1, 0.83)` is an audit crop, **not an identified deck-builder zone**. Capture is refused unless the latest canonical scene is DeckBuilder. A changed scene record/window geometry discards the result. Diagnostics explicitly state current editor counts are unconfirmed. Before creating a regression fixture, inspect and further crop the captured game content to remove any private names. Do not commit the raw debug capture.

## Tests and validation

Twenty focused test cases were added: 12 snapshot/selected-target/count-difference cases, 2 real saved-deck adapter cases and 6 parser cases. They cover the requested A/B/C/D multiplicities, separate basics, nonbasics, exact matches, selected Build 2 conversion, fresh observed revisions, unavailable/incomplete/saved-only/session-mismatched observations, immutable aggregation, actual saved WOE counts and unrelated/malformed records. The pure revision/build tests do not establish a live overlay pipeline.

| Suite | Before | Added | Passed now | Failed/skipped |
|---|---:|---:|---:|---:|
| Domain | 56 | 0 | 56 | 0 |
| RecommendationEngine | 163 | 0 | 163 | 0 |
| Data | 151 | 0 | 151 | 0 |
| ArenaIntegration | 114 | 6 | 120 | 0 |
| Application | 158 | 14 | 172 | 0 |
| App | 225 | 0 | 225 | 0 |
| Total | 867 | 20 | 887 | 0 |

Both `dotnet build DraftTG.sln` and `dotnet build DraftTG.sln -p:DraftTGPortableBuild=true` passed with **zero warnings/errors**. Both corresponding full `dotnet test ... --no-build` runs passed 887/887. TRX identity comparison retained every prior 867 case with no missing tests. See [test-retention.json](artifacts/phase10d-audit/test-retention.json) and the final foundation build/test logs in `artifacts/phase10d-audit`.

SHA-256 comparison of the pre-audit source/test inventory found only three changed existing files: Program.cs for explicit CLI dispatch, and two test project files for public fixture copying. Scoring, providers, deck construction/order, draft parsing/state, runtime capture and artwork matcher source hashes remain unchanged. No previous test source was modified. This check does not rely on the damaged Git index. See [source-change-summary.json](artifacts/phase10d-audit/source-change-summary.json).

Semantic comparison performance can be reproduced with `dotnet run --project artifacts/phase10d-audit/semantic-performance/SemanticDifferenceAudit.csproj -c Release`. The measured median was **0.0299 ms**, P95 **0.0742 ms**, over 10,000 warmed synchronous comparisons on explicitly synthetic 40-card decks, not live Arena observations. Full results are in [semantic-performance.json](artifacts/phase10d-audit/semantic-performance.json). Builder localization and highlight-model generation cannot be measured before implementation.

## Files and remaining validation

New production foundation files: `src/DraftTG.Domain/ArenaDeckSnapshot.cs`, `src/DraftTG.ArenaIntegration/ArenaDeckLogParser.cs`, `src/DraftTG.Application/ArenaSavedDeckAdapter.cs`, `src/DraftTG.Application/ArenaDeckDifference.cs`, and `src/DraftTG.App/DeckBuilderCaptureAuditCommand.cs`. Program.cs has the explicit command dispatch. New tests have corresponding parser/adapter/difference filenames; both Application and ArenaIntegration test projects copy the public saved fixture. Three `arena-woe-saved-deck-*` fixture files record public observations and provenance. ARCHITECTURE.md and ROADMAP.md describe the incomplete state. Audit artifacts contain public structure/counts, verification helpers, test/build results and the synthetic performance harness; no builder screenshot was saved.

Windows physical validation is **not performed**. Need a real builder capture, a user-performed add/remove observation, measured tile/quantity mapping and then manual outline/build-switch/scroll/click-through acceptance. If the old WOE deck is unavailable, an accessible completed deck can establish the visual/editor-source audit, while real WOE comparisons remain historical until that deck is confirmed. Portable compilation/tests pass, but native macOS deck-builder visual capture/localization and physical execution remain unimplemented/unvalidated.

No Phase 10E work, new deck builds, strategy heuristics or input automation was introduced. Phase 10D remains open until its current-editor and visual acceptance requirements are fulfilled.
