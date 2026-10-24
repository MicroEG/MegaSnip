using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MicroSnip.Updates;
using Xunit;

namespace MicroSnip.Tests.Updates;

public sealed class UpdateWindowSmokeTests
{
    [Fact]
    public void Window_ShowsAvailableUpdate()
    {
        Exception? failure = null;
        string screenshotPath = Path.Combine(
            Path.GetTempPath(),
            "MicroSnip.UpdateWindowSmoke.Available.png");

        var thread = new Thread(() =>
        {
            try
            {
                var result = new UpdateCheckResult(
                    UpdateCheckStatus.UpdateAvailable,
                    "0.1.0",
                    "0.2.0",
                    new UpdateRelease(
                        "v0.2.0",
                        "MicroSnip 0.2.0",
                        "https://github.com/MicroEG/MicroSnip/releases/tag/v0.2.0",
                        "Fixes and improvements."));

                var window = new UpdateWindow(
                    new Version(0, 1, 0, 0),
                    _ => Task.FromResult(result));

                window.Show();
                window.UpdateLayout();
                DoEvents();

                Assert.Equal(
                    "0.1.0",
                    ((TextBlock)window.FindName("CurrentVersionText")).Text);
                Assert.Equal(
                    "MicroSnip 0.2.0 is available",
                    ((TextBlock)window.FindName("StatusTitleText")).Text);
                Assert.Contains(
                    "You have 0.1.0.",
                    ((TextBlock)window.FindName("StatusDetailText")).Text);
                Assert.Equal(
                    Visibility.Visible,
                    ((Button)window.FindName("OpenReleaseButton")).Visibility);
                Assert.Equal(
                    Visibility.Visible,
                    ((Border)window.FindName("ReleaseNotesPanel")).Visibility);
                Assert.Equal(
                    "Fixes and improvements.",
                    ((TextBlock)window.FindName("NotesText")).Text);

                window.UpdateLayout();
                RenderWindow(window, screenshotPath);

                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The UI thread did not finish.");
        Assert.Null(failure);
        Assert.True(File.Exists(screenshotPath));
    }

    [Fact]
    public void Window_ShowsFailureMessage()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var window = new UpdateWindow(
                    new Version(0, 1, 0, 0),
                    _ => Task.FromException<UpdateCheckResult>(
                        new UpdateCheckException("GitHub could not be reached.")));

                window.Show();
                window.UpdateLayout();
                DoEvents();

                Assert.Equal(
                    "Could not check for updates",
                    ((TextBlock)window.FindName("StatusTitleText")).Text);
                Assert.Equal(
                    "GitHub could not be reached.",
                    ((TextBlock)window.FindName("StatusDetailText")).Text);
                Assert.Equal(
                    Visibility.Collapsed,
                    ((Button)window.FindName("OpenReleaseButton")).Visibility);

                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The UI thread did not finish.");
        Assert.Null(failure);
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

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
