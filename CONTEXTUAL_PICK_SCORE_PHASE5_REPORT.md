# Contextual Pick Score — Phase 5: 0–50 Calibration, Tier Validation and Contextual Sanity Audit

**Outcome in one line:** I measured the v1.4 score and found the final calibration already matches the score semantics, so I kept it unchanged. What I changed were the badge tier boundaries, now aligned with those semantics (tier scheme `pick-score-tiers-v2`). The constants now live in one place, and the badge consumes the canonical tier. Displayed numbers and rankings are bit-identical to v1.4, and the model version stays `contextual-pick-score-v1.4`.

Two v1.4 behaviours are documented as component-level gaps rather than calibration problems (§9):
- **Anchor I:** an off-colour bomb late scores about 24, where the target is 10–20.
- **Late "strong" tier:** at P3P5 it is mostly reached by below-average cards that fill open slots or upgrade a weak deck.

Neither can be fixed by any monotone global calibration.

## 1. Current pipeline (unchanged)

The pipeline runs in this order: components → weights → weighted value → calibration → exact score → displayed integer → visual tier.

**Components**
- **Q (quality):** `SoftLimit(.5 + (adjustedGIH − baseline)/.16, knee .35)`, using the Phase 8 500-game shrinkage.
- **L (lane):** `clamp(.5 + .5·laneAdjustment/humanMaximum)`.
- **A (archetype):** `clamp(.5 + .5·pairAffinity·activeConfidence)`.
- **D (deck need):** `clamp(F + .30·c·played·S, 0, 1)` from phase 4, neutral at about .55 at the cut line.

**Weights.** They are interpolated by smoothstep between completed picks 4, 21 and 35:

| Stage | Q | L | A | D |
|---|---|---|---|---|
| Early (pick 4) | .70 | .20 | .10 | 0 |
| Mid (pick 21) | .50 | .20 | .15 | .15 |
| Late (pick 35) | .30 | .10 | .20 | .40 |

**Score chain**
- Weighted value: `w = Σ wᵢ·cᵢ`.
- Calibration: `V = SoftLimit(.5 + 1.4·(w − .5), knee .45)`, the one global map.
- Exact score: `50·V`. The unrounded value is the ranking authority.
- Displayed score: `round(50·V)`, half away from zero. It is monotone, so it can tie two cards but never reverse them.
- Tier: `PickScoreTierThresholds.Classify(display)`, which `MysticBadgeTierPalette` maps to a palette.

## 2. Semantic anchors (now in `ContextualPickScoreCalibration.Anchors`)

Scores come from the offline scorelab (synthetic RG deck, environment baseline 56%, 10k games unless stated).

| Key | Situation | Target | v1.4 / Phase 5 | Status |
|---|---|---|---|---|
| A | Average card, P1P1 | 20–26 | 25 | ✓ |
| B | +3 pp, P1P1 | 30–36 | 34 | ✓ |
| C | +10 pp bomb (3k games), P1P1 | 43–49 | 48 (+13 pp: 49) | ✓ |
| D | Exceptional bomb with positive context | 45–50 | 49–50 (lane can lift it to 50) | ✓ |
| E | +1 pp on-colour, makes deck, P3P5 | 28–35 | 31 | ✓ |
| F | +5 pp on-colour upgrade, P3P5 | 35–43 | 42 (+3 pp: about 39) | ✓ |
| G | Average 2-drop filling a missing early play, P3P5 | above neutral, below premium | 32 | ✓ |
| H | 65% off-colour, P1P3 | competitive | 47 | ✓ |
| I | 65% off-colour, P3P5 | 10–20 | **24** (68%: 25; 60%: 19) | **gap** |
| J | −3 pp, does not make deck, P3P5 | below cards that do | 12 | ✓ |

Further anchors:
- A useless 45% off-colour card late scores **4**.
- An on-colour 68% bomb late scores **48**.

The low tail keeps its ordering: useless 4 < bench 12 < off-colour 60% 19 < off-colour bomb 24.

## 3. Distribution audit (9 stages × 1,000 packs, real FRA ratings)

**Harness:** `fraprobe dist`.
- Each pack has 1 rare (mythic 1 in 8), 3 uncommons and 10 commons, drawn from real FRA cards with GIH (multiface cards use their front-face rows).
- To simulate other drafters, the `pick−1` cards with the lowest ALSA are removed first, with ±2 seeded noise.
- Pools are deterministic spreads of the real 42-card FRA pool.
- Seeds are `100000·pack + 1000·pick + n`. The run made 0 network requests.

