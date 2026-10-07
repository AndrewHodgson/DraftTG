# DraftTG — Independent Localization Architecture Review

Date: 2026-10-05. Reviewer: Claude (independent of the Phase 9B.x / 9B.9 authors).
Question: **What is the most reliable long-term way for DraftTG to know which Arena card is at which screen rectangle?**

## Executive summary

**Short answer.** Do not move to process-memory inspection, and do not keep pure artwork search as the only way to locate cards. The most promising architecture combines two independent sources:

1. **Order without pixels.** Arena ships a local SQLite card database (`Raw_CardDatabase_*.mtga`). Its `Cards` table carries Arena's own precomputed **sort keys**: `Order_MythicToCommon`, `Order_ColorOrder`, `Order_Title`, `Order_CMCWithXLast`, `Order_CreaturesFirst`, `Order_LandLast` and `Order_BasicLandsFirst`. Both ground-truth packs in this repository, P1P6 and P1P7 (consecutive picks, 17 cards), are reproduced exactly by sorting the Player.log pack on those keys. Rarity comes first, then Arena's color order, then a within-group key. The raw log order matches neither pack.
2. **Rectangles and verification from one capture.** One event-driven capture gives slot rectangles and confirms the predicted card at each slot. When the two sources disagree, DraftTG fails closed.

This was **not found in any earlier DraftTG phase**. Phase 9B.3/9B.4 considered a sort rule but had one pack and no authoritative keys, and correctly declined it. Arena's local database supplies those keys, and they behave the same on Windows and macOS, because the Mac client ships the same database.

**But the evidence is not yet conclusive.** 120 of the 400 sort hypotheses I tested are consistent with both observed packs. They fall into 20 functionally distinct rules, which disagree on about half of randomly simulated packs. One or two real drafts, with orders recorded automatically from the existing matcher's confident results, should settle the rule. Until then the order source must only **cross-check** the existing locator.

Per Phase 12, **no production code was changed.** I added one isolated, read-only diagnostic prototype, `tools/ArenaSortOrderProbe/`. Section 14 gives a concrete evidence-gated next phase.

| Path | Verdict |
|---|---|
| Player.log identity | **Keep.** It is the semantic authority. It confirmably carries no position or order data. |
| Arena local DB sort keys → visual slot order | **New primary candidate for order**, gated on evidence collection. It is cross-platform. |
| WGC capture + artwork matcher | **Keep.** Its role shifts from open-ended search to rectangle finding plus verification. |
| Read-only Unity/process memory | **Reject.** Cards can be identified, but no robust read-only path reaches screen rectangles. macOS needs root or a debugger entitlement plus a separate IL2CPP reader. |
| UI Automation / HWND | **Closed today.** Arena exposes no card elements. Unity 6.3, which Arena uses, *can* expose screen-reader nodes with screen-space frames, so this is a cheap watch item. |
| OCR | **Deferred.** It is not needed for draft packs. Re-evaluate for deck-builder list rows. |
| Manual mapping | **Keep** as the final fallback. |

---

## Evidence labels

Every finding below is tagged:

- **[Confirmed]**: directly observed in files, data or documentation during this review, or in prior DraftTG reports that cite physical evidence.
- **[Experimental]**: produced by a prototype or experiment I ran. It is reproducible, but the sample is limited.
- **[Inference]**: reasoned from confirmed facts and not directly observed.
- **[Speculation]**: plausible, unverified, and flagged for testing.

**Review environment limits [Confirmed].** This session could read and stage files from the user's Windows machine, including the repo, Player.log and the Arena install. It could not run programs there: no remote shell was available. No draft was in progress. The current and previous Player.log contain no draft records since the last log rotation. **Live memory prototypes and multi-pick live tests were therefore not run in this session.** The live memory results in section 5 come from the Phase 9B.9 audit. All of my own experiments ran offline against real Arena data files copied read-only from the user's install.

---

## 1. Current DraftTG architecture summary

**[Confirmed from source and docs]**

```text
Player.log ──FileArenaLogSource──► ArenaDraftLogParser ──► ArenaDraftStateEngine ──► DraftPack (multiset of grpIds, log order)
                                    ArenaDeckLogParser (SceneChanged, saved CourseDeck)
                                                                     │
                                                                     ▼
                       OverlayViewModel / PlacementContext (immutable pack + generation token)
                                                                     │
                     AutomaticCardLocalizationSession : ICardVisualLocator
                        ├─ ArenaCaptureCoordinator → PreferredArenaFrameCapture → WGC (default), GDI (opt-in diag), DXGI DD (benchmark only)
                        ├─ PackFrameSynchronizer (settle delay, stale-frame/hash rejection, retries)
                        ├─ ScryfallVisualReferenceCache (small/front thumbnails, ≤14 current candidates)
                        └─ CardTemplateRecognizer (geometry hypotheses → local art registration → 20×14 NCC
                              → PackVisualAssignment exact O(n·2ⁿ) → 0.94 score / 0.07 margin / overlap guards)
                                                                     │
                     CardVisualLocalizationResult.IsSafeFor(request) → badges; else VisualCardPlacement manual map
```

