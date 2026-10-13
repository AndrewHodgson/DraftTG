using System.IO.Compression;
using System.Text;

namespace DraftTG.HistoricalEvaluation;

/// <summary>RFC-style quoted fields/newlines, bounded by one record; no corpus-sized buffers.</summary>
public sealed class StreamingCsv : IDisposable
{
    private readonly TextReader _reader;
    private readonly int _maximumRecordCharacters;
    public StreamingCsv(TextReader reader, int maximumRecordCharacters = 4 * 1024 * 1024)
    { _reader = reader; _maximumRecordCharacters = maximumRecordCharacters; }
    public static StreamingCsv Open(string file) => new(new StreamReader(file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
        ? new GZipStream(File.OpenRead(file), CompressionMode.Decompress) : File.OpenRead(file), Encoding.UTF8, true, 64 * 1024));
    public string[]? Read(CancellationToken token = default)
    {
        var fields = new List<string>(); var field = new StringBuilder();
        bool quoted = false, closed = false, any = false; int length = 0;
        while (true)
        {
            if ((length & 4095) == 0) token.ThrowIfCancellationRequested();
            var ch = _reader.Read();
            if (ch < 0)
            {
                if (quoted) throw new InvalidDataException("Unterminated quoted CSV field at EOF.");
                if (!any) return null;
                fields.Add(field.ToString()); return fields.ToArray();
            }
            any = true;
            if (++length > _maximumRecordCharacters) throw new InvalidDataException("CSV record exceeds bounded parser limit.");
            var c = (char)ch;
            if (quoted)
            {
                if (c == '"')
                {
                    if (_reader.Peek() == '"') { _reader.Read(); field.Append('"'); }
                    else { quoted = false; closed = true; }
                }
                else field.Append(c);
            }
            else if (c == ',' || c is '\r' or '\n')
            {
                fields.Add(field.ToString()); field.Clear(); closed = false;
                if (c == ',') continue;
                if (c == '\r' && _reader.Peek() == '\n') _reader.Read();
                return fields.ToArray();
            }
            else if (c == '"' && field.Length == 0 && !closed) quoted = true;
            else if (closed || c == '"') throw new InvalidDataException("Invalid characters in quoted CSV field.");
            else field.Append(c);
        }
    }
    public void Dispose() => _reader.Dispose();
}