**Harness limitation.** The pools carry no pick history and the cached data has no pair statistics, so L and A stay about neutral (|L| ≤ 1.1 points, A = 0). Their magnitudes are analysed separately (§6).

**Score distribution by stage, v1.4 = Phase 5** (identical for all 87,000 cards)

| Stage | Mean | Med | p10 | p25 | p75 | p90 | p95 | p99 | Min | Max | 0–9 | 10–19 | 20–29 | 30–39 | 40–44 | 45–50 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| P1P1 | 21.0 | 21 | 11 | 16 | 26 | 31 | 35 | 43 | 2 | 49 | 9.2% | 34.2% | 44.2% | 10.7% | 1.2% | 0.5% |
| P1P5 | 19.3 | 19 | 9 | 14 | 24 | 28 | 31 | 36 | 2 | 44 | 11.2% | 41.4% | 39.0% | 8.3% | 0.2% | 0.0% |
| P1P10 | 18.1 | 18 | 8 | 14 | 23 | 27 | 29 | 31 | 3 | 40 | 15.0% | 44.5% | 36.6% | 3.9% | 0.0% | 0.0% |
| P2P1 | 21.2 | 22 | 12 | 17 | 26 | 29 | 31 | 39 | 3 | 48 | 5.6% | 32.2% | 53.5% | 7.7% | 0.8% | 0.1% |
| P2P5 | 18.8 | 19 | 10 | 15 | 23 | 27 | 29 | 31 | 4 | 44 | 9.7% | 40.5% | 46.0% | 3.7% | 0.1% | 0.0% |
| P2P10 | 18.2 | 18 | 9 | 14 | 23 | 28 | 30 | 31 | 4 | 32 | 14.2% | 42.4% | 37.1% | 6.3% | 0.0% | 0.0% |
| P3P1 | 19.3 | 18 | 10 | 14 | 24 | 32 | 33 | 40 | 4 | 48 | 7.9% | 51.2% | 27.9% | 11.8% | 0.6% | 0.5% |
| P3P5 | 19.2 | 15 | 9 | 11 | 30 | 36 | 37 | 38 | 4 | 47 | 13.1% | 53.3% | 7.0% | 26.4% | 0.1% | 0.1% |
| P3P10 | 16.4 | 13 | 8 | 10 | 22 | 32 | 36 | 37 | 4 | 38 | 22.5% | 49.4% | 14.5% | 13.6% | 0.0% | 0.0% |

**v1.3 comparison**
- **P1:** identical.
- **Late means:**
  - P3P1 16.7 → 19.3;
  - P3P5 15.5 → 19.2;
  - P3P10 12.9 → 16.4.
- **Late 30–39 share at P3P5:** 10.7% → 26.4%, the phase 4 shift.
- **Top pick unchanged from v1.3 to v1.4:**
  - P1: 98–100%;
  - P2: 91–97%;
  - P3: 88–93%.

**Late P3 is bimodal.** On-colour cards that make the deck average 33.2 at P3P5 (p10 27, p90 38). Off-colour cards average 12.4 (p90 18), and lands average 16.5.

## 4. Pack-level distribution (v1.4 = Phase 5)

| Stage | Top | 2nd | Pack median | Top − median | Cards 40+ | Cards 45+ | Cards <15 | Packs with a 45+ | Packs with ≥ 3 cards at 40+ | Packs with top ≤ 27 | Packs with top ≥ 40 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| P1P1 | 35.8 | 30.3 | 21.1 | 14.7 | 0.24 | 0.07 | 2.9 | 7% | 0% | 4.4% | 22.8% |
| P1P5 | 30.2 | 26.5 | 19.4 | 10.8 | 0.02 | 0.00 | 2.5 | 0% | 0% | 28.4% | 1.6% |
| P1P10 | 25.8 | 21.6 | 18.3 | 7.5 | 0 | 0 | 1.3 | 0% | 0% | 64.3% | 0.2% |
| P2P1 | 33.0 | 28.9 | 21.4 | 11.5 | 0.13 | 0.02 | 2.1 | 2% | 0% | 4.8% | 12.3% |
| P2P5 | 28.1 | 25.2 | 18.9 | 9.2 | 0.01 | 0 | 2.5 | 0% | 0% | 38.0% | 0.8% |
| P2P10 | 26.2 | 21.8 | 18.2 | 8.0 | 0 | 0 | 1.6 | 0% | 0% | 55.4% | 0% |
| P3P1 | 34.4 | 30.2 | 18.1 | 16.2 | 0.15 | 0.07 | 4.3 | 7% | 0% | 3.9% | 14.6% |
| P3P5 | 36.3 | 32.6 | 16.1 | 20.3 | 0.02 | 0.01 | 4.9 | 1% | 0% | 2.5% | 2.4% |
| P3P10 | 28.3 | 20.1 | 14.3 | 14.0 | 0 | 0 | 3.2 | 0% | 0% | 42.1% | 0% |

