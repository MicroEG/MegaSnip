using System.IO;
using MicroSnip.Infrastructure;

namespace MicroSnip.History;

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
        Path.Combine(AppDataPaths.RootDirectory, "history");

    internal static string Resolve(string? configuredDirectory) =>
        string.IsNullOrWhiteSpace(configuredDirectory)
            ? DefaultDirectory
            : Path.GetFullPath(configuredDirectory);
}
