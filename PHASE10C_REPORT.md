# Phase 10C — Multiple Suggested Deck Builds

Implemented up to three distinct two-color proposals using the existing Phase 10B builder. Recommended Build 1 is unchanged. The actual completed WOE pool supports **two** builds, RG and UG; no third build is fabricated. Phase 10D remains unstarted.

## Files and types

Production files modified:

- `src/DraftTG.RecommendationEngine/DeckBuildingModels.cs`: adds `AlternativeColorPair` plan source.
- `src/DraftTG.RecommendationEngine/BaselineDeckBuilder.cs`: extracts the shared core and exposes `BuildForPair`, retaining the baseline entry point and rules.
- `src/DraftTG.Application/DeckConstructionService.cs`: shares existing statistics preparation between baseline and suggested-set APIs; no new provider algorithm.
- `src/DraftTG.Application/DeckConstructionCoordinator.cs`: publishes suggested sets with the baseline and fences completed-session workers.
- `src/DraftTG.App/MainWindowViewModel.cs`: selected-build state, retention/reset, selected details and differences.
- `src/DraftTG.App/BaselineDeckPresentation.cs`: uses the shared “Mana Base” heading for any selected build.
- `src/DraftTG.App/RailPanelView.axaml`: compact Suggested Decks selector and one detail list with a difference expander.

Production files added:

- `src/DraftTG.RecommendationEngine/SuggestedDeckModels.cs`: immutable `SuggestedDeckSet`, `SuggestedDeck`, `SuggestedDeckId`, `SuggestedDeckRank`, `SuggestedDeckComparison`, `SuggestedDeckCandidateDiagnostic`, `SuggestedDeckDifference`, `SuggestedDeckLandDifference`, and validated `SuggestedDeckConfiguration`. Availability reuses `DeckBuildAvailability`.
- `src/DraftTG.RecommendationEngine/SuggestedDeckBuilder.cs`: pair enumeration, independent construction, validation, common comparison and alternative ordering.
- `src/DraftTG.App/SuggestedDeckPresentation.cs`: explicit coverage, comparison diagnostics, counted spell/land differences.

Three focused test files were added: `SuggestedDeckBuilderTests.cs`, `SuggestedDeckConstructionTests.cs`, and `SuggestedDeckPresentationTests.cs` in their corresponding RecommendationEngine, Application and App test projects. `ARCHITECTURE.md` and `ROADMAP.md` are updated. The repeatable offline production-code audit lives in `artifacts/phase10c-audit/Phase10CAudit.csproj` and `Program.cs`; its privacy-safe output is [real-woe-suggested-builds.json](artifacts/phase10c-audit/real-woe-suggested-builds.json).

## Baseline and alternative construction

`SuggestedDeckBuilder` calls `BaselineDeckBuilder.Build` directly and retains the same returned deck object for Build 1. `Build` and `BuildForPair` invoke one `BuildForPlan` core. All builds use the unchanged optimizer, creature/early/high-cost constraints, primary-face mana demand, nonbasic policy, generated basic allocation and owned-copy accounting. Alternative inventory is never reduced by another build's selections. Each build independently gets 23 spells, 17 lands and 40 total cards under the default configuration.

All ten canonical pairs are checked with the existing Phase 10B metadata/color eligibility rules. Colorless spell copies count for every pair. Every pair supplying at least 23 eligible copies is optimized, even if the output maximum is lower. Invalid results retain an exclusion reason; composition relaxation or missing statistics alone does not exclude a valid pair. Output diversity is one build per pair, with a configurable maximum of 1–3, default 3. Archetype names come from the existing set profile.

The common comparison averages selected spell **occurrences** using overall Phase 8 adjusted values, otherwise the same overall environment neutral prior as Phase 10B. No pair-specific affinity is included in this metric. If no common basis exists, quality is unavailable and unscored coverage remains explicit. Build 1 stays fixed regardless of alternative quality. Alternatives sort lexicographically by:

