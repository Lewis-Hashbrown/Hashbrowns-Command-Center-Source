using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;

namespace ClientDashboard;

public sealed class CaptureFrame
{
    public required BitmapSource Image { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
}

public static class WindowCapture
{
    public static CaptureFrame? Capture(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd)) return null;
        if (NativeMethods.IsIconic(hwnd)) return null;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return null;

        int w = rect.Width;
        int h = rect.Height;
        if (w <= 0 || h <= 0) return null;

        // Capture full window (not only client area) so bottom/status bars are preserved.
        IntPtr srcDc = NativeMethods.GetWindowDC(hwnd);
        if (srcDc == IntPtr.Zero) return null;

        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;

        try
        {
            memDc = NativeMethods.CreateCompatibleDC(srcDc);
            hBitmap = NativeMethods.CreateCompatibleBitmap(srcDc, w, h);
            var old = NativeMethods.SelectObject(memDc, hBitmap);

            if (!NativeMethods.BitBlt(memDc, 0, 0, w, h, srcDc, 0, 0, NativeMethods.SRCCOPY))
                return null;

            NativeMethods.SelectObject(memDc, old);

            using var bmp = Image.FromHbitmap(hBitmap);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var source = BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32,
                null, data.Scan0, data.Stride * h, data.Stride);
            bmp.UnlockBits(data);
            source.Freeze();

            return new CaptureFrame
            {
                Image = source,
                Width = w,
                Height = h
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(hwnd, srcDc);
        }
    }
}
