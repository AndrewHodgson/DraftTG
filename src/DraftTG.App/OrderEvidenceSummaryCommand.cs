using DraftTG.Application;

namespace DraftTG.App;

/// <summary>
/// Developer summary of accumulated Phase 9E.1 order evidence. Reads the ledger and opens the Arena card database
/// read-only; writes nothing unless --output is given. No capture, network or Arena process access.
/// </summary>
internal static class OrderEvidenceSummaryCommand
{
    private const string Usage = "Usage: DraftTG.App --order-evidence-summary [--ledger <order-evidence.jsonl>] [--db <Raw_CardDatabase file or directory>] [--output <text file>]";

    public static int Run(string[] args)
    {
        try
        {
            string? ledger = null, database = null, output = null;
            for (var i = 1; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length) throw new ArgumentException(Usage);
                switch (args[i])
                {
                    case "--ledger": ledger = args[i + 1]; break;
                    case "--db": database = args[i + 1]; break;
                    case "--output": output = args[i + 1]; break;
                    default: throw new ArgumentException(Usage);
                }
            }
            var service = OrderEvidenceRecorder.CreateDefaultService(ledger, database);
            service.Initialize();
            var text = ArenaDisplayOrderEvidencePresentation.Summary(service);
            if (output is not null) File.WriteAllText(output, text);
            Console.WriteLine(text);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}"); return 1; }
    }
}
