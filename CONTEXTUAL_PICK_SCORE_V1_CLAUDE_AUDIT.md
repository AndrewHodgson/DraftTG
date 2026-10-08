# Contextual Pick Score V1 — Independent Audit

Audit date: 2026-10-07. Scope: the uncommitted Contextual Pick Score V1 working tree on top of `97b2719`. This was an audit only: no production source, test or asset was modified.

**Evidence labels used throughout**

| Label | Meaning |
|---|---|
| **FACT** | Read in the code, or measured in this audit with the harness below. |
| **INFERENCE** | Follows from facts, but was not measured directly. |
| **HYPOTHESIS** | Plausible, not demonstrated. |
| **RECOMMENDATION** | A proposed change. |

**Audit harness**

All of it is isolated and git-ignored, under `artifacts/pick-score-v1-claude-audit/`:

- `scorelab/`: 18 controlled synthetic draft states.
  - It runs the real Phase 8 → 9A → 9B → 9C → Pick Score pipeline.
  - Baseline is 56.0%; every candidate and pool card has 10,000 games unless stated.
- `fraprobe/`: the real local FRA Premier cache and the saved 42-card FRA pool, with extra diagnostics and more realistic packs.
- `scenarios-output.txt`, `fra-probe-output.txt` and `codex-tool-repro.json`: raw outputs.

Both harnesses reference the DLLs built by `tools/DraftTG.PickScoreAudit`. They use the same offline caches as Codex's tool, and the network handler recorded 0 requests.

---

## A. Executive Assessment

**Verdict: a solid, honest foundation, but not yet trustworthy as the headline number on the badge.**

Pick Score V1 is well built as software:

- deterministic and offline;
- immutable, with no mutation of the real pool;
- fenced against stale generations and identity mismatches;
- explainable component by component;
- fast enough for live drafting.

Codex's report is unusually candid about its own limits, and most of its factual claims check out.

The problems are in calibration and sensitivity, not crashes. Three behaviours would visibly mislead a real drafter:

1. **Late in the draft, "makes the projected deck" is an on/off switch worth about 18 points** (more than a third of the 0–50 scale).
   - A raw GIH change of 0.05 percentage points moved a card from **12 to 31**.
   - The same average on-colour card falls from 27 to 15 as the pool crosses the 23-spell threshold. (FACT, scenarios S13 and S18.)
2. **The quality component tops out at baseline + 8pp, and ties are then broken by sample size.**
   - In FRA, seven cards between 64.6% and 71.5% adjusted GIH all receive the maximum quality value.
   - At P1P1 a 72% mythic ranked **below** a 65% uncommon, because the uncommon has more games. (FACT, S1 and S14.)
3. **Every double-faced or adventure card in FRA (21 of the 290 17Lands rows) loses its statistics.**
   - The production ratings mapper joins on the full name ("Hallway Heckler // Vicious Verse"), while 17Lands publishes the front face ("Hallway Heckler").
   - Those cards show a neutral **25** with no "estimated" marker on the badge, where the true early-draft equivalent ranges from 7 to 37. This is a pre-existing Phase 8 gap that V1 now puts front and centre. (FACT.)

Strategically, the model behaves well at the extremes:

- early picks follow card quality;
- late picks prefer on-colour playables that make the deck over unplayable off-colour bombs, which is the right call for Overwrite the Multiverse.

It is weakest in the middle of the draft, where good drafters reason about a primary colour plus candidate second colours. V1 instead projects a single best pair and gives everything outside it zero deck value. In the incomplete-shell regime it picks that pair mostly by card count rather than card quality.

**Readiness:**

- **Use it as an advisory signal now.**
- **Fix the three quick items before treating the score as authoritative:** the top-end tie-break, double-faced card statistics, and the spurious structural reasons.
- **Smooth the deck-membership step before relying on late pack 2 and pack 3 scores.**

No Critical issues were found.

---

## B. Verified Implementation

