# Contextual Pick Score — Phase 7: Final V1 Validation, Stability Audit, Generalization Review and Freeze

**Verdict: V1 READY**, frozen as **`contextual-pick-score-v1.6`** with tier scheme `pick-score-tiers-v2`.

I tried to break v1.5 and found one substantive defect.

- **The defect:** a hard pair-viability threshold made scores flip by up to 27 points between consecutive picks.
- **The fix:** near-viable pairs (up to 2 spells short of the leading shell) now enter with a tapered weight, and every build's virtual cut line uses that build's own slots.
  - Six-point-plus pick-to-pick swings fall from 1.4% to 0.8%, and the largest from 27 to 19.
  - The largest swing for a non-land probe falls from 27 to 14.
  - No large delta is unexplained.
- **What did not change:**
  - the final calibration;
  - the tier thresholds;
  - Q, L, A;
  - the Phase 6 formulas;
  - the deck optimizer;
  - localization.
- **Scores:** P1P1, P1P5, P2P5, P3P1 and P3P5 in the 9 × 1,000 replay are identical to v1.5.

The specification is frozen in [CONTEXTUAL_PICK_SCORE_V1_SPEC.md](CONTEXTUAL_PICK_SCORE_V1_SPEC.md). Twelve golden fixtures pin it.

## 1. V1 model map (frozen)

The pipeline runs in this order:

1. **Inputs**
   - the Arena snapshot (pack, picks, pool);
   - set/format 17Lands card rows (GIH, games, ALSA), resolved through the Phase 3 statistical identity;
   - the environment baseline;
   - Scryfall gameplay metadata;
   - optional 17Lands pair statistics;
   - pack observations.
2. **Components:**
   - **Q:** Phase 8 shrink, then `SoftLimit(.5 + (adj − baseline)/.16, .35)`; estimated cards use .5.
   - **L:** Phase 9B lane evidence.
   - **A:** Phase 9C pair lift × active confidence.
   - **D:** graded fit against `max(real cut, X)` plus `.30·c·gate·S`, blended over viable and near-viable pairs.
3. **Playability relevance:** `R = 1 − ramp(picks)·(1 − P)·.8`, and `Q_eff = Q > .5 ? .5 + R(Q − .5) : Q`.
4. **Weighted value:** `w = wQ·Q_eff + wL·L + wA·A + wD·D`, with weights interpolated by smoothstep between picks 4, 21 and 35.
5. **Final calibration:** `V = SoftLimit(.5 + 1.4(w − .5), .45)`.
6. **Scores and tier:**
   - exact score `50V`, which is the ranking authority;
   - displayed integer: `round(50V)`, half away from zero;
   - tier: `PickScoreTierThresholds`, which the badge palette maps to its treatments (border and animation).
7. **Badge:** Pick Score [EST], GIH, ALSA.

## 2. Defect found and fixed: the pair-viability cliff

### Reproduction

In the self-draft replay, draft 91 hovers between UG and UR. The strong-red probe (Kiora of Fire and Ashes, 62%) scored, at P3P2 through P3P8:

**45, 42, 17, 17, 16, 16, 43**

Each flip followed a single green pick.

### Root cause

`CandidateDeckImpactEvaluator.Project` projected only pairs with eligible spells ≥ `target`, where `target = min(23, max eligible)`.

- **Effect of one card:** a single green pick raises UG's count. UR, one spell behind with equal quality, then disappears entirely from the projections.
- **Consequences:** the red card's deck need drops from about .9 to the .10 floor, and its playability P goes from .68 to 0.
- **History:** the cliff predates Phase 6 (V1 deck fit had it); Phase 6's P inherited it.

The same mechanism produced:
- the +16 one-pick jump in the synthetic BG → RG pivot;
- a +10 jump for red and a −13 drop for black in the pivot sequence.

### Fix (model v1.6)

