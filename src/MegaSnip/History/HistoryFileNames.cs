using System;
using System.IO;

namespace MegaSnip.History;

internal static class HistoryFileNames
{
    internal const string Image = "image.png";
    internal const string Thumbnail = "thumb.png";
    internal const string Metadata = "meta.json";
    internal const string Database = "history.db";
}

internal static class HistoryPaths
{
    internal static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MegaSnip",
            "history");

    internal static string Resolve(string? configuredDirectory) =>
        string.IsNullOrWhiteSpace(configuredDirectory)
            ? DefaultDirectory
            : Path.GetFullPath(configuredDirectory);
}
