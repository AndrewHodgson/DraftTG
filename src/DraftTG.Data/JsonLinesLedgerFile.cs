using System.Text;

namespace DraftTG.Data;

/// <summary>
/// Minimal append-only JSON Lines file. Each append writes one complete line in a single write and flushes it
/// to disk. A torn final line from an unexpected exit is left in place for the reader to skip, and the next
/// append starts on a fresh line so it can never merge with the torn fragment. Callers serialize appends.
/// </summary>
public sealed class JsonLinesLedgerFile(string path)
{
    public string Path { get; } = path;

    public static JsonLinesLedgerFile OrderEvidence(IApplicationDataPathProvider paths) =>
        new(System.IO.Path.Combine(paths.GetApplicationDataDirectory(), "localization", "order-evidence.jsonl"));

    public IReadOnlyList<string> ReadLines()
    {
        if (!File.Exists(Path)) return [];
        using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }

    public void AppendLine(string json)
    {
        if (json.Contains('\n') || json.Contains('\r')) throw new ArgumentException("A ledger record must be a single line.", nameof(json));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using var stream = new FileStream(Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var separator = false;
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            separator = stream.ReadByte() != '\n';
        }
        stream.Seek(0, SeekOrigin.End);
        var bytes = Encoding.UTF8.GetBytes((separator ? "\n" : "") + json + "\n");
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }
}
