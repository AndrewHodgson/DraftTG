# Phase 10B — Baseline 40-Card Deck Construction

Follow-up: [Phase 10B.1](PHASE10B_1_REPORT.md) resolves the completion parsing gap described in this historical Phase 10B report; the validated deck-selection algorithm is unchanged.

Phase 10B implements one deterministic, advisory two-color baseline, with completed-only background construction and a collapsible Baseline Suggested Deck panel. Default successful output is exactly **40 cards: 23 spells and 17 lands**. Inventory counts, generated basics, sideboard copies, final plan, constraints and selection reasons remain explicit. Phase 10C is unstarted.

**Validation:** Windows and portable .NET 10 builds have **zero warnings and zero errors**. Both suites pass **828/828**, with no failures or skips: all 802 previous cases plus 26 focused additions. Native macOS execution and physical panel acceptance were not performed.

**Live limitation discovered:** the current Player.log contains a genuine completed WOE pool, but its `CurrentModule: DeckSelect` completion payload is ignored by the existing production parser. The real-pool sanity check below uses an explicitly offline adaptation of that payload. It establishes deck-building behavior, not automatic activation from that live completion format. Player.log parsing was left unchanged as requested.

## Models and availability

New provider-neutral RecommendationEngine types include `DeckBuildInput`, `DeckBuildResult`, `DeckBuildAvailability`, `DeckBuildConfidence`, `DeckPlan`, `DeckPlanSource`, `DeckPairEvidence`, `DeckCardStrength`, `DeckCardStrengthSource`, `DeckCardEntry`, `GeneratedBasicLandEntry`, `DeckCardDecision`, `DeckDecisionReason`, `BaselineDeck`, `DeckCompositionDiagnostics`, `DeckStrengthSummary`, `ManaDemand` and `BaselineDeckConfiguration`.

Counted output collections are copied/read-only; demand/source dictionaries are frozen. The output includes main nonlands, selected drafted nonbasics, generated basics, sideboard, creature/curve analysis, demand/sources and per-card decisions. Generated basics have no drafted-card identity and consume no inventory. Drafted basics remain unused pool entries. There is no Constructed four-copy limit.

Complete viable pools return Ready. Partial and Unknown pools with enough known eligible cards return ProvisionalPartialPool and ProvisionalUnknownPool. Too few eligible spells return InsufficientEligibleCards; missing metadata that prevents establishing eligibility returns MissingRequiredMetadata. No unavailable result fabricates 40 cards. Missing statistics use an explicit confidence/source fallback rather than making a spell ineligible. The application service returns DraftInProgress without statistics requests during active drafting.

## Plan, eligibility and selection strength

Final Phase 9C context is recomputed from final pool color evidence and the set profile, independent of the cleared current pack. A viable active archetype pair wins. Otherwise all ten pairs are ranked by `pairPoolFit = coverage * (.75 + .25 * balance)`, then combined color evidence, balance and canonical WUBRG pair order. The highest-ranked pair supplying the target spell count becomes PoolEvidenceFallback.

An eligible spell has centrally recognized nonland type, known Domain colors and a supported primary mana cost. Its colors must be colorless or a subset of the selected pair. Unknown mana value remains selectable but receives no early-play credit and appears in the Unknown curve bucket. Lands are selected separately. No rarity, name-based strength or intentional third color enters selection.

Strength hierarchy:

1. Phase 8 adjusted overall statistical value.
2. Optionally add the existing Phase 9C adjustment when final ActiveArchetype, selected pair, exact set/format context and usable pair row match. Existing colorless affinity eligibility is preserved.
3. For a usable environment baseline but no usable mapped card statistic, use that baseline as NeutralBaselineFallback, a selection prior rather than measured GIH.
4. Without a usable baseline, use null-valued DeterministicUnscored strength and InsufficientStatistics confidence. Null contributes zero only to the internal ordering objective; no percentage is displayed or invented.

Phase 9A's per-card color adjustment, Phase 9B lane signals and Phase 9D trophy conversion never enter deck strength. The new code calls the unchanged pure Phase 8 scorer and existing Phase 9C math. Objective sums and average measured selection value are diagnostics, without a calibrated deck rating or win prediction.

## Configuration and optimizer

