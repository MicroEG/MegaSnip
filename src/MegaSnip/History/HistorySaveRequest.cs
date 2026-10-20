using System;
using System.Drawing;

namespace MegaSnip.History;

internal sealed class HistorySaveRequest
{
    public required string Id { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required string Text { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public string MonitorId { get; init; } = string.Empty;

    public string? OcrLanguage { get; init; }

    public required string AppVersion { get; init; }

    public Bitmap? Image { get; init; }

    public bool SaveImage { get; init; } = true;
}
