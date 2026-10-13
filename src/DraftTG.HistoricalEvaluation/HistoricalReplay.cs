using DraftTG.Application;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.HistoricalEvaluation;

public sealed record HistoricalReplayResult(string ModelVersion, ContextualPickScore[] Cards,
    DraftPackObservationHistory ObservationHistory, string[] MissingInputs, int UnscorableCards,
    double TotalMilliseconds, int OptimizerCalls, double InputColorCommitment = 0);

/// <summary>The scoring boundary accepts only pick-time state. Labels cannot enter the engine.</summary>
public sealed class HistoricalReplayAdapter(CardCatalog catalog, HistoricalIdentityResolver resolver,
    IReadOnlyList<HistoricalStatsSnapshot> statistics, IReadOnlyDictionary<string, string[]> bonusSheets,
    bool cacheImmutableEvidence = false)
{
    // Opt-in offline infrastructure only. Copy array containers so caller mutation cannot
    // silently invalidate the cache. Each adapter is owned by one sequential replay.
    private readonly IReadOnlyList<HistoricalStatsSnapshot> _statistics = cacheImmutableEvidence
        ? statistics.Select(s => s with { Cards=s.Cards.ToArray(), AlternateQualityCards=s.AlternateQualityCards?.ToArray() }).ToArray()
        : statistics;
    private readonly IReadOnlyDictionary<string,string[]> _bonusSheets = cacheImmutableEvidence
        ? bonusSheets.ToDictionary(p=>p.Key,p=>p.Value.ToArray(),StringComparer.Ordinal)
        : bonusSheets;
    private readonly Dictionary<HistoricalCardStats[], DraftTG.Data.SeventeenLandsRating[]> _providerRows = [];
    private readonly Dictionary<(HistoricalCardStats[] Features, string Set), LimitedCardStatisticsCatalog> _environments = [];

    private DraftTG.Data.SeventeenLandsRating[] ProviderRows(HistoricalCardStats[] features)
    {
        if (!cacheImmutableEvidence) return Rows(features);
        if (!_providerRows.TryGetValue(features,out var rows)) _providerRows[features]=rows=Rows(features);
        return rows;
    }
    private LimitedCardStatisticsCatalog Environment(HistoricalCardStats[] features,string set)
    {
        if (!cacheImmutableEvidence) return EnvironmentStats(features,set);
        if (!_environments.TryGetValue((features,set),out var environment))
            _environments[(features,set)]=environment=EnvironmentStats(features,set);
        return environment;
    }
    public static ContextualPickScoreVersion Version(string model) => model switch
    {
        "contextual-pick-score-v1.6" => ContextualPickScoreVersion.V1_6,
        "contextual-pick-score-v1.6.1" => ContextualPickScoreVersion.V1_6_1,
        "contextual-pick-score-v1.6.2" => ContextualPickScoreVersion.V1_6_2,
        _ => throw new ArgumentException("Only frozen v1.6 / v1.6.1 / v1.6.2 models are supported.")
    };
    public HistoricalReplayResult Replay(PickTimeState inputs, string model, EvaluationRegime regime,
        DraftPackObservationHistory? observations = null)
    {
        var version = Version(model);
        var missing = new List<string>();
        if (inputs.PickNumber > 14)
            return new(model, [], observations ?? DraftPackObservationHistory.Empty,
                ["Frozen Domain/score baseline supports pick coordinates 1–14; historical pick 15 is retained but not replayed."], inputs.OfferedCards.Sum(c => c.Count), 0, 0);
        if (inputs.OfferedCards.Any(c => c.Card.Identifier is null))
            return new(model, [], observations ?? DraftPackObservationHistory.Empty, ["Unresolved offered identity; decision excluded rather than ranking a truncated pack."], inputs.OfferedCards.Sum(c => c.Count), 0, 0);
        if (inputs.PriorPicks.Any(p => p.Pack > inputs.PackNumber || p.Pack == inputs.PackNumber && p.Pick >= inputs.PickNumber))
            throw new InvalidDataException("Future history prohibited at replay boundary.");
        var offered = Expand(inputs.OfferedCards).ToArray();
        var poolIds = Expand(inputs.PoolBeforePick).ToArray();
        var unresolved = inputs.PoolBeforePick.Where(c => c.Card.Identifier is null).Sum(c => c.Count);
        if (unresolved > 0) missing.Add($"{unresolved} unresolved pool occurrences.");
        if (!inputs.PoolIsComplete) missing.Add("Partial source pre-pick inventory.");
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(inputs.PackNumber), PickNumber.Create(inputs.PickNumber)), offered),
            new(inputs.PriorPicks.Where(p => p.Card.Identifier is not null && p.Pick <= 14).OrderBy(p => p.Pack).ThenBy(p => p.Pick)
                .Select(p => new DraftPick(new(PackNumber.Create(p.Pack), PickNumber.Create(p.Pick)), p.Card.Identifier!))),
            inputs.Format == "TradDraft" ? DraftFormat.BestOfThree : DraftFormat.BestOfOne) { RecoveredPool = new(poolIds) };
        var context = new LimitedStatisticsContext(inputs.SetCode, Format(inputs.Format));
        var compatible = _statistics.Where(s => s.SetCode == inputs.SetCode && s.Format == inputs.Format && s.Regime == regime).ToArray();
        if (regime == EvaluationRegime.ChronologicalBacktest)
        {
            if (inputs.DraftTimestamp is null) throw new InvalidDataException("Chronological replay requires a reliable decision-date boundary.");
            compatible = compatible.Where(s => s.AvailableAt is { } at && at < inputs.DraftTimestamp
                && s.Cards.All(c => c.AvailableAt is { } ca && ca <= at)
                && (s.AlternateQualityCards?.All(c => c.AvailableAt is { } ca && ca <= at) ?? true)).OrderByDescending(s => s.AvailableAt).ToArray();
            if (compatible.Length == 0) throw new InvalidDataException("No strictly earlier, timestamped statistics; chronological backtest refused.");
        }
        else if (compatible.Length > 1) throw new InvalidDataException("Multiple mature snapshots; select one explicitly.");
        var stats = compatible.FirstOrDefault();
        if (stats is null) missing.Add("Exact set/format aggregate statistics unavailable.");
        var features = stats?.Cards ?? [];
        var rows = ProviderRows(features);
        var mapped = LimitedStatisticsMapper.Map(rows, catalog, context, snapshot);
        var env = Environment(features, inputs.SetCode);
        var alternate = new Dictionary<CardIdentifier, AlternateFormatQualityEvidence>();
        if (stats?.AlternateQualityCards is { } alt && SparsePrimaryQualityPrior.SourceFormat(context) is { } permitted)
        {
            if (alt.Any(c => c.SetCode != inputs.SetCode || Format(c.PrimaryFormat) != permitted || c.Period != "ALL_TIME"))
                throw new InvalidDataException("Alternate evidence violates frozen FRA prior policy.");
            var altMapping = LimitedStatisticsMapper.Map(ProviderRows(alt), catalog, new(inputs.SetCode, permitted), snapshot);
            var baseline = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, altMapping.Catalog, Environment(alt, inputs.SetCode)).EnvironmentBaseline;
            if (baseline is { } b)
                foreach (var id in offered.Distinct())
                    if (SparsePrimaryQualityPrior.HasUsableGih(altMapping.Catalog.StatisticsFor(id)))
                    {
                        var a = altMapping.Catalog.StatisticsFor(id)!;
                        alternate[id] = new(id, context, new(inputs.SetCode, permitted), a.GameInHandWinRate!.Value,
                            a.GameInHandGameCount!.Value, b, altMapping.ResolvedNames[id], stats.AvailableAt, "Cache", "ALL_TIME");
                    }
        }
        var result = new LimitedStatisticsLoadResult(context, stats is null ? null : context, mapped.Catalog,
            stats is null ? LimitedStatisticsSource.Unavailable : LimitedStatisticsSource.Cache, stats?.AvailableAt, mapped.Diagnostic)
        { CardCatalog = catalog, EnvironmentCatalog = env, StatisticsResolutions = mapped.Resolutions,
            StatisticsResolvedNames = mapped.ResolvedNames, AlternateQualityEvidence = alternate };
        if ((observations?.Observations.Count ?? 0) == 0 && inputs.GlobalPickIndex > 0) missing.Add("Earlier observed packs unavailable; lane history starts here.");
        missing.Add("Historical slot/display order unavailable; source column order retained for deterministic occurrence ties.");
        missing.Add("Frozen baseline assumes 42 picks / 14 per pack; historical coordinates are passed unchanged.");
        if (inputs.PriorPicks.Any(p => p.Pick > 14)) missing.Add("Prior pick-15 identities retained in recovered pool; unsupported chronology coordinate omitted from Domain history.");
        missing.Add("Pair statistics and successful-deck corpus not supplied; production unavailable-data behavior retained.");
        var pool = new DraftPoolSnapshot(snapshot.DraftedPool,
            inputs.PoolIsComplete && unresolved == 0 ? DraftPoolCompleteness.Complete : DraftPoolCompleteness.Partial, unresolved);
        var update = new LimitedStatisticsUpdate(snapshot, false, result, observationHistory: observations, draftPool: pool,
            pickScoreConfiguration: new(version: version, supplyProfile: FuturePickSupplyProfile.For(inputs.SetCode)));
        if (update.Recommendation!.EnvironmentBaseline is null) missing.Add("No usable exact-format environment GIH baseline; scores unavailable.");
        var scores = update.PickScores!;
        return new(model, scores.Cards.ToArray(), update.ObservationHistory, missing.ToArray(),
            scores.Cards.Count(c => c.ContextualValue is null), scores.Metrics.TotalMilliseconds, scores.Metrics.OptimizerCalls,
            update.ContextualRecommendation!.Profile.CommitmentStrength);
    }
    private LimitedCardStatisticsCatalog EnvironmentStats(HistoricalCardStats[] features, string set)
    {
        // Resolve strict exact printings; do not use the live mapper's representative first-printing baseline join.
        var bonus = _bonusSheets.GetValueOrDefault(set, []);
        var rows = new List<LimitedCardStatistics>();
        foreach (var group in features.GroupBy(f => f.SourceIdentity, StringComparer.Ordinal))
        {
            if (group.Distinct().Count() != 1) continue;
            var feature = group.Distinct().Single(); var identity = resolver.Resolve(feature.SourceIdentity, set, bonus);
            if (identity.Identifier is not { } id) continue;
            rows.Add(new(id, feature.GIHGames, GameInHandWinRate: feature.GIHWinRate, AverageLastSeenAt: feature.ALSA, AverageTakenAt: feature.ATA));
        }
        return new(rows.DistinctBy(r => r.CardIdentifier));
    }
    private static DraftTG.Data.SeventeenLandsRating[] Rows(HistoricalCardStats[] features) => features.Select(f =>
        new DraftTG.Data.SeventeenLandsRating(f.SourceIdentity, f.GIHGames, GameInHandWinRate: f.GIHWinRate,
            OpeningHandWinRate: f.OpeningHandWinRate, DrawnWinRate: f.DrawnWinRate, AverageLastSeenAt: f.ALSA, AverageTakenAt: f.ATA)).ToArray();
    public static IEnumerable<CardIdentifier> Expand(IEnumerable<HistoricalCardQuantity> quantities) => quantities
        .Where(c => c.Card.Identifier is not null).SelectMany(c => Enumerable.Repeat(c.Card.Identifier!, c.Count));
    public static LimitedStatisticsFormat Format(string format) => format switch
    { "PremierDraft" => LimitedStatisticsFormat.PremierDraft, "QuickDraft" => LimitedStatisticsFormat.QuickDraft,
        "TradDraft" => LimitedStatisticsFormat.TraditionalDraft, _ => throw new InvalidDataException("Unsupported draft format.") };
}
