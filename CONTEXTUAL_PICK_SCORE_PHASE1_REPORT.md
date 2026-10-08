# Contextual Pick Score — Stabilization Phase 1: Continuous Deck-Fit Scoring

Date: 2026-10-07. Model version `contextual-pick-score-v1` → **`contextual-pick-score-v1.1`**.

Scope: the deck-fit (D) component only. Untouched in this phase:

- Q, L, A, the stage weights and the 1.4 calibration;
- the 0–50 scale, badge tiers and artwork;
- top-end saturation and tie-break;
- multi-face 17Lands name lookups;
- Phase 9E.2B.

Nothing is committed.

Evidence lives in `artifacts/pick-score-v1-claude-audit/phase1/` (git-ignored):

| Files | Contents |
|---|---|
| `scenarios-before.txt` / `scenarios-after.txt` | All audit scenarios S1–S21 plus D1–D3, run with the frozen pre-fix DLLs and then the final DLLs. |
| `fra-before.txt` / `fra-after.txt` | Real FRA probe. |
| `pool-change-*.txt` | Small pool-change scenario. |
| `ab-*.json` | Interleaved benchmark. |
| `scorelab-Program.cs` / `sync.sh` | The harness. |

---

## 1. Root cause of the original jump

V1 deck fit for a nonland card (`CandidateDeckImpactEvaluator`, V1) was:

```
D = 0                                                    if not in the projected maindeck
D = clamp(.65 + .25·qualityGain + .10·structure, 0, 1)   if in it
```

From P3P8 onward D carries weight 0.40, and the final calibration multiplies by 1.4 × 50. Crossing the membership boundary therefore adds `.65 × .40 × 70 = 18.2` points.

Membership is the optimizer's **argmax decision**, which is discontinuous in a candidate's strength. In the audit fixture (27 on-colour playables at 57.0%), the candidate's inclusion flipped between 57.00% and 57.05% (12 → 31).

Near-ties were also decided by the optimizer's bitmask tie-break. Before this fix, shifting *every pool card* by 0.1 pp moved a 57.0% candidate from 30 to 12 (D3).

The qualityGain term, by contrast, was already nearly continuous. Its average-deck delta is about 0 at the boundary.

## 2. Exact changes

| File | Change |
|---|---|
| `src/DraftTG.RecommendationEngine/CandidateDeckImpactEvaluator.cs` | Projections retain the optimizer's per-card decisions and composition floors. `Project` returns every viable pair (best first) instead of only the best. Nonland D is the graded, pair-blended fit below. Open-slot value (replacement level) is computed from stage and supply. New reasons describe the cut-line margin. Optional `environmentBaseline` parameter. **Unchanged:** land D, structural/quality-gain formulas, caching, pair viability rules and the optimizer. |
| `src/DraftTG.RecommendationEngine/ContextualPickScoreModels.cs` | Seven named parameters plus helpers on `ContextualPickScoreConfiguration`. Init-only diagnostics on `CandidateDeckImpact`: `CutlineMargin`, `MembershipValue`, `ReplacementLevel`, `BestPairWeight`, `ProjectedPairCount`. Model version v1.1. Positional signatures unchanged. |
| `src/DraftTG.RecommendationEngine/ContextualPickScoreEngine.cs` | Passes the Phase 8 environment baseline to the evaluator (one argument). |
| `src/DraftTG.Application/PickScorePresentation.cs` | One extra diagnostics line: margin, graded membership, open-slot value, best-pair weight. |
| `tests/DraftTG.RecommendationEngine.Tests/ContextualPickScoreDeckFitTests.cs` | **New**, 14 tests. |
| `tests/DraftTG.RecommendationEngine.Tests/ContextualPickScoreTests.cs` | One assertion relaxed (see §6). |
| `ARCHITECTURE.md`, `ROADMAP.md` | Algorithm description and phase entry. |

## 3. Mathematical formulation

Rates are fractions (.01 = one GIH percentage point). `v(x)` is the optimizer's own selection value: Phase 8 adjusted GIH, plus the existing archetype adjustment when pair data apply.