| Codex claim | Status | Evidence |
|---|---|---|
| Build: 0 warnings, 0 errors | **Verified** | `dotnet build DraftTG.sln --no-incremental`: 0 warnings, 0 errors. |
| 1,057 tests pass | **Verified** | Domain 56, RecommendationEngine 189, Application 249, Data 160, ArenaIntegration 127, App 276 = 1,057; 0 failed. |
| 30 new tests | **Partially verified** | 26 in `ContextualPickScoreTests.cs` and 3 in `ContextualPickScorePresentationTests.cs` are confirmed. The tier-boundary test sits in `MysticRatingBackgroundTests.cs`, which is a wholly new file (12 tests, shared with the earlier badge work), so it cannot be isolated from git history. |
| 14-card scoring, 73 ms median | **Verified (approx.)** | Re-run: median 78.6 ms, p95 99.5 ms, max 100.0 ms, 12 optimizer calls. |
| Stress 206 ms median / 237 ms max | **Verified (approx.)** | Re-run: median 209.0 ms, p95 226.2 ms, max **252.0 ms**, 15 calls. The maximum is 15 ms above the claim; this is ordinary run-to-run noise. |
| Offline audit: 0 network requests | **Verified** | The tool's handler counted 0. Code inspection: `ContextualPickScoreEngine` and `CandidateDeckImpactEvaluator` do no I/O. |
| Printed rarity has no scoring influence | **Verified for direct use** | No `CardRarity` reference in the engine or presentation; the engine test swaps rarity. *Indirect*: GIH sample size (rarity-correlated) drives Phase 8 shrinkage, which is appropriate, and the rank tie-break (issue H2), which is not. |
| FRA scores 46/37/30/28/21/10 | **Verified exactly** | Ranks 1/2/4/7/11/14 also reproduced (`codex-tool-repro.json`). |
| Weights sum to 1; D weight is zero through P1P5 | **Verified** | Code and test. D becomes non-zero (0.0015) at P1P6. |
| Q reuses the Phase 8 adjusted GIH; no second shrinkage | **Verified** | `ContextualPickScoreEngine.cs:41`. |
| Quick Draft L is half the human deviation | **Verified** | `ContextualPickScoreEngine.cs:43` with the 9B bot maximum. |
| ALSA/ATA are not quality inputs | **Verified** | ALSA is used only as Phase 9B's lateness reference, `(pick − ALSA)/3`. |
| Unsettled or missing pair affinity stays neutral | **Verified** | `ContextualPickScoreEngine.cs:44`; tests. |
| Structural gains are "deficit reductions, not blanket bonuses" | **Contradicted in part** | In incomplete shells the floors are compared unscaled, and nonland spells receive a "mana shortfall" gain from basic-land rounding (issue M2). |
| "Fixing receives a contribution only when … reducing a quantified shortfall" | **Contradicted in part** | The same mana term applies to nonland spells: a minor-colour creature "reduces the shortfall" (S11). |
| Estimated scores are identified | **Partially verified** | The status panel and diagnostics do identify them. `IsPickScoreEstimate` exists (`CurrentPackCardPresentation.cs:70`) but is **not bound** in any view, so the badge shows a plain number. |
| Animation reads only the score, not rarity | **Verified** | `MysticBadgeTierPalette.Tier(int?)`, `MysticRatingBackground.cs:32`. |
| Badge animation CPU about 0.31% of the machine | **Not re-measured** | Only the saved artifacts were reviewed (`performance.json`, preview PNGs). |
| WOE saved packs are unavailable | **Verified** | No WOE QuickDraft cache, hence no baseline, hence a dash. |

**Caveat on Codex's FRA evidence (FACT):**

- The "pack" is the pool's own top 14 cards, added again as extra copies, at **P3P14**. A pack at the final pick has one card, so this state cannot occur.
- Because every strong card appears "late" at pick 14, Phase 9B's lateness signal saturates and inflates L for those colours (L = 0.58 for red).
- Two of the leaders are second copies of *legendary* planeswalkers, whose duplicate value the model does not discount.

The reproduction is exact. It is still weak evidence of real pick quality.

---

## C. Mathematical Audit

### C1. The formula as implemented (FACT)

| Term | Definition |
|---|---|
| `Q` | `clamp(0.5 + (adjGIH − baseline)/0.16, 0, 1)` |
| `L` | `clamp(0.5 + 0.5·laneAdj/0.020, 0, 1)` |
| `A` | `clamp(0.5 + 0.5·affinity·pairConfidence, 0, 1)` |
| `D` | `0` if the card is not in the projected maindeck; otherwise `0.65 + 0.25·min(1, ΔavgDeckGIH/0.01) + 0.10·structure` |
| `D` for lands | `0.50 + 0.40·shortfallReduction`, or `0.10` |
| `Score` | `round(50·clamp(0.5 + 1.4·(Σ wᵢ·cᵢ − 0.5), 0, 1))` |

Weights are (Q, L, A, D): 0.70 / 0.20 / 0.10 / 0 early, then 0.50 / 0.20 / 0.15 / 0.15 at pick 21, then 0.30 / 0.10 / 0.20 / 0.40 from pick 35. Adjacent anchors are blended with smoothstep.

**Correctness of the arithmetic (FACT):**

- Every value is bounded and finite.
- Contributions are `70·w·(c − 0.5)` and sum exactly to the unclamped value.
- Weights sum to 1 (checked to 1e-12).
- Results are deterministic: no wall-clock input, and every tie-break is ordinal.

**Within its own definitions, the arithmetic is correct.**

### C2. Sensitivity: what one percentage point of GIH buys

