using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MegaSnip.Clipboard;
using MegaSnip.History;
using MegaSnip.Ocr;
using MegaSnip.Settings;
using Xunit;
using DrawingColor = System.Drawing.Color;

namespace MegaSnip.Tests.History;

public sealed class HistoryWindowSmokeTests
{
    [Fact]
    public void Window_RendersSeededHistory()
    {
        Exception? failure = null;
        string screenshotPath = Path.Combine(
            Path.GetTempPath(),
            "MegaSnip.HistoryWindowSmoke.png");
        string narrowScreenshotPath = Path.Combine(
            Path.GetTempPath(),
            "MegaSnip.HistoryWindowSmoke.Narrow.png");
        string root = Path.Combine(
            Path.GetTempPath(),
            "MegaSnip.Tests",
            Guid.NewGuid().ToString("N"));

        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(root);

                var repository = new HistoryRepository(Path.Combine(root, "history"));
                var service = new HistoryService(repository);
                service.InitializeAsync().GetAwaiter().GetResult();
                Seed(service);

                var window = new HistoryWindow(
                    service,
                    new StubOcrService(),
                    new StubClipboardService(),
                    "en-US",
                    LineBreakMode.Preserve,
                    askBeforeDelete: false,
                    HistoryRowAction.OpenDetail,
                    "Ctrl+Alt+T",
                    _ => null);

                window.Show();
                var list = (ListBox)window.FindName("HistoryList");
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);

                while (list.Items.Count < 5 && DateTime.UtcNow < deadline)
                {
                    DoEvents();
                    Thread.Sleep(10);
                }

                DoEvents();

                if (list.Items.Count == 0)
                {
                    object? viewModel = typeof(HistoryWindow)
                        .GetField("_viewModel", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(window);
                    object? status = viewModel?.GetType()
                        .GetProperty("StatusMessage", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(viewModel);
                    object? count = viewModel?.GetType()
                        .GetProperty("TotalCount", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(viewModel);

                    throw new InvalidOperationException(
                        $"History list did not load. IsLoaded={window.IsLoaded}; " +
                        $"ItemsSource={list.ItemsSource?.GetType().Name ?? "null"}; " +
                        $"TotalCount={count}; Status={status}");
                }

                Assert.True(((Button)window.FindName("CopyTextButton")).IsEnabled);
                Assert.True(((Button)window.FindName("CopyImageButton")).IsEnabled);
                Assert.True(((Button)window.FindName("CopyBothButton")).IsEnabled);

                RenderWindow(window, screenshotPath);

                window.Width = 900;
                window.Height = 600;
                window.UpdateLayout();
                DoEvents();
                RenderWindow(window, narrowScreenshotPath);

                window.Close();
                service.Dispose();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The UI thread did not finish.");
        Assert.Null(failure);
        Assert.True(File.Exists(screenshotPath));
        Assert.True(File.Exists(narrowScreenshotPath));
    }

    private static void RenderWindow(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Round(content.ActualWidth)),
            Math.Max(1, (int)Math.Round(content.ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Seed(HistoryService service)
    {
        string[] texts =
        [
            "MegaSnip keeps every capture on this PC.\r\nNothing is uploaded, ever.",
            "Invoice #1042\r\nTotal: $128.40\r\nDue: November 3, 2026",
            "会议纪要：离线 OCR 与历史记录\r\n第二行：中文与 emoji 🎉",
            "This snip was saved without an image.\r\nIt still appears in the list.",
            "SELECT name, created_at FROM history_entries ORDER BY created_at DESC;",
        ];

        for (int index = 0; index < texts.Length; index++)
        {
            DateTimeOffset createdAt = DateTimeOffset.UtcNow.AddMinutes(-index * 7);
            bool withImage = index < 4;

            using Bitmap? image = withImage ? CreateImage(index) : null;

            service.SaveAsync(
                    new HistorySaveRequest
                    {
                        Id = HistoryEntryId.Create(createdAt),
                        CreatedAtUtc = createdAt,
                        Text = texts[index],
                        Width = withImage ? image!.Width : 640,
                        Height = withImage ? image!.Height : 240,
                        MonitorId = @"\\.\DISPLAY1",
                        OcrLanguage = index == 2 ? "zh-Hans" : "en-US",
                        AppVersion = "0.1.0",
                        Image = image,
                        SaveImage = withImage,
                    })
                .GetAwaiter()
                .GetResult();
        }
    }

    private static Bitmap CreateImage(int index)
    {
        var bitmaps = new[]
        {
            (DrawingColor.FromArgb(30, 52, 96), DrawingColor.FromArgb(220, 236, 255)),
            (DrawingColor.FromArgb(96, 58, 30), DrawingColor.FromArgb(255, 238, 220)),
            (DrawingColor.FromArgb(22, 78, 66), DrawingColor.FromArgb(214, 255, 241)),
            (DrawingColor.FromArgb(70, 30, 70), DrawingColor.FromArgb(252, 224, 255)),
        };

        (DrawingColor background, DrawingColor foreground) = bitmaps[index % bitmaps.Length];
        var image = new Bitmap(520, 220);

        using (Graphics graphics = Graphics.FromImage(image))
        {
            graphics.Clear(background);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(foreground);
            using var font = new Font("Segoe UI", 18, System.Drawing.FontStyle.Bold);
            using var smallFont = new Font("Segoe UI", 12);

            graphics.DrawString(
                "MegaSnip sample capture " + (index + 1),
                font,
                brush,
                new PointF(28, 42));
            graphics.DrawString(
                "Offline OCR · history · export",
                smallFont,
                brush,
                new PointF(28, 92));
        }

        return image;
    }

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class StubOcrService : IOcrService
    {
        public IReadOnlyList<OcrLanguage> AvailableLanguages => [];

        public Task WarmUpAsync(
            string? preferredLanguageTag,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> RecognizeAsync(
            Bitmap bitmap,
            string? preferredLanguageTag,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("OCR result");
    }

    private sealed class StubClipboardService : IClipboardService
    {
        public Task<bool> TrySetTextAsync(
            string text,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> TrySetImageAsync(
            string imagePath,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> TrySetTextAndImageAsync(
            string text,
            string imagePath,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
