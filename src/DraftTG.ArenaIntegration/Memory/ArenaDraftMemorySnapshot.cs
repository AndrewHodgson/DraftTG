namespace DraftTG.ArenaIntegration.Memory;

public enum ArenaMemoryStatus
{
    Success, NotRunning, UnsupportedPlatform, UnsupportedBuild, DraftSceneNotActive,
    AccessDenied, MetadataUnavailable, TransientReadFailure, ObjectGraphIncomplete, InvalidPackState
}

public sealed record ArenaManagedAssemblyIdentity(string Name, string Sha256, Guid Mvid);
public sealed record ArenaBuildFingerprint(string Architecture, string UnityVersion, string ExecutableSha256,
    string UnityPlayerSha256, string MonoSha256, IReadOnlyList<ArenaManagedAssemblyIdentity> Assemblies);

/// <summary>A passive observation, not an atomic process snapshot or a production draft provider.</summary>
public sealed record ArenaDraftMemorySnapshot(ArenaMemoryStatus Status, string Detail, int? ProcessId,
    ArenaBuildFingerprint? Build, string? RuntimeProfile, DateTimeOffset ReadTimestamp,
    bool IsDraftSceneActive, string? ViewMode, IReadOnlyList<uint> OrderedOfferedGrpIds,
    string? Identity, TimeSpan Duration, long BytesRead)
{
    public string? ProcessInstance { get; init; }
    public string? DraftSessionId { get; init; }
    public string? CourseId { get; init; }
    public string? EventName { get; init; }
    public string? DraftKind { get; init; }
    public int? PackNumber { get; init; }
    public int? PickNumber { get; init; }
    public bool IsFirstAttach { get; init; }
    public TimeSpan TraversalDuration { get; init; }
}
