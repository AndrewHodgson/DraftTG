# Contextual Pick Score V1 — Frozen Specification

**Status: FROZEN** (Phase 7, 2026-10-08). This is the authoritative V1 reference.

| Item | Value |
|---|---|
| Model version | `contextual-pick-score-v1.6` |
| Tier scheme | `pick-score-tiers-v2` |
| Supply profiles | `FRA` (calibrated); `provisional-default` (FRA-derived values for every other set, labelled as provisional) |

Any change that alters a score requires:
1. a model version bump;
2. a regression comparison against the golden fixtures (§12) and the deterministic replays (`fraprobe dist` and `fraprobe selfdraft`).

## 1. Scope and inputs

The score is a 0–50 contextual pick recommendation for each card occurrence in the current Arena draft pack. It is offline and deterministic; no network calls are made in the scoring path.

**Inputs**
- **The draft snapshot:**
  - current pack position and cards;
  - pick history and pool;
  - pool completeness. A partial history uses the coordinate as the stage and does not invent chronology.
- **17Lands card ratings** for the selected set and format. Card rows are resolved through the Phase 3 statistical identity (§9), and the environment baseline is the games-weighted mean GIH.
- **Scryfall gameplay metadata:** mana cost, colours, types, mana value, layout and faces, Oracle text, produced mana.
- **Optional extras:**
  - 17Lands colour-pair statistics (Phase 9C);
  - pack observations (Phase 9B lane evidence);
  - role overrides.

Printed rarity is never an input.

## 2. Components

### Q — intrinsic quality

- **Phase 8 shrinkage:** `adj = (g·n + baseline·500) / (n + 500)`.
- **Q:** `SoftLimit(.5 + (adj − baseline)/.16, .35)`.
- **No usable GIH** (no row, or no rate or games) with a baseline present: Q = .5, the estimated case (EST).
- **No baseline:** the score is unavailable and shown as "—".

### L — lane (Phase 9B)

`L = clamp(.5 + .5·laneAdjustment / maxHumanLaneAdjustment(.020), 0, 1)`. The lane evidence comes from cards arriving later than their ALSA:
- late-arrival scale 3;
- signals from pick 4, full at pick 8;
- pack recency weights 1 / .5 / .25;
- evidence scale 2;
- full confidence after 4 observations.

L is neutral (.5) without observations.

### A — archetype (Phase 9C)

`A = clamp(.5 + .5·normalizedAffinity·activeConfidence, 0, 1)`.
- **Affinity:** the pair-shrunk GIH (300-game prior) lift over the overall rate, relative to the baselines, divided by .04 and clamped to [−1, 1].
- **Activation:** an archetype is active only if its confidence is ≥ .35 and its lead is ≥ .10. This is a threshold, worth at most about 3 points at activation (Phase 7 §6).
- **Without pair statistics** (FRA has none cached), A = .5.

### D — deck need (Phases 1, 4, 6, 7)

D is computed only when the deck-need weight is above 0 (after 4 completed picks). It uses the unchanged Phase 10 optimizer with no extra optimizer semantics.

**Projections**
- **Pairs:** colour pairs ranked by Phase 10C, with `target = min(23, max eligible nonlands)`.
- **Viable pairs:** pairs with eligible spells ≥ target are fully viable.
- **Near-viable pairs:** pairs with eligible spells ≥ target − 2 are projected at their own size (Phase 7).
- **Taper:** a pair `s` spells short keeps `1 − s/3` of its blend weight, and the remainder returns to the best fully viable pair.
- **Before and after:** every pair is projected before and after adding the candidate. Caches are per pack.

**Cut line, per build**
- **Real cut:** the best swappable bench card if the candidate is included, or the weakest swappable maindeck card if excluded. Swaps respect the creature and early-play floors and the top-end cap.
- **Expected future cut X** (an order statistic):
  - supply `S = remaining picks × .8`;
  - contested slots `m` = open slots + maindeck spells below baseline − 3 pp;
  - if S ≥ m: `X = FutureFillerLevel(S/m)`, i.e. baseline −3 / −1 / +0.8 pp at coverage 1 / 3 / ≥ 10, linear in between;
  - otherwise X = the (m − S)-th best contested card, where open slots count as baseline − 10 pp, interpolated and capped at baseline − 3 pp.
- **Cut and margin:** `cut = forced ? best bench card : max(realCut, X)`, and `margin = selection value − cut`.
- **A pair the candidate newly makes viable:** `margin = (Q̄_new − Q̄_current)·spells`, and its fit is never below the fit in pairs that already existed.

