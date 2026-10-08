# Contextual Pick Score — Phase 6: Playability-Gated Quality and Continuous Deck Fit

**Outcome in one line:** model `contextual-pick-score-v1.5` fixes both component-level gaps Phase 5 found. The final calibration and tier scheme `pick-score-tiers-v2` are untouched.

- **Off-colour bombs:** an excellent off-colour card late now scores 16, where v1.4 gave 24 and the target is 10–20. It stays at 47 at P1P3.
- **Open slots:** an open slot is no longer free membership. Every candidate is now measured against the expected marginal card of the final deck, which is an order statistic over contested slots and later supply.

Scores and rankings at P1P1 and P1P5 are bit-identical to v1.4. Q, L, A, the statistical identity, roles, the deck optimizer, localization and the badge are unchanged.

## 1. The two Phase 5 gaps, reproduced (v1.4)

### Anchor I: excellent off-colour card at P3P5 (65% GIH, committed red-green pool)

| Component | Value | Weight | Points from 25 |
|---|---|---|---|
| Q | .957 | .311 | +9.9 |
| L / A | .50 / .50 | .106 / .197 | 0 |
| D | .100 (floor) | .386 | −10.8 |
| **Score** | | | **24** |

**Root cause.** Late in the draft Q still carries weight .31. A card no realistic build can play kept its full intrinsic upside (+10 points), and the D floor only cancelled it. Nothing asked whether that strength was actionable. A 68% bomb scored 25.

### Late Strong-tier inflation (P3, v1.4 replay, 35–39 cards)

| Stage | 35–39 cards | Intrinsically good (Q ≥ .6) | Open slot, Q < .6 | Real upgrade, Q < .6 | Of which weak (Q < .5) with ≥ 2 structure points |
|---|---|---|---|---|---|
| P3P1 (21 spells) | 233 | 37 | **196** | 0 | — |
| P3P5 (23 spells) | 1,547 | 0 | 0 | 1,547 | 860 |
| P3P10 | 359 | 0 | 0 | 359 | 0 |

**Root causes.** There were two:

1. **Open slots used a near-free cut line.** With no real alternative, a candidate was compared with `baseline − 4 pp`, dropping toward −10 pp as "urgency" rose. Urgency used a 0.6 playable share and full urgency below 1× coverage. So mid-draft shells were "urgent" at about 1.1× coverage, which is normal. At P3P1, a 57% card cleared that line by 4.8 pp and reached D .955.
2. **A weak full deck's weakest card was treated as permanent.** In the replay pools (spreads of the real, weak 42-card pool), the 23rd card is about 50%. A 55.8% card was a 5.7 pp "upgrade" even when 9 picks remained, and later picks would replace that 50% card anyway.

**Correction to the Phase 5 report.** Phase 5 attributed P3P5's Strong tier to open slots. The replay pools at P3P5 are actually full (23 spells), so the cause there is (2). Phase 5's "land" group also tested the DirectManaSource flag (8), not Land (4), so those figures described mana-producing cards.

## 2. Part A — playability-gated quality

Q (intrinsic quality) is unchanged and stored as `QualityComponent`. Composition uses an effective quality:

```
Q_eff = Q > .5 ? .5 + R·(Q − .5) : Q
R     = 1 − ramp(picks) · (1 − P) · (1 − .20)
ramp  = smoothstep((completedPicks − 10) / (35 − 10))
```

**Why only the upside is scaled.** An unplayable card's excellence is less actionable, but scaling symmetrically would make useless off-colour cards look better (45%: 4 → 12). Q_eff is continuous and non-decreasing in Q for every R.

**Playability P** is the plausibility-weighted share of realistic builds that can play the card:

```
P = Σ_i π_i · e_i
π_i = softmax(average spell quality of viable pair i / 1 pp), over viable pairs after this pick
```

- **Which pairs count:** π uses the existing projections, so a pair the candidate newly makes viable counts. No extra optimizer calls are made.
- **e_i = 1** when the card's colours fit pair i.
- **e_i = splash credit** of `.6 · clamp(drafted lands producing the colour / 3, 0, 1)`, when exactly one colour lies outside pair i and the card needs only one pip of it.
- **e_i = 0** otherwise.
- **P = 1** for lands, and when no projection exists (early draft, or deck need not weighted).

