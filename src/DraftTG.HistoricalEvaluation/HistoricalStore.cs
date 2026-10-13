using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using DraftTG.Domain;
using Microsoft.Data.Sqlite;

namespace DraftTG.HistoricalEvaluation;

/// <summary>Disk-backed unique keys, set/draft split isolation and labels stored separately from inputs.</summary>
public sealed class HistoricalStore : IDisposable
{
    public SqliteConnection Connection { get; }
    public HistoricalStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        Connection = new(new SqliteConnectionStringBuilder { DataSource=path, Pooling=false }.ToString()); Connection.Open();
        if(Convert.ToInt32(Scalar("SELECT COUNT(*) FROM pragma_table_info('decisions')"))>0
            && Convert.ToInt32(Scalar("SELECT COUNT(*) FROM pragma_table_info('decisions') WHERE name='metadata'"))==0)
            throw new InvalidDataException("Obsolete development store schema; reimport into a new database.");
        Execute("PRAGMA foreign_keys=ON; PRAGMA cache_size=-8192; PRAGMA temp_store=FILE;");
        Execute("""
            CREATE TABLE IF NOT EXISTS manifests(id TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS drafts(source TEXT NOT NULL, id TEXT NOT NULL, set_code TEXT NOT NULL,
              partition TEXT NOT NULL, PRIMARY KEY(source,id));
            CREATE TABLE IF NOT EXISTS decisions(dataset TEXT NOT NULL, source TEXT NOT NULL, draft TEXT NOT NULL,
              set_code TEXT NOT NULL, format TEXT NOT NULL, pack INT NOT NULL, pick INT NOT NULL, ordinal INT NOT NULL,
              inputs TEXT NOT NULL, label TEXT NOT NULL, outcome TEXT NOT NULL, provenance TEXT NOT NULL, metadata TEXT NOT NULL,
              PRIMARY KEY(source,draft,pack,pick));
            CREATE INDEX IF NOT EXISTS decision_order ON decisions(set_code,format,source,draft,ordinal);
            CREATE TABLE IF NOT EXISTS decks(dataset TEXT NOT NULL, draft TEXT NOT NULL, build TEXT NOT NULL,
              payload TEXT NOT NULL, PRIMARY KEY(dataset,draft,build));
            """);
    }
    public int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = Connection.CreateCommand(); cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }
    public object? Scalar(string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = Connection.CreateCommand(); cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd.ExecuteScalar();
    }
    public HistoricalDatasetManifest[] Manifests()
    {
        using var cmd = Connection.CreateCommand(); cmd.CommandText = "SELECT payload FROM manifests ORDER BY id";
        using var rows = cmd.ExecuteReader(); var result = new List<HistoricalDatasetManifest>();
        while (rows.Read()) result.Add(HistoricalJson.Read<HistoricalDatasetManifest>(rows.GetString(0)));
        return result.ToArray();
    }
    public IEnumerable<HistoricalDraftDecision> ReadDecisions(SetSplitManifest split, DatasetPartition? partition = null,
        IReadOnlySet<string>? sets = null, CancellationToken token = default)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT dataset,inputs,label,outcome,provenance,metadata FROM decisions ORDER BY set_code,format,source,draft,ordinal";
        using var rows = cmd.ExecuteReader();
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            var inputs = HistoricalJson.Read<PickTimeState>(rows.GetString(1));
            if (partition is not null && split.For(inputs.SetCode) != partition || sets is not null && !sets.Contains(inputs.SetCode)) continue;
            yield return new(HistoricalJson.Schema, rows.GetString(0), inputs,
                HistoricalJson.Read<HistoricalPickLabel>(rows.GetString(2)), HistoricalJson.Read<DraftOutcome>(rows.GetString(3)),
                HistoricalJson.Read<SourceProvenance>(rows.GetString(4)), rows.GetString(5)=="null"?null:HistoricalJson.Read<HistoricalPlayerMetadata>(rows.GetString(5)));
        }
    }
    public void Dispose() => Connection.Dispose();
    public static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static string HashText(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}

