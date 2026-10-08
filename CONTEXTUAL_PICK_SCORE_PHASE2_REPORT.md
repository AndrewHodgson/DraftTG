# Contextual Pick Score — Stabilization Phase 2: Score Saturation and Ranking Accuracy

Date: 2026-10-07. Model version `contextual-pick-score-v1.1` → **`contextual-pick-score-v1.2`**.

**Scope:** saturation, ranking precision and the directly related calibration.

**Unchanged in this phase:**
- Phase 1 graded deck fit;
- the weights, L, A and D;
- the 0–50 range, tier boundaries and badge artwork;
- double-faced card lookups;
- Phase 9E.2B.

Nothing is committed.

Evidence is in `artifacts/pick-score-v1-claude-audit/phase2/` (git-ignored):

| File | Contents |
|---|---|
| `packs-*.txt`, `packs2-*.txt` | 1,000 real-rating replay packs × 2 versions |
| `analyze_packs.py` | Comparator script |
| `fra2-*.txt` | FRA probe |
| `scenarios-v1.1.txt`, `scenarios-v1.2.txt` | All synthetic audit scenarios |
| `ab2-*.json` | Interleaved benchmark |
| `fraprobe-Program.cs` | Replay harness |

---

## 1. Root cause of score saturation

Tracing one candidate through the pipeline. Rows marked **Lost** are where information was discarded.

| Step | v1.1 behaviour | Information lost? |
|---|---|---|
| Raw GIH, sample size | Exact | No |
| Phase 8 adjusted GIH | Shrunk toward the baseline with a 500-game prior; strictly increasing | No (shrinkage is intended uncertainty handling) |
| Format baseline | Games-weighted mean | No |
| **Quality component Q** | `clamp(.5 + (adj − baseline)/.16, 0, 1)` | **Lost.** Every card ≥ baseline + 8 pp gets Q = 1. In FRA that is 7 cards spanning 64.6–71.5% adjusted GIH. |
| Lane L, archetype A | Linear in already-bounded inputs (fit ∈ [−1, 1], affinity ∈ [−1, 1]) | No (the clamps never bind) |
| Deck fit D (Phase 1) | Logistic plus bounded terms | No |
| Weighted sum | Linear | No |
| **Final calibration** | `clamp(.5 + 1.4(w − .5), 0, 1)` | **Lost.** Clips whenever w ≥ .857, e.g. a near-maximum Q plus an open lane or positive archetype. Clips at 0 for w ≤ .143. |
| Integer rounding | `round(50 × value)` | Display only; ranking used the unrounded value |
| Ranking | value ↓, Q ↓, data weight ↓, pack index ↑ | Inherited both losses |
| Tier / top pick | Tier from the integer; top pick from rank 1 | Inherited |

**Saturation happened in both places:**
- **Primarily in Q.** At P1P1 with a neutral lane, Q = 1 gives an unclipped final value of .99, displayed as 50.
- **Also in the final calibration.** It binds as soon as lane or archetype evidence is positive on top of a near-maximal Q, or late with high deck fit.

The 0–50 integer display itself loses only rounding precision, which is unavoidable and acceptable.

## 2. Root cause of the incorrect tie-break

Once two candidates both had Q = 1 and the same L, A and D, their contextual values were exactly equal. The second key (Q) was also equal (1), so the **third key, `StatisticalDataWeight = games/(games + 500)`, decided**.

Data weight is effectively a rarity proxy: mythics and rares have about 2–8k games, commons and uncommons 15–60k. So the more-played, weaker card won.

The tie-break order itself was reasonable. The defect was that clipping *created* ties between materially different cards.

## 3. Implementation changes