**Ramp.**
- R stays 1 through 10 completed picks, so pivoting is preserved.
- It is smooth, with no pack boundary, and reaches its full effect at the late anchor (35 picks).
- **Floor .20:** a card no build can play keeps 20% of its upside, as hate-draft and pivot value.

**No double penalty.** R reads only projected-build feasibility. It ignores lane openness (L, Phase 9B) and archetype lift (A, Phase 9C), and it only removes upside: the most it can remove late is 80% of +10.5 points (about 8.4). L can move a late card by at most ±3.5 points, A by at most ±7 (and only with pair data). The D floor values deck improvement and applies regardless of Q.

**Diagnostics** show:
- Q raw, R, playability and Q effective;
- an "Intrinsic quality: excellent · Direct …" line;
- a separate "Playability: unlikely to make your current RG deck — quality relevance 23%" line.

The engine reason says "Quality relevance 23%: unlikely to be playable in a realistic projected deck, so its strength counts less."

## 3. Part B — continuous deck fit against the expected final cut line

### Evidence: FRA supply (`fraprobe supply`, 2,000 packs per pick)

These figures describe the best red-green nonland card a drafter sees at each pick, with Phase 8 adjusted GIH (baseline 56.37%):

| Pick | Mean | p25 | p50 | p90 | Playable at ≥ baseline −4 pp |
|---|---|---|---|---|---|
| 1 | +0.9 pp | −0.1 | +0.6 | +3.8 | 99% |
| 5 | −0.6 | −1.4 | −0.1 | +0.8 | 97% |
| 9 | −1.5 | −2.8 | −1.4 | +0.8 | 88% |
| 13 | −2.8 | −3.9 | −2.8 | +0.6 | 54% |

### The virtual cut line X (expected marginal card of the final deck without the candidate)

**Inputs**
- **S (supply):** remaining picks × .8, the share of picks a committed drafter turns into on-colour playables.
- **Contested slots m:** open slots plus maindeck spells below baseline − 3 pp, i.e. cards a typical later pick beats.

**When S ≥ m:** `X = FutureFillerLevel(S / max(m, 1))`, a later pick taking the marginal slot. It is piecewise linear:

| Coverage | X |
|---|---|
| 1 | baseline − 3 pp (lower-quartile later card) |
| 3 | baseline − 1 pp (median later card) |
| ≥ 10 | baseline + 0.8 pp (90th percentile) |

**When S < m:** later picks take S contested slots, and X is the (m − S)-th best existing contested card.
- Unfillable open slots count as emergency filler at baseline − 10 pp.
- Fractional ranks interpolate, and X is capped at baseline − 3 pp.

X is continuous and non-decreasing in S, and it moves smoothly as m changes.

**Cut line and margin**

```
cut    = forced ? best bench card : max(real optimizer cut, X)
margin = selection value − cut
```

- The real cut is the bench alternative if the card is included, or the weakest swappable maindeck card if excluded. Phase 1 feasibility is respected.
- The "newly viable pair" margin from Phase 1 is unchanged.
- F (Phase 4) is unchanged in form. A true sidegrade at the real cut is still D .55; a weak card in a provisional slot is now measured against what later picks should supply.
- **Structure gate:** `D = clamp(F + .30·c·gate·S, 0, 1)` with `gate = forced ? 1 : logistic(margin / 2 pp)`. This replaces membership at 1 pp, so a far-below-cut needed card keeps a little need credit and a strong one keeps most of it.

**Urgency** is a diagnostic only: `clamp(1 − coverage / 3)`. It appears in diagnostics and in the reason "+ Deck still needs N playables with only M picks remaining" when S < 2 × open.

**Constants** (all on `ContextualPickScoreConfiguration`)

| Constant | Value |
|---|---|
| `ExpectedPlayableShare` | .8 (was .6) |
| `NormalFillerOffset` | .03 at coverage 1 (also the contested threshold) |
| `SelectiveFillerOffset` | .01 at `SelectiveSupplyCoverage` 3 |
| `DeepFillerOffset` | −.008 at `DeepSupplyCoverage` 10 |
| `EmergencyFillerOffset` | .10 |
| `StructureGateScale` | .02 |
| `PlayabilityTemperature` | .01 |
| `SplashCredit` | .6 |
| `SplashSourcesForFullSupport` | 3 |
| `QualityRelevanceStartPicks` | 10 |
| `QualityRelevanceFloor` | .20 |

