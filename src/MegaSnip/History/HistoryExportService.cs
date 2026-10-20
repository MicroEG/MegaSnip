using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MegaSnip.History;

internal sealed record HistoryExportItem(
    string Id,
    DateTimeOffset CreatedAtUtc,
    string Text,
    string? ImagePath,
    int Width,
    int Height);

internal sealed record HistoryExportResult(
    string Directory,
    int ExportedCount,
    IReadOnlyList<string> Warnings);

internal static class HistoryExportService
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    internal static HistoryExportResult Export(
        IReadOnlyList<HistoryExportItem> items,
        string destinationRoot,
        DateTimeOffset localNow,
        string appVersion)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);

        if (items.Count == 0)
        {
            throw new ArgumentException("There is nothing to export.", nameof(items));
        }

        Directory.CreateDirectory(destinationRoot);
        string directory = CreateExportDirectory(destinationRoot, localNow);
        var warnings = new List<string>();
        var index = new StringBuilder();
        int exportedCount = 0;

        index.AppendLine("# MegaSnip history export");
        index.AppendLine();
        index.AppendLine(
            "- Exported: " +
            localNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        index.AppendLine($"- Snips: {items.Count.ToString("N0", CultureInfo.InvariantCulture)}");
        index.AppendLine($"- App version: {appVersion}");
        index.AppendLine();
        index.AppendLine("| Captured | Size | Characters | Files | Text |");
        index.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (HistoryExportItem item in items)
        {
            if (!HistoryEntryId.IsValid(item.Id))
            {
                warnings.Add("Skipped an entry with an invalid id.");
                continue;
            }

            string baseName = "entry-" + item.Id;
            bool textWritten = TryWriteText(
                Path.Combine(directory, baseName + ".txt"),
                item.Text,
                item.Id,
                warnings);

            bool imageWritten = false;
            if (!string.IsNullOrEmpty(item.ImagePath))
            {
                imageWritten = TryCopyImage(
                    item.ImagePath,
                    Path.Combine(directory, baseName + ".png"),
                    item.Id,
                    warnings);
            }

            var files = new List<string>(2);
            if (imageWritten)
            {
                files.Add($"[PNG]({baseName}.png)");
            }

            if (textWritten)
            {
                files.Add($"[TXT]({baseName}.txt)");
            }

            index.Append("| ")
                .Append(FormatTimestamp(item.CreatedAtUtc))
                .Append(" | ")
                .Append(item.Width.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" × ")
                .Append(item.Height.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" px | ")
                .Append(item.Text.Length.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(files.Count == 0 ? "—" : string.Join(", ", files))
                .Append(" | ")
                .Append(EscapeCell(item.Text))
                .AppendLine(" |");

            exportedCount++;
        }

        TryWriteText(Path.Combine(directory, "index.md"), index.ToString(), "index", warnings);

        return new HistoryExportResult(directory, exportedCount, warnings);
    }

    private static string CreateExportDirectory(string destinationRoot, DateTimeOffset localNow)
    {
        string folderName =
            "export-" + localNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string directory = Path.Combine(destinationRoot, folderName);
        int suffix = 2;

        while (Directory.Exists(directory) || File.Exists(directory))
        {
            directory = Path.Combine(
                destinationRoot,
                folderName + "-" + suffix.ToString(CultureInfo.InvariantCulture));
            suffix++;
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string FormatTimestamp(DateTimeOffset createdAtUtc) =>
        createdAtUtc
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string EscapeCell(string text)
    {
        string line = text
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (line.Length == 0)
        {
            return "—";
        }

        if (line.Length > 100)
        {
            line = line[..100] + "…";
        }

        return line.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static bool TryWriteText(
        string path,
        string text,
        string entryId,
        List<string> warnings)
    {
        try
        {
            File.WriteAllText(path, text, Utf8NoBom);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Entry {entryId}: text could not be exported ({ex.Message})");
            return false;
        }
    }

    private static bool TryCopyImage(
        string sourcePath,
        string destinationPath,
        string entryId,
        List<string> warnings)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                warnings.Add($"Entry {entryId}: the saved image is missing.");
                return false;
            }

            File.Copy(sourcePath, destinationPath);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException)
        {
            warnings.Add($"Entry {entryId}: image could not be exported ({ex.Message})");
            return false;
        }
    }
}
