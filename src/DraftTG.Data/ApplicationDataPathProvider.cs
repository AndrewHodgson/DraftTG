namespace DraftTG.Data;

public interface IApplicationDataPathProvider
{
    string GetApplicationDataDirectory();
}

public sealed class WindowsApplicationDataPathProvider : IApplicationDataPathProvider
{
    private readonly string _localApplicationDataPath;

    public WindowsApplicationDataPathProvider(string? localApplicationDataPath = null)
    {
        _localApplicationDataPath = localApplicationDataPath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        ArgumentException.ThrowIfNullOrWhiteSpace(_localApplicationDataPath);
    }

    public string GetApplicationDataDirectory() =>
        Path.Combine(_localApplicationDataPath, "DraftTG");
}

public sealed class MacOSApplicationDataPathProvider : IApplicationDataPathProvider
{
    private readonly string _homeDirectory;

    public MacOSApplicationDataPathProvider(string? homeDirectory = null)
    {
        _homeDirectory = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(_homeDirectory);
    }

    public string GetApplicationDataDirectory() =>
        Path.Combine(_homeDirectory, "Library", "Application Support", "DraftTG");
}

public static class ApplicationDataPathProviderFactory
{
    public static IApplicationDataPathProvider CreateDefault()
    {
        if (OperatingSystem.IsWindows()) return new WindowsApplicationDataPathProvider();
        if (OperatingSystem.IsMacOS()) return new MacOSApplicationDataPathProvider();
        throw new PlatformNotSupportedException(
            "DraftTG application-data paths are currently supported on Windows and macOS.");
    }
}