| Setting | Default |
|---|---:|
| TargetDeckSize | 40 |
| TargetNonlandCount | 23 |
| TargetLandCount | 17 |
| MinimumCreatures | 14 |
| PreferredCreatures | 16 |
| MinimumEarlyPlays | 4 |
| HighCostThreshold | 5 |
| MaximumHighCostCards | 6 |
| MaximumDraftedNonbasicLands | 4 |
| MinimumBasicsPerUsedColor | 6 |

Early means known mana value <=2; high cost means known value >=5. These are generic heuristics, not role classifications. Creature and early floors become the minimum of configured floor, available eligible occurrences and target size. The high-cost cap rises only to the smallest count feasible with both floors. Relaxations are recorded.

Stable CardIdentifier/copy occurrences enter a bounded count-state dynamic program. State tracks selected count, actual creatures, early count clamped at its required floor and high-cost count. For each state it retains the best objective, measured count and stable occurrence bit mask. The final tie order is objective, measured-strength count, distance from PreferredCreatures, then stable identity/copy order. A small independent exhaustive oracle test verifies the optimizer objective. Production does not enumerate all 23-card subsets.

The same final state table supplies counterfactual optima with each constraint removed and with all composition constraints removed. Decision flags identify actual changes to selection under those counterfactuals; a creature is not automatically labeled CreatureRequirement merely because it is a creature. Aggregate flags can describe different occurrences of one counted card. The builder supports pools up to 200 occurrences, checks cancellation during optimization, and validates configuration, including jointly supportable floor settings.

## Nonbasics and mana

Structured produced-mana information was already preserved in Phase 10A. Nonbasic selection requires known card colors compatible with the plan. Reliable production of both selected colors ranks before one; Phase 8 overall strength then stable identity/copy order break ties. Reliable production exclusively outside the pair rejects the land. With no reliable colored production, measured Phase 8 value at least the environment baseline is required; a neutral prior cannot establish usefulness. At most four drafted copies occupy land slots.

Multiface whole-card production may describe a back face, so Phase 10B does not credit it as a primary-face source. Missing production does not prove a utility/fixing land is useful. Oracle text is not parsed. The real Evolving Wilds remains in the sideboard because cached inputs do not establish structured production or usable measured statistical support under this conservative policy.

Mana demand uses the primary face's cost when available, otherwise the first cost in a combined display. Adventure/front/back/split costs are never added to top-level costs a second time. Standard colored symbols count one pip; colored hybrid alternatives split one pip evenly. Numeric/generic, C, X/Y/Z and snow symbols add no colored demand. Colored Phyrexian and numeric hybrids conservatively retain colored demand. Unsupported/missing costs remain explicit. The helper does not implement alternate-face choices, colorless/snow source requirements or activated costs.

Generated basics fill `17 - selectedNonbasicCount`. For colors with positive selected-spell demand, reserve six each when possible. If insufficient slots exist, use `floor(slots / usedColors)` each. Allocate the remaining slots proportionally to demand, floor the quotas, then give leftovers by largest fractional remainder with WUBRG ties. An effectively monocolor deck receives no unused-color basics. A generic-only deck uses the first canonical plan color for generic mana. Known nonbasic sources are diagnostic; they do not reduce basic allocation. For B=12/G=8 with 17 slots, this reserve-then-remainder policy gives 9 Swamps/8 Forests.

## Runtime and UI

Application's `DeckConstructionService` loads overall/environment and optional final-active-pair statistics through the existing service/client/cache. A synthetic identity lookup scopes the existing mapper; it does not fabricate Arena pack state or pick chronology. Statistics failures preserve an honest deterministic build where metadata/inventory permit one. No trophy request is introduced.

`DeckConstructionCoordinator` schedules completed-only background work, deduplicates identical final inventories and cancels/fences old work. The view model verifies generation, draft identity/event and pool equality before publishing. A new draft clears the old build. Runtime waits for canceled deck work before disposing the shared statistics HTTP client.

The rail's Baseline Suggested Deck expander shows counted Creatures, Other Spells, Lands and generated basics; plan/source/confidence; exact totals; creature count; Baseline Mana Base and fractional-safe curve. Nested Sideboard / Not Included and Build diagnostics expose cuts, priors, constraints, demand/sources and reason flags. Publication does not trigger pack presentation or alter passive badge placement. Advisory output performs no Arena input, card changes, import or add/remove highlighting.

## Real completed WOE audit