1. CommonSpellQuality descending.
2. Final pair pool fit descending.
3. Measured selected spell copies descending.
4. Neutral fallback copies ascending.
5. Composition relaxation count ascending.
6. Combined final color evidence descending.
7. Canonical WUBRG pair order.

This is an internal ordering metric, **not** predicted deck win rate or a calibrated rating. Trophy conversion/evidence never changes BuildRank. Existing affinity rules permit pair adjustments only on the active-archetype baseline; alternatives use overall values.

Sets expose the completed pool/hash, opaque session identity, available builds, recommended and selected build, viable evaluated count, all candidate diagnostics and exclusions. Deck IDs deterministically hash session plus pair, independent of rank or selected cards. Selection creates a replacement immutable set. The rail defaults to Build 1 and retains the selected ID during panel collapse, unrelated updates and same-session replacement sets; absence falls back to Build 1. A corrected completed pool hides the old inventory proposal during reconstruction while retaining the pair choice. A new draft clears both suggestions and selection. Generation/session/pool fences discard stale workers even if a provider ignores cancellation. Rail movement/expansion and captures do not trigger construction.

## Network behavior

The completed-session service loads the same overall environment and optional final active-pair dataset used before Phase 10C. `MaximumAdditionalPairDataRequests` defaults to 2, bounded to 0–2. **Zero additional pair-data requests are made**, including when that cap is zero. There is no ten-pair fetch, alternative retry loop or new cache behavior. Initial relevant loads finish before the set is constructed.

The measured automatic real-log replay loaded the overall environment once and the existing final **RG** pair once, both from cache. It made **zero HTTP requests**. A no-network handler would return 503 if a cache refresh were attempted; no such attempt occurred. The audit's later pure-builder measurements reuse the same mapped inputs and contain no provider requests.

## Real completed WOE pool

The audit snapshots current `Player.log` with shared read access, replays its actual records from byte zero through the first successful WOE DeckSelect completion and following scene records, and uses the actual Scryfall and exact WOE QuickDraft local statistics caches. The automatic path is parser → state engine → session coordinator → deck coordinator → view model. It does not fabricate a Completed snapshot. No raw private log is copied to the repository.

The final replay recorded one changed completion, zero parser warnings, zero state conflicts, and a **Complete 42-card pool**. All ten eligibility checks were:

| Pair | Eligible spell copies | Result |
|---|---:|---|
| WU | 11 | Excluded: fewer than 23 |
| WB | 4 | Excluded: fewer than 23 |
| WR | 14 | Excluded: fewer than 23 |
| WG | 21 | Excluded: fewer than 23 |
| UB | 10 | Excluded: fewer than 23 |
| UR | 20 | Excluded: fewer than 23 |
| UG | 27 | Valid alternative |
| BR | 13 | Excluded: fewer than 23 |
| BG | 21 | Excluded: fewer than 23 |
| RG | 32 | Preserved baseline |

Only UG enters the alternative ordering; Build 3 is absent.

| Detail | Build 1 — Recommended Baseline | Build 2 — Alternative |
|---|---|---|
| Pair / archetype | RG — Big-Creature Beatdown | UG — Ramp and Adventures |
| Plan source | ActiveArchetype | AlternativeColorPair |
| Availability | Ready | Ready |
| Total / spells / lands | 40 / 23 / 17 | 40 / 23 / 17 |
| Creatures | 14 | 10 |
| Basics | 9 Mountains, 8 Forests | 8 Islands, 9 Forests |
| Drafted nonbasics | 0 | 0 |
| Sideboard / not included | 19 owned copies | 19 owned copies |
| Measured selected spells | 19 / 23 | 19 / 23 |
| Neutral fallback / unscored | 4 / 0 | 4 / 0 |
| CommonSpellQuality | 0.583130 | 0.575294 |
| Final pair pool fit | 0.726974 | 0.595395 |
| Early plays / high-cost spells | 12 / 2 | 11 / 4 |
| Composition relaxations | None | Creature floor 14 → 10: eligible pool supply |
| Spell difference from Build 1 | None | Add 12, remove 12, share 11 |
| Land difference from Build 1 | None | Add 8 Islands + 1 Forest; remove 9 Mountains |

