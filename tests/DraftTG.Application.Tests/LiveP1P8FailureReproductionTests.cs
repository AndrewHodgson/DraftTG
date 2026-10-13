using System.Text.Json;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class LiveP1P8FailureReproductionTests
{
    [Fact]
    public void RealP1P8ReproducesReportedRankingAndExposesQualityRelevanceDespiteZeroPlayability()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase9e2b-p1p8-rg-failure.json")));
        var root = doc.RootElement;
        var catalog = new ScryfallCardCatalogDecoder().DecodeCatalogData(root.GetProperty("Cards").GetRawText()).Catalog;
        LimitedCardStatisticsCatalog Statistics(string field) => new(root.GetProperty(field).EnumerateArray().Select(s =>
            new LimitedCardStatistics(CardIdentifier.Create(s.GetProperty("Identity").GetString()!),
                s.GetProperty("GameInHandGameCount").ValueKind == JsonValueKind.Null ? null : s.GetProperty("GameInHandGameCount").GetInt32(),
                GameInHandWinRate: s.GetProperty("GameInHandWinRate").ValueKind == JsonValueKind.Null ? null : s.GetProperty("GameInHandWinRate").GetDouble(),
                AverageLastSeenAt: s.GetProperty("AverageLastSeenAt").ValueKind == JsonValueKind.Null ? null : s.GetProperty("AverageLastSeenAt").GetDouble())));
        var context = new LimitedStatisticsContext("FRA", LimitedStatisticsFormat.QuickDraft);
        var result = new LimitedStatisticsLoadResult(context, context, Statistics("ParticipantStatistics"), LimitedStatisticsSource.Cache, null, null)
        { CardCatalog = catalog, EnvironmentCatalog = Statistics("Statistics") };
        var history = DraftPackObservationHistory.Empty;
        LimitedStatisticsUpdate? last = null;
        foreach (var pack in root.GetProperty("Packs").EnumerateArray())
        {
            CardIdentifier Id(JsonElement id) => CardIdentifier.Create(id.GetString()!);
            var snapshot = new DraftSnapshot(new(new(PackNumber.Create(pack.GetProperty("Pack").GetInt32()), PickNumber.Create(pack.GetProperty("Pick").GetInt32())),
                pack.GetProperty("Offered").EnumerateArray().Select(Id)),
                new(pack.GetProperty("History").EnumerateArray().Select(p => new DraftPick(
                    new(PackNumber.Create(p.GetProperty("Pack").GetInt32()), PickNumber.Create(p.GetProperty("Pick").GetInt32())),
                    CardIdentifier.Create(p.GetProperty("Identity").GetString()!)))), DraftFormat.BestOfOne)
            { RecoveredPool = new(pack.GetProperty("Pool").EnumerateArray().Select(Id)) };
            last = new(snapshot, false, result, observationHistory: history,
                draftPool: new(snapshot.DraftedPool, DraftPoolCompleteness.Complete));
            history = last.ObservationHistory;
        }
        Assert.Equal(7, last!.Snapshot!.DraftedPool.Count);
        Assert.Equal(root.GetProperty("ExpectedBaseline").GetDouble(), last.Recommendation!.EnvironmentBaseline!.Value, 12);
        foreach (var expected in root.GetProperty("ExpectedP1P8").EnumerateArray())
        {
            var score = last.PickScores!.Cards.Single(s => s.CardIdentifier.Value == expected.GetProperty("Identity").GetString());
            Assert.Equal(expected.GetProperty("DisplayedScore").GetInt32(), score.Score0To50);
            Assert.Equal(expected.GetProperty("Rank").GetInt32(), score.CurrentPackRank);
            Assert.Equal(expected.GetProperty("ExactScore0To50").GetDouble(), score.ContextualValue!.Value * 50, 10);
        }
        var unsummon = last.PickScores!.Cards.Single(s => catalog.Find(s.CardIdentifier)!.Name == "Unsummon");
        var green = last.PickScores.Cards.Single(s => catalog.Find(s.CardIdentifier)!.Name == "Arcane Amphisbaena");
        Assert.Equal("contextual-pick-score-v1.6", unsummon.ModelVersion);
        Assert.Equal(0, unsummon.DeckImpact.Playability); Assert.Equal(1, unsummon.QualityRelevance);
        Assert.Equal("RG", unsummon.DeckImpact.AfterPair!.Code); Assert.Equal(1, green.DeckImpact.Playability);
        Assert.True(unsummon.Weights.DeckNeed < .013);
        var audit = new List<CandidateProjectionAudit>();
        var evaluator = new CandidateDeckImpactEvaluator();
        var input = new DeckBuildInput(new(last.Snapshot.DraftedPool, DraftPoolCompleteness.Complete), catalog, result.Catalog, result.EnvironmentCatalog);
        var without = evaluator.Evaluate(last.Snapshot, input, last.ContextualRecommendation!.Profile);
        var with = evaluator.Evaluate(last.Snapshot, input, last.ContextualRecommendation.Profile, diagnostic: audit.Add);
        Assert.Equal(without.Select(i => (i.DeckNeed, i.Playability, i.CutlineMargin)), with.Select(i => (i.DeckNeed, i.Playability, i.CutlineMargin)));
        Assert.All(audit, a => Assert.Equal("RG", Assert.Single(a.Pairs).Pair));
    }
}
