using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MicroSnip.History;

internal static class ThumbnailGenerator
{
    internal const int MaxDimension = 160;

    internal static Bitmap Create(Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Width <= 0 || source.Height <= 0)
        {
            throw new ArgumentException("The image must have a positive size.", nameof(source));
        }

        int longestSide = Math.Max(source.Width, source.Height);
        double scale = Math.Min(1.0, (double)MaxDimension / longestSide);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var thumbnail = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        try
        {
            using Graphics graphics = Graphics.FromImage(thumbnail);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.Clear(Color.White);
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
            return thumbnail;
        }
        catch
        {
            thumbnail.Dispose();
            throw;
        }
    }
}
