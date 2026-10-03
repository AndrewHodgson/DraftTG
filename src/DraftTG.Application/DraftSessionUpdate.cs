using DraftTG.ArenaIntegration;

namespace DraftTG.Application;

public enum DraftSessionDiagnosticKind
{
    ParseError
}

public sealed record DraftSessionDiagnostic(
    DraftSessionDiagnosticKind Kind,
    string Message,
    ArenaDraftLogParseErrorKind? ParseErrorKind = null);

/// <summary>An immutable state or diagnostic update for application consumers.</summary>
public sealed record DraftSessionUpdate(
    ArenaDraftStateSnapshot ArenaState,
    ArenaDraftSnapshotResult SnapshotResult,
    DraftSessionDiagnostic? Diagnostic = null);
