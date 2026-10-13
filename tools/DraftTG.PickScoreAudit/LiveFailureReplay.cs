using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

/// <summary>Offline reconstruction using the production parser, adapter, cached statistics and score pipeline.</summary>
internal static class LiveFailureReplay
{
    internal static async Task RunAsync(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--live-failure <saved Player.log> <output directory>");
        var logPath = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DraftTG");
        var recordedPacks = new Dictionary<string, int[]>();
        foreach (var row in await File.ReadAllLinesAsync(Path.Combine(local, "localization", "deterministic-shadow.jsonl")))
        {
            using var record = JsonDocument.Parse(row);
            var item = record.RootElement;
            if (!item.TryGetProperty("SessionId", out var sid) || sid.GetString() != "0bd92f4fccd6484c91cbc34654ff8097"
                || item.GetProperty("Kind").GetString() != "PackObservation") continue;
            recordedPacks[$"{item.GetProperty("Pack").GetInt32()}/{item.GetProperty("Pick").GetInt32()}"] =
                item.GetProperty("PredictedOrderIdentities")[0].EnumerateArray().Select(v => v.GetInt32()).Order().ToArray();
        }
        var engine = new ArenaDraftStateEngine(); var parser = new ArenaDraftLogParser();
        var states = new List<ArenaDraftStateSnapshot>(); var seen = new HashSet<string>(); var errors = new List<object>(); var lineNumber = 0;
        var lines = await File.ReadAllLinesAsync(logPath);
        foreach (var line in lines)
        {
            lineNumber++;
            try
            {
                foreach (var fact in parser.Parse(new ArenaLogSourceEvent.Line(line)))
                {
                    engine.Apply(fact);
                    var state = engine.Current;
                    if (state.CurrentPack is not { } pack) continue;
                    var key = $"{pack.Coordinate.Pack}/{pack.Coordinate.Pick}";
                    if (!recordedPacks.TryGetValue(key, out var expected) || !pack.CardIdentifiers.Select(a => a.Value).Order().SequenceEqual(expected)) continue;
                    if (seen.Add(key)) states.Add(state);
                }
            }
            catch (Exception ex) when (ex is ArenaDraftLogParseException or ArenaDraftStateConflictException) { errors.Add(new { Line = lineNumber, Kind = ex.GetType().Name, Message = ex.Message,
                Pack = engine.Current.CurrentPack?.Coordinate.Pack, Pick = engine.Current.CurrentPack?.Coordinate.Pick }); }
        }
        if (!states.Any(s => s.CurrentPack!.Coordinate.Pack == 1 && s.CurrentPack.Coordinate.Pick == 8))
            throw new InvalidDataException("The requested real-draft P1P8 was not recovered.");
        using var cache = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(local, "limited-data", "17lands", "FRA_QuickDraft_ALL_TIME_v3.json")));
        var names = cache.RootElement.GetProperty("Payload").GetProperty("data").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToHashSet();
        var selected = new List<string>();
        using (var file = File.OpenRead(Path.Combine(local, "card-data", "scryfall-default-cards.jsonl.gz")))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
            while (await reader.ReadLineAsync() is { } line)
            {
                using var item = JsonDocument.Parse(line);
                if (item.RootElement.GetProperty("set").GetString() == "fra" || names.Contains(item.RootElement.GetProperty("name").GetString())) selected.Add(line);
            }
        var data = new ScryfallCardCatalogDecoder().DecodeCatalogData("[" + string.Join(",", selected) + "]");
        var catalog = data.Catalog;
        var resolver = new ArenaCardResolver(data, ArenaDatabasePrintingIdentitySource.CreateDefault());
        var adapter = new ArenaDraftSnapshotAdapter(resolver);
        using var handler = new NoNetwork(); using var http = new HttpClient(handler);
        var client = new SeventeenLandsCardRatingsClient(http, new Paths(local), new FrozenTime(cache.RootElement.GetProperty("FetchedAt").GetDateTimeOffset() + TimeSpan.FromMinutes(1)));
        var ratings = await client.LoadAsync("FRA", SeventeenLandsFormat.QuickDraft);
        var context = new LimitedStatisticsContext("FRA", LimitedStatisticsFormat.QuickDraft);
        var environment = LimitedStatisticsMapper.MapEnvironment(ratings.Rows, catalog, context);
        var history = DraftPackObservationHistory.Empty;
        var participantStatistics = new Dictionary<CardIdentifier, LimitedCardStatistics>();
        var results = new List<object>(); var fixturePacks = new List<object>();
        foreach (var state in states)
        {
            var resolved = adapter.Convert(state);
            var snapshot = resolved.Snapshot ?? throw new InvalidDataException($"{state.CurrentPack!.Coordinate}: {resolved.Availability}");
            var mapped = LimitedStatisticsMapper.Map(ratings.Rows, catalog, context, snapshot);
            foreach (var statistic in mapped.Catalog.Entries) participantStatistics[statistic.CardIdentifier] = statistic;
            var result = new LimitedStatisticsLoadResult(context, context, mapped.Catalog, LimitedStatisticsSource.Cache, ratings.FetchedAt, null)
            { CardCatalog = catalog, EnvironmentCatalog = environment };
            var update = new LimitedStatisticsUpdate(snapshot, false, result, observationHistory: history, draftPool: resolved.DraftPool,
                pickScoreConfiguration: new(version: ContextualPickScoreVersion.V1_6));
            history = update.ObservationHistory;
            var projections = new List<CandidateProjectionAudit>();
            _ = new CandidateDeckImpactEvaluator().Evaluate(snapshot,
                new(resolved.DraftPool!, catalog, mapped.Catalog, environment), update.ContextualRecommendation!.Profile,
                environmentBaseline: update.Recommendation!.EnvironmentBaseline, diagnostic: projections.Add);
            var cards = update.PickScores!.Cards.Select(score => new
            {
                Name = catalog.Find(score.CardIdentifier)?.Name, Identity = score.CardIdentifier.Value,
                ArenaId = state.CurrentPack!.CardIdentifiers[score.PackIndex].Value, score.PackIndex,
                RawStatistics = mapped.Catalog.StatisticsFor(score.CardIdentifier), score,
                ExactScore0To50 = score.ContextualValue * 50, DisplayedScore = score.Score0To50,
                Explanation = PickScorePresentation.Diagnostics(score),
                Statistical = update.Recommendation!.Cards[score.PackIndex], Lane = update.LaneRecommendation!.Cards[score.PackIndex],
                Archetype = update.ArchetypeRecommendation!.Cards[score.PackIndex],
                Projections = projections.Where(p => p.Candidate == score.CardIdentifier).SelectMany(p => p.Pairs).Select(p => new
                { p.Pair, p.Quality, p.Spells, p.Shortfall, p.BlendWeight, p.CandidateEligibility, p.CandidateInDeck, p.CutlineMargin,
                    Deck = p.Main.Select(e => new { Identity = e.Key.Value, Name = catalog.Find(e.Key)?.Name, Count = e.Value }) })
            }).ToArray();
            results.Add(new { Pack = state.CurrentPack!.Coordinate.Pack, Pick = state.CurrentPack!.Coordinate.Pick,
                Mode = state.Mode!.ToString(), Pool = state.DraftedPool.Occurrences.Select(a => new { ArenaId = a.Value,
                    Identity = resolver.Resolve(a).CardIdentifier?.Value, Name = catalog.Find(resolver.Resolve(a).CardIdentifier!)?.Name }),
                PoolCompleteness = resolved.DraftPool?.Completeness.ToString(),
                Baseline = update.Recommendation!.EnvironmentBaseline, Cards = cards,
                Commitment = update.ContextualRecommendation!.Profile, LaneProfile = update.LaneRecommendation!.Profile });
            fixturePacks.Add(new { Pack = state.CurrentPack!.Coordinate.Pack, Pick = state.CurrentPack!.Coordinate.Pick,
                Offered = snapshot.CurrentPack.AvailableCardIdentifiers.Select(i => i.Value),
                Pool = snapshot.DraftedPool.CardIdentifiers.Select(i => i.Value),
                History = snapshot.History.Picks.Select(p => new { Pack = p.Position.Pack.Value, Pick = p.Position.Pick.Value, Identity = p.SelectedCardIdentifier.Value }),
                ArenaOffered = state.CurrentPack!.CardIdentifiers.Select(a => a.Value), ArenaPool = state.DraftedPool.Occurrences.Select(a => a.Value) });
        }
        Directory.CreateDirectory(output);
        var orderEvidence = (await File.ReadAllLinesAsync(Path.Combine(local, "localization", "order-evidence.jsonl")))
            .Select(l => JsonSerializer.Deserialize<ArenaDisplayOrderObservation>(l)!).ToArray();
        var databaseReader = new ArenaCardDatabaseReader(ArenaCardDatabaseLocator.FindNewest(null)!);
        var comparatorAudits = new List<object>();
        foreach (var line in await File.ReadAllLinesAsync(Path.Combine(local, "localization", "deterministic-shadow.jsonl")))
        {
            using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
            if (r.GetProperty("SessionId").GetString() != "0bd92f4fccd6484c91cbc34654ff8097" || r.GetProperty("Kind").GetString() != "PackObservation"
                || r.GetProperty("CardCount").GetInt32() < 13) continue;
            var ids = r.GetProperty("PredictedOrderIdentities")[0].EnumerateArray().Select(n => n.GetInt32()).ToArray();
            var lookup = databaseReader.ReadSortKeys(ids);
            var before = ArenaDisplayOrderModel.Evaluate(orderEvidence.Where(o => o.RecordedAt < r.GetProperty("PredictedAt").GetDateTimeOffset()
                && !o.LogOrder.Order().SequenceEqual(ids.Order())).ToArray());
            var prediction = ArenaDisplayOrderModel.Predict(ids, lookup.Keys, before.Survivors);
            comparatorAudits.Add(new { RecordedAt = r.GetProperty("RecordedAt").GetString(),
                Pack = r.GetProperty("Pack").GetInt32(), Pick = r.GetProperty("Pick").GetInt32(),
                Survivors = before.Survivors.Select(h => new { h.Index, h.Name }), SortKeys = lookup.Keys.Values,
                RecordedOrdersReproduced = prediction.Orders.Select(o => string.Join(",", o.Order)).Order()
                    .SequenceEqual(r.GetProperty("PredictedOrderIdentities").EnumerateArray().Select(o => string.Join(",", o.EnumerateArray().Select(n => n.GetInt32()))).Order()),
                CandidateRules = prediction.Orders.Select(o => new { o.Order, o.RuleCount,
                    Rules = before.Survivors.Where(h => ids.OrderBy(id => lookup.Keys[id], h).SequenceEqual(o.Order)).Select(h => h.Name) }),
                SupportedSlotRectangles = ArenaDraftSlotGeometry.TryCreateLayout(r.GetProperty("ClientWidth").GetInt32(),
                    r.GetProperty("ClientHeight").GetInt32(), ids.Length, ArenaDraftSlotContext.WindowedDraftPack).Layout?.Slots });
        }
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(output, "reproduction.json"), JsonSerializer.Serialize(new
        {
            ComparatorAudits = comparatorAudits, SourceLogSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(logPath))),
            DraftScope = "eaf5816cd8fb811a69ba001241680841", ratings.FetchedAt, Format = "FRA QuickDraft",
            NetworkRequests = handler.Requests, ParseErrors = errors, Results = results,
            Limit = "Replay uses cached card/statistics data and chronological offered packs; historical UI/pair-response timestamps were not recorded."
        }, options));
        var fixtureStates = states.Where(s => s.CurrentPack!.Coordinate.Pack == 1 && s.CurrentPack.Coordinate.Pick <= 8).ToArray();
        var needed = fixtureStates.SelectMany(s => s.CurrentPack!.CardIdentifiers.Concat(s.DraftedPool.Occurrences)).Select(a => resolver.Resolve(a).CardIdentifier!.Value).ToHashSet();
        var raws = selected.Select(s => JsonSerializer.Deserialize<JsonElement>(s)).Where(c => needed.Contains(c.GetProperty("id").GetString()!)).ToArray();
        await File.WriteAllTextAsync(Path.Combine(output, "fixture-input.json"), JsonSerializer.Serialize(new
        {
            Source = "Real first 9E.2B draft; frozen v1.6 failure reproduction, not live promotion evidence",
            DraftScope = "eaf5816cd8fb811a69ba001241680841", Format = "QuickDraft", ratings.FetchedAt,
            Cards = raws, Packs = fixturePacks.Take(8),
            ParticipantStatistics = participantStatistics.Values.Select(s => new
            { Identity = s.CardIdentifier.Value, s.GameInHandGameCount, s.GameInHandWinRate, s.AverageLastSeenAt }),
            Statistics = environment.Entries.Select(s => new { Identity = s.CardIdentifier.Value, s.GameInHandGameCount, s.GameInHandWinRate, s.AverageLastSeenAt })
        }, options));
        Console.WriteLine($"Recovered {states.Count} packs; {errors.Count} parser/state warnings; {handler.Requests} network requests. Outputs: {output}");
        if (handler.Requests != 0) throw new InvalidOperationException("Offline replay attempted network access.");
    }
}
