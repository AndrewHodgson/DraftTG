# Contextual Pick Score — Phase 3: Multiface 17Lands Statistics and a Visible Estimated Marker

Date: 2026-10-08. Model version `contextual-pick-score-v1.2` → **`contextual-pick-score-v1.3`**.

**Scope:**
- Statistical identity for multiface cards.
- Pick Score evidence provenance.
- The EST badge marker.

**Unchanged:**
- the v1.2 formula, soft limits, weights, deck fit, ranking keys and tier boundaries;
- localization, Phase 9E, placement, WGC and artwork;
- the Arena sort comparator, deck-builder algorithms and Phase 10D;
- badge animation.

Nothing is committed.

Evidence is in `artifacts/pick-score-v1-claude-audit/phase3/` (git-ignored):

| File | Contents |
|---|---|
| `id-v12.txt` / `id-v13.txt` | Full FRA identity and score audit |
| `p3-orig-*.txt` / `p3-multi-*.txt` | Pack replays |
| `analyze_p3.py` | Comparator |
| `fra3-*.txt` | Saved-pool probe |
| `ab3-*.json` | Benchmark |
| `badge-preview-static.png`, `est-zoom.png` | Rendered badges |
| `fraprobe-Program.cs` | Probe source |

---

## 1. Root cause

| Layer | Behaviour | Status |
|---|---|---|
| **Arena identity** (`ArenaCardResolver`) | Scryfall has no `arena_id` for FRA. Arena's own database resolves the primary GrpId (link type 19, titled with the front face, e.g. 106315 "Hallway Heckler") by set + collector number. `NameAgrees` already accepts face names, so it reaches the Scryfall printing "Hallway Heckler // Vicious Verse". | **Correct; unchanged** |
| Scryfall decode | `layout: "prepare"` mapped to `CardLayout.Other`. Faces were kept, but the layout was lost. | Lossy, but not the cause |
| **17Lands join** (`LimitedStatisticsMapper.MapParticipants`) | `byName.TryGetValue(card.Name)`, i.e. a lookup for "Hallway Heckler // Vicious Verse". **17Lands names the row "Hallway Heckler".** No row is found. | **Root cause** |
| **Environment baseline** (`MapEnvironment`) | `catalog.FindByExactName("Hallway Heckler")` finds nothing, so those 21 rows were silently dropped from the format baseline. | **Same root cause** (second symptom) |
| Presentation guard | `CurrentPackCardPresentation` required the resolved row name to equal `card.Name`. | Would have rejected a correct fix |
| Scoring | No row means no Phase 8 adjusted GIH, so Q = .5, so a flat neutral 25 at P1P1, labelled estimated only in diagnostics. | Consequence |

**Identity diverges only at the statistics join.** The printing identity was always right.

## 2. Actual 17Lands representation

The cached FRA `/api/card_data` has 290 rows.

- Compared against all **118,587** Scryfall printings, exactly **21 rows match no canonical card name**. All 21 are front faces of FRA `prepare` cards.
- **No row uses the full "Front // Back" name, and none uses a back face.**
- 17Lands publishes one row per drafted card (the Arena primary object). There are no per-face rows.
- Back faces are **shared between different cards**: "Soul Tether" ×3, "Seed Suture" ×3, "Peer Review" ×3, "Omit Variables" ×3, "Vicious Verse" ×3. A back-face alias would therefore be unsafe.

**Representative records:**

| Card (Scryfall canonical) | Layout | Set # | Arena GrpId (primary / back) | DraftTG CardIdentifier | Old lookup key | 17Lands row | Old result | Why |
|---|---|---|---|---|---|---|---|---|
| Hallway Heckler // Vicious Verse | prepare | FRA 85 | 106315 / 106316 | 7e324816-552f-455d-97c4-5ea6b26d2e6e | full name | "Hallway Heckler" | missing | front-face row |
| Carnivorous Cultivator // Enroot | prepare | FRA 100 | 106333 / 106334 | 79dd5c54-5ea5-47b5-8f9b-50ed57a5ea45 | full name | "Carnivorous Cultivator" | missing | front-face row |
| Diviner of Victory // Unwind History | prepare | FRA 28 | 106253 / 106254 | 0853bb80-8664-432a-8457-600139fd96d5 | full name | "Diviner of Victory" | missing | front-face row |
| Pyre Rhymer // Molten Tide | prepare | FRA 91 | 106323 / 106324 | 2b0ebea0-86de-4da4-9fe8-dacc1e75c161 | full name | "Pyre Rhymer" | missing | front-face row |
| Konstrari Improviser // Soul Tether | prepare | FRA 139 | 106376 / 106377 | 42e28bd2-486b-45d4-8840-6e33c19c2d57 | full name | "Konstrari Improviser" | missing | front-face row; back face shared with 2 other cards |