- **Domain / RecommendationEngine** are platform-free. **Application** owns `ICardVisualLocator`, normalized `DraftCardLayout` (14 slots, 4–7 column profiles), `CardVisualLocalizationResult` safety validation, `VisualCardPlacement` (manual map) and `ArenaDeckDifference` (deck-builder semantics, Phase 10D).
- **App/Platform** owns Windows capture: WGC one-shot with retained device/item (9B.7/9B.8), GDI diagnostic, and Desktop Duplication benchmark (9B.10).
- **macOS** compiles the portable core and reports *Manual fallback*. No Mac capture is implemented.
- **Status:** WGC + matcher passed 9/9 (P1P6 PNG) and 8/8 (P1P7 live WGC) at unchanged thresholds. Phase 9B.7 is user-confirmed physically complete on Windows. Full-draft, low-card (5→1), resize/monitor, deck-builder and Mac acceptance remain open (9B.8, 9B.10, 10D).
- **Deck builder (10D):** semantic foundation only. It covers saved-deck parsing, `ArenaDeckSnapshot` and add/remove difference. No builder image has been captured, and no tile/row localization exists.
- **Existing research tooling:** `tools/DraftTG.LocalizationAudit` (`snapshot`, `memory`, `duplicate`) from Phase 9B.9.

## 2. Current architecture weaknesses

1. **Identity and position come from one fragile signal [Inference].** The artwork matcher must solve identity and placement together from pixels. Every failure mode lands on that single path: stale frame, geometry hypothesis, art registration, reference-printing mismatch, or a missing Scryfall image for a digital-only card. Synchronization guards reduce stale-frame risk, but no independent check confirms an accepted assignment.
2. **The history shows fragility, not a bad matcher [Confirmed].** The 0/9 → 9/9 matcher fix (registration), GDI stale-source pixels (9B.7), WGC handle growth (9B.8), and the semantic replay conflict blocking the DD benchmark (9B.10) all consumed phases. Each fix was sound, but every fix lived in the pixel path.
3. **Reference images are external [Confirmed].** Thumbnails come from Scryfall at runtime: network, rate limits, printing/URL mapping. Arena-only digital cards (Alchemy, rebalanced `A-` cards) and Arena-specific art treatments have no guaranteed Scryfall equivalent [Inference].
4. **No macOS automatic path [Confirmed].** ScreenCaptureKit is planned but not implemented.
5. **The deck builder is unaddressed visually [Confirmed].** Tiles, list rows, quantities, scrolling and filters are all unknown (10D).
6. **Card-count layouts are unsurveyed [Confirmed].** Geometry hypotheses for 14→1 cards are synthetic. Only 8- and 9-card states at 2560×1440 have real frames.

## 3. Arena log findings

### What Player.log provides [Confirmed]

| Information | Present? | Evidence |
|---|---|---|
| Current pack card IDs (multiset) | **Yes.** `DraftPack` (BotDraft/Quick Draft, zero-based `PackNumber`/`PickNumber`) or `PackCards` (`Draft.Notify`, one-based `SelfPack`/`SelfPick`) | Parser source; 9B.3/9B.4 payload audit |
| Pick submissions, picked cards, pool | Yes | Parser; 9B.10.2 |
| Screen/scene transitions | **Yes.** `Client.SceneChange {"fromSceneName","toSceneName","context"}` with scenes such as `Home`, `EventLanding`, `DeckBuilder` and (per 10D) `Draft` | Today's Player.log and Player-prev.log; `ArenaDeckLogParser` already parses it |
| Saved deck contents | Yes. Server `CourseDeck` after `EventSetDeckV3` | 10D |
| Visual order / display index / position | **No** | Payload fields are exactly `Result, EventName, DraftStatus, PackNumber, PickNumber, NumCardsToPick, DraftPack, PackStyles, PickedCards, PickedStyles`. Style arrays were empty. |
| Layout / revision / hover / selection / scroll | **No** | No such records in any audited log. A selection appears only as the submitted pick. |
| Deck-builder card lists in display order, unsaved edits | **No**, as far as observed. Saves only. | 10D. Whether unsaved edits ever log is undetermined. |

**Log order ≠ screen order [Confirmed].** P1P6 log order is `Scarecrow Guide … Hatching Plans`, but screen order is `Hatching Plans … Scarecrow Guide`. P1P7 differs as well. Neither is a reversal (9B.3).

**Conclusion [Confirmed].** Player.log can tell DraftTG *what* is on screen and *which screen* Arena is on. It cannot tell DraftTG *where*. No log-only localization exists. Section 4 shows that the missing *order* can come from another local Arena file.

