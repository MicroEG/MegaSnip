using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MegaSnip.History;
using MegaSnip.Ocr;
using MegaSnip.Settings;
using Xunit;

namespace MegaSnip.Tests.Settings;

public sealed class SettingsWindowSmokeTests
{
    [Fact]
    public void Window_RendersHistorySettings()
    {
        Exception? failure = null;
        string screenshotPath = Path.Combine(
            Path.GetTempPath(),
            "MegaSnip.SettingsWindowSmoke.png");
        string historyScreenshotPath = Path.Combine(
            Path.GetTempPath(),
            "MegaSnip.SettingsWindowSmoke.History.png");

        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings
                {
                    SaveHistory = true,
                    SaveHistoryImages = true,
                    AskBeforeDelete = true,
                    DefaultHistoryAction = HistoryRowAction.CopyText,
                    HistoryRetention = new HistoryRetentionSettings
                    {
                        AutoCleanupEnabled = true,
                        MaxEntries = 500,
                        MaxAgeDays = 30,
                        MaxDiskMB = 500,
                    },
                };

                var window = new SettingsWindow(
                    settings,
                    new[] { new OcrLanguage("en-US", "English (United States)") },
                    () => settings,
                    _ => null,
                    _ => { },
                    _ => Task.FromResult(
                        HistoryDirectoryChangeResult.Success(
                            HistoryPaths.Resolve(settings.HistoryDirectory))));

                window.Show();
                window.UpdateLayout();
                DoEvents();

                Assert.True(((CheckBox)window.FindName("SaveHistoryCheck")).IsChecked);
                Assert.True(((CheckBox)window.FindName("SaveHistoryImagesCheck")).IsChecked);
                Assert.True(((CheckBox)window.FindName("AskBeforeDeleteCheck")).IsChecked);
                Assert.True(((CheckBox)window.FindName("AutoCleanupCheck")).IsChecked);
                Assert.Equal(
                    1,
                    ((ComboBox)window.FindName("DefaultHistoryActionCombo")).SelectedIndex);
                Assert.Equal(
                    HistoryPaths.Resolve(null),
                    ((TextBlock)window.FindName("HistoryPathText")).Text);
                Assert.Equal("500", ((TextBox)window.FindName("MaxEntriesBox")).Text);
                Assert.Equal("30", ((TextBox)window.FindName("MaxAgeDaysBox")).Text);
                Assert.Equal("500", ((TextBox)window.FindName("MaxDiskMBBox")).Text);
                Assert.True(((StackPanel)window.FindName("HistoryOptionsPanel")).IsEnabled);
                Assert.True(((StackPanel)window.FindName("AutoCleanupOptionsPanel")).IsEnabled);

                RenderWindow(window, screenshotPath);

                window.Height = 1160;
                window.UpdateLayout();
                DoEvents();
                ((ScrollViewer)window.FindName("FormScroll")).ScrollToEnd();
                DoEvents();
                RenderWindow(window, historyScreenshotPath);

                var saveHistory = (CheckBox)window.FindName("SaveHistoryCheck");
                saveHistory.IsChecked = false;
                DoEvents();

                Assert.False(((StackPanel)window.FindName("HistoryOptionsPanel")).IsEnabled);

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
        Assert.True(File.Exists(historyScreenshotPath));
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
