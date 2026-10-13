using System.Text;
using System.Text.Json;

namespace DraftTG.ArenaIntegration.Memory;

public static class ArenaDraftMemoryFormatter
{
    public static string Format(ArenaDraftMemorySnapshot s)
    {
        var output = new StringBuilder();
        output.AppendLine(s.Status switch
        {
            ArenaMemoryStatus.NotRunning => "Arena memory: MTGA not running",
            ArenaMemoryStatus.UnsupportedBuild => "Arena memory: unsupported build",
            ArenaMemoryStatus.Success or ArenaMemoryStatus.DraftSceneNotActive => "Arena memory: connected",
            _ => $"Arena memory: {s.Status}"
        });
        if (s.Build is not null) output.AppendLine($"Build: {JsonSerializer.Serialize(s.Build)}");
        if (s.RuntimeProfile is not null) output.AppendLine($"Runtime profile: {s.RuntimeProfile}");
        if (s.ProcessId is not null) output.AppendLine($"PID: {s.ProcessId}");
        if (s.IsDraftSceneActive || s.Status is ArenaMemoryStatus.Success or ArenaMemoryStatus.DraftSceneNotActive)
            output.AppendLine($"Draft scene: {(s.IsDraftSceneActive ? "active" : "inactive")}");
        if (s.Status == ArenaMemoryStatus.Success)
        {
            output.AppendLine($"View: {s.ViewMode}");
            output.AppendLine($"Pack count: {s.OrderedOfferedGrpIds.Count}");
            output.AppendLine("Ordered offered cards:");
            for (var i = 0; i < s.OrderedOfferedGrpIds.Count; i++) output.AppendLine($"  {i}  GrpId={s.OrderedOfferedGrpIds[i]}");
            output.AppendLine($"Identity: {s.Identity}");
        }
        output.AppendLine($"Status: {s.Status} — {s.Detail}");
        output.AppendLine($"Read: {s.ReadTimestamp:O}; {s.Duration.TotalMilliseconds:F1} ms; {s.BytesRead} bytes");
        return output.ToString();
    }
}
