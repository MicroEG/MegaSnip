using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MegaSnip.Clipboard;

internal sealed class ClipboardImagePayload
{
    internal const string PngFormat = "PNG";

    private readonly byte[] _pngBytes;
    private readonly Bitmap _opaqueBitmap;

    private ClipboardImagePayload(byte[] pngBytes, Bitmap opaqueBitmap)
    {
        _pngBytes = pngBytes;
        _opaqueBitmap = opaqueBitmap;
    }

    internal static ClipboardImagePayload Load(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        byte[] pngBytes = File.ReadAllBytes(imagePath);

        using var stream = new MemoryStream(pngBytes, writable: false);
        using var source = new Bitmap(stream);

        return new ClipboardImagePayload(pngBytes, FlattenToOpaque(source));
    }

    internal DataObject CreateDataObject(string? text)
    {
        var data = new DataObject();

        if (text is not null)
        {
            data.SetData(DataFormats.UnicodeText, text);
        }

        data.SetData(DataFormats.Bitmap, true, _opaqueBitmap);
        data.SetData(
            PngFormat,
            false,
            new MemoryStream(_pngBytes, writable: false));
        return data;
    }

    private static Bitmap FlattenToOpaque(Bitmap source)
    {
        var flattened = new Bitmap(
            source.Width,
            source.Height,
            PixelFormat.Format24bppRgb);

        try
        {
            using Graphics graphics = Graphics.FromImage(flattened);
            graphics.Clear(Color.White);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, source.Width, source.Height));
            return flattened;
        }
        catch
        {
            flattened.Dispose();
            throw;
        }
    }
}
