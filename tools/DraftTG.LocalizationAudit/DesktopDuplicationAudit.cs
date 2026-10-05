using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DraftTG.LocalizationAudit;

/// <summary>Isolated DXGI probe. Only an Arena-center crop crosses GPU-to-CPU; never saves the desktop.</summary>
internal static class DesktopDuplicationAudit
{
    public static object Read(int pid,string outputDirectory)
    {
        var window=WindowAudit.NativeWindows(pid).Single(w=>w.Class=="UnityWndClass" && w.Visible && !w.Minimized);
        var hwnd=(nint)Convert.ToInt64(window.Handle[2..],16); var client=window.ClientScreen;
        // Explicit audit crop, not production geometry: exclude chrome/account strip and bottom tray.
        var crop=new PixelRect(client.X+client.Width/12,client.Y+client.Height/6,client.Width*5/6,client.Height*2/3);
        if((long)crop.Width*crop.Height>8_000_000) throw new InvalidOperationException("Audit crop exceeds pixel budget.");
        var initiallyOccluded=Occluded(crop,pid);
        nint factory=0,adapter=0,output=0,output1=0,device=0,context=0,duplication=0,resource=0,texture=0,staging=0;
        var frameOwned=false; var operation="DXGI factory"; var watch=Stopwatch.StartNew();
        try
        {
            var factoryId=new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); Check(CreateDXGIFactory1(ref factoryId,out factory));
            OutputDescription selected=default; var found=false;
            for(uint a=0;a<8 && !found;a++)
            {
                Release(ref adapter); if(Method<EnumObject>(factory,12)(factory,a,out adapter)<0) break;
                for(uint o=0;o<8;o++)
                {
                    Release(ref output); if(Method<EnumObject>(adapter,7)(adapter,o,out output)<0) break;
                    Check(Method<GetOutputDescription>(output,7)(output,out var desc));
                    if(desc.Attached!=0 && crop.X>=desc.Bounds.Left && crop.Y>=desc.Bounds.Top
                        && crop.X+crop.Width<=desc.Bounds.Right && crop.Y+crop.Height<=desc.Bounds.Bottom)
                    { selected=desc; found=true; break; }
                }
            }
            if(!found) throw new InvalidOperationException("No single DXGI output contains the Arena crop; spanning monitors unsupported by this probe.");
            if(selected.Rotation!=1) throw new NotSupportedException("Rotated outputs are rejected by this prototype.");
            operation="D3D11 device on selected output adapter"; Check(D3D11CreateDevice(adapter,0,0,0x20,0,0,7,out device,out _,out context));
            output1=Query(output,new("00cddea8-939b-4b83-a340-a685226666cc"));
            operation="IDXGIOutput1.DuplicateOutput"; Check(Method<DuplicateOutput>(output1,22)(output1,device,out duplication));
            operation="IDXGIOutputDuplication.AcquireNextFrame";
            FrameInfo info=default;
            while(true)
            {
                var result=Method<AcquireFrame>(duplication,8)(duplication,100,out info,out resource);
                if(result==unchecked((int)0x887A0027) && watch.Elapsed<TimeSpan.FromSeconds(2)) continue;
                Check(result); frameOwned=true;
                // A pointer-only update has no new desktop pixels. Do not treat it as a current Arena image.
                if(initiallyOccluded || (info.LastPresentTime!=0 && info.AccumulatedFrames!=0)) break;
                Method<ReleaseFrame>(duplication,14)(duplication); frameOwned=false; Release(ref resource);
                if(watch.Elapsed>=TimeSpan.FromSeconds(2)) throw new InvalidOperationException("Only pointer updates received; no desktop pixel update within two seconds.");
            }
            if(WindowAudit.Client(hwnd)!=client) throw new InvalidOperationException("Arena geometry changed; discard.");
            if(initiallyOccluded || Occluded(crop,pid)) return new { Success=false,NativeFrameAcquired=true,ImageSaved=false,
                Reason="Desktop Duplication acquired a frame, but Arena center is covered; no pixels read back or saved.",
                LastPresentQpc=info.LastPresentTime,DurationMs=watch.Elapsed.TotalMilliseconds,Crop=crop };
            if(info.ProtectedMasked!=0) throw new InvalidOperationException("Frame masks protected content; no image saved.");
            texture=Query(resource,new("6f15aaf2-d208-4e89-9ab4-489535d34f9c"));
            Method<GetTextureDescription>(texture,10)(texture,out var source);
            if(source.Format!=87 || source.Samples!=1) throw new NotSupportedException($"Unsupported desktop format {source.Format} / samples {source.Samples}.");
            var x=crop.X-selected.Bounds.Left; var y=crop.Y-selected.Bounds.Top;
            if(x<0 || y<0 || x+(long)crop.Width>source.Width || y+(long)crop.Height>source.Height) throw new InvalidDataException("Crop outside desktop texture.");
            operation="Regional D3D staging copy/map";
            var description=new TextureDescription { Width=(uint)crop.Width,Height=(uint)crop.Height,Mips=1,Array=1,Format=87,Samples=1,Usage=3,CpuAccess=0x20000 };
            Check(Method<CreateTexture>(device,5)(device,ref description,0,out staging));
            var box=new Box { Left=(uint)x,Top=(uint)y,Right=(uint)(x+crop.Width),Bottom=(uint)(y+crop.Height),Back=1 };
            Method<CopyRegion>(context,46)(context,staging,0,0,0,0,texture,0,ref box);
            Check(Method<Map>(context,14)(context,staging,0,1,0,out var mapped));
            var stride=checked(crop.Width*4); var bytes=new byte[checked(stride*crop.Height)];
            try
            {
                if(mapped.Data==0 || mapped.Pitch<stride) throw new InvalidDataException("Invalid staging row pitch.");
                for(var row=0;row<crop.Height;row++) Marshal.Copy(mapped.Data+checked((nint)((long)row*mapped.Pitch)),bytes,row*stride,stride);
            }
            finally { Method<Unmap>(context,15)(context,staging,0); }
            var min=765; var max=0;
            for(var i=0;i<bytes.Length;i+=4) { var rgb=bytes[i]+bytes[i+1]+bytes[i+2]; min=Math.Min(min,rgb); max=Math.Max(max,rgb); }
            if(min==max) throw new InvalidDataException("Uniform desktop crop cannot validate visible cards; no image saved.");
            var bitmap=BitmapSource.Create(crop.Width,crop.Height,96,96,PixelFormats.Bgra32,null,bytes,stride);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path=Path.Combine(outputDirectory,"arena-center-duplication.png"); using(var file=File.Create(path)) encoder.Save(file);
            return new { Success=true,Method="DXGI Desktop Duplication",Window=window.Handle,Crop=crop,PixelWidth=crop.Width,PixelHeight=crop.Height,
                LastPresentQpc=info.LastPresentTime,AccumulatedFrames=info.AccumulatedFrames,DurationMs=watch.Elapsed.TotalMilliseconds,ImagePath=path,
                Limitations="Single unrotated SDR output; sampled visibility guard; no recognition or superiority proof. Audit crop is not production calibration." };
        }
        catch(Exception ex) { return new { Success=false,Operation=operation,Exception=ex.GetType().Name,HResult=$"0x{ex.HResult:X8}",ex.Message,Crop=crop }; }
        finally
        {
            Release(ref staging); Release(ref texture); Release(ref resource);
            if(frameOwned && duplication!=0) Method<ReleaseFrame>(duplication,14)(duplication);
            Release(ref duplication); Release(ref context); Release(ref device); Release(ref output1); Release(ref output); Release(ref adapter); Release(ref factory);
        }
    }
    private static bool Occluded(PixelRect crop,int pid)
    {
        foreach(var (fx,fy) in new[]{(.1,.1),(.9,.1),(.5,.5),(.1,.9),(.9,.9)})
        { var h=WindowFromPoint(new(){X=crop.X+(int)(crop.Width*fx),Y=crop.Y+(int)(crop.Height*fy)}); GetWindowThreadProcessId(h,out var p); if(p!=pid) return true; }
        return false;
    }
    private static T Method<T>(nint obj,int slot) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*nint.Size));
    private static nint Query(nint obj,Guid id) { Check(Marshal.QueryInterface(obj,in id,out var result)); return result; }
    private static void Check(int result)=>Marshal.ThrowExceptionForHR(result);
    private static void Release(ref nint obj) { if(obj!=0) { Marshal.Release(obj); obj=0; } }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct OutputDescription
    { [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name; public Rect Bounds; public int Attached,Rotation; public nint Monitor; }
    [StructLayout(LayoutKind.Sequential)] private struct FrameInfo
    { public long LastPresentTime,LastMouseTime; public uint AccumulatedFrames; public int Coalesced,ProtectedMasked; public Point Pointer; public int PointerVisible; public uint MetadataBytes,PointerBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct TextureDescription { public uint Width,Height,Mips,Array,Format,Samples,Quality,Usage,Bind,CpuAccess,Misc; }
    [StructLayout(LayoutKind.Sequential)] private struct Box { public uint Left,Top,Front,Right,Bottom,Back; }
    [StructLayout(LayoutKind.Sequential)] private struct Mapped { public nint Data; public uint Pitch,Depth; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumObject(nint obj,uint index,out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetOutputDescription(nint obj,out OutputDescription description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int DuplicateOutput(nint obj,nint device,out nint duplication);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AcquireFrame(nint obj,uint timeout,out FrameInfo info,out nint resource);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReleaseFrame(nint obj);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetTextureDescription(nint obj,out TextureDescription description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(nint obj,ref TextureDescription description,nint data,out nint texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyRegion(nint obj,nint dest,uint destIndex,uint x,uint y,uint z,nint source,uint sourceIndex,ref Box box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Map(nint obj,nint resource,uint subresource,uint type,uint flags,out Mapped map);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Unmap(nint obj,nint resource,uint subresource);
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid id,out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter,uint driver,nint software,uint flags,nint levels,uint count,uint sdk,out nint device,out uint level,out nint context);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out uint process);
}
