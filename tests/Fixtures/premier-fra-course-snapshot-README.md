# Premier Draft FRA course-list fixture

Derived on 2026-10-05 from the live Windows Player.log, after Arena had been restarted following a completed Premier Draft. The log contained no draft pick, pack or completion records. The only draft evidence was the `EventGetCoursesV2` response that Arena writes on each EventLanding visit.

`premier-fra-course-snapshot-live.jsonl` keeps three consecutive records:
1. The `<== EventGetCoursesV2(...)` response marker. The correlation UUID is replaced with `redacted-correlation`.
2. The top-level `{"Courses":[...]}` response, with all 10 courses in their original order.
3. The `EventLanding -> DeckBuilder` scene change.

Sanitizing of the `Courses` response:
- `CourseId` values are replaced with `redacted-course-N`.
- `CourseDeckSummary`, `CourseDeck`, wins/losses and `JumpStart` are removed, because they contain deck names, deck IDs and private history.
- `InternalEventName`, `CurrentModule`, `ModulePayload`, `CardPool`, `CardPoolByCollation` and `CardStyles` are unchanged.

The `PremierDraft_FRA_20260929` course is in `DeckSelect` with a 42-card `CardPool`: 33 distinct GrpIds, of which 7 appear twice and 1 three times. The list also keeps a non-draft `Ladder` course in DeckSelect and a `Jump_In_2024` course with a 40-card `CardPool`. Both must be ignored.

`premier-fra-course-cards.json` holds 33 real Scryfall FRA printings from the local `default_cards` cache, restricted to identity and gameplay fields. **Scryfall's bulk data had no `arena_id` for any FRA printing on this date.** Each record's `arena_id` was therefore set by pairing Arena's own `Raw_CardDatabase` row (`ExpansionCode`, `CollectorNumber`) with the Scryfall printing of the same set and collector number. That pairing is a test-only mapping, not production identity.

`arena-card-database-fra-identity.sql` is a tiny SQLite fixture, not Wizards' database. It holds the same 33 GrpIds with only the identity columns the fallback reads: `ExpansionCode`, `CollectorNumber`, `TitleId`, `IsToken`, `IsRebalanced`, and the enUS title. Values are copied from Arena Data 2026.63.0.270. The fallback tests remove `arena_id` from `premier-fra-course-cards.json` to reproduce Scryfall's current state, and check that the bridge reproduces the pairing above.
