#if WINDOWS
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using SkiaSharp;

namespace DraftTG.App.Platform;

/// <summary>Small native bridge for the SDK projections. ABI slots are from Windows SDK d3d11.h; no third-party capture/graphics framework.</summary>
internal sealed class WgcDirect3DInterop : IDisposable
{
    private nint _device, _context;
    public IDirect3DDevice WinrtDevice { get; }
    private static readonly Guid DxgiDeviceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid SurfaceAccessId = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    private static readonly Guid Texture2DId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    public WgcDirect3DInterop()
    {
        nint dxgi = 0, inspectable = 0;
        try
        {
            // Hardware D3D11 device, BGRA support, SDK version 7; the request worker owns the immediate context.
            Check(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out _device, out _, out _context));
            dxgi = Query(_device, DxgiDeviceId);
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            WinrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        catch { Release(ref _context); Release(ref _device); throw; }
        finally { Release(ref inspectable); Release(ref dxgi); }
    }

    public SKBitmap CopyCrop(IDirect3DSurface surface, ArenaDraftCrop crop)
    {
        nint surfaceAbi = 0, access = 0, texture = 0, staging = 0;
        SKBitmap? bitmap = null;
        try
        {
            surfaceAbi = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
            access = Query(surfaceAbi, SurfaceAccessId);
            var id = Texture2DId;
            Check(Method<GetInterface>(access, 3)(access, ref id, out texture));
            Method<GetTextureDescription>(texture, 10)(texture, out var source);
            if (source.Format != 87 || source.SampleCount != 1 || crop.X < 0 || crop.Y < 0
                || crop.X + (long)crop.Width > source.Width || crop.Y + (long)crop.Height > source.Height)
                throw new InvalidDataException("WGC surface format/bounds differ from the requested BGRA crop.");
            var description = new TextureDescription
            {
                Width = (uint)crop.Width, Height = (uint)crop.Height, MipLevels = 1, ArraySize = 1,
                Format = 87, SampleCount = 1, Usage = 3, CpuAccessFlags = 0x20000 // D3D11_USAGE_STAGING / CPU_READ
            };
            Check(Method<CreateTexture>(_device, 5)(_device, ref description, 0, out staging));
            var box = new ResourceBox { Left = (uint)crop.X, Top = (uint)crop.Y, Front = 0,
                Right = (uint)(crop.X + crop.Width), Bottom = (uint)(crop.Y + crop.Height), Back = 1 };
            Method<CopyRegion>(_context, 46)(_context, staging, 0, 0, 0, 0, texture, 0, ref box);
            Check(Method<MapResource>(_context, 14)(_context, staging, 0, 1, 0, out var mapped)); // D3D11_MAP_READ
            try
            {
                var rowBytes = checked(crop.Width * 4);
                if (mapped.Data == 0 || mapped.RowPitch < rowBytes) throw new InvalidDataException("WGC mapped row pitch is invalid.");
                bitmap = new SKBitmap(crop.Width, crop.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                var row = new byte[rowBytes];
                for (var y = 0; y < crop.Height; y++)
                {
                    Marshal.Copy(mapped.Data + checked((nint)((long)y * mapped.RowPitch)), row, 0, rowBytes);
                    Marshal.Copy(row, 0, bitmap.GetPixels() + y * bitmap.RowBytes, rowBytes);
                }
            }
            finally { Method<UnmapResource>(_context, 15)(_context, staging, 0); }
            var result = bitmap; bitmap = null; return result;
        }
        finally { bitmap?.Dispose(); Release(ref staging); Release(ref texture); Release(ref access); Release(ref surfaceAbi); }
    }

    public static GraphicsCaptureItem CreateItem(nint hwnd)
    {
        nint name = 0, factory = 0, item = 0;
        try
        {
            const string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
            Check(WindowsCreateString(runtimeClass, runtimeClass.Length, out name));
            var factoryId = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
            Check(RoGetActivationFactory(name, ref factoryId, out factory));
            var itemId = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            Check(Method<CreateForWindow>(factory, 3)(factory, hwnd, ref itemId, out item));
            return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }
        finally { Release(ref item); Release(ref factory); if (name != 0) WindowsDeleteString(name); }
    }
    public void Dispose()
    {
        try { WinrtDevice.Dispose(); }
        finally { Release(ref _context); Release(ref _device); }
    }
    private static T Method<T>(nint obj, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * nint.Size));
    private static nint Query(nint obj, Guid id) { Check(Marshal.QueryInterface(obj, in id, out var result)); return result; }
    private static void Release(ref nint obj) { if (obj != 0) { Marshal.Release(obj); obj = 0; } }
    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);

    [StructLayout(LayoutKind.Sequential)] private struct TextureDescription
    { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct ResourceBox { public uint Left, Top, Front, Right, Bottom, Back; }
    [StructLayout(LayoutKind.Sequential)] private struct MappedResource { public nint Data; public uint RowPitch, DepthPitch; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(nint self, ref TextureDescription description, nint initial, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetTextureDescription(nint self, out TextureDescription description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInterface(nint self, ref Guid id, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateForWindow(nint self, nint hwnd, ref Guid id, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyRegion(nint self, nint destination, uint subresource, uint x, uint y, uint z, nint source, uint sourceSubresource, ref ResourceBox box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapResource(nint self, nint resource, uint subresource, uint map, uint flags, out MappedResource result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UnmapResource(nint self, nint resource, uint subresource);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        nint levels, uint levelCount, uint sdkVersion, out nint device, out uint featureLevel, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint device);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, int length, out nint result);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid id, out nint result);
}
#endif
