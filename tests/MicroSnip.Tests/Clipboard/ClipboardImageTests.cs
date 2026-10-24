using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using MicroSnip.Clipboard;
using Xunit;

namespace MicroSnip.Tests.Clipboard;

public sealed class ClipboardImageTests : IDisposable
{
    private readonly string _directory;
    private readonly string _imagePath;

    public ClipboardImageTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "MicroSnip.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _imagePath = Path.Combine(_directory, "entry.png");
        CreateTransparentPng(_imagePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task TrySetImageAsync_PlacesPngAndOpaqueBitmapFormats()
    {
        DataObject? captured = null;
        var service = new ClipboardService(data => captured = data, TimeSpan.Zero);

        bool copied = await service.TrySetImageAsync(_imagePath);

        Assert.True(copied);
        Assert.NotNull(captured);
        IDataObject data = captured;

        Assert.True(data.GetDataPresent(DataFormats.Bitmap));
        using var bitmap = Assert.IsType<Bitmap>(data.GetData(DataFormats.Bitmap));
        Assert.Equal(40, bitmap.Width);
        Assert.Equal(30, bitmap.Height);
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(
            Color.FromArgb(255, 12, 90, 200).ToArgb(),
            bitmap.GetPixel(15, 15).ToArgb());

        Assert.True(data.GetDataPresent(ClipboardImagePayload.PngFormat, autoConvert: false));
        using var stream = Assert.IsType<MemoryStream>(
            data.GetData(ClipboardImagePayload.PngFormat));
        Assert.Equal(File.ReadAllBytes(_imagePath), stream.ToArray());

        Assert.False(data.GetDataPresent(DataFormats.UnicodeText, autoConvert: false));
    }

    [Fact]
    public async Task TrySetTextAndImageAsync_PlacesTextAndImageTogether()
    {
        DataObject? captured = null;
        var service = new ClipboardService(data => captured = data, TimeSpan.Zero);

        bool copied = await service.TrySetTextAndImageAsync(
            "first line\r\nsecond line",
            _imagePath);

        Assert.True(copied);
        Assert.NotNull(captured);
        IDataObject data = captured;

        Assert.Equal("first line\r\nsecond line", data.GetData(DataFormats.UnicodeText));
        Assert.True(data.GetDataPresent(DataFormats.Bitmap));
        Assert.True(data.GetDataPresent(ClipboardImagePayload.PngFormat, autoConvert: false));

        using var bitmap = Assert.IsType<Bitmap>(data.GetData(DataFormats.Bitmap));
        Assert.Equal(40, bitmap.Width);
    }

    [Fact]
    public async Task TrySetImageAsync_RetriesTransientClipboardLocks()
    {
        int attempts = 0;
        DataObject? captured = null;
        var service = new ClipboardService(
            data =>
            {
                attempts++;

                if (attempts < 4)
                {
                    throw new ExternalException("Clipboard is busy.");
                }

                captured = data;
            },
            TimeSpan.Zero);

        bool copied = await service.TrySetImageAsync(_imagePath);

        Assert.True(copied);
        Assert.Equal(4, attempts);
        Assert.NotNull(captured);
        using var bitmap = Assert.IsType<Bitmap>(
            captured.GetData(DataFormats.Bitmap));
        Assert.Equal(40, bitmap.Width);
    }

    [Fact]
    public async Task TrySetImageAsync_GivesUpAfterTenAttempts()
    {
        int attempts = 0;
        var service = new ClipboardService(
            _ =>
            {
                attempts++;
                throw new ExternalException("Clipboard is busy.");
            },
            TimeSpan.Zero);

        bool copied = await service.TrySetImageAsync(_imagePath);

        Assert.False(copied);
        Assert.Equal(10, attempts);
    }

    [Fact]
    public async Task TrySetImageAsync_DoesNotTouchClipboardWhenImageIsMissing()
    {
        int attempts = 0;
        var service = new ClipboardService(
            _ => attempts++,
            TimeSpan.Zero);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.TrySetImageAsync(
                Path.Combine(_directory, "missing.png")));

        Assert.Equal(0, attempts);
    }

    private static void CreateTransparentPng(string path)
    {
        using var bitmap = new Bitmap(40, 30, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);

            using var brush = new SolidBrush(Color.FromArgb(255, 12, 90, 200));
            graphics.FillRectangle(brush, new Rectangle(10, 10, 20, 10));
        }

        bitmap.Save(path, ImageFormat.Png);
    }
}
