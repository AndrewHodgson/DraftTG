using DraftTG.ArenaIntegration;

namespace DraftTG.LocalizationAudit;

internal sealed record SemanticSnapshot(DateTime LastWriteUtc, string Status, string? PackLabel, int[] Cards,
    int[] PreviousPackCards, int RelevantEvents, int ParseErrors, bool LogBounded, string[] FactTrace)
{
    public bool SamePack(SemanticSnapshot other) => Status == other.Status && PackLabel == other.PackLabel && Cards.SequenceEqual(other.Cards);
    public static SemanticSnapshot Read()
    {
        var path = new WindowsArenaLogLocationProvider().GetLocation().FilePath;
        var info = new FileInfo(path);
        if (info.Length > 64L * 1024 * 1024) throw new InvalidOperationException("Log exceeds the 64 MiB audit limit; use a smaller current log snapshot.");
        var engine = new ArenaDraftStateEngine(); var parser = new ArenaDraftLogParser(); var events = 0; var errors = 0;
        int[] previous = []; int[] lastPack = []; var trace = new Queue<string>();
        void Trace(string value) { trace.Enqueue(value); if (trace.Count > 32) trace.Dequeue(); }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (stream.Position > 64L * 1024 * 1024) throw new InvalidOperationException("Log grew beyond the 64 MiB audit limit.");
            try
            {
                foreach (var fact in parser.Parse(new ArenaLogSourceEvent.Line(line)))
                {
                    Trace(fact switch
                    {
                        ArenaDraftLogEvent.PackPresented p => $"PackPresented P{p.Pack.Coordinate.Pack}P{p.Pack.Coordinate.Pick}: {string.Join(',', p.Pack.CardIdentifiers.Select(c => c.Value))}",
                        ArenaDraftLogEvent.PickSubmitted p => $"PickSubmitted P{p.Pick.Coordinate.Pack}P{p.Pick.Coordinate.Pick}",
                        _ => fact.GetType().Name
                    });
                    events++; engine.Apply(fact);
                    if (fact is ArenaDraftLogEvent.PackPresented pack)
                    {
                        var ids = pack.Pack.CardIdentifiers.Select(c => c.Value).ToArray();
                        if (!ids.SequenceEqual(lastPack)) { previous = lastPack; lastPack = ids; }
                    }
                }
            }
            catch (ArenaDraftLogParseException ex) { errors++; Trace($"Parse error: {ex.Kind}; field={ex.Field}; pack={ex.Pack}; pick={ex.Pick}"); }
            catch (ArenaDraftStateConflictException ex) { errors++; Trace($"Replay conflict: {ex.Kind}; P{ex.Coordinate.Pack}P{ex.Coordinate.Pick}"); }
        }
        var state = engine.Current; var current = state.CurrentPack;
        return new(info.LastWriteTimeUtc, state.Status.ToString(), current is null ? null : $"P{current.Coordinate.Pack}P{current.Coordinate.Pick}",
            state.Status == ArenaDraftSessionStatus.Active && current is not null ? current.CardIdentifiers.Select(c => c.Value).ToArray() : [], previous, events, errors, true, trace.ToArray());
    }
}
