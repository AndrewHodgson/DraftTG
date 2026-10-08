# Contextual Pick Score — Phase 4: Deck Need, Card Roles and Marginal Deck Improvement

Date: 2026-10-08. Model version `contextual-pick-score-v1.3` → **`contextual-pick-score-v1.4`**.

**Scope:** only D (deck need).

**Unchanged:**
- Q, L and A;
- Phase 8 shrinkage;
- the v1.2 soft limits;
- the stage weights;
- the Phase 3 statistical identity;
- the deck-builder selection algorithm;
- localization, placement, WGC and the sort comparator;
- Phase 10D;
- badge visuals and tier bands.

Nothing is committed.

Evidence is in `artifacts/pick-score-v1-claude-audit/phase4/` (git-ignored):

| File | Contents |
|---|---|
| `roles-v14.txt` | FRA role audit |
| `role-prototype.py` | Prototype used to design and review the rules |
| `pool4-*.txt` | Saved-pool replay |
| `p4-multi-*.txt` | 1,000-pack replay |
| `scenarios-v1.3/4.txt` | Synthetic audit scenarios |
| `ab4-*.json` | Benchmark |
| `fraprobe-Program.cs`, `*.py` | Harness |

---

## 1. D before this phase (v1.3), audited from code

**Nonland candidates.** For every viable projected after-pair *p*:

```
m_p  = logistic(margin / s)                                  margin = candidate value − projected cut line (phase 1)
D_p  = clamp( m_p·(0.65 + 0.10·structure_p) + 0.25·clamp(ΔavgDeckGIH / .01, 0, 1), 0, 1 )   (0 if off-pair)
D    = Σ_p softmax_p(avg quality / 0.25 pp) · D_p             (+ option floor for newly viable pairs)
```

**Lands:** `0` if not selected; `.10` if selected; `.50 + .40·shortfallReduction` if it reduces a shortfall.

| Signal | Measures | Range | Problem found |
|---|---|---|---|
| membership `m` | Probability of making the deck (cut-line margin, 1–2 pp logistic) | 0–1 | Sound (phase 1) |
| `0.65` | Value of making the deck | 0–.65 | Cut-line card = .33, so a sidegrade is *penalised* about 6 points late (not neutral) |
| `0.25·qualityGain` | Average deck GIH delta | 0–.25 | **Double counts the margin** (Δavg ≈ margin/23) |
| `structure` (×0.10) | max(creature, early, top-end reduction, mana) | 0–1 | **Unscaled floors** (14/16/4/6) applied to incomplete shells (spurious "short creature floor"); **mana term rewarded nonland spells** for basic-allocation rounding; only reductions of top-end, no redundancy; max effect about 2.8 points; gated by inclusion |
| off-pair | — | 0 | Full penalty; no sideboard/future value |
| lands | Selected/shortfall | 0–.9 | Binary |
| removal / roles | — | — | Not modelled |

## 2. New D (v1.4)

Few orthogonal terms, centrally configured in `ContextualPickScoreConfiguration`:

```
F_p  = f + (1 − f)·( ½·m_p + ½·u_p )                       f = DeckFitFloor = 0.10
       m_p = logistic(margin / s)                           membership: "is it played?" (s = 1–2 pp, phase 1)
       u_p = logistic(margin / (margin > 0 ? 0.04 : s))     upgrade: "how much better than what it replaces?"
S_p  = max(creatureNeed, earlyNeed, removalNeed) − topEndRedundancy        (each 0–1, deficit-relative)
D_p  = clamp( F_p + 0.30 · c · played_p · S_p , 0, 1 )      c = structure confidence, played = m_p (1 if floor-forced)
D    = Σ_p π_p·D_p   (unchanged phase 1 softmax blend and option floor)
```

**What the terms mean.** D ≈ **0.55 at the cut line** (neutral). It falls to the 0.10 floor for cards that cannot be played, and approaches 1 for large upgrades that also fill a need.

**Why these terms do not double count:**