| File | Change |
|---|---|
| `src/DraftTG.RecommendationEngine/ContextualPickScoreModels.cs` | New `SoftLimit(value, knee)`. `NormalizeQuality` and `Calibrate` use it. New `QualitySoftKnee = .35` and `CalibrationSoftKnee = .45`. Version v1.2. |
| `src/DraftTG.RecommendationEngine/ContextualPickScoreEngine.cs` | Contributions gain a "Soft limit" entry (actual − linear), so diagnostic points sum to the real value. Comment documenting the ranking keys. The ranking keys are otherwise unchanged. |
| `tests/DraftTG.RecommendationEngine.Tests/ContextualPickScoreRankingTests.cs` | **New**, 12 tests. |
| `tests/DraftTG.Application.Tests/ContextualPickScorePresentationTests.cs` | +1 test (top-pick designation through the presentation pipeline). |
| `tests/DraftTG.RecommendationEngine.Tests/ContextualPickScoreTests.cs` | One assertion block updated (see §6). |
| `ARCHITECTURE.md`, `ROADMAP.md` | Algorithm notes and a phase entry. |

**Soft limit:**

```
SoftLimit(y, k) = y                                             if |y − .5| ≤ k
                = .5 ± (k + h·(1 − exp(−(|y − .5| − k)/h)))      otherwise, h = .5 − k
```

The function is the identity inside the knee and matches its slope at the knee. Beyond the knee it approaches 0 or 1 exponentially, so it is continuous, C¹, strictly increasing and bounded in (0, 1).

- **Q** uses k = .35: unchanged within ±5.6 pp of baseline, which covers most cards.
- **The final value** uses k = .45: unchanged for displayed scores 2.5–47.5.

**Why normalization, not just a new tie-breaker.** Swapping the tie-break key (e.g. ranking by win rate) would have left two cards at an identical 50 while ranking them differently, and it would still lose information whenever L or A differ. It would also invite raw win-rate sorting to override context.

With soft limits, the contextual value itself is strictly increasing in every component. Two consequences:

- Ranking is by contextual value alone for any distinct candidates.
- Ranking can never disagree with the displayed integers: a higher-ranked card never shows a lower score.

**Why bounded rather than linear-unbounded Q.** Diminishing returns are strategically right. An unbounded Q would let a 90% off-colour card late outscore a solid on-colour upgrade; the test `ExtremeScoresStayInRangeOrderedAndCannotOverrideLateContext` checks this.

**Uncertainty and sample size.**
- These are handled only by the existing Phase 8 shrinkage, so there is no second penalty. A 72% mythic over 2,500 games keeps a 69.3% adjusted value and ranks first. A 75% card over 150 games shrinks to about 60% and correctly trails a 62% card measured over 40,000 games.
- Data weight remains a key only for exact ties ("genuinely equivalent"). There it reasonably prefers the better-evidenced card, followed by pack order.

## 4. Internal versus displayed score

- `ContextualValue`, the unrounded value in (0, 1), is the internal ranking value. It is strictly ordered for distinct candidates.
- `Score0To50 = round(50 × ContextualValue)` is unchanged.
- The top-pick border (`IsContextPick`) goes to rank 1. Because ranking and display are monotone together, the highlighted card always has the highest displayed score, sharing it only when two cards round to the same integer.

**Displayed P1P1 score by adjusted GIH above baseline** (neutral lane and archetype; unrounded value in brackets):

| adj − baseline | v1.1 | v1.2 |
|---|---|---|
| −8 pp | 1 | 3 (3.20) |
| −4 pp | 13 | 13 |
| 0 | 25 | 25 |
| +4 pp | 37 | 37 |
| +5.6 pp | 42 | 42 |
| +6.4 pp | 45 | 44 (44.23) |
| +8 pp | 50 | 47 (46.80) |
| +10 pp | 50 | 48 (48.20) |
| +13 pp | 50 | 49 (48.71) |
| +20 pp | 50 | 49 (48.87) |
| +10 pp, open lane (L = .75) | 50 | 50 (49.56) |

**Badge tiers.** The six tiers and their boundaries are **unchanged**. The premium tier (45–50) still starts at about +7 pp early (previously +6.4 pp). 50 is now reserved for exceptional quality *with* supportive context, rather than every card above +8 pp.

**Known display limit.** Above about +10 pp, early integers converge at 48–49. Ranking still separates those cards, but the badge cannot show the difference in whole numbers.