**Side finding [Confirmed].** The `tests/Fixtures/arena-wgc-p1p7/manifest.json` `Source` text says the user-stated order was "contradicted by supplied PNG pixels". That referred to the earlier stale GDI image (9B.7). I viewed the fixture's current WGC PNG: it shows exactly `ExpectedVisualOrder`. The manifest note is stale.

**Cross-platform note [Confirmed / unresolved].** `ARCHITECTURE.md` uses `~/Library/Logs/Wizards Of The Coast/MTGA/Player.log` on macOS. Wizards' support article lists `~/Library/Application Support/com.wizards.mtga/Logs/Logs`. Verify on a Mac. This is not a localization issue, but it would break the semantic source on macOS.

## 4. Unity/runtime and local game-data findings

### 4.1 Arena's local card database carries display sort keys [Confirmed]

`MTGA_Data/Downloads/Raw/Raw_CardDatabase_8d9a18c7….mtga` is SQLite 3. It was opened read-only (`mode=ro`). Versions: `Data 2026.63.0.270`, `GRP 270.1318799`. The `Cards` table (27,071 rows) includes:

```text
GrpId, ArtId, ArtPath, TitleId, Rarity, ExpansionCode, CollectorNumber, Colors, Types, DraftContent, IsPrimaryCard, …,
Order_LandLast, Order_ColorOrder, Order_CreaturesFirst, Order_ManaCostDifficulty, Order_CMCWithXLast,
Order_Title, Order_MythicToCommon, Order_BasicLandsFirst
```

- `Order_MythicToCommon`: 0 mythic, 1 rare, 2 uncommon, 3 common, 4 basic.
- `Order_ColorOrder`: 0–4 is W, U, B, R, G; 5 and above encode multicolor combinations in a fixed order (5–21 observed in WOE); 31 is colorless. A few WOE cards with an empty `Colors` field still get 0–4, probably color-identity lands or artifacts (not verified). Arena's own key therefore differs from naive "colors" logic in exactly the cases where reimplementing it would go wrong.
- `Order_Title`: normalized lowercase title, for example `returnfromthewild`.
- Draftable cards in current sets have non-null keys. 6,943 rows, mostly tokens and non-draft rows, have NULL keys.

`Localizations_<lang>` tables give card names in every client language. They are useful for any future OCR. The companion `Raw_ArtCropDatabase_*.mtga` holds Arena's own art-crop rectangles per art asset (`Crops(Path, Format, X, Y, Z, W)`) [Confirmed]. DraftTG could use these to align artwork against Arena's actual crop instead of a Scryfall thumbnail [Inference].

**The client contains a sorter built on those keys [Confirmed, metadata names only].** `SharedClientCore.dll` metadata contains `CardSorters/SortType.cs`, `CardSortHelpers.cs`, `DatabaseSortTypes`, `GetDatabaseSortOrderFunc`, `GetRaritySortOrder`, `GetColorSortOrder`, `GetCreatureNoncreatureSortOrder`, `GetLandNonlandSortOrder` and `GetBasicLandsFirstSortOrder`, plus enum names matching the DB columns (`MythicToCommon`, `ColorOrder`, `CMCWithXLast`, `LandLast`, `CreaturesFirst`, `BasicLandsFirst`). `Core.dll` contains `DraftPackHolder`, `DraftPackCardView`, `DraftPackLayoutData` ("Draft Pack Holder Parameters"), `DraftColumnMetaCardHolder` and `DraftListMetaCardHolder`. I read only string/metadata names. No IL was decompiled and no code was executed.
**[Inference]** The draft holder probably orders cards with these database sort functions, and lays them out from serialized `DraftPackLayoutData`. Which sort the draft holder uses is **not** confirmed from code. Section 13 tests it empirically instead.

### 4.2 Runtime [Confirmed, Phase 9B.9 plus third-party documentation]

- Windows Steam build: Unity **6000.3.14f1**, **Mono** (`mono-2.0-bdwgc.dll`, managed DLLs in `MTGA_Data/Managed`).
- macOS build: **IL2CPP** (`GameAssembly.dylib`) according to the open-source `mtga-reader` project's documentation. This review did not verify it on a Mac.
- Managed metadata contains `DraftPackCardView : CDCMetaCardView : MetaCardView : MonoBehaviour` and `CardData → CardPrintingData → CardPrintingRecord.GrpId` (uint). Holders with view lists exist. `UnityEngine.Object.m_CachedPtr` links to the native object (9B.9).

## 5. Read-only memory inspection findings

**[Confirmed, 9B.9 live probe]** With `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` only, a bounded 18.5 MB sample found 1 raw UTF-16 occurrence among 5 current-pack IDs and **zero** typed objects or rectangles. That probe was a byte search, not a structural walk, so it does not show what a proper Mono object walk could do.

