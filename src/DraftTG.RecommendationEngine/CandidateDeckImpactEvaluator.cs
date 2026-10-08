using System.Collections.Frozen;
using System.Diagnostics;
using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

/// <summary>Per-pack immutable counterfactuals using the unchanged Phase 10 optimizer. No I/O.</summary>
public sealed class CandidateDeckImpactEvaluator(ContextualPickScoreConfiguration? configuration = null,
    BaselineDeckConfiguration? deckConfiguration = null, bool reuseEquivalentPairProjections = true,
    LimitedCardRoleClassifier? roles = null)
{
    private readonly ContextualPickScoreConfiguration _score = configuration ?? new();
    private readonly BaselineDeckConfiguration _deck = deckConfiguration ?? new();
    private readonly LimitedCardRoleClassifier _roles = roles ?? new();
    public int OptimizerCalls { get; private set; }
    public double ElapsedMilliseconds { get; private set; }
    private sealed record Projection(ArchetypeColorPair Pair, IReadOnlyDictionary<CardIdentifier, int> Main,
        double? Quality, int Spells, int Creatures, int Early, int High, double ManaShortfall, int Relaxations, string ConstraintSignature,
        IReadOnlyList<DeckCardDecision> Decisions, DeckCompositionDiagnostics Composition, int Shortfall = 0);
    private sealed record Need(double Creature, double Early, double Removal, double Redundancy, double Signed, double Confidence,
        ProjectedDeckStructure Structure);
    private sealed record PairFit(double Value, double? Margin, double Membership, Need? Need = null, CardIdentifier? Displaced = null, bool Entered = false, bool Forced = false,
        double? Real = null, bool Virtual = false, double Gate = 0, double? Replacement = null);

    /// <param name="environmentBaseline">Phase 8 environment baseline; derived from the inputs when omitted.</param>
    public IReadOnlyList<CandidateDeckImpact> Evaluate(DraftSnapshot snapshot, DeckBuildInput input,
        ColorCommitmentProfile commitment, CancellationToken token = default, double? environmentBaseline = null)
    {
        OptimizerCalls = 0;
        var clock = Stopwatch.StartNew();
        // Cache is scoped to this immutable statistics/configuration snapshot, never across drafts.
        var projections = new Dictionary<string, Projection>(StringComparer.Ordinal);
        var impacts = new Dictionary<CardIdentifier, CandidateDeckImpact>();
        var befores = Project(input);
        var before = befores.FirstOrDefault();
        // Same stage definition as the score engine: completed picks, or the coordinate when history is partial.
        var position = snapshot.CurrentPack.Position;
        var picks = Math.Max(snapshot.DraftedPool.Count, (position.Pack.Value - 1) * _score.PicksPerPack + position.Pick.Value - 1);
        var scale = _score.CutlineScaleAt((double)picks / _score.ExpectedDraftPicks);
        var baseline = environmentBaseline ?? new StatisticalRecommendationEngine()
            .Recommend(snapshot.CurrentPack, input.Statistics, input.EnvironmentStatistics).EnvironmentBaseline;
        // Phase 6: later picks contest the open slots and every maindeck spell clearly below what a typical later pick
        // supplies. The virtual cut line is the expected marginal card of the final deck without this candidate.
        var open = Math.Max(0, _deck.TargetNonlandCount - (before?.Spells ?? 0));
        var remaining = Math.Max(0, _score.ExpectedDraftPicks - picks - 1);
        var supply = remaining * _score.ExpectedPlayableShare;
        var weak = WeakSpells(before);
        var contested = open + weak.Length;
        var coverage = supply / Math.Max(1, contested);
        var replacement = Replacement(before);
        var urgency = _score.SupplyUrgency(coverage);
        foreach (var id in snapshot.CurrentPack.AvailableCardIdentifiers.Distinct())
        {
            token.ThrowIfCancellationRequested();
            var card = input.Catalog.Find(id);
            if (card is null || !card.GameplayMetadata.ColorsKnown || !card.GameplayMetadata.CardTypes.IsKnown
                || card.GameplayMetadata.IsNonlandSpell && !DeckPlanSelector.HasSpellMetadata(card))
            {
                impacts[id] = Unavailable(card is null ? 0 : commitment.Fit(card.Colors), "Projected deck unavailable for this candidate's missing metadata.");
                continue;
            }
            var hypothetical = new DraftPoolSnapshot(new(input.Pool.Inventory.CardIdentifiers.Append(id)),
                input.Pool.Completeness, input.Pool.UnresolvedOccurrenceCount);
            var afters = Project(input with { Pool = hypothetical });
            var after = afters.FirstOrDefault();
            if (after is null)
            {
                impacts[id] = Unavailable(commitment.Fit(card.Colors), "No eligible projected spell shell; color-fit fallback only.");
                continue;
            }
            var makes = after.Main.GetValueOrDefault(id) > (before?.Main.GetValueOrDefault(id) ?? 0);
            var delta = after.Quality is { } quality && before?.Quality is { } oldQuality ? quality - oldQuality : (double?)null;
            var mana = before is null ? 0 : before.ManaShortfall - after.ManaShortfall;
            double need; PairFit? best = null; var bestWeight = 1d;
            var playability = card.GameplayMetadata.IsLand ? 1 : Playability(card, afters);
            var fits = Array.Empty<PairFit>();
            if (card.GameplayMetadata.IsLand)
                // Fixing earns value only by reducing a measured colour-source shortfall of the projected mana base.
                // A selected land that fixes nothing only replaces a basic, so it keeps the floor like any non-improving card.
                need = makes && mana > 0 ? _score.LandFixingBase + _score.LandFixingWeight * Math.Clamp(mana, 0, 1) : _score.DeckFitFloor;
            else
            {
                // Graded fit in every viable pair, blended by how plausible each pair is after this pick.
                fits = afters.Select(p => Fit(card, p, befores.FirstOrDefault(b => b.Pair == p.Pair) ?? before)).ToArray();
                var weights = PairWeights(afters);
                // A pair this candidate newly makes viable is an extra option: it may add value but never
                // dilutes the card below its fit in decks that already existed (max keeps continuity).
                var existing = fits.Where((_, i) => befores.Any(b => b.Pair == afters[i].Pair)).Select(f => f.Value).DefaultIfEmpty(_score.DeckFitFloor).Max();
                need = Math.Clamp(fits.Select((f, i) => (befores.Any(b => b.Pair == afters[i].Pair) ? f.Value : Math.Max(f.Value, existing))
                    * weights[i]).Sum(), 0, 1);
                best = fits[0]; bestWeight = weights[0];
            }
            var replaced = before?.Main.Where(e => e.Value > after.Main.GetValueOrDefault(e.Key))
                .OrderBy(e => e.Key.Value, StringComparer.Ordinal).Select(e => (CardIdentifier?)e.Key).FirstOrDefault();
            var reasons = new List<string>();
            if (card.GameplayMetadata.IsLand)
                reasons.Add(!makes ? "- Not selected for the projected mana base." : mana > 0 ? "+ Reduces a projected colour-source shortfall (fixing)."
                    : "- Selected only as a basic substitute: no measured colour shortfall to fix, so no fixing value.");
            else if (best is not { Entered: true }) reasons.Add("- Cannot enter a realistic projected maindeck (off-colour or blocked by deck constraints).");
            else if (best.Margin is { } margin)
            {
                if (best.Forced) reasons.Add(Invariant($"· Quality versus the best bench card: {margin * 100:+0.0;-0.0;0.0} pp."));
                else if (best.Virtual && best.Real is null)
                {
                    var build = before is not null && after.Pair != before.Pair ? Invariant($" in the projected {after.Pair.Code} build") : "";
                    reasons.Add(margin >= 0 ? Invariant($"+ Fills an open slot{build}: {margin * 100:0.0} pp above the expected later filler ({best.Replacement:P1}).")
                        : Invariant($"- Fills an open slot{build}, but later picks should supply better ({-margin * 100:0.0} pp below the expected filler {best.Replacement:P1})."));
                }
                else if (best.Virtual)
                    reasons.Add(Invariant($"· Later picks should replace your weakest projected card; measured against the expected later filler ({best.Replacement:P1}): {margin * 100:+0.0;-0.0;0.0} pp."));
                else reasons.Add(best.Membership >= .75 ? Invariant($"+ Makes the projected deck: clears the cut line by {margin * 100:0.0} pp.")
                    : best.Membership >= .25 ? Invariant($"· Near the projected deck cut line ({margin * 100:+0.0;-0.0;0.0} pp).")
                    : Invariant($"- Does not make the current best build ({-margin * 100:0.0} pp below the cut line)."));
                if (best.Forced) reasons.Add("+ Kept in by the projected deck's composition limits (creature/early-play floors or top-end cap).");
                else if (best.Membership >= .5 && best.Displaced is { } displaced && input.Catalog.Find(displaced) is { } weaker)
                    reasons.Add(Invariant($"+ Replaces {weaker.Name} in the projected 23."));
                if (open > 0 && supply < 2 * open) reasons.Add(Invariant($"+ Deck still needs {open} playable{(open == 1 ? "" : "s")} with only {remaining} picks remaining."));
                else if (open > 0 && urgency < .2) reasons.Add("· Plenty of picks remain, so open-slot value is modest.");
            }
            if (best?.Need is { } detail) reasons.AddRange(NeedReasons(detail, best.Gate));
            if (fits.Skip(1).Any(f => f.Value > _score.DeckFitFloor) && bestWeight < .9) reasons.Add("· Deck fit is shared across close projected color pairs.");
            if (before?.Pair != after.Pair) reasons.Add("· Changes the strongest viable projected color pair.");
            if (after.Spells < _deck.TargetNonlandCount) reasons.Add("· Incomplete projected shell, not a finished 40-card deck.");
            if (input.Pool.Completeness != DraftPoolCompleteness.Complete) reasons.Add("· Projection uses a partial/unknown inventory.");
            if (commitment.Fit(card.Colors) < -.25 && !makes) reasons.Add("- Outside your established colors.");
            impacts[id] = new(true, makes, replaced, delta, after.Creatures - (before?.Creatures ?? 0), after.Early - (before?.Early ?? 0),
                after.High - (before?.High ?? 0), mana, before is not null && before.Pair != after.Pair,
                before is not null && before.ConstraintSignature != after.ConstraintSignature,
                after.Spells < _deck.TargetNonlandCount, before?.Pair, after.Pair, best?.Need?.Signed ?? 0, need, Array.AsReadOnly(reasons.ToArray()))
            {
                CutlineMargin = best?.Margin, MembershipValue = best?.Membership ?? (makes ? 1 : 0), ReplacementLevel = replacement,
                RealCutline = best?.Real, ComparedWithFutureFiller = best?.Virtual ?? false, OpenSlots = open, RemainingPicks = remaining,
                SupplyCoverage = coverage, SupplyUrgency = urgency, ContestedSlots = contested, ExpectedSupply = supply,
                SupplyProfile = $"{_score.SupplyProfile.Name} ({_score.SupplyProfile.Provenance})", StructureGate = best?.Gate ?? 0, Playability = playability,
                BestPairWeight = bestWeight, ProjectedPairCount = afters.Count, DisplacedCard = best?.Displaced,
                ProjectedStructure = before is null ? null : StructureOf(before), StructureConfidence = best?.Need?.Confidence ?? 0,
                CreatureNeed = best?.Need?.Creature ?? 0, EarlyPlayNeed = best?.Need?.Early ?? 0, RemovalNeed = best?.Need?.Removal ?? 0,
                TopEndRedundancy = best?.Need?.Redundancy ?? 0
            };
        }
        ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds;
        return Array.AsReadOnly(snapshot.CurrentPack.AvailableCardIdentifiers.Select(id => impacts[id]).ToArray());

        // Structural need of the projected deck (before this pick), measured against the existing builder targets and
        // scaled to 23 spells for incomplete shells. Needs are deficit-relative, never blanket role bonuses; the
        // strongest single need counts (correlated needs do not stack); top-end redundancy is subtracted.
        Need NeedFor(Card card, Projection deck)
        {
            var structure = StructureOf(deck);
            var k = (double)_deck.TargetNonlandCount / Math.Max(1, structure.Spells);
            var metadata = card.GameplayMetadata;
            var creature = metadata.IsCreature ? Math.Clamp((_deck.PreferredCreatures - structure.Creatures * k)
                / (_deck.PreferredCreatures - _deck.MinimumCreatures + 2d), 0, 1) : 0;
            var early = metadata.IsNonlandSpell && metadata.ManaValue is <= 2
                ? Math.Clamp((_deck.MinimumEarlyPlays - structure.EarlyPlays * k) / Math.Max(1d, _deck.MinimumEarlyPlays), 0, 1) : 0;
            var removal = _roles.Classify(card).RemovalWeight * Math.Clamp((_score.HealthyRemovalCount - structure.Removal * k) / _score.HealthyRemovalCount, 0, 1);
            var redundancy = metadata.ManaValue >= _deck.HighCostThreshold
                ? Math.Clamp((structure.HighCost * k - (_deck.MaximumHighCostCards - 1)) / _score.TopEndRedundancySpan, 0, 1) : 0;
            var confidence = Math.Clamp(structure.Spells / _score.StructureConfidenceSpells, 0, 1);
            return new(creature, early, removal, redundancy, Math.Max(Math.Max(creature, early), removal) - redundancy, confidence, structure);
        }

        ProjectedDeckStructure StructureOf(Projection deck) => new(deck.Spells, deck.Creatures, deck.Early, deck.High,
            deck.Decisions.Where(d => d.SelectedCount > 0).Sum(d => input.Catalog.Find(d.CardIdentifier) is { GameplayMetadata.IsLand: false } c
                ? d.SelectedCount * _roles.Classify(c).RemovalWeight : 0));

        IEnumerable<string> NeedReasons(Need need, double gate)
        {
            var s = need.Structure; var shell = s.Spells < _deck.TargetNonlandCount ? Invariant($" in a {s.Spells}-spell shell") : "";
            bool Matters(double value) => _score.StructureWeight * need.Confidence * gate * value >= .01;
            if (Matters(need.Early)) yield return Invariant($"+ Fills missing early plays (projected {s.EarlyPlays} of {_deck.MinimumEarlyPlays}{shell}).");
            if (Matters(need.Creature)) yield return Invariant($"+ Raises creatures from {s.Creatures} to {s.Creatures + 1} (preferred {_deck.PreferredCreatures}{shell}).");
            if (Matters(need.Removal)) yield return Invariant($"+ Projected deck has only {s.Removal:0.#} removal spell(s){shell}.");
            if (Matters(need.Redundancy)) yield return Invariant($"- Projected deck already has {s.HighCost} cards at MV {_deck.HighCostThreshold:0}+ (cap {_deck.MaximumHighCostCards}{shell}).");
            if (need.Confidence < 1 && (Matters(need.Early) || Matters(need.Creature) || Matters(need.Removal) || Matters(need.Redundancy)))
                yield return Invariant($"· Structure weighed at {need.Confidence:P0} confidence ({s.Spells} projected spells).");
        }

        // F: graded maindeck value from the cut-line margin. The optimizer is exact, so no feasible single swap improves
        // its deck: an excluded candidate is at or below its weakest swappable maindeck card and an included one at or
        // above its best swappable alternative; both sides meet at margin zero (continuity, phase 1). A second, wider
        // logistic lets F keep rising with how weak a card it replaces. Structure is realised only as far as the card
        // is likely to be played (membership) and shrunk by projected-shell confidence.
        PairFit Fit(Card card, Projection pair, Projection? reference)
        {
            var floor = new PairFit(_score.DeckFitFloor, null, 0);
            if (!DeckPlanSelector.ColorEligible(card, pair.Pair)) return floor;
            var same = reference?.Pair == pair.Pair ? reference : null;
            // The virtual cut line belongs to the build being evaluated: its own open slots and weak spells (phase 7).
            var pairReplacement = Replacement(same ?? reference);
            var included = pair.Main.GetValueOrDefault(card.Identifier) > (same?.Main.GetValueOrDefault(card.Identifier) ?? 0);
            var spells = pair.Decisions.Select(d => (Decision: d, Card: input.Catalog.Find(d.CardIdentifier)!))
                .Where(x => x.Card is not null && DeckPlanSelector.HasSpellMetadata(x.Card) && DeckPlanSelector.ColorEligible(x.Card, pair.Pair)
                    && x.Decision.Strength?.SelectionValue is not null).ToArray();
            var value = spells.FirstOrDefault(x => x.Decision.CardIdentifier == card.Identifier).Decision?.Strength?.SelectionValue;
            int creatures = Count(Creature), early = Count(Early), high = Count(High);
            double? cut = null, real = null; CardIdentifier? displaced = null; var forced = false;
            if (value is not null && included)
            {
                var bench = spells.Where(x => x.Decision.CardIdentifier != card.Identifier && x.Decision.PoolCount > x.Decision.SelectedCount)
                    .OrderByDescending(x => x.Decision.Strength!.SelectionValue).ThenBy(x => x.Decision.CardIdentifier.Value, StringComparer.Ordinal).ToArray();
                var alternative = bench.FirstOrDefault(x => Swappable(card, x.Card));
                // A card a composition floor forces in has no feasible swap: compare its quality with the best bench card
                // and let the structure term (at full membership) carry the need, instead of crediting the need twice.
                forced = alternative.Decision is null && bench.Length > 0;
                var compare = forced ? bench[0] : alternative;
                real = compare.Decision?.Strength!.SelectionValue; displaced = compare.Decision?.CardIdentifier;
                // Phase 6: an open slot (no real alternative), or a weakest card later picks should beat anyway, is
                // contested by the expected later filler; a forced card keeps its quality comparison.
                cut = forced ? real : Higher(real, pairReplacement);
            }
            else if (value is not null)
            {
                var weakest = spells.Where(x => x.Decision.CardIdentifier != card.Identifier && x.Decision.SelectedCount > 0 && Swappable(x.Card, card))
                    .OrderBy(x => x.Decision.Strength!.SelectionValue).ThenBy(x => x.Decision.CardIdentifier.Value, StringComparer.Ordinal).FirstOrDefault();
                // No maindeck card can be exchanged without breaking a creature/early floor or the top-end cap.
                if (weakest.Decision is null) return floor;
                real = weakest.Decision.Strength!.SelectionValue; displaced = weakest.Decision.CardIdentifier;
                cut = Higher(real, pairReplacement);
            }
            var margin = value - cut;
            // A pair this card makes viable is an alternative build, not open slots: it is worth only what that
            // build gains over the current best deck, expressed per card as the total spell-quality difference.
            if (included && same is null && reference is not null && pair.Quality is { } unlocked && reference.Quality is { } current)
                margin = (unlocked - current) * pair.Spells;
            var membership = margin is { } m ? ContextualPickScoreConfiguration.Logistic(m / scale) : included ? 1 : 0;
            // Below the cut both terms use the narrow scale (an excluded card falls to the floor quickly); above it the
            // wide scale keeps rewarding larger upgrades. Equal at zero, so F stays continuous.
            var upgrade = margin is { } u ? ContextualPickScoreConfiguration.Logistic(u / (u > 0 ? _score.ReplacementScale : scale)) : membership;
            var fit = _score.DeckFitFloor + (1 - _score.DeckFitFloor) * (.5 * membership + .5 * upgrade);
            var compared = same ?? reference;
            var need = compared is null ? null : NeedFor(card, compared);
            // Structure is realised as far as the card is likely to be played against the same cut line, on a wider
            // scale than membership: a far-below-cut card keeps a little need credit, never enough to look strong.
            var gate = forced ? 1 : margin is { } g ? ContextualPickScoreConfiguration.Logistic(g / _score.StructureGateScale) : included ? 1 : 0;
            var structural = need is null ? 0 : _score.StructureWeight * need.Confidence * gate * need.Signed;
            var unlockedPair = included && same is null && reference is not null;
            var isVirtual = !forced && !unlockedPair && cut is not null && (real is null || pairReplacement > real);
            return new(Math.Clamp(fit + structural, 0, 1), margin, forced ? 1 : membership, need, forced ? null : displaced, true, forced, real, isVirtual, gate, pairReplacement);

            // Exchanging `removed` for `added` keeps the projection's creature/early floors and top-end cap.
            bool Swappable(Card removed, Card added)
            {
                var composition = pair.Composition;
                return creatures - Creature(removed) + Creature(added) >= composition.RequiredCreatures
                    && early - Early(removed) + Early(added) >= composition.RequiredEarlyPlays
                    && high - High(removed) + High(added) <= composition.EffectiveHighCostCap;
            }
            int Count(Func<Card, int> flag) => spells.Sum(x => flag(x.Card) * x.Decision.SelectedCount);
        }
        static double? Higher(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
        // Maindeck spells a typical later pick beats (below baseline − 3 pp), contested by later picks.
        double[] WeakSpells(Projection? deck) => baseline is { } reference && deck is not null ? deck.Decisions.Where(d => d.SelectedCount > 0
            && d.Strength?.SelectionValue < reference - _score.NormalFillerOffset && input.Catalog.Find(d.CardIdentifier) is { } c
            && DeckPlanSelector.HasSpellMetadata(c)).SelectMany(d => Enumerable.Repeat(d.Strength!.SelectionValue!.Value, d.SelectedCount)).ToArray() : [];
        // Expected marginal card of that build's final deck without the candidate (phase 6 order statistic).
        double? Replacement(Projection? deck) => baseline is { } level
            ? _score.FinalMarginalCard(level, WeakSpells(deck), Math.Max(0, _deck.TargetNonlandCount - (deck?.Spells ?? 0)), supply) : null;
        int Creature(Card c) => c.GameplayMetadata.IsCreature ? 1 : 0;
        int Early(Card c) => c.GameplayMetadata.ManaValue is <= 2 ? 1 : 0;
        int High(Card c) => c.GameplayMetadata.ManaValue >= _deck.HighCostThreshold ? 1 : 0;

        // Phase 6 playability P: plausibility-weighted share of realistic builds (viable pairs after this pick, so a pair
        // the candidate makes viable counts) that can play the card. Plausibility is softmax(average spell quality / 1 pp),
        // wider than the deck-fit blend: close alternatives stay live, distant ones fade. No extra optimizer calls.
        double Playability(Card card, IReadOnlyList<Projection> pairs)
        {
            if (pairs.Count == 0) return 1;
            var measured = pairs.All(p => p.Quality is not null);
            var top = measured ? pairs.Max(p => p.Quality!.Value) : 0;
            var raw = pairs.Select(p => measured ? Math.Exp((p.Quality!.Value - top) / _score.PlayabilityTemperature) : 1d).ToArray();
            var total = raw.Sum();
            var weights = Tapered(pairs, raw.Select(w => w / total).ToArray());
            return Math.Clamp(pairs.Select((p, i) => weights[i] * Eligibility(card, p.Pair)).Sum(), 0, 1);
        }
        // Phase 10 builds no splashes, so an off-pair card is playable only as a splash: exactly one colour outside the
        // pair, needing one pip of it, with drafted lands producing it (full support at three).
        double Eligibility(Card card, ArchetypeColorPair pair)
        {
            if (DeckPlanSelector.ColorEligible(card, pair)) return 1;
            var outside = card.Colors.Colors.Where(c => !pair.Colors.Contains(c)).ToArray();
            if (!card.GameplayMetadata.ColorsKnown || outside.Length != 1 || DeckManaDemand.For(card).Colors.GetValueOrDefault(outside[0]) > 1) return 0;
            var kind = (ManaKind)(int)outside[0];
            var sources = input.Pool.Entries.Sum(e => input.Catalog.Find(e.CardIdentifier) is { GameplayMetadata: { IsLand: true, ProducedMana: { } produced } }
                && produced.Contains(kind) ? e.Count : 0);
            return _score.SplashCredit * Math.Clamp(sources / _score.SplashSourcesForFullSupport, 0, 1);
        }

        // Plausibility of each viable pair from its projected average quality; the best pair dominates
        // unless another is close, so a candidate tipping two near-equal pairs changes the blend smoothly.
        double[] PairWeights(IReadOnlyList<Projection> pairs)
        {
            if (pairs.Any(p => p.Quality is null)) return pairs.Select((_, i) => i == 0 ? 1d : 0).ToArray();
            var top = pairs.Max(p => p.Quality!.Value);
            var raw = pairs.Select(p => Math.Exp((p.Quality!.Value - top) / _score.PairTemperature)).ToArray();
            var total = raw.Sum();
            return Tapered(pairs, raw.Select(w => w / total).ToArray());
        }
        // Phase 7: a near-viable pair (a few spells short of the leading shell) keeps only part of its plausibility;
        // the rest returns to the best fully viable pair (index 0). Its weight reaches zero exactly where it stops being
        // projected, so a pair fades in and out instead of switching on at the count threshold.
        double[] Tapered(IReadOnlyList<Projection> pairs, double[] weights)
        {
            for (var i = 1; i < pairs.Count; i++)
            {
                var taper = _score.ViabilityTaper(pairs[i].Shortfall);
                weights[0] += weights[i] * (1 - taper); weights[i] *= taper;
            }
            return weights;
        }

        IReadOnlyList<Projection> Project(DeckBuildInput candidateInput)
        {
            var pairs = DeckPlanSelector.RankPairs(candidateInput);
            var target = Math.Min(_deck.TargetNonlandCount, pairs.Max(p => p.EligibleNonlands));
            if (target == 0) return [];
            BaselineDeckConfiguration ConfigFor(int spells)
            {
                if (spells == _deck.TargetNonlandCount) return _deck;
                var fraction = (double)spells / _deck.TargetNonlandCount;
                return new BaselineDeckConfiguration(
                    targetDeckSize: _deck.TargetLandCount + spells, targetLandCount: _deck.TargetLandCount, targetNonlandCount: spells,
                    minimumCreatures: (int)Math.Floor(_deck.MinimumCreatures * fraction),
                    preferredCreatures: (int)Math.Floor(_deck.PreferredCreatures * fraction),
                    minimumEarlyPlays: (int)Math.Floor(_deck.MinimumEarlyPlays * fraction), highCostThreshold: _deck.HighCostThreshold,
                    maximumHighCostCards: Math.Min(spells, (int)Math.Ceiling(_deck.MaximumHighCostCards * fraction)),
                    maximumDraftedNonbasicLands: _deck.MaximumDraftedNonbasicLands, minimumBasicsPerUsedColor: _deck.MinimumBasicsPerUsedColor);
            }
            var viable = new List<(Projection Deck, DeckPairEvidence Evidence)>();
            // Phase 7: pairs up to ViabilityTolerance spells short of the leading shell are projected at their own size
            // and enter the blends with a tapered weight (see Tapered), instead of appearing only at the count threshold.
            foreach (var pair in pairs.Where(p => p.EligibleNonlands > 0 && p.EligibleNonlands >= target - _score.ViabilityTolerance))
            {
                token.ThrowIfCancellationRequested();
                var spells = Math.Min(target, pair.EligibleNonlands);
                // Off-pair sideboard entries cannot affect this explicit pair's selected cards.
                var eligible = candidateInput.Pool.Entries
                    .Where(e => candidateInput.Catalog.Find(e.CardIdentifier) is { } c && DeckPlanSelector.ColorEligible(c, pair.Pair)
                        && (DeckPlanSelector.HasSpellMetadata(c) || c.GameplayMetadata.IsLand)).ToArray();
                // With no drafted lands and identical eligible spells, selection is pair-independent.
                // Require all colored mana demands inside the pair; never share an unsupported cost.
                // Generated basics may name a different unused color, but the compared projection
                // fields (membership, quality, structure, source shortfall) are identical.
                var equivalent = reuseEquivalentPairProjections && eligible.All(e =>
                    candidateInput.Catalog.Find(e.CardIdentifier) is { } c && !c.GameplayMetadata.IsLand
                    && DeckManaDemand.For(c).Colors.Where(d => d.Value > 0).All(d => pair.Pair.Colors.Contains(d.Key)));
                var key = $"{(equivalent ? "equivalent" : pair.Pair.Code)}:{spells}:" + string.Join(";", eligible
                    .OrderBy(e => e.CardIdentifier.Value, StringComparer.Ordinal).Select(e => $"{e.CardIdentifier.Value.Length}:{e.CardIdentifier.Value}:{e.Count}"));
                if (!projections.TryGetValue(key, out var projection))
                {
                    var result = new BaselineDeckBuilder(ConfigFor(spells)).BuildForPair(candidateInput, pair.Pair, token);
                    OptimizerCalls++;
                    if (result.Deck is not { } deck) continue;
                    var comparison = SuggestedDeckBuilder.Compare(deck, pair);
                    var main = deck.Nonlands.Concat(deck.NonbasicLands).GroupBy(e => e.CardIdentifier)
                        .ToFrozenDictionary(g => g.Key, g => g.Sum(e => e.Count));
                    var totalDemand = deck.ColoredDemand.Values.Sum();
                    var shortfall = totalDemand <= 0 ? 0 : deck.ColoredDemand.Sum(d => Math.Max(0,
                        Math.Ceiling(deck.LandCount * d.Value / totalDemand) - deck.KnownColoredSources.GetValueOrDefault(d.Key)));
                    projection = new(pair.Pair, main, comparison.CommonSpellQuality, deck.NonlandCount, deck.CreatureCount,
                        deck.Composition.SelectedEarlyPlays, deck.Composition.SelectedHighCostCards, shortfall, deck.Composition.Relaxations.Count,
                        $"{deck.Composition.RequiredCreatures}:{deck.Composition.RequiredEarlyPlays}:{deck.Composition.EffectiveHighCostCap}:"
                            + string.Join(";", deck.Composition.Relaxations), deck.Decisions, deck.Composition);
                    projections[key] = projection;
                }
                viable.Add((projection with { Pair = pair.Pair, Shortfall = target - spells }, pair));
            }
            // Common overall quality and Phase 10C tie-break semantics; no pair-GIH double count.
            // Fully viable pairs first (the best one anchors the blends), then near-viable pairs.
            return viable.OrderBy(p => p.Deck.Shortfall).ThenByDescending(p => p.Deck.Quality).ThenByDescending(p => p.Evidence.PoolFit)
                .ThenBy(p => p.Deck.Relaxations).ThenByDescending(p => p.Evidence.CombinedEvidence)
                .ThenBy(p => p.Deck.Pair.Colors.Colors[0]).ThenBy(p => p.Deck.Pair.Colors.Colors[1]).Select(p => p.Deck).ToArray();
        }
    }

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    internal static CandidateDeckImpact Unavailable(double colorFit, string reason) => new(false, false, null, null,
        0, 0, 0, 0, false, false, true, null, null, 0, Math.Clamp(.5 + .5 * colorFit, 0, 1), Array.AsReadOnly(new[] { reason }));
}
