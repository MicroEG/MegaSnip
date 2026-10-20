using System.Threading;
using System.Threading.Tasks;

namespace MegaSnip.Clipboard;

internal interface IClipboardService
{
    Task<bool> TrySetTextAsync(string text, CancellationToken cancellationToken = default);

    Task<bool> TrySetImageAsync(string imagePath, CancellationToken cancellationToken = default);

    Task<bool> TrySetTextAndImageAsync(
        string text,
        string imagePath,
        CancellationToken cancellationToken = default);
}
