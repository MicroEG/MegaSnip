using System;
using System.IO;
using System.Text;
using MicroSnip.Settings;

namespace MicroSnip.Clipboard;

internal static class ClipboardTextFormatter
{
    internal static string Format(string text, LineBreakMode mode)
    {
        ArgumentNullException.ThrowIfNull(text);

        return mode == LineBreakMode.JoinWithSpaces ? JoinLines(text) : text;
    }

    private static string JoinLines(string text)
    {
        var builder = new StringBuilder(text.Length);
        using var reader = new StringReader(text);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(trimmed);
        }

        return builder.ToString();
    }
}