The audit read the local Scryfall gzip cache and a shared-read snapshot of current Player.log. It used only the existing WOE QuickDraft overall/RG cache; its HTTP handler cannot access the network. No full private log or account/session identifiers were copied into artifacts.

Production replay recognized no completed WOE state from this completion payload. The offline harness located the genuine `DeckSelect / DraftStatus=Completed / PickedCards` array of 42 cards, retained the parsed state at that same boundary/event, and verified the raw multiset contains the preceding parsed inventory. That baseline also has 42 copies. Only the offline audit constructs a Completed snapshot with that exact recovered multiset. The existing adapter resolves it as Complete. This is not a production parser change or proof of live activation.

| Measurement | Result |
|---|---|
| Pool | 42 resolved copies, Complete in offline adaptation |
| Plan | RG — Big-Creature Beatdown |
| Source | ActiveArchetype |
| Main deck | 23 spells + 17 lands = 40 |
| Creatures | 14 |
| Early plays | 12 |
| High-cost cards | 2; effective cap 6 |
| Nonbasics | 0 |
| Generated basics | Mountain ×9, Forest ×8 |
| Colored primary-face demand | R=12, G=11 |
| Known colored sources | R=9, G=8 |
| Sideboard | 19 drafted copies |
| Neutral-prior selections | 4 copies |
| Unscored selections | 0 |
| Constraint relaxations | None |
| Inventory conservation | True for every pool identity |
| Off-color spells | 0 |

### Selected spells

| Card | Copies | MV | Creature | Strength source |
|---|---:|---:|---|---|
| Gingerbrute | 1 | 1 | Yes | Overall |
| Hollow Scavenger // Bakery Raid | 1 | 3 | Yes | Neutral prior |
| Beanstalk Wurm // Plant Beans | 1 | 5 | Yes | Neutral prior |
| Candy Trail | 1 | 1 | No | Overall |
| Bellowing Bruiser // Beat a Path | 1 | 5 | Yes | Neutral prior |
| The Huntsman's Redemption | 1 | 3 | No | Archetype adjusted |
| Titanic Growth | 1 | 2 | No | Archetype adjusted |
| Eriette's Tempting Apple | 1 | 4 | No | Overall |
| Flick a Coin | 1 | 3 | No | Archetype adjusted |
| Tough Cookie | 1 | 2 | Yes | Archetype adjusted |
| Ratcatcher Trainee // Pest Problem | 1 | 2 | Yes | Neutral prior |
| Brave the Wilds | 1 | 1 | No | Archetype adjusted |
| Howling Galefang | 1 | 4 | Yes | Archetype adjusted |
| Dragon Mantle | 1 | 1 | No | Archetype adjusted |
| Welcome to Sweettooth | 1 | 2 | No | Archetype adjusted |
| Cut In | 1 | 4 | No | Archetype adjusted |
| Edgewall Pack | 1 | 4 | Yes | Archetype adjusted |
| Merry Bards | 1 | 3 | Yes | Archetype adjusted |
| Charming Scoundrel | 1 | 2 | Yes | Archetype adjusted |
| Redcap Thief | 1 | 3 | Yes | Archetype adjusted |
| Skewer Slinger | 1 | 2 | Yes | Archetype adjusted; creature constraint affects selection |
| Ruby, Daring Tracker | 2 | 2 | Yes | Archetype adjusted |

Spell curve: **[0,2):4; [2,3):8; [3,4):5; [4,5):4; [5,6):2; [6,7):0; 7+:0; Unknown:0**. Sideboard copies and all decisions are in [real-woe-build.json](artifacts/phase10b-audit/real-woe-build.json).

The final five warmed pure-builder calls took **14.64, 14.17, 15.47, 11.89 and 15.17 ms**, median **14.64 ms**, while validation was also running. An earlier isolated audit measured a 12.24 ms median. This excludes full-cache import, log replay, network/cache I/O and UI dispatch.

Manual inspection confirms exact 40/23/17 totals, preserved Ruby duplicates, no off-color inclusion or inventory overuse, 14 creatures, and only two 5+ cards. The conservative nonbasic policy omits Evolving Wilds. Roles, interaction density, Food/Bargain synergies, token-producing noncreatures and Adventure sequencing are not modeled. An alternative human build could reasonably differ. This is a sanity check rather than a strategically optimal deck claim.

