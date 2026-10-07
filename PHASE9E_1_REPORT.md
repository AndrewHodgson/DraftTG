# Phase 9E.1 — Arena display-order evidence collection

Status: **implemented, observational only.** Gate 1 is **not** satisfied: no live draft observations exist yet. Nothing in this phase changes badge placement, card identity, matcher thresholds, capture, recommendations or deck building. Phase 9E.2 has not been started.

This phase implements Step 1 of section 14 in [CLAUDE_LOCALIZATION_REVIEW.md](CLAUDE_LOCALIZATION_REVIEW.md). Its only purpose is to collect enough independent evidence to prove or refute one exact Arena visual card-sort rule.

## Pipeline

```text
Player.log pack (GrpIds, log order) ─┐
                                     ├─► ArenaDisplayOrderEvidenceGate (strict) ─► predict BEFORE learning ─► append JSONL ─► re-evaluate
existing safe automatic placement  ──┘          │                                       ▲
   (WGC + artwork matcher, unchanged)           │                                       │
                                                 └── rejection reason → rail line        Raw_CardDatabase (read-only) sort keys
```

`OverlayViewModel.ApplyAutomaticLocalization` raises `AutomaticLocalizationApplied` after it has already applied a safe result. `OrderEvidenceRecorder` observes that event. Its handler catches every exception, so an evidence failure can never reach the localization path. Database reads, prediction, ledger writes and re-evaluation run serialized off the UI thread. The only output is the rail text.

## Arena card database

| Item | Value |
|---|---|
| Path found | `C:\Program Files (x86)\Steam\steamapps\common\MTGA\MTGA_Data\Downloads\Raw\Raw_CardDatabase_8d9a18c7ea752803bb93e190da54707e.mtga` (Steam) |
| Modified | 2026-10-01 12:24:30 UTC |
| `Versions` | `Data 2026.63.0.270`, `GRP 270.1318799` |
| Journal mode | `delete` (not WAL), so a read-only open needs no write access |
| `Cards` | 60 columns. All required columns are present. |

**Location** (`ArenaCardDatabaseLocator`): an explicit path wins, either a file or a directory, passed as `--db` or the `DRAFTTG_ARENA_CARD_DATABASE` environment variable. Otherwise the newest `Raw_CardDatabase_*.mtga` by modification time is chosen from:
- `%ProgramFiles(x86)%` and `%ProgramFiles%`: `Steam\steamapps\common\MTGA\MTGA_Data\Downloads\Raw`
- the same two roots: `Wizards of the Coast\MTGA\MTGA_Data\Downloads\Raw`
- macOS: `~/Library/Application Support/com.wizards.mtga/Downloads/Raw/`

The macOS path is **not runtime-validated**. A Steam library on another drive needs the override. Arena process-path discovery was not added, because .NET `MainModule` needs `PROCESS_VM_READ`.

**Read-only access** (`ArenaCardDatabaseReader`):
- Uses `Microsoft.Data.Sqlite` 10.0.12 with `Mode=ReadOnly` and `Pooling=false`.
- Each call opens the database, runs one bounded query and closes it, so no lock or handle outlives a query.
- The file is never copied, migrated or vacuumed.
- Tests prove the file hash is unchanged and that no journal, WAL or copy appears, even when the file carries the read-only attribute.
- Missing required columns, a file that is not SQLite, or a missing `Cards` table return `Unavailable` with a diagnostic. Nothing throws to DraftTG.

**Fields mapped** (`ArenaCardSortKeys`). Values are copied verbatim; NULL stays null; no key is rederived from Scryfall.

| Model field | Arena column | DB type |
|---|---|---|
| `GrpId` | `GrpId` | INT |
| `MythicToCommon` | `Order_MythicToCommon` | INT (0 mythic, 1 rare, 2 uncommon, 3 common, 4 basic) |
| `ColorOrder` | `Order_ColorOrder` | INT |
| `Title` | `Order_Title` | TEXT (compared ordinally) |
| `CmcWithXLast` | `Order_CMCWithXLast` | INT (X spells 1000+) |
| `CreaturesFirst` | `Order_CreaturesFirst` | INT |
| `LandLast` | `Order_LandLast` | INT |
| `BasicLandsFirst` | `Order_BasicLandsFirst` | INT |
| `CollectorNumber`, `ExpansionCode` | same | TEXT |

