# Phase 10A completion report

Implemented the drafted-pool and card-metadata foundation, descriptive analysis, and a collapsible control-rail panel. The final pool survives Completed; new sessions replace it. Phase 10B deck construction has not started.

## Validation

| Assembly | Previous | Added | Passing |
|---|---:|---:|---:|
| Domain | 44 | 12 | 56 |
| Data | 147 | 4 | 151 |
| Application | 145 | 5 | 150 |
| RecommendationEngine | 121 | 3 | 124 |
| ArenaIntegration | 107 | 0 | 107 |
| App | 213 | 1 | 214 |
| **Total** | **777** | **25** | **802** |

Both Windows and portable .NET 10 builds finish with **zero warnings and zero errors**. Both suites pass **802 tests**, with no failures/skips. Commands, from the repository root:

```powershell
dotnet build DraftTG.sln
dotnet test DraftTG.sln --no-build
dotnet build DraftTG.sln -p:DraftTGPortableBuild=true
dotnet test DraftTG.sln --no-build -p:DraftTGPortableBuild=true
```

Final logs/results are under [artifacts/phase10a-validation](artifacts/phase10a-validation), including `windows-build.log`, `portable-build.log`, `final-windows-tests-v2` and `final-portable-tests`. Portable tests ran on Windows; native macOS execution and physical control-panel acceptance remain unverified.

Before/after SHA inventories and [integrity-summary.json](artifacts/phase10a-validation/integrity-summary.json) establish that no existing test methods were removed and no Arena Integration source or existing RecommendationEngine source changed. The original statistics providers/mapper, parser/state engine, WGC/Desktop Duplication, artwork matcher, synchronization, calibration, badges and placement are unchanged. Phase 8/9A/9B/9C scores and ranks are unchanged; Phase 9D adjustment remains zero. No deck-selection or role-classification logic exists.

## Pool model and assembly

Domain adds immutable `DraftPoolSnapshot`, `DraftPoolEntry` and `DraftPoolCompleteness`. The snapshot wraps existing `DraftedCardPool` occurrence data and derives read-only identifier/count entries, total count, known count, known unique count, and unresolved occurrence count. No coordinate or recommendation rank is required. Value equality preserves deterministic adaptation. A complete snapshot cannot contain unresolved identities.

Application adds `ArenaDraftPoolAssembler` and exposes `ArenaDraftSnapshotResult.DraftPool` even without an actionable recommendation snapshot. It uses the state engine's existing merged `DraftedPool`, rather than summing a recovered snapshot with the entire history. Existing merge semantics retain the recovered multiset, reconcile missing covered exact selections, and add exact selections after the recovered boundary. Exact A/B plus recovered A/B/C/D therefore yields **four**, preserving duplicate counts. Resolver behavior remains unchanged; missing/ambiguous IDs prevent recommendations as before, but known pool entries remain inspectable with explicit unresolved counts.

Completeness is independent of history chronology:

- **Complete:** counts match the supported current boundary and a validated recovered inventory or contiguous exact history proves full coverage. A recovered pool can be Complete with earlier coordinates unknown. The existing supported normal format is three packs with fourteen single-card selections each.
- **Partial:** known selections exist but full coverage is not proven, the count is short, or some identities cannot resolve. An old 15-card recovered pool at Completed remains Partial, rather than being called a final complete pool.
- **Unknown:** reliability/boundary cannot be established, a picked-card integrity diagnostic is present, or the mode/coordinates/multi-card selections are unsupported.

The completed boundary is 42 selections for the currently supported format. Exact final selections can extend the recovered baseline to that boundary. Between packs, exact/recovered facts establish the latest count. Source reset clears inventory; a genuinely new identified session replaces it under existing engine rules. Anonymous sessions still rely on those existing identity/reset rules; this phase does not reinterpret Arena parsing. Domain has no Arena provenance. Unresolved identities contribute to total count but cannot supply a reliable unique-card count.

## Metadata and Scryfall boundary

`Card` keeps its existing six-argument constructor and gains `GameplayMetadata`, defaulting to explicit Unknown for manual callers. Production cards receive an immutable `CardGameplayMetadata` profile with:

- Nullable finite, nonnegative **double** mana value, preserving fractions and distinguishing missing from zero.
- Canonical mana cost and original display type line.
- Central `CardTypeSet`, flags for Creature/Land/Instant/Sorcery/Artifact/Enchantment/Planeswalker/Battle/Other, and creature/land/basic/nonland-spell predicates.
- Basic supertype and Plains/Island/Swamp/Mountain/Forest subtype recognition from type metadata.
- Layout and immutable `CardFaceMetadata` with name, cost, value, optional colors, type, Oracle text and string P/T.
- Optional Oracle text, read-only keywords, and string power/toughness retaining `*` and `1+*`.
- Nullable structured produced-mana kinds, including Colorless in **ManaKind**, not MagicColor. Unrecognized symbols are retained separately.
- `ColorsKnown` for explicit unknown diagnostics, without altering existing `Card.Colors` normalization or scoring behavior.