### Cut-line margin within one viable projected pair *p*

Let `F` be the set of feasible single swaps: exchanges that keep *p*'s creature floor, early-play floor and top-end cap (taken from the optimizer's own composition result).

| Candidate *c* in pair *p* | Cut line |
|---|---|
| Included | `cut = max v(y)` over bench cards `y ≠ c` with `(c out, y in) ∈ F`. If there are none, `cut = r` (open-slot value). |
| Excluded | `cut = min v(x)` over maindeck cards `x ≠ c` with `(x out, c in) ∈ F`. If there are none, the card cannot enter and fit = 0. |
| Not colour-eligible | fit = 0 (unchanged). |
| *p* newly viable because of *c* | `margin = (Q̄_p,after − Q̄_best,before) · spells_p`: what the unlocked build gains over today's best deck, in per-card units. |

`margin = v(c) − cut`.

**Why this is continuous.** The optimizer is exact, so no feasible single swap improves its deck.

- An excluded candidate is therefore at or below its weakest swappable maindeck card (margin ≤ 0).
- An included candidate is at or above its best swappable alternative (margin ≥ 0).
- As `v(c)` rises, the inclusion flip happens exactly where these meet, at margin 0.

The membership *decision* jumps; the margin does not.

### Graded fit in pair *p*

```
m_p = logistic(margin / s),  s = 0.01 + 0.01·(1 − progress)      (2 pp early → 1 pp at end)
D_p = clamp( m_p·(0.65 + 0.10·structure_p) + 0.25·qualityGain_p, 0, 1 )
```

`structure_p` and `qualityGain_p` are V1's existing terms, computed against the same pair's before projection. They are only counted when the card is included. The 0.65 / 0.25 / 0.10 constants are V1's.

### Open-slot value `r` (replacement level)

```
open = 23 − projected spells;  supply = 0.6 · picks remaining after this one
urgency = clamp((1.5 − supply/open) / 0.5, 0, 1)     (0 when open = 0)
r = baseline − 0.04 − urgency·0.06                   (baseline−4 pp … baseline−10 pp)
```

### Pair blend

```
π_p = softmax(Q̄_p,after / 0.0025)    over all viable after-pairs (Q̄ = average common spell quality)
D   = Σ_p π_p · D'_p,   D'_p = D_p                           for pairs viable before this pick
                        D'_p = max(D_p, max_existing D_q)     for pairs this candidate newly makes viable
```

The `max` in the second line is a floor: an unlocked pair is an extra *option*. It can add value but cannot dilute the card below its fit in decks that already existed. Max and softmax of continuous terms stay continuous.

Lands keep V1's formula; see the limitations.

### New parameters

All on `ContextualPickScoreConfiguration`:

| Parameter | Value | Meaning |
|---|---|---|
| `CutlineScaleLate` / `CutlineScaleEarly` | .01 / .02 | Logistic width of the cut-line margin. |
| `ReplacementLevelOffset` | .04 | Open slot = baseline − 4 pp while later picks can fill it. |
| `ShortfallReplacementOffset` | .10 | Open slot = baseline − 10 pp when they probably can't. |
| `ExpectedPlayableShare` | .6 | Share of remaining picks assumed to become on-colour playables. |
| `ComfortableSupplyCoverage` | 1.5 | Supply/open ratio above which there is no urgency. |
| `PairTemperature` | .0025 | Softmax temperature over average spell quality (0.25 pp). |

These are heuristic calibration choices, like V1's constants, not fitted values.

**PairTemperature was tightened from 0.5 pp to 0.25 pp during development.** At 0.5 pp, a deck 0.75 pp worse *per card* (about 17 pp in total) still received 14–18% weight. That wrongly diluted on-colour fits, and two pre-written regression tests caught it.

## 4. Why this is strategically appropriate

