using System.Diagnostics;

namespace DraftTG.ArenaIntegration;

/// <summary>Shared MTGA discovery convention. The caller owns and disposes every result.</summary>
public static class ArenaProcessDiscovery
{
    public const string ProcessName = "MTGA";
    public static Process[] GetCandidates() => Process.GetProcessesByName(ProcessName);
}
