# Live Windows artwork matcher audit

**The exact supplied PNG now matches all 9 cards correctly, with the existing 0.94 threshold and 0.07 ambiguity margin unchanged.** The old matcher reproduces 0/9 because its coarse artwork alignment depresses every correct candidate's score. References, assignment and Windows capture were retained.

## Evidence and reproduction

The user supplied arena-draft-capture.png (1325 x 1131) and its Phase 9B.5 log from the temporary DraftTG directory. The log shows UnityWndClass HWND 0x140C8E, MTGA PID 42472, visible/non-minimized/non-foreground Arena, 2560 x 1440 client bounds, DPI 1.0, Medium integrity for both apps, initialized capture and a fresh frame. The PNG visibly contains the nine identified cards. The log's Localization invoked: no reflects the save-capture action, which deliberately bypasses recognition; the reported prior automatic attempt was matched 0/9.

The source PNG and fixture have identical SHA256:

`0524FEC7B15BC8699A81400F2A3677DB3DE33D05E86CB1956344672A4C2ACCA1`

The first offline run used the unchanged matcher objective/proposals and all nine cached references. It reproduced 0/9 before implementing registration. The preserved original source is artifacts/artwork-matcher/original-recognizer.cs.txt. The same serialized pack identity and five-column calibration profile were used. Expected visual order is ground truth for reports/assertions only and is not supplied to the recognizer.

Detailed evidence files:

- [Before: every slot, rectangle, candidate, score and rejection](artifacts/artwork-matcher/before/scores.txt)
- [Before: full winning-proposal similarity matrix and candidate rectangles](artifacts/artwork-matcher/before/scores.json)
- [After: every slot, rectangle, candidate and score](artifacts/artwork-matcher/after/scores.txt)
- [After: full similarity matrix and candidate rectangles](artifacts/artwork-matcher/after/scores.json)
- [Before annotated frame](artifacts/artwork-matcher/before/localization-latest.png)
- [After annotated frame](artifacts/artwork-matcher/after/localization-latest.png)

## Before-fix slot evidence

Coordinates below are full-card proposals in captured-frame pixels, x/y/width/height. Both full-card and actual artwork-core rectangles are included in the linked detailed text/JSON. Score is the minimum of global and independent left/right normalized RGB correlations; it is a similarity gate, not a calibrated probability. Each row's assigned candidate equals its best candidate and the expected card.

All rows require score >= 0.94 and identity/competing-slot margin >= 0.07. None fails margin or overlap, and there are no missing references. The exact rejection reason for every row is assigned score below 0.94.

| Slot | Crop x/y/w/h (px) | Expected | Best | Best score | Second | Second score | Threshold | Result/rejection |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | --- |
| 1 | 21.73/34.11/251.75/351.76 | Hatching Plans | Hatching Plans | 0.900651 | Stonesplitter Bolt | 0.278161 | 0.94 | assigned score 0.900651 below threshold 0.94 |
| 2 | 286.73/34.11/251.75/351.76 | Stonesplitter Bolt | Stonesplitter Bolt | 0.883120 | Merry Bards | 0.061931 | 0.94 | assigned score 0.883120 below threshold 0.94 |
| 3 | 551.73/34.11/251.75/351.76 | Rat Out | Rat Out | 0.803668 | Grabby Giant // That's Mine | 0.469201 | 0.94 | assigned score 0.803668 below threshold 0.94 |
| 4 | 801.63/34.11/251.75/351.76 | Grabby Giant // That's Mine | Grabby Giant // That's Mine | 0.795374 | Rat Out | 0.428388 | 0.94 | assigned score 0.795374 below threshold 0.94 |
| 5 | 1066.63/34.11/251.75/351.76 | Merry Bards | Merry Bards | 0.805130 | Return from the Wilds | 0.241781 | 0.94 | assigned score 0.805130 below threshold 0.94 |
| 6 | 21.73/389.62/251.75/351.76 | Redcap Thief | Redcap Thief | 0.906660 | Scarecrow Guide | 0.356164 | 0.94 | assigned score 0.906660 below threshold 0.94 |
| 7 | 286.73/389.62/251.75/351.76 | Return from the Wilds | Return from the Wilds | 0.856670 | Gingerbrute | 0.369478 | 0.94 | assigned score 0.856670 below threshold 0.94 |
| 8 | 570.61/413.29/213.99/304.43 | Gingerbrute | Gingerbrute | 0.788314 | Return from the Wilds | 0.462362 | 0.94 | assigned score 0.788314 below threshold 0.94 |
| 9 | 801.63/389.62/251.75/351.76 | Scarecrow Guide | Scarecrow Guide | 0.876549 | Return from the Wilds | 0.482834 | 0.94 | assigned score 0.876549 below threshold 0.94 |

