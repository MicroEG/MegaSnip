using System;
using System.Globalization;

namespace MegaSnip.History;

internal static class HistoryEntryId
{
    internal static string Create(DateTimeOffset createdAtUtc) =>
        string.Concat(
            createdAtUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
            "-",
            Guid.NewGuid().ToString("N")[..8]);

    internal static bool IsValid(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80)
        {
            return false;
        }

        foreach (char character in id)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