**What a proper structural reader could reach [Inference, supported by external projects].**
- Open-source *read-only* Mono/IL2CPP readers already traverse Arena's managed objects by class and field *name*, with offsets resolved from runtime metadata rather than hardcoded. Examples: `mtga-tracker-daemon` (UnitySpy-based) and `mtgatool/mtga-reader`, a Rust port of UnitySpy that reads decks, collection, inventory and ranks on Windows/Mono **and** macOS/IL2CPP. **Identity reached through managed objects is therefore plausible**: `DraftPackHolder` → card views → `CardData` → `GrpId`. Probably **visual order** too, as the holder's list order [Speculation until demonstrated].
- **Screen rectangles are a different matter.** `RectTransform` and `Transform` state, canvas scaling and camera matrices live in Unity's **native** C++ objects behind `m_CachedPtr`, not in managed fields (9B.9 found only native or injected accessors). Reading them means hardcoding undocumented native layouts per Unity version, then reimplementing hierarchy composition, canvas scaling, camera projection, clipping and DPI. Draft cards are `CDC` (3D card display) views, so a perspective camera is likely in the path [Speculation]. That is exactly the technique used by game-cheat ESP code, which the brief rules out as a dependency. It also breaks on any Unity upgrade.
- **macOS [Confirmed from documentation].** Apple's debugging-tool entitlement allows `task_for_pid` for targets carrying `get-task-allow`, needs admin authorization when run by a non-root user, and cannot reach SIP-protected processes. `mtga-reader` documents that macOS needs **root or the debugger entitlement**. It reports Arena's Mac bundle as currently *unhardened* (`flags=0x0`) and warns this could change in any update. That is a third-party observation.
- **Untapped [Confirmed from public pages].** The MTGA-specific help article says the Companion **watches Player.log**. Only the generic, multi-game article mentions read-only memory access. The Draftsmith page I fetched today offers only a **"Download for Windows"** link. Untapped is evidence that per-card overlays are achievable. It is **not** evidence that memory reading is how they do it, or that it works on macOS.

**Decision: reject memory inspection as a localization source.** It cannot robustly reach rectangles without native-layout reading, which is fragile and ESP-like. The best it could deliver is identity plus order. Section 4.1 delivers the same from a documented-shape local data file, with no process access, no admin rights and no anti-cheat exposure. It would also need two separate readers, Mono on Windows and IL2CPP on macOS, plus root or an entitlement prompt on Mac.

## 6. UIA and native-window findings

- **[Confirmed, 9B.9, twice on the real desktop]** UIA root is a `Pane "MTGA"` with `UnityWndClass` and **0 raw-view descendants**. The HWND tree is one Unity surface plus two hidden IME helpers. No card child windows exist. **Both paths stay closed** as card locators. HWND remains the right source for the client rectangle, DPI, monitor and visibility.
- **New [Confirmed from Unity documentation].** Unity **6.3**, Arena's engine line, added **native desktop screen-reader support** for Narrator on Windows and VoiceOver on macOS. Developers opt in by assigning `AssistiveSupport.activeHierarchy`. Each `AccessibilityNode` has `label` and `frame` ("bounding rectangle … in screen coordinates"). Arena has not opted in (0 descendants).
  **[Speculation]** If Wizards ever ships accessibility for Arena, card nodes with labels and screen frames would be the best possible cross-platform structural source. **Recommendation:** at startup, log the UIA descendant count (and the AX child count on Mac). This costs nothing and reopens the path automatically.

## 7. Visual recognition findings

- **[Confirmed]** With the registration fix, the existing matcher is accurate on real frames: 9/9 with scores of 0.980–0.996, and 8/8 with 0.968–0.995. On both frames the matcher's assignment **equals** the order predicted by the DB sort keys (section 13). The two methods agree independently.
- **[Confirmed]** WGC one-shot capture of the calibrated region costs about 0.3–0.45 s cold, plus matching. DD is comparable (9B.10). Neither needs continuous capture.
- **[Experimental, from viewing both frames]** At 2560×1440, 8- and 9-card packs use the **same left-aligned 5-column grid**: identical slot origins, about 265 px column pitch and about 356 px row pitch in the 1325×1131 crop (slot origins from the artwork-matcher report). The slot index is therefore a stable function of position for these counts. Other counts, resolutions and window modes are unobserved.
- **OCR [Confirmed constraints].** `Windows.Media.Ocr` is supported only for apps with package identity (MSIX). DraftTG is unpackaged, so OCR on Windows means packaging or bundling an engine. Apple Vision is local and supports custom words. OCR finds text lines, not card rectangles. For draft packs it adds no signal the matcher lacks [Inference]. For deck-builder **list rows**, where card names are the dominant visual feature, it may become the best signal [Inference].

## 8. Deck-builder implications