1. **Near-viable projection.** Pairs with eligible spells ≥ `target − ViabilityTolerance` (2) are projected at their own shell size. No optimizer change.
2. **Taper.**
   - A pair `s` spells short keeps `1 − s/3` of its blend weight (1, 2/3, 1/3, 0), in both the deck-fit blend and the playability softmax.
   - The removed weight returns to the best fully viable pair, so a pair fades in and out instead of switching at the count threshold.
   - Fully viable pairs still sort first, so the "best deck" anchor is unchanged.
3. **Per-build virtual cut line.** Each evaluated build uses its own open slots and weak spells.
   - Before the fix, a near-viable build's open slot was judged against the best deck's line.
   - The open-slot reason now names the build ("Fills an open slot in the projected UR build").

### Explanation-only fix

A card kept in by the top-end cap was described as "Required by the projected deck's creature/early-play floor". It now reads "Kept in by the projected deck's composition limits (creature/early-play floors or top-end cap)."

### Evidence (same 240 self-drafts, 59,040 probe transitions)

| | v1.5 | v1.6 (tolerance 2) | Tolerance 1 (rejected) |
|---|---|---|---|
| Transitions with \|Δ\| ≥ 6 | 827 (1.4%) | **468 (0.8%)** | 655 (1.1%) |
| Largest \|Δ\| | 27 | 19 (dual land) | 23 |
| Strong-red probe max \|Δ\| | 27 | **10** | 14 |
| Hard-removal probe max \|Δ\| | 24 | **11** | 12 |
| Green 2-drop probe max \|Δ\| | 23 | **12** | 15 |
| Needed role with \|Δ\| ≥ 10 | 152 | **30** | 52 |
| Unexplained large deltas | 0 | 0 | — |
| FRA saved-pool median (quiet machine, interleaved) | 70.6–72.7 ms | 91.1–93.9 ms | 80.4–86.1 ms |
| Optimizer calls (FRA pool / stress) | 12 / 15 | 21 / 15 | 19 / 15 |

**Performance decision.** The tolerance-2 fix costs about 21 ms (+29%) on the normal pack, coming from 9 extra near-viable projections. The stress pack is unchanged.

The spec's 15% threshold was exceeded and investigated: the cost is inherent to projecting the alternative builds, and it buys the largest stability gain. Tolerance 1 is cheaper but leaves 40% more large swings.

The median stays interactive at about 93 ms, and the warm repeated-scoring loop measured 44 ms on a quiet machine. The new regression threshold is 15% over the v1.6 baseline (investigate above about 107 ms on a quiet machine).

## 3. Semantic anchors A–P (v1.6, scorelab; exact, displayed, tier)

| Anchor | Meaning | Exact | Shown | Tier | Explanation (key reasons) |
|---|---|---|---|---|---|
| A | average P1P1 | 25.00 | 25 | solid | near-average intrinsic GIH |
| B | +3 pp P1P1 | 33.75 | 34 | solid | strong intrinsic GIH |
| C | +10 pp bomb P1P1 | 47.37 | 47 | premium | excellent intrinsic GIH |
| D | +13 pp bomb, active RG archetype with pair lift, P2P5 | 49.64 | 50 | premium | excellent GIH; performs well in the active pair; fills an open slot |
| E | +1 pp on-colour, makes deck, P3P5 | 30.64 | 31 | solid | near the cut line (+1.0 pp); replaces the 23rd card |
| F | +5 pp on-colour upgrade, P3P5 | 41.92 | 42 | excellent | clears the cut by 4.8 pp |
| G | average card filling a need (1 early play) | 32.43 | 32 | solid | kept in by composition limits; fills missing early plays (1 of 4) |
| H | excellent off-colour P1P3 | 47.37 | 47 | premium | excellent GIH (R 1) |
| I | excellent off-colour P3P5 | 16.32 | 16 | marginal | quality relevance 21%; cannot enter a realistic maindeck |
| J | −3 pp, misses the deck late | 12.41 | 12 | very weak | 2.9 pp below the cut |
| K | needed early play, equal quality (0 early plays) | 35.76 | 36 | strong | fills missing early plays (0 of 4) |
| L | redundant top end (projected 7 MV5+, cap 6) | 23.31 | 23 | marginal | "already has 7 cards at MV5+" (golden fixture; an equal 3-drop scores 26) |
| M | removal when removal-light | 30.41 | 30 | solid | only 0 removal in the projected deck |
| N | removal when removal-healthy (5) | 26.35 | 26 | solid | sidegrade, no removal credit |
| O | real cut-line sidegrade, last picks | 26.40 | 26 | solid | near the cut line (0.0 pp) |
| P | provisional open-slot filler (strong 22-spell deck, pick 30) | 24.20 | 24 | marginal | "fills an open slot, but later picks should supply better"; "plenty of picks remain" |

