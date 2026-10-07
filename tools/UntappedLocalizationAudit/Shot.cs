using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace UntappedLocalizationAudit;

/// <summary>Composited-desktop capture of one screen rectangle (GDI BitBlt with CAPTUREBLT so layered overlays are included).</summary>
internal static class Shot
{
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dest, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    private const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    public static Bitmap Grab(Rect32 r)
    {
        var bitmap = new Bitmap(r.W, r.H, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        var screen = GetDC(0);
        var dest = g.GetHdc();
        try { BitBlt(dest, 0, 0, r.W, r.H, screen, r.X, r.Y, SRCCOPY | CAPTUREBLT); }
        finally { g.ReleaseHdc(dest); ReleaseDC(0, screen); }
        return bitmap;
    }

    public static void Capture(Rect32 r, string path)
    {
        using var bitmap = Grab(r);
        bitmap.Save(path, ImageFormat.Png);
    }
}