- **[Confirmed]** DraftTG already knows the target deck (Phase 10C) and the saved deck (10D). It does not know the *current unsaved* editor contents or anything visual.
- **[Inference]** The order-key approach generalizes partially. Arena's builder sorts its pool and deck with the same `CardSortHelpers` family. The builder has user-selectable sorts or filters and scrolls, so order is not a pure function of contents the way a draft pack is. The visible window is still a **contiguous slice** of a predictable sequence. Sequence alignment between detected tiles and the predicted full order is a strong constraint for both identity and scroll offset [Speculation until builder frames are observed].
- **Recommended builder stack [Inference]:**
  - Scene gate: `Client.SceneChange → DeckBuilder`.
  - Candidate set: drafted pool (≤45 distinct) plus basics.
  - Tile identity: artwork matcher with Arena art crops, using predicted-order alignment as a prior.
  - List rows: row detection plus name OCR or row-art matching, constrained to the pool.
  - Quantities: read from row badges.
  - Fail closed per tile.
- Memory would not fix the builder either. It might reveal the visible item list, but still no rectangles.
- **Do not build builder outlines until real builder frames exist** (the 10D gate).

## 9. Cross-platform implications

| Component | Windows | macOS | Notes |
|---|---|---|---|
| Player.log | Exists | Exists (path to verify, section 3) | Same records [Inference] |
| Arena card DB sort keys | `…\MTGA_Data\Downloads\Raw\` (Steam) or the default install path | `~/Library/Application Support/com.wizards.mtga/Downloads/Raw/` (per `mtga-mcp`) | Same SQLite schema [Inference]. Read with Microsoft.Data.Sqlite (portable). |
| Capture | WGC (done), DD (benchmark) | ScreenCaptureKit `SCScreenshotManager` (planned) | Mac needs Screen Recording permission |
| Artwork verification | Portable Skia | Same | Same code |
| OCR (later) | Needs MSIX or a bundled engine | Apple Vision | Builder only |
| Memory reading | Mono reader; no admin needed for same-user processes | IL2CPP reader plus root or debugger entitlement | Rejected |
| UIA / AX watch | UIA probe exists | `AXUIElement` with Accessibility trust | Free to keep |

The recommended architecture introduces **no Windows-only dependency**. Its only platform-specific part, capture, is already behind `IArenaRegionCapture`.

## 10. External technical sources

Untapped / Arena companions:
- [Untapped MTGA Companion — "How does the Untapped.gg Companion work?" (log-based)](https://help.hearthsim.net/en/articles/5020719-how-does-the-untapped-gg-companion-work)
- [Untapped generic Companion article (read-only memory, standard Windows/macOS APIs)](https://help.hearthsim.net/en/articles/8377705-how-does-the-untapped-gg-companion-work)
- [Untapped Draftsmith page (Windows download only, as fetched 2026-10-05)](https://mtga.untapped.gg/draftsmith) · [What is Draftsmith?](https://help.hearthsim.net/en/articles/5415743-what-is-draftsmith)
- [frcaton/mtga-tracker-daemon (memory reader, Windows/Linux)](https://github.com/frcaton/mtga-tracker-daemon)
- [mtgatool/mtga-reader (UnitySpy port; Windows/Mono and macOS/IL2CPP; root/admin)](https://github.com/mtgatool/mtga-reader) · [its macOS IL2CPP notes](https://raw.githubusercontent.com/mtgatool/mtga-reader/main/docs/MACOS_IL2CPP.md)
- [hackf5/unityspy (MIT, read-only Mono reader)](https://github.com/hackf5/unityspy)
- [fkadriver/mtga-mcp (Mac Raw_CardDatabase path)](https://github.com/fkadriver/mtga-mcp)
- [bstaple1/MTGA_Draft_17Lands (log-based draft tool)](https://github.com/bstaple1/MTGA_Draft_17Lands)
- [Wizards: Player.log locations and Detailed Logs](https://mtgarena-support.wizards.com/hc/en-us/articles/360000726823-Creating-Log-Files-on-PC-Mac-Steam)

Unity:
- [Unity 6.3 native desktop screen-reader support](https://discussions.unity.com/t/native-desktop-screen-reader-support-now-available-in-unity-6-3/1681788)
- [AccessibilityNode (frame in screen coordinates)](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Accessibility.AccessibilityNode.html) · [AssistiveSupport](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Accessibility.AssistiveSupport.html)
- [RectTransform.GetWorldCorners](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RectTransform.GetWorldCorners.html) · [RectTransformUtility.WorldToScreenPoint](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RectTransformUtility.WorldToScreenPoint.html) · [Scripting backends (Mono vs IL2CPP)](https://docs.unity3d.com/6000.3/Documentation/Manual/scripting-backends-intro.html)

Microsoft / Apple:
- [Windows.Media.Ocr (package identity required)](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr?view=winrt-26100)
- [IGraphicsCaptureItemInterop.CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow) · [Screen capture overview](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
- [Desktop Duplication API](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api) · [ReadProcessMemory](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-readprocessmemory) · [Obtaining UI Automation elements](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/obtaining-ui-automation-elements)
- [Apple debugging-tool entitlement (`task_for_pid` conditions)](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.cs.debugger)
- [ScreenCaptureKit](https://developer.apple.com/documentation/ScreenCaptureKit?language=objc) · [SCScreenshotManager](https://developer.apple.com/documentation/screencapturekit/scscreenshotmanager) · [Vision text recognition](https://developer.apple.com/documentation/vision/recognizing-text-in-images)

Card data: [Card Kingdom — Rimefur Reindeer rarity (C)](https://www.cardkingdom.com/mtg/wilds-of-eldraine/rimefur-reindeer). This is a cross-check only. The Arena DB itself gives `Order_MythicToCommon = 3`.

## 11. Decision matrix

Scale: 1 is poor and 5 is best. For effort and maintenance, 5 means *low*. Scores combine measured evidence with labeled inference. **Gates** come first: an approach that fails one cannot be primary, however it scores.

**Gates:** (G1) fails closed on its own errors; (G2) has a credible macOS path; (G3) is read-only and passive without admin or root prompts.

| | A. Current: log + WGC + artwork | B. Log + Arena sort keys + fixed layout (no pixels) | C. Log + OCR | D. Log + artwork + OCR | E. Log + Unity memory → rects | F. Memory primary + visual fallback | **G. Log + sort keys + capture verify (recommended)** |
|---|---|---|---|---|---|---|---|
| G1 fail-closed | ✔ | **✘** (wrong rule → silent misplacement) | ✔ | ✔ | ✘ (no rect proof) | ✔ via fallback | ✔ (two-source agreement) |
| G2 macOS path | ✔ planned (SCK) | ✔ | ✔ Vision | ✔ | **✘** IL2CPP + root/entitlement | **✘** | ✔ |
| G3 passive, no elevation | ✔ | ✔ | ✔ | ✔ | ✘ on Mac | ✘ on Mac | ✔ |
| Semantic accuracy | 4 | 3 (rule unproven) | 4 | 5 | 4 | 4 | **5** |
| Rectangle accuracy | 4 | 2 (layouts unsurveyed) | 3 | 4 | 1 (undemonstrated) | 3 | **4** |
| Latency | 3 | 5 | 3 | 2 | 4 | 3 | 3–4 |
| CPU/GPU cost | 3 | 5 | 2 | 2 | 3 | 3 | 3 |
| Implementation effort | 5 (exists) | 4 | 2 | 2 | 1 | 1 | **4** |
| Maintenance burden | 3 | 4 | 3 | 2 | 1 | 1 | 4 |
| Arena-version fragility | 3 | 3 (silent if rule changes) | 3 | 4 | 1 | 2 | **4** (a change shows as disagreement) |
| Windows | 4 | 5 | 2 (MSIX/engine) | 3 | 2 | 2 | 4 |
| macOS | 2 (capture TBD) | 4 | 4 | 3 | 1 | 1 | 3 |
| Deck-builder suitability | 3 | 2 | 4 (list rows) | 4 | 3 (no rects) | 3 | 3 (+order prior) |
| Privacy | 4 (local crops) | 5 (no pixels) | 4 | 4 | 2 (process memory) | 2 | 4 |
| Failure recovery | 4 | 1 | 3 | 4 | 2 | 4 | **5** |
| **Unweighted sum** | 42 | 43, but **fails G1** | 37 | 39 | 25, **fails G2/G3** | 29, **fails G2/G3** | **46–47** |

Approach H, UIA/AX via Unity 6.3 accessibility, would score near 5 everywhere **if it existed**. It is unavailable today (0 nodes), so it stays a watch item.

## 12. Recommended architecture

**What I would build if this were my product (Option 4, evidence-supported):**

```text
                         ┌─────────────── semantic authority (unchanged) ───────────────┐