These figures answer the spec's questions:
- **How often does an ordinary pack contain a 45+?** In about 7% of fresh packs, almost always a mythic or rare bomb.
- **How often are several cards 40+?** Never three in one pack.
- **Is the best card often only about 25?** Yes, in mid-pack picks: weak packs read weak.
- **Are strong and weak packs distinguishable?** Yes. P1P1 tops range from 28 (p10) to 44 (p90).

50 never occurred. It needs an exceptional card plus strong positive context.

## 5. Calibration decision: option D (tier boundaries only)

**Measurements against the calibration options:**
- **The P1P1 GIH-to-score map matches the semantics.** With weights `.7Q + .3·neutral`, the score bands correspond to these GIH ranges relative to the 56.4% baseline:
  - 0–9: below about 51% (−5 pp);
  - 10–14: about 51.7–52.7%;
  - 15–19: about 52.7–53.9%;
  - 20–24: 54–55.4%;
  - 25–29: 55.6–57%;
  - 30–34: 57–58.3%;
  - 35–39: 58.4–59.5%;
  - 40–44: 60.5–63%;
  - 45+: above about 63.5% (+7 pp).
- **Anchors A–H and J are met** with the existing map.
- **The tails are compressed but strictly ordered.** At the top, +10 pp scores 48 and +13 pp scores 49. At the bottom, a useless card scores 4 and a bench card 12.
- **A center or slope change (option B)** would move every anchor that currently passes to fix none of them.
- **A soft-tail change (option C)** is unnecessary: 50 is already rare and 0 is never reached.
- **Option A alone (no change at all)** would leave the tier semantics misaligned.
- **The remaining issues are not calibration-level:**
  - anchor I sits at w ≈ .484, between an average P1P1 card (w = .5) and anchors that must stay lower;
  - late "strong" scores come from D, not from the map.
  - A monotone global map cannot separate either case.

**Kept calibration (exact, unchanged)**
- `V = SoftLimit(.5 + 1.4·(w − .5), .45)`.
- `SoftLimit(y, k)` is the identity for `|y − .5| ≤ k`. Otherwise it is `.5 ± (k + h·(1 − e^{−(|y−.5|−k)/h}))` with `h = .5 − k`.
- Exact score is `50·V`; displayed score is `round(50·V)`, half away from zero.

**Tier boundaries**

| Canonical tier (palette) | Old | New | Meaning |
|---|---|---|---|
| VeryWeak (Graphite) | 0–9 | **0–14** | irrelevant / very unlikely to improve the deck |
| Marginal (Steel) | 10–19 | **15–24** | weak / marginal |
| Solid (Silver) | 20–29 | **25–34** | solid playable / clearly desirable |
| Strong (Gold) | 30–39 | **35–39** | strong |
| Excellent (Rich gold) | 40–44 | 40–44 | excellent |
| Premium | 45–50 | 45–50 | exceptional |

Why the old bands were wrong:
- Gold started at 30, so "clearly desirable" cards looked "strong". That produced 10.7% gold at P1P1 and 26.4% at P3P5.
- Silver mixed marginal (20–24) with solid (25–29).
- An average P1P1 card (25) sat mid-silver, beside marginal cards.

**Tier share of all cards, old → new bands**

| Stage | Graphite | Steel | Silver | Gold | Rich gold | Premium |
|---|---|---|---|---|---|---|
| P1P1 | 9.2 → 20.9% | 34.2 → 45.0% | 44.2 → 28.5% | 10.7 → 3.9% | 1.2% | 0.5% |
| P2P5 | 9.7 → 24.8% | 40.5 → 55.2% | 46.0 → 19.8% | 3.7 → 0.1% | 0.1% | 0% |
| P3P5 | 13.1 → 48.5% | 53.3 → 21.1% | 7.0 → 14.7% | 26.4 → 15.5% | 0.1% | 0.1% |
| P3P10 | 22.5 → 64.5% | 49.4 → 14.4% | 14.5 → 13.9% | 13.6 → 7.2% | 0% | 0% |

