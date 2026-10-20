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

    private readonly Action<string> _setClipboardText;
    private readonly TimeSpan _retryDelay;

    public ClipboardService()
        : this(SetClipboardText, DefaultRetryDelay)
    {
    }

    internal ClipboardService(Action<string> setClipboardText, TimeSpan retryDelay)
    {
        ArgumentNullException.ThrowIfNull(setClipboardText);

        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        _setClipboardText = setClipboardText;
        _retryDelay = retryDelay;
    }

    public async Task<bool> TrySetTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _setClipboardText(text);
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

    private static void SetClipboardText(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        // The built-in overload retries with blocking sleeps; this loop owns the schedule.
        System.Windows.Forms.Clipboard.SetDataObject(
            data, copy: true, retryTimes: 0, retryDelay: 0);
    }
}