**Fit and need**
- **Fit:**
  - `F = .10 + .90·(½σ(margin/s) + ½σ(margin/(margin > 0 ? .04 : s)))`;
  - `s = .01 + .01·(1 − progress)`.
- **Structure:**
  - `S = max(creatureNeed, earlyNeed, removalNeed) − topEndRedundancy`, where:
    - `k = 23 / projected spells`;
    - creatures: `clamp((16 − creatures·k)/4)`;
    - early plays (MV ≤ 2): `clamp((4 − early·k)/4)`;
    - removal: `weight·clamp((3 − removal·k)/3)`, with weight 1 for hard and .5 for conditional removal;
    - redundancy (MV ≥ 5): `clamp((high·k − 5)/3)`;
  - `c = clamp(spells/18)`;
  - `gate = forced ? 1 : σ(margin/.02)`.
- **Per-pair value:** `D_pair = clamp(F + .30·c·gate·S, 0, 1)`; off-pair cards get the floor .10.
- **Blend:** `D = Σ w_i·D_i`, with `w = softmax(Q̄_i / .0025)`, tapered.
- **Lands:** `.55 + .35·min(1, sourceShortfallReduction)` when selected and reducing a measured shortfall, otherwise .10.

### Roles (Phase 4)

- **Structured:** Creature, EarlyPlay, Land, DirectManaSource; Fixing (lands producing at least 2 colours).
- **Precision-first Oracle rules on castable faces:** HardRemoval and ConditionalRemoval.
- **Not used:** ramp and other roles are deferred.
- **Overrides:** data-driven, by identifier or `SET#collector`.

## 3. Playability relevance (Phase 6, Phase 7 taper)

- **Playability:** `P = Σ π_i·e_i` over projected pairs after the pick.
  - `π` = softmax(average spell quality / .01), with the near-viable taper.
  - `e_i = 1` if the card's colours fit the pair.
  - `e_i = .6·clamp(drafted lands producing the colour / 3)` for exactly one outside colour with one pip of it.
  - `e_i = 0` otherwise.
  - P = 1 for lands, or when no projection exists.
- **Relevance:** `R = 1 − smoothstep((picks − 10)/(35 − 10))·(1 − P)·(1 − .20)`.
- **Effective quality:** `Q_eff = Q > .5 ? .5 + R·(Q − .5) : Q`. Raw Q is retained for display and tie-breaks.

## 4. Composition, calibration, rounding and ranking

**Weights.** Q/L/A/D weights follow a smoothstep between completed picks 4, 21 and 35:

| At pick | Q | L | A | D |
|---|---|---|---|---|
| 4 | .70 | .20 | .10 | 0 |
| 21 | .50 | .20 | .15 | .15 |
| 35 | .30 | .10 | .20 | .40 |

Progress = max(completed picks, coordinate) / 42.

**Value and score**
- `w = wQ·Q_eff + wL·L + wA·A + wD·D`.
- **Final calibration (frozen since v1.2):** `V = SoftLimit(.5 + 1.4·(w − .5), .45)`.
  - `SoftLimit(y, k)` is the identity within k of .5;
  - beyond that it is `.5 ± (k + h(1 − e^{−(|y−.5|−k)/h}))` with `h = .5 − k`.
- **Exact score:** `50·V`.
- **Displayed score:** `round(50·V)`, half away from zero. It is monotone, so a higher-ranked card never shows a lower integer.

**Ranking:** exact contextual value descending, then raw Q descending, then Phase 8 data weight descending, then pack index ascending. Sample size only separates exact ties. The top-ranked card is the context pick.

## 5. Tiers (`pick-score-tiers-v2`, `PickScoreTierThresholds`)

| Score | Tier | Badge palette |
|---|---|---|
| 0–14 | VeryWeak | Graphite |
| 15–24 | Marginal | Steel |
| 25–34 | Solid | Silver |
| 35–39 | Strong | Gold |
| 40–44 | Excellent | Rich gold |
| 45–50 | Premium | Premium |
| null or outside 0–50 | Unavailable | — |

The badge palette, animation, diagnostics and preview all consume this one classification.

## 6. Score semantics (anchors)

| Score | Meaning |
|---|---|
| 45–50 | exceptional |
| 40–44 | excellent |
| 35–39 | strong |
| 30–34 | clearly desirable |
| 25–29 | solid |
| 20–24 | marginal |
| 15–19 | weak in context |
| 10–14 | very unlikely to improve the deck |
| 0–9 | irrelevant |

The anchors A–J are in `ContextualPickScoreCalibration.Anchors`; Phase 7 §3 lists A–P with their values.

## 7. EST semantics

