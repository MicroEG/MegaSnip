using System;
using System.IO;
using System.Linq;

namespace MegaSnip.History;

internal sealed record HistoryMigrationResult(
    string SourceDirectory,
    string TargetDirectory,
    int CopiedFileCount,
    long CopiedByteCount);

internal static class HistoryDirectoryMigrator
{
    internal static HistoryMigrationResult Copy(
        string sourceDirectory,
        string targetDirectory)
    {
        string source = Normalize(sourceDirectory);
        string target = Normalize(targetDirectory);

        if (AreSameDirectory(source, target))
        {
            throw new InvalidOperationException(
                "History is already stored in this folder.");
        }

        if (IsWithin(target, source))
        {
            throw new InvalidOperationException(
                "The history folder cannot be moved inside itself.");
        }

        if (File.Exists(target))
        {
            throw new IOException("The selected path is a file, not a folder.");
        }

        bool targetExisted = Directory.Exists(target);

        if (targetExisted && Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw new InvalidOperationException(
                "Choose an empty folder for the history.");
        }

        if (File.Exists(source))
        {
            throw new IOException("The current history path is not a folder.");
        }

        bool sourceExisted = Directory.Exists(source);
        Directory.CreateDirectory(target);

        int copiedFiles = 0;
        long copiedBytes = 0;

        try
        {
            if (sourceExisted)
            {
                foreach (string sourceFile in Directory.EnumerateFiles(
                    source,
                    "*",
                    SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(source, sourceFile);
                    string targetFile = Path.Combine(target, relativePath);
                    string? targetParent = Path.GetDirectoryName(targetFile);

                    if (!string.IsNullOrEmpty(targetParent))
                    {
                        Directory.CreateDirectory(targetParent);
                    }

                    File.Copy(sourceFile, targetFile, overwrite: false);
                    copiedFiles++;
                    copiedBytes += new FileInfo(targetFile).Length;
                }
            }
        }
        catch
        {
            DeleteCopiedContents(target, targetExisted);
            throw;
        }

        return new HistoryMigrationResult(
            source,
            target,
            copiedFiles,
            copiedBytes);
    }

    internal static bool TryDeleteSource(
        string sourceDirectory,
        out string? warning)
    {
        warning = null;

        try
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning =
                "History was moved, but the old folder could not be removed: " +
                ex.Message;
            return false;
        }
    }

    internal static void DeleteCopiedTarget(string targetDirectory)
    {
        string target = Normalize(targetDirectory);
        DeleteCopiedContents(target, targetExisted: true);
    }

    internal static bool AreSameDirectory(string first, string second) =>
        string.Equals(
            Normalize(first),
            Normalize(second),
            StringComparison.OrdinalIgnoreCase);

    private static void DeleteCopiedContents(
        string target,
        bool targetExisted)
    {
        try
        {
            if (!targetExisted)
            {
                Directory.Delete(target, recursive: true);
                return;
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(target))
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsWithin(string path, string parent)
    {
        string normalizedParent = Normalize(parent);
        string prefix = normalizedParent.EndsWith(
            Path.DirectorySeparatorChar)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;

        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