All provider JSON fields remain in Data:

| Scryfall field | Normalized profile |
|---|---|
| `cmc` | ManaValue |
| `mana_cost` | ManaCost |
| `type_line` | TypeLine and centrally derived types/basic status |
| `layout` | Neutral CardLayout |
| `card_faces` | Immutable face metadata and appropriate front fallback |
| `oracle_text`, `keywords` | Optional text and immutable keywords |
| `power`, `toughness` | Optional strings |
| `produced_mana` | Recognized ManaKind values and retained unrecognized symbols |
| Existing `colors` / face colors | Existing Card.Colors policy, with availability recorded |

Negative, nonfinite or nonnumeric present mana values and malformed present types produce focused `InvalidManaValue` / `InvalidCardType` errors. Absent optional/legacy fields remain unavailable. Catalog ordering, exact IDs, duplicate-ID rejection, duplicate names and Arena mappings are preserved.

Adventure and double-faced cards use permanent/front types. Grabby Giant therefore counts as a creature, while its Adventure face remains inspectable as an instant. Combined Adventure cost/type display strings are retained. Missing top-level cost/text/P/T can use the front face. Split cards retain both card types and the provider's combined mana value/cost. Top-level mana value is authoritative when present; front supplied value is the fallback. The known top-level value can populate a front Adventure/transform/modal face, while unsupported absent face values/colors remain Unknown. There is no symbol evaluator, casting-cost difficulty model, or face-choice algorithm.

All optional fields requested for audit were included at low cost. They do not drive scoring or roles. Whole-card produced mana may describe an alternate face; it is not a selected mana-source policy.

## Real cached-data audit and cache impact

Before designing fields, inspected the existing `default_cards` raw cache. Then ran the production `ScryfallJsonlGzipCardDataLoader` against it after normalization changes. **All 118,469 printings imported**, including **381 WOE printings**. After front fallback, the full catalog has **zero unknown top-level mana values, types or colors**. This is measured cache evidence, not a claim about every future provider response.

Representative normalized results:

| Card / printing | MV | Cost | Primary types / finding |
|---|---:|---|---|
| Voracious Vermin, WOE 116 | 3 | `{2}{B}` | Creature; string P/T retained |
| Candy Grapple, WOE 83 | 2 | `{1}{B}` | Instant; Bargain retained as a keyword only |
| Rat Out, WOE 103 | 1 | `{B}` | Instant |
| Return from the Wilds, WOE 181 | 3 | `{2}{G}` | Sorcery |
| Gingerbrute, WOE 246 | 1 | `{1}` | Artifact **and** Creature; colorless, Haste retained |
| Hopeless Nightmare, WOE 95 | 1 | `{B}` | Enchantment |
| Forest, WOE 276 | 0 | Empty | Basic Land / Forest; produces Green, remains a colorless card |
| Evolving Wilds, WOE 256 | 0 | Empty | Nonbasic Land; absent produced mana is unavailable, not “no fixing” |
| Grabby Giant // That's Mine, WOE 133 | 4 | `{3}{R} // {1}{R}` | Creature primary; two faces, Adventure spell retained |
| Greta, Sweettooth Scourge, WOE 205 | 3 | `{1}{B}{G}` | BG Creature; both color memberships |
| Skyclave Cleric // Skyclave Basilica, ZNR 40 | 2 | `{1}{W}` | Creature primary, Land back; face cost fallback |
| Balamb Garden, SeeD Academy // Balamb Garden, Airborne, FIN 272 | 0 | Empty | Land primary, Artifact back |
| Consecrate // Consume, RNA 224 | 6 | `{1}{W/B} // {2}{W}{B}` | Instant and Sorcery; combined split value |

The real cache has **two Sole Performer Unfinity printings** with produced symbol `T`. These import safely with `UnrecognizedManaSymbols`, rather than rejecting all cards or interpreting `T` as a Magic color. Many face records omit face-specific mana value/colors; those omissions are explicitly retained instead of inferred from mana symbols.

Evidence: [cache-schema-audit.json](artifacts/phase10a-audit/cache-schema-audit.json), [normalized-cache-audit.json](artifacts/phase10a-audit/normalized-cache-audit.json), and the public offline [regression fixture](tests/Fixtures/phase10a-scryfall-cards.json). The local audit harness can rerun with:

