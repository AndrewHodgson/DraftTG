using System.Text.Json;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class LiveP1P8FailureReproductionTests
{
    [Theory]
    [InlineData(ContextualPickScoreVersion.V1_6)]
    [InlineData(ContextualPickScoreVersion.V1_6_1)]
    [InlineData(ContextualPickScoreVersion.V1_6_2)]
    public void RealP1P8KeepsLegacyResultsAndCorrectsCommitmentRelevanceOnlyInV162(ContextualPickScoreVersion version)
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
        using var comparison = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase9f2-live-pack-v1.6.1.json")));
        using var v162 = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase9e2b-p1p8-v1.6.2.json")));
        var alternateRows = comparison.RootElement.GetProperty("AlternateProviderCache").GetProperty("Payload").GetProperty("data").EnumerateArray()
            .Select(r => new SeventeenLandsRating(r.GetProperty("name").GetString()!,
                r.GetProperty("ever_drawn_game_count").ValueKind == JsonValueKind.Null ? null : r.GetProperty("ever_drawn_game_count").GetInt32(),
                GameInHandWinRate: r.GetProperty("ever_drawn_win_rate").ValueKind == JsonValueKind.Null ? null : r.GetProperty("ever_drawn_win_rate").GetDouble())).ToArray();
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
            var alternate = LimitedStatisticsMapper.Map(alternateRows, catalog, new("FRA", LimitedStatisticsFormat.PremierDraft), snapshot);
            var quality = snapshot.CurrentPack.AvailableCardIdentifiers.Where(id => result.Catalog.StatisticsFor(id) is { } primary
                && !SparsePrimaryQualityPrior.HasUsableGih(primary) && SparsePrimaryQualityPrior.HasUsableGih(alternate.Catalog.StatisticsFor(id)))
                .ToDictionary(id => id, id => new AlternateFormatQualityEvidence(id, context, new("FRA", LimitedStatisticsFormat.PremierDraft),
                    alternate.Catalog.StatisticsFor(id)!.GameInHandWinRate!.Value, alternate.Catalog.StatisticsFor(id)!.GameInHandGameCount!.Value,
                    comparison.RootElement.GetProperty("AlternateBaseline").GetDouble(), alternate.ResolvedNames[id], null, "Cache", "ALL_TIME"));
            result = result with { AlternateQualityEvidence = quality };
            last = new(snapshot, false, result, observationHistory: history,
                draftPool: new(snapshot.DraftedPool, DraftPoolCompleteness.Complete), pickScoreConfiguration: new(version: version));
            history = last.ObservationHistory;
        }
        Assert.Equal(7, last!.Snapshot!.DraftedPool.Count);
        Assert.Equal(root.GetProperty("ExpectedBaseline").GetDouble(), last.Recommendation!.EnvironmentBaseline!.Value, 12);
        foreach (var expected in root.GetProperty("ExpectedP1P8").EnumerateArray())
        {
            var score = last.PickScores!.Cards.Single(s => s.CardIdentifier.Value == expected.GetProperty("Identity").GetString());
            if (version == ContextualPickScoreVersion.V1_6)
            {
                Assert.Equal(expected.GetProperty("DisplayedScore").GetInt32(), score.Score0To50);
                Assert.Equal(expected.GetProperty("Rank").GetInt32(), score.CurrentPackRank);
                Assert.Equal(expected.GetProperty("ExactScore0To50").GetDouble(), score.ContextualValue!.Value * 50, 10);
            }
            else if(version == ContextualPickScoreVersion.V1_6_1)
            {
                var newer = comparison.RootElement.GetProperty("P1P8Comparison").EnumerateArray()
                    .Single(r => r.GetProperty("CardIdentifier").GetProperty("Value").GetString() == score.CardIdentifier.Value);
                Assert.Equal(newer.GetProperty("AfterExact").GetDouble(), score.ContextualValue!.Value*50, 10);
                Assert.Equal(newer.GetProperty("After").GetProperty("CurrentPackRank").GetInt32(), score.CurrentPackRank);
            }
            else
            {
                var expectedNew=v162.RootElement.GetProperty("Cards").EnumerateArray().Single(r=>r.GetProperty("Identity").GetString()==score.CardIdentifier.Value);
                Assert.Equal(expectedNew.GetProperty("AfterExact").GetDouble(),score.ContextualValue!.Value*50,10);
                Assert.Equal(expectedNew.GetProperty("After").GetProperty("CurrentPackRank").GetInt32(),score.CurrentPackRank);
                var previous=expectedNew.GetProperty("Score");
                Assert.Equal(previous.GetProperty("QualityComponent").GetDouble(),score.QualityComponent!.Value,12);
                Assert.Equal(previous.GetProperty("LaneComponent").GetDouble(),score.LaneComponent,12);
                Assert.Equal(previous.GetProperty("ArchetypeComponent").GetDouble(),score.ArchetypeComponent,12);
                Assert.Equal(previous.GetProperty("DeckNeedComponent").GetDouble(),score.DeckNeedComponent,12);
                Assert.Equal(previous.GetProperty("DeckImpact").GetProperty("Playability").GetDouble(),score.DeckImpact.Playability,12);
            }
        }
        var unsummon = last.PickScores!.Cards.Single(s => catalog.Find(s.CardIdentifier)!.Name == "Unsummon");
        var green = last.PickScores.Cards.Single(s => catalog.Find(s.CardIdentifier)!.Name == "Arcane Amphisbaena");
        Assert.Equal(new ContextualPickScoreConfiguration(version: version).ModelVersion, unsummon.ModelVersion);
        Assert.Equal(0, unsummon.DeckImpact.Playability);
        if(version == ContextualPickScoreVersion.V1_6_2)
        {
            Assert.True(green.ContextualValue>unsummon.ContextualValue);
            Assert.Equal(1,green.CurrentPackRank);Assert.Equal(2,unsummon.CurrentPackRank);
            Assert.InRange(unsummon.QualityRelevance,.05,.15);Assert.InRange(unsummon.ColorCommitment!.Strength,.9,1);
            Assert.Equal(1,unsummon.BaseQualityRelevance);
            foreach(var picks in new[]{0,1,2})
            {
                var open=new DraftSnapshot(new(new(PackNumber.Create(1),PickNumber.Create(picks+1)),last.Snapshot.CurrentPack.AvailableCardIdentifiers),
                    new(last.Snapshot.History.Picks.Take(picks)),DraftFormat.BestOfOne)
                {RecoveredPool=new(last.Snapshot.DraftedPool.CardIdentifiers.Take(picks))};
                var resultOpen=new LimitedStatisticsUpdate(open,false,result,draftPool:new(open.DraftedPool,DraftPoolCompleteness.Complete),
                    pickScoreConfiguration:new(version:version));
                var blue=resultOpen.PickScores!.Cards.Single(c=>c.CardIdentifier==unsummon.CardIdentifier);
                Assert.Equal(1,blue.CurrentPackRank);Assert.Equal(unsummon.QualityComponent,blue.QualityComponent);
            }
            Assert.Contains("current RG plan",last.Occurrences[new(unsummon.PackIndex,unsummon.CardIdentifier)].DiagnosticText);
        }
        else
        {
            Assert.Equal(1,unsummon.QualityRelevance);
            Assert.Equal(28.70495083517041,unsummon.ContextualValue!.Value*50,10);
        }
        Assert.Equal(25.501120969070218, green.ContextualValue!.Value*50, 10);
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
