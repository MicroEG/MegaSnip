using System;
using System.IO;

namespace MicroSnip.Infrastructure;

internal static class AppDataPaths
{
    internal const string ProductName = "MicroSnip";
    internal const string LegacyProductName = "MegaSnip";

    internal static string RootDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ProductName);

    internal static string LegacyRootDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            LegacyProductName);

    internal static void MigrateLegacyDirectory() =>
        MigrateLegacyDirectory(RootDirectory, LegacyRootDirectory);

    internal static void MigrateLegacyDirectory(
        string targetDirectory,
        string legacyDirectory)
    {
        try
        {
            if (Directory.Exists(targetDirectory) ||
                !Directory.Exists(legacyDirectory))
            {
                return;
            }

            Directory.Move(legacyDirectory, targetDirectory);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