The Rich gold and Premium bands are unchanged, so their shares are unchanged.

**Model version.** Displayed scores did not change, so the model version stays `contextual-pick-score-v1.4`. The tier scheme gets its own presentation version, `pick-score-tiers-v2`, which appears in the expanded diagnostics together with the exact unrounded score and the tier name.

## 6. Component magnitudes (points from neutral 25)

**Analytic maxima** (`70·weight·deviation`; D measured from the cut-line neutral .55)

| Stage | Q | L | A | D up / down |
|---|---|---|---|---|
| Early (≤ 4 picks) | ±24.5 | ±7.0 | ±3.5 | 0 |
| Mid (21) | ±17.5 | ±7.0 | ±5.3 | +4.7 / −4.7 |
| Late (≥ 35) | ±10.5 | ±3.5 | ±7.0 | +12.6 / −12.6 |

**Empirical D in the replay** (mean |points| / maximum + / maximum −):

| Stage | Mean | Max + | Max − |
|---|---|---|---|
| P1P10 | 0.9 | +1.1 | −0.9 |
| P2P5 | 3.3 | +4.8 | −3.9 |
| P2P10 | 3.9 | +5.3 | −4.6 |
| P3P1 | 6.9 | +9.6 | −7.7 |
| P3P5 | 10.5 | +13.0 | −10.4 |
| P3P10 | 9.5 | +12.1 | −11.2 |

**Answers:**
- An otherwise identical card can gain up to about +13 late from D, and lose up to about 13. It never gains +20.
- D exceeding Q late is the designed weighting (.40 vs .30), not an accident.
- L and A stay below Q at every stage.

**Matched pairs (identical GIH)**

| Pair | Score delta |
|---|---|
| Needed 2-drop vs redundant 4-drop (P3P5) | **+4** (30 vs 26) |
| Creature vs sorcery when short of creatures | **+3** (21 vs 18) |
| Removal vs ordinary spell, no removal in deck | **+4** (30 vs 26) |
| Same 56% card, in a 54% deck vs a 58% deck | 32 vs 18: **+14** |
| On- vs off-colour 60% card at P3P5 | **+21** (40 vs 19) |
| On- vs off-colour 60% card at P1P3 | **0** (37 vs 37) |

Deck need refines; it does not dominate. Colour is decisive late and irrelevant early.

## 7. The 2-drop judgment case

**Setup:** a deck with zero early plays and 24 cards at 57%, at P3P5 (33 picks). Weights there are wQ .311, wL .106, wA .197, wD .386.

| | 55% 2-drop | 58% 4-drop |
|---|---|---|
| Q (after Phase 8 shrinkage) | .440 | .619 |
| D | .555 | .661 |
| Cut-line margin | −1.9 pp (not selected) | +1.0 pp (selected) |
| Early-play need | 1.0 | — |
| Structure confidence | 1.0 | — |
| Score | **25** | **32** |

**Where the 6.8-point gap comes from:**
- Q accounts for **3.9 points**: (.619 − .440)·.311·70.
- D accounts for **2.9 points**. The unchanged deck optimizer does not play a 55% 2-drop in a 57% deck, so its early-play need is realised only at membership ≈ .13.

**Break-even sweeps** (4-drop fixed at 58% → 32):

| 2-drop GIH | 52% | 53% | 54% | 55% | 56% | 57% | 58% |
|---|---|---|---|---|---|---|---|
| Need 1.0 (zero early plays) | 18 | 19 | 22 | 25 | 30 | **36** | 40 |
| Need 0.5 (two early plays, 57% deck) | — | — | 18 | 21 | 26 | **32** (tie) | — |

- With zero early plays, the needed 2-drop wins once its deficit is under about 1.5 pp (break-even about 56.4%).
- With half need, the break-even is about 1 pp.
- At equal quality, the needed 2-drop wins 35 vs 28.

**Conclusion.** A 3 pp GIH gap is large in 17Lands terms; it is about the difference between a premium common and filler. Need compensates up to about 1–1.5 pp, scaled by how badly the slot is needed and whether the card would actually be played. This behaviour is consistent across the matched-pair and break-even scenarios and follows the projected deck, so it is not a systematic Deck Need defect. v1.4 behaviour is retained.