Removed: `ReplacementLevelOffset` .04, `ShortfallReplacementOffset` .10 and `ComfortableSupplyCoverage` 1.5.

**New `CandidateDeckImpact` fields:**
- `RealCutline`, `ComparedWithFutureFiller`, `OpenSlots`, `ContestedSlots`, `RemainingPicks`, `ExpectedSupply`, `SupplyCoverage`, `SupplyUrgency`, `StructureGate`, `Playability`;
- `ReplacementLevel` now holds X.

**New `ContextualPickScore` fields:** `QualityRelevance` and `EffectiveQualityComponent`.

**New reasons**
- "+ Fills an open slot: 2.5 pp above the expected later filler (52.8%)."
- "− Fills an open slot, but later picks should supply better …"
- "· Later picks should replace your weakest projected card; measured against the expected later filler …"
- "· Plenty of picks remain, so open-slot value is modest."
- "+ Deck still needs 3 playables with only 4 picks remaining."

The badge is unchanged: score, GIH, ALSA and EST.

## 4. Audits

### Open slots (picks fixed at 30, average 56% candidate; scorelab)

| On-colour spells | 52% pool: v1.4 → v1.5 | 56% pool | 58% pool |
|---|---|---|---|
| 10 | 36 → 33 | 36 → 32 | 36 → 32 |
| 18 | 35 → 33 | 35 → 31 | 35 → 31 |
| 20 | 33 → 33 | 33 → 29 | 33 → 29 |
| 22 | 33 → 33 | 33 → **24** | 33 → **24** |
| 23 (full) | 33 → 33 | 26 → **24** | 20 → **20** |

The 22 → 23 step was 7 (56% pool) and 13 (58% pool) points in v1.4. It is now 0 and 4. The remaining 4 points are a real difference: displacing a 58% card is not the same as filling a slot. The weak 52% pool has 23 contested slots that later picks cannot cover, so its upgrades keep their value.

### Pick progress (18 on-colour 56% spells, 5 short; filler at 55% / 56%)

| Picks completed | 20 | 24 | 28 | 32 | 36 | 39 | 41 |
|---|---|---|---|---|---|---|---|
| v1.4, 55% filler | 26 | 27 | 29 | 34 | 37 | 37 | 37 |
| v1.5, 55% filler | **23** | 24 | 26 | 29 | 30 | 35 | 37 |
| v1.5, 56% filler | 26 | 27 | 30 | 32 | 34 | 37 | 38 |

Monotone: the virtual line never rises, and D and urgency never fall, as picks disappear.

### Cut-line continuity

- **Full deck at 41 picks:** 55.95% → 28 and 56.05% → 29, with D differing by .013. 55.5% scores 26, 56.5% scores 31, +2 pp scores 36 and −3 pp scores 13.
- **Real pool at P3P13:** Inspired Tethermage (52.3%, 0.0 pp) scores 22 and Way of the Pyromancer (52.2%, −0.1 pp) scores 21.

### Off-colour bomb across the draft (65% blue vs a red-green pool)

| Picks | 2 | 10 | 14 | 18 | 22 | 26 |
|---|---|---|---|---|---|---|
| v1.4 | 47 | 44 | 41 | 38 | 37 | 33 |
| v1.5 | 47 | 44 | 40 | 34 | 31 | 25 |
| R | 1 | 1 | .945 | .807 | .624 | .436 |

The P3P5 anchor is 24 → 16. The test fixture confirms the decline never rises and falls at most 7 points per four picks.

### Alternate pairs and splashes (scorelab)

| Scenario | v1.4 → v1.5 | R / P |
|---|---|---|
| Pick 18, black-green established, red-green close (C) | 46 → **45** | .943 / .707 |
| Pick 18, red far behind | 38 → 35 | .807 / 0 |
| Pick 34, red-green close | 46 → 43 | .714 / .641 |
| Pick 34, red far behind (D) | 24 → **16** | .204 / 0 |
| Splash {3}{U}, 0 / 1 / 3 blue-producing lands (E) | 24 → 16 / 18 / **21** | P 0 / .2 / .6 |
| {1}{U}{U} with 3 lands | 24 → 16 | P 0 (no double-pip splash) |

