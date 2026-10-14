using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DraftTG.ArenaIntegration.Memory.Windows;

namespace DraftTG.ArenaIntegration.Memory;

public enum ArenaMemoryTransition
{
    NotRunning, ProcessAppeared, ProcessReplaced, UnsupportedBuild, DraftInactive, InTransition,
    PackAppeared, PackChanged, ViewModeChanged, Recovered
}
public sealed record ArenaDraftMemoryUpdate(long Generation, ArenaMemoryTransition Transition,
    ArenaDraftMemorySnapshot CurrentRead, ArenaDraftMemorySnapshot? CurrentPack, ArenaDraftMemorySnapshot? LastStablePack,
    string? SemanticPackKey);

/// <summary>Serial reads, two identical coherent reads to publish, immediate retirement on changed/failed reads.</summary>
public sealed class ArenaDraftMemoryWatcher : IDisposable
{
    private readonly Func<ArenaDraftMemorySnapshot> read;
    private readonly IDisposable? owned;
    private string? candidateKey, stableKey, processInstance, lastEmission;
    private bool current;
    private long generation;
    public ArenaDraftMemorySnapshot? LastStablePack { get; private set; }
    public long Generation => Interlocked.Read(ref generation);
    public TimeSpan PollInterval { get; }
    public event Action<ArenaDraftMemorySnapshot>? PollObserved;
    public ArenaDraftMemoryWatcher(TimeSpan? interval = null, Func<ArenaDraftMemorySnapshot>? reader = null,
        Func<uint,uint,ArenaOfferedCardIdentity>? printingReconciler = null)
    {
        PollInterval = interval ?? TimeSpan.FromMilliseconds(100);
        if (PollInterval < TimeSpan.FromMilliseconds(50)) throw new ArgumentOutOfRangeException(nameof(interval));
        if (reader is null) { var r = new ArenaDraftMemoryReader(printingReconciler); read = () => r.ReadCurrent(); owned = r; }
        else read = reader;
    }
    public ArenaDraftMemoryUpdate? Observe(ArenaDraftMemorySnapshot snapshot)
    {
        if (snapshot.Status == ArenaMemoryStatus.Success && (!snapshot.IsDraftSceneActive || snapshot.ProcessId is null
            || snapshot.ProcessInstance is null || snapshot.Identity is null || snapshot.ViewMode is not ("list" or "column")
            || snapshot.OrderedOfferedGrpIds.Count is < 1 or > 32 || snapshot.OrderedOfferedGrpIds.Any(id => id == 0 || id > int.MaxValue)))
            snapshot = snapshot with { Status = ArenaMemoryStatus.InvalidPackState, Detail = "Incomplete success snapshot rejected by watcher.", OrderedOfferedGrpIds = [] };
        PollObserved?.Invoke(snapshot);
        var changedProcess = processInstance != snapshot.ProcessInstance;
        var previousProcess = processInstance;
        processInstance = snapshot.ProcessInstance;
        if (changedProcess) { candidateKey = stableKey = null; current = false; LastStablePack = null; }
        if (snapshot.Status != ArenaMemoryStatus.Success)
        {
            candidateKey = null; current = false;
            var transition = snapshot.Status switch
            {
                ArenaMemoryStatus.NotRunning => ArenaMemoryTransition.NotRunning,
                ArenaMemoryStatus.UnsupportedBuild or ArenaMemoryStatus.UnsupportedPlatform => ArenaMemoryTransition.UnsupportedBuild,
                ArenaMemoryStatus.DraftSceneNotActive => ArenaMemoryTransition.DraftInactive,
                _ => ArenaMemoryTransition.InTransition
            };
            return Emit(snapshot, transition, null, $"{snapshot.Status}:{snapshot.Detail}:{snapshot.ProcessInstance}");
        }
        var key = StateKey(snapshot);
        if (current && key == stableKey) return null;
        if (candidateKey != key)
        {
            candidateKey = key; current = false;
            return Emit(snapshot, changedProcess ? previousProcess is null ? ArenaMemoryTransition.ProcessAppeared : ArenaMemoryTransition.ProcessReplaced
                : ArenaMemoryTransition.InTransition, null, "candidate:" + key);
        }
        var transitionStable = stableKey == key ? ArenaMemoryTransition.Recovered
            : LastStablePack is null ? ArenaMemoryTransition.PackAppeared
            : LastStablePack.ViewMode != snapshot.ViewMode && SemanticKey(LastStablePack) == SemanticKey(snapshot)
                ? ArenaMemoryTransition.ViewModeChanged : ArenaMemoryTransition.PackChanged;
        stableKey = key; current = true; LastStablePack = snapshot;
        return Emit(snapshot, transitionStable, snapshot, "stable:" + key);
    }
    private ArenaDraftMemoryUpdate? Emit(ArenaDraftMemorySnapshot snapshot, ArenaMemoryTransition transition, ArenaDraftMemorySnapshot? pack, string emission)
    {
        if (lastEmission == emission) return null;
        lastEmission = emission;
        return new(Interlocked.Increment(ref generation), transition, snapshot, pack, LastStablePack, pack is null ? null : SemanticKey(pack));
    }
    public static string StateKey(ArenaDraftMemorySnapshot s) => Hash($"{s.ProcessInstance}|{s.DraftSessionId}|{s.PackNumber}|{s.PickNumber}|{s.Identity}|{s.ViewMode}|{string.Join(',', s.OrderedOfferedGrpIds)}|{string.Join(',',s.PrintingIdentities.Select(i=>i.VisibleGrpId))}");
    public static string SemanticKey(ArenaDraftMemorySnapshot s) => Hash(s.PackNumber is not null && s.PickNumber is not null
        ? $"{s.DraftSessionId ?? s.CourseId ?? s.ProcessInstance + s.Identity?.Split("/list:")[0]}|{s.EventName}|{s.PackNumber}|{s.PickNumber}"
        : $"{s.ProcessInstance}|{s.DraftSessionId}|{s.Identity}|{string.Join(',', s.OrderedOfferedGrpIds)}");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public async IAsyncEnumerable<ArenaDraftMemoryUpdate> WatchAsync(TimeSpan? duration = null, [EnumeratorCancellation] CancellationToken token = default)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (!token.IsCancellationRequested && (duration is null || started.Elapsed < duration))
        {
            var update = Observe(read());
            if (update is not null) yield return update;
            await Task.Delay(PollInterval, token).ConfigureAwait(false);
        }
    }
    public void Dispose() => owned?.Dispose();
}