## 5. Before and after examples

| Scenario | v1.1 | v1.2 | Expected | Result |
|---|---|---|---|---|
| **Audit: P1P1 72% mythic (2.5k games) vs 65% uncommon (40k) vs 63.5% common (60k)** | 50 #2 / **50 #1** / 48 #3 | **49 #1** / 48 #2 / 46 #3 | Stronger card first; distinct scores | **Fixed** |
| S1: four former 50s (68% mythic, 66% WU, 65.5% uncommon, 66% WUB) | all 50; ranks 4, 2, 1, 3 (mythic last) | all 48; ranks 1, 2, 3, 4 = adjusted-GIH order | Order by strength | **Fixed** |
| Late P3P8 deep RG: off-colour 72% (2.5k) vs 66% (40k) | Both Q = 1, tied; data weight puts the 66% card above the 72% | 72% above 66%; on-colour 61% upgrade still #1 | Off-colour bombs ordered; deck fit still dominant | **Fixed** (test) |
| 58.00% vs 58.05% (same integer) | Ordered by unrounded value | Same | Stronger first, same display | Unchanged / pass |
| Exact equivalents after shrinkage (75%/500 vs 68.75%/1,000 games, baseline 50%) | Data weight | Data weight (more evidence first) | Deterministic | Pass |
| Small-sample 75%/150 games vs 62%/40k | 62% first | 62% first | Shrinkage prevents noise | Pass |
| P1P4: 68% off-colour vs 60% on-colour | Off-colour first | Off-colour first | Early quality | Pass |
| P3P8: same pair | On-colour first | On-colour first | Late commitment | Pass |
| P3P8: 90% off-colour vs 60% on-colour upgrade | On-colour first | On-colour first | Saturation keeps context decisive | Pass |
| Real FRA (Codex what-if): Ajani / Chandra / Konstrari / Warlord / Overwrite / Rank Rat | 46/37/30/25/21/10 | 46/37/30/25/**20**/10 | Order preserved | Pass |
| Real FRA, realistic late packs (P3P5, P2P5) | Jace 24 #6, 39 #1; Vraska 34 | Jace 23 #6, 38 #1; Vraska 33 | Off-colour bombs slightly lower, ranks identical | Pass |

## 6. Regression tests

New tests in `ContextualPickScoreRankingTests` (12). Each states the expected behaviour before its assertions.

1. Audit case: 72% mythic beats 65% uncommon at P1P1 (rank, value, display).
2. Two cards that both displayed 50 under v1.1: both still premium, distinct values, stronger first.
3. Two cards rounding to the same integer: ranked by unrounded value, independent of pack order.
4. Genuinely equivalent after shrinkage (exactly equal 62.5%): more evidence first; identical twins keep pack order.
5. Small-sample bomb vs large-sample good card, plus a typical-sample mythic vs a large-sample uncommon.
6. Early ranking equals adjusted-GIH order across colours, including above the old +8 pp cap.
7. Late: on-colour upgrade beats the off-colour bombs, and the off-colour bombs are ordered by strength.
8. High-quality off-colour vs strong on-colour: off-colour wins early, on-colour wins late.
9. Extremes (92%/85%, 30%/20%) stay in 0–50 with distinct ordered values; a 90% off-colour card cannot override late context.
10. Sweep 45%–80% in 0.1 pp steps at P1P1: strictly increasing value, each displayed step ≤ 1.
11. Determinism across repeated runs.
12. Contributions plus the soft-limit entry sum exactly to the actual value.

Plus `TopPickDesignationFollowsTheUnroundedContextualWinner` (Application): exactly one highlighted occurrence, the mythic. Its displayed score is ≥ the uncommon's, and the summary names v1.2.

**These tests detect the defect.** Running the same compiled tests against the frozen v1.1 engine DLL, **8 of 12 ranking tests fail**: 1, 2, 5, 6, 7, 9, 10 and 12. The four that pass on both (3, 4, 8, 11) are invariants that v1.2 must preserve. All 14 Phase 1 deck-fit tests pass on both versions.

