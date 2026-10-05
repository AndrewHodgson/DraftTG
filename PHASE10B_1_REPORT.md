# Phase 10B.1 — Live Draft Completion / DeckSelect Integration

The observed real Quick Draft completion now activates Phase 10B automatically through the normal parser/state/coordinator/view-model path. A replay of current Player.log produces **Draft complete**, a **Complete 42-card pool**, and a **Ready RG baseline of 40 = 23 spells + 17 lands**. No manually created Completed snapshot or direct builder invocation is used.

**Validation:** 839 passing tests on Windows and portable .NET 10, comprising all 828 previous cases and 11 focused additions. Both solution builds report zero warnings/errors. **Physical observation after a fresh live final pick and native macOS execution remain unverified.** The Phase 10B selection algorithm is unchanged. Phase 10C remains unstarted.

## Actual raw framing and sequence

The source is current `C:\Users\theyc\AppData\LocalLow\Wizards Of The Coast\MTGA\Player.log`, read with shared access while Arena is running. The initial source audit is [raw-sequence-audit.json](artifacts/phase10b1-audit/raw-sequence-audit.json). Line numbers refer to individual read snapshots; the live file can grow/change.

The authoritative completion response is a **standalone top-level object**:

```json
{
  "CurrentModule": "DeckSelect",
  "Payload": "<string-encoded draft response JSON>",
  "DTO_InventoryInfo": "<unrelated inventory DTO omitted here>"
}
```

The actual nested Payload contains:

| Field | Observed value |
|---|---|
| Result | Success |
| EventName | QuickDraft_WOE_20260929 |
| DraftStatus | Completed |
| PackNumber / PickNumber | 2 / 13, zero-based P3P14 |
| NumCardsToPick | 1 |
| DraftPack | Empty array |
| PickedCards | 42 string-encoded Arena card IDs |
| PackStyles / PickedStyles | Empty arrays |
| DraftId | Absent |

The inventory DTO contains currencies, inventory changes, wildcards, cosmetics and other unrelated data. It is not a drafted pool. The outer incoming/outgoing UUID is a request correlation identifier, not a draft identifier.

Chronological sequence:

1. BotDraft PickNext response at P3P14: **41 PickedCards**, one available card.
2. Outgoing `[UnityCrossThreadLogger]==> BotDraftDraftPick`: string-encoded `request` with EventName and PickInfo, selecting **87053** at zero-based 2/13.
3. Standalone `<== BotDraftDraftPick(correlation)` marker, with no JSON.
4. Top-level DeckSelect response: successful explicit Completed status, empty actionable pack and **42 PickedCards**.
5. EventGetCoursesV2 response, followed by `Client.SceneChange` from **Draft to DeckBuilder**.

The completion multiset equals the prior 41 plus **one occurrence of 87053**. Array order is not pick chronology. The successful explicit Completed status is authoritative for completion. The scene change only corroborates entering deck building.

The current log has **one** such top-level WOE completion envelope. It also repeatedly references DeckSelect inside Courses/course-detail data. A separate real PremierDraft_FRA course detail has `CurrentModule`, `ModulePayload: "{}"`, `CourseDeckSummary` and `CardPool: 42`; it has no explicit draft Completed status or this response's PickedCards field. Those records are not treated as completion or substituted for the WOE drafted pool. Repeated equivalent top-level completion is tested synthetically because the observed top-level response appears once.

## Reproduced failure and parser change

The original production parser run against the extracted actual five-line fixture yielded:

| Measurement | Before | After |
|---|---:|---:|
| Canonical completion facts | 0 | 1 |
| Ignored DeckSelect envelopes | 1 | 0 |
| Parser warnings | 1 | 0 |
| Status | Active | Completed |
| Drafted pool | 42 | 42 |
| Recovered baseline | 41 | 41 |
| Exact selections in minimal fixture | 1 | 1 |

Evidence: [before-parser.json](artifacts/phase10b1-audit/before-parser.json), [after-parser.json](artifacts/phase10b1-audit/after-parser.json). The original classifier recognized BotDraft modules but not DeckSelect; the response marker also caused a false malformed-JSON warning.

`ArenaDraftLogParser.ParseDeckSelection` now validates the actual **root** CurrentModule, nested Payload, Result=Success, explicit DraftStatus=Completed and draft event/mode. It emits the existing `ArenaDraftLogEvent.DraftCompleted` once per response, enriched with `ArenaDraftCompletionOrigin`, optional mode and `FinalPickedCards`. It does not introduce another completion event type or leak Arena JSON into Application/Domain.

