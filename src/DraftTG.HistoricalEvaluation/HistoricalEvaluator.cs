using System.Diagnostics;
using DraftTG.RecommendationEngine;

namespace DraftTG.HistoricalEvaluation;

public sealed record HumanPickMetrics(long Decisions, long Eligible, long Top1Count, long Top3Count, long Top5Count,
    double? Top1, double? Top3, double? Top5, double? MeanReciprocalRank, double? MeanNormalizedRank,
    double? MeanScoreGap, long EstimatedCardScores, long TotalOfferedOccurrences, long MaindeckLabelCount,
    double? MeanObservedMaindeckRate, double? MeanActualRankWhenObservedMaindeckRatePositive,
    long PartialPoolDecisions, long MissingGihCards);
public sealed class MetricAccumulator
{
    private long _decisions, _eligible, _top1, _top3, _top5, _estimated, _cards, _deckLabels, _positiveDeckLabels, _partial, _missingGih;
    private double _rr, _normalized, _gap, _deckRate, _deckRank;
    public void Add(int? actualRank, int occurrences, double? gap, int estimated = 0, double? maindeckRate = null,
        bool partialPool = false, int missingGih = 0)
    {
        _decisions++; _cards += occurrences; _estimated += estimated; _missingGih += missingGih;
        if (partialPool) _partial++;
        if (actualRank is not { } rank || gap is null) return;
        if (rank < 1 || rank > occurrences) throw new ArgumentOutOfRangeException(nameof(actualRank));
        _eligible++; if (rank == 1) _top1++; if (rank <= 3) _top3++; if (rank <= 5) _top5++;
        _rr += 1d / rank; _normalized += occurrences <= 1 ? 0 : (double)(rank - 1) / (occurrences - 1); _gap += gap.Value;
        if (maindeckRate is { } rate)
        { _deckLabels++; _deckRate += rate; if (rate > 0) { _positiveDeckLabels++; _deckRank += rank; } }
    }
    public HumanPickMetrics Summary() => new(_decisions, _eligible, _top1, _top3, _top5,
        Mean(_top1, _eligible), Mean(_top3, _eligible), Mean(_top5, _eligible), Mean(_rr, _eligible),
        Mean(_normalized, _eligible), Mean(_gap, _eligible), _estimated, _cards, _deckLabels,
        Mean(_deckRate, _deckLabels), Mean(_deckRank, _positiveDeckLabels), _partial, _missingGih);
    private static double? Mean(double total, long count) => count == 0 ? null : total / count;
}
public sealed record DecisionAgreement(int? Rank, int Occurrences, double? ScoreGap);
public sealed record ModelChange(string DatasetId, string DraftId, string SetCode, int Pack, int Pick,
    string ModelA, string ModelB, int? ActualRankA, int? ActualRankB, string? TopA, string? TopB,
    double LargestAbsoluteScoreChange, bool RankingChanged, bool MissingDataChanged);
public sealed record ModelComparison(string ModelA, string ModelB, long Compared, long RankingChanged,
    long ActualRankImproved, long ActualRankRegressed, long MissingDataDifferences, ModelChange[] LargestScoreChanges,
    ModelChange[] LargestRankImprovements, ModelChange[] LargestRankRegressions);
public sealed record EvaluationSummary(string Schema, string Regime, string Interpretation, string Build,
    string[] Models, string SplitHash, string CatalogHash, string StatsHash, long Decisions, long Drafts,
    IReadOnlyDictionary<string, HumanPickMetrics> Metrics, ModelComparison[] Comparisons,
    HistoricalDatasetManifest[] Datasets, double Seconds, double DecisionsPerSecond, long PeakWorkingSetBytes,
    long OptimizerCalls, double EngineMilliseconds, IReadOnlyDictionary<string, long> MissingInputReasons,
    string LeakagePolicy, string[] Limitations);