- **It measures what a drafter asks: "how far is this card above or below what it would replace?"**
  - A card 0.05 pp better than the cut is not materially different from one 0.05 pp worse.
  - A card 3 pp above the cut is a real upgrade.
  - The logistic width (1–2 pp) is comparable to GIH sampling noise (about 0.5–0.7 pp for a 10k-game difference). It also reflects that later picks will still move the cut line, hence wider earlier.
- **"Technically makes the deck" no longer earns a flat 18 points.**
  - In an incomplete shell every on-colour card "made" the V1 deck. Now it is measured against the open-slot value, so chaff stays low while genuine playables keep value.
- **Deck completion urgency is explicit.**
  - With plenty of picks left, a 50% card is below what later picks will provide.
  - When remaining picks probably cannot fill the deck, open slots fall toward baseline − 10 pp, and any real playable becomes worth taking (S21).
- **Needs are measured against the right alternative.**
  - A 2-drop forced in by the early-play floor has no feasible in-role swap, so it is measured against the open-slot value rather than the generic bench.
  - That restored the "needed 2-drop is competitive" behaviour the audit asked for (S5: the 55% 2-drop now edges the 58% 4-drop).
- **Close colour pairs share value instead of winner-take-all.**
  - With two equally good full decks, equal red and white cards are worth the same, and a card's value rises smoothly as it tips one deck ahead (S20).
- **Raw strength and context stay separate.**
  - Q (raw strength) is unchanged.
  - D now cleanly represents marginal deck fit.
  - Off-colour cards still get exactly 0 D.

## 5. Before and after

Previous = frozen pre-fix DLLs; updated = final build. Both run on identical fixtures. Baseline is 56.0% and 10k games unless stated.

| Scenario | Previous score | Updated score | Expected behavior | Result |
|---|---|---|---|---|
| **S18 audit cliff: deep RG at P3P8, candidate 57.00% → 57.05%** | **12 → 31** | **21 → 22** | Negligible rate change, negligible score change | **Fixed** |
| S19 sweep 55.0–60.0% in 0.25 pp steps: largest adjacent change | 19 (at the cut) | 2 | Smooth transition | **Fixed** |
| S19 strong (60%) vs weak (55%) | 35 vs 10 | 34 vs 13 | Still clearly separated | Pass |
| Just below / just above the cut (56.75% / 57.25%) | 12 / 31 | 20 / 23 | Near-equivalent | **Fixed** |
| Test sweep 53–62% in 0.1 pp steps | (step of 19) | every step ≤ 1 point, monotone | Bounded sensitivity | **Fixed** (test) |
| D3: 57.0% candidate, every pool card +0.1 pp | 30 → 12 | 21 → 21 | Tiny pool change, tiny effect | **Fixed** |
| D3: +1 irrelevant off-colour card in the pool | 30 → 30 | 21 → 21 | No effect | Pass |
| S20: two tied full pairs (GR/GW); white card 56.5% → 57.0% | 12 → 30 | 16 → 18 | Smooth as it tips WG ahead | **Fixed** |
| S20: equal 58% red vs white while pairs are tied | 32 vs 32 | 23 vs 23 | Equal, and lower than a sole-pair card (split option value) | Pass |
| Powerful on-colour late (66% green, deep RG) | 42 | 42 | Keeps high score | Pass |
| Powerful off-colour late (67% blue, deep RG) | 21 | 21 | No deck fit; below real upgrades | Pass |
| On-colour 60% upgrade, deep RG | 35 | 34 | Strong | Pass |
| Average 57% sidegrade, deep RG (S3) | 12 | 21 | Marginal: neither a cliff-low nor a "makes the deck" bonus | Pass (by design) |
| Average 55% "24th card", complete deck (S15) | 10 | 11 | Low | Pass |
| Small +1 pp upgrade 59% in 58% deck (S15) | 33 | 28 | Modest, not a flat membership bonus | Pass (intended reduction) |
| Needed 56% 2-drop, deck with zero early plays (D1) | 32 | 31 | Clearly valuable | Pass |
| Needed 55% 2-drop vs 58% 4-drop, zero early plays (S5) | 30 vs **32** | **29** vs 27 | Needed 2-drop competitive | **Improved** |
| Thin deck, 50% chaff, P3P8 with 6 picks left (S3b) | 23 | 7 | Below expected replacement; little value | Changed (intended) |
| Same chaff as picks run out (S21, picks 33 → 41) | 23 at every pick | 7, 7, 20, 23, 23, 23, 23 | Gains value only when the deck may not fill | **Improved** |
| Incomplete shell, 48% chaff at P2P5 (S12) | 10 | 4 | No bonus merely for making a shell | **Improved** |
| Count-driven pair flip, 52% white card (S11) | 24 | 17 | Chaff should not earn full fit by tipping a count tie | **Improved** (flip itself remains) |
| P1P1, empty pool (S1) | 50/50/50/50/40/25/10 | identical | No deck fit; pure quality | Pass (unchanged) |
| Early uncertain commitment, P1P9 (S2) | 45 / 34 / 25 | 45 / 34 / 25 | Stronger off-colour card still preferred | Pass (unchanged) |
| Stage trajectory: average on-colour card at picks 24 → 28 (S13) | 27 → 15 | 26 → 19 | Smaller drop, now caused by the deck genuinely filling | **Improved** |
| Stage trajectory: on-colour 50% card at picks 8–24 (S13) | 9–15 | 8–9 | No membership bonus | **Improved** |
| Real FRA, Codex what-if (Ajani / Chandra / Konstrari / Warlord / Overwrite / Rank Rat) | 46/37/30/28/21/10 | 46/37/30/**25**/21/10 | Ordering preserved; below-baseline Warlord less inflated | Pass |
| Real FRA, unseen pack at P3P5 and P2P5 (all 14 cards each) | — | P3P5 ranks identical; P2P5 has two adjacent swaps between cards ≤ 1 point apart (Fblthp/Lyra, Craterclaw/Multiply by Zero); every score within 1 point | Stable on real data | Pass |