FACT: derived from the constants and confirmed by the harness.

| Stage (completed picks) | Score points per +1pp adjusted GIH | Score points for "makes the deck" (D: 0 → 0.65) | That step in GIH terms |
|---|---|---|---|
| 0–4 (P1P1–P1P5) | 3.06 | 0 | 0pp |
| 14 (P2P1) | 2.51 | 4.3 | 1.7pp |
| 21 (P2P8) | 2.19 | 6.8 | 3.1pp |
| 28 (P3P1) | 1.75 | 12.5 | 7.1pp |
| 35+ (P3P8 onward) | 1.31 | 18.2 | **13.9pp** |

INFERENCE: from P3P8 onward, an on-colour card that just makes the deck at baseline − 6.5pp outranks *any* off-colour bomb. For late pack 3 that is broadly correct. The problem is that the on/off step is not graded.

### C3. Saturation and compression

**FACT:** Q saturates at ±8pp around the baseline.

**FACT (real FRA cache):**

- Baseline 56.47%; standard deviation of adjusted GIH 3.6pp.
- 7 of 270 rated cards sit at Q = 1, and 10 at Q ≥ 0.9.
- The cards at Q = 1 span 64.6% to 71.5% adjusted GIH. Ajani (71.5%) and Sphinx of False Conclusions (64.7%) are indistinguishable.

**FACT:** the rank tie-break after Q is `StatisticalDataWeight`, i.e. `games/(games+500)` (`ContextualPickScoreEngine.cs:71-72`). Rares and mythics have 2,500–8,000 games; commons and uncommons have 15,000–60,000. So among saturated cards the **lower-quality, higher-sample card wins**:

| Scenario | Higher-GIH card | Ranked first instead | Why |
|---|---|---|---|
| S1 | mythic 68% (66.3% adjusted, 3k games): #4 | uncommon 65.5% (30k games): #1 | Both have score 50 |
| S14 | mythic 72% (69.3% adjusted): #2 | uncommon 65%: #1 | Same tie-break |

INFERENCE: any small L or A difference also decides between saturated cards. A lane nudge of ±0.05 outweighs a 7pp quality gap once both are clamped.

**FACT:** the lower clamp is rarely reached (2 FRA cards at Q = 0).

**Compression late:**

- A is neutral (0.5) wherever no pair dataset exists. That is every format except WOE's descriptive profile, and FRA has none. A still holds 20% of the weight late, compressing scores toward 25. The 1.4 gain only partly compensates.
- **FACT (S13):** on-colour bombs late score 43–46 and never 50.
- **INFERENCE:** the premium tier (45–50) means "+6.4pp card" early but "on-colour bomb that also improves the deck" late. The tier labels in Codex's report are presented as stage-invariant; they are not.

### C4. Discontinuities (FACT)

1. **Membership cliff** (`CandidateDeckImpactEvaluator.cs:45, 58-61`).
   - In a deep RG pool with 27 playables at 57.0%, a candidate at 57.00% scores 12, at 57.05% scores 31 (S18).
   - That is a 19-point jump from 0.05pp, roughly a tenth of the standard error of a 10,000-game GIH.
   - Whether a card exactly at the cut line is "in" is decided by the optimizer's bitmask tie-break.
2. **Full-shell threshold.** The same average on-colour card scores 27 at pick 24 and 15 at pick 28 as the pool reaches 23 eligible spells (S13).
3. **Pair viability threshold** (`CandidateDeckImpactEvaluator.cs:89, 100`).
   - Only pairs with the *maximum* eligible count are viable for an incomplete shell.
   - Adding any card of a tied colour therefore makes its pair the only viable one: a 52% white card "changes the strongest viable projected pair" to WG and receives D = 0.75 (S11).
   - At the 22 → 23 boundary, one red card unlocked RG and received D = 0.90, the whole pivot's value (S16).

### C5. Double counting

| Pairing | Assessment |
|---|---|
| **Q and D's quality gain** | Mild. The gain is (candidate − replaced)/23 pp, scaled by 0.01. Ajani reached 0.83 because it replaced a 52% card; typical on-colour upgrades reach 0.05–0.3. At most about 7 points late, usually 1–3. Acceptable as a marginal-value refinement. |
| **Q and A** | A is defined as lift *after* subtracting overall quality, so it is not a direct double count. But the relative weight of a percentage point of pair lift versus overall GIH moves with stage: about 0.5× mid-draft and 1.33× late (A: 0.20·12.5·conf versus Q: 0.30·6.25). The noisier signal (pair data, 300-game prior) is weighted *more* than overall GIH late. INFERENCE: unintended. |
| **Structure and membership** | The builder enforces floors, so a needed 2-drop makes the deck partly *because* it is needed, then also receives structure credit. Small, and largely intended. |
| **Mana term for nonland spells** | Defect (M2). It rewards colour-ratio rounding, not fixing. |

