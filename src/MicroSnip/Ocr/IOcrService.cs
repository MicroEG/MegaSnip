using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace MicroSnip.Ocr;

internal interface IOcrService
{
    IReadOnlyList<OcrLanguage> AvailableLanguages { get; }

    Task WarmUpAsync(string? preferredLanguageTag, CancellationToken cancellationToken = default);

    Task<string> RecognizeAsync(
        Bitmap bitmap,
        string? preferredLanguageTag,
        CancellationToken cancellationToken = default);
}
