using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

// Offline audit only: cached production data and saved fixtures. No live runtime, no cache writes.
var root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? ".");
var output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? Path.Combine(root, "artifacts", "pick-score-v1", "audit.json"));
var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DraftTG");
var fixtures = Path.Combine(root, "tests", "Fixtures");
var ratingsFile = Path.Combine(local, "limited-data", "17lands", "FRA_PremierDraft_ALL_TIME_v3.json");
using var cache = JsonDocument.Parse(await File.ReadAllTextAsync(ratingsFile));
var ratingNames = cache.RootElement.GetProperty("Payload").GetProperty("data").EnumerateArray()
    .Select(r => r.GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal);
var selected = new List<string>();
using (var file = File.OpenRead(Path.Combine(local, "card-data", "scryfall-default-cards.jsonl.gz")))
using (var gzip = new GZipStream(file, CompressionMode.Decompress))
using (var reader = new StreamReader(gzip))
    while (await reader.ReadLineAsync() is { } line)
    {
        using var item = JsonDocument.Parse(line);
        if (item.RootElement.GetProperty("set").GetString() is "fra" or "woe"
            || ratingNames.Contains(item.RootElement.GetProperty("name").GetString())) selected.Add(line);
    }
var decoder = new ScryfallCardCatalogDecoder();
var data = decoder.DecodeCatalogData("[" + string.Join(",", selected) + "]");
var fraData = decoder.DecodeCatalogData(await File.ReadAllTextAsync(Path.Combine(fixtures, "premier-fra-course-cards.json")));
var catalog = new CardCatalog(data.Catalog.Cards.Concat(fraData.Catalog.Cards).DistinctBy(c => c.Identifier));
var handler = new NoNetwork();
using var http = new HttpClient(handler);
var fetched = cache.RootElement.GetProperty("FetchedAt").GetDateTimeOffset();
var client = new SeventeenLandsCardRatingsClient(http, new Paths(local), new FrozenTime(fetched + TimeSpan.FromHours(1)));
var fraRatings = await client.LoadAsync("FRA", SeventeenLandsFormat.PremierDraft);
var environment = LimitedStatisticsMapper.MapEnvironment(fraRatings.Rows, catalog, new("FRA", LimitedStatisticsFormat.PremierDraft));
var examples = new List<object>();

// Actual WOE pack positions; missing prior picks and absent exact-format cache stay explicit.
foreach (var name in new[] { "arena-artwork-live", "arena-wgc-p1p7" })
{
    using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, name, "manifest.json")));
    var cards = manifest.RootElement.GetProperty("Cards").EnumerateArray().Select(c => CardIdentifier.Create(c.GetProperty("Id").GetString()!)).ToArray();
    var snapshot = new DraftSnapshot(new(new(PackNumber.Create(manifest.RootElement.GetProperty("PackNumber").GetInt32()),
        PickNumber.Create(manifest.RootElement.GetProperty("PickNumber").GetInt32())), cards), new(), DraftFormat.BestOfOne);
    var update = Update(snapshot, catalog, new(), new(), "WOE", LimitedStatisticsFormat.QuickDraft);
    AddExamples(name + " actual pack, earlier pool unavailable; no WOE QuickDraft cache", update, 2);
}

// Actual duplicate-preserving completed FRA inventory; candidate choices are labeled what-if.
var state = new ArenaDraftStateEngine(); var parser = new ArenaDraftLogParser();
foreach (var line in await File.ReadAllLinesAsync(Path.Combine(fixtures, "premier-fra-course-snapshot-live.jsonl")))
    foreach (var fact in parser.Parse(new ArenaLogSourceEvent.Line(line))) state.Apply(fact);
var resolved = new ArenaDraftSnapshotAdapter(new ArenaCardResolver(fraData)).Convert(state.Current);
var pool = resolved.DraftPool ?? throw new InvalidOperationException("Saved FRA pool unavailable.");
var poolCards = pool.Entries.Select(e => catalog.Find(e.CardIdentifier)!).ToArray();
var candidateIds = poolCards.OrderByDescending(c => fraRatings.Rows.FirstOrDefault(r => r.Name == c.Name)?.GameInHandWinRate)
    .Take(14).Select(c => c.Identifier).ToArray();
var whatIf = new DraftSnapshot(new(new(PackNumber.Create(3), PickNumber.Create(14)), candidateIds), new(), DraftFormat.BestOfOne)
    { RecoveredPool = pool.Inventory };
var mapped = LimitedStatisticsMapper.Map(fraRatings.Rows, catalog, new("FRA", LimitedStatisticsFormat.PremierDraft), whatIf);
var late = Update(whatIf, catalog, mapped.Catalog, environment, "FRA", LimitedStatisticsFormat.PremierDraft, pool);
AddExamples("FRA saved completed 42-card pool; hypothetical additional copy, late-stage weights (not a recorded pick)", late, 6);

var measured = new List<object> { Benchmark("FRA saved pool / 14 candidates / full Phase 8–9 + score pipeline", () =>
    Update(whatIf, catalog, mapped.Catalog, environment, "FRA", LimitedStatisticsFormat.PremierDraft, pool)) };
// Deliberately difficult metadata-complete fixture: all pairs viable, varied curve/types, 42 cards.
var worstPool = Enumerable.Range(0, 42).Select(i => Synthetic($"pool-{i:00}", i)).ToArray();
var worstPack = Enumerable.Range(0, 14).Select(i => Synthetic($"candidate-{i:00}", i + 1)).ToArray();
var worstCatalog = new CardCatalog(worstPool.Concat(worstPack));
var worstStats = new LimitedCardStatisticsCatalog(worstCatalog.Cards.Select((c, i) => new LimitedCardStatistics(c.Identifier, 10000,
    GameInHandWinRate: .52 + i % 9 * .01)));
