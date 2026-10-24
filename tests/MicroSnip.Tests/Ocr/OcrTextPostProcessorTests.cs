using System;
using System.Collections.Generic;
using MicroSnip.Ocr;
using Xunit;

namespace MicroSnip.Tests.Ocr;

public sealed class OcrTextPostProcessorTests
{
    [Fact]
    public void Normalize_JoinsLinesWithWindowsLineBreaks()
    {
        string result = OcrTextPostProcessor.Normalize(
            new[] { "First line", "Second line", "Third line" });

        Assert.Equal("First line\r\nSecond line\r\nThird line", result);
    }

    [Fact]
    public void Normalize_CollapsesWhitespaceRunsAndTrimsLines()
    {
        string result = OcrTextPostProcessor.Normalize(
            new[] { "  Hello   world  ", "\tTabbed\tline\t" });

        Assert.Equal("Hello world\r\nTabbed line", result);
    }

    [Fact]
    public void Normalize_SkipsBlankAndWhitespaceOnlyLines()
    {
        string result = OcrTextPostProcessor.Normalize(
            new[] { string.Empty, "  ", "Kept", "\t\t", "Also kept", string.Empty });

        Assert.Equal("Kept\r\nAlso kept", result);
    }

    [Fact]
    public void Normalize_NoContent_ReturnsEmptyString()
    {
        string result = OcrTextPostProcessor.Normalize(new List<string>());

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_NullLines_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => OcrTextPostProcessor.Normalize(null!));
    }
}