All ten Phase 5 anchors (A–J) stay in range, with I in 10–20. **Direct vs estimated:** a direct card at exactly the neutral prior and an estimated card give identical exact scores at P1P1 (25.0000), P2P5 (26.2609) and P3P5 (26.3515).

## 4. Whole-draft trajectories (strong red 62% card; scorelab)

| Trajectory | P1P1 | P1P5 | P1P10 | P2P1 | P2P5 | P2P10 | P3P1 | P3P5 | P3P10 | Largest step |
|---|---|---|---|---|---|---|---|---|---|---|
| A: red becomes main (RG) | 42 | 42 | 42 | 42 | 42 | 42 | 42 | 43 | 44 | 1 |
| B: we draft UB | 42 | 42 | 41 | 36 | 32 | 27 | 21 | 17 | 15 | 6 |
| C: red for 6 picks, then UB | 42 | 42 | 42 | 42 | 35 | 27 | 21 | 17 | 15 | 8 |
| D: BG, then pivot to RG from pick 14 | 42 | 42 | 41 | 36 | 32 | 36 | 40 | 39 | 40 | 5 (v1.5: 16) |
| E: UB + 3 R-producing lands ({3}{R} splash) | 42 | 42 | 41 | 36 | 32 | 31 | 25 | 20 | 19 | 6 |
| F: UB, {1}{R}{R}, no path | 42 | 42 | 41 | 36 | 32 | 27 | 21 | 17 | 15 | 6 |

Every movement is smooth and explained by R (the ramp from pick 10) and by D (pair viability and fit). E stays above F because of splash playability.

**Real self-drafts (strong-red probe, mean by stage):**
- **Red in the final pair (28 drafts):** 43 → 44 → 44 → 42 → 43 → 43 → 41 → 42 → 40.
- **Red absent (212 drafts):** 43 → 44 → 42 → 37 → 34 → 29 → 23 → 21 → 22.

**Pivot (BG → RG, scorelab), v1.5 → v1.6:**
- **Red:** 36 → 35 → 33 → 31 → 32 → 34 → 37 → 37, with no step above 3. In v1.5 there was a +10 jump.
- **Black:** 37 … 34 → 31 → 26 → 23 → 20, gradual. In v1.5 it dropped 38 → 25 in one step.

## 5. Score-delta audit (self-draft replay, 240 drafts, 6 probes, v1.6)

Of the 59,040 consecutive probe transitions, **468 have |Δ| ≥ 6**. Every one is explained by a component that accounts for at least half of the change (0 unexplained). All of them are deck need, except 3 from quality relevance.

| Cause | Count |
|---|---|
| Cut-line or structure movement | 182 |
| Real ↔ virtual cut switch (urgency at the end of the draft) | 115 |
| Projected pair change, with or without playability/membership | 119 |
| Playability change (near-viable taper) | 32 |
| Makes ↔ misses | 20 |

The largest remaining swings are the dual-land probe (±16–19; see §11) and an estimated card at the final pick (+14). The latter is the last open slot with zero remaining picks, so emergency filler is the alternative.

## 6. Small-change and component stability (scorelab)

**One unrelated card added to the pool** (a 45% colourless artifact, or an off-colour white card), with P2P5, P3P5 and P3P13 pools and four candidates: every score moved by **0 or ±1**.