public sealed class HistoricalImporter(HistoricalStore store, HistoricalIdentityResolver identities,
    SetSplitManifest split, string catalogHash)
{
    public HistoricalDatasetManifest Import(string file, DatasetImportOptions options,
        Action<long>? progress = null, CancellationToken token = default)
    {
        ValidateOptions(options);
        var hash = HistoricalStore.HashFile(file);
        var importHash=HistoricalStore.HashText(HistoricalJson.Serialize(options)+"|phase-a-normalization-v1");
        var existing = store.Manifests().SingleOrDefault(m => m.DatasetId == options.DatasetId);
        if (existing is not null)
        {
            if (existing.FileHash != hash || existing.IdentityCatalogHash != catalogHash || existing.SplitHash != HistoricalStore.HashText(HistoricalJson.Serialize(split))
                || existing.ImportOptionsHash != importHash)
                throw new InvalidDataException("Cached dataset provenance changed; use a new dataset ID/store.");
            return existing;
        }
        var timer = Stopwatch.StartNew(); long raw = 0, accepted = 0, malformed = 0, duplicate = 0, conflicting = 0;
        long resolved = 0, unresolved = 0, ambiguous = 0, outcomes = 0, finalDeck = 0, rate = 0;
        DateTimeOffset? min = null, max = null;
        var issues = new SortedDictionary<string, long>(StringComparer.Ordinal);
        using var transaction = store.Connection.BeginTransaction();
        using var csv = options.SourceSchema == "17lands-wide-draft-v1" ? StreamingCsv.Open(file) : null;
        using var jsonl = csv is null ? OpenJsonl(file) : null;
        var header = csv?.Read(token) ?? [];
        if (csv is not null) ValidateHeader(header);
        var map = header.Select((value, index) => (value, index)).ToDictionary(x => x.value, x => x.index, StringComparer.Ordinal);
        var knownMappings = new Dictionary<string, HistoricalCardIdentity>(StringComparer.Ordinal);
        HistoricalCardIdentity Resolve(string name)
        {
            if (!knownMappings.TryGetValue(name, out var value))
            {
                value = identities.Resolve(name, options.SetCode, options.BonusSets);
                if(knownMappings.Count<16384)knownMappings[name]=value;
            }
            return value;
        }
        while (true)
        {
            token.ThrowIfCancellationRequested();
            // Structural quoting errors abort the file transaction; attempting line recovery could shift every column.
            var values = csv?.Read(token); var line = jsonl is null ? null : ReadJsonlLine(jsonl,token);
            if (csv is not null ? values is null : line is null) break;
            raw++;
            HistoricalDraftDecision decision;
            try
            {
                decision = csv is not null ? ParseWide(values!, header, map, options, hash, raw, Resolve)
                    : HistoricalJson.Read<HistoricalDraftDecision>(line!);
                if (csv is null) { ValidateDecision(decision,options); decision = NormalizeCanonical(decision, Resolve); }
                ValidateDecision(decision, options);
            }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException
                or System.Text.Json.JsonException or InvalidDataException)
            { malformed++; continue; }
            var input = decision.Inputs;
            var previous = store.Scalar("SELECT set_code || ':' || partition FROM drafts WHERE source=$s AND id=$d", ("$s", options.Source), ("$d", input.DraftId)) as string;
            var scope = options.SetCode + ":" + options.Partition;
            if (previous is not null && previous != scope) throw new InvalidDataException("A draft cannot cross sets or partitions.");
            store.Execute("INSERT OR IGNORE INTO drafts VALUES($s,$d,$set,$part)", ("$s", options.Source), ("$d", input.DraftId), ("$set", options.SetCode), ("$part", options.Partition.ToString()));
            // Source attribution belongs to this exact imported file, including canonical JSONL.
            var provenance = new SourceProvenance(options.Source, options.SourceUri, hash, raw, options.SourceSchema);
            var inputsJson = HistoricalJson.Serialize(input); var labelJson = HistoricalJson.Serialize(decision.Label);
            var outcomeJson = HistoricalJson.Serialize(decision.Outcome);
            var prior = store.Scalar("SELECT inputs || label || outcome FROM decisions WHERE source=$s AND draft=$d AND pack=$p AND pick=$k",
                ("$s", options.Source), ("$d", input.DraftId), ("$p", input.PackNumber), ("$k", input.PickNumber)) as string;
            if (prior is not null)
            {
                if (prior != inputsJson + labelJson + outcomeJson) throw new InvalidDataException("Conflicting duplicate decision; file transaction rejected.");
                duplicate++; continue;
            }
            // A changed final event record within one draft is not a reliable outcome label.
            var priorOutcome = store.Scalar("SELECT outcome FROM decisions WHERE source=$s AND draft=$d LIMIT 1", ("$s", options.Source), ("$d", input.DraftId)) as string;
            if (priorOutcome is not null && priorOutcome != outcomeJson) throw new InvalidDataException("Conflicting draft outcome labels; file transaction rejected.");
            store.Execute("INSERT INTO decisions VALUES($dataset,$source,$draft,$set,$format,$pack,$pick,$ordinal,$inputs,$label,$outcome,$provenance,$metadata)",
                ("$dataset", options.DatasetId), ("$source", options.Source), ("$draft", input.DraftId), ("$set", input.SetCode), ("$format", input.Format),
                ("$pack", input.PackNumber), ("$pick", input.PickNumber), ("$ordinal", input.GlobalPickIndex), ("$inputs", inputsJson),
                ("$label", labelJson), ("$outcome", outcomeJson), ("$provenance", HistoricalJson.Serialize(provenance)),
                ("$metadata",HistoricalJson.Serialize(decision.PlayerMetadata)));
            accepted++;
            foreach (var item in input.OfferedCards.Concat(input.PoolBeforePick).Append(new(decision.Label.ActualPickedCard, 1)))
            {
                if (item.Card.Identifier is not null) resolved += item.Count;
                else
                {
                    unresolved += item.Count;
                    if (item.Card.Status == IdentityStatus.Ambiguous) ambiguous += item.Count;
                    issues[item.Card.SourceIdentity + " | " + item.Card.Status] = issues.GetValueOrDefault(item.Card.SourceIdentity + " | " + item.Card.Status) + item.Count;
                }
            }
            if (decision.Outcome.FinalWins.HasValue && decision.Outcome.FinalLosses.HasValue) outcomes++;
            if (decision.Outcome.FinalMainDeck is not null) finalDeck++;
            if (decision.Label.ObservedMaindeckRate.HasValue) rate++;
            if (input.DraftTimestamp is { } date) { if (min is null || date < min) min = date; if (max is null || date > max) max = date; }
            if (raw % 1000 == 0) progress?.Invoke(raw);
        }
        // Completed source inventory is a post-draft label, attached only after the streaming input pass.
        // Last pre-pick pool plus its actual selected occurrence is sufficient; no earlier gaps filled by guesses.
        if(options.SourceSchema=="17lands-wide-draft-v1")
        {
            using var lastCommand=store.Connection.CreateCommand();
            lastCommand.CommandText="SELECT draft,inputs,label,outcome FROM decisions WHERE dataset=$ds AND pack=3 AND pick=$last";
            lastCommand.Parameters.AddWithValue("$ds",options.DatasetId);lastCommand.Parameters.AddWithValue("$last",options.PicksPerPack);
            using var lastRows=lastCommand.ExecuteReader();
            while(lastRows.Read())
            {
                token.ThrowIfCancellationRequested();
                var last=HistoricalJson.Read<PickTimeState>(lastRows.GetString(1));
                if(!last.PoolIsComplete)continue;
                var selected=HistoricalJson.Read<HistoricalPickLabel>(lastRows.GetString(2));
                var final=last.PoolBeforePick.Append(new(selected.ActualPickedCard,1)).GroupBy(q=>q.Card.SourceIdentity,StringComparer.Ordinal)
                    .Select(g=>new HistoricalCardQuantity(g.First().Card,g.Sum(q=>q.Count))).ToArray();
                var outcome=HistoricalJson.Read<DraftOutcome>(lastRows.GetString(3)) with {FinalPool=final,
                    DeckEvidence="Completed source pool reconstructed from last pre-pick inventory plus last selected card; not a final deck."};
                store.Execute("UPDATE decisions SET outcome=$o WHERE source=$s AND draft=$d",("$o",HistoricalJson.Serialize(outcome)),("$s",options.Source),("$d",lastRows.GetString(0)));
            }
        }
        var finalPoolPicks=Convert.ToInt64(store.Scalar("SELECT COUNT(*) FROM decisions WHERE dataset=$id AND json_extract(outcome,'$.FinalPool') IS NOT NULL",("$id",options.DatasetId)),CultureInfo.InvariantCulture);
        var drafts = Convert.ToInt64(store.Scalar("SELECT COUNT(DISTINCT draft) FROM decisions WHERE dataset=$id", ("$id", options.DatasetId)), CultureInfo.InvariantCulture);
        var manifest = new HistoricalDatasetManifest(options.DatasetId, options.Source, options.SourceUri, options.SetCode,
            options.Format, options.SourceSchema, header, hash, new FileInfo(file).Length, DateTimeOffset.UtcNow, raw, accepted,
            drafts, min, max, resolved, unresolved, ambiguous, malformed, duplicate, conflicting, outcomes, finalDeck, rate,
            options.Partition, options.IsPrefixSample, timer.Elapsed.TotalSeconds, raw / Math.Max(timer.Elapsed.TotalSeconds, .000001),
            Process.GetCurrentProcess().PeakWorkingSet64, issues, catalogHash, HistoricalStore.HashText(HistoricalJson.Serialize(split)),
            importHash, options.PicksPerPack, finalPoolPicks);
        store.Execute("INSERT INTO manifests VALUES($id,$payload)", ("$id", options.DatasetId), ("$payload", HistoricalJson.Serialize(manifest)));
        transaction.Commit(); progress?.Invoke(raw); return manifest;
    }
    private void ValidateOptions(DatasetImportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatasetId) || options.SetCode != options.SetCode.ToUpperInvariant()
            || split.For(options.SetCode) != options.Partition || options.PicksPerPack is < 1 or > 15
            || options.Format is not ("PremierDraft" or "QuickDraft" or "TradDraft")
            || options.SourceSchema is not ("17lands-wide-draft-v1" or HistoricalJson.Schema))
            throw new InvalidDataException("Invalid dataset import scope/schema/split.");
    }
    private static StreamReader OpenJsonl(string file) => new(file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
        ? new System.IO.Compression.GZipStream(File.OpenRead(file), System.IO.Compression.CompressionMode.Decompress) : File.OpenRead(file));
    private static string? ReadJsonlLine(TextReader reader,CancellationToken token)
    {
        var buffer=new System.Text.StringBuilder();
        while(reader.Read() is var next && next>=0)
        {
            if((buffer.Length&4095)==0)token.ThrowIfCancellationRequested();
            if(next=='\n')return buffer.ToString().TrimEnd('\r');
            if(buffer.Length>=4*1024*1024)throw new InvalidDataException("JSONL record exceeds bounded parser limit.");
            buffer.Append((char)next);
        }
        return buffer.Length==0?null:buffer.ToString();
    }
    private static void ValidateHeader(string[] header)
    {
        if (header.Distinct(StringComparer.Ordinal).Count() != header.Length) throw new InvalidDataException("Duplicate CSV columns.");
        foreach (var name in new[] { "draft_id", "expansion", "event_type", "pack_number", "pick_number", "pick" })
            if (!header.Contains(name)) throw new InvalidDataException($"Missing required column {name}.");
        if (!header.Any(c => c.StartsWith("pack_card_", StringComparison.Ordinal)) || !header.Any(c => c.StartsWith("pool_", StringComparison.Ordinal)))
            throw new InvalidDataException("Offered/past-pool columns required; no pool invented from future picks.");
    }
    private static HistoricalDraftDecision NormalizeCanonical(HistoricalDraftDecision d, Func<string,HistoricalCardIdentity> resolve)
    {
        HistoricalCardQuantity[] Cards(HistoricalCardQuantity[] values) => values.Select(q => q with { Card=resolve(q.Card.SourceIdentity) }).ToArray();
        return d with { Inputs=d.Inputs with { OfferedCards=Cards(d.Inputs.OfferedCards), PoolBeforePick=Cards(d.Inputs.PoolBeforePick),
            PriorPicks=d.Inputs.PriorPicks.Select(p => p with { Card=resolve(p.Card.SourceIdentity) }).ToArray() },
            Label=d.Label with { ActualPickedCard=resolve(d.Label.ActualPickedCard.SourceIdentity) },
            Outcome=d.Outcome with { FinalPool=d.Outcome.FinalPool is null?null:Cards(d.Outcome.FinalPool),
                FinalMainDeck=d.Outcome.FinalMainDeck is null?null:Cards(d.Outcome.FinalMainDeck),
                FinalSideboard=d.Outcome.FinalSideboard is null?null:Cards(d.Outcome.FinalSideboard) } };
    }
    private static HistoricalDraftDecision ParseWide(string[] row, string[] header, Dictionary<string, int> map,
        DatasetImportOptions options, string hash, long number, Func<string, HistoricalCardIdentity> resolve)
    {
        if (row.Length != header.Length) throw new InvalidDataException("CSV width mismatch.");
        string? Value(string key) => map.TryGetValue(key, out var i) && row[i].Length > 0 ? row[i] : null;
        int? Int(string key) => Value(key) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : null;
        double? Rate(string key) => Value(key) is { } v ? double.Parse(v, CultureInfo.InvariantCulture) : null;
        HistoricalCardQuantity[] Quantities(string prefix) => header.Select((h, i) => (h, i)).Where(x => x.h.StartsWith(prefix, StringComparison.Ordinal))
            .Select(x => (Name: x.h[prefix.Length..], Count: int.Parse(row[x.i], CultureInfo.InvariantCulture)))
            .Where(x => x.Count != 0).Select(x => new HistoricalCardQuantity(resolve(x.Name), x.Count)).ToArray();
        var pack = (Int("pack_number") ?? throw new InvalidDataException("Pack missing.")) + 1;
        var pick = (Int("pick_number") ?? throw new InvalidDataException("Pick missing.")) + 1;
        var pool = Quantities("pool_");
        var skill = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in new[] { "user_n_games_bucket", "user_game_win_rate_bucket", "user_n_matches_bucket", "user_match_win_rate_bucket" })
            if (Value(field) is { } v) skill[field] = v; // Lifetime buckets are descriptive metadata, never model features.
        var input = new PickTimeState(Value("expansion")!, Value("event_type")!, Value("draft_id")!,
            Value("draft_time") is { } time ? DateTimeOffset.Parse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) : null,
            pack, pick, (pack - 1) * options.PicksPerPack + pick - 1, Quantities("pack_card_"), pool, [],
            pool.Sum(c => c.Count) == (pack - 1) * options.PicksPerPack + pick - 1);
        var wins=Int("event_match_wins");var losses=Int("event_match_losses");
        var terminal=options.Format=="TradDraft" ? wins.HasValue&&losses.HasValue&&wins+losses==3
            : wins==7||losses==3;
        return new(HistoricalJson.Schema, options.DatasetId, input,
            new(resolve(Value("pick") ?? throw new InvalidDataException("Selected card missing.")), Rate("pick_maindeck_rate"), Rate("pick_sideboard_in_rate")),
            new(terminal?wins:null, terminal?losses:null, ReportedEventWins:wins, ReportedEventLosses:losses,
                OutcomeEvidence:terminal ? "Public source event record reaches the supported format's terminal record; retained as final outcome."
                    : "Public source event record at dataset publication; completion/retirement not established. Final outcome remains missing."),
            new(options.Source, options.SourceUri, hash, number, options.SourceSchema),
            new(Value("rank")??Value("user_rank"),skill,"Source rank and lifetime game/match buckets; exact as-of-pick availability not established. Analysis only."));
    }
    public static void ValidateDecision(HistoricalDraftDecision decision, DatasetImportOptions options)
    {
        if(decision.Inputs is null || decision.Label is null || decision.Outcome is null)
            throw new InvalidDataException("Inputs, label and outcome envelopes required (missing outcome fields remain null).");
        var i = decision.Inputs;
        if(i.OfferedCards is null||i.PoolBeforePick is null||i.PriorPicks is null||decision.Label.ActualPickedCard is null)
            throw new InvalidDataException("Required decision arrays/actual pick missing.");
        if (decision.DatasetVersion != HistoricalJson.Schema || i.SetCode != options.SetCode || i.Format != options.Format
            || string.IsNullOrWhiteSpace(i.DraftId) || i.PackNumber is < 1 or > 3 || i.PickNumber < 1 || i.PickNumber > options.PicksPerPack
            || i.GlobalPickIndex != (i.PackNumber - 1) * options.PicksPerPack + i.PickNumber - 1)
            throw new InvalidDataException("Decision scope/coordinate mismatch.");
        foreach (var q in i.OfferedCards.Concat(i.PoolBeforePick).Concat(decision.Outcome.FinalMainDeck ?? [])
            .Concat(decision.Outcome.FinalPool ?? []).Concat(decision.Outcome.FinalSideboard ?? []))
            if (q is null || q.Card is null || q.Count is < 1 or > 100 || string.IsNullOrWhiteSpace(q.Card.SourceIdentity)) throw new InvalidDataException("Invalid card quantity.");
        if (i.OfferedCards.Sum(c => c.Count) is < 1 or > 30 || i.PoolBeforePick.Sum(c => c.Count) > i.GlobalPickIndex
            || i.PoolIsComplete && i.PoolBeforePick.Sum(c => c.Count) != i.GlobalPickIndex)
            throw new InvalidDataException("Invalid offered/past pool size; future inventory prohibited.");
        if (i.PriorPicks.Any(p => p.Pack < 1 || p.Pack > 3 || p.Pick < 1 || p.Pick > options.PicksPerPack
            || p.Pack > i.PackNumber || p.Pack == i.PackNumber && p.Pick >= i.PickNumber)
            || i.PriorPicks.Select(p => (p.Pack, p.Pick)).Distinct().Count() != i.PriorPicks.Length)
            throw new InvalidDataException("Future/duplicate pick in pick-time history.");
        if (!i.OfferedCards.Any(c => c.Card.SourceIdentity == decision.Label.ActualPickedCard.SourceIdentity
            || c.Card.CanonicalId is not null && c.Card.CanonicalId == decision.Label.ActualPickedCard.CanonicalId))
            throw new InvalidDataException("Actual pick not in offered pack.");
        foreach (var rate in new[] { decision.Label.ObservedMaindeckRate, decision.Label.ObservedSideboardInRate })
            if (rate is { } r && (!double.IsFinite(r) || r < 0 || r > 1)) throw new InvalidDataException("Invalid participation rate.");
        if (decision.Outcome.FinalWins < 0 || decision.Outcome.FinalLosses < 0 || decision.Outcome.ReportedEventWins<0||decision.Outcome.ReportedEventLosses<0)
            throw new InvalidDataException("Invalid outcome.");
    }
}