```powershell
dotnet run --project artifacts/phase10a-audit/CacheAudit.csproj -- 'C:\Users\theyc\AppData\Local\DraftTG\card-data\scryfall-default-cards.jsonl.gz'
```

The raw cache SHA-256 is unchanged: `13af9242d12dddc7f24c3d384bb88c66751f1b444731b8989718a76527c92ee8`. Existing JSONL already contains the metadata. **No redownload, invalidation, schema bump or migration** is required; no separate normalized profile cache exists.

## Analysis and UI

Provider-neutral `DraftPoolAnalyzer` / `DraftPoolAnalysis` expose copy-aware totals and unique resolved identities, creature/land/nonland counts, every type count, W/U/B/R/G membership, colorless/multicolor counts, and nonland/creature curves. Unknown types/colors, unresolved occurrences and missing catalog records are explicit.

Colors and types overlap: BG contributes to B and G; Artifact Creature contributes to both types. Colorless counts describe actual card colors, including colorless lands, not mana production. Curve intervals are **[0,2), [2,3), [3,4), [4,5), [5,6), [6,7), 7+**, plus Unknown. Lands do not enter the spell curve. A known nonland with missing value enters Unknown; an unknown type is reported separately rather than assumed to be a spell. Fractional values fit these intervals without rounding or loss.

The status rail adds a modest **Draft pool** expander with totals, colors, spell curve, completeness and missing-data counts. Nested selectable **Pool entry diagnostics** expose identity/name/count, colors, value/cost/types/flags, Oracle text, keywords, P/T, faces and mana production. Entries do not clutter ordinary logs or badges. At Completed the panel and analysis remain available while the actionable pack disappears. There is no dead Build Deck button. Existing active Phase 9C archetype text remains a separate diagnostic; it does not influence this analysis.

The model preserves catalog identities and counts. Future corpus comparison can use the existing ID lookup and canonical names/counts from Phase 9D, retaining that corpus's drafted-vs-available-pool distinction. No comparison or deck scoring is implemented now.

## Focused tests and files

The 25 additions cover nine parameterized type lines; finite/fractional/unknown mana values; immutable optional metadata and basic types; pool multiset counts; real WOE/Adventure/transform/modal/split import; focused malformed-field errors; unknown fields and unusual mana/P/T; raw-cache reparsing without HTTP; authoritative merge and duplicate preservation; partial/unknown/unresolved coverage; exact additions after a recovered boundary; completion and engine reset; overlapping copy-aware analysis; fractional/unknown curves; missing metadata; and completed UI preservation/replacement.

Changed production files:

- `src/DraftTG.Domain/Card.cs`, new `CardGameplayMetadata.cs`, new `DraftPoolSnapshot.cs`.
- `src/DraftTG.Data/ScryfallCardNormalization.cs`, `CardCatalogImportException.cs`.
- New `src/DraftTG.Application/ArenaDraftPoolAssembler.cs`; `ArenaDraftSnapshotAdapter.cs`, `ArenaDraftSnapshotResult.cs`.
- New `src/DraftTG.RecommendationEngine/DraftPoolAnalyzer.cs`.
- New `src/DraftTG.App/DraftPoolPresentation.cs`; `MainWindowViewModel.cs`, `RailPanelView.axaml`.

Added test files: `DeckBuildingMetadataTests.cs`, `DeckBuildingNormalizationTests.cs`, `DraftPoolAssemblyTests.cs`, `DraftPoolAnalyzerTests.cs`, `DraftPoolPresentationTests.cs`, and `tests/Fixtures/phase10a-scryfall-cards.json`. Data test project includes the fixture. Existing test source is unchanged. Updated `ARCHITECTURE.md`, `ROADMAP.md`, and this report; audit/validation artifacts are local and ignored by Git. No Git index repair or staging was performed.

## Limits for Phase 10B

Pool completeness currently follows supported 3×14, single-selection formats. Unknown identities/types cannot support fully informed construction. Unobserved selections remain unknown; ending a draft alone does not recover them. No saved-pool persistence is added. Existing anonymous-session boundary limitations remain.

Mana costs remain canonical text; alternate faces, Adventure costs, split choices, fixing, colored pip requirements, role/synergy classification and land optimization need explicit later policies. Optional missing produced mana cannot prove absence of fixing. Basic lands can be recognized but are not added to any deck. Trophy evidence remains limited by its prior source/denominator restrictions.

**Stop at Phase 10A.** ROADMAP now places baseline 40-card construction in 10B, multiple builds in 10C, and deck-builder/add-remove presentation in 10D.
