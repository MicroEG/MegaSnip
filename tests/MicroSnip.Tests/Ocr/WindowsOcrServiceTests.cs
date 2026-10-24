using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using MicroSnip.Ocr;
using Xunit;

namespace MicroSnip.Tests.Ocr;

public sealed class WindowsOcrServiceTests
{
    [Fact]
    public async Task RecognizeAsync_ReadsFixedSampleImage()
    {
        string imagePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "ocr-sample.png");
        Assert.True(File.Exists(imagePath), $"Missing OCR fixture: {imagePath}");

        using var bitmap = new Bitmap(imagePath);
        var service = new WindowsOcrService();

        string text = await service.RecognizeAsync(bitmap, "en-US");

        Assert.Equal("MicroSnip OCR Test\r\nLine two 42", text);
    }

    [Fact]
    public void AvailableLanguages_ReportsInstalledOcrLanguages()
    {
        var service = new WindowsOcrService();

        Assert.NotEmpty(service.AvailableLanguages);
    }
}
