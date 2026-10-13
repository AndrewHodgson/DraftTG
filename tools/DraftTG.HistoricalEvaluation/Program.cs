using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Schema;
using DraftTG.Data;
using DraftTG.HistoricalEvaluation;

if (args.Length == 0 || args[0] is "help" or "--help")
{
    Console.WriteLine("""
        Offline historical evaluator; no scoring changes or UI required.
        import --source FILE --options OPTIONS.json --catalog CARDS.json --splits SPLITS.json --db FILE.sqlite
        validate --db FILE.sqlite --splits SPLITS.json --output REPORT.json
        import-decks --source GAME.csv[.gz] --options OPTIONS.json --catalog CARDS.json --db FILE.sqlite
        decks --db FILE.sqlite --catalog CARDS.json --output DIRECTORY
        stats --catalog CARDS.json --stats STATS.json --bonus BONUS.json --output DIRECTORY
        evaluate --db FILE.sqlite --catalog CARDS.json --stats STATS.json --splits SPLITS.json --bonus BONUS.json
                 --model contextual-pick-score-v1.6.2 --output DIRECTORY [--split Holdout] [--sets OTJ,WOE]
        compare (same options) --model-a contextual-pick-score-v1.6.1 --model-b contextual-pick-score-v1.6.2
        evaluate --models contextual-pick-score-v1.6,contextual-pick-score-v1.6.1,contextual-pick-score-v1.6.2
        evaluate --research-q PHASE_B_Q_VALUES.json (offline Q-only ablation; frozen L/A/D, weights, commitment and R)
        golden --root REPO --output DIRECTORY (reuses frozen regression tests; requires built solution)
        report --source summary.json --output DIRECTORY
        schema --output DIRECTORY (JSON schemas exported from the actual canonical C# types)
        Regime defaults to MatureFormatReplay; chronological mode refuses missing/prior-date evidence.
        Ctrl+C cancels; failed/cancelled imports roll back. Exact completed imports are hash-cached.
        """); return;
}
try
{
    var options = new Dictionary<string,string>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--") || i + 1 >= args.Length) throw new ArgumentException("Expected --option value pairs.");
        options.Add(args[i][2..],args[i+1]);
    }
    string Required(string key) => options.TryGetValue(key,out var v) ? v : throw new ArgumentException($"--{key} required.");
    string Optional(string key, string fallback) => options.GetValueOrDefault(key,fallback);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_,e) => { e.Cancel = true; cancellation.Cancel(); };
    var token = cancellation.Token;
    if(args[0]=="schema")
    {
        var output=Required("output");Directory.CreateDirectory(output);
        var schemaOptions=new JsonSerializerOptions(HistoricalJson.Compact)
            { TypeInfoResolver=new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
        foreach(var entry in new (string Name,Type Type)[]{("historical-decision-v1",typeof(HistoricalDraftDecision)),
            ("historical-dataset-manifest-v1",typeof(HistoricalDatasetManifest)),("historical-card-stats-v1",typeof(HistoricalStatsSnapshot)),
            ("historical-deck-v1",typeof(HistoricalLimitedDeck)),("historical-splits-v1",typeof(SetSplitManifest))})
        {
            var schema=schemaOptions.GetJsonSchemaAsNode(entry.Type,new JsonSchemaExporterOptions {TreatNullObliviousAsNonNullable=true});
            File.WriteAllText(Path.Combine(output,entry.Name+".schema.json"),schema.ToJsonString(HistoricalJson.Pretty));
        }
        return;
    }
    if (args[0] == "golden")
    {
        var root = Path.GetFullPath(Required("root")); var output = Path.GetFullPath(Required("output")); Directory.CreateDirectory(output);
        var runDirectory=Path.Combine(output,"run-"+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffff"));Directory.CreateDirectory(runDirectory);
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory=root, UseShellExecute=false };
        foreach (var a in new[] { "test", "DraftTG.sln", "--no-build", "--filter",
            "FullyQualifiedName~LiveP1P8FailureReproductionTests|FullyQualifiedName~SparsePrimaryQualityTests|FullyQualifiedName~ContextualPickScoreDeckFitTests|FullyQualifiedName~ContextualPickScoreV1FreezeTests",
            "--logger", "trx;LogFilePrefix=golden", "--results-directory", runDirectory }) start.ArgumentList.Add(a);
        using var process = Process.Start(start)!; await process.WaitForExitAsync(token);
        var counters=Directory.GetFiles(runDirectory,"golden*.trx").Select(path=>System.Xml.Linq.XDocument.Load(path)
            .Descendants().Single(n=>n.Name.LocalName=="Counters")).ToArray();
        var tested=counters.Sum(c=>long.Parse(c.Attribute("total")!.Value));
        var failed=counters.Sum(c=>long.Parse(c.Attribute("failed")!.Value));
        var fixturePaths = new[] { "phase9e2b-p1p8-rg-failure.json", "phase9e2b-p1p8-v1.6.2.json", "phase9f2-live-pack-stat-provenance.json", "phase9f2-live-pack-v1.6.1.json" };
        var result = new { Passed=process.ExitCode==0&&tested>0&&failed==0, Tested=tested, Failed=failed, ExitCode=process.ExitCode, RunDirectory=runDirectory, Build=BuildIdentity(),
            Interpretation="Golden must-not-regress tests, separate from historical human agreement.",
            Fixtures=fixturePaths.Select(p => new { Path=p, Sha256=HistoricalStore.HashFile(Path.Combine(root,"tests","Fixtures",p)) }) };
        File.WriteAllText(Path.Combine(output,"golden.json"),JsonSerializer.Serialize(result,HistoricalJson.Pretty));
        Environment.ExitCode=process.ExitCode==0&&tested==0?1:process.ExitCode; return;
    }
    if (args[0] == "report")
    { var summary = HistoricalJson.Read<EvaluationSummary>(File.ReadAllText(Required("source"))); Reports.Write(summary,Required("output")); return; }
    if(args[0]=="stats")
    {
        var featureCatalog=new ScryfallCardCatalogDecoder().Decode(File.ReadAllText(Required("catalog")));
        var resolver=new HistoricalIdentityResolver(featureCatalog);
        var snapshots=HistoricalJson.Read<HistoricalStatsSnapshot[]>(File.ReadAllText(Required("stats")));ValidateStats(snapshots);
        var bonus=HistoricalJson.Read<Dictionary<string,string[]>>(File.ReadAllText(Required("bonus")));
        var output=Required("output");Directory.CreateDirectory(output);
        using var writer=new StreamWriter(Path.Combine(output,"card-stat-features.jsonl"));
        var coverages=new List<object>();
        foreach(var snapshot in snapshots)
        {
            var mapped=snapshot.Cards.Select(c=>new {Identity=resolver.Resolve(c.SourceIdentity,snapshot.SetCode,bonus.GetValueOrDefault(snapshot.SetCode,[])),Features=c}).ToArray();
            foreach(var row in mapped)writer.WriteLine(HistoricalJson.Serialize(row));
            coverages.Add(new {snapshot.SetCode,snapshot.Format,snapshot.Regime,snapshot.Provenance,
                Rows=mapped.Length,Resolved=mapped.Count(r=>r.Identity.Identifier is not null),
                Unresolved=mapped.Count(r=>r.Identity.Identifier is null),GIH=snapshot.Cards.Count(c=>c.GIHWinRate.HasValue&&c.GIHGames>0),
                ALSA=snapshot.Cards.Count(c=>c.ALSA.HasValue),ATA=snapshot.Cards.Count(c=>c.ATA.HasValue),
                GP=snapshot.Cards.Count(c=>c.GamesPlayed.HasValue),OH=snapshot.Cards.Count(c=>c.OpeningHandWinRate.HasValue),
                Drawn=snapshot.Cards.Count(c=>c.DrawnWinRate.HasValue),Drafted=snapshot.Cards.Count(c=>c.DraftedCount.HasValue),
                SampleSeenPositions=snapshot.Cards.Count(c=>c.SampleAverageSeenPosition.HasValue),
                SampleTakenPositions=snapshot.Cards.Count(c=>c.SampleAverageTakenPosition.HasValue)});
        }
        File.WriteAllText(Path.Combine(output,"feature-coverage.json"),JsonSerializer.Serialize(new {SourceHash=HistoricalStore.HashFile(Required("stats")),Datasets=coverages,
            LaneReadiness="Observed source pick position is in PickTimeState; published ALSA/ATA remain nullable. Sample-derived position descriptors are separate and never substituted into baseline lane inputs. No lane formula added."},HistoricalJson.Pretty));return;
    }
    using var store = new HistoricalStore(Required("db"));
    if (args[0] == "validate")
    {
        var splits = HistoricalJson.Read<SetSplitManifest>(File.ReadAllText(Required("splits")));
        var manifests = store.Manifests();
        foreach (var m in manifests) if (splits.For(m.SetCode) != m.Partition || m.SplitHash != HistoricalStore.HashText(HistoricalJson.Serialize(splits)))
            throw new InvalidDataException("Imported split differs from evaluation split; reimport into a new store.");
        var result = new { Valid=true, Datasets=manifests, TotalDecisions=manifests.Sum(m=>m.PickCount),
            IdentityResolvedOccurrences=manifests.Sum(m=>m.IdentityResolvedCount), IdentityUnresolvedOccurrences=manifests.Sum(m=>m.IdentityUnresolvedCount),
            LeakagePolicy="Input and label columns separated; replay streams drafts in source coordinates. No individual-pick random split." };
        var output = Required("output"); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(result,HistoricalJson.Pretty)); Console.WriteLine(output); return;
    }
    var catalogFile = Required("catalog");
    var catalog = new ScryfallCardCatalogDecoder().Decode(File.ReadAllText(catalogFile));
    var identities = new HistoricalIdentityResolver(catalog);
    if (args[0] == "decks")
    {
        var output=Required("output"); Directory.CreateDirectory(output);
        new HistoricalDeckImporter(store,catalog,identities).Export(Path.Combine(output,"decks.jsonl"),Path.Combine(output,"deck-aggregates.json"),token); return;
    }
    if (args[0] is "import" or "import-decks")
    {
        var importOptions = HistoricalJson.Read<DatasetImportOptions>(File.ReadAllText(Required("options")));
        object result;
        if (args[0] == "import-decks") result = new HistoricalDeckImporter(store,catalog,identities).ImportGames(Required("source"),importOptions.DatasetId+"-games",
            importOptions.SetCode,importOptions.Format,importOptions.BonusSets,importOptions.SourceUri.Replace("draft_data/","game_data/").Replace("draft_data_public.","game_data_public."),token);
        else
        {
            var splits = HistoricalJson.Read<SetSplitManifest>(File.ReadAllText(Required("splits")));
            result = new HistoricalImporter(store,identities,splits,HistoricalStore.HashFile(catalogFile)).Import(Required("source"),importOptions,
                count=>Console.Error.WriteLine($"Import rows: {count}"),token);
        }
        Console.WriteLine(JsonSerializer.Serialize(result,HistoricalJson.Pretty)); return;
    }
    if (args[0] is "evaluate" or "compare")
    {
        var statsFile = Required("stats"); var stats = HistoricalJson.Read<HistoricalStatsSnapshot[]>(File.ReadAllText(statsFile));
        ValidateStats(stats);
        var splits = HistoricalJson.Read<SetSplitManifest>(File.ReadAllText(Required("splits")));
        foreach (var m in store.Manifests())
            if (splits.For(m.SetCode) != m.Partition || m.SplitHash != HistoricalStore.HashText(HistoricalJson.Serialize(splits))
                || m.IdentityCatalogHash != HistoricalStore.HashFile(catalogFile)) throw new InvalidDataException("Evaluation catalog/split differs from import provenance.");
        var bonuses = HistoricalJson.Read<Dictionary<string,string[]>>(File.ReadAllText(Required("bonus")));
        var models = args[0] == "compare" ? new[] { Required("model-a"),Required("model-b") }
            : Optional("models",Optional("model","contextual-pick-score-v1.6.2")).Split(',');
        var partition = options.TryGetValue("split",out var p) ? Enum.Parse<DatasetPartition>(p,true) : (DatasetPartition?)null;
        var sets = options.TryGetValue("sets",out var s) ? s.ToUpperInvariant().Split(',').ToHashSet(StringComparer.Ordinal) : null;
        var output = Required("output"); Directory.CreateDirectory(output);
        var summary = new HistoricalEvaluator(store,new(catalog,identities,stats,bonuses),splits).Evaluate(models,
            Enum.Parse<EvaluationRegime>(Optional("regime","MatureFormatReplay"),true),Path.Combine(output,"decisions.jsonl"),BuildIdentity(),
            HistoricalStore.HashFile(catalogFile),HistoricalStore.HashFile(statsFile),partition,sets,
            count=>Console.Error.WriteLine($"Replayed decisions: {count}"),token,
            options.TryGetValue("research-q",out var researchQ)?new ExperimentalQualitySubstitution(File.ReadAllText(researchQ)):null);
        Reports.Write(summary,output);
        if (options.TryGetValue("golden-report",out var golden)) File.Copy(golden,Path.Combine(output,"golden.json"),true);
        foreach (var m in models) Console.WriteLine($"{m}: {HistoricalJson.Serialize(summary.Metrics[m+" | overall"])}");
        Console.WriteLine($"{summary.Decisions} decisions / {summary.Seconds:F2}s / {summary.DecisionsPerSecond:F2} decisions/s"); return;
    }
    throw new ArgumentException("Unknown command.");
}
catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled; incomplete import transaction rolled back."); Environment.ExitCode=130; }
catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode=1; }