Final PickedCards ingestion is restricted to the observed Quick Draft single-selection shape (`NumCardsToPick=1`). An explicit completion can still preserve already-known inventory when no trustworthy final pool is supplied. Deck, sideboard and generic CardPool arrays are never ingested as drafted inventory. Nested Courses references, bare module names, ModulePayload-only records, failed/noncompleted responses and sealed events do not trigger this path. Existing BotDraft/Human completion remains additive and independent of DeckSelect.

The incoming pick marker is skipped just like the existing incoming status marker. The outgoing request still produces the exact pick. Malformed nested JSON/card arrays retain focused parser errors.

## Authoritative inventory and mismatch policy

The state engine applies completion atomically: mark Completed, clear the actionable pack and retain exact/recovered selections. It compares trusted completion PickedCards to the existing recovered/exact **multiset**, including copy counts.

1. **Already-known pool of 42 and matching completion:** keep existing inventory authoritative, including its recovered baseline object. Do not add 42 incoming occurrences to 42 known occurrences. In the real sequence this preserves recovered 41 plus the exact final selection.
2. **Known pool shorter than 42, trustworthy final snapshot contains every known occurrence:** accept the full 42-card multiset as a coordinate-free recovered pool. This can fill a missing final pick or initialize a completed replay, without inventing historical coordinates.
3. **Unexpected total or missing known multiplicities:** retain all known inventory, do not merge, and report FinalPoolMismatch with recovered/exact count, completion count, expected 42 and the retained-source rule. Existing reliability policy makes the pool Unknown; a viable Phase 10B result is explicitly provisional. A later agreeing completion can clear the warning.

The supported expected total remains the existing 3×14 single-selection Quick Draft contract. No alternative pack sizes or arbitrary deck-to-pool correction are inferred. Unresolved card identities still follow Phase 10A's Partial/Unknown policies.

Repeated agreeing completion is a no-op in the state engine. The session coordinator publishes only changed state, and the unchanged deck coordinator deduplicates identical final inventories. Tests verify one completion update, one statistics request and one baseline build even after three duplicate responses. Identifiable stale responses for another event/draft are ignored.

## New sessions and automatic rail activation

The production flow is now:

```text
raw final pick / DeckSelect response
  → canonical DraftCompleted
  → state Completed + preserved final inventory
  → Domain DraftPoolSnapshot + pool analysis
  → existing asynchronous DeckConstructionCoordinator
  → Baseline Suggested Deck in the rail
```

No completion polling, refresh button or direct developer builder call is introduced. The view model also reconciles a Completed update carrying an inventory warning: it clears the actionable pack and shows Draft complete while retaining the warning and provisional pool/build status.

The current log's EventJoin requests contain `request.EventName`, `EntryCurrencyType`, `EntryCurrencyPaid` and an outer request ID. The observed draft entry is PremierDraft_FRA; Quick Draft was already underway when this log began. The generic entry-request shape is reused by the existing draft-mode parser and tested for a same-event Quick Draft.

A **new explicit entry request after completion** resets even an anonymous session with the same event name. A privately retained entry-request correlation token deduplicates that request, so replaying the same token does not reopen a completed session. It never becomes DraftIdentifier or Domain identity. Identifiable different-draft/event starts retain existing reset behavior. The unchanged deck coordinator/view-model generation, session and pool fences prevent old asynchronous results from becoming the new draft's suggested deck. Same-event anonymous reset is tested through raw parser → coordinator → view model, with no fabricated draft ID.

Without any identifiable entry/session boundary, anonymous same-event ambiguity remains; this change does not invent identity from a course/deck record or scene name. Physically exercising a same-event fresh Quick Draft remains part of pending live acceptance.

## Real current-log automatic replay

The final audit read an in-memory snapshot of actual Player.log from byte zero through the WOE completion and following scene change. It passed untouched raw lines through the real parser, state/session coordinator, production MainWindowViewModel and DeckConstructionCoordinator. It loaded the real Scryfall/17Lands caches, kept the source open until asynchronous publication and used a headless dispatcher. The audit never manufactured Completed state or invoked the baseline builder directly.

| Measured result | Value |
|---|---|
| Changed completion transitions | 1 |
| Parser warnings / state conflicts in replay window | 0 / 0 |
| Before / after drafted pool | 42 / 42, conserved |
| Retained recovered baseline | 41 |
| Retained exact history in full replay | 33 selections |
| Final Domain pool | 42, Complete |
| View-model status | Draft complete |
| Baseline panel | Available automatically |
| Actionable pack | Hidden |
| Build availability | Ready |
| Plan | RG, ActiveArchetype |
| Main deck | 40 = 23 spells + 17 lands |
| Mana | Mountain ×9, Forest ×8; no selected nonbasics |
| Creatures / sideboard copies | 14 / 19 |
| Neutral-prior selections / constraint relaxations | 4 / none |
| HTTP requests | 0; network disabled in audit |