### C6. Is 0–50 an effective scale?

Yes, keep it.

- Resolution is fine: about 1 point per 0.33pp early.
- Integers are readable, and the six tiers are coarse enough.

What hurts communication is the *stage-dependence* and the *cliffs*, not the scale.

How well it separates pick quality:

| Category | Separation |
|---|---|
| Excellent vs good | Clear early (50 vs 37–42); compressed late (43–46 vs 35–38). |
| Playable vs poor | Clear early; bimodal late (in-deck 23–35 vs out 4–15). |
| Speculative pick | Not represented. There is no notion of "keeps options open." |

---

## D. MTG Strategy Audit

| # | Principle | V1 behaviour | Assessment |
|---|---|---|---|
| 1 | Raw card strength | Phase 8 shrunk GIH, dominant early | Good; saturation above +8pp (H2). |
| 2 | GIH and other 17Lands data | GIH only; IWD, OH WR and GP WR deliberately unused | Reasonable and avoids double counting. GIH's known biases (gold cards, lands, archetype strength) are not corrected. FRA: two-colour cards average +1.2pp over mono, lands −1.4pp. That pattern is *consistent with* selection bias but does not prove it. |
| 3 | ALSA's role | Only as the lateness expectation in lane detection | Correct role. |
| 4 | Colour commitment | Mid and late: a single projected pair, and D = 0 outside it | Too binary. A second colour with 5 cards is treated like a colour with 0 (S10, S12). Phase 9A fit is used only in the fallback. |
| 5 | Draft stage | Smoothstep weights | Sound in shape. Scale meaning drifts (C3). |
| 6 | Fixing and splashing | No splash; duals get D = 0.10 | Gap. A U/G dual for a held blue bomb scores like an on-colour RG dual: 13 and 13 (S7). Documented by Codex. |
| 7 | Mana curve | Early-play floor (MV ≤ 2), high-cost cap | Present but tiny: at most about 2.8 points late. A deck with *zero* ≤2-drops still prefers a 58% four-drop over a 55% two-drop (S5). |
| 8 | Creature/spell balance | Creature floor | Present but small. Mid-draft it fires spuriously (M2). |
| 9 | Removal | Not modelled (no reliable roles) | Gap. Equal-GIH removal and a vanilla creature score identically even in a removal-free deck (S4). GIH already captures most removal value. |
| 10 | Archetype synergy | Pair-lift A with active-pair confidence | Directionally right (S8: the gap shrank from 10 to 3 points). Only WOE has data, so it is inert elsewhere. |
| 11–14 | Roles, build-arounds, mechanics, multi-role cards | Foundation only | Honest omission; nothing is faked. Fine for V1. |
| 15 | Deck consistency | Two-colour projection; pip demand diagnostic | No castability penalty early: WUB 67% and WWUU 67% score 50 at P1P2, above a 62% mono card at 42 (S9). |
| 16 | Speculative early picks | D weight is 0 early, so quality is pure | Good flexibility. No penalty for colour-hungry cards (see 15). |
| 17 | Late-draft completion | Thin pool: every on-colour card makes the deck; even 50% chaff (23) edges an off-colour bomb (21) | Correct (S3). |
| 18 | Situational vs broad | Not modelled | Acceptable for V1. |

**How recommendations change across the draft (FACT, S13):**

| Candidate | Score trajectory | Assessment |
|---|---|---|
| Off-colour 67% bomb | 50 → 48 (P1P9) → 42 (P2P1) → 37 (P2P8) → 29 → 21 (P3P8+) | Sensible decline. |
| On-colour 60% card | 37 throughout | Stable. |
| On-colour average card | 25 → 27, then falls to 15 at pick 28 | The pick-28 drop is the H1 cliff. |

The middle stage is where V1 deviates most from strong drafting: there is no credit for a plausible second colour, and the pair is chosen by count.

**Overwrite the Multiverse (FACT, then INFERENCE).**

- It is `{4}{B}{B}`, 64.2% adjusted. The saved pool's deck is clearly RG: 4 black cards plus 2 WB cards out of 42.
- Q = 0.984, but D = 0 because a BB six-drop cannot enter an RG maindeck. That removes the 0.40 D weight, giving 21.
- The legacy Phase 9B #3 ranking came from a colour adjustment capped at 2.5pp.
- INFERENCE: late in pack 3 with this pool, Overwrite is not playable. A double-black cost also rules out a splash. The drop is **justified**. Hate-drafting value in a human pod is real but small and subjective.

---

## E. Counterfactual Test Results

Scenario verdicts:

- **PASS**: matches sound strategy.
- **SUBJ**: a defensible difference of opinion.
- **GAP**: a documented, unmodelled feature.
- **DEFECT**: behaviour that contradicts the model's own intent or sound strategy.