**Changed existing assertion.** `NormalizationHasExplicitNeutralEndpointsAndClamps` asserted that extremes map *exactly* to 0 and 1, which is the hard clamp this phase removes. It now asserts:
- neutral .5 at baseline;
- extremes within 0.001 of the bounds;
- strict ordering beyond the old clamp points;
- `Calibrate(1) < 1`.

No other existing test changed. The existing contribution-sum test passes unchanged thanks to the soft-limit entry.

**Results:**

- `dotnet build DraftTG.sln --no-incremental`: **0 warnings, 0 errors**.
- `dotnet test DraftTG.sln --no-build`: **1,084 passed**, 0 failed. Domain 56, Data 160, Application 250, ArenaIntegration 127, RecommendationEngine 215, App 276.

## 7. Real draft replay findings

**Data available:**
- The real local FRA Premier ratings cache: 290 rows, 270 rated cards.
- The real saved, completed 42-card FRA pool.

**Not available:**
- A recorded FRA draft with per-pick chronology.
- Any WOE ratings cache, so the saved WOE packs remain unscorable.

That limitation is unchanged from earlier phases. I therefore replayed **deterministic packs of real FRA cards with real rarity slots**:

- 1 rare-or-mythic slot (mythic 1 in 8), 3 uncommons, and 10 commons;
- scored at P1P1 (14 cards, empty pool) and at P1P5, P2P5 and P3P5 (10 cards each), against subsets of the real pool;
- run on both v1.1 and v1.2.

A stress variant adds a second rare/mythic slot.

| Stage | Packs | Top pick changed | v1.1 tied tops | Mean rank correlation | Top picks with < 4k games (v1.1 → v1.2) |
|---|---|---|---|---|---|
| P1P1 | 200 + 200 stress | 0 / 0 | 0 / 0 | 1.0000 / 1.0000 | 16 → 16 / 26 → 26 |
| P1P5 | 100 + 100 | 0 / 0 | 0 / 0 | 1.0000 / 1.0000 | 6 → 6 / 14 → 14 |
| P2P5 | 100 + 100 | 0 / 0 | 0 / 0 | 1.0000 / 1.0000 | 6 → 6 / 9 → 9 |
| P3P5 | 100 + 100 | 0 / 0 | 0 / 0 | 1.0000 / 0.9999 | 5 → 5 / 12 → 12 |

**Interpretation:**

- **On real FRA ratings, the saturation defect almost never changes a real recommendation.** Only 7 FRA cards exceed +8 pp, all rares or mythics, so two of them rarely share a pack. The audit's failure needs two saturated cards (or a saturated card plus a clipped final value) in the same pack.
- **Where it matters, the fix is clear.** In synthetic and edge cases (S1, S14, two late off-colour bombs, multi-rare packs, formats with more cards above +8 pp) v1.2 corrects the order.
- **The only real-data ranking change in 1,000 packs** is a swap between two bottom-of-pack cards (scores 2 and 3, adjusted ~49%). The low-end soft limit narrowed their quality gap enough that a 0.03 deck-fit difference decided. This is harmless.
- **Stronger early picks are no longer demoted.** No v1.1 top pick was a sample-size tie-break victim on this data; the engine tests prove the corrected behaviour.
- **Late recommendations still follow deck construction.** P3P5 ranks are identical, and D values are unchanged.
- **No systematic overvaluation of low-sample cards.** The count of low-sample top picks is identical in every stage.
- **No score inflation.** Tier counts are identical in every stage; only formerly pinned 50s moved (to 45–49).
- **Phase 1 continuity is unchanged.** D values are identical, and all deck-fit tests pass.

## 8. Score distribution changes

**Replay packs**, all cards, tiers 0–9 / 10–19 / 20–29 / 30–39 / 40–44 / 45–50:

