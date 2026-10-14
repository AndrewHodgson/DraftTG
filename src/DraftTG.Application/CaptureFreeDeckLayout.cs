using DraftTG.ArenaIntegration.Memory;
namespace DraftTG.Application;

public sealed record CaptureFreeDeckRow(int CollectionIndex,int VisualIndex,uint GrpId,int Quantity,string Holder,
    ArenaClientRectangle Rectangle,ArenaClientRectangle VisibleRectangle,bool Visible,bool Culled,bool Clipped,string Source);
public sealed record CaptureFreeDeckLayoutResult(ArenaClientRectangle? Viewport,ArenaClientRectangle? Content,
    double? ScrollPixels,IReadOnlyList<CaptureFreeDeckRow> Rows,string? Decline)
{ public bool IsAvailable=>Decline is null&&Viewport is not null; public double? NormalizedScrollPixels { get; init; } }

public static class CaptureFreeDeckLayout
{
    public static CaptureFreeDeckLayoutResult Map(ArenaDeckViewMemory? deck,int width,int height,string? mode,bool windowed,string area="Main")
    {
        CaptureFreeDeckLayoutResult Fail(string message)=>new(null,null,null,[],message);
        if(area!="Main")return Fail("Sideboard display geometry is not physically qualified");
        if(!CaptureFreeDraftLayout.Map(width,height,14,mode,windowed).IsAvailable)return Fail("Unsupported deck client/layout envelope");
        if(deck?.MainGeometry is not {Active:true,Scrolling:false,CanvasMode:1} geometry)return Fail("Main deck geometry inactive, moving or unavailable");
        var canvas=geometry.CanvasWorld;
        bool Valid(ArenaUiRect r)=>double.IsFinite(r.X)&&double.IsFinite(r.Y)&&double.IsFinite(r.Width)&&double.IsFinite(r.Height)&&r.Width>0&&r.Height>0;
        if(!Valid(canvas)||!Valid(geometry.ViewportWorld)||!Valid(geometry.ContentWorld)
            ||Math.Abs(canvas.Width/canvas.Height-width/(double)height)>0.0001)return Fail("Canvas/client geometry does not agree");
        ArenaClientRectangle Project(ArenaUiRect r)=>new((r.X-canvas.X)/canvas.Width*width,
            (canvas.Y+canvas.Height-r.Y-r.Height)/canvas.Height*height,r.Width/canvas.Width*width,r.Height/canvas.Height*height);
        var viewport=Project(geometry.ViewportWorld);var content=Project(geometry.ContentWorld);
        var scrollPixels=Math.Max(0,viewport.Y-content.Y);var range=Math.Max(0,content.Height-viewport.Height);
        var normalized=deck.MainScroll is { } value&&float.IsFinite(value)&&value is >=0 and <=1?(1-value)*range:(double?)null;
        if(normalized is null||Math.Abs(normalized.Value-scrollPixels)>1)return Fail("Normalized scroll and native content displacement disagree");
        if(viewport.X<0||viewport.Y<0||viewport.X+viewport.Width>width+1||viewport.Y+viewport.Height>height+1)
            return Fail("Main viewport outside Arena client");
        var entries=deck.Entries.Where(e=>e.Area==area).ToArray();
        if(entries.Any(e=>e.Quantity<=0||e.GrpId==0||e.Geometry is null||!Valid(e.Geometry.WorldRect)))return Fail("Complete current deck row geometry required");
        var ordered=entries.OrderByDescending(e=>e.Geometry!.WorldRect.Y+e.Geometry.WorldRect.Height).ThenBy(e=>e.Geometry!.WorldRect.X).ToArray();
        if(ordered.Zip(ordered.Skip(1),(a,b)=>Math.Abs(a.Geometry!.WorldRect.Y-b.Geometry!.WorldRect.Y)<.0001).Any(x=>x))
            return Fail("Ambiguous equal-height main rows");
        var rows=ordered.Select((e,index)=>
        {
            var rectangle=Project(e.Geometry!.WorldRect);var intersection=Intersect(rectangle,viewport);
            var visible=e.Geometry.Active&&intersection.Width>0&&intersection.Height>0;
            return new CaptureFreeDeckRow(e.Index,index,e.GrpId,e.Quantity,e.Area,rectangle,intersection,visible,!visible,
                visible&&(intersection.Width<rectangle.Width-.1||intersection.Height<rectangle.Height-.1),e.SourcePath);
        }).ToArray();
        return new(viewport,content,scrollPixels,rows,null) {NormalizedScrollPixels=normalized};
    }
    public static ArenaClientRectangle Intersect(ArenaClientRectangle a,ArenaClientRectangle b)
    {
        var x=Math.Max(a.X,b.X);var y=Math.Max(a.Y,b.Y);
        return new(x,y,Math.Max(0,Math.Min(a.X+a.Width,b.X+b.Width)-x),Math.Max(0,Math.Min(a.Y+a.Height,b.Y+b.Height)-y));
    }
}