Three **coverage traits** are used only for Gate‑1 reporting, never for ordering. They are optional and null if their column is absent.

| Trait | Rule |
|---|---|
| `IsMulticolor` | `Colors` has two or more entries |
| `IsHybrid` | `OldSchoolManaText` contains `(X/Y)` with X and Y both in WUBRG |
| `IsNonbasicLand` | `Types` contains 5 and `Supertypes` lacks 1 |

The enum values come from the database's own `Enums` table: CardType 5 = Land, SuperType 1 = Basic, CardColor 1–5 = WUBRG.

## Hypothesis family and model

`ArenaDisplayOrderModel` is pure, deterministic and portable: no Avalonia, capture, Scryfall or file access. It ports `tools/ArenaSortOrderProbe` exactly:
- `Order_MythicToCommon` is the primary key.
- It is followed by every ordered choice of 1–3 of {color, landLast, creaturesFirst, cmc, basicLandsFirst, title, collector, grpId}.
- A final `GrpId` tiebreak applies. NULL keys sort last.
- That gives **400 rules** (8 + 56 + 336), enumerated in the probe's `itertools.permutations` order.

No new hypotheses were invented.

- **`Evaluate(observations, universe)`**:
  - Replays observations in ledger order.
  - Counts *discriminating* observations: surviving rules predicted at least two orders.
  - Counts *contradictions*: no rule surviving before the observation reproduces it.
  - Returns survivors and functional equivalence classes.
- **Functional equivalence.** Rules are grouped by the total order they induce over a class universe: every draftable, primary, non-token card of the observed expansions, plus all observed cards. Because every rule is a strict total order with a GrpId tiebreak, equal orders over the universe imply equal predictions on every pack drawn from it. With no observed cards, no classes are claimed.
- **`Predict(pack, keys, survivors)`** returns:
  - `Unanimous`: one order.
  - `Discriminating`: several orders, each with its rule count.
  - `Unavailable`: missing keys, empty pack, or no survivors.

  `CompareWith(actual)` then gives `Agrees`, `Undetermined` (several orders, one matches), `Disagrees` (none matches) or `Unavailable`.

**No production code consumes `Predict`.** No rule is selected, ranked or preferred because it has few contradictions.

## P1P6 / P1P7 regression (real DB and synthetic fixture)

Both packs satisfy log order ≠ visual order.

| Check | Result |
|---|---|
| Hypotheses | 400 |
| Survivors after P1P6 + P1P7 | **120 / 400**, identical to the probe |
| Functional classes over the real WOE+WOT universe (339 cards) | **20**, identical to the review |
| Contradictions | 0 |
| Kept (fit both packs) | `rarity>color>title`, `rarity>color>collector`, `rarity>collector` |
| Refuted | `rarity>color>cmc>title` (P1P6 red commons), `rarity>title`, `rarity>grpId`, `rarity>cmc` |

The real-DB numbers come from replaying both fixture packs through the C# `--order-evidence-summary` command, using a scratch ledger outside the repository. The 120-survivor result is asserted in the tests against the synthetic fixture.

## Recording gate

An observation is written only when **all** of the following hold. Otherwise nothing is written, and the rail shows `Pn Pk order evidence: not recorded (<reason>)`.

1. No manual placement is involved: no confirmed manual map and no manual edits. Manual mapping is never training data.
2. The request is live (generation > 0). Request, result and current placement generations are equal, and the pack equals the current pack. Stale results are rejected.
3. `CardVisualLocalizationResult.IsSafeFor(request)` is true. Existing semantics are unchanged: ≥ 0.94, HighConfidence/Confirmed, unique keys and slots, overlap guard.
4. The pack size, occurrence count, match count and rectangle count are equal. A partial result is rejected even though `IsSafeFor` accepts partial maps.
5. Every match is HighConfidence or Confirmed.
6. The Player.log Arena pack has the same coordinate and count as the localized Domain pack.
7. Visual order comes from the matched rectangles' geometry: rows by vertical center, then left to right. A card whose vertical center falls 25–75 % of a card height from its neighbor's is ambiguous, and the whole result is rejected.

