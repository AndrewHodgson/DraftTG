using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DraftTG.Application;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

internal static class SparseQualityReplay
{
    internal static async Task RunAsync(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--sparse-quality-replay <frozen v1.6 live-pack fixture> <output.json>");
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(args[0])); var root = fixture.RootElement;
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DraftTG");
        var bulk = Path.Combine(local, "card-data", "scryfall-default-cards.jsonl.gz");
        if (Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(bulk))) != root.GetProperty("ScryfallBulkSha256").GetString())
            throw new InvalidDataException("Historical catalog cache changed; do not silently rewrite this replay.");
        var data = await new ScryfallJsonlGzipCardDataLoader().LoadAsync(bulk);
        var ids = root.GetProperty("OrderedIdentities").EnumerateArray().Select(i => CardIdentifier.Create(i.GetString()!)).ToArray();
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(1), PickNumber.Create(1)), ids), new(), DraftFormat.BestOfOne);
        var context = new LimitedStatisticsContext("FRA", LimitedStatisticsFormat.QuickDraft);
        using var handler = new NoNetwork(); using var http = new HttpClient(handler);
        var responses = new Dictionary<SeventeenLandsFormat, SeventeenLandsRatingsResult>();
        var rawCaches = new Dictionary<SeventeenLandsFormat, JsonElement>();
        foreach (var format in new[] { SeventeenLandsFormat.QuickDraft, SeventeenLandsFormat.PremierDraft })
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(local, "limited-data", "17lands", $"FRA_{format}_ALL_TIME_v3.json"));
            if (format == SeventeenLandsFormat.QuickDraft && Convert.ToHexString(SHA256.HashData(bytes)) != root.GetProperty("ProviderCacheSha256").GetString())
                throw new InvalidDataException("Historical primary statistics cache changed.");
            var raw = JsonSerializer.Deserialize<JsonElement>(bytes); rawCaches.Add(format, raw);
            var client = new SeventeenLandsCardRatingsClient(http, new Paths(local), new FrozenTime(raw.GetProperty("FetchedAt").GetDateTimeOffset().AddMinutes(1)));
            var response = await client.LoadAsync("FRA", format);
            if (response.Source != SeventeenLandsSource.Cache) throw new InvalidDataException("Validated offline cache required.");
            responses.Add(format, response);
        }
        var service = new LimitedStatisticsService(new FrozenRatings(responses), data.Catalog);
        var loaded = await service.LoadAsync(context, snapshot);
        var before = new LimitedStatisticsUpdate(snapshot, false, loaded, pickScoreConfiguration: new(version: ContextualPickScoreVersion.V1_6));
        var after = new LimitedStatisticsUpdate(snapshot, false, loaded, pickScoreConfiguration: new(version: ContextualPickScoreVersion.V1_6_1));
        var cards = after.PickScores!.Cards.Select(score => new
        {
            GrpId = root.GetProperty("OrderedGrpIds")[score.PackIndex].GetInt32(), Name = data.Catalog.Find(score.CardIdentifier)!.Name,
            score.CardIdentifier, score.PackIndex, Primary = loaded.Catalog.StatisticsFor(score.CardIdentifier),
            Before = before.PickScores!.Cards[score.PackIndex], After = score,
            BeforeExact = before.PickScores.Cards[score.PackIndex].ContextualValue!.Value * 50,
            AfterExact = score.ContextualValue!.Value * 50,
            Diagnostics = after.Occurrences[new(score.PackIndex, score.CardIdentifier)].DiagnosticText
        }).ToArray();
        var p1p8 = await ReplayP1P8Async(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "phase9e2b-p1p8-rg-failure.json"), service, data.Catalog);
        var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            Source = "Frozen real FRA P1P1, offline v1.6/v1.6.1 comparison; not new live evidence",
            PrimaryBaseline = before.Recommendation!.EnvironmentBaseline,
            AlternateBaseline = loaded.AlternateQualityEvidence.Values.First().SourceEnvironmentBaseline,
            AlternateQualityEvidence = loaded.AlternateQualityEvidence.Values.OrderBy(e => e.CardIdentifier.Value),
            AlternateProviderCache = rawCaches[SeventeenLandsFormat.PremierDraft], Cards = cards,
            P1P8Comparison = p1p8, NetworkRequests = handler.Requests,
            Limits = "Only unavailable primary GIH can borrow quality. Primary statistics, ALSA and all contextual stages remain unchanged."
        }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
        foreach (var card in cards.OrderBy(c => c.After.CurrentPackRank))
            Console.WriteLine($"{card.GrpId} {card.Name}: {card.Before.Score0To50} #{card.Before.CurrentPackRank} -> {card.After.Score0To50} #{card.After.CurrentPackRank}; Q {card.After.QualityComponent:R}; exact {card.AfterExact:R}; {card.After.QualityProvenance}");
        Console.WriteLine($"Network requests: {handler.Requests}; output {output}");
    }

    private static async Task<object> ReplayP1P8Async(string path, LimitedStatisticsService service, CardCatalog catalog)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var context = new LimitedStatisticsContext("FRA", LimitedStatisticsFormat.QuickDraft);
        var history = DraftPackObservationHistory.Empty;
        LimitedStatisticsUpdate? before = null, after = null;
        foreach (var pack in doc.RootElement.GetProperty("Packs").EnumerateArray())
        {
            var snapshot = new DraftSnapshot(new(new(PackNumber.Create(pack.GetProperty("Pack").GetInt32()), PickNumber.Create(pack.GetProperty("Pick").GetInt32())),
                pack.GetProperty("Offered").EnumerateArray().Select(i => CardIdentifier.Create(i.GetString()!))),
                new(pack.GetProperty("History").EnumerateArray().Select(p => new DraftPick(new(PackNumber.Create(p.GetProperty("Pack").GetInt32()), PickNumber.Create(p.GetProperty("Pick").GetInt32())),
                    CardIdentifier.Create(p.GetProperty("Identity").GetString()!)))), DraftFormat.BestOfOne)
            { RecoveredPool = new(pack.GetProperty("Pool").EnumerateArray().Select(i => CardIdentifier.Create(i.GetString()!))) };
            var loaded = await service.LoadAsync(context, snapshot);
            var pool = new DraftPoolSnapshot(snapshot.DraftedPool, DraftPoolCompleteness.Complete);
            before = new(snapshot, false, loaded, observationHistory: history, draftPool: pool, pickScoreConfiguration: new(version: ContextualPickScoreVersion.V1_6));
            after = new(snapshot, false, loaded, observationHistory: history, draftPool: pool,
                pickScoreConfiguration: new(version: ContextualPickScoreVersion.V1_6_1));
            history = before.ObservationHistory;
        }
        foreach (var row in doc.RootElement.GetProperty("ExpectedP1P8").EnumerateArray())
        {
            var score = before!.PickScores!.Cards.Single(c => c.CardIdentifier.Value == row.GetProperty("Identity").GetString());
            if (Math.Abs(score.ContextualValue!.Value*50 - row.GetProperty("ExactScore0To50").GetDouble()) > 1e-9)
                throw new InvalidDataException("Historical P1P8 primary inputs changed; cannot assert incidentally unchanged quality.");
        }
        return after!.PickScores!.Cards.Select(score => new
        {
            Name = catalog.Find(score.CardIdentifier)!.Name, score.CardIdentifier, score.PackIndex,
            Before = before!.PickScores!.Cards[score.PackIndex], After = score,
            BeforeExact = before.PickScores.Cards[score.PackIndex].ContextualValue!.Value*50,
            AfterExact = score.ContextualValue!.Value*50
        }).ToArray();
    }

    private sealed class FrozenRatings(Dictionary<SeventeenLandsFormat, SeventeenLandsRatingsResult> responses) : ISeventeenLandsCardRatingsClient
    {
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format, bool forceRefresh = false, CancellationToken cancellationToken = default)
        { if (expansion != "FRA") throw new InvalidDataException("Exact FRA required."); return Task.FromResult(responses[format]); }
    }
}
