namespace DraftTG.ArenaIntegration;

public sealed record ArenaLogLocation
{
    public ArenaLogLocation(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("An Arena log path is required.", nameof(filePath));
        }
        FilePath = filePath;
    }

    public string FilePath { get; }
}

public interface IArenaLogLocationProvider
{
    ArenaLogLocation GetLocation();
}

public sealed class MacArenaLogLocationProvider(string? homeDirectory = null)
    : IArenaLogLocationProvider
{
    public ArenaLogLocation GetLocation()
    {
        var home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new ArenaLogLocation(System.IO.Path.Combine(
            home,
            "Library",
            "Logs",
            "Wizards Of The Coast",
            "MTGA",
            "Player.log"));
    }
}

public sealed class WindowsArenaLogLocationProvider(string? userProfile = null)
    : IArenaLogLocationProvider
{
    public ArenaLogLocation GetLocation()
    {
        var profile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new ArenaLogLocation(System.IO.Path.Combine(
            profile,
            "AppData",
            "LocalLow",
            "Wizards Of The Coast",
            "MTGA",
            "Player.log"));
    }
}

public static class ArenaLogLocationProviderFactory
{
    public static IArenaLogLocationProvider CreateDefault()
    {
        if (OperatingSystem.IsWindows()) return new WindowsArenaLogLocationProvider();
        if (OperatingSystem.IsMacOS()) return new MacArenaLogLocationProvider();
        throw new PlatformNotSupportedException("DraftTG supports Arena log discovery on Windows and macOS.");
    }
}