The order of GrpIds comes from each match's occurrence key (`PackIndex`) mapped into the Arena log pack. The adapter preserves order and duplicates.

## Evidence ledger

| Item | Value |
|---|---|
| Path | `<DraftTG app data>/localization/order-evidence.jsonl` |
| Windows | `%LOCALAPPDATA%\DraftTG\localization\order-evidence.jsonl` |
| macOS | `~/Library/Application Support/DraftTG/localization/order-evidence.jsonl` |

The ledger is never stored inside the repository at runtime.

**Records.** Each record is self-contained and versioned (`SchemaVersion: 1`, `EvidenceSource`) and holds:
- `ObservationId`, `DraftScope`, event name, expansion, pack, pick and card count
- log order and confirmed visual order
- Arena client size and capture crop size
- Arena `Data`/`GRP` versions and the database file name
- UTC timestamp, method, minimum and mean match scores
- **the Arena sort keys in effect when recorded**

Replay therefore never depends on a later database version.

**Appends** (`JsonLinesLedgerFile`):
- One complete line per write, flushed to disk, serialized by one lock.
- If an earlier write was torn, the next append starts on a fresh line.
- Single-line JSON only.

**Reload** (`ArenaDisplayOrderEvidenceLedger.Load`):
- Malformed JSON, unknown schema versions, inconsistent lists, missing keys and identity mismatches are skipped with line diagnostics.
- One corrupt row never stops startup.

**Duplicate identity.** `ObservationId` is SHA‑256 over (draft scope, pack, pick, *unordered* GrpId multiset). One visual pack state contributes once:
- UI refreshes, retries and re-applies are suppressed.
- Conflicting duplicates (same identity, different order) are counted on reload.

**Draft scope** (`ArenaDraftEvidenceScopeTracker`):
- An Arena draft ID is used when present.
- Quick Draft has none, so the scope is the DraftTG runtime session plus event name plus an epoch. The epoch advances when the coordinate moves backwards (a new draft) or the event changes.
- Only a SHA‑256 digest of the scope is persisted, never the raw Arena draft ID.

## Prediction before learning

For each accepted pack, the service:
1. Reads the pack's keys.
2. Predicts using the survivors from the evidence *before* this pack.
3. Classifies the result as agrees, undetermined or DISAGREES.
4. Appends, then re-evaluates.

A pack is never scored after training on itself. A missing database or missing keys means `unavailable`, and nothing is recorded.

## Rail diagnostic and summary command

The status panel has one small line under `Card placement`, with tooltip "Diagnostic only". Examples:

```text
Order evidence: no observations yet
P1P6 predicted order: agrees · rule classes 20 → 20
P1P7 predicted order: undetermined (3 candidate orders) · rule classes 20 → 12
P2P3 predicted order: DISAGREES · rule classes 12 → 0
P1P6 predicted order: unavailable (Arena card database not found.)
P1P8 order evidence: not recorded (partial automatic result (6 / 7))
```

**Card placement diagnostics** keeps the last 12 live-validation lines. Each line shows the coordinate, card count, prediction status before learning, orders, classes and rules before, the actual confirmed order, the outcome, and classes and rules after. The same lines are written to `Trace`.

Developer summary (read-only; writes only with `--output`):

```bash
dotnet run --project src/DraftTG.App -- --order-evidence-summary [--ledger <file>] [--db <file-or-directory>] [--output <file>]
```

The summary prints:
- observations and cards
- raw survivors out of 400
- distinct classes and universe size
- contradictions and discriminating observations
- skipped or duplicate lines
- a REFUTED banner when no rule survives
- Gate‑1 checklist
- per-Arena-Data-version observation and contradiction counts
- per-expansion counts
- representatives of the surviving classes

