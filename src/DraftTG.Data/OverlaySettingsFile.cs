namespace DraftTG.Data;

/// <summary>Only the small overlay calibration document; no general settings framework.</summary>
public interface IOverlaySettingsFile
{
    Task<string?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(string json, CancellationToken cancellationToken = default);
}

public sealed class OverlaySettingsFile(IApplicationDataPathProvider paths) : IOverlaySettingsFile
{
    private readonly string _path = Path.Combine(paths.GetApplicationDataDirectory(), "overlay-calibration.json");
    public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        if (new FileInfo(_path).Length > 64 * 1024) throw new IOException("Overlay calibration file is too large.");
        return await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
    }
    public async Task WriteAsync(string json, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