Notable rank changes (others are adjacent swaps between cards within a point):

- S5: the needed 2-drop now edges the 4-drop.
- S7: an on-colour 56% card (19) now ranks above unchanged dual lands (13).
- S3b: the off-colour bomb now ranks above 50% chaff while picks remain.

## 6. Tests

**14 new tests** in `ContextualPickScoreDeckFitTests`. Each states the expected drafting behaviour before asserting it.

1. 57.00% → 57.05% at the audit cut line: ≤ 1 point.
2. ±0.25 pp around the cut: membership 0.3–0.5 / 0.5–0.7, scores within 4.
3. 53–62% in 0.1 pp steps: monotone, each step ≤ 1 point; 60% beats 55% by ≥ 12.
4. Powerful on-colour late: margin > 7 pp, D ≥ 0.7, score ≥ 40.
5. Powerful off-colour late: D = 0; below an on-colour upgrade, above chaff.
6. Needed average 2-drop ≥ 5 points above the same card as a sidegrade.
7. Average card with little marginal value: D ≤ 0.25, score ≤ 20.
8. P1P1: no optimizer work, D weight 0, quality ordering, average card about 25.
9. Early uncertain commitment (P1P9): stronger off-colour card preferred; D moves the score < 1 point.
10. Late established colours: 60% on-colour beats 63% off-colour.
11. Small pool changes (+1 off-colour card ≤ 1 point; whole pool +0.1 pp ≤ 2 points).
12. Two tied full pairs: white-card sweep with steps ≤ 2; equal red and white cards score equally.
13. Deck-completion urgency: chaff gains ≥ 0.4 D as picks run out; a better filler always stays ahead.
14. Determinism.

**Tests 4 and 6 initially failed** against the first implementation (pair temperature 0.5 pp, no option floor). I fixed the *algorithm*, not the tests.

**One existing assertion changed.** `RedundantInferiorCandidateHasNoMarginalMembershipValue` required D to be exactly 0. A card 5.7 pp below the cut now has D ≤ 0.02 (actual 0.005) plus a margin < −5 pp. Its strategic intent is unchanged; the exact zero encoded the removed step function. No other existing test needed changes.