### Quality-aware structure (full 57% deck, zero early plays; 2-drop vs equal-rate 4-drop)

| 2-drop GIH | 50% | 53% | 56% | 58% | 61% |
|---|---|---|---|---|---|
| Needed 2-drop | 15 | 19 | 30 | 40 | 45 |
| Equal-rate 4-drop | 7 | 11 | 22 | 32 | 41 |

These are identical in v1.4: the floor forces the 2-drop, so the gate is 1. A terrible needed 2-drop gets some credit but stays very weak (M); an average one leads by 8 (L); a strong one gets both quality and need (N).

## 5. Semantic anchors (scorelab, v1.4 → v1.5)

| Anchor | Score |
|---|---|
| A average P1P1 | 25 → 25 |
| B +3 pp | 34 → 34 |
| C bomb +10 / +13 pp | 48 / 49 → 48 / 49 |
| E solid late | 31 → 31 |
| F strong upgrade +5 pp | 42 → 42 |
| G needed average 2-drop | 32 → 32 |
| H off-colour P1P3 | 47 → 47 |
| **I off-colour 65% at P3P5** | **24 → 16** |
| I′ off-colour 68% | 25 → 16 |
| J misses the deck | 12 → 12 |
| On-colour bomb late | 48 → 48 |
| Useless off-colour | 4 → 4 |

**Matched pairs**
- Unchanged: needed 2-drop +4, removal +4, creature +3.
- On- vs off-colour 60% late: 40 vs 19 → 40 vs 15.
- A 56% card in a 54% deck at 33 picks: 32 → 27. With 8 picks left, the deck's weak cards are measured against the expected later filler.

## 6. Replay (same 9 × 1,000 deterministic FRA packs, 0 network requests)

### v1.4 → v1.5

| Stage | Mean Δ | Max \|Δ\| | Cards with rank change | Top-pick changes | On-colour mean | Off-colour mean | v1.5: makes deck vs misses (on-colour) |
|---|---|---|---|---|---|---|---|
| P1P1 | 0 | 0 | 0% | 0% | — | — | — |
| P1P5 | 0 | 0 | 0% | 0% | — | — | — |
| P1P10 | −0.47 | 2 | 6.7% | 1.7% | 17.4 → 16.6 | 18.3 → 18.3 | 16.6 |
| P2P1 | −0.74 | 4 | 35.2% | 4.2% | 23.0 → 21.0 | 20.1 → 20.0 | 21.0 |
| P2P5 | −0.56 | 4 | 17.7% | 4.0% | 21.2 → 19.9 | 17.4 → 17.2 | 19.9 |
| P2P10 | −0.53 | 5 | 11.6% | 7.0% | 20.5 → 19.4 | 15.5 → 15.3 | 19.4 |
| P3P1 | −0.84 | 9 | 15.9% | 3.4% | 26.2 → 25.1 | 15.8 → 14.9 | 25.1 |
| P3P5 | −1.99 | 11 | 19.2% | 5.6% | 31.5 → 26.6 | 12.4 → 11.9 | 28.5 vs 7.5 |
| P3P10 | −0.50 | 6 | 4.7% | 1.6% | 24.8 → 23.7 | 10.9 → 10.6 | 28.8 vs 10.4 |

Up to P3P1 the replay pools are open shells, so every on-colour card is an open-slot case. From P3P5 they are full decks with real replacements.

**P1 stability.** P1P1 and P1P5 are bit-identical: deck need is unweighted there and R is 1. P1P10 moves by at most 2 points.

### v1.5 score distribution