| ID | Scenario | Expected | Actual (score / rank) | Verdict |
|---|---|---|---|---|
| S1 | P1P1, empty pool | Rank by quality; distinguish bombs; no-stats card flagged | 68% mythic, 65.5% uncommon, WU gold 66%, WUB 66% all **50**. Ranks: uncommon #1, mythic #4. 61% → 40, 56% → 25, 51% → 10. No-stats card → 25 (estimated, unflagged on badge). | **DEFECT** (H2); GAP (M3) |
| S2 | P1P4 and P1P9, three or eight green picks, blue 63% | Blue 63% over green 59% | 45 vs 34 at both stages | PASS |
| S3a | P3P8, deep RG (27 playables at 57%) | Upgrade > others; sidegrades low | On-colour 60% → 35; off-colour bomb → 21; on-colour 57% → **12** (sidegrade, out) | PASS, with the H1 cliff |
| S3b | P3P8, thin RG (21 spells) | Any playable over an off-colour bomb | 60% → 37, 57% → 32, 54% → 28, 50% → 23; bomb → 21 | PASS |
| S4 | Removal-free RG, P3P3 | Removal modestly preferred | Removal 32 = vanilla 32 | GAP |
| S5 | Zero ≤2-drops, P3P5 | 2-drop competitive with a slightly better 4-drop | 4-drop 58% → 32 over 2-drop 55% → 30 (structure +2.6 pts) | SUBJ / underweighted (M6) |
| S6 | 10 MV5+ cards | Top-end upgrade OK if it replaces top-end | 6-drop 60% → 35 > 3-drop 57% → 33 (replaces weaker top-end under the cap) | PASS |
| S7 | RG holding a blue bomb; U/G dual | U/G dual > on-colour dual | Both 13; on-colour average 12 | GAP (M8) |
| S8 | Synergy card: 53% overall / 61% in RG, conf 0.87 | Close to, or above, a generic 58% (56% in RG) | 25 vs 28 (without pair data: 21 vs 31) | SUBJ: right direction, conservative |
| S9 | Hard-to-cast cards | Early: small discount for 3-colour/WWUU. P2P8 committed RG: on-colour 58% ≥ off-pair WWUU | P1P2: WWUU 50, WUB 50, mono 62% 42. P2P8: WWUU **37** > on-colour 31 | **DEFECT/GAP** (M3, M4) |
| S10 | P2P8, five 58% cards | On-colour ≈ gold ≈ colourless > second colour W > unplayed B | 32 / 32 / 32 / **24 / 24** | GAP (M4) |
| S11 | P3P2, G primary, R and W tied | Pair driven by quality; chaff doesn't pivot | Every W card (even 52%) flips RG → WG and gets D = 0.75, structure 1.0 | **DEFECT** (M1, M2) |
| S12 | P2P5 incomplete shell | Chaff low; good third colour > unplayed colour | Chaff 10 (D = 0.70); W 62% = U 62% = 33 | PASS ranking; GAP (M4) |
| S13 | Same six cards at 14 stages | Smooth evolution | See section D; cliff at pick 28; late bombs cap at 43–45 | PASS overall; H1, M7 |
| S14 | 72% mythic vs 65% uncommon, same colour, P1P1 | Mythic first | Uncommon #1, mythic #2, both 50 | **DEFECT** (H2) |
| S15 | P3P9, complete deck | Upgrade > off-colour bomb > deep second colour > 24th card | 33 > 21 > 17 > 10 | PASS |
| S17 | 9C active pair GW, projected deck RG | Consistent explanation | W card: A = 0.886 ("performs well in active pair") **and** D = 0 ("likely sideboard-only") | **DEFECT** (M5) |
| S18 | Membership sweep | Smooth | 57.00% → 12, 57.05% → **31** | **DEFECT** (H1) |
| FRA-R1 | Real unseen FRA pack, P3P5, 32-card RG subset | On-colour upgrades over off-colour bombs | On-colour 56–58% → 27–32; Jace 69% (UU) → 24 | PASS |
| FRA-R2 | Same pack, P2P5, 18-card subset | Jace competitive mid-draft | Jace 39 #1; Vraska 34; but Craterclaw Colossus (7 mana, RRR) gets "structure 1.0" | PASS ranking; **DEFECT** in reason (M2) |

Genuine algorithm defects: S1/S14, S11, S17, S18 and the FRA-R2 reasons. The rest are reasonable differences of opinion or documented gaps.

---

## F. Identified Issues

### Critical

None. No crash, determinism failure, mutation of the real pool, network use, identity leak or stale publication was found.

### High

**H1. Binary deck membership dominates the late score and creates cliffs.**