The one modelling question it raises belongs to the deck builder, which is out of scope here: whether the builder should relax its early-play floor when supply is that short.

## 8. Off-colour floor and cut-line neutrality

**Off-colour floor (D .10).** It keeps information. Late off-colour cards still order by quality: 45% → 4, 60% → 19, 65% → 24, 68% → 25. At P3P5, 67% of off-colour cards fall below 15. A hard zero would cost only about 2.7 points late (`.1·.39·70`) and would not bring anchor I into range, so the floor is kept.

**Cut-line neutrality**

| Pool | 55.5% | 56.0% (sidegrade) | 56.5% | Note |
|---|---|---|---|---|
| 15 picks (incomplete) | 27 | 28 | 29 | open slots, small wD .11 |
| 24 picks (incomplete) | 28 | 29 | 31 | wD .18 |
| 33 picks (complete) | 23 | **26** | 29 | margin 0 → D .55 |
| 41 picks (complete) | 23 | **26** | 29 | margin 0 → D .55 |

- On the real 42-card FRA pool, Essence Burn (+0.6 pp over the cut, below-average quality) scores 24.
- "Barely makes the projected deck" therefore reads solid at best, never strong.
- In incomplete pools, open-slot value is real but small, because D's weight is still low.

## 9. Known gaps (Phase 6 candidates, not changed here)

1. **Anchor I.** A late off-colour bomb scores 24–25 instead of 10–20.
   - **Cause:** late Q weight .30 contributes about +10 points, and the .10 floor only offsets it.
   - **Real drafts:** a negative lane signal would usually take another 1–3 points.
   - **Tie case:** it ties Essence Burn (24) on the real pool.
   - **Candidate fix:** a component-level castability gate on late Q. Calibration cannot do this.
2. **Late "strong" composition.**
   - **What the replay shows:** P3P5 cards scoring 35–39 have a mean Q of .467 and a mean D of .979. They are below-average cards that fill open slots, or that upgrade the weak replay pools.
   - **Scale:** with the new bands, 15.5% of P3P5 cards are Gold; 46% of on-colour cards that make the deck score 35 or more.
   - **Real complete pool:** at P3P13, only upgrades of about +2.8 pp or more reach 35.
   - **Why it is not a defect:** this follows the phase 1 open-slot replacement level and phase 4's upgrade logistic.
   - **Open question:** whether open-slot urgency or F's 4 pp upgrade scale should saturate later. This needs component work, not calibration.
3. **Score confidence (audit only).** A separate confidence indicator could later combine:
   - Phase 8 data weight;
   - projected-shell confidence c;
   - pair-weight dispersion.

   Today EST covers missing quality data. A high-confidence/low-confidence badge marker is not warranted until component gaps 1–2 are settled. Nothing was added to the badge.

## 10. Real FRA pool scenarios (v1.4 = Phase 5, `fraprobe poolreplay`)

