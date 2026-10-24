using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MicroSnip.Clipboard;
using MicroSnip.Infrastructure;
using MicroSnip.Ocr;
using MicroSnip.Settings;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace MicroSnip.Preview;

internal partial class PreviewWindow : Window
{
    private static readonly Brush MutedBrush = CreateBrush(Color.FromRgb(138, 148, 166));
    private static readonly Brush SuccessBrush = CreateBrush(Color.FromRgb(110, 231, 168));
    private static readonly Brush ErrorBrush = CreateBrush(Color.FromRgb(255, 135, 128));

    private readonly IOcrService _ocrService;
    private readonly IClipboardService _clipboardService;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Bitmap _capture;
    private string? _languageTag;
    private LineBreakMode _lineBreakMode;
    private bool _isBusy;

    internal PreviewWindow(
        Bitmap capture,
        string text,
        string? languageTag,
        LineBreakMode lineBreakMode,
        IOcrService ocrService,
        IClipboardService clipboardService)
    {
        _capture = capture;
        _languageTag = languageTag;
        _lineBreakMode = lineBreakMode;
        _ocrService = ocrService;
        _clipboardService = clipboardService;

        try
        {
            InitializeComponent();

            PreviewImage.Source = CreateThumbnail(_capture);
            TextEditor.Text = text;
            CharacterCountText.Text = FormatCharacterCount(text.Length);
            SetStatus("Ready");
            Loaded += OnLoaded;
        }
        catch
        {
            _lifetime.Dispose();
            _capture.Dispose();
            throw;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Cancel();
        _capture.Dispose();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    internal void ApplySettings(string? languageTag, LineBreakMode lineBreakMode)
    {
        _languageTag = languageTag;
        _lineBreakMode = lineBreakMode;
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static string FormatCharacterCount(int count) =>
        count == 1 ? "1 char" : $"{count} chars";

    private static BitmapSource CreateThumbnail(Bitmap source)
    {
        const int maxWidth = 360;
        const int maxHeight = 160;

        double scale = Math.Min(
            1d,
            Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height));
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var thumbnail = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        using (Graphics graphics = Graphics.FromImage(thumbnail))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        using var stream = new MemoryStream();
        thumbnail.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        return image;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TextEditor.Focus();
        TextEditor.CaretIndex = TextEditor.Text.Length;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        CharacterCountText.Text = FormatCharacterCount(TextEditor.Text.Length);
    }

    private async void OnRerunClicked(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        Bitmap input;

        try
        {
            input = (Bitmap)_capture.Clone();
        }
        catch (Exception ex)
        {
            SetStatus("Could not start OCR: " + ex.Message, ErrorBrush);
            return;
        }

        SetBusy(true);
        SetStatus("Running OCR…", MutedBrush);

        try
        {
            string text = await Task.Run(
                () => _ocrService.RecognizeAsync(input, _languageTag, _lifetime.Token),
                CancellationToken.None).ConfigureAwait(true);

            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            TextEditor.Text = text;
            SetStatus(
                text.Length == 0 ? "No text detected in this image." : "OCR complete.",
                text.Length == 0 ? ErrorBrush : SuccessBrush);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                SetStatus("OCR failed: " + ex.Message, ErrorBrush);
            }
        }
        finally
        {
            input.Dispose();

            if (!_lifetime.IsCancellationRequested)
            {
                SetBusy(false);
            }
        }
    }

    private async void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        await CopyTextAsync();
    }

    private async void OnCopyCloseClicked(object sender, RoutedEventArgs e)
    {
        bool copied = await CopyTextAsync();

        if (copied && !_lifetime.IsCancellationRequested)
        {
            Close();
        }
    }

    private async Task<bool> CopyTextAsync()
    {
        if (_isBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(TextEditor.Text))
        {
            SetStatus("There is no text to copy.", ErrorBrush);
            return false;
        }

        string text = ClipboardTextFormatter.Format(TextEditor.Text, _lineBreakMode);

        SetBusy(true);
        SetStatus("Copying…", MutedBrush);

        try
        {
            bool copied = await _clipboardService
                .TrySetTextAsync(text, _lifetime.Token)
                .ConfigureAwait(true);

            if (_lifetime.IsCancellationRequested)
            {
                return false;
            }

            SetStatus(
                copied
                    ? _lineBreakMode == LineBreakMode.JoinWithSpaces
                        ? "Copied as a single line."
                        : "Copied to the clipboard."
                    : "Could not copy — the clipboard is busy.",
                copied ? SuccessBrush : ErrorBrush);

            return copied;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                SetStatus("Could not copy: " + ex.Message, ErrorBrush);
            }

            return false;
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        RerunButton.IsEnabled = !isBusy;
        CopyButton.IsEnabled = !isBusy;
        CopyCloseButton.IsEnabled = !isBusy;
        TextEditor.IsReadOnly = isBusy;
    }

    private void SetStatus(string message, Brush? foreground = null)
    {
        StatusText.Text = message;
        StatusText.Foreground = foreground ?? MutedBrush;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