## Gate 1

`ArenaDisplayOrderGateProgress.Satisfied` requires **all** of the following:

**Volume**
- ≥ 2 **complete** drafts
- ≥ 80 observations

**Coverage**
- a full P1P1 of at least 14 cards
- a rare and a mythic
- a multicolor card
- a hybrid card, required whenever the observed expansions' draft universe contains one
- a bonus-sheet card at the same rarity as a main-set card in the same pack
- a duplicate within a pack
- a nonbasic land
- ≥ 2 distinct Arena client sizes

**Model**
- survivors > 0
- exactly **one** functional class
- **zero** contradictions

**Complete draft** is strict:
- The scope must contain P1P1, which sets pack size N.
- Every coordinate of packs 1–3, picks 1…N, must be present, with card count N − pick + 1.
- No coordinate outside that range may appear.

A draft joined late, a DraftTG restart during a Quick Draft (new runtime scope), or any single unlocalized pick makes the draft incomplete. Its observations still count individually.

Satisfying Gate 1 only makes Phase 9E.2 eligible for a separate decision. **No code path promotes a rule automatically.** If every hypothesis is refuted, the summary says so, the rail shows `hypothesis family REFUTED; DB order disabled`, and `Predict` returns `Unavailable`. The hypothesis family must not be expanded to rescue the idea.

**Arena updates.** The database version is recorded per observation, and prior evidence is never deleted. The summary breaks down contradictions per `Data` version, so a new client that starts contradicting the established rule is visible. Any future production use must fail closed on such contradictions.

## Why predictions are not production-authoritative

Two packs leave 20 functionally distinct rules, and they disagree on about half of simulated packs (review §13). Using any one of them for placement now could silently misplace badges. The existing WGC capture, `CardTemplateRecognizer`, 0.94/0.07 gates, artwork registration, stale-frame synchronization and manual fallback are unchanged, and they remain the only source of visual identity. A DISAGREES result is recorded and shown, but it hides nothing.

## Privacy

The ledger holds only:
- GrpIds and coordinates
- window and crop dimensions
- Arena database versions and the file *name*
- scores and status

It contains no screenshots or pixels, no full file paths (which can contain a user name), no account names or passwords, no raw draft IDs, and no other Player.log content. A test asserts the record contains no `Users` path fragment or image reference.

## macOS implications

The reader, model, ledger and summary are portable, and the portable `net10.0` build passes. `Microsoft.Data.Sqlite` ships native SQLite for osx-x64 and osx-arm64. The macOS database path is implemented but unvalidated.

macOS still has no automatic capture, so no Mac observations can be recorded until ScreenCaptureKit exists. The summary command works there.

**Unresolved Player.log path discrepancy:**
- `ArenaLogLocation` uses `~/Library/Logs/Wizards Of The Coast/MTGA/Player.log`.
- Wizards' support article lists `~/Library/Application Support/com.wizards.mtga/Logs/Logs`.

The production path was **not** changed without Mac evidence. Verify it on a Mac before Phase 9E Step 4.

## Optional audits

**`Raw_ArtCropDatabase_2b1d…mtga`** (read-only):
- Tables are `Formats` (17 rows) and `Crops(Path, Format, X, Y, Z, W, Generated)` (32,790 rows).
- Paths have the form `Assets/Core/CardArt/<bucket>/<ArtId>_AIF`, so they join `Cards.ArtId`.
- Formats include Normal, Frameless, Borderless and others.

It holds crop *parameters*, not pixels; the art textures live in Unity asset bundles. Using it would mean extracting Arena assets, a larger and more fragile dependency than Scryfall thumbnails. **Recommendation:** do not pursue it unless Scryfall references demonstrably fail for digital-only or Arena-specific art. Scryfall reference preparation is unchanged.

**UIA watch: deferred.** The Avalonia App does not reference `System.Windows.Automation`, and adding the UIA COM client to production is not trivial. `tools/DraftTG.LocalizationAudit` (`snapshot`) already reports the raw descendant count. Re-run it after Arena client updates; any count above 0 means structural accessibility should be re-audited.

