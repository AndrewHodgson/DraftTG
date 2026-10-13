using System.Text.Json;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class SparsePrimaryQualityTests
{
    private static Card Card(string id, string name, string set = "FRA") => new(CardIdentifier.Create(id), name,
        ColorSet.Colorless, CardRarity.Common, CardSetCode.Create(set), CollectorNumber.Create(id));
    private static DraftSnapshot Snapshot(params Card[] cards) => new(new(new(PackNumber.Create(1), PickNumber.Create(1)), cards.Select(c => c.Identifier)), new(), DraftFormat.BestOfOne);
    private static SeventeenLandsRatingsResult Ratings(SeventeenLandsFormat format, params SeventeenLandsRating[] rows) =>
        new("FRA", format, rows, SeventeenLandsSource.Cache, DateTimeOffset.Parse("2026-10-05T00:00:00Z"),
            metadata: new("/api/card_data", "ALL_TIME", rows.Length));

    [Fact]
    public async Task ServiceKeepsPrimaryRowsHealthyScoresAndAlsaSeparateWhileUsingSafeFrontFaceQualityJoin()
    {
        var direct = Card("direct", "Direct"); var sparse = Card("sparse", "Sparse");
        var noRow = Card("no-row", "No Row"); var otherSet = Card("reprint", "Sparse", "OLD");
        var nullAlternate = Card("null-alt", "Null Alternate"); var missingAlternate = Card("missing-alt", "Missing Alternate");
        var face = Card("face", "Front // Back") with { GameplayMetadata = new(2, "{2}", "Creature", CardLayout.Prepare,
            [new("Front", "{2}", 2, ColorSet.Colorless, "Creature", null, null, null),
                new("Back", "{3}", 3, ColorSet.Colorless, "Sorcery", null, null, null)]) };
        var catalog = new CardCatalog([direct, sparse, noRow, otherSet, nullAlternate, missingAlternate, face]);
        var primary = Ratings(SeventeenLandsFormat.QuickDraft, new("Direct", 2000, GameInHandWinRate: .59, AverageLastSeenAt: 4),
            new("Sparse", 371), new("Null Alternate", 20), new("Missing Alternate", 20), new("Front", 100));
        var alternate = Ratings(SeventeenLandsFormat.PremierDraft, new("Direct", 10000, GameInHandWinRate: .9),
            new("Sparse", 3717, GameInHandWinRate: .69, AverageLastSeenAt: 1.7), new("No Row", 10000, GameInHandWinRate: .8),
            new("Null Alternate", 100), new("Front", 5000, GameInHandWinRate: .62));
        var client = new Client(primary, alternate);
        var snapshot = Snapshot(direct, sparse, noRow, otherSet, nullAlternate, missingAlternate, face);
        var loaded = await new LimitedStatisticsService(client, catalog).LoadAsync(new("FRA", LimitedStatisticsFormat.QuickDraft), snapshot);
        Assert.False(loaded.IsFallback); Assert.Equal(LimitedStatisticsFormat.QuickDraft, loaded.ActualSourceContext!.Format);
        Assert.Equal([SeventeenLandsFormat.QuickDraft, SeventeenLandsFormat.PremierDraft], client.Requests);
        Assert.Equal(2, loaded.AlternateQualityEvidence.Count);
        Assert.Null(loaded.Catalog.StatisticsFor(sparse.Identifier)!.GameInHandWinRate);
        Assert.Null(loaded.Catalog.StatisticsFor(sparse.Identifier)!.AverageLastSeenAt);
        Assert.Equal("Front", loaded.AlternateQualityEvidence[face.Identifier].ProviderName);
        var legacy = new LimitedStatisticsUpdate(snapshot, false, loaded, pickScoreConfiguration: new(version: ContextualPickScoreVersion.V1_6));
        var current = new LimitedStatisticsUpdate(snapshot, false, loaded);
        var repeated = new LimitedStatisticsUpdate(snapshot, false, loaded);
        Assert.Equal(legacy.PickScores!.Cards[0].ContextualValue, current.PickScores!.Cards[0].ContextualValue);
        Assert.Equal(PickScoreQualityProvenance.DirectPrimary, current.PickScores.Cards[0].QualityProvenance);
        Assert.Equal(PickScoreQualityProvenance.AlternateFormatPrior, current.PickScores.Cards[1].QualityProvenance);
        Assert.Equal(PickScoreQualityProvenance.AlternateFormatPrior, current.PickScores.Cards[6].QualityProvenance);
        Assert.Equal(.5, legacy.PickScores.Cards[1].QualityComponent);
        foreach (var index in new[] { 2, 3, 4, 5 }) Assert.Equal(PickScoreQualityProvenance.NeutralEstimate, current.PickScores.Cards[index].QualityProvenance);
        Assert.Equal("—", current.Cards[sparse.Identifier].AverageLastSeen);
        Assert.Contains("source FRA/PremierDraft", current.Occurrences[new(1, sparse.Identifier)].DiagnosticText);
        Assert.Equal(current.PickScores.Cards.Select(c => c.ContextualValue), repeated.PickScores!.Cards.Select(c => c.ContextualValue));
        Assert.Equal(legacy.PickScores.Cards.Select(c => (c.LaneComponent, c.ArchetypeComponent, c.DeckNeedComponent, c.QualityRelevance)),
            current.PickScores.Cards.Select(c => (c.LaneComponent, c.ArchetypeComponent, c.DeckNeedComponent, c.QualityRelevance)));
    }

    [Fact]
    public async Task UnavailableOrWrongContextAlternateRetainsSuccessfulPrimaryLoadAndNeutralFallback()
    {
        var direct = Card("direct", "Direct"); var sparse = Card("sparse", "Sparse");
        var primary = Ratings(SeventeenLandsFormat.QuickDraft, new("Direct", 1000, GameInHandWinRate: .57), new("Sparse", 371));
        foreach (var alternate in new[]
        {
            new SeventeenLandsRatingsResult("FRA", SeventeenLandsFormat.PremierDraft, [], SeventeenLandsSource.Unavailable),
            new SeventeenLandsRatingsResult("OLD", SeventeenLandsFormat.PremierDraft, [new("Sparse", 10000, GameInHandWinRate: .7)], SeventeenLandsSource.Cache,
                metadata: new("/api/card_data", "ALL_TIME", 1)),
            Ratings(SeventeenLandsFormat.TradDraft, new SeventeenLandsRating("Sparse", 10000, GameInHandWinRate: .7))
        })
        {
            var snapshot = Snapshot(direct, sparse);
            var loaded = await new LimitedStatisticsService(new Client(primary, alternate), new([direct, sparse])).LoadAsync(new("FRA", LimitedStatisticsFormat.QuickDraft), snapshot);
            Assert.Equal(LimitedStatisticsSource.Cache, loaded.Source); Assert.Empty(loaded.AlternateQualityEvidence);
            Assert.Equal(PickScoreQualityProvenance.NeutralEstimate, new LimitedStatisticsUpdate(snapshot, false, loaded).PickScores!.Cards[1].QualityProvenance);
        }
    }

    [Fact]
    public void FrozenRealPackReproducesV161WhileAllTenMeasuredCardsAndAllPrimaryFieldsStayUnchanged()
    {
        using var oldDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase9f2-live-pack-stat-provenance.json")));
        using var newDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase9f2-live-pack-v1.6.1.json")));
        var original = oldDoc.RootElement; var expected = newDoc.RootElement;
        var catalog = new ScryfallCardCatalogDecoder().DecodeCatalogData(original.GetProperty("Cards").GetRawText()).Catalog;
        var ids = original.GetProperty("OrderedIdentities").EnumerateArray().Select(e => CardIdentifier.Create(e.GetString()!)).ToArray();
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(1), PickNumber.Create(1)), ids), new(), DraftFormat.BestOfOne);
        LimitedCardStatistics ReadStat(JsonElement row) => new(CardIdentifier.Create(row.GetProperty("CardIdentifier").GetProperty("Value").GetString()!),
            Int(row, "GameInHandGameCount"), GameInHandWinRate: Number(row, "GameInHandWinRate"), AverageLastSeenAt: Number(row, "AverageLastSeenAt"));
        var statistics = new LimitedCardStatisticsCatalog(original.GetProperty("Expected").EnumerateArray().Select(r => ReadStat(r.GetProperty("Statistics"))));
        var environment = new LimitedCardStatisticsCatalog(original.GetProperty("EnvironmentStatistics").EnumerateArray().Select(ReadStat));
        var priors = expected.GetProperty("AlternateQualityEvidence").EnumerateArray().Select(e => new AlternateFormatQualityEvidence(
            CardIdentifier.Create(e.GetProperty("CardIdentifier").GetProperty("Value").GetString()!),
            new("FRA", LimitedStatisticsFormat.QuickDraft), new("FRA", LimitedStatisticsFormat.PremierDraft),
            e.GetProperty("RawGameInHandWinRate").GetDouble(), e.GetProperty("GameInHandGameCount").GetInt32(), e.GetProperty("SourceEnvironmentBaseline").GetDouble(),
            e.GetProperty("ProviderName").GetString()!, e.GetProperty("FetchedAt").GetDateTimeOffset(), e.GetProperty("Source").GetString()!, e.GetProperty("TimePeriod").GetString()!)).ToDictionary(e => e.CardIdentifier);
        foreach (var evidence in priors.Values)
        {
            var raw = expected.GetProperty("AlternateProviderCache").GetProperty("Payload").GetProperty("data").EnumerateArray().Single(r => r.GetProperty("name").GetString() == evidence.ProviderName);
            Assert.Equal(raw.GetProperty("ever_drawn_win_rate").GetDouble(), evidence.RawGameInHandWinRate);
            Assert.Equal(raw.GetProperty("ever_drawn_game_count").GetInt32(), evidence.GameInHandGameCount);
        }
        var context = new LimitedStatisticsContext("FRA", LimitedStatisticsFormat.QuickDraft);
        var resolutions = original.GetProperty("Expected").EnumerateArray().ToDictionary(r => CardIdentifier.Create(r.GetProperty("Identifier").GetProperty("Value").GetString()!),
            r => new StatisticalIdentityResolution(Enum.Parse<StatisticalIdentityKind>(r.GetProperty("StatisticalIdentity").GetProperty("Kind").GetString()!), r.GetProperty("LookupKey").GetString()));
        var loaded = new LimitedStatisticsLoadResult(context, context, statistics, LimitedStatisticsSource.Cache, null, null)
        { CardCatalog = catalog, EnvironmentCatalog = environment, AlternateQualityEvidence = priors, StatisticsResolutions = resolutions };
        var before = new LimitedStatisticsUpdate(snapshot, false, loaded, pickScoreConfiguration: new(version: ContextualPickScoreVersion.V1_6));
        var after = new LimitedStatisticsUpdate(snapshot, false, loaded, pickScoreConfiguration:new(version:ContextualPickScoreVersion.V1_6_1));
        var current = new LimitedStatisticsUpdate(snapshot,false,loaded,pickScoreConfiguration:new(version:ContextualPickScoreVersion.V1_6_2));
        Assert.Equal(after.PickScores!.Cards.Select(c=>(c.ContextualValue,c.QualityPrior,c.QualityProvenance)),
            current.PickScores!.Cards.Select(c=>(c.ContextualValue,c.QualityPrior,c.QualityProvenance)));
        Assert.Equal("contextual-pick-score-v1.6.1", after.PickScores!.Configuration.ModelVersion);
        foreach (var row in expected.GetProperty("Cards").EnumerateArray())
        {
            var i = row.GetProperty("PackIndex").GetInt32(); var score = after.PickScores.Cards[i];
            Assert.Equal(row.GetProperty("AfterExact").GetDouble(), 50*score.ContextualValue!.Value, 10);
            Assert.Equal(row.GetProperty("After").GetProperty("Score0To50").GetInt32(), score.Score0To50);
            Assert.Equal(row.GetProperty("After").GetProperty("CurrentPackRank").GetInt32(), score.CurrentPackRank);
            if (before.PickScores!.Cards[i].Availability == PickScoreAvailability.Measured)
                Assert.Equal(before.PickScores.Cards[i].ContextualValue, score.ContextualValue);
            Assert.Equal(before.Cards[ids[i]], after.Cards[ids[i]]);
        }
        var uldaros = after.PickScores.Cards[0];
        Assert.Equal(48, uldaros.Score0To50); Assert.Equal(1, uldaros.CurrentPackRank);
        Assert.Equal(PickScoreQualityProvenance.AlternateFormatPrior, uldaros.QualityProvenance);
        Assert.Null(after.Occurrences[new(0, ids[0])].RawGIH); Assert.Null(after.Occurrences[new(0, ids[0])].ALSA);
        Assert.Equal(PickScoreAvailability.EstimatedMissingStatistics, uldaros.Availability);
        Assert.Equal(PickScoreQualityProvenance.FrontFacePrimary, after.PickScores.Cards[10].QualityProvenance);
        Assert.Equal(30.00447708798361, after.PickScores.Cards[6].ContextualValue!.Value * 50, 10);
    }

    private static double? Number(JsonElement e, string key) => e.GetProperty(key).ValueKind == JsonValueKind.Null ? null : e.GetProperty(key).GetDouble();
    private static int? Int(JsonElement e, string key) => e.GetProperty(key).ValueKind == JsonValueKind.Null ? null : e.GetProperty(key).GetInt32();
    private sealed class Client(SeventeenLandsRatingsResult primary, SeventeenLandsRatingsResult alternate) : ISeventeenLandsCardRatingsClient
    {
        public List<SeventeenLandsFormat> Requests { get; } = [];
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format, bool forceRefresh = false, CancellationToken cancellationToken = default)
        { Requests.Add(format); return Task.FromResult(format == SeventeenLandsFormat.QuickDraft ? primary : alternate); }
    }
}
