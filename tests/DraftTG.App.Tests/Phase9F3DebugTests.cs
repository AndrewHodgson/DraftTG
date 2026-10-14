using System.Text.Json;
using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.ArenaIntegration.Memory;
namespace DraftTG.App.Tests;

public sealed class Phase9F3DebugTests
{
    [Fact]
    public void WindowMovementTranslatesWithoutNewGenerationButDpiAndResizeInvalidate()
    {
        var read=new ArenaDraftMemorySnapshot(ArenaMemoryStatus.Success,"fixture",1,null,null,DateTimeOffset.UnixEpoch,true,"list",
            [1,2],"fixture",TimeSpan.Zero,0) { ProcessInstance="fixture" };
        var window=new ArenaWindowGeometry((nint)1,10,20,1600,900,1,true) { ProcessId=1,IsWindowed=true };
        var key=ArenaUiLocalizationDebugCommand.Key(read,window);
        Assert.Equal(key,ArenaUiLocalizationDebugCommand.Key(read,window with { X=100,Y=200 }));
        Assert.NotEqual(key,ArenaUiLocalizationDebugCommand.Key(read,window with { Scaling=1.25 }));
        Assert.NotEqual(key,ArenaUiLocalizationDebugCommand.Key(read,window with { Width=1280,Height=720 }));
        Assert.NotEqual(key,ArenaUiLocalizationDebugCommand.Key(read,window with { IsMinimized=true }));
        var stable=new CaptureFreeUiStability();stable.Observe(key,DateTimeOffset.UnixEpoch);
        stable.Observe(key,DateTimeOffset.UnixEpoch.AddMilliseconds(200));
        var json=JsonSerializer.Serialize(ArenaUiLocalizationDebugCommand.Snapshot(read,window,stable,2));
        Assert.Contains("\"Handle\":1",json); // IntPtr must never reach the JSON serializer.
    }
    [Fact]
    public void DeckOccurrencesAndQuantitiesAreDiagnosticAndNeverInventVisibleAnchors()
    {
        var read=new ArenaDraftMemorySnapshot(ArenaMemoryStatus.TransientReadFailure,"declined pack",1,null,null,DateTimeOffset.UnixEpoch,true,"list",
            [],"fixture",TimeSpan.Zero,0) { ProcessInstance="fixture",Ui=new(null,new([
                new(0,101,2,"Main",null,"main.tiles[0]"),new(1,101,1,"Sideboard",null,"side.tiles[1]")],.5f,1,true,"Unproven transform"),null,1) };
        var data=ArenaUiLocalizationDebugCommand.Snapshot(read,null,new(),1);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(data));
        var deck=json.RootElement.GetProperty("DeckView");
        Assert.Equal(JsonValueKind.Null,deck.GetProperty("Rows").ValueKind);
        var rows=deck.GetProperty("Memory").GetProperty("Entries").EnumerateArray().ToArray();
        Assert.Equal(new[]{2,1},rows.Select(x=>x.GetProperty("Quantity").GetInt32()));
        Assert.Equal(new[]{"Main","Sideboard"},rows.Select(x=>x.GetProperty("Area").GetString()));
        Assert.All(rows,x=>{Assert.Equal(JsonValueKind.Null,x.GetProperty("Geometry").ValueKind);
            Assert.Equal(JsonValueKind.Null,x.GetProperty("Visible").ValueKind);});
        Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("Geometry").GetProperty("Rectangles").ValueKind);
    }
}
