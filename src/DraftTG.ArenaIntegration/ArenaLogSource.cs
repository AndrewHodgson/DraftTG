using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace DraftTG.ArenaIntegration;

public abstract record ArenaLogSourceEvent
{
    private ArenaLogSourceEvent() { }

    public sealed record Line(string Text) : ArenaLogSourceEvent;
    public sealed record SourceReset : ArenaLogSourceEvent;
}

public interface IArenaLogSource
{
    IAsyncEnumerable<ArenaLogSourceEvent> ReadEventsAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ArenaLogSourceException(string message, Exception? innerException = null)
    : IOException(message, innerException);

/// <summary>
/// Asynchronously follows one Arena Player.log without interpreting its lines.
/// Monitoring is produced on the thread pool rather than the Avalonia UI thread.
/// </summary>
public sealed class FileArenaLogSource : IArenaLogSource
{
    private const int ReadChunkSize = 64 * 1024;
    private const int ProbeSize = 4 * 1024;
    private readonly TimeSpan _pollingInterval;

    public FileArenaLogSource(
        ArenaLogLocation? location = null,
        TimeSpan? pollingInterval = null)
    {
        Location = location ?? ArenaLogLocationProviderFactory.CreateDefault().GetLocation();
        _pollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(300);
        if (_pollingInterval <= TimeSpan.Zero)
        {
            _pollingInterval = TimeSpan.FromMilliseconds(1);
        }
    }

    public FileArenaLogSource(string filePath, TimeSpan? pollingInterval = null)
        : this(new ArenaLogLocation(filePath), pollingInterval) { }

    public ArenaLogLocation Location { get; }

    public IAsyncEnumerable<ArenaLogSourceEvent> ReadEventsAsync(
        CancellationToken cancellationToken = default)
    {
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateUnbounded<ArenaLogSourceEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var producer = Task.Run(
            () => MonitorAsync(channel.Writer, linkedCancellation.Token),
            CancellationToken.None);

        return ConsumeAsync(channel.Reader, producer, linkedCancellation, cancellationToken);
    }

    private static async IAsyncEnumerable<ArenaLogSourceEvent> ConsumeAsync(
        ChannelReader<ArenaLogSourceEvent> reader,
        Task producer,
        CancellationTokenSource linkedCancellation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            await linkedCancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            linkedCancellation.Dispose();
        }
    }

    private async Task MonitorAsync(
        ChannelWriter<ArenaLogSourceEvent> writer,
        CancellationToken cancellationToken)
    {
        var state = new MonitorState();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await PollAsync(state, writer, cancellationToken).ConfigureAwait(false);
                await Task.Delay(_pollingInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            writer.TryComplete();
        }
        catch (Exception error)
        {
            writer.TryComplete(error);
        }
    }

    private async Task PollAsync(
        MonitorState state,
        ChannelWriter<ArenaLogSourceEvent> writer,
        CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(
                Location.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                ReadChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            if (state.HasGeneration) state.WasMissing = true;
            return;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ArenaLogSourceException(
                $"Could not open Arena log '{Location.FilePath}'.",
                error);
        }

        await using (stream.ConfigureAwait(false))
        {
            var length = stream.Length;
            var contentChanged = state.HasGeneration
                && !state.WasMissing
                && length >= state.Offset
                && !await ProbeMatchesAsync(stream, state.Probe, cancellationToken).ConfigureAwait(false);
            var startsNewGeneration = state.HasGeneration
                && (state.WasMissing || length < state.Offset || contentChanged);

            if (startsNewGeneration)
            {
                state.Offset = 0;
                state.PartialLine.Clear();
                state.Probe = [];
                await writer.WriteAsync(new ArenaLogSourceEvent.SourceReset(), cancellationToken)
                    .ConfigureAwait(false);
            }

            state.HasGeneration = true;
            state.WasMissing = false;

            try
            {
                stream.Seek(state.Offset, SeekOrigin.Begin);
                var buffer = new byte[ReadChunkSize];
                int count;
                while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    state.Offset += count;
                    state.PartialLine.AddRange(buffer.AsSpan(0, count).ToArray());
                    await EmitCompleteLinesAsync(state.PartialLine, writer, cancellationToken)
                        .ConfigureAwait(false);
                }

                state.Probe = await ReadProbeAsync(stream, state.Offset, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new ArenaLogSourceException(
                    $"Could not read Arena log '{Location.FilePath}'.",
                    error);
            }
        }
    }

    private static async Task EmitCompleteLinesAsync(
        List<byte> bufferedBytes,
        ChannelWriter<ArenaLogSourceEvent> writer,
        CancellationToken cancellationToken)
    {
        while (bufferedBytes.IndexOf((byte)'\n') is var newlineIndex && newlineIndex >= 0)
        {
            var count = newlineIndex;
            if (count > 0 && bufferedBytes[count - 1] == (byte)'\r') count--;
            var line = Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(bufferedBytes)[..count]);
            bufferedBytes.RemoveRange(0, newlineIndex + 1);
            await writer.WriteAsync(new ArenaLogSourceEvent.Line(line), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<bool> ProbeMatchesAsync(
        FileStream stream,
        byte[] expected,
        CancellationToken cancellationToken)
    {
        if (expected.Length == 0) return true;
        stream.Seek(0, SeekOrigin.Begin);
        var actual = new byte[expected.Length];
        var read = await stream.ReadAtLeastAsync(
            actual,
            expected.Length,
            throwOnEndOfStream: false,
            cancellationToken).ConfigureAwait(false);
        return read == expected.Length && actual.AsSpan().SequenceEqual(expected);
    }

    private static async Task<byte[]> ReadProbeAsync(
        FileStream stream,
        long observedLength,
        CancellationToken cancellationToken)
    {
        var count = (int)Math.Min(observedLength, ProbeSize);
        if (count == 0) return [];

        stream.Seek(0, SeekOrigin.Begin);
        var probe = new byte[count];
        var read = await stream.ReadAtLeastAsync(
            probe,
            count,
            throwOnEndOfStream: false,
            cancellationToken).ConfigureAwait(false);
        return read == count ? probe : probe[..read];
    }

    private sealed class MonitorState
    {
        public bool HasGeneration { get; set; }
        public bool WasMissing { get; set; }
        public long Offset { get; set; }
        public List<byte> PartialLine { get; } = [];
        public byte[] Probe { get; set; } = [];
    }
}