- **F alone carries "makes the deck" and "replacement value."** Both come from the same cut-line margin, just at two scales: membership saturates within about 2 pp; the upgrade term keeps growing to about 10 pp.
- **The separate average-quality gain is removed.**
- **Below the cut, both scales are narrow,** so an excluded card falls quickly to the floor. Both scales meet at margin 0, so F stays continuous (the phase 1 guarantee).
- **S takes the strongest single need,** so correlated needs (creature and 2-drop) never stack. Redundancy is subtracted.
- **S is realised only as far as the card is likely to be played,** and shrunk by confidence. That keeps it from re-crediting quality.
- **Floor-forced cards:** a 2-drop the builder's early-play floor forces in has no feasible swap. Its F compares its quality with the best bench card, and its need is credited once through S at full `played`. v1.3 measured it against the open-slot value **and** added structure, so it counted twice.

**Lands:**

| Case | D |
|---|---|
| Selected and reduces a measured shortfall | `0.55 + 0.35·reduction` |
| Otherwise (unselected, or only a basic substitute) | floor 0.10 |

**Removed:** the nonland mana-shortfall term (the audit's M2 artifact), the unscaled floors, and the binary structure gate.

### Structure needs (projected deck before the pick, best pair, scaled to 23 spells)

| Need | Applies to | Formula | Targets (existing builder config) |
|---|---|---|---|
| creature | candidate is a creature | `clamp((16 − creatures·k)/4, 0, 1)` | Preferred 16, Minimum 14 (14 creatures → .5; 11 → 1) |
| early | nonland MV ≤ 2 | `clamp((4 − early·k)/4, 0, 1)` | MinimumEarlyPlays 4 |
| removal | reliable removal role | `weight·clamp((3 − removal·k)/3, 0, 1)` | **HealthyRemovalCount 3** (new, conservative, *not* data-derived); hard = 1, conditional = .5 |
| top-end redundancy | MV ≥ 5 | `clamp((high·k − 5)/3, 0, 1)` subtracted | MaximumHighCostCards 6 (6 → .33, 8 → 1) |

`k = 23 / projectedSpells` scales incomplete shells.

**Structure confidence** is `c = clamp(projectedSpells / 18, 0, 1)`. It is pool-based, not progress-based, so there is no second progress penalty. The outer stage weights already set D's influence; a 9-spell shell gets 0.5.

**Explanations.** Expanded diagnostics now read, for example:

```
Deck need: strong (D 0.85)
  + Makes the projected deck: clears the cut line by 3.1 pp.
  + Replaces Chandra's Emberling in the projected 23.
  + Raises creatures from 15 to 16 (preferred 16).
  - Projected deck already has 7 cards at MV 5+ (cap 6).
```

Other reason types:
- `+ Required by the projected deck's creature/early-play floor.`
- `+ Projected deck has only 1 removal spell(s).`
- `- Does not make the current best build (2.3 pp below the cut line).`
- `- Selected only as a basic substitute: no measured colour shortfall to fix, so no fixing value.`

Labels: strong ≥ .65, weak ≤ .40, neutral otherwise. The badge is unchanged (score, GIH, ALSA, EST).

**CandidateDeckImpact** additions (init-only, provider-neutral):
- `DisplacedCard`;
- `ProjectedStructure(Spells, Creatures, EarlyPlays, HighCost, Removal)`;
- `StructureConfidence`, `CreatureNeed`, `EarlyPlayNeed`, `RemovalNeed`, `TopEndRedundancy`.

Existing fields such as `CommonDeckQualityDelta` and before/after pair and counts are kept; `StructuralGain` now holds the signed S.

## 3. Card roles

`LimitedCardRoleClassifier` (RecommendationEngine). Intrinsic roles are parsed **once per immutable `Card`** (`ConditionalWeakTable`): 290 FRA cards take 18–29 ms the first time and 0.07 ms when cached.

| Role | Status | Source |
|---|---|---|
| Creature, EarlyPlay (nonland MV ≤ 2), Land | Reliable now | Structured types / mana value |
| Fixing | Reliable now, **lands only** | Structured `produced_mana` with ≥ 2 colours |
| HardRemoval / ConditionalRemoval | Reliable now (precision-first) | High-confidence Oracle rules |
| Ramp, nonland fixing | **Deferred** | FRA audit: `produced_mana` flags Emrakul (13-drop), Koth (landfall) and Gideon's Memorial (planeswalker-only mana) |
| Tempo (bounce), fight, sweepers, CombatTrick, CardAdvantage, Synergy, Finisher | **Deferred / needs override** | Uncertain semantics |

**Removal rules.** Lower-cased, with `−` normalised. Text is taken from castable faces only: all faces of Adventure/Prepare/modal/split cards, the front face otherwise. It is split into clauses, and clauses targeting "you control" (without "opponent") are skipped.

| Rule | Pattern | Result |
|---|---|---|
| Destroy/exile | `destroy/exile [up to one] target … creature / nonland permanent` (not "card", not "noncreature", not flicker) | Hard; **conditional** if qualified (an adjective before the noun, or a trailing "with…"/"that…") |
| Damage | `deals N damage to target creature / any target` | Hard if N ≥ 3 or X and unqualified; conditional if N = 2 or qualified; nothing at 1 |
| Shrink | `target creature gets −N/−N` | Hard if N ≥ 3; conditional if 2 |
| Bite | "deals damage equal to its power to target creature" against an opponent | Conditional |
| Pacifism | an Enchantment with "enchanted creature can't attack or block" | Conditional |

**Overrides.** `LimitedCardRoleOverrideTable` holds `Add`/`Remove` keyed by `CardIdentifier` or `SET#collector`; removals always win. Every role reports its source (`structured`, `high-confidence Oracle rule`, `explicit override`, `removed by override`). FRA needed **no overrides**; the two false-positive patterns found in the audit were fixed in the generic rules.

## 4. FRA role audit (290 17Lands-relevant cards, production catalog)

| Role | Count |
|---|---|
| Creature | 161 |
| EarlyPlay | 109 |
| Land | 21 |
| Fixing | 16 |
| Ramp | 0 (deferred) |
| **HardRemoval** | **18** |
| **ConditionalRemoval** | **12** |

**Hard removal (18):**
- Ajani's Anguish, Awaken the Inferno, Chandra Torch of Defiance (−3), Craftwork Crusher (ETB mode), Extended Absence, Fulminous Forte;
- Last Gasp, Lich's Relic, Memory Trap, No Admittance, Prophesied End, Silence the Echo;
- Stingerquill Charm, Twisted Fates, Violent Echoes, Vraska's Final Mercy, Warrior's Blades, Wrath of the Bloodmane.

**Conditional removal (12):**

| Card | Restriction |
|---|---|
| Compel Brutality | bite |
| Essence Burn | black or green |
| Gideon's Memorial | attacking/blocking |
| Konstrari Charm | flying |
| Refute Destiny | green or blue |
| Solitary Cell | MV ≤ 3 |
| Sureshot Sower | flying |
| Surgical Precision | toughness ≥ 4 |
| Terminal Criticism | blue or red |
| Tether Technician | 2 damage |
| Theorix Charm | −2/−2 |
| Your Fate Ends Here | MV ≥ 3 |

**Fixing (16 lands):** the Commons ×5, the Annexes ×5, Deserted Beach, Haunted Ridge, Overgrown Farmland, Rockfall Vale, Room of Refuge, Shipwreck Marsh.

**Reviewed and corrected during the audit** (generic rule fixes, not per-card overrides):
- *Archive Arbiter* ("destroy target **noncreature**, nonland permanent") was a false positive and is now excluded.
- Konstrari Charm, Essence Burn, Gideon's Memorial, Refute Destiny and Terminal Criticism were over-classified as hard; they are now conditional.

**Deliberately unclassified (precision first):**
- **Fight:** Mind Meanderer, Yoshimaru, Vigorbloom Charm.
- **Sweepers:** Overwrite the Multiverse, Austere Command, Kindred Judgment, Rise of the Deathbringer, Echoverse Fulcrum.
- **Bounce:** Unsummon, Diviner's prepared spell.
- **Temporary or conditional exile:** Vindictive Triumph, Vraska's land-gated destroy.
- **Other:** Hapatra (−1/−1 counters), Garruk +2 (−4/−1).

Removal enters scoring only through `RemovalNeed`, after this audit.

## 5. Successful 6+ win / trophy data (Phase 9D) — actually available

| Question | Answer |
|---|---|
| Full successful-deck card lists? | The domain model supports them (`SuccessfulDeckSample` maindeck/pool with counts, verified build schema). **No corpus exists locally for any set.** |
| Colour-pair classification? | Yes, in the model (`SuccessfulDeckKey`). |
| Creature count, curve, early plays, removal and fixing derivable? | Structurally yes, given card lists plus the catalog and these roles. |
| Sets/formats with data? | **None.** The 17Lands trophy API envelope restricts outside use; the adapter fails closed before fetching builds. `limited-data/17lands/` contains only `FRA_PremierDraft_ALL_TIME_v3.json` (card ratings). |
| FRA? | **No.** |

**Decision:** no `SuccessfulDeckStructureProfile` was built. It would be infrastructure with no data and no testable value. V1 uses the existing deck-builder constraints plus one documented removal target. Trophy frequency remains unused (the Phase 9D denominator decision is preserved).

**Future data-driven replacement:** if a permitted source appears, per set/format/pair medians and percentiles with a minimum sample threshold should replace the constants above, falling back to these generic targets below the threshold.

**Archetype-specific targets** (§23) are likewise deferred: no trustworthy set profile carries structural targets.

## 6. Colour-pair changes

These are unchanged from phases 1–2:
- D compares the realistic projected builds before and after, through the softmax blend over viable pairs.
- A newly viable pair is valued by its whole-deck gain over today's best deck.
- Late absurd pivots are not rewarded, because a pivot must produce a viable 23-spell build that beats the current best. Q, L, A and colour commitment are untouched.

## 7. Tests

**43 new test cases in 22 methods.** Each states the expected behaviour first and compares cards with identical Q/L/A.

**`ContextualPickScoreDeckNeedTests` (16 methods), covering scenarios A–O plus confidence:**

| Scenario | Expected behaviour |
|---|---|
| A, B | 2-drop vs 3-drop when the deck is short of early plays / when early plays are healthy |
| C, D | Creature shortage / healthy creature count |
| E, F | Average 6-drop reduced; strong 6-drop still competitive |
| G, H | Removal shortage / healthy removal |
| I | Makes the deck: D > .7 |
| J | Replacing a 48% 23rd card is worth more than replacing a 56% one; the displaced card is reported |
| K | Never makes the deck: about the floor, below neutral |
| L | Needed RG fixer > .55 |
| M | Unneeded WU fixer: floor |
| N | P1P3: no structural influence |
| O | The same deficit matters more late than mid-draft |
| — | Confidence scaling |

**`LimitedCardRoleClassifierTests` (5 methods, 26 cases):**
- a 22-case precision theory: hard, conditional and qualified rules, the unicode minus, and the guards for noncreature, flicker, graveyard, bounce, fight, sweeper and pump wording;
- pacifism on enchantments only;
- castable faces (Prepare/Adventure count, Transform back face does not);
- fixing for lands only and ramp deferred;
- overrides by identifier and printing, with removal winning and the source reported.

**`ContextualPickScorePresentationTests.LateDiagnosticsExplainDeckNeedWithLabelAndSignedReasons`** (Application).

**Existing assertions updated** (same intent, expressed on the new scale):

| Test | Old | New | Why |
|---|---|---|---|
| Off-colour D | Exactly 0 | `DeckFitFloor` (3 tests) | Sideboard floor (§29) |
| Off-colour late score range | 10–21 | 10–25 | +2.8 from the floor |
| Crowded top-end `StructuralGain` | 0 | < 0 | Redundancy is now explicit |
| Redundant inferior card | ≤ .02 | Floor (+.02) | Floor |
| Sidegrade D | .2–.45 | .45–.6 | Neutral moved from .33 to .55 |
| "Little marginal value" card | D ≤ .25, score ≤ 20 | D ≤ .45, score ≤ 23 | Neutral moved from .33 to .55 |
| Two version strings | v1.3 | v1.4 | Version bump |

**Algorithm fixes that tests and replays revealed** (I changed the code, not the tests):
1. The wide replacement logistic also applied below the cut, so a card 5.7 pp under the cut kept D 0.19. Now asymmetric.
2. Floor-forced cards were credited twice (open-slot margin plus structure), giving 39 for an average needed 2-drop. Now quality versus the bench, plus need once (35).
3. Selected non-fixing duals got a neutral 0.5 and scored like playables (an off-pair GW dual at 24 in RG; found by the saved-pool replay). Now the floor.

Five scenario failures during development were **fixture bugs**:
- off-colour cards formed a viable white-red deck;
- a "weak 23rd card" that was actually benched;
- a hand-counted shell size.

They were corrected in the fixtures. No new assertion was loosened.

**Results:**

- `dotnet build DraftTG.sln --no-incremental`: **0 warnings, 0 errors**.
- `dotnet test DraftTG.sln --no-build`: **1,157 passed**, 0 failed. Domain 56, Data 160, Application 272, ArenaIntegration 127, RecommendationEngine 261, App 281.
- **Deterministic:** the 5,835-line pack replay re-run is identical.
- **Q and estimate status** are identical between v1.3 and v1.4 on all 5,800 replayed cards.

## 8. Replays (v1.3 → v1.4)

### FRA sampled packs

The same seeds and multiface-inclusive sampler as Phase 3, with real ratings.

| Stage | Top-pick changes | Cards with a rank change | Score changes | Tiers 0–9/10–19/20–29/30–39/40–44/45–50 |
|---|---|---|---|---|
| P1P1 (200) | 0 | 0 | none (D weight 0) | unchanged |
| P1P5 (100) | 0 | 0 | none | unchanged |
| P2P5 (100) | 4 | 136 / 1,000 | 0 to +2 | 93/389/471/41/6/0 → 87/332/494/81/6/0 |
| P3P5 (100) | 2 | 248 / 1,000 | +0 to +6 | 232/541/165/55/6/1 → 117/547/183/135/7/11 |

**Cause of the late shift (expected from the specified neutral ≈ 0.5):**

| Card group | Change |
|---|---|
| Off-colour | +2/+3 (floor 0 → 0.10) |
| On-colour cards that make the deck | +5/+6 (a cut-line card moved from D .33 to .55; clear upgrades from about .7 to about .9) |
| Non-fixing lands | 0 |

- **Ordering is largely preserved.** The on-colour vs off-colour gap widened by about 3 points late.
- **The 11 premium P3P5 cards** are genuinely strong on-colour cards: Ajani 49, Garruk Curse Breaker 48, Craftwork Crusher ×4 46, Kiora ×3 45, Hungering Puppetbeast ×2 45.
- **Both late top-pick changes are sensible:**
  - Tether Technician (on-colour conditional removal) over Kindred Judgment (off-colour sweeper);
  - Skilled Battlecarver (on-colour creature that makes the deck) over Sphinx of False Conclusions (off-colour).
- **Tier bands were not changed.** The late upward shift should be reviewed in a calibration phase; see §10.

### Saved FRA pool replay

Real 42-card pool, subsets, and real unseen FRA cards; full table in `pool4-*.txt`.

| State | Example | v1.3 → v1.4 | Why |
|---|---|---|---|
| P3P13, full pool (projected 15 creatures, 9 early, 3 MV5+, 3 removal) | Craterclaw Colossus (7-mana creature) | 30 → 38 (rank 5 → 3) | Makes the deck, replaces Chandra's Emberling, creatures 15 → 16 |
| P3P13 | Sureshot Sower (54.3%) | 25 → 32 | Clears the cut by 1.9 pp, creature need |
| P3P13 | The Theorist, Jace Beleren (off-colour UU bomb) | 21 → 24 (rank 10 → 11) | Cannot enter; floor |
| P3P13 | Rockfall Vale / Overgrown Farmland (duals) | 12 / 10 → 12 / 10 | Basic substitutes, no shortfall |
| P2P8, 18-card shell | Rockfall Vale | 27 → 27 (D .90) | Reduces a measured shortfall: fixing credited |
| P2P8 | Fblthp / Vinelasher | 31 → 33 / 26 → 28 | Creature need at 83% confidence (15-spell shell) |
| Any | Removal candidates (Wrath of the Bloodmane, Fulminous Forte) | +5 to +6, no removal bonus | The pool already projects 3 removal spells, so no need |

### Synthetic audit scenarios

Full output in `scenarios-*.txt`.

| Scenario | v1.3 → v1.4 | Assessment |
|---|---|---|
| Removal-free deck: removal vs vanilla | 29 / 29 → 34 / 34 | The old S4 fixture uses MV2 cards with no Oracle text, so no role was detected; G/H cover removal explicitly |
| Zero-early-play deck: 55% 2-drop vs 58% 4-drop (S5) | 29 vs 27 → **25 vs 32** | **Flagged.** v1.3's 2-drop lead came from the double count fixed here. With need credited once (max 0.30 × 0.4 × 70 ≈ 8 points), a 3 pp quality gap still wins. An equal-quality needed 2-drop scores 35 against 28 for a sidegrade (scenario A / D1). |
| Excess top end: 60% 6-drop vs 57% 3-drop | 34 / 32 → 31 / 28 | Redundancy reduces the 6-drop; quality still keeps it ahead |
| Cut-line sweep (S18) | Continuous 21 → 22 … → 28 → 28 | Phase 1 continuity preserved |
| Thin deck late, chaff vs off-colour bomb | Chaff 11 < bomb 25 with picks left; chaff 28 > bomb 24 when picks run out | Phase 1 urgency preserved |

## 9. Performance

Interleaved v1.3 vs v1.4 tool binaries, 4 rounds of 20 samples each.

| Workload | v1.3 median | v1.4 median | v1.4 p95 | v1.4 worst | Optimizer calls / pack |
|---|---|---|---|---|---|
| FRA pool, 14 candidates | 70.8–73.3 ms | 71.3–73.3 ms | 97–113 ms | 113.6 ms | 12 (unchanged) |
| All-pairs stress | 218–231 ms | 217–221 ms | — | — | 15 (unchanged) |

- **No additional deck-builder evaluations.** Structure needs and removal counts reuse the projections and decisions already built.
- **Role classification** runs once per card; the cached cost is negligible.
- **No network** in scoring; the offline tool reports 0 requests.
- **Platform:** no platform APIs and no dependencies were added.

## 10. Limitations and concerns

1. **Late score inflation.** Making the cut-line card neutral (D .55) and adding the 0.10 floor shift late scores up:
   - +5/+6 for on-colour cards, +2/+3 for off-colour cards;
   - P3P5 gold 55 → 135 and premium 1 → 11 per 1,000.

   Ranking is mostly preserved. A calibration phase should decide whether to recentre the final calibration or the tiers. Phase 4 deliberately did not change tiers.
2. **Constants are heuristic:**
   - the generic targets come from the existing builder;
   - `HealthyRemovalCount = 3` and the 0.30 structure weight are conservative V1 choices, not fitted.

   There is no successful-deck data to calibrate them (§5).
3. **Zero-early-play judgment call (S5).** A needed but 3 pp weaker 2-drop now trails the stronger 4-drop. This is defensible, but intentionally reported.
4. **Fixing is credited only through the measured source shortfall of a two-colour projection.**
   - Splash-enabling fixing is deferred, because the builder has no splash support.
   - Nonland fixing and ramp are deferred (unreliable structured data).
5. **Removal recall is intentionally limited.** Fight, sweepers, bounce, conditional or temporary exile and counters are unclassified. FRA has 30 recognised removal cards; real interaction is higher.
6. **The early-play definition is unchanged** (any nonland MV ≤ 2, including tricks), for consistency with the builder floor. A creature-only curve view is deferred.
7. **Archetype-specific structure targets** are deferred (no data).
8. **Existing limits** remain: incomplete-shell pair viability is count-driven (M1); the land path is unchanged apart from the floor; the chronological real-draft replay is still unavailable.

Phase 4 is complete. Phase 5 has not been started.