The real Build 1 equals the current standalone baseline in pair, plan source, spells, nonbasics, basics and sideboard. Its card/count and land lists also exactly equal the saved **pre-Phase-10C Phase 10B.1** audit, so this verification does not rely only on comparing two new entry points. The timed pure suggested set equals the automatically published set.

UG adds Obyra's Attendants, Bestial Bloodline, Sleight of Hand ×2, Return from the Wilds, Into the Fae Court ×2, Skybeast Tracker ×2, Merfolk Coralsmith, Compulsion and Toadstool Admirer. It removes Bellowing Bruiser, Flick a Coin, Ratcatcher Trainee, Dragon Mantle, Cut In, Edgewall Pack, Merry Bards, Charming Scoundrel, Redcap Thief, Skewer Slinger and Ruby, Daring Tracker ×2. Full counted main/sideboard lists, curves, mana demands and source diagnostics are in the JSON audit.

Manual inspection and invariant checks found no off-color spells, inventory overuse, repeated pair, incorrect total or third-color mana. The UG allocation reflects 14 green and 9 blue primary-face pips. Its lower common quality and ten-creature supply are visible; it is an alternative with a substantial composition limitation, not an equally strong recommendation. The RG plan remains recommended.

## Performance and validation

Six pure SuggestedDeckSet generations on the real pool measured **22.69, 23.68, 24.04, 40.19, 19.86, 20.13 ms**. The median of the five warm runs was **23.68 ms**. Measurements exclude catalog/statistics loading and native capture; they include baseline construction, all-pair checks, viable alternative optimization, comparison and differences. The first audit measured a 22.3 ms warm median. No additional optimizer or premature performance tuning was needed.

The final commands were:

```text
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
dotnet build DraftTG.sln -p:DraftTGPortableBuild=true
dotnet test DraftTG.sln --no-build -p:DraftTGPortableBuild=true
```

Both targets pass **867 tests**, retaining all **839** previous tests and adding **28**: RecommendationEngine 17, Application 4, App 7. Final builds have **zero warnings and zero errors**. Test cases cover baseline invariance with/without affinity, three/two/one viable pairs, deterministic ordering and pool-fit tie breaks, evaluating every viable pair, independent nonbasics/mana/inventory, duplicate-count differences, low/no data, provisional relaxation visibility, stable IDs, bounded provider traffic, stale callbacks and selection/reset/selected detail behavior. Existing test files were not changed. No pixel rendering tests were added.

Final suite counts: Domain 56, RecommendationEngine 163, Data 151, ArenaIntegration 114, Application 158, App 225. Machine-readable test retention and source inventories are in `artifacts/phase10c-validation/`; build/test logs and final TRX files are retained there.

## Limits and deferred work

Physical Windows rail selection has **not** been exercised in a newly running UI. The production-code replay and view-model tests are headless; compiled Avalonia bindings pass the build. Portable .NET builds/tests pass on Windows, but **native macOS execution remains unvalidated**.

Eligibility establishes a legal proposal, not strategic strength. The baseline optimizer still lacks full synergy/card-role evaluation, removal density, activated/alternate-face mana costs, and credit for indirect fixing. Neutral priors are not measured GIH. Distinct pairs may share most spells in a colorless-heavy pool; same-pair duplicates are excluded. Builds with feasible constraint relaxation remain inspectable rather than silently disappearing. Session IDs are not persisted across app restarts. No text export was added because there was no established deck-copy workflow to reuse.

Phase 10D deck-builder localization, selected-build comparison against Arena and add/remove highlights remain deferred. Arena clicking, import/export and simulated input are absent. Same-pair strategy variants, splashes, richer synergy and successful-deck composition analysis remain future work. Phase 8, Phase 9A/B/C formulas, Phase 9D's disabled trophy scoring, parsing/completion and capture/artwork sources are unchanged.