- **Where:** `CandidateDeckImpactEvaluator.cs:45, 58-61`; `ContextualPickScoreModels.cs:41, 47-49`.
- **What:** D jumps 0 → ≥0.65, worth 18.2 points from P3P8 onward. A 0.05pp GIH change moved a card 12 → 31 across two badge tiers (S18).
- **More examples:** the pool-size threshold (S13: 27 → 15); count-driven pair flips (S11).
- **Effect:** scores near the cut line are noise-dominated, and two near-identical cards can land in different tiers.

**H2. Quality saturation, then a sample-size tie-break, mis-ranks top cards.**

- **Where:** `ContextualPickScoreModels.cs:57`; `ContextualPickScoreEngine.cs:71-72`.
- **What:** everything ≥ baseline + 8pp shares Q = 1. That is 7 FRA cards spanning 6.9pp.
- **Effect:** ties go to higher `DataWeight`, i.e. commons and uncommons over rares and mythics. P1P1 ranked a 72% mythic below a 65% uncommon (S14), and a 66.3% mythic 4th of four 50s (S1). This affects the single most consequential decision (P1 picks), and the "Context Pick" border marks the wrong card.

**H3. Double-faced, adventure and split cards get no 17Lands statistics in the production ratings path.**

- **Where:** `LimitedStatisticsMapper.cs:62` (participants) and `:26` (environment) join `card.Name` (Scryfall "A // B") exactly to the provider name (front face).
- **Scale:** all 21 unresolved FRA rows (7% of the set) are front faces of multi-face cards.
- **Effect on the score:** these cards show a neutral 25, while their true P1P1-equivalent ranges from 7 (Whiplash Wordsmith) to 37 (Diviner of Victory).
- **Effect on the badge:** `IsPickScoreEstimate` (`CurrentPackCardPresentation.cs:70`) is not bound, so the badge looks measured.
- **Effect on deck projections:** the saved FRA pool's 3× Hallway Heckler (52.8%) and Pyre Rhymer (51.2%) are valued at the 56.5% neutral prior.
- **Fix pattern already exists:** the trophy client canonicalizes front-face names (`DraftTGRuntime.cs:61`).
- Pre-existing in Phase 8; amplified by V1.

### Medium

**M1. Incomplete-shell pair selection is count-driven.**

- **Where:** `CandidateDeckImpactEvaluator.cs:89, 100`.
- **What:** viability requires the *maximum* eligible count, so mid-draft D effectively asks "is this card in the colours with the most cards?" The candidate itself tips ties (S11).

**M2. Spurious structural gains and reasons.**

- **(a) Unscaled floors.** Evaluator lines 52-55 compare `before` against the *unscaled* `_deck.MinimumCreatures` (14), `PreferredCreatures` (16), `MinimumEarlyPlays` (4) and `MaximumHighCostCards` (6), although incomplete shells use scaled limits (lines 92-98). Every creature in a 16–21-card shell "helps a short creature floor."
- **(b) Mana term for nonland spells.** The term (lines 50, 56, 125-126) rewards changes in basic-land rounding. With six minimum basics per colour, any unbalanced deck carries a "shortfall", so adding a minor-colour spell "reduces a known mana-source shortfall."
- **Impact:** ≤ about 2.8 points, but the *explanations* shown to the user are wrong. On real data, a 7-mana RRR creature got the full structural gain at P2P5.

**M3. No castability or flexibility discount early.**

- **What:** 3-colour and double-double-pip cards with high GIH reach 50 at P1P2 (S9).
- **Why it matters (INFERENCE / domain knowledge):** GIH for gold cards is measured only in decks that can cast them. The FRA averages (two-colour +1.2pp over mono) are consistent with that bias.

**M4. No second-colour credit mid-draft.**

- A colour with 5 drafted cards and a colour with 0 receive identical scores (S10, S12).
- A committed RG drafter at P2P8 is told to take WWUU 67% (37) over an on-colour 58% (31).
- Phase 9A colour fit is computed but only used in the fallback.

**M5. Two notions of "the pair" disagree.**

- Phase 9C's active pair comes from colour counts; the deck projection's pair comes from average quality.
- Result: A rewards cards that D says won't make the deck, with contradictory reasons side by side (S17).
- Separately, A's effective weight relative to Q flips from about 0.5× (mid) to 1.33× (late).

**M6. Curve and creature needs are underweighted relative to strategy.**

- The structure term is capped at 0.10 within D, so at most about 2.8 points late (S5).
- This is a calibration choice; flagged because the documentation says these needs are modelled.

**M7. Stage-dependent meaning of tiers, and neutral components still weighted.**

- Late on-colour bombs cap at about 42–46. A and L at a neutral 0.5 still hold 30% of the late weight.
- Codex's interpretation bands ("45–50 exceptional") are stage-invariant and should not be presented that way.