The 42-card pool is a Ready RG deck whose cut line is weak (Chandra's Emberling, 52.3%).

| Candidate | Role | P2P8 (18 cards) | P3P5 (32 cards) | P3P13 (full pool) | New tier at P3P13 |
|---|---|---|---|---|---|
| Violent Echoes | strong on-colour upgrade (+7 pp over cut) | 37 | 41 | **42** | Excellent |
| Fblthp, Knows the Way | upgrade, raises creatures to 16 | 33 | 37 | 40 | Excellent |
| Craterclaw Colossus (MV7) | top end, no redundancy (3 at MV5+) | 29 | 34 | 38 | Strong |
| Marwyn, the Clearcutter | below-average 1-drop, +2.8 pp over weak cut | 27 | 32 | 35 | Strong |
| Sureshot Sower | average 2-drop (+1.9 pp) | 23 | 29 | 32 | Solid |
| Essence Burn | removal just above the cut (+0.6 pp) | 19 | 24 | 24 | Marginal |
| The Theorist, Jace Beleren | off-colour bomb | 38 | 26 | **24** | Marginal (anchor I gap) |
| Extended Absence | off-colour removal | 29 | 20 | 19 | Marginal |
| Rockfall Vale / Overgrown Farmland | unneeded duals | 27 / 14 | 13 / 10 | 12 / 10 | Very weak |

**Reading the table:**
- The early P2P8 lead of the off-colour bomb (38) fades by pack 3, as intended.
- Unneeded fixing is correctly near-irrelevant.
- Rockfall Vale at P2P8 earns 27 because it does reduce a measured shortfall there.

## 11. Estimated cards, sample size and determinism

- **Estimated quality.** Calibration and tier take only the contextual value. An estimated card at P1P1 scores exactly like a direct card at baseline, and relabelling a score as estimated changes neither its number nor its tier. There is no EST penalty.
- **Sample size.** Phase 8 shrinkage remains the only sample-size adjustment.
- **Final build vs v1.4.** The full 9 × 1,000 replay on the final build is **identical to v1.4 for all 87,000 cards**: exact value, integer and rank. Top-pick changes are 0.
- **Network.** 0 requests.

## 12. Implementation

**`ContextualPickScoreCalibration`** (new file, RecommendationEngine):
- `DefaultGain` 1.4 and `SoftKnee` .45;
- `Calibrate`, `ExactScore`, `DisplayScore` and `SoftLimit`;
- the `Anchors` table.

**`ContextualPickScoreConfiguration`** delegates to it: default gain, `CalibrationSoftKnee`, `Calibrate` and `SoftLimit`.

**`ContextualPickScore`**
- `Score0To50` uses `DisplayScore`.
- The new `Tier` property returns the canonical `PickScoreTier`.

**`PickScoreTier`** and **`PickScoreTierThresholds`**:
- `Version` is `pick-score-tiers-v2`;
- `Classify`, `Range` and `Describe`.

**`MysticBadgeTierPalette`**
- `Tier` maps the canonical tier to a palette, and `Label` derives its ranges from the thresholds.
- Animation machinery is unchanged.
- The preview now shows the band edges 14 / 24 / 34.

**Diagnostics.** The engine's contribution points use the central maximum score. Expanded diagnostics add `Exact score …; tier … (pick-score-tiers-v2)`.

Q, L, A, D, the role classifier, statistical identity, deck builder, localization, slots and the sort comparator are untouched.

## 13. Tests

There are 16 new test methods (31 cases) plus updated assertions.

**`ContextualPickScoreCalibrationTests`** (14 methods, 28 cases):
- monotonic and bounded over w ∈ [−.5, 1.5];
- tail ordering;
- the unchanged v1.4 map and configuration delegation;
- 15 tier-boundary cases;
- tier partition;
- P1P1 anchors A–C;
- late anchors E/F/J;
- anchor G;
- H/I, with the documented gap bound;
- average early vs late cut-line consistency;
- matched pairs;
- EST unaffected;
- rarity independence;
- deterministic replay with display never contradicting rank.

**App:**
- the canonical tier drives the palette for −1…51 and null, and labels match the ranges;
- Common at 47 is Premium and Mythic at 12 is Graphite.

**Updated:**
- the App tier-band and label assertions moved to the v2 bands;
- the Application late-diagnostics test asserts the tier line.

**Totals: 1,188 solution tests pass** (Domain 56, Data 160, Application 272, ArenaIntegration 127, RecommendationEngine 289, App 284), with 0 warnings and 0 errors.

## 14. Performance

Interleaved v1.4 vs Phase 5 tool binaries, 4 rounds of 20 samples each:

| Workload | v1.4 median | Phase 5 median | Phase 5 p95 | Optimizer calls |
|---|---|---|---|---|
| FRA pool, 14 candidates | 71.1–84.8 ms | 70.6–73.9 ms | 98–99 ms | 12 (unchanged) |
| All-pairs stress | 214–227 ms | 219–234 ms | 236–299 ms | 15 (unchanged) |

Differences are within run-to-run noise. Calibration and tier classification are a few arithmetic operations; no deck-builder evaluations were added.

## 15. Evidence

Evidence is in `artifacts/pick-score-v1-claude-audit/phase5/` (git-ignored):

| File | Contents |
|---|---|
| `dist-summary.json` | Per-stage and pack-level statistics for v1.4 and v1.3 |
| `analyze_dist.py`, `extra.py` | Analysis scripts |
| `fraprobe-Program.cs` | Harness, including the `dist` mode |
| `scorelab-p5.txt` | Anchors, pairs, sweeps, cut line |
| `ab5-*.json` | Benchmarks |

The 11 MB raw replay CSVs are kept only in the scratchpad.
