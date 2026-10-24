using System;
using System.Drawing;
using System.Drawing.Imaging;
using MicroSnip.Infrastructure;

namespace MicroSnip.Capture;

internal sealed class ScreenCapture : IScreenCapture
{
    public Bitmap Capture(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "The capture rectangle must have a positive width and height.");
        }

        // The overlay is hidden immediately before this call. Wait for DWM to
        // commit that change so the overlay cannot leak into the capture.
        _ = NativeMethods.DwmFlush();

        var bitmap = new Bitmap(
            bounds.Width,
            bounds.Height,
            PixelFormat.Format32bppRgb);

        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                new Point(bounds.Left, bounds.Top),
                Point.Empty,
                bounds.Size,
                CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