Arena's primary title, the 17Lands row name and the Scryfall front face are identical for all 21 cards.

## 3. Multiface layouts

Two different populations matter here: the cards actually in FRA, and the layouts that exist in the full catalog.

**FRA itself** (462 printings, 285 canonical names) contains only two layouts:

| Layout | FRA printings | Canonical names |
|---|---|---|
| `normal` | 433 | 264 |
| `prepare` | 29 | 21 (some names have alternate printings) |

**The full Scryfall catalog** contains these multiface layouts, which determines how the resolver must treat them:

| Layout | Printings in catalog | Resolver treatment |
|---|---|---|
| adventure | 456 | Front-face alias allowed |
| transform | 1,059 | Front-face alias allowed |
| modal_dfc | 326 | Front-face alias allowed |
| prepare | 122 | Front-face alias allowed |
| split | 351 | Exact name only (halves are equal parts; Rooms use this layout) |
| flip | 45 | Exact name only (`Other`, fail closed) |
| art_series, double_faced_token, reversible_card | — | Not draftable; exact name only |

- `CardLayout.Prepare` was added, and the decoder now maps `"prepare"` to it. It was previously `Other`; no other behaviour depended on the distinction.
- **Adventure cards:** no non-FRA 17Lands data is cached locally, so their naming could not be verified offline. If 17Lands uses the canonical full name, rule A (exact match) handles them unchanged. If it uses the front face, rule B handles them under the same safety rules.

## 4. Resolver: statistical identity separate from printing identity

New `SeventeenLandsStatisticalIdentity` (Application). The Domain `CardIdentifier` remains the exact printing; nothing in Domain identity changed. A resolver is built per provider snapshot over an indexed dictionary.

**Precedence:**

**A. Exact canonical name.** If the snapshot has a row named exactly `card.Name`, it is used exactly as before. If that name has conflicting rows, the result is `ConflictingProviderRows`, and the resolver does **not** fall through to B.

**B. Structured front-face alias.** Only if A finds no row, and only when **all** of these hold:

1. The layout is Adventure, Transform, ModalDoubleFaced or Prepare. That means a structured Scryfall layout whose front face is the drafted object.
2. The card has at least 2 named faces, and `card.Name == string.Join(" // ", faceNames)` exactly.
3. The provider row named after `Faces[0].Name` is unique and non-conflicting.
4. **No** catalog card is canonically named the alias.
5. Every eligible multiface card in the catalog with that front face has **this card's canonical name** (same card, possibly reprinted).

**Never used:**
- back faces;
- split halves;
- case, punctuation or whitespace changes;
- `Contains` or `StartsWith` matching;
- Levenshtein or any other edit distance;
- picking the first row of several.

**Set and format scope:** the provider snapshot is already exactly the selected expansion and event format (unchanged service behaviour). The resolver never consults another snapshot.

**Reprints:** printings sharing the canonical name are the same card and share its row, consistent with existing single-faced reprint behaviour.

**Applied to all three paths:**
- participants (pack and pool, including deck construction and pair statistics);
- the environment baseline;
- the standalone catalog.

**Presentation guard:** the badge identity guard now accepts a row name only if `SeventeenLandsStatisticalIdentity.Denotes(card, name)`, i.e. it is the canonical name or the structural alias. Another card's name, or a back face, still rejects the whole occurrence.

**Diagnostics:** each occurrence shows its `17Lands identity`, which is one of:
- exact;
- front face;
- no row;
- conflicting;
- ambiguous.

## 5. Evidence provenance and the definition of Estimated

New `PickScoreQualityEvidence` on every `ContextualPickScore` (engine). The existing `PickScoreAvailability` is unchanged.

| Evidence | Condition (Phase 8 input actually used) | Q | Badge |
|---|---|---|---|
| `DirectCardStatistics` | The row has a valid GIH rate **and** a positive GIH sample, including a safely resolved multiface alias | measured | no EST |
| `NoProviderRow` | No row resolved (absent, conflicting or ambiguous) | neutral prior .5 | **EST** |
| `ProviderRowWithoutUsableGih` | The row exists but GIH is null or the sample is 0/null (ALSA may exist) | neutral prior .5 | **EST** |
| `NoEnvironmentBaseline` | No format baseline | — | dash, no EST |

**Partial-data rules:**