Evidence and counted deck list: [real-log-automatic-build.json](artifacts/phase10b1-audit/real-log-automatic-build.json). The counts/mana/selection match the earlier Phase 10B offline result. This audit covers the observed completed WOE transition, not every later game/course record in the growing log.

**Measured:** the updated production code automatically activates Phase 10B from the actual logged completion sequence and publishes the correct rail model. **Inference:** the same logged envelope arriving during ordinary monitoring follows the same code path. **Not determined:** physical rail rendering/final-pick acceptance in a newly running app, a fresh anonymous same-event Quick Draft entry, or native macOS execution. No new Arena picks, navigation, import or input was performed. The previously running app was not restarted or silently replaced.

The local ignored harness can rerun this Windows replay:

```powershell
dotnet build artifacts/phase10b1-audit/LiveReplay.csproj
dotnet run --no-build --project artifacts/phase10b1-audit/LiveReplay.csproj -- 'C:\Users\theyc\AppData\Local\DraftTG\card-data\scryfall-default-cards.jsonl.gz' 'C:\Users\theyc\AppData\LocalLow\Wizards Of The Coast\MTGA\Player.log' 'C:\Users\theyc\AppData\Local\DraftTG'
```

## Tests, files and validation

Eleven additions:

- **7 ArenaIntegration cases:** actual canonical completion/conservation/idempotence; trustworthy recovery/bootstrap; count/multiplicity mismatch and warning recovery; ignoring generic deck/course data; focused malformed fields; legacy Bot/Human completion; genuine new-session reset, same-event entry deduplication and identifiable stale completion rejection.
- **2 Application cases:** automatic Ready 40/23/17 build from raw fixture with duplicate suppression; completed-to-new-anonymous-same-event reset without another build.
- **2 App cases:** automatic rail/pool/deck transition and same-event reset; mismatch warning with cleared actionable pack and provisional preserved pool.

| Suite | Passing cases |
|---|---:|
| Domain | 56 |
| RecommendationEngine | 146 |
| Data | 151 |
| ArenaIntegration | 114 |
| Application | 154 |
| App | 218 |
| Total per target | **839** |

Final validation runs `dotnet build DraftTG.sln`, `dotnet test DraftTG.sln --no-build`, and their `-p:DraftTGPortableBuild=true` equivalents. Both targets pass all 839 without failed/skipped cases; both builds have zero warnings/errors. Portable validation runs on Windows, not native macOS. [test-retention.json](artifacts/phase10b1-validation/test-retention.json) confirms all 828 previous case names retained and exactly 11 added. Logs and final TRX files are under [phase10b1-validation](artifacts/phase10b1-validation).

Modified production files:

- `src/DraftTG.ArenaIntegration/ArenaDraftLogParser.cs`
- `src/DraftTG.ArenaIntegration/ArenaDraftTypes.cs`
- `src/DraftTG.ArenaIntegration/ArenaDraftStateEngine.cs`
- `src/DraftTG.ArenaIntegration/ArenaDraftState.cs`
- `src/DraftTG.Application/DraftSessionCoordinator.cs`
- `src/DraftTG.App/MainWindowViewModel.cs`

New tests: `DeckSelectCompletionTests.cs`, `DeckSelectActivationTests.cs`, `DeckSelectLiveUiTests.cs`. Three test project files copy the new fixture/catalog. New privacy-safe fixtures: `deckselect-woe-completion-live.jsonl`, `deckselect-woe-cards.json`, and their README. The raw fixture omits DTO_InventoryInfo, replaces correlation UUIDs and retains only five necessary records. The catalog contains 38 public printing records for the 42 owned copies. No private full log or account data was copied.

Updated ARCHITECTURE.md and ROADMAP.md; the historical Phase 10B report links to this follow-up. Audit/validation files are local ignored artifacts. Source SHA-256 inventories confirm existing test source, Phase 8/9 logic, statistics providers/mapping, Domain/Data, the complete Phase 10B selection implementation, WGC/Desktop Duplication, matcher and placement sources remain unchanged. No Git index repair or staging was performed.

**Stop after Phase 10B.1. Phase 10C remains unstarted.**
