namespace DraftTG.Data.Tests;

public sealed class OverlaySettingsFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DraftTG-overlay-" + Guid.NewGuid());
    [Fact]
    public async Task SmallDocumentPersistsAcrossInstancesAndReplacesAtomically()
    {
        var file = new OverlaySettingsFile(new Paths(_directory));
        Assert.Null(await file.ReadAsync());
        await file.WriteAsync("{\"Version\":1}");
        Assert.Equal("{\"Version\":1}", await new OverlaySettingsFile(new Paths(_directory)).ReadAsync());
        await file.WriteAsync("{\"Version\":2}");
        Assert.Equal("{\"Version\":2}", await file.ReadAsync());
        Assert.Single(Directory.GetFiles(_directory));
    }
    [Fact]
    public async Task CancelledSavePreservesPreviousDocument()
    {
        var file = new OverlaySettingsFile(new Paths(_directory));
        await file.WriteAsync("original");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.WriteAsync("replacement", cancellation.Token));
        Assert.Equal("original", await file.ReadAsync());
        Assert.Single(Directory.GetFiles(_directory));
    }
    private sealed class Paths(string directory) : IApplicationDataPathProvider
    { public string GetApplicationDataDirectory() => directory; }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