| Stage | Mean | Med | p90 | p99 | Max | Top | 2nd | Pack median | 0–14 | 15–24 | 25–34 | 35–39 | 40–44 | 45–50 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| P1P1 | 21.0 | 21 | 31 | 43 | 49 | 35.8 | 30.3 | 21.1 | 20.9% | 45.0% | 28.5% | 3.9% | 1.2% | 0.5% |
| P2P1 | 20.5 | 21 | 28 | 38 | 48 | 32.3 | 28.3 | 20.7 | 17.0% | 54.8% | 26.1% | 1.3% | 0.8% | 0.1% |
| P3P1 | 18.5 | 17 | 31 | 40 | 48 | 33.8 | 29.1 | 17.3 | 31.8% | 48.3% | 18.3% | 0.5% | 0.6% | 0.5% |
| P3P5 | 17.2 | 14 | 35 | 38 | 47 | 34.0 | 28.3 | 14.5 | 51.3% | 26.5% | 11.8% | **10.1%** | 0.1% | 0.1% |
| P3P10 | 15.9 | 13 | 31 | 37 | 37 | 27.5 | 19.3 | 13.8 | 67.2% | 12.2% | 14.6% | 6.1% | 0% | 0% |

In v1.4 the 35–39 shares were 3.9 / 1.4 / 1.7 / **15.5** / 7.2%; the tier boundaries themselves are unchanged.

### Strong and Premium composition, v1.4 → v1.5

**Strong (35–39)**
- **P3P1:** 233 → **64**. Open-slot mediocre cards 196 → 27, and those remaining average Q .59 (about +1.5 pp). All 37 intrinsically good cards stay.
- **P3P5:** 1,547 → 1,010, mean Q .467 → .504. Weak cards (Q < .5) carried by structure: 860 → 411.
- **P3P10:** 359 → 306.

The remaining late Strong cards upgrade a deck with more weak cards than later picks can replace. They clear the expected final marginal card by +3.6 pp on average, so they are genuine contextual picks for those weak replay pools.

**Premium (45+)** is unchanged at every stage (P3P1 75, P3P5 9), always Q ≥ .6.

### Off-colour high-Q cards that miss the deck

| Stage | Cards | v1.4 mean (share ≥ 20) | v1.5 mean (share ≥ 20) | Mean R |
|---|---|---|---|---|
| P2P5 | 12 | 33.7 (100%) | 31.4 (100%) | .81 |
| P3P1 | 148 | 28.1 (100%) | 21.1 (100%) | .35 |
| P3P5 | 16 | 22.2 (100%) | **16.2 (0%)** | .23 |
| P3P10 | 2 | 21.0 (100%) | 15.0 (0%) | .20 |

**Examples**
- **P3P1:** The Theorist, Jace Beleren (Q .987, L .50, A .50, D .10, P 0, R .353) goes 31 → 22. Garruk, Veiled Butcher goes 30 → 22.
- **P3P5:** Null Summoner (Q .882, R .232) goes 23 → 17.

## 7. Real 42-card FRA pool (`fraprobe poolreplay`, v1.4 → v1.5)

| Candidate | Role | P2P8 (18 cards) | P3P5 (32, one open slot) | P3P13 (full) |
|---|---|---|---|---|
| Violent Echoes | strong upgrade | 37 → 37 | 41 → 41 | 42 → 42 |
| Fblthp, Knows the Way | upgrade plus 16th creature | 33 → 32 | 37 → 37 | 40 → 40 |
| Craterclaw Colossus | top end (no redundancy at 3 MV5+) | 29 → 29 | 34 → 34 | 38 → 37 |
| Sureshot Sower | average-minus 2-drop | 23 → 22 | 29 → 28 | 32 → 31 |
| Essence Burn | removal near the cut | 19 → 17 | 24 → 22 | 24 → 24 |
| Inspired Tethermage | just above the cut (0.0 pp) | — | — | 22 |
| Way of the Pyromancer | just below the cut (−0.1 pp) | — | — | 21 |
| The Theorist, Jace Beleren | off-colour bomb | 38 → **33** | 26 → **17** | 24 → **16** |
| Extended Absence | off-colour removal | 29 → 27 | 20 → 17 | 19 → 16 |
| Rockfall Vale / Overgrown Farmland | unneeded duals | 27 / 14 → 27 / 14 | 13 / 10 | 12 / 10 |

**Notes**
- The real pool has about 8 maindeck spells below baseline − 3 pp. Later picks cannot replace them all, so its upgrades keep their value, which is correct for that deck.
- At P2P8 the open-slot fillers drop 1–2 points, measured against the expected later filler (53.8%).
- Rockfall Vale at P2P8 still reduces a measured colour shortfall.