EST appears only when Q uses the neutral prior. GIH shows "—" (never a fake rate), while ALSA is shown if present. L, A and D are computed normally.

There is no EST penalty: a card at exactly the prior and an estimated card give identical exact scores. The tier derives only from the score.

## 8. Explanation semantics

Reasons are generated from the same values the score uses:

| Reason | Condition |
|---|---|
| Q label | ≥ .85 excellent, ≥ .65 strong, < .40 below baseline, otherwise near-average |
| Lane "open" / "less support" | L > .55 / L < .45 |
| Archetype | A > .55 / A < .45 |
| Quality-relevance reason | R < .9 and Q > .5 |
| Deck-fit lines | Margin and membership thresholds; open-slot reasons name the build when it is not the current best pair |
| Need lines | Only when `.30·c·gate·value ≥ .01` |

The evidence label is strong for D ≥ .65, weak for D ≤ .40, otherwise neutral. Expanded diagnostics show:
- the exact score and tier;
- raw, relevance and effective Q;
- the components and weights;
- the contributions;
- the real and virtual cut lines;
- supply, contested slots, urgency and the structure gate;
- the supply-profile provenance.

The badge shows only the score [EST], GIH and ALSA.

## 9. Statistical identity (Phase 3)

1. Exact canonical name first.
2. Otherwise a front-face alias, only for structured Adventure, Transform, ModalDoubleFaced or Prepare cards whose canonical name equals the face names joined by " // ".

Resolution fails closed on any ambiguity, never uses back faces and never matches fuzzily. Only FRA has been verified physically; for other sets the worst case is a transparent EST.

## 10. Supply profiles (Phase 7)

`FuturePickSupplyProfile` holds the share and filler offsets above.
- **FRA:** calibrated from FRA PremierDraft red-green availability.
- **Every other set:** `provisional-default`, with identical values, labelled "provisional: FRA-derived values, not calibrated for this set".

New sets should be calibrated with `fraprobe supply` before their profile is marked calibrated.

## 11. Supported use and known limitations

**Supported:** 17Lands card ratings for the active set in Premier, Traditional or Quick Draft (`LimitedStatisticsFormat`; Quick Draft uses the bot lane scaling of Phase 9B). Validated physically on FRA Premier Draft only.

**Known, non-blocking limitations**
- **Splashes:** no splash-deck construction. Splash credit restores relevance only; D stays at the floor.
- **Fixing lands:** binary deck need.
- **Archetype activation:** a threshold step of at most about 3 points.
- **Supply calibration:** FRA only.
- **Multiface identity:** verified only on FRA.
- **Lane evidence:** depends on observed packs.
- **Deck targets:** generic, with no successful-deck structural profiles.

## 12. Golden V1 fixtures (`ContextualPickScoreV1FreezeTests`)

Each fixture is checked to ±0.01 points on the exact score and ±0.001 on components, and must be identical when the card is printed as Mythic instead of Common.

| Fixture | Exact | Shown | Tier | Q | R | D |
|---|---|---|---|---|---|---|
| p1p1-bomb | 47.369 | 47 | Premium | .9565 | 1 | .5 |
| p1p1-average | 25.000 | 25 | Solid | .5 | 1 | .5 |
| early-off-colour-bomb (P1P3) | 47.369 | 47 | Premium | .9565 | 1 | .4929 |
| late-off-colour-bomb (P3P5) | 16.320 | 16 | Marginal | .9565 | .2145 | .1 |
| needed-two-drop | 35.757 | 36 | Strong | .5595 | 1 | .85 |
| redundant-top-end | 23.314 | 23 | Marginal | .5 | 1 | .4376 |
| removal-need | 30.406 | 30 | Solid | .5 | 1 | .7 |
| real-cut-upgrade | 37.880 | 38 | Strong | .6786 | 1 | .8261 |
| estimated-card | 26.352 | 26 | Solid | .5 | 1 | .55 |
| active-archetype-card | 31.199 | 31 | Solid | .5 | 1 | .6582 |
| viable-alternate-build | 45.970 | 46 | Premium | .9708 | .9678 | .9669 |
| open-slot-filler | 24.199 | 24 | Marginal | .5 | 1 | .465 |

## 13. Version history

| Version | Change |
|---|---|
| v1.0 | V1 |
| v1.1 | Graded deck fit |
| v1.2 | Soft limits |
| v1.3 | Multiface identity and EST |
| v1.4 | Deck need and roles |
| v1.4 / tiers v2 | Tier alignment, no score change |
| v1.5 | Playability relevance and expected-future cut line |
| **v1.6** | **Near-viable pair taper and per-build cut line (frozen V1)** |
