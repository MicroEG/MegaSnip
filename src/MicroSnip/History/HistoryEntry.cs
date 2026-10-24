using System;
using System.IO;

namespace MicroSnip.History;

internal sealed class HistoryEntry
{
    public required string Id { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required string Text { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required string MonitorId { get; init; }

    public string? OcrLanguage { get; init; }

    public required int CharCount { get; init; }

    public required string AppVersion { get; init; }

    public required bool HasImage { get; init; }

    public required bool IsOrphaned { get; init; }

    public required string EntryDirectory { get; init; }

    public string? ImagePath => HasImage
        ? Path.Combine(EntryDirectory, HistoryFileNames.Image)
        : null;

    public string? ThumbnailPath => HasImage
        ? Path.Combine(EntryDirectory, HistoryFileNames.Thumbnail)
        : null;

    public string MetadataPath => Path.Combine(EntryDirectory, HistoryFileNames.Metadata);

    public DateTime LocalCreatedAt => CreatedAtUtc.ToLocalTime().DateTime;
}