| Stage | v1.1 | v1.2 | Cards at 50 | Max score |
|---|---|---|---|---|
| P1P1 | 242 / 1018 / 1203 / 288 / 37 / 12 | identical | 7 → 0 | 50 → 49 |
| P1P5 | 99 / 340 / 419 / 116 / 19 / 7 | identical | 5 → 0 | 50 → 49 |
| P2P5 | 77 / 397 / 477 / 38 / 10 / 1 | identical | 0 → 0 | 47 → 46 |
| P3P5 | 250 / 519 / 183 / 45 / 2 / 1 | identical | 0 → 0 | 45 → 45 |

**Whole FRA card set at P1P1** (270 rated cards, modelled with neutral lane):

| | Tiers | Top 12 displayed |
|---|---|---|
| v1.1 | 23 / 82 / 105 / 40 / 10 / 10 | 50 ×7, 49, 46, 45, 44, 44 |
| v1.2 | 23 / 82 / 105 / 40 / 11 / 9 | 49, 49, 48, 48, 47, 47, 47, 46, 45, 44, 44, 43 |

The maximum change for any card is 3 points.

No recalibration of tiers was needed. One card moved from premium (45) to rich gold (44).

## 9. Performance

Interleaved A/B: the same tool binary with the v1.1 and v1.2 engines, 4 rounds of 20 samples each.

| Workload | v1.1 medians | v1.2 medians |
|---|---|---|
| Real FRA pool, 14 candidates | 74.0 / 76.2 / 75.0 / 75.9 ms | 74.2 / 75.7 / 75.7 / 75.8 ms |
| All-pairs stress | 231.1 / 231.6 / 223.3 / 230.3 ms | 238.0 / 232.6 / 222.1 / 234.0 ms |

There is no measurable difference: the change is two `exp` calls per card. The stress maximums were 240–249 ms on v1.1 and 238–263 ms on v1.2, apart from one v1.2 outlier at 323 ms in a single round. That outlier looks like run noise (GC or other processes).

Absolute stress figures are higher than in Phase 1's session (~211 ms) on both arms, which reflects machine load. Optimizer calls are unchanged (12 / 15). The offline tool reports **0 network requests**.

**Determinism:** the 5,835-line replay re-run is byte-identical, and the determinism test passes. The change uses only `Math.Exp`, `Math.Abs` and `Math.CopySign`: no I/O, no new dependencies, nothing platform-specific.

## 10. Remaining limitations

1. **Display resolution at the top.** Early, cards from about +10 pp to +20 pp all display 48–49. The internal order is correct and the top pick is right, but the badge number cannot show those differences. Showing them would need a non-integer display or a different scale, which is outside this phase.
2. **The knees are heuristic.** They were chosen to leave about 95% of scores exactly as in v1.1. Phase 8's fixed ±8 pp span is still not adapted to each format's spread (audit L4).
3. **Low-end compression lets small context differences decide among very weak cards.** One bottom-of-pack swap in the replay; it is harmless.
4. **Sample size enters only through Phase 8 shrinkage** (500-game prior, not tuned here) and the exact-tie key. There is no explicit uncertainty band.
5. **Real chronological draft replay is still unavailable** (no recorded FRA pick history; no WOE ratings). The replay uses real ratings and pool with sampled packs.
6. **Unaddressed by design:** the multi-face 17Lands name join and estimate marker, structural reason artifacts, count-driven incomplete-shell pair viability, and land deck fit.

## 11. Readiness for Phase 3

**Ready.** All completion criteria are met:

| Criterion | Status |
|---|---|
| 72% vs 65% ranking failure | Corrected |
| Equal-display cards keep internal ordering | Yes |
| Sample size only breaks exact ties | Yes |
| Highlight goes to the true contextual winner | Yes |
| 0–50 scale and tiers | Intact, distribution essentially unchanged |
| Phase 1 tests | Pass |
| Build, tests, determinism and offline checks | Pass |

**Recommended Phase 3 candidate:** the multi-face 17Lands name join plus a visible estimate marker. That is the remaining High-severity audit item, and it affects about 7% of FRA cards in every pack. Waiting for instruction; Phase 3 has not been started.