## Root cause and alternatives audited

The matcher already compares an artwork-dominant core, rather than the full card. Its fixed core is x=0.075, y=0.165, width=0.85, height=0.40 within a hypothesized portrait card. Both the reference and captured core are area-averaged to 20 x 14 RGB samples, centered and energy normalized. The main rules text and most outer frame are excluded, although a thin artwork/type-line boundary can remain in some frame styles.

The old geometry search had relatively large scale steps (0.85/1.0/1.1), portrait/as-calibrated aspect choices and origin steps of 0.06 of a cell. This found the right cards, but not sufficiently accurate artwork alignment. Arena and paper-card thumbnail rendering differ in proportions/resampling, so matching a portrait-card hypothesis does not precisely align the art core. NCC is sensitive to those shifts/stretches.

For example, Hatching Plans' core changes from 40.61/92.15/213.99/140.70 to approximately 44.67/98.94/208.32/136.14 px. Gingerbrute's old core was too small and displaced: 586.66/463.52/181.89/121.77 becomes approximately 567.62/456.69/208.28/135.43 px. Correcting local geometry using the same image/descriptor/threshold raises both scores above 0.98.

| Candidate cause | Evidence/conclusion on this PNG |
| --- | --- |
| Windows capture | Correct nine-card regional image and successful capture stages; no redesign needed. |
| Incorrect card rectangles | Correct visual neighborhoods/order, but coarse rectangles have origin/size errors. These cause inaccurate sub-crops, particularly Gingerbrute. |
| Incorrect artwork sub-crops | Primary failure: coarse alignment shifts/stretches the compared art. Fine alignment alone fixes all nine. |
| Missing/wrong references | No missing references; exact-ID cache thumbnails all decode, and their visible art corresponds to the frame. |
| Printing/art mismatch | No mismatch observed for these exact Arena/Scryfall IDs; unchanged references reach 0.980-0.996 after alignment. No name-first printing substitution was used. |
| Excessive frame/text inclusion | Full-card comparison is not implemented. Small boundary contamination is possible with coarse crops, but no wholesale frame/text removal or OCR was needed. |
| Resize/aspect mismatch | A contributing geometry issue. Independent fine width/height alignment and identical descriptor sampling address it. |
| Similarity algorithm weakness | Its sensitivity to misregistration matters; the existing correlation objective is sufficient once aligned on this frame. |
| Threshold too strict | Every old score fails 0.94, but lowering the gate is unnecessary after alignment. The 0.94 gate stays unchanged. |
| One-to-one assignment | Original assignment is already correct for all nine. Same solver is reused after refinement. |

## Algorithm change

After selecting the best coarse proposal, register each candidate artwork locally with bounded coordinate descent. Test origin translation and width/height independently around the artwork center, using the existing normalized RGB descriptor/correlation score. Step sizes are 0.04, 0.02, 0.01, 0.005 and 0.0025; each has at most four iterations and eight neighbors. Origin must remain within 10% of the coarse seed's dimensions; width/height must remain within 85%-115% of the seed. Invalid/out-of-frame rectangles are skipped and the score only improves.

This preserves a small, bounded local search rather than assuming Arena's text/title/frame scaling equals the reference. Both images use identical 20 x 14 area averaging and mean/energy normalization. Original physical crop aspects may differ slightly; independent dimension refinement corrects them while mapping both to the same descriptor grid.

Recompute the similarity matrix, solve the unchanged exact occurrence assignment, then apply the unchanged 0.94 score, 0.07 identity/slot margin, overlap, variance and minimum-pack-evidence guards. No threshold was lowered, no OCR/dependency was added and no expected visual ordering is used. Manual fallback and asynchronous result guards remain unchanged.

## Reference-image preparation audit

Production ScryfallVisualReferenceCache indexes the existing local compressed bulk file by printing UUID and obtains image_uris.small. It loads visual-references/<exact CardIdentifier UUID>.jpg or downloads that UUID's allowlisted small/front URI if absent. This audit needed no downloads: all nine existing files were copied as offline fixtures. Each is 146 x 204. The whole thumbnail is decoded, then its central art core is normalized by the recognizer; art_crop is not fetched or used.

The reference files/URIs were not changed. Metadata includes art_crop URIs for provenance, but the matcher continues using the existing small front snapshots. Using the same exact-ID references for the before/after runs isolates alignment as the fix. Names, rarity, recommendation rank or a first printing from a name search never select an image.