Player.log ─────────────►│ DraftPack multiset + coordinate + generation; SceneChange      │
                         └───────────────────────────────┬───────────────────────────────┘
                                                         │
Raw_CardDatabase (read-only) ──► ArenaDisplayOrderModel ─┤  predicted slot order + confidence
  Order_* keys + learned rule      (rule chosen by evidence ledger;                          
                                   Unanimous / Discriminating / Unavailable)                 
                                                         ▼
               one event-driven capture after settle (WGC; SCK on Mac; DD optional)
                                                         ▼
     CardTemplateRecognizer (existing) → rectangles + per-slot scores for ALL candidates
                                                         ▼
                OrderVerifiedCardLocator (decorator over ICardVisualLocator)
     ┌─────────────────────────────────────────────┬───────────────────────────────────────┐
     │ matcher assignment == predicted order        │ publish (Confirmed)                    │
     │ matcher partial, prediction Unanimous,       │ publish predicted identities for       │
     │   slot count == pack size, every slot's      │   unresolved slots (Phase 2, after     │
     │   predicted-card score ≥ calibrated verify   │   calibration from the ledger)         │
     │ disagreement / stale / count mismatch        │ hide badges, log evidence → retry/manual│
     └─────────────────────────────────────────────┴───────────────────────────────────────┘
                                                         ▼
                               VisualCardPlacement manual mapping (unchanged, last resort)
