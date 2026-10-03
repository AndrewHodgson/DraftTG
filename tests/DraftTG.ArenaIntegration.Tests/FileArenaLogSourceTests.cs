using System.Text;
using DraftTG.ArenaIntegration;

namespace DraftTG.ArenaIntegration.Tests;

public sealed class FileArenaLogSourceTests
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(10);

    [Fact]
    public void MacDefaultLocationUsesCurrentHomeShape()
    {
        var location = new MacArenaLogLocationProvider("/Users/example").GetLocation();
        Assert.Equal(
            Path.Combine("/Users/example", "Library", "Logs", "Wizards Of The Coast", "MTGA", "Player.log"),
            location.FilePath);
    }

    [Fact]
    public void WindowsDefaultLocationUsesLocalLow()
    {
        var location = new WindowsArenaLogLocationProvider(@"C:\Users\Example").GetLocation();
        Assert.Equal(
            Path.Combine(@"C:\Users\Example", "AppData", "LocalLow", "Wizards Of The Coast", "MTGA", "Player.log"),
            location.FilePath);
        Assert.DoesNotContain($"AppData{Path.DirectorySeparatorChar}Local{Path.DirectorySeparatorChar}", location.FilePath);
    }

    [Fact]
    public void CustomLocationIsRetained()
    {
        var source = new FileArenaLogSource("/tmp/custom-player.log", PollingInterval);
        Assert.Equal("/tmp/custom-player.log", source.Location.FilePath);
    }

    [Fact]
    public async Task ExistingLogIsReadFromByteZeroInOrder()
    {
        using var fixture = new LogFixture();
        fixture.Write("first\r\nsecond\n");
        var events = await ObserveAsync(fixture.Source(), 2, () => Task.CompletedTask);
        Assert.Equal(["first", "second"], Lines(events));
    }

    [Fact]
    public async Task AppendedLinesAreEmittedOnce()
    {
        using var fixture = new LogFixture();
        fixture.Write("existing\n");
        var events = await ObserveAsync(fixture.Source(), 2, async () =>
        {
            await Task.Delay(60);
            fixture.Append("appended\n");
        });
        Assert.Equal(["existing", "appended"], Lines(events));
    }

    [Fact]
    public async Task SeveralLinesInOneAppendRemainOrdered()
    {
        using var fixture = new LogFixture();
        fixture.Write("");
        var events = await ObserveAsync(fixture.Source(), 3, () =>
        {
            fixture.Append("one\ntwo\nthree\n");
            return Task.CompletedTask;
        });
        Assert.Equal(["one", "two", "three"], Lines(events));
    }

    [Fact]
    public async Task PartialLineWaitsForTerminator()
    {
        using var fixture = new LogFixture();
        fixture.Write("partial");
        var events = await ObserveAsync(fixture.Source(), 1, async () =>
        {
            await Task.Delay(80);
            fixture.Append(" line\n");
        });
        Assert.Equal(["partial line"], Lines(events));
    }

    [Fact]
    public async Task Utf8ScalarSplitAcrossWritesIsPreserved()
    {
        using var fixture = new LogFixture();
        var bytes = Encoding.UTF8.GetBytes("€");
        fixture.Write(bytes[..2]);
        var events = await ObserveAsync(fixture.Source(), 1, async () =>
        {
            await Task.Delay(80);
            fixture.Append([bytes[2], (byte)'\n']);
        });
        Assert.Equal(["€"], Lines(events));
    }

    [Fact]
    public async Task MalformedUtf8UsesReplacementCharacter()
    {
        using var fixture = new LogFixture();
        fixture.Write([0xff, (byte)'\n']);
        var events = await ObserveAsync(fixture.Source(), 1, () => Task.CompletedTask);
        Assert.Equal(["�"], Lines(events));
    }

    [Fact]
    public async Task MonitoringCanBeginBeforeFileExists()
    {
        using var fixture = new LogFixture(createFile: false);
        var events = await ObserveAsync(fixture.Source(), 1, () =>
        {
            fixture.Write("created\n");
            return Task.CompletedTask;
        });
        Assert.Equal(["created"], Lines(events));
        Assert.DoesNotContain(events, item => item is ArenaLogSourceEvent.SourceReset);
    }

    [Fact]
    public async Task TruncationEmitsResetAndReadsNewGeneration()
    {
        using var fixture = new LogFixture();
        fixture.Write("old-generation-line\n");
        var events = await ObserveMutationAfterFirstAsync(fixture, () => fixture.Write("new\n"));
        Assert.Collection(
            events,
            item => Assert.Equal("old-generation-line", Assert.IsType<ArenaLogSourceEvent.Line>(item).Text),
            item => Assert.IsType<ArenaLogSourceEvent.SourceReset>(item),
            item => Assert.Equal("new", Assert.IsType<ArenaLogSourceEvent.Line>(item).Text));
    }

    [Fact]
    public async Task PhysicalReplacementIsDetectedEvenWhenLarger()
    {
        using var fixture = new LogFixture();
        fixture.Write("old\n");
        var events = await ObserveMutationAfterFirstAsync(
            fixture,
            () => fixture.Replace("replacement-is-larger\n"));
        Assert.IsType<ArenaLogSourceEvent.SourceReset>(events[1]);
        Assert.Equal("replacement-is-larger", Assert.IsType<ArenaLogSourceEvent.Line>(events[2]).Text);
    }

    [Fact]
    public async Task DeleteAndRecreateEmitsReset()
    {
        using var fixture = new LogFixture();
        fixture.Write("old\n");
        var events = await ObserveMutationAfterFirstAsync(fixture, () =>
        {
            File.Delete(fixture.FilePath);
            Thread.Sleep(50);
            fixture.Write("recreated\n");
        });
        Assert.IsType<ArenaLogSourceEvent.SourceReset>(events[1]);
        Assert.Equal("recreated", Assert.IsType<ArenaLogSourceEvent.Line>(events[2]).Text);
    }

    [Fact]
    public async Task IdlePollingDoesNotDuplicateLines()
    {
        using var fixture = new LogFixture();
        fixture.Write("once\n");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var enumerator = fixture.Source().ReadEventsAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("once", Assert.IsType<ArenaLogSourceEvent.Line>(enumerator.Current).Text);

        cancellation.CancelAfter(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await enumerator.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task CancellationTerminatesConsumptionCleanly()
    {
        using var fixture = new LogFixture();
        fixture.Write("");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in fixture.Source().ReadEventsAsync(cancellation.Token)) { }
        });
    }

    private static async Task<List<ArenaLogSourceEvent>> ObserveAsync(
        FileArenaLogSource source,
        int expectedCount,
        Func<Task> mutation)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var results = new List<ArenaLogSourceEvent>();
        var collector = Task.Run(async () =>
        {
            await foreach (var item in source.ReadEventsAsync(cancellation.Token))
            {
                results.Add(item);
                if (results.Count == expectedCount) break;
            }
        }, CancellationToken.None);

        await Task.Delay(50, cancellation.Token);
        await mutation();
        await collector.WaitAsync(cancellation.Token);
        return results;
    }

    private static async Task<List<ArenaLogSourceEvent>> ObserveMutationAfterFirstAsync(
        LogFixture fixture,
        Action mutation)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await using var enumerator = fixture.Source().ReadEventsAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        var results = new List<ArenaLogSourceEvent>();

        Assert.True(await enumerator.MoveNextAsync());
        results.Add(enumerator.Current);
        mutation();

        while (results.Count < 3 && await enumerator.MoveNextAsync())
        {
            results.Add(enumerator.Current);
        }
        return results;
    }

    private static string[] Lines(IEnumerable<ArenaLogSourceEvent> events) =>
        events.OfType<ArenaLogSourceEvent.Line>().Select(line => line.Text).ToArray();

    private sealed class LogFixture : IDisposable
    {
        private readonly string _directory;

        public LogFixture(bool createFile = true)
        {
            _directory = Path.Combine(Path.GetTempPath(), $"DraftTG-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            FilePath = Path.Combine(_directory, "Player.log");
            if (createFile) File.WriteAllBytes(FilePath, []);
        }

        public string FilePath { get; }
        public FileArenaLogSource Source() => new(FilePath, PollingInterval);
        public void Write(string value) => Write(Encoding.UTF8.GetBytes(value));
        public void Write(byte[] value) => File.WriteAllBytes(FilePath, value);
        public void Append(string value) => Append(Encoding.UTF8.GetBytes(value));
        public void Append(byte[] value)
        {
            using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(value);
            stream.Flush(flushToDisk: true);
        }
        public void Replace(string value)
        {
            var replacement = Path.Combine(_directory, "replacement.log");
            File.WriteAllText(replacement, value, new UTF8Encoding(false));
            File.Move(replacement, FilePath, overwrite: true);
        }
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
