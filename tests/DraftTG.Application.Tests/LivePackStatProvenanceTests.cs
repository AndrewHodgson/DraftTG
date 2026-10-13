using System.Text.Json;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class LivePackStatProvenanceTests
{
    [Fact]
    public async Task FrozenRealPackPreservesProviderNullsIdentityProvenanceAndAllV16Scores()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "phase9f2-live-pack-stat-provenance.json")));
        var root = doc.RootElement;
        var data = new ScryfallCardCatalogDecoder().DecodeCatalogData(root.GetProperty("Cards").GetRawText());
        var frozenArena = new FrozenArena(root.GetProperty("Expected"));
        var resolver = new ArenaCardResolver(data, frozenArena);
        var grpIds = root.GetProperty("OrderedGrpIds").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        var identities = grpIds.Select(id => resolver.Resolve(ArenaCardIdentifier.Create(id)).CardIdentifier!).ToArray();
        Assert.Equal(14, identities.Length);
        Assert.Equal(root.GetProperty("OrderedIdentities").EnumerateArray().Select(n => n.GetString()), identities.Select(i => i.Value));
        Assert.All(grpIds, id => Assert.Equal(ArenaCardResolutionSource.ArenaDatabaseSetCollector,
            resolver.Resolve(ArenaCardIdentifier.Create(id)).Source));
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(1), PickNumber.Create(1)), identities), new(), DraftFormat.BestOfOne);
        var context = new LimitedStatisticsContext("FRA", LimitedStatisticsFormat.QuickDraft);
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-provenance-test-" + Guid.NewGuid().ToString("N"));
        var cacheDirectory = Path.Combine(directory, "limited-data", "17lands");
        Directory.CreateDirectory(cacheDirectory);
        try
        {
            // Exercise the production DTO/cache validator, not a second parser for normalized statistics.
            await File.WriteAllTextAsync(Path.Combine(cacheDirectory, "FRA_QuickDraft_ALL_TIME_v3.json"), root.GetProperty("ProviderCache").GetRawText());
            using var handler = new RejectNetwork(); using var http = new HttpClient(handler);
            var fetched = root.GetProperty("ProviderCache").GetProperty("FetchedAt").GetDateTimeOffset();
            var provider = new SeventeenLandsCardRatingsClient(http, new TestPaths(directory), new TestTime(fetched.AddMinutes(1)));
            var ratings = await provider.LoadAsync("FRA", SeventeenLandsFormat.QuickDraft);
            Assert.Equal(SeventeenLandsSource.Cache, ratings.Source);
            Assert.Equal(290, ratings.Rows.Count);
            var mapping = LimitedStatisticsMapper.Map(ratings.Rows, data.Catalog, context, snapshot);
            Assert.Null(mapping.Diagnostic); // Null fields do not mean the provider row is absent.
            var environment = new LimitedCardStatisticsCatalog(root.GetProperty("EnvironmentStatistics").EnumerateArray()
                .Select(s => new LimitedCardStatistics(CardIdentifier.Create(s.GetProperty("CardIdentifier").GetProperty("Value").GetString()!),
                    Count(s, "GameInHandGameCount"), GameInHandWinRate: Number(s, "GameInHandWinRate"), AverageLastSeenAt: Number(s, "AverageLastSeenAt"))));
            var result = new LimitedStatisticsLoadResult(context, context, mapping.Catalog, LimitedStatisticsSource.Cache, fetched, null)
            { CardCatalog = data.Catalog, EnvironmentCatalog = environment, StatisticsResolvedNames = mapping.ResolvedNames, StatisticsResolutions = mapping.Resolutions };
            var update = new LimitedStatisticsUpdate(snapshot, false, result, draftPool: new(snapshot.DraftedPool, DraftPoolCompleteness.Complete),
                pickScoreConfiguration: new(version: ContextualPickScoreVersion.V1_6));
            Assert.Equal(root.GetProperty("ExpectedBaseline").GetDouble(), update.Recommendation!.EnvironmentBaseline!.Value, 12);
            Assert.Equal("contextual-pick-score-v1.6", update.PickScores!.Configuration.ModelVersion);
            Assert.Equal("pick-score-tiers-v2", PickScoreTierThresholds.Version);
            Assert.Equal(0, update.PickScores.Metrics.OptimizerCalls);
            foreach (var expected in root.GetProperty("Expected").EnumerateArray())
            {
                var index = expected.GetProperty("PackIndex").GetInt32(); var id = identities[index];
                var score = update.PickScores.Cards[index]; var frozenScore = expected.GetProperty("Score");
                var statistic = mapping.Catalog.StatisticsFor(id)!;
                var raw = Assert.Single(expected.GetProperty("RawProviderRows").EnumerateArray());
                Assert.Equal(Number(raw, "ever_drawn_win_rate"), statistic.GameInHandWinRate);
                Assert.Equal(Count(raw, "ever_drawn_game_count"), statistic.GameInHandGameCount);
                Assert.Equal(Number(raw, "avg_seen"), statistic.AverageLastSeenAt);
                Assert.Equal(expected.GetProperty("LookupKey").GetString(), mapping.ResolvedNames[id]);
                Assert.Equal(expected.GetProperty("StatisticalIdentity").GetProperty("Kind").GetString(), mapping.Resolutions[id].Kind.ToString());
                Assert.Equal(expected.GetProperty("DisplayedScore").GetInt32(), score.Score0To50);
                Assert.Equal(expected.GetProperty("Rank").GetInt32(), score.CurrentPackRank);
                Assert.Equal(expected.GetProperty("ExactScore0To50").GetDouble(), score.ContextualValue!.Value * 50, 10);
                Assert.Equal(frozenScore.GetProperty("QualityEvidence").GetString(), score.QualityEvidence.ToString());
                Assert.Equal(frozenScore.GetProperty("QualityComponent").GetDouble(), score.QualityComponent!.Value, 12);
                Assert.Equal(frozenScore.GetProperty("EffectiveQualityComponent").GetDouble(), score.EffectiveQualityComponent!.Value, 12);
                Assert.Equal(1, score.QualityRelevance);
                Assert.Equal(.5, score.LaneComponent); Assert.Equal(.5, score.ArchetypeComponent); Assert.Equal(.5, score.DeckNeedComponent);
                Assert.Equal(new PickScoreWeights(.7, .2, .1, 0), score.Weights);
                Assert.Null(score.DeckImpact.AfterPair);
            }
            var uldaros = mapping.Catalog.StatisticsFor(identities[0])!;
            Assert.Null(uldaros.GameInHandWinRate); Assert.Equal(371, uldaros.GameInHandGameCount); Assert.Null(uldaros.AverageLastSeenAt);
            Assert.Contains("row has no usable GIH", update.Occurrences[new(0, identities[0])].DiagnosticText);
            Assert.Equal(0, handler.Requests);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static double? Number(JsonElement row, string field) => row.GetProperty(field).ValueKind == JsonValueKind.Null ? null : row.GetProperty(field).GetDouble();
    private static int? Count(JsonElement row, string field) => row.GetProperty(field).ValueKind == JsonValueKind.Null ? null : row.GetProperty(field).GetInt32();
    private sealed class FrozenArena(JsonElement expected) : IArenaPrintingIdentitySource
    {
        public ArenaCardPrintingIdentity? Find(int grpId) => expected.EnumerateArray()
            .Where(e => e.GetProperty("GrpId").GetInt32() == grpId)
            .Select(e => JsonSerializer.Deserialize<ArenaCardPrintingIdentity>(e.GetProperty("Arena").GetRawText())).SingleOrDefault();
    }
    private sealed class TestPaths(string directory) : IApplicationDataPathProvider
    { public string GetApplicationDataDirectory() => directory; }
    private sealed class TestTime(DateTimeOffset time) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => time; }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Requests++; throw new HttpRequestException("Frozen provenance regression must not access network."); }
    }
}
