using System.Text.Json;
using DraftTG.ArenaIntegration.Memory;
using DraftTG.ArenaIntegration.Memory.Windows;

namespace DraftTG.App;

internal static class ArenaMemoryDraftCommand
{
    internal static int Run(string[] args)
    {
        int? pid = null;
        var json = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--json") json = true;
            else if (args[i] == "--pid" && i + 1 < args.Length && int.TryParse(args[++i], out var value) && value > 0) pid = value;
            else { Console.Error.WriteLine("Usage: DraftTG.App --arena-memory-draft[-once] [--pid <MTGA PID>] [--json]"); return 1; }
        }
        var snapshot = new ArenaDraftMemoryReader().ReadOnce(pid);
        Console.WriteLine(json ? JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }) : ArenaDraftMemoryFormatter.Format(snapshot));
        if (!json && snapshot.Status == ArenaMemoryStatus.Success)
        {
            MemoryCardNames names;
            try { names = MemoryCardNames.LoadAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { Console.WriteLine("Catalog unavailable: " + ex.Message); names = new(null, null); }
            Console.WriteLine("Resolved memory order:"); MemoryCardNames.Print(names.Resolve(snapshot));
        }
        return snapshot.Status is ArenaMemoryStatus.Success or ArenaMemoryStatus.DraftSceneNotActive or ArenaMemoryStatus.NotRunning ? 0 : 2;
    }
}
