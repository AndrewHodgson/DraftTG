using System.Reflection;
using System.Text.Json;
using DraftTG.Data;

namespace DraftTG.App;

/// <summary>Opt-in diagnostics on the existing shadow ledger. Never supplies placement.</summary>
internal static class LiveLocalizationValidation
{
    internal static bool Enabled { get; set; }
    internal static string SessionId { get; } = Guid.NewGuid().ToString("N");
    internal static string Build { get; } = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    internal static string BuildId { get; } = typeof(Program).Assembly.ManifestModule.ModuleVersionId.ToString();
    internal static string LedgerPath => Path.Combine(ApplicationDataPathProviderFactory.CreateDefault().GetApplicationDataDirectory(),
        "localization", "deterministic-shadow.jsonl");

    // Caller passes immutable snapshots. The existing observer queue owns append ordering/I/O.
    internal static string Event(string kind, object details) => JsonSerializer.Serialize(new
    {
        Schema = 3, Source = "DraftTG/9E.2B live deterministic validation", Kind = kind,
        LiveValidationMode = Enabled, SessionId, Build, BuildId, RecordedAt = DateTimeOffset.UtcNow, Details = details
    });
}