var worstSnapshot = new DraftSnapshot(new(new(PackNumber.Create(3), PickNumber.Create(14)), worstPack.Select(c => c.Identifier)), new(), DraftFormat.BestOfOne)
    { RecoveredPool = new(worstPool.Select(c => c.Identifier)) };
measured.Add(Benchmark("Synthetic stress / 42 varied colorless spells / all ten pairs viable / 14 distinct candidates", () =>
    Update(worstSnapshot, worstCatalog, worstStats, environment, "FRA", LimitedStatisticsFormat.PremierDraft)));
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
{
    ModelVersion = new ContextualPickScoreConfiguration().ModelVersion, NetworkRequests = handler.Requests,
    CachedRatings = new { fraRatings.Expansion, Format = fraRatings.Format.ToString(), fraRatings.FetchedAt, Rows = fraRatings.Rows.Count,
        EnvironmentRows = environment.Entries.Count(), Baseline = late.Recommendation!.EnvironmentBaseline },
    SavedFraPool = new { pool.TotalCardCount, Unique = pool.Entries.Count, Status = pool.Completeness.ToString() },
    Examples = examples, Benchmarks = measured,
    Limits = "No WOE statistics cache or chronological FRA pack history; WOE is unavailable, FRA candidates are completed-pool what-if examples. Loading cached files excluded from pack timings. No pair dataset or trophy structural corpus is supplied."
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(output); Console.WriteLine($"Network requests: {handler.Requests}");
if (handler.Requests != 0) throw new InvalidOperationException("Offline audit attempted network access.");

void AddExamples(string source, LimitedStatisticsUpdate update, int count)
{
    var cards = update.PickScores!.Cards.OrderByDescending(c => c.ContextualValue).ToArray();
    // Include contrasting outcomes instead of just the strongest candidates.
    var chosen = count == 6 ? new[] { 0, 1, 3, 6, 10, 13 } : Enumerable.Range(0, count).ToArray();
    foreach (var index in chosen.Where(i => i < cards.Length))
    {
        var score = cards[index]; var occurrence = update.Occurrences[new(score.PackIndex, score.CardIdentifier)];
        examples.Add(new { Source = source,
            Coordinate = $"P{update.Snapshot!.CurrentPack.Position.Pack.Value}P{update.Snapshot.CurrentPack.Position.Pick.Value}",
            CoordinateMeaning = source.StartsWith("FRA", StringComparison.Ordinal) ? "Synthetic late-stage coordinate for completed-pool counterfactual; not recorded pick history" : "Actual saved pack coordinate",
            Card = occurrence.CardName, occurrence.PackIndex,
            OldPhase9BRank = occurrence.LaneRank, PickScore = score.Score0To50, ScoreRank = score.CurrentPackRank,
            Availability = score.Availability.ToString(), Q = score.QualityComponent, L = score.LaneComponent,
            A = score.ArchetypeComponent, D = score.DeckNeedComponent, score.ColorFit, score.DraftProgress,
            score.DeckImpact, score.Contributions, score.Reasons, Explanation = PickScorePresentation.Diagnostics(score) });
    }
}
static LimitedStatisticsUpdate Update(DraftSnapshot snapshot, CardCatalog catalog, LimitedCardStatisticsCatalog stats,
    LimitedCardStatisticsCatalog environment, string set, LimitedStatisticsFormat format, DraftPoolSnapshot? pool = null) => new(snapshot, false,
        new(new(set, format), !stats.Entries.Any() ? null : new(set, format), stats,
            !stats.Entries.Any() ? LimitedStatisticsSource.Unavailable : LimitedStatisticsSource.Cache, null, null)
        { CardCatalog = catalog, EnvironmentCatalog = environment }, draftPool: pool);
static object Benchmark(string name, Func<LimitedStatisticsUpdate> run)
{
    for (var i = 0; i < 3; i++) run();
    var elapsed = new List<double>(); var simulation = new List<double>(); var calls = new List<int>();
    for (var i = 0; i < 20; i++)
    {
        var timer = Stopwatch.StartNew(); var result = run(); elapsed.Add(timer.Elapsed.TotalMilliseconds);
        simulation.Add(result.PickScores!.Metrics.ProjectedDeckMilliseconds); calls.Add(result.PickScores.Metrics.OptimizerCalls);
    }
    elapsed.Sort(); simulation.Sort();
    return new { Name = name, Samples = elapsed.Count, MedianMs = (elapsed[9] + elapsed[10]) / 2,
        P95Ms = elapsed[18], MaximumMs = elapsed[^1], DeckSimulationMedianMs = (simulation[9] + simulation[10]) / 2,
        DeckSimulationP95Ms = simulation[18], OptimizerCallsMin = calls.Min(), OptimizerCallsMax = calls.Max(),
        Environment.ProcessorCount, Runtime = Environment.Version.ToString() };
}
static Card Synthetic(string name, int index) => new(CardIdentifier.Create(name), name, ColorSet.Colorless, CardRarity.Common,
    CardSetCode.Create("FRA"), CollectorNumber.Create(name))
{ GameplayMetadata = new(index % 6 + 1, "{3}", index % 3 == 0 ? "Sorcery" : "Artifact Creature") };
sealed class Paths(string directory) : IApplicationDataPathProvider
{ public string GetApplicationDataDirectory() => directory; }
sealed class FrozenTime(DateTimeOffset time) : TimeProvider
{ public override DateTimeOffset GetUtcNow() => time; }
sealed class NoNetwork : HttpMessageHandler
{
    public int Requests;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Requests++; throw new HttpRequestException("Network disabled by offline score audit.", null, HttpStatusCode.ServiceUnavailable); }
}
