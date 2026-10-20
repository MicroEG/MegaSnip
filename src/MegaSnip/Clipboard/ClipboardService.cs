using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MegaSnip.Clipboard;

internal sealed class ClipboardService : IClipboardService
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly Action<DataObject> _setClipboardData;
    private readonly Func<string, ClipboardImagePayload> _loadImage;
    private readonly TimeSpan _retryDelay;

    public ClipboardService()
        : this(SetClipboardData, ClipboardImagePayload.Load, DefaultRetryDelay)
    {
    }

    internal ClipboardService(Action<DataObject> setClipboardData, TimeSpan retryDelay)
        : this(setClipboardData, ClipboardImagePayload.Load, retryDelay)
    {
    }

    internal ClipboardService(
        Action<DataObject> setClipboardData,
        Func<string, ClipboardImagePayload> loadImage,
        TimeSpan retryDelay)
    {
        ArgumentNullException.ThrowIfNull(setClipboardData);
        ArgumentNullException.ThrowIfNull(loadImage);

        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        _setClipboardData = setClipboardData;
        _loadImage = loadImage;
        _retryDelay = retryDelay;
    }

    public Task<bool> TrySetTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TrySetAsync(() => CreateTextDataObject(text), cancellationToken);
    }

    public async Task<bool> TrySetImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        ClipboardImagePayload payload = await Task.Run(
            () => _loadImage(imagePath),
            cancellationToken).ConfigureAwait(true);

        return await TrySetAsync(
            () => payload.CreateDataObject(text: null),
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<bool> TrySetTextAndImageAsync(
        string text,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        ClipboardImagePayload payload = await Task.Run(
            () => _loadImage(imagePath),
            cancellationToken).ConfigureAwait(true);

        return await TrySetAsync(
            () => payload.CreateDataObject(text),
            cancellationToken).ConfigureAwait(true);
    }

    private async Task<bool> TrySetAsync(
        Func<DataObject> createData,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _setClipboardData(createData());
                return true;
            }
            catch (ExternalException)
            {
                if (attempt == MaxAttempts)
                {
                    return false;
                }
            }

            await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(true);
        }

        return false;
    }

    private static DataObject CreateTextDataObject(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        return data;
    }

    private static void SetClipboardData(DataObject data)
    {
        // The built-in overload retries with blocking sleeps; this loop owns the schedule.
        System.Windows.Forms.Clipboard.SetDataObject(
            data, copy: true, retryTimes: 0, retryDelay: 0);
    }
}