**Results:**

- `dotnet build DraftTG.sln --no-incremental`: **0 warnings, 0 errors**.
- `dotnet test DraftTG.sln --no-build`: **1,071 passed**, 0 failed. Domain 56, Data 160, Application 249, ArenaIntegration 127, RecommendationEngine 203 (189 + 14), App 276.

**Determinism and offline operation:**

- Two consecutive full harness runs produced identical output, and match the comparison tables above.
- The offline FRA tool reports **0 network requests**.
- The engine change adds no I/O, randomness, platform APIs or dependencies, so it is Windows/macOS neutral.

## 7. Performance

There are **no additional optimizer calls**: 12 for FRA and 15 for stress, same as before. The fit uses the decisions the optimizer already produced.

Interleaved A/B: the same tool binary, alternating the old and new engine DLLs, 4 rounds of 20 samples each.

| Workload | Old median (4 rounds) | New median (4 rounds) |
|---|---|---|
| Real FRA pool, 14 candidates | 72.1–73.0 ms | 72.5–77.2 ms |
| All-pairs stress | 201–205 ms | 205–220 ms |

That is roughly +4%: per-pair swap scans over about 40 pool cards × viable pairs × distinct candidates. While validating, I removed one repeated composition count from the swap check. Single unpaired runs on this machine varied by ±15%, so treat these figures as approximate.

## 8. Remaining limitations

1. **The parameters are heuristic:** cut-line width, open-slot offsets, playable share and pair temperature. They should be validated with a replay of completed drafts before tuning.
2. **The pair-viability rule is unchanged.** Incomplete shells still consider only pairs with the maximum eligible count (audit M1). A chaff card can still flip the projected pair (S11); it simply no longer earns full fit for doing so.
3. **Structure and quality-gain terms count only when the card is included.** At the boundary the jump is at most `0.5·0.10·0.40·70 ≈ 1.4` points (structure), and the quality gain is about 0 there. The audit's structural-reason artifacts (unscaled floors, nonland mana term) are untouched.
4. **Land deck fit is unchanged:** 0 / 0.10 / 0.50 + 0.40·shortfall. A selected dual still steps from 0 to 0.10, about 2.8 points. Needed fixing is gated by membership as in V1.
5. **Urgency moves with context.** As remaining picks run out, a chaff card can rise about 13 points over two picks (S21, picks 35 → 37). This is a deliberate response to a real context change, but it is a ramp, not a constant.
6. **The full-shell transition remains (smaller).** When the pool first reaches 23 eligible spells, average cards drop as the cut line appears (S13: 26 → 19). This reflects a real change in the deck.
7. **Margin units differ slightly from quality-gain units.** The margin uses the optimizer's selection value, which includes the existing ≤ 1.5 pp × confidence archetype adjustment when pair data apply (WOE only). The quality gain uses common Phase 8 quality, as in V1.
8. Top-end saturation, the sample-size tie-break and multi-face name joins are unchanged by design. They belong to later phases.

## 9. Concerns for further review

- **The score distribution shifted.** Small upgrades and sidegrades now score lower than V1's flat membership gave them (for example, a +1 pp upgrade 33 → 28; a +3 pp upgrade 35 → 34). Clear upgrades and bombs are essentially unchanged, and chaff in incomplete shells drops sharply. Badge tiers (unchanged) will show more silver and less gold for marginal on-colour cards. Review this before the saturation phase recalibrates the top.
- **"Option floor" for newly viable pairs.** This is a judgment call. The alternative, pure probability weighting, both diluted on-colour bombs and gave weak cards credit for unlocking equal decks.
- **Open-slot urgency** assumes 60% of remaining picks become on-colour playables. Real drafts vary by lane openness; Phase 9B lane evidence could inform this later.
- The S20 behaviour (an equal-pair card about 23 instead of 32) is intentional: the value is split across two plausible decks. Confirm that this matches the desired product behaviour.

Stop here. Phase 2 (saturation/tie-break) and later items have not been started.