| Card | Arena ID | Printing UUID | Set/collector | Used reference URI |
| --- | ---: | --- | --- | --- |
| Scarecrow Guide | 86982 | 3dc08461-bec6-4a18-b782-da4c92abb789 | woe/250 | [small/front](https://cards.scryfall.io/small/front/3/d/3dc08461-bec6-4a18-b782-da4c92abb789.jpg?1783915057) |
| Return from the Wilds | 86889 | 9597d9ea-d9b2-4009-8e7c-02caa3585bc5 | woe/181 | [small/front](https://cards.scryfall.io/small/front/9/5/9597d9ea-d9b2-4009-8e7c-02caa3585bc5.jpg?1783915079) |
| Redcap Thief | 86849 | cda6bdeb-a0d6-46ca-ba8c-317ee0096416 | woe/147 | [small/front](https://cards.scryfall.io/small/front/c/d/cda6bdeb-a0d6-46ca-ba8c-317ee0096416.jpg?1783915089) |
| Grabby Giant // That's Mine | 86831 | fab7646a-61e8-446b-9dba-ac6e0db82f10 | woe/133 | [small/front](https://cards.scryfall.io/small/front/f/a/fab7646a-61e8-446b-9dba-ac6e0db82f10.jpg?1783915094) |
| Merry Bards | 86840 | b0058b9b-e919-45eb-9da0-690f62aa252e | woe/140 | [small/front](https://cards.scryfall.io/small/front/b/0/b0058b9b-e919-45eb-9da0-690f62aa252e.jpg?1783915092) |
| Gingerbrute | 86978 | 09a4578a-7dc6-4da3-93ee-913b10be5740 | woe/246 | [small/front](https://cards.scryfall.io/small/front/0/9/09a4578a-7dc6-4da3-93ee-913b10be5740.jpg?1783915058) |
| Rat Out | 86799 | f2c42755-bf91-4c75-95c6-d2a60ba3492a | woe/103 | [small/front](https://cards.scryfall.io/small/front/f/2/f2c42755-bf91-4c75-95c6-d2a60ba3492a.jpg?1783915104) |
| Stonesplitter Bolt | 86853 | fb22f79c-3075-439d-a072-ceaabe35d76f | woe/151 | [small/front](https://cards.scryfall.io/small/front/f/b/fb22f79c-3075-439d-a072-ceaabe35d76f.jpg?1783915088) |
| Hatching Plans | 87058 | e8ebcbfb-1522-4ff5-b23a-f3ea9e08ad9b | wot/20 | [small/front](https://cards.scryfall.io/small/front/e/8/e8ebcbfb-1522-4ff5-b23a-f3ea9e08ad9b.jpg?1783914928) |

All nine artworks were visually inspected in the supplied frame/reference examples and confirmed by the exact-frame correlations after registration. This does not promise that every future alternative treatment uses the same art; unknown/wrong images still require confidence/ambiguity checks. A regression deliberately substitutes wrong reference artwork and verifies that it cannot authorize the wrong identity.

## After-fix slot evidence

All nine best/assigned identities equal the user's expected order. Every slot is accepted using 0.94/0.07, and the complete result passes the existing placement safety check. Detailed output also records the competing-slot alternatives and all nine candidates for every slot.

| Slot | Crop x/y/w/h (px) | Expected / best / assigned | Score | Second | Second score | Threshold | Result |
| --- | --- | --- | ---: | --- | ---: | ---: | --- |
| 1 | 26.29/42.78/245.08/340.36 | Hatching Plans | 0.982005 | Stonesplitter Bolt | 0.443291 | 0.94 | accepted |
| 2 | 287.33/38.51/246.74/351.76 | Stonesplitter Bolt | 0.979759 | Hatching Plans | 0.261211 | 0.94 | accepted |
| 3 | 548.28/41.30/246.51/343.86 | Rat Out | 0.995517 | Grabby Giant // That's Mine | 0.502122 | 0.94 | accepted |
| 4 | 809.99/41.51/245.92/342.36 | Grabby Giant // That's Mine | 0.990876 | Rat Out | 0.515987 | 0.94 | accepted |
| 5 | 1071.67/41.93/244.10/341.92 | Merry Bards | 0.983131 | Return from the Wilds | 0.394369 | 0.94 | accepted |
| 6 | 26.40/398.68/244.87/342.34 | Redcap Thief | 0.986205 | Scarecrow Guide | 0.545037 | 0.94 | accepted |
| 7 | 287.40/399.97/245.48/341.92 | Return from the Wilds | 0.985570 | Scarecrow Guide | 0.516559 | 0.94 | accepted |
| 8 | 549.24/400.82/245.03/338.58 | Gingerbrute | 0.992556 | Scarecrow Guide | 0.552706 | 0.94 | accepted |
| 9 | 810.38/399.95/244.71/343.49 | Scarecrow Guide | 0.993967 | Return from the Wilds | 0.552561 | 0.94 | accepted |

## Offline developer command

From the solution directory:

```powershell
dotnet build DraftTG.sln --no-restore
dotnet src/DraftTG.App/bin/Debug/net10.0/DraftTG.App.dll --localize-image tests/Fixtures/arena-artwork-live/arena-draft-capture.png --manifest tests/Fixtures/arena-artwork-live/manifest.json --output artifacts/artwork-matcher/after
```

Append `--coarse-only` and choose another output directory to reproduce the original 0/9 with identical inputs/thresholds. The command exits before Avalonia startup, performs no Arena lookup/capture or network requests, and writes scores.txt, scores.json and localization-latest.png to the explicit output directory. JSON contains the entire winning-proposal/candidate similarity matrix, card/artwork rectangles, exact reference metadata/dimensions, missing-reference IDs, best/second/assigned scores, competing-slot score, thresholds and exact rejection reasons. The expected-order labels exist only in diagnostics/assertions.

For a new capture, provide a manifest containing PackNumber, PickNumber, Columns and Cards in pack serialization order, each with Id, Name, Set, CollectorNumber and ReferenceFile. ReferenceUri/ArenaId/art provenance are useful for auditing. ExpectedVisualOrder is optional ground truth. Paths are local files relative to the manifest; no automatic online image resolution or printing guessing occurs.

## Tests, scope and validation

Six focused new tests:

1. Original PNG reproduces 0/9 with all nine correct assignments, exact before scores, and threshold-only rejections.
2. Registered PNG matches all nine correctly above 0.97 without reducing confidence/margin gates; the result satisfies placement safety.
3. The live PNG remains correctly recognized at 75% and 120% capture size with uniform dimming.
4. Missing exact-printing reference remains unresolved while the other eight identities are correct.
5. Wrong reference artwork cannot authorize the wrong identity or the ambiguous donor.
6. The offline command writes complete per-slot/reference/rectangle/threshold evidence and an annotated PNG without Arena/network.

Existing tests continue covering unrelated/flat imagery, ambiguous artwork, duplicate occurrences, cancellation, partial results, manual override, statistics identity, capture reliability and stale UI publication.

Build: zero warnings/errors. Tests: **658 passed, 0 failed, 0 skipped** (six new; previous 652 preserved). Project totals: Domain 42, ArenaIntegration 94, Application 135, Data 134, RecommendationEngine 102, App 151. Final TRX files are under artifacts/artwork-matcher/final-tests; test-summary.json records their counters.

Existing code changes are limited to CardTemplateRecognition.cs, Program.cs and the App test project's fixture-copy configuration. New files are OfflineArtworkCommand.cs, LiveArtworkMatcherTests.cs and the privacy-safe image/reference/manifest fixture with README. Documentation changes are this report, ARCHITECTURE.md and ROADMAP.md. SHA256 audit artifacts record the before state and changed-existing-file list. ScryfallVisualReferenceCache, Windows capture/coordinator/session/UI, Application/Domain, Phase 8/9A/9B models, statistics mapping, providers, Arena parser and global.json are byte-identical. No Git index repair or unrelated cleanup was performed.

The fixture is the user's exact region-only crop with no visible account/chat/desktop content. Only the nine corresponding local reference snapshots are included; no general screenshot or artwork corpus was gathered. Pixel normalization/search is local and the offline command uses no external service.

## Acceptance limit

This establishes **9/9 correct offline identification on the exact real Arena frame**. It also establishes that the supplied Phase 9B.5 capture succeeds for this observed non-foreground frame with equal integrity levels. It does not prove all future packs/printings/treatments/animations or the rebuilt overlay's live publication and physical badge alignment.

Live validation is still required: run the rebuilt desktop app on a new pack (where no manual mapping/edit is already preserved), check the automatic result and card/badge positions, then make consecutive picks. An existing manual map intentionally prevents an asynchronous result from replacing it. Compare new captures/confidence diagnostics if another rendering layout or artwork variant fails before broadening search or adding OCR. No Phase 9C work was started.