**Archetype activation (Phase 9C).** A turns on only when confidence ≥ .35 and lead ≥ .10, which is a threshold. Its jump is bounded by `.5·affinity·confidence` × wA:
- **Measured:** a synergy card goes 26 → 29 at activation, a 3-point step, then rises smoothly to 31.
- **Generic card:** gets no synergy credit (A ≤ .5).

I report this rather than change it: the maximum activation step is about 2.5 points late, and it is Phase 9C behaviour that was not to be touched.

**Lane signals (P1P10, red 58% candidate):**

| Observations | Score |
|---|---|
| none | 31 |
| one late red card | 32 (+1) |
| four consistent late red cards | 38 (+7, the cap of the ±7-point early lane range) |
| contradictory red then green | 34 |
| the same four signals aged to P3P1 | +1 |

**Deck-cut trajectory.** A 57% card tracked from an 8-spell shell to a 29-spell full deck scores 29, 30, 30, 30, 28, 30, 31, 31, 31. The largest step is 2, with no membership jump.

**Remaining-picks urgency** (18 spells, 5 short):

| Picks | 50% filler | 53% filler |
|---|---|---|
| 20 | 9 | 17 |
| 28 | 8 | 18 |
| 34 | 8 | 23 |
| 38 | 28 | 33 |
| 41 | 25 | 31 |

Urgency never makes a weak card premium: a 50% card that must be played reads solid (28 at pick 38, 25 at the last pick). The rise is steep once supply falls below the open slots (S < m), as designed.

## 7. Role signal sanity

| Role | Matched delta (P3P5, scorelab) | Replay structural-need points (mean / max / min) |
|---|---|---|
| Creature (6 creatures) | +3 (0 when healthy) | +2.4 / +8.4 / −2.9 |
| Early play (0 / 2 early) | +8 / +4 | +1.3 / +8.4 / 0 |
| Hard removal (none / 5) | +4 / 0 | +1.4 / +6.8 / −1.9 |
| Conditional removal (none) | +2 | +1.4 / +8.4 / 0 |
| Top-end redundancy (projected 7 / 10 MV5+) | −2 / −4 vs equal 3-drop | — |
| Fixing land | binary (see §11) | 671 of 6,602 late lands have D > .1 |

No role overwhelms Q. The structural-need maximum is 8.4 points at late weights. A terrible needed card (a 50% 2-drop) scores 15, a 52% removal card with no removal in the deck scores 11, and twelve 6-drops penalise a 60% 6-drop to 33.

## 8. Removal classification spot audit

The roles are identical to the Phase 4 audit: 18 hard, 12 conditional, 16 fixing, ramp 0, no overrides.

99 FRA cards mention destroy, exile, damage or "target creature" without being classified. A deterministic sample of 14 of them is all correctly excluded:
- pump (Academic Ascent);
- counters and recursion (Rewrite Regrets);
- player-only damage, e.g. the prepare spell "Vicious Verse" (1 damage to an opponent);
- lifelink grants;
- −5/−0 modes.

The classifier stays conservative, and no change was made.

## 9. EST and direct-vs-estimated validation

- **Display:** EST shows beside the score, and GIH shows "—" (existing App tests).
- **Q:** neutral (.5); L, A and D are unaffected.
- **Replay:** 1,231 EST card evaluations, mean 24.2, max 39.
- **High EST scores are legitimate:** Codie and Yargle reach 39 with D ≈ 1.0 when they genuinely upgrade the deck.
- **Ranking:** 322 EST cards were the top pick. With all candidates estimated, ranking follows context (26 / 26 / 14).
- **No hidden penalty:** direct-at-prior and estimated cards are identical in exact score (§3).

## 10. Canonical presentation, ranking and invariance