static string BuildIdentity()
{
    var git = new ProcessStartInfo("git") { UseShellExecute=false, RedirectStandardOutput=true };
    git.ArgumentList.Add("rev-parse");git.ArgumentList.Add("HEAD");
    using var process = Process.Start(git)!;var commit=process.StandardOutput.ReadToEnd().Trim();process.WaitForExit();
    var assembly=typeof(HistoricalReplayAdapter).Assembly.Location;
    return $"git={commit}; evaluationAssemblySha256={HistoricalStore.HashFile(assembly)}; scoreAssemblySha256={HistoricalStore.HashFile(typeof(DraftTG.RecommendationEngine.ContextualPickScoreConfiguration).Assembly.Location)}; runtime={Environment.Version}; working-tree build (see baseline-freeze.json)";
}
static void ValidateStats(HistoricalStatsSnapshot[] snapshots)
{
    foreach(var s in snapshots)
    {
        if(!Enum.IsDefined(s.Regime))throw new InvalidDataException("Invalid statistics evaluation regime.");
        if(s.Cards.Any(c=>c.PrimaryFormat!=s.Format))throw new InvalidDataException("Primary stats require exact draft format.");
        foreach(var c in s.Cards.Concat(s.AlternateQualityCards??[]))
        {
            if (c.SetCode!=s.SetCode || string.IsNullOrWhiteSpace(c.SourceIdentity)) throw new InvalidDataException("Stats set/identity mismatch.");
            foreach(var rate in new[]{c.GIHWinRate,c.GamesPlayedWinRate,c.OpeningHandWinRate,c.DrawnWinRate,c.SetBaseline})
                if(rate is { } r && (!double.IsFinite(r)||r<0||r>1)) throw new InvalidDataException("Invalid stat rate; percentages must be explicit fractions.");
            if(c.GIHGames<0||c.GamesPlayed<0||c.DraftedCount<0||c.ALSA<1||c.ATA<1) throw new InvalidDataException("Invalid stat counts/positions.");
        }
    }
}
static class Reports
{
    public static void Write(EvaluationSummary s,string output)
    {
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(s,HistoricalJson.Pretty));
        var text = new StringBuilder($"# Historical baseline evaluation\n\nRegime: **{s.Regime}**. {s.Interpretation}\n\nBuild: `{s.Build}`\n\nDecisions: {s.Decisions}; drafts: {s.Drafts}; seconds: {s.Seconds:F3}; decisions/s: {s.DecisionsPerSecond:F3}.\n\n");
        text.AppendLine("| Model | Eligible / total | Top 1 | Top 3 | Top 5 | MRR | Normalized rank | Score gap |\n|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach(var model in s.Models)
        {
            var m=s.Metrics.GetValueOrDefault(model+" | overall"); if(m is null)continue;
            text.AppendLine(FormattableString.Invariant($"| {model} | {m.Eligible}/{m.Decisions} | {m.Top1:P2} | {m.Top3:P2} | {m.Top5:P2} | {m.MeanReciprocalRank:F6} | {m.MeanNormalizedRank:F6} | {m.MeanScoreGap:F6} |"));
        }
        text.AppendLine("\nHuman agreement is revealed preference. Association with an event record does not prove individual picks were optimal. No winner is declared from these aggregates.\n");
        foreach(var c in s.Comparisons)text.AppendLine($"- {c.ModelA} → {c.ModelB}: {c.RankingChanged} ranking changes; {c.ActualRankImproved} actual-rank improvements; {c.ActualRankRegressed} regressions; {c.MissingDataDifferences} missing-data differences.");
        text.AppendLine("\n## Coverage and limitations\n"); foreach(var l in s.Limitations)text.AppendLine("- "+l);
        text.AppendLine("\nAll set/format/pack/pick/record/rank/skill/card-count/commitment strata and the bounded largest changes are in summary.json and metrics.csv. Full occurrence scores/components and labels are in ignored decisions.jsonl. Golden test results, if supplied, are in separate golden.json.\n");
        File.WriteAllText(Path.Combine(output,"report.md"),text.ToString());
        using var csv=new StreamWriter(Path.Combine(output,"metrics.csv"));
        csv.WriteLine("stratum,total,eligible,top1,top3,top5,mrr,normalized_rank,score_gap");
        foreach(var pair in s.Metrics)
        { var m=pair.Value; csv.WriteLine(FormattableString.Invariant($"\"{pair.Key.Replace("\"","\"\"")}\",{m.Decisions},{m.Eligible},{m.Top1},{m.Top3},{m.Top5},{m.MeanReciprocalRank},{m.MeanNormalizedRank},{m.MeanScoreGap}")); }
    }
}
