using System.Globalization;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.HistoricalEvaluation;

public static class HistoricalDeckFeatures
{
    public static HistoricalDeckSummary Summarize(HistoricalCardQuantity[] main, CardCatalog catalog)
    {
        var role = new LimitedCardRoleClassifier();
        int total = 0, unresolved = 0, structured = 0, lands = 0, spells = 0, creatures = 0, nonCreatureSpells=0, early = 0, top = 0, fixing = 0, oracle = 0, unknownMana = 0;
        double removal = 0;
        var curve = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var colors = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var pips = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var q in main)
        {
            total += q.Count;
            var card = q.Card.Identifier is { } id ? catalog.Find(id) : null;
            if (card is null) { unresolved += q.Count; continue; }
            var m = card.GameplayMetadata; var roles = role.Classify(card);
            if (roles.HasStructuredMetadata) structured += q.Count;
            if (m.OracleText is not null || m.Faces.Any(f => f.OracleText is not null)) oracle += q.Count;
            if (m.IsLand) lands += q.Count;
            if (m.IsNonlandSpell)
            {
                spells += q.Count;
                if(!m.IsCreature)nonCreatureSpells+=q.Count;
                var bucket = m.ManaValue is { } mv ? mv >= 7 ? "7+" : Math.Floor(mv).ToString(CultureInfo.InvariantCulture) : "unknown";
                curve[bucket] = curve.GetValueOrDefault(bucket) + q.Count;
                if (m.ManaValue >= 5) top += q.Count;
            }
            if (m.IsCreature) creatures += q.Count;
            if (roles.Roles.HasFlag(LimitedCardRole.EarlyPlay)) early += q.Count;
            if (roles.Roles.HasFlag(LimitedCardRole.Fixing)) fixing += q.Count;
            removal += roles.RemovalWeight * q.Count;
            var color = m.ColorsKnown ? card.Colors.Count == 0 ? "colorless" : string.Concat(card.Colors.Colors.Select(c => "WUBRG"[(int)c])) : "unknown";
            colors[color] = colors.GetValueOrDefault(color) + q.Count;
            var demand = DeckManaDemand.For(card);
            if (!demand.IsKnown) unknownMana += q.Count;
            foreach (var entry in demand.Colors) pips[entry.Key.ToString()] = pips.GetValueOrDefault(entry.Key.ToString()) + entry.Value * q.Count;
        }
        return new(total, unresolved, structured, lands, spells, creatures, nonCreatureSpells, early, top, fixing, removal,
            oracle, curve, colors, pips, unknownMana);
    }
}

