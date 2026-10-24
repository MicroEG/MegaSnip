using System;
using MicroSnip.Clipboard;
using MicroSnip.Settings;
using Xunit;

namespace MicroSnip.Tests.Clipboard;

public sealed class ClipboardTextFormatterTests
{
    [Fact]
    public void Format_PreserveMode_ReturnsTextUnchanged()
    {
        const string text = "First line\r\n\r\nSecond line\tindented";

        string formatted = ClipboardTextFormatter.Format(text, LineBreakMode.Preserve);

        Assert.Equal(text, formatted);
    }

    [Fact]
    public void Format_JoinMode_TrimsAndJoinsNonEmptyLines()
    {
        const string text = "  First line  \r\n\r\n\tSecond line\t\r\nThird line";

        string formatted = ClipboardTextFormatter.Format(text, LineBreakMode.JoinWithSpaces);

        Assert.Equal("First line Second line Third line", formatted);
    }

    [Fact]
    public void Format_JoinMode_HandlesMixedLineEndings()
    {
        const string text = "One\nTwo\r\nThree\rFour";

        string formatted = ClipboardTextFormatter.Format(text, LineBreakMode.JoinWithSpaces);

        Assert.Equal("One Two Three Four", formatted);
    }

    [Fact]
    public void Format_EmptyText_ReturnsEmptyString()
    {
        Assert.Equal(
            string.Empty,
            ClipboardTextFormatter.Format(string.Empty, LineBreakMode.JoinWithSpaces));
    }

    [Fact]
    public void Format_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ClipboardTextFormatter.Format(null!, LineBreakMode.Preserve));
    }
}