public sealed class HistoricalEvaluator(HistoricalStore store, HistoricalReplayAdapter adapter, SetSplitManifest split)
{
    public static DecisionAgreement Agreement(HistoricalDraftDecision d, HistoricalReplayResult r)
    {
        var n = d.Inputs.OfferedCards.Sum(c => c.Count);
        if (r.UnscorableCards > 0 || r.Cards.Length != n || d.Label.ActualPickedCard.Identifier is not { } id)
            return new(null, n, null);
        var actual = r.Cards.Where(c => c.CardIdentifier == id).OrderBy(c => c.CurrentPackRank).FirstOrDefault();
        return actual?.CurrentPackRank is not { } rank ? new(null, n, null)
            : new(rank, n, (r.Cards.Max(c => c.ContextualValue) - actual.ContextualValue) * 50);
    }
    public EvaluationSummary Evaluate(string[] models, EvaluationRegime regime, string outputJsonl, string build,
        string catalogHash, string statsHash, DatasetPartition? partition = null, IReadOnlySet<string>? sets = null,
        Action<long>? progress = null, CancellationToken token = default, ExperimentalQualitySubstitution? experimentalQuality = null)
    {
        foreach (var model in models) _ = HistoricalReplayAdapter.Version(model);
        if (models.Distinct().Count() != models.Length || models.Length == 0) throw new ArgumentException("Unique models required.");
        if (experimentalQuality is not null)
        {
            if (!models.SequenceEqual(new[]{"contextual-pick-score-v1.6.2"})) throw new ArgumentException("Q research requires frozen v1.6.2 only.");
            models=models.Concat(experimentalQuality.Models).ToArray();
        }
        var timer = Stopwatch.StartNew(); long decisions = 0, drafts = 0, calls = 0; double engineMs = 0;
        var metrics = new SortedDictionary<string, MetricAccumulator>(StringComparer.Ordinal);
        var missing = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var history = models.ToDictionary(m => m, _ => DraftPackObservationHistory.Empty);
        var past = new List<HistoricalPriorPick>(); string? scope = null;
        var comparisons = (from a in models.Select((m, i) => (m, i)) from b in models.Skip(a.i + 1) select new ComparisonAccumulator(a.m, b)).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputJsonl))!);
        using var writer = new StreamWriter(outputJsonl);
        foreach (var d in store.ReadDecisions(split, partition, sets, token))
        {
            token.ThrowIfCancellationRequested(); var input = d.Inputs;
            var draftScope = d.Provenance.Provider + ":" + input.DraftId;
            if (draftScope != scope)
            { scope = draftScope; drafts++; past.Clear(); foreach (var model in models) history[model] = DraftPackObservationHistory.Empty; }
            // Only past labels become known history, after their own scoring completed. Never look ahead.
            var prior = past.Concat(input.PriorPicks).GroupBy(p => (p.Pack, p.Pick)).Select(g => g.First()).OrderBy(p => p.Pack).ThenBy(p => p.Pick).ToArray();
            input = input with { PriorPicks = prior };
            var results = new Dictionary<string, HistoricalReplayResult>();
            foreach (var model in models)
            {
                var result = experimentalQuality is not null && experimentalQuality.Models.Contains(model)
                    ? experimentalQuality.Apply(input,results["contextual-pick-score-v1.6.2"],model)
                    : adapter.Replay(input, model, regime, history[model]);
                history[model] = result.ObservationHistory; results[model] = result;
                calls += result.OptimizerCalls; engineMs += result.TotalMilliseconds;
                var agreement = Agreement(d, result);
                var commitment = result.InputColorCommitment;
                foreach (var key in Keys(input, d.Outcome, d.PlayerMetadata, model, commitment))
                {
                    if (!metrics.TryGetValue(key, out var accumulator)) metrics[key] = accumulator = new();
                    accumulator.Add(agreement.Rank, agreement.Occurrences, agreement.ScoreGap,
                        result.Cards.Count(c => c.IsQualityEstimated), d.Label.ObservedMaindeckRate,
                        !input.PoolIsComplete||input.PoolBeforePick.Any(q=>q.Card.Identifier is null),
                        result.Cards.Count(c => c.QualityEvidence is PickScoreQualityEvidence.NoProviderRow or PickScoreQualityEvidence.ProviderRowWithoutUsableGih));
                }
                foreach (var reason in result.MissingInputs) missing[model + " | " + reason] = missing.GetValueOrDefault(model + " | " + reason) + 1;
            }
            foreach (var comparison in comparisons) comparison.Add(d, results[comparison.A], results[comparison.B]);
            writer.WriteLine(HistoricalJson.Serialize(new
            {
                d.DatasetId, input.SetCode, input.Format, input.DraftId, input.PackNumber, input.PickNumber, input.GlobalPickIndex,
                d.Provenance, AnalysisOnly = new { d.Label, d.Outcome, d.PlayerMetadata },
                Models = models.Select(m => new { Model = m, results[m].Cards, results[m].MissingInputs,
                    results[m].UnscorableCards, results[m].TotalMilliseconds, Agreement = Agreement(d, results[m]) })
            }));
            past.Add(new(input.PackNumber, input.PickNumber, d.Label.ActualPickedCard));
            decisions++; if (decisions % 100 == 0) progress?.Invoke(decisions);
        }
        return new("historical-evaluation-v1", regime.ToString(), "Human revealed-preference agreement; not pick correctness or causal outcome benefit.",
            build, models, HistoricalStore.HashText(HistoricalJson.Serialize(split)), catalogHash, statsHash, decisions, drafts,
            metrics.ToDictionary(p => p.Key, p => p.Value.Summary()), comparisons.Select(c => c.Summary()).ToArray(), store.Manifests(),
            timer.Elapsed.TotalSeconds, decisions / Math.Max(timer.Elapsed.TotalSeconds, .000001), Process.GetCurrentProcess().PeakWorkingSet64,
            calls, engineMs, missing, "Scoring accepts PickTimeState only; selected pick and DraftOutcome are joined after scoring. Lifetime skill buckets are strata only.",
            ["Prefix pilot is not random/representative; small game samples are not all-time format estimates.",
             "Mature aggregates may include future outcomes relative to draft time; this is not a chronological backtest.",
             "Historical pick 15 cannot enter frozen Domain. Other coordinates retain frozen 42/14 stage assumptions.",
             "No Arena slot order, pair stats or trophy corpus; source-column tie order and missing-input fallback are explicit.",
             "Observed maindeck rates do not identify individual copies or a final 23; game builds are not final deck choices.",
             "Common commitment strata use unchanged Phase9A profile, not candidate-dependent/model-dependent predictions."]);
    }
    private static IEnumerable<string> Keys(PickTimeState i, DraftOutcome o, HistoricalPlayerMetadata? metadata, string model, double commitment)
    {
        var root = model + " | "; yield return root + "overall";
        yield return root + "set=" + i.SetCode; yield return root + "format=" + i.Format;
        yield return root + "set-format=" + i.SetCode + "/" + i.Format;
        yield return root + "pack=" + i.PackNumber; yield return root + "pick=" + i.PickNumber;
        yield return root + "stage=" + i.PackNumber + "/" + i.PickNumber;
        yield return root + "card-count=" + i.OfferedCards.Sum(c => c.Count);
        yield return root + "rank=" + (metadata?.SourceRank ?? "unknown");
        yield return root + "commitment=" + (commitment < .25 ? "0-.25" : commitment < .5 ? ".25-.5" : commitment < .75 ? ".5-.75" : ".75-1");
        yield return root + "record=" + (o.FinalWins is { } wins && o.FinalLosses is { } losses ? $"{wins}-{losses}" : "unknown");
        yield return root + "reported-record=" + (o.ReportedEventWins is { } rw && o.ReportedEventLosses is { } rl ? $"{rw}-{rl}" : "unknown");
        yield return root + "outcome=" + (o.FinalWins is not { } w ? "unknown" : w <= 2 ? "0-2 wins" : w <= 4 ? "3-4 wins" : $"{w} wins");
        foreach (var skill in metadata?.LifetimeBuckets??new Dictionary<string,string>()) yield return root + "skill=" + skill.Key + "/" + skill.Value;
    }
    private sealed class ComparisonAccumulator(string a, string b)
    {
        public string A => a; public string B => b;
        long compared, changed, improved, regressed, missing;
        readonly List<ModelChange> scoreChanges = [], improvements = [], regressions = [];
        public void Add(HistoricalDraftDecision d, HistoricalReplayResult ra, HistoricalReplayResult rb)
        {
            var aa = Agreement(d, ra); var ab = Agreement(d, rb); compared++;
            var differentMissing = (aa.Rank is null) != (ab.Rank is null) || ra.UnscorableCards != rb.UnscorableCards
                || ra.Cards.Select(c => c.QualityEvidence).SequenceEqual(rb.Cards.Select(c => c.QualityEvidence)) == false;
            if (differentMissing) missing++;
            if (aa.Rank is null || ab.Rank is null) return;
            var ranking = !ra.Cards.OrderBy(c => c.CurrentPackRank).Select(c => c.PackIndex)
                .SequenceEqual(rb.Cards.OrderBy(c => c.CurrentPackRank).Select(c => c.PackIndex));
            if (ranking) changed++;
            if (ab.Rank < aa.Rank) improved++; if (ab.Rank > aa.Rank) regressed++;
            var delta = ra.Cards.Zip(rb.Cards).Max(p => Math.Abs((p.First.ContextualValue!.Value - p.Second.ContextualValue!.Value) * 50));
            var change = new ModelChange(d.DatasetId, d.Inputs.DraftId, d.Inputs.SetCode, d.Inputs.PackNumber, d.Inputs.PickNumber,
                a, b, aa.Rank, ab.Rank, ra.Cards.Single(c => c.CurrentPackRank == 1).CardIdentifier.Value,
                rb.Cards.Single(c => c.CurrentPackRank == 1).CardIdentifier.Value, delta, ranking, differentMissing);
            Keep(scoreChanges, change, c => c.LargestAbsoluteScoreChange);
            if (ab.Rank < aa.Rank) Keep(improvements, change, c => c.ActualRankA!.Value - c.ActualRankB!.Value);
            if (ab.Rank > aa.Rank) Keep(regressions, change, c => c.ActualRankB!.Value - c.ActualRankA!.Value);
        }
        private static void Keep(List<ModelChange> list, ModelChange change, Func<ModelChange, double> metric)
        { list.Add(change); list.Sort((x,y) => metric(y).CompareTo(metric(x))); if (list.Count > 20) list.RemoveAt(20); }
        public ModelComparison Summary() => new(a,b,compared,changed,improved,regressed,missing,scoreChanges.ToArray(),improvements.ToArray(),regressions.ToArray());
    }
}