/// <summary>Observed game builds are retained as observed builds, never renamed "final decks".</summary>
public sealed class HistoricalDeckImporter(HistoricalStore store, CardCatalog catalog, HistoricalIdentityResolver resolver)
{
    public object ImportGames(string file, string datasetId, string set, string format, string[] bonusSets,
        string uri, CancellationToken token = default)
    {
        using var transaction = store.Connection.BeginTransaction();
        store.Execute("""
            CREATE TABLE IF NOT EXISTS deck_games(dataset TEXT NOT NULL,draft TEXT NOT NULL,build TEXT NOT NULL,
              game_key TEXT NOT NULL, won INT NOT NULL, PRIMARY KEY(dataset,draft,build,game_key));
            CREATE TABLE IF NOT EXISTS deck_manifests(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            """);
        var hash = HistoricalStore.HashFile(file);
        var cached = store.Scalar("SELECT payload FROM deck_manifests WHERE id=$id", ("$id", datasetId)) as string;
        if (cached is not null)
        {
            if (!cached.Contains(hash, StringComparison.Ordinal)) throw new InvalidDataException("Deck dataset changed; use a new ID.");
            return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(cached);
        }
        using var csv = StreamingCsv.Open(file); var headers = csv.Read(token) ?? throw new InvalidDataException("Missing game header.");
        if (headers.Distinct().Count() != headers.Length) throw new InvalidDataException("Duplicate game columns.");
        var map = headers.Select((h,i) => (h,i)).ToDictionary(p => p.h, p => p.i);
        foreach (var required in new[] { "draft_id", "build_index", "game_number", "won", "expansion", "event_type" })
            if (!map.ContainsKey(required)) throw new InvalidDataException($"Game column {required} unavailable.");
        long rows = 0, duplicates = 0, malformed = 0;
        while (csv.Read(token) is { } values)
        {
            token.ThrowIfCancellationRequested(); rows++;
            if (values.Length != headers.Length) { malformed++; continue; }
            string? V(string key) => map.TryGetValue(key, out var i) && values[i].Length > 0 ? values[i] : null;
            if (V("expansion") != set || V("event_type") != format) { malformed++; continue; }
            var draft = V("draft_id")!; var build = V("build_index")!;
            var main = new List<HistoricalCardQuantity>();
            try
            {
                foreach (var h in headers.Where(h => h.StartsWith("deck_", StringComparison.Ordinal)))
                {
                    var numeric = double.Parse(V(h) ?? "0", CultureInfo.InvariantCulture);
                    if (!double.IsFinite(numeric) || numeric != Math.Floor(numeric) || numeric is < 0 or > 100) throw new FormatException("Invalid game deck count.");
                    var count = (int)numeric;
                    if (count > 0) main.Add(new(resolver.Resolve(h[5..], set, bonusSets), count));
                }
            }
            catch (FormatException) { malformed++; continue; }
            if (main.Count == 0 || string.IsNullOrWhiteSpace(draft) || string.IsNullOrWhiteSpace(build)) { malformed++; continue; }
            var won = V("won") switch { "True" or "true" or "1" => 1, "False" or "false" or "0" => 0, _ => -1 };
            if (won < 0) { malformed++; continue; }
            // Older schemas reset game_number without match_number/time; it is not a unique game ID.
            // Preserve source rows in that case rather than falsely deduplicating different games.
            var gameKey = V("match_number") is not null || V("game_time") is not null
                ? string.Join('/', V("match_number"), V("game_number"), V("game_time")) : "source-row:" + rows;
            var oldGame = store.Scalar("SELECT won FROM deck_games WHERE dataset=$ds AND draft=$d AND build=$b AND game_key=$g",
                ("$ds",datasetId),("$d",draft),("$b",build),("$g",gameKey));
            if (oldGame is not null) { if (Convert.ToInt32(oldGame) != won) throw new InvalidDataException("Conflicting game duplicate."); duplicates++; continue; }
            var eventJson = store.Scalar("SELECT outcome FROM decisions WHERE source='17LandsPublic' AND draft=$d AND set_code=$s AND format=$f LIMIT 1",
                ("$d",draft),("$s",set),("$f",format)) as string;
            var eventOutcome = eventJson is null ? null : HistoricalJson.Read<DraftOutcome>(eventJson);
            var deck = new HistoricalLimitedDeck(set, format, draft, build, main.ToArray(), null, eventOutcome?.FinalPool,
                eventOutcome?.FinalWins, eventOutcome?.FinalLosses, V("rank"),
                "Observed game build, not a recovered final deck. Wins/losses unknown; observed games stored separately.",
                HistoricalDeckFeatures.Summarize(main.ToArray(),catalog), new("17LandsPublic",uri,hash,rows,"17lands-wide-game-v1"));
            var old = store.Scalar("SELECT payload FROM decks WHERE dataset=$ds AND draft=$d AND build=$b", ("$ds",datasetId),("$d",draft),("$b",build)) as string;
            if (old is not null && HistoricalJson.Serialize(HistoricalJson.Read<HistoricalLimitedDeck>(old).MainDeck) != HistoricalJson.Serialize(deck.MainDeck))
                throw new InvalidDataException("A game build ID has conflicting deck contents; not collapsed.");
            store.Execute("INSERT OR IGNORE INTO decks VALUES($ds,$d,$b,$p)", ("$ds",datasetId),("$d",draft),("$b",build),("$p",HistoricalJson.Serialize(deck)));
            store.Execute("INSERT INTO deck_games VALUES($ds,$d,$b,$g,$w)", ("$ds",datasetId),("$d",draft),("$b",build),("$g",gameKey),("$w",won));
        }
        var buildCount = store.Scalar("SELECT COUNT(*) FROM decks WHERE dataset=$id", ("$id",datasetId));
        var result = new { DatasetId=datasetId, FileHash=hash, FileSize=new FileInfo(file).Length, SourceUri=uri,
            SourceSchema="17lands-wide-game-v1", ObservedColumns=headers, Rows=rows, Malformed=malformed, Duplicates=duplicates,
            ObservedBuilds=buildCount, FinalDecksRecovered=0, Policy="Game wins are observed participation counts; only exact draft-id/set/format joins supply final event records. No missing sideboard or pool invented.",
            GameKeyPolicy=map.ContainsKey("match_number")||map.ContainsKey("game_time") ? "match/game/time source coordinates" : "source row identity; no reliable cross-row game deduplication in this schema" };
        store.Execute("INSERT INTO deck_manifests VALUES($id,$p)",("$id",datasetId),("$p",HistoricalJson.Serialize(result)));
        transaction.Commit(); return result;
    }
    public void Export(string outputJsonl, string aggregateJson, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputJsonl))!);
        using var writer = new StreamWriter(outputJsonl);
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = "SELECT payload FROM decks ORDER BY dataset,draft,build";
        using var reader = cmd.ExecuteReader();
        // Base card frequencies retain exact continuous outcomes. Pair co-occurrence can be computed from row-level decks later.
        var aggregate = new SortedDictionary<string,long>(StringComparer.Ordinal); long builds = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested(); var deck = HistoricalJson.Read<HistoricalLimitedDeck>(reader.GetString(0));
            writer.WriteLine(HistoricalJson.Serialize(deck)); builds++;
            var colors = string.Concat(deck.Summary.ColorDistribution.Where(c => c.Key != "colorless" && c.Key != "unknown" && c.Value > 0).SelectMany(c => c.Key).Distinct().OrderBy(c => "WUBRG".IndexOf(c)));
            var prefix = $"{deck.SetCode}/{deck.Format}/{colors}/record:{deck.Wins?.ToString() ?? "unknown"}-{deck.Losses?.ToString() ?? "unknown"}";
            aggregate[prefix + "/builds"] = aggregate.GetValueOrDefault(prefix + "/builds") + 1;
            foreach (var id in deck.MainDeck.Select(c => c.Card.CanonicalId).Where(c => c is not null).Distinct())
                aggregate[prefix + "/card:" + id] = aggregate.GetValueOrDefault(prefix + "/card:" + id) + 1;
        }
        File.WriteAllText(aggregateJson, System.Text.Json.JsonSerializer.Serialize(new { ObservedBuilds=builds, Counts=aggregate,
            Interpretation="Observed builds, not unique drafts. Final records unknown here; no successful-vs-unsuccessful inference. Draft-id joins can add reliable event labels later; all card quantities retained for pair analyses." }, HistoricalJson.Pretty));
    }
}