- **Tiers.** A single implementation (`PickScoreTierThresholds`) drives the badge treatment, animation, diagnostics and preview. Existing tests cover every score from −1 to 51 and null.
- **Badge content** is unchanged: score [EST], raw GIH, raw ALSA, and "—" when unavailable. The ordinal rank is not the main number.
- **Rank vs displayed integer:** 10,080 replay packs show 0 cases of a higher-ranked card displaying a lower integer.
- **Exact ties:** 19 adjacent exact ties across 75,600 cards, all EST cards with identical neutral quality. They are resolved by raw Q, then data weight, then pack order (deterministic). Sample size never overrides a differing exact value.
- **Printed rarity:** each golden fixture is re-run as Mythic vs Common and is identical in exact score, integer and tier. The existing App tests keep a Common at 47 premium and a Mythic at 12 very weak.
- **Determinism:** the first 20 self-drafts were re-run byte-for-byte identical (11,340 rows) for v1.5 and again for v1.6. The 9 × 1,000 replay is identical across the supply-profile refactor (87,000 rows).

## 11. Outlier search and surprising results (v1.6, 75,600 card evaluations)

| Check | Count | Assessment |
|---|---|---|
| Low Q (< .40) at 40+ | 0 | — |
| High Q (> .85) at ≤ 10 | 0 | — |
| No realistic build at 35+ | 16 | All in P1 or early P2, where R = 1 by design (pivoting is preserved), e.g. Craftwork Crusher at P1P7 scoring 44 |
| Off-colour, unplayable, P3, 25+ | 0 | — |
| EST at 45+ | 0 | — |
| EST at 35+ | 11 | Legitimate (D ≈ 1) |
| Weak card ranked above a bomb | 6 | The bomb is late and off-colour (R ≈ .35); the weak card makes the deck. Legitimate |

**Surprising results, all reviewed and legitimate:**
- **Largest positive context (+16.5):** Mindseeker Oculus (Q .75) at P3P8 scoring 47, with an open lane (L .86) and a real upgrade (D 1.0).
- **Largest negative context (−12.6):** late off-colour chaff.
- **Highest-Q card at 14 or below:** late off-colour Primal Witchstalker (Q .69, R .20).
- **Strongest on-colour card excluded from the deck:** Icy Reception (Q .63) in a deep blue pool, scoring 29–35.

**Remaining discontinuity (accepted V1 limitation).**
- **What:** fixing lands keep Phase 4's binary deck need: .55 + .35 × shortfall reduction when selected and reducing a measured colour-source shortfall, otherwise .10. A dual land can move ±16–19 points when the projected source shortfall changes by one source.
- **Why it is acceptable:** lands average 16.8 (maximum 39) and only 671 of 6,602 late lands carry fixing value. Smoothing it needs a continuous mana-demand model.

## 12. 10,000+ state replay summary (self-draft, v1.6)

**Scale:** 240 deterministic drafts × 42 picks = **10,080 pack states** (75,600 cards) plus 60,480 probe evaluations, run with 0 network requests.

| Stage | Mean | p90 | 0–14 | 15–24 | 25–34 | 35–39 | 40–44 | 45–50 | Mean score of the picked card |
|---|---|---|---|---|---|---|---|---|---|
| P1 early | 20.3 | 30 | 22.4% | 47.3% | 26.7% | 2.8% | 0.6% | 0.1% | 32.5 |
| P2 early | 20.0 | 29 | 24.0% | 50.5% | 21.7% | 3.0% | 0.8% | 0.1% | 32.8 |
| P3 early | 17.9 | 34 | 45.2% | 31.6% | 15.0% | 6.0% | 1.9% | 0.2% | 35.1 |
| P3 late | 14.1 | 28 | 67.8% | 19.2% | 9.2% | 3.5% | 0.3% | 0% | 19.0 |

**Picks**
- **Tiers of the picked card:** solid 45%, marginal 22%, strong 18%, very weak 9%, excellent 6%, premium 0.8%.
- **Rarity of the picked card:** Common 8,076, Uncommon 1,547, Rare 410, Mythic 47.

**Final pairs:** UB 150, WU 30, UG 28, UR 28, BG 4. The simulated drafter has no competing drafters, and FRA's best commons are blue.

