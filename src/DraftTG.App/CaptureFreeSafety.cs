namespace DraftTG.App;

internal static class CaptureFreeSafety
{
    private sealed class State { internal int Attempts; }
    private static State? processState;
    private static readonly AsyncLocal<State?> testState = new();
    internal static bool Enabled => processState is not null || testState.Value is not null;
    internal static int RejectedCaptureAttempts => (processState ?? testState.Value)?.Attempts ?? 0;
    internal static void EnableForProcess() => processState ??= new();
    internal static void AssertCaptureAllowed(string capability)
    {
        if ((processState ?? testState.Value) is not { } state) return;
        Interlocked.Increment(ref state.Attempts);
        throw new InvalidOperationException("Capture-free mode forbids initializing " + capability);
    }
    internal static IDisposable TestScope()
    {
        var previous = testState.Value; testState.Value = new();
        return new Scope(() => testState.Value = previous);
    }
    private sealed class Scope(Action restore) : IDisposable { public void Dispose() => restore(); }
}
