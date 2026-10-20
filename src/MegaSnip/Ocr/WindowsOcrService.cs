using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MegaSnip.Ocr;

internal sealed class WindowsOcrService : IOcrService
{
    private readonly Lazy<IReadOnlyList<OcrLanguage>> _availableLanguages =
        new(LoadAvailableLanguages);

    public IReadOnlyList<OcrLanguage> AvailableLanguages => _availableLanguages.Value;

    public async Task WarmUpAsync(
        string? preferredLanguageTag,
        CancellationToken cancellationToken = default)
    {
        using var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppRgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
        }

        _ = await RecognizeAsync(bitmap, preferredLanguageTag, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> RecognizeAsync(
        Bitmap bitmap,
        string? preferredLanguageTag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            throw new ArgumentException("The image must have a positive size.", nameof(bitmap));
        }

        cancellationToken.ThrowIfCancellationRequested();

        OcrEngine engine = CreateEngine(preferredLanguageTag);
        Bitmap prepared = PrepareImage(bitmap, OcrEngine.MaxImageDimension);

        try
        {
            using SoftwareBitmap softwareBitmap =
                await ToSoftwareBitmapAsync(prepared, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            OcrResult result = await engine.RecognizeAsync(softwareBitmap)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            return OcrTextPostProcessor.Normalize(result.Lines.Select(line => line.Text));
        }
        finally
        {
            if (!ReferenceEquals(prepared, bitmap))
            {
                prepared.Dispose();
            }
        }
    }

    private static IReadOnlyList<OcrLanguage> LoadAvailableLanguages()
    {
        var languages = new List<OcrLanguage>();

        foreach (Language language in OcrEngine.AvailableRecognizerLanguages)
        {
            languages.Add(new OcrLanguage(language.LanguageTag, language.DisplayName));
        }

        return languages.AsReadOnly();
    }

    private static OcrEngine CreateEngine(string? preferredLanguageTag)
    {
        if (!string.IsNullOrWhiteSpace(preferredLanguageTag))
        {
            OcrEngine? preferred = TryCreateEngine(preferredLanguageTag);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        OcrEngine? profileEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (profileEngine is not null)
        {
            return profileEngine;
        }

        foreach (Language language in OcrEngine.AvailableRecognizerLanguages)
        {
            OcrEngine? engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine is not null)
            {
                return engine;
            }
        }

        throw new InvalidOperationException(
            "No Windows OCR language pack is installed. Add a language with OCR support in Windows Settings.");
    }

    private static OcrEngine? TryCreateEngine(string languageTag)
    {
        try
        {
            return OcrEngine.TryCreateFromLanguage(new Language(languageTag));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Bitmap PrepareImage(Bitmap source, uint maxDimension)
    {
        double scale = CalculateScale(source.Width, source.Height, (int)maxDimension);
        if (Math.Abs(scale - 1) < 0.0001)
        {
            return source;
        }

        int width = Math.Max(1, (int)Math.Floor(source.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(source.Height * scale));
        var target = new Bitmap(width, height, PixelFormat.Format32bppRgb);

        try
        {
            using (Graphics graphics = Graphics.FromImage(target))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height));
            }

            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    private static double CalculateScale(int width, int height, int maxDimension)
    {
        int longestSide = Math.Max(width, height);
        double scale = longestSide switch
        {
            <= 400 => 3.0,
            <= 1600 => 2.0,
            _ => 1.0,
        };

        if (longestSide * scale > maxDimension)
        {
            scale = (double)maxDimension / longestSide;
        }

        return scale;
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(
        Bitmap bitmap,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[] encoded;
        using (var memoryStream = new MemoryStream())
        {
            bitmap.Save(memoryStream, ImageFormat.Bmp);
            encoded = memoryStream.ToArray();
        }

        var randomAccessStream = new InMemoryRandomAccessStream();

        try
        {
            var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0));

            try
            {
                writer.WriteBytes(encoded);
                await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
                writer.DetachStream();
            }
            finally
            {
                writer.Dispose();
            }

            randomAccessStream.Seek(0);

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(randomAccessStream)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            return await decoder
                .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            randomAccessStream.Dispose();
        }
    }
}
