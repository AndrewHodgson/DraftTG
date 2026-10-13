using System.Diagnostics;
using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

/// <summary>Composes existing Phase 8/9 signals and Phase 10 marginal projections. No provider calls.</summary>
public sealed class ContextualPickScoreEngine(ContextualPickScoreConfiguration? configuration = null,
    BaselineDeckConfiguration? deckConfiguration = null, ILimitedCardRoleProfiles? roleProfiles = null,
    ILimitedCardRoleOverrides? roleOverrides = null)
{
    public ContextualPickScoreConfiguration Configuration { get; } = configuration ?? new();
    public ContextualPickScoreResult Recommend(DraftSnapshot snapshot, CardCatalog catalog,
        ArchetypeRecommendationResult archetype, LimitedCardStatisticsCatalog statistics,
        LimitedCardStatisticsCatalog environment, DraftPoolSnapshot? pool = null, CancellationToken token = default,
        IReadOnlyDictionary<CardIdentifier, AlternateFormatQualityEvidence>? alternateQuality = null,
        IReadOnlySet<CardIdentifier>? primaryFrontFaces = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(catalog);
        if (!snapshot.CurrentPack.AvailableCardIdentifiers.SequenceEqual(archetype.Cards.Select(c => c.CardIdentifier))
            || archetype.Cards.Where((c, i) => c.PackIndex != i).Any())
            throw new ArgumentException("Pick scores require the exact ordered current-pack occurrence results.", nameof(archetype));
        var clock = Stopwatch.StartNew();
        var lane = archetype.LaneRecommendation;
        var commitment = lane.PoolRecommendation.Profile;
        var coordinateCount = (snapshot.CurrentPack.Position.Pack.Value - 1) * Configuration.PicksPerPack
            + snapshot.CurrentPack.Position.Pick.Value - 1;
        var progress = Math.Clamp((double)Math.Max(commitment.CompletedPickCount, coordinateCount) / Configuration.ExpectedDraftPicks, 0, 1);
        var weights = Configuration.WeightsAt(progress);
        var baseline = lane.StatisticalRecommendation.EnvironmentBaseline;
        var classifier = new LimitedCardRoleClassifier(roleProfiles, roleOverrides);
        var evaluator = new CandidateDeckImpactEvaluator(Configuration, deckConfiguration, roles: classifier);
        IReadOnlyList<CandidateDeckImpact>? impacts = null;
        if (baseline is not null && (weights.DeckNeed > 0
            || Configuration.Version == ContextualPickScoreVersion.V1_6_2 && commitment.CompletedPickCount > 0))
        {
            pool ??= new(snapshot.DraftedPool, snapshot.DraftedPool.Count == coordinateCount
                ? DraftPoolCompleteness.Complete : DraftPoolCompleteness.Partial);
            impacts = evaluator.Evaluate(snapshot, new(pool, catalog, statistics, environment), commitment, token, baseline);
        }
        var projectedCommitment = Configuration.Version == ContextualPickScoreVersion.V1_6_2 && pool is not null
            ? ProjectedColorCommitment.From(commitment, evaluator.PoolProjections, pool, coordinateCount) : null;
        var cards = archetype.Cards.Select(card =>
        {
            token.ThrowIfCancellationRequested();
            var statistical = card.StatisticalRecommendation;
            var estimated = statistical.AdjustedValue is null;
            // Q is direct only when Phase 8 had a valid GIH rate and positive sample; ALSA alone never qualifies.
            var row = statistics.StatisticsFor(card.CardIdentifier);
            var evidence = baseline is null ? PickScoreQualityEvidence.NoEnvironmentBaseline : !estimated ? PickScoreQualityEvidence.DirectCardStatistics
                : row is null ? PickScoreQualityEvidence.NoProviderRow : PickScoreQualityEvidence.ProviderRowWithoutUsableGih;
            double? q = baseline is { } b ? estimated ? .5 : Configuration.NormalizeQuality(statistical.AdjustedValue!.Value, b) : null;
            AlternateFormatQualityPrior? prior = null;
            if (estimated && baseline is { } primaryBaseline && Configuration.Version != ContextualPickScoreVersion.V1_6)
            {
                prior = SparsePrimaryQualityPrior.TryCreate(catalog.Find(card.CardIdentifier), row, primaryBaseline,
                    alternateQuality?.GetValueOrDefault(card.CardIdentifier));
                if (prior is not null)
                {
                    q = Configuration.NormalizeQuality(prior.EffectiveQualityValue, primaryBaseline);
                    evidence = PickScoreQualityEvidence.AlternateFormatPrior;
                }
            }
            var provenance = q is null ? PickScoreQualityProvenance.Missing
                : prior is not null ? PickScoreQualityProvenance.AlternateFormatPrior
                : estimated ? PickScoreQualityProvenance.NeutralEstimate
                : primaryFrontFaces?.Contains(card.CardIdentifier) == true ? PickScoreQualityProvenance.FrontFacePrimary
                : PickScoreQualityProvenance.DirectPrimary;
            var maximum = lane.Profile.Configuration.MaxHumanLaneAdjustment;
            var l = maximum > 0 ? Math.Clamp(.5 + .5 * card.LaneRecommendation.LaneAdjustment / maximum, 0, 1) : .5;
            var a = Math.Clamp(.5 + .5 * (card.Affinity.NormalizedAffinity ?? 0) * (archetype.Profile.Active?.Confidence ?? 0), 0, 1);
            var colorFit = card.LaneRecommendation.PoolRecommendation.ColorFit;
            var impact = impacts?[card.PackIndex] ?? CandidateDeckImpactEvaluator.Unavailable(colorFit,
                baseline is null ? "No environment baseline; score unavailable." : "Deck need has zero early-draft weight; no optimizer work performed.");
            // Early v1.6.2 projections are for playability only. Keep the prior unweighted D fallback value;
            // no deck-need coefficient or optimizer decision is changed.
            if (weights.DeckNeed == 0 && impacts is not null)
                impact = impact with { DeckNeed = CandidateDeckImpactEvaluator.Unavailable(colorFit, "Deck need not weighted yet.").DeckNeed };
            var d = impact.DeckNeed;
            // Phase 6: Q stays intrinsic; only its upside is scaled by how realistically the card can be played.
            var baseRelevance = Configuration.QualityRelevance(impact.Playability, progress);
            var relevance = Configuration.Version == ContextualPickScoreVersion.V1_6_2 && q is { } intrinsic
                ? Configuration.CommitmentQualityRelevance(intrinsic,baseRelevance,projectedCommitment?.Strength ?? 0,impact.Playability)
                : baseRelevance;
            double? effective = q is { } raw ? ContextualPickScoreConfiguration.EffectiveQuality(raw, relevance) : null;
            var reasons = new List<string>();
            if (q is null) reasons.Add("No valid environment baseline; no Pick Score.");
            else if (prior is not null) reasons.Add(FormattableString.Invariant(
                $"Estimated intrinsic quality: alternate {prior.Evidence.SourceContext.Format} prior; GIH {prior.Evidence.RawGameInHandWinRate:P2}, n={prior.Evidence.GameInHandGameCount}, sample weight {prior.SampleWeight:F4}, cross-format reliability {prior.CrossFormatReliability:F2}. Primary GIH remains unavailable."));
            else if (estimated) reasons.Add(evidence == PickScoreQualityEvidence.NoProviderRow
                ? "Estimated intrinsic quality (neutral format prior): no 17Lands row for this card in this set/format."
                : "Estimated intrinsic quality (neutral format prior): this card's 17Lands row has no usable GIH win rate.");
            else reasons.Add(q >= .85 ? "Excellent intrinsic GIH performance." : q >= .65 ? "Strong intrinsic GIH performance."
                : q < .4 ? "Below-baseline intrinsic GIH performance." : "Near-average intrinsic GIH performance.");
            if (q > .5 && relevance < .9)
                reasons.Add(Configuration.Version == ContextualPickScoreVersion.V1_6_2 && relevance < baseRelevance && projectedCommitment?.Pair is { } plan
                    ? FormattableString.Invariant($"Card quality is less useful outside your current {plan.Code} plan: commitment {projectedCommitment.Strength:P0}, projected playability {impact.Playability:P0}, quality relevance {relevance:P0}. Premium quality retains a speculation tail.")
                    : FormattableString.Invariant($"Quality relevance {relevance:P0}: unlikely to be playable in a realistic projected deck, so its strength counts less."));
            if (l > .55) reasons.Add("Candidate colors appear open from late-card evidence.");
            if (l < .45) reasons.Add("Less lane support for these colors; absence is not proof of a closed lane.");
            if (a > .55) reasons.Add("Performs particularly well in the active color pair.");
            if (a < .45) reasons.Add("Performs poorly relative to the active color pair's baseline.");
            if (weights.DeckNeed > 0) reasons.AddRange(impact.Reasons);
            if (snapshot.DraftedPool.Count < coordinateCount) reasons.Add("Draft stage comes from the coordinate; missing pool cards were not reconstructed.");
            var parts = new[] { ("Quality", effective ?? .5, weights.Quality), ("Lane", l, weights.Lane),
                ("Archetype", a, weights.Archetype), ("Deck Need", d, weights.DeckNeed) };
            var weighted = parts.Sum(p => p.Item2 * p.Item3);
            var linear = .5 + Configuration.CalibrationGain * (weighted - .5);
            // Component points are the linear calibration; the soft-limit entry makes them sum to the actual value.
            var contributions = parts.Select(p => new PickScoreContribution(p.Item1, p.Item2, p.Item3,
                ContextualPickScoreCalibration.MaximumScore * Configuration.CalibrationGain * p.Item3 * (p.Item2 - .5)))
                .Append(new("Soft limit", linear, 0, ContextualPickScoreCalibration.MaximumScore * (Configuration.Calibrate(weighted) - linear))).ToArray();
            return new ContextualPickScore(card.PackIndex, card.CardIdentifier, q is null ? null : Configuration.Calibrate(weighted),
                q, l, a, d, progress, colorFit, statistical.DataWeight, weights,
                q is null ? PickScoreAvailability.NoEnvironmentBaseline : estimated ? PickScoreAvailability.EstimatedMissingStatistics : PickScoreAvailability.Measured,
                Configuration.ModelVersion, impact, classifier.Classify(catalog.Find(card.CardIdentifier)),
                Array.AsReadOnly(contributions), Array.AsReadOnly(reasons.ToArray()))
                { QualityEvidence = evidence, QualityProvenance = provenance, QualityPrior = prior,
                    QualityRelevance = relevance, BaseQualityRelevance = baseRelevance,
                    ColorCommitment = projectedCommitment, EffectiveQualityComponent = effective };
        }).ToArray();
        // The unrounded contextual value is strictly increasing in every component, so it alone orders distinct
        // candidates. Quality, then Phase 8 evidence weight, then pack order only separate exact ties.
        var ranked = cards.Where(c => c.ContextualValue is not null).OrderByDescending(c => c.ContextualValue)
            .ThenByDescending(c => c.QualityComponent).ThenByDescending(c => c.StatisticalDataWeight).ThenBy(c => c.PackIndex).ToArray();
        if (ranked.Length >= 2)
            for (var i = 0; i < ranked.Length; i++) cards[ranked[i].PackIndex] = ranked[i] with { CurrentPackRank = i + 1, IsContextPick = i == 0 };
        return new(cards, Configuration, new(evaluator.OptimizerCalls, evaluator.ElapsedMilliseconds, clock.Elapsed.TotalMilliseconds));
    }
}