**M8. Fixing and splash undervalued** (documented limitation).

- A selected dual land gets D = 0.10, a score of about 11–13, which falls in the band labelled "unlikely to make the deck" even though the land is in the deck.
- A U/G dual for a held blue bomb equals an on-colour RG dual (S7).

**M9. The test suite encodes the calibration rather than strategy.**

- Engine tests mostly assert score ranges (e.g. `InRange(…, 10, 21)`).
- None covers: two saturated cards, a membership near-tie, multi-face statistics joins, the second colour versus an unplayed colour, or explanation consistency.

### Low

- **L1. No cancellation into scoring.** `LimitedStatisticsUpdate.cs:60-62` passes no cancellation token into `Recommend`. Stale work runs to completion (≤ about 250 ms), and publication is correctly fenced.
- **L2. Wasted optimizer work.** The optimizer runs from P1P6 when the D weight is 0.0015–0.02: real cost, negligible effect.
- **L3. Legendary duplicates.** Second copies of legendary cards are not discounted (relevant to FRA's legend-heavy set and to Codex's own what-if).
- **L4. Fixed quality span.** The 16pp span ignores format spread (FRA standard deviation 3.6pp). Formats with tighter spreads will compress toward 25.
- **L5. Rounding at the floor.** A weighted value of 0.01 displays as 1 (0.5 rounds away from zero). Cosmetic.
- **L6. ALL_TIME window.** The ratings window is `ALL_TIME`, and early-format data can differ from the current metagame. Pre-existing; not a V1 change.

---

## G. Recommended Improvements

Ordered by benefit relative to effort. All of these keep the system offline, deterministic, cross-platform and free of new dependencies.

| Priority | RECOMMENDATION | Expected benefit | Difficulty / performance |
|---|---|---|---|
| **1** | **Graded membership.** Replace the 0/0.65 step with a margin-based value, e.g. `D_member = 0.65·σ((s_cand − s_cut)/τ)`. `s_cut` is the strength of the weakest included spell (or the best excluded spell when the candidate is in); `τ` is about 1pp or the pooled GIH standard error. Keep the quality and structure terms. | Removes the 12 ↔ 31 cliffs; near-ties score near-equally; robust to GIH noise. | Low–medium. Uses projections already computed; no extra optimizer calls. |
| **2** | **Top-end ranking.** Tie-break on adjusted GIH (or unclamped Q) *before* `DataWeight`. Optionally replace the hard clamp with soft saturation, e.g. a logistic with the same midpoint, so 65% vs 72% still differ by a few points. | Fixes P1 mis-ranking; restores separation among bombs. | Low. |
| **3** | **Multi-face statistics join + visible estimate.** Join the ratings mapper (participants and environment) on front-face names, using the trophy client's validated pattern. Bind `IsPickScoreEstimate` to a small "est." or `~` marker on the badge. | Correct scores for about 7% of FRA cards; honest display. | Low. |
| 4 | **Structural correctness.** Compare against the scaled shell configuration. Drop the mana term for nonland spells, or compute the shortfall without the basic-floor rounding artifact. | Correct reasons; removes spurious points. | Low. |
| 5 | **Soft pair model.** Compute D as an expectation over the top 2–3 viable pairs weighted by a stage-dependent softmax of projected quality. In incomplete shells, rank pairs by quality-weighted evidence instead of requiring the maximum count. | Second-colour credit mid-draft (M4); kills count-driven flips (M1); smoother commitment. | Medium. Same projections (all 10 pairs are already built when viable). |
| 6 | **One pair for A and D.** Use the projection pair for A, or suppress A when the two pairs disagree. | Consistent explanations (M5). | Low. |
| 7 | **Early castability factor.** A small penalty on the Q deviation for 3+ colours or heavy off-pair pips, fading out as commitment rises. | Addresses gold-card GIH bias and early flexibility (M3). | Low, but needs calibration evidence (item 8). |
| 8 | **Offline evaluation harness before any weight tuning.** Replay the user's recorded Player.log drafts. Measure (a) the share of top-scored picks that reach the final maindeck, (b) score stability under ±0.5pp bootstrap GIH noise, and (c) explanation consistency. | Converts heuristic weights into evidence-based ones; guards future changes. | Medium. No runtime cost. |
| 9 | Per-format quality scaling (environment standard deviation or percentile) | Comparable scores across formats (L4). | Low–medium; verify with item 8 first. |
| 10 | Splash support (single-pip off-colour bombs + demonstrated fixing) | Better fixing and splash decisions (M8). | Medium–high; later. |
| 11 | Curated removal and role profiles per set | Removal needs (S4). | Medium, and needs maintained data. Only with item 8 evidence. |

**Not recommended now:**