| Data present | Result |
|---|---|
| GIH but no ALSA | **Direct**. ALSA shows "—". |
| ALSA but no GIH | **Estimated**. ALSA is shown; GIH shows "—". |
| GIH with a zero sample | **Estimated** |

- **Real FRA partial rows:** 20 rows, mostly bonus-sheet cards (e.g. Consider, Splinter Twin, Loyal Tutor), have ALSA and a small game count but GIH withheld by 17Lands. They correctly remain Estimated.
- The neutral prior is never displayed as GIH or ALSA.
- The existing low-sample asterisk on a withheld GIH ("—*") is an established, tested presentation and is kept.

**What EST means.** It means card-level quality is estimated. **It does not mean a weak recommendation.** Lane, archetype and deck fit still use real draft context, and an estimated card can score high.

Expanded diagnostics now read, for example:

```
Pick Score: 28 / 50 (Estimated); Model: contextual-pick-score-v1.3; EstimatedMissingStatistics
Evidence:
Intrinsic quality: Estimated — no 17Lands row for this card in this set/format; neutral format prior used
Lane: candidate colours appear open
Archetype: neutral (no clear signal)
Deck need: Clears the projected deck cut line by 3.1 pp.
```

## 6. Badge

The score row is now `[score] EST`:

- `EST` is a 9-DIP SemiBold `TextBlock` in the ALSA line's neutral silver (`#FFB9C6D8`), beside the 15-DIP score in the same horizontal row.
- It is bound to `CardBadgeViewModel.IsPickScoreEstimate`, which is false while loading and when no score exists.
- The badge size (86×42) is unchanged; the name line and the animated background are unchanged.
- There is no warning colour, and no tooltip: badges stay visual-only, as an existing test enforces.

**The quality tier still comes only from the score.** An estimated 41 keeps the 40–44 animation tier. Neither rarity nor estimate status affects the tier.

The offline preview now includes two estimated badges (42 EST and 30 EST, "GIH —"). Rendered at 1×, both read clearly; see `badge-preview-static.png` and `est-zoom.png`.

## 7. Model version

**Bumped to `contextual-pick-score-v1.3`.** The formula is unchanged, but user-visible scores change for 21 FRA cards, and the format baseline changes (21 more rows). This follows the existing practice: v1.1 and v1.2 were bumped for user-visible score changes.

## 8. Full FRA audit (production catalog, real cache)

The audit loads the full 118,587-printing Scryfall file through the production loader, uses the real FRA Premier cache, and runs the production mapper and scorer. P1P1 scores are computed with a neutral lane.