```

Why this design:

1. **Two independent signals** must agree before anything is drawn. A stale frame, wrong geometry, misregistered art or an Arena change to sort order or layout each produce a *disagreement*, never a silently wrong badge. Today a single-signal error can only be caught by the matcher's own thresholds.
2. **The order model is a pure function of local data.** It is instant, pixel-free and portable. It also works for cards whose artwork is ambiguous or missing from Scryfall.
3. **Capture stays event-driven** (one frame per pack, as now). No high-FPS loop, no upload, no process access.
4. **It is self-validating over time.** Every confident matcher result becomes an order observation in a local ledger (IDs only). If Arena changes its sort, the ledger refutes the rule and the order model disables itself automatically. DraftTG then falls back to today's behavior.
5. **It extends to the builder** as an order prior for slice alignment, without a separate technology.
6. **It retains every existing safety contract**: generations, tokens, `IsSafeFor`, the 0.94/0.07 thresholds and manual mapping.

What I would **not** build: memory reading, a continuous capture loop, cloud OCR, a hardcoded sort rule shipped without the ledger gate, or builder outlines before real builder frames exist.

## 13. Prototype results

### P1 — Arena sort-key order probe [Experimental]

The tool is `tools/ArenaSortOrderProbe/arena_sort_order_probe.py`: read-only, stdlib Python, outside the solution. Run here against the user's real Arena DB (`Data 2026.63.0.270`) and the repo's fixtures.

**Hypothesis family.** Rarity (`Order_MythicToCommon`) is the primary key, followed by any ordered choice of 1–3 of {color, landLast, creaturesFirst, cmc, basicLandsFirst, title, collector, grpId}, with a `GrpId` tiebreak. That gives 400 hypotheses.

| Observation | Cards | Log order = screen? | Ground truth source |
|---|---:|---|---|
| WOE Quick Draft P1P6 | 9 | No | User-confirmed, PNG fixture, matcher 9/9 |
| WOE Quick Draft P1P7 (next pick) | 8 | No | Live WGC PNG, matcher 8/8 (I viewed it) |

Results:
- **120 / 400 hypotheses reproduce both packs exactly.** Survivors include `rarity>color>title`, `rarity>color>collector`, `rarity>collector` and `rarity>color>creaturesFirst>…`.
- **Refuted examples:** `rarity>color>cmc>title`. P1P6 red commons are Grabby Giant (4), Merry Bards (3), Redcap Thief (3). Raw log order, ascending/descending GrpId and plain name order were already refuted in 9B.3.
- Over the full WOE+WOT draft pool, the 120 survivors collapse to **20 functionally distinct rules**.
- **Simulated discrimination:** 3,000 random packs per set, sizes 1–14/15, about 1/7.4 mythic upgrade.

| Set | Survivors unanimous | 2 orders | 3+ orders |
|---|---:|---:|---:|
| WOE (+WOT bonus) | 51.0% | 34.8% | 14.2% |
| FRA | 49.9% | 35.5% | 14.6% |
| ECL | 52.6% | 38.0% | 9.3% |
| SOS | 50.8% | 37.0% | 12.2% |
| HOB | 56.6% | 32.3% | 11.1% |
| MSH | 60.8% | 37.3% | 1.9% |

**Interpretation.** Two packs prove that a rarity-then-color-group structure explains Arena's order with zero contradictions across 17 cards. They do **not** pin down the within-group key. About half of real packs are discriminating, so one complete draft (~40 packs, ~20 discriminating) should collapse the 20 classes to one, or refute the whole family. **The evidence is inconclusive for production promotion.** That is why the Phase 12 decision below is to stop.

### P2 — Player.log audit [Confirmed]

Scanned today's Player.log (372 lines) and Player-prev.log (5,675 lines):
- Request/response families: `GraphGetGraphState`, `EventGetCoursesV`, `QuestGetQuests`, `DeckGetDeckSummariesV`, `EventClaimPrize`, `EventEnterPairing`, `RankGet…` and others.
- Scenes: `Home`, `EventLanding`, `DeckBuilder`, plus `DuelScene`/`MatchEndScene` unloads.
- No draft records, because no draft occurred after rotation.
- No position, order, layout, hover or scroll fields of any kind.

The probe's `log` command was validated on synthetic lines in both the BotDraft (escaped `DraftPack`, zero-based) and `Draft.Notify` (`PackCards`, one-based) shapes.

### P3 — Arena metadata names [Confirmed]

String-level metadata search of `SharedClientCore.dll` and `Core.dll` found the sorter and holder names listed in section 4.1. No IL was read and no game code was executed.

### P4 — Memory, UIA, HWND (Phase 9 items 1–6)

Not re-run in this session: there was no remote shell and no active draft. The Phase 9B.9 results stand:
- UIA: 0 descendants.
- HWND: no card children.
- Memory: raw-hit only, 0 rectangles.

**I am not claiming any structural solution reaches screen rectangles. None was demonstrated, and section 5 explains why I do not expect one without native-layout reading.**

### Phase 12 decision

**Stop after the audit.** The architecture is clearly better *as a design*: two-source fail-closed, cross-platform, no new risk. But its key empirical premise, the exact sort rule, is not yet proven. No production code, scoring, deck-building, statistics, parser or capture code was modified. Added files: this document and `tools/ArenaSortOrderProbe/` (prototype plus README).

## 14. Concrete next implementation phase

**Phase 9E — Order-verified localization (evidence first).** Each step has a gate. Nothing user-visible changes until gate 2.

**Step 1 — Evidence collection (no UI changes). Small; Windows-testable now.**
1. `DraftTG.Data/ArenaCardDatabaseSortKeys.cs`. Locate the newest `Raw_CardDatabase_*.mtga`: Steam path, default Windows path, macOS `~/Library/Application Support/com.wizards.mtga/Downloads/Raw/`, or an explicit override. Open it read-only with `Microsoft.Data.Sqlite` (`Mode=ReadOnly`). Validate that the required `Order_*` columns exist. Return keys for requested GrpIds, or `Unavailable`. Never copy or modify the file. Hold no lock beyond a query.
2. `DraftTG.Application/ArenaDisplayOrderModel.cs`. Hypothesis family as in the prototype. `Evaluate(observations)` returns the surviving rules. `Predict(pack)` returns `(order, Unanimous | Discriminating | Unavailable)`. Pure and portable.
3. `DraftTG.App/OrderEvidenceRecorder.cs`. When a live `CardVisualLocalizationResult.IsSafeFor(request)` is true **and** all n occurrences are HighConfidence/Confirmed, append one JSONL record (IDs, event, coordinate, matched visual order, client size, card count, Arena DB `Data` version) to the app-data directory. IDs only, no pixels.
4. Rail diagnostics line: `Predicted order: agrees | DISAGREES | undetermined (n rules)`.
5. Tests:
   - A tiny **synthetic** SQLite fixture with the same schema and key values for the 17 fixture cards. Do not commit Wizards' database.
   - Hypothesis scoring reproducing section 13.
   - Unavailable/missing-column fail-closed behavior.
   - Recorder writes only on full-confidence results.
   - Rail line.

**Gate 1.** At least 2 complete drafts (≥80 packs) recorded. One equivalence class survives, with **zero contradictions**. Coverage must include:
- ≥1 rare and ≥1 mythic
- multicolor and hybrid cards
- a bonus-sheet card at the same rarity as a main-set card
- duplicates and nonbasic lands
- a full 14/15-card P1P1
- two different Arena window sizes

If the whole family is refuted, close Phase 9E and keep today's architecture.

**Step 2 — `OrderVerifiedCardLocator : ICardVisualLocator`** (decorator around `AutomaticCardLocalizationSession`). Publish only when matcher assignment equals the prediction. On disagreement, hide badges, record evidence and retry once. This is strictly safer than today.

**Gate 2.** One more complete draft with zero false placements, and every disagreement explained.

**Step 3 — Assisted completion.** When the matcher is partial and the prediction is Unanimous, the detected slot count must equal the pack size and each slot's *predicted-card* score must clear a verification threshold calibrated from ledger score distributions. Thresholds are not lowered without data. This reduces manual mapping without weakening identity safety.

**Step 4 — macOS.** Implement the existing ScreenCaptureKit plan behind `IArenaRegionCapture`. Reuse `ArenaCardDatabaseSortKeys` with the Mac path. Verify the Mac Player.log path discrepancy (section 3). Validate one Mac draft against the same ledger.

**Step 5 — Deck builder (after the 10D visual audit gate).**
1. Capture real builder frames during normal use.
2. Record tile/list layouts and the sort selector state.
3. Extend the ledger to builder ordering.
4. Implement tile identity with the artwork matcher plus a predicted-sequence alignment prior.
5. Evaluate OCR only for list rows.
6. Wire outlines through the existing `ArenaDeckDifference`.

**Always-on, near-zero cost.** At startup, log the UIA raw descendant count on Windows and the AX child count on macOS. If either is ever above 0, open a structural-accessibility investigation (section 6).

**Housekeeping (documentation only).**
- Update the stale `Source` note in `tests/Fixtures/arena-wgc-p1p7/manifest.json` (section 3).
- Optionally evaluate `Raw_ArtCropDatabase` crops as an Arena-native art registration aid.