**P2–P3 nonland cards**

| Group | Mean | p90 |
|---|---|---|
| On-colour, makes the deck | 24.5 | 36 |
| On-colour, misses the deck | 15.0 | 25 |
| Off-colour | 13.6 | 21 |

**9 × 1,000 stage replay, v1.5 → v1.6:**
- P1P1, P1P5, P2P5, P3P1 and P3P5 are identical.
- P1P10, P2P1, P2P10 and P3P10 move by at most 4 points, with top-pick changes of 0.4–7.2%.
- Tier shares are within 0.5 percentage points everywhere except P2P10 (very weak 32.4 → 27.5%).

## 13. Generalization audit: FRA-specific constants

**A. Mathematically generic**
- the soft-limit knees (.35 Q, .45 final);
- calibration gain 1.4 and the 0–50 scale;
- the logistic, smoothstep and softmax forms;
- temperatures .0025 (deck-fit blend) and .01 (playability);
- the near-viable taper.

**B. Generic Limited configuration (not data-fitted):**
- **Draft shape:** 42 picks, 14 per pack.
- **Weights and anchors:** weight anchors 4 / 21 / 35 and the weight table.
- **Statistics:** Q span .16 and the Phase 8 500-game prior.
- **Deck targets:** 23 spells, 16 / 14 creatures, 4 early plays, top-end cap 6 at MV5+, healthy removal 3.
- **Deck-need terms:**
  - floor .10, replacement scale 4 pp, cut-line scales 1–2 pp;
  - structure weight .30, confidence 18 spells, redundancy span 3, structure gate 2 pp;
  - land fixing .55 / .35.
- **Splash and relevance:** splash credit .6 with 3 sources; R floor .20 and ramp 10 → 35.
- **Viability and tiers:** viability tolerance 2; tiers v2.

**C/D. Derived from FRA (specifically FRA red-green card availability):** the future-pick supply profile:
- playable share .8;
- filler offsets −3 / −1 / +0.8 pp at coverage 1 / 3 / ≥ 10;
- emergency filler at baseline −10 pp. This value predates Phase 6 (it came from Phase 1) and is retained in the profile.

**On another set:**
- The supply profile is now explicit (`FuturePickSupplyProfile`).
- FRA uses its calibrated profile. Every other set uses the same values as `provisional-default`, and the expanded diagnostics say so: "Supply profile: provisional-default (provisional: FRA-derived values, not calibrated for this set)".
- Offsets are relative to each set's own environment baseline, so they shift with the set.
- **What could be wrong elsewhere:** the depth of late playables, which affects how strongly open slots and weak cards are discounted. The likely error is 1–2 pp of virtual cut, a few late-draft points. It does not affect early scores or the ordering among on-colour cards competing for the same slot.
- **Remedy:** run `fraprobe supply` for the new set's pairs and add its profile.

This is not blocking for V1. The refactor is score-neutral for FRA (87,000 identical rows).

## 14. Known limitations (non-blocking)

1. **Splash decks are not built (Phase 10).**
   - **Frequency:** 41.5% of late off-colour cards have partial playability (from splash credit or near-viable pairs), but D stays at the floor.
   - **Effect:** they score at most 23 (mean 10.8) and are the top pick in only 74 of 10,080 packs, all weak packs where the top score was 21 or less.
   - **Severity:** low. It can under-rate a genuinely splashable bomb but never produces a wrong strong pick.
2. **Other-set statistical identity (Phase 3)** is validated physically only on FRA. It is structurally safe:
   - exact name first;
   - a front-face alias only for structured Adventure, Transform, MDFC or Prepare layouts whose canonical name equals the face names joined by " // ";
   - fails closed on any ambiguity;
   - no fuzzy matching and no back faces.

   In the worst case a card shows as EST, which is transparent, never a wrong row. Acceptable V1 risk.
