using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MicroSnip.Ocr;

internal static partial class OcrTextPostProcessor
{
    internal static string Normalize(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var builder = new StringBuilder();

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string normalizedLine = WhitespaceRun().Replace(line, " ").Trim();
            if (normalizedLine.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("\r\n");
            }

            builder.Append(normalizedLine);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();
}