## Files

**Added**
- `src/DraftTG.Data/ArenaCardDatabase.cs`: sort keys, locator, read-only reader.
- `src/DraftTG.Data/JsonLinesLedgerFile.cs`
- `src/DraftTG.Application/ArenaDisplayOrderModel.cs`
- `src/DraftTG.Application/ArenaDisplayOrderEvidence.cs`: observation, gate, ledger, Gate‑1 progress, scope.
- `src/DraftTG.Application/ArenaDisplayOrderEvidenceService.cs`: service and presentation text.
- `src/DraftTG.App/OrderEvidenceRecorder.cs`
- `src/DraftTG.App/OrderEvidenceSummaryCommand.cs`
- `tests/Fixtures/arena-card-database-synthetic.sql`: schema plus the 17 fixture cards' real key values, and 5 invented rows (900001–900005). Wizards' database is not committed.
- `tests/Shared/SyntheticArenaDatabase.cs`
- `tests/DraftTG.Data.Tests/ArenaCardDatabaseTests.cs`
- `tests/DraftTG.Application.Tests/ArenaDisplayOrderTests.cs`
- `tests/DraftTG.App.Tests/OrderEvidenceRecorderTests.cs`
- `PHASE9E_1_REPORT.md`

**Modified**
- `DraftTG.Data.csproj`: `Microsoft.Data.Sqlite`.
- `OverlayViewModel`: post-apply event and two diagnostic strings.
- `MainWindowViewModel`: internal `CurrentArenaState`.
- `AutomaticCardLocalizationSession`: internal `ArenaWindow`.
- `OverlayDesktopSession`: composition.
- `RailPanelView.axaml`: two text lines.
- `Program.cs`: the command.
- Three test `.csproj` files: fixture links.
- `ARCHITECTURE.md`, `ROADMAP.md`, `tools/ArenaSortOrderProbe/README.md`.

**Not modified:** capture or WGC/DD, `CardTemplateRecognizer`, thresholds, registration, synchronization, manual placement, the parser and state engine, statistics, Phase 8/9A–9D scoring, deck building and `ArenaDeckDifference`.

## Validation

| Check | Result |
|---|---|
| `dotnet build DraftTG.sln` | 0 warnings, 0 errors (clean `--no-incremental` rebuild) |
| Portable build (`-p:DraftTGPortableBuild=true`) | 0 warnings, 0 errors |
| `dotnet test DraftTG.sln --no-build` | **920 passed**, 0 failed. 887 existing cases still pass. |
| New tests | 16 methods, 33 cases: Data 5/9, Application 10/23, App 1/1 |

The new tests cover:
- the DB reader: read-only open, exact keys, NULL keys, missing ID, missing column, unavailable or corrupt DB, locator, torn ledger tail
- the P1P6/P1P7 regression and functional classes
- prediction status and agreement
- the recording gate: full, partial, ambiguous, manual, stale, log mismatch, ambiguous rows
- ledger reload, corruption, duplicates and version, plus deterministic replay
- identity
- prediction before learning, and fail-closed recording without a DB
- strict complete-draft tracking and Gate‑1
- the scope tracker
- rail text
- App wiring: partial not recorded, full recorded, duplicate suppressed, badges unchanged

## Live validation and Gate‑1 progress

**No live observations were collected.** MTG Arena was not running during implementation, and the current Player.log contains no draft records (last scene `DeckBuilder`).

Gate‑1 progress is therefore **0 complete drafts, 0 / 80 observations**, with every coverage item outstanding. The fixture packs are regression inputs only; they were not written to the user's ledger.

The user should keep DraftTG running from P1P1 through P3 last pick in at least two drafts, in at least two Arena window sizes, then run `--order-evidence-summary`.

**Contradicting evidence:** none so far. Both available real packs are reproduced by 120 rules with zero contradictions. The hypothesis family is neither confirmed nor refuted.