3. **Fixing-land binary deck need** (§11).
4. **Archetype activation step** of at most about 3 points (§6). FRA has no cached pair statistics, so A is neutral in real FRA use.
5. **Supply calibration** is FRA-only (§13).
6. **The simulated drafter** has no competing drafters (lane evidence comes from ALSA removal); the replays are stress tests, not win-rate validation.

## 15. Tests

**New: `ContextualPickScoreV1FreezeTests`** (8 methods, 20 cases):

| Test | Cases |
|---|---|
| Golden fixtures, each re-run with Common vs Mythic rarity | 12 |
| Near-viable taper | 1 |
| Gradual pivot | 1 |
| Unrelated pool card | 1 |
| Archetype activation bound | 1 |
| Lane signal growth | 1 |
| Deck-cut trajectory | 1 |
| Accurate top-end-cap reason | 1 |
| Supply-profile fallback and provenance | 1 |

The golden fixtures are:
- P1P1 bomb;
- average card;
- early off-colour bomb;
- late off-colour bomb;
- needed 2-drop;
- redundant top end;
- removal need;
- real cut-line upgrade;
- estimated card;
- active-archetype card;
- viable alternate build;
- open-slot filler.

**Updated**
- Model version assertions changed from v1.5 to v1.6.
- An Application diagnostics test now also checks the supply-profile provenance line.
- Three fixtures were corrected after the v1.6 change, each documented in its test comment:
  - the urgency test now holds the pool fixed and varies only the draft position (growing padding had made padding pairs near-viable);
  - the off-colour floor comparison uses 1e-12 precision;
  - the shell-confidence test uses a mono-red shell, so the evaluated build is the reported one.

**Totals:** **1,229 solution tests pass** (Domain 56, Data 160, Application 273, ArenaIntegration 127, RecommendationEngine 329, App 284), with 0 warnings and 0 errors.

## 16. Performance and memory

| Workload | Median | p95 | Optimizer calls |
|---|---|---|---|
| FRA saved pool (late projected deck), v1.6 | 91–94 ms | 127–129 ms | 21 |
| Stress, v1.6 | 200 ms | 210–225 ms | 15 (unchanged) |

These figures come from the quiet interleaved run. Later runs on a loaded machine measured about 100–130 ms for both v1.5 and v1.6, so they can't separate the two.

- **Explanation generation:** 0.16 ms per 14-card pack (25 KB of text).
- **Memory:** 600 repeated scorings plateau at 10.1 MB managed and about 77–80 MB working set, with no growth. Caches are per pack (projections), per catalog (identity, `ConditionalWeakTable`) or per card (roles).
- **Network:** 0 requests in every run.

## 17. Recommendation

**Freeze Contextual Pick Score V1 at `contextual-pick-score-v1.6`** (tiers v2, FRA-calibrated supply profile with a labelled provisional default) and move back to live Arena validation and localization work.

Future score-changing work requires a model version bump and a regression comparison against the V1 golden fixtures and the replay harness.

**Post-V1 candidates, not started:**
- per-set supply calibration;
- splash deck construction;
- continuous fixing-land valuation;
- successful-deck structural profiles;
- archetype-specific role targets;
- smoothing archetype activation;
- validation on additional sets and formats.

## 18. Evidence

Evidence is in `artifacts/pick-score-v1-claude-audit/phase7/` (git-ignored):

| File | Contents |
|---|---|
| `selfdraft-v15-analysis.txt`, `selfdraft-v16-analysis.txt`, `selfdraft-v16-tolerance1-analysis.txt` | Self-draft replay analyses |
| `sd_analyze.py`, `fraprobe-Program.cs` | Analysis script and harness (`selfdraft` / `memory` / `supply` / `dist` modes) |
| `replay-v15-vs-v16.txt`, `dist7-analysis.txt` | Stage replay comparison and distributions |
| `scorelab-p7-v15.txt`, `scorelab-p7-v16.txt` | Synthetic audits |
| `roles-v16.txt` | Role classification |
| `memory.txt` | Memory check |
| `ab7*.json` | Benchmarks |
