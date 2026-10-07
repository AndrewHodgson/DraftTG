# Phase 9E.2A.2 — Offline Arena draft sort static audit

Date: 2026-10-07. Static, read-only Windows interoperability investigation.

## Result

**CONFIRMED FROM IL:** this installed client selects the draft comparator
**MythicToCommon → LandLast → ColorOrder → localized Title**, ascending at every
level. Equal keys preserve incoming occurrence order. There is **no final GrpId,
collector, creature or mana-value tie-break** in this draft comparator.

The main draft controller sorts before passing the collection to the holder.
The holder also has a conditional sorting path using the same static filter.
The selection is embedded managed data, not an unresolved Unity prefab enum.
This establishes the implementation for the recorded build; it is stronger
evidence than fitting one of the surviving hypotheses to screenshots.

**EXPERIMENTAL:** the isolated reference reproduces **4/4 distinct saved packs,
44/44 card positions, zero contradictions**. The production ledger has no
observations. Historical 48-pack Untapped summaries are compatible with this
key family, but cannot be independently replayed from aggregate counts alone.

Production `ArenaDisplayOrderModel`, badge placement, thresholds, recommendations
and deck building were not edited in this phase. No live gameplay, process
memory, injection, hooks, assembly execution or game-file writes were used.
Phase 9E.2B has not begun.

## Installation and build identity