## 8. Performance

Interleaved v1.4 vs v1.5 tool binaries, 4 rounds of 20 samples each:

| Workload | v1.4 median | v1.5 median | v1.5 p95 | v1.5 worst | Optimizer calls |
|---|---|---|---|---|---|
| FRA pool, 14 candidates | 71.3–78.7 ms | 74.1–77.6 ms | 104–128 ms | 130 ms | 12 (unchanged) |
| All-pairs stress | 233–245 ms | 231–234 ms | 236–257 ms | 265 ms | 15 (unchanged) |

R and X reuse existing projections and decisions, adding only a sort of contested cards and a softmax per candidate. There were 0 network requests.

## 9. Tests

**New: `ContextualPickScorePlayabilityTests`** (19 methods, 20 cases).
- **Formulas:** continuity and monotonicity of the filler level, the order statistic and R.
- **Scenarios A–E:** P1P1, P1P3, alternate pair, the late 10–20 range with ordering and raw Q kept, and splash vs double pip.
- **Smooth off-colour decline.**
- **EST unaffected.**
- **Scenarios F–N:**
  - F/G: cut straddle;
  - H/I: clear upgrade and clear miss;
  - J/K: open-slot filler with many vs few picks;
  - urgency monotonicity;
  - 22 → 23 continuity for weak and average pools;
  - an open slot is not free membership;
  - a weak card later picks will replace;
  - L/M/N: needed 2-drop at terrible, average and strong quality.
- **Monotone in GIH** against both the real and the virtual cut.

**New Application test:** a late off-colour bomb's evidence shows "Intrinsic quality: excellent" and "Playability: unlikely to make your current RG deck", the diagnostics show Q raw and R, and the score is 10–20.

**Updated**
- Model version assertions changed from v1.4 to v1.5 (3 tests).
- The calibration test was renamed to the frozen v1.4 map, and anchor I is now asserted at 10–20.
- Phase 4's J test moved to the final picks, where a 48% 23rd card is really the cut. Earlier in the draft it is now (correctly) measured against the expected later filler, which the new test covers.

**Totals:** **1,209 solution tests pass** (Domain 56, Data 160, Application 273, ArenaIntegration 127, RecommendationEngine 309, App 284), with 0 warnings and 0 errors.

## 10. Remaining limitations

- **Splash support** counts only drafted lands that produce the colour. Phase 10 builds no splashes, so a splashable card earns relevance (R) but its D stays at the floor.
- **Playability** uses the same pair viability as the deck builder. An incomplete shell's viability is count-driven (Phase 4 limitation), so a lopsided pool can make a colour-plus-one pair legitimately viable late.
- **Supply constants** (share .8 and the filler levels) come from FRA red-green availability in the replay sampler, not from per-set data. Other formats may differ. The order statistic assumes later playables beat every contested card.
- **Late Strong scores remain** for weak decks whose weak cards outnumber later supply (P3P5 replay: 10.1%). This is by design, but it depends on pool strength.
- **L and A** are about neutral in the replay harness (no pick history or pair data), so their interaction with R is analytic rather than replayed.
- **The R floor** of .20 (hate-draft and pivot value) is a judgment, not data-derived.

## 11. Evidence

Evidence is in `artifacts/pick-score-v1-claude-audit/phase6/` (git-ignored):

| File | Contents |
|---|---|
| `replay-v14-vs-v15.txt` | Replay comparison |
| `dist6-v15-analysis.txt`, `dist6-v15-summary.json` | v1.5 distributions (the first block in `dist6-v15-analysis.txt` is v1.5, the second v1.4) |
| `fra-supply.txt` | FRA supply evidence |
| `p6-v14.txt`, `p6-v15.txt` | Scorelab open-slot, progress, continuity, off-colour, splash and structure audits |
| `p5-v15.txt` | Anchors |
| `pool6-cmp.txt` | Real pool, before and after |
| `ab6-*.json` | Benchmarks |
| `audit6.py`, `cat6.py`, `cmp6.py`, `fraprobe-Program.cs` | Harness and analysis scripts |