- Oracle-text semantic classifiers;
- mechanic-synergy engines;
- trophy-frequency scoring;
- learned or ML weights;
- dynamic per-pick re-weighting beyond the current smoothstep.

Without the item 8 evaluation harness there is no evidence any of these would improve picks, and each adds substantial complexity.

**On the badge:** don't redesign. Revisit tier boundaries only after items 1–3, because they change the score distribution.

---

## H. Performance and Reliability

**Latency (FACT).**

| Workload | Median | p95 | Max | Optimizer calls |
|---|---|---|---|---|
| Real FRA, 14 candidates | 78.6 ms | 99.5 ms | 100.0 ms | 12 |
| Stress | 209 ms | 226 ms | 252 ms | 15 |
| Scenario packs | 5–30 ms warm | — | — | — |

The first optimizer use in a process costs about 70 ms of JIT warm-up. **Adequate for live drafts**: Arena's pick timer and pack animation are seconds long.

**Threading (FACT).**

- Scoring runs in the coordinator's `Task.Run` worker, outside the gate.
- Publication is fenced by generation, revision and snapshot reference.
- The engine and evaluator are created per update, with no shared mutable state, so they are thread-safe.
- The interim update (without scores) is not published, so badges never flash "—" before scores arrive.

**Allocation and caching.**

- **FACT:** projections are cached per evaluation, keyed on the eligible multiset, and duplicates are memoized.
- **INFERENCE:** the bounded DP state count keeps allocation modest. There is no cross-update cache, which is correct for safety.
- **FACT:** Phase 8–9C run twice per pack (interim plus final). That is cheap.

**Determinism (FACT).**

- Repeat runs gave identical output.
- Tie-breaks are ordinal.
- No timing enters any score.

**Offline and missing data.**

- **FACT:** no baseline → dash. Missing stats → estimate (flag not shown on the badge). Missing metadata → colour-fit fallback with a reason.
- **FACT:** no new network calls.

**Cross-platform.**

- **FACT:** the engine is pure .NET with no OS APIs.
- **INFERENCE:** the badge background uses only Avalonia primitives, so it should behave identically on macOS. Not run on macOS in this audit.

**Concerns (both minor):**

- L1: no cancellation into scoring.
- L2: optimizer work at negligible D weight.

---

## I. Scoring System Verdict

**1. Is Contextual Pick Score V1 mathematically sound?**

Sound in construction, unsound in sensitivity. The components are well defined, bounded and correctly summed. But:

- a hard clamp erases the top of the quality range;
- a step function dominates late scores;
- a count-based viability rule chooses mid-draft pairs;
- two structural terms are computed against the wrong reference.

**2. Does it meaningfully improve draft recommendations?**

| Stage | Compared with Phase 8/9 | Notes |
|---|---|---|
| Late draft | **Yes** | Correctly demotes unplayable off-colour bombs (Overwrite) and rewards real on-colour upgrades. |
| Early draft | No improvement | It *is* Phase 8 with a worse tie-break (H2). |
| Mid draft | Mixed | No second-colour reasoning (M1, M4). |

**3. Is it consistent with strong Limited strategy?**

Directionally: quality early, deck fit late. It departs from strong play in four ways:

- binary single-pair commitment from pack 2;
- no castability discount;
- curve needs barely matter;
- splashes and fixing are ignored.

**4. Are the current weights justified?**

Not empirically. Codex labels them heuristic, which is honest. Internally, they make "makes the deck" worth about 14pp of GIH late, and pair lift outweigh overall GIH late. Both should be validated by the evaluation harness (G-8) before tuning.

**5. Is the system over-engineered or underdeveloped?**

Over-engineered in mechanism, underdeveloped in calibration. A full per-candidate deck optimization across ten pairs feeds what is essentially a yes/no flag plus a small modulation. The same machinery could deliver a graded margin and a multi-pair expectation at no extra cost.

**6. Is it ready for real-world use?**

As an advisory number, yes, with the caveats above. As the authoritative headline badge, not until:

- H2 and H3 are fixed (both small);
- the M2 reasons are corrected;
- H1 is smoothed. Until then, late-draft scores near the deck cut line are noise-sensitive.

**7. Three most important next improvements:**

1. **Graded deck-membership value** (G-1, ideally with the soft pair expectation G-5), to remove the late cliffs and count-driven pair flips.
2. **Top-end ranking fix** (G-2): tie-break on adjusted GIH, optionally soft saturation, so P1 picks rank bombs correctly.
3. **Multi-face statistics join plus a visible estimate marker** (G-3), so about 7% of FRA cards stop showing a fabricated-looking 25, together with the structural-reason corrections (G-4).

---

*Phase 9E.2B was not started. No production code was modified. Harness sources and raw outputs are in `artifacts/pick-score-v1-claude-audit/` (git-ignored).*