**CONFIRMED FROM LOCAL DATA:** Steam app 2141910, build **25640408**, installation
`C:\Program Files (x86)\Steam\steamapps\common\MTGA` (Steam appmanifest).
Managed paths below are relative to its `MTGA_Data\Managed\` directory.

| Assembly/file | Assembly version | File version | SHA-256 |
|---|---|---|---|
| Core.dll | 0.0.0.0 | 0.0.0.0 | `947441EECC9A9231503AF434AD7970E6AF0BC2C56148D56991BBA5B6C76458E6` |
| SharedClientCore.dll | 0.0.0.0 | 0.0.0.0 | `FF02A80282F36C45D6A639C6DC5013EFF16406BCE6EB0EF9985B59E1C3244B33` |
| System.Core.dll | 4.0.0.0 | 4.6.57.0 | `A5E7D47313DA0ECA0B92A901A30498F5E970916FAF63B70B74DD62B09760ED72` |
| mscorlib.dll | 4.0.0.0 | 4.6.57.0 | `6C76E06E6D44CF0F39BF59E6A90770968B70093AD4C1572DFBC5E46E431C5FBF` |
| netstandard.dll | 2.1.0.0 | 2.1.0.0 | `6AE62E082DC494A2433984177F60CA4DB5FAE69B1F360A8B33754172B310B8C5` |
| Wizards.Arena.Models.dll | 3.20.0.0 | 3.20.0.0 | `7C955BD48BF8C136686D145244B9889AF258A48F338A3E2A77ED0CED1FD9A725` |
| Wizards.MDN.GreProtobuf.dll | 63.20.2.0 | 63.20.2.0 | `0170F35EC3AF708871D41D6A6E939F8FAF5972A18691F8C7AC168F3458BFC928` |
| Microsoft.Data.Sqlite.dll | 6.0.4.0 | 6.0.422.16106 | `CF16EF655A86ACAF00865516EC0425C1687C07AABE13984B75529D2EF003B658` |

Core MVID `304dcc50-070c-4610-9196-b6124619943f`; SharedClientCore MVID
`5028378c-e315-416c-92ef-a565601d5b8d`. Assembly versions of zero are not release
identifiers; hashes and Steam build identify the inspected code.
Full paths, sizes, MVIDs, product versions and reference names for these files
are in [inspected-build.json](tools/ArenaManagedStaticAudit/inspected-build.json).
The ignored full inventory covers the other installed managed dependencies,
including Unity modules and the interfaces assembly; no binaries were copied.

`MTGA.exe` and `UnityPlayer.dll` identify Unity **6000.3.14f1 (d68c3f99a318)**,
file version `6000.3.14.14060607`. This is the engine version, not an Arena release
number. Arena's read-only DB version rows are **Data 2026.63.0.4 / GRP 4.1327304**:
`MTGA_Data\Downloads\Raw\Raw_CardDatabase_b8e6eb972e40c139da666ec805aa8a2b.mtga`,
modified `2026-10-06 20:22:39Z`. A separate human-facing client release label was
not recovered; the identities above are the scope of this finding.

## Method and relevant types

Existing `tools/ArenaSortOrderProbe` is a Python SQLite hypothesis probe, not an
IL decoder. No `ilspycmd`/`ildasm` was on PATH and no global .NET tool was installed.
The installed SDK's `System.Reflection.Metadata`/`PEReader` APIs sufficed.
[ArenaManagedStaticAudit](tools/ArenaManagedStaticAudit/README.md) decodes metadata,
method IL, token references and embedded field data without loading Arena code.
Raw dumps stay in ignored `artifacts/arena-static-sort/`; repository additions
contain the analysis tool, independent reference and factual audit only.

**CONFIRMED FROM IL:** relevant types are `DraftContentController`,
`Wotc.Mtga.Wrapper.Draft.DraftPackHolder` (Core type `0x020011D8`),
`DraftPackCardView` (`0x020002DB`), `DraftPackLayoutData` (`0x020002D8`),
`DraftColumnMetaCardHolder` (`0x020002D5`), `DraftListMetaCardHolder`,
`CardCollection` and `Wotc.Mtga.Cards.CardSorter` (`0x02001154`). The sorter is
singular `CardSorter` in this build. SharedClientCore supplies `CardSortHelpers`
(`0x020003A1`), `SortType` (`0x020003A2`), `SortTypeFilters` (`0x020003A3`),
`CardPrintingData`, `CardPrintingRecord`, `SqlGreLocalizationProvider` and
`Languages`. `DatabaseSortTypes` is a static collection, not a type or draft
configuration.

## Actual draft construction and slot assignment

**CONFIRMED FROM IL:** principal call chain, with build-specific method tokens:

1. Core `DraftContentController.OnDraftPacksUpdatedCallback` (`0x0600745D`)
   receives `PickInfo.PackCards`, resolves the card data/cosmetic information,
   and calls the integer-list `ToCardCollection` (`0x06007465`) with its sorting
   flag **true** (`IL_00E3–00E4`).
2. `ToCardCollection` resolves incoming IDs in list order into printing records.
   When the flag is true, `IL_009D–00B3` calls `CardSorter.Sort` (`0x0600710A`)
   with database-sort skipping **false** and `SortTypeFilters.DraftPack`.
   It materializes the sorted list, attaches indices, then applies preferred
   printing/style handling and adds cards to a `CardCollection`.
3. Back in the callback, `IL_012C–0139` calls
   `DraftPackHolder.Coroutine_SetCards` (`0x060074D0`) with sorting **false**,
   because this controller already sorted. The coroutine stores the pending
   collection and flag, then enters `SetCards_internal`.
4. The holder's `SetCards_internal` state machine `MoveNext`
   (`0x0600D0D3`) consumes the pending collection. `IL_008D–00AA` conditionally
   calls `CardSorter.Sort(CardCollection, adapter, DraftPack)` (`0x06007108`)
   for callers requesting sorting. The ordinary callback above bypasses this
   second sort. Both paths select the same filter.
5. `CardCollection` maintains an insertion-ordered `_itemList`; enumeration
   (`0x06001350`) uses that list, not dictionary iteration. Repeated final
   printing IDs increase quantity on the first item. The holder enumerates
   collection items and creates `Quantity` views for each (`IL_00AB–00E1`).
6. `CreateCard` (`0x060074D6`) calls `DraftPackCardView.SetDataIncludingBans`,
   then appends the view to `_allCardViews` (`IL_0077–0083`). `LayoutCards`
   (`0x060074DC`) iterates that list; `IL_00C2–00CB` computes column `i % columns`
   and row `i / columns`, then applies upper-left plus column/row offset and
   scale. There is no later comparator in view creation/layout.

Thus the answer is **A and B**: the main holder receives a collection explicitly
sorted upstream, and has an explicit optional sort of its own. C is not the
sort selection for this path. Input order matters only for equal comparator
keys and for the no-sort branches. The string-list `ToCardCollection` overload
for other uses passes sorting false; it does not supersede the pack callback.

## Draft-specific selection and comparator semantics

**CONFIRMED FROM IL:** `SortTypeFilters.DraftPack` is initialized in
SharedClientCore `.cctor` (`0x060012AA`). It allocates four `SortType` entries,
calls `RuntimeHelpers.InitializeArray` with field `0x04001615`, then stores
field `0x04000EB6`. Reading its PE RVA `0x180708` yields exactly:

```text
06 00 00 00  00 00 00 00  01 00 00 00  05 00 00 00
Int32 little-endian: 6, 0, 1, 5
```

The enum names and helper semantics are:

| Value / SortType | Helper or operation | DB relationship | Selected for draft |
|---|---|---|---|
| 0 LandLast | 1 if `Types` contains Land (5), otherwise 0 | `Order_LandLast` | Second |
| 1 ColorOrder | Color rank table; identity for Artifact (1) **or** Land (5), printed colors otherwise | `Order_ColorOrder` | Third |
| 2 CreaturesFirst | 0 for Creature (2), otherwise 1 | `Order_CreaturesFirst` | No |
| 3 ManaCostDifficulty | Sum colored mana quantities with color enum 1–5 | `Order_ManaCostDifficulty` | No |
| 4 CMCWithXLast | CMC (minimum linked face CMC for linked-face type 6), plus 1000 if X-like mana enum 8/9 occurs | `Order_CMCWithXLast` | No |
| 5 Title | `IGreLocProvider.GetLocalizedText(printing.TitleId, null, false)` | Localizations for `TitleId`; **does not read `Order_Title` here** | Fourth |
| 6 MythicToCommon | `5 - printing.Rarity` | `Order_MythicToCommon` | First |
| 7 BasicLandsFirst | 0 for unlimited basic land, otherwise 1 | `Order_BasicLandsFirst` | No |
| 8 IsNew | Inventory new-card membership | Inventory, not a Cards sort column | No |

`CardSortHelpers.DatabaseSortTypes` (`.cctor` `0x060012A8`) is the set 0–7.
It controls skipping when `CardSorter`'s boolean asks to skip database sorting;
both draft sort calls inspected pass false. It is not the draft priority list.

`CardSorter.SortInternal` (`Core 0x0600710D`) starts with `OrderBy(_ => 0)`;
the lambda (`0x0600CEA6`) returns zero. It processes the filter left to right,
using ascending `ThenBy` with default comparers (`IL_0084` string, `IL_00ED`
integer), then returns immediately at `IL_0100–0101`. No final key is appended.

Rarity enum values: None 0, Land 1, Common 2, Uncommon 3, Rare 4, MythicRare 5.
Subtracting from 5 puts mythic first, then rare, uncommon, common, Land, None.
The land key acts **within rarity**, not globally before every common card.

The color table is not simple numeric mask order. W/U/B/R/G flags are 1/2/4/8/16.
In increasing rank 0–31, the masks are:

```text
1,2,4,8,16,3,5,6,10,12,20,24,9,17,18,7,
14,28,25,19,13,26,21,11,22,15,30,29,27,23,31,0
```

`GetColorSortOrder` (`0x060012A1`) and its predicate (`0x06001F85`) prove the
artifact-or-land identity branch; `CardColorExtensions.ToFlags` (`0x06000268/269`)
ORs WUBRG flags from the enum lists. `CardPrintingData` getters resolve the
record's Rarity, Types, Colors, ColorIdentity and TitleId fields. Numeric
`Order_*` columns are equivalent precomputed inputs in the tested packs, not
direct column reads in this in-memory comparator.

## Title, stability and final ties

**CONFIRMED FROM IL:** title lookup uses the current language because its
language argument is null. `SqlGreLocalizationProvider.GetLocalizedText`
(`0x06001493`) selects `Loc` from `Localizations_{language}` by `LocId` and
`Formatted=0`, falling back to `Formatted=1` when missing. This is localized
display text, not the normalized `Cards.Order_Title` string used by DraftTG.

Arena's installed System.Core `EnumerableSorter<TElement,TKey>.CompareAnyKeys`
(`0x0600080B`) descends to the next key on equality and finally returns
`index1 - index2` (`IL_002F–0032`). This proves stable source order for equal
keys. mscorlib `GenericComparer.Compare` (`0x0600627D`) delegates to
`IComparable<T>.CompareTo`; `String.CompareTo` (`0x060004F2`) uses current-culture
comparison. `Languages.set_CurrentLanguage` (`0x06000FB8`) sets the current
thread culture with `CreateSpecificCulture`, falling back to invariant on error.
Its static default language is en-US; actual thread culture remains a runtime
input, including when the setter sees no language change.

The pure reference takes an injected title comparer and preserves occurrences.
Its en-US host .NET replay is not proof of binary-identical Unicode collation
with Unity Mono. Missing localizations/preproduction diagnostics and preferred
printing transformations are outside the reference's raw comparator inputs.
Collection quantity grouping can coalesce equal final printing IDs after sorting;
this does not introduce a GrpId comparison. Neither saved pack contains a full
key tie, so fixtures alone could not establish stability.

## Comparison against available evidence

**EXPERIMENTAL:** replay inputs come from the existing
`tests/Fixtures/phase9e2a1-replay-metadata.json` numeric orders and
`tools/ReplayDeterministicSlotEvidence.ps1` incoming audit arrays. Audit ground
truth is the uniquely fully verified candidate's saved slots, not a newly fitted
sort order. Each reference computes keys from raw local card metadata and en-US
localized text, then compares directly with the current C# hypothesis model.

| Distinct observation | Positions matched | Full key ties | Contradictions |
|---|---:|---:|---:|
| WOE P1P6 | 9/9 | 0 | 0 |
| WOE P1P7 | 8/8 | 0 | 0 |
| Audited P1P1 | 14/14 | 0 | 0 |
| Audited P1P2 | 13/13 | 0 | 0 |
| Current Phase 9E.1 production ledger | 0 observations | n/a | 0 |

The repeated P1P2 captures at 1723×1009, 1280×720 and 1920×1080 record the
same 13-ID order; they are one observation of sorting, not three independent
packs. The empty-ledger P1P1 replay is also the same pack. No rows were written.
`--order-evidence-summary` confirms 0 observations / 400 survivors; the ledger
file is currently absent, rather than an existing zero-byte file.

**CONFIRMED FROM LOCAL DATA / EXPERIMENTAL comparison:** all 44 card occurrences
agree with the DB's `Order_MythicToCommon`, `Order_LandLast` and `Order_ColorOrder`
values. The reference results have zero DB-key mismatches. The four observations
leave **110 current C# hypotheses / 43 functional classes** on the current
**652-card** ANA/FRA/USG/WOE/WOT universe. The older report used 655 cards; that
historical universe is not asserted to be identical to today's queried rows.
`rarity>landLast>color>title` survives, with normalized title and GrpId semantics
of the model. Saved results are in ignored `artifacts/arena-static-sort/` and can
be regenerated with the tool README.

**INFERENCE, not independent replay:** the Untapped audit reports 48 orders from
three drafts, 44 surviving rules and 13 classes, including land-last/color/title
families. Those aggregates are compatible with the IL choice, but do not give
the individual arrays or the full survivor list. No sanitized historical pack
arrays were found in the repository inputs. We did not reopen raw Untapped
storage, reuse its reader or claim 48/48 validation of this exact comparator.
The original inference that every stored reader order equals Arena's visual
order also remains separate from the managed-code proof.

## Implications, remaining boundaries and recommendation

**CONFIRMED FROM IL:** managed selection removes the ambiguity about which
comparator keys this build chooses. It is no longer necessary to choose a best
fitted rarity/color rule for this build. It would resolve the competing sort
configurations behind the 43 observed-fit classes and historical 13 classes.
It does **not** make a current model class globally identical to Arena: current
rules use ordinal normalized `Order_Title` and a final GrpId key, while Arena
uses localized culture-sensitive text and stable source order. A focused test
demonstrates the reversed-ID tie difference. Replacing the model requires a
separate compatibility decision and live validation; nothing was replaced.

**UNRESOLVED:** installed-code identity after a client update; actual runtime
language/culture on all paths; complete Mono Unicode collation equivalence;
preferred printing/skin collisions; unobserved formats and duplicate packs;
historical 48-pack exact agreement without sanitized arrays. These do not hide
the comparator selection just proven. No missing assembly or Unity sort asset
blocks that selection. Core has no embedded manifest resources relevant to it;
SharedClientCore contains the static filter data. The holder/layout serialized
fields concern prefabs, parents, cameras, animation and layout arrays; they do
not supply the draft filter. Further static asset investigation would be useful
for geometry, not needed to recover this sorting priority.

**CONFIRMED FROM IL / geometry limit:** `LayoutCards` uses row-major indices and
can derive column count from configured row count and parent width. Thus its
code does not prove five columns or the empirical height-scaled pixel formula
at every aspect or pack size. Existing 8/9-card captures and missing absolute
crop-origin limitations are already documented in Phase 9E.2A.1; no new capture
measurement or envelope expansion was made. Validated geometry remains 13–14
cards, supported windowed heights/aspects. Stale-pick clearing, WinEvent tracking
and promotion designs remain as documented there; this phase adds no behavior.

Recommendation: preserve shadow-only placement and both evidence gates. Use the
reference as build-scoped implementation evidence in a separately authorized
future phase. Before promotion, establish a compatible localization/collation
policy, retain occurrence stability, account for preferred printings, verify
geometry and obtain the required live comparisons. Code proof and saved images
are not live Gate 1 or Phase 9E.2B evidence. Stop at this offline audit.

## Validation and changes

- `dotnet build DraftTG.sln`: **0 warnings, 0 errors**.
- `dotnet test DraftTG.sln --no-build`: **996 passed**, 0 failed/skipped
  (Domain 56, Recommendation 163, Data 160, ArenaIntegration 127,
  Application 244, App 246).
- Isolated audit project build: **0 warnings, 0 errors**.
- `ArenaManagedStaticAudit self-test`: **10 focused reference tests passed**;
  these are separate from the solution test count.
- Saved-pack reference replay: **4 exact matches / 4 tested**, zero
  contradictions and numeric-key mismatches.
- New implementation is confined to `tools/ArenaManagedStaticAudit/`.
  Documentation changes are this audit and appended architecture/roadmap notes.
  Existing dirty work from earlier phases was preserved; nothing was staged or
  committed and no binaries/raw IL dumps were added to repository inputs.
