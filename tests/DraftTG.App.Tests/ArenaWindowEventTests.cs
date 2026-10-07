using DraftTG.App.Platform;

namespace DraftTG.App.Tests;

public sealed class ArenaWindowEventTests
{
    private static readonly ArenaWindowGeometry Window = new(42,10,20,1000,600,1,false) { ProcessId=7 };
    private sealed class NativeApi : IArenaWinEventApi
    {
        public readonly List<(uint First,uint Last,uint Pid,nint Handle,ArenaWinEventCallback Callback)> Registered=[];
        public readonly HashSet<nint> Outstanding=[];
        public int FailCall {get;set;}
        public bool FailRemoval {get;set;}
        private int _calls;
        public nint Register(uint first,uint last,ArenaWinEventCallback callback,uint processId)
        {
            var handle=(nint)(++_calls);
            if (_calls==FailCall) return 0;
            Registered.Add((first,last,processId,handle,callback)); Outstanding.Add(handle); return handle;
        }
        public bool Unregister(nint hook)=>!FailRemoval && Outstanding.Remove(hook);
        public void Emit(int registration,uint kind,nint window=42,int objectId=0,int childId=0)
        { var hook=Registered[registration]; hook.Callback(hook.Handle,kind,window,objectId,childId,1,0); }
    }
    [Fact]
    public void NativeEventsAreProcessScopedAndFilterToKnownWindowObjects()
    {
        var api=new NativeApi(); using var events=new WindowsArenaWindowEvents(api); var updates=0;
        events.Changed+=()=>updates++; events.Track(Window);
        Assert.True(events.IsRegistered); Assert.Equal(4,api.Registered.Count);
        Assert.All(api.Registered,h=>Assert.Equal(7u,h.Pid));
        Assert.Equal(WindowsArenaWindowEvents.LocationChange,api.Registered[0].First);
        api.Emit(0,WindowsArenaWindowEvents.LocationChange,window:77);
        api.Emit(0,WindowsArenaWindowEvents.LocationChange,objectId:-4);
        api.Emit(0,WindowsArenaWindowEvents.LocationChange,childId:1);
        Assert.Equal(0,updates);
        api.Emit(0,WindowsArenaWindowEvents.LocationChange); api.Emit(1,WindowsArenaWindowEvents.Foreground);
        api.Emit(2,WindowsArenaWindowEvents.MinimizeStart); api.Emit(2,WindowsArenaWindowEvents.MinimizeEnd);
        api.Emit(3,WindowsArenaWindowEvents.ObjectHide); api.Emit(3,WindowsArenaWindowEvents.ObjectShow);
        Assert.Equal(6,updates);
    }
    [Fact]
    public void ReplacementAndLostWindowUnregisterOldHooksAndIgnoreLateCallbacks()
    {
        var api=new NativeApi(); var events=new WindowsArenaWindowEvents(api); var updates=0;
        events.Changed+=()=>updates++; events.Track(Window); events.Track(Window with {X=500});
        Assert.Equal(4,api.Registered.Count); // Translation does not register another native observer.
        events.Track(null); Assert.Empty(api.Outstanding);
        events.Track(Window with {Handle=77,ProcessId=8}); Assert.Equal(4,api.Outstanding.Count);
        Assert.All(api.Registered.Skip(4),h=>Assert.Equal(8u,h.Pid));
        api.Emit(0,WindowsArenaWindowEvents.LocationChange); Assert.Equal(0,updates);
        api.Emit(4,WindowsArenaWindowEvents.LocationChange,window:77); Assert.Equal(1,updates);
        events.Dispose(); events.Dispose(); Assert.Empty(api.Outstanding);
        api.Emit(4,WindowsArenaWindowEvents.LocationChange,window:77); Assert.Equal(1,updates);
    }
    [Fact]
    public void PartialRegistrationFailureCleansUpAndCanRetryOnHealthCheck()
    {
        var api=new NativeApi {FailCall=2}; using var events=new WindowsArenaWindowEvents(api);
        events.Track(Window); Assert.False(events.IsRegistered); Assert.Empty(api.Outstanding);
        events.Track(Window); Assert.True(events.IsRegistered); Assert.Equal(4,api.Outstanding.Count);
    }
    [Fact]
    public void FailedNativeRemovalDisablesCallbacksAndRetainsOwnershipUntilRetry()
    {
        var api=new NativeApi(); var events=new WindowsArenaWindowEvents(api); var updates=0;
        events.Changed+=()=>updates++; events.Track(Window);
        api.FailRemoval=true; events.Dispose(); Assert.Equal(4,api.Outstanding.Count);
        api.Emit(0,WindowsArenaWindowEvents.LocationChange); Assert.Equal(0,updates);
        api.FailRemoval=false; events.Dispose(); Assert.Empty(api.Outstanding);
    }
    private sealed class Source : IArenaWindowEvents
    {
        public event Action? Changed;
        public bool IsRegistered=>true;
        public bool Disposed {get;private set;}
        public void Track(ArenaWindowGeometry? window) { }
        public void Emit()=>Changed?.Invoke();
        public void Dispose()=>Disposed=true;
    }
    [Fact]
    public void DispatcherPumpCoalescesDuplicatesAndSuppressesQueuedUpdatesAfterDisposal()
    {
        var source=new Source(); var posted=new Queue<Action>(); var updates=0;
        var pump=new ArenaWindowEventPump(source,posted.Enqueue,()=>updates++);
        source.Emit(); source.Emit(); Assert.Single(posted); Assert.Equal(0,updates);
        posted.Dequeue()(); Assert.Equal(1,updates);
        source.Emit(); pump.Dispose(); posted.Dequeue()(); source.Emit();
        Assert.True(source.Disposed); Assert.Equal(1,updates); Assert.Empty(posted);
    }
}