| | v1.2 | v1.3 |
|---|---|---|
| FRA canonical names | 285 | 285 |
| — of which multiface | 21 (**7.4%**, confirming the audit's ~7%) | 21 |
| 17Lands rows | 290 (FRA + bonus sheet) | 290 |
| Rows attached to a card | 269 | **290** |
| Single-faced rows attached | 269 | 269 (**attached row unchanged for all 269**) |
| Multiface rows attached | **0 / 21** | **21 / 21** |
| Ambiguous / conflicting | — | **0 / 0** |
| Truly missing rows | 0 | 0 |
| Estimated scores | **41** (21 multiface + 20 GIH-less rows) | **20** (GIH-less rows only) |
| Environment rows / baseline | 269 / 56.4675% | 290 / **56.3652%** |

The baseline drop (−0.10 pp) is the environment now including the 21 multiface rows, which have 283,925 games and a below-average mean. As a result:

- single-faced P1P1 scores: **206 unchanged, 63 +1**;
- no single-faced card's attached row changed.

**Multiface regression table** (P1P1, neutral context):

| Card | Layout | DraftTG canonical name | 17Lands name | Old result | New result | GIH (games) | ALSA | Evidence (old → new) | Score |
|---|---|---|---|---|---|---|---|---|---|
| Carnivorous Cultivator | prepare | Carnivorous Cultivator // Enroot | Carnivorous Cultivator | no row | front face | 60.5% (6,027) | 2.25 | Estimated → Direct | 25 → **37** |
| Diviner of Victory | prepare | Diviner of Victory // Unwind History | Diviner of Victory | no row | front face | 60.5% (6,564) | 2.54 | Estimated → Direct | 25 → **37** |
| Variable Chaser | prepare | Variable Chaser // Arc of Fortune | Variable Chaser | no row | front face | 57.3% (5,468) | 2.73 | Estimated → Direct | 25 → 28 |
| Vigorbloom Vanguard | prepare | Vigorbloom Vanguard // Seed Suture | Vigorbloom Vanguard | no row | front face | 56.7% (13,682) | 3.36 | Estimated → Direct | 25 → 26 |
| Konstrari Improviser | prepare | Konstrari Improviser // Soul Tether | Konstrari Improviser | no row | front face | 54.1% (28,434) | 4.33 | Estimated → Direct | 25 → 18 |
| Bloodline Recollector | prepare | Bloodline Recollector // Ancestral Craving | Bloodline Recollector | no row | front face | 51.9% (1,566) | 2.84 | Estimated → Direct | 25 → 15 |
| Hallway Heckler | prepare | Hallway Heckler // Vicious Verse | Hallway Heckler | no row | front face | 52.6% (11,752) | 6.85 | Estimated → Direct | 25 → 14 |
| Woodwork Prodigy | prepare | Woodwork Prodigy // Soul Tether | Woodwork Prodigy | no row | front face | 52.7% (12,319) | 3.54 | Estimated → Direct | 25 → 14 |
| Pyre Rhymer | prepare | Pyre Rhymer // Molten Tide | Pyre Rhymer | no row | front face | 50.4% (3,145) | 3.16 | Estimated → Direct | 25 → 9 |
| Whiplash Wordsmith | prepare | Whiplash Wordsmith // Vicious Verse | Whiplash Wordsmith | no row | front face | 50.1% (7,956) | 6.72 | Estimated → Direct | 25 → 7 |
| *Control:* Consider (bonus sheet) | normal | Consider | Consider | exact (GIH null) | exact (GIH null) | — (476) | 5.18 | Estimated → Estimated (EST) | 25 → 25 |
| *Control:* Last Gasp | normal | Last Gasp | Last Gasp | exact | exact | 57.3% | — | Direct → Direct | unchanged / +1 |

All 21 cards are in `id-v13.txt`. The flat estimate distorted Q materially:
- Q was **too high** for 12 cards (true GIH 50.1–55.8%; real scores 2–18 points lower);
- Q was **too low** for 6 cards (56.5–60.5%; 1–12 points higher);
- 3 were genuinely neutral (Blossom-Blessed Angel, Fatehold Chronologist, Void Extrapolator: still 25).

## 9. Pack replay

The same deterministic generator and seeds as Phase 2 were used. The v1.2 arm reproduces Phase 2's 5,835-line output byte-for-byte.

### Phase 2's original sampler

It matched canonical names only, and therefore **silently excluded every multiface card**. Across 500 packs, the only effect is the baseline correction:

| Stage | Scores +1 | Score tiers v1.2 → v1.3 | Top-pick changes |
|---|---|---|---|
| P1P1 | 780 of 2,800 | 1018/1203/288 → 1013/1169/327 for 10–19 / 20–29 / 30–39 | 0 |
| P1P5 | 254 | | 1 (two cards tied at 32) |
| P2P5 | 237 | | 0 |
| P3P5 | 208 | | 1 (two cards tied at 15) |

The scores shown are unchanged in the two top-pick swaps; the internal order of the tied cards moved by the 0.1 pp baseline.

### Corrected sampler, including multiface cards at their real frequency

| Stage | Cards (multiface) | EST cards v1.2 → v1.3 | EST top picks | Top-pick changes | Notes |
|---|---|---|---|---|---|
| P1P1 | 2,800 (257) | 257 → **0** | 2 → 0 | 6 | All six are Carnivorous Cultivator or Diviner of Victory (real 60.5%) newly taking the pick at 37 over 26–36 cards |
| P1P5 | 1,000 (92) | 92 → **0** | 1 → 0 | 4 | Same two cards, plus Variable Chaser |
| P2P5 | 1,000 (104) | 104 → **0** | 6 → 0 | 9 | Estimated on-colour multiface tops (27 EST) replaced; e.g. Konstrari Improviser (real 54.1%) 27 → 21 |
| P3P5 | 1,000 (98) | 98 → **0** | **17 → 0** | 12 | v1.2 recommended mediocre multiface cards at "29 EST" (Konstrari Improviser, Hallway Heckler, Pompous Battlemage, even Pyre Rhymer at a real 50.4% → 10) |

**Score deltas** in the multiface sampler range from −19 to +12, and occur **only on multiface cards**. Every other card moves 0 or +1.

**Tier counts at P1P1:** 221/914/1374/248/30/13 → 247/970/1253/287/30/13. The flat-25 silver bulge redistributes to the cards' real tiers. The top tiers are unchanged.

Real draft chronology (per-pick FRA history) is still unavailable; the packs are sampled from real ratings and rarity slots.

**Saved FRA pool probe:**
- Codex's what-if and the realistic late packs change by ±1 point, with ranks unchanged except one swap between cards tied at 27.
- The pool's three Hallway Hecklers now project at 52.6% instead of the 56.5% prior.
- Codex's six examples are unchanged except Overwrite the Multiverse 20 → 21.

## 10. Tests

**30 new test cases in 20 methods.** Each states the expected behaviour first.

- **`SeventeenLandsStatisticalIdentityTests`** (Application; 13 methods, 21 cases):
  - real FRA fixture: three `prepare` cards resolve by front face, and every single-faced card keeps its exact row;
  - layout theory: prepare, adventure, transform and modal_dfc resolve; split and flip do not;
  - an exact full-name row beats a front-face row;
  - back-face rows are never aliases (shared "Soul Tether");
  - a shared front face across different cards is ambiguous and fails closed;
  - a front face equal to another card's canonical name stays with that card;
  - conflicting alias rows fail closed;
  - same-card reprints share the row, while a different card in another set does not;
  - the environment baseline includes alias rows exactly once;
  - the presentation guard accepts the structural alias and rejects other names and back faces;
  - partial-data theory: GIH with or without ALSA, null GIH, zero sample;
  - a missing row is estimated, with per-component diagnostics;
  - resolved multiface stats are direct, with no EST and the real GIH shown.
- **`ContextualPickScoreEvidenceTests`** (engine; 4 methods):
  - the four evidence states;
  - estimated Q still uses real deck fit and lane context and is not pinned to 25;
  - printed rarity changes neither evidence nor score;
  - estimated scores stay in 0–50 at every stage.
- **`EstimatedBadgeMarkerTests`** (App; 3 methods, 5 cases):
  - the shipped XAML puts EST in the score row, bound to the flag, smaller than the score and in the neutral ALSA colour;
  - the estimate flag and tier: same tier, EST only when estimated, "GIH —";
  - no EST without a score.

**Existing tests touched:**

| Test | Change | Why |
|---|---|---|
| `MysticRatingBackgroundTests.ShippedEffectIsBehindTheOriginalTextInsideTheOriginalBorder` | Expected text list gains "EST" in position | Intended UI addition; all other badge assertions (42-DIP height, border, background order) unchanged |
| Phase 2's `TopPickDesignation…` | Expects `v1.3` | Version bump |
| `OverlayHandleTests` | None | It **caught** a tooltip I had added to EST, which would violate visual-only badges; the tooltip was removed |

**Results:**

- `dotnet build DraftTG.sln --no-incremental`: **0 warnings, 0 errors**.
- `dotnet test DraftTG.sln --no-build`: **1,114 passed**, 0 failed. Domain 56, Data 160, Application 271, ArenaIntegration 127, RecommendationEngine 219, App 281.
- **Determinism:** the identity audit and pack replay re-runs are byte-identical.

## 11. Performance

**Mapping cost:**
- The front-face index is built **once per immutable catalog** (`ConditionalWeakTable`): **23.8 ms** over 118,587 printings, against a ~2 s catalog load.
- Afterwards, environment mapping takes 0.3 ms and participant mapping 0.3–0.5 ms for 290 cards (v1.2: 0.3 ms).
- Nothing runs per animation frame, and no network calls were added.

**Interleaved A/B with the tool binary** (4 rounds of 20 samples each):

| Workload | v1.2 medians | v1.3 medians |
|---|---|---|
| FRA pool, 14 candidates | 74.7 / 76.6 / 76.0 / 78.1 ms | 78.8 / 75.0 / 76.7 / 76.5 ms |
| Stress | 218–235 ms | 223–233 ms |

There is no measurable change, and there are 0 network requests.

## 12. Remaining limitations

1. **Adventure, transform and modal DFC naming is unverified offline.** Only FRA 17Lands data is cached. The resolver's exact-first rule plus the structural alias handles either convention, but has not been checked against real non-FRA rows.
2. **Split cards and Rooms resolve only by exact full name** (fail closed). If 17Lands ever publishes a split card under one half, it would show EST.
3. **20 FRA cards remain Estimated** because 17Lands withholds their GIH. This is correct behaviour.
4. **The baseline shift** (−0.10 pp in FRA) moves many cards by +1. This is a correction, but it does change scores for cards that themselves did not change.
5. **Badge EST size.** The marker uses the same 9-DIP size as the ALSA line. It was verified rendered at 1×, but not on a high-DPI Arena session or on macOS.

Phase 3 is complete. Phase 4 has not been started.
