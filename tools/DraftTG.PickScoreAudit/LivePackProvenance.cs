using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

/// <summary>Offline, diagnostic-only reconstruction. No live memory, capture, provider refresh or cache writes.</summary>
internal static class LivePackProvenance
{
    private static readonly JsonSerializerOptions Options = new()
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    internal static async Task RunAsync(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("--live-pack-provenance <9F.2 ledger> <saved Player.log> <output fixture.json>");
        var ledgerPath = Path.GetFullPath(args[0]); var logPath = Path.GetFullPath(args[1]);
        var output = Path.GetFullPath(args[2]);
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DraftTG");
        var cachePath = Path.Combine(local, "limited-data", "17lands", "FRA_QuickDraft_ALL_TIME_v3.json");
        var bulkPath = Path.Combine(local, "card-data", "scryfall-default-cards.jsonl.gz");
        var ledgerBytes = await File.ReadAllBytesAsync(ledgerPath);
        var records = System.Text.Encoding.UTF8.GetString(ledgerBytes).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonSerializer.Deserialize<JsonElement>(l)).ToArray();
        var memoryRecord = records.First(r => r.GetProperty("Kind").GetString() == "Memory"
            && r.GetProperty("Data").GetProperty("Stable").GetBoolean()
            && r.GetProperty("Data").GetProperty("PackNumber").GetInt32() == 1
            && r.GetProperty("Data").GetProperty("PickNumber").GetInt32() == 1
            && r.GetProperty("Data").GetProperty("CardCount").GetInt32() == 14);
        var memory = memoryRecord.GetProperty("Data");
        var ids = memory.GetProperty("OrderedGrpIds").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        if (ids.Length != 14 || !ids.Contains(106401) || !ids.Contains(106364)
            || memory.GetProperty("EventName").GetString() != "QuickDraft_FRA_20261008")
            throw new InvalidDataException("This command freezes the investigated real FRA P1P1, not another pack.");
        var rendered = records.Last(r => r.GetProperty("Kind").GetString() == "BadgeStages"
            && r.GetProperty("Data").GetProperty("SemanticPackKey").GetString() == memory.GetProperty("SemanticPackKey").GetString()
            && r.GetProperty("Data").GetProperty("Cards").GetArrayLength() == 14);
        var logBytes = await File.ReadAllBytesAsync(logPath);
        var parser = new ArenaDraftLogParser(); var engine = new ArenaDraftStateEngine();
        ArenaDraftStateSnapshot? state = null; var warnings = new List<string>(); var lineIndex = 0;
        foreach (var line in System.Text.Encoding.UTF8.GetString(logBytes).Split('\n'))
        {
            lineIndex++;
            try
            {
                foreach (var fact in parser.Parse(new ArenaLogSourceEvent.Line(line)))
                {
                    engine.Apply(fact); var current = engine.Current;
                    if (current.EventName == memory.GetProperty("EventName").GetString()
                        && current.CurrentPack is { Coordinate.Pack: 1, Coordinate.Pick: 1 } pack
                        && pack.CardIdentifiers.Select(a => a.Value).Order().SequenceEqual(ids.Order()))
                        state = current;
                }
            }
            catch (Exception ex) when (ex is ArenaDraftLogParseException or ArenaDraftStateConflictException)
            { warnings.Add($"Line {lineIndex}: {ex.GetType().Name}: {ex.Message}"); }
        }
        if (state is null || state.CompletedPicks.Count != 0 || state.DraftedPool.Occurrences.Count != 0
            || state.Mode?.Kind != ArenaDraftModeKind.Quick)
            throw new InvalidDataException("Exact log pack with an empty P1P1 pool and QuickDraft context was not recovered.");
        // Retain the native order, with the production adapter and log-owned context/pool/history.
        state = state with { CurrentPack = state.CurrentPack! with
            { CardIdentifiers = new(ids.Select(ArenaCardIdentifier.Create)) } };
        var cacheBytes = await File.ReadAllBytesAsync(cachePath);
        var rawCache = JsonSerializer.Deserialize<JsonElement>(cacheBytes);
        var fetched = rawCache.GetProperty("FetchedAt").GetDateTimeOffset();
        if (fetched > memoryRecord.GetProperty("Time").GetDateTimeOffset())
            throw new InvalidDataException("Cache was fetched after the live observation; cannot assert the original inputs.");
        using var handler = new NoNetwork(); using var http = new HttpClient(handler);
        var provider = new SeventeenLandsCardRatingsClient(http, new Paths(local), new FrozenTime(fetched + TimeSpan.FromMinutes(1)));
        var ratings = await provider.LoadAsync("FRA", SeventeenLandsFormat.QuickDraft);
        if (ratings.Source != SeventeenLandsSource.Cache || handler.Requests != 0)
            throw new InvalidDataException("An exact validated offline cache is required.");
        var names = ratings.Rows.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        var selected = new List<JsonElement>();
        using (var file = File.OpenRead(bulkPath))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
            while (await reader.ReadLineAsync() is { } line)
            {
                var card = JsonSerializer.Deserialize<JsonElement>(line);
                if (card.GetProperty("set").GetString() == "fra" || names.Contains(card.GetProperty("name").GetString()!))
                    selected.Add(card);
            }
        // Use the same complete cached catalog import as production: alias ambiguity and ArenaId ambiguity
        // must not disappear just because an audit selected fewer printings.
        var data = await new ScryfallJsonlGzipCardDataLoader().LoadAsync(bulkPath);
        var databasePath = ArenaCardDatabaseLocator.FindNewest() ?? throw new InvalidDataException("Arena card database unavailable.");
        var lookup = new ArenaCardDatabaseReader(databasePath).ReadPrintingIdentities(ids);
        var resolver = new ArenaCardResolver(data, new ArenaDatabasePrintingIdentitySource(() => databasePath));
        var adapted = new ArenaDraftSnapshotAdapter(resolver).Convert(state);
        var snapshot = adapted.Snapshot ?? throw new InvalidDataException("Pack identities unresolved.");
        var catalog = data.Catalog; var context = new LimitedStatisticsContext("FRA", LimitedStatisticsFormat.QuickDraft);
        var mapping = LimitedStatisticsMapper.Map(ratings.Rows, catalog, context, snapshot);
        var environment = LimitedStatisticsMapper.MapEnvironment(ratings.Rows, catalog, context);
        var result = new LimitedStatisticsLoadResult(context, context, mapping.Catalog, LimitedStatisticsSource.Cache, fetched, mapping.Diagnostic)
        { CardCatalog = catalog, EnvironmentCatalog = environment, StatisticsResolvedNames = mapping.ResolvedNames, StatisticsResolutions = mapping.Resolutions };
        var update = new LimitedStatisticsUpdate(snapshot, false, result, draftPool: adapted.DraftPool,
            pickScoreConfiguration: new(version: ContextualPickScoreVersion.V1_6));
        var audits = update.PickScores!.Cards.Select(score =>
        {
            var card = catalog.Find(score.CardIdentifier)!; var id = ids[score.PackIndex];
            var resolution = mapping.Resolutions[card.Identifier];
            var rawRows = rawCache.GetProperty("Payload").GetProperty("data").EnumerateArray()
                .Where(r => r.GetProperty("name").GetString() == resolution.ProviderName).ToArray();
            var statistics = mapping.Catalog.StatisticsFor(card.Identifier);
            var rendering = rendered.GetProperty("Data").GetProperty("Render").GetProperty("Cards")
                .EnumerateArray().Single(r => r.GetProperty("PackIndex").GetInt32() == score.PackIndex);
            if (rendering.GetProperty("CardIdentity").GetString() != card.Identifier.Value)
                throw new InvalidDataException("Reconstructed printing differs from the live badge identity.");
            return new
            {
                score.PackIndex, GrpId = id, Arena = lookup.Printings[id], ScryfallName = card.Name,
                card.Identifier, Set = card.SetCode.Value, Collector = card.CollectorNumber.Value, card.Rarity,
                ScryfallArenaIdPresent = data.ArenaIdMappings.ContainsKey(id), IdentitySource = resolver.Resolve(ArenaCardIdentifier.Create(id)).Source,
                LookupKey = resolution.ProviderName ?? card.Name, StatisticalIdentity = resolution,
                RawProviderRows = rawRows, Statistics = statistics,
                QualityProvenance = score.IsQualityEstimated ? "ESTIMATED" : resolution.Kind == StatisticalIdentityKind.MultifaceFrontFace ? "FRONT_FACE_FALLBACK" : "DIRECT",
                GihReason = rawRows.Length == 0 ? "A: No provider row" : statistics?.GameInHandWinRate is null
                    ? $"B: Provider ever_drawn_win_rate is null; reported GIH n={statistics?.GameInHandGameCount}. Low sample is consistent with suppression, not a DraftTG filter."
                    : statistics.GameInHandGameCount is not > 0 ? "C: Provider rate exists but sample is not positive" : "Available",
                AlsaReason = rawRows.Length == 0 ? "A: No provider row" : statistics?.AverageLastSeenAt is null
                    ? "B: Provider avg_seen is null. This response contains no seen-count or suppression reason; precise upstream cause unknown." : "Available",
                Statistical = update.Recommendation!.Cards[score.PackIndex], Score = score,
                ExactScore0To50 = score.ContextualValue!.Value * 50, DisplayedScore = score.Score0To50,
                Rank = score.CurrentPackRank, Diagnostic = update.Occurrences[new(score.PackIndex, card.Identifier)].DiagnosticText
            };
        }).ToArray();
        var needed = snapshot.CurrentPack.AvailableCardIdentifiers.Select(i => i.Value).ToHashSet();
        // Retain every decoder input for these printings; omit unrelated prices/links/artwork.
        string[] fields = ["id", "name", "set", "collector_number", "arena_id", "colors", "rarity", "layout", "card_faces",
            "cmc", "mana_cost", "type_line", "oracle_text", "keywords", "power", "toughness", "produced_mana"];
        var packCards = selected.Where(c => needed.Contains(c.GetProperty("id").GetString()!))
            .Select(c => c.EnumerateObject().Where(p => fields.Contains(p.Name)).ToDictionary(p => p.Name, p => p.Value)).ToArray();
        var artifact = new
        {
            Source = "Exact real Phase 9F.2 FRA QuickDraft P1P1; diagnostic replay only, no live promotion evidence",
            ModelVersion = update.PickScores.Configuration.ModelVersion, TierVersion = PickScoreTierThresholds.Version,
            SourceLedgerSha256 = Hash(ledgerBytes), SourceLogSha256 = Hash(logBytes), ProviderCacheSha256 = Hash(cacheBytes),
            ScryfallBulkSha256 = Hash(await File.ReadAllBytesAsync(bulkPath)), ArenaDatabaseSha256 = Hash(await File.ReadAllBytesAsync(databasePath)),
            MemoryEvidence = memoryRecord, BadgeEvidence = rendered,
            EventName = state.EventName, Pack = 1, Pick = 1, OrderedGrpIds = ids,
            OrderedIdentities = snapshot.CurrentPack.AvailableCardIdentifiers.Select(i => i.Value),
            HistoryCount = snapshot.History.Picks.Count, PoolCount = snapshot.DraftedPool.Count,
            ProviderQuery = "https://www.17lands.com/api/card_data?expansion=FRA&event_type=QuickDraft&time_period=ALL_TIME",
            ProviderCache = rawCache, Cards = packCards, EnvironmentStatistics = environment.Entries.OrderBy(s => s.CardIdentifier.Value),
            ExpectedBaseline = update.Recommendation!.EnvironmentBaseline, Expected = audits, NetworkRequests = handler.Requests, LogWarnings = warnings,
            Limit = "Cache predates both live runs and replayed rates match the reported values. Live ledger did not hash statistics at score time; byte-identical historical in-memory payload cannot be independently proved. P1P1 has no active pair, lane history or deck optimizer; not a historical UI screenshot replay."
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(artifact, Options));
        Console.WriteLine($"Recovered 14 native-ordered cards; empty log pool/history; provider rows {ratings.Rows.Count}; network requests {handler.Requests}.");
        Console.WriteLine($"Fixture: {output}");
        foreach (var audit in audits.OrderBy(a => a.Rank))
            Console.WriteLine($"#{audit.Rank}: {audit.GrpId} {audit.ScryfallName}: {audit.DisplayedScore} ({audit.ExactScore0To50:R}) {audit.QualityProvenance}; {audit.GihReason}; ALSA {audit.AlsaReason}");
    }

    internal static async Task PrintAsync(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var id))
            throw new ArgumentException("--card-stat-provenance <frozen fixture.json> <GrpId>");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
        var root = doc.RootElement;
        Console.WriteLine($"Frozen diagnostic inputs: {root.GetProperty("ProviderQuery")}; fetched {root.GetProperty("ProviderCache").GetProperty("FetchedAt")}; model {root.GetProperty("ModelVersion")}");
        var row = root.GetProperty("Expected").EnumerateArray().Single(r => r.GetProperty("GrpId").GetInt32() == id);
        Console.WriteLine(JsonSerializer.Serialize(row, Options));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
