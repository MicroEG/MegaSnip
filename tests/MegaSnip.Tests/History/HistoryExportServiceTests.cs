using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MegaSnip.History;
using Xunit;

namespace MegaSnip.Tests.History;

public sealed class HistoryExportServiceTests : IDisposable
{
    private readonly string _root;

    public HistoryExportServiceTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "MegaSnip.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Export_WritesTextImagesIndexAndReturnsFolder()
    {
        string imageSource = Path.Combine(_root, "source.png");
        File.WriteAllBytes(imageSource, [1, 2, 3, 4]);
        var createdAt = new DateTimeOffset(2026, 10, 21, 9, 15, 30, TimeSpan.Zero);
        var items = new List<HistoryExportItem>
        {
            new("20261021-091530-aaaaaaaa", createdAt, "First line\nSecond line", imageSource, 320, 200),
            new("20261021-091531-bbbbbbbb", createdAt.AddSeconds(1), "Text only", null, 100, 40),
        };

        HistoryExportResult result = HistoryExportService.Export(
            items,
            _root,
            new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero),
            "0.1.0-test");

        Assert.Equal(2, result.ExportedCount);
        Assert.Empty(result.Warnings);
        Assert.Equal("export-20261021-100000", Path.GetFileName(result.Directory));
        Assert.True(Directory.Exists(result.Directory));

        string textPath = Path.Combine(result.Directory, "entry-20261021-091530-aaaaaaaa.txt");
        string imagePath = Path.Combine(result.Directory, "entry-20261021-091530-aaaaaaaa.png");
        Assert.True(File.Exists(textPath));
        Assert.Equal("First line\nSecond line", File.ReadAllText(textPath));
        Assert.True(File.Exists(imagePath));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(imagePath));
        Assert.False(File.Exists(
            Path.Combine(result.Directory, "entry-20261021-091531-bbbbbbbb.png")));

        string index = File.ReadAllText(Path.Combine(result.Directory, "index.md"));
        Assert.Contains("# MegaSnip history export", index, StringComparison.Ordinal);
        Assert.Contains("[PNG](entry-20261021-091530-aaaaaaaa.png)", index, StringComparison.Ordinal);
        Assert.Contains("[TXT](entry-20261021-091531-bbbbbbbb.txt)", index, StringComparison.Ordinal);
        Assert.Contains("First line Second line", index, StringComparison.Ordinal);

        byte[] indexBytes = File.ReadAllBytes(Path.Combine(result.Directory, "index.md"));
        Assert.False(indexBytes.Length >= 3 &&
            indexBytes[0] == 0xEF && indexBytes[1] == 0xBB && indexBytes[2] == 0xBF);
    }

    [Fact]
    public void Export_MissingImage_AddsWarningButExportsText()
    {
        var item = new HistoryExportItem(
            "20261021-101500-cccccccc",
            new DateTimeOffset(2026, 10, 21, 10, 15, 0, TimeSpan.Zero),
            "Missing image",
            Path.Combine(_root, "absent.png"),
            200,
            100);

        HistoryExportResult result = HistoryExportService.Export(
            [item],
            _root,
            new DateTimeOffset(2026, 10, 21, 10, 20, 0, TimeSpan.Zero),
            "0.1.0-test");

        Assert.Single(result.Warnings);
        Assert.Contains("missing", result.Warnings[0], StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(
            Path.Combine(result.Directory, "entry-20261021-101500-cccccccc.txt")));
        Assert.False(File.Exists(
            Path.Combine(result.Directory, "entry-20261021-101500-cccccccc.png")));
    }

    [Fact]
    public void Export_WhenFolderExists_UsesUniqueSuffix()
    {
        var localNow = new DateTimeOffset(2026, 10, 21, 11, 0, 0, TimeSpan.Zero);
        string existing = Path.Combine(_root, "export-20261021-110000");
        Directory.CreateDirectory(existing);
        var item = new HistoryExportItem(
            "20261021-110000-dddddddd",
            localNow,
            "Duplicate folder",
            null,
            10,
            10);

        HistoryExportResult result = HistoryExportService.Export(
            [item],
            _root,
            localNow,
            "0.1.0-test");

        Assert.Equal("export-20261021-110000-2", Path.GetFileName(result.Directory));
        Assert.True(Directory.Exists(existing));
        Assert.True(Directory.Exists(result.Directory));
    }

    [Fact]
    public void Export_EscapesTableMarkdownAndKeepsOneRowPerItem()
    {
        var item = new HistoryExportItem(
            "20261021-120000-eeeeeeee",
            new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero),
            "Alpha | Beta\nGamma",
            null,
            10,
            10);

        HistoryExportResult result = HistoryExportService.Export(
            [item],
            _root,
            new DateTimeOffset(2026, 10, 21, 12, 5, 0, TimeSpan.Zero),
            "0.1.0-test");

        string[] indexLines = File.ReadAllLines(Path.Combine(result.Directory, "index.md"));
        string row = Assert.Single(indexLines, line => line.Contains("Alpha", StringComparison.Ordinal));
        Assert.Contains("\\|", row, StringComparison.Ordinal);
        Assert.Contains("Alpha \\| Beta Gamma", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_RejectsEmptyItemList()
    {
        Assert.Throws<ArgumentException>(() =>
            HistoryExportService.Export(
                Array.Empty<HistoryExportItem>(),
                _root,
                DateTimeOffset.Now,
                "0.1.0-test"));
    }
}
