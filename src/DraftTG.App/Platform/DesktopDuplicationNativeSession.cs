#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace DraftTG.App.Platform;

/// <summary>Serialized native owner. Monitor textures stay on GPU; only the calibrated Arena ROI is read back.</summary>
internal sealed class DesktopDuplicationNativeSession : IDisposable
{
    private nint _factory, _adapter, _output, _output1, _device, _context, _duplication, _staging;
    private readonly ArenaWindowGeometry _window;
    private int _stagingWidth, _stagingHeight;
    public DesktopOutput Output { get; }
    public TimeSpan? PresentationTime { get; private set; }
    public DesktopDuplicationNativeSession(ArenaWindowGeometry window)
    {
        _window = window;
        var operation = "DXGI factory/output enumeration";
        try
        {
            var id = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); Check(CreateDXGIFactory1(ref id, out _factory));
            var descriptions = new List<DesktopOutput>();
            for (uint a = 0; a < 32; a++)
            {
                nint adapter = 0;
                try
                {
                    var result = Method<EnumObject>(_factory, 12)(_factory, a, out adapter);
                    if (result == unchecked((int)0x887A0002)) break;
                    Check(result);
                    for (uint o = 0; o < 32; o++)
                    {
                        nint output = 0;
                        try
                        {
                            result = Method<EnumObject>(adapter, 7)(adapter, o, out output);
                            if (result == unchecked((int)0x887A0002)) break;
                            Check(result); Check(Method<GetOutputDescription>(output, 7)(output, out var desc));
                            descriptions.Add(new((int)a, (int)o, desc.Name,
                                new(desc.Bounds.Left, desc.Bounds.Top, desc.Bounds.Right - desc.Bounds.Left, desc.Bounds.Bottom - desc.Bounds.Top),
                                desc.Attached != 0, desc.Rotation));
                        }
                        finally { Release(ref output); }
                    }
                }
                finally { Release(ref adapter); }
            }
            Output = DesktopDuplicationGeometry.Select(window, descriptions);
            Check(Method<EnumObject>(_factory, 12)(_factory, (uint)Output.Adapter, out _adapter));
            Check(Method<EnumObject>(_adapter, 7)(_adapter, (uint)Output.Index, out _output));
            operation = "D3D11 device on selected adapter";
            Check(D3D11CreateDevice(_adapter, 0, 0, 0x20, 0, 0, 7, out _device, out _, out _context));
            _output1 = Query(_output, new("00cddea8-939b-4b83-a340-a685226666cc"));
            operation = "IDXGIOutput1.DuplicateOutput";
            Check(Method<DuplicateOutput>(_output1, 22)(_output1, _device, out _duplication));
        }
        catch (Exception ex) { Dispose(); throw new CaptureBackendInitializationException(operation + " failed: " + ex.Message, ex, operation); }
    }
    public bool Matches(ArenaWindowGeometry window) => ArenaWindowGeometry.SameCaptureGeometry(_window, window)
        && _factory != 0 && Method<IsCurrent>(_factory, 13)(_factory) != 0;

    public SKBitmap CopyCurrentCrop(ArenaWindowGeometry window, ArenaDraftCrop crop, Action<string> progress,
        Action<Func<DesktopDuplicationMetrics, DesktopDuplicationMetrics>> metrics, CancellationToken token)
    {
        var mappedCrop = DesktopDuplicationGeometry.Map(Output, window, crop);
        var requestedQpc = Stopwatch.GetTimestamp(); var acquire = Stopwatch.StartNew();
        nint resource = 0, texture = 0; var owned = false; Exception? failure = null; SKBitmap? delivered = null;
        try
        {
            progress("Waiting for changed Desktop Duplication image");
            FrameInfo info;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (acquire.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("No current DD desktop pixel update within five seconds.");
                var result = Method<AcquireFrame>(_duplication, 8)(_duplication, 100, out info, out resource);
                if (result == unchecked((int)0x887A0027)) continue;
                Check(result); owned = true;
                metrics(m => m with { FramesAcquired = m.FramesAcquired + 1 });
                if (!DesktopDuplicationGeometry.IsPixelUpdate(info.LastPresentTime, info.AccumulatedFrames))
                    metrics(m => m with { PointerOnlySkipped = m.PointerOnlySkipped + 1 });
                else if (info.LastPresentTime < requestedQpc)
                    metrics(m => m with { PriorPresentationSkipped = m.PriorPresentationSkipped + 1 });
                else break;
                Check(Method<ReleaseFrame>(_duplication, 14)(_duplication)); owned = false; Release(ref resource);
            }
            metrics(m => m with { AcquisitionMs = acquire.Elapsed.TotalMilliseconds });
            token.ThrowIfCancellationRequested();
            GdiBitBltFrameCapture.ValidateWindow(window); GdiBitBltFrameCapture.EnsureUnobstructed(window, crop);
            if (!Matches(window)) throw new InvalidDataException("DXGI output/window topology changed during request; discard.");
            if (info.ProtectedMasked != 0) throw new InvalidDataException("DD protected-content mask; frame discarded.");
            progress("Copying calibrated Arena crop from DD GPU texture");
            var conversion = Stopwatch.StartNew();
            texture = Query(resource, new("6f15aaf2-d208-4e89-9ab4-489535d34f9c"));
            Method<GetTextureDescription>(texture, 10)(texture, out var source);
            if (source.Format != 87 || source.Samples != 1) throw new NotSupportedException($"Unsupported DD pixel format {source.Format}/samples {source.Samples}; SDR BGRA required.");
            if (mappedCrop.X < 0 || mappedCrop.Y < 0 || mappedCrop.X + (long)crop.Width > source.Width || mappedCrop.Y + (long)crop.Height > source.Height)
                throw new InvalidDataException("Physical crop does not fit selected output texture.");
            if (_staging == 0 || _stagingWidth != crop.Width || _stagingHeight != crop.Height)
            {
                Release(ref _staging);
                var desc = new TextureDescription { Width = (uint)crop.Width, Height = (uint)crop.Height, Mips = 1, Array = 1,
                    Format = 87, Samples = 1, Usage = 3, CpuAccess = 0x20000 };
                Check(Method<CreateTexture>(_device, 5)(_device, ref desc, 0, out _staging));
                _stagingWidth = crop.Width; _stagingHeight = crop.Height;
            }
            var box = new Box { Left = (uint)mappedCrop.X, Top = (uint)mappedCrop.Y,
                Right = (uint)(mappedCrop.X + crop.Width), Bottom = (uint)(mappedCrop.Y + crop.Height), Back = 1 };
            Method<CopyRegion>(_context, 46)(_context, _staging, 0, 0, 0, 0, texture, 0, ref box);
            var image = new SKBitmap(crop.Width, crop.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            try
            {
                Check(Method<Map>(_context, 14)(_context, _staging, 0, 1, 0, out var mapped));
                try
                {
                    if (mapped.Data == 0 || mapped.Pitch < image.RowBytes) throw new InvalidDataException("Invalid DD staging pitch.");
                    var row = new byte[image.RowBytes]; var min = 765; var max = 0;
                    for (var y = 0; y < crop.Height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        Marshal.Copy(mapped.Data + checked((nint)((long)y * mapped.Pitch)), row, 0, row.Length);
                        for (var x = 0; x < row.Length; x += 4) { var rgb = row[x] + row[x + 1] + row[x + 2]; min = Math.Min(min, rgb); max = Math.Max(max, rgb); }
                        Marshal.Copy(row, 0, image.GetPixels() + y * image.RowBytes, row.Length);
                    }
                    if (min == max) throw new InvalidDataException("Uniform DD crop cannot validate Arena pixels; discard.");
                }
                finally { Method<Unmap>(_context, 15)(_context, _staging, 0); }
                token.ThrowIfCancellationRequested(); GdiBitBltFrameCapture.ValidateWindow(window);
                GdiBitBltFrameCapture.EnsureUnobstructed(window, crop);
                PresentationTime = TimeSpan.FromSeconds(info.LastPresentTime / (double)Stopwatch.Frequency);
                metrics(m => m with { FirstUsableImageAt = DateTimeOffset.UtcNow, CropMs = conversion.Elapsed.TotalMilliseconds });
                return delivered = image;
            }
            catch { image.Dispose(); throw; }
        }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            Release(ref texture); Release(ref resource);
            if (owned)
            {
                var release = Method<ReleaseFrame>(_duplication, 14)(_duplication);
                if (release < 0 && failure is not null) Trace.TraceError($"DD secondary ReleaseFrame failure: 0x{release:X8}");
                else if (release < 0) { delivered?.Dispose(); Check(release); }
            }
        }
    }
    public void Dispose()
    {
        Release(ref _staging); Release(ref _duplication); Release(ref _context); Release(ref _device);
        Release(ref _output1); Release(ref _output); Release(ref _adapter); Release(ref _factory);
    }
    private static T Method<T>(nint obj, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * nint.Size));
    private static nint Query(nint obj, Guid id) { Check(Marshal.QueryInterface(obj, in id, out var result)); return result; }
    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    private static void Release(ref nint obj) { if (obj != 0) { Marshal.Release(obj); obj = 0; } }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OutputDescription
    { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public Rect Bounds; public int Attached, Rotation; public nint Monitor; }
    [StructLayout(LayoutKind.Sequential)] private struct FrameInfo
    { public long LastPresentTime, LastMouseTime; public uint AccumulatedFrames; public int Coalesced, ProtectedMasked; public Point Pointer; public int PointerVisible; public uint MetadataBytes, PointerBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct TextureDescription { public uint Width, Height, Mips, Array, Format, Samples, Quality, Usage, Bind, CpuAccess, Misc; }
    [StructLayout(LayoutKind.Sequential)] private struct Box { public uint Left, Top, Front, Right, Bottom, Back; }
    [StructLayout(LayoutKind.Sequential)] private struct Mapped { public nint Data; public uint Pitch, Depth; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumObject(nint obj, uint index, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetOutputDescription(nint obj, out OutputDescription description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int DuplicateOutput(nint obj, nint device, out nint duplication);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AcquireFrame(nint obj, uint timeout, out FrameInfo info, out nint resource);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReleaseFrame(nint obj);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int IsCurrent(nint obj);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetTextureDescription(nint obj, out TextureDescription description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(nint obj, ref TextureDescription description, nint data, out nint texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyRegion(nint obj, nint dest, uint destIndex, uint x, uint y, uint z, nint source, uint sourceIndex, ref Box box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Map(nint obj, nint resource, uint subresource, uint type, uint flags, out Mapped map);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Unmap(nint obj, nint resource, uint subresource);
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid id, out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, uint driver, nint software, uint flags, nint levels, uint count, uint sdk, out nint device, out uint level, out nint context);
}
#endif