The log replay also encountered 80 existing parse warnings and zero state conflicts in its captured whole-log snapshot. They are not new deck-builder test failures. The narrow observed activation issue is the ignored DeckSelect completion payload. A separately authorized parser fix and physical live UI check are required before claiming automatic construction works for that exact format. Supported Completed input is covered by integration tests.

The ignored local harness can be rerun without launching or operating Arena:

```powershell
dotnet run --project artifacts/phase10b-audit/DeckAudit.csproj -- 'C:\Users\theyc\AppData\Local\DraftTG\card-data\scryfall-default-cards.jsonl.gz' 'C:\Users\theyc\AppData\LocalLow\Wizards Of The Coast\MTGA\Player.log' 'C:\Users\theyc\AppData\Local\DraftTG'
```

## Tests and validation evidence

Added **26 cases** in three new test files: 22 RecommendationEngine cases, two Application integration cases and two App/ViewModel cases. They cover exact totals/conservation, more than four copies, color eligibility, active/fallback plan, creature/early/high-cost constraints including joint feasibility, neutral/no-baseline fallback, missing metadata, partial/unknown pools, nonbasic policy, mono/two-color basic allocation, five parameterized mana costs, primary Adventure demand, exact pair/context affinity, deterministic ties/rarity independence, an independent small optimizer oracle, configuration/read-only output, completed construction without a current pack, session reset/late worker fencing, and counted/provisional UI without pack presentation changes.

| Suite | Passing cases |
|---|---:|
| Domain | 56 |
| RecommendationEngine | 146 |
| Application | 152 |
| Data | 151 |
| ArenaIntegration | 107 |
| App | 216 |
| Total per target | **828** |

Final commands:

```powershell
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
dotnet build DraftTG.sln -p:DraftTGPortableBuild=true
dotnet test DraftTG.sln --no-build -p:DraftTGPortableBuild=true
```

Both builds report zero warnings/errors; both suites pass 828 with zero failed/skipped. Portable tests ran on Windows, not on native macOS. [test-retention.json](artifacts/phase10b-validation/test-retention.json) compares prior/new case names and confirms all 802 retained, none removed and exactly 26 added. Build logs and final `final-windows-tests-v2` / `final-portable-tests-v2` TRX files are in [phase10b-validation](artifacts/phase10b-validation).

## Files changed and boundaries

New production files:

- RecommendationEngine: `DeckBuildingModels.cs`, `DeckMana.cs`, `DeckPlanSelector.cs`, `DeckSpellOptimizer.cs`, `BaselineDeckBuilder.cs`.
- Application: `DeckConstructionService.cs`, `DeckConstructionCoordinator.cs`.
- App: `BaselineDeckPresentation.cs`.

Modified existing production files: `src/DraftTG.Application/DraftTGRuntime.cs`, `src/DraftTG.App/MainWindowViewModel.cs`, `src/DraftTG.App/RailPanelView.axaml`.

New tests: `tests/DraftTG.RecommendationEngine.Tests/BaselineDeckBuilderTests.cs`, `tests/DraftTG.Application.Tests/DeckConstructionTests.cs`, `tests/DraftTG.App.Tests/BaselineDeckPresentationTests.cs`. Updated `ARCHITECTURE.md`, `ROADMAP.md` and this report. Audit projects/results and validation inventories are local ignored artifacts.

[source-changes.json](artifacts/phase10b-validation/source-changes.json) and before/after SHA-256 inventories establish that existing tests, Domain/Data/ArenaIntegration, Phase 8/9 scoring, statistics provider/mapping, WGC/Desktop Duplication, artwork matcher and localization/placement files are unchanged. No Git index repair or staging was performed.

## Limits and deferred work

This phase uses fixed generic composition and land counts. It does not evaluate roles, detailed synergy, alternate casting plans, splash sources, colorless/snow needs, activated costs, fixing text, or calibrated deck strength. Missing statistics stay visible as priors; missing metadata may prevent construction. Existing inventory/session/completion parsing limits remain. Native macOS and physical UI validation remain outstanding.

Phase 10C may compare multiple builds, alternate pairs, possible splashes and richer successful-deck evidence. Phase 10D may localize Arena's deck builder and add advisory add/remove highlights. Later work may model roles, synergy, mana-source optimization and trophy-deck composition. **None is implemented here. Stop after Phase 10B.**
